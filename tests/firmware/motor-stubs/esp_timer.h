#pragma once
#include <stdint.h>
#define ESP_OK 0
#define ESP_TIMER_TASK 0
struct esp_timer_create_args_t {
  void (*callback)(void *);
  void *arg;
  int dispatch_method;
  const char *name;
};
using esp_timer_handle_t = esp_timer_create_args_t *;
namespace MotorMock {
inline esp_timer_create_args_t timer = {};
inline bool timerOk = true, active = false;
inline uint64_t duration = 0;
inline int64_t timeUs = 0;
}
inline int esp_timer_create(const esp_timer_create_args_t *args, esp_timer_handle_t *timer) {
  MotorMock::timer = *args; *timer = &MotorMock::timer; return ESP_OK;
}
inline int esp_timer_start_once(esp_timer_handle_t, uint64_t duration) {
  MotorMock::duration = duration;
  MotorMock::active = MotorMock::timerOk;
  return MotorMock::timerOk ? ESP_OK : -1;
}
inline int esp_timer_stop(esp_timer_handle_t) { MotorMock::active = false; return ESP_OK; }
inline int64_t esp_timer_get_time() { return MotorMock::timeUs; }
inline int esp_timer_start_periodic(esp_timer_handle_t timer, uint64_t period) {
  return esp_timer_start_once(timer, period);
}
