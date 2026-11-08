# Cân bằng trên gá ba trục: đề xuất có hợp lý không và cần chứng minh điều gì?

**Đối chiếu ngày 07/11/2026**, từ [bản đề xuất ba trục](GIAI_PHAP_CAN_BANG_3_TRUC_ESP32_MPU6050.md), [danh mục nguồn](TAI_LIEU_ESP32_MPU6050_3_TRUC.txt), source DroneRebuild hiện tại và các nguồn gốc dưới đây.

## 1. Kết luận để quyết định

**Hướng kiến trúc trong tài liệu cũ hợp lý: giữ Roll/Pitch bằng vòng góc–tốc độ, bổ sung điều khiển tốc độ Yaw, rồi trộn cả ba trục. Nhưng chưa đủ căn cứ để nói chỉ thêm Yaw PID là gá của bạn chắc chắn cân bằng.**

Ba nhóm điều kiện phải cùng đạt:

1. **Gá và nguồn:** không tạo mô-men cản/đổ lớn hơn khả năng motor, nguồn đủ ổn định khi có tải.
2. **Phần mềm:** số đo đúng trục, ba vòng rate có phản hồi âm, mixer đúng chiều và có xử lý giới hạn.
3. **Kiểm chứng:** log cho thấy bám mục tiêu và hồi phục sau nhiễu nhỏ trong những lần thử có thể lặp lại.

Điều bạn đã quan sát — bay tự do khá ổn định nhưng gục trên gá — là lý do cần xem **cả cơ học gá lẫn controller**, không chỉ tăng gain hoặc quy toàn bộ lỗi cho thiếu Yaw.

File này tổng hợp bằng chứng và quy trình đề xuất; **chưa triển khai chức năng mới, chưa xác nhận nghiệm thu trên phần cứng**.

## 2. Các nguồn thực sự chứng minh điều gì?

| Nguồn gốc đã đối chiếu | Điều nguồn hỗ trợ | Điều không thể suy ra |
| --- | --- | --- |
| [PX4: PID Tuning, mục Rate Controller](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#rate-controller) | Vòng trong điều khiển body rate của Roll, Pitch, Yaw; chất lượng vòng này ảnh hưởng các vòng ngoài | Gain của PX4 dùng nguyên được cho duty của DroneRebuild |
| [ArduPilot: Copter Attitude Control](https://ardupilot.org/dev/docs/apmcopter-programming-attitude-control-2.html) | Sai số góc tạo rate mong muốn; sai số rate đi qua PID để tạo lệnh điều khiển | PID riêng từng trục chạy tốt thì mọi gá đều chạy tốt |
| [PX4: Control Allocation](https://docs.px4.io/main/en/concept/control_allocation) | Phân bổ thrust/torque thành lệnh actuator phụ thuộc hình học cơ cấu | Một ma trận dấu Yaw bất kỳ đều đúng với motor thực tế |
| [PX4: Airmode & Mixer Saturation](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#airmode-mixer-saturation) | Khi motor hết khoảng điều khiển, cần xử lý collective/torque thay vì mặc định mọi yêu cầu đều thực hiện được | Bật Airmode hay cho tăng Base không giới hạn là giải pháp phù hợp cho gá này |
| [Sharma, Dijkstra, Prasad: Open Gimbal, 2023](https://arxiv.org/html/2310.02678) | Gá cho drone nhỏ phải hạn chế quán tính tăng thêm và can thiệp vào động lực; thiết kế dùng ổ quay ma sát thấp | Bất kỳ khung cho quay 360° nào cũng tương đương bay tự do |
| [Brescianini & D’Andrea: Tilt-Prioritized Quadrocopter Attitude Control, 2020](https://www.research-collection.ethz.ch/entities/publication/96b0f75c-8ade-4c02-b7fa-cb6497e17e84) | Có cơ sở nghiên cứu cho việc ưu tiên chỉnh hướng lực đẩy/độ nghiêng hơn Yaw | Thứ tự allocator cụ thể trong bản đề xuất đã được chứng minh cho DroneRebuild |

**Cách sử dụng bằng chứng:** lấy nguyên tắc thiết kế từ nguồn; gain, dấu motor, giới hạn và tiêu chí đạt phải kiểm chứng trên hệ của bạn. Các mục thực hành dưới đây là suy luận kỹ thuật áp dụng cho DroneRebuild, không phải quy trình nghiệm thu do PX4 chứng nhận.

## 3. Những câu trong bản đề xuất cũ cần hiểu lại

| Nội dung cũ | Đánh giá sau đối chiếu |
| --- | --- |
| “ESP32 + MPU6050 đủ để giữ ổn định gá ba trục” | Có thể đủ về loại phép đo cho mục tiêu gần ngang và chống quay, nhưng chưa chứng minh đủ mô-men, chất lượng gá, nguồn và thời gian xử lý |
| “Không có magnetometer thì không dùng Outer Yaw Angle” | Quá tuyệt đối. Có thể giữ hướng **tương đối** trong thời gian giới hạn bằng gyro; không có tham chiếu ngoài thì không bảo đảm heading tuyệt đối lâu dài |
| “Thiếu Yaw nên Roll/Pitch gục” | Là một khả năng cần xét, chưa phải chẩn đoán. Gá lệch trọng tâm hoặc rate/mixer chạm biên cũng có thể gây gục |
| “Cần thêm giảm accelerometer correction khi gia tốc khác 1g” | Code hiện tại **đã có trọng số này**; cần đánh giá hiệu quả thực tế, không ghi nhận là chức năng hoàn toàn chưa có |
| “Dùng Mahony” | Là một lựa chọn nghiên cứu, không bắt buộc đổi bộ lọc. Code đang dùng Madgwick IMU |
| “Không clamp từng motor” | Phải hiểu là không chỉ dựa vào clamp độc lập để phân bổ lệnh. Vẫn cần chặn giới hạn cuối cùng trước khi xuất PWM |
| “Ưu tiên Roll/Pitch rồi giảm Yaw” | Hợp lý làm mục tiêu thiết kế; thuật toán cụ thể còn phải xét khoảng motor, nguồn và phản hồi anti-windup |
| “Thử được trên gá ba trục” nghĩa là tự hồi từ mọi góc | Không đúng. Tự giữ gần ngang khác bài toán hồi từ tư thế lộn ngược hoặc quay liên tục 360° |

Yaw Rate target = 0 nghĩa là chống tốc độ quay quanh trục Z của thân. Khi thân gần ngang, nó gần với việc ngừng xoay hướng. Khi nghiêng lớn và quay phối hợp, body rate Z không đồng nhất với đạo hàm góc Euler Yaw; không dùng hai khái niệm này thay nhau trong mọi tư thế.

## 4. Code hiện tại đã có gì, còn thiếu gì?

Đây là kết quả đọc source tại thời điểm viết, không suy từ tên tài liệu:

| Hạng mục | Thực trạng |
| --- | --- |
| IMU | [Imu.h](../../../firmware/DroneRebuild/Imu.h) đọc SPI, chấp nhận WHO_AM_I `0x70` hoặc `0x71` |
| Quy ước body | [Hardware.h](../../../firmware/DroneRebuild/Hardware.h): X=−sensor Y, Y=sensor X, Z=sensor Z |
| Roll/Pitch | [RollCore.h](../../../firmware/DroneRebuild/RollCore.h) có hai controller cascade độc lập và mixer chung |
| Yaw PID | Chưa có controller, target và correction Yaw trong mixer |
| Giới hạn motor | Mixer giới hạn từng motor về 0–255 và phản hồi cờ giới hạn về các PID; chưa có allocator ba trục |
| Bộ ước lượng | Madgwick IMU, dùng quaternion; góc Euler được lấy ra để điều khiển Roll/Pitch |
| Gia tốc khác 1g | Đã có trọng số `clamp((0.20 − abs(normAccel−1))/0.15, 0, 1)` trong cập nhật attitude |
| Telemetry hai trục | [DroneRebuild.ino](../../../firmware/DroneRebuild/DroneRebuild.ino) gửi PID chính của Roll khi chạy cả hai; chưa có PID chi tiết cả ba trục |

### Chú ý tên cảm biến

Bạn đã gửi INFO có `WHO_70`. [Register Map của MPU-6500, Register 117](https://invensense.tdk.com/wp-content/uploads/2015/02/MPU-6500-Register-Map2.pdf) ghi giá trị nhận dạng `0x70`. Đây phù hợp với nhánh driver hiện tại; không lấy tên “MPU6050” trong tài liệu đề xuất làm lý do thay driver hoặc sơ đồ nối dây. Link TDK hiện có thể chuyển sang trang tìm tài liệu; tra mã **RM-MPU-6500A-00** nếu PDF không mở được.

Trọng số theo norm gia tốc chỉ là một biện pháp giảm nhiễu ước lượng. Nó không chứng minh mọi mẫu có norm gần 1g đều chỉ chứa trọng lực.

## 5. Vì sao cần xét gá trước khi kết luận PID sai?

### 5.1. Một ví dụ cơ học đơn giản

Với một cụm quay có trọng tâm cách tâm xoay khoảng `d`, mô-men trọng lực trong mô hình con lắc có độ lớn:

$$|\tau_g|=m g d |\sin\theta|$$

Nguồn về mô-men trọng lực, ma sát và giới hạn điều khiển: [MIT — The Simple Pendulum](https://underactuated.mit.edu/pend.html). Trọng tâm nằm trên điểm tựa tạo tư thế kiểu con lắc ngược; nằm dưới điểm tựa có thể tạo xu hướng hồi về. Đây là mô hình đơn giản để hiểu, không phải mô hình đầy đủ của gá nhiều vòng.

Ví dụ **giả định** `m=0.1 kg`, `d=0.01 m`, góc 10°:

```text
|τg| = 0.1 × 9.81 × 0.01 × sin(10°)
     ≈ 0.00170 N·m
```

Không thể so trực tiếp `0.00170 N·m` với Output limit `20 duty`; còn thiếu quan hệ duty → lực/mô-men thực. Khi xét gá, khối lượng và trọng tâm liên quan còn gồm phần cơ cấu cùng quay.

### 5.2. Suy luận cho hiện tượng bạn đang gặp

Nếu gá thêm ma sát, dây kéo, quán tính hoặc mô-men trọng lực, cùng một bộ PID có thể phản ứng khác bay tự do. Việc bạn đã bay gần ngang được là bằng chứng hữu ích về đáp ứng trong lần bay đó; chưa đo được các tác động riêng của gá.

Mục tiêu gá là để motor còn đủ quyền thay đổi tư thế. Nếu controller đã yêu cầu mô-men đúng chiều nhưng gá không chuyển động, hoặc chỉ trượt sau khi tích lũy lực lớn, tăng gain có thể chỉ làm giật mạnh hơn.

Không cần mặc định làm lại mọi kiểm tra bạn đã xác nhận. Chỉ kiểm tra lại phần thay đổi khi chuyển từ gá một trục sang gá ba trục: điểm tựa/trọng tâm, phần cùng quay, ma sát và dây.

## 6. Kiến trúc tối thiểu hợp lý cho mục tiêu của bạn

Mục tiêu đầu tiên nên được định nghĩa là: **giữ Roll/Pitch gần 0°, không quay Yaw liên tục, hồi phục sau nhiễu nhỏ gần tư thế ngang**. Gá được phép quay 360° về cơ khí không có nghĩa controller phải hồi phục từ mọi tư thế.

```text
Góc Roll mục tiêu 0°  → Outer Roll  → p_target → Rate Roll  ─┐
Góc Pitch mục tiêu 0° → Outer Pitch → q_target → Rate Pitch ─┼→ Mixer/allocator
Yaw body-rate mục tiêu 0°/s        → r_target → Rate Yaw   ─┘        ↓
                                                               Motor
       ↑                     ↑
  Góc ước lượng      Gyro body p, q, r đã bù bias
```

Ở đây `p, q, r` là tốc độ quay quanh các trục body, không phải thành phần quaternion.

Với map hiện tại, đầu vào Yaw Rate dự kiến là `r = gyroZ − biasZ`. Vẫn phải xác nhận chiều correction thực tế. Không lấy đạo hàm góc Yaw hiển thị có wrap ±180° để thay cho gyro rate.

Rate Yaw có thể bắt đầu nghiên cứu từ P hoặc PI; không bắt buộc nhánh D phải khác 0. Hệ số phải xác định theo log và thang duty của hệ, không chép gain từ PX4 hoặc lấy bằng Roll/Pitch.

Khi mục tiêu chuyển sang góc lớn và chuyển động phối hợp, nên xét controller attitude quaternion hoặc phép chuyển attitude error thành body-rate phù hợp. **Bộ ước lượng có quaternion không đồng nghĩa vòng điều khiển đã là quaternion controller.**

## 7. Mixer và giới hạn: cần làm đúng điều gì?

### 7.1. Dấu Yaw phụ thuộc chiều motor và quy ước trục

Một mẫu biểu thức, với `r,p,y` ở đây là **correction sau nhân sign**, không phải body rate:

```text
LF = B + r + p − y
RF = B − r + p + y
LR = B + r − p + y
RR = B − r − p − y
```

Mẫu này chia hai nhóm chéo LF/RR và RF/LR. Chỉ dùng khi phản lực của hai nhóm và dấu body-rate thực sự phù hợp. Hình CW/CCW còn phụ thuộc góc nhìn; không suy ra dấu Yaw chỉ từ vị trí motor.

Đây là biểu thức tuyến tính theo duty, chưa phải bản đồ mô-men N·m đã đo. Sự khác nhau giữa motor/cánh và quan hệ duty–lực có thể làm đáp ứng thực khác biểu thức lý tưởng.

Phải phân biệt ba việc: sensor báo chiều nào, correction tăng nhóm nào, và tác động thực có hãm rate đang đo hay không.

### 7.2. Allocator không thay thế giới hạn cuối

Nếu `v_i` là tổng correction của motor i và `m_i=B+v_i`, muốn mọi motor nằm trong 0–255 bằng cách dịch collective thì cần:

```text
−min(v_i) ≤ B mới ≤ 255 − max(v_i)
```

Khoảng trên chỉ tồn tại nếu `max(v_i) − min(v_i) ≤ 255`. Đây là điều kiện toán học của ví dụ mixer; giới hạn dòng, điện áp, duty vận hành và Base cho phép có thể làm khoảng thực nhỏ hơn.

Nếu không đủ khoảng, có thể giảm Yaw trước rồi giảm correction Roll/Pitch theo chính sách đã chọn. Không có một thứ tự allocator duy nhất tự bảo đảm đúng cho mọi gá. Phải phản hồi phần lệnh không thực hiện được về anti-windup, và vẫn clamp ở cuối để không xuất PWM ngoài giới hạn.

Nếu log cho thấy motor luôn còn nhiều khoảng và không bão hòa, thiếu desaturation **không phải lời giải thích trực tiếp** cho lần gục đó. Lúc đó cần xét tracking, cơ học và số đo.

## 8. Lộ trình kiểm chứng có thể dùng để triển khai

Các bước cần Yaw controller là **công việc phát triển tiếp theo**, chưa bật được chỉ bằng nhập thêm hệ số vào app hiện tại. Giữ nguyên các gain Roll/Pitch đang có làm mốc so sánh.

| Giai đoạn | Việc cần chứng minh | Bằng chứng cần có |
| --- | --- | --- |
| A. Cơ học/nguồn | Gá không gây tải bất thường; nguồn không gây reset/mất liên lạc khi tải tăng | Ghi cấu hình gá và pin, hướng gục, log liên tục; đo nguồn nếu nghi sụt áp |
| B. Roll/Pitch cùng chạy | Hai trục còn bám rate khi ghép chung | Target và measured rate từng trục, duty, PID/mixer limit; đối chiếu lần chạy riêng đã có |
| C. Phát triển Yaw Rate | Feedback đúng chiều và có đủ quyền hãm Yaw | Sai số, P/I/D, correction Yaw và kết quả rate thực; chưa thử hồi từ góc nghiêng lớn |
| D. Ghép ba rate | Yaw được hãm mà không làm Roll/Pitch mất tracking | Dữ liệu ba rate đồng thời, motor còn khoảng điều khiển, I không tích lũy vô ích |
| E. Angle Roll/Pitch + Rate Yaw | Gần ngang được giữ và hồi phục sau lệch nhỏ | Góc, ba rate, correction và duty ở cả hai chiều lệch |

Giữ drone chắc trên gá và dùng cách tạo nhiễu không đưa tay vào vùng cánh. Tái sử dụng các kiểm tra motor/sensor đã xác nhận, không làm lại tùy tiện nếu phần cứng không đổi.

### Bộ dữ liệu tối thiểu để biết cần sửa gì

- Thời gian mẫu/`dt`, trạng thái controller và lỗi dừng.
- Roll/Pitch angle; `p,q,r` đã bù bias và các rate target.
- P/I/D, correction trước/sau giới hạn của từng trục.
- Duty LF/RF/LR/RR, cờ bão hòa PID/mixer và phần lệnh allocator không thực hiện được nếu có.
- Base, sign, các gain/limit và điều kiện nguồn của phiên thử.

Hiện đã có gyro Z thô trong telemetry và bias trong INFO, nên có thể đối chiếu rate Z từ dữ liệu đó. Nhưng chưa có target/output Yaw; PID chi tiết Pitch cũng chưa đầy đủ trong telemetry hai trục. Không thể dùng riêng đồ thị PID Roll để nghiệm thu cả ba trục.

### Một tiêu chí thực hành ban đầu

Ví dụ tiêu chí **tự đề xuất**, không phải chuẩn từ các nguồn: trong lượt 10 giây gần ngang, Roll/Pitch ở khoảng ±3°, phần lớn mẫu rate Yaw nằm trong ±3°/s và không quay một chiều kéo dài; không có lỗi nguồn/timing hoặc bão hòa kéo dài. Sau lệch nhỏ 3–5° ở mỗi phía, sai số giảm trở lại, không dao động tăng dần. Rate Yaw trung bình gần 0 chưa đủ nếu thực tế đang lắc mạnh qua lại.

Các ngưỡng phải điều chỉnh theo nhiễu cảm biến và khả năng đo thực tế. Một lượt đạt không đủ kết luận: cần lặp lại trong điều kiện pin và gá được ghi rõ. Tiêu chí này cũng không chứng minh ổn định ở mọi góc hoặc đủ điều kiện bay tự do.

## 9. Đọc kết quả để chọn đúng hướng xử lý

| Quan sát trong log | Hướng kiểm tra tiếp |
| --- | --- |
| Yaw quay nhưng Roll/Pitch vẫn bám tốt | Phát triển/kiểm chứng Yaw Rate, chưa vội chỉnh lại Roll/Pitch |
| Roll/Pitch gục trong khi Yaw rate nhỏ | Không quy hết cho Yaw; xét mô-men gá, sign, tracking, số đo và giới hạn |
| Correction đúng chiều, liên tục chạm limit mà rate vẫn đi ngược | Xét quyền điều khiển thực và tải gá; chưa đủ cơ sở tăng gain |
| Measured rate rung mạnh nhưng chuyển động thật nhỏ | Xét rung, lắp cảm biến, lọc và thời gian mẫu |
| Một trục ổn, ghép trục mới bão hòa motor | Xét headroom và allocator, cùng với anti-windup |
| Chỉ chạy mạnh được khi thêm USB | Giữ giả thuyết vấn đề cấp nguồn; kết quả có USB không thay cho kiểm chứng dùng pin |

Các hàng này là giả thuyết phân loại theo bằng chứng, không phải chẩn đoán tự động. Chưa có log của lần gục trên gá trong yêu cầu này nên chưa chọn được một nguyên nhân duy nhất.

## 10. Độ tin cậy của danh mục nguồn cũ

Ưu tiên các link chính thức PX4/ArduPilot đã mở trong mục 2. Bản PX4 v1.13 có nội dung control allocation mang bối cảnh phiên bản cũ; không trộn tham số v1.12/v1.13 với tài liệu `main` như cùng một phiên bản.

Các link PDF TDK/Mahony/Euston/ETH cũ có link chuyển hướng, bị chặn truy cập hoặc timeout khi kiểm tra. Điều đó không tự chứng minh bài nghiên cứu sai, nhưng không nên ghi là đã đọc được toàn bộ PDF trong lần rà soát này. Nguồn ETH 2020 trong mục 2 được dùng theo phần tóm tắt công bố của chính kho ETH; không suy thành chứng minh ổn định cho firmware của bạn.

Một bài về bộ lọc attitude chỉ hỗ trợ cách ước lượng; nó không chứng minh toàn bộ hệ motor–PID–gá sẽ cân bằng. Tương tự, nghiên cứu Open Gimbal cho thấy một nền tảng thử ba trục cho nano-drone là khả thi, không cung cấp sẵn mô hình hoặc gain cho gá đang có.

**Việc tiếp theo thực tế:** ghi rõ mục tiêu gần ngang, đối chiếu tải cơ học của gá với log Roll/Pitch hiện tại; sau đó phát triển Yaw Rate + mixer/allocator và telemetry tương ứng để kiểm chứng theo từng giai đoạn. Không có căn cứ để chỉ đưa một bộ Kp/Ki/Kd mới rồi cam kết cân bằng được.

Đọc cùng [công thức PID đang áp dụng](../../drone-rebuild/PID_FORMULAS_AND_WORKED_EXAMPLES.md) và [giao thức hiện tại](../../drone-rebuild/PROTOCOL.md).
