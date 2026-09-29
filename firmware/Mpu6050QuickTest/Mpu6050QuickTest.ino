#include <Wire.h>

uint8_t mpuAddress = 0;

bool deviceResponds(uint8_t address) {
  Wire.beginTransmission(address);
  return Wire.endTransmission() == 0;
}

bool writeRegister(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(mpuAddress);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission(true) == 0;
}

bool readRegister(uint8_t reg, uint8_t &value) {
  Wire.beginTransmission(mpuAddress);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(mpuAddress, (uint8_t)1, (uint8_t)true) != 1) return false;
  value = Wire.read();
  return true;
}

bool readMotion(int16_t &ax, int16_t &ay, int16_t &az,
                int16_t &gx, int16_t &gy, int16_t &gz) {
  Wire.beginTransmission(mpuAddress);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(mpuAddress, (uint8_t)14, (uint8_t)true) != 14) return false;

  ax = (int16_t)((Wire.read() << 8) | Wire.read());
  ay = (int16_t)((Wire.read() << 8) | Wire.read());
  az = (int16_t)((Wire.read() << 8) | Wire.read());
  Wire.read();
  Wire.read();
  gx = (int16_t)((Wire.read() << 8) | Wire.read());
  gy = (int16_t)((Wire.read() << 8) | Wire.read());
  gz = (int16_t)((Wire.read() << 8) | Wire.read());
  return true;
}

void setup() {
  Serial.begin(115200);
  Wire.begin();
  Wire.setClock(100000);  // Tốc độ thấp để test dây ổn định hơn.
  delay(500);

  Serial.println(F("\n=== MPU6050 QUICK TEST ==="));
  Serial.println(F("Nano: SDA=A4, SCL=A5, GND=GND, GY-521 VCC=5V"));

  if (deviceResponds(0x68)) mpuAddress = 0x68;
  else if (deviceResponds(0x69)) mpuAddress = 0x69;
  else {
    Serial.println(F("FAIL: Khong tim thay MPU6050 tai 0x68 hoac 0x69."));
    Serial.println(F("Kiem tra VCC, GND, SDA=A4, SCL=A5 va moi han."));
    return;
  }

  Serial.print(F("OK: Tim thay thiet bi tai 0x"));
  Serial.println(mpuAddress, HEX);

  uint8_t whoAmI = 0;
  if (!readRegister(0x75, whoAmI)) {
    Serial.println(F("FAIL: Khong doc duoc WHO_AM_I."));
    mpuAddress = 0;
    return;
  }

  Serial.print(F("WHO_AM_I = 0x"));
  Serial.println(whoAmI, HEX);
  if (whoAmI != 0x68 && whoAmI != 0x69) {
    Serial.println(F("FAIL: Gia tri WHO_AM_I khong dung cho MPU6050."));
    mpuAddress = 0;
    return;
  }

  if (!writeRegister(0x6B, 0x00) ||
      !writeRegister(0x1B, 0x00) ||
      !writeRegister(0x1C, 0x00)) {
    Serial.println(F("FAIL: Khong cau hinh duoc MPU6050."));
    mpuAddress = 0;
    return;
  }

  delay(100);
  Serial.println(F("PASS: MPU6050 giao tiep binh thuong."));
  Serial.println(F("Dat cam bien nam ngang: |AZ| nen gan 1.00g, AX/AY gan 0g."));
  Serial.println(F("Di chuyen/xoay cam bien: cac gia tri phai thay doi."));
  Serial.println();
}

void loop() {
  if (mpuAddress == 0) {
    delay(1000);
    return;
  }

  int16_t ax, ay, az, gx, gy, gz;
  if (!readMotion(ax, ay, az, gx, gy, gz)) {
    Serial.println(F("FAIL: Mat ket noi khi dang doc du lieu."));
    delay(1000);
    return;
  }

  Serial.print(F("ACC(g) X="));
  Serial.print(ax / 16384.0f, 2);
  Serial.print(F(" Y="));
  Serial.print(ay / 16384.0f, 2);
  Serial.print(F(" Z="));
  Serial.print(az / 16384.0f, 2);

  Serial.print(F(" | GYRO(deg/s) X="));
  Serial.print(gx / 131.0f, 1);
  Serial.print(F(" Y="));
  Serial.print(gy / 131.0f, 1);
  Serial.print(F(" Z="));
  Serial.println(gz / 131.0f, 1);

  delay(250);
}
