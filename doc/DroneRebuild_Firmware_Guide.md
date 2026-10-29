# Hướng dẫn đọc firmware DroneRebuild: Inner PID, motor, IMU và luồng điều khiển

> Tài liệu dành cho người mới, dựa trên đoạn `RollCore.h` đã được gửi trong cuộc trò chuyện và nội dung `DroneRebuild.ino` được cung cấp.
>
> **Lưu ý về nguồn:** Phần người dùng ghi là `Imu.h` thực tế lại có nội dung trùng với `RollCore.h` (PID, mixer và Session), không phải mã đọc IMU. Vì vậy, tài liệu giải thích chính xác những gì `DroneRebuild.ino` gọi từ `Imu.h`, nhưng không tự đoán cách SPI, thanh ghi cảm biến, quaternion hoặc Madgwick được triển khai bên trong `Imu.h`. Muốn giải thích chính xác phần đó, cần nội dung thật của `Imu.h`.

---

## 0. Bức tranh tổng thể

Firmware có hai luồng công việc chính:

1. **`controlTask()`**: đọc IMU, hiệu chuẩn gyro, cập nhật tư thế, chạy PID, tạo lệnh motor và phát dữ liệu telemetry.
2. **`guard()`**: chạy định kỳ để kiểm tra điều kiện an toàn và là nơi duy nhất ghi PWM ra motor.

Ngoài ra, **`loop()`** xử lý giao tiếp USB/TCP, nhận lệnh, gửi phản hồi và telemetry.

Luồng điều khiển khái quát:

```text
IMU sample
   ↓
Hiệu chuẩn gyro (khi chưa hiệu chuẩn)
   ↓
Ước lượng tư thế: attitude.update(...)
   ↓
Tính góc Roll/Pitch và tốc độ xoay
   ↓
Controller.step(...)
   ├─ Angle mode: góc → rate target
   └─ Rate mode: command → rate target
   ↓
Rate PID: P + I + D → output.u
   ↓
Mixer → output.motor[4]
   ↓
controlTask publish lệnh vào gate
   ↓
guard() ghi PWM hoặc ghi 0 nếu bị khóa
```

Đừng nhầm các bước này:
- **PID output** là kết quả tính toán điều khiển.
- **Motor command/duty** là lệnh được tạo từ PID và mixer.
- **PWM output** là tín hiệu thực sự được ghi ra chân phần cứng.
- **Tốc độ quay motor** là phản ứng vật lý, không bằng trực tiếp con số duty.

---

## 1. Inner PID: từ sai số tốc độ đến đầu ra điều khiển

Trong `RollCore.h`, sau khi đã có `o.target`, vòng Rate luôn chạy:

```cpp
o.error = o.target - rate;
o.p = c.kp * o.error;
```

### 1.1 Sai số tốc độ

- `o.target`: tốc độ xoay mục tiêu.
- `rate`: tốc độ xoay hiện tại lấy từ gyro sau khi trừ bias và áp dụng dấu trục.
- `o.error`: chênh lệch giữa mục tiêu và thực tế.

Công thức:

\[
e_r = target - rate
\]

Ví dụ `target = -20°/s`, `rate = -5°/s`:

\[
e_r=-20-(-5)=-15°/s
\]

Drone chưa đạt tốc độ mục tiêu.

### 1.2 Thành phần P

```cpp
o.p = c.kp * o.error;
```

\[
P=K_p e_r
\]

Nếu `kp = 0.5`, `error = -15`, thì `P = -7.5` (đơn vị vật lý chính xác của đầu ra cần được xác định theo cách phần cứng sử dụng lệnh; không nên tự gọi nó là duty PWM).

### 1.3 Thành phần D

```cpp
const double rawD = initialized_ ? -(rate - previous_) / dt : 0;
const double alpha = dt / (dt + 1 / (6.283185307179586 * c.dHz));
derivative_ += alpha * (rawD - derivative_);
o.d = c.kd * derivative_;
```

D dựa trên **tốc độ thay đổi của `rate`**:
\[
D_{raw}=-\frac{rate-rate_{previous}}{dt}
\]

Sau đó code lọc D:
\[
D_f \leftarrow D_f+\alpha(D_{raw}-D_f)
\]
\[
D=K_dD_f
\]

`dHz` điều chỉnh tần số cắt của bộ lọc D. Bộ lọc giúp làm mượt tín hiệu D, vì đạo hàm có thể khuếch đại nhiễu cảm biến.

Lần đầu chưa có mẫu trước, nên `rawD = 0`.

### 1.4 Thành phần I

```cpp
const double ie = initialized_
    ? (o.error + previousError_) * 0.5
    : o.error;

const double candidate = bound(
    integral_ + c.ki * ie * dt,
    -c.iLimit,
    c.iLimit
);
```

Công thức gần đúng:
\[
I_{candidate}=bound(I_{old}+K_i e_{avg}dt,-iLimit,+iLimit)
\]

`ie` là trung bình sai số hiện tại và trước đó (tích phân hình thang). `candidate` chỉ là I dự kiến; code chưa chắc chấp nhận nó.

### 1.5 Tổng PID và giới hạn đầu ra

```cpp
const double trial = o.p + candidate + o.d;

if (!previousMixerLimited_ &&
    (std::fabs(trial) <= c.outputLimit || o.error * trial < 0))
    integral_ = candidate;

o.i = integral_;
o.raw = o.p + o.i + o.d;
o.u = bound(o.raw, -c.outputLimit, c.outputLimit);
o.pidLimited = std::fabs(o.raw) >= c.outputLimit;
```

- `trial`: đầu ra giả định nếu dùng I mới.
- `integral_`: chỉ cập nhật khi điều kiện chống windup cho phép.
- `o.raw`: tổng P + I + D trước giới hạn.
- `o.u`: tổng sau khi giới hạn trong `[-outputLimit, +outputLimit]`.
- `pidLimited`: đánh dấu `raw` đã chạm/vượt giới hạn.

Ví dụ nếu `raw = -25` và `outputLimit = 20`, thì `u = -20`.

**Điều cần nhớ:** `outputLimit` giới hạn kết quả PID bên trong. Nó không phải giới hạn trực tiếp cho duty từng motor.

---

## 2. Windup và các giới hạn

### 2.1 Windup là gì?

Nếu hệ thống còn sai số nhưng motor đã chạm giới hạn, I có thể tiếp tục tích lũy. Khi hệ thống thoát giới hạn, I lớn còn giữ lại có thể khiến điều khiển quá mạnh.

### 2.2 Code giảm nguy cơ windup ra sao?

Trong vòng Rate:
- `iLimit` giới hạn độ lớn của I dự kiến.
- `previousMixerLimited_` cho biết mixer ở mẫu trước có bị bão hòa không.
- Nếu mixer mẫu trước bị giới hạn, điều kiện cập nhật I trong vòng Rate không cho chấp nhận `candidate`.
- Nếu không bị giới hạn, I được chấp nhận khi đầu ra thử nằm trong giới hạn hoặc `error * trial < 0`.

Trong vòng Angle, code cũng dùng `previousInnerLimited_`, `previousMixerLimited_`, `rateLimit` và hướng sai số để quyết định có cập nhật `angleIntegral_` không.

Đây là cơ chế chống windup được triển khai trong code; nó dùng trạng thái giới hạn của chu kỳ trước, nên không nên diễn giải như thể nó dự đoán hoàn hảo mọi tình huống bão hòa.

### 2.3 Hai loại giới hạn khác nhau

| Giới hạn | Ý nghĩa |
|---|---|
| `iLimit` | Giới hạn tích phân vòng Rate |
| `outputLimit` | Giới hạn đầu ra Rate PID |
| `rateLimit` | Giới hạn tốc độ mục tiêu được tạo bởi vòng Angle |
| `0..220` | Giới hạn từng lệnh motor sau mixer |

`pidLimited` và `mixerLimited` cũng khác nhau:
- `pidLimited`: tổng PID `raw` chạm/vượt `outputLimit`.
- `mixerLimited`: ít nhất một giá trị `mixed[n]` nằm ngoài `0..220` trước khi clamp.

---

## 3. Mixer Roll/Pitch

Code:

```cpp
const double r = c.motorSign * o.u;

const double mixed[] = {
    c.base + r,
    c.base + (axis == Axis::Roll ? -r : r),
    c.base + (axis == Axis::Roll ? r : -r),
    c.base - r
};
```

`r` là tác động PID đã nhân dấu cấu hình. `base` là lệnh nền được cộng vào từng motor. Mixer tạo bốn lệnh khác nhau để tạo mô-men quanh trục được chọn.

### Roll

| Motor theo chỉ số mảng | Công thức |
|---|---|
| `motor[0]` | `base + r` |
| `motor[1]` | `base - r` |
| `motor[2]` | `base + r` |
| `motor[3]` | `base - r` |

### Pitch

| Motor theo chỉ số mảng | Công thức |
|---|---|
| `motor[0]` | `base + r` |
| `motor[1]` | `base + r` |
| `motor[2]` | `base - r` |
| `motor[3]` | `base - r` |

Sau đó:

```cpp
o.mixerLimited |= mixed[n] < 0 || mixed[n] > 220;
o.motor[n] = static_cast<unsigned>(
    std::floor(bound(mixed[n], 0, 220) + 0.5)
);
```

Mỗi lệnh được giới hạn `0..220` rồi làm tròn đến số nguyên gần nhất.

Ví dụ `base = 80`, `motorSign = 1`, `u = 10`, `axis = Roll`:
- `r = 10`
- motor = `[90, 70, 90, 70]`

Đây là giá trị lệnh theo công thức trong code, không phải phép đo lực hay tốc độ motor. Muốn biết chỉ số 0–3 tương ứng LF/RF/LR/RR cần xem `Hardware.h` hoặc mapping phần cứng; không thể suy ra chắc chắn chỉ từ `RollCore.h`.

`motorSign` chỉ có hai giá trị hợp lệ `+1` hoặc `-1` theo `Config::valid()`. Dấu đúng phải được kiểm tra trên cấu hình và bố trí thực tế. Dấu sai có thể khiến phản hồi thành phản hồi dương và làm drone nghiêng mạnh hơn thay vì sửa nghiêng.

---

## 4. Từ lệnh motor đến PWM thật

Trong `DroneRebuild.ino`, `setup()` thiết lập PWM:

```cpp
for (int pin : hw::motors) {
    pwmReady = ledcAttach(pin, 20000, 8) && pwmReady;
    ledcWrite(pin, 0);
}
```

Theo đoạn code:
- `hw::motors`: các chân motor lấy từ `Hardware.h`.
- `20000`: tần số PWM được yêu cầu là 20 kHz.
- `8`: độ phân giải 8 bit.
- Ban đầu, mỗi chân được ghi giá trị 0.
- `pwmReady` thể hiện các lệnh attach có thành công hay không.

Với độ phân giải 8 bit, dải số LEDC thường là 0–255. Tuy nhiên, mixer đang giới hạn lệnh ở 0–220. Phải xác nhận API/driver và phần cứng ESC/motor để biết chính xác tín hiệu đó được diễn giải ra sao.

### Một luồng dữ liệu quan trọng

Trong `controlTask()`, hàm `publish()` **không ghi PWM trực tiếp**. Nó sao chép các lệnh vào `gate.duty[]` nếu epoch hợp lệ và gate chưa bị dừng:

```cpp
gate.enabled = enable;
gate.deadline = session.deadline;
gate.hb = session.heartbeatAt;
gate.sample = session.sampleAt;
for (int k = 0; k < 4; k++)
    gate.duty[k] = bench ? ... : output.motor[k];
```

Sau đó `guard()` là nơi duy nhất ghi PWM:

```cpp
for (int k = 0; k < 4; k++)
    ok = ledcWrite(
        hw::motors[k],
        g.enabled && !g.stopped ? g.duty[k] : 0
    ) && ok;
```

Nghĩa là:
- Nếu gate được bật và chưa stopped: ghi duty đã công bố.
- Nếu không: ghi 0.
- Nếu `ledcWrite` lỗi: đặt `pwmReady = false` và emergency với lý do `PWM`.

Vì vậy, PID tính ra lệnh khác 0 **không bảo đảm motor nhận lệnh khác 0**.

---

## 5. Gate bảo vệ: vì sao lệnh có thể bị chặn?

`Gate` chứa:
- `stopped`: đầu ra bị dừng.
- `enabled`: cho phép xuất lệnh.
- `epoch`: phiên bản điều khiển để loại bỏ lệnh/phiên cũ.
- `deadline`: hạn chót chạy hữu hạn nếu có.
- `hb`: thời điểm heartbeat gần nhất.
- `sample`: thời điểm mẫu điều khiển gần nhất.
- `duty[4]`: lệnh motor được công bố.
- `reason`: lý do gần nhất.

### 5.1 `emergency()`

```cpp
gate.stopped = true;
gate.enabled = false;
gate.epoch++;
gate.reason = why;
```

Hàm này khóa gate, tăng epoch và ghi lý do. Khi `guard()` chạy, vì gate bị khóa nên nó ghi 0 cho cả bốn motor.

### 5.2 `guard()` kiểm tra gì?

```cpp
if (!g.stopped && g.enabled &&
    ((g.deadline && now >= g.deadline) ||
     now - g.sample >= 100000 ||
     now - g.hb >= 2000000)) {
    emergency(...);
}
```

Nếu gate đang hoạt động, nó sẽ dừng khi:
- Có deadline và đã đến hạn (`DURATION`).
- Không có mẫu điều khiển mới trong 100.000 microsecond = 100 ms (`STALE_CONTROL`).
- Heartbeat cũ ít nhất 2.000.000 microsecond = 2 giây (`HEARTBEAT`).

Timer được đặt chạy mỗi 5.000 microsecond = 5 ms. Khi điều kiện lỗi được phát hiện, `guard()` gọi `emergency()` rồi ghi 0 PWM trong lượt xử lý đó.

`sample` và `hb` khác nhau:
- **Sample freshness** cho biết dữ liệu điều khiển có được cập nhật đều không.
- **Heartbeat** cho biết phía điều khiển/client còn gửi keepalive không.

Heartbeat mới không thay thế cho mẫu IMU mới. Code còn ghi chú rõ rằng heartbeat được publish mà không làm mới thời điểm sample.

---

## 6. Trạng thái DISARMED → ARMED → RUNNING → FAULT

Trong `RollCore.h` có `Session`; trong `.ino` có `Gate`. Hai thứ liên quan nhưng không giống nhau:
- `Session` theo dõi logic phiên ở `controlTask`.
- `Gate` là cơ chế chặn/cho phép xuất PWM.

### 6.1 ARM

Lệnh dạng `ARM epoch` chỉ thành công khi:
- `pwmReady` đúng.
- Token/epoch khớp.
- Client ID hợp lệ.
- Session đang `Disarmed` hoặc `Fault`.

Khi arm thành công, controller được reset và gate được đặt `stopped = false`, nhưng `enabled = false`. Do đó **ARM không tự bật motor**.

### 6.2 START

Lệnh `START` chỉ được chấp nhận khi các điều kiện trong `.ino` thỏa:
- IMU đã calibrated.
- Trục đã verified.
- Cấu hình hợp lệ, `kp > 0`.
- Mode và target hợp lệ.
- Với Angle mode, `angleKp > 0`.
- `session.start(...)` thành công.

Code có ghi chú rằng đối số duration cũ vẫn giữ trong giao thức, nhưng START hiện gọi `session.start(..., 0)`, nghĩa là chạy liên tục cho đến khi có điều kiện dừng.

Sau khi START thành công, `mode`, `target` được đặt và controller reset.

### 6.3 STOP và FAULT

Lệnh `STOP` gọi `emergency("STOP")`. Điều này khóa gate ngay ở mức logic, tăng epoch và khiến guard ghi 0.

`Session::check()` có thể phát hiện:
- `ARM_TIMEOUT`: ở ARMED quá 15 giây.
- `DURATION`: deadline đã tới.
- `STALE_CONTROL`: mẫu điều khiển cũ.
- `HEARTBEAT`: heartbeat cũ.

Khi có lỗi, Session dừng rồi đặt state thành `Fault`. Phía `.ino` cũng đồng bộ epoch và reset controller khi phát hiện gate epoch thay đổi.

### 6.4 BENCH

`BENCH` là đường thử riêng:
- Yêu cầu `props == true` (được bật bởi lệnh `PROPS_REMOVED 1`).
- Motor phải trong phạm vi 0–3.
- Duty không quá 89.
- Duration từ 1 đến 2000 ms.
- Phiên start phải hợp lệ.

Trong chế độ bench, `publish()` chỉ cấp duty cho motor được chọn và duty 0 cho ba motor còn lại. Tên `PROPS_REMOVED` cho thấy thiết kế yêu cầu tháo cánh trước khi thử; luôn xác minh an toàn vật lý trước khi cấp nguồn.

---

## 7. Đọc IMU trong `.ino`: phần có thể xác nhận từ code

Đầu tiên:

```cpp
Imu imu;
Attitude attitude;
RollLevel levels[2];
bool available = imu.begin();
```

- `Imu`: lớp đọc mẫu cảm biến.
- `Attitude`: lớp ước lượng tư thế.
- `RollLevel levels[2]`: lưu mốc cân bằng riêng cho hai trục.
- `imu.begin()`: khởi tạo IMU; chi tiết bus/SPI và cấu hình cảm biến nằm trong `Imu.h` thật, hiện chưa được cung cấp.

Đọc mẫu:

```cpp
int got = available ? imu.sample(a) : -1;
```

Theo cách dùng trong `.ino`:
- `got < 0`: lỗi đọc; nếu đang Running thì emergency `IMU`, đặt `available = false`, `calibrated = false`.
- `got == 0`: chưa có mẫu mới; tăng `missed`.
- `got == 1`: có mẫu mới và xử lý tiếp.

Các trường được sử dụng gồm `ax, ay, az, gx, gy, gz`. Code xử lý chúng như gia tốc ba trục và tốc độ góc ba trục, nhưng đơn vị và hệ số chuyển đổi chính xác cần xem `Imu.h`.

### 7.1 Tính dt

```cpp
dt = lastSample ? double(now - lastSample) * 1e-6 : .005;
lastSample = now;
```

`esp_timer_get_time()` được dùng như thời gian microsecond. Nhân `1e-6` chuyển microsecond thành giây.

Ví dụ chênh lệch 5000 microsecond:
\[
dt=5000\times10^{-6}=0.005s
\]

Tức 5 ms, tương ứng 200 mẫu/giây nếu chu kỳ đều.

Nếu `dt > .0075` (7.5 ms), code tăng bộ đếm `late`. `RollCore.h` còn từ chối dt nhỏ hơn 2 ms hoặc lớn hơn 20 ms.

---

## 8. Hiệu chuẩn gyro: code đang làm gì?

Khi chưa calibrated và chưa quá 30 giây kể từ `calibrationStart`, code kiểm tra mẫu có ổn định không.

```cpp
double norm = sqrt(a.ax*a.ax + a.ay*a.ay + a.az*a.az);

bool stable =
    norm > .85 && norm < 1.15 &&
    fabs(a.gx) < 15 &&
    fabs(a.gy) < 15 &&
    fabs(a.gz) < 15;
```

Điều kiện:
- Độ lớn vector gia tốc nằm trong 0.85–1.15 (có vẻ giả định đơn vị gần g, nhưng cần kiểm tra `Imu.h` để xác nhận).
- Mỗi trục gyro có trị tuyệt đối dưới 15 (đơn vị cần xác nhận).
- Nếu đã có mẫu trong cửa sổ, độ dao động của từng kênh bị giới hạn: 0.08 cho accel, 2.0 cho gyro.

Nếu mẫu không ổn định, `count` và tổng gyro bị reset. Nếu ổn định, code cộng `gx, gy, gz` vào `sum[]` và tăng `count`.

Khi có ít nhất 500 mẫu ổn định:

```cpp
bias[k] = sum[k] / count;
attitude.initialize(a);
calibrated = true;
```

Tức bias gyro là trung bình của 500 hoặc nhiều mẫu ổn định trong cửa sổ đó.

**Lưu ý:** code chỉ cộng gyro vào `sum`; không lấy trung bình accelerometer ở đây. Việc xử lý gia tốc ban đầu được giao cho `attitude.initialize(a)`.

### Trừ bias

Sau khi calibrated:

```cpp
rates[0] = hw::rollSensorSign * (a.gy - bias[1]);
rates[1] = a.gx - bias[0];
```

- Rate Roll lấy `gy` trừ `bias[1]`, rồi nhân `hw::rollSensorSign`.
- Rate Pitch lấy `gx` trừ `bias[0]`.

Điều này cho thấy firmware ánh xạ trục cảm biến sang trục điều khiển bằng dấu và lựa chọn trục. Không nên tự kết luận dấu đúng nếu chưa kiểm tra hướng lắp cảm biến và `Hardware.h`.

---

## 9. Ước lượng tư thế và lấy mốc Roll/Pitch

Khi calibrated:

```cpp
if (!attitude.update(a, bias[0], bias[1], bias[2], dt)) {
    calibrated = false;
    emergency("ESTIMATOR_TIMING");
}
```

`.ino` giao cho `Attitude::update()` việc cập nhật tư thế bằng mẫu IMU, bias gyro và `dt`. Cách kết hợp accel/gyro, quaternion, Madgwick hoặc cách khác **không thể xác nhận từ `.ino` riêng lẻ**; cần phần định nghĩa `Attitude` trong `Imu.h`.

Sau đó:

```cpp
double raw[] = {attitude.roll(), attitude.pitch()};
```

Lấy góc Roll/Pitch thô từ estimator.

`RollLevel` tạo mốc cân bằng:

```cpp
levels[k].observe(raw[k], rates[k], norm);
angles[k] = levels[k].angle(raw[k]);
```

Việc `observe()` chỉ được gọi khi:
- Đã calibrated.
- `dt` nằm trong 2–20 ms.
- Gyro đã trừ bias gần đứng yên (`|gx-bias[0]| <= 1`, tương tự gy, gz).

Nếu không thỏa, cửa sổ quan sát bị reset.

Lệnh `ZERO_ROLL` / `ZERO_PITCH` chỉ được chấp nhận khi đã calibrated, có mẫu gần đây, gate đang dừng và `levels[k].zero()` thành công. Sau khi zero, góc được tính lại và `verified` bị đặt false, do đó cần verify lại trước khi START.

Phân biệt:
- **Gyro calibration**: ước lượng bias gyro.
- **ZERO_ROLL/PITCH**: đặt mốc góc cân bằng cho trục.
- **Yaw display offset**: không có đủ mã ở đây để kết luận cách triển khai.

---

## 10. Nhịp điều khiển và telemetry

Task được tạo với tên `roll200Hz`:

```cpp
xTaskCreatePinnedToCore(controlTask, "roll200Hz", ..., 4, ..., 1);
```

Cuối vòng lặp:

```cpp
vTaskDelayUntil(&wake, pdMS_TO_TICKS(5));
```

Mục tiêu chu kỳ là 5 ms, tức khoảng 200 Hz. Đây là nhịp task dự kiến, không bảo đảm mỗi lần đọc IMU đều có mẫu mới đúng 5 ms; `got == 0`, `missed` và `late` giúp theo dõi tình trạng này.

Telemetry được phát theo `telem` 50 hoặc 100 Hz. Code tạo dòng `T,...` chứa nhiều trường, trong đó có:
- sequence và timestamp;
- trạng thái và epoch;
- mode, count;
- raw accel/gyro;
- góc và rate đang chọn;
- target, error, P/I/D, raw, u;
- duty bốn motor;
- cờ pid/mixer limited;
- dt, missed, late, số bản tin bị drop;
- lý do gate, calibrated, verified, props;
- các hệ số PID và giá trị góc/rate Roll/Pitch/Yaw.

Để phân tích log chính xác, phải dùng đúng thứ tự trường trong chuỗi `snprintf`, không đoán vị trí dựa trên tên.

---

## 11. Vai trò của `setup()` và `loop()`

### `setup()`
1. Đặt các chân motor OUTPUT và LOW.
2. Khởi tạo Serial.
3. Gắn PWM LEDC 20 kHz, 8-bit và ghi 0.
4. Tạo queues cho commands, replies và samples.
5. Khởi động Wi-Fi AP tên `DroneRebuild`.
6. Khởi động server TCP cổng 3333.
7. Tạo timer `guard` chạy mỗi 5 ms.
8. Tạo task `controlTask()`.

Nếu tạo queue, timer hoặc task thất bại, code đánh dấu lỗi/đưa output về trạng thái an toàn.

### `loop()`
- Phát hiện TCP client ngắt kết nối và gọi emergency.
- Chấp nhận client mới khi điều kiện cho phép.
- Đọc dòng lệnh từ USB và TCP.
- Lệnh được định dạng `ID COMMAND`.
- `STOP` gọi emergency ngay khi nhận được, trước cả lúc command được xử lý trong task.
- Gửi reply/telemetry mà không để luồng gửi chậm chặn control task vô hạn.
- Nếu TCP gửi lỗi hoặc bị backpressure, kết nối bị đóng và emergency được gọi trong các trường hợp tương ứng.

`controlTask()` làm điều khiển; `loop()` chủ yếu làm giao tiếp; `guard()` bảo vệ đầu ra PWM.

---

## 12. Lệnh chính và điều kiện đáng chú ý

| Lệnh | Tác dụng theo code |
|---|---|
| `INFO` | Trả thông tin build, IMU, IP, bias và PWM ready; thêm thông tin level |
| `STOP` | Khóa output gate |
| `KEEPALIVE` | Cập nhật heartbeat nếu đúng owner và đang Running |
| `ARM epoch` | Arm nếu PWM sẵn sàng và epoch/client hợp lệ |
| `START epoch mode target duration` | Bắt đầu điều khiển nếu calibration, verify và config hợp lệ; code hiện truyền duration 0 để chạy liên tục |
| `BENCH epoch motor duty duration` | Chạy thử một motor theo điều kiện an toàn trong code |
| `CALIBRATE` | Reset cửa sổ level và bắt đầu lại quá trình hiệu chuẩn |
| `ZERO_ROLL` / `ZERO_PITCH` | Lấy mốc cân bằng trục khi điều kiện hợp lệ |
| `AXIS 0/1` | Chọn Roll/Pitch, reset controller và yêu cầu verify lại |
| `VERIFY 0/1` | Đặt cờ verified |
| `PROPS_REMOVED 0/1` | Đặt cờ báo cánh đã tháo/không tháo |
| `TELEMETRY 50/100` | Chọn tần số telemetry |
| `CONFIG ...` | Cập nhật một nhóm hệ số Rate PID, angleKp, base, motorSign, outputLimit |
| `CONFIG_CASCADE ...` | Cập nhật thêm angleKi/angleKd và các tham số cascade |

Các lệnh config chỉ được xử lý trong trạng thái Disarmed hoặc Fault theo nhánh điều kiện của code.

---

## 13. Cách đọc một mẫu telemetry

Ví dụ giả định:

```text
target = -10
rate   = -4
error  = -6
p      = -3
i      = -0.5
d      = 0.2
raw    = -3.3
u      = -3.3
```

Cách đọc:
1. Vòng điều khiển muốn tốc độ −10.
2. Gyro báo tốc độ −4.
3. Sai số = −10 − (−4) = −6.
4. P/I/D được cộng thành `raw = -3.3`.
5. Vì `outputLimit` chưa bị chạm, `u = raw`.
6. Mixer cộng/trừ `r` với `base` để tạo bốn lệnh.
7. Gate có thể xuất lệnh hoặc chặn và ghi 0 tùy trạng thái, freshness, deadline và heartbeat.

Để biết motor thực sự nhận lệnh gì, phải so sánh duty trong telemetry với `gate.enabled`, `gate.stopped`, trạng thái và `reason`.

---

## 14. Chỉnh PID và kiểm thử: nguyên tắc an toàn

Không chỉnh PID bay thật chỉ dựa vào một ví dụ số. Trước tiên:
1. Xác minh thứ tự motor, hướng quay, dấu gyro và `motorSign`.
2. Kiểm tra output ở trạng thái disarmed; xác nhận PWM bằng 0.
3. Dùng mô phỏng hoặc test không cánh quạt trước.
4. Theo dõi target, rate, error, P/I/D, raw, u, duty, `pidLimited`, `mixerLimited`, dt, missed và late.
5. Chỉnh vòng Rate trước, rồi mới chỉnh vòng Angle.
6. Nếu rung, đảo chiều, tăng dao động hoặc motor không phản ứng như dự kiến, dừng kiểm thử và kiểm tra dấu/mapping trước khi tăng gain.

`BENCH` trong code chỉ cho phép duty tối đa 89 và tối đa 2 giây, nhưng điều đó không thay thế quy trình an toàn vật lý. Tháo cánh quạt, cố định khung, giữ người tránh xa vùng motor và chuẩn bị ngắt nguồn.

### Các giới hạn cần hiểu rõ
- `maxAngle`, `maxRate`, `saturationSeconds`, `saturationError` được giữ trong cấu hình tương thích nhưng không được sử dụng trong đoạn PID/mixer được cung cấp.
- `DryRun` vẫn gọi `Controller::step()` và tính motor; việc không xuất PWM dựa vào `publish(mode != Mode::DryRun)`, không phải do PID tự ngừng tính.
- `publish()` chỉ cập nhật gate khi `gate.epoch == session.epoch && !gate.stopped`.
- Đầu ra PWM cuối cùng được viết ở `guard()`, không phải ngay trong `Controller::step()`.

---

## 15. Phần còn thiếu để hoàn tất việc đọc IMU

Đoạn được gửi dưới tên `Imu.h` thực tế là code `RollCore.h` lặp lại. Để giải thích chính xác các mục sau, cần file `firmware/DroneRebuild/Imu.h` thật:
- chip IMU và cấu hình SPI/I2C;
- thanh ghi và cách chuyển đổi số thô sang đơn vị vật lý;
- đơn vị của `ax/ay/az` và `gx/gy/gz`;
- định nghĩa `Imu::begin()` và `Imu::sample()`;
- định nghĩa `Attitude`, quaternion và thuật toán cập nhật;
- định nghĩa `RollLevel`, ngưỡng zero và cách tính offset.

Không nên suy ra các chi tiết này chỉ từ tên lớp hoặc lời gọi hàm trong `.ino`.
