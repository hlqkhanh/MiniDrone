# Kế hoạch làm lại hệ điều khiển Roll theo từng stage

## 1. Mục tiêu và phạm vi

Tạo project độc lập tại `MiniDrone/DroneRebuild/`, giữ nguyên `Drone`, `Simulator` và ứng dụng đọc IMU riêng.

**Mốc hoàn thành đầu tiên:** trên đế một trục, drone có thể trở về Roll gần 0° từ một góc lệch nhỏ và giữ ổn định, có log chứng minh.

Các lựa chọn đã chốt:

- Dùng phần cứng ESP32 và bộ drone hiện có.
- Làm phần cứng từng bước; mô phỏng hỗ trợ kiểm tra logic.
- Có USB và Wi-Fi ngay từ đầu, gồm kết nối trực tiếp ESP32 và qua router.
- Chuẩn bị đế Roll vì hiện chưa có.
- Chưa triển khai bay tự do, giữ độ cao, vị trí X/Y hoặc PID Yaw.

Không dùng `docs/PID-controller.md` hoặc các tài liệu tự sinh làm căn cứ xác nhận thuật toán. Nguồn thiết kế chính là phần cứng thực tế và tài liệu/source chính thức như [Crazyflie](https://www.bitcraze.io/documentation/repository/crazyflie-firmware/master/functional-areas/sensor-to-control/controllers/) và [ArduPilot](https://ardupilot.org/dev/docs/apmcopter-programming-attitude-control-2.html).

## 2. Kiến trúc và các nguyên tắc cố định

### Luồng điều khiển

```text
IMU → Hiệu chỉnh và đổi trục
          ├→ Gyro Roll → Rate PID ───────────────┐
          └→ Madgwick → Góc Roll → P góc        │
                                   ↓          │
                              Rate target ────┘
                                              ↓
                                   Phần sửa duty
                                              ↓
                                  Mixer + base duty
                                              ↓
                                         Bốn motor
```

Có hai chế độ thử độc lập:

- **RATE_ROLL:** nhận trực tiếp tốc độ mục tiêu, không chạy vòng góc.
- **ANGLE_ROLL:** P góc tạo tốc độ mục tiêu cho Rate PID.

Các quy ước:

- Góc: độ; tốc độ quay: °/s; `dt`: giây.
- Output vòng góc: °/s; output Rate PID: phần sửa duty.
- Toàn bộ đổi trục và chiều motor nằm trong cấu hình phần cứng rõ ràng, không rải dấu âm ở nhiều nơi.
- D của Rate PID lấy theo số đo và có lọc thông thấp; I có giới hạn và chống windup.
- Gain ban đầu bằng 0; chưa cấu hình đúng thì không cho chạy điều khiển.
- Madgwick dùng gia tốc và gyro để ước lượng tư thế; chưa dùng từ kế hoặc coi Yaw là hướng tuyệt đối.

### Thời gian và giao tiếp

- Vòng đọc IMU/điều khiển mục tiêu **200 Hz**, dùng thời gian thực đo được.
- Tách xử lý điều khiển khỏi đọc lệnh, truyền telemetry và phục vụ mạng.
- USB Serial **460800 baud**; TCP **3333** cho Wi-Fi.
- Hai đường dùng cùng parser, cùng lệnh và cùng giới hạn.
- Chỉ một kết nối được sở hữu phiên chạy motor; kết nối khác vẫn được gửi STOP.
- Không quét mạng, ghi cấu hình Wi-Fi vào bộ nhớ hoặc thay kết nối khi đang Armed/Running.
- Telemetry thông thường 50 Hz; chế độ ghi thử nghiệm 100 Hz, có đếm mất mẫu và thời gian cập nhật điều khiển.
- App không tính PID hoặc tự tạo dữ liệu đo thay cho board.

### Trạng thái và bảo vệ

```text
Khởi động → Hiệu chuẩn → Disarmed → Armed → Running
                               ↑                │
                               └── Stop/Fault ──┘
```

- Motor tắt khi khởi động, reset, hiệu chuẩn và lỗi.
- Chỉ một cơ chế phân xử được phép ghi motor; STOP được chốt để lệnh cũ không bật motor trở lại.
- ARM hết hạn sau 15 giây nếu chưa Start.
- Heartbeat mỗi 500 ms; mất heartbeat dừng trong tối đa 2,1 giây.
- Không có mẫu điều khiển hợp lệ quá 100 ms thì dừng bằng cơ chế giám sát độc lập.
- Mọi lần Start có thời hạn; không tự chạy lại sau lỗi.

## 3. Các stage triển khai

### Stage 0 — Chốt phần cứng và tạo nền project

**Mục tiêu:** biết chắc đang điều khiển bo nào và firmware nào.

**Công việc:**

- Tạo project mới, không tham chiếu trực tiếp vào file source/build của project cũ.
- Ghi bảng chân theo sơ đồ và kiểm tra bo thực:
  - SPI: SCK18, MOSI23, MISO19, CS5.
  - Motor: LF14, RF15, LR12, RR13.
- Ghi loại pin, điện áp cho phép, khối lượng bộ drone, vị trí IMU và hướng đầu.
- Đọc WHO_AM_I để xác nhận chip; hỗ trợ ban đầu MPU6500 `0x70` và MPU9250 `0x71`.
- Mỗi firmware báo phiên bản, build ID và mã cấu hình phần cứng.

**Đầu ra:** bản đồ phần cứng và firmware tối thiểu, motor luôn tắt.

**Qua stage khi:** reset luôn về motor tắt; nhận dạng IMU ổn định và phiên bản hiển thị đúng.

---

### Stage 1 — Đọc cảm biến và ghi dữ liệu thô

**Mục tiêu:** kiểm chứng đường đo trước khi có PID.

**Công việc:**

- Đọc đủ gia tốc và gyro, đổi đơn vị đúng.
- Kiểm tra lỗi truyền, dữ liệu không hợp lệ và chu kỳ cập nhật.
- Định nghĩa rõ chiều dương Roll trên thân và đổi trục cảm biến tại một chỗ.
- Hiển thị raw data qua USB.
- Ghi các lượt đứng yên và nghiêng từng trục, motor không chạy.

**Đầu ra:** CSV thô có timestamp board, dữ liệu IMU và thống kê thời gian.

**Qua stage khi:**

- Không có dữ liệu NaN hoặc mẫu lỗi bị dùng tiếp như hợp lệ.
- Xoay đúng trục thì gyro đúng trục và đúng chiều.
- Ghi được 60 giây đứng yên để đánh giá bias/nhiễu.
- Có số đo `dt` thực, không suy chu kỳ PID từ FPS hoặc tốc độ telemetry.

---

### Stage 2 — Hiệu chuẩn và ước lượng tư thế

**Mục tiêu:** góc hiển thị phải tương ứng tư thế thật.

**Công việc:**

- Hiệu chuẩn gyro bằng 500 mẫu đứng yên; chuyển động làm bắt đầu lại cửa sổ.
- Dùng Madgwick 6-DoF, cấu hình được ghi trong log.
- Tách hiệu chỉnh gyro bias khỏi đặt mốc góc.
- Giữ dữ liệu thô để đối chiếu với góc sau ước lượng.
- Không tự đặt lại góc khi nhận thấy thân đứng yên.

**Bài kiểm tra:**

- Đặt ngang rồi nghiêng theo các mốc cơ khí khoảng ±5° và ±10°.
- Đưa về cùng một mốc nhiều lần.
- Đặt yên 60 giây và quan sát sai lệch.
- Xác nhận Yaw tương đối có thể trôi.

**Qua stage khi:** Roll đúng chiều, gần các mốc cơ khí trong khoảng ±2° và không trôi liên tục ở trạng thái đứng yên. Đây là ngưỡng nghiệm thu ban đầu, cần ghi độ chính xác của dụng cụ đối chiếu.

---

### Stage 3 — Hoàn thiện USB, Wi-Fi và logging

**Mục tiêu:** kết nối không làm thay đổi hoặc làm nghẽn điều khiển.

**Công việc:**

- Hỗ trợ USB, AP trực tiếp và router/hotspot.
- Thêm thông điệp phiên bản, trạng thái, cấu hình được chấp nhận, telemetry và lỗi.
- Lệnh gồm INFO, CALIBRATE, ARM, START, STOP, KEEPALIVE và chỉnh cấu hình khi Disarmed.
- Mọi lệnh có phản hồi thành công/thất bại; ô nhập trên app không được coi là cấu hình board.
- Log lưu cấu hình thực, mode, build ID, timestamp và lý do kết thúc.
- Dùng hàng đợi truyền có giới hạn; truyền chậm không được chặn control task.

**Đầu ra:** ứng dụng WPF tối thiểu có kết nối, số đo, trạng thái và ghi log.

**Qua stage khi:**

- USB và hai chế độ Wi-Fi đều nhận đúng dữ liệu.
- Hai kết nối không thể cùng sở hữu motor.
- Mất mạng, đóng app hoặc gửi dữ liệu lỗi được xử lý đúng.
- Tải mạng không gây lỗi thời gian bị che giấu.

---

### Stage 4 — Motor và mixer, chưa chạy PID

**Mục tiêu:** biết lệnh tác động đúng motor và đúng chiều sửa.

**Công việc:**

- Thử từng motor khi đã tháo cánh.
- Dùng PWM 20 kHz, 8 bit theo phần cứng hiện có.
- Chế độ thử motor ban đầu giới hạn tối đa 35% duty và 2 giây mỗi lệnh.
- Kiểm tra vị trí LF/RF/LR/RR và chiều quay.
- Thêm `MIXER_DRY_RUN`: tính bốn duty nhưng không xuất xuống phần cứng.
- Kiểm tra phần sửa Roll dương/âm làm tăng đúng nhóm và giảm đúng nhóm.

**Qua stage khi:**

- Bốn motor đúng nhãn và reset không tự chạy.
- Mixer có kiểm thử dấu, giới hạn và làm tròn.
- STOP, timeout và mất chủ sở hữu đều cắt output.
- Phân biệt được output PID bị giới hạn với mixer cắt lệnh.

---

### Stage 5 — Chuẩn bị và kiểm chứng đế Roll

**Mục tiêu:** phép thử cho phép đánh giá một trục thay vì trộn ảnh hưởng dây và các trục khác.

**Yêu cầu đế:**

- Có trục Roll xác định; Pitch/Yaw và tịnh tiến được giữ có kiểm soát.
- Ma sát đủ nhỏ để quan sát chuyển động hồi phục.
- Vị trí trục so với trọng tâm được ghi nhận.
- Có phạm vi xoay giới hạn và che chắn phù hợp.
- Không có điểm kê giữ thân ở một góc lệch.

**Công việc:**

- Đánh dấu mốc ngang thật và góc nhỏ hai phía.
- Khi motor tắt, kiểm tra thân có xoay và bị mô-men hồi do đế hay không.
- Kiểm chứng chiều lực sửa trong bố trí thử phù hợp.
- Ghi mức nguồn, tình trạng cánh và điểm làm việc base dùng cho từng lượt.

**Qua stage khi:** có thể lặp lại cùng bài thử từ cùng góc nhỏ, biết rõ chiều lực sửa và đặc tính ràng buộc chính của đế.

**Nếu chưa có đế đạt yêu cầu:** vẫn hoàn thiện Stage 0–4, nhưng chưa chuyển sang tuning có lực đẩy.

---

### Stage 6 — Kiểm chứng Rate Roll độc lập

**Mục tiêu:** điều khiển được tốc độ quay trước khi yêu cầu giữ góc.

**Cấu trúc:**

```text
Rate target → Rate PID ← Gyro Roll
                   ↓
             Mixer và motor
```

**Trình tự:**

1. Dùng target 0°/s để kiểm tra tác động chống chuyển động quay.
2. Bắt đầu Ki=Kd=0 và Kp nhỏ.
3. Khi phản ứng đúng chiều, dùng bài lệnh nhỏ định sẵn:
   - +5°/s trong 300 ms.
   - 0°/s trong 700 ms.
   - −5°/s trong 300 ms.
   - 0°/s trong 700 ms.
4. Chỉ đổi một tham số mỗi lượt; giữ base và điều kiện đầu tương đương.
5. Thêm D khi có bằng chứng cần giảm chấn và đã đánh giá nhiễu; thêm I khi có sai lệch kéo dài sau khi các điều kiện trước đã đúng.

Các lượt tự động này có giới hạn góc và thời gian; không giữ rate khác 0 vô thời hạn. Chưa chốt gain bằng số trước khi có dữ liệu.

**Dữ liệu bắt buộc:** target, measured rate, error, P/I/D, output trước/sau giới hạn, bốn duty, Roll, `dt` và cờ lỗi.

**Qua stage khi:**

- Hai chiều đều tạo phản ứng đúng.
- Measured đi theo target, không dao động tăng dần.
- Sau khi về target 0, tốc độ lắng xuống.
- Tỷ lệ chạm giới hạn thấp và không che giấu thiếu khả năng chấp hành.
- Ba lượt cùng cấu hình cho kết quả tương đương.

Ngưỡng đánh giá ban đầu: MAE rate không quá 2°/s trên đoạn sau 100 ms đầu của mỗi xung, tốc độ trở về vùng ±2°/s trong 0,5 giây sau xung, chạm giới hạn không quá 10% mẫu. Nếu không đạt, điều tra và hiệu chỉnh; không nới tiêu chí chỉ để qua stage.

---

### Stage 7 — Ghép P góc với Rate PID

**Mục tiêu:** từ góc lệch nhỏ, đưa Roll về gần 0°.

**Cấu trúc:**

```text
Roll target − Roll → P góc → Rate target → Rate PID → Mixer
```

**Công việc:**

- Giữ cấu hình Rate đã qua Stage 6.
- Vòng góc chỉ dùng P, có giới hạn rate target.
- Bài thử bắt đầu gần ngang, dùng mục tiêu nhỏ ±3° rồi trở về 0°.
- Kiểm tra phục hồi từ góc ban đầu nhỏ được bố trí khi motor đã tắt.
- Mỗi lượt có thời hạn; ngắt khi vượt phạm vi thử hoặc dao động tăng rõ.

**Ngưỡng nghiệm thu ban đầu:**

- Từ lệch khoảng ±3°, trở về vùng ±1° quanh mục tiêu trong 2 giây.
- Giữ trong vùng đó ít nhất 5 giây của lượt thử.
- Không có dao động tăng dần hoặc chạm giới hạn kéo dài.
- Đạt lặp lại ở cả hai phía, mỗi phía ba lượt.

Không dùng ZERO tại thân nghiêng để biến sai lệch vật lý thành số 0.

---

### Stage 8 — Kiểm chứng độ lặp lại và chốt bản Roll v1

**Mục tiêu:** xác định cấu hình đã đạt trong phạm vi nào.

**Công việc:**

- Lặp lại với USB và Wi-Fi.
- Thử lại sau khởi động/hiệu chuẩn mới.
- Thử nhiễu nhỏ và biến thiên nguồn trong phạm vi phần cứng cho phép.
- Kiểm tra mất kết nối, lỗi IMU, quá thời gian và dừng khẩn.
- Lưu một bộ dữ liệu đạt và một bộ dữ liệu thất bại có chú giải.
- Chốt phiên bản source, cấu hình và hướng dẫn thử.

**Hoàn thành khi:** có hồ sơ chứng minh giữ Roll trên đúng đế và điều kiện đã mô tả. Không gọi kết quả này là đã xác nhận bay hoặc tự hover.

Pitch và phối hợp hai trục là giai đoạn tiếp theo, lập kế hoạch riêng sau mốc này.

## 4. Kiểm thử và giao diện dữ liệu cần có

### Kiểm thử tự động

- Parser: dữ liệu chia mảnh, dòng thiếu/thừa, NaN, sai phiên bản và hàng đợi đầy.
- Calibration và estimator: dữ liệu đứng yên, chuyển động, đổi trục, timestamp lỗi và reset.
- PID: đơn vị, `dt`, reset, D không giật theo setpoint, giới hạn và anti-windup.
- Mixer: đúng motor, dấu, làm tròn và phân biệt các loại bão hòa.
- Safety: STOP ưu tiên, lệnh cũ không chạy lại, timeout, lỗi cảm biến và chủ sở hữu kết nối.
- Build firmware ESP32 và app WPF; dùng dữ liệu giả để kiểm tra giao diện trước phần cứng.

### Log và biểu đồ

- Mỗi phiên có ID, build ID, mode, cấu hình thực và ghi chú đế/nguồn.
- Timestamp lấy từ board; số mẫu mất và thống kê thời gian được ghi riêng.
- Ba biểu đồ chính: góc/target, rate/target, output/motor.
- P/I/D và giới hạn nằm cạnh biểu đồ liên quan.
- Có bản tin sự kiện cho Start, Stop, thay cấu hình và lỗi.
- Không dùng tên `torque` cho output duty, không biến dữ liệu thiếu thành số 0.

## 5. Các giả định và quy tắc chuyển stage

- Dùng Arduino-ESP32 core 3.3.12 đang có và .NET 8 cho app; khóa phiên bản trong hướng dẫn build.
- Tái sử dụng ý tưởng, model 3D và thành phần được kiểm tra từ project cũ dưới dạng bản sao; không phụ thuộc trực tiếp vào source đang bị thay đổi.
- Chưa giả định model Gazebo hiện tại với khối lượng 1 kg và lực motor giả định là mô hình đúng của bộ drone 8520.
- Mỗi stage có báo cáo: cấu hình, bài thử, kết quả và đạt/chưa đạt.
- Kiểm thử máy tính chỉ xác nhận phần được mô phỏng; nghiệm thu cơ khí, lực và cảm biến phải có dữ liệu phần cứng.
- Các gain và base được xác định bằng phép thử sau khi qua các điều kiện nền; không sao chép từ Parrot, Crazyflie hoặc log cũ.
- Mốc bắt đầu thực hiện là **Stage 0**, đồng thời chuẩn bị đế cho Stage 5. Không bắt đầu bằng tuning PID hoặc viết toàn bộ simulator.
