# Tích hợp phát hành ngày 18/11/2026

Nền origin/main: `4355c465b151487c460dd1b01910094e68f1160e`.

Đã tích hợp bằng merge giữ lịch sử: PR #9 (`dc54ab9`), #16 (`ba07736`), #17 (`e56925e`), #18 (`5fa1786`), PDF/nhãn tuần (`f06809f`). QHKHSDD lấy từ worktree `feat/planning-document-web-import` trên nền #17, áp dụng diff ba chiều; không chép đè file từ nhánh cũ. Không đưa dữ liệu ZIP/Excel/PDF, journal hoặc xác nhận ngày private vào Git.

Xung đột duy nhất: `VideoTestAccess.cs`; giữ cả InternalsVisibleTo của WebBundleTests và WebVideoLifecycleTests. Các merge tự động được build/kiểm tra trên bản tổng hợp. Worker cũ chỉ có chẩn đoán startup và lựa chọn metadata IIS; cơ chế hàng đợi trong Web của #10 vẫn giữ nguyên, không thêm dịch vụ Windows. Runtime18, TLS, Application Role và mã migration không đổi.

## Kiểm tra bản tích hợp

- Release Web, Worker, WeeklyScheduleImport, MigrationCli và các dự án test liên quan: PASS, 0 lỗi/cảnh báo.
- Storage boundary: 55 kiểm tra PASS, metadata IIS giả cô lập và ACL Windows thực trong fixture; không tác động IIS.
- Editorial PDF: 37 kiểm tra HTTP/SQLite PASS, năm tệp thực đã chuẩn hóa, 104 MiB/2.000 trang, hash tải lại, NotScanned, quyền/CSRF/định dạng; các luồng khác giữ giới hạn.
- Publish: 7 kiểm tra XML/converter WebBundle PASS; giữ private hash, hidden segments, chỉ tăng giới hạn HTTP tại admin/documents.
- PDF inline/nhãn tuần: 25 kiểm tra HTTP/SQL LocalDB cô lập PASS; View/Download, Range, ngày đăng, bộ lọc và thu hồi/cách ly.
- Video timeout/retry: 39 kiểm tra tập trung PASS; FFmpeg thực, mặc định 1.800 giây, hai luồng CPU, một lượt quản trị bổ sung có audit, không giả Clean, không tự xuất bản.
- Source Sync: 51 kiểm tra tập trung PASS; dữ liệu nguồn lưu trước và nguồn giả trong Test, mặc định tắt, phân quyền, hàng đợi bền vững, chống trùng/fencing, không tự công bố. Không crawl nguồn thật trong lượt tích hợp.
- git diff --check PASS. Không chạy toàn bộ bộ E2E upload/video cũ.

## Giới hạn và cập nhật sau duyệt

Chưa deploy hoặc kiểm tra IIS thật; không sửa SQL vận hành/config private. 32 ngày QHKHSDD giữ trong bộ xác nhận riêng, không hardcode vào source. Bộ 32 hồ sơ có 33 PDF vì STT24 gồm hai tài liệu nguồn riêng. PDF chuẩn hóa có cảnh báo mất chữ ký số mật mã, vẫn NotScanned. Không nhập hồ sơ thật.

Menu fallback có QHKHSDD dưới Văn bản. Nếu website sử dụng menu CMS, Admin chỉnh mục LandPlanning hiện có trong bản nháp và công bố theo workflow, không tạo trùng hoặc tự seed khi startup. Source Sync mặc định tắt; chỉ bật qua CMS khi có quyết định vận hành.

Quy trình IIS giữ nguyên: `git pull --ff-only` → `powershell -NoProfile -File scripts/build-for-iis.ps1` → cập nhật nguyên `web` gồm `.runtime`. Không trộn DLL hoặc sửa uat.json/manifest sau Publish. Việc cập nhật không tự retry video cũ; người có quyền quản trị dùng thao tác phục hồi timeout một lần trong CMS.
