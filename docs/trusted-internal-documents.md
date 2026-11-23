# Tài liệu nội bộ do quản trị viên xác nhận

PDF có chữ ký trước đây bị luồng PDF không antivirus từ chối toàn bộ `AcroForm`; thuộc tính bố cục `A` của `StructElem` cũng không phải hành động thực thi. Luồng mới tiếp nhận nguyên byte PDF/ZIP đã được quản trị viên xác nhận nguồn, có kiểm định kỹ thuật riêng. Không chuyển đổi PDF, không xác nhận tính hợp lệ mật mã của chữ ký và không tạo kết quả antivirus `Clean`.

## Phạm vi và cách dùng

- Chỉ trong Staging/UAT hợp lệ theo `UatDeployment.Validate`, dùng cấu hình runtime hiện có. Không thêm khóa `.env`, migration hoặc cấu hình IIS.
- Tài khoản cần `settings.manage`, `media.manage` và quyền sửa đúng hồ sơ/chuyên mục tại thời điểm tải và ngay trước khi lưu.
- Tại **Tài liệu của bản biên tập**, chọn **Tài liệu nội bộ đã kiểm soát nguồn**, nhập nguồn/phạm vi xác nhận và tải PDF/ZIP. Ô xác nhận mặc định không chọn. Luồng upload công khai, Office thông thường và các tệp cũ không thay đổi.
- Endpoint hiện có `POST /admin/documents/{contentId}?format=json` nhận thêm `trustedInternal=true`, `trustedSource` (không rỗng, tối đa 500 ký tự), file và CSRF của phiên CMS. Đây là xác nhận của người đăng nhập, không phải cờ trong metadata bài viết.
- Máy Developer có thể gửi qua HTTPS bằng công cụ CMS; không gửi mật khẩu vào tham số/log, không tải cookie hoặc secret lên Git. Kết quả upload thất lạc phải đối soát `/admin/documents/{contentId}/result?sha256=...` trước khi thử lại.

## Kiểm soát tệp

- `ScanStatus=NotScanned`, `ValidationStatus=TrustedInternalValidatedV1`; proof không có engine, ngày quét, hash kết quả quét hoặc lượt scan giả.
- Audit `document.trusted.accepted.v1` ràng buộc người tải, hồ sơ, MediaId, SHA-256, MIME và storage key. Audit `document.trusted.confirmed` ghi nguồn xác nhận. Trạng thái đơn lẻ không cấp quyền đọc/gắn/công bố.
- PDF: parser strict, tối đa 104 MiB/2.000 trang, một lượt kiểm định mỗi tiến trình, ngân sách 60 giây/1.000.000 token/độ sâu 64. Cho phép trường chữ ký số có cấu trúc ByteRange phù hợp và điều hướng click HTTP(S)/GoTo. Không cho JavaScript, tự chạy, Launch, hành động nối tiếp, biểu mẫu tương tác, XFA, tệp nhúng hoặc hành động nguy hiểm.
- ZIP: tối đa 128 MiB, 256 mục, 512 MiB sau giải nén, tỷ lệ nén tối đa 100; kiểm CRC, header, tên/đường dẫn, va chạm, tệp mã hóa, symlink, ZIP64, ZIP lồng và tệp thực thi. Chỉ PDF/DOCX/XLSX qua bộ kiểm định thật. Không giải nén vào filesystem website; chỉ đọc từng mục có giới hạn trong bộ nhớ.
- Khi gắn và phát hành, đọc lại audit/proof và SHA-256. Tệp sai byte bị cách ly. ZIP luôn trả `attachment`, `nosniff`, CSP sandbox; PDF tiếp tục dùng trình xem có guard hiện hành. Bài nháp hoặc đã thu hồi không cấp quyền tải công khai.
- Đây là kiểm định kỹ thuật và xác nhận nguồn nội bộ, không thay thế hay mô phỏng quét antivirus.

## Kiểm chứng tập trung

Không chạy full regression. Đã kiểm tra:

- 24/24 PDF gốc của đợt 2.3.1: đọc đủ trang, SHA-256 trước/sau không đổi; ZIP STT14 chứa 32 PDF đạt cấu trúc/CRC/hash, không giải nén lên website.
- 40 kiểm tra cô lập về PDF ký, thuộc tính bố cục, JavaScript/Launch/URI nguy hiểm, trường ký vòng, ZIP nguy hiểm, quyền, audit, hash, chống trùng và thu hồi.
- 43 kiểm tra ZIP hiện có: chính sách luồng thường không được tự thừa hưởng ngoại lệ này.
- 15 kiểm tra SQL/controller trên LocalDB mới schema18, gồm dịch truy vấn audit sang SQL Server và loại tệp thiếu grant/nháp/cách ly.
- 37 kiểm tra HTTP/SQL Server trên LocalDB riêng: đăng nhập giả lập bằng Identity/CAPTCHA thật, upload PDF/ZIP, CSRF/quyền, chống trùng, nháp → gửi duyệt → duyệt → công bố, tải byte gốc, giữ ngày hồ sơ. Chính sách chỉ tiêm qua DI của test host đã khóa loopback/LocalDB/kho thử; không đổi guard vận hành.

Lệnh kiểm tra HTTP riêng cho thay đổi này:

```powershell
powershell -NoProfile -File tests/LandPortal.ManagedUploadHttpTests/run.ps1 -TrustedInternal
```

`-DotnetPath` chỉ phục vụ checkout thử không có SDK `.local/dotnet`; không cần cấu hình máy IIS.

## Tiếp tục nhập 2.3.1

Bản sửa tích hợp phần ZIP của PR #24 và bộ lọc Hủy phôi GCN của PR #25 để cập nhật website một lần. Chưa tự merge/deploy hoặc nhập hồ sơ thật.

1. Sau khi review/merge, dùng quy trình hiện có: pull → `scripts/build-for-iis.ps1` → cập nhật nguyên `web` gồm `.runtime`. Giữ kho dữ liệu và cấu hình private; không sửa JSON/manifest sau build.
2. Đối soát CMS và danh mục hiện có; tạo đúng danh mục Hủy phôi GCN qua CMS nếu còn thiếu. Chuyên mục phụ phải nằm trong phạm vi Tài liệu được cấu hình, không tự gán ID mới hoặc công bố cả bản cấu hình nháp chưa rà soát.
3. Dùng kế hoạch/journal riêng 14 STT đã chuẩn bị; không nhập lại Lịch tuần, QHKHSDD hoặc nguồn QLDD. Xác nhận nội bộ từng upload bằng phiên quản trị, đối chiếu ID/hash trước khi gắn.
4. Giữ ngày hồ sơ đã chốt trong metadata; giữ audit xuất bản thực tế. STT1–12 công bố qua workflow khi đủ điều kiện; STT13/14 hẹn 09:00 +07 vào 03/12 và 11/12/2026 sau khi xác minh đồng hồ IIS. Không bù giờ giả.
5. Giữ PDF Gia Lai và ZIP chứa tài liệu Hà Nội của STT14 theo xác nhận riêng của chủ dữ liệu. Không sửa tiêu đề/Excel hoặc coi chữ ký là đã được xác thực mật mã.
6. Excel chỉ nhận hyperlink sau khi bài thật công khai và kiểm chứng. Bài đang hẹn vẫn ghi chờ.

Khi rollback binary, tệp mang trạng thái mới có thể bị bản cũ từ chối phục vụ; phải kiểm tra trước nếu đã công bố hồ sơ. Không đổi thành Clean hoặc hạ chính sách để tránh hiện tượng này. Chưa có dữ liệu thật theo chính sách mới ở thời điểm bàn giao.
