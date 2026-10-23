# PID cân bằng trên đế giữ drone

Chế độ này điều khiển roll và pitch trên ESP32 bằng PID góc nối tiếp PID tốc độ quay. Vòng góc mặc định P=2, I=D=0 để giữ hành vi ban đầu; firmware mới cho phép chỉnh đủ ba hệ số. WPF gửi cấu hình, hiển thị dữ liệu và ghi CSV; vòng điều khiển chạy trực tiếp trên ESP32 ở 200 Hz. Mục tiêu mặc định là roll 0° và pitch 0°. Chưa có giữ hướng yaw, độ cao hoặc vị trí. Xem [luồng mới và cách áp dụng từ dự án tham khảo](luong-dieu-khien-cascade.md).

## Nạp và sử dụng

1. Nạp sketch chính `firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino` cùng tất cả header bằng Arduino ESP32 core 3.x. Build lại WPF bằng .NET 8 hoặc SDK mới hơn. Chế độ này chỉ hỗ trợ ESP32.
2. Đặt drone ngang và giữ yên để hiệu chuẩn gyro. Kết nối USB hoặc Wi-Fi, chờ `CONTROL_V1` và hai phản hồi gain. Các gain mặc định bằng 0, nên chưa thể Start.
3. Tháo cánh để xác minh thứ tự motor và chiều IMU. LF/RF/LR/RR tương ứng GPIO14/15/12/13. Roll = âm góc quanh Y cảm biến, pitch = âm góc quanh X cảm biến, giống quy ước board trong WPF. Theo kiểm tra của người dùng, nghiêng trái (trái thấp, phải cao) cho FW Roll dương. Đã đảo chiều motor-side Roll để ở tư thế đó và đứng yên, target 0°, gain P dương và I/D bằng 0, LF/LR được tăng và RF/RR được giảm. Pitch chưa được xác nhận chiều trên phần cứng và giữ nguyên mixer hiện có. Phải xác minh thứ tự motor và chiều phản hồi trước khi chạy có lực đẩy.
4. Gắn drone chắc vào đế có khoảng quay và khoảng hở cánh phù hợp. Đế phải cho trục thử quay tự do; nếu khóa cứng trục thì không thể đánh giá PID. Ma sát và trọng tâm đế ảnh hưởng gain; gain trên đế chưa chứng minh dùng được khi bay tự do.
5. Trong **BALANCE ON TEST RIG**, chọn ROLL trước. Nhập Kp nhỏ, Ki=0, Kd=0. Bấm **Apply PID**, đối chiếu dòng gain đã nhận từ board. Không có bộ gain mặc định đã được xác nhận cho phần cứng này.
6. Đặt base duty (đơn vị 0–255, cho nhập 1–180), tăng từ mức thấp phù hợp motor. Giữ target 0/0. **Set firmware level reference** lấy tư thế hiện tại làm mốc; chỉ cho phép khi tư thế thô nằm trong ±10°. Nút **Zero orientation** cũ chỉ đổi hình hiển thị, không đổi mốc PID.
7. Xác nhận đế và chiều hiệu chỉnh, bấm **Arm balance**, rồi **Start balance** trong 15 giây. Esc hoặc **STOP / DISARM** dừng tất cả và xóa tích phân. Chỉnh gain chỉ khi đã Stop. Thay ô nhập không tự thay gain trên ESP32.
8. Thử góc lệch nhỏ khoảng ±5°. Giữ vòng góc 2/0/0 khi chỉnh vòng tốc độ trước; tăng Kp tốc độ từng bước, thêm Kd nếu cần giảm vượt góc, thêm Ki nhỏ khi còn sai lệch kéo dài. Lặp lại riêng cho PITCH rồi BOTH. Sau đó mới chỉnh vòng góc nếu cần. Vòng góc giới hạn tốc độ mục tiêu ±45°/s; chỉ firmware `CONTROL_CASCADE_V2` cho sửa gain góc trên WPF.
9. Bấm **Start / stop CSV log** để lưu dữ liệu trong Documents/DroneMotionLogs. Log chứa thời gian micro giây ESP32, góc, tốc độ, target, P/I/D, PWM LF/RF/LR/RR, bão hòa, active và sáu gain đã nhận từ board. Thời gian ESP32 tràn sau khoảng 71 phút; bắt đầu log mới sau reset.

Gain chỉ lưu RAM, khởi động lại trở về 0. Kiểm tra phản hồi trước mỗi lần thử. Giới hạn nhập: Kp 0–5, Ki 0–2, Kd 0–0.2, dùng dấu chấm thập phân. Đơn vị: góc độ, tốc độ độ/giây, thời gian giây, output duty PWM. Các giới hạn này là giới hạn phần mềm, không phải xác nhận mọi giá trị trong đó đều ổn định.

## Điều khiển và bảo vệ

Hai vòng PID dùng tích phân hình thang, đạo hàm âm của giá trị đo và lọc thông thấp đạo hàm 10 Hz. Vòng tốc độ giới hạn đóng góp I ±20 và output mỗi trục ±40 duty. Vòng góc giới hạn I ±10 °/s, output ±45 °/s. Mixer giới hạn motor 0–220 duty. Khi bão hòa mixer, tích phân đóng băng; vòng góc còn đóng băng I nếu đầu ra vòng tốc độ chạm giới hạn ở lần tính trước. Bão hòa mixer liên tục quá 0.5 giây dừng chế độ. Cả hai tầng reset khi Stop hoặc bắt đầu lần chạy mới. Cơ chế tự học gyro bias chỉ hoạt động khi chưa Arm/Start.

Phiên bản này không dùng yaw mixer. Trong `FlightController.h`, `RollMotorDirection=-1` đảo riêng đóng góp Roll ở motor; `PitchMotorDirection=+1` giữ đóng góp Pitch hiện có. Với output PID Roll âm, mixer tăng phía trái; với output Pitch âm, mixer tăng phía trước. Không đảo dấu góc hoặc gyro riêng lẻ, vì hai tín hiệu phải cùng quy ước. Chế độ ROLL/PITCH chỉ vô hiệu hóa hiệu chỉnh trục còn lại; cả bốn motor vẫn có base duty.

Góc Roll/Pitch đo được không còn bị chặn ±10° lúc Start và không làm dừng khi vượt ±25° lúc chạy. Vẫn cần Arm trước Start; target yêu cầu vẫn giới hạn ±10° và đặt mốc ZERO chỉ cho phép khi tư thế thô trong ±10°. Bỏ ngưỡng góc đo chỉ thay đổi điều kiện cho phép chạy, không chứng minh khả năng tự hồi phục ở góc lớn hoặc tư thế lật. Công thức Euler hiện tại không được thiết kế để bảo đảm điều khiển mượt qua mọi tư thế.

Firmware dừng khi lỗi IMU, mẫu/dt không hợp lệ, lỗi PWM, mất kết nối Wi-Fi sở hữu hoặc thiếu heartbeat 2 giây. Supervisor độc lập kiểm tra mỗi 50 ms và cắt PWM nếu vòng PID không ghi output trong 100 ms; độ trễ phát hiện tối đa thêm một chu kỳ supervisor. Không tự khởi động motor lại sau khôi phục cảm biến. Giữ lệnh manual và PID loại trừ nhau. Đóng app/Disconnect/Calibrate gửi STOP trước.

## Giao thức

```text
PID SET ROLL <Kp> <Ki> <Kd>
PID SET PITCH <Kp> <Ki> <Kd>
PID SET ANGLE_ROLL <Kp> <Ki> <Kd>
PID SET ANGLE_PITCH <Kp> <Ki> <Kd>
CONTROL INFO
CONTROL ZERO
CONTROL SETPOINT <rollDeg> <pitchDeg>
CONTROL ARM
CONTROL START <baseDuty> <axis>
CONTROL STOP
MOTOR KEEPALIVE
```

Axis: 0=BOTH, 1=ROLL, 2=PITCH. START cần ARM cùng kết nối. Gain, ZERO và SETPOINT chỉ nhận khi đã dừng/disarm. STOP nhận từ mọi kết nối. Heartbeat mỗi 500 ms từ kết nối đã START; nó không thay thế watchdog vòng điều khiển.

```text
PID,ROLL,0.120000,0.040000,0.003000
STATUS,CONTROL_V1
STATUS,CONTROL_ARMED
STATUS,CONTROL_RUNNING
STATUS,CONTROL_STOPPED
C,micros,roll,pitch,rollRate,pitchRate,rollTarget,pitchTarget,rollP,rollI,rollD,pitchP,pitchI,pitchD,LF,RF,LR,RR,saturated,active
```

Q vẫn được gửi 100 Hz, C khoảng 10 Hz. HELLO báo tần số lấy mẫu/điều khiển 200 Hz. Dữ liệu C là góc firmware sau mốc ZERO; góc hiển thị mô hình có mốc Zero riêng. Các cột P/I/D trong C là vòng tốc độ, chưa truyền P/I/D vòng góc. Firmware mới báo thêm `STATUS,CONTROL_CASCADE_V2` và gain `PID,ANGLE_ROLL,...`/`PID,ANGLE_PITCH,...`. CSV WPF mới thêm sáu cột gain góc cuối dòng; khi kết nối firmware cũ các cột đó để trống.

## Kiểm chứng

```powershell
dotnet test .\DroneMotion.sln
g++ -std=c++17 tests/firmware/flight-controller.test.cpp -o "$env:TEMP/minidrone-flight-test.exe"
& "$env:TEMP/minidrone-flight-test.exe"
g++ -std=c++17 -I tests/firmware/motor-stubs tests/firmware/motor-control.test.cpp -o "$env:TEMP/minidrone-motor-test.exe"
& "$env:TEMP/minidrone-motor-test.exe"
```

Test máy tính kiểm tra công thức, chiều mixer, một mô hình động lực học đơn giản, chống windup và watchdog. Cần thử trên đế để xác nhận chiều thực tế, jitter, rung IMU, nguồn motor, PWM khởi động, STOP và ngắt kết nối. Mục tiêu thử đề xuất: từ ±5–10° trở về trong ±2° và giữ 20–30 giây, không dao động tăng dần hoặc giữ bão hòa. Chỉ kết quả đo thực tế mới chứng minh đạt tiêu chí đó.
