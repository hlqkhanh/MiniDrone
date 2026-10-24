#pragma once
#include <stdint.h>

namespace SampleTiming {

// Keep the normal phase when possible. After a late sample, skip the old
// deadline if it would schedule another sample too soon. Do not replay ticks.
// Call only when the current deadline is due; period is 5 ms on ESP32.
constexpr uint32_t nextDeadline(uint32_t deadline, uint32_t now, uint32_t period) {
  const uint32_t next = deadline + period;
  return static_cast<int32_t>(next - now) < 2000
      ? now + period : next;
}

} // namespace SampleTiming
