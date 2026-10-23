#include <SPI.h>

#if !defined(ARDUINO_ARCH_ESP32)
#error "Select ESP32 Dev Module. This sketch uses the XW_Z1_V2.0 SPI wiring."
#endif

// Only read WHO_AM_I. No sensor reset/configuration, calibration, Wi-Fi or PID.
constexpr int SckPin = 18;
constexpr int MisoPin = 19;
constexpr int MosiPin = 23;
constexpr int CsPin = 5;
constexpr uint32_t SpiClockHz = 250000;
constexpr int MotorPins[] = {14, 15, 12, 13};

uint8_t readWhoAmI() {
  SPI.beginTransaction(SPISettings(SpiClockHz, MSBFIRST, SPI_MODE0));
  digitalWrite(CsPin, LOW);
  delayMicroseconds(1);
  SPI.transfer(0x75 | 0x80); // Read WHO_AM_I register.
  const uint8_t identity = SPI.transfer(0x00);
  delayMicroseconds(1);
  digitalWrite(CsPin, HIGH);
  SPI.endTransaction();
  delayMicroseconds(1);
  return identity;
}

void setup() {
  // Hold outputs low; never attach PWM or start a motor.
  for (int pin : MotorPins) {
    digitalWrite(pin, LOW);
    pinMode(pin, OUTPUT);
  }
  digitalWrite(CsPin, HIGH);
  pinMode(CsPin, OUTPUT);
  Serial.begin(115200);
  SPI.begin(SckPin, MisoPin, MosiPin, CsPin);
  delay(500);
  Serial.println("MPU_DETECT,SPI,SCK=18,MISO=19,MOSI=23,CS=5,HZ=250000");
  Serial.println("FORMAT: MPU_DETECT,millis,WHO_AM_I,result");
}

void loop() {
  const uint8_t identity = readWhoAmI();
  Serial.print("MPU_DETECT,");
  Serial.print(millis());
  Serial.print(",0x");
  if (identity < 0x10) Serial.print('0');
  Serial.print(identity, HEX);
  if (identity == 0x70) Serial.println(",MPU6500_FOUND");
  else if (identity == 0x71) Serial.println(",MPU9250_FOUND");
  else if (identity == 0x00 || identity == 0xFF) Serial.println(",NO_RESPONSE");
  else Serial.println(",UNSUPPORTED_ID");
  delay(250);
}
