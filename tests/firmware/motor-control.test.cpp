// Host tests replace PWM and the ESP timer; invoke the real supervisor callback.
#define ARDUINO_ARCH_ESP32
#include "../../firmware/DroneMotionMpu6050/MotorControl.h"
#include <assert.h>

void stopped() {
  for (int pin : {14, 15, 12, 13}) assert(MotorMock::duty[pin] == 0);
}
void tick(int64_t us) {
  MotorMock::timeUs += us;
  MotorMock::timer.callback(MotorMock::timer.arg);
}
int main() {
  MotorControl motors;
  Print out;
  motors.begin(); stopped();
  assert(MotorMock::active && MotorMock::duration == 50000);
  motors.command("LF 20 1000", false, out);
  assert(out.last == "ERROR,MOTOR_ARM_REQUIRED"); stopped();
  const char *names[] = {"LF", "RF", "LR", "RR", "ALL"};
  const int pins[] = {14, 15, 12, 13};
  for (int selected = 0; selected < 5; ++selected) {
    motors.command("ARM", false, out);
    motors.command((std::string(names[selected]) + " 20 1000").c_str(), false, out);
    assert(out.last == "STATUS,MOTOR_RUNNING");
    for (int i = 0; i < 4; ++i)
      assert(MotorMock::duty[pins[i]] == (selected == 4 || selected == i ? 51u : 0u));
    tick(999000); assert(MotorMock::duty[pins[selected == 4 ? 0 : selected]] == 51);
    tick(1000); stopped();
    motors.poll(out, true); assert(out.last == "STATUS,MOTOR_STOPPED");
  }
  for (const char *invalid : {"ALL 101 1000", "ALL -1 1000", "LF 20x 100",
      "SET 256 0 0 0 10", "SET 1 2 3 4 4294967296", "ALL 20 100 extra",
      "ALL 20 999999999999999999999999", "SET 1 2 3 10", "UNKNOWN", ""}) {
    motors.command("ARM", false, out);
    motors.command(invalid, false, out);
    assert(out.last == "ERROR,MOTOR_INVALID_COMMAND"); stopped();
  }
  // Full physical duty range and uint32 duration, per-motor outputs.
  motors.command("ARM", false, out);
  motors.command("SET 0 1 128 255 4294967295", false, out);
  assert(out.last == "STATUS,MOTOR_RUNNING");
  assert(MotorMock::duty[14] == 0 && MotorMock::duty[15] == 1);
  assert(MotorMock::duty[12] == 128 && MotorMock::duty[13] == 255);
  for (int i = 0; i < 100; ++i) { tick(500000); motors.command("KEEPALIVE", false, out); }
  assert(MotorMock::duty[13] == 255); // No obsolete two-second duration cap.
  tick(2000000); stopped(); // Link timeout, no main-loop poll required.

  motors.command("ARM", false, out);
  motors.command("ALL 100 0", false, out); // Continuous.
  for (int i = 0; i < 20; ++i) { tick(500000); motors.command("KEEPALIVE", false, out); }
  assert(MotorMock::duty[14] == 255);
  motors.command("STOP", true, out); stopped();

  motors.command("ARM", false, out);
  tick(15000000);
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_ARM_REQUIRED");
  motors.command("ARM", true, out);
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_ARM_REQUIRED");
  motors.command("ARM", true, out);
  motors.command("ALL 100 100000", true, out);
  motors.poll(out, false); stopped();
  motors.command("ARM", true, out);
  motors.command("ALL 100 0", true, out);
  tick(1500000);
  motors.command("KEEPALIVE", false, out); // Another connection cannot extend lease.
  tick(500000); stopped();

  // Reapply creates a fresh deadline, not an old pending timeout.
  motors.command("ARM", false, out);
  motors.command("ALL 20 1000", false, out);
  tick(500000);
  motors.command("ARM", false, out);
  motors.command("ALL 30 2000", false, out);
  tick(500000); assert(MotorMock::duty[14] == 77);
  motors.command("KEEPALIVE", false, out);
  tick(1500000); stopped();

  motors.command("ARM", false, out);
  MotorMock::writeOk = false;
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_PWM_FAILED");
  MotorMock::writeOk = true;
  MotorMock::attachOk = false;
  MotorControl broken;
  broken.begin();
  broken.command("ARM", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_UNAVAILABLE");
  MotorMock::attachOk = true;
  MotorMock::timerOk = false;
  MotorControl timerBroken;
  timerBroken.begin();
  timerBroken.command("ARM", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_UNAVAILABLE");
}
