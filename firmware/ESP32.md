# ESP32 XW_Z1_V2.0 — cảm biến SPI và app 3D

Thử riêng 4 động cơ: xem [MotorTest](MotorTest/README.md). Sketch thử độc lập, tự dừng sau tối đa 2 giây; nạp lại sketch cảm biến để dùng app 3D.

**Wi-Fi có hai cách kết nối:** nối laptop vào `DroneMotion_Setup-XXXX`, mật khẩu `drone12345`, trong WPF chọn **Direct ESP32 Wi-Fi** và dùng `192.168.4.1:3333`; hoặc mở `http://192.168.4.1` để cấu hình router, rồi chọn **Router / hotspot** trên WPF và nhập IP LAN của ESP32. Xem [hướng dẫn Wi-Fi](WIFI.md).

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

SPI chạy 250 kHz, mode 0, có khoảng đệm CS 1 µs trước/sau truyền để đáp ứng thời gian giữ CS của MPU6500. Các thanh ghi cấu hình được đọc lại để xác nhận. IMU, Madgwick và vòng cân bằng chạy ở 200 Hz; quaternion gửi ra 100 Hz. Yaw vẫn có thể trôi vì chưa dùng từ kế. Xem [hướng dẫn cân bằng PID trên đế](../doc/vi/pid-can-bang-tren-de.md) để chỉnh Kp/Ki/Kd trên WPF.

## Nạp và kiểm tra

### Đồng bộ hiệu chuẩn và kết nối

ESP32 dùng **MPU6500/MPU9250 qua SPI**; tên thư mục `DroneMotionMpu6050` được giữ để tương thích. Cập nhật cả firmware (giữ tất cả header cùng thư mục, gồm `GyroCalibration.h`) và app WPF để thấy tiến độ mới.

- Hiệu chuẩn không chặn vòng xử lý USB/Wi-Fi, portal hoặc lệnh STOP/PING/INFO. Kết nối giữa lúc hiệu chuẩn nhận ngay trạng thái hiện tại. Gửi CAL lặp lại không làm mất tiến độ.
- Thu 500 mẫu mới, kiểm tra data-ready. Cửa sổ đứng yên cần ít nhất khoảng 2,5 giây; rung/chuyển động làm bắt đầu lại cửa sổ. Thời gian chờ tối đa 30 giây, sau đó tự thử lại sau 3 giây mà không reset cảm biến đang giao tiếp tốt.
- Kiểm tra độ biến thiên gyro và vector gia tốc để ước lượng lệch zero. Không còn yêu cầu gyro chưa bù phải dưới 3°/s. Vẫn giới hạn biên độ gyro 15°/s, gia tốc 0,85–1,15 g và độ biến thiên mỗi trục; phải đặt bo trên mặt chắc, hoàn toàn đứng yên. Quay yaw đều chậm vẫn có thể bị nhầm với lệch zero; không hiệu chuẩn khi đang cầm/xoay bo.
- Giao thức thêm `CAL,<accepted>,500,<reason>` mỗi 500 ms. Reason gồm `COLLECTING`, `MOTION`, `ACCEL_NOT_1G`, `GYRO_OUT_OF_RANGE`, `COMPLETE`. Chỉ gửi READY và Q sau khi hiệu chuẩn thành công. Motor không được khởi động trong lúc hiệu chuẩn.
- Không có mẫu mới trong 3 giây báo `MPU_NO_FRESH_DATA_RECOVERING` và khởi tạo lại, khác với lỗi giữ chưa yên. Mất phản hồi SPI vẫn báo lỗi cảm biến và không cho chạy cân bằng.
- Trong chế độ Direct ESP32 Wi-Fi, lỗi ESP32 không vào được router không thay thế trạng thái cảm biến của app; AP vẫn dùng được.

Đây là kiểm tra phần mềm; vẫn cần nạp và xác nhận `WHO_AM_I_0x70`, tiến độ CAL, `STATUS,READY`, rồi dòng Q liên tục trên bo thực tế.

Nếu muốn kiểm tra nhận dạng trước, mở [ImuSpiProbe.ino](ImuSpiProbe/ImuSpiProbe.ino), nạp lên ESP32 và mở Serial Monitor 115200. Sketch chỉ đọc WHO_AM_I, không ghi thanh ghi cấu hình cảm biến, không bật Wi-Fi, giữ motor tắt. Nó đọc 100 lần tại mỗi tốc độ 100 kHz, 250 kHz và 1 MHz, rồi in số lần nhận MPU6500/MPU9250/0x00/0xFF/mã khác. Ví dụ tốt: `PROBE,HZ,250000,MPU6500,100,MPU9250,0,ZERO,0,FF,0,OTHER,0,LAST,0x70`. Nếu tất cả tốc độ đều chỉ trả ZERO hoặc FF, không thể giải quyết bằng giữ yên hay chỉnh PID; cần ngắt cả pin và USB để thử lại, kiểm tra nguồn 3,3 V tại module và tiếp xúc/chân SPI. Chưa thể khẳng định chip hỏng chỉ từ log này. Probe không gửi quaternion nên chưa dùng được với app 3D; sau khi nhận dạng, nạp sketch chính theo các bước dưới đây.

1. Tháo cánh hoặc ngắt các giắc động cơ khi thử. Firmware đặt GPIO14/15/12/13 LOW khi khởi động; PWM được gắn khi ARM. Phần MANUAL MOTOR OUTPUT trong app hỗ trợ PWM 0–100% riêng từng động cơ, thời gian tùy chọn hoặc liên tục. KEEPALIVE mất 2 giây sẽ dừng; xem README gốc để biết giao thức V2. Đây không phải firmware bay. Giữ toàn bộ file header cùng sketch, gồm MotorControl.h và MotionCompensation.h.
2. Mở đúng file .ino trên đĩa bằng **File > Open** trong Arduino IDE. Nếu trước đó dán code vào sketch_oct16a, cần mở file dự án hoặc chép lại toàn bộ code mới; tab cũ không tự cập nhật.
3. Chọn **ESP32 Dev Module**, cổng USB của bo (máy đã dùng COM6), **Upload Speed = 115200**. Đóng Serial Monitor và Disconnect trong app trước khi Upload.
4. Bấm Verify rồi Upload. Nếu kẹt Connecting, giữ BOOT, nhấn/thả EN, thả BOOT khi bắt đầu ghi.
5. Mở Serial Monitor ở **115200 baud**, nhấn EN, giữ bo đứng yên khoảng 3 giây. Ví dụ với MPU6500:

```text
HELLO,1,200
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

Sketch có AP để kết nối WPF trực tiếp và cấu hình web, đồng thời hỗ trợ STA để dùng router/hotspot. Cả hai dùng cổng TCP 3333 và chỉ nhận một WPF client. Nạp sketch sẽ thay chương trình đang có trên ESP32.

## Nguồn đối chiếu

- [Sơ đồ bo trong dự án](../doc/sodo.pdf).
- [MPU6500 Register Map, WHO_AM_I = 0x70](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6500-Register-Map2.pdf).
- [MPU9250 Register Map, WHO_AM_I = 0x71](https://invensense.tdk.com/wp-content/uploads/2017/11/RM-MPU-9250A-00-v1.6.pdf).
- [Mã tham khảo SPI và cấu hình gia tốc/gyro của SparkFun](https://github.com/sparkfun/SparkFun_MPU-9250_Breakout_Arduino_Library/blob/master/src/MPU9250.cpp).
