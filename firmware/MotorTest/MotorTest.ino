#include <Arduino.h>
#include <ctype.h>
#include <stdlib.h>
#include <string.h>

#if !defined(ARDUINO_ARCH_ESP32)
#error "Select ESP32 Dev Module with Arduino-ESP32 core 3.x."
#endif

// XW_Z1_V2.0, doc/sodo.pdf. Remove ALL propellers before testing.
constexpr uint8_t MotorPins[] = {14, 15, 12, 13}; // LF, RF, LR, RR
constexpr uint32_t PwmFrequency = 20000;
constexpr uint8_t PwmBits = 8;
constexpr unsigned long MaximumPercent = 35;
constexpr unsigned long MaximumRunMillis = 2000;
constexpr uint32_t ArmTimeoutMillis = 15000;
bool attached[4] = {};
bool ready = false;
bool armed = false;
bool running = false;
uint32_t armedAt = 0;
uint32_t stopAt = 0;
char line[64];
uint8_t lineLength = 0;
bool discardLine = false;

void stopMotors() {
  for (uint8_t i = 0; i < 4; ++i) {
    if (attached[i] && !ledcWrite(MotorPins[i], 0)) {
      ledcDetach(MotorPins[i]);
      attached[i] = false;
      ready = false;
    }
    if (!attached[i]) {
      digitalWrite(MotorPins[i], LOW);
      pinMode(MotorPins[i], OUTPUT);
    }
  }
  running = false;
  armed = false;
}

void checkTimeouts() {
  if (running && static_cast<int32_t>(millis() - stopAt) >= 0) {
    stopMotors();
    Serial.println("STOPPED,TIMEOUT,DISARMED");
  } else if (armed && millis() - armedAt >= ArmTimeoutMillis) {
    stopMotors();
    Serial.println("STOPPED,ARM_EXPIRED");
  }
}

bool parseNumber(const char *text, unsigned long &value) {
  if (!text || !*text || strlen(text) > 4) return false;
  for (const char *p = text; *p; ++p) {
    if (!isdigit(static_cast<unsigned char>(*p))) return false;
  }
  value = strtoul(text, nullptr, 10);
  return true;
}

void reject(const char *reason) {
  stopMotors();
  Serial.print("ERROR,");
  Serial.println(reason);
}

void handleLine(char *text) {
  char *context = nullptr;
  char *command = strtok_r(text, " \t", &context);
  if (!command) return;
  char *percentText = strtok_r(nullptr, " \t", &context);
  char *durationText = strtok_r(nullptr, " \t", &context);
  char *extra = strtok_r(nullptr, " \t", &context);
  if (strcmp(command, "STOP") == 0) {
    stopMotors();
    Serial.println("STOPPED,DISARMED");
    return;
  }
  if (strcmp(command, "ARM") == 0 && !percentText) {
    stopMotors();
    if (!ready) { reject("PWM_NOT_READY"); return; }
    armed = true;
    armedAt = millis();
    Serial.println("ARMED,15_SECONDS,PROPELLERS_MUST_BE_REMOVED");
    return;
  }
  int selected = -1;
  if (strcmp(command, "LF") == 0) selected = 0;
  else if (strcmp(command, "RF") == 0) selected = 1;
  else if (strcmp(command, "LR") == 0) selected = 2;
  else if (strcmp(command, "RR") == 0) selected = 3;
  else if (strcmp(command, "ALL") == 0) selected = 4;
  if (selected < 0) { reject("USE_ARM_STOP_OR_LF_RF_LR_RR_ALL_PERCENT_MS"); return; }
  unsigned long percent = 0;
  unsigned long duration = 0;
  if (extra || !parseNumber(percentText, percent) || !parseNumber(durationText, duration) ||
      percent < 1 || percent > MaximumPercent || duration < 1 || duration > MaximumRunMillis) {
    reject("LIMIT_PERCENT_1_TO_35_DURATION_1_TO_2000_MS");
    return;
  }
  checkTimeouts();
  if (!ready || !armed || running) { reject("SEND_ARM_BEFORE_EACH_TEST"); return; }
  // One test per ARM; shutdown needs no further serial input.
  armed = false;
  running = true;
  stopAt = millis() + static_cast<uint32_t>(duration);
  const uint32_t duty = (percent * 255UL + 50UL) / 100UL;
  for (uint8_t i = 0; i < 4; ++i) {
    if (!ledcWrite(MotorPins[i], (selected == 4 || selected == i) ? duty : 0)) {
      ready = false;
      reject("PWM_WRITE_FAILED");
      return;
    }
  }
  Serial.print("RUNNING,"); Serial.print(command);
  Serial.print(','); Serial.print(percent);
  Serial.print("_PERCENT,"); Serial.print(duration); Serial.println("_MS");
}

void setup() {
  for (uint8_t pin : MotorPins) {
    digitalWrite(pin, LOW);
    pinMode(pin, OUTPUT);
  }
  Serial.begin(115200);
  ready = true;
  for (uint8_t i = 0; i < 4; ++i) {
    attached[i] = ledcAttach(MotorPins[i], PwmFrequency, PwmBits);
    if (!attached[i]) ready = false;
  }
  stopMotors();
  Serial.println("MOTOR_TEST,PROPELLERS_OFF,LF14_RF15_LR12_RR13");
  Serial.println(ready ? "READY,DISARMED" : "ERROR,PWM_INIT_FAILED");
  Serial.println("COMMANDS: ARM | LF/RF/LR/RR/ALL <percent:1-35> <ms:1-2000> | STOP");
}

void loop() {
  checkTimeouts();
  uint8_t budget = 64; // Serial traffic must not postpone timer checks.
  while (Serial.available() && budget--) {
    checkTimeouts();
    const char c = static_cast<char>(Serial.read());
    if (c == '\n' || c == '\r') {
      if (!discardLine && lineLength) {
        line[lineLength] = '\0';
        handleLine(line);
      }
      lineLength = 0;
      discardLine = false;
    } else if (!discardLine) {
      if (lineLength < sizeof(line) - 1) line[lineLength++] = c;
      else {
        lineLength = 0;
        discardLine = true;
        reject("LINE_TOO_LONG");
      }
    }
  }
  delay(1);
}
