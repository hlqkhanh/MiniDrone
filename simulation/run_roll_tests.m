function results = run_roll_tests()
here=fileparts(mfilename('fullpath'));addpath(here);verify_controller();
model='DroneRebuildRoll';load_system(fullfile(here,[model '.slx']));
% Give the signal its OWN timestamps. tout may contain a different set of
% solver hits, and To Workspace's default history limit can truncate data.
set_param([model '/Log'],'SaveFormat','Timeseries','MaxDataPoints','inf', ...
 'Decimation','1','SampleTime','0.005');
[cfg,p]=rebuild_config();
cases={ 'angle_nominal',1,p; 'rate_pulses',0,p; 'weaker_slow',1,p.*[1 1 .5 2 1 1 1 1 1]; 'reversed_motor',1,p.*[1 1 1 1 -1 1 1 1 1]; 'gyro_noise',1,p+[0 0 0 0 0 0 .5 0 0] };
results=table();
for k=1:size(cases,1)
 input=Simulink.SimulationInput(model);
 input=input.setVariable('cfg',cfg,'Workspace',model);
 input=input.setVariable('plant',cases{k,3},'Workspace',model);
 input=input.setVariable('mode',cases{k,2},'Workspace',model);
 out=sim(input);logged=out.get('roll_log');
 assert(isa(logged,'timeseries'),'roll_log must be a timeseries. Check Log block settings.');
 t=double(logged.Time(:));v=double(logged.Data);
 % Row-vector signals can be stored as N-by-17 or 1-by-17-by-N.
 if size(v,1)==numel(t)
  v=reshape(v,numel(t),[]);
 elseif size(v,ndims(v))==numel(t)
  v=reshape(v,[],numel(t)).';
 else
  error('Log shape mismatch: %d times, Data size %s.',numel(t),mat2str(size(v)));
 end
 assert(size(v,2)==17,'Expected 17 signal columns; got %d.',size(v,2));
 assert(numel(t)>=2 && all(diff(t)>0),'Missing or non-increasing signal timestamps.');
 assert(t(end)>=9.99 && t(1)<=.005,'Log does not cover the full 10-second experiment.');
 % Log columns: feedback angle/rate, then controller's 14 outputs, command.
 fault=max(v(:,16));tail=t>=max(t)-5;
 if cases{k,2}==1
  tracking=max(abs(v(tail,1)))<=1;
 else
  active=(t>=1.1 & t<1.3)|(t>=2.1 & t<2.3);
  tracking=mean(abs(v(active,2)-v(active,3)))<=2;
 end
 limited=mean(v(:,14));
 if strcmp(cases{k,1},'reversed_motor')
  passed=fault~=0; % A deliberate wrong physical sign MUST trip protection.
 else
  passed=fault==0 && tracking && limited<=.1;
 end
 results=[results;table(string(cases{k,1}),passed,fault,limited,'VariableNames',{'Case','Pass','FaultCode','FractionPidLimited'})]; %#ok<AGROW>
 if k==1
  figure('Name','SYNTHETIC Roll model - not hardware qualification');
  subplot(3,1,1);plot(t,v(:,1));ylabel('Roll deg');grid on;
  subplot(3,1,2);plot(t,v(:,2),t,v(:,3));ylabel('deg/s');legend('measured','target');grid on;
  subplot(3,1,3);plot(t,v(:,9),t,v(:,10:13));ylabel('duty');xlabel('s');grid on;
 end
end
disp(results);
assert(all(results.Pass),'Some simulations failed. Inspect results; do not auto-export gains.');
end
