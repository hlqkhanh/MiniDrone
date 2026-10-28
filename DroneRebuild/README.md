# DroneRebuild — Roll bench v0.1

Project độc lập theo `../PLAN.md`. Không tham chiếu source/build của app hoặc firmware cũ. Bản v0.2 kiểm chứng Roll hoặc Pitch, từng trục trên đế; chưa điều khiển đồng thời hai trục, chưa xác nhận bay hoặc hover. Xem `docs/ROLL_PITCH.md` để cập nhật và kiểm tra trục.

**Buổi thử ngày mai:** [Hướng dẫn chi tiết kiểm tra và hiệu chỉnh Roll trên phần cứng](docs/HUONG_DAN_CAN_BANG_ROLL_THUC_TE.md).

**Giao diện cập nhật 27/10:** cấu hình ở cột trái, tab Điều khiển Roll/Thử từng motor, ARM và bắt đầu ghim dưới, STOP ở góc trên. Bốn biểu đồ góc/rate/output/motor được tách riêng; mở “Chi tiết board và nhật ký” để xem dữ liệu và sự kiện. Các trường cấu hình dài nằm trong vùng cuộn; motor sign/output limit nằm trong “Chiều motor và giới hạn”. Cập nhật này chỉ cần mở lại app đã build, không yêu cầu nạp lại firmware.

## Bắt đầu hôm nay

1. Mở [hướng dẫn Simulink](docs/SIMULINK.md). Gói `DroneRebuild_Simulink.zip` chứa script tự tạo mô hình, chưa phải `.slx` đã chạy được xác nhận ở máy này.
2. Chạy test trên máy: `powershell -ExecutionPolicy Bypass -File DroneRebuild/scripts/test.ps1` từ thư mục MiniDrone.
3. Build: `powershell -ExecutionPolicy Bypass -File DroneRebuild/scripts/build.ps1`. Script dùng bộ công cụ có sẵn trong `.tools`, Arduino ESP32 3.3.12 và .NET 8 target. Máy đang có .NET SDK 9 có thể build target .NET 8.
4. Chạy app: `DroneRebuild/app/bin/Release/net8.0-windows/DroneRebuild.App.exe`. Cần .NET 8 Windows Desktop Runtime.
5. App mặc định mọi gain bằng 0. `Open simulation CSV` chỉ phát lại `build/native_roll.csv`, có nhãn SIMULATION và chỉ dùng khi disconnected.

## Cấu trúc

| Đường dẫn | Vai trò |
|---|---|
| `firmware/DroneRebuild/RollCore.h` | Controller C++ chuẩn, không phụ thuộc Arduino |
| `firmware/DroneRebuild/DroneRebuild.ino` | ESP32 SPI, calibration, Madgwick, task điều khiển, USB/TCP, motor arbiter |
| `app/` | WPF tối thiểu, USB/TCP, ACK, biểu đồ, log |
| `simulation/native_sim.cpp` | Mô phỏng chạy chính RollCore.h trên máy |
| `simulation/roll_sfun.cpp` | C++ S-function gọi chính RollCore.h trong Simulink khi MEX khả dụng |
| `simulation/roll_step.m` | Bản chuyển MATLAB dự phòng; phải qua đối chiếu vector C++ |
| `simulation/build_roll_model.m` | Sinh mô hình Simulink 200 Hz |
| `docs/PROTOCOL.md` | Giao thức mới, không dùng app cũ để điều khiển firmware này |
| `docs/TOMORROW.md` | Trình tự kiểm tra board/đế |
| `docs/STATUS.md` | Kết quả máy tính và điều kiện chưa nghiệm thu |

## Kiến trúc

Vòng góc P tạo rate target; vòng rate PID dùng gyro và tạo phần sửa duty. D theo số đo qua lọc thấp; I tích phân hình thang với giới hạn và conditional anti-windup. Mixer Roll: LF/LR = base + sign×output, RF/RR = base − sign×output. Sign phải xác nhận trên phần cứng.

`RATE_ROLL` nhận rate target trực tiếp. `ANGLE_ROLL` nhận góc mục tiêu. `MIXER_DRY_RUN` tính tương tự RATE nhưng output phần cứng luôn bằng 0. Chỉ chạy Roll; không PID Pitch/Yaw/X/Y/độ cao.

Task control đọc SPI/estimator 200 Hz theo thời gian đo thực. Nó chỉ gửi đề xuất output. Timer arbiter là nơi duy nhất ghi PWM sau setup, chạy mỗi 5 ms, độc lập với control/network, kiểm tra heartbeat, tuổi output và thời hạn. Phần trao đổi proposal dùng critical section ngắn, không giữ khóa khi tính PID hoặc gọi mạng. Các mốc 100 ms/2 giây là ngưỡng phần mềm, chưa được đo jitter thực trên board.

STOP tăng epoch ngay ở transport, xóa quyền xuất motor và làm lệnh ARM/START mang epoch cũ bị từ chối. ARM không được chiếm phiên đã Armed/Running. ACK chỉ trả về kết nối đã gửi lệnh; telemetry chia sẻ giữa hai đường.

## Phần cứng dự kiến

- ESP32 Dev Module, SPI MPU6500 `0x70` hoặc MPU9250 `0x71`.
- SCK18/MOSI23/MISO19/CS5; LF14/RF15/LR12/RR13.
- DLPF CONFIG=3, ACCEL_CONFIG2=3; gyro ±500°/s, accel ±2g, divider=4. Kiểm tra data-ready; không coi đọc lại cùng mẫu là mẫu mới.
- Gyro bias 500 mẫu ổn định. Motor tắt khi calibrate. Không tự ZERO góc. Roll body tạm dùng −sensor Euler Y; gyro Roll = −gyroY. Đây là quy ước cần xác nhận ngày mai.
- Gain bằng 0 sau reset; cấu hình runtime không lưu flash.

## USB / Wi-Fi

USB 460800 baud. AP `DroneRebuild`, mật khẩu `rebuild123`, TCP `192.168.4.1:3333`. Chọn TCP và nhập IP này trong app. Nếu dùng router, chép `WifiSecrets.example.h` thành `WifiSecrets.h`, điền SSID/password, build lại trước phiên thử. Firmware bắt đầu kết nối trong setup; không có portal, scan hay ghi Wi-Fi lúc Armed/Running. Router provision runtime chưa triển khai.

TCP timeout và app mất kết nối không tự khởi động lại motor. Kết nối không có quyền vẫn gửi STOP được. Không dùng hai app để đồng thời thử một bộ motor.

## Nguồn thiết kế

- [Crazyflie controllers](https://www.bitcraze.io/documentation/repository/crazyflie-firmware/master/functional-areas/sensor-to-control/controllers/): phân tầng attitude/rate.
- [ArduPilot attitude control](https://ardupilot.org/dev/docs/apmcopter-programming-attitude-control-2.html): luồng góc → tốc độ → motor.
- [MathWorks C/C++ S-functions](https://www.mathworks.com/help/simulink/sfg/available-s-function-implementations.html): đưa controller C++ vào mô hình.
- [Espressif LEDC](https://docs.espressif.com/projects/arduino-esp32/en/latest/api/ledc.html): PWM API core 3.x.

Không sao chép gain từ các hệ thống này. Tham số plant và gain trong mô phỏng là minh họa, không phải bản nhận dạng drone 8520.
