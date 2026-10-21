# MPU6050 3D Motion Viewer

**Cân bằng PID trên đế giữ drone (ESP32):** xem [hướng dẫn PID và chỉnh gain trên WPF](doc/vi/pid-can-bang-tren-de.md). Có chỉnh Kp/Ki/Kd riêng roll/pitch, Arm/Start/Stop, telemetry và CSV. Gain mặc định bằng 0; cần xác minh mapping và chỉnh trên đế trước khi dùng.

**Wi-Fi có hai cách:** nối thẳng laptop vào `DroneMotion_Setup-XXXX` (mật khẩu `drone12345`), trong WPF chọn **Direct ESP32 Wi-Fi** và `192.168.4.1:3333`; hoặc chọn **Router / hotspot** và IP LAN của ESP32. Cách trực tiếp không cần router, điện thoại hoặc Internet. Xem [cách nạp và kết nối Wi-Fi](firmware/WIFI.md).

**Dùng ESP32 XW_Z1_V2.0:** xem [hướng dẫn nạp và kết nối ESP32](firmware/ESP32.md). Sketch dùng SPI (SCK18/MOSI23/MISO19/CS5), nhận dạng MPU6500 hoặc MPU9250 qua WHO_AM_I. Các bước Nano/I2C bên dưới dành cho Arduino Nano.

Ứng dụng hiển thị hướng cảm biến trong không gian 3D. Arduino Nano + MPU6050 (I2C) tính quaternion ở 100 Hz; ESP32 + MPU6500/MPU9250 (SPI) lấy mẫu và điều khiển ở 200 Hz, gửi quaternion 100 Hz qua USB/Wi-Fi. Ứng dụng WPF chỉ giữ mẫu mới nhất và cập nhật viewport theo nhịp render.

> Firmware chỉ dùng gia tốc/gyro, chưa dùng từ kế kể cả khi phát hiện MPU9250. Roll/pitch được hiệu chỉnh theo trọng lực, còn yaw là tương đối và sẽ trôi dần. Nút **Zero orientation** đặt tư thế hiện tại làm mốc mới nhưng không loại bỏ yaw drift.

## 1. Đấu dây

| GY-521 | Arduino Nano V3 |
|---|---|
| VCC | 5V |
| GND | GND |
| SDA | A4 |
| SCL | A5 |

Sơ đồ trên dành cho breakout GY-521 tiêu chuẩn có regulator. Hãy kiểm tra đúng pinout và regulator trên module trước khi cấp nguồn; chip MPU6050 rời chỉ dùng 3.3 V. Chân `INT` không được sử dụng.

## 2. Nạp firmware

1. Mở `firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino` bằng Arduino IDE.
2. Chọn **Arduino Nano**, processor phù hợp với board; clone cũ thường cần **ATmega328P (Old Bootloader)**.
3. Chọn đúng cổng COM và Upload. Sketch chỉ dùng thư viện `Wire` có sẵn.
4. Sau khi reset, giữ cảm biến hoàn toàn đứng yên cho tới khi LED ngừng nháy và sáng ổn định (khoảng 2.5–3 giây).

LED nháy chậm khi hiệu chuẩn, sáng khi sẵn sàng, và nháy nhanh nếu không tìm thấy/không đọc được MPU6050.

## 3. Chạy ứng dụng

Yêu cầu Windows và .NET 8 SDK/Desktop Runtime.

```powershell
dotnet restore .\DroneMotion.sln
dotnet run --project .\src\DroneMotion.Wpf\DroneMotion.Wpf.csproj
```

Trong ứng dụng:

1. Chọn cổng COM của Nano, giữ baud rate `115200`, rồi chọn **Connect**.
2. Chờ trạng thái **Sensor ready**. Mở cổng COM có thể làm Nano reset nên bước hiệu chuẩn sẽ chạy lại.
3. Đặt cảm biến ở tư thế muốn làm mốc và chọn **Zero orientation**.
4. Chọn **Calibrate** bất cứ lúc nào để đo lại gyro bias; giữ cảm biến đứng yên trong quá trình này.

Không mở Serial Monitor cùng lúc với ứng dụng vì mỗi cổng COM chỉ có thể do một chương trình sở hữu.

## Giao thức serial

Thiết lập: `115200`, 8 data bits, no parity, 1 stop bit. Mỗi bản tin kết thúc bằng newline.

```text
HELLO,1,100
STATUS,CALIBRATING
STATUS,READY
Q,<sequence>,<micros>,<qw>,<qx>,<qy>,<qz>
ERROR,<code>
PONG
```

Ứng dụng có thể gửi `CAL\n` và `PING\n`. Serial Monitor có thể gửi thêm `INFO\n` để đọc lại bus và WHO_AM_I. Hiệu chuẩn hết thời gian chờ sau 15 giây nếu không thu đủ mẫu đứng yên; gửi CAL để thử lại.

## Kiểm thử

### Thử động cơ từ ứng dụng WPF (ESP32)

Nạp lại sketch chính `firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino`
với Arduino-ESP32 core 3.x, giữ toàn bộ file header cùng thư mục, gồm
`MotorControl.h` và `MotionCompensation.h`. Build/chạy lại app WPF.

Trong phần **MANUAL MOTOR OUTPUT**, tháo toàn bộ cánh quạt, xác nhận **All
propellers are removed**, chọn LF/RF/LR/RR hoặc ALL và đặt Common PWM 0–100%.
Chọn CUSTOM hoặc sửa một trong bốn giá trị để đặt PWM riêng cho từng động cơ.
**Copy common PWM to all four** sao chép mức chung sang bốn giá trị CUSTOM.
LF/RF là trái/phải trước, LR/RR là trái/phải sau theo GPIO14/15/12/13.
PWM dùng 8 bit (duty 0–255) ở 20 kHz, không phải tốc độ quay hay lực nâng đo
được. Slider bước 0.1%, textbox cho nhập số trực tiếp; preview hiển thị duty
thực sau lượng tử hóa. Động cơ cần nguồn VBAT phù hợp theo sơ đồ.

Duration nhận 1–4294967295 ms (60000 = 1 phút), hoặc chọn **Continuous until
STOP** để không đặt thời hạn. Không còn giới hạn thử 35%/2 giây trong firmware
chính. Đây là thời gian chạy động cơ thủ công, không phải chế độ bay cân bằng.
**Run / apply** gửi cấu hình và bắt đầu lại thời gian chạy; sửa ô nhập không
tự tác động PWM. **STOP ALL** hoặc **Esc** dừng tất cả. Bỏ xác nhận tháo cánh lúc chạy cũng
yêu cầu dừng.

App tự ARM cho mỗi lần chạy, gửi KEEPALIVE mỗi 500 ms sau xác nhận RUNNING.
Supervisor timer trên ESP32 kiểm tra mỗi 50 ms: hết thời gian hoặc thiếu
KEEPALIVE từ đúng kết nối trong 2 giây thì dừng, kể cả khi loop cảm biến đang
bận. Disconnect/đóng app gửi STOP; Calibrate dừng động cơ trước khi đo bias.
Reset không tự chạy động cơ. Firmware chính V2 báo `STATUS,MOTOR_ADVANCED_V2`;
Arduino Nano và firmware cũ không bật được Run / apply, cần nạp lại sketch
chính rồi reconnect.

Có thể gửi thủ công qua Serial Monitor (115200, Newline):

```text
MOTOR ARM
MOTOR SET 51 76 0 128 60000
MOTOR KEEPALIVE
MOTOR STOP
```

SET nhận duty LF/RF/LR/RR (0–255), sau đó là thời gian ms (0 = liên tục).
Gửi KEEPALIVE lặp lại trong vòng 2 giây để duy trì chạy, không chỉ gửi một lần.
Vẫn hỗ trợ `MOTOR LF 20 1000` hoặc RF/LR/RR/ALL với phần trăm nguyên 0–100.
ARM hết hạn sau 15 giây, chỉ cho một lần chạy và phải đến từ cùng kết nối
USB/Wi-Fi với lệnh chạy. Kiểm thử máy tính dùng PWM/timer giả lập; chưa thay
thế kiểm tra timing, nguồn và động cơ thật.

### Mô hình không cập nhật roll/pitch/yaw

Xem Data và trạng thái stream trong app. Data = 0: kiểm tra đã nạp sketch
chính (MotorTest độc lập không có Q), đợi hiệu chuẩn, hoặc xem lỗi cảm biến.
Firmware mới bỏ qua lỗi đọc tạm thời và sau 3 lỗi liên tiếp sẽ dừng động cơ,
thử khởi tạo/hiệu chuẩn lại cảm biến sau 1 giây. Giữ bo đứng yên khi thấy
SENSOR_RECOVERING_KEEP_STILL. Có Q nhưng mất mô hình: xem lỗi tải GLB ở viewport.
Nếu bo reset khi chạy động cơ, kiểm tra nguồn/dây; tự retry không khắc phục
nguồn sụt hoặc kết nối cảm biến hỏng. Chưa xác minh nguyên nhân trên bo thật.

```powershell
g++ -std=c++17 -I tests/firmware/motor-stubs tests/firmware/motor-control.test.cpp -o "$env:TEMP/minidrone-motor-test.exe"
& "$env:TEMP/minidrone-motor-test.exe"
```

### Giảm trôi yaw

Firmware bù gyro bias từ từ sau ít nhất 3 giây có tốc độ góc dư dưới
0.15 độ/giây, gia tốc gần 1 g và ít thay đổi giữa các mẫu. Khi phát hiện
chuyển động hoặc gián đoạn lấy mẫu, thời gian chờ được tính lại. Hiệu chỉnh
Madgwick từ accelerometer giảm dần khi độ lớn gia tốc lệch 1 g từ 0.05 g
đến 0.20 g, nhằm giảm ảnh hưởng khi dịch chuyển.

Đây là giảm drift, không phải khóa hướng tuyệt đối: quay rất chậm dưới
ngưỡng nhận diện vẫn có thể bị coi là bias; gia tốc ngang cũng không luôn
được phát hiện chỉ bằng độ lớn. Không tự đặt yaw về 0 khi đứng yên.
Sau khi nạp lại firmware, giữ bo đứng yên để Calibrate, bấm Zero orientation,
đo yaw khi đứng yên 60 giây và khi đưa bo về cùng mốc hướng 10 lần.
Cần kiểm tra trên bo thật để đánh giá mức cải thiện.

Kiểm thử bù bias bằng dữ liệu giả (C++11):

```powershell
g++ -std=c++11 tests/firmware/motion-compensation.test.cpp -o "$env:TEMP/motion-compensation-test.exe"
& "$env:TEMP/motion-compensation-test.exe"
```

```powershell
dotnet test .\DroneMotion.sln
```

Các test tự động bao phủ parser serial, dữ liệu phân mảnh/sai, chuẩn hóa quaternion, chuyển hệ trục và zero orientation. Kiểm thử firmware và chiều quay thực tế cần Arduino cùng cảm biến thật.
