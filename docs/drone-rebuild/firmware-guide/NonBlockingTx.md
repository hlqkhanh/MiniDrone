# NonBlockingTx.h — gửi TCP từng phần

Source: [NonBlockingTx.h](../../../firmware/DroneRebuild/NonBlockingTx.h). Function chính: `pumpTcp()`.

## 1. Vấn đề: một lần send chưa chắc gửi hết

TCP là luồng byte (byte stream), không giữ ranh giới “mỗi lần send là một message”. Đề nghị gửi 600 byte nhưng hệ thống chỉ nhận vào buffer 100 byte trong lần gọi đó là tình huống phải xử lý.

Chờ gửi hết bằng mọi giá có thể giữ phần giao tiếp quá lâu. Code này ghi nhớ tiến độ, làm một lượng việc hữu hạn rồi quay lại ở lượt sau.

## 2. function signature

```cpp
template <class Sender>
int pumpTcp(const char* data, std::size_t length, std::size_t& offset, Sender send)
```

| Tham số | Ý nghĩa |
| --- | --- |
| `data` | Địa chỉ vùng byte cần gửi; không tự copy toàn bộ dữ liệu |
| `length` | Tổng độ dài cần gửi |
| `offset` | Đã gửi được bao nhiêu byte; truyền reference để giữ tiến độ |
| `send` | Callable/lambda thực hiện việc gửi |

Template giúp helper không gắn với một class socket cụ thể. `.ino` truyền lambda gọi `::send(..., MSG_DONTWAIT)`.

Helper không tự biến function gửi blocking thành non-blocking. Tùy chọn không chặn của caller là một phần thiết kế.

## 3. Các bước trong function

1. `offset>=length`: đã hoàn tất, trả 1.
2. `left=length-offset`: tính phần chưa gửi.
3. `chunk=min(left,256)`: mỗi lượt chỉ đề nghị tối đa 256 byte.
4. `send(data+offset,chunk)`: gửi từ byte chưa gửi, không gửi lại đầu buffer.
5. Nếu count dương, kiểm tra count không vượt chunk, tăng offset.
6. Nếu lỗi tạm thời EAGAIN/EWOULDBLOCK/EINTR, giữ offset để thử lại.
7. Count bằng 0 hoặc lỗi không thuộc nhóm tạm thời: trả −1.

`data+offset` là dịch địa chỉ bộ nhớ, không phải sửa nội dung buffer.

## 4. Ví dụ gửi 600 byte

| Lượt | Offset trước | Đề nghị | Send thực nhận | Offset sau | Helper trả |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 0 | 256 | 100 | 100 | 0 |
| 2 | 100 | 256 | EAGAIN | 100 | 0 |
| 3 | 100 | 256 | 256 | 356 | 0 |
| 4 | 356 | 244 | 244 | 600 | 1 |

Lượt 3 bắt đầu từ byte 100. Nếu tự đặt offset về 0 sau mỗi lượt, dữ liệu đầu sẽ bị gửi trùng.

Giá trị 0 có nghĩa **chưa hoàn tất**, không nhất thiết là lỗi: có thể đã gửi được một phần hoặc chưa có chỗ trong buffer.

## 5. Caller phải giữ dữ liệu qua các lượt

`.ino` dùng `tcpPending`, `tcpAt`, `tcpSize` để lưu buffer và tiến độ sau khi một lần `loop()` kết thúc.

```text
Trả 1  → bỏ đánh dấu dữ liệu đang chờ
Trả 0  → giữ buffer/offset, lượt sau thử tiếp
Trả −1 → đóng client và báo TCP_SEND_ERROR
```

Thời hạn chờ nghẽn nằm ở `loop()`, không nằm trong helper. Trong luồng hiện tại, TCP chờ quá lâu có thể dẫn tới `TCP_BACKPRESSURE`; USB có cách kiểm tra chỗ trống riêng.

## 6. availableForWrite() có liên quan gì?

Comment source lưu ý NetworkClient kế thừa giá trị mặc định 0 từ `Print::availableForWrite()` trong trường hợp đang xử lý. Thiết kế hiện dùng socket send không chặn để biết tiến độ, không lấy giá trị đó làm dung lượng TCP còn trống.

Đây là lý do của helper trong dự án này, không phải kết luận mọi implementation của client đều giống nhau.

Đọc tiếp [DroneRebuild.md](DroneRebuild.md). [Mục lục](README.md).
