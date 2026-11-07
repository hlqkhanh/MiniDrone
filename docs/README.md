# Tài liệu Drone

Đây là thư mục tài liệu duy nhất của dự án. Dự án hiện dùng toàn bộ DroneRebuild; tài liệu hướng dẫn DroneMotion cũ đã được bỏ khỏi bộ tài liệu đang dùng.

## Nên đọc từ đâu?

1. [Drone giữ cân bằng ở 0° như thế nào?](theory/Can-bang-drone-0-do.md)
2. [PID từ cơ bản đến ứng dụng](theory/PID-controller.md)
3. [Đọc firmware DroneRebuild](drone-rebuild/DroneRebuild_Firmware_Guide.md)
4. [Giao thức và hành vi của firmware](drone-rebuild/PROTOCOL.md)
5. [Thử và hiệu chỉnh Roll trên phần cứng](practice/HUONG_DAN_CAN_BANG_ROLL_THUC_TE.md)

Hướng dẫn thực hành và nhật ký có ghi các mốc phiên bản cũ của **DroneRebuild**. Khi gặp mô tả khác nhau, đối chiếu giao thức và source đang dùng; không lấy giá trị minh họa làm bộ PID đã được xác nhận bay.

## Phân loại

| Thư mục | Nội dung |
| --- | --- |
| [theory/](theory/) | Cân bằng 0°, PID, bộ bài học Drone Simulation and Control phần 1–5 |
| [drone-rebuild/](drone-rebuild/) | Giải thích firmware, giao thức, Roll/Pitch |
| [practice/](practice/) | Trình tự kiểm tra và hiệu chỉnh trên phần cứng |
| [hardware/](hardware/) | Sơ đồ nguyên lý, lắp ráp, PDF gốc và bản dịch, ảnh phần cứng |
| [simulation/](simulation/) | MATLAB/Simulink của DroneRebuild |
| [references/](references/) | PX4 và nghiên cứu điều khiển ba trục |
| [history/](history/) | Kế hoạch, trạng thái kiểm chứng, nhật ký và ghi nhận chuyển thư mục |

## Bài học và tài liệu tham khảo

- Drone Simulation and Control: [Phần 1](theory/drone-control/drone_control_part1.md), [Phần 2](theory/drone-control/drone_control_part2.md), [Phần 3](theory/drone-control/drone_control_part3.md), [Phần 4](theory/drone-control/drone_control_part4.md), [Phần 5](theory/drone-control/drone_control_part5.md).
- PX4: [Bản dịch đầy đủ kèm sơ đồ](references/px4/px4-so-do-bo-dieu-khien.md), [giải thích chi tiết](references/px4/giai-thich-chi-tiet-so-do-dieu-khien-px4.md).
- Phần cứng: [Sơ đồ nguyên lý](hardware/so-do-nguyen-ly.md), [hướng dẫn lắp ráp của bộ kit](hardware/huong-dan-lap-rap-va-su-dung.md). Giao diện/phần mềm trong bản dịch bộ kit thuộc nhà cung cấp, không phải app DroneRebuild.
- Mô phỏng: [Hướng dẫn MATLAB/Simulink](simulation/SIMULINK.md).
- Ba trục: [Phân tích giải pháp](references/three-axis/GIAI_PHAP_CAN_BANG_3_TRUC_ESP32_MPU6050.md), [danh mục nguồn](references/three-axis/TAI_LIEU_ESP32_MPU6050_3_TRUC.txt).
- Nhật ký: [Thử PID ngày 29–30/10/2026](history/NHAT_KY_THU_PID_2026-10-29_30.md).

Chi tiết tài liệu cũ đã bỏ và cách sắp xếp: [ghi nhận ngày 07/11/2026](history/SAP_XEP_TAI_LIEU.md).
