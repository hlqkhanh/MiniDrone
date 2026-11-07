function [y,s] = roll_step(s,c,mode,command,angle,rate,dt)
%#codegen
% Numerical port of RollCore.h, NOT the same compiled C++.
% Check this port against the C++ vectors with verify_controller.m.
% state: integral previous previousError filtered limitedFor initialized mixer
% output: target error p i d raw u LF RF LR RR pidLimited mixerLimited fault
y=zeros(1,14);
fault=0;
valid=all(isfinite(c)) && c(1)>=0 && c(1)<=5 && c(2)>=0 && c(2)<=2 && c(3)>=0 && c(3)<=.2 && c(4)>=0 && c(4)<=5 && ...
 c(5)>0 && c(5)<=40 && c(6)>=0 && c(6)<=c(5) && c(7)>0 && c(7)<=45 && c(8)>0 && c(8)<=50 && ...
 c(9)>=1 && c(9)<=180 && abs(c(10))==1 && c(11)>0 && c(11)<=30 && c(12)>0 && c(12)<=300 && c(13)>0 && c(13)<=2 && c(14)>0;
if ~valid || mode<0 || mode>2 || mode~=floor(mode)
 fault=1;
elseif ~all(isfinite([command angle rate]))
 fault=2;
elseif ~isfinite(dt) || dt<.002 || dt>.02
 fault=3;
elseif c(1)<=0 || (mode==1 && c(4)<=0)
 fault=5;
elseif (mode==1 && abs(command)>10) || (mode~=1 && abs(command)>c(7))
 fault=6;
end
if fault~=0
 s=zeros(1,7); y(14)=fault; return
end
if mode==1
 target=max(-c(7),min(c(7),c(4)*(command-angle)));
else
 target=command;
end
e=target-rate; proportional=c(1)*e;
rawD=0;ie=e;
if s(6)~=0
 rawD=-(rate-s(2))/dt;ie=(e+s(3))*.5;
end
alpha=dt/(dt+1/(2*pi*c(8)));
s(4)=s(4)+alpha*(rawD-s(4));d=c(3)*s(4);
candidate=max(-c(6),min(c(6),s(1)+c(2)*ie*dt));
trial=proportional+candidate+d;
if s(7)==0 && (abs(trial)<=c(5) || e*trial<0)
 s(1)=candidate;
end
raw=proportional+s(1)+d;u=max(-c(5),min(c(5),raw));
pidLimited=abs(raw)>=c(5);
r=c(10)*u;mixed=[c(9)+r c(9)-r c(9)+r c(9)-r];
mixerLimited=any(mixed<0 | mixed>220);
motor=floor(max(0,min(220,mixed))+.5);
% Reserved legacy state slot: saturation no longer trips a motor cutoff.
s(5)=0;
s(2)=rate;s(3)=e;s(6)=1;s(7)=double(mixerLimited);
y=[target e proportional s(1) d raw u motor double(pidLimited) double(mixerLimited) 0];
end
