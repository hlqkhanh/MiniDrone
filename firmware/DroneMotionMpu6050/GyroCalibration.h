#pragma once
#include <stdint.h>
#include <math.h>

// Raw readings at +/-2 g and +/-500 deg/s. Estimate the unknown zero offset
// from a stable window, rather than assuming the offset is already near zero.
class GyroCalibration {
 public:
  static constexpr uint16_t RequiredSamples = 500;
  enum class Reason { Collecting, Motion, Acceleration, GyroRange };
  uint16_t count = 0;
  Reason reason = Reason::Collecting;

  void reset() {
    count = 0;
    reason = Reason::Collecting;
    for (int i = 0; i < 6; ++i) sums_[i] = 0;
  }

  bool add(int16_t ax, int16_t ay, int16_t az, int16_t gx, int16_t gy, int16_t gz) {
    if (count == RequiredSamples) return true;
    const int16_t values[] = {ax, ay, az, gx, gy, gz};
    const float a = sqrtf(float(ax)*ax + float(ay)*ay + float(az)*az) / 16384.0f;
    if (a < 0.85f || a > 1.15f) { reset(); reason = Reason::Acceleration; return false; }
    if (fabsf(gx/65.5f) > 15 || fabsf(gy/65.5f) > 15 || fabsf(gz/65.5f) > 15) {
      reset(); reason = Reason::GyroRange; return false;
    }
    if (count) {
      for (int i = 0; i < 6; ++i) {
        const int lo = values[i] < minimum_[i] ? values[i] : minimum_[i];
        const int hi = values[i] > maximum_[i] ? values[i] : maximum_[i];
        if (hi - lo > (i < 3 ? 1311 : 131)) { // 0.08 g / 2 deg/s peak-to-peak
          reset();
          reason = Reason::Motion;
          break;
        }
      }
    }
    for (int i = 0; i < 6; ++i) {
      if (!count) minimum_[i] = maximum_[i] = values[i];
      if (values[i] < minimum_[i]) minimum_[i] = values[i];
      if (values[i] > maximum_[i]) maximum_[i] = values[i];
      sums_[i] += values[i];
    }
    ++count;
    if (count >= 100) reason = Reason::Collecting;
    return count == RequiredSamples;
  }

  float mean(int axis) const { return count ? float(sums_[axis]) / count : 0; }
  const char *reasonCode() const {
    switch (reason) {
      case Reason::Motion: return "MOTION";
      case Reason::Acceleration: return "ACCEL_NOT_1G";
      case Reason::GyroRange: return "GYRO_OUT_OF_RANGE";
      default: return "COLLECTING";
    }
  }
 private:
  int32_t sums_[6] = {};
  int16_t minimum_[6] = {}, maximum_[6] = {};
};
