#pragma once
#include <Arduino.h>
#include <SPI.h>
#include <math.h>
#include "Hardware.h"

struct ImuSample { double ax=0,ay=0,az=0,gx=0,gy=0,gz=0; };
// A fresh, consecutive half-second of stationary samples is required to zero.
class RollLevel {
  unsigned count_=0;
  unsigned at_=0;
  double values_[100]={},sum_=0,offset_=0;
public:
  void resetWindow() { count_=0;at_=0;sum_=0; }
  void reset() { resetWindow();offset_=0; }
  void observe(double raw,double rate,double norm) {
    if(!isfinite(raw)||!isfinite(rate)||!isfinite(norm)||fabs(rate)>1 || norm<.95 || norm>1.05) { resetWindow();return; }
    if(count_==100)sum_-=values_[at_];else count_++;
    values_[at_]=raw;at_=(at_+1)%100;sum_+=raw;
  }
  bool ready()const {
    if(count_<100 || fabs(sum_/count_)>10)return false;
    double low=values_[0],high=low;
    for(double v:values_) { low=fmin(low,v);high=fmax(high,v); }
    return high-low<=.5;
  }
  bool zero() { if(!ready())return false;offset_=sum_/count_;return true; }
  double angle(double raw)const { return raw-offset_; }
  double offset()const { return offset_; }
};
class Imu {
  uint8_t identity_=0;
  void read(uint8_t reg,uint8_t* b,size_t n) {
    SPI.beginTransaction(SPISettings(250000,MSBFIRST,SPI_MODE0));
    digitalWrite(hw::cs,LOW); delayMicroseconds(1); SPI.transfer(reg|0x80);
    for(size_t k=0;k<n;k++) b[k]=SPI.transfer(0);
    delayMicroseconds(1); digitalWrite(hw::cs,HIGH); SPI.endTransaction();
  }
  uint8_t reg(uint8_t r) { uint8_t b; read(r,&b,1); return b; }
  bool write(uint8_t r,uint8_t v,bool verify=true) {
    SPI.beginTransaction(SPISettings(250000,MSBFIRST,SPI_MODE0)); digitalWrite(hw::cs,LOW); delayMicroseconds(1);
    SPI.transfer(r); SPI.transfer(v); delayMicroseconds(1); digitalWrite(hw::cs,HIGH); SPI.endTransaction();
    delayMicroseconds(10); return !verify || reg(r)==v;
  }
public:
  bool begin() {
    pinMode(hw::cs,OUTPUT); digitalWrite(hw::cs,HIGH); SPI.begin(hw::sck,hw::miso,hw::mosi,hw::cs);
    identity_=reg(0x75);
    if(identity_!=0x70 && identity_!=0x71) return false;
    for(int n=0;n<3;n++) if(reg(0x75)!=identity_) return false;
    write(0x6B,0x80,false); delay(100);
    return write(0x6A,0x10) && write(0x6B,1) && write(0x6C,0) && write(0x1A,3) && write(0x1D,3) &&
      write(0x19,4) && write(0x1B,8) && write(0x1C,0) && write(0x38,1);
  }
  uint8_t identity()const { return identity_; }
  // 0=no NEW sample; -1=invalid device/sample; 1=new sample.
  int sample(ImuSample& s) {
    if(reg(0x75)!=identity_) return -1;
    if(!(reg(0x3A)&1)) return 0;
    uint8_t b[14]; read(0x3B,b,14); bool zero=true,ones=true;
    for(auto x:b) { zero &= x==0; ones &= x==255; }
    if(zero||ones) return -1;
    auto number=[&](int i) { return int16_t(uint16_t(b[i])<<8|b[i+1]); };
    s.ax=number(0)/16384.; s.ay=number(2)/16384.; s.az=number(4)/16384.;
    s.gx=number(8)/65.5; s.gy=number(10)/65.5; s.gz=number(12)/65.5; return 1;
  }
};

// Madgwick IMU gradient descent. Inputs gyro in deg/s, acceleration in g.
class Attitude {
  double w=1,x=0,y=0,z=0;
public:
  void initialize(const ImuSample& a) {
    const double r=atan2(a.ay,a.az),p=atan2(-a.ax,sqrt(a.ay*a.ay+a.az*a.az));
    w=cos(r/2)*cos(p/2); x=sin(r/2)*cos(p/2); y=cos(r/2)*sin(p/2); z=-sin(r/2)*sin(p/2);
  }
  bool update(const ImuSample& a,double bx,double by,double bz,double dt) {
    const double k=3.141592653589793/180;
    const double gx=(a.gx-bx)*k,gy=(a.gy-by)*k,gz=(a.gz-bz)*k;
    double dw=.5*(-x*gx-y*gy-z*gz),dx=.5*(w*gx+y*gz-z*gy),dy=.5*(w*gy-x*gz+z*gx),dz=.5*(w*gz+x*gy-y*gx);
    const double an=sqrt(a.ax*a.ax+a.ay*a.ay+a.az*a.az);
    if(!isfinite(an)||an<1e-6||!isfinite(dt)||dt<=0||dt>.02) return false;
    double ax=a.ax/an,ay=a.ay/an,az=a.az/an;
    double sw=4*w*y*y+2*y*ax+4*w*x*x-2*x*ay;
    double sx=4*x*z*z-2*z*ax+4*w*w*x-2*w*ay-4*x+8*x*x*x+8*x*y*y+4*x*az;
    double sy=4*w*w*y+2*w*ax+4*y*z*z-2*z*ay-4*y+8*y*x*x+8*y*y*y+4*y*az;
    double sz=4*x*x*z-2*x*ax+4*y*y*z-2*y*ay;
    const double sn=sqrt(sw*sw+sx*sx+sy*sy+sz*sz);
    const double weight=fmax(0.,fmin(1.,(.20-fabs(an-1))/.15));
    if(sn>1e-10) { const double b=hw::beta*weight/sn; dw-=b*sw; dx-=b*sx; dy-=b*sy; dz-=b*sz; }
    w+=dw*dt; x+=dx*dt; y+=dy*dt; z+=dz*dt;
    const double norm=sqrt(w*w+x*x+y*y+z*z);
    if(!isfinite(norm)||norm<.1) return false;
    w/=norm;x/=norm;y/=norm;z/=norm; return true;
  }
  // Change quaternion basis before extracting body ZYX Euler angles.
  // Sensor quaternion vector (x,y,z) becomes body (-y,x,z).
  double roll()const { return atan2(2*(-w*y+x*z),1-2*(y*y+x*x))*180/3.141592653589793; }
  double pitch()const { return asin(fmax(-1.,fmin(1.,2*(w*x+y*z))))*180/3.141592653589793; }
};
