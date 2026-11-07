# Chuyển DroneRebuild thành dự án duy nhất

Ngày 07/11/2026, toàn bộ nội dung `Drone/DroneRebuild/` được đưa lên `Drone/` theo yêu cầu của người dùng.

| Trước | Sau |
| --- | --- |
| `Drone/DroneRebuild/firmware/DroneRebuild/` | `Drone/firmware/DroneRebuild/` |
| `Drone/DroneRebuild/app/` | `Drone/app/` |
| `Drone/DroneRebuild/scripts/` | `Drone/scripts/` |
| `Drone/DroneRebuild/simulation/` | `Drone/simulation/` |
| `Drone/DroneRebuild/tests/` | `Drone/tests/` |
| `Drone/DroneRebuild/docs/` | `Drone/docs/` |
| `Drone/DroneRebuild/build/` | `Drone/build/` |

Firmware, file C#/XAML, controller, simulation và tests được chuyển nguyên trạng. Tên sketch và namespace không đổi. Chỉ sửa đường dẫn model trong `app/DroneRebuild.App.csproj`, thư mục công cụ trong `scripts/build.ps1`, quy tắc Git và tài liệu điều hướng. Không ghép logic từ DroneMotion vào DroneRebuild.

Bản DroneMotion cũ (src, firmware, tests, solution, README, PLAN và kết quả cũ) được chuyển sang `MiniDrone/.archive/Drone-legacy-20261107-185012/`, nằm ngoài repo Drone. Thư mục này có manifest SHA-256 của các file DroneRebuild trước khi chuyển. Lịch sử Git cũng vẫn còn nguyên.

Model 3D trong `asset/`, log trong `logs/`, báo cáo trong `reports/`, công cụ dùng chung trong `.tools/` và `.packages/` được giữ lại. Sau bước chuyển dự án, `doc/` và `docs/` tiếp tục được gộp thành một bộ tài liệu; xem [mục lục hiện tại](../README.md) và [ghi nhận sắp xếp tài liệu](SAP_XEP_TAI_LIEU.md).

Các file sinh tự động đã có trong `build/` và `app/obj/` có thể chứa đường dẫn cũ; build lại ứng dụng khi cần. Arduino IDE cần mở sketch tại đường dẫn mới trước khi nạp.
