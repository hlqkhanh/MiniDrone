#if defined(ARDUINO_ARCH_ESP32)
#include <SPI.h>
#else
#include <Wire.h>
#endif
#include <math.h>
#include "TelemetryOutput.h"

namespace Config {
#if defined(ARDUINO_ARCH_ESP32)
// XW_Z1_V2.0 schematic, doc/sodo.pdf: MPU6500 over SPI.
constexpr int SckPin = 18;
constexpr int MisoPin = 19;
constexpr int MosiPin = 23;
constexpr int CsPin = 5;
constexpr uint32_t SpiClockHz = 1000000;
constexpr int MotorPins[] = {14, 15, 12, 13};
constexpr int LedPin = -1;
#else
constexpr int LedPin = LED_BUILTIN;
#endif
constexpr uint32_t BaudRate = 115200;
constexpr uint16_t SampleRateHz = 100;
constexpr uint32_t SamplePeriodMicros = 1000000UL / SampleRateHz;
constexpr uint16_t CalibrationSamples = 500;
constexpr uint32_t CalibrationTimeoutMillis = 15000;
constexpr float AccelScale = 16384.0f;  // +/- 2 g
constexpr float GyroScale = 65.5f;     // +/- 500 degrees/second
constexpr float DegreesToRadians = PI / 180.0f;
constexpr float MadgwickBeta = 0.075f;
}

struct RawSample {
  int16_t ax;
  int16_t ay;
  int16_t az;
  int16_t gx;
  int16_t gy;
  int16_t gz;
};

float gyroBiasX = 0.0f;
float gyroBiasY = 0.0f;
float gyroBiasZ = 0.0f;
float qW = 1.0f;
float qX = 0.0f;
float qY = 0.0f;
float qZ = 0.0f;
bool sensorAvailable = false;
uint32_t sequenceNumber = 0;
uint32_t previousUpdateMicros = 0;
uint32_t nextSampleMicros = 0;
struct CommandBuffer {
  char bytes[20];
  uint8_t length = 0;
  bool discard = false;
};
CommandBuffer serialCommands;
#if defined(ARDUINO_ARCH_ESP32)
CommandBuffer wifiCommands;
#endif
TelemetryOutput Telemetry;
uint8_t mpuAddress = 0x68;
uint8_t sensorIdentity = 0;
const char *initializationError = "MPU_NOT_FOUND";

void setStatusLed(bool enabled) {
  if (Config::LedPin >= 0) {
    digitalWrite(Config::LedPin, enabled ? HIGH : LOW);
  }
}

void beginSensorBus() {
#if defined(ARDUINO_ARCH_ESP32)
  // Keep every motor output off during all sensor-only operations.
  for (int pin : Config::MotorPins) {
    digitalWrite(pin, LOW);
    pinMode(pin, OUTPUT);
  }
  digitalWrite(Config::CsPin, HIGH);
  pinMode(Config::CsPin, OUTPUT);
  SPI.begin(Config::SckPin, Config::MisoPin, Config::MosiPin, Config::CsPin);
#else
  Wire.begin();
  Wire.setClock(400000UL);
#endif
}

bool writeRegister(uint8_t reg, uint8_t value) {
#if defined(ARDUINO_ARCH_ESP32)
  SPI.beginTransaction(SPISettings(Config::SpiClockHz, MSBFIRST, SPI_MODE0));
  digitalWrite(Config::CsPin, LOW);
  SPI.transfer(reg & 0x7F);
  SPI.transfer(value);
  digitalWrite(Config::CsPin, HIGH);
  SPI.endTransaction();
  return true;  // SPI has no ACK; initialization verifies register readback.
#else
  Wire.beginTransmission(mpuAddress);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission(true) == 0;
#endif
}

bool readRegisters(uint8_t reg, uint8_t *buffer, uint8_t count) {
#if defined(ARDUINO_ARCH_ESP32)
  SPI.beginTransaction(SPISettings(Config::SpiClockHz, MSBFIRST, SPI_MODE0));
  digitalWrite(Config::CsPin, LOW);
  SPI.transfer(reg | 0x80);
  for (uint8_t i = 0; i < count; ++i) {
    buffer[i] = SPI.transfer(0x00);
  }
  digitalWrite(Config::CsPin, HIGH);
  SPI.endTransaction();
  return true;
#else
  Wire.beginTransmission(mpuAddress);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) {
    return false;
  }
  if (Wire.requestFrom(mpuAddress, count, static_cast<uint8_t>(true)) != count) {
    return false;
  }
  for (uint8_t i = 0; i < count; ++i) {
    buffer[i] = Wire.read();
  }
  return true;
#endif
}

bool readRegister(uint8_t reg, uint8_t &value) {
  return readRegisters(reg, &value, 1);
}

bool configureRegister(uint8_t reg, uint8_t value) {
  uint8_t actual = 0;
  return writeRegister(reg, value) && readRegister(reg, actual) && actual == value;
}

const char *sensorName(uint8_t identity) {
#if defined(ARDUINO_ARCH_ESP32)
  if (identity == 0x70) return "MPU6500";
  if (identity == 0x71) return "MPU9250";
#else
  if (identity == 0x68 || identity == 0x69) return "MPU6050";
#endif
  return "UNKNOWN";
}

void reportSensorInfo() {
#if defined(ARDUINO_ARCH_ESP32)
  Telemetry.println(F("STATUS,SPI_SCK18_MOSI23_MISO19_CS5"));
#else
  Telemetry.println(F("STATUS,I2C_MPU6050"));
#endif
  uint8_t identity = 0;
  if (!readRegister(0x75, identity)) {
    Telemetry.println(F("ERROR,MPU_READ"));
    return;
  }
  Telemetry.print(F("STATUS,WHO_AM_I_0x"));
  if (identity < 0x10) Telemetry.print('0');
  Telemetry.println(identity, HEX);
  Telemetry.print(F("STATUS,SENSOR_"));
  Telemetry.println(sensorName(identity));
}

bool initializeSensor() {
  initializationError = "MPU_NOT_FOUND";
  sensorIdentity = 0;
  uint8_t whoAmI = 0;
#if defined(ARDUINO_ARCH_ESP32)
  // Require a stable response before writing any sensor configuration.
  bool stableIdentity = true;
  for (uint8_t attempt = 0; attempt < 3; ++attempt) {
    readRegister(0x75, whoAmI);
    if (attempt > 0 && whoAmI != sensorIdentity) stableIdentity = false;
    sensorIdentity = whoAmI;
    delay(10);
  }
  reportSensorInfo();
  if (!stableIdentity) return false;
  if (sensorIdentity == 0x00 || sensorIdentity == 0xFF) return false;
  if (sensorIdentity != 0x70 && sensorIdentity != 0x71) {
    initializationError = "MPU_UNSUPPORTED_ID";
    return false;
  }
#else
  bool found = false;
  for (uint8_t address = 0x68; address <= 0x69; ++address) {
    mpuAddress = address;
    if (readRegister(0x75, whoAmI) && (whoAmI == 0x68 || whoAmI == 0x69)) {
      found = true;
      break;
    }
  }
  if (!found) {
    return false;
  }
  sensorIdentity = whoAmI;
  reportSensorInfo();
#endif

  initializationError = "MPU_CONFIG";
  // Device reset; PWR_MGMT_1 reset bit is self-clearing, so do not verify it.
  if (!writeRegister(0x6B, 0x80)) return false;
  delay(100);
#if defined(ARDUINO_ARCH_ESP32)
  // USER_CTRL.I2C_IF_DIS: use the external SPI interface exclusively.
  if (!configureRegister(0x6A, 0x10)) return false;
#endif
  if (!configureRegister(0x6B, 0x01) || !configureRegister(0x6C, 0x00)) return false;
  delay(200);

  // DLPF enabled; 1 kHz / (1 + 9) = 100 Hz, +/-2 g, +/-500 deg/s.
  bool configured = configureRegister(0x1A, 0x03) &&
                    configureRegister(0x19, 0x09) &&
                    configureRegister(0x1B, 0x08) &&
                    configureRegister(0x1C, 0x00);
#if defined(ARDUINO_ARCH_ESP32)
  configured = configured && configureRegister(0x1D, 0x03);
#endif
  return configured;
}

int16_t decodeSigned16(const uint8_t *bytes) {
  return static_cast<int16_t>((static_cast<uint16_t>(bytes[0]) << 8) | bytes[1]);
}

bool readRawSample(RawSample &sample) {
  // Detect a disconnected SPI device rather than integrating floating data.
  uint8_t identity = 0;
  if (!readRegister(0x75, identity) || identity != sensorIdentity) return false;
  uint8_t bytes[14];
  if (!readRegisters(0x3B, bytes, sizeof(bytes))) return false;
  bool allZero = true;
  bool allOnes = true;
  for (uint8_t value : bytes) {
    allZero = allZero && value == 0x00;
    allOnes = allOnes && value == 0xFF;
  }
  if (allZero || allOnes) return false;

  sample.ax = decodeSigned16(bytes);
  sample.ay = decodeSigned16(bytes + 2);
  sample.az = decodeSigned16(bytes + 4);
  // bytes 6..7 are the temperature reading.
  sample.gx = decodeSigned16(bytes + 8);
  sample.gy = decodeSigned16(bytes + 10);
  sample.gz = decodeSigned16(bytes + 12);
  return true;
}

void setInitialOrientation(float ax, float ay, float az) {
  const float roll = atan2f(ay, az);
  const float pitch = atan2f(-ax, sqrtf(ay * ay + az * az));
  const float halfRoll = roll * 0.5f;
  const float halfPitch = pitch * 0.5f;
  const float cr = cosf(halfRoll);
  const float sr = sinf(halfRoll);
  const float cp = cosf(halfPitch);
  const float sp = sinf(halfPitch);

  qW = cr * cp;
  qX = sr * cp;
  qY = cr * sp;
  qZ = -sr * sp;
}

bool calibrateGyroscope() {
  Telemetry.println(F("STATUS,CALIBRATING"));
  // Temporarily sample at 200 Hz so 500 calibration readings take ~2.5 s.
  if (!configureRegister(0x19, 0x04)) {
    Telemetry.println(F("ERROR,MPU_CONFIG"));
    return false;
  }
  uint16_t accepted = 0;
  int32_t sumGx = 0;
  int32_t sumGy = 0;
  int32_t sumGz = 0;
  int32_t sumAx = 0;
  int32_t sumAy = 0;
  int32_t sumAz = 0;
  uint32_t lastBlink = millis();
  bool ledState = false;
  const uint32_t startedMillis = millis();

  while (accepted < Config::CalibrationSamples) {
    if (millis() - startedMillis >= Config::CalibrationTimeoutMillis) {
      Telemetry.println(F("ERROR,CALIBRATION_TIMEOUT_KEEP_STILL"));
      return false;
    }
    RawSample sample;
    if (!readRawSample(sample)) {
      Telemetry.println(F("ERROR,MPU_READ"));
      return false;
    }

    const float ax = sample.ax / Config::AccelScale;
    const float ay = sample.ay / Config::AccelScale;
    const float az = sample.az / Config::AccelScale;
    const float accelerationMagnitude = sqrtf(ax * ax + ay * ay + az * az);
    const bool stationary = accelerationMagnitude > 0.85f && accelerationMagnitude < 1.15f &&
                            fabsf(sample.gx / Config::GyroScale) < 3.0f &&
                            fabsf(sample.gy / Config::GyroScale) < 3.0f &&
                            fabsf(sample.gz / Config::GyroScale) < 3.0f;

    if (stationary) {
      sumGx += sample.gx;
      sumGy += sample.gy;
      sumGz += sample.gz;
      sumAx += sample.ax;
      sumAy += sample.ay;
      sumAz += sample.az;
      accepted++;
    } else {
      // Require consecutive still samples so motion cannot pollute the bias.
      accepted = 0;
      sumGx = sumGy = sumGz = 0;
      sumAx = sumAy = sumAz = 0;
    }

    if (millis() - lastBlink >= 150) {
      ledState = !ledState;
      setStatusLed(ledState);
      lastBlink = millis();
    }
    delay(5);
  }

  gyroBiasX = static_cast<float>(sumGx) / Config::CalibrationSamples;
  gyroBiasY = static_cast<float>(sumGy) / Config::CalibrationSamples;
  gyroBiasZ = static_cast<float>(sumGz) / Config::CalibrationSamples;
  setInitialOrientation(
      static_cast<float>(sumAx) / Config::CalibrationSamples,
      static_cast<float>(sumAy) / Config::CalibrationSamples,
      static_cast<float>(sumAz) / Config::CalibrationSamples);

  if (!configureRegister(0x19, 0x09)) {
    Telemetry.println(F("ERROR,MPU_CONFIG"));
    return false;
  }

  previousUpdateMicros = micros();
  nextSampleMicros = previousUpdateMicros + Config::SamplePeriodMicros;
  setStatusLed(true);
  Telemetry.println(F("STATUS,READY"));
  return true;
}

void updateMadgwick(float gx, float gy, float gz, float ax, float ay, float az, float dt) {
  float qDotW = 0.5f * (-qX * gx - qY * gy - qZ * gz);
  float qDotX = 0.5f * ( qW * gx + qY * gz - qZ * gy);
  float qDotY = 0.5f * ( qW * gy - qX * gz + qZ * gx);
  float qDotZ = 0.5f * ( qW * gz + qX * gy - qY * gx);

  const float accelerationNormSquared = ax * ax + ay * ay + az * az;
  if (accelerationNormSquared > 1e-8f) {
    const float inverseAccelerationNorm = 1.0f / sqrtf(accelerationNormSquared);
    ax *= inverseAccelerationNorm;
    ay *= inverseAccelerationNorm;
    az *= inverseAccelerationNorm;

    const float twiceW = 2.0f * qW;
    const float twiceX = 2.0f * qX;
    const float twiceY = 2.0f * qY;
    const float twiceZ = 2.0f * qZ;
    const float fourW = 4.0f * qW;
    const float fourX = 4.0f * qX;
    const float fourY = 4.0f * qY;
    const float eightX = 8.0f * qX;
    const float eightY = 8.0f * qY;
    const float ww = qW * qW;
    const float xx = qX * qX;
    const float yy = qY * qY;
    const float zz = qZ * qZ;

    float stepW = fourW * yy + twiceY * ax + fourW * xx - twiceX * ay;
    float stepX = fourX * zz - twiceZ * ax + 4.0f * ww * qX - twiceW * ay - fourX + eightX * xx + eightX * yy + fourX * az;
    float stepY = 4.0f * ww * qY + twiceW * ax + fourY * zz - twiceZ * ay - fourY + eightY * xx + eightY * yy + fourY * az;
    float stepZ = 4.0f * xx * qZ - twiceX * ax + 4.0f * yy * qZ - twiceY * ay;
    const float stepNormSquared = stepW * stepW + stepX * stepX + stepY * stepY + stepZ * stepZ;
    if (stepNormSquared > 1e-8f) {
      const float inverseStepNorm = 1.0f / sqrtf(stepNormSquared);
      stepW *= inverseStepNorm;
      stepX *= inverseStepNorm;
      stepY *= inverseStepNorm;
      stepZ *= inverseStepNorm;
      qDotW -= Config::MadgwickBeta * stepW;
      qDotX -= Config::MadgwickBeta * stepX;
      qDotY -= Config::MadgwickBeta * stepY;
      qDotZ -= Config::MadgwickBeta * stepZ;
    }
  }

  qW += qDotW * dt;
  qX += qDotX * dt;
  qY += qDotY * dt;
  qZ += qDotZ * dt;
  const float quaternionNorm = sqrtf(qW * qW + qX * qX + qY * qY + qZ * qZ);
  if (quaternionNorm > 1e-8f) {
    const float inverseQuaternionNorm = 1.0f / quaternionNorm;
    qW *= inverseQuaternionNorm;
    qX *= inverseQuaternionNorm;
    qY *= inverseQuaternionNorm;
    qZ *= inverseQuaternionNorm;
  }
}

void sendQuaternion(uint32_t timestamp) {
  Telemetry.print(F("Q,"));
  Telemetry.print(sequenceNumber++);
  Telemetry.print(',');
  Telemetry.print(timestamp);
  Telemetry.print(',');
  Telemetry.print(qW, 6);
  Telemetry.print(',');
  Telemetry.print(qX, 6);
  Telemetry.print(',');
  Telemetry.print(qY, 6);
  Telemetry.print(',');
  Telemetry.print(qZ, 6);
  Telemetry.println();
}

void handleCommand(const char *command) {
  if (strcmp(command, "CAL") == 0) {
    if (!sensorAvailable && !initializeSensor()) {
      Telemetry.print(F("ERROR,"));
      Telemetry.println(initializationError);
      return;
    }
    sensorAvailable = calibrateGyroscope();
  } else if (strcmp(command, "INFO") == 0) {
    Telemetry.reportWifiInfo();
    reportSensorInfo();
  } else if (strcmp(command, "PING") == 0) {
    Telemetry.println(F("PONG"));
  }
}

void processCommands(Stream &input, CommandBuffer &buffer) {
  uint8_t budget = 64;
  while (input.available() > 0 && budget--) {
    const char character = static_cast<char>(input.read());
    if (character == '\n' || character == '\r') {
      if (!buffer.discard && buffer.length > 0) {
        buffer.bytes[buffer.length] = '\0';
        handleCommand(buffer.bytes);
      }
      buffer.length = 0;
      buffer.discard = false;
    } else if (!buffer.discard) {
      if (buffer.length < sizeof(buffer.bytes) - 1) buffer.bytes[buffer.length++] = character;
      else { buffer.length = 0; buffer.discard = true; }
    }
  }
}

void setup() {
  if (Config::LedPin >= 0) {
    pinMode(Config::LedPin, OUTPUT);
  }
  setStatusLed(false);
  Serial.begin(Config::BaudRate);
  beginSensorBus();
  Telemetry.beginWifi();
  delay(100);

  Telemetry.print(F("HELLO,1,"));
  Telemetry.println(Config::SampleRateHz);
  sensorAvailable = initializeSensor();
  if (!sensorAvailable) {
    Telemetry.print(F("ERROR,"));
    Telemetry.println(initializationError);
    return;
  }

  delay(100);
  sensorAvailable = calibrateGyroscope();
}

void loop() {
  if (Telemetry.acceptClient()) {
#if defined(ARDUINO_ARCH_ESP32)
    wifiCommands = CommandBuffer{};
#endif
    Telemetry.print(F("HELLO,1,"));
    Telemetry.println(Config::SampleRateHz);
    reportSensorInfo();
    Telemetry.println(sensorAvailable ? F("STATUS,READY") : F("ERROR,SENSOR_NOT_READY_SEND_CAL"));
  }
  processCommands(Serial, serialCommands);
#if defined(ARDUINO_ARCH_ESP32)
  processCommands(Telemetry.clientStream(), wifiCommands);
#endif

  if (!sensorAvailable) {
    // Fast blink indicates that the sensor is missing or unreadable.
    setStatusLed((millis() / 100) % 2);
    delay(5);
    return;
  }

  const uint32_t now = micros();
  if (static_cast<int32_t>(now - nextSampleMicros) < 0) {
    return;
  }
  nextSampleMicros += Config::SamplePeriodMicros;
  if (static_cast<int32_t>(now - nextSampleMicros) > static_cast<int32_t>(Config::SamplePeriodMicros)) {
    nextSampleMicros = now + Config::SamplePeriodMicros;
  }

  RawSample sample;
  if (!readRawSample(sample)) {
    Telemetry.println(F("ERROR,MPU_READ"));
    sensorAvailable = false;
    return;
  }

  float dt = static_cast<uint32_t>(now - previousUpdateMicros) / 1000000.0f;
  previousUpdateMicros = now;
  if (dt < 0.001f || dt > 0.05f) {
    dt = 1.0f / Config::SampleRateHz;
  }

  const float ax = sample.ax / Config::AccelScale;
  const float ay = sample.ay / Config::AccelScale;
  const float az = sample.az / Config::AccelScale;
  const float gx = ((sample.gx - gyroBiasX) / Config::GyroScale) * Config::DegreesToRadians;
  const float gy = ((sample.gy - gyroBiasY) / Config::GyroScale) * Config::DegreesToRadians;
  const float gz = ((sample.gz - gyroBiasZ) / Config::GyroScale) * Config::DegreesToRadians;

  updateMadgwick(gx, gy, gz, ax, ay, az, dt);
  sendQuaternion(now);
}
