# Đọc firmware DroneRebuild từ góc nhìn SE

Quy ước: thuật ngữ IT giữ bằng tiếng Anh, chẳng hạn `field`, `function`, `variable`, `parameter`, `object`, `state`, `queue` và `buffer`. Phần giải thích vẫn viết bằng tiếng Việt.

Bộ tài liệu dành cho người đã biết lập trình nhưng mới làm nhúng (embedded). Mỗi file firmware có một bài riêng: vai trò, dữ liệu vào/ra, luồng chạy, cách đọc các function và ví dụ.

Đối chiếu source ngày **08/11/2026**. Không sửa source trong quá trình viết.

## 1. Một chương trình, không phải bảy firmware độc lập

`DroneRebuild.ino` là sketch Arduino chính. Các header `.h` cung cấp cấu hình, data type và thuật toán. Chúng cùng được biên dịch thành chương trình nạp lên ESP32.

App WPF trên máy tính là một chương trình khác: gửi cấu hình/lệnh và hiển thị dữ liệu. Vòng PID chạy trên ESP32.

| Source | Bài giải thích | Trọng tâm |
| --- | --- | --- |
| `Hardware.h` | [Hardware.md](Hardware.md) | GPIO, SPI, hằng số và hệ trục |
| `DroneRebuild.ino` | [DroneRebuild.md](DroneRebuild.md) | Entry point, task, queue, timer, giao tiếp, PWM |
| `Imu.h` | [Imu.md](Imu.md) | Thanh ghi, dữ liệu cảm biến, bias, quaternion, mốc góc |
| `RollCore.h` | [RollCore.md](RollCore.md) | Config, PID, mixer và state machine |
| `NonBlockingTx.h` | [NonBlockingTx.md](NonBlockingTx.md) | Partial send, retry, buffer gửi |
| `BuildInfo.h` | [BuildInfo.md](BuildInfo.md) | Nhận dạng source và thông tin build |
| `WifiSecrets.example.h` | [WifiSecrets.example.md](WifiSecrets.example.md) | File cấu hình mẫu và AP/STA |

## 2. Kiến thức nhúng tối thiểu

| Khái niệm | Hiểu theo góc nhìn SE | Trong dự án |
| --- | --- | --- |
| Firmware | Chương trình nạp và chạy trên thiết bị | Code Arduino trên ESP32 |
| GPIO | Chân tín hiệu vật lý | Tín hiệu điều khiển motor, chân chọn chip |
| SPI | Giao thức trao đổi byte có clock/chân chọn thiết bị | Đọc thanh ghi IMU |
| PWM | Tín hiệu bật/tắt lặp nhanh, thay đổi tỷ lệ thời gian bật | Lệnh công suất motor |
| Task | Luồng thực thi do RTOS lập lịch; không đồng nhất với `Task` bất đồng bộ của C# | `controlTask()` |
| Queue | Queue truyền message giữa các ngữ cảnh | `commands`, `replies`, `samples` |
| Timer callback | Function được hệ thống gọi theo lịch | `guard()` |
| Critical section | Đoạn đọc/ghi dữ liệu chung có bảo vệ tương tranh | Truy cập Gate |
| Atomic | Thao tác nguyên tử trên variable theo data type/API | Counter, `pwmReady` |
| Real-time | Kết quả phải phù hợp cả về giá trị lẫn thời điểm | Output đúng nhưng quá cũ vẫn bị từ chối |

Real-time không có nghĩa chu kỳ luôn chính xác tuyệt đối. Code đặt lịch, đo thời gian và giám sát; độ trễ/jitter thực còn cần đo.

## 3. Ba nhịp khác nhau

| Công việc | Nhịp cấu hình/danh định | Ý nghĩa |
| --- | --- | --- |
| Lấy mẫu/tính điều khiển | Khoảng 200 Hz, tức 5 ms | Cập nhật quyết định điều khiển |
| Guard | Timer yêu cầu mỗi 5 ms | Giám sát và cập nhật lệnh PWM |
| Sóng PWM | 20 kHz | Tần số bật/tắt phần cứng, không phải số lần tính PID |

Telemetry gửi 50 hoặc 100 Hz. Hai dòng log liên tiếp không nhất thiết là hai lần tính PID liên tiếp.

## 4. Bức tranh chạy chung

```text
WPF → USB/TCP → loop()/receive() → commands queue
                                      ↓
controlTask(): xử lý lệnh → đọc IMU → tạo góc/rate
                                      ↓
                     Controller / DualController
                                      ↓
                               publish() → Gate
                                             ↓
                                         guard() → PWM

controlTask() → replies/samples queue → loop() → USB/TCP → WPF
```

Không phải app vẽ xong đồ thị thì ESP32 mới tính PID tiếp. Giao tiếp, tính toán và giám sát là những trách nhiệm riêng.

## 5. Những cú pháp C++ hay gặp

| Cú pháp | Cách đọc trong source |
| --- | --- |
| `namespace hw` / `rebuild` | Gom tên vào phạm vi, gọi như `hw::baud` |
| `struct Config` | data type chứa dữ liệu cấu hình và function kiểm tra |
| `const Config& c` | Tham chiếu chỉ đọc, không copy cả cấu hình |
| `Config& cfg = configs[axis]` | Bí danh của element đang chọn |
| `Config next = cfg` | Tạo bản copy để parse/validate trước khi nhận |
| `[&](...) { ... }` | Lambda dùng variable xung quanh qua reference |
| `*this = Controller{}` | Gán state mặc định của Controller mới vào object hiện tại |
| `#include`, `#if`, `#define` | Tiền xử lý/biên dịch, không phải câu lệnh chạy mỗi chu kỳ |
| `#pragma once` | Tránh include lại nội dung header trong cùng đơn vị biên dịch |

## 6. Lộ trình đọc

Đọc Hardware trước để nắm chân và trục. Tiếp theo đọc `setup()` và sơ đồ tổng thể trong bài DroneRebuild. Khi gặp một class, chuyển sang bài Imu/RollCore rồi quay lại luồng chính.

Nếu chỉ tập trung PID, đọc `Config → Output → Controller::step()` trước; để `Session` sau. Các bài NonBlockingTx, BuildInfo và Wi-Fi có thể đọc độc lập.

Tham khảo: [tóm tắt các file](../FIRMWARE_FILES_OVERVIEW.md), [công thức PID](../PID_FORMULAS_AND_WORKED_EXAMPLES.md), [ảnh kiến trúc](../CURRENT_SYSTEM_ARCHITECTURE.md).
