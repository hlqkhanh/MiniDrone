# Sắp xếp tài liệu — 07/11/2026

Gộp `Drone/doc/`, `Drone/docs/` và hai file trùng trong `MiniDrone/docs/` về một thư mục `Drone/docs/`. Hai bản PID-controller.md và Can-bang-drone-0-do.md được đối chiếu SHA-256 và trùng hoàn toàn, nên mỗi tài liệu chỉ giữ một bản.

## Tài liệu DroneMotion cũ đã bỏ

- `so-do-luong-code-drone.md` cùng PNG/SVG của sơ đồ: mô tả firmware DroneMotion, FlightController.h và WPF cũ.
- `giai-thich-flight-controller.md`: giải thích FlightController.h đã được thay thế.
- `luong-dieu-khien-cascade.md`: luồng code DroneMotion cũ.
- `huong-dan-field-pid-wpf.md`, `do-thi-pid.md`: giao diện, giới hạn và telemetry C/C2 của app cũ.
- `pid-can-bang-tren-de.md`, `huong-dan-hieu-chinh-pid-chi-tiet.md`: quy trình dành cho firmware/app cũ.
- README tiếng Việt cũ dẫn tới các tài liệu trên.

Các file này và hai bản trùng được lưu ngoài repo tại `MiniDrone/.archive/docs-retired-20261107-185648/`. Thư mục lưu trữ có danh sách đường dẫn trước/sau và mã băm code để đối chiếu.

## Tài liệu giữ lại

Giữ lý thuyết PID/cân bằng, năm bài Drone Simulation and Control và ảnh/script đi kèm, tài liệu PX4 cùng toàn bộ sơ đồ, sơ đồ điện và tài liệu bộ kit, tài liệu DroneRebuild, nghiên cứu ba trục và nhật ký.

Phần đối chiếu với kiến trúc DroneMotion cũ trong bài học phần 3 và liên kết tới sơ đồ cũ trong phần 4–5 được thay bằng liên kết tài liệu DroneRebuild. Phần giải thích PID theo simulator là tài liệu mô phỏng riêng, không phải kiến trúc DroneMotion nên vẫn giữ.

Liên kết Markdown, đường dẫn ảnh, PDF và log được cập nhật theo vị trí mới. Script đóng gói chỉ được cập nhật vị trí tài liệu Simulink; firmware, logic app, controller, tests và mô phỏng không thay đổi.
