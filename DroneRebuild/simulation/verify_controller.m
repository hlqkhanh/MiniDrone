function verify_controller()
% C++ output compared to MATLAB port, including faults/reset, 3600 steps.
here=fileparts(mfilename('fullpath'));addpath(here);
T=readtable(fullfile(here,'controller_vectors.csv'),'TextType','string');
% Fixture config is fixed, independent of user tuning in rebuild_config.m.
c=[0.8 0.05 0.005 2 20 10 20 10 80 1 15 150 0.5 5];
state=zeros(1,7);previous=-1;worst=0;
names=["NONE" "CONFIG" "SENSOR" "TIMING" "ENVELOPE" "GAINS_ZERO" "TARGET" "SATURATION_TRACKING"];
for n=1:height(T)
 if T.scenario(n)~=previous
  state=zeros(1,7);previous=T.scenario(n);
 end
 [y,state]=roll_step(state,c,T.mode(n),T.command(n),T.angle(n),T.rate(n),T.dt(n));
 expected=[T.target(n) T.error(n) T.p(n) T.i(n) T.d(n) T.raw(n) T.u(n) T.LF(n) T.RF(n) T.LR(n) T.RR(n) T.pidLimited(n) T.mixerLimited(n)];
 delta=max(abs(y(1:13)-expected));worst=max(worst,delta);
 assert(delta<1e-8,'Controller differs at fixture row %d by %g',n,delta);
 assert(names(y(14)+1)==T.fault(n),'Fault differs at row %d',n);
end
fprintf('PASS: %d C++/MATLAB vectors, max error %.3g\n',height(T),worst);
end
