# Bàn giao V04-A/B/C/D — 14/10/2026

**PARTIAL về hồ sơ artifact lịch sử; runtime PASS LOCAL. CHƯA PRODUCTION READY.** V04-A audit PASS; V04-B bốn trang PASS LOCAL/CHỜ DUYỆT THIẾT KẾ; V04-C runtime83/83 PASS nhưng bảo toàn raw artifact2B PARTIAL; V04-D điều tra PASS LOCAL, MONITORING/ROOT CAUSE UNKNOWN. Lịch sử2B.1–2B.5 PASS LOCAL giữ nguyên, không làm lại2B hoặc đổi ASP.NET Core10/MVC/Razor/SQL Server.

Đã đọc đầy đủ AGENTS/RTK/master/2A/state/acceptance/reports2A2B/SRS/bảng32CH/prompt tiếp nối. Ba đặc tả v0.4 thiếu lúc đầu đã được người dùng bổ sung; baseline được kiểm chứng trước sửa, không ép số liệu hoặc lấy73 cũ làm kết quả hôm nay.

## Thay đổi thực

- Bốn trang public dùng SQL thật: homepage tin chính/tin phụ cạnh thông báo; modules gọn, giữ14 chuyên mục và hạng mục; card danh sách; bài rộng790px/chữ18px với fontsize/reset/copy/print thật; table/ảnh/PDF đọc trên mobile; tra cứu labels/sort/filterURL. CSS chỉ scope public, không sửa giao diện admin hàng loạt. Block rỗng ẩn, lỗi có log/correlation riêng. Video0tệp không hiện như đã chuyển.
- Search tiếng Việt giữ2B, ưu tiên exact số hiệu/slash, liên quan/mới, thêm signer/hiệu lực xác nhận; paging/count SQL. Canonical theo contentID, legacy slug vẫn resolve; SEO/body/ảnh chỉ public revision; preview không OG. Không full-text PDF/OCR, viewcount hoặc gửi bình luận giả.
- Scope SQL user claims gồm **chính và mọi phụ**, endpoint/list/history/preview/media/workflow/replay/clone/restore/scheduler đều kiểm tra. Scope assignment chống self/expansion/stale stamp và thu hồi phiên; worker kiểm tra fresh requester+reviewer. Không gán claims/quyền Dev.
- Idle30min, GET polling không renew, editor401/403 JSON; reauth đúng tài khoản/CSRF mới, giữ phần chưa lưu trong tab hiện tại, manual save tiếp tục kiểm tra quyền/concurrency. Không tự publish; đóng/reload trước lưu vẫn có thể mất phần chưa lưu.
- Nullable legal until/boundary/evidence theo revision; expired chỉ khi rule/evidence/status được xác nhận; dữ liệu nguồn không được đoán ngày. Null mới không vào JSON hash cũ.
- Public sort/page revisionIDs trước rồi bounded DTO sau, kiểm tra public pointer lại; gỡ/thay revision giữa hai lần đọc bị bỏ. Giảm sort text rộng/memory grant; không đổi timeout/index/SQL instance.
- Audit NuGet phát hiện2 advisory mới ở Pkcs9.0.11. Pin9.0.21 audit sạch nhưng probe thật nạp shared framework10.0.12, nên sửa thành **10.0.13**. Web/worker Release đã nạp app-local10.0.13 thực; SDK riêng10.0.112/EF10.0.12 vẫn giữ. Bản vá theo [Microsoft CVE-2026-84008](https://github.com/dotnet/announcements/issues/468) và [CVE-2026-70162](https://github.com/dotnet/announcements/issues/454); không sửa SDK/runtime hệ thống hoặc triển khai production.

Tái dùng accounts/roles/config draft-live/vocab/search projection/job/worker2B, workflow2A và source importer/cutover. Không làm2C/2D/2E/2F/Phase3.

## File và migration

| Vùng | File chính |
|---|---|
| Public/SQL | `src/Web/Services/PublicContentQuery.cs`, `PublicSiteService.cs`, `Controllers/HomeController.cs`, `PortalController.cs`, `Models/PortalModels.cs` |
| UI/SEO | `Views/Shared/_Layout.cshtml`, `_DocumentMetadata.cshtml`, `Views/Home/Configured.cshtml`, `Views/Portal/Article.cshtml`, `Documents.cshtml`, list views; `wwwroot/css/public-v04.css`; `Client/site.ts`, `editor.ts`, generatedJS |
| Scope/workflow | Application WorkflowModels; Infrastructure CurrentPermissionService/WorkflowService/AccountAdministrationService/SchedulerService; Admin/Media/Scheduler/Account controllers/models/views |
| Phiên | Web Program, Services/AdminSessionPolicy, Controllers/AdminSessionController, Views/Admin/Edit |
| Metadata | Domain ContentMetadata; Infrastructure PortalDbContext/Migrations/snapshot; `docs/evidence/v04-migration.sql` |
| Dependency | Directory.Packages.props, Infrastructure.csproj và5 lock files liên quan, Pkcs10.0.13 |
| Checks/lệnh | V04Tests/V04HttpTests/PublicQueryProbe/TestRuntimePaths;2A/2B harness dùng isolated build; browser v04-public/cms-check; test/run-dev/run-worker-dev/migrate/capture/backup/verify/code-state/smoke scripts |

Migration **20261014023001_V04LegalExpiry** chỉ ADD nullable `EffectiveUntilDate(date)`/`EffectivenessBoundary(nvarchar40)` ở RevisionMetadata/DraftMetadata. Không drop/update payload, không Down. Test migrate trước, backup/xác minh đúng target rồi apply local Dev, backfill search0row. 2A20261012083717 và2B20261013030300 còn trong history.

Backup Dev `.local/backups/LandPortal_Dev-20261014T022900Z`130tệp; Test `LandPortal_Test-20261014T022905Z`219tệp; native COPY_ONLY/CHECKSUM/VERIFYONLY và SHA readback thật, `.env`unchanged. **RESTORE chưa chạy.** Test trước/sau migration983Contents/1366Revisions/202Media không đổi,0mismatch; các run sau thêm fixtures Test được giữ, không reset.

## Bảo toàn Dev

| Chỉ tiêu | Trước/sau thực |
|---|---|
| Contents/SourceRecords |37/37,0bài demo mới |
| Revisions/alias |47/74;hash/pointers/ngày/quan hệ nguyên |
| Media |53:41ảnh/12PDF;0missing hoặc byte/hash mismatch |
| Captures/SQLsnapshots |70/68,68unique URL/hash;không ép70hàng |
| Ngoại lệ/video |8unresolved;2metadata/0tệp, chưa chuyển video thành công |
| Đối chiếu |0 sai khác/17 nhóm, before-dev→after-dev |
| Metadata/config/job |47metadata/0draft;0giá trị expiry mới;1configrevision/18category config (14gốc+4bổ sung);0Devjobs/attempts/heartbeat |
| Identity/env |Hash credentials/stamps/roles/claims nguyên, `.env`readback không đổi;10quyền thiếu chưa Apply |

Evidence `.local/evidence/v04/{before-dev,pre-migration-dev,after-dev,before-identity-dev,after-identity-dev,dev-facts,backup-verify-LandPortal_Dev}.json`. Không chỉ so counts:17nhóm và bytes/hash tệp. Mẫu37 không là toàn nguồn. Không crawl/importDev/tải lại nguồn; importer regression dùng snapshot/kho/SQLTest riêng.

## Test thực

Baseline hôm nay71/73 exit1 với verified2B binaries, build/audit NOT RUN; fail2 HTTP/UI timeout. Run delta đầu82/83 exit1 fail CMS UI do timeout, `regression-failed-memory-*` giữ. Focused content6/accounts10/delta10 từng chạy riêng; không cộng các lần. Test mới từng8/9 do expected exception sai (workflow đã chặn đúng bằng DbUpdateConcurrencyException); harness guard cũng từng fail, không tính PASS.

**Sau sửa cuối03:19:42–03:23:26 UTC (10:19–10:23 VN): `scripts/test.ps1 -IsolatedBuild`, exit0,83/83 runner groups**, locked restore/build0warning/error, audit7project không advisory được báo ở thời điểm này. Runner có wrapper, không phải83test nguyên tử. JSON/log/code hash trong summary;73historical không dùng làm kết quả mới.

Đơn vị riêng: V04HTTP6; CMS HTTP13/UI40 có reauth→manual saveSQL thật; typed UI14/7actions; accounts HTTP5/UI18; config HTTP4/UI34; scheduler HTTP3/UI10 và standalone workers/race/restart/scan/cross-process. Public Dev UI trước20/20/sau20/20,5viewport, actions2/3,0JS/hotlink/mutation. Không cộng UI/HTTP/smoke/worker vào83. [UI review](ui-review-v04.md) lưu ảnh trước/sau và giới hạn thiết bị/WCAG/CWV.

Release web126files,11URL/5privacy thật,0private packagefiles. Worker process --once exit0, Test-only empty job filter/0publication effects; boot không chứng minh workflow (workflow có test riêng). Startup hook chỉ Test xác nhận DLL10.0.13 ở cảhai package. Không chạy exploit/scanner thật.

SQL risk ở [quan sát timeout](sql-category-timeout-observation.md): tái hiện,158wait samples thật, semaphore memory grant; estimated plan46MB→2,7MB. Regression cuối không còn lỗi public ngoài kỳ vọng; nguồn áp lực bộ nhớ và mọi timeout cũ vẫn UNKNOWN.

## Sự cố artifact và checkpoint

Lệnh smoke kế thừa ban đầu vẫn ghi tên `publish-2b-smoke.json`, làm mất **raw artifact cũ** khi ghi V04. Đã chuyển output thật sang `publish-v04-smoke.json`, sửa script và đặt note rõ ở path cũ; không có bản gốc trong backup workspace, **không giả tái tạo**. Summary73/report2B/frozen manifests/final-run-log2B/package2B cũ và dữ liệu SQL/media/Identity giữ; không tuyên bố raw checksum2B nguyên. Yêu cầu bảo toàn lịch sử khiến **V04-C PARTIAL về artifact**, dù runtime83/83 PASS. Cần đối chiếu raw gốc nếu có; không làm lại2B/CMS hoặc reimport để bù.

## URL/lệnh và phần chưa làm

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -IsolatedBuild -NoBuild -Port 5198
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-worker-dev.ps1 -IsolatedBuild -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-worker-dev.ps1 -IsolatedBuild -NoBuild -TestProfile -Once
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -IsolatedBuild
```

Loopback5198: `/`, `/chuyen-muc/xay-dung-pho-bien-chinh-sach-phap-luat`, `/bai-viet/cf59602d-ac7c-4dd7-a2f7-4f3cb194b3b2`, `/van-ban`, `/admin/login`. Không gửi lệnh dừng preexisting5188; cuối lượt không còn listener5188 được quan sát, không suy đoán nguyên nhân. Binary cũ không coi là V04/bản đã vá. Probes dừng đúng owned processes; preview5198 hiện chạy PID39512 với descriptor `.local/evidence/v04/preview.json`; không chạy workerDev. Dừng đúng preview bằng `rtk proxy powershell -NoProfile -File .local/v04-stop-preview.ps1` (kiểm PID/starttime/executable), không kill dotnet hàng loạt.

10 quyền Dev chưa cấp: **roles.manage,permissions.grant,permissions.grant.privileged,site.view,site.manage,site.publish,content.schedule,content.unpublish,content.trash,content.restore**. Không reset/cấp quyền/env/email. Upload chưa quét chỉ khi có ngoại lệ Development local tường minh, vẫn NotScanned; Production/Staging/worker không lách.

32CH ở [gap](srs-v04-gap.md). Q04 tiện ích, Q09 legal rule/căn cứ, Q12 lịch/sổtay/chuyênđề chưa xác nhận, không tạo giả. Scanner/MFA/SMTP/HTTPS/restore/UAT/tải production chưa xong. Không commit/push/deploy/DNS/service/fullcrawl hoặc đóng2C/2D/2E/2F/Phase3. Tiếp: xử lý raw artifact nếu có, PO duyệt thiết kế và Q04/Q12; chỉ sau yêu cầu mới sang2C OTP/bình luận/hỏiđáp/liênhệ/gópý kiểmduyệt, rồi2D video uploadmáy.
