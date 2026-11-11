# Bàn giao upload quản trị không antivirus

Ngày 11/11/2026. Kiểm thử HTTP/SQL Server cô lập đã hoàn tất: đủ điều kiện đề xuất merge PR #1. Chưa triển khai hoặc nghiệm thu trên IIS thật.

## Đã kiểm tra trên Developer

- Release Web, Infrastructure, Worker và project kiểm thử hiện có: build PASS, không warning/error.
- Chạy `dotnet run --project tests/LandPortal.ManagedUploadTests -c Release`: database SQLite trong bộ nhớ và kho tạm riêng, không kết nối SQL Server/migration, không dùng tài khoản thật.
- Kết quả lần chạy cuối: 40/40 kiểm tra tập trung PASS; thêm 1 kiểm tra parser Publish nhận hai cờ mới PASS. Không cộng các lần chạy trước. Kiểm tra Office là kiểm tra cấu trúc và nội dung nguy hiểm, không phải chứng nhận toàn bộ chuẩn OpenXML hoặc kết quả antivirus.
- Upload thật JPG/PNG (giải mã/mã hóa lại, SHA), PDF/DOCX/XLSX, private read, giả định dạng, MIME sai, giới hạn byte, PDF hỏng/hành động JavaScript, macro, công thức, quan hệ ngoài, XML lỗi, hash trùng/tamper, policy tắt, quyền không đủ, thu hồi public grant; banner nháp/công bố; validator gắn ảnh đại diện và tài liệu vào nháp.
- Public controller được gọi trực tiếp với grant fixture lưu trong SQLite: attachment, nosniff, trạng thái NotScanned. Đây **không phải** chứng cứ đăng nhập/CSRF qua HTTP hoặc duyệt/xuất bản bài qua transaction SQL Server. Không giả lập SQL lock để báo workflow PASS.
- Bổ sung E2E thật bằng `powershell -NoProfile -File tests/LandPortal.ManagedUploadHttpTests/run.ps1`: **48/48 kiểm tra PASS trong lần chạy cuối** (gồm setup/guard và các bước HTTP), không cộng lần chạy trước. SQL Server LocalDB 17.0.1000.7, compatibility 160, database test mới đủ 18 migration. Kestrel dùng middleware/controllers/views hiện hành; chỉ test host inject MediaUploadOptions no-AV, không nới guard vận hành.
- HTTP Identity/CAPTCHA/CSRF thật với bốn tài khoản giả: JPG cover, PNG trong thân bài, PDF/DOCX/XLSX đính kèm; SQL xác nhận đủ 5 liên kết nháp; xem trước/tải riêng; Submit → Approve → PublishApproved; bài công khai 200; ba tài liệu attachment/nosniff/byte khớp và hai ảnh 200. Từ chối file giả, thiếu CSRF, upload/đọc/xuất bản không đủ quyền. Cả 5 media giữ NotScanned.
- Các lần harness ban đầu chưa đạt được giữ riêng: sai tên trường form, ảnh bị gửi nhầm vào danh sách tài liệu, và đường dẫn MDF mặc định trùng giữa instance. Đã sửa harness theo đúng form CMS và đường dẫn SQL riêng từng run; không sửa guard ứng dụng. Kết quả cuối tại `.local/tests/noav-http-3dc8b7223ec5/result.json` (private, không commit).
- Chưa kiểm tra browser/IIS thật; LocalDB không chứng minh cấu hình vận hành SQL Server 2022/IIS. Không đọc/ghi lại 703 media thật, tài khoản BA hoặc SQL vận hành. Runtime18/TLS/Application Role giữ nguyên. Host và instance test đã dừng; dữ liệu test giữ lại để đối soát, không xóa database khác.

## Sau khi đủ gate và merge main

1. Tại checkout source riêng trên máy build/IIS, kiểm tra `git status`, rồi `git fetch origin`, `git switch main`, `git pull --ff-only origin main`. Đối chiếu `git rev-parse HEAD` với SHA bàn giao **sau merge**.
2. Giữ file `.env` private hiện hành. Sau phê duyệt vận hành, hai cờ mới là `Media__AllowManagedImageWithoutAv=true` và `Media__AllowManagedDocumentsWithoutAv=true`; mặc định false. Chỉ Staging/UAT hợp lệ. Không sửa trực tiếp `private/uat.json` vì manifest ràng buộc hash. Nếu giữ nguyên private config đang chạy, vận hành có thể đặt hai biến môi trường này cho đúng IIS App Pool và cả 5 Worker rồi restart đúng tiến trình; không đặt toàn máy và không thay các giá trị SQL/TLS/role/secret.
3. `powershell -NoProfile -File scripts/build-for-iis.ps1 -OutputName TTDL-IIS-ManagedUpload` để tạo **toàn bộ** Web/Worker/ops/manifest tương thích tại `.local/publish/TTDL-IIS-ManagedUpload`. Chạy từ checkout source ngoài thư mục đang phục vụ, chọn tên output mới nếu tên này đã tồn tại; không build đè bản đang chạy.
4. Trong cửa sổ bảo trì, dừng đúng App Pool WWW_GDLA và chỉ các dịch vụ Worker đang chạy. Sao lưu binary Web/Worker, ops, manifest và cấu hình hiện hành vào thư mục riêng ngoài webroot, ghi hash; giữ bản này để rollback. Không backup SQL hoặc copy lại media cho thay đổi này.
5. Dùng quy trình cập nhật **gói đầy đủ** của đơn vị; giữ nguyên cấu hình IIS/web.config môi trường, appsettings, private config/secret/media/data. Manifest phải khớp file thực tế và private config được giữ lại; nếu công cụ cập nhật không hỗ trợ giữ và ràng buộc lại cấu hình, dừng để bổ sung đúng công cụ, không chép DLL lẻ hoặc bỏ guard. Không dùng Runtime18 hotfix chỉ thay Infrastructure.dll.
6. Khởi động lại đúng pool và các Worker trước đó đã chạy. Kiểm tra `/`, `/tin-tuc`, `/admin/login`, `/health`; bằng tài khoản được cấp quyền, thử ảnh banner và PDF/DOCX/XLSX qua nháp → duyệt → xuất bản → tải, không dùng nội dung người dân thật. Tệp mới phải là NotScanned, tài liệu tải dạng attachment; người không có quyền bị từ chối.
7. Nếu lỗi, dừng đúng pool/Worker, khôi phục **cả bộ binary và manifest/cấu hình trước cập nhật**, khôi phục giá trị hai cờ trước đó, rồi khởi động lại/kiểm tra URL. Không rollback SQL. Giữ media mới riêng tư; không xóa để rollback.

Không cấp phê duyệt giả hoặc tự bật hai cờ tại Developer. Đọc/duyệt nội dung không phải kết luận antivirus; tệp NotScanned vẫn có rủi ro khi được mở bằng ứng dụng người dùng. Không mở rộng sang citizen/video/disclosure Clean-only.
