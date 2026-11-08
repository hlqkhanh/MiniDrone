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
| `CONFIG kp ki kd angleKp base motorSign outputLimit` | Disarmed/Fault; outputLimit 10–100 với I limit mặc định 10; Base 1–220; mọi giá trị finite, sign ±1 |
| `VERIFY 0|1` | Người vận hành xác nhận mapping/cảm biến/đế; không phải board tự đo được chiều lực |
| `PROPS_REMOVED 0|1` | Xác nhận tháo cánh chỉ cho bench |
| `ARM epoch` | Epoch hiện tại đọc từ ACK/T; không có phiên Armed/Running khác |
| `START epoch mode target durationMs` | Mode 0 Rate, 1 Angle, 2 DryRun; chạy liên tục đến STOP hoặc fault. App gửi durationMs=0; firmware bỏ qua đối số durationMs cũ. Đã calibrate, verified, Kp dương; ANGLE cần Angle P dương. Không kiểm tra ngưỡng góc lúc START. |
| `BENCH epoch index duty durationMs` | Index LF0/RF1/LR2/RR3, duty 0–255 theo PWM 8-bit; 100–2000ms hoặc 0 để chạy liên tục; cần propsRemoved và Armed |
| `BENCH_MULTI epoch lf rf lr rr durationMs` | Thử tổ hợp motor với duty riêng 0–255; ít nhất một duty >0; 100–2000ms hoặc 0 để chạy liên tục; cần propsRemoved và Armed. Không thay đổi lệnh BENCH cũ |
| `KEEPALIVE` | Chỉ chủ phiên Running; gửi mỗi 500 ms |
| `STOP` | Mọi kết nối, ưu tiên cắt output; tăng epoch làm vô hiệu lệnh cũ |
| `TELEMETRY 50|100` | Disarmed/Fault |

T v0.2: 50 trường gồm prefix. Trường 0–44 giữ vị trí v0.1; trường13/14 là góc/tốc độ của TRỤC ĐANG CHỌN, gains/sign/limit cũng thuộc trục đó. Năm trường thêm (zero-based): 45 axis (0/1), 46 roll, 47 rollRate, 48 pitch, 49 pitchRate. App v0.2 đọc cả T45 cũ (chỉ Roll) và T50; app cũ phải cập nhật cùng firmware.

Sau ZERO, góc trong T, điều kiện START và giới hạn controller đều dùng góc sau bù của trục tương ứng. Rate không đổi. INFO trả thêm dòng LEVEL chứa axis, rollRaw/rollOffset/roll, pitchRaw/pitchOffset/pitch, ready của trục đang chọn. Reset nguồn hoặc CALIBRATE xóa hai mốc. Bù này không thay thế hiệu chuẩn accelerometer hay phép bù đầy đủ góc lắp.

Quy đổi mounting trước khi lấy góc: quaternion sensor (w,x,y,z) → body (w,-y,x,z). Roll dùng atan2 (±180°), Pitch dùng asin (±90°); gần Pitch ±90° vẫn có singularity Euler. Rate body: -gyroY cho Roll, +gyroX cho Pitch. Không có magnetometer nên không cung cấp heading Yaw tuyệt đối. Mixer Roll = [B+r,B-r,B+r,B-r], Pitch = [B+p,B+p,B-p,B-p] theo LF/RF/LR/RR; sign mỗi trục phải xác minh thực tế.

START không còn yêu cầu hai góc trong ±5°. Khi chạy, không dừng bởi góc/tốc độ vượt ngưỡng trên cả hai trục (bỏ ENVELOPE/OTHER_AXIS_ENVELOPE) hoặc bão hòa kéo dài (bỏ SATURATION_TRACKING). PID/mixer vẫn giới hạn duty và báo pidLimited/mixerLimited; chống windup vẫn hoạt động. BENCH vẫn hữu hạn. Session/gate dùng deadline=0 cho START, deadline hữu hạn cho BENCH. STOP, heartbeat, stale-control và các fault khác vẫn hoạt động.

Mode telemetry 3 chỉ BENCH (không chạy PID), phân biệt với START mode 0/1/2.

`missing` trong header v0.1 là số lần poll chưa có data-ready, **không phải số mẫu vật lý mất được đo chính xác**. Seq chỉ tăng trên mẫu hợp lệ; khoảng seq đứt ở app cho biết telemetry bị bỏ/giảm mẫu, không phải toàn bộ sự kiện IMU. `late` đếm khoảng mẫu >7.5ms. PWM telemetry là lệnh đề xuất đã qua gate, không phải đo lực, RPM hoặc điện áp thực.

HEALTH mỗi 500 ms: epoch, sensor available, calibrated, calCount, PWM ready, gate reason; vẫn có khi IMU lỗi để phân biệt lỗi sensor với mất transport. Log sự kiện và telemetry riêng; cấu hình cố định trong RollCore.h phải đối chiếu source ID khi phân tích.

Các fault: CONFIG, SENSOR, TIMING (dt ngoài 2–20ms), GAINS_ZERO, TARGET, STALE_CONTROL, HEARTBEAT, PWM và lỗi queue. ENVELOPE/OTHER_AXIS_ENVELOPE/SATURATION_TRACKING đã bỏ từ 28/10/2026; DURATION chỉ còn cho BENCH. Các trường cấu hình mô phỏng maxAngle/maxRate/saturationSeconds/saturationError và mã fault số 4/7 được giữ để tương thích layout dữ liệu cũ, không còn tạo cắt motor.

TCP gửi non-blocking trực tiếp qua socket, tối đa256 byte/lượt, giữ cursor khi gửi thiếu hoặc EAGAIN; không dùng `NetworkClient.availableForWrite()` vì core3.3.12 kế thừa giá trị0. App đợi INFO tối đa8s khi kết nối, ACK các lệnh thường2s, thời gian write và đóng transport có giới hạn riêng.

Mặc định output ±20 duty; cấu hình cho phép output limit 10–100, motor 0–255, base 1–220, rate target ±20°/s, Angle target ±10°. Không tự dùng các giới hạn này làm khẳng định phù hợp với mọi bố trí đế.

## Góc IMU cho tab 3D

T53 giữ nguyên 50 trường trước và thêm 50 `imuRoll`, 51 `imuPitch`, 52 `imuYaw` (độ, body ZYX). Đây là góc estimator trước bù ZERO_ROLL/ZERO_PITCH. Yaw tương đối từ gyro, không phải hướng la bàn tuyệt đối.

App đọc T45/T50/T53. Tab 3D cần T53; với firmware cũ app hiển thị chờ góc IMU, không dựng góc còn thiếu. Nút **Hiệu chỉnh yaw** lưu mốc yaw trong app và chỉ trừ mốc đó; roll/pitch luôn theo IMU. Mốc yaw xóa khi ngắt/kết nối lại. Không gửi ZERO_ROLL, ZERO_PITCH hoặc CALIBRATE khi bấm nút này. Các nút lấy mốc cân bằng PID và hiệu chuẩn gyro vẫn riêng biệt.

CSV mới có 53 cột; các góc 3D để trống khi nhận firmware cũ.

## Inner / Outer PID

Firmware mới gửi T55, giữ nguyên T53 và thêm 53 `angleKi`, 54 `angleKd` của trục đang chọn. App đọc T45/T50/T53/T55.

`CONFIG_CASCADE innerKp innerKi innerKd outerKp outerKi outerKd base motorSign outputLimit` cập nhật cả hai vòng của trục đang chọn, chỉ khi Disarmed/Fault. Mỗi trục giữ cấu hình riêng trong RAM. Lệnh xóa VERIFY, reset trạng thái PID và output. `CONFIG` cũ vẫn dùng được và giữ Ki/Kd Outer hiện tại.

RATE chỉ chạy Inner PID (°/s → duty). ANGLE chạy Outer PID (° → mục tiêu °/s, giới hạn ±rateLimit) rồi Inner PID. Outer dùng tích phân hình thang, giới hạn I ±rateLimit, chống windup khi target rate/Inner/mixer bão hòa; D trên góc đo, lọc bằng dHz để tránh kick khi đổi setpoint. STOP/ARM/START/đổi trục/cấu hình reset cả hai vòng. Outer Kp 0–5, Ki 0–2, Kd 0–1. Mặc định Ki/Kd Outer = 0 nên giữ hành vi P góc cũ. App firmware cũ chỉ áp dụng được Outer P; muốn Ki/Kd khác 0 cần nạp firmware T55.

Các mô hình MATLAB/Simulink với layout cấu hình 14 giá trị hiện tại vẫn dùng Outer Ki/Kd = 0; chưa mô phỏng các gain Outer mới qua layout đó.

## Chạy riêng Roll / Pitch hoặc cả hai

Firmware quảng bá `DUAL_AXIS_V1` trong INFO. `START_BOTH epoch mode rollTarget pitchTarget` dùng cùng mode RATE/ANGLE/DRYRUN cho hai trục, target riêng. Kiểm tra gain/config hợp lệ của cả hai và cùng Base trước khi chạy. PID và tích phân độc lập; trộn chung: LF=B+r+p, RF=B-r+p, LR=B+r-p, RR=B-r-p. Mỗi sign riêng; clamp 0–255, phản hồi bão hòa mixer về cả hai PID. Chỉ xuất qua gate chung, STOP/heartbeat/stale và dry-run như trước.

Telemetry T56 giữ nguyên T55, thêm trường 55 `runAxes` (0 Roll, 1 Pitch, 2 cả hai). Khi chạy cả hai, telemetry PID/rate/output chính vẫn là Roll; các góc Roll/Pitch và PWM là đo/lệnh chung. Chưa có đồ thị PID chi tiết riêng cho Pitch khi chạy cả hai.

App tách trục chỉnh gain/lấy mốc khỏi phạm vi chạy. Apply cả hai gửi AXIS/CONFIG_CASCADE tuần tự, không tự CALIBRATE/ZERO. Gain trục chưa sửa được đọc lại từ board; gain đã nhập được giữ riêng. Base hai trục phải bằng nhau, không tự thay đổi Base của Roll. Firmware cũ vẫn chạy đơn trục nhưng không hỗ trợ cả hai.

Mốc cân bằng hiện tại vẫn nằm trong RAM như trước: đổi trục/Apply/START không xóa mốc; reset hoặc nạp firmware sẽ xóa mốc. Ghi lại gain đã chỉnh trước khi nạp.


## Hạ bán tự động STABILIZED_DESCENT_V1

INFO có capability `STABILIZED_DESCENT_V1`, thay cho SOFT_STOP_V1. Ramp SOFT_STOP đã bị loại bỏ. App mới có nút hạ riêng; STOP/Esc luôn yêu cầu cắt ngay.

- `DESCEND epoch rate floor`: đúng owner/epoch, Running hai trục Angle, không BENCH/DryRun. Rate 0.1–10 duty/s; floor < Base ban đầu và > tổng outputLimit hai trục.
- `DESCEND_PAUSE epoch`, `DESCEND_RESUME epoch`: giữ/tiếp tục giảm collective. Không resume tại sàn.
- `DESCEND_BOOST epoch delta`: tăng tối đa 10 duty mỗi command (app dùng 5), không vượt Base ban đầu, sau đó giữ.

T56 giữ layout, state vẫn RUNNING và reason DESCENDING/DESCENT_PAUSED/DESCENT_FLOOR. Dòng `DESCENT,epoch,reason,collective,floor,rate` gửi owner khoảng mỗi 500 ms. Base trong T vẫn là config đã Apply; collective động nằm ở dòng DESCENT. Watchdog và TCP disconnect vẫn cắt theo chính sách cũ. Chạm floor không disarm và không chứng minh đã đáp.

Chi tiết: [STABILIZED_DESCENT.md](STABILIZED_DESCENT.md).
