**Checkpoint mới Phase3H-Fix:** [báo cáo](phase-3h-fix-report.md)/[runbook mới](phase-3h-fix-runbook.md). Test20Keep/repeat/verify và15b13 đã đạt;13packet210 để duyệt. Old b01 superseded bằng aa52120d/41c7bf..., chưa ký/Apply. SQLrootUNKNOWN; các trạng tháiONHOLD dưới đây là lịch sử, không tự gọi rootFIXED.

# Phase 3H — chuẩn bị batch QLDD đã kiểm chứng

Đọc `phase-3h-report.md` và `.local/phase3h-current.json` trước khi chạy. **Checkpoint PARTIAL:12 packet để duyệt/EXECUTION ON HOLD; b13Devpreview và Testreplaypreflight bịtimeout, không tự retry.** Phạm vi lượt chuẩn bị: quét bản sao công khai trên SQL Test, preview/preflight Dev, tạo review **chưa ký**. Approval Phase 3G không áp dụng cho batch mở rộng. Không chạy lại helper vào report đã có. Chỉ dùng future dependency allowlist `apply-backup-dependencies-complete-v2.json`;v1 thiếu raw nonArticle không dùng.

## Dữ liệu và job quét

Danh tính phải có receipt `Phase3B.Import` của hai manifest Phase3F, thuộc checkpoint nguồn thật và revision nguồn đang công khai trên Test. Không dùng tổng hàng `Contents` làm số nội dung nguồn. Projected checkpoint giữ nguyên metadata/capture/request của parent; chỉ chọn trọn các item, tối đa20/job. Không sửa checkpoint gốc hoặc con trỏ crawl SQL.

`recovery-public-inventory` dùng scope `Phase3H.VerifiedPublicNetworkBatch.v1`: tối đa60 SHA/job, không chứa original Dev53 hoặc media Dev đang quarantine. Byte, format, capture Network, liên kết revision Test và hash được kiểm trước đăng ký. Mỗi job giữ giới hạn3 receipt/tài nguyên,180/job của pipeline cũ; không tạo job mới để che lỗi hoặc vượt retry budget. Legacy53+7 vẫn giữ guard riêng.

`scripts/phase3h.ps1` dùng build đã kiểm SHA, mutex Test, named database/storage. Các action Inventory/Scan chỉ Test; Dev chỉ Preview/Preflight/Reconcile. Wrapper không có action ký/Apply. Ví dụ đọc lại batch đầu bằng đường dẫn/hash từ packet:

```powershell
$h=Get-Content .local/phase3h-current.json -Raw|ConvertFrom-Json
$tool=Get-Content ($h.root+'/build-final-attestation.json') -Raw|ConvertFrom-Json
$packet=(Get-Content ($h.root+'/batches.json') -Raw|ConvertFrom-Json).batches | Where-Object label -EQ 'b01'
$deps=@{Cli=$tool.cli;CliSha=$tool.sha256;Checkpoint=$packet.checkpoint;CheckpointSha=$packet.checkpointSha;Inventory=$packet.inventory;InventorySha=$packet.inventorySha;ProofSet=$packet.proofSet;ProofSetSha=$packet.proofSetSha}
$out='.local/evidence/phase3f/phase3h-review-'+[Guid]::NewGuid().ToString('N')
rtk proxy powershell -NoProfile -File scripts/phase3h.ps1 -Action Preflight -Database LandPortal_Dev -ReportRoot ($out+'/preflight') @deps -Plan $packet.plan -PlanSha $packet.manifestSha256
```

Scan/resume dùng cùng Inventory/JobId với report mới trong `.local/evidence/phase3c/`; không ghi đè proof set cũ. Chỉ dùng exact SHA trong inventory. Giữ file và SQL audit khi thất bại; không sửa nhãn Clean. Proof phải có native Defender output digest, SQL receipt, TTL24h, current engine/definitions, definitions tuổi<72h. Definition drift làm proof hết hiệu lực ngay cả khi chưa hết TTL. Không StageTest/promote Dev53 trong mốc này.

## Duyệt và Apply trong lượt được cấp quyền sau

1. Chủ dự án xác nhận **BatchId + manifest SHA256 + target + delta** cụ thể trong bảng bàn giao. `PENDING_USER_APPROVAL` không phải approval. Review có `approvedBy=null` và không cấp quyền thực thi.
2. Kiểm tra lại certificate CurrentUser94797BD79E275B09DEF613036BC00DF4EA91345F, chain/EKU/hạn/pin và Windows operator. Không tạo/trust certificate mới; không dùng Dev signer cho Production. Dùng `scripts/sign-dev-import-approval.ps1` chỉ sau xác nhận thật; không suy xác nhận từ prompt chuẩn bị batch.
3. Fresh strict proof/manifest/target/schema/root và preflight. Nếu proof đổi, tạo packet/preview mới và xin xác nhận exact hash mới; không thay dependency dưới approval cũ. Nếu stamp đổi hoặc timeout, reconcile trước retry. Không tăng CommandTimeout.
4. Backup **Dev hiện tại40/60 hoặc trạng thái mới được xác minh**, không dùng backup baseline37 làm bộ backup trước Apply mới. Dùng cơ chế long-path của `backup-approved-batch.ps1`/`backup-file-snapshot.py`, thư mục mới, kiểm SQL COPY_ONLY/CHECKSUM/VERIFYONLY/header/SHA và hash/kích thước/số file. Kiểm đúng instance MinhAnh\SQLEXPRESS; stale registered THANGPD là rename đã xác minh, không sửa metadata. Xác minh `Sql__ExpectedDatabase=LandPortal_Dev` và database GUID.
5. Backup SQLTest có **receipt mới của job này**, cùng dependency allowlist: projected/parent checkpoint, raw cần thiết, inventory/proofset và proof JSON/byte liên quan, plan/review/approval, cấu hình không chứa secret. Snapshot Dev media/snapshot/key theo runbook; giữ ACL private. Backup Test cũ trước scan không bao gồm receipt mới. VERIFYONLY không phải SQL restore drill.
6. Chạy CLI đã hash-pin `safe-apply` với `--checkpoint/sha --inventory/sha --proof-set/sha --plan/sha --approval --backup-receipt` và report mới. Chỉ cấp process signer pin đã được phép; không sửa `.env`/quyền. Không gọi batch-commit thông thường lên Dev. Không copy database Test.
7. `batch-reconcile`, `safe-verify`/readback, idempotent replay theo cùng approval còn hợp lệ, đối chiếu expected delta và17 nhóm bảo toàn, Identity/env, SHA/date/author/categories/alias/publication, smoke bài/chuyên mục/media. Chỉ tiếp tục batch khác khi không mismatch. Không gọi APPLIED chỉ từ preview.

## Rollback và lỗi giữa chừng

COMMIT không chắc chắn phải reconcile BatchId/PlanSHA/receipt trước retry; giữ trace fingerprint/session/clientConnectionID/transaction stage. Không suy exception là rollback. Không tạo manifest thay thế batch đang dở.

Rollback có action riêng được ký: `safe-rollback` cùng manifest và dependency, backup fresh, report mới. Chỉ gỡ publication mới còn nguyên current/published revision/EditGeneration; editor đã sửa thì dừng xử lý thủ công. Giữ source/revision/asset/alias/audit và file; không xóa dữ liệu ngoài batch, không restore đè toàn Dev cho lỗi nhỏ. Kiểm media mất public grant sau gỡ. Đây là kế hoạch; chưa thực hiện rollback các batch Dev mở rộng trong lượt chuẩn bị.

## NukeViet theo SRS1.0

Hai nguồn QLDD/gdla đều thuộc phạm vi được user cho phép thu thập công khai. Không cần xin lại quyền nguồn chỉ vì thiếu file approval riêng. Pipeline hiện có `MigrationSourceIdentityPolicy`/`NukeVietBundle`: namespace `gdla.gov.vn/{vi/}news`, canonical/module/id phải khớp, selector `#news-bodyhtml`, raw và file hash, bundle private, chỉ đề nghị body-match để người duyệt. Không merge vì title/id giống QLDD.

Mapping cần hồ sơ nguồn thật: news/category→chuyên mục đích; document module→văn bản/dự thảo theo schema thật; tác giả/ngày đăng; file→quarantine/native proof; URL cũ→alias đích sau duyệt. News parser đã có không chứng minh document adapter hoặc SQL write adapter NukeViet đã nghiệm thu. Chưa có mẫu/export thật đủ bằng chứng thì giữ pending mapping/adapter; fixture chỉ kỹ thuật. Không ghi website NukeViet đang vận hành hoặc crawl lại diện rộng trong lượt này. Blocker NukeViet không chặn các batch QLDD độc lập.

SQL timeout UNKNOWN/MONITORING, incident artifact lịch sử PARTIAL, scanner Production/MFA/restore/UAT/cutover vẫn riêng. **CHƯA PRODUCTION READY.**
