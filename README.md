# MPU6050 3D Motion Viewer

**Cấu hình Wi-Fi trên trình duyệt:** [cách nạp và kết nối Wi-Fi](firmware/WIFI.md). Nối vào `DroneMotion_Setup-XXXX` (mật khẩu `drone12345`), mở `http://192.168.4.1` để chọn mạng. Sau đó laptop trở về cùng LAN với ESP32, app dùng IP trên trang và port `3333`.

**Dùng ESP32 XW_Z1_V2.0:** xem [hướng dẫn nạp và kết nối ESP32](firmware/ESP32.md). Sketch dùng SPI (SCK18/MOSI23/MISO19/CS5), nhận dạng MPU6500 hoặc MPU9250 qua WHO_AM_I. Các bước Nano/I2C bên dưới dành cho Arduino Nano.

Ứng dụng hiển thị hướng cảm biến trong không gian 3D. Arduino Nano + MPU6050 (I2C) hoặc ESP32 + MPU6500/MPU9250 (SPI) tính quaternion ở 100 Hz và gửi qua USB serial; ứng dụng WPF chỉ giữ mẫu mới nhất và cập nhật viewport theo nhịp render.

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

```powershell
dotnet test .\DroneMotion.sln
```

Các test tự động bao phủ parser serial, dữ liệu phân mảnh/sai, chuẩn hóa quaternion, chuyển hệ trục và zero orientation. Kiểm thử firmware và chiều quay thực tế cần Arduino cùng cảm biến thật.
