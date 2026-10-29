# Backup ngắn và resume exact batch

[Kết quả thực tế](phase-3g-backup-resume-report.md): BACKUP PASS, Apply dừng ở native signature guard. Không chạy lại Apply với binary hiện hành khi chưa xử lý lỗi progress/stderr và kiểm chứng. Không execute file approval PS1.

## Phạm vi backup và vị trí

`scripts/backup-approved-batch.ps1` chỉ dùng local Development, SQL project Dev/Test. Tham số `BackupRoot` là thư mục **đã tồn tại**, private, đã có quyền SQL service, dưới `.local/backups`. Không cấp quyền nếu thiếu; không tự tạo `C:\LPBackups` hoặc nới guard vốn không chấp nhận external backup. `DirectoryId` là tên mới ngắn1–12ký tự; không reuse. Nội dung nguồn giữ nguyên relative names/cấu trúc. Python extended paths không cần bật policy Windows.

`d/`: SQLDev + media/snapshots/keys/project.env + file-manifest. `t/`: SQLTest chứa recovery job/proof receipts. `p/`: exact dependencies, original paths trong dependency-manifest. `r/`: file restore rehearsal riêng. `RESTORE.md`: hướng dẫn phục hồi, không restore đè database hoặc active storage tự động.

Ví dụ dưới đây **chỉ tạo backup mới**, không ký hoặc Apply. Phải xác minh root/hash/source/approval còn đúng trước chạy, cùng account Dev đã được phép:

```powershell
. ./.local/phase3g-resume-context.ps1
    $packet='.local/evidence/phase3f/phase3g-stabilize-20261021T021233Z-20e31fbb'
    $allow=$packet+'/dependency-allowlist.json'
    $approval='.local/approvals/phase3c/80ed3e4c-f2e9-4814-bc18-42f66c07f02f.ps1'
    $report='.local/evidence/phase3f/backup-'+[Guid]::NewGuid().ToString('N')
    # The local context above loads the exact verified Development profile/roots.
    # Never inherit Test roots for the Dev snapshot.
    & ./scripts/backup-approved-batch.ps1 `
      -BackupRoot '.local/backups/LandPortal_Test-20261021T015310Z' `
      -DirectoryId ('g'+[Guid]::NewGuid().ToString('N').Substring(0,8)) `
      -EvidenceDirectory $report -DependencyManifest $allow `
      -DependencyManifestSha (Get-SigningSha $allow) `
      -AdditionalFiles @($approval,($approval+'.receipt.json'))
```

Không sửa allowlist để chấp nhận bytes khác. Receipt backup chỉ phát hành cuối sau khi SQL/copy/readback/dependencies đều đạt; lỗi giữ partial, STOP. Backup có secrets/private keys, không log nội dung, không đưa Git. Backup mới21/10 ở `.../.local/backups/LandPortal_Test-20261021T015310Z/gaae71ea6/`; không ghi đè.

## Kiểm thử tập trung và readback

```powershell
rtk proxy python scripts/test-backup-file-snapshot.py
rtk proxy python scripts/backup-file-snapshot.py verify --manifest .local/backups/LandPortal_Test-20261021T015310Z/gaae71ea6/d/file-manifest.json --verify-sources --report .local/evidence/phase3f/NEW_UNIQUE_READBACK.json
```

CLI report dùng CreateNew, phải đổi tên mỗi lần. `verify-sources` so lại inventory nguồn, không chỉ kiểm tra những file đã copy. Directory đọc lỗi phải raise, không omit. Restore-file probe là bản sao private riêng; SQLVERIFYONLY không phải SQLrestore drill. Không restore lên Dev hoặc Test đang có dữ liệu để tăng bằng chứng.

## Approval, preflight và Apply

Vị trí backup không nằm trong metadata ký; plan/hash/target/SID/actions/expiry vẫn phải đúng. Approval hiện26af.../a6b... actionApply chỉ3content/4revision/7JPEG/6alias, expiry21/10/2026 21:54:56VN. Không tự gia hạn/ký lại hoặc mở rộng. Thay proof-set sẽ thay plan binding: cần packet mới và exact owner confirmation, không thay ngầm. Giữ strict Defender/current definitions/24h proof/72h definitions guard.

Dùng cấu hình process và `$deps` trong [runbook packet](phase-3g-stabilization-runbook.md). Lệnh sau **chưa được retry trong run này**; chỉ dùng khi mọi guard đã được kiểm chứng lại và native signature bridge đã xử lý đúng:

```powershell
# Existing process profile/deps/cli/approval already verified; NEW unique report directories.
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/before-apply') @deps
# Stop on any nonzero exit / changed stamps / proof / target / signature.
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/apply') @deps --approval $approval --backup-receipt $completeDevBackupReceipt
rtk proxy $script:DotnetPath $cli safe-verify --report ($out+'/verify') @deps
```

Nếu Apply lỗi/ack không rõ, chỉ `batch-reconcile` đọc trước, không blind retry. Signed actionApply không tự cấp actionRollback; rollback phải đúng protocol/phạm vi. Khi commit và verify thực sự đạt mới repeat same batch để đo0delta/AlreadyCommitted và smoke trang/ảnh. So riêng37bản cũ và expected3/4/7/6; không báo full17rawhashgiống khi đã có delta hợp lệ.

Native guard issue hiện tại: Get-AuthenticodeSignature trảValid/đúngthumbprint nhưng child ghi progressCLIXML vào stderr; code từ chối any stderr. Không tắt check chữ ký/pin/errors để né. Bước sửa hẹp dự kiến là kiểm soát **progress presentation trong child**, vẫn từ chối errors/nonzero/invalid signature/scope/hash/pin. Probe chỉ đọc đã tái hiện; ứng dụng chưa sửa và chưa retry Apply. Không liên hệ hoặc thay trạng thái rootcause của SQLtimeout lịch sử vì đây là lỗi guard khác.
