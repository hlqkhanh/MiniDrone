#include "../firmware/DroneRebuild/Imu.h"
#include <cassert>
#include <iostream>
int main() {
  Attitude a;ImuSample s;s.az=1;
  a.initialize(s);
  for(int n=0;n<12000;n++)assert(a.update(s,0,0,0,.005));
  assert(std::fabs(a.roll())<1e-8);
  // Sensor pitch -10deg is positive body roll under the declared mounting.
  const double k=3.141592653589793/180;
  s.ax=std::sin(10*k);s.az=std::cos(10*k);
  a.initialize(s);assert(std::fabs(a.roll()-10)<1e-8);
  s.ax=-std::sin(10*k);a.initialize(s);assert(std::fabs(a.roll()+10)<1e-8);
  s.ax=0;s.az=1;a.initialize(s);
  s.gy=-10;
  // High acceleration suppresses accel correction, exposing gyro sign.
  s.az=2;
  for(int n=0;n<200;n++)assert(a.update(s,0,0,0,.005));
  assert(std::fabs(a.roll()-10)<.01);
  assert(!a.update(s,0,0,0,.1));
  s.ax=NAN;assert(!a.update(s,0,0,0,.005));
  std::cout<<"PASS Madgwick poses, stationary drift, gyro sign and invalid dt/sample\n";
}
