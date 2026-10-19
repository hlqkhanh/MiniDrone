# ESP32 XW_Z1_V2.0 — cảm biến SPI và app 3D

Thử riêng 4 động cơ: xem [MotorTest](MotorTest/README.md). Sketch thử độc lập, tự dừng sau tối đa 2 giây; nạp lại sketch cảm biến để dùng app 3D.

**Cấu hình Wi-Fi bằng trình duyệt:** xem [hướng dẫn Wi-Fi](WIFI.md). Nối vào `DroneMotion_Setup-XXXX`, mật khẩu `drone12345`, mở `http://192.168.4.1` để chọn mạng. App dùng IP trên trang sau khi ESP32 kết nối router, port `3333`.

Sketch: [DroneMotionMpu6050.ino](DroneMotionMpu6050/DroneMotionMpu6050.ino), kèm `TelemetryOutput.h` cùng thư mục. Giữ tên thư mục cũ để mở lại thuận tiện trong Arduino IDE.

Khi chọn ESP32, sketch dùng SPI theo [sơ đồ bo](../doc/vi/so-do-nguyen-ly.md); khi chọn Arduino Nano, sketch vẫn dùng MPU6050 qua I2C A4/A5. Không còn dùng SDA21/SCL22 trên ESP32. Không cần cài thư viện cảm biến bên ngoài: SPI/Wire đi kèm board core.

## Chân và nhận dạng cảm biến

| Tín hiệu | GPIO ESP32 | Chân module |
| --- | --- | --- |
| SCK | 18 | SCL |
| MOSI | 23 | SDA |
| MISO | 19 | AD0 |
| CS / NCS | 5 | NCS |
| VCC | 3V3 | VCC |
| GND | GND | GND |

Đây là kết nối có sẵn theo sơ đồ XW_Z1_V2.0, không yêu cầu nối lại bo đã lắp đúng. Firmware đọc WHO_AM_I (0x75) ba lần trước khi cấu hình:

| WHO_AM_I | Xử lý |
| --- | --- |
| 0x70 | MPU6500: đọc gia tốc và gyro |
| 0x71 | MPU9250: đọc gia tốc và gyro, chưa dùng từ kế |
| 0x00 / 0xFF hoặc phản hồi không ổn định | Dừng, báo MPU_NOT_FOUND |
| Mã khác, kể cả 0x73 | Dừng, báo MPU_UNSUPPORTED_ID; cần đối chiếu model trước khi bổ sung driver |

SPI chạy 1 MHz, mode 0. Các thanh ghi cấu hình được đọc lại để xác nhận. Quaternion được tính ở 100 Hz bằng Madgwick với gia tốc/gyro; yaw vẫn có thể trôi vì chưa dùng từ kế.

## Nạp và kiểm tra

Nếu muốn kiểm tra nhận dạng trước, mở [ImuSpiProbe.ino](ImuSpiProbe/ImuSpiProbe.ino), nạp lên ESP32 và mở Serial Monitor 115200. Sketch chỉ đọc WHO_AM_I mỗi giây, không ghi thanh ghi cấu hình cảm biến. Mong đợi `PROBE,WHO_AM_I,0x70,MPU6500` hoặc `PROBE,WHO_AM_I,0x71,MPU9250` ổn định. Nếu mã khác, gửi log để kiểm tra. Probe không gửi quaternion nên chưa dùng được với app 3D; sau khi nhận dạng, nạp sketch chính theo các bước dưới đây.

1. Tháo cánh hoặc ngắt các giắc động cơ khi thử. Firmware đặt GPIO14/15/12/13 LOW khi khởi động và không phát PWM; đây là sketch đọc cảm biến, không phải firmware bay.
2. Mở đúng file .ino trên đĩa bằng **File > Open** trong Arduino IDE. Nếu trước đó dán code vào sketch_oct16a, cần mở file dự án hoặc chép lại toàn bộ code mới; tab cũ không tự cập nhật.
3. Chọn **ESP32 Dev Module**, cổng USB của bo (máy đã dùng COM6), **Upload Speed = 115200**. Đóng Serial Monitor và Disconnect trong app trước khi Upload.
4. Bấm Verify rồi Upload. Nếu kẹt Connecting, giữ BOOT, nhấn/thả EN, thả BOOT khi bắt đầu ghi.
5. Mở Serial Monitor ở **115200 baud**, nhấn EN, giữ bo đứng yên khoảng 3 giây. Ví dụ với MPU6500:

```text
HELLO,1,100
STATUS,SPI_SCK18_MOSI23_MISO19_CS5
STATUS,WHO_AM_I_0x70
STATUS,SENSOR_MPU6500
STATUS,CALIBRATING
STATUS,READY
Q,0,<micros>,<qw>,<qx>,<qy>,<qz>
```

Dòng Q trong ví dụ chỉ mô tả định dạng; các phần trong dấu <> sẽ là số thực tế. Nếu hiện SENSOR_MPU9250 và WHO_AM_I_0x71, firmware dùng phần gia tốc/gyro tương ứng; tên MPU6050 trên cửa sổ app cũ không phải thông tin nhận dạng phần cứng.

## Lệnh chẩn đoán trên Serial Monitor

Chọn kết thúc dòng **Newline**, gửi:

- `INFO`: đọc lại WHO_AM_I, in bus và tên chip nhận được. Chạy được cả khi khởi tạo thất bại.
- `PING`: nhận `PONG`, xác nhận chương trình vẫn chạy.
- `CAL`: hiệu chuẩn lại; nếu trước đó lỗi cảm biến hoặc hiệu chuẩn, thử nhận dạng/khởi tạo lại trước. Giữ bo đứng yên. Quá 15 giây không đủ mẫu đứng yên sẽ báo lỗi và ngừng gửi Q.

Firmware xử lý lệnh giữa các mẫu. Trong lúc hiệu chuẩn, lệnh chờ đến khi hiệu chuẩn xong hoặc hết thời gian chờ.

| Lỗi | Việc cần kiểm tra |
| --- | --- |
| MPU_NOT_FOUND | Nguồn 3V3/GND, chân SPI theo sơ đồ; gửi toàn bộ log sau EN, đặc biệt WHO_AM_I |
| MPU_UNSUPPORTED_ID | Gửi mã WHO_AM_I để đối chiếu đúng chip; không đổi thành MPU6050 bằng cách sửa số ID |
| MPU_CONFIG | Nhận dạng được nhưng thanh ghi cấu hình không đọc lại đúng; kiểm tra kết nối và nguồn |
| MPU_READ | ID thay đổi, truyền thất bại hoặc cả khối dữ liệu là 0x00/0xFF; kiểm tra kết nối/nguồn, rồi gửi CAL để thử lại |
| CALIBRATION_TIMEOUT_KEEP_STILL | Đặt bo đứng yên trên mặt bàn, gửi CAL để thử lại |

## Kết nối app 3D

1. Khi đã có READY và Q, đóng Serial Monitor/Plotter.
2. Trong **MPU6050 Motion Viewer**, chọn **Refresh > COM6** (hoặc COM USB thực tế), **115200 > Connect**.
3. Nhấn EN nếu cần xem lại trạng thái khởi động. Giữ bo đứng yên khi hiệu chuẩn.
4. Xác nhận Serial khoảng 100 samples/s, mô hình thay đổi khi nghiêng bo. App có thể hiện “Device responded to ping” sau READY; đó chỉ là phản hồi kiểm tra kết nối.
5. Chọn **Zero orientation** để đặt mốc. **Calibrate** để đo lại gyro bias.

Giao thức HELLO/STATUS/ERROR/Q và CAL/PING giữ nguyên nên dùng được với bản app đã build, không cần build lại WPF. INFO trả thêm các dòng STATUS mà parser hiện tại đã hỗ trợ.

Sketch có AP cấu hình web và STA để truyền dữ liệu trên Wi-Fi có sẵn; không cung cấp điều khiển bay của firmware nhà cung cấp. Nạp sketch sẽ thay chương trình đang có trên ESP32.

## Nguồn đối chiếu

- [Sơ đồ bo trong dự án](../doc/sodo.pdf).
- [MPU6500 Register Map, WHO_AM_I = 0x70](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6500-Register-Map2.pdf).
- [MPU9250 Register Map, WHO_AM_I = 0x71](https://invensense.tdk.com/wp-content/uploads/2017/11/RM-MPU-9250A-00-v1.6.pdf).
- [Mã tham khảo SPI và cấu hình gia tốc/gyro của SparkFun](https://github.com/sparkfun/SparkFun_MPU-9250_Breakout_Arduino_Library/blob/master/src/MPU9250.cpp).
