function build_roll_model(implementation)
% Run from this folder in MATLAB Online. Requires MATLAB + Simulink only.
here=fileparts(mfilename('fullpath'));addpath(here);
if nargin==0
 implementation='auto';
end
useCpp=strcmp(implementation,'cpp');
if strcmp(implementation,'auto') || useCpp
 try
  mex('-outdir',here,fullfile(here,'roll_sfun.cpp'));
  useCpp=true;
  disp('Controller: canonical RollCore.h compiled as C++ S-function.');
 catch exception
  if strcmp(implementation,'cpp')
   rethrow(exception);
  end
  warning('C++ MEX unavailable: %s. Using MATLAB port, checked against C++ vectors.',exception.message);
  verify_controller();useCpp=false;
 end
end
model='DroneRebuildRoll';
if bdIsLoaded(model)
 error('Close DroneRebuildRoll first. Save edits under a different name before rebuilding.');
end
if isfile(fullfile(here,[model '.slx']))
 error('Model already exists. Open it, or rename it before generating a fresh copy.');
end
[cfg,plant]=rebuild_config();mode=1;
new_system(model);open_system(model);
set_param(model,'SolverType','Fixed-step','Solver','FixedStepDiscrete','FixedStep','0.005','StopTime','10','ReturnWorkspaceOutputs','on');
workspace=get_param(model,'ModelWorkspace');
assignin(workspace,'cfg',cfg);assignin(workspace,'plant',plant);assignin(workspace,'mode',mode);
add_block('simulink/Sources/Digital Clock',[model '/Time'],'SampleTime','0.005','Position',[30 40 70 70]);
add_block('simulink/Sources/Constant',[model '/Mode'],'Value','mode','Position',[30 110 70 140]);
add_block('simulink/Sources/Constant',[model '/Controller_config'],'Value','cfg','Position',[220 180 290 210]);
add_block('simulink/Sources/Constant',[model '/Synthetic_plant'],'Value','plant','Position',[520 180 590 210]);
addFunction('Reference','reference_block.m',[120 35 230 110]);
if useCpp
 add_block('simulink/User-Defined Functions/S-Function',[model '/Controller'],'FunctionName','roll_sfun','Position',[330 45 470 150]);
else
 verify_controller();
 addFunction('Controller','controller_block.m',[330 45 470 150]);
end
addFunction('Roll_plant','plant_block.m',[620 40 760 140]);
add_block('simulink/Discrete/Unit Delay',[model '/Sensor_delay'],'SampleTime','0.005','InitialCondition','[plant(6) 0]','Position',[620 280 690 320]);
add_block('simulink/Signal Routing/Mux',[model '/Mux'],'Inputs','3','Position',[850 50 855 190]);
add_block('simulink/Sinks/Scope',[model '/Scope'],'Position',[930 40 980 90]);
add_block('simulink/Sinks/To Workspace',[model '/Log'],'VariableName','roll_log','SaveFormat','Timeseries','MaxDataPoints','inf','Decimation','1','SampleTime','0.005','Position',[925 140 1010 180]);
add_line(model,'Time/1','Reference/1','autorouting','on');
add_line(model,'Mode/1','Reference/2','autorouting','on');
add_line(model,'Reference/1','Controller/1','autorouting','on');
add_line(model,'Sensor_delay/1','Controller/2','autorouting','on');
add_line(model,'Controller_config/1','Controller/3','autorouting','on');
add_line(model,'Mode/1','Controller/4','autorouting','on');
add_line(model,'Controller/1','Roll_plant/1','autorouting','on');
add_line(model,'Synthetic_plant/1','Roll_plant/2','autorouting','on');
add_line(model,'Time/1','Roll_plant/3','autorouting','on');
add_line(model,'Mode/1','Roll_plant/4','autorouting','on');
add_line(model,'Roll_plant/1','Sensor_delay/1','autorouting','on');
add_line(model,'Sensor_delay/1','Mux/1','autorouting','on');
add_line(model,'Controller/1','Mux/2','autorouting','on');
add_line(model,'Reference/1','Mux/3','autorouting','on');
add_line(model,'Mux/1','Scope/1','autorouting','on');
add_line(model,'Mux/1','Log/1','autorouting','on');
save_system(model,fullfile(here,[model '.slx']));
disp('Created DroneRebuildRoll.slx. Run run_roll_tests next. Parameters are synthetic.');
 function addFunction(name,file,pos)
  path=[model '/' name];
  add_block('simulink/User-Defined Functions/MATLAB Function',path,'Position',pos);
  chart=find(sfroot,'-isa','Stateflow.EMChart','Path',path);
  chart.Script=fileread(fullfile(here,file));
 end
end
