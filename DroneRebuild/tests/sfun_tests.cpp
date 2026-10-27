#include "../simulation/roll_sfun.cpp"
#include <cassert>
#include <iostream>
int main() {
 SimStruct s;double command=5,feedback[2]={0,0},mode=0;
 double cfg[]={.8,.05,.005,2,20,10,20,10,80,1,15,150,.5,5};
 s.inputs[0]=&command;s.inputs[1]=feedback;s.inputs[2]=cfg;s.inputs[3]=&mode;
 mdlInitializeSizes(&s);mdlInitializeSampleTimes(&s);mdlStart(&s);
 mdlOutputs(&s,0);double first=s.output[3];
 mdlOutputs(&s,0);assert(s.output[3]==first); // Repeated output evaluation must not integrate again.
 assert(s.output[7]>s.output[8]);
 mdlUpdate(&s,0);mdlOutputs(&s,0);assert(s.output[3]>first);
 feedback[0]=16;mdlOutputs(&s,0);assert(s.output[13]==4);
 for(int k=7;k<11;k++)assert(s.output[k]==0);
 mdlUpdate(&s,0);feedback[0]=0;mdlOutputs(&s,0);assert(s.output[13]==4); // Fault latched until restart.
 mdlTerminate(&s);assert(s.pwork==nullptr);
 std::cout<<"PASS C++ S-function wrapper idempotence, update, fault latch, motor zero\n";
}
