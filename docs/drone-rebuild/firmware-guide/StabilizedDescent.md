# StabilizedDescent.h — giảm collective trong khi giữ PID chạy

Source: [StabilizedDescent.h](../../../firmware/DroneRebuild/StabilizedDescent.h). [Mục lục](README.md).

Class này quản lý một profile hạ bán tự động dùng IMU hiện có. Nó giữ collective hiện tại, Base sàn, tốc độ giảm, target Roll/Pitch và trạng thái pause. Nó không biết độ cao, không tự xác nhận đã đáp và không ghi PWM.

| Function | Vai trò |
| --- | --- |
| `begin()` | Chỉ nhận phiên đúng owner/epoch đang chạy hai trục Angle; kiểm tra config và giới hạn, lưu collective ban đầu |
| `step()` | Theo dt hợp lệ, giảm collective tới sàn và đưa target góc dần về 0 |
| `pause()` | Giữ collective trong khi PID bên ngoài vẫn chạy |
| `resume()` | Tiếp tục giảm nếu còn cao hơn Base sàn |
| `boost()` | Tăng collective trong giới hạn Base ban đầu, rồi pause |
| `reason()` | Trả reason để hiển thị/log trạng thái |
| `reset()` | Xóa profile ở các ranh giới STOP/fault/phiên mới |

`.ino` gọi class này trước khi tính PID/mixer với collective mới. `DualController` vẫn giữ state và nhận số đo mới mỗi lượt. Không có đoạn nào nhân dần toàn bộ correction PID về 0 như ramp cũ.

Hướng dẫn sử dụng và giới hạn: [STABILIZED_DESCENT.md](../STABILIZED_DESCENT.md).
