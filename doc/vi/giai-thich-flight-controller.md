# Giải thích mã nguồn FlightController.h cho người mới

File được giải thích: [firmware/DroneMotionMpu6050/FlightController.h](../../firmware/DroneMotionMpu6050/FlightController.h).

Comment trong code dùng tiếng Anh; tài liệu này giải thích bằng tiếng Việt. Nội dung theo phiên bản hiện tại: hai tầng PID, chiều Roll đã đảo ở bộ trộn, không chặn chạy theo độ lớn góc đo. Các ví dụ số dùng để hiểu phép tính, không phải bộ gain đã được xác nhận trên phần cứng.

## 1. File này chịu trách nhiệm gì?

Hãy coi `FlightController.h` là phần **tính toán lệnh điều khiển**. Nó nhận trạng thái drone đã được firmware chuẩn bị, rồi tính ra bốn giá trị duty motor.

```text
Firmware đọc IMU và ước lượng tư thế
          ↓
attitude(): đổi tư thế/gyro sang góc và tốc độ theo trục thân
          ↓
start(): kiểm tra điều kiện nội bộ và bắt đầu một lượt chạy
          ↓ lặp update(dt)
PID góc → tốc độ yêu cầu → PID tốc độ → bộ trộn → duties[4]
          ↓
MotorControl ghi PWM ra phần cứng
```

File này **không tự đọc SPI, không tự nhận lệnh Wi-Fi và không trực tiếp gọi GPIO để quay motor**. Những việc đó nằm trong [firmware chính](../../firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino) và [MotorControl.h](../../firmware/DroneMotionMpu6050/MotorControl.h).

Tách như vậy giúp kiểm thử thuật toán trên máy tính mà không cần motor thật.

## 2. Bản đồ các phần trong file

| Thành phần | Vai trò | Cách đọc |
|---|---|---|
| `#pragma once` | Tránh khai báo lặp khi header được include nhiều lần | Thiết lập đầu file |
| `#include <math.h>` | Cung cấp hàm toán như `asinf`, `atan2f`, `isfinite` | Phục vụ phép đổi góc và kiểm tra dữ liệu |
| `namespace Flight` | Gom các tên thuộc phần điều khiển vào một phạm vi | `Flight::Controller` là lớp `Controller` trong phạm vi này |
| `clamp()` | Giữ số trong một khoảng | Dùng cho giới hạn PID và motor |
| `Gains` | Lưu ba hệ số Kp/Ki/Kd | Các thông số người dùng cấu hình |
| `PID` | Một bộ tính PID có bộ nhớ riêng | Dùng lại cho góc và tốc độ |
| `Controller` | Kết nối các PID với trạng thái drone và bộ trộn | Phần tổ chức toàn bộ vòng điều khiển |

`struct` và `class` đều có thể chứa biến và hàm. Trong file này, `Gains` và `PID` nhóm dữ liệu/phép tính nhỏ; `Controller` tổ chức một bộ điều khiển hoàn chỉnh.

## 3. clamp(): tại sao phải giới hạn một con số?

```cpp
inline float clamp(float v, float lo, float hi) {
  return fmaxf(lo, fminf(hi, v));
}
```

`v` là số cần kiểm tra; `lo`/`hi` là giới hạn dưới/trên. `fminf` chọn số nhỏ hơn, `fmaxf` chọn số lớn hơn.

```text
clamp(12, −40, 40)  = 12
clamp(100, −40, 40) = 40
clamp(−80, −40, 40) = −40
```

Các tầng có giới hạn riêng để sai số lớn không làm lệnh tăng không giới hạn. Cắt một con số trong phần mềm không có nghĩa chuyển động thật đã được bảo đảm nằm trong giới hạn đó.

## 4. Gains: hệ số khác kết quả tính như thế nào?

```cpp
struct Gains {
  float kp = 0;
  float ki = 0;
  float kd = 0;
};
```

`float` là kiểu số có phần thập phân. `kp`, `ki`, `kd` là hệ số do người dùng cấu hình. Ví dụ `kp=0.2` và sai số 10 thì phần P được tính ra là 2.

**`gains.kp` là hệ số; `pid.p` là kết quả ở lần cập nhật hiện tại.** Vì vậy, dòng P trên telemetry có thể lớn hơn Kp rất nhiều mà không phải lỗi hiển thị.

## 5. PID: một bộ tính có bộ nhớ riêng

### 5.1. Các biến trong PID

| Biến | Giữ thông tin gì? | Vì sao cần? |
|---|---|---|
| `gains` | Kp/Ki/Kd của bộ này | Hai tầng có thể dùng hệ số khác nhau |
| `outputLimit` | Giới hạn tổng đầu ra | Chặn độ lớn lệnh từ tầng này |
| `integralLimit` | Giới hạn phần tích lũy I | Tránh I tăng không giới hạn |
| `p`, `i`, `d` | Ba phần đóng góp đang tính | Cộng thành lệnh và gửi về app |
| `previous` | Giá trị **đo** của lần trước | Tính thay đổi của giá trị đo để tạo D |
| `filtered` | Đạo hàm âm sau lọc | Giảm ảnh hưởng nhiễu lên D |
| `previousError` | Sai số của lần trước | Tính tích phân hình thang |
| `initialized` | Đã có mẫu trước hay chưa | Tránh dùng lịch sử chưa tồn tại ở mẫu đầu |

Mỗi đối tượng PID có các biến này riêng. PID góc Roll không dùng chung bộ nhớ tích phân với PID tốc độ Roll.

### 5.2. reset(): xóa lịch sử để bắt đầu lại

`reset()` đưa p/i/d, bộ nhớ giá trị đo/sai số và bộ lọc về 0; đánh dấu chưa có mẫu trước. Nó giữ nguyên gain và giới hạn.

Việc này cần khi đổi gain hoặc bắt đầu lượt chạy mới: không muốn phần I tích lũy từ lượt trước bất ngờ tham gia vào lượt mới.

### 5.3. update(): bốn đối số có nghĩa gì?

```cpp
float update(float target, float measured, float dt, bool integrate)
```

| Đối số | Nghĩa |
|---|---|
| `target` | Giá trị muốn đạt |
| `measured` | Giá trị đang đo |
| `dt` | Thời gian từ lượt trước, đơn vị giây |
| `integrate` | Có cho phép cập nhật I trong lượt này không |

Ở vòng góc, target/measured là độ và output là độ/giây. Ở vòng tốc độ, target/measured là độ/giây và output là hiệu chỉnh duty. Cùng một hàm nhưng vai trò và đơn vị phụ thuộc nơi gọi.

### 5.4. Trình tự tính bên trong PID.update()

**Bước 1 — kiểm tra dữ liệu.** PID yêu cầu dt hữu hạn, lớn hơn 0 và không quá 0.02 giây; target và measured phải hữu hạn. Nếu sai, hàm trả 0. Riêng bộ điều khiển bên ngoài có kiểm tra lỗi và dừng, được giải thích ở mục 12.

**Bước 2 — tính sai số và P.**

```text
error = target − measured
p = kp × error
```

**Bước 3 — chuẩn bị phần I.** Có mẫu trước thì lấy trung bình sai số cũ và mới. Mẫu đầu chỉ dùng sai số mới:

```text
integralError = (error + previousError) / 2
candidate = giới_hạn(i + ki × integralError × dt)
```

`candidate` là phần I mới đang được đề xuất, chưa chắc được lưu vào `i`.

**Bước 4 — tính D từ giá trị đo.**

```text
raw = −(measured − previous) / dt
```

Nếu target đổi đột ngột nhưng measured chưa đổi, D không tự xuất hiện một xung lớn chỉ vì bước nhảy target. Mẫu đầu cho raw=0. Sau đó code lọc raw bằng bộ lọc thông thấp 10 Hz, rồi nhân Kd:

```text
filtered += dt / (dt + 0.0159155) × (raw − filtered)
d = kd × filtered
```

**Bước 5 — quyết định có nhận I mới không.**

```cpp
if (integrate && (fabsf(trial) <= outputLimit || error * trial < 0)) {
  i = candidate;
}
```

`trial` là tổng thử `p + candidate + d`. Điều kiện cho tích lũy khi tổng còn trong biên, hoặc sai số hướng ngược tổng và giúp kéo nó về biên. Khi `integrate=false`, I được giữ nguyên nhưng P/D vẫn cập nhật. Đây là cơ chế **anti-windup**, tức hạn chế tích lũy I khi lệnh đã bị giới hạn.

**Bước 6 — trả tổng đã giới hạn.**

```cpp
return clamp(p + i + d, -outputLimit, outputLimit);
```

Vì chỉ tổng trả về bị cắt, biến `p` riêng vẫn có thể lớn hơn `outputLimit`. Ví dụ P=111.34, I=D=0 và limit=40 thì output=40.

## 6. Controller: nơi ghép hai tầng và các trục

### 6.1. Bốn bộ PID

```cpp
PID rollPID;       // Inner Roll.
PID pitchPID;      // Inner Pitch.
PID rollAnglePID;  // Outer Roll.
PID pitchAnglePID; // Outer Pitch.
```

Hàm khởi tạo `Controller()` đặt hai PID góc thành 2/0/0, output ±45°/s và I ±10°/s. Hai PID tốc độ giữ gain mặc định 0/0/0, output ±40 duty và I ±20 duty từ định nghĩa PID.

Outer mặc định chỉ dùng P. Trục được chọn chưa có Kp vòng tốc độ lớn hơn 0 thì Start bị từ chối.

### 6.2. Các nhóm biến trạng thái

| Nhóm | Biến | Ý nghĩa |
|---|---|---|
| Trạng thái chạy | `active`, `fault` | Có đang chạy không; nhãn trạng thái/lý do lỗi |
| Bộ trộn | `saturated`, `duties[4]` | Có lệnh vượt biên không; bốn duty cuối |
| Đo được | `roll`, `pitch` | Góc thân sau trừ mốc, đơn vị độ |
| Đo được | `rollRate`, `pitchRate` | Tốc độ thân, đơn vị độ/giây |
| Mục tiêu từ người dùng | `rollTarget`, `pitchTarget` | Góc muốn giữ |
| Mục tiêu từ Outer | `rollRateTarget`, `pitchRateTarget` | Tốc độ quay muốn đạt |
| Kết quả Inner | `rollOutput`, `pitchOutput` | Hiệu chỉnh duty trước chiều motor |
| Mốc ngang | `zeroRoll`, `zeroPitch` | Offset góc từ CONTROL ZERO |
| Trục điều khiển | `axis` | 0=BOTH, 1=ROLL, 2=PITCH |
| Riêng tư trong lớp | `base_`, `saturatedFor_` | Ga nền, thời gian bão hòa liên tục |

`public` cho phép firmware đọc/cấu hình qua đối tượng, ví dụ `FlightControl.roll`. Các thành viên sau `private` chỉ được truy cập trực tiếp từ bên trong lớp.

### 6.3. Hai hệ số chiều motor

```cpp
RollMotorDirection = -1.0f;
PitchMotorDirection = 1.0f;
```

Chúng đổi chiều đóng góp khi ra motor, không đổi gain, góc hoặc gyro. Roll=-1 là thay đổi theo phản hồi người dùng rằng trái thấp cho FW Roll dương nhưng mixer cũ làm nghiêng thêm. Pitch=+1 giữ chiều hiện có và cần kiểm tra riêng.

`static constexpr` nghĩa là đây là hằng của lớp, xác định khi biên dịch. Nó không phải ô nhập WPF đang đổi được lúc chạy.

## 7. Controller.stop(): dừng phần tính toán trước

Hàm thực hiện:

1. Đặt `active=false`.
2. Reset cả bốn PID.
3. Xóa tốc độ yêu cầu và output hai trục.
4. Xóa cờ/thời gian bão hòa.
5. Đặt mọi phần tử `duties[]` về 0.

Trong vòng `for (auto &duty : duties)`, dấu `&` làm `duty` tham chiếu trực tiếp phần tử mảng, nên gán `duty=0` sửa đúng phần tử đó.

**Chỉ sửa duties trong RAM chưa tắt PWM phần cứng.** Hàm `stopControl()` trong firmware chính còn gọi `Motors.stop()` để tắt đầu ra. Mốc ZERO, target và gain không bị xóa bởi `Controller.stop()`.

## 8. setGains(): chọn đúng bộ PID để cập nhật

Đối số `which` có bảng riêng, khác với `axis`:

| `which` | Bộ PID được đổi |
|---|---|
| 1 | Tốc độ Roll |
| 2 | Tốc độ Pitch |
| 3 | Góc Roll |
| 4 | Góc Pitch |

Hàm từ chối khi đang `active`, chỉ số sai, gain không hữu hạn hoặc ngoài Kp 0–5, Ki 0–2, Kd 0–0.2. Firmware gọi bên ngoài còn kiểm tra trạng thái Arm và motor để không nhận gain trong trạng thái không phù hợp.

Biểu thức `which == 1 ? rollPID : ...` chọn một PID. Biến `PID &pid` là tham chiếu đến đúng bộ được chọn; gán `pid.gains=g` cập nhật bộ đó, không tạo một bản PID tách rời. Sau khi nhận gain, bộ được chọn được reset lịch sử.

## 9. attitude(): chuyển dữ liệu tư thế vào trục điều khiển

Đầu vào gồm quaternion `w/x/y/z` và gyro `gx/gy` theo radian/giây. Quaternion là cách lưu hướng thân trong không gian; thuật toán Madgwick tạo nó ở firmware chính, không nằm trong header này.

Hàm kiểm tra bình phương độ dài quaternion có trong 0.9–1.1 và gyro có hữu hạn không. Nếu sai, đặt góc thành `NAN`, nghĩa là giá trị không hợp lệ, để Start/update không dùng dữ liệu đó điều khiển.

Với cách gắn board hiện tại:

```text
Roll thân  = âm góc quanh Y cảm biến − zeroRoll
Pitch thân = âm góc quanh X cảm biến − zeroPitch
Roll rate  = −gyroY × hệ số đổi radian sang độ
Pitch rate = −gyroX × hệ số đổi radian sang độ
```

Đổi trục vì hướng đặt cảm biến không trùng cách gọi Roll/Pitch của thân. Trừ ZERO chỉ áp dụng cho góc: tốc độ quay không bị trừ một mốc góc.

`asinf` và `atan2f` đổi quaternion sang góc. Chúng có quy ước/phạm vi góc và điểm kỳ dị; bỏ chặn theo độ nghiêng không biến công thức này thành bộ điều khiển phục hồi được từ mọi tư thế.

## 10. start(): kiểm tra rồi bắt đầu một lượt mới

```cpp
bool start(float base, int selectedAxis)
```

Hàm yêu cầu:

- Chưa active; base hữu hạn và trong 1–180.
- `selectedAxis` từ 0 đến 2.
- Góc, tốc độ và target hợp lệ.
- Kp vòng góc và vòng tốc độ của trục được chọn lớn hơn 0.

Nó không chặn theo độ lớn góc đo. Target vẫn phải nằm trong ±10° qua `validTargets()`.

Nếu đạt, hàm gọi `stop()` để xóa lịch sử lượt trước, lưu base/trục, đặt `active=true` và nhãn `CONTROL_RUNNING`.

**ARM và START là hai lớp xử lý khác nhau.** Header này không tự kiểm tra hạn Arm hoặc kết nối sở hữu; firmware chính kiểm tra các điều kiện đó trước khi cho chạy motor. Giá trị `true` từ hàm này chưa tự ghi một duty ra GPIO.

## 11. validTargets() và fail()

`validTargets()` yêu cầu target Roll/Pitch hữu hạn và trong ±10°. Đây là kiểm tra lệnh mục tiêu, không phải độ nghiêng đo được.

`fail(reason)` gọi `stop()` trước rồi lưu `fault=reason` và trả `false`. Firmware chính nhận `false` để dừng phần cứng và báo lỗi về WPF.

## 12. Controller.update(dt): đọc theo tám bước trong code

### Bước 1 — kiểm tra đầu vào

Nếu chưa active thì trả false. Trong lúc chạy, dt phải từ **0.002 đến 0.02 giây**, tức 2–20 ms; góc/rate phải hữu hạn và target hợp lệ.

Lỗi trả qua `fail()` với nhãn timing, attitude hoặc setpoint. Đây là lớp kiểm tra điều khiển; khác việc `PID.update()` riêng lẻ chỉ trả 0 khi nhận đối số không hợp lệ.

### Bước 2 — quyết định vòng góc có được tích lũy I không

```cpp
const bool rollIntegrate = !saturated && fabsf(rollOutput) < rollPID.outputLimit;
```

Dùng cờ bộ trộn và output của **lượt trước**. Nếu motor hoặc vòng tốc độ đã hết biên, không muốn vòng góc tiếp tục tích lũy I như thể hệ thống còn khả năng làm theo yêu cầu. Pitch tương tự.

### Bước 3 — chạy OUTER

```cpp
rollRateTarget = axis == 2
    ? 0
    : rollAnglePID.update(rollTarget, roll, dt, rollIntegrate);
```

Toán tử `điều_kiện ? A : B` chọn A khi đúng, B khi sai. `axis==2` là chỉ Pitch, nên Roll bị tắt; các chế độ khác chạy vòng góc Roll. Kết quả là tốc độ yêu cầu, không phải duty.

### Bước 4 — chạy INNER

```cpp
rollOutput = axis == 2
    ? 0
    : rollPID.update(rollRateTarget, rollRate, dt, !saturated);
```

Vòng tốc độ nhận kết quả Outer làm target, gyro làm measured. Output là phần sửa duty. Tích lũy I vòng tốc độ được giữ nếu mixer đã bão hòa ở lượt trước.

Pitch chạy cùng cách với điều kiện `axis==1`. Cả hai tầng được tính nối tiếp trong mỗi lượt, ở nhịp mục tiêu 200 Hz do firmware chính tổ chức.

### Bước 5 — áp dụng chiều motor

```text
motorRoll = RollMotorDirection × rollOutput
motorPitch = PitchMotorDirection × pitchOutput
```

Chiều áp dụng sau tổng PID, nên P/I/D đều đi qua cùng quy ước. Không đảo riêng phần P rồi để I/D theo chiều cũ.

### Bước 6 — cộng ga nền và trộn motor

```text
LF = base + motorRoll − motorPitch
RF = base − motorRoll − motorPitch
LR = base + motorRoll + motorPitch
RR = base − motorRoll + motorPitch
```

Ví dụ chỉ Roll, target=0°, góc=+5°, rate=0, Outer Kp=2, Inner Kp=0.2, I/D=0 và base=80:

```text
Sai số góc = −5° → tốc độ yêu cầu = −10°/s
Sai số tốc độ = −10°/s → rollOutput = −2 duty
motorRoll = −1 × (−2) = +2
LF/LR = 82; RF/RR = 78
```

Đây là tăng tương đối phía trái theo mapping đã sửa. Giá trị thật còn phụ thuộc chuyển động gyro, I/D và giới hạn.

### Bước 7 — giới hạn, làm tròn và ghi duties[]

Nếu có `mixed[n]` vượt 0–220, cờ `saturated` được đặt true. Sau đó mỗi lệnh được giới hạn và làm tròn:

```cpp
duties[n] = static_cast<unsigned>(clamp(mixed[n], 0, 220) + 0.5f);
```

Sau clamp số không âm. Cộng 0.5 rồi đổi sang số nguyên không dấu giúp làm tròn đến duty nguyên gần nhất. Vì thế hiệu chỉnh quá nhỏ có thể bị lượng tử hóa thành cùng duty.

`saturated` ở đây chỉ mô tả **mixer**, không báo việc PID tốc độ đã bị cắt ở ±40. P=111.34 vẫn có thể đi qua output=40 và tạo motor 50/130/50/130 với base=90, trong khi mixer chưa vượt 0–220 nên saturated=false.

### Bước 8 — theo dõi bão hòa kéo dài

```text
Nếu saturated: saturatedFor_ += dt
Nếu không:     saturatedFor_ = 0
```

Liên tục quá 0.5 giây thì `fail("CONTROL_SATURATION_LIMIT")`. Nếu mọi bước đạt, update trả true và firmware mới chuyển duties cho MotorControl.

## 13. Điểm nối với file .ino

Trong vòng lặp firmware chính, luồng rút gọn là:

```cpp
FlightControl.attitude(qW, qX, qY, qZ, gx, gy);

if (FlightControl.active) {
  if (!FlightControl.update(dt)) {
    stopControl(FlightControl.fault);
  } else if (!Motors.writeControl(FlightControl.duties)) {
    stopControl("CONTROL_PWM_OR_WATCHDOG_FAULT");
  }
}
```

Header xử lý tính toán; `.ino` quyết định khi nào chạy, xử lý lỗi và đưa lệnh sang phần cứng. MotorControl còn có giám sát riêng cho heartbeat và tuổi lệnh output. Những cơ chế ngoài header này vẫn cần thiết ngay cả khi các phép tính PID đúng.

## 14. Đọc file theo thứ tự nào để bớt rối?

1. Nhìn các nhóm biến trong `Controller` để phân biệt góc đo, mục tiêu và output.
2. Đọc `Controller.update()` từ bước 1 đến 8 để thấy luồng tổng thể.
3. Quay lên `PID.update()` để hiểu một bộ PID tính gì.
4. Đọc `attitude()` để biết góc/gyro được chuyển trục ra sao.
5. Đọc `start()`, `stop()`, `setGains()` để hiểu vòng đời một lượt thử.
6. Cuối cùng đối chiếu `.ino` và MotorControl để hiểu kết nối với phần cứng.

Hướng dẫn các field trên app nằm ở [huong-dan-field-pid-wpf.md](huong-dan-field-pid-wpf.md); ví dụ đồ thị nằm ở [do-thi-pid.md](do-thi-pid.md). Tài liệu này giải thích code hiện có, không xác nhận khả năng cân bằng trên phần cứng ở mọi tư thế.
