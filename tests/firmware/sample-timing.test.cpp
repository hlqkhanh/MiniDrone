#include "../../firmware/DroneMotionMpu6050/SampleTiming.h"
#include "../../firmware/DroneMotionMpu6050/FlightController.h"
#include <assert.h>
#include <initializer_list>
#include <string.h>

int main() {
  using SampleTiming::nextDeadline;
  // Normal 200 Hz phase is preserved.
  assert(nextDeadline(5000, 5000, 5000) == 10000);
  assert(nextDeadline(5000, 6000, 5000) == 10000);
  assert(nextDeadline(5000, 8000, 5000) == 10000);
  // Previously the 9 ms sample would be followed at 10 ms (a 1 ms dt).
  assert(nextDeadline(5000, 9000, 5000) == 14000);
  assert(nextDeadline(14000, 14000, 5000) == 19000);
  assert(nextDeadline(5000, 16000, 5000) == 21000);
  // Preserve Nano's regular 100 Hz schedule as well.
  assert(nextDeadline(10000, 10000, 10000) == 20000);

  // Check multiple delay sizes across the micros() wrap boundary. Every due
  // sample schedules a future tick at least 2 ms away, never a catch-up burst.
  for (uint32_t deadline : {5000u, 0xfffffff0u}) {
    for (uint32_t delay = 0; delay < 60000; delay += 137) {
      const uint32_t now = deadline + delay;
      const uint32_t next = nextDeadline(deadline, now, 5000);
      assert(static_cast<int32_t>(next - now) >= 2000);
      assert(static_cast<int32_t>(next - now) <= 5000);
    }
  }

  // Scheduling does not relax either controller timing guard.
  for (float invalidDt : {0.001f, 0.021f, 0.060f}) {
    Flight::Controller fc;
    assert(fc.setGains(1, {0.1f, 0, 0}));
    assert(fc.start(50, 1));
    assert(!fc.update(invalidDt));
    assert(!fc.active && strcmp(fc.fault, "CONTROL_TIMING_FAULT") == 0);
    for (auto duty : fc.duties) assert(duty == 0);
  }
}
