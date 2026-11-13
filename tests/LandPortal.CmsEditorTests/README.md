# Kiểm thử CMS tập trung

Chỉ dùng SQL Server LocalDB instance mới `LPCms_<12 hex>` và database fixture `LandPortal_Test`. Không đọc `.env`, không dùng credential thật. Runner kiểm tra instance mới/database chưa tồn tại rồi mới tạo schema 18 từ migration chính thức. SMTP tắt; các tài khoản đều được sinh riêng cho fixture. Kết thúc dừng host và đúng instance, giữ evidence trong `.local/tests` (ngoài Git).

```powershell
powershell -NoProfile -File tests/LandPortal.CmsEditorTests/run.ps1 -DotnetPath (Get-Command dotnet).Source
```

Mặc định `-Focus All`: upload/tài liệu/workflow/lịch, video và các màn hình CMS. Có thể chọn `VideoAndScreens`, `Screens`, `WeeklyOnly` khi chỉ sửa nhóm tương ứng để không lặp các kiểm tra đã PASS. Mỗi mode luôn tạo fixture mới; không có tùy chọn trỏ vào database đang dùng.

Rà soát bổ sung PR #11 dùng `-Focus Followup`: gắn video phía server và hẹn giờ với tài khoản đồng thời có Review/PublishApproved/PublishDirect. Có thể chọn `VideoFollowup` hoặc `ScheduleOnly` để kiểm tra riêng sau sửa. Video dùng hook của Worker và SQL thật, không gọi HTTP để gắn; hẹn giờ dùng HTTP thật, sau đó chạy SchedulerService để kiểm tra lại người duyệt và xuất bản. Không đọc `.env`, không chạy FFmpeg hay SQL vận hành.

Browser bổ sung: `cms-video-background-check.mjs` kiểm tra bảo toàn nội dung/rowversion khi server gắn video; `cms-schedule-check.mjs <evidenceRoot>` dùng `schedule-pending.html` và `schedule-approved.html` từ runner. Cả hai dùng editor/Jodit thật, endpoint giả lập; không thay thế kiểm tra HTTP/SQL ở trên. Nhóm video editor hiện có cũng được chạy tập trung để kiểm tra tích hợp polling/upload.

Test host biên dịch chính Program/route/middleware của Web; riêng nguồn cấu hình và policy no-AV dùng DI trong fixture có ràng buộc LocalDB/loopback/storage mới/SMTP Disabled. Không đổi guard vận hành. Video controller test dùng byte/hash fixture để thử ràng buộc auto-attach; không gọi FFmpeg và không coi fixture là video thật đã chuyển mã. HTTP kiểm tra file PDF/DOCX/XLSX/PNG thật theo cấu trúc định dạng.

Kết quả ngày 13/11/2026 theo nhóm, không cộng lặp 18 kiểm tra setup/Identity:

- `cms-editor-d82bcec06fd2`: upload/workflow 35 PASS, sau đó dừng ở fixture Video đặt metadata sai loại; chưa tính cả lượt này là COMPLETE.
- `cms-editor-f16c33d20483`: video/controller 28 PASS sau khi sửa fixture, sau đó dừng ở fixture tạo video bằng tài khoản không có quyền video.
- `cms-editor-e10e99e6119b`: Screens COMPLETE 33 PASS = 18 setup + 15 màn hình/readiness/liên kết. GET lịch với bộ lọc chưa chính xác được thay bằng kiểm tra riêng sau.
- `cms-editor-452392c33b2e`: WeeklyOnly COMPLETE 19 PASS = 18 setup + truy vấn lịch tuần có bản ghi, khoảng ngày và trạng thái phiên bản thật.
- `cms-editor-d58a2a3f1f9a`: Followup COMPLETE 57 PASS = 18 setup + 19 video + 20 hẹn giờ. Sau review chỉ nhóm video thay đổi và được chạy lại ở lượt dưới; không cộng lặp hai lượt.
- `cms-editor-ed0afcf5f0d7`: VideoFollowup COMPLETE 40 PASS = 18 setup + **22 video**, bổ sung autosave gỡ tệp trước snapshot, kiểm tra khóa khi gỡ và giới hạn 100 tệp. LocalDB riêng đã dừng. Hẹn giờ giữ kết quả 20 PASS của lượt trên.

Browser dùng `tests/browser/cms-editor-check.mjs`; đặt `CMS_EDITOR_RENDERED_ROOT` tới thư mục `editor.html`/`weekly.html` từ runner để dùng HTML MVC thực. Mặc định dùng fixture HTML. Endpoint browser được giả lập, không chứng minh quyền hoặc SQL; kiểm tra HTTP/SQL nằm trong runner trên. Cần cài Playwright cho môi trường kiểm thử, không thêm thư viện vào sản phẩm.

Nhóm tài khoản nhạy cảm được tách riêng tại `LandPortal.CmsAccountTests` và `docs/cms-account-handoff.md`.
