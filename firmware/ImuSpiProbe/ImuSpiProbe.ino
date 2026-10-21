#include <SPI.h>

#if !defined(ARDUINO_ARCH_ESP32)
#error "Select ESP32 Dev Module for the XW_Z1_V2.0 SPI probe."
#endif

// Read-only identification for doc/sodo.pdf. No sensor configuration writes.
constexpr int SckPin = 18;
constexpr int MisoPin = 19;
constexpr int MosiPin = 23;
constexpr int CsPin = 5;
constexpr int MotorPins[] = {14, 15, 12, 13};

uint8_t readIdentity(uint32_t clockHz) {
  SPI.beginTransaction(SPISettings(clockHz, MSBFIRST, SPI_MODE0));
  digitalWrite(CsPin, LOW);
  delayMicroseconds(1);
  SPI.transfer(0x75 | 0x80);
  const uint8_t identity = SPI.transfer(0x00);
  delayMicroseconds(1);
  digitalWrite(CsPin, HIGH);
  SPI.endTransaction();
  delayMicroseconds(1);
  return identity;
}

void setup() {
  for (int pin : MotorPins) {
    digitalWrite(pin, LOW);
    pinMode(pin, OUTPUT);
  }
  digitalWrite(CsPin, HIGH);
  pinMode(CsPin, OUTPUT);
  SPI.begin(SckPin, MisoPin, MosiPin, CsPin);
  Serial.begin(115200);
  delay(100);
  Serial.println("PROBE,SPI_SCK18_MOSI23_MISO19_CS5");
}

void loop() {
  const uint32_t clocks[] = {100000, 250000, 1000000};
  for (uint32_t clockHz : clocks) {
    uint16_t found6500 = 0, found9250 = 0, zero = 0, ones = 0, other = 0;
    uint8_t lastIdentity = 0;
    for (int n = 0; n < 100; ++n) {
      lastIdentity = readIdentity(clockHz);
      if (lastIdentity == 0x70) ++found6500;
      else if (lastIdentity == 0x71) ++found9250;
      else if (lastIdentity == 0) ++zero;
      else if (lastIdentity == 0xFF) ++ones;
      else ++other;
      delay(5);
    }
    Serial.print("PROBE,HZ,"); Serial.print(clockHz);
    Serial.print(",MPU6500,"); Serial.print(found6500);
    Serial.print(",MPU9250,"); Serial.print(found9250);
    Serial.print(",ZERO,"); Serial.print(zero);
    Serial.print(",FF,"); Serial.print(ones);
    Serial.print(",OTHER,"); Serial.print(other);
    Serial.print(",LAST,0x"); Serial.println(lastIdentity, HEX);
  }
}
