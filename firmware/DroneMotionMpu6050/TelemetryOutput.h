#pragma once
#include <Arduino.h>
#if defined(ARDUINO_ARCH_ESP32)
#include <WiFi.h>
#include <lwip/sockets.h>
#include <errno.h>
#include "TelemetrySendBuffer.h"
#include "DirectWifi.h"
#if __has_include("WifiConfig.local.h")
#include "WifiConfig.local.h"
#else
#include "WifiConfig.example.h"
#endif
#endif

// Send complete protocol lines to USB and to one Wi-Fi viewer.
class TelemetryOutput : public Print {
 public:
  using Print::write;
  size_t write(uint8_t byte) override {
    if (used_ < sizeof(line_)) line_[used_++] = byte;
    else overflow_ = true;
    if (byte == '\n') {
      if (!overflow_) {
#if defined(ARDUINO_ARCH_ESP32)
        // USB congestion must never stall the control loop. Drop whole lines.
        if (Serial.availableForWrite() >= static_cast<int>(used_)) Serial.write(line_, used_);
#else
        Serial.write(line_, used_);
#endif
#if defined(ARDUINO_ARCH_ESP32)
        if (client_.connected()) {
          tx_.append(line_, used_, millis());
          flushWifi();
        }
#endif
      }
      used_ = 0;
      overflow_ = false;
    }
    return 1;
  }

  void beginWifi() {
#if defined(ARDUINO_ARCH_ESP32)
    if (directWifi_.begin(*this, WifiConfig::Port)) server_.begin();
#endif
  }

  void reportWifiInfo() {
#if defined(ARDUINO_ARCH_ESP32)
    directWifi_.report(*this);
#endif
  }
  bool acceptClient() {
#if defined(ARDUINO_ARCH_ESP32)
    flushWifi();
    auto incoming = server_.accept();
    if (!incoming) return false;
    if (client_.connected()) { incoming.stop(); return false; }
    client_.stop();
    tx_.clear();
    client_ = incoming;
    client_.setNoDelay(true);
    return true;
#else
    return false;
#endif
  }

#if defined(ARDUINO_ARCH_ESP32)
  NetworkClient &clientStream() { return client_; }
#endif

 private:
  uint8_t line_[320];
  size_t used_ = 0;
  bool overflow_ = false;
#if defined(ARDUINO_ARCH_ESP32)
  using TxBuffer = TelemetrySendBuffer<2048>;
  TxBuffer tx_;
  void flushWifi() {
    if (!client_.connected()) { tx_.clear(); return; }
    // Some core versions keep connected() true after a graceful TCP close.
    // Detect EOF without consuming command bytes so a reconnect can be accepted.
    uint8_t pending;
    if (recv(client_.fd(), &pending, 1, MSG_DONTWAIT | MSG_PEEK) == 0) {
      client_.stop(); tx_.clear(); return;
    }
    int sendError = 0;
    const auto result = tx_.flush(millis(), [this, &sendError](const uint8_t *bytes, size_t length) {
      const int sent = send(client_.fd(), bytes, length, MSG_DONTWAIT);
      sendError = sent < 0 ? errno : 0;
      return TxBuffer::SendResult{sent, sendError == EAGAIN || sendError == EWOULDBLOCK ||
                                      sendError == EINTR || sendError == ENOBUFS || sendError == ENOMEM};
    });
    if (result == TxBuffer::FlushResult::Failed || result == TxBuffer::FlushResult::Stalled) {
      // Log directly to USB so diagnostics cannot recurse into this sender.
      Serial.print(result == TxBuffer::FlushResult::Stalled
        ? F("ERROR,TCP_SEND_STALLED_20S,") : F("ERROR,TCP_SEND_FAILED,"));
      Serial.println(sendError);
      client_.stop();
      tx_.clear();
    }
  }

  NetworkServer server_{WifiConfig::Port};
  NetworkClient client_;
  DirectWifi directWifi_;
#endif
};
