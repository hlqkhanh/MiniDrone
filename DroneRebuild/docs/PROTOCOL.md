# Giao thức DroneRebuild v0.1

ASCII, newline, USB 460800 hoặc TCP 3333. Mỗi lệnh: `<requestId> <command>`. ACK chỉ về kết nối gửi lệnh:

```text
ACK,<requestId>,OK|ERROR,<epoch>,<command/reason>
```

App phải chờ ACK và kiểm tra cấu hình board đọc lại qua T, không coi ô nhập là giá trị đã Apply. Không tương thích app WPF cũ.

| Lệnh | Điều kiện / ý nghĩa |
|---|---|
| `INFO` | Phiên bản, source hash, build timestamp, HW profile, chip, IP, beta, gyro bias, PWM ready |
| `CALIBRATE` | Disarmed/Fault; motor tắt; cửa sổ 500 mẫu, timeout 30 s; cần kiểm tra lại xác nhận mapping |
| `CONFIG kp ki kd angleKp base motorSign outputLimit` | Disarmed/Fault; outputLimit 10–40 với I limit mặc định 10; mọi giá trị finite, sign ±1 |
| `VERIFY 0|1` | Người vận hành xác nhận mapping/cảm biến/đế; không phải board tự đo được chiều lực |
| `PROPS_REMOVED 0|1` | Xác nhận tháo cánh chỉ cho bench |
| `ARM epoch` | Epoch hiện tại đọc từ ACK/T; không có phiên Armed/Running khác |
| `START epoch mode target durationMs` | Mode 0 Rate, 1 Angle, 2 DryRun; max 10000 ms; đã calibrate, verified, Kp dương; góc đầu ≤5° |
| `BENCH epoch index duty durationMs` | Index LF0/RF1/LR2/RR3, duty ≤89 (≤35%), max 2000ms, cần propsRemoved và Armed |
| `KEEPALIVE` | Chỉ chủ phiên Running; gửi mỗi 500 ms |
| `STOP` | Mọi kết nối, ưu tiên cắt output; tăng epoch làm vô hiệu lệnh cũ |
| `TELEMETRY 50|100` | Disarmed/Fault |

T: 45 trường gồm prefix, theo header trong app: seq, timestamp board microseconds 64-bit, state, epoch, mode, calCount, accel g, gyro deg/s trước trừ bias, roll, gyro Roll đã bù bias, target, error, P/I/D, raw/limited output, bốn duty command, hai cờ giới hạn, dt, not-ready polls, late intervals, queue drops, reason, calibrated/verified/props, gains/base/sign/limit, command.

Mode telemetry 3 chỉ BENCH (không chạy PID), phân biệt với START mode 0/1/2.

`missing` trong header v0.1 là số lần poll chưa có data-ready, **không phải số mẫu vật lý mất được đo chính xác**. Seq chỉ tăng trên mẫu hợp lệ; khoảng seq đứt ở app cho biết telemetry bị bỏ/giảm mẫu, không phải toàn bộ sự kiện IMU. `late` đếm khoảng mẫu >7.5ms. PWM telemetry là lệnh đề xuất đã qua gate, không phải đo lực, RPM hoặc điện áp thực.

HEALTH mỗi 500 ms: epoch, sensor available, calibrated, calCount, PWM ready, gate reason; vẫn có khi IMU lỗi để phân biệt lỗi sensor với mất transport. Log sự kiện và telemetry riêng; cấu hình cố định trong RollCore.h phải đối chiếu source ID khi phân tích.

Các fault: CONFIG, SENSOR, TIMING (dt ngoài 2–20ms), ENVELOPE (góc >15°, rate >150°/s), GAINS_ZERO, TARGET, SATURATION_TRACKING (liên tục 0.5s có PID chạm biên và error >5°/s, hoặc mixer bị cắt), STALE_CONTROL, HEARTBEAT, PWM và lỗi queue.

Mặc định output ±20 duty, motor 0–220, base 1–180, rate target ±20°/s, Angle target ±10°. Đổi các giới hạn cố định cần sửa source, chạy test và build lại. Không tự dùng các ngưỡng v0.1 làm khẳng định phù hợp với mọi bố trí đế.
