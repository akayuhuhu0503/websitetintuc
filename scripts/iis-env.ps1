# Offline input reader. Never evaluates .env or opens SQL; do not print settings.
function Get-IisInputGaps($InputSettings){
 $gaps=@();$s=$InputSettings.Settings;$m=$InputSettings.Metadata
 foreach($k in @('IIS_SITE_NAME','IIS_MACHINE_NAME')){if([string]::IsNullOrWhiteSpace([string]$m[$k]) -or [string]$m[$k] -match 'REPLACE|PENDING|FIXTURE|[\r\n]'){$gaps+=$k}}
 return $gaps
}
function Read-IisEnvironment([string]$Path,[switch]$ForPublish) {
 $settings=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'uat-config.example.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $allowed=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
 foreach($section in $settings.PSObject.Properties){foreach($key in $section.Value.PSObject.Properties){[void]$allowed.Add($section.Name+'__'+$key.Name)}}
 $meta=@{DOTNET_ENVIRONMENT='Staging';ASPNETCORE_ENVIRONMENT='Staging';LANDPORTAL_HOME='';IIS_SITE_NAME='';IIS_MACHINE_NAME=''}
 $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($line in [IO.File]::ReadAllLines($Path)){
  $text=$line.Trim();if(-not $text -or $text.StartsWith('#')){continue}
  if($text -notmatch '^([A-Za-z][A-Za-z0-9_]*)=(.*)$'){throw 'Dòng .env không hợp lệ: cần dạng Key=value; không hiển thị giá trị bí mật'}
  $key=$matches[1];$value=$matches[2]
  if(-not $seen.Add($key)){throw ('Trường .env bị lặp: '+$key)}
  if($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))){$value=$value.Substring(1,$value.Length-2)}
  if($key -cin @('LANDPORTAL_HOME','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','IIS_SITE_NAME','IIS_MACHINE_NAME')){$meta[$key]=$value;continue}
  if($key -cin @('Deployment__TargetApproved','Deployment__TargetApprovalReference','Scanner__PolicyReference','Scanner__DefenderPrivacyPolicyReference','Video__ProcessorApproved','Video__LicenseReference')){continue} # Obsolete keys in existing private inputs are ignored, not converted into approvals.
  if(-not $allowed.Contains($key)){throw ('Trường không được hỗ trợ trong .env IIS: '+$key+'. Dùng scripts/ttdl.env.example; không dùng .env Development.')}
  $parts=$key -split '__';$settings.($parts[0]).($parts[1])=$value
 }
 if($ForPublish){
  foreach($key in @('ConnectionStrings__Portal','Runtime__RolePassword','Deployment__ExpectedSqlServerIdentity')){
   $parts=$key -split '__'
   if(-not $seen.Contains($key) -or [string]::IsNullOrWhiteSpace([string]$settings.($parts[0]).($parts[1]))){throw ('Thiếu cấu hình bắt buộc: '+$key)}
  }
  if($meta.DOTNET_ENVIRONMENT -cne 'Staging' -or $meta.ASPNETCORE_ENVIRONMENT -cne 'Staging'){throw 'Chỉ hỗ trợ Staging/UAT; không tự bật hoặc kế thừa ngoại lệ sang Production'}
  try{
   $portable=New-Object System.Data.Common.DbConnectionStringBuilder
   $portable.set_ConnectionString([string]$settings.ConnectionStrings.Portal)
   foreach($key in @($portable.Keys)){if(($key -replace ' ','') -ieq 'HostNameInCertificate'){[void]$portable.Remove($key)}}
   $sql=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ($portable.get_ConnectionString())
  }catch{throw 'Invalid private SQL syntax; values withheld'}
  if(-not $seen.Contains('Deployment__ExpectedSqlServer')){$settings.Deployment.ExpectedSqlServer=$sql.DataSource}
  if(-not $seen.Contains('Sql__ExpectedDatabase')){$settings.Sql.ExpectedDatabase=$sql.InitialCatalog}
  if(-not $seen.Contains('Deployment__SqlAuthentication')){$settings.Deployment.SqlAuthentication=if($sql.IntegratedSecurity){'Windows'}else{'SqlPassword'}}
  if(-not $seen.Contains('Deployment__ExpectedSqlLogin')){$settings.Deployment.ExpectedSqlLogin=$sql.UserID}
  # Unallocated technical values stay empty until discovery on the IIS machine.
  foreach($key in @('Deployment__ExpectedHost','Public__BaseUrl','Storage__MediaRoot','Storage__SnapshotRoot','Storage__TempRoot','Storage__KeysRoot','Scanner__DefenderServiceIdentity')){
   if(-not $seen.Contains($key)){$parts=$key -split '__';$settings.($parts[0]).($parts[1])=''}
  }
  if(-not $seen.Contains('Video__Enabled')){$settings.Video.Enabled='false'}
  Assert-IisPublishConfiguration $settings
 }
 return [pscustomobject]@{Settings=$settings;Metadata=$meta}
}

function Assert-IisPublishConfiguration($Settings) {
 Assert-UatSqlConfiguration $Settings
 if($Settings.Sql.ExpectedDatabase -ieq 'LandPortal_LanDemo'){throw 'LAN demo is forbidden for this handoff'}
 if([string]$Settings.Sql.CompatibilityLevel -cne '160'){throw 'SQL Server 2022 compatibility 160 required'}
 if([string]$Settings.AdminSession.IdleMinutes -cne '30'){throw 'Preserve the approved 30-minute idle policy'}
 $physical=[string]$Settings.Deployment.ExpectedSqlServerIdentity
 if([string]::IsNullOrWhiteSpace($physical) -or $physical -match 'REPLACE|[\r\n]'){throw 'Developer must bind the confirmed physical SQL identity'}
 $secret=[string]$Settings.Runtime.RolePassword
 if($secret.Length -lt 24 -or $secret.Length -gt 128){throw 'Existing runtime role secret must contain 24 to 128 characters; value withheld'}
 $sql=New-Object System.Data.Common.DbConnectionStringBuilder
 $sql.set_ConnectionString([string]$Settings.ConnectionStrings.Portal)
 foreach($key in @($sql.Keys)){if($key -iin @('Password','Pwd') -and [string]$sql[$key] -ceq $secret){throw 'SQL login and application role secrets must differ'}}
 if([string]$Settings.Media.AllowUnscannedDevelopmentUploads -ine 'false'){throw 'Unscanned media bypass forbidden'}
 if($Settings.Mail.Transport -cne 'Disabled' -or [string]$Settings.Mail.DeliveryEnabled -ine 'false'){throw 'This offline handoff keeps SMTP Disabled'}
 if(-not [string]::IsNullOrWhiteSpace([string]$Settings.ConnectionStrings.GiayHuyReadOnly)){
  try{$source=New-Object System.Data.SqlClient.SqlConnectionStringBuilder ([string]$Settings.ConnectionStrings.GiayHuyReadOnly)}catch{throw 'Invalid private source SQL syntax; values withheld'}
  if($Settings.Deployment.Profile -cne 'UAT' -or $source.DataSource -cne '45.120.229.43,1433' -or $source.InitialCatalog -cne 'QuanLyDuAnTrungTamDuLieuDatDai' -or $source.UserID -cne 'landportal_giayhuy_reader' -or $source.IntegratedSecurity -or -not $source.Encrypt -or [string]::IsNullOrEmpty($source.Password) -or $source.AttachDBFilename -or $source.PersistSecurityInfo){throw 'Source connection must use the approved UAT View-only reader; values withheld'}
  if($Settings.GiayHuy.PublicationScope -cne 'OfficialConfirmedEightFields-OwnerApproved-20261104' -or ($source.TrustServerCertificate -and $Settings.GiayHuy.LanUatTlsException -cne 'OwnerApproved-20261104')){throw 'Source publication/TLS approval missing or outside UAT'}
  if($source.Password -ceq $secret){throw 'Source login and runtime role secrets must differ'}
 }
 # Target allocation/HTTPS/storage/scanner/video authority is checked at
 # activation/Preflight, never fabricated to make Publish succeed.
}
