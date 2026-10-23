# Chỉ detect MPU qua SPI

Sketch độc lập cho bo XW_Z1_V2.0 trong `doc/sodo.pdf`. Chỉ đọc WHO_AM_I (0x75) mỗi 250 ms, in thời gian và mã nhận dạng. Không reset/ghi cấu hình cảm biến, không hiệu chuẩn, Wi-Fi, quaternion hay PID. Các chân motor 14/15/12/13 giữ LOW.

## Cách dùng

1. Mở `MpuDetect.ino` bằng Arduino IDE.
2. Chọn **ESP32 Dev Module**, đúng cổng COM, **Upload Speed 115200**, rồi Upload.
3. Mở **Serial Monitor, 115200 baud**. Đóng ứng dụng khác đang dùng cùng cổng COM.
4. Quan sát khoảng 30 giây, nhất là thời điểm điện áp cảm biến tụt. Nếu bỏ lỡ đầu log, nhấn EN trên ESP32.

Ví dụ:

```text
MPU_DETECT,500,0x70,MPU6500_FOUND
MPU_DETECT,750,0x70,MPU6500_FOUND
MPU_DETECT,1000,0x00,NO_RESPONSE
```

`0x70`: MPU6500; `0x71`: MPU9250. `0x00`/`0xFF`: không có phản hồi nhận dạng hợp lệ; chưa đủ kết luận cảm biến hỏng. Mã khác được in nguyên dạng để đối chiếu.

Chân theo sơ đồ: SCK=18, MISO=19, MOSI=23, CS=5; nguồn cảm biến 3,3 V và GND chung. Đây là phép thử SPI cho bo drone, không phải sketch I2C cho MPU6050/GY-521 rời.

Sketch này không phát Wi-Fi và không kết nối WPF. Sau khi test, nạp lại `firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino` để dùng ứng dụng.
