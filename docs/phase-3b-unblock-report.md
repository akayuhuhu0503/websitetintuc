# Phase 3B Unblock — 19/10/2026

**Importer PASS LOCAL trong phạm vi kiểm thử; tăng dữ liệu nguồn PARTIAL/BLOCKED; Dev NOT_READY_FOR_APPROVAL / NEEDS_APPROVAL. CHƯA PRODUCTION READY.** Không chạy full137, không Apply Dev, không migration, không fetch nguồn.

## Kết quả trực tiếp

1. **30/30 danh tính vẫn BLOCKING**, không phải30 bài tin mới:20 Article,2 Announcement,2 StaticPage,3 LegalDocument,1 DraftDocument,2 Video. Cả30 đã có CMS ID. [Bảng từng danh tính](phase-3b-unblock-analysis.md) ghi cả nguyên nhân giao nhau.
2. **0/30 đủ điều kiện nhập thêm an toàn** theo policy hiện hành. Có1 ảnh phụ thật sự thiếu có thể bỏ với cảnh báo; bài3681 vẫn còn ảnh khác NotScanned.
3. **0 bài nguồn/0 asset nguồn nhập thêm** trong lượt này. Tổng batch thật đã có vẫn3 bài mới3666/3670/3673 và7 JPEG Clean; không cộng fixture kỹ thuật thành dữ liệu nguồn.
4. **30 danh tính cần xử lý**, trong đó29 có scanner blocker;1 PDF không qua format;2 video thiếu tệp phát. Các số giao nhau. Cần người có thẩm quyền cho quy trình quét/áp dụng tài nguyên gốc; không tự đổi53 NotScanned.
5. **Dev chưa READY_FOR_APPROVAL để Apply.** Manifest dự kiến3 content mới,7 ảnh thật Clean,1 comparison đủ điều kiện,6 Keep,20 URL mapping (6 alias mới). Công cụ cố ý từ chối mọi Dev write; chưa có entrypoint Apply/rollback Dev được duyệt. Không đưa lệnh import legacy để lách. Hồ sơ chuẩn bị được, chưa phải batch có lệnh Apply an toàn hoạt động.
6. Tiếp theo: phê duyệt quy trình quét/áp dụng53 tệp đúng bytes mà vẫn bảo toàn nguồn; xử lý PDF3233; cung cấp/quyền lấy video40/41. Chỉ mở rộng nguồn sau approval PO/source-owner, không full crawl để né các blocker hiện tại.

## Bằng chứng và phân loại

Run `.local/evidence/phase3b/unblock-20261019T030741Z-26e14fd7/`. `blocked-analysis.json` giữ phân tích ban đầu; `blocked-analysis-reviewed.json` đối chiếu dry-run mới, `blocked-analysis.csv` và `test-preview/findings.csv` có từng URL/hash/role/giải pháp. Không sửa checkpoint3A.

29 danh tính tham chiếu **53 tệp NotScanned khác nhau:39 JPEG +2 PNG +12 PDF**. Tập SHA khớp **chính xác53 tài nguyên gốc được bảo vệ** trên Dev và Test. Đây là tệp có bytes, chưa có proof Clean được áp dụng, không phải ảnh không tải được. Không bỏ chúng như ảnh tùy chọn để vượt scanner, không nâng trạng thái bằng proof bản sao.

Article3233: capture HTTP200 `application/pdf`,2.919.460 bytes; có20 byte tiền tố trước `%PDF`. Không kết luận URL hiện đang hỏng, không cắt tiền tố rồi giả vờ là file nguồn nguyên vẹn, không tự sửa parser/scanner để chấp nhận. Hai lỗi format/attachment là cùng1 tài nguyên, không cộng thành2 trường hợp.

Article3681: ảnh thứ2 có HTTP200/image/jpeg nhưng0 bytes, SHA `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`. Đối chiếu văn bản đầy đủ và caption ảnh Phó Cục trưởng làm việc với đoàn, quyết định bỏ ảnh minh họa thiếu chỉ cho Test trong `optional-images-review.json`, SHA `10df0a7801cb30216593c6ea5bf8f537140e992fab3743faf037c938886a13ab`. Warning NON-BLOCKING thay lỗi format của ảnh rỗng; vẫn giữ caption và ảnh đầu bị scanner chặn. Không tự sửa bài đang public.

Không có bằng chứng thiếu **cover bắt buộc riêng**: lựa chọn ảnh đầu làm cover của adapter không phải yêu cầu nghiệp vụ của nguồn. Summary/author tùy chọn thiếu được giữ trống và cảnh báo; không có blocker metadata thiết yếu mới trong30 mục. Video41 còn URL gdla ngoài quyền nguồn được duyệt; video40 không có URL tệp phát. Cơ cấu lãnh đạo1780 là trang gần như chỉ có ảnh, không được áp quy tắc bỏ ảnh phụ.

## Thay đổi phần mềm

- `src/MigrationCli/BatchReadiness.cs` mới: phân loại BLOCKING/NON-BLOCKING/NEEDS_REVIEW, role tài nguyên và kiểm điều kiện review ảnh phụ.
- `BatchModel.cs`, `SourceBatch.cs`: plan schema2 có finding và review ảnh theo identity/URL/lý do/evidence; vẫn đọc plan1 cũ. Chỉ bỏ ảnh **thiếu hoặc capture0byte xác minh hash**, có review; không bỏ capture không rỗng NotScanned/Infected/sai hash/sai format. PDF, ảnh liên kết, trang chỉ ảnh và Video không có ngoại lệ này. Không sửa danh sách AssetUrls/raw của staging. Fingerprint ràng buộc quyết định bỏ ảnh; commit kiểm lại bytes và CMS stamp.
- `SqlImporter.Batch.cs`, `BatchCommand.cs`: warning SQL audit theo content/revision/batch; export `findings.csv`; review file phải trong evidence riêng và khớp SHA. Không thay public/current/editor/approval của bài đã có.
- `src/Web/Views/Admin/History.cshtml`: vùng cảnh báo nhập nguồn trên trang lịch sử, dùng ACL và HTML escaping hiện có.
- `scripts/phase3b.ps1`: `-BuildRoot` riêng, `-OptionalImages/-OptionalImagesSha`; Dev vẫn chỉ Preview.
- `scripts/test.ps1`, `scripts/test-phase3b.ps1`, `tests/LandPortal.Tests/Program.cs`, `BatchUnblockTests.cs` mới: vùng `Unblock` riêng, không khởi tạo FFmpeg/video. Test mới cũng được đăng ký vào runner Batch/All cho các lượt sau; chưa chạy All ở đây.

**Không migration/package/.env/Identity/grant Dev thay đổi.** Build riêng `.local/build/phase3b/unblock-current`, giữ binary/preview cũ.

## Kiểm thử đã chạy sau sửa cuối

Focused cuối `phase3b-tests-20261019T031651Z-6aa5ff6f`: **7/7 PASS,0 FAIL**, exit0; build solution gồm CLI/Web/Worker0 warning/error;347 code paths/0 drift,12.408 raw được guard/0 drift. Không cộng với focused7/7 trước đó hoặc full137 lịch sử. Dependency audit NOT RUN (`-SkipAudit`, không đổi package).

| Kiểm tra | Kết quả / phạm vi |
|---|---|
| Ảnh thiếu có review → SQL content, caption/text, không img/hotlink/asset giả; warning đúng revision | PASS, fixture TECHNICAL TEST; không dùng làm dữ liệu nguồn |
| Chặn bỏ NotScanned/Infected/capture mất proof/PDF/trang ảnh thiết yếu | PASS, negative trên state Test riêng; không ghi scan conclusion |
| Transaction từng mục/resume/idempotence/publication rollback | PASS, SQL Test fixture, giữ revisions/files/audit |
| Fault trước commit → SQL rollback; retry context mới | PASS; fault injection, không gọi là process-kill acceptance |
| Source comparison không ghi đè editor/live/approval; stale stamp bị từ chối | PASS, fixture riêng |
| Hash/size/proof sai, Production và Apply Dev bị từ chối | PASS |
| HTTP thật3 bài nguồn/7 ảnh byte SHA đúng/chuyên mục; guest NotScanned và admin preview bị chặn | PASS, owned5301 đã dừng; log riêng `unblock-http/` |
| Full137/video/CMS ngoài vùng, scanner mới/EICAR, UI admin tương tác/screenshots mới, restore drill | NOT RUN trong lượt này |

Raw lần build đầu lỗi thiếu namespace `HtmlPolicy` giữ tại `phase3b-tests-20261019T031142Z-27260719`; chưa chạy SQL tests ở lần build lỗi. Đã sửa và có focused sau code cuối, không xóa lỗi hoặc gọi lượt đầu PASS.

Dry-run Test mới đọc40, có đối chiếu riêng đúng30 danh tính ban đầu:30 Blocked,9 Keep,1 comparison; đây không phải30 bài mới. Lượt chạy lại **plan gốc đã commit**, bằng code mới, trả10 AlreadyCommitted/30 Blocked. `test-before-repeat.json`/`test-after-repeat.json`:0 khác counts/source/content/revision/asset/file; `original-batch-verify/verify.json` PASS,0 mismatch. Không commit lại fresh plan để thêm audit không cần thiết hoặc tạo comparison QA trùng.

`test-final-readback.json` sau focused cuối:40 source identities và60 assets liên quan (53 gốc+7 mới từ lượt trước),0 sai hash. So với readback trước repeat:0 khác source/content/revision/asset/file của40 thật. Delta toàn DB Test sau focused cuối là5 content/6 revisions/5 source records/10 aliases/17 audit/5 ImportRuns **TECHNICAL TEST**,0 assets; tất cả là fixture ngoài40, không tính nhập nguồn.

## Bảo toàn, Dev manifest và bàn giao

Trước sửa seal Phase3B cũ **1.833 tệp/0 drift**. Sau sửa có delta code/docs kê trong manifest mới; raw cũ được giữ nguyên. Backup Test mới `.local/backups/LandPortal_Test-20261019T031036Z`, COPY_ONLY/CHECKSUM/VERIFYONLY, **2.931 file,791 inventory file**, independent hash readback0 khác; chưa restore. Quyền SQL service chỉ vào thư mục backup riêng, không grant Dev.

Dev sau cùng **0 khác/17 nhóm**,37 content/47 revision/74 alias,53 tài nguyên41ảnh/12PDF NotScanned,70 capture/68 SQL snapshot,8 ngoại lệ,2video metadata/0 tệp. Identity và.env hash nguyên. `source-before/after.json`, `identity-before/after.json`, `reconciliation.json` lưu riêng. SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**, artifact incident lịch sử vẫn PARTIAL.

`dev-batch-manifest.json` SHA **dd03f606176efb0c629184213298db12031fec71ff70bf3b7764a43a7dc7f7da** chứa metadata/fingerprint/raw SHA3 content mới,7 exact-byte assets/engine/time,20 mapping, expected count/hash,3 source differences (2 blocked +1 comparison đủ tài nguyên), before stamp, exclusions và rollback specification. `dev-url-mapping.csv` không đổi domain thật. Dev fresh preview SHA **eb9fc5a9e59000597116c529deea0bb705027d8951015f17d874b96278ab33fa**.

Nếu cả10 dòng đủ điều kiện sau này được duyệt, dự kiến delta3 content/4 revisions/7 assets/6 aliases,53 gốc nguyên; comparison không public. Đây là **dự kiến chưa thực thi**, không thay Dev hiện tại. Rollback Dev vẫn specification, chưa có lệnh được duyệt: chỉ gỡ3 publication mới còn nguyên revision/stamp, dừng nếu editor đã sửa; giữ nguồn/revision/file/audit và74 alias cũ. Không reset/restore toàn DB tự động.

Lệnh kiểm thử đúng vùng:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3b.ps1 -Region Unblock -SkipAudit
```

Dry-run đọc (helper local chỉ dùng lại ở **report directory mới**, đường dẫn hiện tại đã có bằng chứng nên sẽ từ chối ghi đè):

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3b.ps1 -Action Preview -Database LandPortal_Dev -NoBuild -BuildRoot .local/build/phase3b/unblock-current -Checkpoint .local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin -CheckpointSha d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3
```

Không có lệnh Apply Dev hợp lệ để cung cấp ở checkpoint này; `-Action Commit -Database LandPortal_Dev` bị chặn chủ đích. Preview cũ5298 giữ nguyên nếu còn chạy, không gọi là UI code mới. Mốc sau cần approval và entrypoint áp/rollback có thẩm quyền; không tự grant/.env/push/deploy/DNS/email hoặc chuyển sang crawl rộng.

Preview code mới đã mở riêng **http://127.0.0.1:5302/**, SQL Test, strict scanner policy, Mail Disabled. Health/bài3666/chuyên mục3GET200 ghi tại `preview-smoke.json` sau focused, không cộng vào7/7. Chạy lại khi port rảnh:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3b-unblock-preview-web.ps1
```

Test có fixture TECHNICAL TEST; link bài thật ở trên dùng CMS ID nguồn đã đối chiếu. Không public website hoặc dừng preview cũ.

Khóa bàn giao tại `manifest-final.json` / `manifest-readback.json` trong run Unblock; delta so seal cũ trong `prior-seal-delta.json`. Tham chiếu gọn [summary](evidence/phase3b/unblock-20261019T030741Z-26e14fd7/summary.json) và [manifest reference](evidence/phase3b/unblock-20261019T030741Z-26e14fd7/manifest-reference.json). Seal không ghi đè artifact lịch sử.
