#include "../../firmware/DroneMotionMpu6050/GyroCalibration.h"
#include <assert.h>

int main() {
  GyroCalibration c;
  // The original <3 deg/s absolute gate rejected this stationary zero offset.
  for (int n = 0; n < 500; ++n) {
    const bool ready = c.add(0, 0, 16384, 328 + (n%3-1), -262, 65);
    assert(ready == (n == 499));
  }
  assert(fabsf(c.mean(3) - 328) < 1);
  assert(c.mean(4) == -262 && c.mean(2) == 16384);
  c.reset();
  // Persistent motion/noise cannot be accepted as a zero offset.
  for (int n = 0; n < 1000; ++n) assert(!c.add(0, 0, 16384, n%2 ? 300 : -300, 0, 0));
  assert(c.count < 2);
  c.reset();
  for (int n = 0; n < 1000; ++n) assert(!c.add(0, 0, 23000, 0, 0, 0));
  assert(c.reason == GyroCalibration::Reason::Acceleration && c.count == 0);
  for (int n = 0; n < 1000; ++n) assert(!c.add(0, 0, 16384, 2000, 0, 0));
  assert(c.reason == GyroCalibration::Reason::GyroRange && c.count == 0);
  // Motion restarts the complete still window; orientation need not be level.
  for (int n = 0; n < 250; ++n) assert(!c.add(0, 0, 16384, 0, 0, 0));
  assert(!c.add(0, 16384, 0, 0, 0, 0));
  assert(c.count == 1);
  for (int n = 1; n < 500; ++n) c.add(0, 16384, 0, 0, 0, 0);
  assert(c.count == 500 && c.mean(1) == 16384);
}
