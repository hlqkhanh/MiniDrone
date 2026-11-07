#include "../firmware/DroneRebuild/Imu.h"
#include <cassert>
#include <iostream>
int main() {
  RollLevel level;
  assert(!level.zero());
  for(int n=0;n<100;n++)level.observe(5.4,0,1);
  assert(level.zero());assert(std::fabs(level.angle(5.4))<1e-9);
  assert(std::fabs(level.angle(8.4)-3)<1e-9);
  // Repeated zero uses the raw angle, so the offset never accumulates.
  assert(level.zero());assert(std::fabs(level.offset()-5.4)<1e-9);
  level.observe(5.4,2,1);assert(!level.zero());
  for(int n=0;n<100;n++)level.observe(n%2?5:6,0,1);
  assert(!level.zero());
  for(int n=0;n<100;n++)level.observe(11,0,1);
  assert(!level.zero());
  level.reset();assert(level.offset()==0 && !level.ready());
  for(int n=0;n<120;n++)level.observe(-5,0,1);
  assert(level.zero());assert(std::fabs(level.angle(-2)-3)<1e-9);
  level.observe(-5,0,1.2);assert(!level.ready());
  Attitude a;ImuSample s;s.az=1;
  a.initialize(s);
  for(int n=0;n<12000;n++)assert(a.update(s,0,0,0,.005));
  assert(std::fabs(a.roll())<1e-8);
  // Sensor pitch -10deg is positive body roll under the declared mounting.
  const double k=3.141592653589793/180;
  s.ax=std::sin(10*k);s.az=std::cos(10*k);
  a.initialize(s);assert(std::fabs(a.roll()-10)<1e-8);
  s.ax=-std::sin(10*k);a.initialize(s);assert(std::fabs(a.roll()+10)<1e-8);
  // Combined body roll/pitch pose, then world-vertical yaw through 180deg.
  // Gravity in body axes is [-sin(theta),sin(phi)cos(theta),cos(phi)cos(theta)].
  for(double phi:{-25.,0.,20.})for(double theta:{-15.,0.,12.}) {
    ImuSample pose;
    pose.ax=sin(phi*k)*cos(theta*k);pose.ay=sin(theta*k);pose.az=cos(phi*k)*cos(theta*k);
    a.initialize(pose);
    assert(fabs(a.roll()-phi)<1e-8);assert(fabs(a.pitch()-theta)<1e-8);
    const double yawRate=90;
    pose.gx=sin(phi*k)*cos(theta*k)*yawRate;
    pose.gy=sin(theta*k)*yawRate;
    pose.gz=cos(phi*k)*cos(theta*k)*yawRate;
    for(int n=0;n<400;n++)assert(a.update(pose,0,0,0,.005));
    assert(fabs(a.roll()-phi)<.15);assert(fabs(a.pitch()-theta)<.15);
  }
  // Roll uses atan2 after the body transform: no fold-back at 90 degrees.
  for(double phi:{85.,95.,135.,-135.}) {
    ImuSample pose;pose.ax=sin(phi*k);pose.az=cos(phi*k);
    a.initialize(pose);assert(fabs(a.roll()-phi)<1e-8);
  }
  s.ax=0;s.az=1;a.initialize(s);
  s.gz=90;
  for(int n=0;n<200;n++)assert(a.update(s,0,0,0,.005));
  assert(std::fabs(a.yaw()-90)<.01);
  assert(std::fabs(a.roll())<1e-8 && std::fabs(a.pitch())<1e-8);
  s.gz=0;a.initialize(s);
  s.gy=-10;
  // High acceleration suppresses accel correction, exposing gyro sign.
  s.az=2;
  for(int n=0;n<200;n++)assert(a.update(s,0,0,0,.005));
  assert(std::fabs(a.roll()-10)<.01);
  assert(!a.update(s,0,0,0,.1));
  s.ax=NAN;assert(!a.update(s,0,0,0,.005));
  std::cout<<"PASS level offsets, body Roll/Pitch poses, world-yaw invariance, inverted roll, drift, gyro sign and invalid samples\n";
}
