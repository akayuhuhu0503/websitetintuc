[CmdletBinding()]
param([string]$EnvFile,[string]$PublishRoot=(Split-Path -Parent $PSScriptRoot),[switch]$PrepareForCopy,[switch]$ValidateOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'uat-common.ps1')
. (Join-Path $PSScriptRoot 'iis-env.ps1')
function Get-IisFailureDescription([string]$Message){
 switch -Regex ($Message){
  'stopped site/pool|site.*stopped' {return 'Cần dừng đúng site IIS và application pool trước khi gắn cấu hình; công cụ không tự dừng dịch vụ.'}
  'Dedicated|service account|identity|pool required' {return 'Cần application pool riêng: No Managed Code, Load User Profile=true và tài khoản dịch vụ ít quyền đã được cấp.'}
  'HTTPS|certificate|binding|CN/SAN|trust chain' {return 'Thiếu hoặc sai binding HTTPS/chứng chỉ cho site: kiểm tra hostname, hạn, private key, EKU và chuỗi tin cậy.'}
  'New private home|Existing private|never overwritten' {return 'Thư mục cấu hình private đã tồn tại. Công cụ giữ nguyên dữ liệu; cần đối soát cấu hình cũ trước khi nâng cấp, không xóa hoặc ghi đè.'}
  'Defender|scanner|privacy|unscanned' {return 'Microsoft Defender hoặc chính sách scanner/privacy chưa đạt. Cần Defender hoạt động, chữ ký Microsoft/definitions hợp lệ và chính sách TTDL đã duyệt.'}
  'authority|approval|reference' {return 'Phê duyệt chưa hợp lệ hoặc không khớp máy/site/SQL. Điền reference thực tế đã được duyệt trong cùng .env.'}
  'Manifest|integrity|sealed|publish packet' {return 'Gói Publish không còn nguyên vẹn hoặc đã gắn đích; không sửa manifest để vượt kiểm tra. Dùng gói mới hoặc đối soát receipt hiện có.'}
  'physical path' {return 'IIS Physical Path phải trỏ thư mục web/ của gói Publish vừa copy.'}
  'Unauthorized|denied|permitted' {return 'Thiếu quyền đọc IIS hoặc tạo thư mục private. Chạy công cụ bằng tài khoản vận hành được cấp quyền; không cấp quyền SQL mới.'}
  '^Chưa chọn file' {return 'Chưa chọn file .env; không thay đổi cấu hình.'}
  '^File \.env phải' {return 'File .env phải ở ngoài webroot.'}
  '^IIS_MACHINE_NAME' {return 'IIS_MACHINE_NAME không khớp máy đang chạy; không tạo phê duyệt cho máy khác.'}
  '^Máy chưa có IIS' {return 'Máy chưa có IIS Management API; chạy bước cấu hình trên đúng máy IIS.'}
  default {return 'Cấu hình chưa đạt kiểm tra runtime. Kiểm tra đúng SQL/UAT, đường dẫn private ngoài webroot và quyền tài khoản vận hành; không tắt guard để khởi động.'}
 }
}
if($MyInvocation.InvocationName -eq '.'){return}
try{
 if(-not $EnvFile){
  Add-Type -AssemblyName System.Windows.Forms
  $dialog=New-Object Windows.Forms.OpenFileDialog;$dialog.Title='Chọn cùng file .env đã dùng để Publish (không chọn uat.json)';$dialog.Filter='File cấu hình (*.env;*.ready)|*.env;*.ready|Tất cả file (*.*)|*.*'
  if($dialog.ShowDialog() -ne 'OK'){throw 'Chưa chọn file .env; không thay đổi cấu hình'};$EnvFile=$dialog.FileName
 }
 $source=Get-UatSafePath $EnvFile;$code=Get-UatSafePath $PublishRoot
 if($source.StartsWith((Join-Path $code 'web')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'File .env phải ở ngoài webroot'}
 $inputSettings=Read-IisEnvironment $source -ForPublish
 $gaps=@(Get-IisInputGaps $inputSettings)
 if($gaps.Count){Write-Output ('CHƯA ĐỦ ĐIỀU KIỆN IIS: cần TTDL điền/phê duyệt trong cùng .env: '+($gaps -join '; '));exit 2}
 if($inputSettings.Metadata.IIS_MACHINE_NAME -ine $env:COMPUTERNAME){throw 'IIS_MACHINE_NAME không khớp máy đang chạy; không tạo phê duyệt cho máy khác'}
 $api=Join-Path $env:windir 'System32/inetsrv/Microsoft.Web.Administration.dll'
 if(-not(Test-Path -LiteralPath $api)){throw 'Máy chưa có IIS Management API; chạy bước cấu hình trên đúng máy IIS'}
 if($ValidateOnly){Write-Output 'DỮ LIỆU CẤU HÌNH IIS HỢP LỆ; chưa kiểm tra binding, service account hoặc scanner; không ghi file';return}
 $privateInput=Join-Path ([Environment]::GetFolderPath('UserProfile')) ('LandPortal-IIS-Private\setup-'+[Guid]::NewGuid().ToString('N'))
 [void](Get-UatSafePath $privateInput)
 $acl=New-Object Security.AccessControl.DirectorySecurity;$acl.SetAccessRuleProtection($true,$false)
 foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))}
 [void][IO.Directory]::CreateDirectory($privateInput,$acl)
 # Approval is transcribed only from explicit operator input, never from discovery.
 $approval=@{Status='APPROVED';MachineName=[string]$inputSettings.Metadata.IIS_MACHINE_NAME;SiteName=[string]$inputSettings.Metadata.IIS_SITE_NAME;PhysicalSqlServer=[string]$inputSettings.Settings.Deployment.ExpectedSqlServerIdentity;Database=[string]$inputSettings.Settings.Sql.ExpectedDatabase;DeploymentReference=[string]$inputSettings.Settings.Deployment.TargetApprovalReference;ScannerPolicyReference=[string]$inputSettings.Settings.Scanner.PolicyReference;DefenderPrivacyPolicyReference=[string]$inputSettings.Settings.Scanner.DefenderPrivacyPolicyReference}
 $json=Join-Path $privateInput 'input.json';$authority=Join-Path $privateInput 'authority.json'
 [IO.File]::WriteAllText($json,($inputSettings.Settings|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText($authority,($approval|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 & (Join-Path $PSScriptRoot 'configure-iis-uat.ps1') -PublishRoot $code -PrivateConfig $json -SiteName $inputSettings.Metadata.IIS_SITE_NAME -ApprovalFile $authority -DataHome $inputSettings.Metadata.LANDPORTAL_HOME -Apply -PrepareForCopy:$PrepareForCopy
 Write-Output 'ĐÃ TỰ TẠO VÀ GẮN CẤU HÌNH PRIVATE. Web và 5 Worker dùng chung uat.json. Chưa khởi động IIS/Worker; chạy Preflight bằng tài khoản dịch vụ trước khi mở site.'
}catch{
 # Never echo a configuration/provider exception, which may contain secret values.
 Write-Output ('CẤU HÌNH IIS BỊ CHẶN: '+(Get-IisFailureDescription $_.Exception.Message))
 Write-Output 'Không chạy SQL/migration hoặc khởi động dịch vụ. Không tạo phê duyệt hoặc secret thay người vận hành.'
 exit 2
}
