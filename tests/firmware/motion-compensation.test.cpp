#include "../../firmware/DroneMotionMpu6050/MotionCompensation.h"
#include <assert.h>

int main() {
  MotionCompensation compensation;
  float bx = 0, by = 0, bz = 0;
  // Stable residual bias is learned gradually, only after the dwell period.
  for (int i = 0; i < 290; ++i)
    compensation.update(0, 0, 1, 0, 0, 0.1f - bz, 0.01f, bx, by, bz);
  assert(bz == 0);
  for (int i = 0; i < 12000; ++i)
    compensation.update(0, 0, 1, 0, 0, 0.1f - bz, 0.01f, bx, by, bz);
  assert(fabsf(bz - 0.1f) < 0.001f);
  assert(bx == 0 && by == 0);

  // A sustained turn above the conservative threshold must not become bias.
  const float before = bz;
  for (int i = 0; i < 1000; ++i)
    compensation.update(0, 0, 1, 0, 0, 0.3f, 0.01f, bx, by, bz);
  assert(bz == before);
  for (int i = 0; i < 290; ++i)
    compensation.update(0, 0, 1, 0, 0, 0.05f, 0.01f, bx, by, bz);
  assert(bz == before);

  // Translation and unstable gravity vectors must inhibit learning.
  compensation.reset();
  for (int i = 0; i < 1000; ++i)
    compensation.update(0, 0, 1.2f, 0, 0, 0.1f, 0.01f, bx, by, bz);
  assert(bz == before);
  for (int i = 0; i < 1000; ++i)
    compensation.update(i % 2 ? 0.1f : -0.1f, 0, 1, 0, 0, 0.1f,
                        0.01f, bx, by, bz);
  assert(bz == before);
  assert(MotionCompensation::accelerationWeight(0, 0, 1) == 1);
  assert(MotionCompensation::accelerationWeight(0, 0, 1.3f) == 0);
  const float weight = MotionCompensation::accelerationWeight(0, 0, 1.1f);
  assert(weight > 0 && weight < 1);
}
