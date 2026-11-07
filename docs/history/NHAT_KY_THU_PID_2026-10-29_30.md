# Nhật ký thử PID DroneRebuild — 29 và 30/10/2026

Tổng hợp ngày 01/11/2026 từ 15 phiên trong `Drone/logs` và các trao đổi khi thử. Thời gian tên phiên là giờ Việt Nam (UTC+7); timestamp có hậu tố `Z` trong `events.log` là UTC.

## Phạm vi và cách đọc

- Phần mềm: **DroneRebuild**, thử riêng trục Roll trên gá; chưa nghiệm thu bay tự do hoặc điều khiển đồng thời Roll/Pitch.
- Các phiên ghi nhận firmware `DroneRebuild-0.2`, profile `XW_Z1_SPI_ROLL_PITCH_V2`, source ID `FD870B97CE1031ED`, build `Oct 29 2026 12:22:48`.
- Tất cả các lượt trong bảng dùng target **0**, Base **80 duty**, Output limit **20 duty**. Thông số lấy từ telemetry board, không suy từ ô nhập trên app. Không có lượt nào trong tập log này ghi Base=120.
- PID ghi theo thứ tự **Kp / Ki / Kd**. Mode RATE có target đơn vị °/s; ANGLE có target đơn vị °.
- Thời lượng là chênh lệch giữa mẫu RUNNING đầu và cuối đã lưu; không nhất thiết bao gồm toàn bộ thời gian motor chạy. Góc là số đo ước lượng, chưa phải số đo đối chiếu độc lập.
- Góc Roll hiển thị chuyển từ −180° sang +180° khi đi qua biên; không tính chênh lệch trực tiếp qua biên để suy quãng quay.

## Ngày 29/10 — xác minh dấu, kiểm tra gá và tune RATE

### Diễn biến công việc

1. Lượt đầu dùng motor sign `+1`: khung tăng tốc quay quanh trục Roll, gyro chạm trần đo. P tính đúng theo sai số nhưng chiều tác động thực cần được đối chiếu.
2. Người thử xác nhận khi nâng trái/hạ phải thì Roll rate âm; đồng thời báo đã kiểm tra mapping motor và cánh. Đổi sign Roll sang `−1`. Đây là xác nhận thao tác của người thử, không phải phép đo lực độc lập trong CSV.
3. Một số lượt sau đổi dấu vẫn dùng Kp=1, tạo dao động và chạm giới hạn nhiều. Sau đó giảm Kp về 0.2, thử D=0.001 và thêm I=0.05.
4. Ảnh và mô tả cho thấy ống gắn với drone quay trượt quanh dây. Người thử báo nghiêng nhẹ khi motor tắt rồi nhả thì khung tiếp tục đổ. Có khả năng trục quay không gần trọng tâm; ma sát, dây và vị trí trọng tâm chưa được đo định lượng.
5. Người thử báo đã chỉnh gá trước phiên 14:38. Log sau đó ít trôi và ít quay mạnh hơn. Không thể quy toàn bộ cải thiện cho gain vì bố trí cơ khí đã thay đổi.
6. Giảm Kp từ 0.2 xuống 0.15, giữ I=0.05 và D=0.001; thử thêm lượt ngắn và STOP khi góc chưa quá lớn.

### Các phiên ngày 29/10

Nhấn tên phiên để mở telemetry; `events.log` nằm cùng thư mục.

| Phiên | Rate PID | Sign | RUNNING đã lưu (s) | Góc đầu → cuối (°) | Kết quả chính |
|---|---|---:|---:|---|---|
| [13:40:58](../../logs/20261029-134058-258/telemetry.csv) | 0.2 / 0 / 0 | +1 | 1.84 | −0.76 → −70.00, có quay qua biên ±180° | Rate tới −498.77°/s do gyro chạm trần; PID chạm giới hạn 88.2% mẫu. Có STOP. |
| [13:57:48](../../logs/20261029-135748-287/telemetry.csv) | 1 / 0 / 0 | −1 | 8.37 | −1.18 → +174.69 | Rate −120.04 đến +54.74°/s; chạm giới hạn 67.9%. File hết khi RUNNING. |
| [14:06:55](../../logs/20261029-140655-739/telemetry.csv) | 1 / 0 / 0 | −1 | 1.26 | −0.69 → −31.72 | Chạm giới hạn 59.4%; board vẫn ghi Kp=1. Có STOP. |
| [14:10:22](../../logs/20261029-141022-479/telemetry.csv) | 0.2 / 0 / 0 | −1 | 0.68 | +1.03 → −18.45 | Không chạm giới hạn; rate −63.93 đến +5.40°/s, còn dao động và trôi âm. |
| [14:15:04](../../logs/20261029-141504-503/telemetry.csv) | 0.2 / 0 / 0.001 | −1 | 8.38 | +1.19 → +156.54, qua biên | Trong 0.68 s đầu, đỉnh rate âm giảm nhưng rate trung bình gần như chưa cải thiện. Vẫn lộn ngược; file hết khi RUNNING. |
| [14:22:19](../../logs/20261029-142219-488/telemetry.csv) | 0.2 / 0.05 / 0.001 | −1 | 10.42 | −0.37 → −164.45 | I lên 8.26 duty nhưng khung đã gần úp ngược; không coi việc chậm lại ở đây là nghiệm thu RATE. |
| [14:38:17](../../logs/20261029-143817-007/telemetry.csv) | 0.2 / 0.05 / 0.001 | −1 | 13.88 | −1.00 → −39.26 | Sau báo cáo sửa gá, rate trong −28.86 đến +23.23°/s; không chạm giới hạn. File hết khi RUNNING. |
| [16:15:47](../../logs/20261029-161547-520/telemetry.csv) | 0.15 / 0.05 / 0.001 | −1 | 14.92 | +0.95 → −22.27 | Dao động giảm so với lượt 14:38, vẫn trôi âm; không chạm giới hạn. |
| [16:23:04](../../logs/20261029-162304-358/telemetry.csv) | 0.15 / 0.05 / 0.001 | −1 | 2.54 | −1.47 → −15.88 | Rate âm lớn nhất −27.63°/s; gần STOP về khoảng −1.37°/s. I cuối 0.74 duty. |

### Kết luận trong ngày

- Đã xác lập sign Roll `−1` theo phép đối chiếu chuyển động được người thử báo cáo.
- Giảm Kp từ 1 xuống các mức 0.2/0.15 giảm hiện tượng điều khiển gắt; bộ cuối ngày là **0.15 / 0.05 / 0.001**.
- RATE target=0 chỉ yêu cầu ngừng quay, không yêu cầu về góc ngang. Tuy nhiên rate vẫn có sai lệch và dao động, nên chưa nghiệm thu vòng RATE.
- Hai phiên 14:22 và 14:38 ghi `propsRemoved=1`. Người thử xác nhận lượt 14:22 thực tế có đủ bốn cánh: checkbox không phản ánh đúng trạng thái vật lý. Cờ này không phải cảm biến phát hiện cánh và không thay đổi phép tính RATE.

## Ngày 30/10 — so sánh I/D và thử ANGLE

### Các phiên ngày 30/10

Tất cả dùng sign `−1`, Base=80 và Output limit=20.

| Phiên | Mode | Rate PID | Angle PID | RUNNING đã lưu (s) | Góc đầu → cuối (°) | Kết quả chính |
|---|---|---|---|---:|---|---|
| [10:11:49](../../logs/20261030-101149-321/telemetry.csv) | RATE | 0.15 / 0.15 / 0.002 | Không dùng | 31.62 | +1.04 → −18.64 | Đổi cả Ki và Kd so với ngày trước; không tách được tác dụng riêng. I tối đa 3.52 duty. |
| [10:20:01](../../logs/20261030-102001-556/telemetry.csv) | RATE | 0.15 / 0.15 / 0.001 | Không dùng | 35.71 | +0.77 → −34.41 | Giảm D: rate ít dao động hơn nhưng trôi lâu dài nhiều hơn. I tối đa 5.47 duty. |
| [10:34:46](../../logs/20261030-103446-680/telemetry.csv) | RATE | 0.15 / 0.15 / 0.001 | Không dùng | 1.90 | −0.22 → −9.18 | STOP sớm; có hãm rate nhưng vẫn trôi. Đoạn 1.3–1.9 s: rate TB −0.42°/s, sai số tuyệt đối TB 3.03°/s. |
| [12:19:12](../../logs/20261030-121912-142/telemetry.csv) | RATE | 0.15 / 0.15 / 0.001 | Không dùng | 28.51 | −0.74 → −11.87 | Sau giây 10, góc trong −14.26 đến −10.97°; rate TB −0.04°/s nhưng sai số tuyệt đối TB 4.22°/s. Người thử xác nhận không có chặn. |
| [12:43:46](../../logs/20261030-124346-938/telemetry.csv) | ANGLE | 0.15 / 0.15 / 0.001 | 0.5 / 0 / 0 | 49.00 | +4.15 → +0.60 | Góc toàn lượt −4.83 đến +4.15°. Trong giây 40–49, sai số góc tuyệt đối TB 0.84°. I RATE tối đa 7.20 duty; Pitch tới +9.63°. |
| [12:51:10](../../logs/20261030-125110-830/telemetry.csv) | ANGLE | 0.15 / 0.15 / 0.001 | 0.5 / 0 / 0 | 78.41 | −2.91 → −3.53 | Ban đầu đi xa mục tiêu tới −10.58°, sau đó hồi chậm. I RATE chạm 10 duty khoảng giây 19 và thường xuyên ở sát trần. File hết khi RUNNING. |

### So sánh D trên cùng cửa sổ thời gian

Hai lượt RATE 10:11 và 10:20 cùng Kp=0.15, Ki=0.15; D lần lượt 0.002 và 0.001. Mỗi cấu hình chỉ có một lượt dài tương ứng; chưa kiểm soát đầy đủ nguồn, gá và nhiễu.

| Chỉ số | Kd=0.002 | Kd=0.001 |
|---|---:|---:|
| Sai số rate tuyệt đối TB, giây 3–10 | 4.13°/s | 2.61°/s |
| Sai số rate tuyệt đối TB, giây 10–30 | 3.70°/s | 3.07°/s |
| Rate trung bình, giây 10–30 | −0.30°/s | −1.07°/s |
| Góc khoảng giây 30 | −19.02° | −33.73° |

Giảm D cho rate êm hơn trong các lượt này nhưng không cải thiện đồng thời độ trôi. Không kết luận một bộ tốt hơn toàn diện chỉ từ rate trung bình hoặc góc cuối.

### Kiểm tra vòng ANGLE và giới hạn I

- Khi Angle Ki=Angle Kd=0, code tạo `rateTarget = clamp(AngleKp × (targetAngle − angle), ±rateLimit)`.
- Vòng RATE dùng `error = rateTarget − rollRate`, sau đó tính P/I/D và mixer. Đối chiếu P với công thức trong log 12:51 cho sai khác tối đa khoảng 6×10⁻⁷ duty do số liệu làm tròn; không phát hiện sai phép tính P ở phép kiểm tra này. Đây không phải kiểm chứng toàn bộ firmware.
- `iLimit=10` là giới hạn riêng của **I vòng RATE** trong `RollCore.h`. `pidLimited` báo tổng P+I+D chạm Output limit=20; vì vậy `pidLimited=0` không có nghĩa I chưa chạm trần.
- Khoảng 20 giây cuối của phiên 12:51: rate target TB khoảng **+1.53°/s**, rate thực TB **−0.03°/s**, sai số TB **+1.56°/s**, I TB khoảng **9.98 duty**. Vòng RATE chưa bám được lệnh hồi góc nhỏ kéo dài.
- Không tăng trần I, không thêm Angle Ki/Kd chỉ để che tình trạng này. Chưa đủ dữ liệu để quy nguyên nhân cho ma sát gá, lực motor, nguồn, trọng tâm hoặc sai lệch đo.

## Trạng thái cuối hai ngày và việc tiếp theo

**Bộ cấu hình cuối đã ghi nhận — chỉ làm mốc đối chiếu, chưa nghiệm thu:**

```text
Trục: Roll
Mode: ANGLE; target: 0°
Rate Kp/Ki/Kd: 0.15 / 0.15 / 0.001
Angle Kp/Ki/Kd: 0.5 / 0 / 0
Base: 80 duty; Motor sign: −1; Output limit: 20 duty
I limit RATE trong source: 10 duty
```

- Đã quan sát khả năng giữ Roll gần ngang ở một lượt ANGLE; lượt từ góc âm hồi chậm, ban đầu lệch thêm và I RATE sát trần. **Chưa xác nhận ổn định hai chiều, chưa đủ điều kiện kết luận bay được.**
- Người thử báo đã kiểm tra motor/cánh, nguồn và gá; đây là báo cáo thao tác, chưa có số đo lực, điện áp khi tải hoặc ma sát đi kèm.
- Gá ống–dây đã được điều chỉnh nhưng chưa có chặn hành trình theo trao đổi cuối. Không mô tả gá này như có giới hạn ±10°.
- Firmware trong các phiên START chạy liên tục; không tự cắt theo góc hoặc thời gian START. BENCH có thời hạn riêng. Không nhầm đề xuất bổ sung bảo vệ với tính năng đã triển khai.
- Việc còn lại: xác minh ma sát khởi động/điểm kẹt hai chiều gần ngang khi motor tắt; nếu trơn thì cần phép kiểm tra lực hai nhóm có giữ chắc và che chắn; ghi điện áp pin và điều kiện gá để so sánh được các lượt.
- Chưa chọn thêm gain mới sau khi phát hiện I RATE sát trần. Ưu tiên xác định vì sao vòng RATE không bám lệnh nhỏ trước khi tăng độ mạnh vòng ANGLE.

## Giới hạn dữ liệu

- Các phiên `135748`, `141504`, `143817` ngày 29 và `125110` ngày 30 không chứa lệnh STOP và kết thúc dữ liệu khi RUNNING. Điều này chỉ chứng minh thiếu đoạn dừng trong file, không chứng minh motor không được dừng sau đó.
- Ghi chú phiên chưa có thông tin đầy đủ về pin, điện áp khi tải, gá, cánh và tác động ngoài. Không quy toàn bộ khác biệt giữa các lượt cho PID.
- Tất cả các lượt ANGLE nêu trên và phần lớn RATE không có cờ tổng PID/mixer chạm giới hạn; riêng I RATE vẫn có thể chạm giới hạn như phiên cuối.
- Báo cáo này không sửa firmware/app, không thay đổi cấu hình trên board và không thay thế dữ liệu gốc.

Nguồn triển khai: [RollCore.h](../../firmware/DroneRebuild/RollCore.h), [DroneRebuild.ino](../../firmware/DroneRebuild/DroneRebuild.ino), [Imu.h](../../firmware/DroneRebuild/Imu.h). Nguồn số liệu: telemetry và events trong 15 thư mục phiên được liên kết ở trên.
