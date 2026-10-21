# PID Controller: giải thích từ cơ bản đến ứng dụng trên mini drone

Tài liệu này dành cho người mới học điều khiển tự động. Bạn có thể đọc phần trực giác trước, sau đó xem công thức, ví dụ tính toán và cách triển khai trong simulator của dự án.

## 1. PID giải quyết vấn đề gì?

Giả sử bạn muốn drone giữ góc nghiêng roll bằng **0°**, tức là thân drone nằm ngang theo trục roll. Một luồng gió khiến drone nghiêng sang **10°**. Bộ điều khiển cần liên tục:

1. Đọc góc hiện tại từ hệ thống ước lượng trạng thái.
2. So sánh với góc mong muốn.
3. Tính mức điều chỉnh.
4. Phân phối lệnh đến các motor để đưa drone về góc mong muốn.
5. Lặp lại vì drone vẫn chuyển động và nhiễu vẫn xuất hiện.

PID là một cách tính mức điều chỉnh ở bước 3. Tên PID gồm:

- **P — Proportional:** phản ứng theo sai lệch hiện tại.
- **I — Integral:** cộng dồn sai lệch theo thời gian.
- **D — Derivative:** phản ứng theo tốc độ thay đổi của sai lệch.

Một cách ghi nhớ: **P nhìn hiện tại, I nhớ quá khứ, D nhìn xu hướng chuyển động.** D không biết chính xác tương lai; nó dùng tốc độ thay đổi hiện tại để giúp giảm phản ứng quá đà.

## 2. Vòng điều khiển phản hồi

```text
Giá trị mong muốn r ──► [So sánh: e = r - y] ──► [PID] ──► Lệnh u
                                  ▲                          │
                                  │                          ▼
                            Giá trị đo y ◄── [Cảm biến] ◄── [Hệ vật lý]
```

| Ký hiệu | Ý nghĩa | Ví dụ khi điều khiển góc roll |
|---|---|---|
| `r`, setpoint | Giá trị mong muốn | Góc roll cần giữ: 0° |
| `y`, measurement | Giá trị đo hoặc ước lượng | Góc roll hiện tại: 10° |
| `e = r - y` | Sai lệch | `0 - 10 = -10°` |
| `u`, output | Lệnh điều khiển | Mức hiệu chỉnh để tạo mô men quay |
| `dt` | Thời gian giữa hai lần cập nhật | `0.002 s` tương ứng 500 Hz |

Dấu âm trong ví dụ có nghĩa cần tác động theo chiều làm giảm góc roll, với điều kiện quy ước dấu của cảm biến, bộ điều khiển và cơ cấu chấp hành thống nhất.

**PID không tự biết chiều quay motor.** Nếu lệnh hiệu chỉnh làm sai lệch tăng lên, hệ sẽ trở thành phản hồi dương và có thể mất ổn định dù hệ số PID nhỏ.

## 3. Thành phần P: lệch càng nhiều, sửa càng mạnh

Công thức:

```text
P(t) = Kp × e(t)
```

Ví dụ minh họa với `Kp = 0.5`:

| Sai lệch e | Đóng góp P |
|---:|---:|
| 10 | 5 |
| 4 | 2 |
| 1 | 0.5 |
| 0 | 0 |

P giống như việc bạn thấy xe lệch khỏi giữa làn đường: lệch nhiều thì chỉnh vô lăng mạnh hơn, lệch ít thì chỉnh nhẹ hơn.

- `Kp` quá nhỏ: phản ứng yếu, trở về mục tiêu chậm.
- `Kp` lớn hơn: thường phản ứng nhanh hơn trong vùng làm việc phù hợp.
- `Kp` quá lớn: dễ vượt mục tiêu, dao động hoặc mất ổn định.

### Tại sao chỉ P có thể chưa đủ?

Giả sử có một tác động liên tục cần lệnh bù khác 0 để cân bằng. Nếu chỉ dùng P, lệnh bù đó đòi hỏi sai lệch khác 0 vì `P = Kp × e`. Hệ có thể dừng gần mục tiêu nhưng vẫn còn **sai lệch xác lập**.

Đây là tình huống thường gặp, không phải quy luật cho mọi hệ. Có hệ tự chứa cơ chế tích phân trong động lực học và có thể đạt sai lệch bằng 0 khi chỉ dùng P trong một số điều kiện.

## 4. Thành phần I: xử lý sai lệch nhỏ nhưng kéo dài

Công thức liên tục:

```text
I(t) = Ki × ∫ e(τ) dτ
```

Trong chương trình, ta cộng dồn từng khoảng thời gian nhỏ:

```text
I_mới = I_cũ + Ki × e × dt
```

Ví dụ: `e = 2`, `Ki = 0.1`, `dt = 0.1 s`. Mỗi lần cập nhật, I tăng:

```text
ΔI = 0.1 × 2 × 0.1 = 0.02
```

Nếu sai lệch giữ nguyên trong 10 lần cập nhật, tức 1 giây, I tăng tổng cộng `0.2`. Nhờ vậy, một sai lệch nhỏ tồn tại lâu vẫn tạo được lệnh bù đủ lớn.

Khi hệ đã đạt mục tiêu và `e = 0`, I **không tự trở về 0**. Nó giữ phần bù đã tích lũy, trừ khi có cơ chế reset hoặc xử lý khác. Đây chính là điều giúp hệ chống lại một tác động kéo dài.

- `Ki` quá nhỏ: loại bỏ sai lệch kéo dài chậm.
- `Ki` quá lớn: dễ tích lũy quá mức, gây vượt mục tiêu và dao động chậm.

### Integral windup: tích lũy khi đã hết khả năng điều khiển

Giả sử output chỉ được phép nằm trong `[-1, 1]`, nhưng PID đang yêu cầu `3`. Cơ cấu chấp hành chỉ thực hiện được `1`. Nếu I vẫn tăng, nó tích lũy một giá trị quá lớn dù hệ không thể đáp ứng thêm.

Khi sai lệch giảm hoặc đổi dấu, I còn lớn khiến output tiếp tục đẩy sai hướng quá lâu. Hiện tượng này gọi là **integral windup**.

Các cách xử lý thường gặp:

- Giới hạn đóng góp I trong một khoảng cho phép.
- Tạm ngừng tích phân khi output bão hòa và sai lệch đang đẩy nó bão hòa sâu hơn.
- Cho phép tích phân khi nó giúp kéo output ra khỏi bão hòa.
- Dùng back-calculation: phản hồi chênh lệch giữa lệnh yêu cầu và lệnh thực thi để điều chỉnh I.

Các cách trên là những lựa chọn thiết kế; không phải chương trình PID nào cũng dùng tất cả.

## 5. Thành phần D: giảm phản ứng quá đà

Công thức cơ bản:

```text
D(t) = Kd × de(t)/dt
```

Trong chương trình:

```text
D = Kd × (e_hiện_tại - e_trước) / dt
```

Giả sử mục tiêu cố định là 0°. Drone đang ở -5° và quay nhanh về 0°. Sai lệch vẫn dương, nên P vẫn yêu cầu quay theo chiều tăng góc. Tuy nhiên, góc đang tăng nhanh nên sai lệch đang giảm nhanh; D âm và làm giảm lực điều chỉnh tổng.

D tạo tác dụng giống giảm chấn: giúp hãm chuyển động trước khi vượt mục tiêu. Với mục tiêu cố định, nếu drone đang đi xa mục tiêu, D cũng có thể tăng tác động chống lại chuyển động đó.

- `Kd` phù hợp: có thể giảm vượt mục tiêu và dao động.
- `Kd` quá lớn: dễ khuếch đại nhiễu, làm lệnh motor rung hoặc khiến đáp ứng bị ghìm quá mạnh.

### Vì sao D nhạy với nhiễu?

D lấy chênh lệch hai mẫu rồi chia cho `dt`. Nếu cảm biến thay đổi do nhiễu một lượng `0.01` trong `0.001 s`, tốc độ thay đổi tính được là `10 đơn vị/s`, dù hệ vật lý có thể gần như đứng yên.

Vì vậy, nhánh D thường cần lọc thông thấp. Lọc mạnh giúp giảm nhiễu nhưng cũng làm phản ứng D trễ hơn.

### Đạo hàm trên giá trị đo để tránh derivative kick

Vì `e = r - y`:

```text
de/dt = dr/dt - dy/dt
```

Nếu setpoint đổi đột ngột, đạo hàm sai lệch có thể tạo một xung D lớn, gọi là **derivative kick**. Một cách triển khai phổ biến là lấy:

```text
D = -Kd × dy/dt
```

Khi setpoint cố định, cách này tương đương đạo hàm sai lệch. Khi setpoint thay đổi, nó bỏ qua đạo hàm của setpoint và chỉ phản ứng với chuyển động đo được. Simulator trong dự án dùng cách này.

## 6. Ghép P, I và D

Dạng PID song song với các hệ số độc lập:

```text
e(t) = r(t) - y(t)
u(t) = Kp × e(t) + Ki × ∫ e(τ) dτ + Kd × de(t)/dt
```

Trong dạng rời rạc cơ bản:

```text
e_k = r_k - y_k
P_k = Kp × e_k
I_k = I_(k-1) + Ki × e_k × dt
D_k = Kd × (e_k - e_(k-1)) / dt
u_k = P_k + I_k + D_k
```

Đây là công thức để hiểu nguyên lý. Khi triển khai thực tế còn cần giới hạn output, chống windup, lọc D và xử lý khởi tạo.

### Ví dụ tính một bước

Ví dụ dưới đây dùng **đạo hàm trên giá trị đo**, chưa lọc D và chưa chạm giới hạn:

```text
r = 10, y = 8             → e = 2
y_trước = 7.8
dt = 0.1 s
Kp = 0.5, Ki = 0.2, Kd = 0.1
I_trước = 0.3
```

Tính lần lượt:

```text
P = 0.5 × 2                          = 1.00
I = 0.3 + 0.2 × 2 × 0.1              = 0.34
D = -0.1 × (8 - 7.8) / 0.1           = -0.20
u = 1.00 + 0.34 - 0.20               = 1.14
```

Hệ đang tiến về mục tiêu nên D làm giảm lệnh điều khiển. Nếu output chỉ cho phép `[-1, 1]`, lệnh cuối cùng bị giới hạn còn `1`; cơ chế chống windup sẽ quyết định có nhận giá trị I mới hay không.

Các con số trên chỉ phục vụ tính toán minh họa, không phải bộ hệ số để nạp vào drone.

## 7. Vai trò của thời gian lấy mẫu và đơn vị

### `dt` phải tính bằng giây

Nếu vòng điều khiển chạy 500 Hz:

```text
dt = 1 / 500 = 0.002 s
```

Dùng nhầm `2` mili giây thành `2` giây khiến phần tăng I lớn gấp 1000 lần và đạo hàm nhỏ đi 1000 lần so với tính đúng.

Nên dùng thời gian thực giữa các mẫu. Vòng lặp ổn định giúp điều khiển dễ dự đoán hơn; đo `dt` đúng không loại bỏ được tác hại của trễ hoặc mất mẫu lớn.

### Độ và radian không thể thay thế trực tiếp

```text
180° = π rad
10° ≈ 0.1745 rad
```

Cùng một góc vật lý nhưng giá trị số khác nhau khoảng 57.3 lần. Vì vậy, hệ số được chỉnh cho độ không thể giữ nguyên khi đổi đầu vào sang radian.

Nếu đơn vị sai lệch là `E` và đơn vị output là `U`, thì:

| Hệ số | Đơn vị |
|---|---|
| `Kp` | `U / E` |
| `Ki` | `U / (E × s)` |
| `Kd` | `U × s / E` |

Ngoài đơn vị, cần kiểm tra dạng công thức. Một số tài liệu dùng `Kp × [e + (1/Ti)∫e dt + Td × de/dt]`; khi đó `Ki = Kp/Ti` và `Kd = Kp×Td`. Không sao chép hệ số giữa hai dạng mà chưa quy đổi.

## 8. PID trên drone: vòng góc và vòng tốc độ quay

Drone thường tách bài toán giữ tư thế thành hai vòng lồng nhau:

```text
Góc mong muốn
      │
      ▼
[Vòng ngoài: điều khiển góc] ◄── Góc ước lượng
      │
      ▼
Tốc độ quay mong muốn
      │
      ▼
[Vòng trong: điều khiển tốc độ quay] ◄── Tốc độ quay đo được
      │
      ▼
Lệnh hiệu chỉnh roll / pitch / yaw
      │
      ▼
[Motor mixer + throttle] ──► Lệnh từng motor ──► Drone
```

- **Vòng góc:** trả lời “cần quay nhanh bao nhiêu để đạt góc mong muốn?”.
- **Vòng tốc độ quay:** trả lời “cần tác động bao nhiêu để đạt tốc độ quay đó?”.
- **Motor mixer:** kết hợp hiệu chỉnh các trục với throttle để tính lệnh từng motor theo cấu hình khung và quy ước dấu.

Roll là nghiêng sang hai bên, pitch là chúi/ngẩng, yaw là quay hướng quanh trục thẳng đứng của thân theo quy ước hệ tọa độ đang dùng.

Vòng trong cần phản ứng nhanh hơn vòng ngoài. Khi chỉnh hệ số, thường ổn định vòng tốc độ quay trước, rồi mới chỉnh vòng góc.

PID giữ tư thế không đồng nghĩa giữ độ cao. Điều khiển độ cao cần thông tin trạng thái phù hợp và một vòng điều khiển riêng để điều chỉnh lực nâng.

## 9. Liên hệ với mã nguồn trong dự án

Các file liên quan:

- [PIDController.hpp](../Simulator/drone-simulation/include/PIDController.hpp): khai báo hệ số và trạng thái PID.
- [PIDController.cpp](../Simulator/drone-simulation/src/PIDController.cpp): công thức cập nhật PID.
- [FlightController.hpp](../Simulator/drone-simulation/include/FlightController.hpp): hệ số mặc định và các bộ điều khiển.
- [FlightController.cpp](../Simulator/drone-simulation/src/FlightController.cpp): nối các vòng điều khiển với motor mixer.

### 9.1. Các tham số của `PIDGains`

| Tham số | Ý nghĩa trong mã hiện tại |
|---|---|
| `kp`, `ki`, `kd` | Hệ số P, I, D |
| `limit` | Giới hạn độ lớn output, tức `[-limit, limit]` |
| `integralLimit` | Giới hạn đóng góp I đã nhân `ki` |
| `derivativeHz` | Tần số cắt dùng để tính bộ lọc thông thấp bậc một cho đạo hàm |

`integral_` trong lớp lưu **đóng góp I vào output**, không phải chỉ lưu tổng `∫e dt`. Vì vậy, mã cộng trực tiếp `ki * error * dt` vào biến này.

### 9.2. Một lần gọi `update()` làm gì?

```cpp
double update(double target, double measurement,
              double dt, bool integrate = true);
```

Trình tự xử lý:

1. Trả về `0` nếu `dt` không thuộc `(0, 0.1]` giây, hoặc target/measurement không hữu hạn. Nhánh này không reset trạng thái PID.
2. Tính `error = target - measurement`.
3. Tính đạo hàm âm của measurement; ở lần hợp lệ đầu tiên, dùng đạo hàm thô bằng `0` để tránh lấy chênh lệch với trạng thái chưa khởi tạo.
4. Lọc đạo hàm bằng bộ lọc thông thấp bậc một.
5. Tính I ứng viên và giới hạn nó trong `[-integralLimit, integralLimit]`.
6. Chỉ nhận I ứng viên khi `integrate` bật và điều kiện chống windup cho phép.
7. Tính tổng P + I + D rồi giới hạn output.

Công thức bộ lọc trong mã:

```text
tau = 1 / (2 × π × derivativeHz)
alpha = dt / (dt + tau)
derivative_mới = derivative_cũ + alpha × (raw - derivative_cũ)
```

Với tần số cắt dương, tăng `derivativeHz` làm đạo hàm bám mẫu mới nhanh hơn nhưng giảm khả năng lọc nhiễu. Cần cấu hình tần số này dương và các giới hạn không âm; hàm hiện tại không kiểm tra toàn bộ tính hợp lệ của bộ hệ số.

Điều kiện nhận I ứng viên trong mã là:

```cpp
integrate && (std::abs(trial) <= gains_.limit || error * trial < 0)
```

Trong đó `trial` là tổng output tính với I ứng viên. Điều kiện cho phép cập nhật khi tổng chưa vượt giới hạn, hoặc khi dấu sai lệch ngược dấu output thử. Với `ki` dương như cấu hình hiện tại, trường hợp sau cho phép I đi theo hướng đưa output ra khỏi bão hòa.

`integrate = false` **chỉ đóng băng I**, không xóa I và không tắt P/D. Muốn xóa trạng thái tích lũy phải gọi `reset()`; hàm này cũng xóa lịch sử đạo hàm và trạng thái khởi tạo.

### 9.3. Các vòng trong `FlightController`

Hệ số mặc định hiện có:

| Vòng điều khiển | `kp` | `ki` | `kd` | Giới hạn output |
|---|---:|---:|---:|---:|
| Góc roll/pitch | 4 | 0 | 0 | 2 |
| Tốc độ roll | 0.12 | 0.04 | 0.003 | 0.5 |
| Tốc độ pitch | 0.12 | 0.04 | 0.003 | 0.5 |
| Tốc độ yaw | 0.1 | 0.02 | 0 | 0.15 |

Vòng góc hiện tại thực chất dùng **P**, vì `ki = kd = 0`; nó còn được gọi với `integrate = false`. Output của vòng này là tốc độ quay mục tiêu cho vòng trong. Lệnh góc dùng radian, tốc độ quay dùng radian/giây.

Ví dụ, nếu góc roll mục tiêu là `0.1 rad` và góc hiện tại là `0 rad`, vòng góc tạo tốc độ quay mục tiêu `4 × 0.1 = 0.4 rad/s`, chưa chạm giới hạn `2 rad/s`.

Vòng tốc độ roll/pitch dùng đủ P, I, D. Vòng yaw hiện dùng PI và nhận trực tiếp tốc độ yaw mục tiêu; không có vòng giữ góc yaw trong luồng này.

`FlightController` chỉ cho cập nhật I của vòng tốc độ khi throttle lớn hơn `0.35` và cờ bão hòa đang lưu là false. Cờ này đến từ kết quả motor mixer của lần cập nhật trước, vì mixer của lần hiện tại chạy sau PID. Ngoài điều kiện này, mỗi PID vẫn có kiểm tra bão hòa output riêng.

Các PID được reset khi disarm hoặc khi throttle nhỏ hơn `0.02`. Output vòng tốc độ được chuyển cho mixer dưới tên `torque`; việc nó mang đơn vị vật lý hay là giá trị hiệu chỉnh chuẩn hóa phụ thuộc quy ước của mixer và mô hình. Không nên mặc định các giá trị này là N·m.

Các hệ số trên mô tả cấu hình mã nguồn hiện tại, không phải kết quả xác nhận phù hợp với mọi drone thật.

## 10. Cách chỉnh PID từng bước

### Bước 1: kiểm tra điều kiện trước khi chỉnh

Kiểm tra chiều trục cảm biến, chiều hiệu chỉnh motor, đơn vị, chất lượng đo, `dt` và giới hạn output. Nếu các yếu tố này sai, đổi hệ số PID khó giải quyết đúng vấn đề.

Ưu tiên thử trong simulator. Khi kiểm tra chiều motor trên phần cứng, tháo cánh quạt; thử nghiệm có cánh cần quy trình và khu vực phù hợp.

### Bước 2: chỉnh P của vòng trong

Bắt đầu với `Ki = 0`, `Kd = 0` và `Kp` nhỏ. Tăng `Kp` từng bước, dùng một thay đổi mục tiêu nhỏ để quan sát tốc độ đáp ứng và mức dao động. Nếu bắt đầu dao động kéo dài, giảm hệ số để lấy lại biên ổn định.

Không cần cố đẩy hệ đến dao động mạnh để tìm một con số giới hạn, đặc biệt trên phần cứng thật.

### Bước 3: thêm D nếu cần giảm vượt mục tiêu

Tăng `Kd` từng bước nhỏ và theo dõi đồng thời đáp ứng lẫn nhiễu output. Kiểm tra bộ lọc D trước khi tiếp tục tăng nếu lệnh motor rung nhanh.

### Bước 4: thêm I để bù sai lệch kéo dài

Tăng `Ki` từ nhỏ, theo dõi thời gian xóa sai lệch và hiện tượng vượt mục tiêu sau khi đổi lệnh. Cấu hình giới hạn I và chống windup cùng lúc.

### Bước 5: chỉnh vòng ngoài và thử nhiều điều kiện

Sau khi vòng tốc độ quay ổn định, chỉnh vòng góc. Thử cả thay đổi mục tiêu và nhiễu tác động, ở nhiều mức throttle hoặc tải phù hợp. Đổi từng tham số và lưu kết quả để có thể so sánh.

Quy trình trên là điểm bắt đầu thực hành; thứ tự và phương pháp tối ưu còn phụ thuộc động lực học, bộ lọc và cơ cấu chấp hành của hệ.

## 11. Đọc đồ thị và nhận biết vấn đề

Nên ghi lại `timestamp`, target, measurement, error, các đóng góp P/I/D, output trước/sau giới hạn và cờ bão hòa. Đặt target và measurement trên cùng đồ thị để nhìn rõ khả năng bám lệnh.

| Hiện tượng | Khả năng cần kiểm tra |
|---|---|
| Đáp ứng chậm | P thấp, lọc/trễ lớn, giới hạn output hoặc lực điều khiển thiếu |
| Vượt mục tiêu nhiều | P/I cao, giảm chấn chưa đủ, trễ hoặc windup |
| Sai lệch nhỏ tồn tại lâu | I thấp/tắt, I bị đóng băng, bão hòa hoặc thiếu lệnh bù |
| Rung nhanh, output nhiễu | D/P cao, nhiễu cảm biến, rung cơ khí hoặc lọc chưa phù hợp |
| Dao động chậm quanh mục tiêu | I lớn, tương tác giữa các vòng hoặc trễ hệ thống |
| Đổi setpoint làm D nhảy mạnh | Đạo hàm trên sai lệch, derivative kick |
| Output liên tục chạm giới hạn | Mục tiêu quá mạnh, cấu hình giới hạn hoặc khả năng chấp hành không đủ |
| Càng điều khiển càng lệch | Sai dấu phản hồi, sai trục hoặc sai mapping motor |

Đây là các hướng chẩn đoán, không phải kết luận chỉ dựa vào một biểu hiện.

Các đại lượng thường dùng để so sánh:

- **Thời gian tăng:** mất bao lâu để đáp ứng đi từ một ngưỡng thấp đến ngưỡng cao đã chọn, thường là 10% đến 90% của mức thay đổi cuối.
- **Độ vượt mục tiêu:** đáp ứng vượt mức cuối bao nhiêu; khi ghi phần trăm phải nêu rõ mức thay đổi dùng làm chuẩn.
- **Thời gian xác lập:** mất bao lâu để đáp ứng đi vào và ở trong dải sai số đã chọn.
- **Sai lệch xác lập:** sai lệch còn lại sau khi quá trình quá độ đã qua.

## 12. Những nhầm lẫn thường gặp

1. **“Tăng cả ba hệ số sẽ điều khiển tốt hơn.”** Mỗi hệ số giải quyết một phần khác nhau và đều có thể làm hệ mất ổn định nếu chọn sai.
2. **“Sai lệch bằng 0 thì output phải bằng 0.”** I có thể giữ một lệnh bù khác 0; D cũng có thể khác 0 nếu hệ đang chuyển động qua mục tiêu.
3. **“Giới hạn output là đủ chống windup.”** I vẫn có thể tiếp tục tăng phía sau giới hạn nếu không được xử lý riêng.
4. **“Đóng băng I giống reset I.”** Đóng băng giữ giá trị đã có, reset xóa trạng thái.
5. **“PID trực tiếp trả ra tốc độ từng motor.”** Trong dự án, PID trả hiệu chỉnh theo trục; mixer mới tạo lệnh từng motor.
6. **“Giữ góc tốt nghĩa là drone tự giữ độ cao.”** Hai bài toán cần các vòng điều khiển và thông tin trạng thái tương ứng.
7. **“Bộ hệ số tốt trên simulator sẽ dùng ngay được trên drone thật.”** Độ trễ, nhiễu, lực motor, khối lượng và quán tính thực tế có thể khác mô hình.

Để hiểu một bộ PID cụ thể, hãy luôn xác định bốn điểm: **nó điều khiển đại lượng nào, dùng đơn vị gì, output tác động vào đâu và xử lý giới hạn ra sao**.
