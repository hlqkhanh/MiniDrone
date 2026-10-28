#include <Arduino.h>
#include <WiFi.h>
#include <esp_timer.h>
#include <sys/socket.h>
#include <atomic>
#include "RollCore.h"
#include "Hardware.h"
#include "Imu.h"
#include "BuildInfo.h"
#include "NonBlockingTx.h"
#if __has_include("WifiSecrets.h")
#include "WifiSecrets.h"
#else
#define REBUILD_STA_SSID ""
#define REBUILD_STA_PASSWORD ""
#endif
using namespace rebuild;
// PWM has ONE writer: guard callback. Control publishes proposals only.
struct Gate { bool stopped=true,enabled=false; uint32_t epoch=0; uint64_t deadline=0,hb=0,sample=0; unsigned duty[4]={}; const char* reason="BOOT"; } gate;
portMUX_TYPE gateMux=portMUX_INITIALIZER_UNLOCKED;
QueueHandle_t commands, replies, samples;
struct Command { uint32_t owner,id; char text[180]; };
struct Line { uint32_t owner=0; char text[850]; };
struct Input { char data[200];unsigned n=0;bool overflow=false; } usb,tcp;
std::atomic<uint32_t> droppedTelemetry{0},droppedReplies{0};
std::atomic<bool> pwmReady{false};
WiFiServer server(3333); WiFiClient client;
uint32_t clientId=100;
bool hadTcpClient=false;
esp_timer_handle_t guardTimer;
const char* stateName(State s) { switch(s) { case State::Armed:return "ARMED";case State::Running:return "RUNNING";case State::Fault:return "FAULT";default:return "DISARMED"; } }
Gate snapshot() { portENTER_CRITICAL(&gateMux); Gate g=gate; portEXIT_CRITICAL(&gateMux); return g; }
void emergency(const char* why) {
  portENTER_CRITICAL(&gateMux); gate.stopped=true;gate.enabled=false;gate.epoch++;gate.reason=why; portEXIT_CRITICAL(&gateMux);
}
void guard(void*) {
  Gate g=snapshot(); const uint64_t now=esp_timer_get_time();
  if(!g.stopped && g.enabled && ((g.deadline && now>=g.deadline) || now-g.sample>=100000 || now-g.hb>=2000000)) {
    emergency(g.deadline && now>=g.deadline?"DURATION":now-g.sample>=100000?"STALE_CONTROL":"HEARTBEAT"); g.enabled=false;
  }
  bool ok=true;
  for(int k=0;k<4;k++) ok=ledcWrite(hw::motors[k],g.enabled && !g.stopped?g.duty[k]:0) && ok;
  if(!ok) { pwmReady=false; emergency("PWM"); }
}
void reply(uint32_t id,bool ok,const char* message,uint32_t owner=0) {
  Line l;l.owner=owner; snprintf(l.text,sizeof(l.text),"ACK,%lu,%s,%lu,%s",(unsigned long)id,ok?"OK":"ERROR",(unsigned long)snapshot().epoch,message);
  if(xQueueSend(replies,&l,0)!=pdTRUE) { droppedReplies++; emergency("REPLY_OVERFLOW"); }
}
void controlTask(void*) {
  Imu imu; Attitude attitude; RollLevel levels[2]; Controller controller; Config configs[2]; Session session;
  unsigned axis=0;
  bool available=imu.begin(),calibrated=false,verified=false,props=false;
  int count=0; double sum[3]={},lo[6]={},hi[6]={},bias[3]={};
  uint64_t calibrationStart=esp_timer_get_time(),lastSample=0;
  uint32_t seq=0,missed=0,late=0; unsigned telem=50;
  ImuSample a; double angles[2]={},rates[2]={},dt=.005; Output output;
  Mode mode=Mode::Rate; double target=0; bool bench=false; unsigned benchMotor=0,benchDuty=0;
  TickType_t wake=xTaskGetTickCount();
  auto publish=[&](bool enable) {
    portENTER_CRITICAL(&gateMux);
    if(gate.epoch==session.epoch && !gate.stopped) {
      gate.enabled=enable;gate.deadline=session.deadline;gate.hb=session.heartbeatAt;gate.sample=session.sampleAt;
      for(int k=0;k<4;k++) gate.duty[k]=bench?(k==int(benchMotor)?benchDuty:0):output.motor[k];
    }
    portEXIT_CRITICAL(&gateMux);
  };
  for(;;) {
    const uint64_t now=esp_timer_get_time();
    Gate g=snapshot();
    if(g.epoch!=session.epoch) { session.stop(g.reason);session.epoch=g.epoch;controller.reset();output=Output{};bench=false; }
    if(!session.check(now)) { emergency(session.reason); session.epoch=snapshot().epoch;output=Output{};controller.reset(); }
    Command cmd;
    for(int budget=0;budget<4 && xQueueReceive(commands,&cmd,0)==pdTRUE;budget++) {
      Config& cfg=configs[axis];
      bool ok=false; const char* response="INVALID"; unsigned epoch,duration,motor,duty,hz; int which; double value; char extra;
      if(!strcmp(cmd.text,"INFO")) {
        Line l;l.owner=cmd.owner; snprintf(l.text,sizeof(l.text),"INFO,%s,%s,%s %s,WHO_%02X,source_%s,AP_%s,STA_%s,beta_%.3f,gyroBias_%.6f_%.6f_%.6f,pwm_%d",hw::version,hw::profile,__DATE__,__TIME__,imu.identity(),rebuildSourceId,WiFi.softAPIP().toString().c_str(),WiFi.localIP().toString().c_str(),hw::beta,bias[0],bias[1],bias[2],pwmReady.load());
        if(xQueueSend(replies,&l,0)!=pdTRUE) { droppedReplies++; emergency("REPLY_OVERFLOW"); }
        Line levelInfo;levelInfo.owner=cmd.owner;
        snprintf(levelInfo.text,sizeof(levelInfo.text),"LEVEL,axis_%u,rollRaw_%.6f,rollOffset_%.6f,roll_%.6f,pitchRaw_%.6f,pitchOffset_%.6f,pitch_%.6f,ready_%d",axis,attitude.roll(),levels[0].offset(),angles[0],attitude.pitch(),levels[1].offset(),angles[1],levels[axis].ready());
        if(xQueueSend(replies,&levelInfo,0)!=pdTRUE) { droppedReplies++;emergency("REPLY_OVERFLOW"); }
        ok=true;response="INFO";
      } else if(!strcmp(cmd.text,"STOP")) { emergency("STOP");ok=true;response="STOP"; }
      else if(!strcmp(cmd.text,"KEEPALIVE")) { ok=cmd.owner==session.owner && session.state==State::Running; if(ok) session.heartbeat(cmd.owner,now);response="KEEPALIVE"; }
      else if(sscanf(cmd.text,"ARM %u %c",&epoch,&extra)==1) {
        ok=pwmReady && session.arm(cmd.owner,epoch,now);
        if(ok) { controller.reset();portENTER_CRITICAL(&gateMux); if(gate.epoch==epoch) { gate.stopped=false;gate.enabled=false;gate.reason="ARMED"; } else ok=false;portEXIT_CRITICAL(&gateMux); }
        response="ARM";
      } else if(sscanf(cmd.text,"START %u %d %lf %u %c",&epoch,&which,&value,&duration,&extra)==4) {
        ok=calibrated && verified && cfg.valid() && cfg.kp>0 && which>=0 && which<=2 && std::isfinite(value) &&
          std::fabs(value)<=(which==1?10:cfg.rateLimit) && (which!=1 || cfg.angleKp>0) &&
          // Keep the legacy duration argument on the wire; START is continuous.
          session.start(cmd.owner,epoch,now,0);
        if(ok) { mode=Mode(which);target=value;bench=false;controller.reset();portENTER_CRITICAL(&gateMux);gate.reason="RUNNING";portEXIT_CRITICAL(&gateMux); } response="START";
      } else if(sscanf(cmd.text,"BENCH %u %u %u %u %c",&epoch,&motor,&duty,&duration,&extra)==4) {
        ok=props && motor<4 && duty<=89 && duration>=1 && duration<=2000 && session.start(cmd.owner,epoch,now,duration);
        if(ok) { bench=true;benchMotor=motor;benchDuty=duty;target=0;output=Output{};portENTER_CRITICAL(&gateMux);gate.reason="BENCH";portEXIT_CRITICAL(&gateMux); } response="BENCH";
      } else if(session.state==State::Disarmed || session.state==State::Fault) {
        if(!strcmp(cmd.text,"CALIBRATE")) {
          for(auto& level:levels)level.reset();available=imu.begin();calibrated=false;count=0;for(auto &v:sum)v=0;calibrationStart=now;lastSample=0;verified=false;ok=available;response="CALIBRATE";
        } else if(!strcmp(cmd.text,"ZERO_ROLL")) {
          ok=calibrated && lastSample && now-lastSample<20000 && g.stopped && !g.enabled && levels[0].zero();
          if(ok) { angles[0]=levels[0].angle(attitude.roll());verified=false;controller.reset();output=Output{}; }
          response=ok?"ZERO_ROLL":"ZERO_REQUIRES_STILL_CALIBRATED_LEVEL_WITHIN_10_DEG";
        } else if(!strcmp(cmd.text,"ZERO_PITCH")) {
          ok=calibrated && lastSample && now-lastSample<20000 && g.stopped && !g.enabled && levels[1].zero();
          if(ok) { angles[1]=levels[1].angle(attitude.pitch());verified=false;controller.reset();output=Output{}; }
          response=ok?"ZERO_PITCH":"ZERO_REQUIRES_STILL_CALIBRATED_LEVEL_WITHIN_10_DEG";
        } else if(sscanf(cmd.text,"AXIS %d %c",&which,&extra)==1 && which>=0 && which<=1) {
          axis=which;verified=false;controller.reset();output=Output{};target=0;ok=true;response="AXIS";
        } else if(sscanf(cmd.text,"VERIFY %d %c",&which,&extra)==1 && (which==0 || which==1)) { verified=which;ok=true;response="VERIFY"; }
        else if(sscanf(cmd.text,"PROPS_REMOVED %d %c",&which,&extra)==1 && (which==0 || which==1)) { props=which;ok=true;response="PROPS_REMOVED"; }
        else if(sscanf(cmd.text,"TELEMETRY %u %c",&hz,&extra)==1 && (hz==50 || hz==100)) { telem=hz;ok=true;response="TELEMETRY"; }
        else {
          Config next=cfg;
          if(sscanf(cmd.text,"CONFIG %lf %lf %lf %lf %lf %lf %lf %c",&next.kp,&next.ki,&next.kd,&next.angleKp,&next.base,&next.motorSign,&next.outputLimit,&extra)==7 && next.valid()) { if(next.motorSign!=cfg.motorSign)verified=false;cfg=next;controller.reset();ok=true;response="CONFIG"; }
        }
      } else response="STOP_FIRST";
      if(!ok && session.state==State::Armed && (!strncmp(cmd.text,"START",5)||!strncmp(cmd.text,"BENCH",5))) emergency("START_REJECTED");
      reply(cmd.id,ok,response,cmd.owner);
    }
    const int got=available?imu.sample(a):-1;
    if(got<0) { if(session.state==State::Running) emergency("IMU"); available=false;calibrated=false;for(auto& level:levels)level.resetWindow(); }
    if(got==0) missed++;
    if(got==1) {
      dt=lastSample?double(now-lastSample)*1e-6:.005;lastSample=now;
      if(dt>.0075) late++;
      if(!calibrated && now-calibrationStart<30000000) {
        const double norm=sqrt(a.ax*a.ax+a.ay*a.ay+a.az*a.az);
        double v[]={a.ax,a.ay,a.az,a.gx,a.gy,a.gz};
        bool stable=norm>.85 && norm<1.15 && fabs(a.gx)<15 && fabs(a.gy)<15 && fabs(a.gz)<15;
        if(count) for(int k=0;k<6;k++) if(fmax(hi[k],v[k])-fmin(lo[k],v[k])>(k<3?.08:2.)) stable=false;
        if(!stable) { count=0;for(auto& x:sum)x=0; }
        else { for(int k=0;k<6;k++) { if(!count) lo[k]=hi[k]=v[k];lo[k]=fmin(lo[k],v[k]);hi[k]=fmax(hi[k],v[k]); } for(int k=0;k<3;k++) sum[k]+=v[k+3];count++; }
        if(count>=500) { for(int k=0;k<3;k++)bias[k]=sum[k]/count;attitude.initialize(a);calibrated=true; }
      }
      if(calibrated) {
        if(!attitude.update(a,bias[0],bias[1],bias[2],dt)) { calibrated=false;emergency("ESTIMATOR_TIMING"); }
        rates[0]=hw::rollSensorSign*(a.gy-bias[1]);rates[1]=a.gx-bias[0];
        const double raw[]={attitude.roll(),attitude.pitch()};
        const double norm=sqrt(a.ax*a.ax+a.ay*a.ay+a.az*a.az);
        const bool still=fabs(a.gx-bias[0])<=1 && fabs(a.gy-bias[1])<=1 && fabs(a.gz-bias[2])<=1;
        for(unsigned k=0;k<2;k++) {
          if(calibrated && dt>=.002 && dt<=.02 && still)levels[k].observe(raw[k],rates[k],norm);else levels[k].resetWindow();
          angles[k]=levels[k].angle(raw[k]);
        }
      }
      if(session.state==State::Running) {
        if(bench) { session.sampleAt=now;publish(true); }
        else if(calibrated) {
          output=controller.step(configs[axis],mode,target,angles[axis],rates[axis],dt,Axis(axis));
          if(strcmp(output.fault,"NONE")) emergency(output.fault);
          else { session.sampleAt=now;publish(mode!=Mode::DryRun); }
        } else emergency("NOT_CALIBRATED");
      }
      seq++;
      if(seq%(200/telem)==0) {
        const Config& cfg=configs[axis];
        Line l;const auto gateNow=snapshot();const bool running=session.state==State::Running && !gateNow.stopped;
        snprintf(l.text,sizeof(l.text),"T,%lu,%llu,%s,%lu,%d,%u,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%.6f,%u,%u,%u,%u,%d,%d,%.6f,%lu,%lu,%lu,%lu,%s,%d,%d,%d,%.6f,%.6f,%.6f,%.6f,%.6f,%.0f,%.6f,%.6f",
          (unsigned long)seq,(unsigned long long)now,running?"RUNNING":gateNow.stopped?"DISARMED":stateName(session.state),(unsigned long)gateNow.epoch,bench?3:int(mode),count,
          a.ax,a.ay,a.az,a.gx,a.gy,a.gz,angles[axis],rates[axis],output.target,output.error,output.p,output.i,output.d,output.raw,output.u,
          running && (bench || mode!=Mode::DryRun)?gateNow.duty[0]:0,running && (bench || mode!=Mode::DryRun)?gateNow.duty[1]:0,running && (bench || mode!=Mode::DryRun)?gateNow.duty[2]:0,running && (bench || mode!=Mode::DryRun)?gateNow.duty[3]:0,
          output.pidLimited,output.mixerLimited,dt,(unsigned long)missed,(unsigned long)late,(unsigned long)droppedTelemetry,(unsigned long)droppedReplies,
          gateNow.reason,calibrated,verified,props,cfg.kp,cfg.ki,cfg.kd,cfg.angleKp,cfg.base,cfg.motorSign,cfg.outputLimit,target);
        const size_t used=strlen(l.text);
        snprintf(l.text+used,sizeof(l.text)-used,",%u,%.6f,%.6f,%.6f,%.6f",axis,angles[0],rates[0],angles[1],rates[1]);
        if(xQueueSend(samples,&l,0)!=pdTRUE) droppedTelemetry++;
      }
    }
    // Publish heartbeat without refreshing sample freshness.
    portENTER_CRITICAL(&gateMux);if(gate.epoch==session.epoch && !gate.stopped) gate.hb=session.heartbeatAt;portEXIT_CRITICAL(&gateMux);
    static uint64_t healthAt=0;
    if(now-healthAt>=500000) {
      healthAt=now;Line health;const auto status=snapshot();
      snprintf(health.text,sizeof(health.text),"HEALTH,%lu,%d,%d,%d,%d,%s",(unsigned long)status.epoch,available,calibrated,count,pwmReady.load(),status.reason);
      if(xQueueSend(replies,&health,0)!=pdTRUE)droppedReplies++;
    }
    vTaskDelayUntil(&wake,pdMS_TO_TICKS(5));
  }
}
void receive(Stream& s,Input& b,uint32_t owner) {
  for(int budget=0;budget<256 && s.available();budget++) {
    char c=s.read();
    if(c=='\n') {
      if(!b.overflow && b.n) {
        b.data[b.n]=0; Command cmd{};
        if(sscanf(b.data,"%lu %179[^\r\n]",(unsigned long*)&cmd.id,cmd.text)==2) {
          cmd.owner=owner;
          if(!strcmp(cmd.text,"STOP")) emergency("STOP");
          if(xQueueSend(commands,&cmd,0)!=pdTRUE) { emergency("COMMAND_OVERFLOW");reply(cmd.id,false,"QUEUE_FULL",owner); }
        } else { reply(0,false,"FORMAT_EXPECT_ID_SPACE_COMMAND",owner); }
      } else if(b.overflow) { emergency("LINE_OVERFLOW");reply(0,false,"LINE_TOO_LONG",owner); }
      b.n=0;b.overflow=false;
    } else if(c!='\r') { if(b.n<sizeof(b.data)-1 && !b.overflow)b.data[b.n++]=c;else b.overflow=true; }
  }
}
Line usbPending{},tcpPending{};size_t usbAt=0,tcpAt=0,usbSize=0,tcpSize=0;
bool enqueueOutput(const Line& line) {
  if(usbSize || tcpSize) return false;
  usbPending=tcpPending=line;size_t length=strlen(line.text);usbPending.text[length++]='\n';usbAt=0;
  tcpPending=usbPending;usbSize=line.owner==0 || line.owner==1?length:0;
  tcpSize=client.connected() && (line.owner==0 || line.owner==clientId)?length:0;tcpAt=0;return true;
}
void setup() {
  for(int pin:hw::motors){digitalWrite(pin,LOW);pinMode(pin,OUTPUT);}
  Serial.setTxBufferSize(2048);Serial.begin(hw::baud);
  pwmReady=true;for(int pin:hw::motors) { pwmReady=ledcAttach(pin,20000,8)&&pwmReady;ledcWrite(pin,0); }
  commands=xQueueCreate(16,sizeof(Command));replies=xQueueCreate(24,sizeof(Line));samples=xQueueCreate(4,sizeof(Line));
  if(!commands||!replies||!samples){pwmReady=false;return;}
  WiFi.mode(WIFI_AP_STA);WiFi.softAP("DroneRebuild","rebuild123");
  if(strlen(REBUILD_STA_SSID))WiFi.begin(REBUILD_STA_SSID,REBUILD_STA_PASSWORD);
  server.begin();server.setNoDelay(true);
  esp_timer_create_args_t args={};args.callback=guard;args.name="roll_output_guard";args.dispatch_method=ESP_TIMER_TASK;
  if(esp_timer_create(&args,&guardTimer)!=ESP_OK||esp_timer_start_periodic(guardTimer,5000)!=ESP_OK){pwmReady=false;return;}
  if(xTaskCreatePinnedToCore(controlTask,"roll200Hz",10000,nullptr,4,nullptr,1)!=pdPASS) { pwmReady=false;emergency("TASK_CREATE"); }
}
void loop() {
  if(!commands||!replies||!samples)return;
  if(hadTcpClient && !client.connected()) { emergency("TCP_DISCONNECT");client.stop();hadTcpClient=false;tcp=Input{};tcpSize=0; }
  if(server.hasClient()) {
    auto newcomer=server.accept();
    if(!client.connected() && !snapshot().enabled) { client=newcomer;hadTcpClient=true;clientId++;tcp=Input{}; }
    else newcomer.stop();
  }
  receive(Serial,usb,1);if(client.connected())receive(client,tcp,clientId);
  // USB and TCP have independent cursors. A slow subscriber is dropped rather
  // than blocking the control task. Telemetry queues are bounded.
  if(usbSize) { int room=Serial.availableForWrite();if(room>0)usbAt+=Serial.write((uint8_t*)usbPending.text+usbAt,min(size_t(room),usbSize-usbAt));if(usbAt==usbSize)usbSize=0; }
  if(tcpSize) {
    if(!client.connected())tcpSize=0;
    else {
      const int sent=pumpTcp(tcpPending.text,tcpSize,tcpAt,[](const char* bytes,size_t count) {
        return ::send(client.fd(),bytes,count,MSG_DONTWAIT);
      });
      if(sent>0)tcpSize=0;
      else if(sent<0) { client.stop();tcpSize=0;emergency("TCP_SEND_ERROR"); }
    }
  }
  static uint32_t blockedAt=0;
  if(usbSize||tcpSize) { if(!blockedAt)blockedAt=millis();if(millis()-blockedAt>100){ if(tcpSize){client.stop();tcpSize=0;emergency("TCP_BACKPRESSURE");} if(usbSize){usbSize=0;droppedTelemetry++;}blockedAt=0;} }
  else { blockedAt=0;Line l;if(xQueueReceive(replies,&l,0)==pdTRUE || xQueueReceive(samples,&l,0)==pdTRUE)enqueueOutput(l); }
  delay(1);
}
