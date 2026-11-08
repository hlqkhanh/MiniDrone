# Hardware.h — cấu hình phần cứng và hệ trục

Source: [Hardware.h](../../../firmware/DroneRebuild/Hardware.h). [Mục lục](README.md).

## 1. Vai trò

Với SE, đây gần với một module hằng số cấu hình. Các file khác đọc `hw::motors`, `hw::baud`... để thực hiện công việc. Include file này không tự bật motor, mở SPI hoặc kết nối Serial.

## 2. Cấu hình này có hiệu lực khi nào?

```cpp
#pragma once
namespace hw {
    constexpr unsigned baud = 460800;
}
```

`#pragma once` tránh include lặp. `namespace hw` gom tên; `hw::baud` xác định rõ nơi khai báo. `constexpr` là hằng có thể dùng tại compile time, không phải variable app cập nhật qua Wi-Fi khi đang chạy.

Bạn có thể liên hệ với một configuration module trong ứng dụng Java: các module khác dùng tên cấu hình thay vì tự ghi lại các con số. Tuy nhiên, ở đây các giá trị được đưa vào firmware khi build. Nếu sửa `baud` hay GPIO trong file trên máy tính thì cần build và nạp lại firmware; board đang chạy không tự nhận thay đổi đó.

`Hardware.h` không chỉ chứa thông tin điện. Nó còn chứa quy ước diễn giải số đo và một thông số của bộ ước lượng. Tên file chỉ cho biết đây là nơi gom các cấu hình dùng chung gắn với hệ thống hiện tại.

### 2.1. GPIO là gì?

**GPIO (General-Purpose Input/Output)** là chân tín hiệu của vi điều khiển. Khi làm input, ESP32 đọc tín hiệu đi vào; khi làm output, ESP32 chủ động phát tín hiệu ra. Một số GPIO cũng có thể kết nối với peripheral như SPI hoặc PWM.

- **HIGH / LOW:** hai mức logic dùng để biểu diễn tín hiệu số.
- **GND:** mốc điện áp tham chiếu của mạch.
- **Peripheral:** khối phần cứng chuyên xử lý một chức năng, ví dụ SPI hoặc tạo PWM, giúp CPU không phải tự thực hiện từng thao tác nhỏ.

Số GPIO là tên chân theo chip, không phải số thứ tự khi đếm chân từ mép board. Muốn tìm GPIO14 trên board thật, cần xem nhãn hoặc pinout.

## 3. SPI là gì và các chân dùng thế nào?

| Hằng số | GPIO | Nhiệm vụ |
| --- | ---: | --- |
| `sck` | 18 | Clock SPI từ ESP32 |
| `miso` | 19 | Dữ liệu IMU → ESP32 |
| `mosi` | 23 | Dữ liệu/lệnh ESP32 → IMU |
| `cs` | 5 | Chọn chip IMU |

Có thể hình dung ESP32 yêu cầu “đọc ô dữ liệu địa chỉ X”, chip trả byte. Nhưng thao tác này thực sự đổi mức điện trên chân, không chỉ đọc một object trong RAM.

`Imu::begin()` dùng các chân để mở SPI. `read()/write()` điều khiển CS để chọn chip. Số GPIO không phải số thứ tự chân đếm từ mép board.

### 3.1. SPI truyền thông tin như thế nào?

**SPI (Serial Peripheral Interface)** là giao tiếp truyền dữ liệu số giữa các chip. ESP32 chủ động tạo clock và gửi yêu cầu; IMU nhận yêu cầu và trả dữ liệu.

Tên các tín hiệu có thể đọc như sau:

- **SCK — Serial Clock:** clock đồng bộ việc truyền bit.
- **MOSI — Master Out, Slave In:** dữ liệu từ ESP32 sang IMU.
- **MISO — Master In, Slave Out:** dữ liệu từ IMU về ESP32.
- **CS — Chip Select:** chọn chip đang giao tiếp.

Master/Slave là cách mở rộng tên MISO/MOSI truyền thống; trong hệ thống này, ESP32 là controller chủ động còn IMU là peripheral.

```text
ESP32                         IMU
GPIO18  ── SCK ─────────────> clock
GPIO23  ── MOSI ────────────> lệnh/dữ liệu
GPIO19  <─ MISO ───────────── dữ liệu trả về
GPIO5   ── CS ──────────────> chọn chip
```

**Clock không phải dữ liệu đo.** Nó cung cấp nhịp để hai chip biết lúc nào cần phát/đọc bit theo chế độ SPI đã chọn.

Trong `Imu.h`, SPI transaction được cấu hình ở **250 kHz**. Tức là một chu kỳ clock khoảng 4 microsecond, 8 bit cần khoảng 32 microsecond clock. Một lần đọc có nhiều byte và các thao tác khác nên không thể hiểu là đọc được 250000 mẫu IMU mỗi giây. Clock SPI cũng khác nhịp tính PID.

### 3.2. Register là gì?

**Register** là ô có địa chỉ bên trong chip dùng để giữ dữ liệu đo, trạng thái hoặc cấu hình. Có thể liên hệ với một field có địa chỉ để truy cập, nhưng đây là tài nguyên phần cứng với quy tắc do chip định nghĩa.

Một lần đọc trong firmware đi theo luồng:

1. Kéo CS xuống LOW để chọn IMU.
2. Gửi địa chỉ register cùng thông tin yêu cầu đọc qua MOSI.
3. Tiếp tục tạo clock để nhận các byte trả về qua MISO.
4. Đưa CS lên HIGH để kết thúc giao tiếp.

`Hardware.h` chỉ cho biết dùng chân nào. `Imu.h` mới chứa các bước truyền/nhận và địa chỉ register cụ thể. Các byte đọc được còn cần ghép số, quy đổi đơn vị và xử lý để tạo góc/rate; xem [Imu.md](Imu.md).

## 4. Bảng motor

```cpp
constexpr int motors[4] = {14, 15, 12, 13}; // LF RF LR RR
```

| Index | Nhãn | Vị trí theo thân drone | GPIO |
| --- | --- | --- | ---: |
| 0 | LF | Trước trái | 14 |
| 1 | RF | Trước phải | 15 |
| 2 | LR | Sau trái | 12 |
| 3 | RR | Sau phải | 13 |

`ledcWrite(hw::motors[0], 110)` là ghi duty vào PWM gắn với GPIO14 theo API đang dùng. Nó không phải 110 vòng/phút, 110 V hay lực 110 N.

GPIO là tín hiệu điều khiển mạch công suất, không trực tiếp cấp toàn bộ dòng motor.

### 4.1. Trái/phải được tính theo đâu?

Trái/phải được tính theo thân drone khi nhìn từ đuôi về đầu. Nếu nhìn từ trên xuống và đặt đầu drone hướng lên trang:

```text
                 ĐẦU DRONE

        LF [0]              RF [1]
        GPIO14              GPIO15

                  thân

        LR [2]              RR [3]
        GPIO12              GPIO13

                 ĐUÔI DRONE
```

Đầu/đuôi phải theo hướng đã quy ước cho thân drone và cách lắp IMU. Không thể chỉ thấy cổng USB rồi mặc định đó luôn là đầu hoặc đuôi.

Mixer trả bốn duty theo thứ tự LF, RF, LR, RR. Firmware dùng cùng index để chọn GPIO trong `motors`. Nếu mixer yêu cầu tăng bên trái, index 0 và 2 phải thực sự nối tới LF và LR. Nếu mapping hoặc dây sai, correction tính đúng vẫn có thể tác động lên sai motor.

Comment `LF RF LR RR` thể hiện vị trí mà code dự kiến, không tự xác nhận vị trí dây thực tế.

### 4.2. Vì sao GPIO không trực tiếp cấp nguồn motor?

GPIO dùng cho tín hiệu điều khiển, còn motor cần dòng từ nguồn qua mạch công suất. Hai đường có vai trò khác nhau:

```text
GPIO ESP32 ── tín hiệu ──> mạch công suất ──> motor
                                 ↑
                              nguồn pin
```

Mạch tín hiệu và mạch công suất cần có mốc tham chiếu phù hợp, thường là GND chung theo sơ đồ thiết kế. Không nên đọc tên “chân motor GPIO14” thành “motor lấy toàn bộ điện chạy từ GPIO14”.

### 4.3. PWM, duty và lực đẩy khác nhau thế nào?

**PWM (Pulse-Width Modulation)** điều khiển tỉ lệ thời gian tín hiệu bật trong mỗi chu kỳ. **Duty cycle** là tỉ lệ bật đó. Trong `setup()`, `.ino` gắn peripheral LEDC để tạo PWM **20 kHz, 8-bit** trên các GPIO motor.

- **20 kHz:** 20000 chu kỳ xung mỗi giây, mỗi chu kỳ dài 50 microsecond.
- **8-bit:** thang lệnh có 256 mức, từ 0 đến 255.
- **Duty 0:** lệnh đầu ra bằng 0.
- **Duty 110:** khoảng `110 / 255 × 100% = 43,1%` của thang duty.
- **Duty 255:** mức cao nhất của thang duty.

Tỉ lệ trên là cách quy đổi theo thang lệnh, còn chi tiết tạo xung phụ thuộc peripheral PWM. Duty 110 không có nghĩa motor tạo 43,1% lực nâng tối đa. Quan hệ giữa duty, tốc độ motor và lực đẩy phụ thuộc điện áp pin, motor, cánh và tải; firmware hiện không quy đổi duty trực tiếp thành newton hoặc N·m.

```text
PID / mixer tính duty
          ↓
guard() kiểm tra và ghi duty được phép
          ↓
LEDC tự tạo PWM 20 kHz bằng phần cứng
          ↓
Mạch công suất → motor → lực đẩy
```

CPU không cần tự bật/tắt từng xung PWM. Khi đổi duty, peripheral tiếp tục tạo xung theo thiết lập mới. Vì thế, nhịp PID 200 Hz và PWM 20 kHz có thể cùng tồn tại mà không mâu thuẫn.

## 5. Hệ sensor và hệ body

Hệ sensor gắn với chip; hệ body gắn với thân drone. Chip lắp xoay so với thân thì phải đổi trục.

**Coordinate frame** là hệ trục dùng để mô tả hướng và thành phần của một đại lượng. Hệ sensor đi theo cách đặt trục của chip, còn hệ body đi theo thân drone. Roll là chuyển động quay quanh body X, Pitch quanh body Y, Yaw quanh body Z. Các trục body nghiêng theo drone, không luôn cố định với căn phòng.

Ví dụ chip lắp xoay trên board: quay quanh body X có thể làm sensor Y thay đổi mạnh nhất. Do đó, tên `gyroX` không tự bảo đảm đây là Roll rate.

```text
Body X = −Sensor Y
Body Y = +Sensor X
Body Z = +Sensor Z
```

Với các thành phần vector, mapping này có nghĩa: Roll rate lấy từ sensor Y rồi đảo dấu, Pitch rate lấy từ sensor X giữ dấu, thành phần quay quanh body Z lấy từ sensor Z giữ dấu. Dấu trừ mô tả chiều của hệ trục, không có nghĩa sensor bị lỗi.

Comment source gọi đây là mapping thuận tay phải (right-handed). Quy tắc bàn tay phải xác định chiều quay dương quanh trục: ngón cái chỉ chiều dương của trục, các ngón còn lại khum theo chiều quay dương. Muốn liên hệ với thao tác nâng trái/hạ phải trên drone thật vẫn phải biết hướng trục vật lý và cách lắp IMU.

`.ino` sử dụng `rollSensorSign=-1`:

```text
Roll rate = −(gyroY − biasY)
Pitch rate = gyroX − biasX
```

Ví dụ gyroY=10°/s, biasY=2°/s thì Roll rate=−8°/s.

**Gyro bias** là độ lệch nền: khi không quay, gyro thực tế có thể vẫn báo số khác 0. Trong ví dụ trên, trừ bias được `10 − 2 = 8°/s` theo sensor Y, sau đó đổi dấu để có `−8°/s` theo Roll của body.

**Rate khác góc:** gyro báo tốc độ quay theo °/s, không trực tiếp báo đang nghiêng bao nhiêu độ. Nếu giữ thân đứng yên ở một góc nghiêng, rate vẫn về gần 0 dù góc chưa về 0. Có thể liên hệ với vị trí và vận tốc: vật ở một vị trí khác gốc vẫn có thể đứng yên.

**Sensor sign khác motor sign:** sensor sign đổi chiều diễn giải số đo; `Config.motorSign` đổi chiều correction đưa vào mixer. Không đổi tùy tiện cái này để bù nhầm cái kia.

```text
Gyro → trừ bias → đổi trục/dấu → rate đưa vào PID
                                      ↓
                               correction duty
                                      ↓
                               motorSign → mixer
```

Một cái tác động ở phía đo lường, một cái ở phía điều khiển motor. Đổi dấu số đo để bù motor mapping sai có thể làm rate không còn nhất quán với góc do bộ ước lượng tạo ra.

Ngoài ra, `rollSensorSign` chỉ được dùng trực tiếp cho đường Roll rate trong `.ino`. `Attitude` ở `Imu.h` còn có phép chuyển hệ trục khi tính góc từ quaternion. Đổi riêng hằng này không tự thay đổi toàn bộ phép đổi trục cho góc. Comment mapping cũng chỉ mô tả quy ước; code xử lý mới thực thi nó.

## 6. Các hằng số còn lại

| Hằng số | Công dụng |
| --- | --- |
| `version` | Nhãn phiên bản để đưa vào INFO |
| `profile` | Nhãn cấu hình phần cứng; không phải phép tự nhận dạng toàn bộ board |
| `beta=0.075` | Mức hiệu chỉnh của Madgwick, còn nhân trọng số trong `Attitude::update()` |
| `baud=460800` | Tốc độ Serial; không phải số dòng telemetry mỗi giây |

Beta thuộc bộ ước lượng, không phải gain PID. Đổi beta tác động tới cách tạo góc; đổi Kp/Ki/Kd tác động tới lệnh điều khiển từ các góc/rate đó.

### 6.1. Tại sao cần beta và bộ ước lượng góc?

**Bộ ước lượng (estimator)** tạo thông tin hướng từ số đo IMU:

- Gyro đo tốc độ quay. Tích phân rate theo thời gian giúp cập nhật hướng, nhưng sai lệch nhỏ có thể tích lũy thành drift.
- Accelerometer đo gia tốc riêng (specific force). Khi gần đứng yên, tín hiệu cho biết hướng so với trọng lực; khi tăng tốc hoặc rung, tín hiệu không chỉ phản ánh tư thế.
- Madgwick kết hợp thông tin để cập nhật **quaternion**, một dạng biểu diễn hướng trong không gian, rồi tính các góc cần dùng.

`beta` điều chỉnh mức hiệu chỉnh từ accelerometer trong thuật toán. Trong source hiện tại, mức tác động còn được nhân với trọng số: khi độ lớn gia tốc lệch xa 1 g, trọng số giảm để hạn chế ảnh hưởng của accelerometer.

Trong cùng điều kiện, tăng beta làm phần hiệu chỉnh mạnh hơn nhưng có thể khiến nhiễu/gia tốc chuyển động ảnh hưởng rõ hơn; giảm beta làm hiệu chỉnh yếu hơn và có thể sửa drift chậm hơn. Không thể kết luận “beta càng lớn thì drone càng cân bằng”.

```text
IMU → estimator có beta → góc/rate → PID có Kp/Ki/Kd → duty motor
```

Chi tiết cách triển khai được giải thích trong [Imu.md](Imu.md). `Hardware.h` chỉ cung cấp giá trị beta cho thuật toán đó.

### 6.2. Baud khác byte/giây và telemetry rate

`460800` là baud của Serial. Với UART thông thường trong trường hợp này, nó tương ứng khoảng 460800 bit mỗi giây, không phải 460800 byte hoặc dòng mỗi giây.

Để hình dung, với frame 8N1, mỗi byte dữ liệu cần khoảng 10 bit gồm start bit, 8 data bit và stop bit. Giới hạn lý thuyết khi đó khoảng `460800 / 10 = 46080 byte/giây`, chưa tính giới hạn buffer, USB bridge và phần mềm.

| Thông số | Mô tả việc gì? |
| --- | --- |
| Serial 460800 baud | Tốc độ tín hiệu Serial |
| SPI 250 kHz | Clock truyền bit giữa ESP32 và IMU |
| PWM 20 kHz | Tần số xung điều khiển motor |
| Control task danh định 200 Hz | Nhịp xử lý điều khiển |
| Telemetry 50 hoặc 100 Hz | Nhịp tạo mẫu telemetry chờ gửi |

Tăng baud không tự làm PID tính nhanh hơn. Serial Monitor cần chọn đúng baud để đọc dữ liệu firmware; baud này không phải cấu hình tốc độ Wi-Fi hay TCP.

### 6.3. Version và profile không phải tự nhận dạng phần cứng

`version` hiện là `DroneRebuild-0.2`, `profile` là `XW_Z1_SPI_ROLL_PITCH_V2`. Đây là các string do developer khai báo và đưa vào INFO để nhận diện phiên bản/profile dự kiến.

Đổi tên profile không tự cấu hình lại chip, không phát hiện cách nối dây và không xác nhận motor mapping đúng. Nếu sửa code nhưng không cập nhật nhãn, nhãn vẫn giữ giá trị cũ.

Hai nhãn này cũng khác `rebuildSourceId` trong `BuildInfo.h`: source ID được script sinh từ nội dung source theo quy tắc của script. Xem [BuildInfo.md](BuildInfo.md).

## 7. Mối liên hệ

```text
Hardware.h → DroneRebuild.ino: Serial, PWM, sign số đo, INFO
           → Imu.h: chân SPI và beta
```

Không phải mọi cấu hình đều đã được gom vào file này. Để đọc đúng chỗ:

| Bạn muốn tìm | Nơi cần đọc |
| --- | --- |
| GPIO của SPI/motor và các hằng ở trên | `Hardware.h` |
| Clock SPI, register, đọc byte, bộ ước lượng | `Imu.h` |
| Tạo PWM 20 kHz, 8-bit | `setup()` trong `.ino` |
| Kiểm tra và ghi duty khi chạy | `guard()` trong `.ino` |
| PID, motor sign, output limit và mixer | `RollCore.h` |
| Hiệu chuẩn gyro, nhịp task và telemetry | `controlTask()` trong `.ino` |

`Hardware.h` mô tả các giá trị để những phần này dùng chung; nó không thay thế quá trình khởi tạo hoặc logic điều khiển ở các file khác.

Đọc tiếp [Imu.md](Imu.md) hoặc [DroneRebuild.md](DroneRebuild.md).
