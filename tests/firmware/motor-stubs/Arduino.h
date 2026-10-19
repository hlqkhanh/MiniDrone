#pragma once
#include <stdint.h>
#include <string>
#define F(value) value
#define LOW 0
#define OUTPUT 1
namespace MotorMock {
inline uint32_t now = 0;
inline uint32_t duty[40] = {};
inline bool attachOk = true, writeOk = true;
}
inline uint32_t millis() { return MotorMock::now; }
inline void digitalWrite(uint8_t pin, int value) { MotorMock::duty[pin] = value; }
inline void pinMode(uint8_t, int) {}
inline bool ledcAttach(uint8_t, uint32_t, uint8_t) { return MotorMock::attachOk; }
inline bool ledcDetach(uint8_t) { return true; }
inline bool ledcWrite(uint8_t pin, uint32_t duty) {
  if (!MotorMock::writeOk) return false;
  MotorMock::duty[pin] = duty; return true;
}
class Print {
 public:
  std::string last;
  void println(const char *value) { last = value; }
};
