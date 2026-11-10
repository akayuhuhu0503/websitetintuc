[CmdletBinding()]
param([string]$IisPublishRoot,[switch]$VerifyCopied,[switch]$AlreadyCopied)
$ErrorActionPreference='Stop'
function Assert-ApprovedGdlaMediaRoot([string]$MediaRoot){
 # Owner-selected destination, 09/11/2026. This checks runtime; it never changes it.
 $approved='D:\WWW_GDLA\publish\TTDL-IIS\private\media'
 if([string]::IsNullOrWhiteSpace($MediaRoot)){throw 'BLOCKED_STORAGE_ROOT_MISSING: chưa xác định được MediaRoot runtime.'}
 $actual=[IO.Path]::GetFullPath($MediaRoot).TrimEnd([char[]]@('\','/'))
 if(-not $actual.Equals($approved,[StringComparison]::OrdinalIgnoreCase)){throw ('BLOCKED_STORAGE_ROOT_MISMATCH: runtime dùng '+$actual+'; đích được chủ dự án chốt là '+$approved+'. Không copy/quét/ghi SQL hoặc tự đổi config.')}
}
$packet=Join-Path $PSScriptRoot 'package';$tool=Join-Path $PSScriptRoot 'bin/UatDataHandoff.dll'
if(-not(Test-Path -LiteralPath $packet -PathType Container) -or -not(Test-Path -LiteralPath $tool -PathType Leaf)){throw 'Copy nguyên gói bàn giao. Không đặt gói trong thư mục web công khai.'}
$delivery=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tool-checksums.json') -Raw -Encoding UTF8|ConvertFrom-Json
foreach($property in $delivery.PSObject.Properties){
 $relative=[string]$property.Name
 if($relative -notlike 'bin/*' -or $relative -match '(^|/)\.\.?(/|$)|\\|:'){throw 'Đường dẫn checksum tool không hợp lệ.'}
 $path=Join-Path $PSScriptRoot $relative
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$property.Value){throw 'File công cụ đã thay đổi. Nhận lại gói đúng hash từ Developer; không bỏ kiểm tra.'}
}
if(-not $IisPublishRoot){
 $candidates=@()
 $appcmd=Join-Path $env:windir 'System32/inetsrv/appcmd.exe'
 if(-not(Test-Path -LiteralPath $appcmd)){$appcmd=Join-Path $env:windir 'Sysnative/inetsrv/appcmd.exe'}
 if(Test-Path -LiteralPath $appcmd){
  $siteXml=& $appcmd list site /xml
  if($LASTEXITCODE -eq 0){
   $sites=[Xml.XmlDocument]::new();$sites.XmlResolver=$null;$sites.LoadXml(($siteXml -join "`n"))
   foreach($site in $sites.GetElementsByTagName('SITE')){
    $matchesHost=@($site.GetAttribute('bindings').Split(',')|Where-Object {$_.Trim() -match '^https/[^,]*:443:(www\.)?gdla\.gov\.vn$'})
    if($matchesHost.Count -eq 0 -or $site.GetAttribute('state') -ine 'Started'){continue}
    $selector='/vdir.name:'+$site.GetAttribute('SITE.NAME')+'/'
    $physicalPaths=& $appcmd list vdir $selector /text:physicalPath
    if($LASTEXITCODE -ne 0){continue}
    foreach($item in $physicalPaths){
     if([string]::IsNullOrWhiteSpace($item)){continue}
     $physical=[Environment]::ExpandEnvironmentVariables([string]$item)
     $candidate=Split-Path ([IO.Path]::GetFullPath($physical)) -Parent
     if((Split-Path $physical -Leaf) -ieq 'web' -and (Test-Path -LiteralPath (Join-Path $candidate 'private/uat.json')) -and (Test-Path -LiteralPath (Join-Path $candidate 'manifest.json'))){$candidates+=$candidate}
    }
   }
  }
 }
 $candidates=@($candidates|Select-Object -Unique)
 if($candidates.Count -ne 1){throw 'Chưa tự xác định được gói IIS đang chạy. Chạy lại với -IisPublishRoot đường_dẫn_TTDL-IIS_thực_tế. Không sửa .env hoặc tạo uat.json.'}
 $IisPublishRoot=$candidates[0]
}
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$savedDotnet=$env:DOTNET_ENVIRONMENT;$savedAspnet=$env:ASPNETCORE_ENVIRONMENT
try{
 $env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
 if($VerifyCopied){
  $ErrorActionPreference='Continue'
  $destinationText=& $dotnet $tool media-copy-destination --package $packet --iis-root ([IO.Path]::GetFullPath($IisPublishRoot))
  $destinationExit=$LASTEXITCODE;$ErrorActionPreference='Stop'
  if($destinationExit -ne 0){throw 'Chưa xác định được storage runtime an toàn. Không copy vào đường dẫn suy đoán.'}
  $destination=($destinationText -join "`n")|ConvertFrom-Json
  if($destination.status -cne 'COPY_DESTINATION_RESOLVED_NOT_SCANNED'){throw 'Kết quả xác định storage không hợp lệ; chưa quét hoặc ghi SQL.'}
  Assert-ApprovedGdlaMediaRoot ([string]$destination.mediaRoot)
  Write-Output ('COPY THU MUC: '+(Join-Path $PSScriptRoot 'COPY-MEDIA/uat-data'))
  Write-Output ('DAN VAO MEDIA ROOT THUC TE: '+[string]$destination.mediaRoot)
  Write-Output ('THU MUC CAN CO DU 703 FILE: '+[string]$destination.packageDirectory)
  Write-Output 'Windows Explorer: chỉ gộp thư mục; bỏ qua file trùng, KHÔNG Replace/ghi đè. Copy không đồng nghĩa Clean hoặc được công bố.'
  if(-not $AlreadyCopied){$answer=Read-Host 'Copy xong đúng 703 file thì nhấn Enter để kiểm hash và quét thật; gõ HUY để dừng (không nhập mật khẩu)';if($answer -ne ''){throw 'Đã dừng theo người vận hành; chưa quét hoặc ghi SQL.'}}
 }
 if($VerifyCopied){Write-Output 'Kiểm hash và quét thật các file đã copy trên IIS; không copy/ghi đè media, không kết nối/ghi SQL, bật SMTP hoặc cấp quyền.'}
 else{Write-Output 'Quét thật trên máy IIS và chuyển file vào storage runtime. Không kết nối/ghi SQL, không bật SMTP, không cấp quyền hoặc công bố bài.'}
 $ErrorActionPreference='Continue'
 $operation=if($VerifyCopied){'verify-copied-media'}else{'stage-media'}
 & $dotnet $tool $operation --package $packet --iis-root ([IO.Path]::GetFullPath($IisPublishRoot))
 $code=$LASTEXITCODE;$ErrorActionPreference='Stop'
 if($code -ne 0){throw ('MEDIA BLOCKED (exit '+$code+'). Giữ file/cache để đối soát; không sửa trạng thái Clean, không retry bằng công cụ khác. Gửi kết quả cho Developer.')}
 Write-Output 'MEDIA READY: gửi file KET-QUA-MEDIA-*.json và SHA256 vừa in cho Developer qua kênh đã thống nhất. Chưa import SQL hoặc phát hành 252 bài.'
}finally{$env:DOTNET_ENVIRONMENT=$savedDotnet;$env:ASPNETCORE_ENVIRONMENT=$savedAspnet}
