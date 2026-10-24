# Tài liệu tiếng Việt

## Bắt đầu học bộ điều khiển drone

Nếu mới sử dụng, đọc theo thứ tự này để tránh phải hiểu công thức trước khi biết các ô trên app dùng làm gì:

| Thứ tự | Tài liệu | Sau khi đọc, bạn hiểu được gì? |
|---|---|---|
| 1 | [Luồng và các field PID trên WPF](huong-dan-field-pid-wpf.md) | Vì sao cần hai vòng PID; từng ô/nút làm gì; đi từ góc lệch đến PWM bằng một ví dụ |
| 2 | [Cách đọc đồ thị PID](do-thi-pid.md) | Nhìn góc, tốc độ, P/I/D và motor theo đúng thứ tự để giải thích phản ứng |
| 3 | [Thực hành trên đế](pid-can-bang-tren-de.md) | Chuẩn bị, kiểm tra motor, Apply/Arm/Start/Stop và ghi lại từng lượt thử |
| 4 | [Hiệu chỉnh PID chi tiết theo số đo](huong-dan-hieu-chinh-pid-chi-tiet.md) | Chọn base, chỉnh Inner/Outer, đánh giá limit 45/40 và quyết định bằng đồ thị/CSV |
| 5 | [Luồng kỹ thuật và liên hệ code](luong-dieu-khien-cascade.md) | Công thức, mapping trục, giới hạn, thời gian và giao thức khi cần đọc sâu |

Các ví dụ số để học phép tính không phải bộ gain đã được xác nhận cho mọi phần cứng. Mỗi tài liệu tách rõ lệnh bạn nhập, giá trị board xác nhận và dữ liệu cảm biến gửi về.

## Tài liệu lắp ráp và sơ đồ

Bản dịch từ hai PDF trong thư mục `doc`. Bản gốc được giữ nguyên. Thư mục `assets` chứa các hình trích từ bản gốc để đối chiếu.

| Tài liệu gốc | Bản Markdown tiếng Việt | Bản PDF tiếng Việt |
| --- | --- | --- |
| [Drone Kit Assembly and Usage Instructions.pdf](../Drone%20Kit%20Assembly%20and%20Usage%20Instructions.pdf) | [Hướng dẫn lắp ráp và sử dụng](huong-dan-lap-rap-va-su-dung.md) | [Tải PDF](huong-dan-lap-rap-va-su-dung.pdf) |
| [sodo.pdf](../sodo.pdf) | [Sơ đồ nguyên lý — chú giải tiếng Việt](so-do-nguyen-ly.md) | [Tải PDF](so-do-nguyen-ly.pdf) |

- Hướng dẫn gốc có 5 trang, trong đó trang 1, 3, 5 không có nội dung hướng dẫn; bản dịch bao phủ các trang có nội dung (2 và 4).
- PDF sơ đồ có 1 trang. Bản dịch giữ hình mạch và bổ sung bảng dịch nhãn, bảng chân cùng diễn giải riêng; không thay tên net hoặc mã linh kiện trong hình.
- Các ghi chú của người dịch được phân biệt với phần dịch nguồn. Thông tin nhận dạng chip trong ảnh sơ đồ có giới hạn độ nét và chưa được xác minh trên phần cứng.
- Sơ đồ ghi module **MPU6500** dùng **SPI**: SCK GPIO18, MOSI GPIO23, MISO GPIO19, CS GPIO5. Firmware ban đầu dùng MPU6050/I²C; nhánh ESP32 nay đã được cập nhật SPI, xem [hướng dẫn triển khai](../../firmware/ESP32.md).
