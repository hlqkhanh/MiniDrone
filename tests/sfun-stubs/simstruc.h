#pragma once
// Minimal host harness, NOT a replacement for MATLAB execution.
using real_T=double;using int_T=int;
struct SimStruct { const double* inputs[4]={};double output[14]={};double discrete[1]={};void* pwork=nullptr; };
constexpr int SS_OPTION_EXCEPTION_FREE_CODE=1;
inline void ssSetNumSFcnParams(SimStruct*,int){}
inline void ssSetNumContStates(SimStruct*,int){}
inline void ssSetNumDiscStates(SimStruct*,int){}
inline bool ssSetNumInputPorts(SimStruct*,int){return true;}
inline bool ssSetNumOutputPorts(SimStruct*,int){return true;}
inline void ssSetInputPortWidth(SimStruct*,int,int){}
inline void ssSetInputPortDirectFeedThrough(SimStruct*,int,int){}
inline void ssSetInputPortRequiredContiguous(SimStruct*,int,int){}
inline void ssSetOutputPortWidth(SimStruct*,int,int){}
inline void ssSetNumSampleTimes(SimStruct*,int){}
inline void ssSetNumPWork(SimStruct*,int){}
inline void ssSetOptions(SimStruct*,int){}
inline void ssSetSampleTime(SimStruct*,int,double){}
inline void ssSetOffsetTime(SimStruct*,int,double){}
inline void ssSetPWorkValue(SimStruct*s,int,void*p){s->pwork=p;}
inline void* ssGetPWorkValue(SimStruct*s,int){return s->pwork;}
inline double* ssGetRealDiscStates(SimStruct*s){return s->discrete;}
inline const void* ssGetInputPortSignal(SimStruct*s,int k){return s->inputs[k];}
inline double* ssGetOutputPortRealSignal(SimStruct*s,int){return s->output;}
