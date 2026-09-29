#include <Wire.h>
#include <math.h>

namespace Config {
constexpr uint8_t MpuAddress = 0x68;
constexpr uint8_t LedPin = LED_BUILTIN;
constexpr uint32_t BaudRate = 115200;
constexpr uint16_t SampleRateHz = 100;
constexpr uint32_t SamplePeriodMicros = 1000000UL / SampleRateHz;
constexpr uint16_t CalibrationSamples = 500;
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
char commandBuffer[20];
uint8_t commandLength = 0;

bool writeRegister(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(Config::MpuAddress);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission(true) == 0;
}

bool readRegister(uint8_t reg, uint8_t &value) {
  Wire.beginTransmission(Config::MpuAddress);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) {
    return false;
  }
  if (Wire.requestFrom(Config::MpuAddress, static_cast<uint8_t>(1), static_cast<uint8_t>(true)) != 1) {
    return false;
  }
  value = Wire.read();
  return true;
}

bool initializeMpu6050() {
  uint8_t whoAmI = 0;
  if (!readRegister(0x75, whoAmI) || (whoAmI != 0x68 && whoAmI != 0x69)) {
    return false;
  }

  // Clock source: PLL with X gyro. DLPF: 44 Hz. Output rate: 1 kHz / (1 + 9) = 100 Hz.
  return writeRegister(0x6B, 0x01) &&
         writeRegister(0x1A, 0x03) &&
         writeRegister(0x19, 0x09) &&
         writeRegister(0x1B, 0x08) &&
         writeRegister(0x1C, 0x00);
}

bool readRawSample(RawSample &sample) {
  Wire.beginTransmission(Config::MpuAddress);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) {
    return false;
  }

  if (Wire.requestFrom(Config::MpuAddress, static_cast<uint8_t>(14), static_cast<uint8_t>(true)) != 14) {
    return false;
  }

  sample.ax = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
  sample.ay = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
  sample.az = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
  Wire.read();  // Temperature high byte
  Wire.read();  // Temperature low byte
  sample.gx = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
  sample.gy = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
  sample.gz = static_cast<int16_t>((Wire.read() << 8) | Wire.read());
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
  Serial.println(F("STATUS,CALIBRATING"));
  // Temporarily sample at 200 Hz so 500 calibration readings take ~2.5 s.
  if (!writeRegister(0x19, 0x04)) {
    Serial.println(F("ERROR,MPU_CONFIG"));
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

  while (accepted < Config::CalibrationSamples) {
    RawSample sample;
    if (!readRawSample(sample)) {
      Serial.println(F("ERROR,MPU_READ"));
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
      digitalWrite(Config::LedPin, ledState);
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

  if (!writeRegister(0x19, 0x09)) {
    Serial.println(F("ERROR,MPU_CONFIG"));
    return false;
  }

  previousUpdateMicros = micros();
  nextSampleMicros = previousUpdateMicros + Config::SamplePeriodMicros;
  digitalWrite(Config::LedPin, HIGH);
  Serial.println(F("STATUS,READY"));
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
  Serial.print(F("Q,"));
  Serial.print(sequenceNumber++);
  Serial.print(',');
  Serial.print(timestamp);
  Serial.print(',');
  Serial.print(qW, 6);
  Serial.print(',');
  Serial.print(qX, 6);
  Serial.print(',');
  Serial.print(qY, 6);
  Serial.print(',');
  Serial.print(qZ, 6);
  Serial.println();
}

void handleCommand(const char *command) {
  if (strcmp(command, "CAL") == 0) {
    sensorAvailable = calibrateGyroscope();
  } else if (strcmp(command, "PING") == 0) {
    Serial.println(F("PONG"));
  }
}

void processSerialCommands() {
  while (Serial.available() > 0) {
    const char character = static_cast<char>(Serial.read());
    if (character == '\n' || character == '\r') {
      if (commandLength > 0) {
        commandBuffer[commandLength] = '\0';
        handleCommand(commandBuffer);
        commandLength = 0;
      }
    } else if (commandLength < sizeof(commandBuffer) - 1) {
      commandBuffer[commandLength++] = character;
    } else {
      commandLength = 0;
    }
  }
}

void setup() {
  pinMode(Config::LedPin, OUTPUT);
  digitalWrite(Config::LedPin, LOW);
  Serial.begin(Config::BaudRate);
  Wire.begin();
  Wire.setClock(400000UL);
  delay(100);

  Serial.print(F("HELLO,1,"));
  Serial.println(Config::SampleRateHz);
  sensorAvailable = initializeMpu6050();
  if (!sensorAvailable) {
    Serial.println(F("ERROR,MPU_NOT_FOUND"));
    return;
  }

  delay(100);
  sensorAvailable = calibrateGyroscope();
}

void loop() {
  processSerialCommands();

  if (!sensorAvailable) {
    // Fast blink indicates that the sensor is missing or unreadable.
    digitalWrite(Config::LedPin, (millis() / 100) % 2);
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
    Serial.println(F("ERROR,MPU_READ"));
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
