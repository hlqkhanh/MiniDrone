#include "../../firmware/DroneMotionMpu6050/FlightController.h"
#include <assert.h>
#include <limits>

int main() {
  Flight::Controller fc;
  assert(!fc.start(60,0)); // Zero default gains cannot start.
  assert(!fc.setGains(1,{NAN,0,0}));
  assert(!fc.setGains(1,{6,0,0}));
  assert(fc.setGains(1,{0.2f,0,0}));
  assert(fc.setGains(2,{0.2f,0,0}));
  fc.attitude(1,0,0,0,0,0);
  assert(fc.start(60,1));
  assert(!fc.setGains(1,{1,0,0}));
  fc.roll=5;
  assert(fc.update(0.005f));
  assert(fc.rollOutput<0 && fc.pitchOutput==0);
  assert(fc.duties[1]>fc.duties[0] && fc.duties[3]>fc.duties[2]);
  fc.roll=1; fc.rollRate=-20;
  assert(fc.update(0.005f)); assert(fc.rollOutput>0); // Brake before crossing zero.
  fc.pitch=30;
  assert(!fc.update(0.005f) && !fc.active);
  for(auto duty:fc.duties) assert(duty==0);
  fc.pitch=0; fc.roll=0;
  assert(fc.start(60,2)); fc.pitch=5;
  assert(fc.update(0.005f));
  assert(fc.duties[0]>fc.duties[2]);
  assert(!fc.update(0.03f)); // Timing fault.
  fc.attitude(NAN,0,0,0,0,0);
  assert(!fc.start(60,0));
  fc.attitude(1,0,0,0,0,0);
  assert(fc.start(60,0));
  assert(!fc.update(std::numeric_limits<float>::quiet_NaN()));

  Flight::PID pid;
  pid.gains={0,1,0};
  for(int n=0;n<20000;n++) pid.update(10,0,0.005f,true);
  assert(pid.i<=20 && pid.i>19);
  float stored=pid.i; pid.update(10,0,0.005f,false); assert(pid.i==stored);
  pid.reset(); assert(pid.i==0);
  pid.gains={0,0,0.1f};
  assert(pid.update(0,10,0.005f,true)==0); // No derivative startup spike.
  assert(pid.update(0,20,0.005f,true)<0);
  // Fixed measurement, changed target: derivative has no setpoint kick.
  pid.reset(); pid.update(0,10,0.005f,true);
  assert(pid.update(40,10,0.005f,true)==0);

  // Simple deterministic roll plant: exercise the full cascade and mixer over time.
  fc.stop(); fc.roll=8; fc.pitch=0; fc.rollRate=0;
  fc.rollPID.gains={0.5f,0.05f,0.005f};
  assert(fc.start(80,1));
  for(int n=0;n<2000;n++) {
    assert(fc.update(0.005f));
    fc.rollRate += (fc.rollOutput*15 - fc.rollRate*2)*0.005f;
    fc.roll += fc.rollRate*0.005f;
  }
  assert(fabsf(fc.roll)<0.5f && fabsf(fc.rollRate)<0.5f);
}
