# Phase 3B — nhập và đối soát, 19/10/2026

**Checkpoint tiếp theo:** [Phase3B Unblock](phase-3b-unblock-report.md), focused cuối7/7 riêng,0 nhập thêm từ30 vì còn blocker thật. Báo cáo137/137 bên dưới là lịch sử trước deltaUnblock, không coi là full regression code mới. Dev chưaApply.

**PASS LOCAL phạm vi Test batch có bằng chứng; tổng thể PARTIAL, APPLY DEV NEEDS_APPROVAL. CHƯA PRODUCTION READY.** Không quét nguồn trong lượt này. [Manifest](phase-3b-import-manifest.md), [URL mapping](phase-3b-url-mapping.md), [runbook](phase-3b-runbook.md).

## Baseline đã kiểm chứng

Trước sửa, manifest Phase3A **2.503 tệp/0 drift**. Đọc đủ prompt đính kèm, master/SRS v0.5 DOCX, state/acceptance và tài liệu3A/importer. Source Dev **17 nhóm/0 khác**;37 nội dung/47 revisions/74 alias;53 tài nguyên41 ảnh/12PDF đềuNotScanned;70 capture/68 SQL snapshot;8 ngoại lệ;2 video metadata/0 tệp. Identity/.env/SRS giữ nguyên. Mốc129/129 là lịch sử, không dùng thay full mới.

## Kết quả phân loại40 danh tính

| Phạm vi | Có sẵn không khác | Có sẵn có sai khác | Mới | Tổng |
|---|---:|---:|---:|---:|
| Dev dry-run đọc |34|3|3|40|
| Test preview ban đầu |34|3|3|40|

Hai tập đều giao37 nhưng các sai khác không giống nhau. Dev:3233 là markup/chuẩn hóa, dự thảo13 và5 là typed ngày kết thúc lịch sử so với chuỗi evidence nguồn. Test:3233 markup;3349 quan hệ chuyên mục của fixture metadata cũ;3678 có revision nguồn mang hậu tố QA từ regression trước. Đây là **sai khác cần đối chiếu**, không chứng minh ba bài vừa được nguồn cập nhật. Không fetch mới, không tự sửa dữ liệu Dev để ép đồng nhất.

Ba bài mới thật:3666 “Hội thảo lấy ý kiến về dự án Luật Đất đai (sửa đổi) tại thành phố Đà Nẵng”,3670 “Tháo gỡ khó khăn trong thực hiện chính sách, pháp luật đất đai tại thành phố Đà Nẵng”,3673 “Hội thảo lấy ý kiến về dự án Luật Đất đai (sửa đổi) tại Thành phố Hồ Chí Minh”. Không coi40 là40 bài mới.

Batch Test `f9ecfc09-e72d-4111-ab38-639fe7076384`: **3 content mới/3 revision nguồn public mới/7 MediaAsset sạch mới**,1 revision so sánh nguồn cho3678,6 Keep không tạo revision,30 Blocked. Tổng10 mục đủ điều kiện xử lý, không gọi10 là10 bài mới. Blocked có overlap lý do:53 tham chiếu tài nguyên chưa Clean,2 sai/không hỗ trợ định dạng,1 attachment chưa giải quyết,2 video metadata. PDF cache không được nâng Clean: **0 PDF mới nhập,0 video file**. Các fixture kỹ thuật Test có tên TECHNICAL TEST và run riêng, không cộng vào các số nguồn này.

Bảy ảnh dùng proof Microsoft Defender thật đã lưu ở3A, kiểm hash/định dạng lại trước copy và sau materialize; không nói vừa quét lại engine trong3B. Full mới có nhánh scanner Clean/EICAR thật riêng. HTTP header nguồn PNG nhưng bytes JPEG; MIME đích theo bytes.53 original không áp proof/đổi trạng thái.

## Quy trình và checkpoint

Offline Validated Source → Staging → Preview → Commit → Verify. Read checkpoint nén SHA đúng session, kiểm raw text/markup/metadata/required file list. Preview có diff và stamp CMS, không ghi DB. CLI chỉ Commit SQLTest/Test storage/Development; Dev chỉ Preview. Schema cũ đủ dùng, **migration NONE**, không đổi kiến trúc ASP.NET Core10/SQLServer/MVC.

Tái sử dụng SourceParser/HtmlPolicy/SqlImporter/source metadata/unique identity và alias; thêm nhánh `SqlImporter.Batch` riêng để không dùng policy auto-public của importer cũ cho cập nhật. New chỉ public trên Test sau file Clean. Existing chỉ tạo comparison revision, không chạm current/published/approved/working draft hoặc live taxonomy; Keep không thêm revision. Không bắt bổ sung thủ công trường nguồn thiếu.

Transaction Serializable/applock kiểm mã trả về, chống race giữa batch. ImportRuns.Id=BatchId, audit mỗi mục gắn plan SHA/identity/revision làm checkpoint bền vững. File immutable CreateNew/hash/fsync trước SQL; orphan giữ private nếu rollback. Không tự xóa nguồn hoặc reset DB. Có dừng sau2 mục commit, resume context/process mới và lặp cùng plan. Sau code cuối, repeat **10 AlreadyCommitted/30 Blocked,0 content/revision/asset mới**, verify **0 mismatch**. Verify đối chiếu raw, revision, title/sapo/tác giả/text/metadata/chuyên mục, alias và exact bytes. Fingerprint được bổ sung markup trong lúc phát triển; plan cũ đã có audit vẫn verify đúng fingerprint đã commit, preview mới phải dùng code hiện tại.

Rollback đã kiểm trên technical fixture: gỡ public của nội dung mới chưa bị sửa; giữ source/revision/file/audit; từ chối bài đã biên tập. Không chạy rollback trên ba bài thật hoặc Dev, chưa làm SQL restore drill, chưa process-kill đúng giữa response/materialize và SQL. Không dùng fault injection thay bằng chứng process kill.

## HTTP/UI và URL

Ba bài thật đã kiểm GET/HEAD media đúng SHA, ngày/tác giả, ảnh và chú thích, category, body, related, tìm kiếm có/không dấu, alias301 trực tiếp→bài200, guest private preview/file bị chặn. Kiểm public home/category/legal/draft và ba bài trên Edge390/1440px: **14 trang đạt**, không off-origin request/lỗi JS/ảnh lỗi. Ảnh và log: `.local/evidence/phase3b/phase3b-tests-20261019T024059Z-ea19272e/batch-public-http`. [Bài mobile](../.local/evidence/phase3b/phase3b-tests-20261019T024059Z-ea19272e/batch-public-http/ui/page-4-390.png), [bài desktop](../.local/evidence/phase3b/phase3b-tests-20261019T024059Z-ea19272e/batch-public-http/ui/page-4-1440.png). Không dùng hotlink để tuyên bố đã chuyển. Luồng document/PDF/draft còn được regression cũ kiểm riêng; **PDF mới chưa nhập vì NotScanned**, không lấy UI văn bản cũ thay bằng chứng nhập PDF mới.

85 URL alias đã chuẩn bị mapping tới gdla; **6 alias của3 bài mới** đã áp Test và HTTP301→200 trực tiếp. Mapping85 không đồng nghĩa85 redirect Production đã triển khai. Những alias còn lại giữ đề xuất/recheck trên target được duyệt; không tạo vòng/chuỗi/home redirect, không sửaDNS/domain thật.

## Kiểm thử và lỗi được giữ

Full sau sửa code/script cuối: **137/137 PASS,0 FAIL,exit0**, `phase3b-tests-20261019T024059Z-ea19272e`. Build Web/Worker/CLI/Tests0 warning/0 error, audit dependency theo log. Code/raw protected0 drift trong frozen run. HTTP/UI/worker/scanner là evidence con, không cộng thành full mới; focused và129 cũ không cộng.

Lịch sử: hai focused6/6 trước bổ sung HTTP; focused6/7 lỗi HEAD405; focused6/7 tiếp theo do inspector đợi h1 trên trang chủ không có h1, đã đợi main và kiểm ảnh lazy sau decode; focused7/7 trước full. Full đầu **131/136,5FAIL**:3 lỗi workflow chọn media Phase3BValidated (Save chỉ nhận Validated/SourceValidated),1 SCAN browser navigation timeout chưa rõ nguyên nhân,1 video fixture giả định một sweep50 phiên xử lý hết backlog Test. Full thứhai **136/137,1FAIL** ở thumbnail category: ảnh upload QA NotScanned được card tham chiếu trong khi endpoint strict từ chối. Sau sửa: focused **8/8** rồi full cuối ở trên. Không cộng counts hoặc ghi đè raw FAIL.

Đã tích hợp trạng thái batch vào workflow với Clean/hash và Development loopback; không áp proof Test vào Staging/Production. Lọc thumbnail bằng chính sách media trong SQL và giữ grant của revision; Source53NotScanned vẫn theo policy Development cũ, không ẩn nguồn để épUI. Có negative card fixture và tamper/editor test. Video chỉ sửa assumption trong test bằng sweep hữu hạn, production sweep không đổi. SCAN timeout cũ không tái hiện ở hai full sau, vẫn UNKNOWN, không tăng timeout và không gọi nó là cùng nguyên nhân SQL category.

Backup Test **5.499 tệp**, gồm791 inventory file; Dev922 tệp sau focused Test đạt, cùngSQL COPY_ONLY/CHECKSUM/VERIFYONLY và private ACL. Readback manifest0 khác; không restore. Backup Test lần đầu lỗi quyền ghi thư mục mới của SQL service, giữ thư mục dở; chỉ cấp ACL vào đúng backup project bằng flag có sẵn, không grant Identity/SQL Dev. Build ban đầu lỗi CS0103 vì wiring trước khi bổ sung lớp test đã được sửa; transcript và các raw FAIL được giữ, không thay số final.

## Bảo toàn và thay đổi

Sau full,17 nhóm Dev0 khác,37/53 NotScanned/70capture68snapshot/8 ngoại lệ/2metadata0file giữ nguyên. Identity/env/SRS không đổi; manifest3A lịch sử không sửa, chỉ code/docs delta được kê riêng. Source3A checkpoint/raw không ghi, không có GET nguồn mới. SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**; V04/D15/v05 artifact incident **PARTIAL**.

- `scripts/backup-project.ps1` (modified).
- `scripts/phase3b.ps1` (new).
- `scripts/test.ps1` (modified).
- `scripts/test-phase3b.ps1` (new).
- `src/Infrastructure/BatchMediaProof.cs` (new).
- `src/Infrastructure/MediaService.cs` (modified).
- `src/Infrastructure/OutboxReconciliationService.cs` (modified).
- `src/Infrastructure/WorkflowService.cs` (modified).
- `src/MigrationCli/BatchCommand.cs` (new).
- `src/MigrationCli/BatchModel.cs` (new).
- `src/MigrationCli/Program.cs` (modified).
- `src/MigrationCli/SourceBatch.cs` (new).
- `src/MigrationCli/SqlImporter.Batch.cs` (new).
- `src/MigrationCli/SqlImporter.cs` (modified).
- `src/Web/Controllers/AdminMediaController.cs` (modified).
- `src/Web/Controllers/MediaController.cs` (modified).
- `src/Web/Services/PublicContentQuery.cs` (modified).
- `tests/browser/batch-check.mjs` (new).
- `tests/LandPortal.Tests/BatchImportTests.cs` (new).
- `tests/LandPortal.Tests/BatchPublicHttpTests.cs` (new).
- `tests/LandPortal.Tests/Program.cs` (modified).
- `tests/LandPortal.Tests/TestEvidencePaths.cs` (modified).
- `tests/LandPortal.Tests/VideoTests.cs` (modified).

Các docs/AGENTS và summary/hash manifest riêng được cập nhật. Chi tiết SHA ở `.local/evidence/phase3b/phase3b-20261019T020204Z-5170f87b/changed-code.json`. Evidence task `.local/evidence/phase3b/phase3b-20261019T020204Z-5170f87b`, full `.local/evidence/phase3b/phase3b-tests-20261019T024059Z-ea19272e`, Dev plan `.local/evidence/phase3b/batch-20261019T021920Z-5e055ad2/plan.json`. File manifest cuối/readback nằm trong task; raw history vẫn nguyên.

## Còn thiếu và bước tiếp

APPLY DEV **NEEDS_APPROVAL**; công cụ cố ý không có write Dev. Cần duyệt3 bài mới, xử lý sai khác/30 Blocked, scanner/file policy và dry-run fresh sau bất kỳ thay đổi CMS nào; kiểm target/backup/17 nhóm trước Apply do người có thẩm quyền triển khai. Crawl rộng NEEDS_APPROVAL, nguồn512 URL/270 candidates/tổngUNKNOWN,83/81/cơ cấu/adapter còn thiếu. Không nhập NukeViet/gDLA hoặc mở host video. Production scanner/MFA/SMTP thật/restore/UAT/PO/AC16.1/lookup khóa/cutover giữ gate cũ. Website CHƯA PRODUCTION READY.

Lệnh chạy có trong [runbook](phase-3b-runbook.md). Test HTTP tự mở5299 rồi dừng tiến trình do run sở hữu; preview local có thể chạy5298 theo helper trong workspace, không public. Không dừng preview5198/5208/5288 hoặc process ngoài lượt này.

Preview mới đang chạy tại **http://127.0.0.1:5298/** (SQL Test), khởi động sau full. Health/trang chủ/ba bài thật đềuHTTP200,5 GET smoke riêng không cộng vào137. Bài3666: `http://127.0.0.1:5298/bai-viet/4f6daf9f-8013-4f6d-aaff-fdbc8c061032`. Test có fixture QA, không phải số liệu nguồn hoặc website chính thức.

Khóa bàn giao: `manifest-final.json`, `manifest-readback.json`, `preservation-final.json` trong task evidence; bản tham chiếu gọn ở `docs/evidence/phase3b/phase3b-20261019T020204Z-5170f87b/`. Không sửa các seal cũ để ép khớp. Apply Dev vẫn chưa được xác nhận.
