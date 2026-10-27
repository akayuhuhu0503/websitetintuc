# Vận hành scanner — 2C-SCAN

## Cập nhật Phase3C

Theo prompt3C mới, được quét53 public/anonymous **Test copies** và refresh7ảnhpublic, rồi Stage cóaudit lên mirrorSQLTest saubackup/byteverify. Phần cấm đổiTest53 ở mục lịch sử bên dưới được supersede **chỉ cho Test đã cho phép**; Dev53 vẫnNotScanned, khôngStageDev/khôngscanDev. [Báo cáo3C](phase-3c-media-recovery-report.md), [runbook3C](phase-3c-runbook.md). Proof SQL-receipted/hash/engine/policy/TTL24h, dừng2/resume58/reuse60khôngquétlại. Native60Clean,controlsEICARInfected/healthcancelScanFailed;khônggiảlậpClean. PolicyProduction/PII và externalapprovalDev chưađượcduyệt. Không chạy lại full2C/137 cho delta recovery.

Provider Test là Microsoft Defender có sẵn, được xác minh Microsoft Authenticode, service/engine hoạt động và chữ ký không quá72 giờ. Không cài service, không đổi firewall/exclusion/MAPS/SubmitSamplesConsent, không cập nhật chữ ký bằng lệnh của dự án. Native custom scan chỉ `-Scan -ScanType 3 -File <one Test file> -DisableRemediation`; không quét toàn máy. Exit0 không đủ kết luận: adapter đòi output hoàn tất, đúng đường dẫn fixture, “found no threats”, không có threat, bytes/hash copy không đổi; exit2 chỉ Infected nếu có threat/summary đúng, trường hợp khác ScanFailed.

Quy tắc này dựa trên [Microsoft MpCmdRun](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus): exit0 có thể bao gồm đã phát hiện/xử lý, exit2 cũng có thể là lỗi. `-DisableRemediation` là lựa chọn custom scan từng lệnh, không tắt bảo vệ thời gian thực hoặc sửa policy toàn máy. Parser không hiểu output/locale mới sẽ fail-closed, không suy Clean.

Máy Test hiện có SubmitSamplesConsent=1/MAPSReporting=2. Vì không thay policy và không được đưa tệp riêng tư lên dịch vụ ngoài, **DefenderTest chỉ nhận hash đã cho phép của fixture vô danh hoặc tài nguyên nguồn công khai**; không nhận tệp công dân tùy ý. Constructor khóa Development + LandPortal_Test + media/temp/keys dưới `.local/tests` + explicit opt-in + allowlist SHA256. Production/Staging/Dev bị chặn. Không dùng provider này cho PII thật dựa vào một checkbox. Production cần engine/hạ tầng on-prem được chấp thuận hoặc chính sách Defender do đơn vị có thẩm quyền duyệt.

Clamd adapter loopback INSTREAM vẫn có, mặc định không tự chuyển sang Defender. Clamd thiếu/timeout/sai protocol/StreamMaxLength sẽ ScanFailed. Khi có clamd được phép, xác minh PING/VERSIONCOMMANDS/signature database và StreamMaxLength≥10.485.760 byte; không mở TCP công cộng, không tự cài daemon. Xem [giao thức ClamAV](https://docs.clamav.net/manual/Usage/ClamdProtocol.html).

## Lệnh Test

Sau build `scripts/test-phase2c-scan.ps1`, đặt hash fixture đã xác minh (không lấy hash của dữ liệu người dân thật rồi gọi vô danh):

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-inquiry-scanner-test.ps1 -ScanBuild -Provider DefenderTest -ApprovedAnonymousHashes <SHA256> -Health
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-inquiry-scanner-test.ps1 -ScanBuild -Provider DefenderTest -ApprovedAnonymousHashes <SHA256> -FileId <GUID-Test> -Once
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2c-scan.ps1 -Region All
```

Nhiều hash phân cách dấu phẩy. Config chỉ process: Scanner__Provider, Scanner__DefenderAnonymousTestOnly=true, Scanner__DefenderAllowedTestHashes, Scanner__TimeoutSeconds (1–120, mặc định30). Không sửa .env. Health chỉ đọc metadata/count SQL, exit0 healthy/2 not-ready; không mutate queue. Worker --once exit0 có thể xử lý ScanFailed hoặc không có job, không phải chứng minh Clean. Testweb5229 và sink random do E2E sở hữu/dừng; manual Web/Sink/Worker dùng `run-interactions-test.ps1 -Scan -Component ...`, web5219/sink2525.

Không scanner daemon được tạo trong mốc này. Worker foreground Ctrl+C; không kill process khác/toàn dotnet. Các process E2E tự dừng đúng PID, full run có mutex SQL Test. Preview do lượt này mở có descriptor/start-time riêng; chỉ dừng theo descriptor đó. Giữ preexisting5198/5208 nếu có, không dùng descriptor lịch sử để kill PID được tái sử dụng.

## Kết quả, cách ly và sự cố

Upload hỏi đáp vẫn chỉ1 PDF/JPEG/PNG≤10MiB; không mở .com/ZIP cho khách. Private root ngoài static. SQL lưu NotScanned/Scanning/Clean/Infected/ScanFailed/Deleted. Claim applock/lease2phút, retry tối đa3/backoff2/4/8phút, scoped recovery khi có --file. Completion kiểm lease token/rowversion/hash/size, transaction state+audit một lần; late result không ghi đè. Infected không tự retry/promote. Audit chỉ AssetId/hash/size/state/provider/version/signature/time/output digest/error/latency, không PII/body/filename.

Signature health trước/sau Defender scan phải khớp; stale/changed/untrusted/not-running/offline/timeout/unknown output/oversize/unapproved hash đều fail-closed. Worker đọc/hash trước và sau trên cùng handle; file mất/quarantined bởi OS không được suy Clean. OS chặn EICAR thô là negative có thật; integration dùng ZIP chứa đúng mẫu EICAR68 byte trong vùng Test nội bộ theo [EICAR](https://www.eicar.org/download-anti-malware-testfile/), không chạy thực thi mẫu. Không tắt AV để ép test.

Private GET/HEAD/Range phải fresh read+file.read/current scope, đúng Clean proof và hash. Server giữ chính handle đã hash để gửi, không đóng rồi mở path khác. Bytes đổi:409, thu hồi kết quả cũ thành ScanFailed+audit; lần sau423. Role bị thu hồi/transfer không tải lại được; no-store/nosniff/attachment. Lookup/DTO/search/public/static/thumbnail/preview không cấp file cách ly. CMS/banner/source vẫn theo published revision/private preview/scanner policy2A/2B; không đổi lịch sử hoặc xóa media dùng chung.

Trực ca đọc queue counts qua -Health và audit `inquiry.scan.completed`/`inquiry.scan.invalidated`; đối chiếu pending/fail/infected/latency, điều tra attempts3 chưa có UI reset. Khi engine unavailable: giữ private, không gỡ policy. Infected: không preview/tải/extract qua public, không tự retry. Lưu hash/correlation/version, chuyển đơn vị an toàn thông tin theo quy trình được duyệt; chưa có alert hạ tầng/escalation Production tự động.

Chưa có chính sách retention hồ sơ chính thức. GC chỉ tệp chưa hoàn tất OTP/orphan do hệ thống tạo theo TTL; không xóa source/đã gắn hồ sơ. Bảo toàn SQL+media+keys, backup checksum/target theo runbook dự án; VERIFYONLY không phải diễn tập restore. Email outbox Uncertain giữ nguyên quy trình đối soát, không có scanner retry thay email retry.

## 53 tài nguyên nguồn

Chỉ sao chép từ kho Test vào thư mục Test read-only, đối chiếu trước/sau với manifest53 hash, scan thật và ghi báo cáo riêng. Không áp kết quả vào MediaAssets Test/Dev hoặc published pointers. Bản gốc vẫn NotScanned; kết luận Test chỉ là đánh giá tại thời điểm/definition cụ thể, không bảo đảm tuyệt đối an toàn. Không crawl lại hoặc tính2video metadata là video đã chuyển.

Áp dụng tương lai cần quyết định có thẩm quyền: scope/backup/rollback, exact byte/hash+engine/time, xử lý findings, bảo vệ cache/last-live-reference và thông báo ảnh hưởng; Test trước, Dev/Production theo quyền riêng. Không tự ẩn53 tệp đang công khai. Scanner Production/chương trình quét nguồn, SMTP/HTTPS/restore/UAT/quyền vận hành vẫn là blocker. SQL timeout MONITORING/ROOT CAUSE UNKNOWN, V04 rawsmoke2B historical PARTIAL không bị đóng bởi scanner.
# Liên kết mốc2D

Video dùng [worker/runbook2D](video-worker-runbook.md) và [policy media/revision](video-media-architecture.md). Mặc định scanner hỏi đáp2C vẫn10MiB; chỉ instancevideo được truyền max≤1GiB, không nới uploadhỏiđáp/PDF thông thường. Chỉ Development/SQLTest/khoTest/explicit SHA fixturevôdanh-public mới cóDefenderTest.53originalsDev giữNotScanned; Production/privatePII vẫn chưa được duyệt. Kết quả2D không tự nâng trạng thái overall2C hoặcsource53.

