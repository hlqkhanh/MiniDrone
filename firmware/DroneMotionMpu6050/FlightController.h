#include <Wire.h>

// Sensor-only sketch for Nano/Uno + MPU6050. No motor pins or control commands.
// Raw scales: +/-2 g and +/-500 degrees/second. No fusion or bias correction.
uint8_t address = 0x68;
bool ready = false;
uint32_t nextSample = 0;
uint32_t retryAt = 0;

bool readRegisters(uint8_t reg, uint8_t *data, uint8_t count) {
  Wire.beginTransmission(address);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(address, count, static_cast<uint8_t>(true)) != count) return false;
  for (uint8_t i = 0; i < count; i++) data[i] = Wire.read();
  return true;
}

bool configure(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(address);
  Wire.write(reg); Wire.write(value);
  if (Wire.endTransmission(true) != 0) return false;
  uint8_t actual = 0;
  return readRegisters(reg, &actual, 1) && actual == value;
}

bool initializeImu() {
  uint8_t identity = 0;
  bool found = false;
  for (uint8_t candidate = 0x68; candidate <= 0x69; candidate++) {
    address = candidate;
    if (readRegisters(0x75, &identity, 1) && (identity == 0x68 || identity == 0x69)) {
      found = true; break;
    }
  }
  if (!found) return false;
  // CONFIG=0 bypasses the selectable MPU6050 DLPF (device bandwidth remains finite).
  // With bypass, gyro internal rate is 8 kHz; divider 79 yields 100 Hz.
  return configure(0x6B, 0x01) && configure(0x6C, 0x00) &&
         configure(0x1A, 0x00) && configure(0x19, 79) &&
         configure(0x1B, 0x08) && configure(0x1C, 0x00);
}

int16_t signed16(const uint8_t *data) {
  return static_cast<int16_t>((static_cast<uint16_t>(data[0]) << 8) | data[1]);
}

void setup() {
  Serial.begin(115200);
  Wire.begin();
  Wire.setClock(400000UL);
#if defined(WIRE_HAS_TIMEOUT)
  Wire.setWireTimeout(25000, true);
#endif
  delay(200);
  Serial.println(F("STATUS,RAW_IMU_NO_FUSION_NO_BIAS"));
}

void loop() {
  if (!ready) {
    if (static_cast<int32_t>(millis() - retryAt) < 0) return;
    ready = initializeImu();
    retryAt = millis() + 1500;
    if (!ready) { Serial.println(F("ERROR,MPU6050_NOT_READY_CHECK_A4_A5")); return; }
    Serial.println(F("STATUS,MPU6050_READY_DLPF_BYPASS_100HZ"));
    nextSample = micros() + 10000;
  }
  const uint32_t now = micros();
  if (static_cast<int32_t>(now - nextSample) < 0) return;
  nextSample = now + 10000; // No catch-up bursts after a slow transaction.
  uint8_t bytes[14];
  if (!readRegisters(0x3B, bytes, sizeof(bytes))) {
    Serial.println(F("ERROR,IMU_READ")); ready = false; return;
  }
  bool allZero = true, allOnes = true;
  for (uint8_t value : bytes) { allZero &= value == 0; allOnes &= value == 0xFF; }
  if (allZero || allOnes) { Serial.println(F("ERROR,IMU_INVALID")); ready = false; return; }
  Serial.print(F("RAW,")); Serial.print(now);
  for (uint8_t offset = 0; offset < 6; offset += 2) {
    Serial.print(','); Serial.print(signed16(bytes + offset) / 16384.0f, 4);
  }
  for (uint8_t offset = 8; offset < 14; offset += 2) {
    Serial.print(','); Serial.print(signed16(bytes + offset) / 65.5f, 3);
  }
  Serial.println();
}
