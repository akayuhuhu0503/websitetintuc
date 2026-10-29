# Phase 3C — Media Recovery & Safe Dev Import, 19/10/2026

**PASS LOCAL phạm vi Test có bằng chứng; tổng thể PARTIAL. Dev chưa Apply, chưa đủ điều kiện vận hành. CHƯA PRODUCTION READY.** Run `.local/evidence/phase3c/phase3c-20261019T033700Z-136775a5/`; [runbook](phase-3c-runbook.md).

## Kết quả

| Câu hỏi | Kết quả thực tế |
|---|---|
| Tài nguyên kiểm tra/quét thật | **60**:53 bản sao nguồn Test +7 ảnh công khai làm mới proof cũ.60 Clean bằng Defender thật; không dùng nhãn Clean cũ làm bằng chứng mới. |
| Giải phóng trong30 danh tính | **27/30 hoàn toàn đủ điều kiện trên Test**; điều kiện scanner của29/29 được xử lý nhưng2 video vẫn thiếu tệp. Dev vẫn30 blocked vì53 gốc chưa được duyệt quét/áp dụng. |
| Nội dung nhập thêm Test | **0 content mới,0 asset nguồn mới** vì30 danh tính đều đã có CMS ID. Xử lý37 dòng đủ điều kiện toàn plan:35 Keep +2 source comparison; tạo thực tế **2 revision so sánh** cho3349/3678, không đổi public/editor payload của40 nguồn. |
| PDF/video | PDF3233 có ứng viên phục hồi private nhưng chưa xác minh chữ ký; vẫn BLOCKED. Video40 thiếu file đúngID;41 directMP4 host gdla chưa được phép.0 video file/0 chuyển đổi. |
| Apply/verify/rollback Test | **PASS LOCAL**:backup thật, SQL transaction, fault sau materialize, orphan private, retry/idempotence, verify và gỡ publication không xóa source/revision/file. |
| Dev READY? | **PREPARED / BLOCKED_EXTERNAL_APPROVAL_POLICY**:đã có công cụ và manifest/lệnh dự kiến; thiếu chính sách người ký/cert được duyệt và approval ký đúng batch; positive signed path NOT RUN. Không Apply Dev. |
| Cần người phụ trách | Phê duyệt người ký/trust policy/Windows operator; batch và backup fresh trước Apply; chính sách scan/áp dụng53 Dev; xác minh PDF/chữ ký hoặc cung cấp bản nguồn hợp lệ; xác nhận file đúngvideo40 và quyền gdla/video41. |

Không gọi27 là27 bài mới. Không gọi Test53 Clean là Dev53 đã được quét. Fixture kỹ thuật SQL/PNG/EICAR không tính dữ liệu nguồn.

## Scanner và proof

Tái sử dụng `DefenderInquiryScanner` đang có, Microsoft Authenticode/engine active/definitions≤72h, allowlist exact SHA public/anonymous, Development + SQL Test + Test storage. Engine thật **1.1.26090.9**, definitions **1.461.96.0**, updated **2026-10-18T18:29:12Z**. Không đổi AV policy, cài service, firewall hoặc đưa tệp công dân riêng tư vào scanner.

Pipeline mới Inventory → VerifyHash → private quarantine → Scan → VerifyResult → StageTest → Report. SQL append-only audit giữ proof hash/job; proof JSON fsync trước SQL receipt. Job ràng buộc manifest SHA, resource hash/bytes, policy `Phase3C.PublicAnonymousExactBytes.DefenderTest.Reuse24h.Definitions72h.v1`, engine/definitions và TTL24h. Bản proof không có SQL receipt, sai policy/bytes/engine hoặc hết hạn không được dùng. Giới hạn3 receipt/asset/job, Infected không retry tự động. Một worker/job dùng SQL session applock. Không khẳng định đã thử mọi race hoặc kill giữa mọi bước.

Chạy2 tệp rồi dừng có chủ đích; process khác resume cùngJobId tái sử dụng2 proof và quét58 tệp còn lại. Final focused gọi lại reuse60/60, **0 receipt scan mới**. Quarantine và proof đặt dưới `.local/tests/phase3c/{JobId}`. Checkpoint3A `d41be...18b3` giữ nguyên. Bước register ràng buộc inventory được bổ sung sau native scan đầu: đã tái dựng/đối chiếu chính xác manifest với checkpoint và source-before thật rồi ghi receipt hiện tại; không tạo lại raw/native result lịch sử.

`StageTest` áp dụng có audit vào **53 mirror SQL Test** sau backup/đối chiếu bytes, chuyển Clean + validation exact-byte hiện có;7 ảnh đã Clean chỉ được làm mới proof. Đây là phạm vi Test được prompt3C mới cho phép, supersede quy tắc giữ Test53 NotScanned trước đó. **53 Dev vẫn NotScanned**, bytes/nội dung nguồn không thay. Overlay chỉ trong RAM, không sửa trạng thái checkpoint3A. Khi targetDev, overlay loại bỏ cả53 protected proofs; chỉ7 ảnh mới được xét. Không có lệnh StageDev.

## PDF và video

`pdf-video-recovery.json` lưu raw SHA, thời gian, URL và phân tích. PDF Article3233 SHA `6b6064ed8725c5105dd5baa905a3d8d899578fc5d5bd3072bd274eeb7fe5b308`,2.919.460 bytes. Header ở offset **21**, đính chính con số20 của báo cáo3B; không sửa artifact cũ. Các snapshot/cache đã lưu cùng SHA lỗi, chưa có bản đối chứng hợp lệ độc lập.

Ứng viên bỏ21 byte tiền tố có SHA `f27fc2f1c00ef72f77fde2b54cf3c42365edf6d3f987e9fdd8e87b2b0f9e1c08`,3 trang, reader không cần repair, render hash3/3 giống bản đọc có repair. Nhưng có `/ByteRange`; chữ ký mật mã **chưa xác minh**. Giữ ứng viên private/NotScanned, không nhập asset, không public, không tự coi cùng tính xác thực pháp lý. Bản raw/original giữ nguyên.0 GET nguồn mới; không trim rồi ghi đè file gốc.

Video41 có direct URL `http://gdla.gov.vn/uploads/videoclips/video/90_NGAY_DAT_DAI_V5_full.mp4` trong đúngmetadata ID41; host ngoài allowlist/quyền nhập, không fetch. Video40 có `ContentNews` rỗng cho đúngID40. Template của trang40 có link YouTube `12v32i0E3ac`, nhưng metadata gán link đó cho **ID41**, không chứng minh file thuộc40. Không lấy video khác/Youtube thay cho40; snapshot41 có directMP4 khác nên cần đối chiếu nguồn. Chưa có bằng chứng playlist/HLS đúngID; không kết luận URL đã chết hoặc nguồn hiện không còn chỉ từ snapshot. Pipeline2D giữ nguyên và NOT RUN vì không có tệp hợp lệ được phép.

## Apply/rollback

`safe-apply/safe-verify/safe-rollback` dùng lại SourceBatch/SqlImporter, không xây importer khác. Preview/manifest/hash/stamp chống stale, unique SourceIdentity/alias/hash, serializable per-item + SQL applock. File fsync/hash trong private storage trước SQL commit; audit theo item phục vụ resume. SQL rollback không xóa file: lỗi sau materialize để orphan private, lần sau xác minh và dùng lại. Filesystem không được gọi là transaction SQL.

SafeApply yêu cầu local đúngSQL/database, quyền Windows SQL operator hiện có (db_owner/sysadmin), backup≤12h với SHA `.bak` và SQL HEADER đúng target; không cấp quyền. Test có confirm exact planSHA. Dev yêu cầu approval Authenticode từ signer đã được tổ chức duyệt/pin, đúng planSHA/target/operatorSID/action/expiry. Approval chỉ đọc, không execute; giữ handle chống thay file trong khi xác minh. Không có approve command tự tạo trust/token. `.env`/trust store/Identity không thay.

Rollback chỉ gỡ3 publication mới thuộc batch nếu chưa bị editor sửa và pointers/revision/generation còn khớp; dừng khi có xung đột. Giữ source/revisions/assets/audit/aliases; không xóa dữ liệu cũ hoặc file dùng chung, không reset/restore DB. Alias trỏ bài bị gỡ sẽ không làm bài công khai trở lại. Positive kýDev, restore drill và kill ở mọi điểm NOT RUN.

## Kiểm thử sau sửa code cuối

**6/6 PASS,0 FAIL**, run `phase3c-tests-20261019T035806Z-8c569e1c`,17 code paths/0 drift. Build CLI/Web/Tests và dependencies0 warning/error, không đổi package; Worker/video/full regression NOT RUN. Lượt6/6 trước sửa file-lock/fault fixture giữ riêng, không cộng12/12. Build trung gian có6 cảnh báo platform đã được xử lý bằng Windows guard; build cuối0 cảnh báo.

- Native anonymous controls:1 PNG Clean, EICAR ZIP Infected thật; truy vấn health thực bị hủy50ms trả ScanFailed, không mock Clean. Đây không phải cố ý gây lỗi engine toàn máy. Nguồn60 không có Infected.
- Resume/cache/policy binding:60 reuse,0 receipt scan mới; policy sai bị từ chối.
- SafeApply Test dùng backup thật, verify, repeat và rollback hai lần. Fixture ảnh vô danh được scan thật trước khi dùng; fault sau materialize rollback SQL nhưng file private còn; retry materialize một lần; rollback giữ file/revision, HTTP guest không tải được file Clean đã mất grant. Không tính fixture thành nguồn.
- SQL fault rollback, source/editor/public isolation, stale CMS stamp được kiểm tra đúng vùng.
- HTTP thật60 media nguồn/60 grant public đúng SHA,12 PDF Range206; NotScanned và guest admin preview bị chặn; Clean fixture sau rollback404. Smoke bài3349/3681/231/1780 và chuyên mục. Owned5303 đã dừng. Không chụp UI hàng loạt.

Batch nguồn Test thực dùngplan SHA `fe4ded6337f7cfac9c227b51c19857dcb32bea4b07be438d14e4f719234e6214`:35Keep/2Comparison/3Blocked. Verify cuối0 lỗi; repeat **37 AlreadyCommitted/3 Blocked**,0 khác counts/source/revisions/assets/audit/files.2 revision source mới là comparison,40 public/editor payload giữ nguyên;0 asset nguồn mới vì dùng lại60 tệp có sẵn.

## Bảo toàn và checkpoint

Dev trước/sau **0 khác/17 nhóm**:37 content/47 revision/74 alias,53 asset41ảnh/12PDF NotScanned,70capture/68snapshot,8 ngoại lệ,2video metadata/0 tệp. Identity/env nguyên. Test40 nguồn không đổi content payload hoặc filehash; riêng53 scan state thay đổi được phép và2 comparison append. Backup Test `LandPortal_Test-20261019T034031Z`:2931file/791inventory, COPY_ONLY/CHECKSUM/VERIFYONLY + readback0 khác; restore NOT RUN.

Không migration. File code thay đổi: `src/Infrastructure/DefenderInquiryScanner.cs`; MigrationCli `MediaRecovery.cs`, `RecoveryCommand.cs`, `RecoveryEvidence.cs`, `ApprovedBatchExecution.cs`, `SafeBatchCommand.cs` mới, và `BatchModel.cs`/`SourceBatch.cs`/`BatchCommand.cs`/`Program.cs`; `scripts/phase3c.ps1`/`test-phase3c.ps1` mới, allowlist evidence của `backup-project.ps1`; `tests/LandPortal.Tests/MediaRecoveryTests.cs` mới và runner/fixture/evidence paths. Không sửa Web/MVC workflow, CMS/video implementation hoặc package. Backup chỉ dùng ACL thư mục backup Test riêng theo script có sẵn, không grant SQL/Identity/Dev.

Manifest Dev `dev-apply-manifest.json`, SHA **4d081c4654ded6abb521501c2868165d423571959a61c10c0bf1366a0173a13f**:3 new/7 ảnh fresh/1 comparison/6Keep/20mapping(6alias mới), kèm53 protected media đề nghị review **không ápDev**. Dev plan SHA **27f308ece2e5a762b549ce56f762ba1c3a1e938d98c22ee9135d08b002303dc3**. Lệnh Apply dự kiến trong `proposed-dev-apply-command.txt`; chưa chạy, cần approval/backup/thời hạn proof còn hiệu lực. Xem [runbook](phase-3c-runbook.md) trước khi xem xét.

Preview code mới **http://127.0.0.1:5304/**, SQL Test/MailDisabled/strict media. Health200 và2 alias3349 trả301→bài200, log riêng `preview-smoke.json`, không cộng6/6. Chạy `rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File .local/phase3c-preview-web.ps1` khi port rảnh. Các preview cũ giữ nguyên.

SQL timeout **MONITORING / ROOT CAUSE UNKNOWN**; artifact V04/D15/v05 vẫn PARTIAL. Chưa có Production scanner/privacy/MFA/SMTP/restore/UAT/cutover/full nguồn. Không Dev apply/grant/.env/AV-policy/Git/deploy/DNS/email/full-crawl. Khóa evidence mới chỉ hash code/phạm vi liên quan, không tạo lại hàng nghìn hash toàn repo.

Seal mới `manifest-final.json`/`manifest-readback.json` trong task; tham chiếu [summary](evidence/phase3c/phase3c-20261019T033700Z-136775a5/summary.json) và [manifest](evidence/phase3c/phase3c-20261019T033700Z-136775a5/manifest-reference.json). Seal cũ/raw không ghi đè hoặc dựng lại.
