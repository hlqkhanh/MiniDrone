# Chạy DroneRebuild trên MATLAB Online

## 1. Upload một gói

Giữ mô hình `DroneRoll.slx` đang học; mô hình mới có tên khác.

1. Trên máy Windows mở thư mục `MiniDrone/DroneRebuild`.
2. Trong MATLAB Online, khung Files, chọn Upload Files (hoặc kéo thả) file `DroneRebuild_Simulink.zip` vào MATLAB Drive.
3. Ở Command Window chạy:

```matlab
unzip('DroneRebuild_Simulink.zip');
cd('DroneRebuild/simulation');
build_roll_model
```

Script tạo và mở `DroneRebuildRoll.slx`. Nó thử biên dịch S-function C++ bằng MEX để chạy đúng `RollCore.h` của firmware. Nếu môi trường Online không hỗ trợ, nó báo rõ và chuyển sang bản MATLAB sau khi kiểm tra 3600 vector đầu vào/đầu ra từ C++.

Nếu muốn dùng ngay bản MATLAB, bỏ bước MEX:

```matlab
build_roll_model('matlab')
```

Hai cách chưa được chạy trên MATLAB tại máy Windows này. Nếu có lỗi, giữ nguyên thông báo và gửi lại để sửa. Không dùng kết quả native thay cho việc xác nhận mô hình Simulink đã chạy.

Script không ghi đè model có sẵn: muốn tạo lại hãy lưu bản sửa sang tên khác, đóng model, đổi tên file cũ rồi chạy lại.

## 2. Chạy bài đầu

Chạy trong Command Window:

```matlab
results = run_roll_tests;
```

Nó thực hiện:

- Đối chiếu controller MATLAB với fixture C++.
- ANGLE: từ +3° về 0°.
- RATE: xung +5°/s 300 ms, sau đó −5°/s 300 ms.
- Plant yếu hơn, motor chậm hơn.
- Đảo dấu tác động vật lý: phải kích hoạt fault, không được coi là PASS bám góc.
- Thêm nhiễu gyro giả định.

Bảng `Pass` và ba biểu đồ xuất hiện. Có assertion khi bài thử không đạt: không đổi tiêu chí để che lỗi.

## 3. Ý nghĩa các khối

```text
Reference → Controller → Roll_plant → Sensor_delay → phản hồi Controller
                ↑            ↑
          Controller_config  Synthetic_plant
```

- `Reference`: góc 0 hoặc bài xung rate.
- `Controller`: Angle P → Rate PID → duty mixer; bước 5 ms.
- `Roll_plant`: mô-men/duty, độ trễ motor, quán tính, giảm chấn và mô-men đàn hồi đế tùy chọn; tích phân nội bộ 1 ms.
- `Sensor_delay`: trễ một mẫu 5 ms. Góc đang lý tưởng hóa; gyro có thể thêm nhiễu/bias.
- `Scope`: toàn bộ các tín hiệu; `run_roll_tests` tách thành ba đồ thị dễ đọc.
- Plant không phụ thuộc dấu mixer của controller: đổi sai motor sign không được tự đổi luôn plant để che lỗi.

Mô hình này chưa mô phỏng accel 3D và Madgwick toàn chuỗi, không kiểm tra được rung cao tần, sụt nguồn, SPI thật hoặc độ trễ task trên ESP32. Estimator có test C++ riêng. Đây là mô hình một trục trên đế, chưa phải drone bay 6DOF.

## 4. Thay thông số để nghiên cứu

Trong model, tham số nằm trong **Model Workspace**, không phải Base Workspace. Cách đặt bằng lệnh:

```matlab
[cfg, plant] = rebuild_config;
cfg(1) = 0.4;          % Rate Kp: thử so với 0.8
mode = 0;             % 0 RATE; 1 ANGLE; 2 DRY_RUN calculation
mw = get_param('DroneRebuildRoll','ModelWorkspace');
assignin(mw,'cfg',cfg);
assignin(mw,'plant',plant);
assignin(mw,'mode',mode);
out = sim('DroneRebuildRoll');
```

Hoặc nhấn Run màu xanh, Stop Time=10. `run_roll_tests` luôn lấy lại cấu hình từ `rebuild_config.m`, nên nếu muốn thay cấu hình toàn bộ bộ test thì sửa file đó. Nhưng khi đổi config, fixture chỉ còn khớp bộ config ban đầu: `verify_controller` cố ý dùng cấu hình fixture cố định, không dùng gain mới của bạn.

`cfg` theo thứ tự:

| Vị trí | Tham số | Đơn vị |
|---|---|---|
| 1,2,3 | Rate Kp, Ki, Kd | Theo rate deg/s và duty |
| 4 | Angle Kp | (deg/s)/deg |
| 5,6 | Output limit, I limit | duty |
| 7 | Rate target limit | deg/s |
| 8 | D cutoff | Hz |
| 9,10 | Base, motor sign | duty, ±1 |
| 11,12 | Fault angle, fault rate | deg, deg/s |
| 13,14 | Saturation tracking timeout, error | s, deg/s |

`plant` theo thứ tự: quán tính, giảm chấn, mô-men/duty, thời hằng motor, dấu vật lý, góc đầu, biên nhiễu gyro, bias gyro, độ cứng đế.

Tham số khởi đầu là giả định: J=0.0001 kg·m², b=0.00004 N·m·s/rad, torque/duty=0.00002 N·m, motor lag=0.06 s. Không tự coi đây là số đo của drone thật.

## 5. Từ mô phỏng sang thử drone

Không có nút tự upload gain mô phỏng vào firmware. App phần cứng vẫn khởi động gain=0. Lưu cấu hình ứng viên và kết quả, rồi ngày mai kiểm tra hardware/mapping/đế trước. Bắt đầu bằng Rate và bài nhỏ có thời hạn, dùng dữ liệu thật cập nhật `plant`. Chỉ ghép Angle sau khi Rate thực đạt.

Chạy tốt trong mô phỏng không tự vượt qua các điều kiện phần cứng trong PLAN.md.
