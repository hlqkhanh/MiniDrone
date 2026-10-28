# Roll / Pitch v0.2 — kiểm chứng từng trục trên đế

**Thay đổi 28/10/2026:** START chạy liên tục, bỏ điều kiện góc ±5°, duration và dừng theo ENVELOPE/OTHER_AXIS_ENVELOPE/SATURATION_TRACKING. Output và chống windup vẫn giới hạn theo cấu hình. BENCH vẫn hữu hạn; dùng PROTOCOL.md cho giao thức hiện tại.

## Cập nhật

Nạp lại firmware/DroneRebuild/DroneRebuild.ino và mở app mới. INFO cần báo DroneRebuild-0.2, profile XW_Z1_SPI_ROLL_PITCH_V2. App cũ không đọc telemetry 50 trường mới. Không tự upload hay chạy motor trong bước build.

## Kiểm tra cảm biến trước motor

1. Motor dừng, tháo cánh; cố định IMU gần song song với khung.
2. Đặt khung ngang, hiệu chuẩn IMU và đợi hoàn tất. Nếu cần, chọn Roll rồi lấy mốc 0°, chọn Pitch rồi lấy mốc 0°. Reset nguồn/hiệu chuẩn lại xóa cả hai mốc.
3. Nghiêng trái/phải: Roll phải thay đổi chính; chúi/ngẩng: Pitch phải thay đổi chính. Nếu hai trục bị nhầm hoặc dấu tốc độ không cùng dấu biến thiên góc gần ngang, chưa ARM; kiểm tra mounting.
4. Giữ khung ngang và xoay quanh phương thẳng đứng (Yaw) chậm. Roll/Pitch phải gần mốc ban đầu sau khi đứng yên. Nếu tăng rõ hoặc còn lệch sau khi dừng, ghi log gồm INFO/LEVEL và đoạn trước–trong–sau khi xoay. Cần phân biệt tay làm khung nghiêng, trục lắp sai, dây kéo khung, gia tốc khi xoay và lỗi cảm biến.

## Chạy trên đế

1. Chọn Roll hoặc Pitch ở mục Trục thử. Đây là hai cấu hình riêng trong RAM; mặc định gain của cả hai bằng 0. App giữ riêng các ô nhập trong phiên, không tự chép gain Roll sang Pitch.
2. Đế phải cho phép chuyển động đúng trục đang chọn và giữ chắc các trục còn lại. Không thử khi cầm tay hoặc đặt khung tự do trên bàn.
3. Xác minh vị trí LF/RF/LR/RR và chiều lực từng trục. Roll sửa trái–phải; Pitch sửa trước–sau. Motor sign có thể khác giữa hai trục, không suy ra từ gain mô phỏng.
4. Điền thông số trục đang chọn, bấm Áp dụng, kiểm tra dòng Board Roll/Pitch đã nhận đúng cấu hình. Đánh dấu lại xác nhận đế/trục/chiều lực. Mỗi lần đổi trục/lấy mốc phải xác nhận lại.
5. Tune RATE trước ANGLE như hướng dẫn Roll, dùng lượt ngắn trên đế; dừng nếu lực làm tăng lệch. Không dùng bộ MATLAB như thông số phần cứng đã được xác nhận.
6. START không còn kiểm tra ngưỡng góc. Khi chạy không cắt motor theo góc/tốc độ hoặc bão hòa; cờ PID/mixer chạm biên vẫn hiển thị. Esc/STOP, watchdog và heartbeat vẫn hoạt động. START không có thời hạn; BENCH vẫn hữu hạn.

## Góc và Yaw

V0.1 lấy −EulerY của sensor làm Roll, có hiện tượng gập số tại 90° và chưa đúng phép đổi hệ trục khi phối hợp nhiều góc. V0.2 đổi quaternion sang hệ body trước khi lấy Euler; Roll có khoảng ±180°, Pitch ±90°. Euler vẫn suy biến gần Pitch ±90°, và mốc bù vô hướng chỉ dành cho góc nhỏ gần ngang. Yaw tuyệt đối không đo được với accelerometer+gyro, vì chưa dùng magnetometer.

Xoay quanh trục Z của khung khi khung đã nghiêng không nhất thiết tương đương xoay quanh phương thẳng đứng: các góc Euler có thể thay đổi thật. Vì vậy bài kiểm tra Yaw cần giữ mặt phẳng khung ngang và xét số đo sau khi đứng yên, không chỉ nhìn đỉnh nhiễu lúc xoay.

## Phạm vi kiểm chứng

Test native kiểm tra hướng mixer Pitch, hai dấu motor, góc Roll/Pitch kết hợp và giữ tilt qua 180° world-Yaw giả lập. Bộ mô phỏng động lực/Simulink hiện vẫn là Roll, chưa nhận dạng plant Pitch hoặc nghiệm thu phần cứng. Đây chưa phải firmware bay tự do hay điều khiển đồng thời Roll+Pitch.
