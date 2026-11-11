# Upload tài liệu PDF/DOCX/XLSX của biên tập viên (nhánh review)

Trạng thái 11/11/2026: **Build Release PASS; kiểm thử service/controller cô lập PASS; chưa nghiệm thu HTTP/SQL Server workflow, chưa merge, chưa triển khai IIS hoặc bật cấu hình**. Xem `managed-upload-iis-handoff.md`.

## Phạm vi

Quy trình biên tập viên gửi tệp qua Zalo, admin xem xét nội dung rồi đăng bài **không thay đổi**. Website chỉ thay cơ chế tiếp nhận tệp ở phần `/admin/documents/{contentId}` khi được cấp quyền quản lý media và sửa bài. Không tạo chức năng nộp hồ sơ của người dân; không tự đăng tải bất kỳ tài liệu nào.

- Hỗ trợ **.pdf, .docx, .xlsx** (định dạng hiện có; không hỗ trợ .doc/.xls cũ hoặc .docm/.xlsm).
- Chế độ riêng cho UAT/Staging hiện hành khi cấu hình private `Media:AllowManagedDocumentsWithoutAv=true`; mặc định **tắt**. Không bật trên Production.
- Bỏ yêu cầu scanner-health và worker antivirus đối với tài liệu **mới** trong luồng CMS này, **không** bỏ kiểm tra định dạng (PDF parser, OpenXML ZIP, hạn 10 MiB, reject macro/ActiveX/embedded/external relationship, Excel formula).
- Ghi trạng thái **ScanStatus=NotScanned**, **ValidationStatus=OfficeValidatedNoAv**. Không ghi `Clean` hoặc tạo bằng chứng quét giả.
- Tệp lưu ngoài webroot, khóa quyền xem/tải; chỉ public sau khi revision được duyệt và xuất bản như trước. Tệp mới thuộc phạm vi của bài chứa nó; theo dõi audit. Đối chiếu độ dài/SHA256 khi phục vụ; tài liệu không quét bắt buộc tải xuống như attachment và trả `X-Media-Scan-Status: NotScanned`.
- Luồng tài liệu cũ đang chờ scan, scan worker, 703 media nhập từ nguồn, dữ liệu SQL, upload công khai/công dân, video, TLS/Application Role không thay đổi. Bản đang chạy không tự nhận thay đổi nếu chưa triển khai.
- Luồng Công khai thông tin có điều kiện kiểm duyệt bổ sung vốn đòi `Clean`; bản này **không** tự bỏ điều kiện đặc biệt đó.

## Triển khai

1. Quản trị hạ tầng rà soát rủi ro bỏ quét virus và xác nhận chính sách vận hành. Duyệt nhánh GitHub.
2. Developer chạy build tập trung Web/Infrastructure và kiểm thử upload/tải/từ chối PDF hỏng, DOCX và XLSX macro hoặc quan hệ ngoài, kiểm thử quyền, trùng SHA và xuất bản bản nháp.
3. Deploy gói Web + Infrastructure (không dùng gói Runtime18 chỉ thay DLL), có bản sao lưu và rollback. Không migration, không đụng 703 media.
4. Đặt giá trị private `Media:AllowManagedDocumentsWithoutAv=true` cho đúng profile UAT đã được phê duyệt. Tuyệt đối không commit cấu hình bí mật.
5. Thử một tài liệu vô danh/hợp lệ → vào **Tài liệu đính kèm** → tải lên → trở lại bản nháp → duyệt và xuất bản theo quyền → tải về bằng attachment. Xác nhận không có tuyên bố scan Clean.

Lưu ý: đọc/duyệt nội dung qua Zalo **không phải** quét mã độc. PDF/DOCX/XLSX có thể chứa nội dung khai thác lỗi ứng dụng mở tài liệu. Chỉ mở khi được đơn vị vận hành chấp nhận rủi ro và duy trì cập nhật bản vá, RBAC, audit.
