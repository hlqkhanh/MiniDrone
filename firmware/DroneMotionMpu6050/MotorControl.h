#pragma once
#include <Arduino.h>
#if defined(ARDUINO_ARCH_ESP32)
#include <esp_timer.h>
#include <freertos/FreeRTOS.h>
#include <freertos/semphr.h>
#include <stdlib.h>
#include <string.h>

// Bench tests only. Timer task can stop PWM even while the main loop is busy.
class MotorControl {
 public:
  void begin() {
    mutex_ = xSemaphoreCreateMutex();
    if (!mutex_) return;
    for (uint8_t i = 0; i < 4; ++i) {
      attached_[i] = ledcAttach(pins_[i], 20000, 8);
      if (!attached_[i]) { stop(); return; }
    }
    esp_timer_create_args_t args = {};
    args.callback = timeout;
    args.arg = this;
    args.dispatch_method = ESP_TIMER_TASK;
    args.name = "motor_stop";
    ready_ = esp_timer_create(&args, &timer_) == ESP_OK;
    stop();
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
  }

  void poll(Print &out, bool wifiConnected) {
    if (!mutex_) return;
    xSemaphoreTake(mutex_, portMAX_DELAY);
    if ((armed_ || running_) && ownerWifi_ && !wifiConnected) stopLocked();
    if (armed_ && millis() - armedAt_ >= 15000) stopLocked();
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
      stopLocked();
      response = "STATUS,MOTOR_STOPPED";
    } else if (strcmp(text, "ARM") == 0) {
      stopLocked();
      if (ready_) {
        stoppedPending_ = false;
        armed_ = true; ownerWifi_ = wifi; armedAt_ = millis();
        response = "STATUS,MOTOR_ARMED";
      } else response = "ERROR,MOTOR_UNAVAILABLE";
    } else {
      char buffer[48];
      if (strlen(text) < sizeof(buffer)) {
        strcpy(buffer, text);
        char *context = nullptr;
        char *target = strtok_r(buffer, " ", &context);
        char *percentText = strtok_r(nullptr, " ", &context);
        char *durationText = strtok_r(nullptr, " ", &context);
        char *extra = strtok_r(nullptr, " ", &context);
        int selected = -1;
        const char *names[] = {"LF", "RF", "LR", "RR", "ALL"};
        for (int i = 0; target && i < 5; ++i)
          if (strcmp(target, names[i]) == 0) selected = i;
        unsigned long percent = 0, duration = 0;
        if (selected >= 0 && !extra && number(percentText, percent) &&
            number(durationText, duration) && percent >= 1 && percent <= 35 &&
            duration >= 1 && duration <= 2000) {
          if (ready_ && armed_ && !running_ && ownerWifi_ == wifi &&
              millis() - armedAt_ < 15000) {
            armed_ = false;
            running_ = true;
            bool written = true;
            for (int i = 0; i < 4; ++i)
              if (!ledcWrite(pins_[i], selected == 4 || selected == i
                  ? (percent * 255UL + 50UL) / 100UL : 0)) written = false;
            if (written && esp_timer_start_once(timer_, duration * 1000ULL) == ESP_OK)
              response = "STATUS,MOTOR_RUNNING";
            else { ready_ = false; stopLocked(); response = "ERROR,MOTOR_PWM_OR_TIMER_FAILED"; }
          } else { stopLocked(); response = "ERROR,MOTOR_ARM_REQUIRED"; }
        } else stopLocked();
      } else stopLocked();
    }
    xSemaphoreGive(mutex_);
    out.println(response);
  }

 private:
  const uint8_t pins_[4] = {14, 15, 12, 13}; // LF, RF, LR, RR
  bool attached_[4] = {};
  bool ready_ = false, armed_ = false, running_ = false;
  bool ownerWifi_ = false, stoppedPending_ = false;
  uint32_t armedAt_ = 0;
  SemaphoreHandle_t mutex_ = nullptr;
  esp_timer_handle_t timer_ = nullptr;

  static bool number(const char *text, unsigned long &value) {
    if (!text || !*text || strlen(text) > 4) return false;
    for (const char *p = text; *p; ++p) if (*p < '0' || *p > '9') return false;
    value = strtoul(text, nullptr, 10);
    return true;
  }
  void stopLocked() {
    if (timer_) esp_timer_stop(timer_);
    for (int i = 0; i < 4; ++i) {
      if (attached_[i] && !ledcWrite(pins_[i], 0)) {
        ledcDetach(pins_[i]); attached_[i] = false; ready_ = false;
      }
      if (!attached_[i]) { digitalWrite(pins_[i], LOW); pinMode(pins_[i], OUTPUT); }
    }
    if (running_ || armed_) stoppedPending_ = true;
    running_ = armed_ = false;
  }
  static void timeout(void *arg) { static_cast<MotorControl *>(arg)->stop(); }
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
