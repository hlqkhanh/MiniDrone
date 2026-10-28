#pragma once
#include <cmath>
#include <cstdint>

// Canonical controller: firmware and native simulation compile THIS file.
namespace rebuild {
inline double bound(double x, double lo, double hi) { return x < lo ? lo : x > hi ? hi : x; }
struct Config {
  double kp=0, ki=0, kd=0, angleKp=0;
  double outputLimit=20, iLimit=10, rateLimit=20, dHz=10;
  double base=80, motorSign=1, maxAngle=15, maxRate=150;
  double saturationSeconds=0.5, saturationError=5;
  bool valid() const {
    const double all[]={kp,ki,kd,angleKp,outputLimit,iLimit,rateLimit,dHz,base,motorSign,maxAngle,maxRate,saturationSeconds,saturationError};
    for(double x:all) if(!std::isfinite(x)) return false;
    return kp>=0 && kp<=5 && ki>=0 && ki<=2 && kd>=0 && kd<=0.2 && angleKp>=0 && angleKp<=5 &&
      outputLimit>0 && outputLimit<=40 && iLimit>=0 && iLimit<=outputLimit && rateLimit>0 && rateLimit<=45 &&
      dHz>0 && dHz<=50 && base>=1 && base<=180 && std::fabs(motorSign)==1 &&
      maxAngle>0 && maxAngle<=30 && maxRate>0 && maxRate<=300 && saturationSeconds>0 && saturationSeconds<=2 && saturationError>0;
  }
};
enum class Mode { Rate=0, Angle=1, DryRun=2 };
enum class Axis { Roll=0, Pitch=1 };
struct Output {
  double target=0,error=0,p=0,i=0,d=0,raw=0,u=0;
  unsigned motor[4]={0,0,0,0};
  bool pidLimited=false,mixerLimited=false;
  const char* fault="NONE";
};
class Controller {
  double integral_=0, previous_=0, previousError_=0, derivative_=0, limitedFor_=0;
  bool initialized_=false, previousMixerLimited_=false;
public:
  void reset() { *this=Controller{}; }
  Output step(const Config& c, Mode mode, double command, double angle, double rate, double dt, Axis axis=Axis::Roll) {
    Output o;
    auto fail=[&](const char* why) { reset(); Output f; f.fault=why; return f; };
    if(!c.valid() || static_cast<int>(mode)<0 || static_cast<int>(mode)>2 || (axis!=Axis::Roll && axis!=Axis::Pitch)) return fail("CONFIG");
    if(!std::isfinite(command)||!std::isfinite(angle)||!std::isfinite(rate)) return fail("SENSOR");
    if(!std::isfinite(dt)||dt<0.002||dt>0.02) return fail("TIMING");
    if(std::fabs(angle)>c.maxAngle || std::fabs(rate)>c.maxRate) return fail("ENVELOPE");
    if(c.kp<=0 || (mode==Mode::Angle && c.angleKp<=0)) return fail("GAINS_ZERO");
    if((mode==Mode::Angle && std::fabs(command)>10) || (mode!=Mode::Angle && std::fabs(command)>c.rateLimit)) return fail("TARGET");
    o.target=mode==Mode::Angle ? bound(c.angleKp*(command-angle),-c.rateLimit,c.rateLimit) : command;
    o.error=o.target-rate; o.p=c.kp*o.error;
    const double rawD=initialized_ ? -(rate-previous_)/dt : 0;
    const double alpha=dt/(dt+1/(6.283185307179586*c.dHz));
    derivative_+=alpha*(rawD-derivative_); o.d=c.kd*derivative_;
    const double ie=initialized_ ? (o.error+previousError_)*0.5 : o.error;
    const double candidate=bound(integral_+c.ki*ie*dt,-c.iLimit,c.iLimit);
    const double trial=o.p+candidate+o.d;
    if(!previousMixerLimited_ && (std::fabs(trial)<=c.outputLimit || o.error*trial<0)) integral_=candidate;
    o.i=integral_; o.raw=o.p+o.i+o.d; o.u=bound(o.raw,-c.outputLimit,c.outputLimit);
    o.pidLimited=std::fabs(o.raw)>=c.outputLimit;
    const double r=c.motorSign*o.u;
    // Roll: left/right. Pitch: front/rear. motorSign must be checked
    // independently for each axis; these are commands, not measured forces.
    const double mixed[]={c.base+r,c.base+(axis==Axis::Roll?-r:r),c.base+(axis==Axis::Roll?r:-r),c.base-r};
    for(int n=0;n<4;n++) {
      o.mixerLimited |= mixed[n]<0 || mixed[n]>220;
      o.motor[n]=static_cast<unsigned>(std::floor(bound(mixed[n],0,220)+0.5));
    }
    limitedFor_=(o.pidLimited && std::fabs(o.error)>c.saturationError) || o.mixerLimited ? limitedFor_+dt : 0;
    if(limitedFor_>=c.saturationSeconds) return fail("SATURATION_TRACKING");
    previous_=rate; previousError_=o.error; initialized_=true; previousMixerLimited_=o.mixerLimited;
    return o;
  }
};
enum class State { Disarmed, Armed, Running, Fault };
class Session {
public:
  State state=State::Disarmed;
  uint32_t owner=0,epoch=0;
  uint64_t armedAt=0,heartbeatAt=0,sampleAt=0,deadline=0;
  const char* reason="BOOT";
  void stop(const char* why="STOP") { state=State::Disarmed; owner=0; ++epoch; reason=why; }
  bool arm(uint32_t client,uint32_t token,uint64_t now) {
    if(token!=epoch || client==0 || (state!=State::Disarmed && state!=State::Fault)) return false;
    state=State::Armed; owner=client; armedAt=now; reason="ARMED"; return true;
  }
  bool start(uint32_t client,uint32_t token,uint64_t now,uint32_t durationMs) {
    if(state!=State::Armed || token!=epoch || client!=owner || now-armedAt>=15000000 || durationMs<1 || durationMs>10000) return false;
    state=State::Running; deadline=now+uint64_t(durationMs)*1000; sampleAt=heartbeatAt=now; reason="RUNNING"; return true;
  }
  void heartbeat(uint32_t client,uint64_t now) { if(client==owner && state==State::Running) heartbeatAt=now; }
  bool check(uint64_t now) {
    const char* why=nullptr;
    if(state==State::Armed && now-armedAt>=15000000) why="ARM_TIMEOUT";
    if(state==State::Running) {
      if(now>=deadline) why="DURATION";
      else if(now-sampleAt>=100000) why="STALE_CONTROL";
      else if(now-heartbeatAt>=2000000) why="HEARTBEAT";
    }
    if(why) { stop(why); state=State::Fault; return false; }
    return true;
  }
};
}
