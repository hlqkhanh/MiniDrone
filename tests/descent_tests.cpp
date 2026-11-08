#include "../firmware/DroneRebuild/StabilizedDescent.h"
#include <cassert>
#include <cstring>
#include <iostream>
using namespace rebuild;

int main() {
    Session s;assert(s.arm(1,0,1000));assert(s.start(1,0,2000,0));
    Config r,p;r.kp=p.kp=.2;r.ki=p.ki=.15;r.kd=p.kd=.001;
    r.angleKp=p.angleKp=1;r.base=p.base=110;
    StabilizedDescent descent;
    auto start=[&](uint32_t owner=1,uint32_t epoch=0,Mode mode=Mode::Angle,bool both=true,bool bench=false,double rate=2,double floor=80){
        return descent.begin(s,owner,epoch,r,p,mode,both,bench,3,-2,rate,floor);
    };
    assert(!start(2));assert(!start(1,1));assert(!start(1,0,Mode::Rate));
    assert(!start(1,0,Mode::DryRun));assert(!start(1,0,Mode::Angle,false));
    assert(!start(1,0,Mode::Angle,true,true));
    for(double rate:{double(NAN),0.,.09,10.01})assert(!start(1,0,Mode::Angle,true,false,rate));
    for(double floor:{double(NAN),0.,40.,110.,111.})assert(!start(1,0,Mode::Angle,true,false,2,floor));
    p.base=109;assert(!start());p.base=110;
    p.angleKp=0;assert(!start());p.angleKp=1;
    assert(start());assert(!start());
    assert(descent.collective()==110 && descent.active() && !descent.paused());
    assert(!descent.step(NAN) && !descent.step(.03));assert(descent.collective()==110);

    DualController pid;
    // Match an uninterrupted dual-controller reference on entry: no I/D reset.
    DualController reference;
    auto initial=pid.step(r,p,Mode::Angle,3,-2,0,0,0,0,.005);
    reference.step(r,p,Mode::Angle,3,-2,0,0,0,0,.005);
    (void)initial;
    assert(descent.step(.005));
    Config dr=r,dp=p;dr.base=dp.base=descent.collective();
    auto first=pid.step(dr,dp,Mode::Angle,descent.rollTarget(),descent.pitchTarget(),0,0,0,0,.005);
    auto expected=reference.step(dr,dp,Mode::Angle,descent.rollTarget(),descent.pitchTarget(),0,0,0,0,.005);
    assert(first.roll.i==expected.roll.i && first.pitch.i==expected.pitch.i);
    assert(first.roll.i>0 && first.pitch.i<0);
    double previous=descent.collective();
    unsigned changesAtFloor=0,previousLF=0;
    for(int n=1;n<4000;n++) {
        assert(descent.step(.005));
        assert(descent.collective()<=previous && descent.collective()>=80);
        previous=descent.collective();
        dr.base=dp.base=descent.collective();
        // Disturbances continue to drive live, independent corrections at the floor.
        auto o=pid.step(dr,dp,Mode::Angle,descent.rollTarget(),descent.pitchTarget(),
                        n%2?-2:2,n%3?-1:1,0,0,.005);
        assert(!std::strcmp(o.roll.fault,"NONE"));
        for(auto m:o.roll.motor)assert(m>0 && m<=255);
        if(descent.atFloor() && o.roll.motor[0]!=previousLF)changesAtFloor++;
        previousLF=o.roll.motor[0];
        const uint64_t now=2000+uint64_t(n)*5000;
        s.sampleAt=now;s.heartbeat(1,now);assert(s.check(now));
    }
    assert(descent.atFloor() && descent.collective()==80);
    assert(changesAtFloor>100); // Fresh corrections, not frozen duty decay.
    assert(s.state==State::Running); // Floor is not touchdown/disarm.
    assert(descent.rollTarget()==0 && descent.pitchTarget()==0);
    assert(!descent.resume(s,1,0));
    assert(!descent.boost(s,2,0,5));assert(!descent.boost(s,1,1,5));
    assert(!descent.boost(s,1,0,NAN));assert(!descent.boost(s,1,0,11));
    assert(descent.boost(s,1,0,5));assert(descent.collective()==85 && descent.paused());
    for(int n=0;n<100;n++)assert(descent.step(.005));
    assert(descent.collective()==85);
    assert(descent.resume(s,1,0));assert(descent.step(.005));assert(descent.collective()<85);
    assert(descent.pause(s,1,0));previous=descent.collective();
    assert(descent.step(.005) && descent.collective()==previous);
    for(int n=0;n<10;n++)descent.boost(s,1,0,10);
    assert(descent.collective()==110 && !descent.boost(s,1,0,5));
    assert(r.base==110 && p.base==110); // Tuned config never gets overwritten.

    // The same Session watchdogs remain active throughout descent.
    auto stale=s;assert(!stale.check(stale.sampleAt+100000));
    assert(!std::strcmp(stale.reason,"STALE_CONTROL"));
    auto lost=s;lost.sampleAt=lost.heartbeatAt+2000000;
    assert(!lost.check(lost.sampleAt));assert(!std::strcmp(lost.reason,"HEARTBEAT"));
    s.stop();assert(!descent.pause(s,1,0));assert(!descent.resume(s,1,0));
    descent.reset();assert(!descent.active());
    std::cout<<"PASS descent eligibility/owner/epoch, live dual PID, smooth targets, floor hold, pause/resume/boost, unchanged config and watchdogs\n";
}
