# Trạng thái kiểm chứng — cập nhật 27/10/2026

## Bổ sung 28/10/2026 — bỏ góc START và cắt do bão hòa

- Log phần cứng người dùng: START đã OK, sau đó motor dừng với SATURATION_TRACKING. Bản F7F5DF8CECA27C50 còn điều kiện START ±5° và bảo vệ bão hòa.
- Bản mới bỏ cả điều kiện góc START và cắt motor SATURATION_TRACKING. Duty, pidLimited/mixerLimited và chống windup vẫn giữ; watchdog/STOP/fault cảm biến vẫn hoạt động. App không cần thay đổi thêm.
- Native controller và wrapper PASS, gồm giữ điều khiển qua 12 giây PID saturation/mixer clipping cho Roll/Pitch, PWM hữu hạn và I không vượt giới hạn. 18 mô phỏng Roll PASS. Đã cập nhật fallback MATLAB/vector; chưa chạy MATLAB đối chiếu lại.
- Firmware build PASS với ESP32 core 3.3.12, source ID `CD7A4E0DEE7905EB`, flash 959811 bytes, global RAM 49572 bytes. Chưa nạp bản này vào board. Các mục dưới mô tả các bản trước.

## Cập nhật 28/10/2026 — START liên tục, bỏ envelope

- RATE/ANGLE/DryRun không còn thời hạn START; duration trên giao thức được giữ nhưng bỏ qua, session/gate dùng deadline=0. Bỏ ENVELOPE và OTHER_AXIS_ENVELOPE khi đang chạy. START vẫn cần cả hai góc trong ±5°. BENCH vẫn có thời hạn, các watchdog/STOP và fault khác vẫn hoạt động.
- Native regression PASS: Roll/Pitch vượt ngưỡng góc/tốc độ cũ không tự fault; session liên tục chạy gần 60 giây với heartbeat/sample mới; STOP, mất heartbeat, stale-control và deadline BENCH vẫn dừng. Controller/IMU/S-function/TCP và 18 mô phỏng Roll đều PASS. Đã cập nhật fallback MATLAB và 3600 vector C++; chưa chạy đối chiếu MATLAB trong phiên này.
- App bỏ ô Duration, START gửi 0. Release build vào `build/app-continuous` thành công 0 warning/error; smoke render 1040×740 PASS. Bản app đang mở khóa executable ở thư mục bin cũ, nên bản mới nằm riêng, chưa thay phiên đang chạy.
- Firmware ESP32 core 3.3.12 build PASS, source ID `F7F5DF8CECA27C50`, flash 960155 bytes, global RAM 49572 bytes. Chưa upload hoặc thử motor trên board.

Các mục bên dưới ghi lại hành vi/kết quả trước thay đổi này.

## Bản 0.2 Roll / Pitch

- Chọn Roll hoặc Pitch, mỗi lượt một trục trên đế. Hai cấu hình PID/sign/limit và hai mốc ngang độc lập; mặc định gain đều 0. Pitch dùng chênh lực trước–sau. Chưa điều khiển đồng thời hai trục hoặc bay tự do.
- Sửa phép tính góc: đổi quaternion sensor sang body (w,-y,x,z), rồi lấy ZYX Euler. Roll dùng atan2, Pitch asin. Không còn dùng −sensor EulerY như v0.1. Hardware mounting vẫn cần xác minh.
- Mốc 0° yêu cầu mẫu mới, đã hiệu chuẩn, đứng yên ở cả ba gyro, motor dừng. START kiểm tra cả hai góc ±5°; khi chạy, trục không điều khiển vượt ±15°/±150°/s cũng dừng. Đổi trục/lấy mốc xóa VERIFY/reset PID.
- T50 bổ sung axis và hai cặp góc/rate. App đọc T45 cũ cho Roll hoặc T50 mới; biểu đồ hai góc, tốc độ/PID trục đang chạy, chọn trục không tự sao chép gains.
- Native tests PASS: mixer Pitch/sign, mapping góc kết hợp Roll/Pitch, giữ tilt qua world-Yaw180° ở 9 tư thế (sai số <0.15°), Roll qua90°, bù mốc và toàn bộ test/simulation Roll trước đó.
- Firmware compile PASS: source ID BCF398CC0AD7C3AD, flash960483 bytes, global RAM49572 bytes. WPF Release build0 warning/error, render Pitch1320×900 và1040×740 PASS. Loopback PASS cả T50 Pitch (mapping, history reset khi đổi trục, loại NaN) và các ca kết nối cũ; không kết nối hoặc ARM drone trong test.
- Người dùng đã xác nhận kết nối Wi-Fi/IMU trên bản trước và chỉnh lại cơ khí IMU. Bản 0.2 chưa được nạp/thử nghiệm thu vật lý. Mô hình động lực/Simulink vẫn là Roll, chưa nhận dạng plant Pitch.

Các mục bên dưới ghi lại lịch sử v0.1; dùng ROLL_PITCH.md và PROTOCOL.md cho hành vi hiện tại.

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

## Sửa kết nối USB/TCP 27/10/2026

- Phát hiện từ source Arduino-ESP32 3.3.12: `NetworkClient` không override `Print::availableForWrite()`, hàm kế thừa luôn trả0. Firmware cũ vì vậy không gửi TCP và ngắt vì backpressure. Đã chuyển sang một lần socket `send(..., MSG_DONTWAIT)` tối đa256 byte mỗi vòng, có cursor/partial write, retry EAGAIN/EWOULDBLOCK/EINTR và xử lý đóng socket.
- App bỏ FlushAsync trên mỗi lệnh serial, đặt giới hạn cả chờ write lock/write/command lock, INFO bắt tay chờ tối đa8s. Khi thất bại, đóng transport mà không chờ gửi STOP lại trên đường đang lỗi; bật lại kết nối. Disconnect có thể hủy cả khi đang bắt tay. Callback reader của kết nối cũ không cập nhật lên phiên mới.
- Test WPF thực qua localhost fake board PASS: ACK đúng + telemetry chia thành hai gói; INFO chậm2.5s; peer đóng trước ACK; ACK sai request ID. Hai tình huống lỗi đều có thời gian chờ giới hạn, tài nguyên được dọn và nút kết nối lại được bật. Test không kết nối drone, không gửi ARM/START.
- Native TCP sender regression PASS: short writes, EAGAIN, closed socket, chunk limit. Controller/estimator không thay đổi bởi sửa này.
- Cần upload firmware mới để sửa Wi-Fi trên board, và mở app mới. Chưa có xác nhận kết nối phần cứng sau sửa từ người dùng.
- Kiểm tra bổ sung: stream giả không cho Flush xác nhận lệnh được gửi không gọi Flush; stream giả cố tình bỏ qua cancellation xác nhận app vẫn timeout write và nhả khóa. Chuyển callback reader ra khỏi UI context, cho ngắt trong lúc đang kết nối, không để reader cũ sửa trạng thái phiên mới.
- Firmware cuối sau sửa build thành công với source ID `9610E96790911A9C`, flash khoảng957KB, global RAM khoảng49KB. Các biến đếm chia sẻ chuyển sang atomic; phát hiện TCP disconnect không dựa vào `operator bool()` vốn cũng gọi `connected()`.
