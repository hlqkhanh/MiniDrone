# RollCore.h — đọc lõi PID, mixer và phiên điều khiển từ góc nhìn SE

Source: [RollCore.h](../../../firmware/DroneRebuild/RollCore.h). Tài liệu này đi theo các khối trong file, không yêu cầu bạn đã học điều khiển tự động.

## 1. File này làm gì và không làm gì?

Nó nhận cấu hình, mục tiêu, góc/rate và dt; tính correction duty, bốn lệnh motor, flag giới hạn hoặc lỗi. Nó còn định nghĩa state machine quản lý một phiên chạy.

Nó **không đọc IMU, không nhận socket, không gọi LEDC và không tự cho motor quay**. `.ino` tổ chức những phần đó. Đây là lý do cùng header có thể được dùng bởi firmware và mô phỏng/test native.

Các controller có state I/D qua thời gian, nên không phải pure function dù không làm I/O phần cứng. Muốn lặp lại một test phải dùng cùng state ban đầu và cùng chuỗi sample.

## 2. Bản đồ bên trong

| Thành phần | Cách nhìn của SE |
| --- | --- |
| `namespace rebuild` | Nhóm tên thuộc lõi điều khiển |
| `bound()` | Function tiện ích clamp |
| `Config` | DTO cấu hình có validation |
| `Mode`, `Axis` | Enum chọn thuật toán/trục |
| `Output`, `DualOutput` | DTO kết quả |
| `Controller` | Thuật toán có state cho một trục |
| `DualController` | Ghép hai controller và một mixer chung |
| `State`, `Session` | State machine và quyền/thời gian của phiên |

Tên file có chữ Roll nhưng không chỉ điều khiển Roll: có Pitch và phối hợp cả hai. Yaw chưa có trong `Axis` hoặc mixer hiện tại.

## 3. bound(): function nhỏ dùng nhiều lần

```cpp
return x < lo ? lo : x > hi ? hi : x;
```

Nếu x dưới lo thì trả lo; trên hi thì trả hi; còn lại giữ x. Ví dụ `bound(25,-20,20)=20`.

Clamp chỉ giới hạn một con số. Nó không tự chứng minh motor tạo đúng lực hoặc rate thật không vượt giới hạn.

## 4. Config: biết tên field trước khi đọc công thức

| Nhóm | Trường | Ý nghĩa |
| --- | --- | --- |
| Inner PID tốc độ | `kp`, `ki`, `kd` | Gain tạo correction duty từ sai số tốc độ góc |
| Outer PID góc | `angleKp`, `angleKi`, `angleKd` | Gain tạo target rate từ sai số góc |
| Giới hạn | `outputLimit`, `iLimit`, `rateLimit` | Biên correction, biên I Inner, biên target rate |
| Lọc | `dHz` | Tần số parameter cho bộ lọc D |
| Mixer | `base`, `motorSign` | Duty nền và dấu correction của trục |
| Tương thích cũ | `maxAngle`, `maxRate`, `saturationSeconds`, `saturationError` | Còn giữ trong bố cục cấu hình, không dùng để cắt motor theo các ngưỡng đó trong controller hiện tại |

`valid()` kiểm tra mọi field hữu hạn và nằm trong phạm vi source cho phép. Ví dụ sign phải là ±1, iLimit không vượt outputLimit.

**Valid không có nghĩa đã đủ điều kiện chạy.** Config mặc định có gain bằng 0 và vẫn có thể qua kiểm tra phạm vi; `step()` còn yêu cầu Inner Kp>0, ANGLE yêu cầu Angle Kp>0. `.ino` cũng có kiểm tra trước START.

Config là object bên ngoài Controller. `Controller::reset()` xóa state thuật toán, không tự gán các gain của Config về 0.

## 5. Mode và Axis

| Enum | Giá trị | Nghĩa |
| --- | ---: | --- |
| `Mode::Rate` | 0 | Command là target tốc độ °/s, không chạy Outer |
| `Mode::Angle` | 1 | Command là góc mục tiêu °; Outer tạo target rate |
| `Mode::DryRun` | 2 | Trong lõi tính theo target rate; `.ino` không bật xuất motor theo kết quả này |
| `Axis::Roll` | 0 | Trộn correction trái/phải |
| `Axis::Pitch` | 1 | Trộn correction trước/sau |

DryRun không phải “duty luôn 0 trong mọi field của Output”. Lõi vẫn có thể tính array motor; caller mới ngăn xuất PWM thật.

## 6. Output: kết quả không chỉ có bốn motor

| Trường | Nội dung |
| --- | --- |
| `target` | Target rate của Inner, kể cả mode ANGLE |
| `error` | Target rate trừ measured rate |
| `p/i/d` | Đóng góp Inner PID theo duty |
| `raw` | Tổng P+I+D trước outputLimit |
| `u` | Correction đã giới hạn, trước motorSign |
| `motor[4]` | Duty theo LF/RF/LR/RR |
| `pidLimited`, `mixerLimited` | Cờ chạm biên PID/mixer |
| `fault` | Chuỗi mã lỗi, hoặc NONE |

Trong ANGLE, đừng đọc `Output.target` thành góc target của người dùng. Nó đã là tốc độ mà Inner cần bám.

## 7. Vì sao Controller có nhiều variable private?

Một PID cần nhớ quá khứ. Nếu chỉ giữ sai số hiện tại thì không thể tích phân hoặc tính thay đổi số đo giữa hai mẫu.

| State | Vai trò |
| --- | --- |
| `integral_` | Phần I Inner đã có hệ số Ki |
| `previousError_` | Sai số Inner của mẫu trước để lấy trung bình hình thang |
| `previous_` | Rate đo trước đó |
| `derivative_` | State lọc đạo hàm rate |
| `initialized_` | Inner đã có mẫu trước chưa |
| `angleIntegral_`, `previousAngleError_` | I và sai số trước của Outer |
| `previousAngle_`, `angleDerivative_` | Góc trước và state lọc D Outer |
| `angleInitialized_` | Outer đã có mẫu trước chưa |
| `previousMixerLimited_`, `previousInnerLimited_` | Cờ giới hạn trước đó phục vụ anti-windup |

`reset()` dùng `*this=Controller{}` để lấy lại các state mặc định. `mixerFeedback()` cho mixer chung phản hồi một flag giới hạn về controller này.

## 8. Đọc step() theo tám đoạn, không cần hiểu tất cả trong một lần

### 8.1. Đọc interface trước

```text
step(config, mode, command, angle, rate, dt, axis)
```

`angle` tính bằng °, `rate` bằng °/s, dt bằng giây. Axis mặc định là Roll nếu caller không truyền.

`const Config&` không copy cấu hình và không cho sửa qua reference đó. Kết quả `Output` được trả về cho caller quyết định bước tiếp theo.

### 8.2. Validation và lambda fail

Đầu function từ chối config/mode/axis sai, giá trị không hữu hạn, dt ngoài 0.002–0.020 s, gain không đủ và command ngoài biên.

Lambda `fail` reset state rồi trả Output có `fault`. Nó không tự gọi emergency hoặc ghi PWM; `.ino` đọc fault rồi xử lý dừng.

### 8.3. Outer: từ góc tới target rate

Chỉ chạy trong ANGLE:

```text
Sai số góc = command − angle
P góc = angleKp × sai số góc
I góc: tích phân hình thang, có điều kiện cập nhật
D góc: âm thay đổi góc / dt, rồi qua lọc
Target rate = clamp(P góc + I góc + D góc, ±rateLimit)
```

Trong RATE/DryRun, target rate lấy trực tiếp command và state Outer bị xóa.

### 8.4. Inner P: từ sai số rate tới correction tức thời

```text
error = target rate − rate đo
p = kp × error
```

Rate âm không tự yêu cầu P âm: nếu target 0 và measured −10, sai số là +10.

### 8.5. Inner D: thay đổi rate rồi lọc

Code dùng `−(rate−previousRate)/dt`, không trực tiếp lấy đạo hàm sai số target. Mẫu đầu sau reset dùng D thô bằng 0.

`alpha` xác định mức bộ lọc đi theo giá trị mới; `derivative_` là state đã lọc; `o.d=kd×derivative_` là phần đóng góp cuối.

### 8.6. Inner I và anti-windup

`ie` là sai số trung bình của hai mẫu, hoặc sai số hiện tại ở mẫu đầu. `candidate` là I dự kiến đã clamp trong ±iLimit. `trial` là tổng lệnh nếu nhận candidate.

Candidate chỉ được nhận theo điều kiện chống tích lũy quá mức (anti-windup). Nếu không đạt, giữ I cũ. Outer và Inner có cách đặt điều kiện khác nhau, đặc biệt với flag mixer và nhánh cho rút I; xem [công thức chính xác](../PID_FORMULAS_AND_WORKED_EXAMPLES.md).

Điều kiện dựa vào các flag chu kỳ trước vì đó là thông tin sẵn có tại thời điểm đang tính.

### 8.7. Tổng lệnh và mixer

```text
raw = p + i + d
u = clamp(raw, ±outputLimit)
correction có dấu = motorSign × u
```

Roll trộn trái/phải; Pitch trộn trước/sau. Sau đó clamp mỗi motor về 0–255 và làm tròn bằng `floor(x+0.5)`.

Biến local `r` trong step là correction có dấu, không mặc định là Yaw body rate. Tên ngắn phải đọc theo phạm vi function.

### 8.8. Lưu state rồi trả kết quả

Cuối function lưu rate/error/flag giới hạn cho mẫu sau. Không bỏ qua đoạn này khi đọc: nó là lý do lần gọi tiếp theo khác một Controller vừa reset.

## 9. Ví dụ một lần step()

Ví dụ học, không phải cấu hình khuyến nghị: ANGLE target 0°, angle −3°, rate −8°/s, dt=0.005 s; Inner=0.2/0.15/0.001, Outer=1/0/0, Base=110, sign=−1, limit giữ mặc định. Controller vừa reset.

```text
Outer: error góc=3 → target rate=3°/s
Inner: error=3−(−8)=11°/s
P=0.2×11=2.2 duty
I dự kiến=0.15×11×0.005=0.00825 duty
D=0 ở mẫu đầu
Không bão hòa nên nhận I; u=2.20825 duty

correction signed=−2.20825
LF/LR=107.79175 → 108
RF/RR=112.20825 → 112
```

Đó là kết quả tính. Muốn PWM thật xuất ra, phiên và Gate của `.ino` phải cho phép.

## 10. DualController: hai state riêng, một bộ motor chung

`roll_` và `pitch_` là hai Controller độc lập về I/D. `compatible()` yêu cầu config hợp lệ và Base bằng nhau.

`step()` chạy hai trục; một trục lỗi thì trả lỗi và reset cả hai. Nếu thành công, nó tính lại array motor chung:

```text
LF = B + roll + pitch
RF = B − roll + pitch
LR = B + roll − pitch
RR = B − roll − pitch
```

Ở đây roll/pitch đã nhân sign riêng. Nó không cộng hai Base và không cộng hai array motor hoàn chỉnh từ các controller đơn trục.

Cờ mixer chung được trả về cả hai controller qua `mixerFeedback()`. Nếu hai trục cùng làm một motor chạm biên, không thể chỉ nhìn outputLimit riêng để nói mọi lệnh vẫn thực hiện được.

## 11. State và Session: phần quản lý phiên, không phải công thức PID

| State | Ý nghĩa |
| --- | --- |
| Disarmed | Chưa có phiên được chuẩn bị/chạy |
| Armed | Đã nhận quyền chuẩn bị, chờ start hợp lệ |
| Running | Phiên đang thực thi; việc có xuất motor còn phụ thuộc mode/Gate |
| Fault | Session ghi nhận lỗi theo đường kiểm tra của nó |

Các field owner/epoch giống thông tin quyền phiên/thế hệ; armedAt/heartbeatAt/sampleAt/deadline là mốc thời gian. Thời gian trong class dùng microsecond từ caller.

| Function | Công việc |
| --- | --- |
| `arm(client,token,now)` | Kiểm tra epoch, client khác 0 và state cho phép; ghi owner và thời điểm Armed |
| `start(...)` | Yêu cầu Armed, đúng owner/epoch, chưa quá ARM timeout; chuyển Running và lập deadline nếu duration khác 0 |
| `heartbeat()` | Chỉ nhận cập nhật của đúng owner khi Running |
| `check()` | Kiểm tra Armed quá 15 s, deadline, sample quá cũ 100 ms hoặc heartbeat quá 2 s |
| `stop()` | Về Disarmed, bỏ owner, tăng epoch và lưu reason |

Nếu check phát hiện lỗi, nó gọi stop rồi đặt State::Fault. Gate cũng có đường giám sát riêng; đừng mặc định mọi lỗi luôn được hiển thị thành đúng một state giống nhau ở mọi lớp.

`durationMs=0` biểu diễn chạy không có deadline hữu hạn. `.ino` dùng cách này cho START, còn BENCH có thể chọn duration theo nhánh lệnh. “Không có duration” không có nghĩa bỏ heartbeat hoặc kiểm tra sample.

Session không tự reset PID khi arm: caller `.ino` gọi reset riêng. Đây là cách tách phần quản lý phiên khỏi state thuật toán.

## 12. Những câu hỏi tự kiểm tra

- Config.valid() trả true có đủ chạy không? Chưa, còn gain, sample, phiên và điều kiện caller.
- `o.u` có phải duty motor cuối không? Không, còn sign, Base, mixer, clamp/làm tròn và Gate.
- Có I trong một mẫu thì lần sau có tính lại từ 0 không? Không, trừ khi có reset hoặc đường xử lý state tương ứng.
- Class này đọc góc từ sensor không? Không, caller truyền vào.
- File hiện có Yaw PID không? Chưa có.

Đọc tiếp [DroneRebuild.md](DroneRebuild.md) để xem caller sử dụng kết quả; [công thức và nhiều chu kỳ tính](../PID_FORMULAS_AND_WORKED_EXAMPLES.md) để kiểm tra số học. [Mục lục](README.md).
