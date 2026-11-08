# Imu.h — từ byte cảm biến tới góc và tốc độ quay

Source: [Imu.h](../../../firmware/DroneRebuild/Imu.h). Bài này không yêu cầu biết trước quaternion hoặc thanh ghi; bắt đầu từ dữ liệu vào/ra.

## 1. File có ba trách nhiệm, không chỉ đọc cảm biến

```text
Imu                 → giao tiếp SPI, đọc và quy đổi sample
Attitude            → kết hợp gyro/accel để ước lượng hướng thân
RollLevel           → giữ cửa sổ mẫu và bù mốc ZERO

ImuSample           → DTO chứa một sample
```

Theo góc nhìn SE, `Imu` giống adapter phần cứng; `Attitude` là thuật toán có state; `RollLevel` là một cửa sổ dữ liệu có điều kiện hợp lệ. Chúng không tự điều khiển motor.

## 2. Hai cảm biến trong IMU cho biết gì?

| Thành phần | Đo gì? | Không nên hiểu nhầm |
| --- | --- | --- |
| Gyroscope / gyro | Tốc độ quay quanh ba trục | Không trực tiếp đo góc Roll/Pitch |
| Accelerometer | Gia tốc riêng (specific force) theo ba trục | Không trực tiếp cung cấp vị trí hoặc độ cao |

Khi đứng yên, accelerometer giúp nhận biết hướng trọng lực trong hệ sensor. Khi có gia tốc động/rung, số đo không chỉ thay đổi vì thân nghiêng.

Gyro phản ứng với chuyển động quay, nhưng sai lệch nền (bias) sẽ tích lũy thành trôi góc nếu chỉ tích phân. Bộ ước lượng kết hợp hai nguồn thay vì coi một nguồn là hoàn hảo.

## 3. ImuSample là DTO có đơn vị

```cpp
struct ImuSample {
    double ax = 0, ay = 0, az = 0, gx = 0, gy = 0, gz = 0;
};
```

- `ax/ay/az`: gia tốc theo đơn vị g, sau quy đổi từ số nguyên cảm biến.
- `gx/gy/gz`: tốc độ góc theo °/s, sau quy đổi thang đo.
- Trục vẫn thuộc hệ cảm biến; việc tạo rate body nằm ở `.ino`.

`a.gy=10` không phải góc 10°. Nó nghĩa là đang có tốc độ quay khoảng 10°/s theo trục Y sensor trong mẫu đó.

## 4. Thanh ghi (register) có thể hiểu như một bảng key–value nhỏ

Chip IMU có các ô đánh địa chỉ: một số lưu cấu hình, một số lưu state/dữ liệu. ESP32 dùng SPI để đọc/ghi địa chỉ ấy.

Ví dụ trong source:

```text
Đọc địa chỉ thanh ghi 0x75
    → nhận giá trị nhận dạng 0x70 hoặc 0x71
```

`0x75` là địa chỉ thanh ghi; `0x70/0x71` là giá trị mà driver hiện chấp nhận. Đừng nhầm chúng với địa chỉ I²C hoặc số GPIO. Tiền tố `0x` biểu diễn số hệ 16.

## 5. Class Imu: các function đọc/ghi mức thấp

### read(reg, buffer, n)

Mở SPI transaction với parameter trong source, kéo CS xuống để chọn chip, gửi địa chỉ đọc, nhận `n` byte rồi nhả CS. Biểu thức `reg | 0x80` đặt bit báo thao tác đọc theo cách giao tiếp của chip.

CS có thể hình dung như “đang nói chuyện với thiết bị này”; không phải một variable boolean chỉ tồn tại trong RAM. `SPI.transfer()` vừa phát vừa nhận dữ liệu theo clock.

### reg(address)

Wrapper đọc đúng một byte. Nó dùng `read()` để tránh viết lại toàn bộ thủ tục SPI khi chỉ cần một thanh ghi.

### write(address, value, verify)

Gửi địa chỉ và byte cấu hình. Nếu `verify=true`, đọc lại thanh ghi để kiểm tra bằng giá trị đã yêu cầu. Một số thao tác như reset dùng `verify=false`, không giả định bit reset giữ nguyên mãi.

Ba function trên là phần private; caller cấp cao chủ yếu làm việc với `begin()` và `sample()`.

## 6. begin(): khởi tạo và nhận dạng chip

Luồng chính:

1. Cấu hình chân CS và SPI theo Hardware.h.
2. Đọc WHO_AM_I, chấp nhận 0x70 hoặc 0x71 và kiểm tra lặp lại.
3. Reset chip và chờ khoảng khởi động trong source.
4. Ghi cấu hình nguồn, bộ lọc, nhịp sample, thang đo và data-ready.
5. Trả true nếu chuỗi kiểm tra/ghi cấu hình đạt.

Các phép quy đổi phía sau tương ứng cấu hình ±2g và ±500°/s đang dùng. Không thay thang đo mà giữ nguyên hệ số quy đổi rồi kỳ vọng số đo đúng.

Tên dự án/tài liệu có chữ MPU6050 không thay đổi tập chip driver này nhận qua SPI. `identity()` chỉ trả giá trị đã nhận để `.ino` đưa vào INFO.

## 7. sample(): một lần lấy mẫu trả về ba state

| Giá trị | Ý nghĩa | Caller làm gì? |
| --- | --- | --- |
| 1 | Có mẫu mới hợp lệ theo các kiểm tra hiện tại | Xử lý sample |
| 0 | Chưa có data-ready mới | Không coi là đã đọc một mẫu chuyển động mới |
| −1 | Lỗi nhận dạng/dữ liệu theo kiểm tra hiện tại | `.ino` xử lý lỗi cảm biến |

Function đọc 14 byte bắt đầu từ vùng dữ liệu:

| Offset byte trong buffer | Nội dung được dùng |
| --- | --- |
| 0–1, 2–3, 4–5 | Accel X/Y/Z |
| 6–7 | Nhiệt độ, không được đưa vào ImuSample hiện tại |
| 8–9, 10–11, 12–13 | Gyro X/Y/Z |

Lambda `number(i)` ghép byte cao/thấp thành `int16_t`, tức số nguyên 16-bit có dấu. Ví dụ `FF 9C` biểu diễn −100 khi được diễn giải theo kiểu số có dấu phù hợp, không phải một số dương 65436 dùng thẳng.

Source chia accel cho 16384 và gyro cho 65.5:

```text
Accel raw = 16384 → 1 g
Gyro raw = 655   → 10°/s
```

Kiểm tra toàn buffer bằng 0 hoặc toàn 0xFF giúp bắt một số lỗi, không phải phép chứng minh mọi sample còn lại đều chính xác vật lý.

## 8. Bias gyro khác ZERO góc

Ví dụ sensor báo `gyroY=2°/s` dù thân đứng yên. Trung bình nhiều mẫu đứng yên có thể cho bias gần 2°/s. Trừ bias giúp rate về gần 0.

Việc thu 500 mẫu và tính bias nằm trong **controlTask() của .ino**, không tự hoàn tất trong `Imu::begin()`.

ZERO góc lại xử lý một chuyện khác: góc ước lượng là −2° ở tư thế muốn làm mốc thì lưu offset −2°. Sau đó góc đưa vào PID là góc thô trừ offset. Hai phép bù không thay thế nhau.

## 9. Attitude: giữ state quaternion

### Quaternion là gì ở mức cần đọc code?

`w,x,y,z` là bốn số mô tả phép quay 3D, không phải bốn motor hoặc bốn gain. Không đọc trực tiếp `x` là Roll, `y` là Pitch.

Class giữ quaternion qua các mẫu. Các function `roll()/pitch()/yaw()` chuyển state đó thành góc để phần khác dùng.

### initialize(sample)

Dùng hướng accel để ước lượng góc ban đầu rồi tạo quaternion. Đây không phải xác định hướng Bắc hoặc heading tuyệt đối bằng từ kế.

### update(sample, bx, by, bz, dt)

Có thể chia công thức dài thành sáu bước dễ đọc:

1. Trừ bias khỏi gyro và đổi °/s sang rad/s.
2. Tính biến thiên quaternion do gyro.
3. Tính norm accel và kiểm tra giá trị/thời gian cơ bản.
4. Tính hướng hiệu chỉnh Madgwick từ accel.
5. Nhân mức hiệu chỉnh với beta và trọng số tin cậy theo norm accel, rồi tích phân theo dt.
6. Chuẩn hóa quaternion và trả true/false.

Không cần suy ra toàn bộ gradient mới hiểu luồng dữ liệu. Nhóm `sw/sx/sy/sz` thuộc bước hiệu chỉnh; `dw/dx/dy/dz` là biến thiên được dùng để cập nhật state.

### Trọng số accel đang làm gì?

```text
weight = clamp((0.20 − |normAccel−1|) / 0.15, 0, 1)
```

Nếu norm gần 1g trong khoảng lệch tối đa 0.05g, weight bằng 1. Lệch từ 0.05g đến 0.20g thì giảm dần; lệch từ 0.20g trở lên thì weight bằng 0. Đây là heuristic đã có trong source, không phải phát hiện hoàn hảo mọi gia tốc động.

Chuẩn hóa quaternion đưa độ dài về 1, không xóa sai số vật lý trong sensor hoặc tự tạo reference Yaw tuyệt đối.

## 10. Lấy góc body và lấy rate body là hai đường khác nhau

Trong class, state quaternion được cập nhật từ gyro/accel sensor. Khi lấy góc, code sử dụng phép đổi cơ sở tương ứng vector quaternion sensor `(x,y,z)` sang body `(-y,x,z)`.

Trong `.ino`, rate body được tính trực tiếp từ gyro đã trừ bias:

```text
Roll rate = −(gy−biasY)
Pitch rate = gx−biasX
```

Không lấy hai góc Roll liên tiếp rồi chia dt để thay nhánh gyro. Tư thế quaternion và controller PID góc Euler cũng không phải cùng một thuật toán.

## 11. RollLevel: cửa sổ mẫu để lấy mốc

Tên class là RollLevel nhưng có hai object cho Roll/Pitch. Các variable:

- `values_[100]`: vòng đệm 100 góc gần nhất phù hợp.
- `at_`: vị trí ghi tiếp theo.
- `count_`: số mẫu đang có.
- `sum_`: tổng cửa sổ để lấy trung bình nhanh.
- `offset_`: mốc góc đã được xác nhận lấy ZERO.

| Function | Nhiệm vụ |
| --- | --- |
| `observe()` | Nhận mẫu finite, rate không quá 1°/s và norm 0.95–1.05g; không đạt thì xóa cửa sổ |
| `ready()` | Đủ 100 mẫu, trung bình gần ngang trong ±10° và khoảng góc tối đa 0.5° |
| `zero()` | Chỉ khi ready mới lưu trung bình thành offset |
| `angle(raw)` | Trả raw−offset |
| `offset()` | Cho đọc mốc hiện tại |
| `resetWindow()` | Xóa mẫu cửa sổ, giữ offset |
| `reset()` | Xóa cả cửa sổ và offset |

Ngoài class này, `.ino` còn kiểm tra đứng yên ở các gyro, thời gian và state dừng trước khi nhận ZERO. Vì vậy `zero()` không phải toàn bộ điều kiện của lệnh ZERO.

Ví dụ offset=−2°, raw=−1° thì góc sau bù là `−1−(−2)=+1°`. ZERO không làm sensor ngừng đo và không đổi chiều gyro.

## 12. Tự kiểm tra đã hiểu chưa

- `sample()` trả 0 có phải IMU hỏng không? Không, có thể chỉ chưa có mẫu mới.
- Gyro rate 0 có nghĩa góc 0 không? Không, thân có thể đang đứng yên ở góc nghiêng.
- Xóa cửa sổ mẫu có luôn xóa ZERO không? Không, resetWindow giữ offset.
- Có quaternion có nghĩa đã điều khiển Yaw không? Không, ước lượng và điều khiển là hai lớp khác nhau.

Đọc tiếp [RollCore.md](RollCore.md) để xem controller dùng góc/rate này thế nào. [Mục lục](README.md).
