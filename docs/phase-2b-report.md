# Báo cáo 2B — 13/10/2026

**2B.1–2B.5 PASS LOCAL; CHƯA PRODUCTION READY.** Regression73/73 và gói web/worker đã chạy thực. Đây là báo cáo hiện tại, thay checkpoint ban đầu; giữ lịch sử0–1/2A tại báo cáo tương ứng. Quyền vận hành mới chưa được cấp theo yêu cầu.

Baseline binary 2A được chạy lại: 38/38 SQL/HTTP groups và 40 browser page checks riêng. Dev manifest trước thay đổi giữ 37 nội dung, 53 tài nguyên nguồn (41 ảnh/12 PDF), 70 capture/68 SQL snapshot, 8 ngoại lệ, 2 video metadata/0 tệp video; 0 mismatch/17 nhóm với cuối 2A.

Backup mới Dev130/Test173 tệp đọc lại checksum đúng, SQL VERIFYONLY PASS; không chứng minh restore. `.env` không đổi. Evidence riêng `.local/evidence/phase2b` ignored, không ghi credential vào báo cáo.

Migration additive **`20261013030300_PortalAdministration`** giữ **`20261012083717_CmsRevisionWorkflow`** của 2A. Review SQL ở [phase2b-migration.sql](evidence/phase2b-migration.sql); xác minh đúng named local SQLEXPRESS/Windows auth/compat160. Test trước, đối chiếu0/17 rồi mới Dev sau kiểm lại target/backup. Dev cuối **0 mismatch/17 nhóm**. Không startup migrate/Down/reset/import lại Dev/crawl nguồn/auto-grant vận hành.

| Checkpoint | Chức năng thực | Bằng chứng vùng |
|---|---|---|
|2B.1|Thông báo/Giới thiệu/Văn bản/Dự thảo; typed metadata cùng immutable revision; nhiều PDF có nhãn; preview/duyệt/đăng/gỡ/restore qua workflow2A|SQL6 nhóm; HTTP4 loại trong13 checkpoint CMS; typed UI14 trang/7 thao tác PASS|
|2B.2|Tài khoản/nhóm, mật khẩu tạm buộc đổi, khóa/thu hồi phiên, quyền hiện thời, ủy quyền catalog, last-admin SQL/audit|SQL10; HTTP5; UI18 trang/4 thao tác PASS; grant dry-run Test không đổi claims/stamps/audit|
|2B.3|Immutable draft/live config, đủ14 chuyên mục và hạng mục gốc; menu/footer/liên kết/banner/logo/home/pin/contact/vocabulary|SQL7; HTTP4; UI34 trang/5 thao tác PASS; Dev readonly15 trang/3 kiểm chứng nguồn PASS|
|2B.4|Normalized published projection; lọc tiếng Việt/loại/chuyên mục/ngày/cơ quan/loại văn bản/lĩnh vực/ngày ban hành; count/OFFSET SQL, ID tie-breaker|Trong SQL6 có23 fixture cùng ngày: pages10/10/3, không lặp; HTTP/UI tra cứu kết hợp/draft isolation PASS; actual SQL/timing/plan|
|2B.5|Lịch SQL exact revision/hash/principals, UTC+7; console worker độc lập, lease/generation/token, retry giới hạn, quyền/media lúc chạy, transaction publish/audit/completion|SQL/process9; HTTP3; UI10 trang/4 thao tác PASS; hai worker cạnh tranh, web tắt và web có public state trước worker|

Các số SQL/HTTP/UI là đơn vị riêng, **không cộng** thành full suite. Báo cáo runner73 bao gồm wrapper HTTP/UI; 40/14/18/34/10 trang browser ghi riêng.

## Bảo toàn, schema và file thay đổi

Dev vẫn **37 Content/SourceRecord, 47 revision, 74 alias, 53 source assets (41 ảnh/12 PDF), 70 capture/68 SQL snapshot chống trùng, 8 ngoại lệ riêng, 2 video metadata/0 tệp**. Source identity/title/summary/body/author/hash/ngày/chuyên mục/alias/media metadata+bytes/quan hệ/snapshot/ngoại lệ/legal-draft cũ so sánh17 nhóm0 mismatch; `after-dev-final.json`. Test ngay sau migration `after-test-migration.json`0/17. Fixture Test không tính là nguồn Dev.

Native backup COPY_ONLY CHECKSUM + RESTORE VERIFYONLY và manifest kho/config có ACL; Dev130/130, Test173/173 file hash đúng. Kiểm lại Dev sau regression vẫn130/130 và `.env` byte hash không đổi. **Chưa restore database hoặc diễn tập restore toàn hệ**. `.local/evidence/phase2b/backup-*.json`, `backup-verify-*.json`.

Thêm owned `RevisionMetadata`/`DraftMetadata`, `RevisionSearchProjection`, reviewer provenance/Identity status, immutable SiteConfigurationRevision/children/header draft-live, PublicationJobs/Attempts/WorkerHeartbeats. Backfill metadata từ dữ liệu cũ đã xác minh, giữ date-only/raw UTC và trạng thái chưa xác định. Projection backfill bounded50: **516 Test lúc migrate, 47 Dev**. Dev mới có **47 RevisionMetadata, 0 DraftMetadata, 47 projection, 1 config revision/18 category configs, 0 jobs/attempts/heartbeat**. Đây là bảng bổ sung, không phải bài nguồn mới. `initialize-site.ps1` lần đầutrue, chạy lạifalse; không seed startup.

`dev-identity-final.json`: aggregate SHA Identity/password hashes/security stamps/roles/claims trước-sau PASS; không lưu/in raw secret. Không tự gán quyền hoặc reset password.

SQL **Test báo riêng sau các lượt thử**, không trộn vào nguồn Dev: trước migration386 content/516 revision/141 media/87 SourceRecord; hiện **845 content/1178 revision/193 media/99 SourceRecord**,1178 RevisionMetadata/projection,693 DraftMetadata,117 config revisions/3753 category-config rows,117 jobs/130 attempts/20 heartbeat rows. Đây là fixture cũ cộng fixture của các lượt focused/FAIL/final được giữ lại, không phải số test hoặc số nội dung đã chuyển nguồn. `test-final-counts.json`, không reset Test.

File chính:

- Domain/Application: `ContentMetadata`, `SiteEntities`, `PublicationJob`, permission catalog.
- Infrastructure: `WorkflowService`, `CurrentPermissionService`, `AccountAdministrationService`, `SiteConfigurationService`, `SchedulerService`, `PublicMediaPolicy`, `SearchProjectionService`, importer, EF models/migration. History immutable và chống trùng vẫn giữ.
- Web: Admin typed metadata/PDF labels; accounts/roles; site forms/private preview/history; schedules; `PublicContentQuery`, tra cứu/search/home/menu/footer/contact từ live config. Client vẫn Jodit tự host và workflow/autosave2A.
- Worker console chỉ PublicationJobs. Scripts web/worker owned process, explicit migrate/init/backfill/grant dry-run, tests vùng, publish/smoke/preservation. `.env.example` thêm scheduler defaults; **`.env` không sửa**.

Draft không đổi public metadata/media/taxonomy. Approval đúng revision/hash; sửa sau duyệt vô hiệu approval/lịch cũ. Endpoint/resource kiểm quyền SQL hiện thời; rowversion stale409; late autosave không lùi trạng thái. Restore thành private draft. Banner/logo nháp theo live references, thu hồi/hết hạn không giữ grant nhờ cache.

`-AllowUnscannedMedia` chỉ **Development loopback tường minh**, tệp vẫn **NotScanned**. Source NotScanned cũng chỉ grandfather ở Development loopback. Production/Staging không phục vụ/publish NotScanned; worker cùng policy, Staging bypass startup bị từ chối thực. Scanner thật chưa có.

Public SQL mỗi request +`no-store`; cấu hình chỉ cache trong một request để header/menu/footer cùng revision. Không có cache nội dung giữa request/process phải refresh admin. Web từng xem public trước worker thấy revision mới ở request tiếp theo; banner hết hạn lọc theo giờ request. Chưa benchmark/cache production.

SQL Express compat160/collation SQL_Latin1_General_CP1_CI_AS/FullTextInstalled0. Search normalized substring **HTML text + metadata**, không PDF/OCR. Actual plan/5 timings nhỏ: `search-probe.json`, `search-plan.xml`; không coi B-tree Number tối ưu mọi substring. Public query dùng unique LEFT JOIN projection/live category thay scalar subqueries; paging/count SQL vẫn đúng. Actual application-role category probe3 lần khoảng60/11/10ms; không phải benchmark production.

## Regression cuối và lỗi thực

Run **`final-20261013-042438`**, 13/10/2026 **11:24:38–11:28:03 UTC+7**: `scripts/test.ps1 -SkipAudit`, exit0, **73/73 runner groups**, locked restore/build0 warning/error. Log/report ở `.local/evidence/phase2b/final-20261013-042438.log`/`-tests.json`, `final-run.json`. `final-code-verification.json` xác minh **173 tệp, 0 path/SHA thay đổi**, canonical sorted code SHA **`40064a776dca01277b4a78687b42974823cdc565365e8b32b7d4e25befd63cc8`**. Hash ban đầu phụ thuộc thứ tự `rg` khác nhau; giữ raw records và độc lập kiểm từng path/hash, không đổi kết quả test để gọi PASS.

Browser cuối: CMS40/40 và Word paste/table/ảnh/PDF/workflow; typed14/14 +7 thao tác; accounts18/18 +4; config34/34 +5; scheduler10/10 +4. UI sâu390/1440, smoke360/768/1024; keyboard/labels/reflow/validation/trạng thái thực. Chặn mọi off-origin trong **profile browser**, không đổi firewall máy; 0 JS/off-origin. Root/agent xem ảnh menu/quyền/lịch/document/public desktop-mobile; footer contrast9,799. Không chứng nhận WCAG/iOS/Safari/UAT. Dev readonly riêng15/15 trang +3 kiểm chứng nguồn/PDF284.120byte SHA đúng, 0 request ghi; `dev-public/`.

Giữ failed runs `full-first-failed.json`70/73, `full-second-failed.json`72/73, `full-third-failed.json`72/73. Sửa machine-name case guard, media default-test profile, blank PDF-caption/null binder, live-category picker/validation, scheduler fakeclock fixture che homepage và footer link. Fakeclock fixture chỉ **gỡ public qua workflow trên Test**, giữ revisions/jobs/audit, không xóa/reset/đổi ngày.

Có timeout SQL category thật trong hai lần UI. Không tăng timeout/bỏ assert; query cũ cũng chạy lại40/40 thành công, **chưa chứng minh nguyên nhân**. Giảm subquery tương quan bằng unique joins, lưu SQL/probe/DMV Test; full cuối không timeout. Nếu tái diễn cần điều tra, không suy một probe nhanh thành tải production đạt. Browser mới sửa CDP response-body mất khi navigation và locator trùng search header; vẫn kiểm persisted SQL/public thực, không fake response.

NuGet audit mới **7 project**, npm Web: feeds hiện tại0 vulnerability, `evidence/nuget-audit-2b.json`/`npm-audit-2b.json`. Audit không chạy trong lệnh `-SkipAudit` trên; đây là lần chạy riêng, không phải security audit production.

Gói Release web `.local/publish/phase2b/web`: **121 file/397.971.204 byte**, đủ editor/native/license, không runtime private files. `scripts/smoke-publish.ps1` thực chạy Test5191: **11 public URL PASS**, **5 privacy checks PASS** (admin302; env/snapshot404); process đã dừng. [publish-2b-smoke.json](evidence/publish-2b-smoke.json).

Worker `.local/publish/phase2b/worker`: **81 file/395.171.237 byte**, native/PDF/license đủ, không env/media/snapshot/keys. Gói Worker.dll actual boot **exit0**, `--once --job` scoped completed Test fixture, bypass uploadedNotScanned false; không claim job khác. Actual đăng khi web tắt và hai worker cạnh tranh đã được chứng minh riêng trong SQL/process suite. `worker-package-inspection.json`/boot log. Mọi owned web/worker runtime đã dừng; không cài service/deploy.

Bản summary an toàn: [phase2b-tests-summary.json](evidence/phase2b-tests-summary.json), gồm run/time/command/code state/exit, nguồn/bảng mới và số từng vùng/gói. Raw SQL/private fixture/log/screens trong `.local/evidence/phase2b` ignored; không chứa credential trong summary.

## Chạy và quyền còn thiếu

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-worker-dev.ps1 -NoBuild
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -SkipAudit
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -Region Content -SkipAudit
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-worker.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-publish.ps1
```

Web **http://127.0.0.1:5188/**; `/admin/login`, `/admin?kind=LegalDocument`, `/admin/create?kind=DraftDocument`, `/admin/users`, `/admin/roles`, `/admin/site`, `/admin/schedules`; public `/tim-kiem`, `/van-ban`, `/du-thao`. Worker console không HTTP URL; Ctrl+C dừng foreground; background stop xác minh PID/start time/executable/workspace. Không cài service. Xem [runbook](scheduler-runbook.md).

Tài khoản vận hành thiếu **effective permissions**: `roles.manage`, `permissions.grant`, `permissions.grant.privileged`, `site.view`, `site.manage`, `site.publish`, `content.schedule`, và 2A `content.unpublish`, `content.trash`, `content.restore`. Create/view/approved publish tương thích đã suy từ edit/publish cũ, không nhầm missing stored claims. Màn hình mới403 khi thiếu quyền; không tự nâng quyền để demo. [Hướng dẫn quyền](account-admin-guide.md) và `scripts/grant-portal-permissions.ps1` có exact ID/name/DB/target, mặc định dry-run; **không Apply Dev**.

Hướng dẫn [biên tập](editor-guide.md), [cấu hình](site-admin-guide.md), [kiến trúc](cms-architecture.md) đã cập nhật. Không cần nhập lại bài để dùng CMS/schema mới.

Scanner thật/MFA/restore/UAT/2C bình luận kiểm duyệt/2D video tải máy/toàn nguồn và cutover vẫn chưa hoàn thành. Lượt này chỉ 2B; không public/deploy/commit/push/DNS/service/email.

Không đóng8 ngoại lệ hoặc coi2 metadata là video đã chuyển. Directive adapter và Cơ cấu tổ chức nguồn chưa xác minh vẫn unresolved, không bịa nội dung. Bước tiếp theo: phân bổ quyền vận hành có thẩm quyền qua dry-run khi được yêu cầu; sau đó **2C** tiếp nhận câu hỏi/liên hệ/**bình luận dưới bài**/góp ý dự thảo có kiểm duyệt. **2D video tải trực tiếp từ máy** và **Giai đoạn3 chuyển toàn nguồn tự động** giữ trong backlog, không yêu cầu nhập tay từng bài.
