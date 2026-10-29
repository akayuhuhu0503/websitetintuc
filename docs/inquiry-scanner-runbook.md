> **Checkpoint mới 2C-SCAN:** genuine Defender Test đã kiểm Clean/EICAR; dùng [scanner-operations.md](scanner-operations.md) và [báo cáo](phase-2c-scanner-report.md) cho lệnh/giới hạn hiện tại. Không áp provider DefenderTest cho arbitraryPII hoặc Production;53nguồn gốc vẫnNotScanned. Đoạn dưới là runbook Finish lịch sử khi chưa có engine Test; không dùng blocker cũ để phủ nhận bằng chứng mới hoặc tự nâng overall2C PASS.

---

# Tệp hỏi đáp và scanner — 2C-Finish

Checkpoint hiện chưa có engine scanner Test. `F1 SCAN-INTEGRATION BLOCKED`: ca Clean download/Infected thật chưa chạy. Không cài daemon/service, không gửi tệp ra dịch vụ ngoài. 53 media nguồn vẫn NotScanned, worker mới chỉ xử lý `InquiryAttachments`.

Mỗi hồ sơ nhận tối đa một PDF/JPEG/PNG, 10 MiB (10.485.760 byte). MIME khai báo và byte thực đều kiểm; không nhận executable/SVG/HTML/DOCX hoặc tên đường dẫn/đa phần mở rộng. Tệp streamed vào `Storage.MediaRoot/inquiry-private/<random>.ext`, ngoài wwwroot; SQL giữ hash/size/session/challenge/submission, tên được mã hóa Data Protection. ASP.NET multipart buffer nằm trong kho temp của project. Không dùng tên khách gửi làm đường dẫn. Kho Test dưới `.local/tests/phase2a`; không dùng kho Dev để thử.

Trước OTP, tệp gắn phiên và challenge; sau OTP đúng payload mới gắn hồ sơ atomically. Tệp chưa gửi hết hạn sau một giờ; worker quét dọn tối đa 100 hàng/lượt, giữ tệp đã có hồ sơ. Orphan do SQL commit không chắc được đối soát sau 24 giờ, chỉ tên GUID/phần mở rộng do hệ thống tạo trong inquiry-private, không xóa tên lạ/không đi qua reparse point. Không có chính sách tự xóa hồ sơ đã nhận; retention phải được đơn vị xác nhận.

Trạng thái: NotScanned → Scanning → Clean / Infected / ScanFailed. Claim SQL/applock, lease 2 phút, tối đa 3 lần, backoff 2/4/8 phút; restart thu hồi lease hết hạn, fencing chặn kết quả đến muộn. Scanner timeout 30 giây, health 3 giây. Worker đọc/hash trước và sau scan; Clean chỉ từ phản hồi thật của engine, lưu engineVersion/scannedAt/scannedHash. Không có cờ Development đổi thành Clean.

Tích hợp hiện dùng clamd TCP **loopback**, không cấu hình remote host: PING, VERSIONCOMMANDS và INSTREAM/chunk. Đối chiếu [giao thức chính thức ClamAV](https://docs.clamav.net/manual/Usage/ClamdProtocol.html). Môi trường Production/Staging hiện bị chặn, cần triển khai và nghiệm thu riêng. Scanner chưa cấu hình/unavailable/timeout → ScanFailed, không mở download.

Sau khi có engine thật được vận hành chuẩn bị, dùng process environment `Scanner__ClamdPort` qua tham số script; không sửa `.env`:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-inquiry-scanner-test.ps1 -ClamdPort 3310 -Once -FileId <GUID-Test>
```

Chỉ chạy với file của SQL Test/kho Test đã đối chiếu. Exit 0 của worker có thể chỉ là boot hoặc xử lý lỗi, **không chứng minh Clean**. Kiểm SQL state/hash/version và log health; ca sạch và EICAR/virus phải qua engine thật, dùng mẫu Test được phép và kiểm ACL, Range, tamper. Không dùng giả phản hồi clamd. Hết 3 lần ScanFailed: giữ cách ly, điều tra engine/hash; chưa có UI reset số lần scan, không sửa nhãn thành Clean để mở tệp.

GET `/admin/hoi-dap/tep/{id}` cần `interaction.inquiry.read` + `interaction.inquiry.file.read` và phạm vi inbox hiện tại. Chưa có bằng chứng Clean trả 423, ngoài phạm vi 404; guest yêu cầu đăng nhập. Range cũng kiểm đủ quyền. `/media/{id}`, lookup và public không cung cấp attachment. Tệp Infected giữ private/cách ly, không trả bytes. Scanner Production/quét media nguồn/ACL hệ điều hành/khôi phục kho và keys vẫn là blocker go-live.
