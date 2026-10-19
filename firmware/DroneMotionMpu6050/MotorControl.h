#pragma once
#include <Arduino.h>
#if defined(ARDUINO_ARCH_ESP32)
#include <esp_timer.h>
#include <freertos/FreeRTOS.h>
#include <freertos/semphr.h>
#include <string.h>

// Manual bench output, not a flight controller. Supervisor runs independently
// of the IMU/main loop and stops on deadline or a two-second heartbeat timeout.
class MotorControl {
 public:
  void begin() {
    mutex_ = xSemaphoreCreateMutex();
    if (!mutex_) return;
    // Attach lazily on ARM; telemetry startup needs no motor PWM peripherals.
    esp_timer_create_args_t args = {};
    args.callback = supervise; args.arg = this;
    args.dispatch_method = ESP_TIMER_TASK; args.name = "motor_guard";
    ready_ = esp_timer_create(&args, &timer_) == ESP_OK &&
             esp_timer_start_periodic(timer_, 50000) == ESP_OK;
  }
  void stop() {
    if (!mutex_) return;
    xSemaphoreTake(mutex_, portMAX_DELAY);
    stopLocked();
    xSemaphoreGive(mutex_);
  }
  void report(Print &out) {
    bool ready = false;
    if (mutex_) {
      xSemaphoreTake(mutex_, portMAX_DELAY);
      ready = ready_;
      xSemaphoreGive(mutex_);
    }
    out.println(ready ? F("STATUS,MOTOR_AVAILABLE") : F("STATUS,MOTOR_UNAVAILABLE"));
    if (ready) out.println(F("STATUS,MOTOR_ADVANCED_V2"));
  }
  void poll(Print &out, bool wifiConnected) {
    if (!mutex_) return;
    xSemaphoreTake(mutex_, portMAX_DELAY);
    if ((armed_ || running_) && ownerWifi_ && !wifiConnected) stopLocked();
    const bool stopped = stoppedPending_;
    stoppedPending_ = false;
    xSemaphoreGive(mutex_);
    if (stopped) out.println(F("STATUS,MOTOR_STOPPED"));
  }
  void command(const char *text, bool wifi, Print &out) {
    if (!mutex_) { out.println(F("ERROR,MOTOR_UNAVAILABLE")); return; }
    xSemaphoreTake(mutex_, portMAX_DELAY);
    const char *response = "ERROR,MOTOR_INVALID_COMMAND";
    if (strcmp(text, "STOP") == 0) {
      stopLocked(); response = "STATUS,MOTOR_STOPPED";
    } else if (strcmp(text, "KEEPALIVE") == 0) {
      if (running_ && ownerWifi_ == wifi) heartbeatAt_ = esp_timer_get_time();
      response = nullptr;
    } else if (strcmp(text, "ARM") == 0) {
      stopLocked();
      if (ready_ && attachLocked()) {
        stoppedPending_ = false;
        armed_ = true; ownerWifi_ = wifi; armedAt_ = esp_timer_get_time();
        response = "STATUS,MOTOR_ARMED";
      } else response = "ERROR,MOTOR_UNAVAILABLE";
    } else {
      uint32_t duties[4] = {}, duration = 0;
      if (parseRun(text, duties, duration)) {
        const int64_t now = esp_timer_get_time();
        if (ready_ && armed_ && !running_ && ownerWifi_ == wifi && now - armedAt_ < 15000000LL) {
          armed_ = false;
          bool written = true;
          for (int i = 0; i < 4; ++i) if (!ledcWrite(pins_[i], duties[i])) written = false;
          if (written) {
            running_ = true; heartbeatAt_ = now;
            deadline_ = duration ? now + static_cast<int64_t>(duration) * 1000LL : 0;
            response = "STATUS,MOTOR_RUNNING";
          } else { ready_ = false; stopLocked(); response = "ERROR,MOTOR_PWM_FAILED"; }
        } else { stopLocked(); response = "ERROR,MOTOR_ARM_REQUIRED"; }
      } else stopLocked();
    }
    xSemaphoreGive(mutex_);
    if (response) out.println(response);
  }
 private:
  const uint8_t pins_[4] = {14, 15, 12, 13};
  bool attached_[4] = {};
  bool ready_ = false, armed_ = false, running_ = false;
  bool ownerWifi_ = false, stoppedPending_ = false;
  int64_t armedAt_ = 0, heartbeatAt_ = 0, deadline_ = 0;
  SemaphoreHandle_t mutex_ = nullptr;
  esp_timer_handle_t timer_ = nullptr;
  static bool number(const char *text, uint32_t &value) {
    if (!text || !*text) return false;
    uint64_t parsed = 0;
    for (const char *p = text; *p; ++p) {
      if (*p < '0' || *p > '9') return false;
      parsed = parsed * 10 + static_cast<unsigned>(*p - '0');
      if (parsed > UINT32_MAX) return false;
    }
    value = static_cast<uint32_t>(parsed); return true;
  }
  static bool parseRun(const char *text, uint32_t duties[4], uint32_t &duration) {
    char buffer[64];
    if (strlen(text) >= sizeof(buffer)) return false;
    strcpy(buffer, text);
    char *context = nullptr;
    char *target = strtok_r(buffer, " \t", &context);
    if (!target) return false;
    if (strcmp(target, "SET") == 0) {
      for (int i = 0; i < 4; ++i)
        if (!number(strtok_r(nullptr, " \t", &context), duties[i]) || duties[i] > 255) return false;
    } else {
      int selected = -1;
      const char *names[] = {"LF", "RF", "LR", "RR", "ALL"};
      for (int i = 0; i < 5; ++i) if (strcmp(target, names[i]) == 0) selected = i;
      uint32_t percent;
      if (selected < 0 || !number(strtok_r(nullptr, " \t", &context), percent) || percent > 100) return false;
      for (int i = 0; i < 4; ++i)
        duties[i] = selected == 4 || selected == i ? (percent * 255UL + 50UL) / 100UL : 0;
    }
    return number(strtok_r(nullptr, " \t", &context), duration) &&
           strtok_r(nullptr, " \t", &context) == nullptr;
  }
  bool attachLocked() {
    for (int i = 0; i < 4; ++i) {
      if (!attached_[i]) attached_[i] = ledcAttach(pins_[i], 20000, 8);
      if (!attached_[i]) { ready_ = false; stopLocked(); return false; }
    }
    return true;
  }
  void stopLocked() {
    for (int i = 0; i < 4; ++i) {
      if (attached_[i] && !ledcWrite(pins_[i], 0)) {
        ledcDetach(pins_[i]); attached_[i] = false; ready_ = false;
      }
      if (!attached_[i]) { digitalWrite(pins_[i], LOW); pinMode(pins_[i], OUTPUT); }
    }
    if (running_ || armed_) stoppedPending_ = true;
    running_ = armed_ = false;
  }
  static void supervise(void *arg) {
    auto *self = static_cast<MotorControl *>(arg);
    xSemaphoreTake(self->mutex_, portMAX_DELAY);
    const int64_t now = esp_timer_get_time();
    if ((self->armed_ && now - self->armedAt_ >= 15000000LL) ||
        (self->running_ && (now - self->heartbeatAt_ >= 2000000LL ||
          (self->deadline_ && now >= self->deadline_)))) self->stopLocked();
    xSemaphoreGive(self->mutex_);
  }
};
#else
class MotorControl {
 public:
  void begin() {}
  void stop() {}
  void report(Print &out) { out.println(F("STATUS,MOTOR_UNAVAILABLE")); }
  void poll(Print &, bool) {}
  void command(const char *, bool, Print &out) { out.println(F("ERROR,MOTOR_UNAVAILABLE")); }
};
#endif
