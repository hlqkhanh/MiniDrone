# WifiSecrets.example.h — mẫu cấu hình router

Source: [WifiSecrets.example.h](../../../firmware/DroneRebuild/WifiSecrets.example.h). Nơi dùng: [DroneRebuild.ino](../../../firmware/DroneRebuild/DroneRebuild.ino).

## 1. Giống file .env.example ở điểm nào?

Nó cho biết cần field tên mạng và mật khẩu. Nhưng chương trình không đọc file `.env` lúc runtime: macro ở đây được xử lý khi compile.

```cpp
#pragma once
#define REBUILD_STA_SSID ""
#define REBUILD_STA_PASSWORD ""
```

SSID là tên mạng. PASSWORD là mật khẩu tham gia mạng đó. Dấu ngoặc kép biểu diễn string C/C++.

## 2. File mẫu không tự được dùng

`.ino` kiểm tra:

```cpp
#if __has_include("WifiSecrets.h")
#include "WifiSecrets.h"
#else
#define REBUILD_STA_SSID ""
#define REBUILD_STA_PASSWORD ""
#endif
```

Nếu có file thật `WifiSecrets.h`, compiler dùng nó; không có thì dùng empty string. File `.example.h` không tự được include chỉ vì nằm cùng thư mục.

Khi cần cấu hình router, tạo file thật từ mẫu. Sửa riêng mẫu mà không tạo file thật sẽ không làm nhánh include dùng nó.

## 3. AP và STA

| Vai trò | Ý nghĩa | Trong dự án |
| --- | --- | --- |
| AP — access point | ESP32 tạo mạng riêng | Laptop nối trực tiếp mạng drone |
| STA — station | ESP32 tham gia router | Laptop và ESP32 có thể giao tiếp qua mạng chung phù hợp |

`setup()` dùng `WIFI_AP_STA`. AP được tạo bằng cấu hình trong `.ino`; SSID STA không rỗng thì gọi thêm `WiFi.begin(...)`.

Điền router không có nghĩa AP tự bị xóa. File mẫu cũng không mở server; TCP server được tổ chức trong `.ino`.

## 4. Vòng đời cấu hình

```text
File thật trên máy → compile → chuỗi nằm trong firmware
                                  ↓ upload
                           setup() gọi WiFi.begin()
```

Đổi mật khẩu trên máy không tự đổi ESP32 đang chạy. Cần build/nạp bản có cấu hình mới. Luồng hiện tại không có portal nhập router runtime.

INFO báo `STA_0.0.0.0` nghĩa là chưa có địa chỉ STA ở thời điểm đó; không tự chứng minh lỗi PID. AP và controller là các phần khác nhau.

## 5. Secrets không có nghĩa tự mã hóa

File thật được `.gitignore` bỏ qua, mẫu có thể chia sẻ. Git ignore không mã hóa string trong firmware và không thay cho bảo vệ cấu hình. Khi chia sẻ tài liệu, dùng giá trị mẫu, không cần đưa mật khẩu thật vào.

Đọc tiếp [DroneRebuild.md](DroneRebuild.md), phần khởi tạo mạng. [Mục lục](README.md).
