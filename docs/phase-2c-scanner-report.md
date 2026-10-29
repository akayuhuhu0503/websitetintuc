# Báo cáo 2C-SCAN — 15/10/2026

**2C-SCAN PASS LOCAL trên SQL Server Test với engine Microsoft Defender thật.** Toàn bộ 2C/2C-Finish vẫn **PARTIAL** đến khi đối chiếu riêng toàn bộ tiêu chí; toàn dự án **CHƯA PRODUCTION READY**. Không làm2D/2E/2F/Phase3/Phase4. Không đổi ASP.NET Core10/MVC-Razor/SQL Server hoặc dựng lại chức năng cũ.

Task `scan-20261015T023338Z-f412ffbd`. Full runner cuối `scan-tests-20261015T030513Z-767f928e`: **100/100 nhóm PASS, 0 FAIL**, exit0, 2026-10-15T03:05:14.1157371+00:00–2026-10-15T03:11:24.7593622+00:00 UTC; build web/worker0 warning/0 error. 245 frozen paths,0 drift tại bàn giao. UI/HTTP/worker/source-copy/package là bằng chứng riêng, không cộng vào số nhóm. [Summary/hash](evidence/phase2c/scan-20261015T023338Z-f412ffbd/summary.json), [vận hành](scanner-operations.md).

## Hiện trạng và gap đã xử lý

Trước sửa đã đọc master/2A/2B/SRS/change table/2C/Finish/SCAN/state/reports/acceptance. Không có hướng dẫn thư mục con bổ sung. Git toàn workspace là WIP/untracked có sẵn; không stage/commit/push. Baseline Finish focused8 có242 đường dẫn băm, đối chiếu trước sửa0 drift. Migration cuối thực `20261014163306_InquiryFinish`. Port5198/5208/5219 không còn listener lúc khảo sát; không dừng process lịch sử theo PID cũ.

| Phần | Trước sửa | Delta / bằng chứng | Hiện tại |
|---|---|---|---|
| Upload hỏi đáp, OTP, private1file/10MiB | ĐÃ CÓ | Tái dùng `InquiryAttachmentService`, bốn luồng, assignment/lookup/SLA/Excel/batch/outbox; regression SQL/HTTP/UI mới | Giữ nguyên |
| Scanner thật | BLOCKED: clamd chưa có endpoint | `DefenderInquiryScanner.cs`: MpCmdRun đã cài, Microsoft Authenticode hợp lệ, health/version/signature, explicit Test hash allowlist | SCAN VERIFIED Test |
| Worker lease/retry/hash | ĐÃ CÓ, thiếu audit chi tiết/fence rowversion | `InquiryScanWorker.cs`: token+rowversion+hash+size, transaction kết quả/audit, scoped recovery, attempt/backoff hữu hạn | PASS LOCAL |
| Download Clean / Infected | THIẾU bằng chứng dương thật; close/reopen path có cửa sổ đổi file | `InquiryManagementController.cs`: hash và trả cùng handle, HEAD/Range cùng ACL, quyền/rowversion kiểm lại; tamper thu hồi proof/audit | PASS LOCAL |
| Test/source report | THIẾU Clean/EICAR và full sau hai sửa Finish | `ScannerTests`, HTTP childworker, Edge40viewport/actions,53read-only copies, final full mới | PASS LOCAL các ca dưới |
| Arbitrary PII/Production scanner | BLOCKED hạ tầng/policy | Không gửi file công dân lên SaaS, không sửa cloud policy, không tự cài daemon | Vẫn BLOCKED Production |

## Engine thật và bằng chứng byte

Windows Defender đang Running/Normal, real-time active; engine **1.1.26090.9**, product **4.18.26090.9**, signature **1.461.27.0**, cập nhật **2026-10-14T14:43:34Z**. Adapter kiểm binary Authenticode Microsoft và health trước/sau, chữ ký≤72giờ; version đổi giữa hai đầu/lỗi/timeout/output không hiểu đều ScanFailed. Không update definitions/install service/thay exclusion/MAPS/SubmitSamplesConsent/firewall.

Lệnh thật: `MpCmdRun.exe -Scan -ScanType 3 -File <random-Test-copy> -DisableRemediation`. Không dùng riêng exit0 làm Clean: cần đúng output hoàn tất+file không phát hiện, không threat, bytes/hash không đổi; exit2 cần threat summary rõ ràng để Infected, còn lại ScanFailed. Quy tắc exit code và custom `-DisableRemediation` theo [Microsoft](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus); không tắt AV thời gian thực. Kết quả Clean là đánh giá của engine tại thời điểm đó, không bảo đảm an toàn tuyệt đối.

| Fixture | Byte / SHA256 | Kết quả thật |
|---|---|---|
| PNG vô danh |94 / `356b41bd6dd0f04d94a748c4a466222194f882d57cef41e1de7ed41e7bb86c02`| SQL Clean, đúng ScannedSha256,1attempt,1audit completion |
| EICAR chuẩn68byte, không chạy thực thi |68 / `275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f`| Native scan phát hiện `Virus:DOS/EICAR_Test_File`; runtime worker đọc raw sau đó bị AV chặn → fail-closed |
| ZIP nội bộ chứa đúng68byte EICAR |186 / `a2e50ef6ea0a4a8aa06efc14b48924732db47581c08312fed0aadd1a3451dfa9`| Native + adapter/worker SQL Infected, đúng hash trước/sau,1attempt, không auto retry |
| PDF nguồn công khai upload qua form Test |Hash/size trong task`final-http-scan-proof.json` audit Clean, worker log và browser download| OTP→NotScanned423→childworker engine thật→Clean GET/HEAD/Range; sau cùng tamper Test cố ý thu hồi proof, không sửa PDF nguồn |

Mẫu chuẩn theo [EICAR](https://www.eicar.org/download-anti-malware-testfile/). Không mở ZIP/.com qua public upload; fixture EICAR chỉ tạo trong quarantine nội bộ SQL Test. Không dùng malware thật hoặc tắt AV để ép test. Raw native probe có log redacted và hash riêng; SQL audit lưu AssetId/hash/size/state/provider/engine/signature/UTC/outputDigest/error/latency/correlation, không body/OTP/email/tên file người dân.

Máy có MAPSReporting2/SubmitSamplesConsent1, giữ nguyên. `DefenderTest` chỉ Development+LandPortal_Test+storage `.local/tests`+explicit opt-in+SHA allowlist tệp vô danh/public đã xác minh. Không áp dụng provider này cho dữ liệu công dân tùy ý hoặc Dev/Staging/Production. Clamd INSTREAM loopback vẫn mặc định, chưa có daemon thật; không gọi clamd verified nhờ fake protocol. Negative socket/fault injection chỉ tạo ScanFailed, không là bằng chứng Clean/EICAR.

## T01–T10

| Mã | Bằng chứng thực | Kết luận |
|---|---|---|
|T01|`scanner-sql/real-engine.json` PNG Clean; `scan-http/http-report.json` PDF Clean qua workerprocess, SHA tải đúng; cán bộ đúng scope tải, foreign404/list-only403|PASS LOCAL|
|T02|Native raw68byte phát hiện; genuine ZIP adapter+SQL Infected, engine/version/signature/hash; GET/HEAD/Range423, public/static/preview/thumbnail404, UI không có tải|PASS LOCAL|
|T03|Socket offline/refused, timeout1sec, malformed response, oversize>10MiB, hash không allowlist, rawEICAR bị OS chặn: không Clean. Retry test actual TCP negative, production/staging constructor guard|PASS LOCAL|
|T04|Hai SQL claimants chỉ1attempt/audit; hash thay trước scan bị chặn; late result bị token/rowversion fence; HTTP sau scan đổi1byte→409/ScanFailed/clear proof/1audit, lần sau423|PASS LOCAL|
|T05|Fixture SQL lease hết hạn được worker instance mới khôi phục, attempts3 bị chặn không kẹt; retry/backoff hữu hạn, Infected không auto retry. Đây là mô phỏng crash bằng lease bền vững SQL, không kill service hệ thống|PASS LOCAL|
|T06|Full có file giả MIME/signature, oversize/stream limit, filename/path traversal/trùng, endpoint/action CSRF/permission, original source bất biến|PASS LOCAL|
|T07|Guest/khác hồ sơ/list-only/direct ID bị chặn; chuyển người và disable session thu hồi quyền ngay; lookup OTP không original/PII/file/internal assignment; no-store/nosniff/attachment và cùng handle cho GET/HEAD/Range|PASS LOCAL|
|T08|Final full 100/100 sau sửa cuối; scannerHTTP8/8, UI40/40 ở5viewport+6actions/0JS/off-origin; core2C/2A/2B/V04/Finish regression; Release web5GET/worker health thật riêng|PASS LOCAL|
|T09|53copy read-only đúng manifest hash,53Clean/0Infected/0Error/0NotScanned trong report Test;53bản gốc vẫnNotScanned;0khác biệt/17groups Dev, Identity/env nguyên|PASS preservation; chưa Apply nguồn|
|T10|Run-ID mới/raw logs/hash/exit/code freeze/manifest; các attemptfail giữ riêng; không phục hồi rawsmoke2B; không tăng command timeout/stress Dev|PASS evidence mới; V04 historical PARTIAL/SQL UNKNOWN giữ|

Không cộng8 HTTP/40UI/6actions/53copies/health vào100nhóm. Edge headless360/390/768/1024/1440 không phải UAT/iOS/WCAG. T05 kiểm durable recovery và processworker độc lập ở E2E; không diễn tập hạ tầng HA/service restart.

## Kiểm thử và lịch sử chính xác

- Lượt cuối `scan-tests-20261015T030513Z-767f928e`, command `scripts/test-phase2c-scan.ps1 -Region All`, 100/100,0fail/0not-run trong runner, build0warning/error. Audit7projects không advisory từ feed đã dùng; không chứng nhận security production.
- HTTP scanner8checkpoints, UI40pages/6actions; raw ở `scan-http`, sink TCP loopback và childworkers thật; chỉ thư Test. Các UI regression CMS40/typed14/accounts18/site34/scheduler10/core2C40/Finish35 là artifact riêng nằm trong lượt full, không cộng số.
- Full đầu `scan-tests-20261015T025115Z-2d53c048`100/100 trước sửa launcher optional Guid health-only; sau đó CLI đã sửa và chạy health thật, rồi **full cuối trên**. Không cộng hai full thành200.
- Focused `scan-tests-20261015T024836Z-e736b768`3/3 là lần trước, không finalfull. Buildfail `024326Z-37165203` biến local trùng, `024437Z-7c9e45e5`0/2 do rawEICAR bị AV và fixtureclock trướcAvailable; giữ raw. Đổi fixture nội bộ sang ZIP/chỉnhclock Test, không sửa AV policy. HealthCLI đầu nullGuid cũng giữ log lỗi, đã sửa default Empty và kiểm lại actual CLI.
- **97/97 Finish lịch sử trước hai sửa cuối;8/8 Finish sau hai sửa** giữ nguyên giá trị lịch sử, không gọi97 là kết quả scanner mới hoặc regression sau sửa cuối. V04 rawsmoke2B mất vẫn PARTIAL.
- Release publish local web/worker (không deploy) dưới `.local/publish/phase2c/scan-20261015T023338Z-f412ffbd`, package hashes/0privatefiles; runtime code không drift sau publish. `ops-3f760dcf` Testweb5239/5GET200/worker --scan --healthexit0/genuine engine healthy; web owned đã dừng. Health không là proof riêng thay Clean/Infected.

## Bảo toàn và schema

Không migration mới hoặc apply schema/Dev data trong mốc này. Latest SQL migration Test/Dev vẫn **20261014163306_InquiryFinish**. Không cần backup/migrate mới vì không đổi schema hay ghi Dev. Backups Finish lịch sử vẫn giữ, không coi VERIFYONLY là restore drill.

Before/after Dev: **37Content/SourceRecord,47revision,74alias;53asset41ảnh12PDF;70capture/68SQLsnapshot;8ngoại lệ;2video metadata/0video file;0khác biệt/17nhóm**. So sánh cả identity/mapping/publicpayload/sourcehistory/title/time/hash/media/capture/snapshot/exception/legal/draft/config, không chỉ đếm. File nguồn/hash/status/public pointer không đổi; không nhập thêm bài nguồn, không crawl lại. Identity/credentials/stamps/roles/claims/.env nguyên. Dev0submission/outbox/attachment/scan-audit/schedules/heartbeat/comments-enabled;10quyền vận hành cũ và quyền interaction không tự grant; mailDisabled.

`source-copy-run/source-53/report.json` có53per-file AssetId/hash/size/engine/signature/UTC/outputDigest/result, copies read-only giữ trong Testtemp.53Clean chỉ trên bản sao; **53originals NotScanned** và không tự ẩn site. Chưa có quyết định Apply; runbook yêu cầu scope/backup/exactbytes/approval/rollback trước thay lịch sử nguồn.

## Tệp thay đổi và bằng chứng

So với manifest Finish cuối:12tệp sửa,3tệp code/script/test mới,0removed, không sửa migration/model schema:

- `scripts/run-dev.ps1`
- `scripts/run-inquiry-scanner-test.ps1`
- `scripts/run-interactions-test.ps1`
- `scripts/test.ps1`
- `src/Infrastructure/InquiryAttachmentService.cs`
- `src/Infrastructure/InquiryScanWorker.cs`
- `src/Web/Controllers/InquiryManagementController.cs`
- `src/Web/Program.cs`
- `src/Worker/Program.cs`
- `tests/browser/finish-check.mjs`
- `tests/LandPortal.Tests/InquiryFinishHttpTests.cs`
- `tests/LandPortal.Tests/Program.cs`
- `scripts/test-phase2c-scan.ps1` (mới)
- `src/Infrastructure/DefenderInquiryScanner.cs` (mới)
- `tests/LandPortal.Tests/ScannerTests.cs` (mới)

Tài liệu: AGENTS.md, PROJECT_STATE, acceptance-matrix, báo cáo scanner này, crosslink trong phase-2c-finish-report/inquiry-scanner-runbook, scanner-operations, SQL timeout observation và summary hash. Master/SRS/prompts không sửa. Frozen-code và working-state hash có trong finalrun; masterartifact manifest bao phủ task+fullrun và docs liên quan. Raw chứa tài khoản/token fixture Test chỉ ở `.local` ignored; summary không mang secrets/PII, không public raw.

Ảnh thật cuối: [Clean desktop](../.local/evidence/phase2c/scan-tests-20261015T030513Z-767f928e/scan-http/ui/detail-1440.png), [Clean mobile](../.local/evidence/phase2c/scan-tests-20261015T030513Z-767f928e/scan-http/ui/detail-390.png), [Infected desktop](../.local/evidence/phase2c/scan-tests-20261015T030513Z-767f928e/scan-http/ui/quarantine-1440.png), [Infected mobile](../.local/evidence/phase2c/scan-tests-20261015T030513Z-767f928e/scan-http/ui/quarantine-390.png). Đã xem ảnh thật khi bàn giao; không ảnh dựng. Quarantine UI hiện Infected và không link tải; không expose nội dung mẫu.

## Chạy tiếp và checkpoint

Preview Dev thực **http://127.0.0.1:5238/**, `/admin/login`; descriptor task`preview.json` với PID/start-time hiện tại.7GET200 readonly, không nghiệp vụ Dev.5198/5208 không listener lúc đầu; không kill preview/process có sẵn. Khi5238 rảnh:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Phase2CScan -NoBuild -Port 5238
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2c-scan.ps1 -Region All
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-inquiry-scanner-test.ps1 -ScanBuild -Provider DefenderTest -ApprovedAnonymousHashes 356b41bd6dd0f04d94a748c4a466222194f882d57cef41e1de7ed41e7bb86c02 -Health
```

Quét fixture Test cụ thể thêm `-FileId <GUID-Test> -Once`; không đưa arbitraryPII vào allowlist. Manual SQLTestweb5219/sink2525: `scripts/run-interactions-test.ps1 -Scan -Component Web|Sink|Worker` mỗi process một terminal; không coi worker mail là scanner, scanner chạy script riêng trên. Ctrl+C foreground; với preview nền chỉ dừng PID/start-time/assembly khớp descriptor, không dùng PID lịch sử/Stop-Process toàn dotnet. Không tạo scanner daemon/service; E2E/Release ownedworkers đã thoát.

Checkpoint tiếp theo: giữ 2C overall PARTIAL, đối chiếu trọn tiêu chí2C/Finish khi được giao; xin hạ tầng on-prem/quy định cloud cho filePII Production, nghiệm thu scannerproduction và xử lý53nguồn theo quyết định riêng; SLA/lịch/retention chính thức, SMTP/receipt/alert thật, MFA/HTTPS/restore/UAT/quyền vận hành. Production parser/locale/updates cần monitor, không failopen. **SQL timeout MONITORING / ROOT CAUSE UNKNOWN**, Dev7GET hạn chế không chứng minh nguyên nhân đã hết. **V04 artifact PARTIAL** nguyên. Không tự triển khai các mốc tiếp, grantDev/.env/reset/email thật/Git/DNS/service/firewall.
