# Đọc đồ thị PID: biết drone đang làm gì và vì sao

Đồ thị giúp bạn nhìn **cả quá trình** thay vì một con số chụp tại một thời điểm. Một góc đo gần 0° chưa đủ để nói drone đã cân bằng: nó có thể đang lao qua 0° rất nhanh và sắp nghiêng sang phía còn lại.

Nếu chưa phân biệt góc, tốc độ và gain, đọc [hướng dẫn các field](huong-dan-field-pid-wpf.md) trước. Tài liệu này dùng đúng luồng:

```text
ANGLE: đang lệch bao nhiêu?
    ↓
BODY RATE: đang quay đúng tốc độ yêu cầu không?
    ↓
RATE PID: thuật toán đang sửa mạnh bao nhiêu?
    ↓
MOTOR OUTPUT: cuối cùng motor nào nhận lệnh gì?
```

## 1. Mở tab và hiểu các nút

Mở **Live PID plots**, cạnh tab **3D orientation**.

| Field/nút | Dùng để làm gì? | Vì sao cần? |
|---|---|---|
| Plot axis: ROLL/PITCH | Chọn trục dữ liệu muốn xem | Tránh trộn hai trục vào một bộ đường khó đọc |
| Pause plots | Dừng thêm mẫu vào đồ thị | Giữ hình đang xem để đọc kỹ |
| Clear plots | Xóa lịch sử đang hiển thị | Bắt đầu quan sát một lượt mới |
| LIVE | Có dữ liệu gần đây và không Pause | Cho biết đồ thị đang có mẫu mới |
| PAUSED | Đang bật Pause | Giải thích vì sao hình đứng yên |
| NO NEW DATA | Hơn 2 giây chưa nhận mẫu mới | Báo không nên xem hình cũ là trạng thái hiện tại |

**Plot axis chỉ đổi cách nhìn, không đổi trục motor đang điều khiển.** Ví dụ bộ điều khiển đang chọn ROLL nhưng Plot axis=PITCH thì bạn đang quan sát Pitch trong lúc chỉ Roll được điều khiển.

**Pause và Clear không dừng motor, không đổi gain và không dừng ghi CSV.** Muốn dừng motor, dùng STOP / DISARM.

## 2. Các trục của đồ thị nghĩa là gì?

- Trục ngang là thời gian theo dữ liệu ESP32, đơn vị giây. Đi từ trái sang phải là từ cũ đến mới.
- Trục dọc là giá trị của đại lượng ghi ở tên biểu đồ; từng biểu đồ có đơn vị riêng.
- Cửa sổ hiển thị 30 giây, giữ tối đa 600 mẫu. Khi mới bắt đầu, phần có dữ liệu sẽ còn ngắn.
- Đường bị ngắt có thể do thời gian giữa các mẫu quá xa. Đoạn trống không có nghĩa đại lượng bằng 0.
- Trục dọc góc/tốc độ/PID tự thay khoảng theo dữ liệu. Cùng một độ cao trên màn hình giữa hai lần thử chưa chắc là cùng giá trị; hãy đọc số trên trục.

Vòng PID mục tiêu là **200 Hz**, tức khoảng 200 lượt tính/giây. Telemetry cho đồ thị khoảng **20 Hz**, tức khoảng 20 mẫu/giây. Đồ thị có thể không thể hiện hết những biến đổi rất nhanh giữa hai lần gửi.

## 3. ANGLE — drone đã đạt góc muốn giữ chưa?

| Đường | Ý nghĩa | Đơn vị |
|---|---|---|
| Measured | Góc firmware đang đo/ước lượng | Độ (°) |
| Target | Góc bạn yêu cầu board giữ | Độ (°) |

**Vì sao cần hai đường?** Nhìn góc đo riêng lẻ không biết nó đã đúng yêu cầu hay chưa. Độ cách giữa hai đường chính là sai lệch cần sửa.

Ví dụ target=0°: sau một tác động nhỏ, Measured từ +5° tiến về 0° rồi ở gần đó là dấu hiệu hồi về mục tiêu. Nếu nó liên tục qua lại +5°, −5°, +5° thì đang dao động.

| Bạn thấy | Cách hiểu ban đầu | Xem thêm ở đâu? |
|---|---|---|
| Measured tiến gần Target và giữ gần đó | Có dấu hiệu bám mục tiêu | Xem rate có giảm, motor có ổn định không |
| Measured càng xa Target | Có thể sai chiều, thiếu khả năng điều khiển hoặc vấn đề khác | Kiểm tra motor, mapping và dấu trước khi tăng gain |
| Measured vượt Target rồi đổi phía liên tục | Dao động hoặc quá điều chỉnh | Xem BODY RATE và RATE PID |
| Measured lệch cố định | Có thể mốc ngang, đế, lực đẩy hoặc sai lệch kéo dài | Kiểm tra lắp đặt và dữ liệu trước khi quy cho Ki |

Góc ở đây dùng mốc firmware. Nút Zero orientation của mô hình 3D không đổi mốc đồ thị này.

## 4. BODY RATE — vì sao đang gần ngang mà vẫn phải sửa?

| Đường | Ý nghĩa |
|---|---|
| Measured | Tốc độ quay từ gyro, đơn vị °/s |
| Target (C2) | Tốc độ quay mà PID góc yêu cầu, đơn vị °/s |

**Vì sao cần biểu đồ này?** ANGLE cho biết vị trí góc, BODY RATE cho biết chuyển động. Để dừng ở đúng góc, drone còn phải giảm tốc độ quay khi đến gần mục tiêu.

Ví dụ góc +5°, target góc 0°, Kp góc=2: tốc độ yêu cầu là −10°/s. Nếu gyro đang −20°/s thì drone đang quay về nhanh hơn yêu cầu. Vòng tốc độ cần tạo hiệu chỉnh giảm tốc quay về. Vì vậy, có lúc lệnh motor đổi chiều sửa trước khi góc về 0°; đó chưa tự chứng minh sai chiều.

Tốc độ mục tiêu bị giới hạn ±45°/s. Đường Target nằm ngang ở +45 hoặc −45 trong một đoạn có thể là vòng góc đang chạm giới hạn lệnh. Rate đo vẫn có thể vượt ±45°/s; đó là chuyển động thực được đo, không phải giới hạn phần mềm cưỡng chế lên thân drone.

## 5. RATE PID — phần nào đang tạo lệnh sửa?

| Đường | Đại lượng | Vì sao cần nhìn? |
|---|---|---|
| P | Kp nhân sai số tốc độ | Nhìn phản ứng theo sai lệch hiện tại |
| I | Phần sai lệch đã tích lũy | Nhìn phần bù kéo dài có tăng quá lâu không |
| D | Phần phản ứng theo biến đổi tốc độ đo | Nhìn ảnh hưởng chuyển động/nhiễu lên điều khiển |
| Output limited (C2) | Tổng P+I+D sau khi bị giới hạn ±40 | Biết lệnh thực được đưa sang bộ trộn |

Tất cả các đường ở biểu đồ này có đơn vị **duty**. Chúng là kết quả của vòng tốc độ; không phải bốn đường gain và cũng không phải P/I/D vòng góc.

Ví dụ P=111.34, I=D=0 thì Output limited=40. Tăng P thêm trong tình huống đó không làm output tức thời vượt 40. Khi Output limited giữ lâu ở ±40, hãy ghi nhận rằng vòng tốc độ đã hết biên lệnh của nó, thay vì coi đường motor chưa chạm 220 là chưa có giới hạn nào tác động.

I tăng mãi hoặc D dao động mạnh là dữ liệu cần điều tra, không tự xác định duy nhất một nguyên nhân. Chương trình có giới hạn I và điều kiện ngừng tích lũy khi bão hòa; xem [luồng kỹ thuật](luong-dieu-khien-cascade.md) nếu muốn đối chiếu công thức.

## 6. MOTOR OUTPUT — motor nào thực sự được ra lệnh tăng?

| Đường | Vị trí | GPIO |
|---|---|---|
| LF | Trái trước | 14 |
| RF | Phải trước | 15 |
| LR | Trái sau | 12 |
| RR | Phải sau | 13 |

Trục dọc vẽ thang 0–255, còn firmware điều khiển hiện giới hạn lệnh motor 0–220. Giá trị ở đây là lệnh PWM, chưa có cảm biến đo RPM hoặc lực đẩy để xác nhận motor đáp ứng đúng.

**Vì sao cần xem đủ bốn đường?** Một output Roll không gửi nguyên xi tới một motor. Bộ trộn cộng/trừ nó vào base cho hai nhóm trái/phải; khi BOTH thì Pitch cùng tham gia.

Ở trạng thái đứng yên, target 0°, I/D bằng 0, chọn ROLL, mapping motor đúng:

- Trái thấp và FW Roll dương: firmware đã chỉnh để LF/LR cao hơn RF/RR.
- Phải thấp và FW Roll âm: RF/RR cao hơn LF/LR.

Nếu có tốc độ quay lớn hoặc I/D khác 0, hãy xem BODY RATE và RATE PID trước khi kết luận chỉ từ góc nghiêng. Base giữ cả bốn motor có lệnh nền; chọn ROLL không có nghĩa chỉ hai motor hoạt động.

## 7. Một lượt đọc cả bốn biểu đồ

Ví dụ target 0°, Roll đang +5°, rate gần 0, PID góc 2/0/0, PID tốc độ 1.7/0/0, base=90. Đây là ví dụ tính, không phải đề xuất gain:

1. **ANGLE:** Measured=+5°, Target=0° → cần quay về chiều âm.
2. **BODY RATE:** Target=−10°/s, Measured≈0 → còn thiếu chuyển động quay về.
3. **RATE PID:** P=−17, I=D=0, Output limited=−17 → chưa chạm ±40.
4. **MOTOR OUTPUT:** sau chiều Roll và bộ trộn, LF/LR=107, RF/RR=73 → phía trái được tăng lệnh.
5. Xem các mẫu sau: góc có tiến về mục tiêu, rate có giảm phù hợp và motor có tránh dao động tăng dần không?

Không có biểu đồ nào đứng riêng đủ để chứng minh hệ thống ổn định. Ví dụ góc tạm gần 0° nhưng rate cao vẫn có thể sắp vượt góc.

## 8. Vì sao có đường không xuất hiện hoặc đồ thị không chạy?

| Hiện tượng | Điều nên kiểm tra |
|---|---|
| Waiting for firmware control telemetry | Kết nối, sensor READY và firmware có gửi C/C2 không |
| PAUSED | Tắt Pause nếu muốn nhận mẫu mới vào lịch sử |
| NO NEW DATA | Kiểm tra kết nối và thời điểm mẫu cuối; hình cũ không nói motor đang chạy |
| Chỉ thiếu Target rate / Output limited | Firmware cũ có thể chỉ gửi C, thiếu bốn trường mở rộng của C2 |
| Pitch PID luôn 0 sau Start | Có đang điều khiển ROLL thay vì PITCH/BOTH không? |
| Một số đường chồng lên nhau | Các giá trị có thể bằng nhau, ví dụ hai motor cùng bên khi chỉ sửa Roll |
| Lịch sử mất sau Start | App chủ động xóa lịch sử để bắt đầu lượt chạy mới |

## 9. Đồ thị và CSV dùng vào hai việc khác nhau

Đồ thị giúp nhìn nhanh trong lúc thử. **Start / stop CSV log** lưu mẫu ra file để xem lại, so sánh và gửi cho người khác phân tích. Pause đồ thị không dừng CSV; nút CSV không chạy/dừng motor.

Khi lưu một lượt thử, ghi kèm trục điều khiển, gain board đã xác nhận, base, mốc ngang và điều kiện đế/pin. Hai lượt có cùng gain nhưng khác base hoặc điều kiện cơ khí có thể cho phản ứng khác.

## 10. Ghi chú cho người đọc code

Các đường vẽ dùng dữ liệu nhận từ firmware, không tạo dữ liệu mô phỏng. C2 hiện có 23 giá trị sau nhãn; giữ 19 trường của C và thêm `rollRateTarget`, `pitchRateTarget`, `rollOutput`, `pitchOutput`.

App đọc cả C cũ 19 giá trị và C2 25 giá trị từ phiên bản remote khác. Với C cũ, bốn trường mới không có mẫu; CSV để trống. Hai trường recovery riêng của C2 25 giá trị không được đưa vào schema CSV hiện tại hoặc cơ chế điều khiển của bản này.

Bạn không cần nhớ schema để dùng app. Nếu muốn đối chiếu, xem [PidHistory.cs](../../src/DroneMotion.Wpf/Models/PidHistory.cs), [PidPlot.cs](../../src/DroneMotion.Wpf/Controls/PidPlot.cs) và [SerialProtocolParser.cs](../../src/DroneMotion.Wpf/Services/SerialProtocolParser.cs).
