# Tài liệu tiếng Việt

Bản dịch từ hai PDF trong thư mục `doc`. Bản gốc được giữ nguyên. Thư mục `assets` chứa các hình trích từ bản gốc để đối chiếu.

| Tài liệu gốc | Bản Markdown tiếng Việt | Bản PDF tiếng Việt |
| --- | --- | --- |
| [Drone Kit Assembly and Usage Instructions.pdf](../Drone%20Kit%20Assembly%20and%20Usage%20Instructions.pdf) | [Hướng dẫn lắp ráp và sử dụng](huong-dan-lap-rap-va-su-dung.md) | [Tải PDF](huong-dan-lap-rap-va-su-dung.pdf) |
| [sodo.pdf](../sodo.pdf) | [Sơ đồ nguyên lý — chú giải tiếng Việt](so-do-nguyen-ly.md) | [Tải PDF](so-do-nguyen-ly.pdf) |

- Hướng dẫn gốc có 5 trang, trong đó trang 1, 3, 5 không có nội dung hướng dẫn; bản dịch bao phủ các trang có nội dung (2 và 4).
- PDF sơ đồ có 1 trang. Bản dịch giữ hình mạch và bổ sung bảng dịch nhãn, bảng chân cùng diễn giải riêng; không thay tên net hoặc mã linh kiện trong hình.
- Các ghi chú của người dịch được phân biệt với phần dịch nguồn. Thông tin nhận dạng chip trong ảnh sơ đồ có giới hạn độ nét và chưa được xác minh trên phần cứng.
- Sơ đồ ghi module **MPU6500** dùng **SPI**: SCK GPIO18, MOSI GPIO23, MISO GPIO19, CS GPIO5. Firmware ban đầu dùng MPU6050/I²C; nhánh ESP32 nay đã được cập nhật SPI, xem [hướng dẫn triển khai](../../firmware/ESP32.md).
