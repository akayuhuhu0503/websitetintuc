# Chẩn đoán kết nối QLDD trên IIS

## Kết luận ngày 18/11/2026

Chưa có bằng chứng mạng/log dưới tài khoản IIS nên chưa xác định được nguyên nhân gốc trên IIS. Không coi Chrome hoặc kết quả máy Developer là bằng chứng IIS kết nối được.

Developer: DNS trả IPv4 công khai 103.9.86.152, không có AAAA; TCP 443 thành công (6 ms); SafeSourceClient HTTPS robots.txt trả 404 (113 ms), hợp lệ theo quy tắc hiện có. So sánh proxy hệ thống không có proxy hiệu lực, cũng trả 404. Một lượt đọc 3 request: robots 404, trang chủ 200, phát hiện 5 liên kết, một bài 3767 trả 200 và parse đúng danh tính/chuyên mục. Không ghi SQL hoặc tải media nguồn.

Lỗi code được xác nhận là mất thông tin chẩn đoán: runner chỉ lưu HttpRequestException. Bổ sung mã DNS/TCP/TLS/HTTP_RESPONSE/NETWORK_POLICY/REDIRECT_LIMIT/HTTP_RETRY_AFTER và bước robots/trang/tệp, không xuất exception message hay URL nhạy cảm. Không thay chính sách proxy, TLS, robots, giới hạn hoặc quyền công bố.

Backoff giữ nguyên: lỗi tự động liên tiếp chờ 15/30 phút, lần ba Paused với thời điểm chờ 45 phút; sau đó quản trị viên yêu cầu lại qua CMS. Bấm lại sớm bị từ chối. Không có bài mới không tạo HttpRequestException. Hàng chờ 0 không tự chứng minh lỗi ở robots: trang chủ/danh sách cũng được đọc trước bước phát hiện.

## Một lệnh trên IIS, không cập nhật website

Giải nén gói GDLA-SOURCE-NETWORK-CHECK ngoài webroot, mở PowerShell tại đó:

```powershell
powershell -NoProfile -File .\KIEM-TRA-NGUON.ps1
```

Cần .NET 10 runtime sẵn có của website, không cần SDK/admin. Công cụ không đọc cấu hình ứng dụng, cookie, secret, SQL hay kho media; chỉ in JSON chẩn đoán. Không in URL/credential proxy. Có tối đa 4 TCP probes (5 giây/IP), một luồng SafeSourceClient robots (60 giây, tối đa 5 request nếu redirect), một GET so sánh proxy hệ thống (60 giây, không theo redirect, không credential). Không crawl hoặc nhập bài. Kết quả thuộc tài khoản chạy lệnh, không giả lập danh tính Application Pool.

- DNS hoặc NETWORK_POLICY: CNTT kiểm tra DNS/split-DNS; không cho phép private IP để vượt guard.
- TCP443 thất bại: kiểm tra outbound TCP 443 từ máy/tài khoản IIS đến nguồn, firewall/route; IP DNS có thể đổi.
- TLS: kiểm tra giờ máy, chuỗi CA và proxy TLS inspection theo chính sách đơn vị; không tắt xác minh chứng chỉ.
- Direct lỗi, proxy thành công: cần CNTT xác nhận đường ra mạng được phép. Ứng dụng vẫn không tự dùng proxy; đây chỉ là phép so sánh chẩn đoán theo yêu cầu.
- Cả hai thành công nhưng ứng dụng lỗi: cần log mã lỗi mới dưới danh tính IIS; kiểm tra chính sách egress theo tài khoản và lỗi gián đoạn. Chưa đủ cơ sở thay firewall/proxy dựa trên máy Developer.
- HTTP robots khác 200/404 hoặc redirect: rà soát phía nguồn/WAF và đích redirect; không bỏ robots/allowlist.

Sau khi môi trường kết nối được, chờ hết backoff và dùng “Kiểm tra bài mới ngay”. Không sửa SQL xóa lỗi, không tự công bố.

## Build công cụ (Developer)

```powershell
dotnet publish tools/SourceNetworkProbe -c Release -r win-x64 --self-contained false -o .local/handoff/GDLA-SOURCE-NETWORK-CHECK
Copy-Item tools/SourceNetworkProbe/KIEM-TRA-NGUON.ps1 .local/handoff/GDLA-SOURCE-NETWORK-CHECK/
```

Kiểm thử: `SourceNetworkProbe --self-test` kiểm tra 9 trường hợp phân loại/an toàn; SourceSyncTests có chế độ `--network-only` (tham số thứ 5) chỉ dùng LocalDB mới riêng, discovery/chống trùng/backoff/khôi phục, không chạy workflow xuất bản. Không thay SQL vận hành. Bản sửa chưa merge/deploy; gói chẩn đoán có thể chạy riêng trước khi duyệt PR.
