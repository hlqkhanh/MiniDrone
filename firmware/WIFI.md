# Cấu hình Wi-Fi ESP32 bằng điện thoại hoặc laptop

ESP32 phát Wi-Fi cấu hình để bạn mở trình duyệt, quét/chọn mạng và nhập mật khẩu. Sau khi kết nối router thành công, cấu hình được lưu trong flash (NVS), lần bật sau tự kết nối lại. App 3D dùng IP của ESP32 trong mạng router và port 3333. Động cơ mặc định tắt; MANUAL MOTOR OUTPUT trong app cho đặt PWM 0–100% riêng từng động cơ hoặc cả bốn, thời gian tùy chọn hoặc liên tục, sau khi tháo cánh. Firmware V2 tự dừng khi thiếu KEEPALIVE khoảng 2 giây.

## 1. Nạp firmware một lần

1. Mở `DroneMotionMpu6050/DroneMotionMpu6050.ino` bằng Arduino IDE từ thư mục dự án. Giữ toàn bộ các file `.h` cùng thư mục, gồm TelemetryOutput.h, TelemetrySendBuffer.h, WifiProvisioner.h, WifiPortalPage.h và WifiConfig.example.h.
2. Chọn **ESP32 Dev Module**, COM USB thực tế (trước đây COM6), **Upload Speed 115200**, rồi Upload. Disconnect app nếu đang dùng COM, đóng Serial Monitor trước khi nạp.
3. Không cần điền SSID/mật khẩu trong WifiConfig.local.h. Phiên bản này lấy thông tin mạng từ trang web/NVS; các trường SSID/mật khẩu cũ trong file local không còn được sử dụng. File local, nếu có, vẫn được Git bỏ qua; Port trong file đó vẫn được dùng, mặc định 3333.

## 2. Mở trang cấu hình trên điện thoại/laptop

1. Cấp nguồn cho ESP32, chờ khởi động/hiệu chuẩn khoảng 3 giây.
2. Mở danh sách Wi-Fi và kết nối mạng **DroneMotion_Setup-XXXX** (4 ký tự cuối tùy bo), mật khẩu **drone12345**.
3. Nếu điện thoại/Windows báo không có Internet, chọn giữ kết nối với mạng này.
4. Điện thoại sẽ nhận diện **captive portal**: trang cấu hình có thể tự bật hoặc hiện thông báo **Đăng nhập vào mạng Wi-Fi** để bạn bấm mở. ESP32 dùng DNS và chuyển hướng HTTP để đưa về trang cấu hình. Nếu điện thoại không hiện thông báo, mở **http://192.168.4.1** bằng trình duyệt (HTTP, không phải HTTPS). Khả năng tự mở phụ thuộc hệ điều hành/cài đặt điện thoại.
5. Trang **tự quét Wi-Fi** khi mở; bấm danh sách **Wi-Fi khả dụng** và chọn mạng. Mạng mạnh nhất ở đầu danh sách, tên trùng chỉ hiện một lần. Bấm **Quét lại Wi-Fi** để cập nhật; với mạng ẩn, mở **Mạng ẩn / nhập tên thủ công**, bật **Dùng mạng ẩn** rồi nhập tên.
6. Chọn **Wi-Fi có mật khẩu**, nhập mật khẩu rồi bấm **Kết nối và lưu**. Với mạng không mật khẩu, chọn đúng loại mạng tương ứng.
7. Chờ tối đa khoảng 30 giây. Khi thành công, trang hiển thị **Đã kết nối Wi-Fi** và địa chỉ ví dụ **192.168.1.105:3333**. Dùng địa chỉ thực tế của bo, không dùng IP ví dụ này.

ESP32-WROOM-32 chỉ quét/kết nối băng tần 2,4 GHz. Laptop có thể dùng 5 GHz hoặc Ethernet nếu ở cùng LAN và router cho thiết bị giao tiếp.

Khi STA chuyển sang kênh của router, Wi-Fi cấu hình AP cũng có thể đổi kênh nên trang tạm mất kết nối. Nếu vậy, nối lại DroneMotion_Setup-XXXX và mở lại http://192.168.4.1 để xem kết quả. AP cấu hình vẫn được giữ bật sau khi kết nối router, hỗ trợ tối đa 4 thiết bị cấu hình cùng lúc; TCP app 3D vẫn nhận một viewer mỗi lần.

### Wi-Fi trường/công ty có tài khoản riêng

Nếu mạng dùng **PEAP**, chọn **Trường / công ty (PEAP)**, điền mật khẩu tài khoản, tên đăng nhập và Identity theo hướng dẫn của IT. Có thể mở phần chứng chỉ CA để dán PEM do IT cung cấp. Username và Identity không nhất thiết giống nhau.

Trang này hỗ trợ mạng cá nhân, mạng mở và WPA2-Enterprise PEAP. Mạng yêu cầu EAP-TLS/chứng chỉ máy khách hoặc trang đăng nhập web cần cấu hình riêng. Mạng trường/khách có thể chặn giao tiếp giữa các thiết bị dù ESP32 đã có IP; đó là vấn đề quyền truy cập của LAN, không phải app thiếu cổng COM.

## 3. Nhận dữ liệu trên app 3D

1. Ghi lại IP/port trang cấu hình vừa hiển thị.
2. Trên laptop, kết nối trở lại **mạng router mà ESP32 vừa tham gia**. Có thể dùng điện thoại cấu hình để laptop không phải chuyển mạng trong bước 2.
3. Mở **Drone Motion Viewer**, bật **Wi-Fi**, nhập IP ESP32 và port **3333**, bấm **Connect**.
4. Khi Data khoảng 100 samples/s, đặt bo ở tư thế chuẩn, bấm **Zero orientation**, rồi nghiêng/xoay để kiểm tra mô hình. **Calibrate** vẫn gửi lệnh qua Wi-Fi.
5. Khi rút USB, bo cần nguồn/pin riêng phù hợp. Wi-Fi không cấp điện. Nếu bo khởi động lại khi đổi nguồn, chờ hiệu chuẩn và bấm Connect lại app.

Không mở cổng 3333 bằng trình duyệt: đây là cổng TCP cho dữ liệu quaternion. Trang cấu hình web dùng cổng 80 tại địa chỉ AP; trang web chỉ mở từ kết nối Wi-Fi cấu hình của ESP32.

## 4. Đổi mạng, khôi phục và xem lại IP

- Đổi router/mật khẩu: nối lại DroneMotion_Setup-XXXX, mở trang, nhập mạng mới. Không cần sửa code hoặc nạp lại.
- Nhập sai mật khẩu: trang báo lỗi sau khoảng 30 giây, cho phép thử lại. Cấu hình mới chỉ được ghi xuống flash sau khi xác thực và nhận IP; mạng đã lưu trước đó vẫn giữ nguyên nếu lần thử mới thất bại.
- Nếu trang báo đã kết nối nhưng chưa lưu được: thử lưu lại trước khi tắt nguồn. Không báo lưu thành công khi NVS lỗi.
- **Xóa mạng đã lưu** xóa thông tin NVS và ngắt STA; AP cấu hình vẫn hoạt động. Lần bật sau sẽ chờ bạn chọn mạng trên web.
- Sau khi router cấp IP khác, nối vào AP cấu hình và xem lại IP, hoặc qua Serial Monitor 115200 gửi **INFO** (Newline). Log có dạng `STATUS,TCP_<IP>:3333`.
- Khi mất mạng đã lưu, ESP32 thử kết nối lại. App báo mất kết nối sau tối đa khoảng 20 giây không có dữ liệu; khi mạng trở lại bấm Connect trong app. App chưa tự reconnect.
- Có IP mà app không vào được: kiểm tra hai thiết bị cùng LAN, IP/port, VPN, firewall hoặc chế độ client isolation của router/mạng khách.
- Kết nối được rồi bị ngắt: firmware giữ phần TCP chưa gửi và thử lại khi bộ đệm mạng tạm đầy, thay vì đóng kết nối ngay. Khi nghẽn kéo dài, bỏ các dòng dữ liệu mới chưa xếp được vào bộ đệm; không cắt dòng đang gửi. Chỉ ngắt khi lỗi socket không phục hồi hoặc không gửi thêm được byte nào trong 20 giây. Qua USB, `ERROR,TCP_SEND_FAILED,<errno>` hoặc `ERROR,TCP_SEND_STALLED_20S,<errno>` cho biết lý do đóng socket. Nếu thấy lại log khởi động, cần kiểm tra ESP32 bị reset/nguồn pin; nếu có `ERROR,WIFI_DISCONNECTED_RECONNECTING`, kiểm tra kết nối Wi-Fi.
- Không thấy Wi-Fi cấu hình: kiểm tra nguồn, đã nạp đúng phiên bản, Serial Monitor và nút EN. Log ban đầu in tên AP thực tế, URL cấu hình và lỗi WIFI_SETUP_AP_FAILED nếu không tạo được AP.

Cấu hình mạng được lưu trong flash ESP32, không được trả lại mật khẩu qua API/trang trạng thái hoặc in ra Serial. Mạng cấu hình dùng mật khẩu thử nghiệm cố định drone12345; dùng nút xóa trước khi chuyển bo cho người khác nếu cần xóa mạng đã lưu. Không chọn Erase All Flash khi nạp nếu muốn giữ cấu hình NVS.

## 5. Kiểm tra và dùng USB

- Bỏ chọn Wi-Fi trong app khi Disconnect, chọn COM/115200 để nhận USB. Không cần đổi firmware. Sửa hướng pitch/yaw vẫn áp dụng ở cả hai chế độ.
- Quét Wi-Fi và phục vụ trang cấu hình có thể gây gián đoạn ngắn trong lúc cấu hình. Kết nối router xong, đóng trang khi kiểm tra độ mượt mô hình.
- Kiểm thử giao diện với API giả (Node 22+ và Chrome): `node tests/portal/wifi-portal.test.cjs`. Kiểm thử này kiểm tra trang web, không thay thế kiểm tra Wi-Fi/NVS trên bo thật.

Nguồn: [API Wi-Fi Espressif](https://docs.espressif.com/projects/arduino-esp32/en/latest/api/wifi.html), [Preferences/NVS](https://docs.espressif.com/projects/arduino-esp32/en/latest/tutorials/preferences.html).
