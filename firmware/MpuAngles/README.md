# Xem Roll, Pitch, Yaw bằng Serial

Sketch thử độc lập cho ESP32 + MPU6500 trên bo XW_Z1_V2.0, SPI 250 kHz: SCK18/MISO19/MOSI23/CS5. Không Wi-Fi, WPF, PID hoặc chạy motor. Giữ nguyên sketch chỉ detect ở `../MpuDetect` để đối chiếu.

1. Mở `MpuAngles.ino`, giữ `AngleEstimator.h` cùng thư mục.
2. Chọn **ESP32 Dev Module**, đúng COM, **Upload Speed 115200**, rồi Upload.
3. Mở Serial Monitor ở **115200 baud**. Giữ bo hoàn toàn đứng yên trên bàn để thu 500 mẫu hiệu chuẩn, thường khoảng 3–5 giây.
4. Sau `READY`, nghiêng bo và xem ba giá trị độ, cập nhật khoảng 10 lần/giây:

```text
WHO_AM_I,0x70
CALIBRATING,KEEP_BOARD_STILL_3_SECONDS
CALIBRATING,250/500
READY,YAW_RELATIVE_MAY_DRIFT
Roll:1.25    Pitch:-3.40    Yaw:0.52
```

Ba góc theo trục cảm biến, chưa áp dụng ánh xạ/mốc zero riêng của mô hình WPF. Roll/pitch ước lượng từ gyro kết hợp trọng lực; yaw chỉ tích phân gyro, lấy mốc gần 0 sau hiệu chuẩn, có thể trôi vì MPU6500 không có la bàn. Đây là sketch kiểm tra cảm biến, không phải điều khiển bay.

Khác sketch detect chỉ đọc WHO_AM_I, tính góc cần đánh thức và cấu hình thang đo cảm biến: ±2 g, ±500°/s, lấy mẫu 200 Hz. Không reset thiết bị. Mất ID, dữ liệu bất hợp lệ hoặc không có mẫu mới sẽ báo ERROR và thử khởi tạo/hiệu chuẩn lại; không tiếp tục in các góc cũ như dữ liệu mới.

Có thể mở Serial Plotter với các dòng `Roll:... Pitch:... Yaw:...` sau khi READY. Nếu nguồn cảm biến tụt, sketch này vẫn có thể mất cảm biến; cần kiểm tra phần nguồn riêng.
