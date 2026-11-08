# DroneRebuild.ino — chương trình chính hoạt động như một hệ thống thế nào?

Source: [DroneRebuild.ino](../../../firmware/DroneRebuild/DroneRebuild.ino). Nên đọc [mục lục và thuật ngữ nền](README.md) trước.

## 1. Vai trò theo góc nhìn SE

File này giống nơi ghép các service trong ứng dụng: tạo phụ thuộc, nhận request, chuyển message, gọi thuật toán và quản lý đầu ra.

Nhưng “đầu ra” ở đây là tín hiệu điện tới motor, nên còn phải kiểm tra phiên điều khiển, thời gian và lỗi phần cứng. Không chỉ xử lý đúng dữ liệu mà còn phải từ chối dữ liệu quá cũ.

Ba phần cần tách trong đầu:

```text
Giao tiếp: loop()                  → nhận/gửi byte
Điều khiển: controlTask()          → lệnh, cảm biến, PID, telemetry
Giám sát và xuất PWM: guard()      → cho phép xuất bao nhiêu duty
```

Vị trí một function ở đầu/cuối file không quyết định function đó chạy trước. Arduino gọi `setup()` rồi lặp `loop()`; các task/timer được tạo trong setup chạy theo cơ chế lập lịch riêng.

## 2. Các include là bản đồ phụ thuộc

| Nhóm | Mục đích |
| --- | --- |
| `Arduino.h`, `WiFi.h`, `esp_timer.h` | GPIO/PWM/Serial, mạng và timer |
| `sys/socket.h`, `atomic` | Gửi socket không chặn và variable atomic |
| `RollCore.h` | Config, PID, mixer, Session |
| `Hardware.h`, `Imu.h` | Hằng phần cứng và cảm biến/ước lượng |
| `BuildInfo.h`, `NonBlockingTx.h` | Thông tin build và helper gửi TCP |
| `WifiSecrets.h` nếu có | Cấu hình router ở thời điểm compile |

`using namespace rebuild` cho phép viết `Config` thay vì `rebuild::Config`. Nó không khởi chạy controller.

## 3. Các DTO và state chung

### Gate: state mà bộ ghi motor nhìn vào

| Trường | Ý nghĩa |
| --- | --- |
| `stopped`, `enabled` | Đã dừng/được phép xuất output hay chưa |
| `ramping` | Đang giảm duty theo dừng mềm |
| `epoch` | Thế hệ điều khiển, thay đổi khi dừng |
| `deadline` | Hạn kết thúc hữu hạn; 0 biểu diễn không đặt hạn trong đường tương ứng |
| `hb`, `sample` | Mốc heartbeat và mẫu điều khiển mới nhất |
| `duty[4]` | Đề xuất duty hiện tại |
| `rampDuty[4]`, `rampStart`, `rampEnd` | Duty ban đầu và thời gian của dừng mềm |
| `reason` | Chuỗi lý do, ví dụ STOP hoặc HEARTBEAT |

#### Snapshot là gì?

`Gate` được nhiều task sử dụng: một task có thể cập nhật dữ liệu trong lúc task khác đang đọc. Khi cần đọc, code lấy một **snapshot**: bản copy của toàn bộ `Gate` để dùng cho bước xử lý tiếp theo. Snapshot không tự thay đổi theo `gate` gốc sau khi đã copy.

Ví dụ, `Gate g = snapshot();` lấy bản copy vào variable local `g`. Các bước sau đọc `g.stopped` hoặc `g.duty[]` mà không cần giữ lock. Muốn biết state mới hơn của `gate`, cần lấy snapshot khác.

#### Atomic là gì, tại sao vẫn cần lock?

**Atomic** nghĩa là một thao tác diễn ra trọn vẹn đối với các task khác. Với một variable `std::atomic`, task khác không đọc được giá trị đang bị ghi dở; kiểu này cũng hỗ trợ trao đổi dữ liệu giữa các task theo quy tắc đồng bộ của C++.

Ví dụ minh họa với một flag độc lập:

```cpp
std::atomic<bool> ready{false};

// Task A ghi:
ready.store(true);

// Task B đọc:
bool value = ready.load();
```

Điều đó bảo vệ từng thao tác trên `ready`. Nhưng **nhiều thao tác atomic liên tiếp không tự trở thành một thao tác chung**. Ví dụ, đọc `stopped` rồi đọc `enabled` vẫn là hai lần đọc riêng. Task khác có thể cập nhật dữ liệu ở giữa hai lần đọc.

Giả sử trước STOP, `stopped = false` và `enabled = true`; sau STOP, `stopped = true` và `enabled = false`. Nếu đọc mà không bảo vệ cả nhóm, một task có thể lấy `stopped = false` trước STOP, rồi lấy `enabled = false` sau STOP. Hai giá trị thuộc hai thời điểm khác nhau.

Ví dụ này giải thích giới hạn của atomic; **các field của `gate` hiện được bảo vệ bằng `gateMux`, không phải mỗi field đều là `std::atomic`**. Ngoài ra, với một atomic counter, chuỗi `load()` → cộng 1 → `store()` vẫn có thể bị xen ngang; `fetch_add(1)` thực hiện phép tăng bằng một thao tác atomic.

#### GateMux và critical section hoạt động thế nào?

`gateMux` đóng vai trò như một lock. Đoạn giữa `portENTER_CRITICAL(&gateMux)` và `portEXIT_CRITICAL(&gateMux)` gọi là **critical section**. Với ESP32, cơ chế này có bảo vệ giữa các core; task đang chờ lock có thể phải chờ bằng spin, không nên hiểu là một lệnh `delay()` hay một mutex luôn đưa task vào trạng thái ngủ.

Ví dụ minh họa cập nhật nhiều field trong cùng một critical section:

```cpp
portENTER_CRITICAL(&gateMux);

gate.stopped = true;
gate.enabled = false;

portEXIT_CRITICAL(&gateMux);
```

Các field thuộc cùng một lần cập nhật được đặt trong cùng critical section. Nếu thả lock giữa hai lần sửa, task đọc có thể lấy snapshot khi mới sửa xong một field.

**Task đọc cũng phải dùng cùng `gateMux`.** Trong firmware, `snapshot()` thực hiện việc lấy lock, copy `gate`, rồi thả lock. Gọi `snapshot()` xong thì bản copy local có thể được sử dụng bên ngoài critical section.

Thứ tự lấy lock quyết định snapshot nhận được:

- **Task đọc lấy lock trước:** task đọc copy state cũ; task cập nhật phải chờ đến khi task đọc thả lock.
- **Task cập nhật đang giữ lock:** task đọc phải chờ đến khi cập nhật xong và thả lock, rồi mới copy state mới. Nó không tự nhận state cũ trong lúc chờ.

```text
Task cập nhật                    Task đọc
ENTER → giữ gateMux
Sửa gate.stopped                 ENTER → chờ gateMux
Sửa gate.enabled                 vẫn chờ
EXIT → thả gateMux
                                 lấy được gateMux
                                 copy Gate đã cập nhật xong
                                 EXIT → thả gateMux
```

Nhờ đó, snapshot không trộn các field trước và sau một lần cập nhật được bảo vệ bởi lock này. **Đọc trực tiếp `gate` mà không lấy cùng lock thì không tự chờ**, và không được đảm bảo tính nhất quán như trên. Quy tắc này áp dụng khi các task đã chạy đồng thời; bản copy local không cần lock chỉ để đọc chính nó.

Giữ critical section thật ngắn: chỉ copy hoặc cập nhật dữ liệu cần bảo vệ; tránh gửi Serial, Wi-Fi, gọi `delay()` hoặc làm việc tốn thời gian bên trong. Lock bảo vệ dữ liệu, không tự bảo đảm dữ liệu đó luôn là state mới nhất khi được dùng về sau; firmware còn dùng `epoch` và các kiểm tra khác để xử lý việc state đã thay đổi.

### Command, Input, Line

Đây là **ba struct chứa dữ liệu ở các giai đoạn khác nhau: nhận → xử lý → gửi**. Chúng không tự chạy công việc; các function trong firmware đọc/ghi dữ liệu bên trong chúng.

#### Input: dữ liệu đang nhận dở

USB/TCP không bảo đảm mỗi lần đọc sẽ nhận trọn một command. Ví dụ bạn gửi `1 INFO` kèm ký tự xuống dòng, firmware có thể nhận `1 IN` trước, rồi mới nhận `FO` và ký tự xuống dòng.

`Input` giữ các ký tự đang nhận trong buffer để ghép thành một dòng hoàn chỉnh. Nó theo dõi số ký tự đã nhận và flag tràn nếu dòng quá dài. USB và TCP có `Input` riêng để dữ liệu của hai nguồn không bị ghép lẫn vào nhau.

#### Command: lệnh đã nhận đủ, chờ xử lý

Khi nhận đủ dòng `1 INFO`, firmware tách dữ liệu thành một `Command` gồm:

- Request ID `1`: dùng để ghép phản hồi với request này.
- Nội dung command `INFO`: cho biết cần thực hiện việc gì.
- Owner: xác định nguồn gửi để định tuyến phản hồi và kiểm tra quyền khi command yêu cầu.

`Command` được đưa vào queue `commands`, chờ task điều khiển lấy ra xử lý. Như vậy, phần nhận USB/TCP có thể tiếp tục công việc mà không phải trực tiếp xử lý toàn bộ command ngay lúc nhận.

#### Line: một dòng dữ liệu chờ gửi ra

Sau khi xử lý INFO, firmware tạo các dòng phản hồi, chẳng hạn dòng INFO, LEVEL và ACK. Mỗi `Line` chứa nội dung một dòng và owner đích; phần gửi dữ liệu dùng thông tin đó để gửi tới USB/TCP phù hợp. Một command có thể tạo nhiều `Line`.

Ngoài phản hồi command, `Line` còn được dùng cho telemetry và HEALTH. Owner đích cũng có thể biểu thị gửi tới các kênh đang hoạt động, thay vì chỉ trả lời một nguồn cụ thể.

```text
USB/TCP nhận từng phần của dòng "1 INFO"
                    ↓
Input: giữ ký tự, ghép đủ dòng khi nhận ký tự xuống dòng
                    ↓
Command: giữ ID, nội dung INFO và owner → queue commands
                    ↓
Task điều khiển xử lý INFO
                    ↓
Line: giữ từng dòng kết quả và owner đích → queue replies
                    ↓
Phần gửi dữ liệu đưa các dòng ra USB/TCP phù hợp
```

Các struct này dùng array `char` có kích thước cố định để chứa text: sức chứa đã được định trước, không tự mở rộng theo độ dài dữ liệu. Vì vậy, firmware cần kiểm tra dữ liệu quá dài; riêng `Input` có flag để ghi nhận dòng nhận bị tràn.

### Owner, request ID và epoch khác nhau

| Tên | Dùng để làm gì? |
| --- | --- |
| `owner` | Nhận biết kết nối nào đang sở hữu phiên/nhận phản hồi |
| `id` | Ghép ACK với một request cụ thể, như số 7 trong `7 INFO` |
| `epoch` | Làm mất hiệu lực quyền/lệnh điều khiển thuộc thế hệ cũ |

Đừng lấy request ID nhập trong Serial làm epoch của ARM. Đây là hai field khác nhau.

## 4. setup(): chương trình lắp ráp hệ thống

`setup()` là phần **chuẩn bị hệ thống sau mỗi lần board khởi động hoặc reset**. Bạn có thể hình dung nó giống bước khởi tạo các service khi một ứng dụng backend bắt đầu chạy: chuẩn bị phần cứng, các kênh giao tiếp, queue và các công việc chạy nền.

Arduino gọi `setup()` một lần trong mỗi lần khởi động, sau đó chạy `loop()` lặp lại. Tuy nhiên, timer và task được tạo trong `setup()` có thể bắt đầu chạy ngay khi scheduler cho phép; chúng không cần chờ `loop()` gọi tới.

### 4.1. Đưa các chân motor về state ban đầu

Firmware đưa bốn chân điều khiển motor về mức LOW và cấu hình chúng làm output. Đây là bước chuẩn bị tín hiệu đầu ra trước khi gắn PWM, với mục đích không phát lệnh chạy motor trong quá trình khởi tạo.

GPIO là chân phát tín hiệu điều khiển tới mạch công suất motor; không phải chân trực tiếp cấp đủ dòng điện để chạy motor.

### 4.2. Chuẩn bị Serial để giao tiếp qua USB

`Serial.setTxBufferSize(2048)` cấu hình buffer gửi Serial với dung lượng yêu cầu 2048 byte. Buffer giữ tạm dữ liệu chờ truyền, giúp phần tạo dữ liệu và phần truyền có thể làm việc theo nhịp khác nhau; dung lượng này không làm đường truyền nhanh vô hạn.

`Serial.begin(hw::baud)` mở Serial ở baud được khai báo trong `Hardware.h`, hiện là **460800**. Đây là kênh để nhận command và gửi INFO, ACK, telemetry tới máy tính. Serial Monitor cần chọn cùng baud để đọc được dữ liệu firmware.

### 4.3. Chuẩn bị PWM cho bốn motor

**LEDC** là peripheral của ESP32 dùng để tạo tín hiệu PWM bằng phần cứng. PWM liên tục bật/tắt tín hiệu; **duty** biểu thị phần thời gian tín hiệu ở mức bật trong mỗi chu kỳ.

Firmware cấu hình PWM với:

| Thiết lập | Ý nghĩa |
| --- | --- |
| 20 kHz | Tín hiệu PWM lặp 20.000 chu kỳ mỗi giây; đây không phải tốc độ tính PID |
| 8-bit | Duty có 256 mức, từ 0 đến 255 |
| Duty ban đầu 0 | Chưa phát lệnh chạy motor |

Ví dụ duty 128 tương ứng khoảng 50% của thang 0–255. Điều này không có nghĩa motor tạo đúng 50% lực nâng: lực còn phụ thuộc pin, motor, cánh và tải.

`pwmReady` ghi nhận kết quả gắn PWM: nếu một chân không gắn được, kết quả chung là false. Việc gán true trước vòng lặp là giá trị bắt đầu để tổng hợp kết quả, chưa chứng minh cả bốn chân đã sẵn sàng. Sau khi gắn từng chân, code ghi duty 0.

### 4.4. Tạo ba queue để trao đổi dữ liệu

Firmware tạo các queue trước khi tạo task điều khiển để task có nơi nhận command và đưa dữ liệu đầu ra:

| Queue | Dung lượng | Vai trò |
| --- | --- | --- |
| `commands` | 16 item `Command` | Command nhận từ USB/TCP chờ xử lý |
| `replies` | 24 item `Line` | Các dòng phản hồi chờ gửi |
| `samples` | 4 item `Line` | Telemetry chờ gửi |

Đây là số item tối đa có thể chờ trong queue, không phải tần số xử lý hay số item có sẵn lúc khởi động. Nếu không tạo được một queue, code đặt `pwmReady = false` và kết thúc `setup()` tại đây, không tạo các thành phần ở các bước sau.

### 4.5. Chuẩn bị Wi-Fi theo hai vai trò AP và STA

**AP (Access Point)** nghĩa là ESP32 tự tạo mạng Wi-Fi để máy tính/điện thoại kết nối trực tiếp. Firmware tạo AP tên `DroneRebuild`.

**STA (Station)** nghĩa là ESP32 tham gia mạng của một router, giống laptop kết nối Wi-Fi. Firmware chỉ bắt đầu kết nối router khi có SSID được cấu hình trong `WifiSecrets.h`.

`WIFI_AP_STA` cho phép hai vai trò cùng hoạt động. Không cấu hình router thì vẫn có thể dùng AP của drone. Gọi bắt đầu kết nối STA cũng không có nghĩa đã kết nối thành công ngay lúc đó; `setup()` không chờ router kết nối xong ở đoạn này.

### 4.6. Mở TCP server để app kết nối

Wi-Fi tạo đường kết nối mạng; **TCP server** là nơi app kết nối để gửi command và nhận dữ liệu. Server của firmware được cấu hình ở **cổng 3333**.

`server.begin()` bắt đầu lắng nghe kết nối. `server.setNoDelay(true)` yêu cầu tắt cơ chế gom các gói nhỏ theo Nagle để giảm độ trễ có thể phát sinh khi gửi những dòng ngắn; nó không bảo đảm mọi dữ liệu được truyền tức thì. Việc nhận client và trao đổi dữ liệu tiếp tục được xử lý trong `loop()`.

### 4.7. Tạo timer gọi guard() định kỳ

**Timer** giúp yêu cầu một công việc được gọi theo chu kỳ. **Callback** là function được đăng ký để hệ thống gọi khi tới lượt; ở đây callback là `guard()`.

Firmware yêu cầu gọi `guard()` mỗi **5000 microsecond = 5 ms**, tương ứng nhịp danh định **200 Hz**. Callback được thực thi trong `ESP_TIMER_TASK`, không phải một ISR do firmware tự viết.

`guard()` kiểm tra điều kiện cho phép xuất motor, các mốc thời gian và dừng mềm, rồi cập nhật PWM. Task điều khiển tính duty đề xuất; `guard()` là nơi đưa duty được phép ra phần cứng. Vì vậy, tạo timer này là chuẩn bị đường xuất motor và cơ chế kiểm tra đi kèm, không phải bắt đầu bay.

Chu kỳ 5 ms là yêu cầu lập lịch, không có nghĩa callback luôn chạy chính xác tuyệt đối từng microsecond. Nếu không tạo hoặc không khởi động được timer, code đặt `pwmReady = false` và kết thúc `setup()` trước bước tạo `controlTask`.

### 4.8. Tạo controlTask chạy nền

`controlTask` phụ trách xử lý command, đọc IMU, hiệu chuẩn, ước lượng góc/rate, tính PID và tạo telemetry. Khi task được tạo thành công, FreeRTOS sẽ lập lịch cho nó chạy độc lập với `loop()`.

Các thiết lập trong lời gọi tạo task có ý nghĩa:

| Thiết lập | Ý nghĩa |
| --- | --- |
| Core 1 | Gắn task vào core 1 của ESP32 |
| Priority 4 | Mức ưu tiên lập lịch, không phải tần số 4 Hz |
| Stack 10000 byte | Vùng nhớ stack dành cho các lời gọi function và dữ liệu local của task theo API ESP-IDF này |
| Tên `roll200Hz` | Tên để nhận diện task; bản thân tên không quyết định tốc độ chạy |

Nhịp danh định 200 Hz của task được thiết lập bằng cơ chế chờ theo chu kỳ bên trong `controlTask`, không phải chỉ bởi thao tác tạo task. Nếu tạo task thất bại, code đặt `pwmReady = false` và gọi `emergency("TASK_CREATE")`.

### 4.9. setup() xong thì drone đã chạy motor chưa?

**Chưa.** Khởi tạo thành công chỉ có nghĩa các thành phần đã được chuẩn bị. PWM ban đầu là duty 0; để xuất duty chạy motor còn cần command và các điều kiện tương ứng về cấu hình, hiệu chuẩn, phiên điều khiển và `Gate`.

Một số nhánh lỗi ở trên dùng `return` để kết thúc sớm `setup()`. Điều đó không có nghĩa ESP32 tắt hay hệ điều hành dừng chạy: Arduino vẫn có thể tiếp tục vào `loop()`, và thành phần nào đã khởi tạo trước đó có thể vẫn tồn tại. Firmware dùng các kiểm tra queue, `pwmReady` và state dừng để xử lý việc hệ thống chưa sẵn sàng.

## 5. Queue: không gọi controller ngay trong function đọc socket

`QueueHandle_t commands, replies, samples;` khai báo ba variable giữ **handle của ba queue**. Handle là giá trị dùng để tham chiếu tới queue khi gửi hoặc nhận dữ liệu.

**Queue** là nơi lưu tạm dữ liệu để các task trao đổi với nhau. Các queue này do **FreeRTOS** cung cấp. FreeRTOS là hệ điều hành nhỏ đã được tích hợp trong Arduino framework trên ESP32, giúp quản lý và lập lịch chạy các task.

| Queue | Vai trò trong firmware |
| --- | --- |
| `commands` | Lưu command đã nhận qua USB/TCP, chờ task điều khiển xử lý |
| `replies` | Lưu ACK, INFO, HEALTH và các dòng phản hồi, chờ gửi ra USB/TCP |
| `samples` | Lưu telemetry, chờ gửi ra USB/TCP |

Dòng khai báo này chưa tạo queue. Các queue được tạo trong `setup()`, với dung lượng lần lượt là 16, 24 và 4 item. Nhờ queue, bên nhận command, bên điều khiển và bên gửi dữ liệu có thể làm việc theo nhịp riêng.

`loop()` và `controlTask()` không trao đổi lệnh bằng cách gọi xuyên trực tiếp vào nhau. `receive()` đẩy một `Command` vào queue; task điều khiển lấy ra theo lượt.

`xQueueSend(..., 0)` có thời gian chờ 0: nếu queue đầy, không đứng đợi mãi. Queue copy dữ liệu element theo kích thước đã khai báo. Với các struct chứa array char này, nội dung dòng được mang theo copy struct.

Xử lý đầy queue tùy đường code: lỗi queue lệnh có thể yêu cầu dừng; telemetry có thể bị bỏ và tăng counter. Không phải mọi dòng bị bỏ đều có cùng tác động.

## 6. receive(): từ byte stream thành một command

Function chỉ đọc tối đa 256 byte mỗi lượt gọi. Nó tích vào Input đến khi có `\n` và bỏ qua `\r`.

Sau đó parse dạng:

```text
<request ID><dấu cách><command>
Ví dụ không chạy motor: 7 INFO
```

`sscanf()` trả số field đọc được. Code kiểm tra số field để biết định dạng phù hợp; dòng không có ID sẽ bị báo lỗi format. Nếu buffer tràn, nó không tiếp tục coi dữ liệu bị cắt là một lệnh hợp lệ.

Điểm đặc biệt: nhận đủ dòng STOP thì gọi `emergency()` trước khi chờ task lấy command từ queue. Đó là ưu tiên cập nhật state dừng, không phải bypass guard để ghi PWM ngay trong function nhận.

## 7. controlTask(): vòng làm việc chính

### 7.1. Vì sao variable PID không mất sau mỗi vòng lặp?

`Controller`, `DualController`, `Session`, `Attitude` và các cấu hình được tạo trước vòng `for(;;)`. Task tồn tại liên tục, nên các object này giữ state qua các lượt. Chúng chỉ bị reset khi code gọi reset hoặc khi chương trình khởi động lại.

Nếu tạo Controller mới bên trong mỗi lần tính, I/D sẽ mất state; source hiện tại không làm vậy.

### 7.2. Những bước mỗi lượt

1. Đọc thời gian và lấy snapshot của `Gate`.
2. Đồng bộ Session nếu epoch đã thay đổi; kiểm tra phiên còn hợp lệ.
3. Xử lý tối đa bốn command đang chờ.
4. Gọi `imu.sample()`; chỉ cập nhật điều khiển theo mẫu mới phù hợp.
5. Nếu đang hiệu chuẩn, thu mẫu đứng yên để tính bias.
6. Cập nhật attitude, rate body và các cửa sổ mốc góc.
7. Nếu Running: xuất đề xuất BENCH hoặc tính PID một/hai trục.
8. Tạo telemetry khi đến nhịp; tạo HEALTH định kỳ.
9. Cập nhật mốc heartbeat trong Gate và nhường tới nhịp task tiếp theo.

Đây là lịch danh định 5 ms, không phải cam kết mọi đường chạy luôn tốn đúng 5 ms. `dt` được tính từ mốc mẫu thực.

### 7.3. Hiệu chuẩn gyro ở đâu?

Ngay trong task: code kiểm tra norm accel, độ lớn gyro và variable thiên mẫu; thu đủ 500 mẫu phù hợp rồi lấy trung bình ba gyro làm bias. Nếu mẫu không ổn định, chuỗi tích lũy được bắt đầu lại.

`Imu` chỉ đọc/quy đổi mẫu. `Attitude` nhận bias để cập nhật tư thế. Đây là ba trách nhiệm khác nhau.

### 7.4. Từ sample tới góc/rate

```text
ImuSample a
 ├─ attitude.update(a, bias..., dt) → Roll/Pitch/Yaw ước lượng
 ├─ −(a.gy−biasY)                 → Roll rate
 └─ +(a.gx−biasX)                 → Pitch rate

Góc ước lượng → levels[k].angle() → góc dùng cho PID
```

`levels` có hai element, giữ offset riêng Roll/Pitch. ZERO trừ mốc góc; nó không thay thế bias gyro.

### 7.5. Từ góc/rate tới output

Chạy một trục thì gọi `controller.step(...)`. Chạy cả hai thì gọi `dualController.step(...)` với góc/rate/config hai trục.

Nếu controller trả fault, task yêu cầu dừng. Nếu hợp lệ, nó cập nhật mốc mẫu và gọi `publish(mode != DryRun)`.

Trong mode DryRun, toán vẫn được tính nhưng Gate không được bật xuất motor theo kết quả PID đó. BENCH là nhánh riêng, dùng `benchDuty` thay vì PID.

## 8. publish(), Session và Gate không phải cùng một việc

`Session` quản lý phiên tại task điều khiển. `publish()` chép đề xuất sang Gate chỉ khi epoch còn khớp và chưa bị dừng.

Có thể hình dung một request cũ đang xử lý thì STOP tới và đổi epoch. Khi request cũ định publish, điều kiện epoch giúp ngăn nó tiếp tục xuất kết quả thuộc thế hệ trước.

`publish()` còn chép deadline, heartbeat và tuổi mẫu. Nó không gọi `ledcWrite()`.

## 9. guard(): nơi biến đề xuất thành PWM

`guard()` chạy trong ngữ cảnh `ESP_TIMER_TASK`, không phải ISR tự viết và không chạy trong UI máy tính.

Trong nhánh chạy thông thường, nó kiểm tra deadline, mẫu điều khiển cũ từ 100 ms và heartbeat cũ từ 2 s. Khi vi phạm, nó yêu cầu dừng. Sau đó nó quyết định duty cho từng chân và gọi LEDC.

### STOP và SOFT_STOP

`emergency()` tắt quyền xuất/ramp, tăng epoch, ghi reason. Duty thực được guard ghi theo state đó; không hiểu lời gọi emergency là một phép đo xác nhận motor cơ học đã đứng hẳn.

`beginSoftStop()` giữ duty tại thời điểm bắt đầu, rồi guard giảm duty gần tuyến tính theo thời gian còn lại. Ví dụ giả định duty ban đầu 120 và ramp 2 s, ở giữa thời gian lệnh còn khoảng 60.

Trong nhánh ramp, kiểm tra của nhánh Running thông thường không được áp dụng y hệt. Session đã được chuyển khỏi Running và PID đã reset theo đường SOFT_STOP. Đây không phải PID điều khiển độ cao để hạ cánh.

## 10. Gửi phản hồi và telemetry

| Dòng | Ý nghĩa |
| --- | --- |
| ACK | Kết quả xử lý request, kèm request ID và epoch |
| INFO | Phiên bản, source ID, chip, mạng, bias và capability |
| LEVEL | Góc thô, offset, góc sau bù và state mốc |
| T | Mẫu cảm biến, PID, motor, cấu hình và thống kê |
| HEALTH | State cảm biến/hiệu chuẩn/PWM và lý do, phát định kỳ |

`reply()` tạo ACK. Các dòng khác được task tạo trực tiếp rồi đưa vào queue. `enqueueOutput()` chuẩn bị buffer USB/TCP và send cursor riêng.

`loop()` ưu tiên lấy từ replies trước samples khi có thể lấy dòng mới. Nó gửi USB theo chỗ trống và TCP qua `pumpTcp()`. Mỗi đường có tiến độ riêng, không giả định đã gửi hết một dòng trong một lượt.

Nếu nghẽn quá ngưỡng trong code, TCP có thể bị đóng với `TCP_BACKPRESSURE`; phần USB chờ có thể bị bỏ. Counter giúp quan sát việc này, không thay thế xác nhận delivery ở app.

## 11. Trace một lệnh INFO, không liên quan motor

```text
Laptop gửi "7 INFO\n"
  → receive() đọc đủ dòng, owner=đường gửi
  → commands queue chứa {id=7, text="INFO", owner=...}
  → controlTask() xử lý INFO
  → đưa INFO, LEVEL và ACK vào replies
  → loop()/enqueueOutput() chọn đúng đường gửi
  → laptop nhận phản hồi có ACK request ID 7
```

Không có bước “tính PID để trả INFO”. Các trách nhiệm được ghép trong cùng firmware nhưng vẫn có luồng riêng.

## 12. Những chỗ dễ đọc nhầm

- `verified` là xác nhận người dùng, không phải kết quả board tự đo chiều lực.
- `props` là xác nhận tháo cánh cho BENCH, không phải cảm biến phát hiện cánh.
- `pwmReady` không đo lực/RPM motor; nó phản ánh việc làm việc với PWM theo code.
- `missed` đếm poll chưa có mẫu mới, không tự bằng số mẫu vật lý bị mất.
- PID chính của telemetry hai trục lấy từ Roll; không đọc nó như PID chi tiết cả Pitch/Yaw.
- `delay(1)` trong loop nhường thời gian; không bảo đảm loop chạy đúng 1 kHz.
- `.ino` hiện chưa có vòng PID Yaw, vòng độ cao hoặc vị trí.

Đọc tiếp [Imu.md](Imu.md), [RollCore.md](RollCore.md), [NonBlockingTx.md](NonBlockingTx.md). Cú pháp lệnh đầy đủ: [PROTOCOL.md](../PROTOCOL.md).
