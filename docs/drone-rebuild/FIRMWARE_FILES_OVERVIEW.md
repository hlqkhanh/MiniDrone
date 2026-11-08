# Bản đồ firmware DroneRebuild: mỗi file, class và function làm gì?

Muốn đọc giải thích riêng từng file, bắt đầu từ [bộ hướng dẫn firmware dành cho SE mới học nhúng](firmware-guide/README.md).

Tóm tắt theo source trong `firmware/DroneRebuild/`, đối chiếu ngày **08/11/2026**. Đây là tài liệu để định hướng khi đọc code; phần công thức chi tiết nằm trong [tài liệu PID](PID_FORMULAS_AND_WORKED_EXAMPLES.md).

**Các file này cùng tạo thành một firmware**, không phải tám chương trình nạp riêng. Arduino bắt đầu từ `setup()` và `loop()` trong `DroneRebuild.ino`; các header `.h` cung cấp cấu hình, data type, function và class được dùng bởi sketch.

## 1. Nhìn nhanh toàn bộ thư mục

| File | Nhiệm vụ chính | Câu hỏi file này trả lời |
| --- | --- | --- |
| [DroneRebuild.ino](../../firmware/DroneRebuild/DroneRebuild.ino) | Ghép toàn bộ hệ thống: khởi tạo, nhận lệnh, chạy điều khiển, giám sát PWM, gửi dữ liệu | Chương trình chạy theo thứ tự và trao đổi dữ liệu thế nào? |
| [StabilizedDescent.h](../../firmware/DroneRebuild/StabilizedDescent.h) | Profile giảm collective, pause/resume/boost | Làm sao giữ PID khi giảm Base? |
| [RollCore.h](../../firmware/DroneRebuild/RollCore.h) | Thuật toán PID, mixer và quản lý phiên điều khiển | Từ góc/rate mục tiêu tính ra lệnh motor thế nào? |
| [Imu.h](../../firmware/DroneRebuild/Imu.h) | Đọc IMU SPI, ước lượng tư thế và quản lý mốc góc | Góc và tốc độ quay đầu vào lấy từ đâu? |
| [Hardware.h](../../firmware/DroneRebuild/Hardware.h) | Các hằng số phần cứng và quy ước hệ trục | Chân nào, chiều nào, baud và parameter nền nào được dùng? |
| [NonBlockingTx.h](../../firmware/DroneRebuild/NonBlockingTx.h) | Gửi TCP từng phần theo cách không chặn | Gửi dữ liệu thế nào để mạng chậm không giữ chương trình quá lâu? |
| [BuildInfo.h](../../firmware/DroneRebuild/BuildInfo.h) | Chuỗi nhận dạng bộ source tại lần sinh thông tin build | Làm sao đối chiếu bản firmware với source đã build? |
| [WifiSecrets.example.h](../../firmware/DroneRebuild/WifiSecrets.example.h) | Mẫu cấu hình kết nối router | Điền tên/mật khẩu Wi-Fi ở đâu nếu muốn dùng router? |

## 2. Luồng dữ liệu cần nhớ trước khi đọc từng function

```text
App gửi lệnh qua USB / TCP
        ↓
receive() ghép đủ một dòng → hàng đợi commands
        ↓
controlTask() xử lý lệnh và đọc mẫu IMU
        ↓
Hiệu chuẩn gyro → ước lượng tư thế → góc/rate body
        ↓
Controller hoặc DualController tính PID và mixer
        ↓
publish() cập nhật đề xuất duty vào Gate
        ↓
guard() kiểm tra quyền xuất và thời gian → ghi PWM motor

Song song:
controlTask() tạo phản hồi/telemetry
        ↓
hàng đợi replies / samples
        ↓
loop() gửi từng phần về USB / TCP
```

Điểm quan trọng: **tính PID và ghi PWM là hai công việc tách nhau**. `Controller` chỉ tính số; `guard()` mới là nơi ghi duty motor trong quá trình vận hành bình thường. `setup()` cũng ghi PWM=0 lúc khởi tạo.

## 3. DroneRebuild.ino — nơi tổ chức chương trình

### 3.1. Các data type và variable dùng chung

| Thành phần | Nhiệm vụ |
| --- | --- |
| `Gate` và variable `gate` | Giữ state được phép xuất motor, duty đề xuất, epoch, thời gian mẫu/heartbeat |
| `gateMux` | Bảo vệ việc đọc/ghi Gate bằng vùng tới hạn (critical section), vì nhiều ngữ cảnh cùng truy cập |
| `Command` | Một lệnh đã nhận: ai gửi (`owner`), mã yêu cầu (`id`) và nội dung (`text`) |
| `Line` | Một dòng cần gửi ra: nội dung và kết nối đích; `owner=0` dùng cho phát tới các đường phù hợp |
| `Input` | Buffer ghép dòng đang nhận; giữ số ký tự và flag tràn. USB/TCP có buffer riêng |
| `commands` | Queue (queue) chuyển lệnh từ phần giao tiếp sang tác vụ điều khiển |
| `replies` | Queue phản hồi như ACK, INFO, HEALTH |
| `samples` | Queue telemetry mẫu điều khiển, có dung lượng giới hạn |
| `droppedTelemetry`, `droppedReplies` | Counter dữ liệu không gửi/không đưa được vào queue tương ứng |
| `pwmReady` | State khởi tạo/ghi PWM; dùng variable atomic để trao đổi giữa các ngữ cảnh |
| `server`, `client`, `clientId`, `hadTcpClient` | Quản lý server TCP, client hiện tại và nhận biết mất kết nối |
| `guardTimer` | Handle của timer gọi `guard()` định kỳ |
| `usbPending`, `tcpPending` và các variable `At/Size` | Dòng đang gửi dở và vị trí gửi của từng đường; không giả định gửi một lần là xong |

`owner` xác định kết nối sở hữu phiên. `id` giúp ghép yêu cầu với ACK. `epoch` là số thế hệ điều khiển: khi dừng, nó thay đổi để lệnh/quyền xuất cũ không tiếp tục được dùng.

### 3.2. Các function chính

| Function | Tóm tắt công việc |
| --- | --- |
| `setup()` | Đặt motor về state ban đầu, mở Serial, khởi tạo LEDC PWM, queue, Wi-Fi AP/STA, TCP server, timer guard và tác vụ điều khiển |
| `loop()` | Quản lý kết nối, nhận lệnh USB/TCP, gửi các dòng còn dang dở và xử lý nghẽn truyền dữ liệu |
| `receive()` | Đọc có giới hạn số byte mỗi lượt, ghép tới ký tự xuống dòng, tách `<id> <command>`, đưa vào queue; xử lý dòng sai/tràn |
| `controlTask()` | Xử lý lệnh, quản lý phiên, đọc/hiệu chuẩn IMU, cập nhật tư thế, tính PID hoặc BENCH và tạo telemetry |
| `publish()` | Lambda bên trong `controlTask()`: chép đề xuất duty và các mốc thời gian vào Gate nếu epoch còn hợp lệ |
| `snapshot()` | Copy Gate trong critical section để đọc một snapshot nhất quán |
| `emergency()` | Đánh dấu dừng, tắt quyền xuất, tăng epoch và ghi lý do. PWM thực tế được guard cập nhật theo state đó |
| `StabilizedDescent` | Giảm collective trong controlTask, giữ PID và hỗ trợ pause/resume/boost |
| `guard()` | Kiểm tra state Gate, thời hạn/độ mới dữ liệu trong mọi phiên chạy, kể cả khi hạ; ghi PWM |
| `reply()` | Tạo dòng `ACK` cho đúng yêu cầu/kết nối, rồi đưa vào queue phản hồi |
| `enqueueOutput()` | Chuẩn bị một dòng cho các kênh cần nhận và khởi tạo send cursor riêng USB/TCP |
| `stateName()` | Đổi enum state thành chữ để đưa vào telemetry |

`guard()` là callback chạy trong `ESP_TIMER_TASK`, không phải một function PID hay một ISR tự viết. Chu kỳ yêu cầu là 5 ms. `controlTask()` cũng được lập lịch theo nhịp danh định 5 ms và tính `dt` từ mẫu thực tế; hai công việc không phải một function gọi nối tiếp cố định.

### 3.3. Bên trong controlTask() chia thành những nhóm gì?

| Nhóm | Các object/variable đáng chú ý | Nhiệm vụ |
| --- | --- | --- |
| Cảm biến | `imu`, `a`, `available` | Khởi tạo và đọc mẫu cảm biến mới |
| Hiệu chuẩn | `count`, `sum`, `lo`, `hi`, `bias`, `calibrated` | Tìm bias gyro từ 500 mẫu đứng yên phù hợp; bắt đầu lại khi mẫu không ổn định |
| Ước lượng/mốc góc | `attitude`, `levels[2]`, `angles`, `rates` | Tạo tư thế body, rate đã bù bias và góc sau bù mốc |
| Điều khiển | `configs[2]`, `controller`, `dualController`, `mode`, `target`, `pitchTarget`, `output` | Chọn cấu hình và chạy một trục hoặc Roll/Pitch đồng thời |
| Phiên/state | `session`, `axis`, `both`, `verified`, `props` | Quyền chạy, trục đang dùng và các xác nhận của người vận hành |
| BENCH | `bench`, `benchDuty[4]` | Dùng duty đặt trực tiếp thay vì kết quả PID |
| Theo dõi thời gian | `lastSample`, `dt`, `wake` | Đo khoảng mẫu và giữ nhịp tác vụ |
| Telemetry | `seq`, `telem`, `missed`, `late` | Đánh số mẫu, giảm nhịp gửi và thống kê việc đọc mẫu/thời gian |

`missed` tăng khi lượt đọc chưa có mẫu mới, không tự có nghĩa một mẫu vật lý đã mất. `late` đếm khoảng mẫu lớn hơn ngưỡng theo dõi của code.

### 3.4. Các nhóm lệnh mà controlTask() xử lý

| Nhóm lệnh | Nhiệm vụ |
| --- | --- |
| `INFO`, `TELEMETRY` | Đọc thông tin firmware và chọn nhịp telemetry |
| `CALIBRATE`, `ZERO_ROLL`, `ZERO_PITCH` | Hiệu chuẩn và lấy mốc góc |
| `AXIS`, `CONFIG`, `CONFIG_CASCADE` | Chọn trục và cập nhật cấu hình PID/motor |
| `VERIFY`, `PROPS_REMOVED` | Ghi nhận xác nhận từ người vận hành; không phải board tự kiểm tra vật lý |
| `ARM`, `START`, `START_BOTH` | Chuẩn bị và bắt đầu phiên điều khiển |
| `BENCH`, `BENCH_MULTI` | Chạy thử một hoặc nhiều motor bằng duty trực tiếp |
| `KEEPALIVE` | Cập nhật heartbeat từ kết nối sở hữu phiên |
| `STOP`, `DESCEND` | STOP cắt quyền xuất; DESCEND giảm collective nhưng vẫn giữ phiên Running và PID |

`receive()` đánh dấu dừng ngay khi nhận đủ dòng `STOP`, trước khi chờ `controlTask()` lấy lệnh từ queue. Ramp SOFT_STOP đã bỏ. DESCEND giữ PID hai trục Angle khi giảm collective, không tự phát hiện đáp; xem [hướng dẫn](STABILIZED_DESCENT.md).

Chi tiết cú pháp/điều kiện xem [PROTOCOL.md](PROTOCOL.md).

## 4. RollCore.h — phần tính toán điều khiển và quản lý phiên

File dùng C++ thông thường, không gọi trực tiếp SPI, Wi-Fi hoặc LEDC. Vì vậy firmware và các bài kiểm thử/mô phỏng native có thể dùng cùng controller.

### 4.1. Các data type và function tiện ích

| Thành phần | Nhiệm vụ |
| --- | --- |
| `namespace rebuild` | Gom các tên của lõi điều khiển vào một phạm vi |
| `bound(x, lo, hi)` | Giữ một số trong đoạn giới hạn, còn gọi là clamp |
| `Config` | Giữ gain Inner/Outer, Base, sign, Output/I/Rate limit và parameter lọc D |
| `Config::valid()` | Kiểm tra giá trị hữu hạn và phạm vi cấu hình hợp lệ |
| `Mode` | Phân biệt `Rate`, `Angle`, `DryRun` |
| `Axis` | Phân biệt `Roll` và `Pitch` |
| `Output` | Kết quả một trục: target rate, error, P/I/D, raw/u, bốn duty, flag giới hạn và lỗi |
| `DualOutput` | Chứa kết quả Roll và Pitch khi chạy cả hai |
| `State` | Các state `Disarmed`, `Armed`, `Running`, `Fault` của Session |

Trong `Config`, các field `maxAngle/maxRate` và `saturationSeconds/saturationError` còn phục vụ tương thích bố cục cấu hình cũ; không hiểu chúng là ngưỡng đang tự cắt motor theo góc/rate/bão hòa trong controller hiện tại.

### 4.2. Controller — tính PID cho một trục

| Thành phần | Nhiệm vụ |
| --- | --- |
| `integral_`, `previousError_` | Lưu phần I và sai số rate trước để tích phân hình thang |
| `previous_`, `derivative_` | Lưu rate trước và state lọc đạo hàm cho D của Inner |
| `angleIntegral_`, `previousAngleError_` | State tích phân của Outer |
| `previousAngle_`, `angleDerivative_` | Góc trước và state lọc D của Outer |
| Các flag `initialized_`, `angleInitialized_` | Biết đã có mẫu trước để tính hay đây là lần đầu |
| `previousMixerLimited_`, `previousInnerLimited_` | Nhớ giới hạn của chu kỳ trước để quyết định cập nhật I |
| `reset()` | Xóa state nhớ của controller; không sửa cấu hình `Config` bên ngoài |
| `mixerFeedback()` | Nhận phản hồi mixer bị giới hạn, đặc biệt khi dùng mixer chung hai trục |
| `step()` | Kiểm tra đầu vào → tính Outer nếu ANGLE → tính Inner → giới hạn → trộn motor → lưu state mẫu |
| Lambda `fail` trong `step()` | Xóa state controller và trả `Output` mang mã lỗi |

Trong RATE/DryRun, target rate lấy từ command. Trong ANGLE, target rate là đầu ra Outer. `Controller` vẫn chỉ **tính** duty; việc cho phép xuất thật nằm ở firmware/gate.

### 4.3. DualController — ghép Roll và Pitch

| Thành phần | Nhiệm vụ |
| --- | --- |
| `roll_`, `pitch_` | Hai Controller có state I/D độc lập |
| `reset()` | Reset cả hai |
| `compatible()` | Kiểm tra hai cấu hình hợp lệ và cùng Base |
| `step()` | Gọi PID hai trục, kiểm tra lỗi, trộn chung `B ± Roll ± Pitch`, giới hạn motor và phản hồi bão hòa về hai PID |

Bốn motor trong hai kết quả được cập nhật theo cùng mixer chung. Đây chưa phải mixer ba trục: không có correction Yaw.

### 4.4. Session — ai đang được điều khiển và phiên còn hợp lệ không?

| Thành phần | Nhiệm vụ |
| --- | --- |
| `state`, `owner`, `epoch`, `reason` | State, kết nối sở hữu, thế hệ và lý do chuyển state |
| `armedAt`, `heartbeatAt`, `sampleAt`, `deadline` | Các mốc thời gian dùng kiểm tra phiên |
| `arm()` | Nhận quyền chuẩn bị khi state, owner và epoch phù hợp |
| `start()` | Chuyển phiên Armed hợp lệ sang Running; tạo deadline nếu duration khác 0 |
| `heartbeat()` | Cập nhật tín hiệu còn sống của đúng owner khi Running |
| `check()` | Kiểm tra ARM timeout, deadline, mẫu điều khiển cũ và heartbeat quá hạn |
| `stop()` | Bỏ quyền sở hữu, chuyển Disarmed, tăng epoch và ghi lý do |

ARM không tự chạy motor. Running cũng không tự bảo đảm PWM khác 0, vì còn phụ thuộc mode, đề xuất duty và Gate. `Session` là logic phiên; `Gate` là state xuất motor được chia sẻ với guard. Hai phần liên hệ qua epoch và các mốc thời gian, không phải một object duy nhất.

## 5. Imu.h — cảm biến, ước lượng tư thế và mốc góc

### 5.1. ImuSample

Chứa một mẫu với `ax/ay/az` theo đơn vị g và `gx/gy/gz` theo °/s. Đây là dữ liệu cảm biến sau quy đổi thang đo, chưa mặc định là mọi góc/rate body đã xử lý xong.

### 5.2. AngleZeroCalibration

`AngleZeroCalibration` xác lập mốc góc 0; firmware tạo **hai object** riêng cho Roll và Pitch.

| Function/state | Nhiệm vụ |
| --- | --- |
| `values_`, `sum_`, `count_`, `at_` | Cửa sổ 100 mẫu góc để kiểm tra đứng yên/lấy trung bình |
| `offset_` | Mốc góc đang trừ khỏi góc thô |
| `observe()` | Nhận mẫu phù hợp về góc/rate/gia tốc; xóa cửa sổ khi mẫu không đạt |
| `ready()` | Kiểm tra đủ mẫu, gần ngang và độ biến thiên góc đủ nhỏ |
| `zero()` | Nếu ready, lấy trung bình cửa sổ làm offset |
| `angle(raw)` | Trả về `raw − offset` |
| `offset()` | Cho đọc mốc hiện tại |
| `resetWindow()` | Xóa cửa sổ mẫu, giữ offset |
| `reset()` | Xóa cả cửa sổ và offset |

### 5.3. Imu — giao tiếp phần cứng

| Function/state | Nhiệm vụ |
| --- | --- |
| `identity_` | Giá trị WHO_AM_I đã nhận dạng |
| `read()` | Function nội bộ đọc một dãy byte qua SPI |
| `reg()` | Đọc một thanh ghi |
| `write()` | Ghi thanh ghi, có lựa chọn đọc lại để xác minh |
| `begin()` | Khởi tạo SPI, kiểm tra chip `0x70/0x71`, cấu hình cảm biến và data-ready |
| `identity()` | Trả nhận dạng chip để dùng trong INFO |
| `sample()` | Kiểm tra mẫu mới, đọc dữ liệu, kiểm tra dữ liệu bất thường và quy đổi accel/gyro |
| Lambda `number` | Ghép hai byte thành số nguyên 16-bit có dấu |

Giá trị trả về của `sample()`: **1** = có mẫu mới; **0** = chưa có mẫu mới; **−1** = lỗi thiết bị/dữ liệu. Đừng coi cả 0 và −1 là cùng một lỗi.

Quy trình tìm bias gyro từ 500 mẫu nằm trong `controlTask()` ở `.ino`, không nằm trọn trong class `Imu`.

### 5.4. Attitude — ước lượng tư thế

| Thành phần | Nhiệm vụ |
| --- | --- |
| `w/x/y/z` | State quaternion của bộ ước lượng |
| `initialize()` | Khởi tạo quaternion từ hướng trọng lực đo được |
| `update()` | Trừ bias gyro, cập nhật Madgwick, giảm ảnh hưởng accel khi norm lệch 1g, tích phân và chuẩn hóa quaternion |
| `roll()`, `pitch()`, `yaw()` | Lấy góc Euler theo quy ước body từ quaternion |

Quaternion được cập nhật từ dữ liệu sensor; phép đổi cơ sở body được thực hiện khi lấy góc. Nhánh rate dùng phép đổi trục trực tiếp ở `.ino`. Yaw ước lượng không có reference hướng tuyệt đối từ từ kế trong luồng này.

## 6. NonBlockingTx.h — gửi TCP mà không chặn lâu

File có function template `pumpTcp()`. Template cho phép truyền một function gửi vào; firmware truyền lambda gọi socket `send(..., MSG_DONTWAIT)`.

| Thành phần | Nhiệm vụ |
| --- | --- |
| `data`, `length` | Nội dung và tổng độ dài cần gửi |
| `offset` | Vị trí đã gửi được; cập nhật qua reference để lần sau tiếp tục |
| `Sender send` | Function gửi do bên gọi cung cấp |
| `chunk` | Giới hạn lượng gửi mỗi lần, tối đa 256 byte |
| Kiểm tra `errno` | Các lỗi tạm thời như EAGAIN/EWOULDBLOCK/EINTR cho phép thử lại |

Kết quả: **1** = gửi xong; **0** = còn chờ/thử lại; **−1** = đóng kết nối hoặc lỗi không tiếp tục được. Function không mở Wi-Fi/server và không tự bảo đảm bên gọi gửi non-blocking; cách gọi trong `.ino` thực hiện điều đó.

## 7. Hardware.h — bảng hằng số của hệ

| Hằng số | Nhiệm vụ |
| --- | --- |
| `sck/miso/mosi/cs` | Chân SPI: 18/19/23/5 |
| `motors[4]` | GPIO theo thứ tự LF/RF/LR/RR: 14/15/12/13 |
| `version`, `profile` | Chuỗi nhận dạng phiên bản và cấu hình phần cứng |
| `rollSensorSign` | Dấu đổi gyro sensor Y sang Roll body |
| `beta` | Mức hiệu chỉnh của bộ Madgwick |
| `baud` | Baud Serial: 460800 |

File này không tự bật GPIO hoặc chạy motor. Các function trong `.ino` và `Imu.h` đọc hằng số ở đây để thực hiện công việc.

## 8. BuildInfo.h — thông tin source của bản build

Chứa `rebuildSourceId`, được đưa vào dòng INFO. Script [build.ps1](../../scripts/build.ps1) sinh string này từ hash nội dung các file source theo quy tắc của script.

Nó không phải ID phần cứng, không phải bộ nhớ lưu PID và không tự cập nhật khi bạn sửa/format code. Nếu chưa chạy lại bước sinh thông tin build, string còn lại có thể thuộc snapshot source trước đó.

## 9. WifiSecrets.example.h — mẫu cấu hình router

Hai macro `REBUILD_STA_SSID` và `REBUILD_STA_PASSWORD` chứa tên/mật khẩu router khi dùng file cấu hình thật.

`.ino` chỉ tự include **`WifiSecrets.h`**, không tự include file `.example.h`. Nếu cần kết nối router, copy file mẫu thành `WifiSecrets.h` rồi điền cấu hình. Nếu không có file thật, `.ino` dùng empty string; AP của drone vẫn được khởi tạo trong `setup()`.

File mẫu không chứa logic kết nối. Việc gọi Wi-Fi nằm trong `.ino`; file thật được cấu hình bỏ qua trong Git.

## 10. Nên đọc code theo thứ tự nào?

1. **Hardware.h:** nắm chân, trục và hằng số.
2. **DroneRebuild.ino → setup():** hiểu hệ được khởi tạo thế nào.
3. **loop() và receive():** hiểu lệnh từ app đi vào đâu.
4. **controlTask():** theo luồng lệnh → cảm biến → PID → publish.
5. **Imu.h:** đọc sâu phần tạo số đo/góc.
6. **RollCore.h:** đi theo `Config → Output → Controller::step → DualController → Session`.
7. **guard() và Gate:** hiểu khi nào lệnh được xuất thật hoặc bị dừng.
8. **NonBlockingTx.h:** đọc cách gửi TCP từng phần.
9. **BuildInfo.h và mẫu Wi-Fi:** xem phần nhận dạng/cấu hình phụ trợ.

Nếu chỉ nhớ ba câu: **Imu.h tạo thông tin chuyển động; RollCore.h tính quyết định điều khiển; DroneRebuild.ino tổ chức việc chạy và kiểm soát xuất motor.**

Đọc tiếp [công thức PID và ví dụ](PID_FORMULAS_AND_WORKED_EXAMPLES.md), [sơ đồ kiến trúc](CURRENT_SYSTEM_ARCHITECTURE.md), [giao thức](PROTOCOL.md). Tài liệu chỉ mô tả source, không thay đổi hành vi firmware.
