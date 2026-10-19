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

uint8_t readIdentity() {
  SPI.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));
  digitalWrite(CsPin, LOW);
  SPI.transfer(0x75 | 0x80);
  const uint8_t identity = SPI.transfer(0x00);
  digitalWrite(CsPin, HIGH);
  SPI.endTransaction();
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
  const uint8_t identity = readIdentity();
  Serial.print("PROBE,WHO_AM_I,0x");
  if (identity < 0x10) Serial.print('0');
  Serial.print(identity, HEX);
  if (identity == 0x70) Serial.println(",MPU6500");
  else if (identity == 0x71) Serial.println(",MPU9250");
  else if (identity == 0x00 || identity == 0xFF) Serial.println(",NO_RESPONSE");
  else Serial.println(",UNSUPPORTED_ID");
  delay(1000);
}
