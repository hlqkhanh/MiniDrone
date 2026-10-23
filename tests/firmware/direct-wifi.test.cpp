#define ARDUINO_ARCH_ESP32
#include "../../firmware/DroneMotionMpu6050/DirectWifi.h"
#include <assert.h>
int main() {
  DirectWifi direct;
  Print log;
  assert(direct.begin(log,3333));
  assert(WiFi.selectedMode==WIFI_AP);
  assert(!WiFi.autoReconnect && !WiFi.persistentEnabled && !WiFi.sleep);
  assert(WiFi.ip==IPAddress(192,168,4,1));
  assert(WiFi.gateway==WiFi.ip && WiFi.subnet==IPAddress(255,255,255,0));
  assert(WiFi.ssid=="DroneMotion_Setup-1234" && WiFi.password=="drone12345");
  assert(WiFi.channel==1 && WiFi.maxClients==4);
  assert(log.output.str().find("STATUS,TCP_AP_192.168.4.1:3333")!=std::string::npos);
  WiFi=WiFiMock{}; WiFi.modeOk=false;
  assert(!direct.begin(log,3333) && WiFi.ssid.empty());
  WiFi=WiFiMock{}; WiFi.configOk=false;
  assert(!direct.begin(log,3333) && WiFi.ssid.empty());
  WiFi=WiFiMock{}; WiFi.apOk=false;
  assert(!direct.begin(log,3333));
  assert(log.output.str().find("ERROR,WIFI_DIRECT_AP_FAILED")!=std::string::npos);
}
