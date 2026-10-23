#include "../../firmware/MpuAngles/AngleEstimator.h"
#include <assert.h>

int main() {
  AngleEstimator a;
  a.reset(0, 0, 1);
  for (int n = 0; n < 1000; ++n) assert(a.update(0, 0, 1, 0, 0, 0, 0.005f));
  assert(fabsf(a.roll) < 0.01f && fabsf(a.pitch) < 0.01f && fabsf(a.yaw) < 0.01f);
  // One second at 90 deg/s yaw must produce approximately 90 degrees.
  for (int n = 0; n < 200; ++n) assert(a.update(0, 0, 1, 0, 0, 90, 0.005f));
  assert(fabsf(a.yaw - 90) < 0.01f);
  assert(fabsf(a.roll) < 0.01f && fabsf(a.pitch) < 0.01f);
  // Known gravity poses and correction toward a tilted, stationary sensor.
  a.reset(0, 0.5f, 0.8660254f);
  assert(fabsf(a.roll - 30) < 0.01f && fabsf(a.pitch) < 0.01f);
  a.reset(-0.70710678f, 0, 0.70710678f);
  assert(fabsf(a.pitch - 45) < 0.01f);
  a.reset(0, 0, 1);
  for (int n = 0; n < 1000; ++n) a.update(0, 0.5f, 0.8660254f, 0, 0, 0, 0.005f);
  assert(fabsf(a.roll - 30) < 0.01f);
  const float before = a.roll;
  assert(!a.update(0, 0, 1, 0, 0, 0, 1));
  assert(!a.update(0, 0, 1, NAN, 0, 0, 0.005f));
  assert(a.roll == before);
  // Reinitialization discards the previous relative yaw.
  a.reset(0, 0, 1);
  assert(fabsf(a.yaw) < 0.01f);
}
