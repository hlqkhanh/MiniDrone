#pragma once

// Wi-Fi credentials are now entered in the browser portal and stored in NVS.
// Optional WifiConfig.local.h can override the telemetry port only.
namespace WifiConfig {
constexpr uint16_t Port = 3333;
}
