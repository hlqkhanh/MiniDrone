# Hạ bán tự động có giữ cân bằng

Chức năng này dùng **IMU hiện có**, giảm collective theo duty/s và tiếp tục chạy PID Angle + Rate của Roll/Pitch. Không đo độ cao/tốc độ hạ, không tự phát hiện đã đáp và không tự disarm khi chạm Base sàn. Đây chưa phải auto-land có feedback độ cao trong [kế hoạch dài hạn](CONTROLLED_LANDING_PLAN.md).

## 1. Cách sử dụng trên app

1. Dùng firmware có capability `STABILIZED_DESCENT_V1` và app mới.
2. Apply cấu hình riêng cho cả Roll/Pitch, chọn phạm vi **Roll + Pitch** và mode **ANGLE**. Cần có bộ PID đã được kiểm chứng phù hợp cho hai trục.
3. ARM và bắt đầu điều khiển như trước. Nút hạ chỉ bật khi chính app này đang quản lý phiên chạy hai trục Angle và dữ liệu còn mới.
4. Nhập **Giảm Base — duty/s** và **Base tối thiểu**, rồi bấm **Bắt đầu hạ · vẫn giữ cân bằng**.
5. Quan sát chuyển động. Nút **⏸ Tạm giữ Base** đổi thành **▶ Tiếp tục giảm** khi tạm giữ; đây là cùng một nút. Có thể dùng **↑ Tăng Base +5 và giữ**. Khi đã đáp, dùng **DỪNG MOTOR** hoặc **Esc** để cắt motor.

Ví dụ về cách nhập, không phải thông số khuyến nghị bay: nếu Base đã tune là 110 và mỗi output limit là 20, có thể yêu cầu giảm 1 duty/s tới Base 80. Sau 5 s, collective danh định còn khoảng 105; tại 80 nó giữ nguyên và PID vẫn chạy. Không thể suy ra drone đã hạ bao nhiêu mét từ các con số này.

Giá trị mặc định trong ô nhập chỉ là giá trị ban đầu của UI. Nếu Base đang là 80 thì sàn 80 không hợp lệ vì không có khoảng để giảm; app báo rõ thay vì tự chọn một sàn khác.

Phím tắt: **S** bắt đầu hạ, **Space** tạm giữ/tiếp tục giảm, **W** tăng Base +5 và giữ. Chỉ có tác dụng khi app được focus và nút tương ứng đang bật; giữ phím không tự lặp command. Khi đang nhập TextBox hoặc chọn ComboBox, các phím này không điều khiển hạ. **Esc vẫn gửi STOP khi đang nhập liệu hoặc đang chờ một thao tác hạ khác.**

## 2. Giới hạn và ý nghĩa các thao tác

| Thao tác | Hành vi |
| --- | --- |
| Bắt đầu hạ | Giữ session Running và state PID; giảm collective sau mỗi mẫu mới |
| Tạm giữ Base | Ngừng giảm collective, PID vẫn sửa tư thế; không có nghĩa đã ngừng rơi |
| Tiếp tục giảm | Tiếp tục tốc độ đã chọn; không thực hiện nếu đã ở sàn |
| Tăng Base +5 và giữ | Tăng tối đa 5 duty, không vượt Base lúc bắt đầu hạ; sau đó giữ |
| Chạm Base sàn | Tiếp tục Angle PID; nút gộp báo “⏸ Đang giữ ở sàn” và bị vô hiệu hóa. Có thể tăng Base rồi mới tiếp tục giảm |
| STOP / Esc | Luôn yêu cầu cắt ngay; không chuyển thành một ramp ba giây |

Firmware chấp nhận tốc độ giảm 0.1–10 duty/s. Base sàn phải nhỏ hơn Base lúc bắt đầu và **lớn hơn tổng output limit của Roll và Pitch**. Điều kiện này giữ khoảng lệnh cho hai correction ở đầu thấp của mixer, không phải phép đo lực nâng tối thiểu hay bảo đảm motor thực còn đủ lực giữ cân bằng.

Boost giới hạn mỗi command ở 10 duty; app dùng bước 5. Không thể tăng vượt Base ban đầu bằng chức năng này. Nếu mức đó không đủ hãm thì chế độ không có thêm khả năng tự xử lý. Duty không tuyến tính với lực; thời lượng hạ và tốc độ chạm đất phụ thuộc pin, motor, khối lượng và môi trường.

## 3. Công thức và đường điều khiển

```text
collective_mới = max(Base_sàn, collective_cũ − tốc_độ_giảm × dt)

motor_i = collective_mới + correction_Roll_i + correction_Pitch_i
```

Hai correction được tính lại liên tục từ IMU. Không lưu bốn duty rồi giảm theo cùng một hệ số. Target góc trước đó được đưa dần về 0 với giới hạn 5°/s; pause chỉ giữ collective, không ngừng đưa target về ngang. State I/D không reset khi bắt đầu hạ vì firmware yêu cầu đang chạy đúng hai trục Angle từ trước.

Config Base đã Apply vẫn giữ nguyên để đọc lại/tune. `.ino` tạo hai bản config dùng cho lượt tính, gán cùng collective động rồi gọi `DualController`. Vì vậy telemetry T56 vẫn báo Base cấu hình; collective thực trong quá trình hạ được báo riêng bằng dòng DESCENT.

`StabilizedDescent.h` chỉ quản lý profile và kiểm tra quyền. Nó không ghi PWM. `controlTask()` dùng profile để gọi PID/mixer; `publish()` đưa kết quả qua Gate; `guard()` vẫn là nơi ghi PWM.

## 4. Protocol

Các command dưới đây vẫn có request ID ở đầu dòng, giống protocol hiện tại. Góc/rate vẫn theo độ và °/s; thay đổi này không chuyển firmware sang radian.

| Command | Ý nghĩa |
| --- | --- |
| `DESCEND epoch reductionPerSecond minimumBase` | Bắt đầu hạ bán tự động trong phiên Running dual Angle |
| `DESCEND_PAUSE epoch` | Giữ collective hiện tại |
| `DESCEND_RESUME epoch` | Tiếp tục giảm collective |
| `DESCEND_BOOST epoch delta` | Tăng collective có giới hạn và chuyển sang giữ |

Tất cả command điều chỉnh hạ yêu cầu đúng owner và epoch của phiên. Chúng không ARM hoặc START một phiên mới. BENCH, DryRun, Rate mode, một trục riêng, config không phù hợp hoặc yêu cầu lặp khi profile đã active đều bị từ chối.

INFO có `STABILIZED_DESCENT_V1`, không còn quảng bá `SOFT_STOP_V1`. Firmware không thực thi ramp `SOFT_STOP` cũ. App cũ không có giao diện hạ mới; phải dùng app mới để điều khiển tính năng này.

T56 giữ nguyên layout. Reason có thể là `DESCENDING`, `DESCENT_PAUSED` hoặc `DESCENT_FLOOR`; state vẫn là RUNNING. Mỗi khoảng 500 ms, board gửi về owner dòng trạng thái:

```text
DESCENT,epoch,reason,collective,minimumBase,reductionPerSecond
```

Dòng này được ghi trong events log. App không thay Base đã Apply bằng collective đang giảm. ACK lỗi của thao tác hạ không tự làm app gửi STOP; app báo lệnh chưa được xác nhận, trong khi heartbeat cho phiên hiện tại vẫn hoạt động. STOP luôn sẵn để người vận hành chủ động cắt.

## 5. Watchdog và giới hạn vận hành

- Heartbeat của owner vẫn được gửi mỗi 500 ms trong khi hạ/giữ/sàn.
- Stale control 100 ms, heartbeat timeout 2 s, lỗi IMU/PWM và TCP disconnect vẫn theo chính sách dừng hiện tại.
- STOP tăng epoch; profile được reset ở bước đồng bộ session hoặc fault. Không thể dùng command epoch cũ để tiếp tục/tăng Base.
- Không tự tiếp tục hạ khi app mất kết nối; đây là lựa chọn giữ nguyên failsafe hiện tại, chưa phải autonomous LAND.
- Không có Yaw controller mới, position hold XY hoặc giải pháp cho MCU reset/sụt nguồn trong thay đổi này.

Không gọi trạng thái Base sàn là LANDED. Trong thực tế phải quan sát và quyết định STOP. Giữ ngang không ngăn được mọi chuyển động ngang và không bảo đảm tốc độ hạ nhỏ.

## 6. Kiểm tra

`tests/descent_tests.cpp` kiểm tra điều kiện mode/owner/epoch/config, giới hạn input, profile liên tục, giữ PID I/D, correction cập nhật khi đã ở sàn, pause/resume/boost, config gốc không bị thay và watchdog vẫn hoạt động.

Diagnostic app sử dụng board giả lập để kiểm tra framing, trạng thái hạ, heartbeat, các thao tác, lỗi điều chỉnh không cắt motor và STOP không biến thành SOFT_STOP. Build ESP32 kiểm tra tích hợp Arduino thực tế; các kiểm tra software này không thay thế thử nghiệm có kiểm soát trên phần cứng thật.
