# V05-Finish — nghiệm thu kỹ thuật local, 15/10/2026

**PASS LOCAL đối với các vùng A/B/C/D có bằng chứng dưới đây; nghiệm thu tổng thể PARTIAL. CHƯA PRODUCTION READY.** AC16.1 và các gate PO/Production không được đổi thành PASS. Giữ ASP.NET Core10/MVC-Razor/SQLServer và các workflow2A/2B/2C/2D hiện hữu.

## Kết quả sau sửa cuối

Full `v05-finish-tests-20261015T134049Z-dc3d22a1` **123/123 PASS,0FAIL,exit0**, 2026-10-15T13:40:58.1496362+00:00–2026-10-15T13:50:24.6112382+00:00. Build Web/Worker0warning/error; audit7projects không báo advisory tại lần chạy này. 325 codepaths/0drift, 9104 protectedartifacts/0drift; raw/result/build/audit nằm tại [`run.json`](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/run.json) và [`run.log`](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/run.log). Không cộng HTTP/UI/worker checkpoints vào123; không lấy117/117 cũ hoặc123 trước sửa làm kết quả hiện hành.

Application full `130537Z-af612cb0` cũng123/123. Sau đó sửa default Guid của helper worker health và các helper kiểm thử được mô tả bên dưới; full hiện hành chạy lại **sau tất cả sửa cuối**. Health lần đầu FAIL do thiếu AssetId mặc định được giữ nguyên; retry helper exit0/Ready:true bằng engine thật, không quét thêm nguồn.

Full `131749Z-b180fbae` sau health-helperfix **122/123,1FAIL** ở video crash test: không quan sát được lease trong cửa sổ chờ2giây, workerstdout chưa được lưu khi Assert sớm; không đủ raw để kết luận nguyên nhân worker. Đã sửa **test** chờ startup có deadline30giây (không tăng SQLtimeout), luôn lưu stdout/stderr và thu dọn đúng child process kể cả lỗi. Không thấy orphanworker còn chạy lúc kiểm tra; không dừng tiến trình khác. Run FAIL được giữ nguyên; focusedVideo 132816Z-42961a77 sau fix đạt8/8/exit0, processorReady=true và7HTTPcheckpoint; full sau đó có runID riêng. Code ứng dụng không đổi sau `130537Z-af612cb0`.

Full `133046Z-d7c5df40` còn lỗi helper scheduler khi `.fill()` một giá trị datetime-local kết thúc bằng `:00`: Edge chuẩn hóa bỏ giây0, Playwright so chuỗi rồi báoMalformed. Đã tái hiện độc lập với browser thật ở00/01/09/59giây, lưu task `datetime-browser-proof.json`; chuẩn hóa chuỗi test bỏ phần giây khi0, không sửa scheduler ứng dụng. Không tăng timeout hoặc đánh PASS lượt lỗi; full hiện hành ở trên chạy sau sửa này.

## Phạm vi đã hoàn thiện

A: menu mẫu Công khai thông tin gồm overview và3typedgroup, cấu hình draft/live theo quyền, giữ14chuyên mục nguồn; group2 luôn lọc khỏi public khi chưa duyệt. SQL Test đã publish template và kiểm desktop/mobile; **chưa Apply Dev**, không tự grant. Search chung nối bài và publicQA snapshot có consent; SQL count/page trước đọc LOB, dấu/không dấu vàđ/d, kind/category/date/sort; loại private/pending/rejected/stale/contact. Không đưa email/phone/attachment gốc vào search.

B: CAPTCHA SQL trên cả login và reauthentication, purpose/session/IP/TTL/one-use; kiểm wrong/expired/reuse/missing/directCSRF; editor hết phiên giữ bản soạn và nhập CAPTCHA thật. Identity/password/lockout/rate/CSRF vẫn kiểm riêng, không thay MFA.

C: PDF/DOCX/XLSX1file10MiB private/quarantine, bảng EditorialDocuments lưu hash/engine/time/lease/retry/fence/rowversion; worker độc lập, fail-closed. Native **Microsoft Defender1.1.26090.9/signature1.461.32.0** thực quét DOCX/XLSX/PDF→Clean, EICAR DOCX→Infected, offline→ScanFailed, đổi byte→DENIED. [Native proof](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-documents/native-scan.json). Không Clean mock. Chỉ Development+SQLTest+Teststorage và SHA anonymous/public allowlist; chưa cho Production/PII. XLSX thuần giá trị; từ chối macro/formula/external/zipbomb/path; DOC/XLS phải chuyển định dạng, không giả hỗ trợ. Download/preview freshACL+currentrevision+proof+exactbyte,private/no-store/Range; disableuser/revoke/reference removal đều chặn.

Hardening PDF Uploaded cũ: không dùng exceptionDevelopment để publish/download NotScanned. Admin có quyền chuyển từng PDF hợp lệ đã liên kết vào queue thật, giữ bytes/path/revision; [real enqueue/scan proof](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/phase2a/legacy-pdf-native.json). Sharing PDF/Excel sạch vẫn theo quyền2A; Word phải cùng nội dung. Không enlist hoặc thay trạng thái53source. LegacyPDF chia sẻ nhiều hồ sơ/chưa rõ quyền hoặc>10MiB cần đối soát riêng, không tự reset/đánhClean.

D: Word upload→scanner→bounded parser bảng7/8cột,verticalmerge/ambiguoushorizontalwarnings/ngàyISO/buổi/giờ/nội dung/chủtrì/thànhphần/địađiểm→preview chỉnh→nháp→approvalđúngrevision→publishv1/v2→restorev1thànhv3. OriginalSHA và bằng chứng rà soát theo revision, GETpreview không tựsave; lỗi nêu bảng/dòng, sửa tay được. Giữ live trước publish, historyprivate/currentoriginalpublicACL, Range đúng bytes. Recovery kiểm SQL lease hết hạn rồi worker độc lập xử lý thật; **không tuyên bố đã kill worker tài liệu giữa scan**. [HTTP](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-http/http-report.json), [fences](../.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1/v05-finish-fences/report.json), [UI và ảnh](ui-review-v05-finish.md).

## Dữ liệu, migration và backup

Migration **20261015091551_V05EditorialDocuments**,4nullablemetadata columns +newsidecartable/index/FKNoAction/rowversion, SQL chỉadditive/backfill0. Test trước, Dev sau xác minh target `LandPortal_Dev`, backup vàapplication full123 run130537, trước các sửa helper kiểm thử. Final full134049 chạy sau đó trên code/scripts cuối. ScriptSQL reviewed lưu task; không startupmigration.

COPY_ONLY/CHECKSUM/VERIFYONLY Test2493file/Dev131file; rehash tất cảbackupfiles0mismatch. Backup Test đầu dùng nhầm profile kho mặc định131file được giữ và **không tính** là backupTeststorage; đã thực hiện đúng profile trước migrate. **Chưa RESTORE**, VERIFYONLY không thay restore drill.

Sau Dev migration và7GETsmoke: **0 sai khác/17 nhóm**;37content/37sourceRecords/47revision/74alias;53sourceassets(41ảnh12PDF) toàn bộNotScanned;70capture tương ứng68SQLsnapshot;8ngoại lệ nguồn;2video metadata/0videofile. Nguồn14chuyên mục nguyên; counts bảngcategory tổng18 không bị ép thành14. Identity/password/stamps/roles/claims/.env nguyên. Dev0EditorialDocuments/0Interactions/0VideoFile; không demo hay sửa bài nguồn. MenuLive/Draft pointer nguyên. Login GET tạo CAPTCHA nonce kỹ thuật, không coi là nghiệp vụ mới. [Preservation](../.local/evidence/phase2d/v05-finish-20261015T090456Z-b90982c8/source-after-final.json), [backupreadback](../.local/evidence/phase2d/v05-finish-20261015T090456Z-b90982c8/backup-readback.json).

## PASS / FAIL / BLOCKED / NOT RUN

| Vùng | Kết quả cuối | Giới hạn |
|---|---|---|
| Full runner trên finalcode/scripts |123PASS /0FAIL|123nhóm, không cộng lượt |
| Finish HTTP |4checkpoint PASS|SQL/storageTest thực; nested trong1runnergroup |
| Finish UI |10pagechecks/3actions PASS|Edge1440/390; không chứng nhận physicalmobile/WCAG/UAT |
| Worker/native |Clean3định dạng/InfectedEICAR/ScanFailed/tamper/lease/race PASS|Test thật, không phải ProductionPIIpolicy |
| Dev smoke |7GET200 PASS|Không POSTnghiệpvụ; không cộng123 |
| AC16.1 |BLOCKED:NO_REAL_WORD_SAMPLES|Fixture technical không thay3Word nghiệp vụ khác tuần |
| Group2 name / privateLookup / publicweeklyhistory |NEEDS_PO / LOCKED|Không tự công bố hoặc mở chi tiết hồ sơ |
| Menu Apply Dev / opsgrants |NOT RUN|10quyền vận hành +4catalogv05 chưa tự cấp |
| Production scanner/MFA/SMTP/restore/UAT/TLS/cutover/fullmigration |BLOCKED / NOT RUN theo gate|Không thay bằng localPASS |
| SQLcategorytimeout |MONITORING / ROOT CAUSE UNKNOWN|Quan sátTestRESOURCE_SEMAPHORE chưa đủ rootcause; không tăngtimeout |
| Raw V04/D15/diagnosticv05 |PARTIAL historical|Không phục dựng giả; rawFinish unique/hash riêng |

## Vận hành và checkpoint tiếp theo

Preview thật **http://127.0.0.1:5288/** (`/admin/login`, `/lich-tuan`, `/cong-khai-thong-tin`, `/tim-kiem`, `/van-ban`). Chạy thêm web dùng port khác nếu5288đang nghe:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -V05Finish -NoBuild -Port 5289
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05-finish.ps1 -Region All
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-v05-finish.ps1 -Region V05
```

Worker/manualTest lệnh và yêu cầu SHAallowlist: [runbook](editorial-documents-runbook.md); editor: [hướng dẫn](v05-finish-admin-guide.md). Buildisolated `.local/build/phase2d/v05-finish-current`; previewcopiedassembly riêng. Portcũ5238/5258/5268 không listener lúc kiểm tra, không xóa artifact/build hoặc dừng tiến trình khác. Không.env/grantsDev/emailthật/Git/deploy/DNS/service/crawl.

Tiếp tục từ checkpoint này: nhận3Word thật+POtemplate để parserUAT/AC16.1; PO/phápchế tênnhóm2; PO/ATTTlookup/history; operator có thẩm quyền cấp quyền/menuDev/scannerProduction. Không dựng lại2D/Finish. Raw task`.local/evidence/phase2d/v05-finish-20261015T090456Z-b90982c8`; raw full`.local/evidence/phase2d/v05-finish-tests-20261015T134049Z-dc3d22a1`; manifest-final.json/readback riêng. Summary an toàn tại `docs/evidence/v05-finish/v05-finish-20261015T090456Z-b90982c8/summary.json`; rawlocal có thể chứafixture/accountTest nên không public nguyênthưmục.

## Tệp mã thay đổi so baseline117

18 added, 48 changed, 0 deleted; exacthash trong taskchanged-code.json. Không coi GitmixedWIP là các thay đổi đều của lượt này.

- `scripts/run-documents-test.ps1`
- `scripts/test-v05-finish.ps1`
- `src/Domain/EditorialDocument.cs`
- `src/Infrastructure/EditorialDocumentModel.cs`
- `src/Infrastructure/EditorialDocumentService.cs`
- `src/Infrastructure/Migrations/20261015091551_V05EditorialDocuments.cs`
- `src/Infrastructure/Migrations/20261015091551_V05EditorialDocuments.Designer.cs`
- `src/Infrastructure/OfficeDocumentPolicy.cs`
- `src/Web/Controllers/EditorialDocumentsController.cs`
- `src/Web/Services/GeneralSearchQuery.cs`
- `src/Web/Views/EditorialDocuments/Index.cshtml`
- `src/Web/Views/Portal/PublicAnswer.cshtml`
- `src/Web/wwwroot/css/public-reflow.css`
- `src/Web/wwwroot/css/search-results.css`
- `tests/browser/login-captcha.mjs`
- `tests/browser/v05-finish-check.mjs`
- `tests/LandPortal.Tests/V05FinishHttpTests.cs`
- `tests/LandPortal.Tests/V05FinishTests.cs`
- `scripts/run-dev.ps1`
- `src/Domain/ContentMetadata.cs`
- `src/Infrastructure/ContentMetadataModel.cs`
- `src/Infrastructure/MediaService.cs`
- `src/Infrastructure/Migrations/PortalDbContextModelSnapshot.cs`
- `src/Infrastructure/PortalDbContext.cs`
- `src/Infrastructure/SiteConfigurationService.cs`
- `src/Infrastructure/V05ContentPolicy.cs`
- `src/Infrastructure/WeeklyDocxReader.cs`
- `src/Infrastructure/WorkflowService.cs`
- `src/Web/Client/editor.ts`
- `src/Web/Controllers/AdminController.cs`
- `src/Web/Controllers/AdminMediaController.cs`
- `src/Web/Controllers/AdminSessionController.cs`
- `src/Web/Controllers/MediaController.cs`
- `src/Web/Controllers/PortalController.cs`
- `src/Web/Controllers/SiteAdminController.cs`
- `src/Web/Models/PortalModels.cs`
- `src/Web/Program.cs`
- `src/Web/Services/PublicSiteService.cs`
- `src/Web/Views/Admin/_V05Metadata.cshtml`
- `src/Web/Views/Admin/Edit.cshtml`
- `src/Web/Views/Admin/Login.cshtml`
- `src/Web/Views/Portal/Search.cshtml`
- `src/Web/Views/Shared/_Layout.cshtml`
- `src/Web/Views/SiteAdmin/Index.cshtml`
- `src/Web/Views/WeeklySchedule/Index.cshtml`
- `src/Web/wwwroot/js/editor.js`
- `src/Worker/Program.cs`
- `tests/browser/accounts-check.mjs`
- `tests/browser/cms-check.mjs`
- `tests/browser/content-check.mjs`
- `tests/browser/finish-check.mjs`
- `tests/browser/interactions-check.mjs`
- `tests/browser/scheduler-check.mjs`
- `tests/browser/site-check.mjs`
- `tests/browser/ui-check.mjs`
- `tests/browser/v05-check.mjs`
- `tests/browser/video-check.mjs`
- `tests/LandPortal.Tests/CaptchaHttp.cs`
- `tests/LandPortal.Tests/Phase2aHttpTests.cs`
- `tests/LandPortal.Tests/Phase2bAccountHttpTests.cs`
- `tests/LandPortal.Tests/Phase2bSchedulerHttpTests.cs`
- `tests/LandPortal.Tests/Phase2bSiteHttpTests.cs`
- `tests/LandPortal.Tests/V04HttpTests.cs`
- `tests/LandPortal.Tests/V05Tests.cs`
- `tests/LandPortal.Tests/V05WordTests.cs`
- `tests/LandPortal.Tests/VideoHttpTests.cs`

---

## Lịch sử checkpoint và lỗi (giữ nguyên raw; thông tin đang chạy phía dưới đã được thay bằng kết quả cuối ở trên)

# V05-Finish — checkpoint đang triển khai, 15/10/2026

Full `133046Z-d7c5df40` kết thúc122/123, chỉ FAIL helper scheduler `datetime-local :00`; tái hiện riêng trên Edge thật (taskdatetime-browser-proof.json). Video đã PASS sau sửa lease-observation. Đã chuẩn hóa chuỗi thời gian trong helper, không sửa scheduler ứng dụng. **Run full sau sửa cuối đang chạy `v05-finish-tests-20261015T134049Z-dc3d22a1`**; chưa dùng lượt trước để báo PASS code cuối.

Checkpoint mới nhất lúc hoàn tất hồ sơ: application full `v05-finish-tests-20261015T130537Z-af612cb0` đạt123/123. Dev đã migrate `20261015091551_V05EditorialDocuments` sau backup/verifytarget/full; đối chiếu sau smoke0khác biệt17nhóm,Identity/.env nguyên,53sourceNotScanned. Full sau sửa helper health `v05-finish-tests-20261015T131749Z-b180fbae` đạt122/123,1FAIL testvideo không quan sát lease trong2giây, raw giữ nguyên; chưa có stdout của worker ở nhánh Assert sớm, không suy đoán rootcause. Đã sửa test chờ startup có deadline30giây và lưu/cleanup ownedworker bằng finally, không tăngSQLtimeout; focusedVideo `v05-finish-tests-20261015T132816Z-42961a77` đang chạy. Thông tin Dev chưa migrate ở phần lịch sử dưới đây đã cũ; kết quả toàn bộ vẫn PARTIAL cho đến full sau sửa cuối.

**PARTIAL — chưa nghiệm thu code cuối, CHƯA PRODUCTION READY.** Tiếp từ migration `20261015075352_SrsV05PublicFormsAndContent`; không dựng lại dự án hoặc 2D. Báo cáo này sẽ được cập nhật bằng run cuối thực tế.

Task riêng `.local/evidence/phase2d/v05-finish-20261015T090456Z-b90982c8`. Đọc SRS DOCX v0.5 đầy đủ, hash `3eefd2a434471997dc179ace32909bc810c1cc787da51c0ceedf9a70c676180b`. Đối chiếu run117 cũ: 307 tệp mã, 0 khác biệt trước sửa; không dùng117 làm kết quả V05-Finish.

Baseline Dev đọc trực tiếp: 37 nội dung, 47 revision, 74 alias, 53 tài nguyên (41 ảnh/12 PDF), 70 capture/68 SQL snapshot, 8 ngoại lệ, 2 video metadata/0 tệp. Media/snapshot/hash khớp; đã lưu digest Identity và `.env` trước sửa. Chưa ghi dữ liệu nghiệp vụ mới lên Dev.

## Delta đã viết, đang kiểm thử

- Menu: typed targets và thao tác thêm mẫu FR17 vào cấu hình nháp, giữ nhóm thứ hai chờ PO/pháp chế; bộ dựng menu luôn lọc tên chưa duyệt. Không đổi14 chuyên mục nguồn, không tự áp live Dev.
- Search: SQL UNION ALL trên khóa công khai, phân trang trước khi đọc tóm tắt, chỉ snapshot hỏi đáp đang được duyệt và có đồng ý công khai; route chi tiết công khai riêng. Giữ khóa lookup cá nhân.
- Login: CAPTCHA SQL một lần/purpose/session/IP/TTL trước Identity, chống CSRF và rate limit hiện có; không thay MFA.
- Tài liệu: bảng bằng chứng SQL riêng, upload PDF/DOCX/XLSX10MiB vào kho cách ly, worker độc lập thật, lease/retry/fence, kiểm định định dạng sau quét. DOCX chỉ dùng lịch tuần; XLSX giá trị thuần, không macro/formula/external links. Nguồn53 không tham gia.
- Word: import Clean đúng byte vào preview của editor có sẵn, sửa dòng, lưu revision, duyệt/đăng; tham chiếu Word gốc và xác nhận rà soát metadata/comments theo revision. Khôi phục vẫn dùng workflow2A để tạo revision/release mới.

Migration mới **`20261015091551_V05EditorialDocuments`**:4 cột nullable ở metadata draft/revision và1 bảng mới với FK NoAction/index/rowversion. SQL Up chỉ ALTER ADD/CREATE/ghi migration history; không UPDATE/DELETE nguồn. Đã apply Test, backfill0; Dev chưa apply ở checkpoint này. Backup Test đầu có SQL Test nhưng dùng profile kho mặc định (131 tệp), được giữ và không tính là backup kho Test. Đã làm lại bằng Test profile riêng: **2493 tệp**, COPY_ONLY/CHECKSUM/VERIFYONLY. VERIFYONLY chưa phải restore drill.

## Kiểm thử đang tích lũy, không cộng lượt

- `v05-finish-tests-20261015T092229Z-182494ee`: build FAIL do kiểu collection trong test mới; chưa chạy nghiệp vụ.
- `v05-finish-tests-20261015T092315Z-4c77c520`:9/13,4FAIL. Menu cha sai kiểu, helper CSRF, truy vấn search timeout, browser login timeout. Native DOCX/XLSX/PDF Clean và EICAR Infected thực đạt. Raw giữ nguyên.
- `v05-finish-tests-20261015T092748Z-63a3bef0`:11/13,2FAIL. Search tiếng Việt chưa khớp theo collation; Wordv2 bị implicit validation của caption rỗng. Menu, native và V05 HTTP cũ đạt. Đã sửa và đang chạy lại, chưa gọi full regression.
- `v05-finish-tests-20261015T093113Z-e927460a`:12/13, còn reflow search mobile; HTTP Wordv1/v2/restorev3 đã qua, UI dừng đúng lỗi tràn ngang.
- `v05-finish-tests-20261015T122542Z-bcf299b6`:14/15, thêm FR17 native file publish và scanner fence tests đều đạt; browser phải mở menu mobile trước khi bấm nhóm.
- `v05-finish-tests-20261015T123148Z-01e02e5d`:15/15 focused, có1 compiler warning action name che inherited method. Sau đó đổi tên action và sửa thông báo validation login; không lấy15/15 thay full sau sửa.
- Full `v05-finish-tests-20261015T123349Z-ded4ee1c`:**121/123,2FAIL UI**, raw/exit giữ riêng. Endpoint reauth V04 đã đạt với CAPTCHA thật; kịch bản CMS browser cũ chưa nhập CAPTCHA mới nên không POST. Site UI phát hiện tiêu đề dài chứa identifier tràn ngang ở390. Toàn bộ6 nhóm Finish mới, các luồng2C, scanner và video native/E2E đạt trong run này; không gọi full PASS. Đã sửa helper browser trả lời câu hỏi thực và CSS ngắt tiêu đề, đang chạy full mới.
- Full `v05-finish-tests-20261015T124159Z-7d0fbdb1`:**123/123,exit0**, build0warning/error, audit7project không báo advisory. Đây là code **trước** hardening đường PDF upload cũ, không dùng làm regression sau sửa cuối.
- Rà soát C tìm thấy ngoại lệ Development PDF cũ còn công khai NotScanned. Đã thêm gate và thao tác chuyển từng PDF Uploaded vào hàng đợi quét thật, không chuyển Source53. Run mới sau hardening đang chạy; regression2A đã chứng minh queue→real scan→exact publish, rồi phát hiện guard cùng hồ sơ chặn việc dùng chung PDF sạch. Cần giữ cùng hồ sơ cho Word và bảo toàn sharing PDF/Excel theo quyền2A; chưa PASS code cuối.
- Full `v05-finish-tests-20261015T125354Z-0386d2d9`:**122/123**, đúng lỗi sharing trên, raw giữ nguyên. Đã thu hẹp guard cùng hồ sơ cho Word; PDF/Excel vẫn cần proof Clean nhưng tham chiếu/quyền theo2A. Thêm kiểm ACL cho preview tệp qua hồ sơ được chia sẻ, giữ pointer/revocation. Focused HTTP thật đang chạy trước full cuối.

## Gate không thay đổi

AC16.1 **BLOCKED:NO_REAL_WORD_SAMPLES**; chưa có3 Word nghiệp vụ thật khác tuần. Nhóm2 FR17 NEEDS_PO/pháp chế; public history tuần NEEDS_PO; lookup riêng tư khóa chờ PO/ATTT. Scanner Production/PII policy, MFA, SMTP thật, restore drill, UAT, domain/TLS/cutover/full migration vẫn chưa nghiệm thu.

V04 raw smoke2B, D15 worker và diagnostic v0.5 trước đây vẫn artifact PARTIAL. Không phục dựng giả. SQL category timeout vẫn **MONITORING / ROOT CAUSE UNKNOWN**. Một quan sát chỉ SQL Test thấy `RESOURCE_SEMAPHORE`, không đủ gán nguyên nhân sự cố lịch sử; không tăng timeout hoặc stress Dev.
