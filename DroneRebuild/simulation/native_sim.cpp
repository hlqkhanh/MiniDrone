#include "../firmware/DroneRebuild/RollCore.h"
#include <fstream>
#include <iomanip>
#include <iostream>
#include <string>
#include <cstring>
using namespace rebuild;
int main(int argc,char**argv) {
  const std::string folder=argc>1?argv[1]:".";
  std::ofstream csv(folder+"/native_roll.csv"), fixture(folder+"/controller_vectors.csv");
  if(!csv || !fixture) return 2;
  csv<<"t,angle,rate,target,p,i,d,raw,output,LF,RF,LR,RR,pidLimited,mixerLimited,fault\n";
  fixture<<"scenario,n,mode,command,angle,rate,dt,target,error,p,i,d,raw,u,LF,RF,LR,RR,pidLimited,mixerLimited,fault\n";
  csv<<std::setprecision(15); fixture<<std::setprecision(15);
  Config c; c.kp=.8; c.ki=.05; c.kd=.005; c.angleKp=2;
  // Synthetic parameters, deliberately NOT measured drone parameters.
  for(int scenario=0;scenario<6;scenario++) {
    Controller ctl;
    for(int n=0;n<600;n++) {
      const double t=n*.005, cmd=scenario==1?3:5*std::sin(3*t);
      double angle=scenario==1?2*std::sin(t):0, rate=8*std::sin(7*t), dt=.005;
      if(scenario==2) rate=-50;
      if(scenario==3 && n==150) dt=.03;
      if(scenario==4 && n==150) angle=16;
      if(scenario==5 && n==150) rate=NAN;
      auto mode=scenario==1?Mode::Angle:Mode::Rate;
      auto o=ctl.step(c,mode,cmd,angle,rate,dt);
      fixture<<scenario<<','<<n<<','<<int(mode)<<','<<cmd<<','<<angle<<','<<rate<<','<<dt<<','<<o.target<<','<<o.error<<','<<o.p<<','<<o.i<<','<<o.d<<','<<o.raw<<','<<o.u;
      for(auto m:o.motor) fixture<<','<<m;
      fixture<<','<<o.pidLimited<<','<<o.mixerLimited<<','<<o.fault<<'\n';
    }
  }
  bool all=true;
  for(double strength:{.5,1.,1.5}) for(double lag:{.02,.06,.12}) for(double initial:{-3.,3.}) {
    Controller ctl; double angle=initial,rate=0,torque=0; bool ok=true; double limited=0;
    for(int n=0;n<2000;n++) {
      const double t=n*.005;
      auto o=ctl.step(c,Mode::Angle,0,angle,rate,.005);
      if(std::strcmp(o.fault,"NONE")) { ok=false; break; }
      const double differential=(int(o.motor[0])+int(o.motor[2])-int(o.motor[1])-int(o.motor[3]))/4.;
      const double wanted=strength*.00002*differential;
      for(int sub=0;sub<5;sub++) {
        torque+=(wanted-torque)*(.001/lag);
        const double accel=(torque-.00004*rate*3.141592653589793/180)/.0001*180/3.141592653589793;
        rate+=accel*.001; angle+=rate*.001;
      }
      limited+=o.pidLimited;
      if(strength==1 && lag==.06 && initial==3) {
        csv<<t<<','<<angle<<','<<rate<<','<<o.target<<','<<o.p<<','<<o.i<<','<<o.d<<','<<o.raw<<','<<o.u;
        for(auto m:o.motor) csv<<','<<m;
        csv<<','<<o.pidLimited<<','<<o.mixerLimited<<','<<o.fault<<'\n';
      }
    }
    ok &= std::fabs(angle)<1 && std::fabs(rate)<2;
    std::cout<<"synthetic strength="<<strength<<" lag="<<lag<<" initial="<<initial<<" final="<<angle<<" rate="<<rate<<" limited="<<limited/2000<<" "<<(ok?"PASS":"FAIL")<<'\n';
    all &= ok;
  }
  // Evaluate the exact PLAN pulse window instead of only final angle.
  for(double kp:{.4,.8,1.2}) {
    auto rateConfig=c;rateConfig.kp=kp;
    Controller ctl;double angle=0,rate=0,torque=0,error=0;int count=0;bool fault=false;
    for(int n=0;n<800;n++) {
      const double t=n*.005;
      const double target=t>=1 && t<1.3?5:t>=2 && t<2.3?-5:0;
      const auto o=ctl.step(rateConfig,Mode::Rate,target,angle,rate,.005);
      if(std::strcmp(o.fault,"NONE")){fault=true;break;}
      if((t>=1.1 && t<1.3)||(t>=2.1 && t<2.3)){error+=std::fabs(target-rate);count++;}
      const double wanted=.00002*(int(o.motor[0])+int(o.motor[2])-int(o.motor[1])-int(o.motor[3]))/4.;
      for(int sub=0;sub<5;sub++){torque+=(wanted-torque)*(.001/.06);rate+=(torque-.00004*rate*3.141592653589793/180)/.0001*180/3.141592653589793*.001;angle+=rate*.001;}
    }
    std::cout<<"synthetic rate candidate kp="<<kp<<" MAE="<<error/count<<" fault="<<fault<<'\n';
  }
  return all?0:1;
}
