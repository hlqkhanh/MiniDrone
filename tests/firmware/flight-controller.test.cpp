#include "../../firmware/DroneMotionMpu6050/FlightController.h"
#include <assert.h>
#include <limits>
#include <initializer_list>

int main() {
  Flight::Controller fc;
  assert(fc.baseDuty() == 0);
  assert(Flight::Controller::MotorMinimum == 0);
  assert(Flight::Controller::MotorMaximum == 220);
  assert(!fc.start(60,0)); // Zero default gains cannot start.
  assert(!fc.setGains(1,{NAN,0,0}));
  assert(!fc.setGains(1,{6,0,0}));
  assert(fc.setGains(1,{0.2f,0,0}));
  assert(fc.setGains(2,{0.2f,0,0}));
  fc.attitude(1,0,0,0,0,0);
  assert(fc.start(60,1));
  assert(fc.baseDuty() == 60 && fc.axis == 1);
  assert(!fc.setGains(1,{1,0,0}));
  fc.roll=5;
  assert(fc.update(0.005f));
  assert(fc.rollOutput<0 && fc.pitchOutput==0);
  assert(fc.duties[0]>fc.duties[1] && fc.duties[2]>fc.duties[3]);
  fc.roll=1; fc.rollRate=-20;
  assert(fc.update(0.005f)); assert(fc.rollOutput>0); // Brake before crossing zero.
  fc.pitch=NAN;
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
    // Model the corrected airframe polarity through actual, quantized motor outputs.
    const float rollTorque = (static_cast<float>(fc.duties[1])+fc.duties[3]-fc.duties[0]-fc.duties[2])*0.25f;
    fc.rollRate += (rollTorque*15 - fc.rollRate*2)*0.005f;
    fc.roll += fc.rollRate*0.005f;
  }
  assert(fabsf(fc.roll)<0.5f && fabsf(fc.rollRate)<0.5f);

  // Outer PID is tunable independently, outputs rates, and resets with Stop.
  fc.stop(); fc.roll=5; fc.rollRate=0; fc.pitch=0;
  assert(fc.setGains(3,{2,0.5f,0.007f}));
  assert(fc.setGains(4,{2,0,0}));
  assert(!fc.setGains(5,{2,0,0}));
  assert(fc.start(80,1));
  assert(!fc.setGains(3,{1,0,0}));
  assert(fc.update(0.005f));
  assert(fc.rollRateTarget < -10 && fc.rollAnglePID.i < 0);
  assert(fc.pitchRateTarget==0 && fc.pitchAnglePID.i==0);
  fc.stop(); assert(fc.rollAnglePID.i==0 && fc.rollRateTarget==0 && fc.rollOutput==0);

  // Derivative responds to motion, not a target step, in both PID stages.
  fc.roll=0; fc.rollTarget=10;
  assert(fc.setGains(3,{5,0,0.1f}));
  assert(fc.start(80,1)); assert(fc.update(0.005f));
  assert(fc.rollRateTarget==45 && fc.rollAnglePID.d==0);
  fc.rollTarget=-10; assert(fc.update(0.005f));
  assert(fc.rollRateTarget==-45 && fc.rollAnglePID.d==0);
  fc.stop(); fc.rollTarget=NAN; assert(!fc.start(80,1));
  fc.rollTarget=0; assert(fc.start(80,1));
  fc.rollTarget=11; assert(!fc.update(0.005f));
  for (auto duty:fc.duties) assert(duty==0);

  // Saturation of the inner loop prevents outer integrator growth next cycle.
  fc.rollTarget=0; fc.roll=5; fc.rollRate=0;
  assert(fc.setGains(1,{5,0,0}));
  assert(fc.setGains(3,{2,0.5f,0}));
  assert(fc.start(80,1)); assert(fc.update(0.005f));
  assert(fc.rollOutput==-40);
  const float outerI=fc.rollAnglePID.i;
  assert(fc.update(0.005f)); assert(fc.rollAnglePID.i==outerI);
  fc.stop(); fc.rollRate=NAN; assert(!fc.start(80,1));

  // Trapezoidal integration and independent output units/limits.
  Flight::PID outer;
  outer.outputLimit=45; outer.integralLimit=10; outer.gains={0,1,0};
  outer.update(2,0,0.005f,true); outer.update(4,0,0.005f,true);
  assert(fabsf(outer.i-0.025f)<1e-6f);
  for(int n=0;n<10000;n++) outer.update(4,0,0.005f,true);
  assert(outer.i<=10 && outer.i>9.9f);

  // Exercise nonzero outer I/D in closed loop, not just the legacy P-only mode.
  fc.stop(); fc.roll=8; fc.pitch=0; fc.rollRate=0; fc.rollTarget=0;
  assert(fc.setGains(1,{0.5f,0.05f,0.005f}));
  assert(fc.setGains(3,{2,0.05f,0.01f}));
  assert(fc.start(80,1));
  for(int n=0;n<4000;n++) {
    assert(fc.update(0.005f));
    const float rollTorque = (static_cast<float>(fc.duties[1])+fc.duties[3]-fc.duties[0]-fc.duties[2])*0.25f;
    fc.rollRate += (rollTorque*15 - fc.rollRate*2)*0.005f;
    fc.roll += fc.rollRate*0.005f;
  }
  assert(fabsf(fc.roll)<0.5f && fabsf(fc.rollRate)<0.5f);

  // Measured angles beyond the former 10-degree Start / 25-degree run gates are accepted.
  // This checks software acceptance and output bounds, not physical recovery capability.
  fc.stop(); fc.rollTarget=fc.pitchTarget=0; fc.rollRate=fc.pitchRate=0;
  assert(fc.setGains(1,{0.2f,0,0}));
  assert(fc.setGains(2,{0.2f,0,0}));
  assert(fc.setGains(3,{2,0,0}));
  assert(fc.setGains(4,{2,0,0}));
  const float poses[][2] = {{-21.2f,1}, {10.1f,-10.1f}, {30,-35}, {-60,60}, {80,179}, {-80,-179}};
  for (const auto &pose: poses) {
    fc.stop(); fc.roll=pose[0]; fc.pitch=pose[1];
    assert(fc.start(75,0));
    for(int n=0;n<150;n++) {
      assert(fc.update(0.005f) && fc.active);
      assert(fabsf(fc.rollRateTarget)<=45 && fabsf(fc.pitchRateTarget)<=45);
      for(auto duty:fc.duties) assert(duty<=220);
    }
  }
  // Crossing the former tilt cutoff during an active run also remains allowed.
  fc.roll=-85; fc.pitch=160;
  assert(fc.update(0.005f) && fc.active);
  fc.roll=NAN;
  assert(!fc.update(0.005f) && !fc.active);
  for(auto duty:fc.duties) assert(duty==0);
  fc.roll=0; fc.pitch=INFINITY;
  assert(!fc.start(75,0));

  // Sign checks in both directions include the quaternion and gyro mapping.
  // These assert electrical output polarity, not unmeasured physical left/right signs.
  for (float degrees : {-5.0f, 5.0f}) {
    fc.stop();
    const float half = degrees * 0.01745329252f * 0.5f;
    fc.attitude(cosf(half),0,sinf(half),0,0,0);
    assert(fc.start(75,1)); assert(fc.update(0.005f));
    if (fc.roll>0) assert(fc.duties[0]>fc.duties[1] && fc.duties[2]>fc.duties[3]);
    else assert(fc.duties[0]<fc.duties[1] && fc.duties[2]<fc.duties[3]);
    assert(fc.duties[0]==fc.duties[2] && fc.duties[1]==fc.duties[3]);

    fc.stop();
    fc.attitude(cosf(half),sinf(half),0,0,0,0);
    assert(fc.start(75,2)); assert(fc.update(0.005f));
    if (fc.pitch>0) assert(fc.duties[0]>fc.duties[2] && fc.duties[1]>fc.duties[3]);
    else assert(fc.duties[0]<fc.duties[2] && fc.duties[1]<fc.duties[3]);
    assert(fc.duties[0]==fc.duties[1] && fc.duties[2]==fc.duties[3]);
  }
  // At level, roll gyro feedback must oppose rotation with the same mixer polarity.
  for (float sensorRate : {-0.1f, 0.1f}) {
    fc.stop(); fc.attitude(1,0,0,0,0,sensorRate);
    assert(fc.start(75,1)); assert(fc.update(0.005f));
    if (fc.rollRate>0) assert(fc.duties[0]>fc.duties[1]);
    else assert(fc.duties[0]<fc.duties[1]);
  }
}
