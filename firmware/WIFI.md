# Kết nối Wi-Fi Direct với ESP32

Firmware chỉ chạy `WIFI_AP`: ESP32 tự phát Wi-Fi để laptop nối trực tiếp. Không cần router, hotspot hoặc Internet. Thông tin router lưu bởi firmware cũ không được đọc, không cần xóa flash. Các file WifiProvisioner.h và WifiPortalPage.h cũ được giữ để tham khảo, không được include hoặc chạy trong sketch hiện tại.

## Nạp và kết nối

1. Mở `firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino` bằng Arduino IDE, giữ toàn bộ header cùng thư mục, gồm DirectWifi.h.
2. Chọn ESP32 Dev Module, ESP32 core 3.x và đúng COM rồi Upload. Disconnect app và đóng Serial Monitor trước khi nạp.
3. Giữ board yên để hiệu chuẩn. Laptop nối Wi-Fi **DroneMotion_Setup-XXXX**, mật khẩu **drone12345**. Bốn ký tự cuối phụ thuộc MAC AP nên có thể khác tên ở firmware cũ.
4. Windows báo không có Internet thì chọn giữ kết nối. Trong WPF bật Wi-Fi, dùng IP **192.168.4.1**, port **3333**, bấm Connect.
5. Chờ hiệu chuẩn hoàn thành và Data khoảng 100 samples/s. Nghiêng board kiểm tra roll/pitch/yaw. Zero orientation đổi mốc hiển thị; Calibrate cần board yên để đo gyro bias.
6. PID, manual motor và CSV dùng cùng kết nối. Xem [hướng dẫn PID trên đế](../doc/vi/pid-can-bang-tren-de.md).

Không mở `http://192.168.4.1`: firmware hiện không chạy web server hoặc DNS captive portal. TCP 3333 dùng cho WPF, không mở bằng trình duyệt. WPF không còn lựa chọn Router/hotspot. USB serial vẫn dùng được khi bỏ chọn Wi-Fi.

## Trạng thái và khắc phục

Serial Monitor 115200, lệnh INFO (Newline) in:

```text
STATUS,WIFI_DIRECT_ONLY
STATUS,WIFI_AP_DroneMotion_Setup-XXXX
STATUS,TCP_AP_192.168.4.1:3333
```

- AP dùng kênh 1, không quét mạng hoặc tự reconnect router. Chỉ nhận một WPF client mỗi lần.
- Thiếu heartbeat khoảng 2 giây dừng motor; watchdog PID vẫn độc lập. Kết nối lại không tự chạy motor, cần Arm/Start lại.
- TCP EOF giải phóng socket đóng bình thường. Nghẽn gửi vẫn có bộ đệm và timeout 20 giây. Log USB có `ERROR,TCP_SEND_FAILED,<errno>` hoặc `ERROR,TCP_SEND_STALLED_20S,<errno>`.
- Data đứng nhưng Wi-Fi còn: đọc trạng thái cảm biến/hiệu chuẩn trên app hoặc USB. Nếu tên mạng biến mất, kiểm tra nguồn và log reset `rst:`, `Brownout`, `Guru Meditation`. Direct-only chưa chứng minh loại bỏ lỗi nguồn hoặc cảm biến.
- Không thấy AP: tìm `ERROR,WIFI_DIRECT_AP_FAILED` trên USB, kiểm tra nguồn và reset board.
- Wi-Fi không cấp điện. Khi rút USB cần nguồn riêng phù hợp; nếu board reset, chờ hiệu chuẩn rồi Connect lại.

WifiConfig.example.h đặt port 3333. WifiConfig.local.h nếu có chỉ ghi đè port, không cấu hình router.

## Kiểm tra trên máy tính

```powershell
dotnet test .\DroneMotion.sln
g++ -std=c++17 -I tests/firmware/direct-wifi-stubs tests/firmware/direct-wifi.test.cpp -o "$env:TEMP/minidrone-direct-test.exe"
& "$env:TEMP/minidrone-direct-test.exe"
```

Test dùng Wi-Fi giả lập để kiểm tra AP-only và lỗi khởi tạo. Cần thử board thật để xác nhận độ ổn định sóng và nguồn.
