#pragma once
#include <math.h>

// Inputs: acceleration in g, angular velocity and bias in degrees/second.
// This estimates zero-rate offset, not an absolute heading reference.
class MotionCompensation {
 public:
  void reset() { elapsedStill = 0; hasPrevious = false; }

  void update(float ax, float ay, float az, float gx, float gy, float gz,
              float dt, float &bx, float &by, float &bz) {
    const float norm = sqrtf(ax * ax + ay * ay + az * az);
    const float dx = ax - previousX, dy = ay - previousY, dz = az - previousZ;
    const bool still = hasPrevious && fabsf(norm - 1.0f) < 0.03f &&
        dx * dx + dy * dy + dz * dz < 0.0004f &&
        gx * gx + gy * gy + gz * gz < 0.0225f;
    previousX = ax; previousY = ay; previousZ = az;
    hasPrevious = true;
    if (!still || dt <= 0 || dt > 0.05f) { elapsedStill = 0; return; }
    elapsedStill += dt;
    if (elapsedStill < 3.0f) return;
    elapsedStill = 3.0f;
    const float gain = dt / (20.0f + dt);
    bx += gain * gx; by += gain * gy; bz += gain * gz;
  }

  static float accelerationWeight(float ax, float ay, float az) {
    const float error = fabsf(sqrtf(ax * ax + ay * ay + az * az) - 1.0f);
    if (error <= 0.05f) return 1.0f;
    if (error >= 0.20f) return 0.0f;
    return (0.20f - error) / 0.15f;
  }

 private:
  float elapsedStill = 0;
  float previousX = 0, previousY = 0, previousZ = 0;
  bool hasPrevious = false;
};
