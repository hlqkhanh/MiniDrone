# Hướng dẫn hiệu chỉnh PID theo số đo cho hệ thống Drone

Mục tiêu của tài liệu là giúp bạn **biết nên thay thông số nào, vì sao thay, thử thế nào và dựa vào đâu để giữ hoặc bỏ thay đổi đó**. Không có một bộ số tốt nhất cho mọi drone. Bộ thông số phù hợp phải gắn với motor, cánh, pin, trọng tâm, đế, phần mềm và phạm vi góc/tốc độ đã thử.

Đây là hướng dẫn thử cân bằng trên đế cho code hiện tại, không phải quy trình xác nhận bay tự do. Các số đánh dấu **ví dụ** hoặc **mức thử chưa xác minh** không phải thông số bảo đảm ổn định trên phần cứng của bạn.

Đọc thêm khi cần: [ý nghĩa các field](huong-dan-field-pid-wpf.md), [đọc đồ thị](do-thi-pid.md), [giải thích FlightController.h](giai-thich-flight-controller.md).

### Nếu mới bắt đầu, hãy đọc theo lộ trình này

Bạn chưa cần hiểu hết công thức hoặc chỉnh đủ sáu hệ số của một trục trong lần đầu. Việc đầu tiên là tạo được **một lượt thử đúng chiều, có số liệu mới và có thể lặp lại**.

| Bạn đang ở bước nào? | Đọc mục nào? | Kết quả cần có trước khi đi tiếp |
|---|---|---|
| Chưa hiểu app điều khiển gì | Mục 1–3 | Phân biệt được góc, tốc độ quay, phần sửa duty và lệnh motor |
| Chưa biết phần cứng đã đúng chưa | Mục 4 | Cảm biến đúng trục, motor đúng vị trí, phản hồi đúng chiều |
| Chưa biết base và output liên quan thế nào | Mục 5 | Hiểu vì sao một bên tăng thì bên kia giảm và khi nào ị cắt lệnh |
| Muốn thực hiện lượt đầu | Mục 6–7 | Giữ Outer cố định, thử riêng Inner Kp và ghi được kết quả |
| Đã có đáp ứng nhưng còn vượt/lắc/lệch | Mục 8–11 | Có lý do cụ thể trước khi thêm D, I hoặc chỉnh Outer |
| Đang phân vân có nên tăng limit | Mục 12 | Chỉ ra được tầng nào chạm biên bằng số liệu |
| Muốn so sánh và lưu bộ tốt | Mục 13–16 | Có cấu hình, CSV và nhận xét để thử lại được |

**Cách dùng tài liệu:** đọc đến bước mình đang làm, thực hiện và ghi nhận kết quả rồi mới chuyển tiếp. Những bảng số dưới đây dùng để giải thích phép tính hoặc gợi ý một thử nghiệm có điều kiện; không phải một danh sách để nhập hết vào app.

## 1. Trước hết, “hợp lý” được đánh giá bằng gì?

Không chọn một bộ gain chỉ vì motor quay mạnh hoặc drone vừa đi qua 0°. Hãy so sánh các lượt thử bằng những câu hỏi sau:

| Tiêu chí | Cần quan sát |
|---|---|
| Đúng chiều | Lệnh sửa giúp tiến về mục tiêu, không làm lệch thêm ngay từ đầu |
| Hồi về | Góc tiến về target trong thời gian phù hợp yêu cầu thử |
| Vượt góc | Không lao qua mục tiêu quá nhiều |
| Dao động | Nếu có dao động ban đầu, biên độ giảm thay vì tăng hoặc kéo dài |
| Sai lệch còn lại | Sau chuyển động ban đầu, góc còn lệch bao nhiêu |
| Theo tốc độ | Rate đo có theo được rate mục tiêu không |
| Khoảng dự phòng | PID/motor có tránh giữ lâu ở giới hạn không |
| Lặp lại | Thử lại cùng điều kiện, cả hai chiều, kết quả có tương tự không |

Bạn có thể đặt một tiêu chí học tập như “từ lệch nhỏ, trở về trong vùng ±2° rồi giữ được 10 giây”. Đây chỉ là **ví dụ tiêu chí do người thử chọn**, không phải tiêu chuẩn bay hoặc kết quả đã đạt. Nếu cảm biến/đế chưa đủ ổn định thì cần xử lý chúng trước, không cố đạt số bằng cách tăng gain.

Để thời gian hồi có ý nghĩa, góc bắt đầu phải ở ngoài vùng chấp nhận; ví dụ ban đầu 3° thì mới đánh giá thời gian vào vùng ±1°. Chọn vùng phù hợp với độ nhiễu thực tế, không chặt hơn khả năng đo rồi coi mọi dao động nhỏ là lỗi PID.

## 2. Hiểu đúng khả năng của giao diện hiện tại

### 2.1. Bốn đại lượng rất dễ nhầm

| Đại lượng | Câu hỏi nó trả lời | Ví dụ | Không nên hiểu thành |
|---|---|---|---|
| Góc, đơn vị ° | Drone đang nghiêng bao nhiêu? | Roll = +5° | Motor đang chạy 5% |
| Tốc độ quay thân, đơn vị °/s | Góc nghiêng đang thay đổi nhanh bao nhiêu? | Roll rate = −3°/s | Tốc độ quay cánh quạt |
| Output vòng tốc độ, đơn vị duty | Cần sửa chênh lệch lệnh motor bao nhiêu? | Roll output = −7 duty | Một motor phải nhận duty −7 |
| PWM motor, đơn vị duty | Mỗi motor cuối cùng nhận mức lệnh nào? | LF=97, RF=83 | Tốc độ vòng/phút hoặc lực đẩy đã đo |

**Đang nghiêng và đang quay là hai chuyện khác nhau.** Drone có thể đứng yên ở góc +5° nên rate gần 0; cũng có thể đang đi qua góc 0° rất nhanh nên chưa thể coi là đã cân bằng.

Trong tài liệu, **gain** là hệ số bạn nhập như Kp=1. **P/I/D** ở phần số đọc là các kết quả đang được tính ra. Thay Kp làm thay đổi cách tính P; nó không đặt P thành một số cố định.

### 2.2. Hai vòng nối với nhau như thế nào?

Luồng điều khiển:

```text
Target góc − góc đo → Outer PID → rate mục tiêu
Rate mục tiêu − gyro → Inner PID → phần sửa duty
Base + phần sửa qua bộ trộn → PWM LF/RF/LR/RR
```

Vòng góc giống bước quyết định **“cần quay về nhanh bao nhiêu”**. Vòng tốc độ kiểm tra **“thân drone đã quay đúng tốc độ đó chưa”**, rồi tăng/giảm lệnh motor để sửa. Không thể đổi trực tiếp −10°/s thành một tốc độ motor cố định: cùng lệnh điện nhưng pin, lực đẩy, ma sát đế và chuyển động đang có có thể tạo ra tốc độ quay thân khác nhau.

Ví dụ chỉ có P, tại một thời điểm:

```text
Muốn giữ Roll = 0°, nhưng đo được +5°
→ Sai số góc = 0 − 5 = −5°

Outer Kp = 2
→ Rate target = 2 × (−5) = −10°/s

Gyro đang đo −3°/s
→ Sai số rate = −10 − (−3) = −7°/s

Inner Kp = 1, Ki=Kd=0
→ Output = −7 duty, chưa chạm giới hạn ±40
```

Đọc bằng lời: drone đã quay theo chiều trở về, nhưng mới đạt −3°/s trong khi yêu cầu là −10°/s. Vòng tốc độ tiếp tục tạo phần sửa để thay đổi chuyển động. Các số trên là **một ví dụ tính toán**, không phải bộ gain đã được chọn cho máy.

Nếu vẫn ở góc +5° nhưng gyro đã là −15°/s thì sai số rate thành `−10 − (−15) = +5°/s`. Output P có thể đổi sang dương để giảm tốc quay về. Vì vậy, **lệnh sửa đổi dấu trước khi góc về 0 chưa đủ để kết luận bị ngược chiều**.

### 2.3. Vì sao chỉnh Inner trước?

Nếu vòng tốc độ chưa thực hiện được yêu cầu, tăng vòng góc chỉ làm nó đòi quay nhanh hơn. Khi đó bạn thấy góc không ổn nhưng khó biết lỗi nằm ở yêu cầu quá mạnh hay ở khả năng thực hiện. Giữ Outer cố định giúp giảm bớt một yếu tố thay đổi để đánh giá Inner rõ hơn.

App **chưa có chế độ tách riêng vòng tốc độ để nhập rate mục tiêu trực tiếp**. Vì vậy, “chỉnh vòng tốc độ trước” trong hướng dẫn này có nghĩa là **giữ Outer cố định**, thay Inner và đánh giá dữ liệu rate trong khi cả chuỗi vẫn hoạt động. Nó không phải phép thử vòng tốc độ hoàn toàn độc lập như chế độ rate/Acro của một autopilot khác.

Target và gain chỉ chỉnh khi dừng/disarm. Muốn so sánh target mới, bạn phải Stop, đổi target rồi Arm/Start lại. Không làm theo hướng dẫn giật cần khi đang bay của hệ thống khác nếu app này không cung cấp thao tác tương ứng.

### 2.4. Trên app, cần nhìn số nào trong lúc thử?

Mở **Live PID plots**, chọn **Plot axis** đúng trục đang điều khiển. Số đo nằm ngay phía trên đường vẽ của từng khung; không cần nhìn lại một danh sách dài ở thanh bên. Đọc theo thứ tự:

| Thứ tự | Dòng trên app | Bạn cần trả lời câu hỏi gì? |
|---|---|---|
| 1 | **Control telemetry**, **age** | Mẫu có mới không, board đang báo RUNNING hay STOPPED? |
| 2 | **ANGLE: Measured / Target / Error** | Góc có tiến về mục tiêu và giữ được không? |
| 3 | **BODY RATE: Measured / Target / Error** | Chuyển động thực có theo yêu cầu của vòng góc không? |
| 4 | **RATE PID: P / I / D / Output** | PID đang đóng góp bao nhiêu và sau giới hạn còn bao nhiêu? |
| 5 | **AT LIMIT** trong BODY RATE hoặc RATE PID | Vòng góc hoặc vòng tốc độ có đang sát biên không? |
| 6 | **MOTORS: LF / RF / LR / RR, Mixer** | Lệnh cuối có phân bổ đúng phía và bị cắt không? |

Giới hạn board thực sự báo có nhãn gọn ở góc phải khung tương ứng: rate target trong BODY RATE, output trong RATE PID, base/biên motor trong MOTORS. Rê chuột vào từng khung để xem đầy đủ gain, output/I và **Build**. Base/trục trong khung motor là của lần Start được chấp nhận gần nhất, không phải nội dung ô nhập vừa sửa. Thông tin này được giữ sau Stop; xem kèm trạng thái chạy. Build là dấu thời gian biên dịch, không phải mã commit.

**Pause** giữ cả đường vẽ, số và cấu hình của mẫu đang xem. Dòng **sample RUNNING/STOPPED** và số giây kế bên nói về mẫu đó; khi Pause, mẫu có thể là RUNNING dù hiện tại board đã Stop. Dòng **Control telemetry** ở thanh bên vẫn cập nhật theo dữ liệu đang nhận. Tắt Pause để tiếp tục theo dõi mẫu mới; Clear xóa số cùng lịch sử rồi chờ mẫu mới. Bốn đồ thị tự chia chiều cao cửa sổ, không cần cuộn.

Nếu **age > 2 giây**, app báo **STALE**: các số còn lại chỉ là mẫu cuối, không đủ để xác định trạng thái hiện tại. Khi ngắt kết nối hoặc bắt đầu phiên mới, app xóa dữ liệu điều khiển cũ. Hình 3D vẫn cập nhật không tự chứng minh C/C2 điều khiển còn mới, vì chúng là các bản tin khác nhau.

`unknown (no CONFIG)` nghĩa là chưa có cấu hình đọc lại; cần firmware hỗ trợ CONFIG. `not reported (C2)` nghĩa là mẫu chưa có rate target/output mở rộng. Đừng điền số 0 thay cho dữ liệu thiếu rồi kết luận PID không hoạt động.

## 3. Những thông số đang có trong code

| Thông số | Mặc định/phạm vi hiện tại | Khi nào xem xét? |
|---|---|---|
| Outer Roll/Pitch Kp/Ki/Kd | Mặc định 2/0/0 | Sau khi đáp ứng rate đã tương đối phù hợp |
| Inner Roll/Pitch Kp/Ki/Kd | Mặc định 0/0/0 | Phần cần xác định từ thử nghiệm đầu tiên |
| Gain nhập qua app | Kp 0–5; Ki 0–2; Kd 0–0.2 | Đây là kiểm tra dữ liệu nhập, không phải miền đã được chứng nhận ổn định |
| Base duty | Nhập 1–180 trên thang 255 | Chọn trước một lượt chỉnh gain; giữ cố định khi so sánh |
| Góc mục tiêu | ±10° | Bắt đầu với mục tiêu và chuyển động nhỏ |
| Outer output / I | ±45°/s / ±10°/s | Xem lệnh tốc độ có bị chặn và rate có theo được không |
| Inner output / I | ±40 duty / ±20 duty | Xem khả năng sửa và tích lũy có bị giới hạn không |
| Motor sau trộn | 0–220 duty | Kiểm tra khoảng dự phòng từng motor |
| Bộ lọc D | 10 Hz | Giữ cố định trong các bước thử gain ban đầu |
| Nhịp PID / đồ thị | Mục tiêu 200 Hz / khoảng 20 Hz | Không suy ra rung nhanh đầy đủ từ đồ thị 20 Hz |

Các số **45 và 40 là giới hạn thiết kế khởi đầu của phiên bản này**, không phải kết quả nhận dạng phần cứng của bạn. Giới hạn I=10/20 cũng chưa phải mức đã tối ưu. Chưa có ô WPF để chỉnh các limit đó; nếu cần đổi phải sửa header, build và nạp lại firmware. Reset board cũng làm mất gain đang lưu trong RAM, nên cần Apply và xác nhận lại.

**Dấu ± nghĩa là có hai chiều.** Giới hạn ±45°/s cho phép lệnh từ −45 đến +45°/s; không bắt drone phải luôn quay 45°/s. Giới hạn ±40 duty cho phép phần sửa từ −40 đến +40; đây là phần tăng/giảm quanh base, còn lệnh cuối motor vẫn nằm trong 0–220.

Với lượt đầu, ưu tiên xác định **base và Inner Kp** trong điều kiện đã kiểm tra. Giữ Outer 2/0/0, Ki/Kd bằng 0 và giữ nguyên các limit để dễ tìm nguyên nhân. Sau đó mới xét có cần thêm thành phần nào.

## 4. Bước A — loại bỏ lỗi khiến tuning không có ý nghĩa

### A1. Cảm biến và mốc ngang

Ba thao tác dưới đây có tên gần giống nhau nhưng giải quyết ba việc khác nhau:

| Thao tác | Thực sự làm gì? | Khi nào dùng? |
|---|---|---|
| **Calibrate** | Thu mẫu đứng yên để ước lượng lệch gyro | Khi khởi động hoặc cần hiệu chuẩn lại; giữ bo đứng yên |
| **Set firmware level reference** | Đặt tư thế hiện tại làm mốc góc cho bộ điều khiển trên ESP32 | Khi drone thật sự ngang, motor dừng và chưa Arm |
| **Zero orientation** | Đổi mốc hiển thị mô hình 3D trong app | Khi muốn chỉnh cách nhìn; không thay mốc cân bằng firmware |

1. Khi motor dừng, đặt drone ngang và giữ yên cho hiệu chuẩn gyro hoàn tất.
2. Xác nhận sensor READY và dữ liệu mới đang cập nhật.
3. Quan sát góc/rate khi đứng yên. Rate không cần đúng tuyệt đối 0, nhưng nếu trôi hoặc nhảy nhiều thì cần kiểm tra cảm biến, lắp đặt và nguồn.
4. Đặt mốc bằng **Set firmware level reference** khi thực sự ngang. **Zero orientation** chỉ đổi hình 3D.
5. Nghiêng chậm từng trục khi motor dừng để kiểm tra trục nào thay đổi và dấu có nhất quán không.

Không lấy một tư thế đang nghiêng làm “ngang” chỉ để số FW angle đẹp. Nếu tư thế ngang thật mà firmware báo lệch lớn, kiểm tra cách gắn IMU/mapping trước.

### A2. Thứ tự motor và dấu phản hồi

Kiểm tra manual với **cánh đã tháo**, xác nhận bảng:

| Motor | Vị trí | GPIO |
|---|---|---|
| LF | Trái trước | 14 |
| RF | Phải trước | 15 |
| LR | Trái sau | 12 |
| RR | Phải sau | 13 |

Theo xác nhận hiện có, trái thấp/phải cao cho FW Roll dương. Code đã đặt `RollMotorDirection=-1`. Ở target 0°, giữ yên, chỉ P hoạt động, Roll dương phải tạo **LF/LR lớn hơn RF/RR**; Roll âm cho quan hệ ngược lại. Pitch chưa được xác minh chiều riêng trên phần cứng.

Nếu lệnh tăng nhầm phía, dừng và sửa mapping/chiều trước. Đừng thử sửa phản hồi ngược bằng tăng gain hoặc limit. Khi drone đang chuyển động nhanh hoặc I/D có đóng góp, dấu hiệu chỉnh còn phụ thuộc rate; kiểm tra chiều ban đầu ở trạng thái giữ yên giúp giảm nhầm lẫn đó.

### A3. Đế và công suất thực

Đế phải cố định được drone nhưng cho trục đang thử quay trong phạm vi phù hợp, có khoảng hở cánh và phương tiện dừng. Không giữ thân hoặc tạo tác động bằng tay gần cánh đang quay. Bố trí tư thế ban đầu/chuyển động thử trước Start hoặc dùng cơ cấu thử phù hợp.

Tháo cánh giúp kiểm tra điện/PWM, **không chứng minh đủ lực cân bằng khi có cánh**. Khi đánh giá lực đẩy trên đế, cần điều kiện thử cơ khí phù hợp riêng. Đế khóa trục, ma sát lớn, trọng tâm lệch, cánh sai hoặc nguồn sụt đều có thể làm kết quả giống “gain quá nhỏ”.

**Chỉ chuyển sang chỉnh gain sau khi các kiểm tra trên đạt.**

## 5. Bước B — xác định base duty có thể dùng cho lượt thử

Base không phải tham số thay cho Kp. Nó quyết định mức lệnh nền chung; gain quyết định chênh lệch để sửa. Cùng Kp nhưng base khác có thể cho phản ứng khác vì quan hệ duty–lực đẩy không được tuyến tính hóa trong code này.

Không có dữ liệu lực đẩy, tải và pin thực tế nên tài liệu không chốt “base 75/90 là đúng”. Thực hiện kiểm tra motor riêng để tìm mức chạy đều; ghi nhận motor nào khởi động muộn hoặc phản ứng khác nhau. Sau đó chọn mức nền cho phép tạo đáp ứng trên đế trong điều kiện đã kiểm soát, thay đổi từng bước và Stop giữa các cấu hình.

Giữ **cùng base và điều kiện pin** trong một chuỗi so sánh gain. Nếu cần đổi base vì không đủ lực hoặc phản ứng quá mạnh, ghi đó là một chuỗi thử khác và đánh giá lại các gain đã chọn.

### Khoảng dự phòng ở hai đầu

Hãy tạm bỏ công thức và xem **hai nhóm motor ở một trục**. Với Roll, đó là nhóm trái LF/LR và nhóm phải RF/RR. Để nghiêng drone theo một chiều, bộ trộn tăng lệnh một nhóm và giảm nhóm còn lại quanh base.

Ví dụ base=90 và độ lớn phần sửa là 20 duty:

```text
Một nhóm: 90 + 20 = 110 duty
Nhóm còn lại: 90 − 20 = 70 duty
```

Ở đây chỉ xét độ lớn để hiểu khoảng trống. Nhóm nào tăng phụ thuộc chiều sửa; trong code hiện tại còn có dấu `RollMotorDirection=-1` trước bộ trộn. Dù chiều nào, cả nhóm tăng lẫn nhóm giảm đều phải nằm trong 0–220.

**Khoảng dự phòng** là số duty có thể tăng hoặc giảm thêm từ base trước khi đụng biên:

- Từ base=90 xuống 0 còn **90 duty để giảm**.
- Từ base=90 lên 220 còn **130 duty để tăng**.
- Muốn hai nhóm cùng thực hiện được `base ± phần sửa`, độ lớn phần sửa phải vừa với **cả hai khoảng**. Khoảng nhỏ hơn ở đây là 90.

Đây là lý do xuất hiện chữ `min`: nó nghĩa là **lấy số nhỏ hơn**, vì đầu chật hơn quyết định mức sửa đối xứng còn làm được.

Với chỉ một trục, độ lớn phần sửa không gây cắt motor bị ràng buộc bởi:

```text
phần sửa ≤ min(base, 220 − base)
```

Ví dụ base=20, output yêu cầu 40 thì một nhóm có thể cần duty âm và sẽ bị cắt về 0. Tăng limit không tạo thêm khoảng dự phòng ở phía dưới.

Viết đầy đủ ví dụ đó:

```text
Base = 20, phần sửa yêu cầu = 40

Nhóm tăng: 20 + 40 = 60          → nhận 60
Nhóm giảm: 20 − 40 = −20        → chỉ được nhận 0

Yêu cầu chênh lệch hai nhóm: 60 − (−20) = 80 duty
Chênh lệch sau khi bị cắt:   60 − 0     = 60 duty
```

Bộ điều khiển đã tính một cách phân bổ mà motor không thực hiện đầy đủ được. **CLIPPED/cắt lệnh** là hiện tượng này. Nó không phải phép đo motor bị hỏng và cũng không có nghĩa cứ tăng base là giải quyết xong.

So sánh ba mức để đọc công thức dễ hơn:

| Base, chỉ là ví dụ | Có thể giảm đến 0 | Có thể tăng đến 220 | Khoảng sửa đối xứng không cắt |
|---|---|---|---|
| 20 | 20 | 200 | 20 duty |
| 90 | 90 | 130 | 90 duty |
| 180 | 180 | 40 | 40 duty |

Với base=90, khoảng trống điện cho một trục là 90 nhưng **Inner vẫn chỉ cho output tối đa 40** theo cấu hình mặc định. Bạn đang gặp hai ràng buộc riêng: giới hạn PID và giới hạn motor. Bảng trên không khuyên chọn base nào; base còn phải phù hợp lực đẩy, đế và nguồn.

Với BOTH, Roll và Pitch có thể cộng cùng chiều trên một motor. Muốn không cắt lệnh, cần kiểm tra từng motor; một điều kiện bảo thủ là:

```text
|motorRoll| + |motorPitch| ≤ min(base, 220 − base)
```

Ví dụ base=50, phần Roll dùng trộn là +30 và phần Pitch dùng trộn là +30. Một motor có thể nhận `50 − 30 − 30 = −10`, còn motor đối diện nhận `50 + 30 + 30 = 110`. Mỗi phần sửa riêng đều nhỏ hơn 40 nhưng khi cộng lại vẫn có motor bị cắt về 0. Bởi vậy, từng trục riêng tốt chưa bảo đảm BOTH cũng tốt.

Hai dấu `| |` nghĩa là **lấy độ lớn, bỏ dấu chiều**: `|−30| = 30`. Điều kiện cộng hai độ lớn là cách kiểm tra bảo thủ để đủ khoảng trống cho các tổ hợp dấu; muốn biết lượt đang chạy có cắt thật không, vẫn xem từng PWM và cờ Mixer.

Đây chỉ là **khoảng dự phòng lệnh điện**, không chứng minh motor còn đủ lực đẩy hoặc pin còn đáp ứng được.

**Vậy sau khi tính xong cần làm gì?** Trong lượt thử, xem Limited output và bốn PWM. Nếu output chưa tới biên mà chuyển động yếu, kiểm tra lực đẩy/đế rồi mới cân nhắc Kp. Nếu output đã sát ±40 nhưng motor chưa bị cắt, kiểm tra khả năng bám rate trước khi xét limit. Nếu Mixer báo cắt, hãy kiểm tra base và tổng hai trục; tiếp tục tăng Kp/limit không lấy lại phần lệnh đã mất ở đầu biên.

## 6. Bước C — chuẩn hóa một lượt thử

### C1. Chuẩn bị các ô trên app

Với lượt đầu chỉ thử Roll, dùng bảng này để tránh nhập nhầm tầng. **B** bên dưới là base bạn đã kiểm tra ở mục 5; không nhập chữ B vào app.

| Field | Thiết lập cho lượt đầu | Vì sao? |
|---|---|---|
| Angle Roll Kp / Ki / Kd | 2 / 0 / 0 | Giữ yêu cầu từ vòng góc cố định khi đánh giá Inner |
| Angle Pitch Kp / Ki / Kd | 2 / 0 / 0 | Giữ cấu hình mặc định; chưa thử chỉnh Pitch |
| Inner rate Roll Kp / Ki / Kd | Một Kp dương nhỏ; Ki=0, Kd=0 | Chỉ đánh giá tác động của P trước; xem ứng viên ở mục 7 |
| Inner rate Pitch Kp / Ki / Kd | Có thể giữ 0 / 0 / 0 nếu chưa có bộ đã đo | Pitch chưa tham gia khi Control axis=ROLL |
| Base duty | B | Giữ mức nền đã kiểm tra; không thay cùng gain |
| Roll / pitch target | 0 / 0 | Yêu cầu giữ mốc ngang firmware |
| Control axis | ROLL | Chỉ cho bộ điều khiển sửa Roll |
| Plot axis | ROLL | Xem đúng dữ liệu Roll; chọn Plot axis không đổi trục điều khiển |

Nếu đã có bộ thông số đo tốt trước đó, lưu bộ đó trước khi nhập ứng viên mới. Khi chỉ thử Roll, không cần ép Inner Pitch Kp thành số dương để “cho đủ”; khi chuyển sang PITCH/BOTH thì các vòng tham gia phải có Kp dương và được board xác nhận.

### C2. Chạy và ghi một lượt

Mỗi lượt phải đủ giống lượt trước để so sánh được:

1. Stop/disarm. Chọn trục **ROLL** trước, target ban đầu 0/0 và base đã kiểm tra.
2. Đặt Outer Roll/Pitch **2/0/0**. Ban đầu đặt Inner của trục thử với **Ki=0, Kd=0**, Kp là một ứng viên dương nhỏ. Giữ trục chưa thử chưa tham gia điều khiển.
3. Apply PID, chờ và đối chiếu gain board phản hồi. Giá trị trong ô nhập chưa đủ để xác nhận đã áp dụng.
4. Bật CSV. Ghi tên/mã lượt thử và cấu hình vào sổ theo mẫu ở cuối tài liệu.
5. Tạo cùng một điều kiện thử nhỏ, chẳng hạn đặt trước góc lệch khoảng ±2° trên đế phù hợp. Không cần thử góc lớn chỉ vì firmware đã bỏ chặn theo góc đo.
6. Xác nhận đế, Arm rồi Start trong 15 giây. Quan sát ngắn ban đầu; Stop ngay nếu phản hồi sai chiều, dao động tăng nhanh, va đế, lỗi nguồn hoặc lỗi firmware.
7. Nếu đáp ứng ổn định trong thử ngắn, mới kéo dài thời gian quan sát theo tiêu chí đã đặt. Stop, lưu log và nhận xét.
8. Đổi **một** tham số; lặp lại cùng điều kiện. Không đổi gain, base, target và giới hạn cùng lúc.

Bạn có thể dùng các lượt riêng target +2° và −2° từ tư thế gần ngang để đánh giá hai chiều. Vì không đổi target trực tiếp khi chạy, đây là các lượt Start riêng, không phải một bước nhảy target liên tục trong cùng phiên điều khiển.

Đừng nhầm **góc bắt đầu +2°** với **target +2°**:

- Bắt đầu ở +2°, target=0°: đang thử khả năng trở về ngang.
- Bắt đầu gần 0°, target=+2°: đang thử khả năng đi tới và giữ một góc nghiêng nhỏ.

Cả hai đều có thể dùng cho một bài thử được chuẩn bị phù hợp, nhưng phải ghi rõ mình đang làm bài nào. Không so thời gian của hai loại này như thể chúng cùng một điều kiện.

### C3. Sau khi bấm Start, nhìn gì trước?

1. **Control telemetry có RUNNING và age nhỏ không?** Nếu chưa, xử lý kết nối/trạng thái; chưa kết luận gain.
2. **Chiều phản ứng có đúng không?** Ở điều kiện ban đầu đứng yên, nếu có dấu hiệu rõ ràng càng nghiêng thêm do tăng nhầm phía, Stop và kiểm tra mapping.
3. **Rate đo có tiến về rate target không?** Nếu yêu cầu nhỏ mà thực tế không đổi, xem output và PWM để biết lệnh đã được tạo hay chưa.
4. **Góc có tiến về target, có vượt và lắc kéo dài không?** Đây là kết quả của cả hai vòng, cần xem cùng rate.
5. **Output/Mixer có chạm biên không?** Nếu có, ghi lại trước khi quyết định tăng gain.

Mỗi lượt kết thúc bằng một nhận xét cụ thể, chẳng hạn: “đúng chiều, chưa chạm limit, góc hồi chậm; lượt sau chỉ tăng Inner Kp một bước”. Tránh ghi mỗi “chưa được” vì lần sau sẽ không biết nên thay gì.

### C4. Vì sao chỉ đổi một tham số?

Giả sử bạn cùng tăng Kp, tăng base và thêm D rồi drone bớt lắc: chưa biết thay đổi nào giúp ích, cũng chưa biết thay đổi nào có thể làm xấu khi pin khác đi. Thay một yếu tố giúp câu hỏi của mỗi lượt rõ ràng: **“Giữ mọi thứ còn lại tương đương, riêng thay đổi này có làm tốt hơn không?”** Nếu chưa thể giữ điều kiện giống nhau, ghi điểm khác đó và chưa chốt kết luận chắc chắn.

### C5. Nếu nút chưa bấm được, kiểm tra luồng trước

| Bạn muốn làm gì? | Cần kiểm tra trước |
|---|---|
| Sửa gain và Apply | Đã kết nối, firmware được nhận diện, đang dừng/disarm và các ô nhập hợp lệ; chờ thao tác trước hoàn tất |
| Arm balance | Sensor READY, đã xác nhận đế/mapping, gain của các vòng đang tham gia đã được board xác nhận với Kp dương |
| Start balance | Đã Arm thành công, chưa quá 15 giây; base và target hợp lệ |
| Đổi gain/target sau khi Arm | Stop/disarm, sửa và Apply nếu đổi gain, rồi thực hiện Arm/Start lại |

**Apply thành công chưa làm motor chạy. Arm thành công cũng chưa bắt đầu cân bằng.** Start mới yêu cầu chạy bộ điều khiển. Nếu nhận lỗi, đọc đúng trạng thái/lỗi mới nhất thay vì tăng gain để thử mở khóa nút.

## 7. Bước D — chọn Kp vòng tốc độ

Giữ Outer=2/0/0 và Inner Ki=Kd=0. Với trục ROLL, thay riêng **Inner rate Roll Kp**, không phải ô Angle Roll Kp.

P phản ứng ngay với sai số tốc độ hiện tại:

```text
P vòng tốc độ = Inner Kp × (rate target − measured rate)
```

Kp lớn hơn nghĩa là cùng một sai số sẽ tạo phần sửa lớn hơn, **cho tới khi output bị giới hạn**. Nó không có nghĩa motor luôn tăng cùng một lượng, vì sai số thay đổi trong lúc drone chuyển động.

### D1. Khởi đầu từ đâu khi chưa có dữ liệu?

Có thể dùng **0.1/0/0 như một ứng viên thử chưa xác minh**, sau khi điều kiện phần cứng/đế ở các bước trước đã đạt. Đây không phải bảo đảm an toàn hoặc cân bằng. Nếu đã có một mức thấp được đo đáp ứng tốt trên chính hệ thống, dùng mức đó làm mốc thay vì bỏ qua dữ liệu cũ.

Không nhảy thẳng đến 1.7 chỉ vì từng nhập con số ấy trên app. Với sai số tốc độ 30°/s, Kp=1.7 đã tạo P=51 duty, vượt output limit 40 trước khi cộng I/D.

### D2. Nhìn rõ ảnh hưởng của lượng tử hóa PWM

Ở góc +2°, target=0°, gyro≈0 và Outer Kp=2, độ lớn sai số tốc độ khoảng 4°/s. Với base là số nguyên và chỉ có P:

| Kp tốc độ, ví dụ | Độ lớn output trước làm tròn | Chênh lệch motor quanh base có thể thấy |
|---|---|---|
| 0.1 | 0.4 duty | Hai bên có thể vẫn bằng base sau làm tròn |
| 0.2 | 0.8 duty | Khoảng ±1 duty |
| 0.5 | 2 duty | ±2 duty |
| 1.7 | 6.8 duty | Khoảng ±7 duty |

Bảng này mô tả phép tính, không phải lộ trình bắt buộc tăng đến 1.7. Motor không đổi rõ ở gain nhỏ có thể do độ phân giải, không phải app chưa chạy. Ngược lại, thấy chênh lệch 1 duty cũng không bảo đảm đủ mô-men thắng ma sát.

**Độ phân giải ở đây là gì?** PID tính được số lẻ nhưng lệnh cuối motor là số nguyên. Ví dụ quanh base=90, yêu cầu 90.4 và 89.6 đều có thể làm tròn thành 90. Vì vậy, **Limited output=0.40** và **hai nhóm PWM đều 90** có thể cùng đúng. Đây là lý do cần nhìn cả output trước bộ trộn lẫn PWM sau làm tròn.

### D3. Tăng hay giữ?

- Nếu output chưa chạm giới hạn, chiều đúng, chuyển động nhỏ và ổn định nhưng rate đo còn theo rate target kém: có thể thử một mức Inner Kp cao hơn. Nếu rate đã bám được target mà góc vẫn hồi chậm, xem lại yêu cầu của Outer ở mục 10 thay vì mặc định tăng Inner.
- Ở vùng rất nhỏ, ví dụ tăng từ 0.10 lên 0.15 là một bước thử; ở vùng đã có đáp ứng rõ, dùng bước nhỏ hơn theo tỷ lệ, chẳng hạn khoảng 10% để so sánh. Các bước này là ví dụ kế hoạch thí nghiệm, không phải quy luật bắt buộc.
- Nếu rung/dao động tăng hoặc đáp ứng xấu hơn: Stop và quay về bộ gần nhất đã tốt hơn. Không cố tăng đến khi xuất hiện dao động lớn để tìm biên.
- Nếu rate không theo nhưng output giữ ở ±40: chuyển sang chẩn đoán limit/lực đẩy, không tiếp tục tăng Kp vì output đang bị cắt.

So sánh **rate đo với rate mục tiêu** và **góc với target** cùng lúc. Vì Outer vẫn hoạt động, dao động của toàn bộ chuỗi không thể chỉ từ một ảnh mà quy chắc chắn cho Inner Kp.

### D4. Ví dụ cách ra quyết định, không phải kết quả đo của drone

Giả sử bạn giữ cùng base, Outer và bài thử:

| Lượt giả định | Chỉ thay gì? | Quan sát giả định | Bước tiếp theo hợp lý |
|---|---|---|---|
| A | Chọn một Inner Kp nhỏ | Có output nhưng PWM làm tròn gần như bằng nhau, góc chưa hồi | Kiểm tra lượng tử hóa/ma sát; có thể thử Kp tăng một bước nếu các điều kiện khác đạt |
| B | Tăng riêng Inner Kp | Đúng chiều, góc hồi rõ hơn, dao động tắt và chưa sát biên | Lưu ứng viên, thử lặp lại cả hai chiều |
| C | Tăng riêng Inner Kp thêm | Vượt góc nhiều hơn, lắc kéo dài hơn B | Quay lại B; chưa tăng tiếp |
| D | Giữ Kp của B, thử thêm D nếu còn lý do | So sánh độ vượt, thời gian tắt dao động và độ nhiễu | Chỉ giữ D nếu cải thiện lặp lại được |

Mục đích là tìm bằng chứng cho từng thay đổi, không phải buộc mọi hệ thống phải đi qua đủ bốn lượt. Nếu ngay A xuất hiện lỗi nguồn hoặc sai chiều, dừng chuỗi và sửa lỗi đó trước.

## 8. Bước E — cân nhắc Kd vòng tốc độ

Chỉ thêm D sau khi chiều đúng, nguồn/đế hợp lý và đã có một mức P tạo đáp ứng có thể đánh giá. Nếu P-only đã đạt tiêu chí của phạm vi thử, chưa cần thêm một biến mới chỉ để đủ chữ PID.

Giữ Kp và Ki=0, thử Kd dương nhỏ. **0.001 là ví dụ bước thử chưa được xác minh**, không phải số bắt buộc. D của code tác động theo thay đổi rate đo, qua bộ lọc 10 Hz.

Nói dễ hiểu: P nhìn “còn thiếu tốc độ bao nhiêu”; D nhìn “tốc độ đo đang thay đổi nhanh thế nào” và tạo đóng góp ngược với sự thay đổi đó. Vì D ở đây lấy từ **rate đo**, không nên hiểu nó là “góc vừa vượt 0 thì phanh” hoặc luôn làm giảm lệnh motor. D có thể dương hoặc âm tùy chuyển động tại thời điểm đó.

So sánh với log P-only:

| Kết quả sau thêm D | Quyết định thử tiếp |
|---|---|
| Vượt góc giảm, dao động tắt nhanh hơn, motor không rung bất thường | Có cơ sở giữ ứng viên để thử lặp lại |
| Gần như không khác, D đóng góp rất nhỏ | Có thể do hiệu ứng nhỏ/độ phân giải; cần dữ liệu trước khi tăng thêm |
| Motor giật/rung, đường D nhiễu mạnh, đáp ứng xấu đi | Giảm hoặc bỏ D; kiểm tra rung và dữ liệu gyro |

Không thay cả Kd lẫn tần số lọc D cùng lúc. Đồ thị 20 Hz không đủ để loại trừ rung/timing nhanh của vòng 200 Hz, và không thể tái tạo chính xác D nội bộ chỉ từ vài điểm gyro trên CSV.

**Khi nào chưa nên thêm D?** Nếu chiều sửa sai, motor chưa đủ lực, output đang giữ biên hoặc dữ liệu đứng yên đã nhiễu rõ, hãy xử lý nguyên nhân đó trước. D không tạo thêm khoảng trống PWM và không sửa được motor lắp nhầm vị trí.

## 9. Bước F — chỉ thêm Ki vòng tốc độ khi có lý do

Ki hữu ích khi sai lệch tốc độ nhỏ nhưng tồn tại kéo dài. Trước đó cần loại trừ sai mốc, đế bị kẹt, chênh lệch motor, pin yếu và đầu ra bão hòa.

Hình dung I như phần ghi nhớ: P có thể đang tạo phần sửa nhỏ nhưng sai số vẫn cùng dấu qua nhiều lần tính; I tăng dần để bổ sung một mức sửa duy trì. Khác P, tác động của I phụ thuộc **cả sai số lẫn thời gian đã tồn tại**. Vì vậy, tăng Ki rồi nhìn ngay một khoảnh khắc thường chưa đủ để đánh giá.

Phải nhìn **sai số rate** khi đánh giá Inner Ki. Chỉ thấy góc còn lệch chưa chứng minh cần tăng Inner Ki, vì góc còn phụ thuộc mốc ngang, vòng góc và cơ khí.

Giữ P/D đã chọn, thêm Ki nhỏ khi có số đo cho thấy cần. **0.01 là ví dụ ứng viên ban đầu chưa xác minh.** Quan sát lâu hơn so với việc nhìn phản ứng P tức thời, nhưng dừng nếu có dao động chậm tăng lên hoặc motor bất thường.

Ví dụ ước lượng tác động, giả sử errorRate luôn 10°/s trong 2 giây và không bị khóa/cắt I:

```text
Mức tăng I xấp xỉ = Ki × errorRate × thời gian
Ki=0.01 → tăng khoảng 0.2 duty
Ki=0.05 → tăng khoảng 1 duty
```

Đây là phép tính để hiểu cỡ tác động, không giả định errorRate thực tế luôn cố định. Thay Ki cần quan sát đường **I**, output và góc để biết tích lũy có làm tốt hơn không.

Khi mixer bão hòa, hoặc tầng trong đã hết biên theo điều kiện code, tích lũy có thể bị giữ. Không thấy I tăng chưa chắc Ki không được nhận. I chạm ±20 liên tục cũng chưa chứng minh nên tăng integralLimit; cần tìm nguyên nhân sai lệch tồn tại.

**Anti-windup** là cơ chế hạn chế việc I tiếp tục tích lũy theo hướng làm đầu ra vượt biên. Nếu lệnh đã hết khả năng sửa mà I vẫn tăng mãi, khi tình huống thay đổi bộ điều khiển có thể còn giữ quá nhiều phần sửa cũ. Code có giới hạn I và điều kiện cho phép tích lũy; chúng giúp kiểm soát hiện tượng này nhưng không thay thế việc chọn gain và kiểm tra phần cứng.

Sau mỗi Stop/Start, code reset I. So sánh các lượt cùng thời gian tính từ Start, thay vì so một lượt vừa bắt đầu với lượt đã tích lũy lâu.

## 10. Bước G — mới đánh giá Kp vòng góc

Sau khi rate đã phản ứng hợp lý trong phạm vi thử, giữ Inner cố định rồi xem **Angle Roll Kp**.

- Rate bám mục tiêu tương đối tốt nhưng hồi góc quá chậm: xem rate mục tiêu có quá nhỏ do gain góc, hay đang bị limit 45 chặn.
- Nếu rate mục tiêu chưa chạm ±45 và đáp ứng còn chậm, có cơ sở thử tăng Kp góc một bước nhỏ.
- Ví dụ so 2.0 với 2.2 là một thử nghiệm tăng 10%, **không phải yêu cầu luôn phải tăng**.
- Nếu vượt góc/dao động lớn hơn hoặc Inner thường xuyên hết biên, quay về mức trước và kiểm tra sự phù hợp hai tầng.

Nếu góc +5°, Outer Kp=2 → rate mục tiêu −10°/s; tăng lên 2.2 → −11°/s. Tức là bạn đang yêu cầu Inner thực hiện chuyển động nhanh hơn, không trực tiếp “tăng lực motor thêm 0.2”.

Hai tình huống dưới đây có thể cùng cho cảm giác “về ngang chậm” nhưng cách xử lý khác nhau:

| Dữ liệu đang thấy | Cách hiểu | Nên kiểm tra gì trước? |
|---|---|---|
| Rate target=−4°/s, measured rate quanh −4°/s | Inner đang theo được yêu cầu tương đối chậm | Yêu cầu của Outer có phù hợp thời gian hồi mong muốn không? |
| Rate target=−10°/s, measured rate gần 0, output=−40 | Inner chưa thực hiện được yêu cầu và đã ở biên | Nguồn, lực đẩy, đế, base và khả năng sửa; chưa tăng Outer |

Đây là các ví dụ đọc số tại một đoạn thử, không phải chỉ tiêu bắt buộc. Hãy xem xu hướng qua nhiều mẫu, vì một điểm trễ ngay sau Start có thể là quá trình chuyển động bình thường.

Outer Ki/Kd ban đầu giữ 0. Không cần bật cả hai để có một bộ điều khiển hoàn chỉnh: cấu trúc P góc → PID tốc độ là một cách triển khai được dùng trong ArduPilot. [Nguồn ArduPilot](https://ardupilot.org/dev/docs/apmcopter-programming-attitude-control-2.html)

Nếu còn sai góc kéo dài, kiểm tra ZERO, cơ khí và Inner trước khi thêm Outer Ki. Bật I ở cả hai tầng cùng lúc làm khó xác định nguồn tích lũy. Nếu có nhu cầu thật sự dùng Outer Ki/Kd, thử từng hệ số riêng, bắt đầu bằng tác động nhỏ dựa trên số đo và giữ toàn bộ bộ tốt trước đó để có thể quay lại.

Outer Ki tích lũy **sai số góc** thành phần bổ sung cho rate target; Inner Ki tích lũy **sai số rate** thành phần bổ sung duty. Hai hệ số cùng tên Ki nhưng đang làm việc với hai đại lượng khác nhau, nên không sao chép số từ tầng này sang tầng kia. Outer Kd ở code lấy thay đổi góc đo qua bộ lọc để đóng góp vào rate target; cũng không có cùng ý nghĩa đơn vị với Inner Kd.

App hiện chưa hiển thị riêng ba thành phần P/I/D của Outer; các số **P / I / D trong RATE PID** chỉ là vòng tốc độ. Có thể xem rate target để biết tổng đầu ra Outer, nhưng chưa thể dùng số I đó để xác nhận Outer I đang tích lũy bao nhiêu. Vì vậy, nếu chưa có lý do và phép đo đủ rõ, hãy giữ Outer Ki/Kd=0.

## 11. Bước H — lặp lại cho Pitch rồi mới BOTH

1. Lưu lại bộ Roll tốt nhất trong phạm vi đã thử.
2. Xác minh chiều Pitch trên phần cứng. Hiện code giữ `PitchMotorDirection=+1`, chưa được xác nhận như Roll.
3. Chọn PITCH và lặp quy trình base cố định, P → đánh giá D → đánh giá I → góc. Base tương tự chưa chắc đáp ứng giống Roll do mô-men quán tính/đế khác.
4. Khi từng trục đã hợp lý, chọn BOTH với thử nghiệm nhỏ. Kiểm tra bốn PWM vì hai hiệu chỉnh có thể cộng nhau làm một motor chạm biên.
5. Nếu BOTH xấu dù từng trục riêng ổn, kiểm tra coupling giữa trục, biên motor, nguồn và cấu trúc đế; không chỉ nhân đồng loạt mọi gain.

Giá trị Roll/Pitch có thể khác nhau. Không có yêu cầu chúng phải bằng nhau chỉ vì giao diện hoặc một video gộp chung hai trục.

## 12. Xác định lại limit 45°/s và 40 duty bằng dữ liệu

### 12.1. Phân biệt ba loại chạm biên

| Loại | Quan sát ở đâu? | Ý nghĩa |
|---|---|---|
| Outer chạm ±45°/s | BODY RATE: Target và **AT LIMIT** | Lệnh tốc độ ở biên cho phép, không còn khoảng tăng theo chiều đó trong cấu hình này |
| Inner chạm ±40 duty | RATE PID: Output và **AT LIMIT** | Phần sửa duty ở biên trước bộ trộn |
| Mixer vượt 0–220 | **Mixer: CLIPPED**, `saturated=1`; PWM ở đầu biên | Có lệnh motor trước clamp vượt phạm vi và bị cắt |

Motor đúng bằng biên chưa chắc làm saturation=1: code chỉ gắn cờ nếu giá trị **trước clamp vượt** biên. Cờ saturation=0 không loại trừ Inner/Outer chạm giới hạn.

**AT LIMIT không tự chứng minh kết quả trước giới hạn đã vượt biên.** Giá trị vừa bằng biên hoặc làm tròn telemetry rất sát biên cũng có chỉ báo. Muốn kết luận limit đang cản đáp ứng, phải xem thêm sai số còn lại và việc giữ ở biên qua một đoạn thời gian.

Ví dụ tổng Inner P+I+D=111 nhưng Limited output=40, base=90 và chỉ Roll: sau đổi dấu Roll ở bộ trộn, PWM có thể là LF/LR=50 và RF/RR=130. Inner đã bị giới hạn, nhưng motor chưa cần lệnh ngoài 0–220 nên Mixer vẫn không báo cắt. **Đó là lý do phải nhìn ba tầng riêng.**

Code dừng nếu mixer vượt biên liên tục hơn khoảng 0,5 giây. Sau Stop các trạng thái/output được reset; ảnh chụp khi đã dừng có thể không giữ dấu hiệu trước lỗi. Đọc thông báo firmware và đoạn CSV ngay trước Stop để tìm nguyên nhân.

### 12.2. Khi nào có cơ sở thử tăng Inner outputLimit?

Chỉ xem xét khi dữ liệu cho thấy cùng lúc: rate chưa theo yêu cầu, output đang ở ±40 đủ lâu để ảnh hưởng, chiều đúng, base/nguồn/motor/cánh/đế đã được kiểm tra, và còn khoảng dự phòng lệnh cùng khả năng cơ khí thực.

Không tăng chỉ vì phần P lớn hơn 40. Nếu motor đã chạm 0/220 hoặc lực đẩy không đáp ứng, tăng limit có thể chỉ kéo dài bão hòa và làm tình huống xấu hơn.

Nếu đáp ứng quá mạnh hoặc hay dùng phần sửa lớn trong phạm vi thử nhỏ, mức limit thấp hơn cũng có thể là một ứng viên cần so sánh. Chọn mức đủ đáp ứng yêu cầu có dự phòng, không chọn mức lớn nhất mà app/code cho phép.

### 12.3. Khi nào có cơ sở thử tăng Outer outputLimit?

Nếu rate mục tiêu giữ ở ±45, gyro theo được lệnh đó và hồi góc vẫn chậm hơn yêu cầu, có cơ sở nghiên cứu mức yêu cầu cao hơn trong phạm vi đế cho phép. Nếu gyro chưa theo được ±45 thì đẩy target rate lên nữa chưa giải quyết được nguyên nhân.

Với Outer Kp=2, chỉ P, giới hạn 45 bắt đầu tác động khi độ lớn sai góc lớn hơn 22.5°. Nếu bạn chỉ thử ±2° và rate target chỉ khoảng ±4°/s, thay 45 thành 60 không giúp lượt thử này nhanh hơn.

### 12.4. Thay limit thế nào để biết hiệu quả?

Trong [FlightController.h](../../firmware/DroneMotionMpu6050/FlightController.h):

- `PID.outputLimit=40` và `integralLimit=20` là mặc định, dùng cho Inner trong cấu hình hiện tại.
- Constructor gán riêng `rollAnglePID` và `pitchAnglePID`: `outputLimit=45`, `integralLimit=10` cho Outer.
- Motor 0–220 được kiểm tra ở mixer và MotorControl. Không nâng riêng một chỗ rồi quên giới hạn ở chỗ khác.

Trong các bước đầu, giữ nguyên limit. Khi đã có bằng chứng cần thay, chọn **một** limit, ghi giá trị trước/sau, build/nạp, Apply lại cùng gain, thử lại cùng điều kiện và so số đo. Tắt Pause, xem nhãn limit ở góc phải BODY RATE/RATE PID và rê chuột để xem đủ **Output / I** nhằm xác nhận firmware mới thực sự báo giới hạn vừa đổi; đừng chỉ dựa vào file đã sửa trong IDE. Ví dụ so 40 với 44 hoặc 45 với 49.5 là kế hoạch thay đổi 10%, không phải khuyến nghị mọi drone nên tăng đến các số đó.

Giữ integralLimit cố định trong lượt đánh giá outputLimit, và ngược lại. Khi đổi limit, cần xem lại quá trình anti-windup và kết quả gain vì vùng bão hòa đã khác.

## 13. Dùng CSV để đưa ra quyết định

### 13.1. Ghi file như thế nào?

1. Khi đang Stop, hoàn tất Apply và xác nhận gain.
2. Bấm **Start / stop CSV log**; kiểm tra dòng phía dưới đã hiện đường dẫn file thay vì `CSV logging off`.
3. Arm/Start, thực hiện bài thử đã chuẩn bị rồi Stop.
4. Bấm nút CSV lần nữa để đóng file sau khi ghi đủ đoạn cần giữ.
5. Ghi nhận xét theo mẫu bên dưới, kèm tên CSV để lần sau tìm được.

File nằm trong **Documents/DroneMotionLogs** của người dùng Windows. Nút CSV chỉ bật/tắt ghi dữ liệu; **Pause/Clear plots** chỉ tác động đồ thị. Không nút nào trong ba nút đó thay thế **STOP / DISARM**.

CSV của app mới thêm build, base/trục lần Start đã chấp nhận, limit output/I từng vòng và biên motor từ CONFIG. Gain lấy từ phản hồi board, không lấy từ ô nhập chưa Apply. Thiếu CONFIG thì metadata tương ứng để trống; thiếu C2 thì bốn cột rate target/output để trống. Cấu hình lần Start gần nhất vẫn có thể xuất hiện ở mẫu `active=0`; vì vậy luôn đọc cờ active.

### 13.2. Chọn đúng đoạn để so sánh

Chỉ phân tích đoạn dữ liệu mới của lượt chạy cần xét, có `active=1`. Đối chiếu đúng target, gain board báo và trục đang chạy. `micros` là thời gian ESP32, cần xử lý reset/tràn; không coi toàn bộ file nhiều lượt là một đoạn liên tục.

### 13.3. Các đại lượng có thể tính

| Đại lượng | Cách hiểu/cách tính |
|---|---|
| Sai số góc | `targetAngle − measuredAngle` |
| Sai số rate | `rateTarget − measuredRate`, cần firmware C2 |
| Thời gian vào vùng chấp nhận | Từ mốc bắt đầu thử đến khi góc vào vùng đã chọn và giữ ở đó trong cửa sổ đánh giá |
| Sai số góc trung bình tuyệt đối | Lấy độ lớn sai số từng mẫu rồi tính trung bình trên cùng một cửa sổ thời gian |
| Vượt góc | Độ lệch xa nhất sang phía bên kia mục tiêu sau khi đi qua nó |
| Mức dùng output | Tỷ lệ mẫu có độ lớn `rollOutput` hoặc `pitchOutput` gần limit của trục đang xét |
| Mixer vượt biên | Tỷ lệ mẫu `saturated=1` và motor nào thường chạm trần/sàn |

Nếu dùng ngưỡng “gần limit” như 98% limit, ghi rõ đó là quy ước phân tích của bạn, không phải cờ phần mềm. Tỷ lệ đếm theo mẫu chỉ xấp xỉ thời gian khi mẫu đều; có khoảng mất dữ liệu thì nên dùng chênh lệch thời gian hợp lệ để cân trọng số.

CSV/đồ thị khoảng 20 Hz không phải toàn bộ lịch sử 200 Hz. Không kết luận rằng không có rung nhanh, jitter hoặc lần chạm biên giữa hai mẫu chỉ vì log trông mượt.

**Ví dụ đọc kết quả bằng lời:** “Từ +3° về target=0°, sau lần vượt đầu góc dao động nhỏ dần; rate đo tiến gần rate target; output không giữ biên, Mixer không cắt.” Nhận xét này có ích hơn “có về 0” vì một drone đang lắc qua lại cũng đi qua 0 nhiều lần. Nếu so hai bộ, dùng cùng góc bắt đầu, cửa sổ quan sát và điều kiện pin/base.

### 13.4. Sổ một lượt thử

```text
Mã lượt / tên file CSV:
Phiên bản firmware và WPF:
Trục điều khiển / trục đồ thị:
Điều kiện đế, pin, motor, cánh:
Mốc ngang / góc ban đầu / target:
Base:
Outer Roll Kp/Ki/Kd:
Inner Roll Kp/Ki/Kd:
Outer Pitch Kp/Ki/Kd:
Inner Pitch Kp/Ki/Kd:
Output/integral limits:
Tham số duy nhất thay so với lượt trước:
Thời gian hồi / vượt góc / sai số còn lại:
Rate có theo target không:
PID và mixer có giữ biên không:
Lỗi firmware hoặc hiện tượng cơ khí/nguồn:
Giữ ứng viên hay quay lại / lý do:
```

## 14. Bảng quyết định nhanh khi kết quả chưa tốt

| Hiện tượng | Đừng kết luận ngay | Kiểm tra theo thứ tự |
|---|---|---|
| Nghiêng càng nhiều ngay sau Start | “Kp nhỏ quá” | Mapping, dấu Roll/Pitch, vị trí motor và hướng lực đẩy |
| Góc nhỏ mà PWM hai bên bằng nhau | “PID không chạy” | Active, gain xác nhận, output trước làm tròn và độ phân giải duty |
| PWM khác nhau nhưng không quay | “Tăng gain là đủ” | Đế có khóa/ma sát, motor/cánh/nguồn, base và lực đẩy |
| Góc hồi chậm, rate bám target nhỏ | “Limit 40 thấp” | Kp góc và yêu cầu thực tế; xem limit nào có chạm không |
| Rate không theo và output giữ ±40 | “Tăng Kp nữa” | Nguồn, cơ khí, base, dự phòng motor; sau đó mới xem limit |
| Motor chạm 0/220 | “Nâng limit PID” | Dự phòng base, tổng hai trục và khả năng đáp ứng thực |
| Rung nhanh sau tăng P/D | “Thêm I để chữa” | Quay lại bộ trước, kiểm tra rung/gyro và cấu hình vừa thay |
| Lắc chậm sau tăng I | “Thiếu P” | So với log trước; giảm/loại thay đổi I để kiểm chứng |
| Dữ liệu cũ còn hiện, các nút bị khóa | “Motor chắc đang chạy” | Kết nối, tuổi mẫu, trạng thái firmware và phản hồi mới |
| Firmware báo lỗi timing/watchdog/IMU | “PID không ổn định” | Sửa lỗi lấy mẫu/truyền/nguồn/cảm biến trước khi tuning |

### Nếu báo CONTROL_TIMING_FAULT

Đây là lỗi nhịp xử lý, không phải thông báo đã cân bằng hoặc bằng chứng base quá thấp. Mục tiêu là khoảng 5 ms/lượt; vòng điều khiển vẫn dừng khi khoảng cách thực tế ngoài 2–20 ms.

Firmware mới sửa tình huống một lượt đến trễ rồi lượt kế tiếp chạy quá sát: lịch bình thường được giữ, nhưng nếu mốc kế tiếp còn cách dưới 2 ms thì bỏ mốc đó và đặt lượt kế tiếp sau 5 ms tính từ lúc bắt đầu lấy mẫu hiện tại. Ví dụ mốc dự kiến 5 ms nhưng thực tế tới 9 ms mới chạy thì lượt sau chuyển sang 14 ms, thay vì chạy ngay 10 ms. Không nới ngưỡng lỗi và không bỏ watchdog.

Trong lúc Arm/chạy cân bằng, trang cấu hình Wi-Fi, DNS cấu hình và nhận viewer mới được hoãn tới khi disarm. Kết nối điều khiển hiện có, STOP và heartbeat vẫn được xử lý. Đây là giảm một nguồn công việc có thể gây trễ; chưa chứng minh đó là nguyên nhân duy nhất trên board.

Nếu vẫn lỗi, app mới hiển thị thêm ngay dưới dòng trạng thái:

```text
CONTROL_TIMING_FAULT
Timing: dt 27.000 ms · late 22.000 ms
IO max 21.000 ms · previous sample 1.500 ms
```

Đây là **ví dụ minh họa**, không phải số đã đo trên drone của bạn:

| Số đọc | Ý nghĩa | Dùng để kiểm tra gì? |
|---|---|---|
| dt | Khoảng thời gian giữa hai lần bắt đầu lấy mẫu | Quá ngắn dưới 2 ms hay quá dài trên 20 ms? |
| late | Lượt hiện tại trễ bao nhiêu so với mốc lịch | Có bị trễ trước khi đọc cảm biến không? |
| IO max | Thời gian dài nhất của một lượt phục vụ motor/kết nối/lệnh giữa hai lần lấy mẫu | Công việc phục vụ I/O có chiếm nhiều thời gian không? Không phải tổng mọi lượt I/O |
| previous sample | Thời gian xử lý mẫu trước, gồm đọc IMU, bộ lọc, điều khiển và gửi telemetry | Lượt xử lý mẫu trước có kéo dài không? Không chỉ riêng thời gian tính PID |

Các khoảng trên không phải hồ sơ đầy đủ của CPU và không cộng lại để suy ra chính xác dt. Hệ điều hành có thể tạm chuyển tác vụ; phải đối chiếu nhiều lần thử trước khi quy nguyên nhân. Dòng chẩn đoán được giữ sau Stop và xóa khi Start lượt mới hoặc reset kết nối. Cần cập nhật cả app lẫn firmware để nhận thông tin này; CSV C2 hiện chưa lưu bản tin TIMING, nên chụp/ghi lại thông báo nếu lỗi tái diễn.

Sau khi nạp bản mới, giữ cùng PID, base và điều kiện đế để so sánh; Apply lại gain vì reset mất giá trị RAM. Chưa nên tăng base hoặc nới giới hạn thời gian chỉ để tránh báo lỗi. Nếu vẫn dừng, gửi cả dòng trạng thái và bốn số timing để xác định bước cần sửa tiếp.

## 15. Khi nào chốt một bộ thông số?

Chỉ chốt **cho phạm vi đã thử** khi:

1. Mapping và chiều hai trục đã được kiểm tra.
2. Lượt nhỏ hai chiều cho đáp ứng lặp lại được, không dao động tăng dần.
3. Rate/góc đạt tiêu chí đã đặt, không chỉ có một ảnh gần 0°.
4. Không có lỗi firmware, nguồn hoặc cơ khí chưa giải thích.
5. Đã thử lại điều kiện pin/base nằm trong phạm vi bạn định sử dụng; nếu đổi điều kiện phải ghi rõ bộ gain còn được đánh giá ở đâu.
6. Lưu gain, limits, base, phiên bản code và CSV chứng minh kèm nhau.

Không cần tối đa Kp, bật hết I/D hoặc tăng tất cả limit. Nếu hai ứng viên đạt cùng yêu cầu, ưu tiên ứng viên đáp ứng ít dao động/nhiễu hơn và còn dự phòng hợp lý trong dữ liệu đã đo. Bộ trên đế không tự trở thành bộ đã xác nhận cho bay tự do.

## 16. Thứ tự làm ngay với hệ thống hiện tại

```text
Kiểm tra cảm biến và ZERO
    → xác minh motor/chiều Roll, kiểm tra riêng Pitch
    → chọn base có cơ sở từ phần cứng
    → ROLL: giữ Outer 2/0/0, thử Inner P
    → đánh giá thêm D nếu cần
    → thêm I khi có sai lệch kéo dài đã loại trừ nguyên nhân khác
    → đánh giá Outer P
    → lặp lại PITCH
    → thử BOTH và kiểm tra biên motor
    → chỉ xét lại limits khi log chứng minh chúng đang cản đáp ứng
    → lưu cấu hình và thử lặp lại
```

Chưa có file đo thực tế thì chưa thể chọn bộ gain/base/limit tối ưu. Để phân tích lượt đầu, cần CSV C2 mới, gain đã xác nhận, base, trục, tư thế bắt đầu và mô tả đế/phản ứng thực tế.

## 17. Nguồn và cách áp dụng

Nguyên tắc phân biệt vòng góc và tốc độ, quan sát bám rate, đánh giá P/D rồi I và tác động nhiễu của D có thể đối chiếu [PX4 — Multicopter PID Tuning Guide](https://docs.px4.io/main/en/config_mc/pid_tuning_guide_multicopter). PX4 có các chế độ và đơn vị khác hệ thống này; tài liệu không lấy gain mẫu hoặc thao tác thử bay của PX4 làm cấu hình áp dụng trực tiếp cho Drone.

[ArduPilot — Copter Attitude Control](https://ardupilot.org/dev/docs/apmcopter-programming-attitude-control-2.html) giải thích cấu trúc góc tạo rate mục tiêu rồi PID rate tạo lệnh motor. Các bước thử trên đế, mức minh họa, giới hạn 45/40, độ phân giải PWM và thao tác WPF trong tài liệu này được giải thích theo code Drone và là đề xuất thử nghiệm cần được xác minh tại chỗ.
