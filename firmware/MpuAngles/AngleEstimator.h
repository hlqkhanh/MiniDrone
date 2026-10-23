#pragma once
#include <math.h>

// Six-axis quaternion complementary filter. Inputs: g, deg/s, seconds.
// Gravity corrects roll/pitch only; yaw has no absolute reference.
class AngleEstimator {
 public:
  float roll = 0, pitch = 0, yaw = 0;

  void reset(float ax, float ay, float az) {
    const float r = atan2f(ay, az) * 0.5f;
    const float p = atan2f(-ax, sqrtf(ay*ay + az*az)) * 0.5f;
    w_ = cosf(r)*cosf(p); x_ = sinf(r)*cosf(p);
    y_ = cosf(r)*sinf(p); z_ = -sinf(r)*sinf(p);
    angles();
  }

  bool update(float ax, float ay, float az, float gx, float gy, float gz, float dt) {
    if (!isfinite(dt) || dt <= 0 || dt > 0.05f ||
        !isfinite(ax) || !isfinite(ay) || !isfinite(az) ||
        !isfinite(gx) || !isfinite(gy) || !isfinite(gz)) return false;
    constexpr float radians = 0.01745329252f;
    gx *= radians; gy *= radians; gz *= radians;
    const float norm = sqrtf(ax*ax + ay*ay + az*az);
    if (norm > 0.85f && norm < 1.15f) {
      ax /= norm; ay /= norm; az /= norm;
      const float vx = 2*(x_*z_ - w_*y_);
      const float vy = 2*(w_*x_ + y_*z_);
      const float vz = 1 - 2*(x_*x_ + y_*y_);
      gx += 2*(ay*vz - az*vy);
      gy += 2*(az*vx - ax*vz);
      gz += 2*(ax*vy - ay*vx);
    }
    const float halfDt = 0.5f*dt;
    const float w = w_, x = x_, y = y_, z = z_;
    w_ += (-x*gx - y*gy - z*gz)*halfDt;
    x_ += ( w*gx + y*gz - z*gy)*halfDt;
    y_ += ( w*gy - x*gz + z*gx)*halfDt;
    z_ += ( w*gz + x*gy - y*gx)*halfDt;
    const float qnorm = sqrtf(w_*w_ + x_*x_ + y_*y_ + z_*z_);
    w_ /= qnorm; x_ /= qnorm; y_ /= qnorm; z_ /= qnorm;
    angles();
    return true;
  }
 private:
  float w_ = 1, x_ = 0, y_ = 0, z_ = 0;
  void angles() {
    constexpr float degrees = 57.29577951f;
    roll = atan2f(2*(w_*x_ + y_*z_), 1-2*(x_*x_ + y_*y_))*degrees;
    const float sinePitch = 2*(w_*y_ - z_*x_);
    pitch = asinf(fmaxf(-1, fminf(1, sinePitch)))*degrees;
    yaw = atan2f(2*(w_*z_ + x_*y_), 1-2*(y_*y_ + z_*z_))*degrees;
  }
};
