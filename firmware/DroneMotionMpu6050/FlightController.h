#pragma once
#include <math.h>

namespace Flight {
inline float clamp(float v, float lo, float hi) { return fmaxf(lo, fminf(hi, v)); }
struct Gains { float kp = 0, ki = 0, kd = 0; };
struct PID {
  Gains gains;
  float outputLimit = 40, integralLimit = 20;
  float p = 0, i = 0, d = 0, previous = 0, filtered = 0;
  float previousError = 0;
  bool initialized = false;
  void reset() { p = i = d = previous = filtered = previousError = 0; initialized = false; }
  float update(float target, float measured, float dt, bool integrate) {
    if (!isfinite(dt) || dt <= 0 || dt > 0.02f || !isfinite(target) || !isfinite(measured)) return 0;
    const float error = target - measured;
    p = gains.kp * error;
    const float integralError = initialized ? (error + previousError) * 0.5f : error;
    float raw = initialized ? -(measured - previous) / dt : 0;
    previousError = error;
    previous = measured; initialized = true;
    filtered += (dt / (dt + 0.0159155f)) * (raw - filtered); // 10 Hz
    d = gains.kd * filtered;
    float candidate = clamp(i + gains.ki * integralError * dt, -integralLimit, integralLimit);
    float trial = p + candidate + d;
    if (integrate && (fabsf(trial) <= outputLimit || error * trial < 0)) i = candidate;
    return clamp(p + i + d, -outputLimit, outputLimit);
  }
};
class Controller {
 public:
  // Motor-side polarity for the mounted airframe; sensor angle/rate signs stay paired.
  // Reverse Roll based on the reported rig test. Pitch remains at its existing polarity.
  static constexpr float RollMotorDirection = -1.0f;
  static constexpr float PitchMotorDirection = 1.0f;
  PID rollPID, pitchPID;
  // Outer loop outputs deg/s; inner loop outputs 8-bit PWM duty corrections.
  PID rollAnglePID, pitchAnglePID;
  Controller() {
    rollAnglePID.gains = pitchAnglePID.gains = {2, 0, 0};
    rollAnglePID.outputLimit = pitchAnglePID.outputLimit = 45;
    rollAnglePID.integralLimit = pitchAnglePID.integralLimit = 10;
  }
  bool active = false, saturated = false;
  float roll = 0, pitch = 0, rollRate = 0, pitchRate = 0;
  float rollTarget = 0, pitchTarget = 0, rollRateTarget = 0, pitchRateTarget = 0;
  float rollOutput = 0, pitchOutput = 0, zeroRoll = 0, zeroPitch = 0;
  unsigned duties[4] = {};
  int axis = 0; // 0 both, 1 roll, 2 pitch
  const char *fault = "CONTROL_STOPPED";
  void stop() {
    active = false; rollPID.reset(); pitchPID.reset(); rollAnglePID.reset(); pitchAnglePID.reset();
    rollRateTarget = pitchRateTarget = rollOutput = pitchOutput = 0;
    saturated = false; saturatedFor_ = 0;
    for (auto &duty : duties) duty=0;
  }
  bool setGains(int which, Gains g) {
    if ((which < 1 || which > 4) || active || !isfinite(g.kp) || !isfinite(g.ki) || !isfinite(g.kd) ||
        g.kp < 0 || g.kp > 5 || g.ki < 0 || g.ki > 2 || g.kd < 0 || g.kd > 0.2f) return false;
    PID &pid = which == 1 ? rollPID : which == 2 ? pitchPID : which == 3 ? rollAnglePID : pitchAnglePID;
    pid.gains = g; pid.reset(); return true;
  }
  void attitude(float w, float x, float y, float z, float gx, float gy) {
    const float norm = w*w+x*x+y*y+z*z;
    if (!isfinite(norm) || norm < 0.9f || norm > 1.1f || !isfinite(gx) || !isfinite(gy)) {
      roll = pitch = NAN; return;
    }
    // Match the mounted board convention in OrientationService: roll=-sensor Y, pitch=-sensor X.
    const float degrees = 57.2957795f;
    roll = -asinf(clamp(2 * (w*y-z*x), -1, 1)) * degrees - zeroRoll;
    pitch = -atan2f(2*(w*x+y*z), 1-2*(x*x+y*y)) * degrees - zeroPitch;
    rollRate = -gy * degrees; pitchRate = -gx * degrees;
  }
  bool start(float base, int selectedAxis) {
    if (active || !isfinite(base) || base < 1 || base > 180 || selectedAxis < 0 || selectedAxis > 2 ||
        !isfinite(roll) || !isfinite(pitch) ||
        !isfinite(rollRate) || !isfinite(pitchRate) || !validTargets() ||
        (selectedAxis != 2 && (rollPID.gains.kp <= 0 || rollAnglePID.gains.kp <= 0)) ||
        (selectedAxis != 1 && (pitchPID.gains.kp <= 0 || pitchAnglePID.gains.kp <= 0))) return false;
    stop(); base_ = base; axis = selectedAxis; active = true; saturatedFor_ = 0; fault="CONTROL_RUNNING"; return true;
  }
  bool update(float dt) {
    if (!active) return false;
    if (!isfinite(dt) || dt < 0.002f || dt > 0.02f) return fail("CONTROL_TIMING_FAULT");
    if (!isfinite(roll) || !isfinite(pitch) || !isfinite(rollRate) || !isfinite(pitchRate)) return fail("CONTROL_ATTITUDE_FAULT");
    if (!validTargets()) return fail("CONTROL_SETPOINT_FAULT");
    // Measured tilt does not gate Start or stop control; invalid IMU data still does.
    // Freeze outer integration if either mixer or inner loop exhausted its authority.
    const bool rollIntegrate = !saturated && fabsf(rollOutput) < rollPID.outputLimit;
    const bool pitchIntegrate = !saturated && fabsf(pitchOutput) < pitchPID.outputLimit;
    rollRateTarget = axis == 2 ? 0 : rollAnglePID.update(rollTarget, roll, dt, rollIntegrate);
    pitchRateTarget = axis == 1 ? 0 : pitchAnglePID.update(pitchTarget, pitch, dt, pitchIntegrate);
    rollOutput = axis == 2 ? 0 : rollPID.update(rollRateTarget, rollRate, dt, !saturated);
    pitchOutput = axis == 1 ? 0 : pitchPID.update(pitchRateTarget, pitchRate, dt, !saturated);
    // Apply direction once, after both PID stages, including P/I/D damping terms.
    const float motorRoll = RollMotorDirection * rollOutput;
    const float motorPitch = PitchMotorDirection * pitchOutput;
    // Motor order: LF, RF, LR, RR (GPIO14, GPIO15, GPIO12, GPIO13).
    float mixed[] = {base_+motorRoll-motorPitch, base_-motorRoll-motorPitch,
                     base_+motorRoll+motorPitch, base_-motorRoll+motorPitch};
    saturated = false;
    for (int n=0;n<4;n++) {
      if (mixed[n] < 0 || mixed[n] > 220) saturated = true;
      duties[n] = static_cast<unsigned>(clamp(mixed[n], 0, 220) + 0.5f);
    }
    saturatedFor_ = saturated ? saturatedFor_ + dt : 0;
    if (saturatedFor_ > 0.5f) return fail("CONTROL_SATURATION_LIMIT");
    return true;
  }
 private:
  bool validTargets() const {
    return isfinite(rollTarget) && isfinite(pitchTarget) && fabsf(rollTarget) <= 10 && fabsf(pitchTarget) <= 10;
  }
  bool fail(const char *reason) { stop(); fault=reason; return false; }
  float base_ = 0, saturatedFor_ = 0;
};
}
