// Host tests exercise command behavior with PWM and timer hardware replaced.
#define ARDUINO_ARCH_ESP32
#include "../../firmware/DroneMotionMpu6050/MotorControl.h"
#include <assert.h>

void stopped() {
  for (int pin : {14, 15, 12, 13}) assert(MotorMock::duty[pin] == 0);
}
int main() {
  MotorControl motors;
  Print out;
  motors.begin(); stopped();
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
    assert(MotorMock::active && MotorMock::duration == 1000000);
    // Deadline callback works without poll/main-loop progress.
    MotorMock::timer.callback(MotorMock::timer.arg);
    stopped();
    motors.poll(out, true);
    assert(out.last == "STATUS,MOTOR_STOPPED");
  }
  for (const char *invalid : {"ALL 36 1000", "ALL 20 2001", "ALL -1 1000",
                              "LF 20x 100", "ALL 0 1", "ALL 20 100 extra", "UNKNOWN"}) {
    motors.command("ARM", false, out);
    motors.command(invalid, false, out);
    assert(out.last == "ERROR,MOTOR_INVALID_COMMAND"); stopped();
  }
  motors.command("ARM", false, out);
  MotorMock::now += 15001;
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_ARM_REQUIRED");
  motors.command("ARM", true, out);
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_ARM_REQUIRED");
  motors.command("ARM", true, out);
  motors.command("ALL 35 2000", true, out);
  assert(MotorMock::duty[14] == 89);
  motors.poll(out, false); stopped();
  motors.command("ARM", false, out);
  motors.command("LF 20 1000", false, out);
  motors.command("STOP", true, out); stopped();
  motors.command("ARM", false, out);
  MotorMock::timerOk = false;
  motors.command("ALL 20 1000", false, out); stopped();
  assert(out.last == "ERROR,MOTOR_PWM_OR_TIMER_FAILED");
  MotorMock::timerOk = true;
  MotorMock::attachOk = false;
  MotorControl broken;
  broken.begin(); stopped();
  broken.command("ARM", false, out);
  assert(out.last == "ERROR,MOTOR_UNAVAILABLE");
}
