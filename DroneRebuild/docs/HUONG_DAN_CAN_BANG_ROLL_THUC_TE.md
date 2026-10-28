# Hướng dẫn thử và hiệu chỉnh Roll trên drone thật

**Bổ sung 28/10/2026:** bỏ điều kiện START ±5° và dừng SATURATION_TRACKING. Các bước/ngưỡng cũ trong tài liệu này không mô tả hành vi mới; xem PROTOCOL.md. Giới hạn duty đầu ra và chống windup vẫn giữ.

**Thay đổi code 28/10/2026:** START hiện chạy liên tục đến STOP hoặc fault, app không còn ô Duration; ENVELOPE/OTHER_AXIS_ENVELOPE đã bỏ. BENCH vẫn hữu hạn và START vẫn cần hai góc trong ±5°. Những bước bên dưới yêu cầu duration 300/1000/8000ms hoặc tự dừng ở ±15° là quy trình cũ; hiện phải kết thúc lượt bằng STOP. Xem PROTOCOL.md cho hành vi mới. Bản sửa chưa được kiểm chứng trên phần cứng.

**Soạn ngày 26/10/2026 cho buổi thử ngày 27/10/2026.**

**Mục tiêu:** từ góc lệch nhỏ, drone trở về gần ngang và giữ Roll ổn định trên đế một trục, có dữ liệu chứng minh. Đây là hướng dẫn tiến tới mục tiêu, không phải cam kết đạt ngay trong một buổi. Khi một điều kiện chưa đạt, ghi lại và xử lý tại bước đó.

Phạm vi: ESP32 XW_Z1, IMU SPI MPU6500/MPU9250, firmware/app **DroneRebuild**. Chưa thử bay tự do, giữ vị trí, độ cao, Pitch hoặc Yaw. Không dùng tài liệu này để vận hành firmware cũ.

### Tên nút trong giao diện mới ngày 27/10

| Tên trong các bước bên dưới | Nút/nhóm trong app mới |
|---|---|
| Connect / Disconnect | Kết nối / Ngắt kết nối |
| INFO / Calibrate | Thông tin board / Hiệu chuẩn IMU |
| Apply / board ACK | Áp dụng cấu hình lên board; đọc dòng “Board đã nhận” phía dưới |
| RATE_ROLL / ANGLE_ROLL | Tốc độ Roll · RATE / Giữ góc Roll · ANGLE |
| Motor sign / Output limit | Mở “Chiều motor và giới hạn” |
| START | “2. Bắt đầu Roll”, ghim dưới cột trái |
| BENCH | Tab “Thử từng motor”, sau ARM bấm “2. Chạy motor” ở khung ghim dưới |
| Start log / Stop log | Bắt đầu ghi log / Dừng ghi log, nhóm Lưu dữ liệu trong cột trái |
| STOP / Esc | DỪNG MOTOR ở góc trên, hoặc phím Esc |
| Readout / sự kiện | Mở “Chi tiết board và nhật ký” dưới biểu đồ |

Cuộn cột trái để xem các trường phía dưới. Vùng ARM/bắt đầu và nút dừng không cuộn theo. Dữ liệu mô phỏng có nhãn MÔ PHỎNG; các nút chạy bị khóa khi chưa có telemetry board mới và các điều kiện cần thiết.

## 1. Đọc nhanh trước buổi thử

1. Xác nhận nguồn, đế và phương tiện ngắt nguồn.
2. Tháo cánh, nạp đúng firmware, kết nối app mới.
3. Kiểm chứng cảm biến đứng yên và nghiêng từng chiều.
4. Thử từng motor không cánh; xác nhận nhãn, chiều quay và các cách dừng.
5. Kiểm chứng đế, lắp cánh đúng loại/chiều khi đã ngắt nguồn; xác nhận chiều lực bằng bố trí thử được che chắn.
6. Xác lập base ứng viên dựa trên phần cứng, chưa dùng base mô phỏng như số đo lực.
7. Kiểm chứng Rate trước, mỗi lượt ngắn và chỉ đổi một yếu tố.
8. Khi Rate có đủ bằng chứng đạt, ghép Angle P và thử hồi phục từ góc nhỏ hai phía.
9. Lưu toàn bộ log và cấu hình thực; đánh giá lặp lại trước khi chốt.

**Không có đế Roll phù hợp: chỉ làm bước 2–4, chưa tuning có lực đẩy.**

## 2. Kết quả mình đã có và điều còn thiếu

### Đã có bằng chứng trên máy

- Cùng `RollCore.h` chạy trong firmware và C++ S-function của Simulink.
- 3600 vector C++/MATLAB khớp; sai khác lớn nhất trên ảnh kết quả là 5.68e-14.
- Năm bài Simulink PASS: góc, xung rate, motor yếu/chậm, sai chiều và nhiễu gyro.
- Firmware ESP32 và app WPF build thành công.

### Chưa xác nhận trên chiếc drone này

- Wiring, hướng IMU, chiều lực thực của motor, chất lượng góc/rate khi motor chạy.
- Điện áp, lực motor theo duty, độ trễ, quán tính và ma sát/mô-men hồi của đế.
- Thời gian ngắt PWM thực khi mất kết nối hoặc task bị lỗi.
- Gain/base phù hợp phần cứng và khả năng giữ Roll.

### Bộ thông số mô phỏng — chỉ làm mốc đối chiếu

| Thông số | Giá trị đã dùng trong mô phỏng |
|---|---:|
| Rate Kp | 0.8 |
| Rate Ki | 0.05 |
| Rate Kd | 0.005 |
| Angle P | 2 |
| Base | 80 duty |
| Motor sign | +1 trong mô hình có chiều vật lý tương ứng |
| Output limit | 20 duty |

Không tự điền nguyên bộ này cho lượt có lực đẩy đầu tiên. Trong app, gain sau khởi động mặc định bằng 0. Mô hình có J, lực motor và độ trễ giả định, chưa được nhận dạng từ chiếc drone thật.

## 3. Đồ cần mang và thông tin cần ghi

- Drone/board, cáp USB truyền dữ liệu, laptop, firmware và app mới.
- Pin/nguồn đúng loại và điện áp cho phép của bộ drone; không suy điện áp từ nguồn USB.
- Đế Roll có chắn cánh, giới hạn cơ khí, mốc ngang; phương tiện ngắt nguồn ở ngoài vùng cánh.
- Dụng cụ đối chiếu góc, cân nếu có, dụng cụ đo điện áp và đồ gá phù hợp.
- Một người theo dõi app và một người quan sát từ vị trí ngoài vùng cánh nếu bố trí cho phép.

Điền trước khi chạy motor:

| Mục | Giá trị thực / ghi chú |
|---|---|
| Ngày giờ / người thử | |
| Board / nhãn phần cứng | |
| Pin: loại, số cell, điện áp cho phép | |
| Điện áp trước lượt / khi tải nếu đo được | |
| Khối lượng bộ drone | |
| Cánh: loại, vị trí, chiều quay | |
| Hướng đầu / vị trí và hướng gá IMU | |
| Trục đế so với trọng tâm | |
| Giới hạn góc đế / mô-men hồi / ma sát | |
| Dụng cụ góc và sai số | |
| Firmware version / source ID / build time | |

Không tháo/lắp cánh, chỉnh dây/đế, giữ hoặc thả thân bằng tay cạnh cánh đang quay. Bố trí góc đầu và cơ cấu giữ/nhả khi motor đã tắt; nếu cần nhả trong lượt thử, dùng cơ cấu từ xa ngoài vùng cánh. Không dựa vào phần mềm STOP như cách bảo vệ duy nhất khi chỉnh cơ khí.

## 4. Mở đúng app và nạp đúng firmware

### 4.1. App

Nhấn Windows + R, nhập:

```text
D:\FPTDocuments\Semester_8\MiniDrone\DroneRebuild\app\bin\Release\net8.0-windows\DroneRebuild.App.exe
```

Không dùng app DroneMotion cũ. `Open simulation CSV` chỉ phát lại file, không kết nối board hoặc áp dụng gain.

### 4.2. Build/nạp

1. Tháo cánh và ngắt nguồn motor khi chuẩn bị nạp.
2. Đóng Serial Monitor và Disconnect trong app trước khi chiếm cổng COM để upload.
3. Mở đúng sketch:

```text
DroneRebuild/firmware/DroneRebuild/DroneRebuild.ino
```

4. Chọn ESP32 Dev Module, đúng cổng COM, Arduino-ESP32 core 3.3.12. Giữ các header cùng thư mục.
5. Để cập nhật source ID và build trước khi nạp, mở PowerShell ở thư mục MiniDrone:

```powershell
powershell -ExecutionPolicy Bypass -File .\DroneRebuild\scripts\build.ps1 -FirmwareOnly
```

Script chỉ build, không tự upload. Dùng Arduino IDE Upload từ chính thư mục source đó sau bước build; nếu sửa source tiếp thì build lại để source ID không bị cũ. Không chọn binary của thư mục firmware cũ.

6. Reset board, xác nhận tất cả motor tắt. Nếu motor tự quay, ngắt nguồn và dừng buổi thử motor để tìm nguyên nhân.

### 4.3. Kết nối

- App: chọn **USB**, nhập cổng COM thực, bấm **Connect**. App cố định 460800 baud.
- Bấm **INFO**, lưu dòng INFO vào log.
- Kỳ vọng version `DroneRebuild-0.1`, profile `XW_Z1_SPI_ROLL_V1`, WHO `70` hoặc `71`, `pwm_1`.
- Source ID lấy từ `BuildInfo.h` của lần build hiện tại; không suy board mới nhất chỉ vì app mới nhất.
- Nếu app hiện `SIMULATION REPLAY`, cần Connect board thật để chuyển về dữ liệu board; kiểm tra timestamp/telemetry đang tăng.

Thử USB trước để giảm số yếu tố phải điều tra. Wi-Fi kiểm tra sau khi bài USB lặp lại được: AP `DroneRebuild`, mật khẩu `rebuild123`, TCP `192.168.4.1:3333`. Router cần cấu hình `WifiSecrets.h` trước khi build, chưa có portal runtime.

## 5. Ghi log từ đầu

1. Điền ô **Ghi chú lượt thử**, ví dụ:

```text
R01; USB; deRoll-A; pin=...V; khongCanh; gocDau=0deg; mucTieu=kiemTraIMU
```

2. Bấm **Start log**, rồi **INFO** để lưu version/source ID và gyro bias.
3. Mỗi cấu hình cần tên lượt/ghi chú rõ. Nếu đổi ghi chú giữa phiên, dừng log rồi bắt đầu log mới để ghi chú mới được ghi vào đầu phiên.
4. File nằm trong Documents thực của Windows:

```text
Documents/DroneRebuildLogs/yyyyMMdd-HHmmss-fff/
    telemetry.csv
    events.log
```

Documents có thể được chuyển sang OneDrive; đường dẫn thực của phiên hiển thị trong vùng sự kiện của app. Dữ liệu Start/Stop/ACK/TX nằm trong `events.log`; dữ liệu đo nằm trong `telemetry.csv`.

5. Bấm **Stop log** sau khi motor đã dừng, chờ thêm đoạn sau dừng nếu cần đối chiếu. Giữ cả hai file.

**Quy tắc:** giá trị trong ô nhập chưa phải cấu hình board. Bấm **Apply / board ACK**, đợi ACK OK và kiểm tra dòng `Board accepted` đã phản ánh số mới.

## 6. Kiểm chứng IMU — motor chưa chạy, cánh tháo

### 6.1. Hiệu chuẩn

1. Đặt board/drone cố định, gần ngang thật.
2. Bấm **Calibrate** và giữ yên cho đến khi `calibrated True`, đủ 500 mẫu.
3. Nếu cửa sổ mẫu liên tục bắt đầu lại, kiểm tra chuyển động, rung, nguồn và dây. Không tăng ngưỡng calibration để bỏ qua vấn đề.
4. Sau calibration, bấm **INFO** lần nữa để ghi bias mới.

Calibration đo gyro bias, không tự đặt lại góc thành 0. Bản 0.2 có nút lấy mốc 0° riêng cho Roll/Pitch, chỉ dùng khi motor dừng và khung đã ngang thật. Reset/hiệu chuẩn lại xóa mốc. Nếu góc lệch đáng kể, kiểm tra mounting/ước lượng trước khi tuning.

### 6.2. Đứng yên 60 giây

Ghi liên tục 60 giây không động vào thân. Xem:

- Roll ổn định, không trôi một chiều liên tục.
- `rollRate` gần 0, ghi độ lệch trung bình và mức dao động.
- `dt` quanh 5 ms; xem cờ `late` và khoảng mẫu thực, không suy dt từ FPS.
- Accel thô có độ lớn gần 1g. Gyro `gx/gy/gz` là trước trừ bias; `rollRate` đã bù bias.
- Không có NaN, ngắt telemetry hoặc INFO nhận dạng sai chip.

Không tự đặt một ngưỡng nhiễu nhỏ rồi kết luận đạt nếu chưa đối chiếu thiết bị. Trước bài xung 5°/s, nhiễu rate phải đủ thấp để nhìn được đáp ứng đó; nếu dao động đứng yên đã cùng cỡ target, cần xử lý đường đo trước.

### 6.3. Nghiêng từng phía

1. Chốt quy ước: **Roll dương = phía nào nghiêng xuống**, viết vào bảng phần cứng. Không mặc định dấu trên màn hình đã đúng với thân.
2. Từ ngang, nghiêng khoảng +5°, về 0°, −5°, về 0°. Đối chiếu dụng cụ góc.
3. Trong lúc góc đang tăng theo chiều dương, gyro Roll phải có chiều phù hợp; giữ yên ở góc mới thì rate trở về gần 0 dù góc khác 0.
4. Lặp lại với ±10° khi vẫn không có motor.
5. Kiểm tra riêng Pitch không bị nhầm thành Roll chính. Bản v0.2 đổi quaternion sang hệ body X=−sensorY, Y=sensorX, Z=sensorZ trước khi tính Euler; rate Roll=−gyroY, Pitch=gyroX. Nếu mounting thật khác cần sửa phép đổi trục, test và build lại. Không dùng công thức −sensor Euler Y của v0.1 để đối chiếu bản mới.

Qua bước cảm biến khi đúng trục/chiều và góc tương ứng mốc cơ khí. PLAN dùng ±2° làm tiêu chí ban đầu Stage2, nhưng để kết luận thân thật giữ ±1° thì phép đo tổng thể phải được xác nhận chính xác hơn; không nhầm sai số bám số trên app với sai số góc vật lý.

## 7. Thử từng motor không cánh

### 7.1. Bảng motor dự kiến, phải kiểm tra thực

Nhìn theo đầu drone đã đánh dấu:

| Nhãn app | Vị trí | GPIO |
|---|---|---:|
| LF | Trước trái | 14 |
| RF | Trước phải | 15 |
| LR | Sau trái | 12 |
| RR | Sau phải | 13 |

### 7.2. Thao tác cho một motor

1. Cánh đã tháo, nguồn đúng; motor đang dừng.
2. Đánh dấu **Đã tháo cánh (chỉ dùng cho BENCH)**.
3. Chọn **LF**, duty nhỏ, có thể dùng giá trị khởi đầu bench `20` đang có trên app. Đây chỉ là lệnh thử không cánh; có thể chưa đủ làm motor quay.
4. Bấm **ARM**, đợi ACK OK, bấm **BENCH 1 giây**.
5. Xác nhận đúng motor, quan sát chiều quay từ vị trí phù hợp.
6. Đợi dừng, ghi kết quả. Muốn chạy lượt mới phải ARM mới.
7. Lặp với RF, LR, RR.

Bench firmware giới hạn duty ≤89, thời gian ≤2 giây; nút app hiện chạy 1 giây. Nếu duty thấp không quay, kiểm tra nguồn, dây và ma sát; không mặc định motor hỏng hoặc tăng ngay lên tối đa. Mọi mức tăng cần phù hợp phần cứng.

### 7.3. Kiểm tra dừng khi vẫn không cánh

- Chạy bench, bấm **STOP / Esc**, motor phải ngừng.
- Chạy lại, không bấm gì, hết thời hạn phải ngừng.
- Chạy lại, Disconnect/đóng app, phải ngừng.
- Reset, motor phải giữ tắt và không tự tiếp tục phiên cũ.

Bench 1 giây không đủ để chứng minh heartbeat timeout 2 giây hoặc watchdog sample 100ms: duration đã dừng trước các tình huống đó. Muốn xác nhận từng cơ chế phải có bài chèn lỗi riêng; không đánh dấu đã kiểm hết watchdog chỉ vì bench tự dừng.

Nếu một cách dừng không hoạt động, ngắt nguồn và sửa trước khi thử với cánh.

## 8. Đế và đường lực — điều kiện trước khi chạy PID có cánh

### 8.1. Đế đạt yêu cầu gì?

- Roll có trục xác định và được phép xoay quanh góc nhỏ.
- Pitch/Yaw/tịnh tiến được giữ có kiểm soát.
- Không có kê chạm giữ thân ở một góc hoặc dây cản đường hồi phục.
- Có mốc ngang thật, giới hạn xoay và chắn cánh.
- Ghi vị trí trục so với trọng tâm, ma sát và mô-men hồi khi motor tắt.

Nếu thân tự trở về ngang mạnh khi motor tắt, đế đang cung cấp mô-men hồi. Phải ghi nhận, không tự coi một lượt trở về ngang khi motor chạy là thành công của PID.

### 8.2. Xác nhận chiều lực

Khi đã ngắt nguồn và bố trí cơ khí xong, lắp đúng cánh theo motor/chiều quay. Thử lực cần bố trí giữ và che chắn phù hợp, không cầm drone bằng tay để chạy cánh.

Mixer code:

```text
LF = base + motorSign × output
RF = base − motorSign × output
LR = base + motorSign × output
RR = base − motorSign × output
```

- Với sign +1, output dương tăng nhóm trái và giảm nhóm phải.
- Cần xác nhận tác động đó tạo **rate Roll dương theo quy ước gyro đã kiểm tra** hay không.
- Nếu không, kiểm tra mapping/chiều lực/mounting; chỉ đổi sign sau khi đã xác định quan hệ thực. Dừng trước khi sửa và ghi thay đổi trong log.
- Không thử lần lượt +1 rồi −1 với gain lớn để đoán dấu nào đúng.
- BENCH yêu cầu cánh tháo; không đánh dấu sai sự thật để dùng BENCH như chức năng thử lực có cánh. Nếu chưa có phương tiện xác minh lực đúng điều kiện, ghi bước này chưa đạt và chưa START có lực đẩy.

Chỉ đánh dấu ô **Đã xác minh trục IMU, chiều lực motor và đế Roll; mốc ngang thật đã kiểm tra** sau khi thực sự hoàn thành các kiểm tra tương ứng.

## 9. Chọn base — không có một số đúng sẵn

Base là duty nền, không phải phần trăm lực nâng, điện áp đo được hoặc RPM. `80` trên app/mô phỏng và `110/120` trong log cũ chưa được xác nhận là phù hợp cho đế mới.

Cần có cơ sở từ nguồn, motor/cánh và bố trí đế để chọn **B0**, ghi rõ cách xác định. Khi đánh giá gain, giữ B0 và nguồn tương đương giữa các lượt. Nếu chưa có phép đo lực, ghi đó là mức ứng viên và giới hạn phạm vi kết luận.

Không vừa tăng base, vừa đổi sign, vừa tăng Kp trong một lượt. Không tăng base để ép một thân đang bị đế chặn. Nếu chưa đủ dữ liệu để chọn mức làm việc có cơ sở, dừng ở đo đường lực thay vì tự điền base80 vì mô phỏng đã PASS.

## 10. Quy trình chung cho mỗi lượt điều khiển

1. Motor đã dừng; kiểm tra mốc ngang, nguồn và điều kiện cơ khí.
2. Điền tên lượt/ghi chú, bấm Start log, bấm INFO.
3. Điền gain, base B0, sign đã kiểm tra, output limit. Không đổi fixed limit trong source để bỏ qua fault.
4. Bấm **Apply / board ACK**; đợi ACK OK; kiểm tra `Board accepted`.
5. Chọn mode, target và duration nguyên tính bằng **ms**.
6. Xác nhận đúng checkbox. Khi đã lắp cánh, bỏ checkbox Props removed ở trạng thái dừng.
7. Bố trí thân gần ngang; firmware không cho START khi |Roll|>5°.
8. Bấm **ARM**, đợi OK; trong 15 giây bấm **START**.
9. Quan sát đồng thời góc thật, góc app, rate, output và motor; sẵn nút STOP và ngắt nguồn.
10. Hết lượt, xác nhận motor dừng. Ghi kết quả; chờ đoạn sau dừng nếu cần rồi Stop log.
11. Đọc log trước khi đổi đúng một thông số. Không đổi gain/target đang Running.

Nếu ARM đã có nhưng muốn chỉnh cấu hình, bấm STOP rồi chỉnh/Apply/ARM lại. Nếu START bị từ chối, đọc ACK/reason; không nhấn liên tục để cố chạy.

## 11. Rate Roll — kiểm chứng vòng trong trước

### 11.1. Cấu hình cơ sở

| Ô app | Thiết lập |
|---|---|
| Mode | RATE_ROLL |
| Rate Kp | Một ứng viên P nhỏ có cơ sở từ đường lực; chưa chốt số cho hardware |
| Ki / Kd | 0 / 0 cho lượt P cơ sở |
| Angle P | 0; không tham gia trong RATE_ROLL |
| Base | B0 đã xác định ở bước 9 |
| Motor sign | Dấu đã kiểm chứng |
| Output limit | Giữ mức thử đã chọn; mặc định 20, không tự tăng khi rung |
| Target | 0°/s trước |
| Duration | Lượt đầu ngắn, ví dụ 1000ms; chỉ tăng thời gian khi phản ứng đã rõ |

Không có một Kp khởi đầu được bảo đảm phù hợp cho motor/đế chưa đo. Có thể chọn mức thấp hơn 0.8 của mô phỏng cho lượt đánh giá đầu; mức cụ thể phải dựa trên tác động/độ phân giải và dữ liệu phần cứng, không suy chỉ từ một số gain.

Phép tính giúp hiểu mức lệnh, không phải đơn thuốc tuning:

```text
Kp = 0.1, sai số rate = 5°/s → P = 0.5 duty
Kp = 0.8, sai số rate = 5°/s → P = 4 duty
```

Duty bị làm tròn số nguyên nên một P rất nhỏ có thể chưa tạo được thay đổi motor rõ; motor cũng có deadband và độ trễ. Không thấy phản ứng chưa chứng minh PID sai, và không phải lý do tăng Kp theo bước lớn.

### 11.2. Target 0°/s kiểm tra điều gì?

Rate0 yêu cầu **ngừng quay**, không yêu cầu thân nằm ngang. Thân có thể dừng ở +3° mà rate gần0; đó chưa phải lỗi giữ góc vì vòng góc đang tắt.

Khi có chuyển động nhỏ được tạo bởi cơ cấu thử từ xa, tác động điều khiển phải có xu hướng hãm chuyển động, không khuếch đại nó. Không đẩy thân bằng tay cạnh cánh để tạo nhiễu.

Nếu ban đầu hoàn toàn đứng yên và không có nhiễu, output gần0 chưa đủ để xác nhận chiều phản hồi hoặc chất lượng Rate.

### 11.3. Lệnh rate nhỏ có thời hạn

Sau khi các điều kiện trước đúng, có thể dùng các lượt ngắn đối xứng, chẳng hạn target +5°/s trong 300ms và lượt khác −5°/s trong 300ms. Chỉ thực hiện khi biên góc còn đủ và cơ khí đã cho phép; đây là bài kiểm tra chiều/đáp ứng ban đầu, không tự là nghiệm thu toàn bộ Rate.

Giữa hai lượt, motor phải dừng, bố trí lại góc đầu tương đương rồi ARM mới. Giữ gain/base/nguồn tương đương. Tốt nhất có video góc thật đồng bộ với log.

**Giới hạn phần mềm hiện tại cần hiểu đúng:**

- App/firmware v0.1 giữ một target trong mỗi START, chưa có sequencer +5 → 0 → −5 → 0 khi motor vẫn giữ base.
- Hết Duration thì motor về0. Việc rate giảm sau đó **không chứng minh Rate PID đã hãm về0 dưới cùng điều kiện base**.
- Không thay chuỗi xung đầy đủ của Stage6 bằng bốn lần START/STOP rồi coi tương đương.
- Để nghiệm thu đúng chuỗi xung Stage6 của PLAN, cần bổ sung và kiểm thử sequencer hoặc đường lệnh thay target được kiểm soát trước. Không cố nhập target mới trong lúc Running: bản hiện tại không cập nhật theo cách đó.

### 11.4. Quy tắc đổi P/I/D

| Hiện tượng có dữ liệu xác nhận | Hướng xử lý |
|---|---|
| Tác động làm rate đi xa target hơn ngay từ đầu | STOP; kiểm tra dấu, mapping, lực, gyro; chưa tuning gain |
| Đúng chiều, output nhỏ, rate phản ứng yếu, cơ khí không cản | Nghiên cứu tăng riêng Kp một bước nhỏ; giữ các yếu tố khác |
| Rate/motor đổi chiều nhanh, dao động tăng | Giảm mức P đang thử; kiểm tra rung/độ trễ/đế; không tăng limit |
| Rate bám tương đối nhưng còn overshoot cần giảm chấn | Đánh giá nhiễu rồi mới nghiên cứu thêm D nhỏ, giữ P cố định để so |
| Có lệch rate kéo dài dù mapping/base/đế đúng | Nghiên cứu I sau khi P đã ổn; kiểm tra giới hạn/windup |
| PID chạm biên liên tục mà error không giảm | STOP; tìm thiếu khả năng tác động, sai dấu, ràng buộc hoặc gain quá gắt |

Không chốt lịch tăng Kp hoặc giá trị D/I trước khi có dữ liệu. Nếu một thay đổi cải thiện một tiêu chí nhưng làm góc/rung xấu đi, chưa coi là cải thiện tổng thể.

### 11.5. Tiêu chí chuyển sang Angle

Theo PLAN, cần đo đủ bài rate hai chiều, không dao động tăng dần, ít chạm biên và ba lượt tương đương. Tiêu chí ban đầu:

- MAE rate ≤2°/s trên đoạn sau 100ms đầu mỗi xung ±5°/s.
- Trở về vùng ±2°/s trong 0.5s sau khi target được đưa về0 khi hệ vẫn chạy.
- Chạm biên không quá10% mẫu trong bài; nên dùng thống kê đủ nhanh và ghi rõ tỷ lệ tính từ telemetry.
- Phản ứng thực đúng chiều, góc không vượt phạm vi cho phép.

Vì v0.1 chưa có sequencer đầy đủ, không đánh dấu tiêu chí hãm sau xung đã đạt chỉ từ lượt Duration300ms. Nếu chưa có dữ liệu đủ để xác nhận Rate, ưu tiên hoàn thiện bước này trước khi tăng độ phức tạp sang Angle.

## 12. Angle Roll — tiến tới giữ thân ngang

Chỉ thực hiện sau khi Rate đã có cấu hình phù hợp và bằng chứng kiểm chứng cần thiết, cảm biến/đế/đường lực đạt.

### 12.1. Điền các ô

| Ô | Thiết lập |
|---|---|
| Mode | ANGLE_ROLL |
| Rate Kp/Ki/Kd | Giữ bộ Rate đã kiểm chứng |
| Angle P | Bắt đầu thấp có cơ sở rồi hiệu chỉnh; 2 trong mô phỏng là mốc tham chiếu, chưa phải số hardware |
| Base / Sign / Output limit | Giữ nguyên cấu hình Rate để so sánh |
| Target | 0° cho bài giữ ngang |
| Duration | Ngắn trước, tăng dần trong giới hạn10000ms khi phản ứng đã ổn |

Angle P tạo tốc độ mục tiêu:

```text
rateTarget = AngleP × (targetAngle − measuredAngle)
```

Ví dụ AngleP=1, target0°, measured+3° → target rate−3°/s. Đây là ví dụ đơn vị/phép tính, không xác nhận gain1 phù hợp cho đế.

### 12.2. Bài hồi phục hai phía

1. Bố trí +3° bằng mốc/cơ cấu đế khi motor tắt; không cầm thân cạnh cánh lúc START.
2. Log, Apply, đọc lại cấu hình, ARM và START với Target0.
3. Quan sát thân thật có đi về ngang hay không. Đường đo đẹp mà thân thật khác thì dừng kiểm tra cảm biến.
4. Chạy lượt đối xứng từ−3° với cấu hình tương đương.
5. Chỉ khi phản ứng ngắn ổn mới tăng duration đủ quan sát giữ ít nhất5s; ví dụ một lượt8000ms nằm trong giới hạn10s của firmware.
6. Lặp mỗi phía ba lượt. Bố trí đúng cùng điều kiện đầu; không tự ZERO tại tư thế nghiêng.

Tiêu chí ban đầu theo PLAN:

- Từ khoảng±3°, vào vùng±1° quanh mục tiêu trong2s.
- Giữ trong vùng đó ít nhất5s.
- Không dao động tăng dần/chạm output kéo dài.
- Đạt cả hai phía, ba lượt mỗi phía, đối chiếu góc thật.

Nếu tăng Angle P làm Rate target lớn nhưng measured Rate chưa theo được, quay lại vòng Rate; không giải quyết bằng tiếp tục tăng Angle P. Nếu Rate bám tốt mà hồi phục góc quá chậm, có thể nghiên cứu tăng riêng Angle P một bước nhỏ.

## 13. Cách đọc ba đồ thị

### Góc Roll

Trong ANGLE mode, đường đo cần về gần target và ở lại. Trong RATE mode, góc có thể đổi khi có lệnh quay; không dùng tiêu chí giữ0° cho RATE.

Đồ thị góc app dùng `command` làm đường cam ngay cả khi mode Rate, nên số cam lúc đó là lệnh rate, **không được diễn giải như góc mục tiêu**. Dùng đồ thị Rate để đánh giá Rate mode.

### Rate

- Xanh: measured; cam: rate target.
- Cần xem độ trễ, sai số, overshoot và dao động, không chỉ trung bình cả lượt.
- Rate trung bình0 vẫn có thể là dao động mạnh hai chiều.

### Output và motor

- Tím: phần sửa duty PID.
- Xanh lá/xám: LF/RF; dữ liệu CSV có đủ cả bốn motor.
- PID chạm±limit khác mixer bị cắt về0–220. Có thể PID chạm biên nhiều nhưng mixer vẫn báo0.
- Duty không phải lực hoặc RPM. PWM còn biên chưa chứng minh đủ mô-men sửa.

Vùng sau Stop phải tách khỏi vùng đang Running. Thân về ngang khi motor đã tắt có thể do đế/dây hoặc thao tác, chưa phải PID thành công.

## 14. Lỗi thường gặp và xử lý

| Thông báo / hiện tượng | Việc cần làm |
|---|---|
| Connect lỗi / không có telemetry | Kiểm tra COM, cáp dữ liệu, app khác đang chiếm COM, đúng sketch và baud |
| WHO khác70/71 hoặc sensor unavailable | Kiểm tra board/chip/SPI/nguồn; không ép code chấp nhận chip chưa xác minh |
| Calibration không xong | Giữ yên, kiểm tra rung/nguồn; timeout30s thì sửa nguyên nhân rồi CAL lại |
| CONFIG bị từ chối | STOP trước; dùng dấu chấm thập phân; số hữu hạn; output limit10–40 vì I limit mặc định10 |
| ARM bị từ chối | Xem trạng thái/epoch; STOP rồi chờ telemetry mới; không có phiên Armed khác |
| START bị từ chối | Kiểm tra calibrated, verified, gain dương, AngleP dương khi Angle, độ lớn Roll≤5°, duration1–10000ms, ARM chưa quá15s |
| SATURATION_TRACKING | Output chạm biên + error>5°/s hoặc mixer clipping kéo dài; không tự nâng limit để chạy tiếp |
| ENVELOPE | Góc>15° hoặc rate>150°/s; giữ ngưỡng, tìm nguyên nhân mất kiểm soát |
| TIMING / ESTIMATOR_TIMING / STALE_CONTROL | Lưu log, kiểm tra lịch mẫu, lỗi bus/task; không gọi là gain xấu và tăng motor |
| HEARTBEAT / TCP_DISCONNECT | Kiểm tra kết nối; xử lý khi motor tắt; không tự restart |
| PWM / queue overflow | Dừng và sửa lỗi phần mềm/phần cứng trước khi thử tiếp |
| Hết lượt ghi DURATION | Kết thúc theo thời hạn, không phải bằng chứng đạt ổn định |
| STALE trên app | Dữ liệu board không còn mới; STOP/ngắt nguồn theo bố trí, chưa coi hình cuối là trạng thái hiện tại |

Firmware có ngưỡng tuổi output100ms và heartbeat2s, nhưng độ trễ ngắt thực tế chưa đo trên board. Không dùng chúng để cho phép đứng sát cánh.

## 15. Phiếu kết quả mỗi lượt

Sao chép bảng này cho từng cấu hình:

| Mục | Giá trị |
|---|---|
| ID lượt / giờ / thư mục log | |
| Version / source ID / WHO | |
| USB hoặc Wi-Fi | |
| Đế / nguồn / điện áp / cánh | |
| Mode / Target / Duration | |
| Rate Kp / Ki / Kd | |
| Angle P / Base / Motor sign / Output limit | |
| Góc thật và góc app trước Start | |
| Yếu tố duy nhất thay đổi so với lượt đối chứng | |
| Sai số rate / dao động / độ trễ | |
| Góc vào vùng±1° lúc nào, ở lại bao lâu | |
| Tỷ lệ PID limit / mixer clip / dt bất thường | |
| Cách kết thúc và reason từ events.log | |
| Đạt / chưa đạt / chưa đủ dữ liệu | |
| Quyết định lượt sau | |

Không điền chỉ số định lượng từ nhìn đồ thị khi chưa tính được; ghi “chưa tính” và giữ CSV.

## 16. Kết thúc buổi thử

1. STOP, xác nhận motor dừng, ngắt nguồn trước khi chỉnh/tháo cơ khí.
2. Stop log, giữ `telemetry.csv` và `events.log` cùng nhau.
3. Lưu cấu hình đã đọc lại từ board, không chỉ ảnh ô nhập.
4. Nếu có video, ghi cách đồng bộ thời điểm Start với log.
5. Ghi các bước đã đạt và chưa đạt. Giữ ít nhất một lượt thất bại có chú thích để so với lượt tốt.
6. Cập nhật mô hình Simulink bằng thông tin đo được, không sửa plant chỉ để “đẹp giống log”.

**Chỉ chốt “đã giữ Roll trên đế” khi đủ bài hai phía, thời gian giữ và độ lặp lại.** Nếu mới xác nhận cảm biến hoặc dấu motor, đó vẫn là tiến độ có ích, nhưng chưa gọi là đã cân bằng. Kết quả trên đế chưa xác nhận bay/hover.

## 17. Tài liệu/source để đối chiếu

- [Kế hoạch gốc](../../PLAN.md).
- [Controller và giới hạn hiện tại](../firmware/DroneRebuild/RollCore.h).
- [Điều kiện START/BENCH và motor gate](../firmware/DroneRebuild/DroneRebuild.ino).
- [Giao thức](PROTOCOL.md), [trạng thái kiểm chứng](STATUS.md), [Simulink](SIMULINK.md).
- [PX4 manual PID tuning](https://docs.px4.io/v1.16/en/config_mc/pid_tuning_guide_multicopter): tài liệu đối chiếu nguyên tắc Rate trước Angle và đánh giá tracking; không lấy gain PX4 nạp vào dự án.

**Điểm cần hoàn thiện trước nghiệm thu Rate đầy đủ:** sequencer target +5→0→−5→0 trong một phiên, có bảo vệ và test; app v0.1 chưa cung cấp bài này. Các bước trong tài liệu không giả định tính năng đó đã tồn tại.
