# Phase 3F — tiếp tục thu thập và nhập Test

Quyền thu thập công khai hai nguồn **đã được người dùng xác nhận trực tiếp trong lượt Phase3F**. Không cần một hồ sơ phê duyệt độc lập mới để tiếp tục phần đó. Bản ghi máy đọc tại `.local/phase3a/approvals/phase3f-20261019T070725Z-fa27c9d0-user-confirmation.json` tham chiếu SHA của trích xác nhận trong run; đây là ghi nhận thẩm quyền đã có, không phải Codex tự đóng vai người phê duyệt. Người xác nhận là chủ dự án/người dùng trong phiên chat; chưa cung cấp họ tên để đưa vào hồ sơ ký chính thức.

SRS1.0: QLDD/SharePoint và gdla/NukeViet là hai nguồn chính thức. Chỉ đọc công khai, không đăng nhập/vượt CAPTCHA, không ghi nguồn, không truy cập CSDL gốc. Apply Dev, signer/trust, quyền Dev, quét file Dev, DNS và Production vẫn cần thẩm quyền riêng.

## Thu thập QLDD

Giữ session `9d95c26f-85a5-4a8a-a2df-61def03cf360`. Không tạo session để đặt lại quota. Danh sách 228 ứng viên thiếu snapshot được chia 12 work order, tối đa20 URL; mỗi order ràng buộc session, checkpoint gốc, SourceIdentity và SHA. Đây là thứ tự công việc, không phải giấy phê duyệt. Ưu tiên đa dạng chuyên mục, gồm76/77 chưa có bài kiểm chứng; chỉ tải detail đã chọn, robots và file liên quan, không chạy toàn bộ frontier danh sách trước.

Collector vẫn lưu SQL checkpoint trước request và sau mỗi bước; file immutable theo SHA. Dùng SQL `CrawlRuns.Checkpoint` làm con trỏ có thẩm quyền; file checkpoint mới nhất trên đĩa có thể là orphan nếu SQL commit chưa hoàn tất. Resume cùng session giữ tổng request và trạng thái từng URL. Không sửa checkpoint gốc hoặc raw cũ.

Giới hạn vận hành ban đầu: một kết nối, nghỉ>=2.05giây và jitter; giữ khoảng nghỉ qua restart. Mỗi invocation<=120GET/25page/20detail/100asset/600giây/256MiB, toàn phiên<=2000request/1000page/1000content/2GiB trong lease vận hành4giờ. Đây là các trần kỹ thuật thận trọng do đội triển khai chọn, không diễn giải thành hạn pháp lý. Robots/cutover vẫn chặn; 429/5xx có backoff/Retry-After và tối đa3attempt, không mở song song để tăng tốc. Hết lease/budget thì lưu checkpoint, không tự bỏ trần.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3f.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3f.ps1 -Action Plan -Checkpoint CHECKPOINT_PATH -CheckpointSha EXACT_SHA -ReportRoot .local/evidence/phase3f/NEW_UNIQUE_RUN
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3f.ps1 -Action Collect -Batch EXACT_BATCH_JSON -BatchSha EXACT_SHA -ApprovalFile .local/phase3a/approvals/phase3f-20261019T070725Z-fa27c9d0-user-confirmation.json -NoBuild
```

Không chạy lại helper vào label đã có. `commands-NOT-APPROVED.txt` của plan đầu là bản chuẩn bị trước khi gắn authority; thay placeholder bằng bản ghi xác nhận thật ở trên. Trạng thái xác nhận hiện hành lấy từ `authority-reference.json`, không từ nhãn mặc định trong work order. Các artifacts lịch sử NEEDS_APPROVAL không được sửa giả thành hồ sơ phê duyệt.

## Scanner và nhập

File mới ở quarantine/private, không hotlink hay tự gắn Clean. Chỉ tạo allowlist từ SHA đúng byte các capture `Origin=Network`, file đã xác minh định dạng, dữ liệu công khai trong phạm vi được người dùng cho phép. `Scan` dùng DefenderTest thật, SQLTest và kho Test; bỏ qua53 bản sao legacy trong lệnh scan mới, không quét/promote53 Dev. Nếu engine không sẵn sàng/byte sai/không Clean thì giữ staging, không nhập công khai để tăng số lượng.

Preview/Preflight/Commit/Verify sử dụng importer hiện có qua `scripts/phase3f.ps1`, tham số checkpoint/plan/proof đều SHA chính xác. Backup trước import và đối soát sau commit. Không lặp Commit khi kết quả chưa chắc chắn: kiểm SQL receipt, reconciliation và readback trước. Instrumentation3E được giữ, root private3F được cho phép cho SQLTest; không log SQL parameters/PII/credentials, không tăng timeout.

Dev chỉ được readonly `Preview` trong wrapper3F. Cần tách plan Dev khỏi Test; GUID Test không phải GUID Dev. Công cụ safe apply/verify/rollback hiện có vẫn đòi schema3, exact media-rootSHA, external Authenticode approval/trust/operator/action/expiry và backup. Quyền crawl không thay các gate đó. Không chạy Apply ở lượt này.

## NukeViet

Offline `NukeVietBundle` nhận snapshot/manifest SHA trong `.local`, không nhận trạng thái Clean do input tự khai. Parser news/canonical/namespace hiện có được giữ; body-match chỉ tạo đề nghị so trùng, không tự merge hoặc chọn bài đăng. File được sao vào quarantine theo hash; thiếu file, scanner, mapping nghiệp vụ và nguồn thật được ghi rõ. URL `.html` chưa được gán target khi chưa duyệt. Chạy lại cùng input vào thư mục mới không ghi SQL/CMS và không ghi đè checkpoint cũ.

Văn bản/route chưa có adapter đã được snapshot chứng minh phải để `NEEDS_DOCUMENT_ADAPTER_OR_ROUTE_REVIEW`; không đoán selector từ fixture hoặc coi fixture là export nghiệp vụ. SQL write adapter NukeViet chỉ hoàn thành sau khi có dữ liệu thật và mapping được đơn vị nghiệp vụ duyệt. Không nhập NukeViet dưới namespace QLDD.

## Các gate riêng

PDF3233/Video40/41 không retry các URL cũ nếu không có bằng chứng mới. Không lấy video trang40 từ template của41. SQL categorytimeout/rootcause lịch sử vẫn UNKNOWN; trace mới giúp ghi nhận khi tái diễn, không chứng minh đã chữa nguyên nhân cũ. Incidents raw V04/D15/v05 giữ PARTIAL; restore/Production scanner/MFA/SMTP/UAT/Word nghiệp vụ và cutover không được tự đánh dấu PASS. Website **CHƯA PRODUCTION READY**.
