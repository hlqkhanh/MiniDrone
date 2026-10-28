# Giao thức DroneRebuild v0.2

ASCII, newline, USB 460800 hoặc TCP 3333. Mỗi lệnh: `<requestId> <command>`. ACK chỉ về kết nối gửi lệnh:

```text
ACK,<requestId>,OK|ERROR,<epoch>,<command/reason>
```

App phải chờ ACK và kiểm tra cấu hình board đọc lại qua T, không coi ô nhập là giá trị đã Apply. Không tương thích app WPF cũ.

| Lệnh | Điều kiện / ý nghĩa |
|---|---|
| `INFO` | Phiên bản, source hash, build timestamp, HW profile, chip, IP, beta, gyro bias, PWM ready |
| `CALIBRATE` | Disarmed/Fault; motor tắt; cửa sổ 500 mẫu, timeout 30 s; cần kiểm tra lại xác nhận mapping |
| `ZERO_ROLL` | Disarmed/Fault, gate đã dừng, IMU đã hiệu chuẩn, mẫu mới <20ms; lấy trung bình 100 mẫu đứng yên (rate ≤1°/s, accel 0.95–1.05g, khoảng góc ≤0.5°), mốc thô trong ±10°. Xóa VERIFY, reset PID. Không ghi flash. |
| `ZERO_PITCH` | Như ZERO_ROLL nhưng bù mốc Pitch. Hai mốc độc lập. Đòi đứng yên ở cả ba trục gyro. |
| `AXIS 0|1` | Disarmed/Fault; 0=Roll, 1=Pitch. Chọn cấu hình riêng trong RAM; xóa VERIFY/reset PID/target. Mỗi lượt chỉ điều khiển một trục. CONFIG và START áp dụng lên trục đang chọn. |
| `CONFIG kp ki kd angleKp base motorSign outputLimit` | Disarmed/Fault; outputLimit 10–40 với I limit mặc định 10; mọi giá trị finite, sign ±1 |
| `VERIFY 0|1` | Người vận hành xác nhận mapping/cảm biến/đế; không phải board tự đo được chiều lực |
| `PROPS_REMOVED 0|1` | Xác nhận tháo cánh chỉ cho bench |
| `ARM epoch` | Epoch hiện tại đọc từ ACK/T; không có phiên Armed/Running khác |
| `START epoch mode target durationMs` | Mode 0 Rate, 1 Angle, 2 DryRun; max 10000 ms; đã calibrate, verified, Kp dương; góc đầu ≤5° |
| `BENCH epoch index duty durationMs` | Index LF0/RF1/LR2/RR3, duty ≤89 (≤35%), max 2000ms, cần propsRemoved và Armed |
| `KEEPALIVE` | Chỉ chủ phiên Running; gửi mỗi 500 ms |
| `STOP` | Mọi kết nối, ưu tiên cắt output; tăng epoch làm vô hiệu lệnh cũ |
| `TELEMETRY 50|100` | Disarmed/Fault |

T v0.2: 50 trường gồm prefix. Trường 0–44 giữ vị trí v0.1; trường13/14 là góc/tốc độ của TRỤC ĐANG CHỌN, gains/sign/limit cũng thuộc trục đó. Năm trường thêm (zero-based): 45 axis (0/1), 46 roll, 47 rollRate, 48 pitch, 49 pitchRate. App v0.2 đọc cả T45 cũ (chỉ Roll) và T50; app cũ phải cập nhật cùng firmware.

Sau ZERO, góc trong T, điều kiện START và giới hạn controller đều dùng góc sau bù của trục tương ứng. Rate không đổi. INFO trả thêm dòng LEVEL chứa axis, rollRaw/rollOffset/roll, pitchRaw/pitchOffset/pitch, ready của trục đang chọn. Reset nguồn hoặc CALIBRATE xóa hai mốc. Bù này không thay thế hiệu chuẩn accelerometer hay phép bù đầy đủ góc lắp.

Quy đổi mounting trước khi lấy góc: quaternion sensor (w,x,y,z) → body (w,-y,x,z). Roll dùng atan2 (±180°), Pitch dùng asin (±90°); gần Pitch ±90° vẫn có singularity Euler. Rate body: -gyroY cho Roll, +gyroX cho Pitch. Không có magnetometer nên không cung cấp heading Yaw tuyệt đối. Mixer Roll = [B+r,B-r,B+r,B-r], Pitch = [B+p,B+p,B-p,B-p] theo LF/RF/LR/RR; sign mỗi trục phải xác minh thực tế.

START cần cả hai góc trong ±5°. Khi chạy PID, trục không điều khiển vượt ±15° hoặc ±150°/s cũng dừng với OTHER_AXIS_ENVELOPE. BENCH vẫn là chế độ tháo cánh riêng.

Mode telemetry 3 chỉ BENCH (không chạy PID), phân biệt với START mode 0/1/2.

`missing` trong header v0.1 là số lần poll chưa có data-ready, **không phải số mẫu vật lý mất được đo chính xác**. Seq chỉ tăng trên mẫu hợp lệ; khoảng seq đứt ở app cho biết telemetry bị bỏ/giảm mẫu, không phải toàn bộ sự kiện IMU. `late` đếm khoảng mẫu >7.5ms. PWM telemetry là lệnh đề xuất đã qua gate, không phải đo lực, RPM hoặc điện áp thực.

HEALTH mỗi 500 ms: epoch, sensor available, calibrated, calCount, PWM ready, gate reason; vẫn có khi IMU lỗi để phân biệt lỗi sensor với mất transport. Log sự kiện và telemetry riêng; cấu hình cố định trong RollCore.h phải đối chiếu source ID khi phân tích.

Các fault: CONFIG, SENSOR, TIMING (dt ngoài 2–20ms), ENVELOPE (góc >15°, rate >150°/s), GAINS_ZERO, TARGET, SATURATION_TRACKING (liên tục 0.5s có PID chạm biên và error >5°/s, hoặc mixer bị cắt), STALE_CONTROL, HEARTBEAT, PWM và lỗi queue.

TCP gửi non-blocking trực tiếp qua socket, tối đa256 byte/lượt, giữ cursor khi gửi thiếu hoặc EAGAIN; không dùng `NetworkClient.availableForWrite()` vì core3.3.12 kế thừa giá trị0. App đợi INFO tối đa8s khi kết nối, ACK các lệnh thường2s, thời gian write và đóng transport có giới hạn riêng.

Mặc định output ±20 duty, motor 0–220, base 1–180, rate target ±20°/s, Angle target ±10°. Đổi các giới hạn cố định cần sửa source, chạy test và build lại. Không tự dùng các ngưỡng v0.1 làm khẳng định phù hợp với mọi bố trí đế.
