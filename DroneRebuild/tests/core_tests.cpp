#include "../firmware/DroneRebuild/RollCore.h"
#include <cassert>
#include <cstring>
#include <limits>
#include <iostream>
using namespace rebuild;
int main() {
  Config c; Controller pid;
  assert(!std::strcmp(pid.step(c,Mode::Rate,0,0,0,.005).fault,"GAINS_ZERO"));
  c.kp=.4; c.ki=.1; c.kd=.01; c.angleKp=2;
  auto o=pid.step(c,Mode::Rate,5,0,0,.005); assert(o.u>0 && o.motor[0]>o.motor[1]);
  pid.reset();auto pitch=pid.step(c,Mode::Rate,5,0,0,.005,Axis::Pitch);
  assert(pitch.u>0 && pitch.motor[0]==pitch.motor[1] && pitch.motor[2]==pitch.motor[3] && pitch.motor[0]>pitch.motor[2]);
  pid.reset();c.motorSign=-1;pitch=pid.step(c,Mode::Rate,5,0,0,.005,Axis::Pitch);
  assert(pitch.motor[0]<pitch.motor[2]);c.motorSign=1;
  assert(!std::strcmp(pid.step(c,Mode::Rate,0,0,0,.005,static_cast<Axis>(2)).fault,"CONFIG"));
  pid.reset(); pid.step(c,Mode::Rate,0,0,0,.005);
  o=pid.step(c,Mode::Rate,5,0,0,.005); assert(o.d==0); // No setpoint derivative kick.
  o=pid.step(c,Mode::Rate,5,0,10,.005); assert(o.d<0);
  pid.reset(); c.motorSign=-1; o=pid.step(c,Mode::Rate,5,0,0,.005); assert(o.motor[0]<o.motor[1]);
  c.motorSign=1;
  assert(!std::strcmp(pid.step(c,Mode::Rate,0,0,NAN,.005).fault,"SENSOR"));
  assert(!std::strcmp(pid.step(c,Mode::Rate,0,0,0,.1).fault,"TIMING"));
  // Crossing the former angle/rate envelope does not stop either axis.
  for(auto axis:{Axis::Roll,Axis::Pitch}) {
    pid.reset();assert(!std::strcmp(pid.step(c,Mode::Angle,0,30,0,.005,axis).fault,"NONE"));
    pid.reset();assert(!std::strcmp(pid.step(c,Mode::Rate,0,0,200,.005,axis).fault,"NONE"));
  }
  assert(!std::strcmp(pid.step(c,Mode::Angle,11,0,0,.005).fault,"TARGET"));
  pid.reset(); c.kp=5; c.ki=1; c.kd=0;
  // Persistent PID/mixer saturation is reported, without shutting motors off.
  for(auto axis:{Axis::Roll,Axis::Pitch}) {
    pid.reset();
    for(int n=0;n<2400;n++) {
      o=pid.step(c,Mode::Angle,0,45,0,.005,axis);
      assert(!std::strcmp(o.fault,"NONE") && o.pidLimited);
      assert(std::fabs(o.u)<=c.outputLimit && std::fabs(o.i)<=c.iLimit);
      assert(o.motor[0]>0 && o.motor[3]>0);
    }
    pid.reset();c.base=1;
    for(int n=0;n<2400;n++) {
      o=pid.step(c,Mode::Rate,20,0,0,.005,axis);
      assert(!std::strcmp(o.fault,"NONE") && o.mixerLimited);
      for(auto m:o.motor)assert(m<=220);
    }
    c.base=80;
  }
  pid.reset(); c.kp=.1; c.ki=2; c.outputLimit=20; c.iLimit=1;
  for(int n=0;n<500;n++) o=pid.step(c,Mode::Rate,1,0,0,.005);
  assert(o.i<=1 && o.i>.9);
  c.motorSign=0; assert(!c.valid()); c.motorSign=1;
  Session s; assert(!s.start(1,0,1000,1000)); assert(s.arm(1,0,1000));
  assert(!s.arm(2,0,1500)); // Armed ownership cannot be stolen.
  assert(!s.start(2,0,2000,1000)); assert(s.start(1,0,2000,1000));
  s.heartbeat(2,4000); assert(s.heartbeatAt==2000);
  assert(!s.check(102000) && !std::strcmp(s.reason,"STALE_CONTROL"));
  assert(!s.arm(1,0,200000)); assert(s.arm(1,s.epoch,200000));
  assert(!s.check(15200000));
  s.stop(); auto stale=s.epoch; assert(s.arm(1,stale,16000000)); s.stop();
  assert(!s.start(1,stale,16000100,1000));
  assert(s.arm(1,s.epoch,17000000)); assert(s.start(1,s.epoch,17000000,10000));
  s.sampleAt=19000000; assert(!s.check(19000000)); assert(!std::strcmp(s.reason,"HEARTBEAT"));
  Session continuous;assert(continuous.arm(1,0,1000));assert(continuous.start(1,0,2000,0));
  assert(continuous.deadline==0);
  for(uint64_t now=2000;now<60000000;now+=50000) {
    continuous.sampleAt=now;continuous.heartbeat(1,now);assert(continuous.check(now));
  }
  continuous.stop();assert(continuous.state==State::Disarmed);
  assert(continuous.arm(1,continuous.epoch,60000000));assert(continuous.start(1,continuous.epoch,60000000,0));
  continuous.sampleAt=62000000;assert(!continuous.check(62000000));assert(!std::strcmp(continuous.reason,"HEARTBEAT"));
  Session staleContinuous;assert(staleContinuous.arm(1,0,1000));assert(staleContinuous.start(1,0,2000,0));
  assert(!staleContinuous.check(102000));assert(!std::strcmp(staleContinuous.reason,"STALE_CONTROL"));
  Session bench;assert(bench.arm(1,0,1000));assert(bench.start(1,0,2000,1000));
  bench.sampleAt=1002000;bench.heartbeat(1,1002000);assert(!bench.check(1002000));assert(!std::strcmp(bench.reason,"DURATION"));
  std::cout<<"PASS controller, limits, anti-windup, signs, faults, session ownership, STOP epochs, deadlines\n";
}
