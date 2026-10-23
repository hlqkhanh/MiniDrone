#include <SPI.h>
#include "AngleEstimator.h"

#if !defined(ARDUINO_ARCH_ESP32)
#error "Select ESP32 Dev Module for the XW_Z1_V2.0 SPI board."
#endif

constexpr int SckPin = 18, MisoPin = 19, MosiPin = 23, CsPin = 5;
constexpr int MotorPins[] = {14, 15, 12, 13};
constexpr float AccelScale = 16384.0f, GyroScale = 65.5f;
AngleEstimator attitude;
bool ready = false, configured = false;
uint8_t sensorId = 0;
uint32_t lastSample = 0, lastPrint = 0, lastIdentityCheck = 0, retryAt = 0;
uint32_t calibrationStarted = 0;
uint16_t calibrationCount = 0;
float sums[6] = {}, gyroSquares[3] = {}, bias[3] = {};
float accelerationNormSum = 0;

void readRegisters(uint8_t reg, uint8_t *bytes, uint8_t count) {
  SPI.beginTransaction(SPISettings(250000, MSBFIRST, SPI_MODE0));
  digitalWrite(CsPin, LOW); delayMicroseconds(1);
  SPI.transfer(reg | 0x80);
  for (uint8_t i = 0; i < count; ++i) bytes[i] = SPI.transfer(0);
  delayMicroseconds(1); digitalWrite(CsPin, HIGH);
  SPI.endTransaction(); delayMicroseconds(1);
}

uint8_t readRegister(uint8_t reg) {
  uint8_t value; readRegisters(reg, &value, 1); return value;
}

bool configure(uint8_t reg, uint8_t value) {
  SPI.beginTransaction(SPISettings(250000, MSBFIRST, SPI_MODE0));
  digitalWrite(CsPin, LOW); delayMicroseconds(1);
  SPI.transfer(reg & 0x7F); SPI.transfer(value);
  delayMicroseconds(1); digitalWrite(CsPin, HIGH);
  SPI.endTransaction(); delayMicroseconds(1);
  return readRegister(reg) == value;
}

void resetCalibrationWindow() {
  calibrationCount = 0;
  accelerationNormSum = 0;
  for (float &sum : sums) sum = 0;
  for (float &square : gyroSquares) square = 0;
}

void sensorFault(const char *reason) {
  Serial.print("ERROR,"); Serial.println(reason);
  ready = configured = false;
  retryAt = millis() + 1000;
}

bool initialize() {
  sensorId = readRegister(0x75);
  Serial.print("WHO_AM_I,0x");
  if (sensorId < 0x10) Serial.print('0');
  Serial.println(sensorId, HEX);
  if (sensorId != 0x70 && sensorId != 0x71) return false;
  // No device reset. Wake it and set known scales for the angle calculation.
  if (!configure(0x6B, 0x01) || !configure(0x6C, 0x00)) return false;
  delay(200);
  if (!configure(0x6A, 0x10) || !configure(0x1A, 0x03) ||
      !configure(0x1B, 0x08) || !configure(0x1C, 0x00) ||
      !configure(0x1D, 0x03) || !configure(0x19, 0x04) ||
      !configure(0x38, 0x01)) return false;
  resetCalibrationWindow();
  calibrationStarted = millis();
  lastSample = micros(); lastPrint = lastIdentityCheck = millis();
  Serial.println("CALIBRATING,KEEP_BOARD_STILL_3_SECONDS");
  return true;
}

void setup() {
  for (int pin : MotorPins) { digitalWrite(pin, LOW); pinMode(pin, OUTPUT); }
  digitalWrite(CsPin, HIGH); pinMode(CsPin, OUTPUT);
  Serial.begin(115200);
  SPI.begin(SckPin, MisoPin, MosiPin, CsPin);
  delay(500);
  Serial.println("MPU_ANGLES,ROLL_PITCH_YAW_DEGREES,NO_WIFI_NO_MOTORS");
}

void loop() {
  if (!configured) {
    if (static_cast<int32_t>(millis() - retryAt) < 0) { delay(1); return; }
    configured = initialize();
    if (!configured) sensorFault("MPU_NOT_FOUND_OR_CONFIG_FAILED");
    return;
  }
  if (millis() - lastIdentityCheck >= 250) {
    lastIdentityCheck = millis();
    if (readRegister(0x75) != sensorId) { sensorFault("MPU_ID_LOST"); return; }
  }
  if (!ready && millis() - calibrationStarted >= 30000) {
    sensorFault("CALIBRATION_TIMEOUT_KEEP_STILL"); return;
  }
  const uint32_t now = micros();
  if (now - lastSample < 5000) { delay(1); return; }
  const uint8_t status = readRegister(0x3A);
  if (status == 0xFF) { sensorFault("SPI_READ_FAILED"); return; }
  if (!(status & 1)) {
    if (now - lastSample > 1000000) sensorFault("NO_FRESH_DATA");
    return;
  }
  uint8_t bytes[14]; readRegisters(0x3B, bytes, sizeof(bytes));
  bool allZero = true, allOnes = true;
  for (uint8_t b : bytes) { allZero &= b == 0; allOnes &= b == 0xFF; }
  if (allZero || allOnes) { sensorFault("INVALID_SAMPLE"); return; }
  float values[6];
  const uint8_t offsets[] = {0, 2, 4, 8, 10, 12};
  for (int i = 0; i < 6; ++i) {
    const int offset = offsets[i];
    const int16_t raw = static_cast<int16_t>((uint16_t(bytes[offset]) << 8) | bytes[offset+1]);
    values[i] = raw / (i < 3 ? AccelScale : GyroScale);
  }
  const float dt = uint32_t(now - lastSample) / 1000000.0f;
  lastSample = now;
  if (!ready) {
    const float norm = sqrtf(values[0]*values[0] + values[1]*values[1] + values[2]*values[2]);
    if (norm < 0.85f || norm > 1.15f || fabsf(values[3]) > 15 ||
        fabsf(values[4]) > 15 || fabsf(values[5]) > 15) resetCalibrationWindow();
    else {
      for (int i = 0; i < 6; ++i) sums[i] += values[i];
      for (int i = 0; i < 3; ++i) gyroSquares[i] += values[i+3]*values[i+3];
      ++calibrationCount;
      accelerationNormSum += norm;
    }
    if (millis() - lastPrint >= 500) {
      lastPrint = millis();
      Serial.print("CALIBRATING,"); Serial.print(calibrationCount); Serial.println("/500");
    }
    if (calibrationCount == 500) {
      bool stable = true;
      for (int i = 0; i < 3; ++i) {
        bias[i] = sums[i+3]/500;
        if (gyroSquares[i]/500 - bias[i]*bias[i] > 0.25f) stable = false;
      }
      const float ax = sums[0]/500, ay = sums[1]/500, az = sums[2]/500;
      // Compare gravity direction consistency without assuming exact 1 g scale.
      if (sqrtf(ax*ax + ay*ay + az*az) < 0.995f*accelerationNormSum/500) stable = false;
      if (!stable) { resetCalibrationWindow(); Serial.println("CALIBRATING,MOTION_DETECTED_RETRY"); }
      else {
        attitude.reset(ax, ay, az);
        ready = true;
        Serial.println("READY,YAW_RELATIVE_MAY_DRIFT");
      }
    }
    return;
  }
  if (!attitude.update(values[0], values[1], values[2],
      values[3]-bias[0], values[4]-bias[1], values[5]-bias[2], dt)) {
    sensorFault("SAMPLE_TIMING_INVALID"); return;
  }
  if (millis() - lastPrint >= 100) {
    lastPrint = millis();
    Serial.print("Roll:"); Serial.print(attitude.roll, 2);
    Serial.print("\tPitch:"); Serial.print(attitude.pitch, 2);
    Serial.print("\tYaw:"); Serial.println(attitude.yaw, 2);
  }
}
