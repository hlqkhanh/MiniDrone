# Giải pháp cân bằng ba trục với ESP32 và MPU6050

> **Rà soát 07/11/2026:** đọc [đánh giá dẫn chứng và lộ trình kiểm chứng](THREE_AXIS_RIG_EVIDENCE_AND_VALIDATION.md) trước khi triển khai. Các khẳng định “đủ để cân bằng”, không dùng Outer Yaw và chức năng lọc còn thiếu bên dưới cần hiểu theo điều kiện được hiệu chỉnh trong bản rà soát; đây chưa phải giải pháp đã nghiệm thu trên gá của bạn.

Mình đã đối chiếu kiến trúc hiện tại với tài liệu MPU6050, PX4, ArduPilot và nghiên cứu về bộ lọc attitude. Kết luận: chỉ với ESP32 + MPU6050 vẫn có thể giữ ổn định đế xoay ba trục, nhưng firmware hiện tại còn thiếu Yaw Rate PID và bộ mixer/desaturation ba trục.

MPU6050 có accelerometer và gyroscope ba trục nhưng không có magnetometer. Vì vậy:

- Giữ Roll/Pitch tuyệt đối theo trọng lực: làm được.
- Chống quay Yaw bằng gyro, tức giữ tốc độ Yaw gần `0°/s`: làm được.
- Giữ một hướng Yaw tuyệt đối lâu dài: không làm chính xác được vì yaw sẽ trôi theo gyro. [Thông số MPU-6000/MPU-6050 của InvenSense](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6000-Datasheet.pdf)

## Nguyên nhân kiến trúc hiện tại chưa đủ

Firmware đang có:

```text
Outer Angle Roll  → Inner Rate Roll
Outer Angle Pitch → Inner Rate Pitch
Không có Rate Yaw
Mixer chỉ có Roll + Pitch
```

Trong khi kiến trúc multicopter thông thường dùng ba vòng Rate độc lập cho Roll, Pitch và Yaw. PX4 cũng xác định Rate controller là vòng trong cùng cho cả ba trục, sau đó các torque command mới được đưa qua control allocation/mixer. [PX4 PID tuning guide](https://github.com/PX4/PX4-Autopilot/blob/main/docs/en/config_mc/pid_tuning_guide_multicopter.md)

Khi Yaw được thả tự do:

```text
Tốc độ Yaw khác 0:  ωYaw ≠ 0°/s
```

nhưng firmware không tạo lực sửa Yaw:

```text
Đầu ra sửa Yaw:  uYaw = 0
```

Do đó khung có thể xoay Yaw. Mô-men này kết hợp với quán tính, dây nguồn và cơ cấu gimbal có thể làm Roll/Pitch mất ổn định dù hai PID riêng chạy tốt.

## Giải pháp phù hợp nhất

### 1. Thêm Inner PID Yaw Rate

Không dùng Outer Angle Yaw vì không có magnetometer. Chỉ đặt:

```text
Tốc độ Yaw mục tiêu = 0°/s
```

Sai số:

```text
Sai số Yaw = Tốc độ mục tiêu − Tốc độ đo được
            = 0 − Tốc độ Yaw đo được
```

Controller:

```text
Đầu ra Yaw = P + I + D

P = KpYaw × Sai số Yaw
I = KiYaw × Tổng sai số Yaw theo thời gian
D = −KdYaw × Tốc độ thay đổi của Yaw đo được

uYaw = KpYaw × eYaw
     + KiYaw × ∫eYaw dt
     − KdYaw × d(ωYaw)/dt
```

Hiểu đơn giản:

- `P` chống lại tốc độ quay Yaw hiện tại.
- `I` bù sai lệch nhỏ tồn tại lâu, chẳng hạn mô-men không đều giữa các motor.
- `D` giảm thay đổi Yaw quá nhanh và giúp hạn chế dao động.

Thực tế nên bắt đầu với:

```text
Yaw Kp nhỏ
Yaw Ki = 0 hoặc rất nhỏ
Yaw Kd = 0
```

Sau đó mới tăng dần. Yaw thường có quyền điều khiển yếu hơn Roll/Pitch; PX4 cũng giảm ưu tiên Yaw so với độ nghiêng. [PX4 parameter reference](https://docs.px4.io/v1.12/en/advanced_config/parameter_reference)

### 2. Mixer ba trục

Với bố trí:

```text
LF và RR: CCW
RF và LR: CW
```

một quy ước mixer có thể là:

```text
LF = Base + Roll + Pitch − Yaw
RF = Base − Roll + Pitch + Yaw
LR = Base + Roll − Pitch + Yaw
RR = Base − Roll − Pitch − Yaw
```

Trong đó:

- `LF`: motor trước trái;
- `RF`: motor trước phải;
- `LR`: motor sau trái;
- `RR`: motor sau phải;
- `Base`: duty nền;
- `Roll`, `Pitch`, `Yaw`: đầu ra sửa của từng bộ PID.

Nếu phản ứng Yaw ngược thì đảo dấu `Yaw`, không đảo Roll/Pitch.

### 3. Không clamp từng motor một cách độc lập

Firmware hiện tính duty rồi cắt riêng từng motor về `0–255`. Cách này làm mất tỷ lệ torque khi một motor bão hòa.

Ví dụ:

```text
Raw:   290, 170, 210, 90
Clamp: 255, 170, 210, 90
```

Correction thực tế không còn đúng với mixer ban đầu.

PX4 xử lý tình huống này bằng control allocation/desaturation; khi lệnh motor vượt khả năng thực hiện, controller điều chỉnh collective hoặc giảm torque được ưu tiên thấp hơn. [PX4 Airmode và mixer saturation](https://docs.px4.io/v1.18/en/config_mc/pid_tuning_guide_multicopter)

Thứ tự ưu tiên phù hợp cho trường hợp của bạn:

1. Giữ Roll/Pitch.
2. Giảm Yaw correction nếu motor bão hòa.
3. Dịch Base lên/xuống nếu còn khoảng.
4. Nếu vẫn vượt, scale đồng đều Roll/Pitch.

Yaw nên bị giảm trước vì mục tiêu chính của đế là giữ ngang.

### 4. Tiếp tục dùng quaternion để ước lượng Roll/Pitch

MPU6050 đủ để kết hợp:

- gyro: phản ứng nhanh nhưng bị drift;
- accelerometer: tham chiếu trọng lực chậm nhưng nhiễu/rung.

Bộ lọc complementary phi tuyến kiểu Mahony phù hợp với IMU giá rẻ và có thể ước lượng gyro bias. [Mahony, Hamel và Pflimlin – nonlinear complementary filter](https://core.ac.uk/download/pdf/52787176.pdf)

Firmware hiện đã dùng quaternion để ước lượng attitude, nên không cần thay toàn bộ. Cần cải thiện:

- chỉ dùng accelerometer correction mạnh khi độ lớn gia tốc gần `1g`;
- giảm ảnh hưởng accelerometer khi motor rung hoặc có gia tốc động;
- dùng gyro Z trực tiếp cho Yaw Rate PID;
- không dùng yaw angle tích phân làm tham chiếu tuyệt đối.

### 5. Chỉnh PID theo đúng thứ tự

Không chỉnh Outer Angle trước. Thứ tự nên là:

1. Rate Roll.
2. Rate Pitch.
3. Rate Yaw.
4. Chạy đồng thời ba Rate controller.
5. Outer Angle Roll.
6. Outer Angle Pitch.
7. Chạy Angle Roll/Pitch + Rate Yaw.

PX4 cũng khuyến nghị chỉnh vòng Rate trước vì mọi vòng phía ngoài đều phụ thuộc nó. [PX4 basic tuning guide](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter_basic)

## Kiến trúc mục tiêu

```text
Quaternion estimator
 ├─ Roll angle ─→ Outer Roll ─→ Roll rate target ─→ Rate PID Roll ─┐
 ├─ Pitch angle → Outer Pitch → Pitch rate target → Rate PID Pitch ├→ Mixer 3 trục
 └─ Gyro Z ─────────────────→ Yaw rate target 0 → Rate PID Yaw ────┘
                                                                  ↓
                                                     Desaturation 0–255
                                                                  ↓
                                                       LF RF LR RR
```

## Có cần thêm phần cứng không?

Với mục tiêu giữ ổn định trên đế ba trục:

```text
ESP32 + MPU6050: đủ
```

Với mục tiêu giữ Yaw đúng một hướng tuyệt đối trong thời gian dài:

```text
ESP32 + MPU6050: không đủ
```

Khi đó cần thêm một tham chiếu ngoài như magnetometer, encoder của đế hoặc hệ thống theo dõi quang học.

Giải pháp nên triển khai cho dự án hiện tại là: thêm Yaw Rate PID mục tiêu `0°/s`, mixer Roll–Pitch–Yaw, và desaturation ưu tiên Roll/Pitch. Đây là thay đổi firmware lẫn app và sẽ cần nạp lại firmware.
