# Buổi kiểm chứng phần cứng

Hướng dẫn từng bước, bảng tham số, tiêu chí và xử lý lỗi: [Hướng dẫn cân bằng Roll thực tế](HUONG_DAN_CAN_BANG_ROLL_THUC_TE.md). Tài liệu dưới đây là bản tóm tắt.

## Mang theo

Source/binary đã build, app mới, cáp USB, nguồn/pin đúng loại, dụng cụ đối chiếu góc và đế Roll. Ghi điện áp cho phép, khối lượng drone, hướng đầu và vị trí IMU; hiện chưa có các số đo này.

## 1. Nạp đúng sketch

Mở `DroneRebuild/firmware/DroneRebuild/DroneRebuild.ino`, chọn ESP32 Dev Module với Arduino-ESP32 3.3.12. Giữ các header cùng thư mục. Build qua `scripts/build.ps1` để cập nhật source ID trước khi upload. Script không tự upload. Arduino IDE upload phải dùng đúng binary/source đã ghi nhận.

App mới USB baud 460800; không dùng app cũ baud 115200. Tháo cánh ở bước kiểm tra cảm biến/motor. Reset: motor phải tắt. INFO phải báo đúng source ID, WHO 70/71, profile XW_Z1_SPI_ROLL_PITCH_V2 và pwm_1. Bản 0.2 chọn Roll hoặc Pitch để thử từng trục; xem ROLL_PITCH.md trước khi thử Pitch.

## 2. Cảm biến trước motor

- Giữ đứng yên hết 500 mẫu; nếu chuyển động cửa sổ sẽ bắt đầu lại.
- Ghi 60s đứng yên. Kiểm tra raw accel độ lớn gần 1g, gyro bias và rate sau bù.
- Nghiêng Roll hai phía theo mốc ±5°/±10°: góc và rate phải đúng chiều; ghi sai số dụng cụ.
- Không dùng ZERO để che thân bị nghiêng. Bản 0.2 có ZERO_ROLL/ZERO_PITCH: chỉ lấy mốc sau khi khung ngang thật, motor dừng và đã hiệu chuẩn. Reset/hiệu chuẩn lại xóa mốc.
- Kiểm tra sai số góc tuyệt đối đủ nhỏ cho mục tiêu ±1° trước khi gọi là nghiệm thu góc thật.

## 3. Bench motor, cánh tháo

- Xác nhận Props removed; ARM; chọn từng LF/RF/LR/RR, duty nhỏ; BENCH chỉ 1 giây.
- Ghi đúng motor và chiều quay; kiểm tra STOP, hết thời hạn, đóng app/mất Wi-Fi.
- Chưa xác nhận chiều lực sửa chỉ bằng quay không cánh. Xác nhận lực trên bố trí cơ khí phù hợp riêng.
- Không thay duty hoặc kéo dài thời gian để bỏ qua giới hạn 35%/2s.

## 4. Đế và chiều lực

Chỉ chuyển sang bài có lực đẩy khi có đế Roll phù hợp: Pitch/Yaw/tịnh tiến giữ có kiểm soát, Roll được xoay, có biên cơ khí và mốc ngang, ma sát/mô-men hồi của đế đã ghi nhận. Dây treo không tự tương đương đế một trục.

`motorSign=+1` là giả định phần mềm: output dương tăng nhóm LF/LR. Kiểm tra xem tác động đó có tạo rate Roll dương theo gyro thật không. Nếu khác, chỉnh cấu hình sign rồi kiểm chứng lại; không đổi cả plant sign và controller sign chỉ để đồ thị đẹp.

## 5. Rate rồi Angle

- Gain phần cứng ban đầu 0. Bộ gain trong `rebuild_config.m` chỉ là mô phỏng, không tự Apply.
- Khi các điều kiện nền đã đạt, dùng Kp ứng viên nhỏ có cơ sở; Ki=Kd=0 cho lượt P cơ sở, base phù hợp phần cứng đã kiểm tra.
- RATE target0 kiểm tra chống chuyển động quay; bài xung nhỏ có thời hạn. Mỗi lần Start phải ARM mới.
- Start từ gần ngang (firmware chặn >5°). Mỗi lượt ghi nguồn, base, gain, target, cấu hình đế và góc đầu.
- Chỉ đổi một yếu tố giữa hai lượt so sánh. Nếu chạm biên kéo dài, lệch thêm hoặc rung tăng, Stop và tìm nguyên nhân.
- Qua tiêu chí Rate của PLAN mới ghép Angle P; từ góc nhỏ hai phía, thử lặp lại.

Buổi thử chưa có đế: chỉ hoàn thiện bước 1–3 và logging. Không đánh dấu Stage5–8 đạt từ kết quả máy tính.
