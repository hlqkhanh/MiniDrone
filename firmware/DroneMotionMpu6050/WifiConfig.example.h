#pragma once

// Direct AP only. Router credentials stored by older firmware are not loaded.
// Optional WifiConfig.local.h can override the telemetry port only.
namespace WifiConfig {
constexpr uint16_t Port = 3333;
}
