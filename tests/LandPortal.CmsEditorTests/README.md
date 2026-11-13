# Kiểm thử CMS tập trung

Chỉ dùng SQL Server LocalDB instance mới `LPCms_<12 hex>` và database fixture `LandPortal_Test`. Không đọc `.env`, không dùng credential thật. Runner kiểm tra instance mới/database chưa tồn tại rồi mới tạo schema 18 từ migration chính thức. SMTP tắt; các tài khoản đều được sinh riêng cho fixture. Kết thúc dừng host và đúng instance, giữ evidence trong `.local/tests` (ngoài Git).

```powershell
powershell -NoProfile -File tests/LandPortal.CmsEditorTests/run.ps1 -DotnetPath (Get-Command dotnet).Source
```

Mặc định `-Focus All`: upload/tài liệu/workflow/lịch, video và các màn hình CMS. Có thể chọn `VideoAndScreens`, `Screens`, `WeeklyOnly` khi chỉ sửa nhóm tương ứng để không lặp các kiểm tra đã PASS. Mỗi mode luôn tạo fixture mới; không có tùy chọn trỏ vào database đang dùng.

Test host biên dịch chính Program/route/middleware của Web; riêng nguồn cấu hình và policy no-AV dùng DI trong fixture có ràng buộc LocalDB/loopback/storage mới/SMTP Disabled. Không đổi guard vận hành. Video controller test dùng byte/hash fixture để thử ràng buộc auto-attach; không gọi FFmpeg và không coi fixture là video thật đã chuyển mã. HTTP kiểm tra file PDF/DOCX/XLSX/PNG thật theo cấu trúc định dạng.

Kết quả ngày 13/11/2026 theo nhóm, không cộng lặp 18 kiểm tra setup/Identity:

- `cms-editor-d82bcec06fd2`: upload/workflow 35 PASS, sau đó dừng ở fixture Video đặt metadata sai loại; chưa tính cả lượt này là COMPLETE.
- `cms-editor-f16c33d20483`: video/controller 28 PASS sau khi sửa fixture, sau đó dừng ở fixture tạo video bằng tài khoản không có quyền video.
- `cms-editor-e10e99e6119b`: Screens COMPLETE 33 PASS = 18 setup + 15 màn hình/readiness/liên kết. GET lịch với bộ lọc chưa chính xác được thay bằng kiểm tra riêng sau.
- `cms-editor-452392c33b2e`: WeeklyOnly COMPLETE 19 PASS = 18 setup + truy vấn lịch tuần có bản ghi, khoảng ngày và trạng thái phiên bản thật.

Browser dùng `tests/browser/cms-editor-check.mjs`; đặt `CMS_EDITOR_RENDERED_ROOT` tới thư mục `editor.html`/`weekly.html` từ runner để dùng HTML MVC thực. Mặc định dùng fixture HTML. Endpoint browser được giả lập, không chứng minh quyền hoặc SQL; kiểm tra HTTP/SQL nằm trong runner trên. Cần cài Playwright cho môi trường kiểm thử, không thêm thư viện vào sản phẩm.

Nhóm tài khoản nhạy cảm được tách riêng tại `LandPortal.CmsAccountTests` và `docs/cms-account-handoff.md`.
