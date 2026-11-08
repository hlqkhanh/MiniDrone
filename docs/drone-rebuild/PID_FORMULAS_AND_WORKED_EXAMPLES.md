# Công thức PID đang dùng trong DroneRebuild — giải thích và tính từng bước

Tài liệu này giải thích **đúng thuật toán trong source hiện tại**, đối chiếu ngày **07/11/2026**. Mục tiêu là giúp bạn nhìn một dòng số đo và hiểu vì sao firmware tạo ra lệnh motor như vậy.

Nguồn chính là [RollCore.h](../../firmware/DroneRebuild/RollCore.h), hàm `Controller::step()` và `DualController::step()`. Cách lấy số đo, gọi PID và xuất telemetry nằm trong [DroneRebuild.ino](../../firmware/DroneRebuild/DroneRebuild.ino). Quy ước trục nằm trong [Hardware.h](../../firmware/DroneRebuild/Hardware.h); góc và mốc cân bằng được xử lý trong [Imu.h](../../firmware/DroneRebuild/Imu.h).

**Các số dưới đây dùng để giải thích phép tính.** Chúng không chứng minh khả năng bay, không phải yêu cầu đổi cấu hình của bạn. Tài liệu không sửa firmware hoặc logic ứng dụng.

## 1. Trước hết: PID đang cố điều khiển điều gì?

DroneRebuild có hai chế độ cần phân biệt:

| Chế độ | Người dùng yêu cầu | Bộ điều khiển làm gì? |
| --- | --- | --- |
| RATE | Tốc độ quay, đơn vị °/s | Chạy PID tốc độ để bám tốc độ đó |
| ANGLE | Góc nghiêng, đơn vị ° | PID góc tạo tốc độ quay cần có; PID tốc độ tiếp tục tạo phần sửa motor |

Trong ANGLE, luồng tính cho mỗi trục là:

```text
Góc mục tiêu và góc đo
          ↓
PID góc (Outer)
          ↓
Tốc độ quay mục tiêu, có giới hạn
          ↓          ← tốc độ quay đo từ gyro
PID tốc độ (Inner)
          ↓
Phần sửa duty, có giới hạn
          ↓          ← Base và Motor sign
Mixer → giới hạn 0–255 → làm tròn → lệnh motor
```

Hai tầng trả lời hai câu hỏi khác nhau:

- **Outer:** đang lệch góc như vậy thì cần quay nhanh bao nhiêu?
- **Inner:** để đạt tốc độ quay đó thì cần sửa lệnh motor bao nhiêu?

Ví dụ: thân đang ở −3°, mục tiêu 0°. Outer có thể yêu cầu quay +3°/s. Nếu gyro vẫn báo −8°/s, Inner phải xử lý sai số **+11°/s**, vì thân đang quay ngược với tốc độ được yêu cầu.

## 2. Ký hiệu và đơn vị

Từ đây dùng dấu chấm thập phân giống code: `0.005` nghĩa là 0,005.

| Ký hiệu | Ý nghĩa | Đơn vị |
| --- | --- | --- |
| `k` | Chu kỳ tính hiện tại | — |
| `dt` | Thời gian từ mẫu trước đến mẫu này | giây |
| `θcmd` | Góc người dùng yêu cầu trong ANGLE | ° |
| `θ` | Góc đã qua ước lượng và bù mốc cân bằng | ° |
| `ωcmd` | Tốc độ người dùng yêu cầu trong RATE | °/s |
| `ωsp` | Tốc độ mà PID tốc độ cần bám | °/s |
| `ω` | Tốc độ quay đo từ gyro, đã bù bias và đổi trục | °/s |
| `eθ` | Sai số góc: `θcmd − θ` | ° |
| `eω` | Sai số tốc độ: `ωsp − ω` | °/s |
| `u` | Phần sửa đầu ra PID tốc độ | duty |
| `B` | Base duty chung | duty |
| `s` | Motor sign, bằng +1 hoặc −1 | — |

Trong tài liệu, **duty** là đơn vị số đếm PWM 8-bit, không phải phần trăm và cũng không phải lực nâng đo được.

Để viết gọn, định nghĩa:

```text
clamp(x, min, max) = giữ x trong đoạn [min, max]

clamp(25, −20, +20) = 20
clamp(−7, −20, +20) = −7
```

## 3. Số đo đưa vào PID lấy từ đâu?

Firmware tính gần 200 Hz, tương ứng một chu kỳ danh định:

```text
dt = 1 / 200 = 0.005 giây = 5 ms
```

Nhưng code dùng **thời gian đo thực giữa hai mẫu**, không luôn ép `dt = 0.005`:

```cpp
dt = lastSample ? double(now-lastSample)*1e-6 : .005;
```

`now` tính bằng microsecond; nhân `1e-6` để đổi sang giây. Trong `Controller::step()`, `dt` phải nằm trong đoạn **0.002–0.020 giây**, nếu không sẽ báo lỗi `TIMING`.

Với quy ước hiện tại:

```text
Roll rate  = −(gyroY − biasY)
Pitch rate = +(gyroX − biasX)

Góc đưa vào PID = góc body từ bộ ước lượng − offset đã lấy mốc
```

Ví dụ `gyroY = 10°/s`, `biasY = 2°/s` thì Roll rate là `−8°/s`.

**Góc không phải rate.** Một thân đang đứng yên ở −3° có thể có rate gần 0°/s. RATE target = 0 không tự yêu cầu thân trở về góc 0°; ANGLE target = 0 mới tạo yêu cầu đó.

## 4. P: sửa theo sai số hiện tại

Công thức P chung:

```text
P = Kp × error
```

Với vòng tốc độ:

```text
eω = ωsp − ω
Pω = kp × eω
```

Nếu `ωsp = +3°/s`, `ω = −8°/s`, `kp = 0.2`:

```text
eω = 3 − (−8) = 11°/s
Pω = 0.2 × 11 = 2.2 duty
```

P tăng ngay khi sai số tăng. Nhưng chỉ có P thì khi sai số về 0, P cũng về 0; nó không lưu lại mức bù cần thiết để chống một mô-men lệch kéo dài.

Không hiểu `Kp = 0.2` là “motor tăng 20%”. Trong ví dụ này, nó tạo **2.2 duty trước mixer**, rồi mới qua sign, giới hạn và làm tròn.

## 5. I: tích lũy sai số qua thời gian

### 5.1. Code lưu trực tiếp phần đóng góp I

Công thức thường được học là:

$$I(t)=K_i\int e(t)\,dt$$

Trong code, biến tích phân đã chứa hệ số `Ki`. Vì vậy cập nhật là:

```text
I mới dự kiến = I cũ + Ki × sai số trung bình × dt
```

Sau đó **không nhân Ki thêm lần nữa** khi cộng `P + I + D`.

### 5.2. Từ chu kỳ thứ hai: tích phân hình thang

Code lấy trung bình sai số hiện tại và sai số trước:

$$\bar e_k=\frac{e_k+e_{k-1}}{2}$$

$$I_{candidate}=\operatorname{clamp}\left(I_{k-1}+K_i\bar e_k\,dt,-L_I,+L_I\right)$$

Trong vòng tốc độ, `LI = iLimit`. Lần tính đầu sau reset chưa có sai số trước, nên dùng `ē = e`.

Ví dụ:

```text
I cũ = 0.00825 duty
Ki = 0.15
Sai số trước = 11°/s
Sai số hiện tại = 8.03°/s
dt = 0.005 s

Sai số trung bình = (11 + 8.03) / 2 = 9.515°/s
Phần I tăng thêm = 0.15 × 9.515 × 0.005 = 0.00713625 duty
I dự kiến = 0.00825 + 0.00713625 = 0.01538625 duty
```

Với `iLimit = 10`, giá trị này nằm trong [−10, +10]. Tuy nhiên, **nằm trong giới hạn I chưa đủ để được nhận**: còn phải qua điều kiện chống windup ở mục 10.

### 5.3. Vì sao I trông tăng rất ít trong một mẫu?

Một mẫu chỉ dài 5 ms. Nếu sai số luôn là 10°/s, `Ki = 0.15`, và không bị chặn tích phân:

```text
Mỗi 5 ms: ΔI = 0.15 × 10 × 0.005 = 0.0075 duty
Sau 1 giây: I tăng khoảng 0.15 × 10 × 1 = 1.5 duty
```

I tác động theo thời gian. Khi đang có I khác 0, sai số tức thời bằng 0 không làm I tự biến mất; I có thể giữ mức bù đã tích lũy.

## 6. D: theo sự thay đổi của số đo, có lọc

### 6.1. D trong code không lấy trực tiếp đạo hàm sai số

Trong vòng tốc độ:

$$d_{raw,k}=-\frac{\omega_k-\omega_{k-1}}{dt}$$

Dấu trừ nằm ngay trong công thức. Nếu rate đo tăng nhanh theo chiều dương, `d_raw` âm; nếu rate đo giảm nhanh, `d_raw` dương.

Trong vòng góc:

$$d_{\theta,raw,k}=-\frac{\theta_k-\theta_{k-1}}{dt}$$

Đây là **derivative on measurement**. Khi target thay đổi nhưng số đo chưa thay đổi, nhánh D không có cú nhảy trực tiếp chỉ vì target nhảy. P vẫn đổi và I có thể tiếp tục cập nhật.

### 6.2. D thô được đưa qua bộ lọc thấp bậc một

Code tính:

$$\tau=\frac{1}{2\pi f_D},\qquad \alpha=\frac{dt}{dt+\tau}$$

$$d_{f,k}=d_{f,k-1}+\alpha(d_{raw,k}-d_{f,k-1})$$

$$D_k=K_d\,d_{f,k}$$

Trong source, `fD = dHz`. Hai vòng dùng cùng giá trị cấu hình `dHz`, nhưng có trạng thái lọc riêng.

Với `dHz = 10 Hz`, `dt = 0.005 s`:

```text
τ = 1 / (2π × 10) ≈ 0.015915494 s
α = 0.005 / (0.005 + 0.015915494)
  ≈ 0.239057224
```

Nếu rate trước là −8°/s và rate mới là −5°/s:

```text
d_raw = −[−5 − (−8)] / 0.005 = −600°/s²

Giả sử d_f cũ = 0:
d_f mới = 0 + 0.239057224 × (−600 − 0)
        ≈ −143.434334°/s²

Với Kd = 0.001:
D = 0.001 × (−143.434334) ≈ −0.143434 duty
```

**Không được dùng −0.6 duty** trong ví dụ này: đó là kết quả nhân Kd với D thô và đã bỏ qua bộ lọc.

Lần tính đầu sau reset, code cho D thô bằng 0 vì chưa có mẫu trước. Các trạng thái lọc cũng bắt đầu bằng 0.

### 6.3. D âm trong ví dụ này có ý nghĩa gì?

Rate đang tăng từ −8 lên −5°/s, tức chuyển động âm đang được hãm. Nhánh D tạo tác động âm, làm giảm bớt lệnh dương từ P và I để hạn chế thay đổi quá gắt.

Không kết luận chiều tăng/giảm motor chỉ từ dấu D. Cần cộng đủ P/I/D, giới hạn đầu ra rồi nhân Motor sign.

## 7. Công thức đầy đủ của vòng góc — Outer

Trong ANGLE, đặt:

```text
eθ = θcmd − θ
Pθ = angleKp × eθ
Dθ = angleKd × đạo hàm góc đã lọc
```

I góc được tính hình thang như mục 5, nhưng dùng `angleKi` và giới hạn **±rateLimit**:

$$I_{\theta,candidate}=\operatorname{clamp}\left(I_{\theta,old}+angleKi\,\bar e_\theta\,dt,-rateLimit,+rateLimit\right)$$

Sau khi quyết định có nhận I dự kiến hay không:

$$\omega_{sp}=\operatorname{clamp}\left(P_\theta+I_\theta+D_\theta,-rateLimit,+rateLimit\right)$$

**Đầu ra Outer là tốc độ quay, không phải duty.**

Nếu Angle = `1 / 0 / 0`, target = 0°, góc đo = −3°:

```text
eθ = 0 − (−3) = +3°
Pθ = 1 × 3 = +3°/s
Iθ = Dθ = 0
ωsp = clamp(3, −20, +20) = +3°/s
```

Nếu góc đo là −30°, vẫn với target 0° và `rateLimit = 20`:

```text
Pθ = +30°/s
ωsp = clamp(30, −20, +20) = +20°/s
```

Đây là lý do target rate có thể đứng ở +20°/s trong khi measured rate vẫn âm: **Outer đã yêu cầu tốc độ dương tối đa cho phép; chuyển động thật chưa bám được yêu cầu đó.** Không có công thức nào ép gyro lập tức bằng target.

## 8. Công thức đầy đủ của vòng tốc độ — Inner

Đầu vào `ωsp` lấy từ Outer khi chạy ANGLE; lấy trực tiếp từ command khi chạy RATE.

$$e_\omega=\omega_{sp}-\omega$$

$$P_\omega=kp\,e_\omega$$

$$I_{\omega,candidate}=\operatorname{clamp}\left(I_{\omega,old}+ki\,\bar e_\omega\,dt,-iLimit,+iLimit\right)$$

$$D_\omega=kd\,d_{\omega,f}$$

Sau điều kiện nhận I:

$$u_{raw}=P_\omega+I_\omega+D_\omega$$

$$u=\operatorname{clamp}(u_{raw},-outputLimit,+outputLimit)$$

`outputLimit` giới hạn phần sửa duty của PID, **không giới hạn trực tiếp tốc độ thật của drone**.

Ví dụ `outputLimit = 20`, `u_raw = 35` thì `u = 20`. Nhưng rate đo vẫn có thể lớn hơn 20°/s vì đây là hai đại lượng khác đơn vị.

## 9. Từ đầu ra PID đến bốn motor

Thứ tự motor cố định trong code là **LF, RF, LR, RR**: trước trái, trước phải, sau trái, sau phải.

### 9.1. Chỉ Roll

Đặt `r = motorSign × u`:

```text
LF = B + r       RF = B − r
LR = B + r       RR = B − r
```

Ví dụ `B = 110`, `u = +2.20825`, Roll sign = −1:

```text
r = −1 × 2.20825 = −2.20825

LF = LR = 110 − 2.20825 = 107.79175
RF = RR = 110 + 2.20825 = 112.20825
```

Sau giới hạn và làm tròn, lệnh là **LF=108, RF=112, LR=108, RR=112**.

### 9.2. Chỉ Pitch

Đặt `p = motorSign × u` của Pitch:

```text
LF = B + p       RF = B + p
LR = B − p       RR = B − p
```

Nếu `B=110`, `u=3`, Pitch sign = +1 thì hai motor trước nhận 113, hai motor sau nhận 107.

### 9.3. Cả Roll và Pitch

Hai PID độc lập tạo `uRoll`, `uPitch`; mỗi trục có sign riêng:

```text
r = rollSign × uRoll
p = pitchSign × uPitch

LF = B + r + p
RF = B − r + p
LR = B + r − p
RR = B − r − p
```

Ví dụ `B=110`, `uRoll=5`, Roll sign=−1, `uPitch=3`, Pitch sign=+1:

| Motor | Phép tính | Duty |
| --- | --- | ---: |
| LF | `110 − 5 + 3` | 108 |
| RF | `110 + 5 + 3` | 118 |
| LR | `110 − 5 − 3` | 102 |
| RR | `110 + 5 − 3` | 112 |

Hai cấu hình phải có cùng Base. Source hiện tại không có thành phần PID Yaw trong bộ trộn này.

### 9.4. Giới hạn và làm tròn

Mỗi motor được xử lý:

```text
duty = floor(clamp(mixed, 0, 255) + 0.5)
```

Đó là làm tròn gần nhất sau khi giới hạn. Ví dụ `107.79 → 108`, `112.21 → 112`.

Một thay đổi nhỏ của PID có thể chưa làm duty nguyên thay đổi. PWM cũng không tuyến tính trực tiếp với lực nâng; cùng lệnh có thể tạo lực khác khi điện áp pin hoặc tải thay đổi.

## 10. Chống windup: khi nào I được cập nhật?

Windup là việc I tiếp tục tích lũy trong lúc đầu ra đã không thể tăng thêm, khiến bộ điều khiển mất nhiều thời gian để hết lệnh dư khi trạng thái thay đổi.

Code có **hai lớp**: giới hạn giá trị I và kiểm tra điều kiện có nhận I mới hay không.

### 10.1. Điều kiện của Inner

Tính thử bằng I dự kiến:

```text
trial = P + I_candidate + D
```

I dự kiến chỉ được nhận nếu:

```text
Chu kỳ trước mixer không bị giới hạn
VÀ
(
    |trial| ≤ outputLimit
    HOẶC error × trial < 0
)
```

Nếu không đạt, giữ nguyên I cũ. `error × trial < 0` nghĩa là sai số đang ngược dấu tổng lệnh dự kiến; cập nhật I lúc này có thể giúp rút bớt lệnh dư.

**Điểm cần đọc đúng:** nếu mixer chu kỳ trước bị giới hạn, Inner giữ I cũ ngay cả khi nhánh `error × trial < 0` đúng. Đây là hành vi của code hiện tại, không phải mọi thuật toán anti-windup đều giống nhau.

### 10.2. Điều kiện của Outer

Outer nhận I dự kiến nếu:

```text
(
    Chu kỳ trước Inner không bị giới hạn
    VÀ chu kỳ trước mixer không bị giới hạn
    VÀ |trial góc| ≤ rateLimit
)
HOẶC error góc × trial góc < 0
```

Khác Inner, nhánh cho phép rút I của Outer nằm ngoài nhóm kiểm tra các cờ giới hạn. Vì vậy không được gom hai điều kiện này thành một công thức giống hệt nhau.

Các cờ lấy từ **chu kỳ trước**: đó là thông tin phản hồi có sẵn khi chu kỳ hiện tại đang tính. Trong chế độ hai trục, cờ bão hòa của mixer chung được phản hồi về cả hai controller.

## 11. Ví dụ đầy đủ: ANGLE Roll từ góc −3°

### 11.1. Dữ liệu ban đầu

Dùng các giá trị bạn từng thử làm ví dụ:

| Thông số | Giá trị |
| --- | ---: |
| Rate Kp / Ki / Kd | 0.2 / 0.15 / 0.001 |
| Angle Kp / Ki / Kd | 1 / 0 / 0 |
| Base | 110 duty |
| Roll Motor sign | −1 |
| Output limit / I limit | 20 / 10 duty |
| Rate limit | 20°/s |
| D filter | 10 Hz |
| `dt` | 0.005 s |
| Góc mục tiêu | 0° |
| Góc đo ban đầu | −3° |
| Rate đo ban đầu | −8°/s |

Giả sử vừa reset PID nên I, trạng thái lọc và các cờ giới hạn trước đó bằng 0/false. Trong ba chu kỳ ví dụ không có bão hòa.

### 11.2. Chu kỳ 0: tính Outer

```text
eθ = 0 − (−3) = 3°
Pθ = 1 × 3 = 3°/s
Iθ = Dθ = 0
ωsp = 3°/s
```

### 11.3. Chu kỳ 0: tính Inner

```text
eω = 3 − (−8) = 11°/s
P = 0.2 × 11 = 2.2 duty

Lần tính đầu: sai số trung bình = 11
I_candidate = 0 + 0.15 × 11 × 0.005 = 0.00825 duty

Lần tính đầu: D thô = 0, D lọc = 0, D = 0
trial = 2.2 + 0.00825 + 0 = 2.20825 duty
```

Mixer trước không bị giới hạn và `|trial| < 20`, nên nhận I mới:

```text
I = 0.00825
u_raw = 2.20825
u = clamp(2.20825, −20, +20) = 2.20825 duty
```

### 11.4. Chu kỳ 0: trộn Roll

```text
r = −1 × 2.20825 = −2.20825
Trái = 107.79175 → 108 duty
Phải = 112.20825 → 112 duty
```

Đây là lệnh giảm trái/tăng phải theo sign đang chọn. Chiều tác động vật lý phải phù hợp với quy ước rate thực tế; công thức mixer chỉ chứng minh chiều của **lệnh**.

### 11.5. Chu kỳ 1: gyro báo −5°/s, góc là −3.03°

Các số đo tiếp theo được giả định để minh họa, không phải kết quả mô phỏng động lực học.

```text
Outer:
ωsp = 1 × [0 − (−3.03)] = 3.03°/s

Inner:
eω = 3.03 − (−5) = 8.03°/s
P = 0.2 × 8.03 = 1.606 duty

I tăng = 0.15 × [(11 + 8.03)/2] × 0.005
       = 0.00713625 duty
I = 0.00825 + 0.00713625 = 0.01538625 duty

D thô = −[−5 − (−8)]/0.005 = −600°/s²
D lọc = 0 + 0.239057224 × (−600 − 0)
      ≈ −143.434334°/s²
D = 0.001 × (−143.434334) ≈ −0.143434 duty

u = 1.606 + 0.01538625 − 0.143434334
  ≈ 1.477951916 duty

Trái = 110 − 1.477951916 → 109
Phải = 110 + 1.477951916 → 111
```

### 11.6. Chu kỳ 2 và bảng tổng hợp

Giả sử tiếp theo góc đo −3.05°, rate đo −2°/s. Khi đó:

```text
ωsp = 3.05°/s
eω = 3.05 − (−2) = 5.05°/s
P = 1.01 duty

I tăng = 0.15 × [(8.03 + 5.05)/2] × 0.005 = 0.004905
I = 0.01538625 + 0.004905 = 0.02029125 duty

D thô = −[−2 − (−5)]/0.005 = −600°/s²
D lọc = −143.434334 + 0.239057224 × [−600 − (−143.434334)]
      ≈ −252.579655°/s²
D ≈ −0.252579655 duty

u = 1.01 + 0.02029125 − 0.252579655
  ≈ 0.777711595 duty
```

| Chu kỳ | Góc đo ° | Rate đo °/s | Target rate °/s | P | I | D | u duty | LF/LR | RF/RR |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | −3.00 | −8 | 3.00 | 2.200000 | 0.008250 | 0.000000 | 2.208250 | 108 | 112 |
| 1 | −3.03 | −5 | 3.03 | 1.606000 | 0.015386 | −0.143434 | 1.477952 | 109 | 111 |
| 2 | −3.05 | −2 | 3.05 | 1.010000 | 0.020291 | −0.252580 | 0.777712 | 109 | 111 |

Điểm đáng chú ý:

- Rate âm đang giảm về gần 0, nên sai số tốc độ và P giảm.
- I vẫn tăng vì sai số tốc độ còn dương.
- D vẫn âm và độ lớn có thể tiếp tục tăng vì bộ lọc chưa theo kịp D thô.
- Hai chu kỳ cuối cho cùng duty nguyên, dù `u` khác nhau, vì có làm tròn.
- Góc vẫn âm thêm một chút vì thân vẫn đang có rate âm. **Hãm quay trước rồi mới quay về** là hai giai đoạn khác nhau; không đánh giá kết quả chỉ bằng hai mẫu góc này.

## 12. Cùng trạng thái đó nhưng chạy RATE target = 0 thì sao?

Vẫn dùng góc −3°, rate −8°/s và PID vừa reset, nhưng chuyển sang RATE target = 0:

```text
Không chạy Outer.
ωsp = 0°/s
eω = 0 − (−8) = 8°/s
P = 0.2 × 8 = 1.6 duty
I = 0.15 × 8 × 0.005 = 0.006 duty
D = 0 trong mẫu đầu
u = 1.606 duty
```

Nếu đã đứng yên ở −3°, rate = 0, vừa reset và không có nhiễu:

```text
eω = 0
P = I = D = 0
u = 0
Bốn motor nhận Base
```

RATE không thấy góc −3° là một lỗi cần sửa. Vì vậy “đứng yên nhưng không nằm ngang” không tự chứng minh Rate PID sai. ANGLE mới sử dụng góc để yêu cầu quay về.

## 13. Nếu thêm Ki và Kd cho Outer thì tính thế nào?

Một ví dụ riêng, không tiếp tục trạng thái của bảng trên:

```text
Angle Kp/Ki/Kd = 1 / 0.2 / 0.01
Target góc = 0°
Góc trước = −3.1°, góc hiện tại = −3°
I góc cũ = 0.4°/s
Trạng thái lọc đạo hàm góc cũ = 0
dt = 0.005 s, dHz = 10 Hz
Không có cờ giới hạn trước đó
```

Tính từng phần:

```text
eθ trước = 3.1°, eθ hiện tại = 3°
Pθ = 1 × 3 = 3°/s

Iθ tăng = 0.2 × [(3.1 + 3)/2] × 0.005 = 0.00305°/s
Iθ dự kiến = 0.4 + 0.00305 = 0.40305°/s

Dθ thô = −[−3 − (−3.1)]/0.005 = −20°/s
Dθ lọc = 0 + 0.239057224 × (−20) ≈ −4.781144°/s
Dθ = 0.01 × (−4.781144) ≈ −0.047811°/s

trial góc = 3 + 0.40305 − 0.047811445
          ≈ 3.355238555°/s
```

Với `rateLimit=20`, tổng còn trong giới hạn và không có cờ chặn, nên nhận I mới. Inner sẽ nhận **target rate ≈ 3.355239°/s**, không nhận riêng Pθ=3.

Trong ví dụ này, `angleKd` nhân đạo hàm **góc**, còn `kd` của Inner nhân đạo hàm **rate**. Không sao chép hai hệ số cho nhau chỉ vì cùng tên Kd.

## 14. Ví dụ PID chạm Output limit

Xét RATE target = 0, rate đo = −150°/s, `kp=0.2`, `ki=0.15`, `kd=0.001`, lần tính đầu sau reset:

```text
eω = 150°/s
P = 0.2 × 150 = 30 duty
I_candidate = 0.15 × 150 × 0.005 = 0.1125 duty
D = 0
trial = 30.1125 duty
```

Với Output limit = 20:

- `|trial| > 20`: không thể nhận I theo nhánh trong giới hạn.
- `error × trial > 0`: I đang muốn đẩy cùng chiều tổng lệnh, không phải rút lệnh dư.
- Vì vậy **giữ I cũ = 0**.

Kết quả thực:

```text
u_raw = 30 + 0 + 0 = 30 duty
u = clamp(30, −20, +20) = 20 duty
pidLimited = true
```

Nếu Base=110, Roll sign=−1 thì trái=90, phải=130. Mixer vẫn trong 0–255, do đó **PID bị giới hạn nhưng mixer không bị giới hạn**.

Source không dùng `maxRate` để cắt motor vì measured rate vượt 150°/s. Ví dụ trên giải thích thuật toán, không phải đề nghị tạo tốc độ đó để thử phần cứng.

## 15. Ví dụ I chạm I limit và ví dụ I rút bớt

### 15.1. Chạm I limit

Giả sử `I cũ=9.999`, sai số trước và hiện tại đều là 5°/s:

```text
I chưa clamp = 9.999 + 0.15 × 5 × 0.005 = 10.00275
I_candidate = clamp(10.00275, −10, +10) = 10
```

Nếu P=1, D=0, mixer trước không giới hạn và Output limit=20:

```text
trial = 1 + 10 = 11
I mới = 10 duty
u = 11 duty
```

I không vượt 10 dù sai số còn dương. I limit giới hạn **riêng I**, không giới hạn tổng P+I+D ở 10.

### 15.2. Rút I dù tổng vẫn ngoài giới hạn

Ví dụ riêng: `I cũ=10`, `error trước=error mới=−1`, `P=−0.2`, `D=+15` và mixer trước không giới hạn:

```text
I_candidate = 10 + 0.15 × (−1) × 0.005 = 9.99925
trial = −0.2 + 9.99925 + 15 = 24.79925
```

Tổng còn vượt +20, nhưng `error × trial = −24.79925 < 0`. Code nhận I mới vì sai số âm đang giúp giảm phần tích lũy dương.

Đây là ví dụ về một trạng thái trong quá trình chạy, không phải chu kỳ đầu sau reset. Nếu mixer trước bị giới hạn thì điều kiện Inner vẫn giữ I cũ như đã nêu ở mục 10.

## 16. Ví dụ mixer chạm giới hạn khi chạy hai trục

Giả sử sau nhân sign đã có `r=25`, `p=25`, Base=220. Mỗi trục có Output limit đủ lớn để nhận phần sửa này, ví dụ 40.

Nếu xét riêng từng trục, lệnh motor chỉ nằm giữa 195 và 245. Nhưng khi cộng cả hai:

```text
LF = 220 + 25 + 25 = 270 → bị cắt về 255
RF = 220 − 25 + 25 = 220
LR = 220 + 25 − 25 = 220
RR = 220 − 25 − 25 = 170
```

Kết quả `mixerLimited=true` cho cả hai controller. Đây là ví dụ toán học với Base cao, không phải cấu hình khuyến nghị.

Điều đó cho thấy: **mỗi PID chưa chạm limit riêng không bảo đảm mixer chung còn đủ khoảng điều khiển**. Code hiện giới hạn từng motor, chưa thực hiện desaturation để phân bổ lại toàn bộ bốn lệnh và giữ nguyên tỷ lệ mô-men.

## 17. Những giới hạn nào đang tồn tại trong code?

Các giá trị sau lấy từ `Config` và điều kiện kiểm tra của controller, không suy từ tài liệu DroneMotion cũ:

| Tham số | Mặc định trong `Config` | Phạm vi/ý nghĩa hiện tại |
| --- | ---: | --- |
| Rate Kp / Ki / Kd | 0 / 0 / 0 | Kp: 0–5; Ki: 0–2; Kd: 0–0.2 |
| Angle Kp / Ki / Kd | 0 / 0 / 0 | Kp: 0–5; Ki: 0–2; Kd: 0–1 |
| `base` | 80 | 1–220 duty |
| `motorSign` | +1 | Chỉ +1 hoặc −1 |
| `outputLimit` | 20 | Lớn hơn 0, tối đa 100 duty; đồng thời phải ≥ `iLimit` |
| `iLimit` | 10 | Từ 0 đến `outputLimit` |
| `rateLimit` | 20 | Lớn hơn 0, tối đa 45°/s |
| `dHz` | 10 | Lớn hơn 0, tối đa 50 Hz |
| ANGLE command | — | Góc yêu cầu trong ±10° |
| RATE command | — | Tốc độ yêu cầu trong ±`rateLimit` |
| Duty motor sau mixer | — | 0–255 |

Kp=0 có thể tồn tại trong cấu hình mặc định, nhưng controller không chạy PID với Inner Kp≤0. ANGLE còn yêu cầu Angle Kp>0, nếu không báo `GAINS_ZERO`.

`CONFIG_CASCADE` hiện nhận các gain, Base, Motor sign và Output limit; không có đối số riêng cho `iLimit`, `rateLimit`, `dHz`. Với I limit mặc định 10, Output limit qua cấu hình hiện tại phải từ **10 đến 100** để hợp lệ. Không hiểu “code cho rateLimit tối đa 45” là app hiện có lệnh để đổi nó.

`maxAngle`, `maxRate`, `saturationSeconds`, `saturationError` còn được giữ cho tương thích cấu hình mô phỏng cũ; trong controller hiện tại chúng **không dùng để tự cắt motor khi vượt góc, vượt rate hoặc bão hòa kéo dài**.

Giới hạn command góc ±10° cũng không có nghĩa góc đo thật được giữ chắc trong ±10°.

Một giới hạn kiến trúc khác: Outer hiện lấy trực tiếp `command − angle`, không có phép wrap sai số góc hoặc bộ điều khiển quaternion trong hàm PID này. Gyro cung cấp rate theo trục body; khi thân nghiêng lớn và quay phối hợp, rate body không đồng nhất với đạo hàm từng góc Euler. Vì vậy các ví dụ một trục gần ngang ở đây không chứng minh đáp ứng ở mọi tư thế ba chiều.

## 18. Khi nào trạng thái I và D bị xóa?

`Controller::reset()` đưa trạng thái controller về ban đầu: I=0, bộ lọc D=0, chưa có mẫu trước, các cờ trước=false. `DualController::reset()` làm tương tự cho cả Roll và Pitch.

Firmware gọi reset khi ARM, START/START_BOTH thành công, đổi trục, áp dụng cấu hình và các đường dừng/lỗi tương ứng. Vì thế **không cần ngắt pin thì I mới mất**; một phiên START mới đã bắt đầu lại trạng thái PID.

Trong RATE, trạng thái Outer được xóa và không dùng để tính target rate. Mode `DryRun` cũng đi theo nhánh target rate trực tiếp trong controller; firmware không cho xuất motor khi chạy mode này.

Với chu kỳ đầu sau reset, phải dùng quy tắc “I lấy sai số hiện tại, D thô bằng 0”. Nếu lấy công thức hai mẫu nhưng tự đặt số đo trước bằng 0, kết quả sẽ khác code.

## 19. Đọc các con số trên log như thế nào?

| Đại lượng trong controller | Phải hiểu là |
| --- | --- |
| `command` / trường target người dùng | Góc ° khi ANGLE; rate °/s khi RATE |
| `output.target` | Target rate °/s mà Inner đang bám, kể cả khi đang ANGLE |
| `output.error` | Sai số rate `target rate − measured rate` |
| `output.p`, `.i`, `.d` | Các phần đóng góp của **Inner**, đơn vị duty |
| `output.raw` | Tổng Inner P+I+D, trước Output limit |
| `output.u` | Phần sửa duty sau Output limit, trước Motor sign |
| `pidLimited` | `abs(raw) >= outputLimit` |
| `mixerLimited` | Có lệnh motor thô <0 hoặc >255 |
| LF/RF/LR/RR | Lệnh duty; không phải lực hoặc RPM đo được |

Một chi tiết biên: nếu `raw` bằng đúng Output limit, `pidLimited` đã true do dùng `>=`, dù clamp chưa làm đổi giá trị. Điều kiện nhận I trong nhánh thông thường lại dùng `<=`. Hai phép so sánh này không mâu thuẫn; chúng phục vụ hai việc khác nhau.

Khi chạy cả Roll + Pitch, telemetry PID chính hiện được lấy từ **Roll**. Không dùng các trường P/I/D đó như thể chúng đồng thời là PID chi tiết của Pitch. Các góc và duty chung vẫn được gửi theo định dạng của firmware.

Các dòng log có thể được gửi thưa hơn nhịp tính PID. Không lấy khoảng thời gian giữa hai dòng CSV làm `dt` của controller nếu giữa chúng đã có các mẫu PID không được gửi.

## 20. Những nhầm lẫn dễ dẫn đến chỉnh sai

| Nhầm lẫn | Cách đọc đúng trong DroneRebuild |
| --- | --- |
| Base 110 là 110% công suất | Là 110/255, khoảng 43.14% duty PWM; không đồng nghĩa 43.14% lực nâng |
| Target rate +20 thì drone phải quay +20 ngay | Là yêu cầu của controller; rate đo có thể chưa bám hoặc vẫn ngược dấu |
| Output +5 thì bên trái luôn tăng | Còn phụ thuộc trục và Motor sign |
| Rate target 0 tự đưa góc về 0 | RATE chỉ yêu cầu dừng quay; ANGLE mới dùng sai số góc |
| D = Kd × đạo hàm error | Code dùng âm đạo hàm số đo, rồi lọc thấp |
| I mới = Ki × I đã lưu | Biến I đã chứa Ki; nhân thêm sẽ sai |
| PID limit và mixer limit là một | Một giới hạn tổng sửa duty, một giới hạn lệnh motor sau trộn |
| I luôn tăng theo mỗi vòng lặp | Có chống windup; khi điều kiện không đạt thì giữ I cũ |
| Tăng duty sẽ xử lý mọi lỗi bám góc | Nguồn, lực thực, chiều điều khiển và giới hạn phối trộn vẫn có thể là nguyên nhân |

## 21. Bảng công thức để tự tính lại một mẫu

Thực hiện theo đúng thứ tự này:

```text
1. Xác định mode và dt thực.

2. Nếu ANGLE:
   eθ = command góc − góc đo
   Tính Pθ, Iθ dự kiến, Dθ thô → lọc → Dθ
   Kiểm tra anti-windup Outer
   rate target = clamp(Pθ + Iθ + Dθ, ±rateLimit)

   Nếu RATE:
   rate target = command rate

3. error rate = rate target − rate đo
4. P = kp × error rate
5. I_candidate = clamp(I cũ + ki × error trung bình × dt, ±iLimit)
6. D thô = −(rate mới − rate trước)/dt; mẫu đầu dùng 0
7. D lọc = D lọc cũ + α × (D thô − D lọc cũ)
8. D = kd × D lọc
9. Kiểm tra anti-windup Inner để chọn I mới hoặc giữ I cũ
10. raw = P + I + D
11. u = clamp(raw, ±outputLimit)
12. Nhân Motor sign của từng trục, trộn với Base
13. Giới hạn từng motor 0–255, rồi floor(x+0.5)
14. Lưu số đo, error và các cờ cho chu kỳ sau
```

Để kiểm tra một phép tính, hãy ghi đủ **mode, command, góc, rate, dt, gain, các giới hạn và trạng thái I/D trước đó**. Chỉ có ba gain và một góc đo sẽ chưa tính lại chính xác được một mẫu giữa phiên.

Đọc tiếp: [giao thức hiện tại](PROTOCOL.md), [hướng dẫn đọc firmware](DroneRebuild_Firmware_Guide.md), [PID nền tảng](../theory/PID-controller.md), [mục lục tài liệu](../README.md).
