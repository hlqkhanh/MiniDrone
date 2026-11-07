#define S_FUNCTION_NAME roll_sfun
#define S_FUNCTION_LEVEL 2
#include "simstruc.h"
#include "../firmware/DroneRebuild/RollCore.h"
#include <cstring>
struct Instance { rebuild::Controller controller; int fault=0; };
static int faultNumber(const char* name) {
  const char* names[]={"NONE","CONFIG","SENSOR","TIMING","ENVELOPE","GAINS_ZERO","TARGET","SATURATION_TRACKING"};
  for(int k=0;k<8;k++)if(!std::strcmp(name,names[k]))return k;
  return 1;
}
static rebuild::Config config(SimStruct* S) {
  const auto* v=static_cast<const real_T*>(ssGetInputPortSignal(S,2));
  rebuild::Config c;
  c.kp=v[0];c.ki=v[1];c.kd=v[2];c.angleKp=v[3];c.outputLimit=v[4];c.iLimit=v[5];c.rateLimit=v[6];c.dHz=v[7];c.base=v[8];c.motorSign=v[9];c.maxAngle=v[10];c.maxRate=v[11];c.saturationSeconds=v[12];c.saturationError=v[13];return c;
}
static rebuild::Output calculate(SimStruct* S,rebuild::Controller& c) {
  const auto* command=static_cast<const real_T*>(ssGetInputPortSignal(S,0));
  const auto* feedback=static_cast<const real_T*>(ssGetInputPortSignal(S,1));
  const auto* mode=static_cast<const real_T*>(ssGetInputPortSignal(S,3));
  return c.step(config(S),rebuild::Mode(int(mode[0])),command[0],feedback[0],feedback[1],.005);
}
static void mdlInitializeSizes(SimStruct* S) {
  ssSetNumSFcnParams(S,0);ssSetNumContStates(S,0);ssSetNumDiscStates(S,1);
  if(!ssSetNumInputPorts(S,4))return;
  const int widths[]={1,2,14,1};
  for(int k=0;k<4;k++){ssSetInputPortWidth(S,k,widths[k]);ssSetInputPortDirectFeedThrough(S,k,1);ssSetInputPortRequiredContiguous(S,k,1);}
  if(!ssSetNumOutputPorts(S,1))return;
  ssSetOutputPortWidth(S,0,14);ssSetNumSampleTimes(S,1);ssSetNumPWork(S,1);
  ssSetOptions(S,SS_OPTION_EXCEPTION_FREE_CODE);
}
static void mdlInitializeSampleTimes(SimStruct* S){ssSetSampleTime(S,0,.005);ssSetOffsetTime(S,0,0);}
#define MDL_START
static void mdlStart(SimStruct* S){ssSetPWorkValue(S,0,new Instance{});ssGetRealDiscStates(S)[0]=0;}
static void mdlOutputs(SimStruct* S,int_T) {
  auto* instance=static_cast<Instance*>(ssGetPWorkValue(S,0));auto* y=ssGetOutputPortRealSignal(S,0);
  for(int k=0;k<14;k++)y[k]=0;
  if(instance->fault){y[13]=instance->fault;return;}
  auto preview=instance->controller;const auto o=calculate(S,preview);
  y[0]=o.target;y[1]=o.error;y[2]=o.p;y[3]=o.i;y[4]=o.d;y[5]=o.raw;y[6]=o.u;
  for(int k=0;k<4;k++)y[7+k]=o.motor[k];
  y[11]=o.pidLimited;y[12]=o.mixerLimited;y[13]=faultNumber(o.fault);
}
#define MDL_UPDATE
static void mdlUpdate(SimStruct* S,int_T) {
  auto* instance=static_cast<Instance*>(ssGetPWorkValue(S,0));
  if(!instance->fault)instance->fault=faultNumber(calculate(S,instance->controller).fault);
  ssGetRealDiscStates(S)[0]+=1;
}
static void mdlTerminate(SimStruct* S){delete static_cast<Instance*>(ssGetPWorkValue(S,0));ssSetPWorkValue(S,0,nullptr);}
#ifdef MATLAB_MEX_FILE
#include "simulink.c"
#else
#include "cg_sfun.h"
#endif
