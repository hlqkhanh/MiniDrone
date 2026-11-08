# BuildInfo.h — nhận dạng source của bản build

Source: [BuildInfo.h](../../../firmware/DroneRebuild/BuildInfo.h). Script: [build.ps1](../../../scripts/build.ps1).

## 1. Vai trò theo góc nhìn SE

Đây là dấu nhận dạng để đối chiếu bản build khi xem log. Tên project giống nhau chưa đủ biết board đang chạy source nào.

```cpp
constexpr const char* rebuildSourceId = "0757AB9FE1FBFBEB";
```

Đây là string trong source lúc viết tài liệu, không cần học thuộc. `constexpr` là hằng dùng được tại compile time; `const char*` trỏ tới string không sửa qua pointer này. `.ino` đưa string vào INFO.

Không có function chạy định kỳ hoặc điều khiển phần cứng trong file.

## 2. Script tạo nó thế nào?

1. Liệt kê file trong thư mục sketch, loại `BuildInfo.h` và `WifiSecrets.h`.
2. Sắp xếp theo tên, tính SHA-256 của từng file.
3. Ghép tên/hash và hash tiếp dữ liệu tổng hợp.
4. Lấy 16 ký tự hex đầu rồi ghi `BuildInfo.h` trước khi compile.

Nó không phải Git commit ID hoặc chữ ký bảo mật xác thực firmware.

Loại BuildInfo khỏi đầu vào tránh tự hash file chứa chính hash đó. File Wi-Fi thật được loại khỏi dấu nhận dạng theo quy tắc của script.

## 3. Vì sao sửa code mà ID chưa đổi?

Vì bấm Save không tự chạy script sinh ID. Format làm đổi byte của source nên lần sinh ID tiếp theo có thể ra string khác dù logic giữ nguyên.

Upload trực tiếp bằng Arduino IDE khi chưa sinh lại ID có thể đưa source mới nhưng string ID cũ lên board. Timestamp `__DATE__/__TIME__` là dữ liệu compiler tạo riêng, không tự cập nhật hash này.

Vì thế ID giống nhau là dữ kiện đối chiếu, không chứng minh mọi file local hiện tại chưa đổi sau build.

## 4. Không nhầm với lưu trữ runtime

File không lưu PID, không lưu ZERO, không đếm reset, không đo pin và không tự ghi cấu hình vào flash khi chạy. Muốn hiểu INFO được tạo ở đâu, đọc nhánh INFO trong [DroneRebuild.md](DroneRebuild.md).

[Mục lục](README.md).
