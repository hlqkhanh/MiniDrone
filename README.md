# MPU6050 3D Motion Viewer

Ứng dụng hiển thị hướng của module GY-521/MPU6050 trong không gian 3D. Arduino Nano tính quaternion ở 100 Hz và gửi qua USB serial; ứng dụng WPF chỉ giữ mẫu mới nhất và cập nhật viewport theo nhịp render.

> MPU6050 không có từ kế. Roll/pitch được hiệu chỉnh theo trọng lực, còn yaw là tương đối và sẽ trôi dần. Nút **Zero orientation** đặt tư thế hiện tại làm mốc mới nhưng không loại bỏ yaw drift.

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

Ứng dụng có thể gửi `CAL\n` và `PING\n`.

## Kiểm thử

```powershell
dotnet test .\DroneMotion.sln
```

Các test tự động bao phủ parser serial, dữ liệu phân mảnh/sai, chuẩn hóa quaternion, chuyển hệ trục và zero orientation. Kiểm thử firmware và chiều quay thực tế cần Arduino cùng cảm biến thật.
