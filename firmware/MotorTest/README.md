# Thử 4 động cơ — XW_Z1_V2.0

Sketch độc lập cho ESP32 Dev Module, Arduino-ESP32 core 3.x. Không tự chạy động cơ khi cấp nguồn/reset. Không có cân bằng, điều khiển bay hoặc quaternion cho app 3D.

## Chuẩn bị

- Tháo toàn bộ cánh quạt, đặt bo trên mặt bàn chắc chắn.
- Động cơ lấy nguồn từ VBAT theo sơ đồ; USB đọc được IMU không chứng minh VBAT đã có điện. Dùng nguồn/pin đúng thông số bộ drone, đúng cực đầu nối; không đấu pin vào 3V3 hoặc GPIO.
- PWM điều khiển MOSFET trên bo, không cấp nguồn động cơ trực tiếp từ GPIO.

| Lệnh | Vị trí theo sơ đồ | GPIO |
| --- | --- | --- |
| LF | Trái trước | 14 |
| RF | Phải trước | 15 |
| LR | Trái sau | 12 |
| RR | Phải sau | 13 |
| ALL | Cả bốn | 14, 15, 12, 13 |

## Nạp và thử

1. Disconnect app 3D, đóng Serial Monitor trước khi nạp.
2. Mở `MotorTest.ino`, chọn ESP32 Dev Module, cổng USB thực tế (trước đây COM6), Upload Speed 115200, rồi Upload.
3. Mở Serial Monitor **115200 baud**, kết thúc dòng **Newline**. Nhấn EN nếu cần. Mong đợi `READY,DISARMED`.
4. Gửi từng dòng riêng để kiểm tra một động cơ trước:

```text
ARM
LF 20 1000
```

LF nhận PWM 20% trong 1000 ms, rồi tự dừng và khóa lại. Muốn thử tiếp phải gửi ARM mới. Vị trí LF/RF/LR/RR theo sơ đồ, không suy từ chiều xoay ảnh chụp.

5. Khi các động cơ riêng hoạt động, thử cả bốn:

```text
ARM
ALL 20 1000
```

6. Gửi `STOP` để dừng sớm và khóa lại. Nhấn EN cũng khởi động lại về trạng thái tắt.

Mỗi lệnh chạy có dạng `ALL 20 1000` hoặc `LF 20 1000`: vị trí, phần trăm PWM, thời gian ms. Đây không phải phần trăm tốc độ quay đo được. Giới hạn PWM 1–35%, thời gian 1–2000 ms. ARM hết hiệu lực nếu không gửi lệnh chạy trong 15 giây. Lệnh sai dừng/khóa; lệnh chạy khi chưa ARM bị từ chối. Hết thời gian tự dừng dù không có thêm dữ liệu serial; sketch không phát hiện rút cáp tức thời, giới hạn chạy vẫn tối đa 2 giây.

Nếu chỉ rung hoặc không quay ở 20%, gửi STOP và kiểm tra nguồn VBAT, đầu nối, tình trạng cơ khí trước. Có thể thử 25% rồi 30% ngắn hạn; không gắn cánh để thử và không tăng giới hạn trong code để xử lý lỗi nguồn. Nếu reset khi chạy cả bốn, kiểm tra nguồn/dây trước.

Nạp MotorTest sẽ thay sketch cảm biến trên ESP32. Muốn dùng app 3D trở lại, nạp `../DroneMotionMpu6050/DroneMotionMpu6050.ino`.

## Đối chiếu

- [Sơ đồ và bảng động cơ](../../doc/vi/so-do-nguyen-ly.md).
- [API LEDC chính thức](https://docs.espressif.com/projects/arduino-esp32/en/latest/api/ledc.html).
