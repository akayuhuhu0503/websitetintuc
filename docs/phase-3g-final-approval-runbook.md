# Lệnh cho exact batch 14849e55 — chỉ sau xác nhận của chủ dự án

**HOLD21/10:** [checkpoint tracking PARTIAL](phase-3g-tracking-fix-report.md). Người dùng yêu cầu sửa nhãn trước ký, chưa duyệt batch. CLI mới `.local/build/phase3g/tracking-current`, scopedSQLtests/preflight timeout; proof112 không khớp live118. Không chạy phần ký/Apply dưới đây khi còn blocked; dependency/binary/plan/proof phải được kiểm lại với packet mới, không tự thay SHA dưới approval cũ.

Các lệnh ký/backup/Apply bên dưới **chưa chạy**. [Báo cáo và 7 hash ảnh](phase-3g-final-approval-report.md). Manifest canonical = plan JSON; không nhầm với SHA của file báo cáo chi tiết. Không tạo preview mới rồi dùng approval cũ cho hash khác.

## 1. Nạp đúng operator/Dev và dependency cố định

PowerShell mới tại root dự án, cùng CurrentUser đã trust certificate. Được người dùng cho phép cấu hình pin **chỉ trong process Development**. Không sửa `.env`, User/Machine environment, trust toàn máy hoặc quyền SQL/Identity.

```powershell
$ErrorActionPreference='Stop'
. ./scripts/dev-signing-common.ps1
Assert-DevSigningScope Development
$run='.local/evidence/phase3f/phase3g-final-20261020T031249Z-93f55984'
$plan=$run+'/dev-preview/plan.json'
$planSha='bad4234f01ff503762c966ccf675cdc9168ca945e5753a8eaa1b21ab85bcbafe'
$review='.local/signing/reviews/phase3g-final-20261020T031249Z-93f55984/review.json'
$reviewSha='79a2916cfd168b7029e5c31e0688aada9527ae35bead3f6257179f9ea3e3d708'
$thumb='94797BD79E275B09DEF613036BC00DF4EA91345F'
if((Get-SigningSha $plan) -cne $planSha -or (Get-SigningSha $review) -cne $reviewSha){throw 'Reviewed bytes changed'}
$b=Get-ProjectConnectionBuilder LandPortal_Dev
$env:ConnectionStrings__Portal=$b.ConnectionString
$env:Sql__ExpectedDatabase='LandPortal_Dev'
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Mail__Transport='Disabled';$env:Media__AllowUnscannedDevelopmentUploads='false'
$env:SafeImport__ApprovedSignerThumbprints=$thumb
foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),(Join-Path $script:ProjectRoot ('.local/'+$pair[1])),'Process')}
$cpSha='d41be99393dcd27549887d46294b7fa03db36c033dc0dfd683d69482cd6a18b3'
$cp='.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/'+$cpSha+'.bin'
$inventory='.local/evidence/phase3c/phase3c-20261019T033700Z-136775a5/inventory/inventory.json'
$invSha='5a5ae635bddb6a3de9c7b3fbea2a75441ad3bc375684cd36a47d755bb0a44b0e'
$proof='.local/evidence/phase3c/phase3g-final-20261020T031249Z-93f55984/scan/proof-set.json'
$proofSha='4d1ca5679f6f20da13bc6d885032485b62bc54759ac341f5c5033d590f7a52d3'
$deps=@('--checkpoint',$cp,'--checkpoint-sha',$cpSha,'--inventory',$inventory,'--inventory-sha',$invSha,'--proof-set',$proof,'--proof-set-sha',$proofSha,'--plan',$plan,'--plan-sha',$planSha)
$cli='.local/build/phase3f/current/bin/MigrationCli/debug/MigrationCli.dll'
$label='phase3g-execute-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$out='.local/evidence/phase3f/'+$label
$connection=New-Object System.Data.SqlClient.SqlConnection $b.ConnectionString
$connection.Open()
try {
 $q=$connection.CreateCommand()
 $q.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)),CAST(SERVERPROPERTY('InstanceName') AS nvarchar(128)),CONVERT(nvarchar(50),database_guid) FROM sys.database_recovery_status WHERE database_id=DB_ID()"
 $rd=$q.ExecuteReader()
 try {
  if(-not $rd.Read() -or $rd.GetString(0) -cne 'LandPortal_Dev' -or $rd.GetString(1) -ine 'MinhAnh\SQLEXPRESS' -or $rd.GetString(2) -ine 'MinhAnh' -or $rd.GetString(3) -ine 'SQLEXPRESS' -or $rd.GetString(4) -ine 'b499750c-a0c8-4003-b7ee-dce4938f78dc'){throw 'Approved target identity differs'}
 } finally {$rd.Dispose()}
 $q.CommandText='SELECT TOP(1) MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC'
 if([string]$q.ExecuteScalar() -cne '20261015091551_V05EditorialDocuments'){throw 'Recheck schema compatibility before proceeding'}
} finally {$connection.Dispose()}
# Owner confirmed THANGPD -> MinhAnh rename; never edit registered SQL metadata.
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/before-sign-preflight') @deps
if($LASTEXITCODE){throw 'Source stamps, proof, or target no longer valid. No signing/Apply.'}
```

Preflight phải fail nếu proof hết TTL/khác definitions. Hạn sớm nhất 21/10/2026 10:16:08 VN; không có nghĩa luôn hợp lệ tới lúc đó nếu definitions đổi. Job recovery hiện 3 proof/resource, cần review job mới nếu phải refresh, không nâng retry hoặc thay proof trong plan đã duyệt.

## 2. Ký chỉ sau exact confirmation

**Điểm dừng bắt buộc của yêu cầu người dùng:** nhận xác nhận Batch ID `14849e55-a85c-4d7f-94cf-1253ac5103ef`, SHA `$planSha`, phạm vi 3 content/4 revision/7 ảnh/6 alias và ngoại lệ tracking đã trình. Không tự điền người duyệt/reference để thay xác nhận. Các Read-Host sau thu thông tin người duyệt thật, không tự xác minh thẩm quyền ngoài hệ thống.

```powershell
$approvedBy=Read-Host 'Nguoi duyet thuc te'
$approvalReference=Read-Host 'Tham chieu xac nhan exact batch/hash cua chu du an'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sign-dev-import-approval.ps1 -Review $review -ReviewSha $reviewSha -Thumbprint $thumb -ApprovedBy $approvedBy -ApprovalReference $approvalReference -ConfirmPlanSha $planSha -ExpiresUtc ([DateTimeOffset]::UtcNow.AddHours(12)) -Actions Apply -ConfirmUserApproval
if($LASTEXITCODE){throw 'Signing failed; do not Apply'}
$approval=Read-Host 'Duong dan approval .ps1 vua duoc script ky va in ra'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-dev-import-signature.ps1 -Mode ApprovedDocument -File $approval -ExpectedThumbprint $thumb -Plan $plan -PlanSha $planSha -Manifest $plan -ManifestSha $planSha -Report ($out+'/signed-approval-verification.json')
if($LASTEXITCODE){throw 'Exact signature/binding invalid'}
```

Không execute file approval. `ExecutionPolicy Bypass` chỉ cho process con như công cụ hiện có, không Set-ExecutionPolicy/global policy. Chữ ký bind plan/target/operator/action/expiry; cert hợp lệ không thay business approval.

## 3. Backup mới và bảo vệ dependency

Sau xác nhận và ký hợp lệ, backup đúng target; flag bên dưới cấp NTFS cho tài khoản dịch vụ SQL **chỉ trên thư mục backup mới của dự án**, không cấp quyền ứng dụng/SQL. Nếu không được phép dùng ACL đó hoặc backup lỗi, dừng; không mở quyền toàn máy.

```powershell
$backupDir='.local/evidence/phase3c/'+$label+'-dev-backup'
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts/backup-project.ps1 -Database LandPortal_Dev -GrantSqlBackupFolderAccess -EvidenceDirectory $backupDir
if($LASTEXITCODE){throw 'Dev backup failed'}
$backupReceipt=$backupDir+'/backup-LandPortal_Dev.json'
$br=Get-Content -LiteralPath $backupReceipt -Raw|ConvertFrom-Json
$bm=Get-Content -LiteralPath (Join-Path $br.path manifest.json) -Raw|ConvertFrom-Json
foreach($f in $bm.files){
 $p=[IO.Path]::GetFullPath((Join-Path $br.path $f.relativePath))
 if(-not $p.StartsWith($br.path.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $p).Length -ne $f.bytes -or (Get-SigningSha $p) -cne $f.sha256){throw 'Backup readback mismatch'}
}
# Updated SQLTest append-only proof receipts are NOT in the pre-scan backup.
# Isolate these Test settings in a child script block/process and restore Dev settings afterwards:
$testBackupDir='.local/evidence/phase3c/'+$label+'-proof-sqltest-backup'
& {
 foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),(Join-Path $script:ProjectRoot ('.local/tests/phase2a/'+$pair[1])),'Process')}
 try { & ./scripts/backup-project.ps1 -Database LandPortal_Test -GrantSqlBackupFolderAccess -EvidenceDirectory $testBackupDir }
 finally { foreach($pair in @(@('MediaRoot','media'),@('SnapshotRoot','snapshots'),@('TempRoot','temp'),@('KeysRoot','keys'))){[Environment]::SetEnvironmentVariable(('Storage__'+$pair[0]),(Join-Path $script:ProjectRoot ('.local/'+$pair[1])),'Process')} }
}
if($LASTEXITCODE){throw 'SQLTest proof receipt backup failed'}
$depRoot=Join-Path $br.path 'batch-dependencies'
[IO.Directory]::CreateDirectory($depRoot)|Out-Null
$allow=Get-Content ($run+'/dependency-allowlist.json') -Raw|ConvertFrom-Json
foreach($f in $allow.files){
 if((Get-SigningSha $f.sourcePath) -cne $f.sha256){throw 'Dependency changed'}
 $dest=Join-Path $depRoot ($f.sha256+'.bin')
 if(-not(Test-Path -LiteralPath $dest)){Copy-Item -LiteralPath $f.sourcePath -Destination $dest}
 if((Get-SigningSha $dest) -cne $f.sha256){throw 'Dependency backup readback mismatch'}
}
Copy-Item -LiteralPath ($run+'/dependency-allowlist.json') -Destination (Join-Path $depRoot 'mapping.json')
# Also preserve and hash actual signed approval + .receipt.json and SQLTest backup receipt here.
# Independently read back the SQLTest backup manifest just as the Dev backup above.
# Receipt <=12h, .bak SHA and SQL HEADERONLY are additionally enforced by the Apply tool.
```

VERIFYONLY/checksum/readback không phải restore drill. Không reset database hay restore toàn bộ. Không gọi dependency-allowlist PREPARED là backup đã thực hiện; ghi receipt/hash thật sau copy.

## 4. Apply, verify và idempotency — chỉ khi tất cả gate đạt

```powershell
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/preflight') @deps
if($LASTEXITCODE){throw 'Preflight failed; no Apply'}
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/apply') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'Stop. Reconcile exact batch before retry; no blind repeat'}
rtk proxy $script:DotnetPath $cli safe-verify --report ($out+'/verify') @deps
if($LASTEXITCODE){throw 'Verify mismatch; stop all subsequent batches'}
rtk proxy $script:DotnetPath $cli batch-preflight --report ($out+'/after-apply-reconcile') @deps
if($LASTEXITCODE){throw 'Reconciliation failed'}
# Only after acknowledged commit + successful verify/reconciliation, same unmodified plan:
rtk proxy $script:DotnetPath $cli safe-apply --report ($out+'/idempotency') @deps --approval $approval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'Stop and reconcile'}
rtk proxy $script:DotnetPath $cli safe-verify --report ($out+'/verify-after-repeat') @deps
if($LASTEXITCODE){throw 'Repeat verification failed'}
```

Kỳ vọng 10 AlreadyCommitted/30 Blocked khi repeat, 0 tăng content/revision/asset/alias; authorization audit có thể append theo thiết kế. Đối chiếu 37 bản cũ riêng với delta 3/4/7/6, .env/Identity/original53; không so tổng 40 với baseline37 rồi gọi mismatch giả. Smoke loopback vài trang chi tiết/chuyên mục/search/7 ảnh với ACL thật sau Apply; không gọi public/private ACL PASS trước khi chạy.

## 5. Reconcile/rollback

Nếu ack lỗi/không rõ, chạy read-only `batch-preflight` với report mới và cùng `$deps`, không tự retry. Rollback hiện có chỉ withdraw publication mới thuộc batch chưa bị editor sửa, giữ records/revisions/media/aliases/audit; không undo vật lý toàn bộ. Approval action Apply ở trên **không cho Rollback**. Nếu cần, lấy xác nhận và ký approval mới `-Actions Rollback` cùng exact review/hash trước khi:

```powershell
$rollbackApproval=Read-Host 'Approval exact batch da duoc duyet/ky action Rollback'
rtk proxy $script:DotnetPath $cli safe-rollback --report ($out+'/rollback') @deps --approval $rollbackApproval --backup-receipt $backupReceipt
if($LASTEXITCODE){throw 'Rollback uncertain; reconcile; do not reset database'}
```

Không chạy rollback khi chưa Apply. Backup hết hạn/proof lỗi/record bị editor sửa thì dừng và xử lý đúng phạm vi; không nâng quyền hoặc bỏ guard. Không Apply batch mở rộng/ảnh chưa được proof mới. SQL timeout ROOT CAUSE UNKNOWN và incident raw lịch sử PARTIAL vẫn riêng.
