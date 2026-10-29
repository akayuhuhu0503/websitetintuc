# Báo cáo Giai đoạn 2A — 12/10/2026

Trạng thái: **GIAI ĐOẠN 2A: PASS trong môi trường local được kiểm chứng**. Focused SQL/HTTP đã kiểm chứng thu hồi tài nguyên đã bỏ khỏi bài; package sau sửa cuối đã publish và smoke thực. Dự án **CHƯA PRODUCTION READY**. Tái sử dụng ASP.NET Core 10 MVC, EF Core, SQL Server, Identity, kho nguồn và public UI đã có. Chỉ CMS bài viết/revision/quyền/ảnh/PDF thuộc lượt này; không gọi đây là CMS đầy đủ các hạng mục.

## Bằng chứng bảo toàn trước thay đổi

`scripts/source-preservation.ps1` kết nối read-only tới database local đã xác minh, không in connection string. Manifest riêng `.local/evidence/phase2a/before-dev.json` ghi source identity/ID, payload và hash từng nhóm, tiêu đề/nội dung/mô tả/tác giả/ngày gốc, chuyên mục, mapping tài nguyên, alias, trường văn bản/dự thảo, revision nguồn, metadata/hash/byte của media và snapshot, lịch sử lỗi và báo cáo nguồn. Tệp bằng chứng đầy đủ và backup chứa dữ liệu nguồn/secret nằm trong `.local` ignored.

| Nhóm đối chiếu Dev | Số thực trước migration | Giải thích |
|---|---:|---|
| Contents / SourceRecords nguồn | 37 / 37 | 26 bài, 2 thông báo, 2 giới thiệu, 3 văn bản, 2 dự thảo, 2 video metadata |
| Revision / alias | 47 / 74 | Revision lịch sử hiện hữu, không phải số bài nguồn |
| SQL MediaAssets nguồn | 53 | 41 ảnh, 12 PDF; 53/53 byte/hash trên đĩa khớp SQL |
| Video file | 0 | Hai đối tượng video chỉ có metadata/poster; chưa chuyển video thành công |
| Manifest snapshot | 70 lượt chụp | 70/70 tệp/hash hợp lệ; 68 cặp URL+SHA256 duy nhất |
| SQL SourceSnapshots | 68 | Importer chống trùng theo URL+SHA256; 2 lượt chụp trùng không tạo thêm dòng SQL. Không phải mất snapshot |
| Ngoại lệ nguồn | 8 riêng biệt | 10 dòng đang mở trong báo cáo; giữ nguyên lịch sử 33 dòng, 23 đã xử lý |

`scripts/backup-project.ps1 -Database LandPortal_Dev -GrantSqlBackupFolderAccess` đã thực hiện SQL native `BACKUP ... COPY_ONLY,CHECKSUM` và **RESTORE VERIFYONLY WITH CHECKSUM thực tế**, đồng thời sao lưu media/snapshot/Data Protection keys/cấu hình `.env` vào thư mục riêng được hạn chế ACL. 130 tệp trong manifest hash. Bằng chứng: `.local/evidence/phase2a/backup-LandPortal_Dev.json`. SQL service chỉ được thêm quyền trong thư mục backup đúng dự án. **Chưa thử restore database**; VERIFYONLY không chứng minh toàn bộ quy trình khôi phục.

Test cũng đã được backup riêng và có manifest `.local/evidence/phase2a/before-test-migration.json`. Test chứa fixture/QA từ các lần trước; không tính chúng là nội dung nguồn Dev, không reset Test để khớp các con số của Dev.

## Baseline thực chạy

`scripts/test.ps1 -SkipAudit -BaselineExistingBuild` đã chạy suite trước thay đổi từ Debug binaries hiện có: **23/23 PASS** trên SQL Server `LandPortal_Test`, gồm HTTP Identity/CSRF/permission/concurrency/import-fixture. Bằng chứng riêng `.local/evidence/phase2a/baseline-sql-tests.json`, `baseline-http-web.log`. Build/package audit không chạy trong lệnh baseline này; không dùng báo cáo 23 test cũ để suy ra kết quả mới.

Trước chạy, test đã được đổi sang kho riêng `.local/tests/phase2a/{media,snapshots,temp,keys}` qua `scripts/test-profile.ps1`. Chỉ sao chép fixture có sẵn từ kho dự án; không crawl/tải lại nguồn, không ghi vào media/snapshot/keys của Dev. Dữ liệu/credential test chỉ nằm trong Test/ignored artifacts.

## Kiểm chứng sau thay đổi

Migration bổ sung `20261012083717_CmsRevisionWorkflow` đã được review SQL (`docs/evidence/phase2a-migration.sql`), áp dụng trước vào `LandPortal_Test`, đối chiếu **0 mismatch/17 nhóm**, rồi mới áp dụng `LandPortal_Dev`. Backfill revision category/cover/public flags giữ ID, nội dung và ngày gốc; không chạy importer trên Dev để demo. Đối chiếu sau và cuối lượt `.local/evidence/phase2a/after-dev-media-reference-fix.json`: **0 mismatch/17 nhóm**, số nguồn/files ở bảng trên giữ nguyên. Bản backup đã đọc lại độc lập **130/130 tệp đúng SHA256**, `.env` không thay đổi byte/hash; `.local/evidence/phase2a/backup-verify-LandPortal_Dev.json`.

Phần mới chạy thực: vùng làm việc `ContentDraft`, immutable revision đầy đủ metadata/chuyên mục/cover/tài nguyên; SQL rowversion và command ID chống ghi đè/gửi lặp; quyền hiện thời đọc từ Identity SQL; gửi duyệt/trả bài có lý do/duyệt/đăng đúng revision/đăng trực tiếp/gỡ/thùng rác/khôi phục/nhân bản; private preview/history/compare; ảnh/PDF upload streaming và kiểm tra byte/decoder thực. Editor Jodit 4.17.3 tự host (MIT) thay Quill core vì Quill cũ làm bảng nguồn chỉ đọc. Autosave chỉ cập nhật vùng làm việc, thao tác gửi duyệt đóng draft và đóng dấu phiên bản. File nháp/thumbnail/preview không mở cho khách; public media phụ thuộc tham chiếu revision đang được công khai.

`scripts/run-dev.ps1 -AllowUnscannedMedia` là ngoại lệ **tường minh trong tiến trình Development trên loopback**, giữ trạng thái NotScanned. Không cấu hình scanner production và không tuyên bố tệp sạch. Mặc định/Production không upload hoặc publish uploaded NotScanned. Publish cũng kiểm tra tệp gốc tồn tại/đúng byte length; file thiếu không tạo thông báo đăng thành công.

Importer dùng snapshot offline, giữ nguồn mới vào comparison revision khi đã có biên tập hoặc bị gỡ/thùng rác. Category/cover thay đổi với legacy content hash giữ nguyên được phát hiện; rerun không tạo revision/alias/media vô ích. Nguồn cũ thiếu chuyên mục chỉ được grandfather khi có đúng source record và chuyên mục gốc thiếu; bài mới/clone vẫn cần chuyên mục, rich text rỗng không được xuất bản. Thay đổi trong source/revision/public queries không tự bật đồng bộ hoặc chạy cutover.

## Test, UI và package thực

| Lệnh / nhóm | Kết quả thực | Bằng chứng |
|---|---|---|
| `scripts/test.ps1 -SkipAudit` | **35/35 PASS**, locked restore/full Debug build 0 warning/error; SQL Server Test thật | `.local/evidence/phase2a/full-sql-tests.json` (full run được giữ riêng) |
| Focused SQL `--compatibility-only` sau các sửa cuối | **2/2 PASS**: legacy thiếu chuyên mục/new template/blank rich text và audit gỡ/thùng rác đúng published revision khác pending draft | `.local/evidence/phase2a/source-compatibility-tests.json` |
| SQL/media regression cuối `--sql-media-only` sau backend prune selections | **37 group thực chạy PASS**, HTTP/UI 1 group ghi **NOT RUN**: save/clone/restore/import/concurrency/scan; stale Inline/Cover/Thumbnail được loại, selected PDF không có body link vẫn hợp lệ, forged image Attachment bị từ chối không ghi SQL; 3 historical asset mappings giữ nguyên | `.local/evidence/phase2a/final-sql-media-tests.json` |
| Focused HTTP sau sửa ngày sắp xếp/history và tham chiếu media cuối | **9 checkpoint PASS**: quyền/CSRF/DTO/live revocation/private media/quy trình, home/category/search/detail từ đúng revision; bài mới SourceDate NULL xuất hiện trên home; history private link/audit đúng revision. Ảnh đã bỏ khỏi body/cover trả public404 sau đăng bản sửa nhưng historical preview200; bỏ PDF khỏi panel không khôi phục qua hidden fields; PDF dùng chung vẫn200, hết last live reference thì404 | `.local/evidence/phase2a/http-retired-media-report.json`, `http-retired-media-tests.json` |
| Media validator | **9 riskcases PASS**: JPEG/PDF nguồn thật, PNG thật, MIME/định dạng/byte/pixel caps, trùng tên/hash/owner, tệp thiếu/envelope xấu/kho public và scanner honesty | `docs/evidence/media-validation-report.json` |
| Browser SQL Test | **40/40 page checks PASS**: editor/list/preview/review list/review editor 390/1440, 30 smoke trang cũ tại 360/390/768/1024/1440; Word paste event, bàn phím, toolbar bảng thật, media picker ảnh/chú thích/PDF, hai người gửi/duyệt/đăng thực | `.local/evidence/phase2a/ui/ui-report.json`, các PNG cùng thư mục |
| Độc lập nguồn | Chặn toàn bộ request ngoài origin trong browser profile; 0 request ngoài/0 page error; PDF local **200, 2.764.932 byte** và chữ ký `%PDF-` | `sourceIndependence` trong browser report |
| NuGet audit | Không báo vulnerable trong 7 projects/feed đã dùng | `docs/evidence/nuget-audit-2a.json` |
| Release local/publish smoke | Gói `.local/publish/phase2a/web`, **109 tệp**, 11 route/asset smoke PASS trên SQL Test; không `.env`/keys/snapshot/private data trong package; lần publish cuối có backend/query/history/CSS và sửa media memberships đã kiểm chứng | `docs/evidence/publish-2a-smoke.json` |

Không cộng các focused probes thành “full suite” mới: full run đã lưu riêng là 35 group; subset SQL/media mới có 37 group PASS và HTTP/UI NOT RUN, gồm các case bổ sung sau full run. Lần focused HTTP cuối dùng `--http-no-ui`: Browser **NOT RUN** trong report này, giữ bằng chứng browser40/40 đã chạy trước sửa DTO/backend không đổi layout. Source identities/files ở Dev không tăng do test. Các test dùng kho `.local/tests/phase2a` hoặc kho fixture media test riêng, không đổi nguồn Dev. Root và QA đã xem trực quan ảnh editor/preview desktop/mobile: chữ Việt dài, bảng/chú thích/PDF và ảnh đọc được, không tràn toàn trang. Đây là Edge viewport emulation, không chứng nhận WCAG hoặc iOS/Safari/hardware thật.

Các lỗi thực đã sửa: gallery response cũ ghi đè kết quả tìm PDF (thêm fence request), Jodit focus helper thiếu nhãn (thêm aria-label), public card có chuỗi định danh dài tràn mobile (min-width/word wrapping), bài mới không có SourceDate bị sắp sau toàn bộ bài nguồn (fallback ngày đăng UTC+7). Review cuối phát hiện hidden `PreservedAssets` khôi phục tài nguyên dù đã xóa placement; controller nay chỉ dựng mapping từ body đã sanitize, cover hiện tại và PDF đang chọn, hidden metadata chỉ được giữ caption cho ID còn dùng. Backend còn loại Inline/Cover/Thumbnail không còn body/cover và từ chối giả ảnh thành Attachment không có placement, trong khi PDF chọn qua panel hợp lệ. Focused HTTP chứng minh thu hồi public và giữ historical/shared access thực. Test harness cũng được sửa riêng: cookie Forbid HTML trả redirect tới access-denied; so sánh text cần decode HTML; ảnh cover đã nằm inline thì không render ảnh trùng; response JSON mất sau browser navigation và autosave có thể tạo ID trước manual save. Không bỏ assert quyền, metadata hoặc layout để che bug.

Không public site, đổi DNS, commit/push, reset database/mật khẩu hoặc gửi email thật. Các tiến trình Test web do suite tạo đã dừng.

## Lệnh chạy và tệp chính

URL local: `http://127.0.0.1:5188/`, admin `/admin/login`.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-dev.ps1 -AllowUnscannedMedia
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
```

Tệp thay đổi chính: `src/Domain/Entities.cs`, `src/Application/CmsContracts.cs` và permissions, `src/Infrastructure/WorkflowService.cs`, `PortalDbContext.cs`, `CurrentPermissionService.cs`, `MediaService.cs`, `HtmlPolicy.cs` và migration; `src/MigrationCli/SqlImporter.cs`; Web admin/media/public query/views/TypeScript/CSS/package locks; tests SQL/HTTP/media/browser; scripts test profile/preservation/backup/readback/run-dev. Thiết kế chi tiết ở `docs/cms-architecture.md`, hướng dẫn thao tác ở `docs/editor-guide.md`.

## Ngoại lệ và các mốc còn lại

Giữ 8 ngoại lệ nguồn: 2 URL không bóc được tiêu đề, 2 tài nguyên byte sai/không chuyển được, 2 video không có tệp được chuyển, robots404, logo404. Không tự đóng ngoại lệ bằng metadata hoặc poster. Scanner production chưa có; tệp nguồn tiếp tục NotScanned.

2B quản trị đầy đủ các hạng mục/tài khoản/menu/homepage/lịch worker; 2C câu hỏi/liên hệ/**bình luận dưới bài**/góp ý có kiểm duyệt; 2D video **tải trực tiếp từ máy**/resume/FFmpeg/worker; Giai đoạn 3 chuyển toàn bộ nội dung và xử lý ngoại lệ vẫn chưa nghiệm thu. Hai yêu cầu bình luận kiểm duyệt và video nội bộ được giữ nguyên.
