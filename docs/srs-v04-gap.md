# Đối chiếu SRS v0.4 — V04-A, 14/10/2026

Đã đọc toàn bộ SRS, bảng thay đổi32CH và prompt tiếp nối. Baseline2B có code/handoff thật, không triển khai lại. Các trạng thái dưới đây chỉ áp dụng bằng chứng local; không đóng toàn bộ SRS hoặc yêu cầu production. Nguồn Dev giữ37 nội dung,53 tài nguyên,70capture/68SQLsnapshot,8ngoại lệ,2video metadata/0tệp.

Ký hiệu bằng chứng: **R**=`.local/evidence/v04/regression-tests.json` (83 nhóm thực sau sửa cuối); **H**=`.local/evidence/v04/http/http-report.json` (6 kiểm tra endpoint/phiên/SEO); **U**=`.local/evidence/v04/ui/{before,after}`; **P**=`before-dev.json`/`after-dev.json` (0 sai khác/17 nhóm); **I**=`before-identity-dev.json`/`after-identity-dev.json`; **S**=`docs/evidence/publish-v04-smoke.json`. UI/HTTP/worker là đơn vị riêng, không cộng vào83.

| CH | Yêu cầu/delta | Bằng chứng code/SQL/UI/test | Trạng thái | Rủi ro / thay đổi hoặc backlog |
|---|---|---|---|---|
| 01 | SQL Server, không đổi nền tảng | EF SqlServer;4 migrations có thật; backup Test219/Dev130 tệp, VERIFYONLY và hash | Đạt | Migration mới4 cột nullable; restore chưa diễn tập |
| 02 | qldd chính, gdla chỉ tham khảo | SourceCatalog/collector allowlist; P; offline importer trong R | Đạt | Không gọi nguồn/crawl trong lượt này; nguồn khác ngoài phạm vi |
| 03 | Không giả có export/code SharePoint | Pipeline snapshot/public đã có; không thêm adapter không có quyền | Ngoài lượt | Phase3: phạm vi và quyền export riêng; không hứa tổng nguồn |
| 04 | Bốn trang đọc/tra cứu, homepage gọn | public-v04.css/Home/Portal Razor; U20 trang thật/3 thao tác | Đạt | CHỜ DUYỆT THIẾT KẾ, không là UAT |
| 05 | Đủ điểm đến, menu bàn phím/mobile | U:14 chuyên mục đích200, skiplink/Tab/Escape; menu config live giữ nguyên | Chưa xác minh | Dịch vụ công/Thư điện tử chưa có đích chính thức trong cấu hình live; Q04. Không tạo href# hoặc tự gán URL |
| 06 | Chuyên đề tổng hợp, không nhân bản bài | Source identity/alias/secondary categories bảo toàn; không có cấu hình chuyên đề đã chốt để sửa | Chưa xác minh | Q12: Đề án06/CSDL quốc gia và quan hệ chuyên đề cần nội dung/phân loại biên tập; không tạo bài trùng |
| 07 | Tiếng Việt, exact số hiệu, liên quan/mới, SQL paging | PublicContentQuery.Relevant; controller giữ filters URL; R:Unicode/slash/wildcards/23 fixtures10/10/3 và card materialization cùng thứ tự | Đạt | HTML+metadata; không full-text PDF/OCR, không benchmark toàn nguồn |
| 08 | Phân biệt rỗng/lỗi/gỡ, log thật | PublicSiteService ghi SQL code/block/correlation; lỗi Featured trả500; phụ hiện block-error. U:empty200/404/error500; R:withdraw/cache | Đạt | Timeout tái hiện được; không gọi đã FIXED. Xem risk SQL |
| 09 | Mẫu thông báo chỉ PDF, không bắt ảnh | R:PDF nguồn thật trong Test, body trống/cover null vẫn publish đúng quyền | Đạt | Không sửa bài Dev hoặc bịa body/ảnh |
| 10 | Workflow2A, đúng revision, URL ổn định | R:approval/hash/concurrency/autosave/restore/public isolation; ContentCard.Url dùng ID, URL slug cũ vẫn resolve; P alias74 nguyên | Đạt | Canonical theo ID; không thực hiện chuyển domain/301 toàn nguồn |
| 11 | Scope gồm chính và mọi mục phụ | CurrentPermissionService.Scope/ScopeRevisions; Workflow/Media/Admin/History/Scheduler endpoints; R/H:deny chính/phụ, replay/clone/private media, requester+reviewer | Đạt | SQL claims scope; legacy chưa có scope giữ quyền cũ. I chứng minh không gán scope/quyền Dev |
| 12 | Idle30min, cảnh báo/reauth giữ bản soạn | AdminSessionPolicy/Cookie/SessionController/editor.ts; H cookie có idle31min và expiry tương lai vẫn401; GET poll không renew; CMS browser reauth và lưu SQL thật | Đạt | Bản chưa lưu giữ trong tab đang mở; đóng/reload trước lưu có thể mất. CAPTCHA/MFA không được coi hoàn tất |
| 13 | Hiệu lực Unknown, ngày biên/evidence theo revision | ContentMetadata.EffectiveStatusAt, nullable until/boundary; R SQL/public/draft/approval/boundary UTC+7; Dev0 giá trị mới | Đạt | Q09 vẫn cần xác nhận nguồn và rule từng văn bản; không tự kết luận pháp lý/merge số hiệu trùng |
| 14 | OTP một lần/rate limit/fail closed | Không có gửi tương tác giả; chưa triển khai OTP | Ngoài lượt | 2C, SMTP và ngưỡng cấu hình phải kiểm thử thật |
| 15 | Tách trả lời/email/public/outbox | Không gửi email thật hoặc báo đã gửi | Ngoài lượt | 2C, retry/idempotency/outbox |
| 16 | Tra cứu hồ sơ riêng tư/email xác minh | Chưa có API hồ sơ công chúng mở mới | Ngoài lượt | 2C, ACL tệp/export/private detail |
| 17 | SLA lịch làm việc có căn cứ | Không tạo lịch nghỉ/SLA giả | Ngoài lượt | 2C/Q05: lịch/nghỉ/làm bù/gia hạn |
| 18 | Góp ý dự thảo riêng/OTP/hạn revision | Draft lookup và hạn giữ2B; không có submit giả | Ngoài lượt | 2C vẫn phải có bình luận, hỏi đáp, liên hệ, góp ý kiểm duyệt |
| 19 | Danh mục hành chính theo kỳ/version | Không thêm số liệu thống kê giả | Ngoài lượt | 2E/Q06 |
| 20 | Excel staging/preview hash/atomic/version | Không tự xây import thống kê trong lượt này | Ngoài lượt | 2E, bảo toàn lịch sử/danh mục/đơn vị |
| 21 | Upload video máy, scanner/processing | P:2 metadata/0tệp; homepage không hiển thị block video như đã chuyển | Ngoài lượt | 2D bắt buộc upload trực tiếp, không thay bằng YouTube |
| 22 | Giới thiệu/lịch/sổ tay có nội dung được phép | CMS StaticPage và revision2B giữ; điểm đến/lịch chuyên mục/sổ tay không bị bỏ | Chưa xác minh | Q12: chưa có bộ lịch chính thức cùng dữ liệu ngày/buổi/chủ trì/thành phần, nhóm sổ tay/chuyên đề để nghiệm thu đầy đủ; không seed giả |
| 23 | Sự kiện view, tổng hợp UTC+7/xếp hạng | Không thêm bộ đếm giả; related hiện4 bài, chưa thống kê7/30ngày | Ngoài lượt | 2F |
| 24 | Config draft/live, media ACL, cache | R:site7 nhóm SQL + HTTP/UI config, draft banner/logo/privatepreview, source14 protected, expiry/withdraw/shared media | Đạt | Không apply Dev config hoặc cấp site permissions; Q03 nhận diện production |
| 25 | Worker SQL bền vững, exact quyền/revision | R scheduler9 + HTTP3/UI10 + hai worker, restart/race/cancel/retry/scan; scope mới kiểm tra lại lúc chạy; S/package worker exit0 riêng | Đạt | Không cài service;5 phút/catch-up không là SLA đã chứng nhận |
| 26 | Đối soát mapping/toàn nguồn | P17 nhóm/source bytes/70↔68; offline import R | Ngoài lượt | Phase3 chưa nhập toàn nguồn; không gọi37/37 là full migration |
| 27 | Alias/301 đúng đối tượng, media/domain | P alias74; R public/private/withdraw | Ngoài lượt | Phase3 kiểm tra đủ mapping và quyền domain; không đổi DNS |
| 28 | Sync chỉ tới cutover, không đè editor | R saved snapshots/idempotency/change comparison/cutover restart giữ | Ngoài lượt | Cơ chế đã có; chưa cutover Dev hoặc đồng bộ toàn nguồn |
| 29 | Responsive/accessibility/CWV có tiêu chí | U:360/390/768/1024/1440, reflow/images/labels/keyboard; lead eager/high priority; reduced-motion CSS | Đạt | Edge viewport, chưa thiết bị thật/WCAG2.2AA chứng nhận/CWV field/tải đại diện |
| 30 | Scanner/MFA/SMTP/HTTPS/restore | R scanner Production/Staging/worker deny; backup/hash thật; package audit phát hiện2 advisory và pin Pkcs10.0.13, actual loaded web/worker xác nhận | Chưa xác minh | Production scanner/MFA/SMTP/HTTPS/restore/RPO-RTO/UAT vẫn BLOCKER; không sửa SDK hệ thống |
| 31 | Canonical/OG đúng public, private không SEO | _Layout/Article/controller; H:public title/body/OG không đổi bởi draft, preview không OG; withdraw404 | Đạt | Base URL cấu hình; không có sitemap/JSON-LD mới để tuyên bố; SEO production chưa đo |
| 32 | Bằng chứng theo run, không cộng test | R83/83 sau sửa cuối; baseline71/73 và fail82/83 lưu riêng, U/S/worker tách đơn vị | Cần sửa | Một artifact raw smoke2B bị tên báo cáo kế thừa ghi đè; giữ summary73/historical report, V04 có file riêng; cần bản gốc nếu muốn phục hồi raw, không bịa tái tạo |

P0 runtime/data/workflow thuộc lượt đã có regression thật. Audit32CH hoàn tất không đồng nghĩa32CH đã triển khai xong. Backlog Q04/Q09/Q12 và2C/2D/2E/2F/Phase3/4 giữ riêng. Hạng mục CH32 làm checkpoint V04-C **PARTIAL về bảo toàn artifact lịch sử**, dù kiểm thử runtime hiện tại PASS.
