> **Bổ sung 2C-SCAN 15/10/2026:** scanner Test thật đã nghiệm thu riêng **PASS LOCAL** với Defender/Clean/EICAR/SQL/ACL/worker; xem [báo cáo](phase-2c-scanner-report.md) và [runbook](scanner-operations.md). Finalfull mới100/100 `scan-tests-20261015T030513Z-767f928e` sau sửa cuối, HTTP8/UI40+6actions riêng.53bản sao TestClean nhưng53gốc vẫnNotScanned/0khác biệt17nhóm; không migration mới/không Applynguồn/quyềnDev. **2C-Finish/2C overall vẫn PARTIAL; CHƯA PRODUCTION READY**. Blocker scanner Test trong báo cáo lịch sử dưới không còn hiện hành; ProductionPII engine/policy và các blocker còn lại chưa đóng.97/97trước2Finishfix và8/8sau vẫn là lịch sử đúng thứ tự, không thay số.

---

# Báo cáo 2C-Finish — 15/10/2026

**2C-Finish PARTIAL; CHƯA PRODUCTION READY.** F1 API/private/quarantine/ACL/negative đã chạy thật; **F1 SCAN-INTEGRATION BLOCKED** do không có engine Test. F2/F3/F4 đạt PASS LOCAL cho các ca đã nêu, với mail sink/SQL Test; không nâng cả 2C PASS nhờ scanner giả. Giữ ASP.NET Core10/MVC-Razor/SQL Server, 2A/2B/V04/bốn luồng2C/14 chuyên mục. Không làm2D/2E/2F/Phase3.

## Delta đã chạy

| Mục | Hành vi / bằng chứng | Kết quả |
|---|---|---|
|F1|Một PDF/JPEG/PNG10 MiB stream private, signature/MIME/name/size; SQL session→challenge→OTP→submission; không là MediaAsset public. NotScanned/ScanFailed guest/public/Range không tải. SQL scan claim/lease/fence/retry, clamd thật adapter/health; GC chưa OTP/orphan giới hạn.|API/ACL/negative PASS; Clean/Infected thật **BLOCKED/NOT RUN**|
|F2|Phân công/chuyển trả lý do/actor/time/history/rowversion/idempotence; cán bộ cũ mất quyền đọc/Excel/thư chờ. Quyền list metadata riêng inquiry.list, PII inquiry.read; không có Read thì detail/file/Excel/respond403 dù có action permissions. Lookup mã+email+OTP INQUIRY_LOOKUP, 5 phút/5 sai/resend/throttle/15 phút session; unknown trung lập, race consume một lần, không trả original/PII/assignment/file.|PASS LOCAL ca Test đã chạy|
|F3|Lịch immutable phiên bản/hiệu lực/cuối tuần/cutoff/work-off, snapshot policy+due; mặc định 10 ngày chỉ tạm tính. Gia hạn reason/actor/audit, UPDLOCK/rowversion chống hai lượt ghi đè. XLSX string cells/no-store/current scope, 6 cột mặc định và5 PII theo quyền riêng. Batch≤20, fresh rights/status/rowversion, transaction mỗi item và kết quả riêng/idempotence.|PASS LOCAL chính sách tạm, không SLA pháp lý|
|F4|Lost ACK sau DATA thật→Uncertain, không auto retry; crash/expired lease không resend. UI quyền outbox.reconcile/current scope; receipt .eml kiểm Message-ID/To/body/hash, unknown giữ, not-sent có căn cứ, resend-risk cần xác nhận; event mới/audit/idempotence, không rollback hồ sơ.|PASS LOCAL sink Test; SMTP Production chưa triển khai|

F3 readback **openpyxl3.1.5**: 60 hàng Test,11/6 cột, 671 ô text;0 formula.6 boundary cells cho =,+,-,@,tab và tiếng Việt/ký tự ngoài BMP đọc lại đúng. Số hàng là fixture Test tích lũy, không thêm nội dung nguồn.

## Kiểm thử và thứ tự thật

- Full executable runner mới **97/97**, exit0, `finish-tests-20261014T171757Z-de2f722d`, 00:17:58 15/10/2026 VN–00:22:41 15/10/2026 VN. Có regression2A/2B/V04/core2C và Finish.89/89 là lịch sử, không sao chép/cộng;97 là nhóm runner, không phải tổng yêu cầu/an ninh production.
- Sau full trên còn sửa `InquiryCalendarService` (khóa update gia hạn) và `InquiryFinishTests` (race/fixture clock). **Không chạy lại toàn runner sau hai thay đổi đó.** Đã chạy vùng thay đổi `-Region Finish` **8/8**, exit0, `finish-tests-20261014T172716Z-9eab500d`, 00:27:16 15/10/2026 VN–00:28:05 15/10/2026 VN:7 nhóm SQL +1 HTTP/UI wrapper. Code freeze242 đường dẫn,0 drift hiện tại; full↔final chỉ2 đường dẫn này, chứng cứ JSON. Không cộng thành105/105 hoặc gọi full97 sau sửa cuối.
- Build solution web/worker sau sửa cuối:0 warning/0 error. NuGet audit7 project không advisory được báo từ các feed đã dùng; không chứng nhận security production.
- HTTP Finish cuối **6 checkpoints** riêng: multipart/OTP/private, list-vs-PII, transfer/answer, lookup, SLA/Excel, real lost-ACK/reconcile. Actual child worker mail/scan, loopback sink TCP, SQL Test, không SMTP mock. Worker scan chạy thật nhưng engine thiếu, exit0 không là Clean.
- UI Finish **35/35 trang/kích thước** (7 route ×360/390/768/1024/1440),5 thao tác thực (keyboard/file types/calendar save/unknown reconcile/batch/Excel nằm trong5 records),0 JS error/0 off-origin/không overflow/label thiếu. Root đã xem desktop/mobile; đây là Edge headless viewport, không WCAG/iOS acceptance.
- Trong full runner: CMS40/typed14/accounts18/site34/scheduler10 và core2C UI40/HTTP riêng; không cộng các count này vào97. Raw có directory của run tương ứng.
- Release verified web/worker mới ở `.local/publish/phase2c/finish-20261014T160724Z-22657579-verified`; manifest package riêng,0 private files. Actual Release web7 GET200 trên SQL Test5222; worker --once --job ID Test không tồn tại, exit0/0publication effect, không deploy. Đây là boot smoke, không thay E2E worker.
- Dev chỉ8 GET tuần tự200 (home/category/source article/documents/contact/inquiry/lookup/login), không test nghiệp vụ, không stress. Preview Finish5208 đã mở lại với binary cuối, PID81868; V04 preexisting5198 giữ nguyên. Mail Dev Disabled.

Các lần lỗi giữ riêng: SQL lookup JSON_VALUE gặp audit cũ không JSON (đã guard ISJSON); UI assertion nhầm MIME accept thành extension (sửa test); test restart đọc entity tracked cũ thay vì SQL mới (sửa readback); build khóa bởi owned preview (dừng đúng process); fake-clock calendar lặp timestamp trong Test cũ chọn phiên bản khác (fixture tăng clock/tick, kiểm đúng calendar ID). Không reset Test/Dev, không xóa artifact lỗi, không tái tạo raw giả. Lượt `finish-tests-20261014T172716Z-9eab500d` là validation sau sửa cuối.

Hai probe SQL đầu `finish-sql-20261014T164031Z-74994b73` và `finish-sql-20261014T164154Z-ef9503f4` chỉ có JSON/các artifact nghiệp vụ, chưa có wrapper run.log/frozen-code/exit manifest đầy đủ: giữ **ARTIFACT PARTIAL**, không tái tạo log hay tính chúng là hồ sơ COMPLETE. Các run `finish-tests-*` sau đó có wrapper riêng; kết quả bàn giao dựa trên run full/focused cuối, không dựa hai probe thiếu manifest.

## Migration và bảo toàn

Migration **20261014163306_InquiryFinish** additive:8 bảng InquiryAttachments/Assignments/Calendars/CalendarExceptions/Extensions/LookupChallenges/LookupSessions/OutboxReconciliations;5 cột inquiry trên InteractionSubmissions và2 cột outbox. Index/FK/rowversion/unique một attachment mỗi hồ sơ, unique lookup consumption/token. Không DDL các bảng nội dung nguồn. EF history protection + runtime SQL DENY UPDATE/DELETE lịch/histories, không cấp quyền Identity Dev.

Test trước: backup273 file, native COPY_ONLY/CHECKSUM/VERIFYONLY; target LandPortal_Test/kho Test đúng, manifest nguồn trước/sau migration0 sai khác17. Dev sau review/Test/backup130 file đã rehash, verify đúng local target+Identity/env; migration và rerun idempotent/search backfill0. VERIFYONLY **không phải** diễn tập restore.

Sau migration và readonly smoke: **37 Content/SourceRecords;47 revision;74 alias;53 media41 ảnh12PDF;70capture/68SQL snapshot;8 ngoại lệ;2video metadata/0 tệp video;0 mismatch/17 nhóm**.53 media NotScanned nguyên. Dev0 submissions/challenges/outbox/snapshots/commands,0 các8 bảng mới,0comments enabled/job/heartbeat. Identity/credentials/stamps/roles/claims/.env nguyên, không tự grant10 quyền cũ hoặc quyền mới.

## Tệp thay đổi

Chính: `InquiryFinishEntities`, `InquiryFinishModel`, attachment/scan/assignment/lookup/calendar/Excel/batch/reconcile services; interaction service/outbox/contracts/permission catalog; Web controller/model/view/JS/CSS/DI; Worker --scan; migration/designer/model snapshot. Scripts Finish runner/launchers/scanner Test, real SQL/HTTP/browser/openpyxl tests. Không khởi tạo lại architecture. Danh sách24 tệp sửa +30 tệp mới so với code manifest core2C:

- `scripts/run-dev.ps1`
- `scripts/run-interactions-test.ps1`
- `scripts/test.ps1`
- `src/Application/TextNormalizer.cs`
- `src/Domain/InteractionEntities.cs`
- `src/Infrastructure/AccountAdministrationService.cs`
- `src/Infrastructure/InteractionContracts.cs`
- `src/Infrastructure/InteractionOutboxService.cs`
- `src/Infrastructure/InteractionService.cs`
- `src/Infrastructure/MediaService.cs`
- `src/Infrastructure/Migrations/PortalDbContextModelSnapshot.cs`
- `src/Infrastructure/PortalDbContext.cs`
- `src/Infrastructure/RuntimeRoleInterceptor.cs`
- `src/Web/Controllers/InteractionAdminController.cs`
- `src/Web/Controllers/InteractionsController.cs`
- `src/Web/Models/InteractionModels.cs`
- `src/Web/Program.cs`
- `src/Web/Views/InteractionAdmin/Detail.cshtml`
- `src/Web/Views/InteractionAdmin/Index.cshtml`
- `src/Web/Views/Interactions/Form.cshtml`
- `src/Web/Views/Shared/_AdminLayout.cshtml`
- `src/Web/wwwroot/css/interactions.css`
- `src/Worker/Program.cs`
- `tests/LandPortal.Tests/Program.cs`
- `scripts/run-inquiry-scanner-test.ps1`
- `scripts/test-phase2c-finish.ps1`
- `src/Domain/InquiryFinishEntities.cs`
- `src/Infrastructure/InquiryAssignmentService.cs`
- `src/Infrastructure/InquiryAttachmentService.cs`
- `src/Infrastructure/InquiryCalendarService.cs`
- `src/Infrastructure/InquiryExcelService.cs`
- `src/Infrastructure/InquiryFinishModel.cs`
- `src/Infrastructure/InquiryLookupService.cs`
- `src/Infrastructure/InquiryScanWorker.cs`
- `src/Infrastructure/InteractionBatchService.cs`
- `src/Infrastructure/Migrations/20261014163306_InquiryFinish.cs`
- `src/Infrastructure/Migrations/20261014163306_InquiryFinish.Designer.cs`
- `src/Infrastructure/OutboxReconciliationService.cs`
- `src/Web/Controllers/InquiryLookupController.cs`
- `src/Web/Controllers/InquiryManagementController.cs`
- `src/Web/Controllers/OutboxAdminController.cs`
- `src/Web/Models/InquiryFinishModels.cs`
- `src/Web/Services/InteractionBrowserSession.cs`
- `src/Web/Views/InquiryLookup/Index.cshtml`
- `src/Web/Views/InquiryLookup/Result.cshtml`
- `src/Web/Views/InquiryManagement/Calendar.cshtml`
- `src/Web/Views/InteractionAdmin/_InquiryOperations.cshtml`
- `src/Web/Views/OutboxAdmin/Detail.cshtml`
- `src/Web/Views/OutboxAdmin/Index.cshtml`
- `src/Web/wwwroot/js/interaction-batch.js`
- `tests/browser/finish-check.mjs`
- `tests/LandPortal.Tests/InquiryFinishHttpTests.cs`
- `tests/LandPortal.Tests/InquiryFinishTests.cs`
- `tests/verify-inquiry-excel.py`

AGENTS/state/acceptance/report/admin/outbox/scanner/GAP cập nhật riêng; prompt/SRS người dùng không sửa. Tracked summary: [summary-handoff](evidence/phase2c/finish-20261014T160724Z-22657579/summary-handoff.json). Raw private `.local/evidence/phase2c/finish-20261014T160724Z-22657579` và từng test run; manifest/hash/exit/COMPLETE, không có .env/rawmail/keys trong package/git.

## Chạy và bằng chứng

Preview public **http://127.0.0.1:5208/**, form `/tuong-tac/Inquiry`, lookup `/tra-cuu-ho-so`, admin `/admin/login`, inbox `/admin/tuong-tac?kind=Inquiry`, outbox `/admin/outbox`; Dev chưa quyền/mail nên không demo gửi/duyệt hoặc bypass. Test tự sở hữu web5221/sink random/child worker và dừng sau E2E. Test thủ công web5219/sink2525; giữ5219 khác V045198.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -Phase2CFinish -NoBuild -Port 5208
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2c-finish.ps1 -Region Finish
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase2c-finish.ps1 -Region All
# Ba terminal Test riêng; sau build, Web/Worker thêm -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Finish -Component Sink
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Finish -Component Web -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-interactions-test.ps1 -Finish -Component Worker -NoBuild
```

Trước build vào finish-current, dừng đúng owned preview theo descriptor/start time; không kill toàn dotnet hoặc V04. Root private `.local/finish-stop-owned.ps1` trỏ descriptor preview đầu đã dừng, **không dùng để dừng preview mới**; descriptor mới `preview-finish-verified.json`. Runbooks [admin](interaction-admin-guide.md), [outbox](interaction-outbox-runbook.md), [scanner](inquiry-scanner-runbook.md). Không đưa credential/OTP/receipt token ra report.

Ảnh thật cuối (private Test, tên/email Test .invalid): [hỏi đáp390](../.local/evidence/phase2c/finish-tests-20261014T172716Z-9eab500d/finish-http/ui/inquiry-390.png) · đường dẫn đúng dưới `finish-http/ui`: `inquiry-390.png`, `detail-1440.png`, `detail-390.png`, `lookup-1440.png`, `uncertain-390.png`, `batch-result-1024.png`. Xem [UI review](ui-review-2c-finish.md) có links đúng và manifest hash. Log/run/json tại `.local/evidence/phase2c/finish-tests-20261014T172716Z-9eab500d`; SQL/scanner/Excel ở finish-*; mail-worker/scan-worker raw trong finish-http. Artifact V04 rawsmoke2B mất vẫn PARTIAL, không fake phục hồi.

## Blocker và tiếp tục

F1 SCAN-INTEGRATION BLOCKED: không có clamd/clamscan/clamdscan PATH/listener3310; chưa chạy sạch/infected/range-after-clean qua engine thật. Khi engine Test được chuẩn bị, chạy health + benign/EICAR được phép, SQL scan lease/retry/tamper/ACL/download và lưu artifact mới. Không cài service, không đổi53 nguồn thànhClean. Có adapter genuine, không mock trảClean.

F2/F3/F4 chức năng Test đã chứng minh; giới hạn cần giữ: roster100 chưa paging/search; Excel dùng state/field, không dùng due-filter của inbox; chưa UI reset scan attempts sau3 failures; chưa automated Uncertain escalation/provider receipt Production; chưa benchmark timing/abuse phân tán. SLA vẫn tạm tính, cần đơn vị duyệt chính sách/lịch/cutoff/gia hạn/retention.

SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**:3 query Test serial category14,5–70,5ms; bounded page26,5–98,1ms;3home640–961ms/24block/0error. Không tái hiện không chứng minh rootcause hết; timeout không tăng, không khảo sát DB khác/stressDev. Telemetry trong task/sql-probe/v04, risk doc giữ incident.

SMTP Production/scanner Production và quét nguồn/MFA/HTTPS/restore drill/UAT/pháp lý/quyền vận hành/alerts chưa nghiệm thu.10 quyền Dev cũ và quyền interaction mới chưa apply; không reset mật khẩu/.env/crawl/commit/push/deploy/DNS/service/emailthật.2Dvideo tải trực tiếp máy/2E/2F/Phase3/Phase4 ngoài lượt này. Không nhập lại từng tin và không gọi mẫu37 là toàn dữ liệu.
