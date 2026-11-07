# Hiểu từng bước: drone giữ cân bằng ở 0° như thế nào?

Tài liệu này giải thích lý thuyết chung về giữ cân bằng cho drone bốn cánh quạt thông thường. Chúng ta bắt đầu từ một chiếc drone đang nghiêng, theo dõi nó trở về ngang, rồi mới tìm hiểu P, I, D làm gì trong quá trình đó.

Bạn chưa cần nhớ công thức ngay. Hãy đọc lần lượt phần 1 đến phần 7 để hiểu chuyển động trước. Các con số trong tài liệu chỉ dùng để minh họa, không phải bộ hệ số khuyến nghị cho một drone cụ thể.

## 1. Trước tiên, chỉ nhìn drone từ phía sau

Hãy tưởng tượng bạn đứng sau drone và nhìn về phía đầu của nó.

Ở góc nhìn này, ta chỉ quan tâm hai bên trái và phải. Tạm thời chưa xét drone chúi đầu, quay hướng, bay lên hay bay xuống.

Khi hai bên cao bằng nhau, drone đang nằm ngang:

```text
Bên trái                         Bên phải
    O---------------+---------------O
                    |
                   Thân

                 Góc = 0°
```

Khi bên phải thấp hơn bên trái, drone đang nghiêng:

```text
Bên trái
    O
     --------
             --------+
                      --------
                              --------O
                                  Bên phải
```

Chuyển động nghiêng trái/phải này gọi là **roll**.

Mục tiêu đầu tiên rất đơn giản: nếu drone bị nghiêng, làm sao đưa hai bên trở lại cao bằng nhau?

Sau khi hiểu roll, bạn có thể áp dụng cùng cách suy nghĩ cho **pitch**, tức chuyển động chúi đầu hoặc ngẩng đầu.

## 2. Muốn nâng bên bị thấp lên thì làm gì?

Mỗi motor quay cánh quạt để tạo lực đẩy. Với drone thông thường đang gần nằm ngang, lực này có tác dụng nâng drone lên.

Bây giờ giả sử bên phải đang thấp hơn.

Để đưa bên phải lên, ta có thể tăng lực đẩy ở bên phải và giảm lực ở bên trái. Sự chênh lệch lực khiến thân drone bắt đầu xoay về ngang.

```text
Bên trái: đẩy ít hơn         Bên phải: đẩy nhiều hơn
          ↑                           ↑↑↑

                 Thân bắt đầu xoay
                 để bên phải đi lên
```

Tác dụng làm vật xoay được gọi là **mô men**. Bạn có thể hình dung nó giống việc dùng tay đẩy một đầu bập bênh lên.

Đến đây, cần nhớ một ý:

**Muốn sửa góc nghiêng, cần điều chỉnh chênh lệch lực giữa hai bên.**

Tăng đều cả bốn motor chủ yếu làm thay đổi tổng lực nâng. Trong trường hợp đối xứng lý tưởng, điều đó không tạo chênh lệch để sửa góc nghiêng.

Việc tăng/giảm ở đây là so với mức lực đang có. Không có nghĩa phải tắt motor phía còn lại.

## 3. Tại sao đưa drone về ngang vẫn chưa xong?

Hãy tưởng tượng bạn đẩy một chiếc xe đồ chơi. Khi bạn ngừng đẩy, xe vẫn có thể tiếp tục chạy một đoạn.

Drone đang xoay cũng có đặc điểm tương tự: khi ta ngừng tạo mô men làm nó xoay, nó không lập tức dừng quay.

Đó là ảnh hưởng của **quán tính**: vật có xu hướng giữ chuyển động đang có.

Theo dõi tình huống bên phải đang thấp:

1. Ta tăng lực bên phải để nâng bên phải lên.
2. Drone bắt đầu xoay về ngang.
3. Drone đi đến góc 0°.
4. Nhưng nếu vẫn còn tốc độ quay, nó tiếp tục xoay qua vị trí ngang.
5. Lúc này bên phải lại cao hơn bên trái.

Như vậy, ta vừa sửa từ nghiêng bên này thành nghiêng sang bên kia.

Để tránh điều đó, cần **hãm chuyển động quay**. Trong ví dụ này, hãm nghĩa là tạo chênh lệch lực theo chiều ngược lại để làm tốc độ quay giảm xuống.

Hãm không đồng nghĩa tắt tất cả motor. Các motor vẫn cần tạo lực nâng; chỉ phần chênh lệch giữa chúng được thay đổi.

### Hai thông tin khác nhau cần biết

| Thông tin | Câu hỏi nó trả lời | Ví dụ |
|---|---|---|
| Góc nghiêng | Drone đang lệch khỏi ngang bao nhiêu? | Đang lệch 5° |
| Tốc độ quay | Góc đó đang thay đổi nhanh đến đâu? | Đang quay về ngang 20° mỗi giây |

Đơn vị `°/s` đọc là “độ mỗi giây”. Ví dụ 20°/s nghĩa là nếu giữ nguyên tốc độ này trong một giây, góc sẽ thay đổi 20°.

**Muốn cân bằng, drone cần vừa gần 0° vừa gần như ngừng quay.** Chỉ đi qua góc 0° chưa phải là đã cân bằng.

## 4. Drone biết góc và tốc độ quay bằng cách nào?

Drone không nhìn thấy chính nó. Nó dùng cảm biến.

Drone thường lấy thông tin chuyển động từ **IMU**, một cụm cảm biến có gyroscope và accelerometer.

### 4.1. Gyroscope: cho biết drone đang quay nhanh thế nào

Gyroscope, thường gọi ngắn là gyro, đo tốc độ quay.

Nó giúp bộ điều khiển biết các tình huống như:

- Drone đang đứng gần yên.
- Drone đang quay về ngang khá chậm.
- Drone đang quay về ngang quá nhanh và cần hãm.

Gyro không trực tiếp cho biết góc nghiêng tuyệt đối. Phần mềm có thể cộng dồn chuyển động đo được để theo dõi góc, nhưng sai số nhỏ sẽ tích lũy theo thời gian.

### 4.2. Accelerometer: giúp tìm phương thẳng đứng khi điều kiện phù hợp

Accelerometer là cảm biến gia tốc.

Khi drone đứng yên, dữ liệu của nó giúp phần mềm suy ra hướng liên quan đến trọng lực, từ đó biết thân đang nghiêng thế nào.

Khi drone tăng tốc hoặc rung mạnh, việc suy ra góc từ cảm biến này khó hơn. Vì vậy, phần mềm không nên luôn tin hoàn toàn vào một mình accelerometer.

### 4.3. Phần mềm kết hợp hai nguồn

Có thể hình dung:

```text
Gyro          → theo dõi chuyển động quay nhanh
Accelerometer → hỗ trợ sửa sai lệch góc tích lũy khi phù hợp
                         |
                         v
                  Góc nghiêng ước lượng
```

Quá trình kết hợp các phép đo để tính tư thế được gọi là **ước lượng tư thế**. Bộ lọc bổ sung và các dạng bộ lọc Kalman là những cách có thể dùng; ở đây chỉ cần hiểu mục đích của chúng.

Từ “ước lượng” có nghĩa đây là kết quả tính từ cảm biến, có thể có sai số. Nếu drone thật đang ngang nhưng phần mềm đọc thành nghiêng 3°, bộ điều khiển sẽ ra quyết định dựa trên thông tin sai đó.

Vì vậy, trước khi chỉnh PID, cần xác nhận: đặt drone ngang thì số đo cũng phải gần 0°, và khi nghiêng sang một phía, phần mềm phải nhận đúng phía.

## 5. Bộ điều khiển lặp lại một công việc rất nhỏ

Bộ điều khiển không tính một lần rồi để drone tự bay mãi. Nó liên tục làm lại:

```text
Đọc cảm biến
     ↓
Xem đang lệch bao nhiêu và quay thế nào
     ↓
Tính cần điều chỉnh ra sao
     ↓
Gửi lệnh motor
     ↓
Đọc cảm biến lần tiếp theo
```

Mỗi lần cập nhật chỉ điều chỉnh một chút. Nhiều lần liên tiếp tạo thành quá trình giữ cân bằng.

Khoảng thời gian giữa hai lần cập nhật gọi là **dt**.

Ví dụ, nếu cập nhật mỗi 0.004 giây thì có 250 lần cập nhật trong một giây. Đây chỉ là ví dụ về thời gian, không phải yêu cầu mọi drone đều phải dùng đúng tốc độ này.

Đến đây, ta đã có đủ ý để học PID: biết góc, biết tốc độ, biết cách tạo chuyển động và biết cần cập nhật liên tục.

## 6. Hiểu P trước: lệch nhiều thì sửa mạnh hơn

Trước hết, hãy dùng một quy tắc đơn giản:

> Drone càng nghiêng nhiều, ta càng muốn nó quay về nhanh hơn.

Ví dụ minh họa:

| Góc đang lệch | Yêu cầu quay về |
|---|---|
| 10° | Quay về khá nhanh |
| 5° | Quay về chậm hơn |
| 1° | Quay về nhẹ nhàng |
| 0° | Không yêu cầu tiếp tục quay |

Đây là ý tưởng của **P**, viết tắt của Proportional: mức phản ứng tỉ lệ với mức sai lệch.

### 6.1. Sai lệch là gì?

Sai lệch là khoảng khác nhau giữa điều ta muốn và điều đang đo được.

```text
Sai lệch = Giá trị mong muốn - Giá trị hiện tại
```

Để bắt đầu tính số, ta đặt quy ước cho ví dụ: chiều nghiêng đang có là chiều dương, chiều quay về ngang là chiều âm.

Nếu muốn 0° mà hiện tại là +5°:

```text
Sai lệch góc = 0 - 5 = -5°
```

Dấu âm nói rằng cần sửa theo chiều giảm góc. Nó không nói “motor phải quay ngược”; chiều tăng/giảm từng motor sẽ do phần phân phối lệnh quyết định.

### 6.2. Hệ số P làm gì?

Ta dùng một số nhân gọi là `Kp`:

```text
Mức phản ứng P = Kp × Sai lệch
```

Cùng một sai lệch, `Kp` lớn hơn cho phản ứng mạnh hơn.

Nếu P quá nhỏ, drone có thể trở về ngang chậm. Nếu P quá lớn, phản ứng có thể quá gắt và gây dao động.

Một cách điều khiển phổ biến là chia thành **hai vòng**, mỗi vòng có thể dùng P cho một công việc khác nhau. Ta sẽ đi từng vòng ở phần tiếp theo.

## 7. Tách thành hai câu hỏi để dễ điều khiển

Thay vì lập tức hỏi “cho mỗi motor chạy bao nhiêu?”, bộ điều khiển chia thành hai bước.

### 7.1. Câu hỏi thứ nhất: cần quay về nhanh bao nhiêu?

Bộ phận thứ nhất nhìn **góc nghiêng**.

Ví dụ minh họa, ta chọn hệ số vòng góc bằng 4 mỗi giây:

```text
Đang lệch +5°
Muốn về 0°
Sai lệch = -5°
Tốc độ quay mong muốn = 4 × (-5) = -20°/s
```

Nghĩa là nó yêu cầu: “hãy quay theo chiều về ngang với tốc độ 20° mỗi giây”.

Khi chỉ còn lệch +1°:

```text
Tốc độ quay mong muốn = 4 × (-1) = -4°/s
```

Nó tự giảm yêu cầu tốc độ khi gần ngang.

Bộ phận này gọi là **vòng điều khiển góc**. Trong ví dụ đang xét, ta chọn dùng P cho vòng này để dễ hiểu. Đây là một lựa chọn thiết kế, không phải mọi bộ điều khiển góc đều bắt buộc chỉ dùng P.

### 7.2. Câu hỏi thứ hai: làm sao đạt tốc độ quay đó?

Bộ phận thứ hai nhìn **tốc độ quay thực tế**.

Giả sử vòng góc đang yêu cầu -20°/s:

| Tốc độ thực tế | Nhận xét | Hành động cần thiết |
|---|---|---|
| 0°/s | Chưa quay | Làm drone bắt đầu quay theo chiều âm |
| -10°/s | Quay về còn chậm | Tăng tốc quay về |
| -20°/s | Đúng tốc độ được yêu cầu | P tốc độ bằng 0; I/D vẫn có thể đóng góp |
| -40°/s | Quay về quá nhanh | Tạo tác động hãm |

Bộ phận này gọi là **vòng điều khiển tốc độ quay**. Một cấu trúc phổ biến là dùng PID ở vòng này cho roll và pitch. Các phần tiếp theo giải thích theo cấu trúc đó.

Bạn có thể nhớ đường đi bằng một dòng:

```text
Góc đang lệch → cần quay nhanh bao nhiêu → cần tác động motor thế nào
```

### 7.3. P tốc độ cũng có thể hãm drone

Đây là điểm cần dừng lại để hiểu rõ.

Giả sử drone còn nghiêng +1°, nên vòng góc chỉ yêu cầu -4°/s. Nhưng drone thực tế đang quay tới -20°/s.

```text
Sai lệch tốc độ = Tốc độ mong muốn - Tốc độ thực tế
                = -4 - (-20)
                = +16°/s
```

Sai lệch dương khiến P tốc độ tạo tác động theo chiều dương, để giảm chuyển động âm đang quá nhanh.

Như vậy, **P ở vòng tốc độ đã có tác dụng hãm**. Không cần đợi góc vượt qua 0° mới bắt đầu hãm.

Điều này cũng giải thích vì sao không nên nhớ máy móc rằng “P chỉ kéo về, D mới hãm”. Vai trò cụ thể phụ thuộc P hoặc D đang nằm ở vòng nào.

## 8. Hiểu I: nhớ phần bù cần duy trì lâu dài

Bây giờ thêm một khó khăn mới.

Giả sử lực thực tế của các motor không hoàn toàn giống nhau. Để giữ ngang, bộ điều khiển luôn phải cho một phía nhận lệnh nhỉnh hơn phía kia.

Đó là một **phần bù liên tục**.

### 8.1. Vì sao P có thể chưa đủ?

P phản ứng theo sai lệch đang có.

Nếu góc và tốc độ đều đúng mục tiêu, sai lệch bằng 0 nên P bằng 0. Trong khi đó, drone vẫn có thể cần phần bù để chống lại sự mất cân đối liên tục.

Nếu không có nơi giữ phần bù, hệ có thể phải chấp nhận lệch một chút để P tiếp tục tạo lệnh sửa.

### 8.2. I cộng dồn sai lệch theo thời gian

**I**, viết tắt của Integral, lưu lại ảnh hưởng của sai lệch qua nhiều lần cập nhật.

Hãy hình dung bộ điều khiển nhận thấy:

> “Tôi đã sửa nhưng tốc độ vẫn thiếu một chút. Nếu chuyện này tiếp tục, tôi sẽ tăng dần phần bù.”

Nếu dùng I trong vòng tốc độ, cách cộng dồn có thể viết:

```text
I mới = I cũ + Ki × Sai lệch tốc độ × dt
```

Đọc từng phần:

- `I cũ`: phần bù đã lưu từ trước.
- `Sai lệch tốc độ`: tốc độ đang thiếu hoặc thừa bao nhiêu.
- `dt`: sai lệch đó tồn tại trong bao lâu ở bước này.
- `Ki`: mức nhanh/chậm của việc thay đổi phần bù.

Nếu sai lệch nhỏ nhưng kéo dài, I vẫn có thể tăng đến mức đáng kể.

### 8.3. Đạt 0° rồi, I có biến mất không?

Không tự biến mất.

Khi sai lệch bằng 0, phần tăng thêm của I bằng 0, nhưng giá trị I đã lưu có thể vẫn còn.

Điều này hữu ích: drone có thể nằm ngang, ngừng quay, nhưng I vẫn giữ phần lệnh cần thiết để bù sự mất cân đối giữa các phía.

Ở đây I không “nhớ góc cũ để kéo drone về chỗ cũ”. Trong vòng tốc độ, nó lưu phần đóng góp được hình thành từ **sai lệch tốc độ quay**.

### 8.4. Vì sao không tăng I thật mạnh?

Nếu tích lũy quá nhanh, I có thể còn lớn khi drone đã gần ngang và khiến drone tiếp tục bị đẩy quá mức.

Một tình huống khác: motor đã chạy đến giới hạn, không thể tăng thêm lực. Nếu I vẫn cộng dồn, phần bù sẽ lớn hơn nhiều so với mức có ích.

Hiện tượng tích lũy quá mức này gọi là **windup**.

Vì vậy, bộ điều khiển thường cần giới hạn I và có cơ chế chống tích lũy quá mức. Chẳng hạn, có thể tạm ngừng tích lũy khi motor đã chạm giới hạn và sai lệch còn yêu cầu đẩy xa hơn vào giới hạn đó. Cách xử lý cụ thể tùy thiết kế.

“Ngừng cập nhật” nghĩa là giữ nguyên giá trị đang có. “Reset” mới là xóa giá trị đó.

## 9. Hiểu D: cần nhìn xem nó đang đo sự thay đổi của cái gì

**D**, viết tắt của Derivative, phản ứng với tốc độ thay đổi của một đại lượng.

Câu này khá trừu tượng. Ta tách thành hai trường hợp.

### 9.1. Nếu D được dùng trên góc

Giả sử ta điều khiển góc trực tiếp.

Góc thay đổi nhanh nghĩa là drone đang quay nhanh. Khi mục tiêu cố định ở 0°, D trên góc có thể tạo tác dụng giảm chấn, giúp hạn chế việc quay quá đà.

Đó là nguồn gốc của cách giải thích quen thuộc: “D giống như phanh”.

Nếu chuyển D sang vòng tốc độ, đại lượng mà nó phản ứng cũng thay đổi. Ta cần phân biệt hai trường hợp để hiểu đúng.

### 9.2. Nếu D được dùng trong vòng tốc độ quay

Vòng trong nhận tốc độ quay làm giá trị đo.

Vì vậy D nhìn xem **tốc độ quay đang thay đổi nhanh đến đâu**. Sự thay đổi tốc độ quay được gọi là **gia tốc góc**.

Ví dụ minh họa:

| Chuyển động | D trên tốc độ nhận thấy gì? |
|---|---|
| Quay đều -20°/s qua nhiều mẫu | Tốc độ gần như không đổi; D tiến về 0 |
| Từ 0 chuyển nhanh sang -20°/s | Tốc độ đang giảm nhanh theo dấu số; D có phản ứng |
| Từ -20 tăng nhanh về -5°/s | Tốc độ đang tăng theo dấu số; D phản ứng theo chiều ngược trường hợp trên |

Một cách triển khai thường dùng là lấy sự thay đổi của giá trị đo rồi đảo dấu, thay vì lấy đạo hàm toàn bộ sai lệch. Theo cách này, nếu tốc độ đo tăng nhanh theo chiều dương, đóng góp D có xu hướng âm, và ngược lại. Cách này cũng tránh xung D trực tiếp do mục tiêu thay đổi đột ngột.

Nó có thể giúp đáp ứng bớt gắt và giảm quá đà khi chỉnh phù hợp. Tuy nhiên, không thể chỉ nhìn chiều drone đang quay để kết luận chiều của D; cần biết tốc độ đó đang thay đổi ra sao.

Đặc biệt, nếu drone quay đều nhưng sai tốc độ mục tiêu, D có thể gần 0 trong khi P tốc độ vẫn đang sửa.

### 9.3. Vì sao D dễ bị nhiễu?

Hãy tưởng tượng gyro báo các giá trị hơi nhảy qua lại dù drone gần đứng yên. D có thể hiểu những thay đổi đó là chuyển động cần phản ứng.

Nếu Kd lớn, những thay đổi nhỏ có thể tạo lệnh motor rung nhanh.

Vì vậy, nhánh D thường cần **bộ lọc D**. Bạn có thể hiểu lọc là làm bớt các thay đổi nhanh đáng ngờ trong tín hiệu. Lọc cũng tạo độ trễ, nên không phải cứ lọc mạnh nhất là tốt nhất.

## 10. Ghép P, I, D trong một lần cập nhật

Đến đây mới cần nhìn công thức tổng:

```text
Lệnh hiệu chỉnh = P + I + D
```

Với vòng tốc độ:

- P sửa mức sai lệch tốc độ hiện tại.
- I giữ và điều chỉnh phần bù hình thành qua thời gian.
- D phản ứng với thay đổi của tốc độ đo, sau khi lọc.

Chúng là ba phần của cùng một lệnh. Không phải drone chạy P trước, rồi đợi P xong mới chạy I, rồi mới chạy D.

Lệnh tổng còn được giới hạn trước khi gửi đến phần phân phối motor.

### Theo dõi cả quá trình từ nghiêng về ngang

Các hàng sau là những thời điểm giả định để giải thích nguyên lý:

| Thời điểm | Tình trạng | Vòng góc yêu cầu gì? | Vòng tốc độ cần làm gì? |
|---|---|---|---|
| Ban đầu | Nghiêng 5°, chưa quay | Quay về ngang | Tạo chuyển động quay về |
| Đang về | Còn nghiêng 3° | Tiếp tục quay, yêu cầu chậm dần | Bám tốc độ mới |
| Gần ngang | Còn nghiêng 1°, quay khá nhanh | Chỉ quay về chậm | Hãm nếu tốc độ thực quá lớn |
| Đi qua ngang | Góc 0° nhưng vẫn quay | Tốc độ mục tiêu bằng 0 | Tiếp tục hãm chuyển động còn lại |
| Đã ổn định | Góc gần 0°, gần ngừng quay | Gần như không yêu cầu quay | Giữ phần bù cần thiết, sửa nhiễu mới |

Dấu và độ lớn cụ thể của I/D phụ thuộc lịch sử chuyển động. Không thể chỉ dựa vào một giá trị góc để biết chính xác cả ba thành phần.

## 11. Từ lệnh hiệu chỉnh đến bốn motor

PID tốc độ chưa trực tiếp trả ra bốn tốc độ motor. Nó trả ra yêu cầu hiệu chỉnh theo các trục.

Một phần khác gọi là **mixer** nhận:

```text
Mức nâng chung (throttle)
Hiệu chỉnh nghiêng trái/phải (roll)
Hiệu chỉnh chúi/ngẩng (pitch)
Hiệu chỉnh quay hướng (yaw)
```

Rồi kết hợp chúng thành lệnh cho từng motor.

Có thể hình dung mixer là người chia việc: cần nâng chung bao nhiêu, phía nào cần thêm lực, phía nào cần bớt lực.

Quy tắc phân phối phụ thuộc vị trí từng motor, chiều quay cánh và quy ước các trục. Vì vậy, trước khi áp dụng một công thức phân phối, cần biết nó dành cho kiểu bố trí nào.

### Motor có giới hạn

Nếu một motor đã ở mức tối đa, mixer không thể yêu cầu nó tăng thêm mãi. Khi không còn đủ khả năng thực hiện yêu cầu, ta nói lệnh bị **bão hòa**.

Ví dụ: cần tăng bên phải để sửa góc, nhưng các motor bên phải đã gần tối đa. Lúc đó drone có thể không sửa nhanh như PID muốn.

Đây cũng là lý do phải kiểm soát I: không nên để nó tích lũy mãi cho một yêu cầu mà motor không thể đáp ứng.

## 12. Thử tính một ví dụ nhỏ, không cần lập trình

Phần này chỉ dùng phép trừ và phép nhân. Mục đích là thấy rõ vì sao gần ngang rồi mà drone vẫn cần hãm.

### 12.1. Bước đầu: drone đang lệch 5° và chưa quay

Ta tiếp tục dùng quy ước góc đang lệch là dương:

```text
Góc mong muốn:      0°
Góc đang có:       +5°
Tốc độ đang có:     0°/s
```

Vòng góc tính:

```text
Sai lệch góc = 0 - 5 = -5°
Tốc độ mong muốn = 4 × (-5) = -20°/s
```

Sau đó vòng tốc độ tính:

```text
Sai lệch tốc độ = -20 - 0 = -20°/s
```

P tốc độ tạo tác động theo chiều âm để drone bắt đầu quay về ngang.

Ta chưa cần chọn hệ số P tốc độ hay tính lực motor cụ thể. Để hiểu chiều tác động, biết dấu của sai lệch là đủ, với giả định hệ số P dương và chiều phản hồi được thiết lập đúng.

### 12.2. Bước sau: drone còn lệch 1° nhưng đang quay nhanh

Giả sử ở một thời điểm sau đó:

```text
Góc đang có:       +1°
Tốc độ đang có:   -20°/s
```

Vòng góc yêu cầu:

```text
Tốc độ mong muốn = 4 × (0 - 1) = -4°/s
```

Nhưng tốc độ thực vẫn là -20°/s. Vì vậy:

```text
Sai lệch tốc độ = -4 - (-20) = +16°/s
```

Dấu đã đổi từ âm sang dương. P tốc độ lúc này yêu cầu tác động ngược lại để hãm.

Điểm cần hiểu: **góc chưa đổi phía, nhưng lệnh tác động đã có thể đổi phía**. Điều đó hợp lý vì drone cần giảm tốc trước khi đi quá xa.

### 12.3. Khi có một tác động gây lệch liên tục

Bây giờ giả sử một phía luôn tạo lực hơi thiếu so với phía còn lại.

Trong nhiều lần cập nhật, sai lệch tốc độ làm I điều chỉnh dần phần bù. Khi đạt trạng thái cân bằng lý tưởng:

```text
Góc gần 0°
Tốc độ quay gần 0°/s
P gần 0
D gần 0 khi tốc độ đã ổn định
I có thể khác 0 để giữ phần bù
```

Để trạng thái đó tồn tại, motor phải còn đủ khả năng tạo phần bù và hệ đo phải đủ đúng.

### 12.4. Cần biết thêm gì mới tính được D?

Chỉ biết góc và tốc độ ở một thời điểm chưa đủ để tính D trên tốc độ đo. Cần biết tốc độ ở thời điểm trước và khoảng thời gian giữa hai lần đo.

Ví dụ, nếu tốc độ đổi từ 0 thành -10°/s trong 0.1 giây:

```text
Mức thay đổi tốc độ = -10 - 0 = -10°/s
Gia tốc góc trung bình = -10 / 0.1 = -100°/s²
```

Với D lấy trên giá trị đo rồi đảo dấu, phần D thô có dấu dương trước khi nhân hệ số dương. Nó chống lại sự tăng nhanh của chuyển động theo chiều âm. Trong bộ điều khiển thực tế, tín hiệu còn cần lọc và giới hạn phù hợp.

`°/s²` đọc là “độ trên giây bình phương”: tốc độ quay thay đổi bao nhiêu độ/giây trong mỗi giây.

### 12.5. Độ và radian là hai cách biểu diễn góc

Bạn có thể học trực giác bằng độ. Trong tính toán kỹ thuật, người ta cũng thường dùng radian:

```text
180° = π rad
5° ≈ 0.0873 rad
20°/s ≈ 0.3491 rad/s
```

Cùng một chuyển động nhưng con số biểu diễn khác nhau. Vì vậy, cần ghi rõ đơn vị của góc, tốc độ, thời gian và hệ số PID.

Không sao chép hệ số từ một tài liệu dùng radian sang phép tính dùng độ mà chưa kiểm tra quy đổi. Cũng cần kiểm tra đầu ra là tốc độ mục tiêu, mô men hay mức hiệu chỉnh đã chuẩn hóa.

## 13. Cân bằng thân có đồng nghĩa đứng yên trên không không?

Chưa đủ. Ta quay lại những chuyển động đã tạm bỏ qua lúc đầu.

### 13.1. Thân ngang nhưng drone vẫn có thể rơi

Giữ góc chỉ đảm bảo tư thế. Nếu tổng lực nâng không đủ, drone vẫn rơi dù thân nằm ngang.

Để treo ở độ cao không đổi, trong điều kiện lý tưởng cần lực nâng cân bằng trọng lượng và vận tốc thẳng đứng bằng 0. Nếu đã đang rơi, cần thêm lực trong một khoảng thời gian để hãm rơi.

Bộ điều khiển độ cao sẽ phụ trách vấn đề này, dựa trên thông tin đo hoặc ước lượng độ cao và vận tốc thẳng đứng.

### 13.2. Thân ngang nhưng drone vẫn có thể trôi ngang

Nếu đã có vận tốc ngang, đưa thân về ngang không có nghĩa vận tốc đó lập tức biến mất.

Muốn hãm trôi hoặc chống gió, drone thường cần nghiêng để tạo lực ngang. Vòng giữ vị trí sẽ quyết định nên nghiêng bao nhiêu.

Vì vậy, có ba mục tiêu khác nhau:

```text
Giữ thân ngang → điều khiển góc và tốc độ quay
Giữ độ cao    → điều khiển chuyển động lên/xuống
Giữ vị trí    → điều khiển chuyển động ngang và độ cao
```

Tài liệu này tập trung mục tiêu thứ nhất. Nó là nền tảng cho hai mục tiêu còn lại.

## 14. Nên quan sát và chỉnh như thế nào khi mới học?

Khi học bằng mô phỏng, hãy bắt đầu với một tình huống đơn giản: chỉ cho roll lệch một góc nhỏ, chưa thêm nhiều nhiễu cùng lúc.

### Lần quan sát 1: đúng chiều chưa?

Khi drone nghiêng, lệnh điều khiển có làm nó quay về ngang không?

Nếu càng sửa càng nghiêng thêm, cần kiểm tra dấu cảm biến và phân phối motor trước. Tăng hệ số PID không sửa được lỗi sai chiều.

### Lần quan sát 2: quay về quá chậm hay quá gắt?

Nhìn cả góc và tốc độ quay. Tìm xem vòng góc yêu cầu tốc độ bao nhiêu và vòng trong có bám được không.

Điều này giúp tránh việc thấy góc xấu rồi chỉnh ngẫu nhiên một hệ số.

### Lần quan sát 3: đã về ngang nhưng còn lệch lâu dài không?

Nếu còn một sai lệch nhỏ dai dẳng, kiểm tra cảm biến, khả năng motor, điều kiện tích phân và phần I. Đừng mặc định mọi sai lệch đều do Ki nhỏ.

### Lần quan sát 4: lệnh motor có rung nhanh không?

Nếu có, kiểm tra rung cơ khí, tín hiệu gyro, P/D và bộ lọc. D quá nhạy với nhiễu là một khả năng, không phải nguyên nhân duy nhất.

Khi chỉnh, thường ổn định vòng tốc độ trước rồi mới chỉnh vòng góc. Thay đổi từng hệ số nhỏ một và giữ log để so sánh. Không có một bộ hệ số PID dùng tốt cho mọi drone: khối lượng, kích thước, motor, cánh quạt, cảm biến và độ trễ đều có ảnh hưởng.

Khi kiểm tra cảm biến và thứ tự motor trên phần cứng, tháo cánh quạt. Các phép thử động có cánh cần khu vực và quy trình thử phù hợp.

## 15. Tự kiểm tra bằng năm câu hỏi nhỏ

**1. Drone đang ở 0° thì có chắc đã cân bằng chưa?**

Chưa. Nó có thể đang đi qua 0° với tốc độ quay khác 0.

**2. Muốn sửa nghiêng thì tăng đều cả bốn motor có đủ không?**

Trong trường hợp đối xứng lý tưởng, không. Cần chênh lệch lực giữa các phía để làm thân xoay.

**3. Trong cấu trúc hai vòng vừa học, P của vòng góc trả ra cái gì?**

Tốc độ quay mong muốn. Nó chưa trả ra bốn lệnh motor.

**4. Vì sao đã nằm ngang mà I vẫn có thể khác 0?**

Vì hệ có thể cần một phần bù liên tục để chống lại sự mất cân đối hoặc mô men nhiễu.

**5. D của vòng tốc độ có luôn chống chiều đang quay không?**

Không. Nó phản ứng với sự thay đổi của tốc độ đo. P tốc độ mới trực tiếp so sánh tốc độ đang quay với tốc độ mong muốn.

Nếu đã trả lời được năm câu này, hãy thử tự kể lại quá trình một chiếc drone từ nghiêng 5° trở về ngang: nó đo gì, yêu cầu quay thế nào, hãm khi nào và giữ phần bù bằng cách nào.

