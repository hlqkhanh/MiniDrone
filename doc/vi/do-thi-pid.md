# Đồ thị PID trực tiếp

Phần đồ thị được tích hợp từ commit `767bc8c` trên remote. Giữ PID hai tầng, chiều Roll đã chỉnh, góc đo không bị chặn Start/ngắt theo góc và hai chế độ kết nối Wi-Fi hiện có của hệ thống Drone.

## Sử dụng

- Chạy app WPF mới, mở tab **Live PID plots** cạnh **3D orientation**.
- Chọn **Plot axis** là ROLL hoặc PITCH; thao tác này chỉ đổi dữ liệu vẽ, không đổi trục điều khiển motor.
- **Pause plots** dừng cập nhật mẫu vào đồ thị, không dừng motor hoặc ghi CSV. **Clear plots** xóa lịch sử hiển thị, không thay đổi PID.
- Lịch sử giữ tối đa 600 mẫu và 30 giây. Sau Start hoặc reset trạng thái kết nối, lịch sử được xóa.
- Nhãn **LIVE**, **PAUSED**, **NO NEW DATA** giúp phân biệt dữ liệu đang cập nhật, đang tạm dừng và quá 2 giây chưa có bản tin mới.

## Bốn biểu đồ

| Biểu đồ | Các đường |
|---|---|
| ANGLE | Góc đo và target góc, đơn vị độ |
| BODY RATE | Tốc độ gyro và tốc độ mục tiêu từ PID góc, đơn vị độ/giây |
| RATE PID | P, I, D và output đã giới hạn của vòng tốc độ, đơn vị duty |
| MOTOR OUTPUT | PWM LF/RF/LR/RR trên thang duty 0–255 |

Đồ thị dùng góc firmware, độc lập với Zero orientation của mô hình 3D. Các đường không phải dữ liệu mô phỏng. Firmware này vẫn giới hạn output motor 0–220; trục đồ thị PWM hiển thị toàn thang 0–255.

## Telemetry mới và tương thích

ESP32 giữ nhịp điều khiển mục tiêu 200 Hz và quaternion 100 Hz, gửi điều khiển `C2` khoảng 20 Hz. C2 giữ 19 giá trị đầu của bản tin C cũ và thêm bốn giá trị:

```text
C2,micros,roll,pitch,rollRate,pitchRate,rollTarget,pitchTarget,
rollP,rollI,rollD,pitchP,pitchI,pitchD,LF,RF,LR,RR,saturated,active,
rollRateTarget,pitchRateTarget,rollOutput,pitchOutput
```

Bản tin thực tế là một dòng, gồm 23 giá trị sau nhãn C2. CSV app mới có thêm bốn cột đó trước các cột gain; gain vòng góc vẫn được giữ.

App cũng đọc C cũ gồm 19 giá trị và C2 từ commit remote gồm 25 giá trị. Hai trường recovery riêng của remote không được áp dụng vào bộ điều khiển hiện tại. Với C cũ, các đường tốc độ mục tiêu và output giới hạn không có mẫu để vẽ; CSV để trống bốn cột mới. Khi đọc C2 25 giá trị, CSV không ghi hai trường recovery ngoài schema hiện tại.

Không tích hợp thay đổi Wi-Fi hoặc cơ chế recovery/góc của remote. Muốn có đầy đủ đường đồ thị với hệ thống hiện tại cần nạp sketch mới; chỉ cập nhật app vẫn xem được những trường mà firmware cũ gửi.

Đồ thị và kiểm thử máy tính không xác nhận chiều motor hoặc độ ổn định trên phần cứng. Dùng STOP / DISARM để dừng motor; Pause/Clear đồ thị không có tác dụng đó.
