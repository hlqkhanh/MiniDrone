# Các cách tạo đầu ra điều khiển motor: duty, mô-men chuẩn hóa, N·m và gia tốc góc

Tài liệu giải thích bốn cách tổ chức đầu ra của **vòng tốc độ góc (angular-rate control loop)**, kèm ví dụ tính từng bước. Soạn ngày **07/11/2026**, đối chiếu với DroneRebuild hiện tại.

**Điều quan trọng:** cách nào cũng phải kết thúc bằng lệnh mà phần cứng motor thực hiện được. Khác biệt nằm ở ý nghĩa đầu ra PID và các khối chuyển đổi từ đầu ra đó tới motor.

Các con số của mô hình lực, quán tính và hệ số chuyển đổi bên dưới đều là **giả định để học**, không phải thông số đã đo của drone. Không dùng bảng duty trong tài liệu làm cấu hình chạy thử.

## 1. Trước tiên phân biệt bốn đại lượng

| Đại lượng | Tiếng Anh | Ý nghĩa | Đơn vị/thang đo |
| --- | --- | --- | --- |
| Duty PWM | PWM duty cycle | Tỷ lệ thời gian tín hiệu PWM ở mức bật; firmware biểu diễn bằng số đếm | DroneRebuild dùng số 0–255 |
| Mô-men chuẩn hóa | Normalized torque | Yêu cầu mô-men theo thang quy ước của controller/allocator | Không thứ nguyên, thường dùng thang −1 đến +1 |
| Mô-men vật lý | Torque | Tác động làm vật quay quanh một trục | N·m |
| Gia tốc góc | Angular acceleration | Tốc độ thay đổi của tốc độ quay | rad/s² hoặc °/s² |

Ví dụ:

```text
“Sửa thêm 5 duty”       → nói về lệnh điện tới motor.
“Mô-men chuẩn hóa +0.2” → nói về mức yêu cầu trong một thang quy ước.
“Tạo mô-men +0.01 N·m”  → nói về tác động cơ học cần tạo.
“Tạo gia tốc +3 rad/s²” → nói về tốc độ quay cần thay đổi nhanh bao nhiêu.
```

**Mô-men chuẩn hóa (normalized torque)** là tên đại lượng. **Chuẩn hóa (normalization)** là tên quá trình đổi đại lượng sang thang quy ước.

## 2. Bức tranh chung: các cách khác nhau ở đâu?

| Cách | PID xuất ra | Các bước tiếp theo |
| --- | --- | --- |
| 1. Sửa duty trực tiếp | Phần sửa duty | Trộn với Base → giới hạn → PWM |
| 2. Mô-men chuẩn hóa | Yêu cầu mô-men theo thang quy ước | Phân bổ → yêu cầu lực đẩy từng motor → đổi sang lệnh motor |
| 3. Mô-men N·m | Mô-men vật lý | Tính lực từng motor → đổi lực sang lệnh motor |
| 4. Gia tốc góc | Gia tốc góc cần có | Dùng mô hình quán tính để tính mô-men → thực hiện các bước như cách 3 |

Bộ phân bổ điều khiển (control allocator) giải bài toán chia yêu cầu chung cho các motor. Khối chuyển đổi lực–lệnh motor giải bài toán motor phải nhận tín hiệu gì để tạo lực đó. Đây là hai nhiệm vụ khác nhau.

PX4 mô tả việc tách yêu cầu mô-men/lực đẩy khỏi khối phân bổ phụ thuộc hình học máy bay. Điều này hỗ trợ cách tổ chức hệ thống, không cung cấp sẵn mô hình lực hoặc gain cho DroneRebuild. [Nguồn: PX4 Control Allocation](https://docs.px4.io/main/en/concept/control_allocation).

## 3. Cách 1 — PID tạo phần sửa duty, như DroneRebuild hiện tại

### 3.1. Luồng xử lý

```text
Rate target − rate đo
          ↓
PID tốc độ
          ↓
Phần sửa duty u
          ↓
Motor sign + mixer + Base
          ↓
Giới hạn 0–255 và làm tròn
          ↓
PWM → motor → cánh quạt → lực và mô-men thực
```

Source [RollCore.h](../../firmware/DroneRebuild/RollCore.h) cộng P/I/D thành `raw`, giới hạn thành `u`, nhân `motorSign` rồi trộn với `base`.

### 3.2. Ví dụ chỉ dùng P để thấy rõ đơn vị

Giả sử:

```text
Rate target = 0°/s
Rate đo = −10°/s
Rate Kp = 0.5 duty / (°/s)
Ki = Kd = 0, trạng thái I/D bằng 0
Base = 110
Roll sign = −1
Output limit = 20
```

Tính:

```text
error = 0 − (−10) = 10°/s
P = 0.5 × 10 = 5 duty
u = clamp(5, −20, +20) = 5 duty
r = sign × u = −1 × 5 = −5 duty

LF = LR = Base + r = 105
RF = RR = Base − r = 115
```

Kết quả: hai motor trái nhận **105**, hai motor phải nhận **115**. Chưa biết chính xác chênh lực là bao nhiêu N hoặc mô-men là bao nhiêu N·m.

`110/255 ≈ 43.14%` là tỷ lệ duty của số đếm Base này. Nó không có nghĩa motor tạo đúng 43.14% lực đẩy lớn nhất.

### 3.3. Ưu điểm và giới hạn

- Ít khối chuyển đổi, dễ triển khai trên hệ hiện tại.
- Có thể hiệu chỉnh để giữ ổn định nhờ vòng phản hồi, dù chưa biết chính xác lực motor.
- Gain gắn với thang duty và điều kiện vận hành. Cùng một correction duty có thể tạo tác động khác khi pin, Base hoặc motor thay đổi.

Vì thế dùng duty không tự làm PID sai. Hạn chế là quan hệ giữa con số điều khiển và tác động cơ học chưa được biểu diễn rõ trong mô hình.

## 4. Cách 2 — PID tạo mô-men chuẩn hóa

### 4.1. Luồng xử lý

```text
Sai số tốc độ góc
          ↓ PID
Yêu cầu mô-men chuẩn hóa Roll/Pitch/Yaw
          ↓ + yêu cầu lực đẩy tổng
Allocator: phân bổ theo hình học và giới hạn motor
          ↓
Lực đẩy chuẩn hóa của từng motor
          ↓ mô hình motor–cánh
Lệnh motor / duty PWM
```

Ý nghĩa quan trọng là tách yêu cầu quay của thân khỏi cách điều khiển từng motor. Một đầu ra `+0.2` phải được hiểu theo quy ước của hệ; bản thân nó không phải 0.2 N·m hoặc tăng duty 20%.

### 4.2. Ví dụ allocator đơn giản cho Roll

Chọn một **quy ước minh họa**, trong đó correction dương làm bên phải tăng lực. Không suy quy ước dấu này áp dụng cho mọi khung hoặc mọi firmware.

Giả sử PID đã trả `uRoll = +0.2`. Allocator minh họa dùng:

```text
Lực đẩy nền chuẩn hóa t = 0.4 cho mỗi motor
Correction vào motor c = 0.5 × uRoll = 0.1

fLF = fLR = t − c = 0.3
fRF = fRR = t + c = 0.5
```

Trong đó `f` là **lực đẩy chuẩn hóa**, không phải duty. Hệ số `0.5` được chọn cho ví dụ, không phải công thức bắt buộc của PX4 hoặc DroneRebuild.

Tổng yêu cầu lực đẩy chuẩn hóa vẫn là:

```text
0.3 + 0.5 + 0.3 + 0.5 = 1.6
```

Hai bên có chênh lực, còn tổng giữ nguyên theo mô hình này.

### 4.3. Chuyển lực đẩy chuẩn hóa sang duty

Giả sử ta đã có mô hình lý tưởng:

$$f=s^2,\qquad s=\frac{duty}{255}$$

Suy ra:

$$s=\sqrt f,\qquad duty=\operatorname{round}(255\sqrt f)$$

Khi đó:

| Nhóm motor | Lực đẩy chuẩn hóa f | Lệnh chuẩn hóa s | Duty sau làm tròn |
| --- | ---: | ---: | ---: |
| Trái LF/LR | 0.3 | √0.3 ≈ 0.54772 | 140 |
| Phải RF/RR | 0.5 | √0.5 ≈ 0.70711 | 180 |

**Không dùng trực tiếp `duty = 255 × f`** nếu đang giả định `f=s²`: phép đó đã bỏ qua quan hệ lực–lệnh motor.

Mô hình bình phương này chỉ là ví dụ học. Motor brushed thực tế không mặc định tuân theo nó trong toàn dải. PX4 có mô hình đường cong lực đẩy và lưu ý quan hệ tín hiệu–lực phụ thuộc điện áp pin. [Nguồn: PX4 Thrust Curve](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#thrust-curve).

### 4.4. Cần thêm dữ liệu gì?

Cần quy ước chuẩn hóa, ma trận/hệ số phân bổ đúng hình học và chiều motor, giới hạn khả thi, cùng quan hệ lực–lệnh motor phù hợp. Không nhất thiết phải biết lực theo N để làm một mô hình chuẩn hóa, nhưng vẫn phải xác định thang và đặc tính tương đối đủ tốt.

**Chỉ lấy output duty chia 255 chưa thực hiện các bước này.** Đó mới là đổi thang số, chưa tự có allocator hoặc tuyến tính hóa lực đẩy.

## 5. Cách 3 — PID tạo mô-men vật lý N·m

### 5.1. Luồng xử lý

```text
Sai số tốc độ góc
          ↓ PID với gain có đơn vị phù hợp
Mô-men yêu cầu, N·m
          ↓ + lực đẩy tổng yêu cầu, N
Phân bổ thành lực từng motor, N
          ↓ đặc tính motor–cánh và điện áp
Duty / lệnh motor
```

Khác với cách 2, ở đây yêu cầu được biểu diễn bằng đơn vị vật lý. Nhưng đơn vị vật lý không làm mô hình tự đúng: vẫn cần dữ liệu và kiểm chứng.

### 5.2. Ví dụ: cần mô-men Roll 0.01 N·m

Giả sử chỉ xét Roll, thân gần ngang, các motor giống nhau và:

```text
Khoảng cách ngang từ mỗi motor tới trục Roll: l = 0.05 m
Lực nền mỗi motor: F0 = 0.25 N
Tổng lực nền: 4 × 0.25 = 1 N
Mô-men Roll yêu cầu: τ = +0.01 N·m
```

**`l` là khoảng cách ngang vuông góc tới trục Roll**, không tự bằng chiều dài cánh tay chéo từ tâm tới motor.

Tiếp tục dùng quy ước của ví dụ: mô-men dương làm tăng lực bên phải, giảm bên trái. Tăng mỗi motor phải thêm `ΔF`, giảm mỗi motor trái `ΔF`:

```text
LF = LR = F0 − ΔF
RF = RR = F0 + ΔF
```

Chênh lực của hai nhóm:

$$F_{right}-F_{left}=2(F_0+\Delta F)-2(F_0-\Delta F)=4\Delta F$$

Do đó:

$$\tau=l(F_{right}-F_{left})=4l\Delta F$$

$$\Delta F=\frac{0.01}{4\times0.05}=0.05\;N$$

Kết quả:

```text
LF = LR = 0.25 − 0.05 = 0.20 N
RF = RR = 0.25 + 0.05 = 0.30 N

Kiểm tra mô-men:
0.05 × [(0.30 + 0.30) − (0.20 + 0.20)] = 0.01 N·m

Kiểm tra tổng lực:
0.20 + 0.30 + 0.20 + 0.30 = 1 N
```

Tổng lực 1 N chỉ là giá trị chọn để tính. Muốn bay lơ lửng còn phải xét trọng lượng thật; muốn giữ trên gá còn phải xét tải của gá.

### 5.3. Đổi lực thành duty bằng mô hình giả định

Giả sử mỗi motor có mô hình:

$$F=F_{max}\left(\frac{duty}{255}\right)^2,\qquad F_{max}=0.5\;N$$

Suy ra:

$$duty=\operatorname{round}\left(255\sqrt{\frac{F}{F_{max}}}\right)$$

| Nhóm motor | Lực yêu cầu mỗi motor | F/Fmax | Duty |
| --- | ---: | ---: | ---: |
| Trái LF/LR | 0.20 N | 0.4 | 161 |
| Phải RF/RR | 0.30 N | 0.6 | 198 |

Ở đây duty được tính từ lực yêu cầu, thay vì lấy trực tiếp `Base ± output PID`.

### 5.4. Cần thêm dữ liệu gì?

- Vị trí từng motor so với trọng tâm và hệ trục.
- Quan hệ duty–lực, có xét điện áp và điều kiện tải phù hợp.
- Với Yaw, quan hệ mô-men phản lực của từng motor–cánh.
- Giới hạn lực khả thi và cách xử lý khi các yêu cầu không cùng thực hiện được.

Không bắt buộc biết mô-men quán tính để viết một PID thực nghiệm xuất N·m, nhưng phải có cách quy đổi và thực hiện lệnh mô-men. Quán tính trở nên cần thiết hơn khi chuyển từ gia tốc góc sang mô-men như cách 4.

## 6. Cách 4 — PID tạo gia tốc góc yêu cầu

### 6.1. Luồng xử lý

```text
Rate target − rate đo
          ↓ PID
Gia tốc góc yêu cầu α
          ↓ mô hình động lực học
Mô-men yêu cầu τ
          ↓ phân bổ lực từng motor
Lệnh motor
```

Gia tốc góc trả lời câu hỏi: **tốc độ quay cần tăng/giảm nhanh bao nhiêu?** Nó không phải góc nghiêng và cũng không phải tốc độ quay.

### 6.2. Ví dụ tính từ sai số rate tới duty

Giả sử chỉ xét một trục quay, bỏ qua mô-men ngoài và ghép trục:

```text
Rate target = 0°/s
Rate đo = −10°/s
Sai số = +10°/s
```

Đổi sang rad/s trước khi dùng mô hình cơ học theo SI:

$$e_\omega=10\frac{\pi}{180}\approx0.174533\;rad/s$$

Giả sử chỉ dùng P với hệ số `Kpα = 20 s⁻¹`:

$$\alpha_{cmd}=K_{p\alpha}e_\omega=20\times0.174533\approx3.490659\;rad/s^2$$

Giả sử mô-men quán tính hiệu dụng của mô hình một trục:

$$J=0.002\;kg\cdot m^2$$

Khi đó:

$$\tau=J\alpha_{cmd}=0.002\times3.490659\approx0.00698132\;N\cdot m$$

Dùng tiếp `l=0.05 m`, `F0=0.25 N` của ví dụ cách 3:

```text
ΔF = τ / (4l)
   = 0.00698132 / (4 × 0.05)
   ≈ 0.0349066 N

Lực mỗi motor trái = 0.25 − 0.0349066 ≈ 0.2150934 N
Lực mỗi motor phải = 0.25 + 0.0349066 ≈ 0.2849066 N
```

Với cùng mô hình `Fmax=0.5 N` và lực tỷ lệ bình phương lệnh:

```text
Duty trái = round[255 × sqrt(0.2150934 / 0.5)] = 167
Duty phải = round[255 × sqrt(0.2849066 / 0.5)] = 192
```

Ta đã đi đủ chuỗi:

```text
Sai số 10°/s
    → 0.174533 rad/s
    → gia tốc góc 3.490659 rad/s²
    → mô-men 0.00698132 N·m
    → lực từng motor
    → duty 167 / 192
```

Các giá trị giả định này không phải gain hay quán tính đã đo của DroneRebuild.

### 6.3. Vì sao ba trục phức tạp hơn τ=Jα?

Trong mô hình vật rắn ba chiều, biểu diễn trong hệ body và với ma trận quán tính phù hợp:

$$\boldsymbol\tau_{motor}+\boldsymbol\tau_{external}
=J\dot{\boldsymbol\omega}+\boldsymbol\omega\times(J\boldsymbol\omega)$$

Trong đó `ω` là vector tốc độ góc, `J` là ma trận quán tính, `τexternal` gồm các mô-men ngoài đang xét. Thành phần tích có hướng thể hiện ảnh hưởng ghép chuyển động quay.

Không cần nhớ công thức này để tính ví dụ một trục. Điều cần hiểu là: khi quay phối hợp, không luôn lấy ba phép `τ=Jα` độc lập là đủ.

Gá nhiều vòng còn có động lực học của chính cơ cấu. Một `J` hiệu dụng một trục không tự mô tả đầy đủ mọi tư thế của gá ba trục.

## 7. Bốn ví dụ cho duty khác nhau có nghĩa cách nào mạnh hơn?

**Không so sánh như vậy.** Các ví dụ đang dùng đầu ra, gain, lực nền và mô hình khác nhau để minh họa cách đổi đơn vị.

| Ví dụ | Đầu ra đang minh họa | Duty trái / phải |
| --- | --- | --- |
| Duty trực tiếp | Correction 5 duty, Base 110, sign −1 | 105 / 115 |
| Mô-men chuẩn hóa | uRoll=0.2, allocator minh họa và f=s² | 140 / 180 |
| Mô-men N·m | τ=0.01 N·m, lực nền 0.25 N/motor | 161 / 198 |
| Gia tốc góc | α≈3.490659 rad/s², J=0.002 kg·m² | 167 / 192 |

Nếu muốn so hiệu quả, phải đặt **cùng phần cứng, cùng mục tiêu, cùng điều kiện đầu và mô hình đã xác định**, rồi so đáp ứng thực. Không lấy độ lớn duty trong bảng làm thước đo chất lượng thuật toán.

## 8. Vì sao không lấy Kp/Ki/Kd cũ dùng cho cả bốn cách?

Vẫn có dạng PID, nhưng đơn vị output đã đổi:

```text
Cách 1: Kp nhân sai số để ra duty.
Cách 2: Kp nhân sai số để ra yêu cầu chuẩn hóa.
Cách 3: Kp nhân sai số để ra N·m.
Cách 4: Kp nhân sai số để ra rad/s².
```

Cùng con số `Kp=0.2` không có cùng tác động trong bốn hệ này. Việc đổi từ °/s sang rad/s cũng cần quy đổi nhất quán hệ số và giới hạn.

Ví dụ: nếu đã biết hệ số tuyến tính gần điểm làm việc `τ≈kτ × correction duty`, ta có thể xây dựng phép đổi thang tương ứng. Nhưng `kτ` cần được xác định; nó không tự bằng 1 và có thể thay đổi theo Base/điện áp.

## 9. Mọi cách đều phải xử lý bão hòa

Nếu một motor đã tới giới hạn, yêu cầu thêm lực không còn thực hiện được. Các cách 2–4 không tự giải quyết điều này chỉ nhờ có tên đại lượng tốt hơn.

Ví dụ mô hình chuẩn hóa yêu cầu:

```text
f trái = 0.85
f phải = 1.05
```

Motor không thể tạo `1.05` nếu `1` đã là giới hạn được mô hình hóa. Cần allocator quyết định giảm yêu cầu, dịch collective nếu khả thi hoặc thay đổi ưu tiên các trục. Cuối cùng vẫn cần chặn giới hạn lệnh motor.

Anti-windup phải biết lệnh đang bị hạn chế để tránh I tiếp tục tích lũy theo một tác động không thể thực hiện. PX4 mô tả các lựa chọn điều chỉnh torque/collective khi mixer chạm biên. [Nguồn: Airmode & Mixer Saturation](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#airmode-mixer-saturation).

## 10. Áp dụng thế nào cho DroneRebuild?

**Hiện tại:** DroneRebuild dùng cách 1. Giữ nguyên cách này vẫn có thể phát triển điều khiển Yaw, mixer ba trục và phản hồi bão hòa tốt hơn.

Lộ trình có thể cân nhắc:

1. Giữ bộ điều khiển hiện tại làm mốc; bổ sung khả năng quan sát đầy đủ các trục và xác định nguyên nhân đáp ứng xấu.
2. Hoàn thiện Yaw Rate, phối trộn và xử lý giới hạn; kiểm chứng nguồn/gá cùng với tracking.
3. Nếu cần giảm phụ thuộc điểm làm việc, xác định quan hệ lệnh motor–lực và điện áp để xây dựng lớp chuyển đổi tốt hơn.
4. Khi có dữ liệu phù hợp, lựa chọn mô-men chuẩn hóa hoặc N·m; xét thêm mô hình quán tính nếu muốn điều khiển theo gia tốc góc.

Đây là thứ tự phát triển đề xuất, không phải yêu cầu phải đi qua đủ mọi cách. Đổi cách biểu diễn output không thay cho kiểm chứng lực thực, chiều điều khiển hoặc tải gá.

Đọc cùng:

- [Công thức PID đang áp dụng trong DroneRebuild](PID_FORMULAS_AND_WORKED_EXAMPLES.md).
- [Ảnh kiến trúc hệ thống hiện tại](CURRENT_SYSTEM_ARCHITECTURE.md).
- [Đánh giá bằng chứng và lộ trình trên gá ba trục](../references/three-axis/THREE_AXIS_RIG_EVIDENCE_AND_VALIDATION.md).

## 11. Các nguồn để đối chiếu

- [PX4 — Control Allocation](https://docs.px4.io/main/en/concept/control_allocation): phân tách controller và bộ phân bổ actuator theo hình học.
- [PX4 — Thrust Curve](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#thrust-curve): quan hệ tín hiệu motor–lực đẩy, mô hình hóa và ảnh hưởng điện áp pin.
- [PX4 — Airmode & Mixer Saturation](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter#airmode-mixer-saturation): xử lý yêu cầu vượt khả năng motor.
- [RollCore.h của dự án](../../firmware/DroneRebuild/RollCore.h): đầu ra correction duty và mixer thực tế hiện tại.

Các ví dụ số là phép tính minh họa do tài liệu này xây dựng, không phải thông số trích từ PX4. Tài liệu không thay đổi code firmware/app.
