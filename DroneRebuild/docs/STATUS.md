# Trạng thái kiểm chứng — 26/10/2026

## Đã chạy trên máy Windows

- Test C++ controller/session: gain=0 chặn chạy, phản hồi rate, D không kick theo setpoint, dấu mixer, sensor/dt/envelope/target lỗi, chống windup, bão hòa kéo dài, reset output, owner và epoch, ARM timeout, heartbeat timeout.
- Test Madgwick bằng header firmware thật và stubs bus: đứng yên 60s, pose ±10°, chiều gyro, đầu vào/dt không hợp lệ.
- Test wrapper S-function bằng host harness: gọi outputs nhiều lần không tích phân thêm, commit ở update, fault giữ chốt và motor về 0. Đây không phải kiểm thử engine Simulink.
- 18 mô phỏng native dùng đúng RollCore.h: lực motor 0.5/1/1.5 lần, lag 20/60/120ms, góc đầu ±3°. Tất cả cuối lượt đạt |Roll|<1°, |rate|<2°/s; chưa dùng kết quả này để tuyên bố toàn bộ tiêu chí Stage7 đạt.
- Plant nominal, xung rate ±5°/s theo PLAN, đo MAE sau 100ms đầu mỗi xung: Kp0.4 ≈2.667°/s, Kp0.8 ≈1.009°/s, Kp1.2 ≈0.562°/s. Đây là một mô hình giả định; MAE thấp hơn không chứng minh Kp1.2 tốt hơn tổng thể hoặc phù hợp phần cứng.
- Firmware build bằng Arduino-ESP32 3.3.12, `espressif:esp32:esp32`, warnings all: thành công, không thấy compiler warning. Khoảng 956KB flash, 49KB global RAM; runtime task/queue/stack chưa đo thực tế.
- App .NET8 WPF build Release: thành công, 0 warning/error. Smoke test mở window ngoài màn hình, phát lại 2000 mẫu synthetic CSV, render được PNG và thoát thành công. Chưa kiểm tra USB/TCP với ESP32 thật.

Lõi C++ canonical là `RollCore.h`; S-function include trực tiếp cùng header. Fallback MATLAB là bản chuyển riêng và có 3600 vector đối chiếu. Không coi hai bản đã khớp trong MATLAB cho đến khi `verify_controller` chạy đạt ở MATLAB Online.

## Chưa chạy ở môi trường này

- MATLAB Online: biên dịch MEX, tạo `.slx`, đối chiếu 3600 vector và chạy bộ bài Simulink. Script đã chuẩn bị, người dùng sẽ chạy trong tài khoản Online. Máy hiện không cài MATLAB/Octave.
- Các bài chủ động lỗi IMU/mất mạng/tải mạng với board thực, đo độ trễ cắt PWM, reset motor, data-ready và sample rate thực tế.
- Xác nhận bo, wiring, WHO_AM_I, hướng trục/góc, chiều lực motor, bias/nhiễu, nguồn/pin và đế.
- Nghiệm thu Rate rồi Angle theo PLAN; mô hình torque/duty và quán tính chưa được nhận dạng.

## Giới hạn v0.1

- Mô hình một trục dùng góc sensor lý tưởng có trễ và gyro nhiễu tùy chọn; chưa mô phỏng đầy đủ IMU→Madgwick trong vòng kín.
- Router SSID cấu hình bằng header trước build, chưa có portal quản lý.
- Chưa có model 3D, đo điện áp hay phản hồi RPM/lực từng motor. App tập trung vào đồ thị và log.
- Telemetry `missing` là not-ready poll counter, không phải phép đo mọi mẫu cảm biến thất lạc.
- Logging lưu toàn bộ T và sự kiện/INFO/TX riêng. Một số fixed limits nằm trong source ID thay vì nhắc lại mọi dòng T.
- Các stage có điều kiện hardware chưa được đánh dấu đạt. Không tự apply bộ gain mô phỏng hoặc coi mô phỏng là chứng nhận bay.

## Cập nhật từ MATLAB Online của người dùng

- Ảnh người dùng xác nhận MEX g++ build thành công, mô hình đã tạo và 3600 vector C++/MATLAB PASS; sai khác tối đa 5.68e-14.
- Bộ bài dừng ở đọc log: dùng `out.tout` để index dữ liệu To Workspace gây sai độ dài. Đã sửa sang Timeseries lấy timestamp cùng tín hiệu, bỏ giới hạn số điểm/decimation, kiểm tra đủ 17 cột và đủ thời gian bài thử. Chưa có kết quả chạy lại sau sửa.
- Cảnh báo đăng ký toolchain Raspberry Pi xuất hiện trước lỗi nhưng mô phỏng đã đi đến đọc kết quả; chưa có bằng chứng cảnh báo đó là nguyên nhân dừng.

### Kết quả chạy lại do người dùng cung cấp

Ảnh MATLAB Online sau cập nhật script xác nhận:

| Case | Pass | FaultCode | FractionPidLimited |
|---|---|---|---|
| angle_nominal | true | 0 | 0 |
| rate_pulses | true | 0 | 0 |
| weaker_slow | true | 0 | 0 |
| reversed_motor | true | 4 | 0.026487 |
| gyro_noise | true | 0 | 0 |

Đối chiếu 3600 vector vẫn PASS, sai khác tối đa 5.68e-14. Đồ thị nominal cho góc từ +3° về gần 0°. Fault4 là ENVELOPE của bài cố ý đảo dấu vật lý: PASS ở đây nghĩa là phát hiện và chốt dừng, không phải controller cân bằng được khi đấu sai motor.

Kết quả trên là ảnh xác nhận của người dùng, chưa có file dữ liệu thời gian từ MATLAB Online được tải về để phân tích thêm. Bộ bài Simulink giả định đã chạy đạt; nghiệm thu hardware, mô hình nhận dạng và các stage cơ khí vẫn chưa đạt/chưa kiểm tra.

## Cập nhật giao diện 27/10/2026

- Chia cột cấu hình và vùng số đo; tab Roll/bench, advanced settings thu gọn, ARM/START ghim dưới và STOP luôn hiển thị.
- Bốn chart riêng; không vẽ command rate như góc target trong RATE mode. Nhãn mục tiêu đổi đơn vị theo mode.
- Trạng thái, dữ liệu mô phỏng, cấu hình board đọc lại và điều kiện thao tác được thể hiện rõ hơn; nút chưa đủ điều kiện bị khóa.
- Build Release WPF thành công, 0 warning/error. Render smoke trên CSV synthetic: Roll, bench, empty/disconnected, kích thước 1320×900 và 1040×740. Kiểm tra ảnh không thấy thành phần bị tràn ngang; phần cấu hình cuộn riêng.
- Chưa kiểm tra thao tác với ESP32 thực trong lần cập nhật UI. Firmware/controller không thay đổi bởi cập nhật này.
