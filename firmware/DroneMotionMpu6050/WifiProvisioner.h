#pragma once
#if defined(ARDUINO_ARCH_ESP32)
#include <WiFi.h>
#include <WebServer.h>
#include <DNSServer.h>
#include <Preferences.h>
#include <esp_system.h>
#include <ctype.h>
#include "WifiPortalPage.h"

class WifiProvisioner {
 public:
  void begin(Print &log, uint16_t port) {
    log_ = &log;
    port_ = port;
    token_ = String(esp_random(), HEX) + String(esp_random(), HEX);
    WiFi.persistent(false);
    WiFi.mode(WIFI_AP_STA);
    WiFi.setAutoReconnect(false);
    WiFi.setSleep(false);
    String suffix = WiFi.macAddress(); suffix.replace(":", "");
    setupSsid_ = "DroneMotion_Setup-" + suffix.substring(suffix.length() - 4);
    const IPAddress ip(192, 168, 4, 1);
    if (!WiFi.softAPConfig(ip, ip, IPAddress(255, 255, 255, 0)) ||
        !WiFi.softAP(setupSsid_.c_str(), "drone12345", 1, false, 4)) {
      log_->println(F("ERROR,WIFI_SETUP_AP_FAILED"));
    }
    // Wildcard DNS plus an HTTP redirect lets phones detect the setup portal.
    dns_.setTTL(60);
    if (!dns_.start(53, "*", WiFi.softAPIP())) {
      log_->println(F("ERROR,WIFI_SETUP_DNS_FAILED"));
    }
    storageReady_ = prefs_.begin("drone-wifi", false);
    const char *headers[] = {"X-Setup-Token"};
    web_.collectHeaders(headers, 1);
    web_.on("/", HTTP_GET, [this]() {
      if (!fromSetup()) return;
      String page = FPSTR(WifiPortalPage);
      page.replace("__SETUP_TOKEN__", token_);
      web_.sendHeader("Cache-Control", "no-store");
      web_.send(200, "text/html; charset=utf-8", page);
    });
    web_.on("/api/status", HTTP_GET, [this]() { if (fromSetup()) sendStatus(); });
    web_.on("/api/scan", HTTP_GET, [this]() { if (fromSetup()) scan(); });
    web_.on("/api/connect", HTTP_POST, [this]() { if (authorized()) connectRequest(); });
    web_.on("/api/forget", HTTP_POST, [this]() {
      if (!authorized()) return;
      if (!storageReady_ || (prefs_.isKey("config") && !prefs_.remove("config"))) {
        error(500, "Không xóa được cấu hình. Hãy thử lại."); return;
      }
      pending_ = disconnecting_ = connecting_ = savePending_ = activeSaved_ = wasConnected_ = retrySaved_ = false;
      WiFi.setAutoReconnect(false);
      WiFi.disconnect(false, false);
      memset(&credentials_, 0, sizeof(credentials_));
      message_ = "Đã xóa mạng đã lưu. Chọn mạng mới để kết nối.";
      json(200, "{\"ok\":true}");
    });
    web_.onNotFound([this]() {
      if (!fromSetup()) return;
      // Use an absolute URL: connectivity probes arrive with external Host names.
      web_.sendHeader("Cache-Control", "no-store");
      web_.sendHeader("Location", String("http://") + WiFi.softAPIP().toString() + "/");
      web_.send(302, "text/plain", "Open the Wi-Fi setup page.");
    });
    web_.begin();
    report();
    if (storageReady_ && prefs_.getBytesLength("config") == sizeof(credentials_) &&
        prefs_.getBytes("config", &credentials_, sizeof(credentials_)) == sizeof(credentials_) && validStored()) {
      activeSaved_ = true;
      queueConnection();
    } else {
      memset(&credentials_, 0, sizeof(credentials_));
      message_ = "Chọn Wi-Fi và nhập mật khẩu để bắt đầu.";
    }
  }

  void poll() {
    dns_.processNextRequest();
    web_.handleClient();
    if (retrySaved_ && static_cast<int32_t>(millis() - retryAt_) >= 0) queueConnection();
    if (pending_) {
      if (static_cast<int32_t>(millis() - startAt_) < 0) return;
      WiFi.setAutoReconnect(false);
      WiFi.disconnect(false, false);
      pending_ = false; disconnecting_ = true; startAt_ = millis() + 200;
      return;
    }
    if (disconnecting_) {
      if (static_cast<int32_t>(millis() - startAt_) < 0) return;
      disconnecting_ = false;
      startConnection();
      return;
    }
    if (connected()) {
      if (!wasConnected_) {
        wasConnected_ = true;
        connecting_ = false;
        WiFi.setAutoReconnect(true);
        message_ = "Đã kết nối. Dùng IP bên dưới trong app 3D.";
      }
      if (savePending_) {
        savePending_ = false;
        activeSaved_ = storageReady_ && prefs_.putBytes("config", &credentials_, sizeof(credentials_)) == sizeof(credentials_);
        if (!activeSaved_) message_ = "Đã kết nối nhưng chưa lưu được cấu hình. Hãy thử lưu lại trước khi tắt nguồn.";
      }
      return;
    }
    if (wasConnected_) {
      wasConnected_ = false;
      connecting_ = true; attemptAt_ = millis();
      message_ = "Mất mạng, ESP32 đang kết nối lại.";
    }
    if (connecting_ && millis() - attemptAt_ >= 30000) {
      connecting_ = false;
      message_ = "Không kết nối được. Kiểm tra tên mạng, mật khẩu, sóng 2,4 GHz và loại bảo mật.";
      log_->println(F("ERROR,WIFI_CONNECT_TIMEOUT_CHECK_PORTAL"));
      if (savePending_) {
        savePending_ = false;
        WiFi.setAutoReconnect(false);
        WiFi.disconnect(false, false);
      } else if (activeSaved_) { retrySaved_ = true; retryAt_ = millis() + 30000; }
    }
  }

  void report() {
    log_->print(F("STATUS,WIFI_SETUP_")); log_->println(setupSsid_);
    log_->print(F("STATUS,SETUP_URL_http://")); log_->println(WiFi.softAPIP());
    log_->print(F("STATUS,TCP_AP_")); log_->print(WiFi.softAPIP());
    log_->print(':'); log_->println(port_);
    if (connected()) {
      log_->print(F("STATUS,TCP_")); log_->print(WiFi.localIP());
      log_->print(':'); log_->println(port_);
    }
  }

  bool stationReady() { return connected(); }

 private:
  struct Credentials {
    uint32_t magic;
    uint8_t mode; // 0=open, 1=personal, 2=PEAP
    char ssid[33];
    char password[129];
    char username[129];
    char identity[129];
    char ca[4097];
  } credentials_{};
  static constexpr uint32_t Magic = 0x44574631;
  WebServer web_{80};
  DNSServer dns_;
  Preferences prefs_;
  Print *log_ = nullptr;
  uint16_t port_ = 3333;
  String setupSsid_, token_, message_;
  bool storageReady_ = false, pending_ = false, disconnecting_ = false;
  bool connecting_ = false, wasConnected_ = false, savePending_ = false, activeSaved_ = false;
  bool retrySaved_ = false;
  uint32_t startAt_ = 0, attemptAt_ = 0, retryAt_ = 0;

  bool connected() {
    return !pending_ && !disconnecting_ && WiFi.status() == WL_CONNECTED &&
           WiFi.localIP() != IPAddress(0, 0, 0, 0) && WiFi.SSID() == credentials_.ssid;
  }
  bool validStored() {
    return credentials_.magic == Magic && credentials_.mode <= 2 && credentials_.ssid[0] &&
      memchr(credentials_.ssid, 0, sizeof(credentials_.ssid)) &&
      memchr(credentials_.password, 0, sizeof(credentials_.password)) &&
      memchr(credentials_.username, 0, sizeof(credentials_.username)) &&
      memchr(credentials_.identity, 0, sizeof(credentials_.identity)) &&
      memchr(credentials_.ca, 0, sizeof(credentials_.ca));
  }
  static String quoted(const String &value) {
    String out = "\"";
    for (size_t i = 0; i < value.length(); ++i) {
      const uint8_t c = value[i];
      if (c == '\\' || c == '"') { out += '\\'; out += static_cast<char>(c); }
      else if (c < 0x20) { char escaped[7]; snprintf(escaped, sizeof(escaped), "\\u%04x", c); out += escaped; }
      else out += static_cast<char>(c);
    }
    return out + '"';
  }
  void json(int code, const String &body) {
    web_.sendHeader("Cache-Control", "no-store");
    web_.send(code, "application/json; charset=utf-8", body);
  }
  void error(int code, const String &message) { json(code, "{\"error\":" + quoted(message) + "}"); }
  bool fromSetup() {
    if (web_.client().localIP() == WiFi.softAPIP()) return true;
    error(403, "Hãy nối vào Wi-Fi cấu hình của ESP32 để mở trang này."); return false;
  }
  bool authorized() {
    if (!fromSetup()) return false;
    if (web_.header("X-Setup-Token") == token_) return true;
    error(403, "Trang đã hết phiên. Hãy tải lại trang cấu hình."); return false;
  }
  void sendStatus() {
    const bool online = connected();
    const char *state = online ? "connected" : (pending_ || disconnecting_ || connecting_) ? "connecting" : credentials_.ssid[0] ? "failed" : "idle";
    const char *mode = credentials_.mode == 0 ? "open" : credentials_.mode == 2 ? "peap" : "personal";
    json(200, String("{\"state\":") + quoted(state) + ",\"ssid\":" + quoted(credentials_.ssid) + ",\"mode\":" + quoted(mode) +
      ",\"ip\":" + quoted(online ? WiFi.localIP().toString() : String()) + ",\"port\":" + port_ +
      ",\"setupSsid\":" + quoted(setupSsid_) + ",\"saved\":" + (activeSaved_ ? "true" : "false") +
      ",\"message\":" + quoted(message_) + "}");
  }
  void scan() {
    if (pending_ || disconnecting_ || connecting_) { error(409, "Đợi kết nối hiện tại hoàn tất rồi quét lại."); return; }
    int16_t count = WiFi.scanComplete();
    if (web_.arg("start") == "1" && count != WIFI_SCAN_RUNNING) {
      WiFi.scanDelete();
      count = WiFi.scanNetworks(true, true);
    }
    if (count == WIFI_SCAN_RUNNING) { json(200, "{\"scanning\":true,\"networks\":[]}"); return; }
    if (count < 0) { error(503, "Chưa quét được mạng. Hãy thử lại."); return; }
    String result = "{\"scanning\":false,\"networks\":[";
    bool first = true;
    for (int16_t i = 0; i < count && i < 30; ++i) {
      const String ssid = WiFi.SSID(i);
      if (!ssid.length()) continue;
      if (!first) result += ',';
      first = false;
      const auto auth = WiFi.encryptionType(i);
      const char *mode = auth == WIFI_AUTH_OPEN ? "open" : auth == WIFI_AUTH_WPA2_ENTERPRISE ? "peap" : "personal";
      result += "{\"ssid\":" + quoted(ssid) + ",\"rssi\":" + String(WiFi.RSSI(i)) + ",\"mode\":" + quoted(mode) + "}";
    }
    json(200, result + "]}");
  }
  void connectRequest() {
    if (pending_ || disconnecting_ || connecting_ || WiFi.scanComplete() == WIFI_SCAN_RUNNING) {
      error(409, "Đợi thao tác hiện tại hoàn tất rồi thử lại."); return;
    }
    const String ssid = web_.arg("ssid"), password = web_.arg("password"), mode = web_.arg("mode");
    const String username = web_.arg("username"), identity = web_.arg("identity"), ca = web_.arg("ca");
    if (!ssid.length() || ssid.length() > 32 || strlen(ssid.c_str()) != ssid.length() ||
        strlen(password.c_str()) != password.length() || strlen(username.c_str()) != username.length() ||
        strlen(identity.c_str()) != identity.length() || strlen(ca.c_str()) != ca.length() ||
        password.length() > 128 || username.length() > 128 || identity.length() > 128 || ca.length() > 4096) {
      error(400, "Tên mạng hoặc thông tin đăng nhập không hợp lệ/quá dài."); return;
    }
    if (mode != "open" && mode != "personal" && mode != "peap") { error(400, "Loại mạng không hợp lệ."); return; }
    bool hexKey = password.length() == 64;
    for (size_t i = 0; i < password.length(); ++i) hexKey = hexKey && isxdigit(static_cast<unsigned char>(password[i]));
    if (mode == "personal" && (password.length() < 8 || (password.length() > 63 && !hexKey))) {
      error(400, "Mật khẩu Wi-Fi cần 8–63 ký tự, hoặc khóa hex 64 ký tự."); return;
    }
    if (mode == "peap" && (!username.length() || !identity.length() || !password.length())) {
      error(400, "Mạng PEAP cần tên đăng nhập, Identity và mật khẩu."); return;
    }
    if (ca.length() && (ca.indexOf("-----BEGIN CERTIFICATE-----") < 0 || ca.indexOf("-----END CERTIFICATE-----") < 0)) {
      error(400, "Chứng chỉ CA cần có định dạng PEM."); return;
    }
    memset(&credentials_, 0, sizeof(credentials_));
    credentials_.magic = Magic;
    credentials_.mode = mode == "open" ? 0 : mode == "peap" ? 2 : 1;
    ssid.toCharArray(credentials_.ssid, sizeof(credentials_.ssid));
    if (mode != "open") password.toCharArray(credentials_.password, sizeof(credentials_.password));
    username.toCharArray(credentials_.username, sizeof(credentials_.username));
    identity.toCharArray(credentials_.identity, sizeof(credentials_.identity));
    ca.toCharArray(credentials_.ca, sizeof(credentials_.ca));
    savePending_ = true; activeSaved_ = false;
    queueConnection();
    json(202, "{\"ok\":true}");
  }
  void queueConnection() {
    retrySaved_ = false;
    pending_ = true; connecting_ = false; wasConnected_ = false;
    startAt_ = millis() + 300;
    message_ = "Đang kết nối. Cấu hình sẽ được lưu sau khi kết nối thành công.";
  }
  void startConnection() {
    if (credentials_.mode == 2) {
#if CONFIG_ESP_WIFI_ENTERPRISE_SUPPORT
      WiFi.begin(credentials_.ssid, WPA2_AUTH_PEAP, credentials_.identity, credentials_.username,
                 credentials_.password, credentials_.ca[0] ? credentials_.ca : nullptr);
#else
      message_ = "ESP32 core chưa hỗ trợ mạng Enterprise."; savePending_ = false; return;
#endif
    } else WiFi.begin(credentials_.ssid, credentials_.mode == 0 ? nullptr : credentials_.password);
    connecting_ = true; attemptAt_ = millis();
    log_->println(F("STATUS,WIFI_STA_CONNECTING"));
  }
};
#endif
