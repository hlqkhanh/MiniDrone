#pragma once
#include <string>
#include <sstream>
#include <stdint.h>
#define F(s) s
#define WIFI_AP 2
class String : public std::string {
 public:
  using std::string::string;
  String(const std::string &s) : std::string(s) {}
  String substring(size_t pos) const { return substr(pos); }
  void replace(const char *from, const char *to) {
    size_t pos=0;
    while ((pos=find(from,pos))!=npos) { std::string::replace(pos,std::string(from).size(),to); pos+=std::string(to).size(); }
  }
};
struct IPAddress {
  int a,b,c,d;
  IPAddress(int a=0,int b=0,int c=0,int d=0):a(a),b(b),c(c),d(d) {}
  bool operator==(const IPAddress &v)const { return a==v.a && b==v.b && c==v.c && d==v.d; }
  friend std::ostream &operator<<(std::ostream &o,const IPAddress &v) { return o<<v.a<<'.'<<v.b<<'.'<<v.c<<'.'<<v.d; }
};
class Print {
 public:
  std::ostringstream output;
  template<class T> void print(const T &v) { output<<v; }
  template<class T> void println(const T &v) { output<<v<<'\n'; }
};
struct WiFiMock {
  bool persistentEnabled=true,autoReconnect=true,sleep=true,modeOk=true,configOk=true,apOk=true;
  int selectedMode=-1,channel=0,maxClients=0;
  IPAddress ip, gateway, subnet;
  std::string ssid,password;
  void persistent(bool enabled) { persistentEnabled=enabled; }
  void setAutoReconnect(bool enabled) { autoReconnect=enabled; }
  void setSleep(bool enabled) { sleep=enabled; }
  bool mode(int value) { selectedMode=value; return modeOk; }
  String softAPmacAddress() { return "AA:BB:CC:DD:12:34"; }
  bool softAPConfig(IPAddress value,IPAddress gw,IPAddress mask) { ip=value; gateway=gw; subnet=mask; return configOk; }
  bool softAP(const char *name,const char *pass,int ch,bool,int clients) {
    ssid=name; password=pass; channel=ch; maxClients=clients; return apOk;
  }
  IPAddress softAPIP() { return ip; }
};
inline WiFiMock WiFi;
