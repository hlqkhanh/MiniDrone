# Thực hành cân bằng trên đế: làm từng bước và biết mình đang kiểm tra gì

Đọc [luồng và ý nghĩa các field](huong-dan-field-pid-wpf.md) trước nếu bạn chưa phân biệt Outer, Inner, target và base. Tài liệu này hướng dẫn thứ tự thao tác; [đồ thị PID](do-thi-pid.md) giúp giải thích kết quả.

Mục tiêu hiện tại là thử Roll/Pitch trên đế. Hệ thống chưa giữ độ cao, vị trí hoặc hướng Yaw tuyệt đối. Kết quả trên đế chưa xác nhận drone sẵn sàng bay tự do.

## 1. Chuẩn bị phần mềm và hiểu nơi điều khiển đang chạy

Nạp [sketch chính](../../firmware/DroneMotionMpu6050/DroneMotionMpu6050.ino) cùng các header trong thư mục bằng Arduino ESP32 core 3.x. Build/chạy WPF bằng .NET 8 hoặc SDK tương thích. Chế độ điều khiển motor này dành cho ESP32.

ESP32 tự tính PID ở mục tiêu 200 Hz. App chỉ gửi cấu hình/lệnh và hiển thị; không cần chờ đồ thị vẽ xong mới có lượt PID tiếp theo.

## 2. Kiểm tra theo từng mốc

| Bước | Bạn làm gì? | Vì sao cần? | Dấu hiệu cần đối chiếu |
|---|---|---|---|
| 1 | Đặt drone ngang, giữ yên để hiệu chuẩn gyro; kết nối USB/Wi-Fi | Gyro có thể có sai lệch nền ngay cả khi đứng yên | Sensor READY; góc/rate cập nhật hợp lý |
| 2 | Tháo cánh, chạy manual từng motor ở mức đã kiểm chứng phù hợp | Biết nhãn motor thực sự trỏ tới đúng vị trí | LF/RF/LR/RR đúng bảng bên dưới; STOP dừng được |
| 3 | Kiểm tra dấu góc và chiều hiệu chỉnh bằng dữ liệu PWM | Phản hồi sai chiều làm nghiêng thêm, không thể chữa bằng tăng gain | Giữ yên ở góc lệch nhỏ, target 0°, I/D=0, so sánh hai bên |
| 4 | Chuẩn bị đế có khoảng quay cho trục cần thử | Đế khóa cứng hoặc ma sát lớn che mất phản ứng cần đánh giá | Drone được giữ chắc, trục thử còn quay được, khoảng hở phù hợp |
| 5 | Khi dừng, chọn ROLL, nhập cấu hình và Apply PID | Giảm số biến đang thử và gửi cấu hình có kiểm soát | Gain board xác nhận khớp ý định |
| 6 | Chọn target, base; chỉ đặt mốc khi drone ngang | Xác định mục tiêu và công suất nền đúng trước khi chạy | Target 0/0 theo mốc ngang; hiểu rõ base đã nhập |
| 7 | Xác nhận đế, Arm rồi Start trong 15 giây | Tách bước chuẩn bị và bước phát lệnh motor | CONTROL_ARMED rồi CONTROL_RUNNING |
| 8 | Quan sát, ghi CSV, Stop trước mỗi thay đổi gain | Biết thay đổi nào dẫn đến kết quả nào | Góc/rate/PID/PWM có diễn biến giải thích được |

## 3. Nhãn motor phải khớp phần cứng

| Nhãn | Vị trí nhìn theo đầu drone | GPIO |
|---|---|---|
| LF | Trái trước | 14 |
| RF | Phải trước | 15 |
| LR | Trái sau | 12 |
| RR | Phải sau | 13 |

Người dùng đã xác nhận trái thấp/phải cao cho FW Roll dương. Code đảo chiều đóng góp Roll trong bộ trộn bằng `RollMotorDirection=-1`. Với target 0°, giữ yên, gain P dương và I/D=0, Roll dương phải tạo LF/LR lớn hơn RF/RR. Góc quá nhỏ có thể làm chênh lệch bị làm tròn về 0 duty.

Khi đang quay nhanh, lệnh còn phải giảm tốc chuyển động nên không chỉ nhìn phía đang thấp để kết luận. Xem ví dụ ở [mục 7 của hướng dẫn field](huong-dan-field-pid-wpf.md#7-đi-hết-luồng-bằng-một-ví-dụ-duy-nhất).

Pitch đang giữ `PitchMotorDirection=+1` và chưa được xác minh chiều trên phần cứng. Thử riêng Pitch trước khi chọn BOTH. Không đảo riêng dấu góc mà bỏ quên dấu gyro.

## 4. Chọn và chỉnh thông số theo cách dễ theo dõi

Gain vòng góc mặc định là **2/0/0**; vòng tốc độ mặc định là **0/0/0**. Kp vòng góc và vòng tốc độ của trục được chọn phải lớn hơn 0 để Start. Gain chỉ lưu RAM; reset board trả về mặc định này.

Giữ vòng góc 2/0/0 trong giai đoạn tìm hiểu đầu giúp bạn tập trung vào vòng tốc độ. Bắt đầu với I/D vòng tốc độ bằng 0 để quan sát ảnh hưởng phần P, sau khi thứ tự motor và chiều phản hồi đã đúng. Chưa có bộ gain/base được xác nhận chung cho phần cứng này.

Mỗi lượt thử nên có một câu hỏi cụ thể, ví dụ “giữ các điều kiện khác, thay Kp tốc độ thì góc hồi về khác thế nào?”. Dừng rồi mới đổi thông số và Apply lại. Không thay đồng thời gain hai tầng, base và target, vì bạn sẽ khó biết yếu tố nào gây ra phản ứng.

| Điều thấy được | Việc cần làm rõ trước khi chỉnh tiếp |
|---|---|
| Nghiêng thêm ngay theo hướng sai | Kiểm tra vị trí motor và chiều phản hồi; không dùng tăng gain để sửa chiều |
| Motor có đổi PWM nhưng thân gần như không phản ứng | Kiểm tra lực đẩy, nguồn, base, ma sát và đế có khóa trục không |
| Góc về nhưng lắc qua lại nhiều | Ghi lại góc/rate và P/I/D để phân biệt quá điều chỉnh với nhiễu |
| Output limited giữ ở ±40 lâu | Vòng tốc độ đang hết biên lệnh; chưa thể kết luận chỉ cần tăng Kp |
| Góc lệch cố định | Kiểm tra mốc ngang, cơ khí, lực đẩy và sai lệch kéo dài trước khi quy về Ki |

Chỉ dùng số đo mới và gain đã được board xác nhận để đánh giá. Base là duty, không phải lực nâng đo được. Một base giống nhau nhưng pin/đế thay đổi có thể cho kết quả khác.

## 5. Hai mốc Zero làm hai việc khác nhau

- **Set firmware level reference:** đổi mốc góc PID. Chỉ nhận khi dừng/disarm và tư thế thô trong ±10°. Đặt drone ngang trước khi bấm.
- **Zero orientation:** đổi mốc mô hình 3D, không đổi mục tiêu hoặc góc dùng cho PID.

Target nhập vẫn giới hạn ±10°. Góc đo được không còn bị chặn Start ±10° hay ngắt ±25°. Việc bỏ chặn không bảo đảm điều khiển tốt ở góc lớn hoặc tư thế lật; công thức góc hiện tại cũng có giới hạn khi đi qua các tư thế đặc biệt.

## 6. Theo dõi và lưu một lượt thử

Mở Live PID plots, đọc theo thứ tự ANGLE → BODY RATE → RATE PID → MOTOR OUTPUT. **Plot axis** chỉ chọn trục để xem, không đổi trục đang điều khiển.

**Start / stop CSV log** bật/tắt ghi file vào Documents/DroneMotionLogs. Ghi kèm điều kiện đế/pin, base và nhận xét từng lượt. CSV gồm góc, rate, target, P/I/D vòng tốc độ, PWM, saturation/active, tốc độ mục tiêu, output giới hạn và gain board đã báo về. Chưa có P/I/D vòng góc trong telemetry.

Pause/Clear đồ thị không dừng motor; nút CSV cũng không dừng motor. Dùng **STOP / DISARM** hoặc Esc để yêu cầu dừng motor. Sau Stop, cả hai tầng reset bộ nhớ điều khiển.

## 7. Nếu nút bị khóa hoặc board tự dừng

| Hiện tượng | Các điều kiện cần kiểm tra |
|---|---|
| Apply bị khóa | Đã kết nối, firmware hỗ trợ, đã dừng/disarm, không có lệnh đang chờ, dữ liệu nhập hợp lệ |
| Các ô Outer bị khóa | Board có báo CONTROL_CASCADE_V2 và trạng thái cấu hình có cho phép sửa không |
| Arm bị khóa | Sensor sẵn sàng, đã xác nhận đế, gain trục được chọn đã nhận và Kp hai tầng >0 |
| Start bị khóa | Đã nhận CONTROL_ARMED, cấu hình hợp lệ, không có lệnh đang chờ |
| Start bị từ chối | Arm hết hạn 15 giây, sai nguồn kết nối, dữ liệu cảm biến/tham số hoặc trạng thái không hợp lệ |
| Dừng khi đang chạy | Đối chiếu thông báo IMU, timing, PWM, heartbeat, watchdog hoặc bão hòa mixer |

Firmware vẫn có kiểm tra lỗi IMU/thời gian, heartbeat 2 giây, watchdog output 100 ms và supervisor kiểm tra mỗi 50 ms. Mixer vượt phạm vi 0–220 liên tục quá 0.5 giây cũng dừng. Các cơ chế này kiểm tra những lỗi khác nhau; heartbeat tốt không thay thế được vòng PID phải cập nhật đúng hạn.

## 8. Khi nào có thể kết luận một thay đổi có ích?

So sánh các lượt có điều kiện tương tự: góc có tiến về target, tốc độ có giảm khi đến gần, dao động có giảm theo thời gian và lệnh có tránh giữ bão hòa kéo dài không? Thời gian hồi, sai lệch còn lại và mức dao động nên lấy từ CSV/đồ thị để so sánh, không chỉ từ cảm giác hoặc một ảnh.

Kiểm thử máy tính xác nhận phép tính và xử lý trạng thái, không xác nhận phần cứng đã ổn định. Tiếp tục xác minh riêng từng trục trước khi ghép BOTH và chưa coi thử trên đế là xác nhận bay tự do.

## Phụ lục: dành cho người cần kiểm tra code

Giao thức lệnh và giới hạn nằm trong [luồng kỹ thuật](luong-dieu-khien-cascade.md). Các kiểm thử có thể chạy từ thư mục Drone:

```powershell
dotnet test .\DroneMotion.sln
g++ -std=c++17 tests/firmware/flight-controller.test.cpp -o "$env:TEMP/minidrone-flight-test.exe"
& "$env:TEMP/minidrone-flight-test.exe"
g++ -std=c++17 -I tests/firmware/motor-stubs tests/firmware/motor-control.test.cpp -o "$env:TEMP/minidrone-motor-test.exe"
& "$env:TEMP/minidrone-motor-test.exe"
```

Hai lệnh C++ cần môi trường đã cài `g++`. Build WPF hoặc test đạt chưa thay thế kiểm tra motor, nguồn và vòng điều khiển trên board thật.
