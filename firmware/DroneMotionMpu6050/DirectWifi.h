#pragma once
#if defined(ARDUINO_ARCH_ESP32)
#include <WiFi.h>

// AP only: no saved router credentials, network scanning or web provisioning.
class DirectWifi {
 public:
  bool begin(Print &log, uint16_t port) {
    port_ = port;
    WiFi.persistent(false);
    WiFi.setAutoReconnect(false);
    if (!WiFi.mode(WIFI_AP)) {
      log.println(F("ERROR,WIFI_DIRECT_AP_FAILED"));
      return false;
    }
    WiFi.setSleep(false);
    String suffix = WiFi.softAPmacAddress();
    suffix.replace(":", "");
    ssid_ = "DroneMotion_Setup-" + suffix.substring(suffix.length() - 4);
    const IPAddress ip(192, 168, 4, 1);
    if (!WiFi.softAPConfig(ip, ip, IPAddress(255, 255, 255, 0)) ||
        !WiFi.softAP(ssid_.c_str(), "drone12345", 1, false, 4)) {
      log.println(F("ERROR,WIFI_DIRECT_AP_FAILED"));
      return false;
    }
    report(log);
    return true;
  }
  void report(Print &log) {
    log.println(F("STATUS,WIFI_DIRECT_ONLY"));
    log.print(F("STATUS,WIFI_AP_")); log.println(ssid_);
    log.print(F("STATUS,TCP_AP_")); log.print(WiFi.softAPIP());
    log.print(':'); log.println(port_);
  }
 private:
  String ssid_;
  uint16_t port_ = 3333;
};
#endif
