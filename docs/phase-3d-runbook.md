# Phase 3D — vận hành giới hạn

SRS chính thức: `SRS_CTTDT-Cuc-QLDD_v1.0.docx`, ngày19/10/2026; bản trích đầy đủ380 đoạn và SHA nằm trong run Phase3D. Chỉ hai nguồn QLDD/SharePoint và gdla/NukeViet. Không dùng quyết định này thay approval crawl rộng, chứng chỉ người ký hoặc quyền Apply Dev.

## Tiếp tục và kiểm tra offline

Checkpoint `.local/phase3d-current.json`; run `phase3d-20261019T044027Z-5990f061`. Không chạy helper vào thư mục đã tồn tại. Giữ checkpoint3A SHA `d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3`, không sửa raw/checkpoint để loại file lỗi.

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-phase3d.ps1
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3d.ps1 -Action Audit -ReportRoot .local/evidence/phase3d/AUDIT_UNIQUE_RUN_ID -Checkpoint .local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3.bin -CheckpointSha d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3
```

Audit không truy cập mạng và không ghi SQL. JSON/CSV từng270 khóa ứng viên, ledger512URL; 40 verified,228 chưa có snapshot chi tiết,2 không bóc tách được. Chưa tìm thêm bài đủ điều kiện. Trang/chuyên mục81/83 chưa thấy hàng không có nghĩa là không có bài. Ưu tiên xác minh các nhóm chưa có bài76/77/81/83 và các nhóm còn nhiều ứng viên75/48/49/51; số theo chuyên mục giao nhau, không cộng thành tổng bài.

Không phát hiện lỗi phân trang có đủ bằng chứng để sửa trong lượt này. Hai snapshot lỗi: DraftDocument3 thiếu tiêu đề; candidateDraftDocument2024 có canonical bài tin nhưng tiêu đề nguồn rỗng. Không lấy menu/sidebar làm bài hoặc tự suy tiêu đề.

## Mở rộng nguồn có duyệt

Hiện chưa có hồ sơ duyệt rộng. Sau khi người phụ trách cung cấp hồ sơ thật về URL/host, người duyệt, thời hạn, request/page/content/asset/byte và tốc độ, dùng lại session3A. Không tạo session khác để đặt lại39/40 GET. Ví dụ lệnh dự kiến, **chưa chạy**, chỉ sau review hồ sơ:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/phase3a.ps1 -Action Resume -RunId 9d95c26f-85a5-4a8a-a2df-61def03cf360 -ApprovalFile .local/phase3a/approvals/HO_SO_THAT_DA_DUYET.json -Requests 100 -Pages 80 -Contents 80 -Assets 100 -Seconds 600 -MiB 256
```

Một kết nối, nghỉ>=2.05giây+jitter, tôn trọng robots/retry/cutover. Kiểm mức giới hạn với hồ sơ thực trước khi chạy; lệnh ví dụ không phải phê duyệt. Có thể nhận export SharePoint chính thức gồm ID/list/canonical/loại, ngày và múi giờ, tác giả/nguồn, taxonomy, HTML, tệp gốc cùng quan hệ/hash, bản ghi xóa và thời điểm xuất. Giữ bản xuất private và kiểm tra nguồn/chống trùng/quét trước staging; không đưa backup nguồn vào database đang chạy.

## NukeViet

`MigrationSourceIdentityPolicy` phân biệt `Source/Module/Kind/LocalId`; SQLSourceId dự kiến chứa module/ngôn ngữ. NumericID/tiêu đề giống QLDD không hợp nhất tự động. Parser offline chỉ nhận route news đã hỗ trợ, canonical cùng nguồn/module/id, `#news-bodyhtml`, tiêu đề và body không rỗng. CLI `nukeviet-preview` kiểm rawSHA, giới hạn8MiB, lưu metadata private và gợi ý so sánh body. Gợi ý trùng body cần người duyệt; trùng tiêu đề chỉ là tín hiệu, không phải cùng bài.

Chưa có snapshot/export NukeViet thật được phép dùng; kiểm thử bằng fixture có nhãn kỹ thuật, không tính chuyển nguồn. Chưa có adapter SQL write NukeViet. Bảng đối chiếu cần đơn vị nghiệp vụ cung cấp/duyệt các cột `Source,Language,Module,SourceCategoryId,SourceCategoryName,TargetPrimaryCategoryId,SecondaryCategoryIds,Evidence,ApprovedBy,ApprovedUtc`. Chưa có hàng nghiệp vụ để điền, không bịa danh mục. Không sửa allowlist crawlerQLDD để tự thu thập NukeViet. Video41 trỏ mộtMP4 gdla đã biết nhưng chưa tải; cần phạm vi lấy tệp cụ thể/tốc độ/dung lượng và đối chiếu đúng video trước dùng pipeline2D.

## Apply Dev có thẩm quyền

Đọc `dev-apply-manifest.json` và `proposed-dev-apply-command.txt` trong run. Plan schema3 SHA `08ea3bcae6385c023ed9a2c8c1ceb6a2f8c4ef78ca5db50eb18dc29018b58e93` ràng buộc database, exact media-rootSHA, inventory/raw/proof/reviewSHA, SourceIdentity và stamp CMS. Schema1/2 vẫn đọc để đối chiếu lịch sử/Test; **Dev authorization mới yêu cầu schema3**. Nếu đổi kho media phải preview và duyệt lại, không sửa plan cũ.

Gói trình duyệt:3 bài mới/7 ảnh/1 comparison/6Keep/20mapping(6alias mới);30 dòng còn chặn không được nhập. Expected delta +3Content/+3SourceRecord/+4Revision/+7MediaAsset/+6Alias. Không đổi public/editor bài cũ; không đổi53 trạng thái Dev. Bản fresh preview và hash là đề nghị, **không phải quyền chạy**.

Người có thẩm quyền phải chọn signer và Windows operator. Approval là file `.local/approvals/phase3c/*.ps1` ký Authenticode, chỉ đọc/không execute. Giữ protocol đã Test: dòng `# LANDPORTAL_PHASE3C_APPROVAL ` + metadata ApprovalId/PlanSha256/TargetDatabase/OperatorSid/ExpiresUtc/Actions. Cert phải có Windows chain Valid và thumbprint trong cấu hình `SafeImport:ApprovedSignerThumbprints` đã được đơn vị duyệt; không tự tạo cert/ký/pin/trust. Approval<=7ngày, đúng operator và actionApply/Rollback. Đây không phải yêu cầu mọi PDF nguồn phải có chữ ký số.

Operator hiện tại có quyền SQL local db_owner/sysadmin theo readback; quyền kỹ thuật không thay chữ ký phê duyệt. Positive signed path và ApplyDev chưa chạy. Không sửa.env hoặc cấp quyền để mở khóa.

Trước Apply: giải quyết SQL timeout, freshpreview/stamp, còn hạn proof24h/cùng engine/definitions, backupDev<=12h/COPY_ONLY/CHECKSUM/VERIFYONLY/header đúngDB/hash file đọc lại. Dùng `backup-project.ps1` theo [runbook3C](phase-3c-runbook.md); helper hiện nhận evidencephase3C, có thể tạo thư mục approval mới3C và tham chiếu từ3D, không ghi vào run cũ.

Backup gồm SQL/media/snapshots/keys/config/inventory và dependency65file trong manifest3D. Proof recovery còn cần SQLTest receipts và `.local/tests/phase3c/{JobId}/proofs`: backup thường không tự enlist job này. Thiếu proof/receipt thì fail-closed, quét lại đúng phạm vi; không tạo lại raw hoặc giả receipt. Không sao chép private files ra SaaS. VERIFYONLY chưa chứng minh restore.

Sau Apply được duyệt: SafeVerify/newReportRoot, đối chiếu expected delta và17 nhóm có ngoại lệ được duyệt. SafeRollback cần actionRollback và backup fresh; chỉ gỡ publication mới chưa bị editor sửa, giữ source/revisions/aliases/files/audit, dừng khi stale/conflict. Không xóa dữ liệu/reset/restore tự động hoặc chạy lại Commit để đăng lại bài đã rollback.

## Chuyển giao hạ tầng

| Hạng mục | Trạng thái thực |
|---|---|
|Code/Test import, rollback, hash, scanner, ACL|READY trong các nhóm tập trung có raw; không thay UAT|
|Hai nguồn và tên FR17group2|Quyết định nghiệp vụ đã chốt; group2 NameApproved Test; Dev/menu chưa Apply|
|QLDD verified40/Test37 đủ điều kiện|PARTIAL tổng nguồn;3 blocker riêng|
|Dev batch|NEEDS_APPROVAL; cần tiền kiểm fresh sau SQL timeout|
|301 hai nguồn|READY phạm vi code/HTTP fixture; AC05.1 thật50URL hai nguồn NOT RUN|
|Hostname/HTTPS|NEEDS_APPROVAL/BLOCKED hạ tầng; dự kiến canonical https://gdla.gov.vn, QLDD301 đúng từngURL; chưa đổiDNS|
|Web targethost|BLOCKED cấu hình: Program hiện chỉ bindloopback/AllowedHosts local; chưa cấu hình reverseproxy/HTTPS/certificate/forwardedheaders cho target|
|Worker targethost|NOT RUN; cần account riêng/quyền SQL+private media+keys/scanner/FFmpeg cùngpolicy, supervisor được duyệt; không cài service|
|Quyền Dev|NEEDS_APPROVAL:10 quyền vận hành2B và4catalogv05 trong sổ bàn giao; Identity giữ nguyên, không auto-grant|
|Production scanner/MFA/SMTP|BLOCKED/NOT RUN; DefenderTest publicSHA không phải scannerPII/Production; không email thật|
|Restore/rollback target/UAT|NOT RUN; cần diễn tập có thẩm quyền và dữ liệu nghiệp vụ,3Word thật/AC16.1 vẫn BLOCKED|
|SQL category timeout|MONITORING / ROOT CAUSE UNKNOWN; thêm timeoutTest3D, không tăng timeout/đổi cấu hìnhSQL|
|Private inquiry lookup|Giữ khóa; cần phương án xác minh sở hữu ATTT chấp thuận, mã+email/CAPTCHA không đủ|

Target domain và CSDL riêng đã chốt về nghiệp vụ, chưa chứng minh máy chủ Bộ chạy Web/Worker. Không bật nonloopback, HTTPS hoặc service chỉ để đánh dấu READY. CHƯA PRODUCTION READY.
