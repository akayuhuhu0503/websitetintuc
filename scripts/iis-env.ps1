# Offline input reader. Never evaluates .env or opens SQL; do not print returned settings.
function Read-IisEnvironment([string]$Path,[switch]$ForPublish) {
 $settings=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'uat-config.example.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $allowed=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
 foreach($section in $settings.PSObject.Properties){foreach($key in $section.Value.PSObject.Properties){[void]$allowed.Add($section.Name+'__'+$key.Name)}}
 $meta=@{};$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($line in [IO.File]::ReadAllLines($Path)){
  $text=$line.Trim();if(-not $text -or $text.StartsWith('#')){continue}
  if($text -notmatch '^([A-Za-z][A-Za-z0-9_]*)=(.*)$'){throw 'Invalid .env entry; use Key=value (values withheld)'}
  $key=$matches[1];$value=$matches[2]
  if(-not $seen.Add($key)){throw ('Duplicate field: '+$key)}
  if($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))){$value=$value.Substring(1,$value.Length-2)}
  if($key -cin @('LANDPORTAL_HOME','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')){$meta[$key]=$value;continue}
  if(-not $allowed.Contains($key)){throw ('Unsupported configuration field: '+$key)}
  $parts=$key -split '__';$settings.($parts[0]).($parts[1])=$value
 }
 if($ForPublish){
  foreach($key in @('LANDPORTAL_HOME','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','ConnectionStrings__Portal','Deployment__Profile','Deployment__TargetApproved','Deployment__TargetApprovalReference','Deployment__ExpectedSqlServer','Deployment__ExpectedSqlServerIdentity','Deployment__SqlAuthentication','Deployment__ExpectedSqlLogin','Sql__ExpectedDatabase','Sql__CompatibilityLevel','Public__BaseUrl','Deployment__ExpectedHost','Storage__MediaRoot','Storage__SnapshotRoot','Storage__TempRoot','Storage__KeysRoot','Runtime__RolePassword','Scanner__Provider','Scanner__PolicyReference','Media__AllowUnscannedDevelopmentUploads','Mail__Transport','Video__Enabled','AdminSession__IdleMinutes')){
   $parts=$key -split '__';$value=if($parts.Count -eq 1){$meta[$key]}else{$settings.($parts[0]).($parts[1])}
   if(-not $seen.Contains($key) -or [string]::IsNullOrWhiteSpace([string]$value)){throw ('Required field: '+$key)}
  }
  if($meta.DOTNET_ENVIRONMENT -cne 'Staging' -or $meta.ASPNETCORE_ENVIRONMENT -cne 'Staging'){throw 'DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT: only approved Staging/UAT is supported; Production is not enabled'}
  if($settings.Sql.ExpectedDatabase -ieq 'LandPortal_LanDemo'){throw 'Sql__ExpectedDatabase: LAN demo is forbidden'}
  if($settings.Sql.CompatibilityLevel -cne '160'){throw 'Sql__CompatibilityLevel: SQL Server 2022 requires 160 for this handoff'}
  if($settings.AdminSession.IdleMinutes -cne '30'){throw 'AdminSession__IdleMinutes: preserve the approved 30-minute policy'}
  $sql=New-Object System.Data.Common.DbConnectionStringBuilder
  try{$sql.set_ConnectionString([string]$settings.ConnectionStrings.Portal)}catch{throw 'ConnectionStrings__Portal: invalid syntax (values withheld)'}
  foreach($sqlKey in @($sql.Keys)){if($sqlKey -iin @('Password','Pwd') -and [string]$sql[$sqlKey] -ceq [string]$settings.Runtime.RolePassword){throw 'Runtime__RolePassword: must differ from SQL login password'}}
  if($settings.Scanner.Provider -ceq 'MicrosoftDefenderOnPrem'){
   foreach($field in @('DefenderPrivacyPolicyReference','DefenderBinarySha256','DefenderServiceIdentity')){if([string]::IsNullOrWhiteSpace([string]$settings.Scanner.$field)){throw ('Required field: Scanner__'+$field)}}
  }
  if($settings.Mail.Transport -cnotin @('Disabled','Smtp')){throw 'Mail__Transport: only Disabled or approved Smtp'}
  if($settings.Mail.Transport -ceq 'Smtp'){
   foreach($field in @('Host','Port','From','Tls','PolicyReference','DeliveryEnabled')){if([string]::IsNullOrWhiteSpace([string]$settings.Mail.$field)){throw ('Required field: Mail__'+$field)}}
   $port=0;$address=$null
   try{$address=New-Object System.Net.Mail.MailAddress ([string]$settings.Mail.From)}catch{throw 'Mail__From: invalid address'}
   if(-not [int]::TryParse([string]$settings.Mail.Port,[ref]$port) -or $port -lt 1 -or $port -gt 65535 -or $settings.Mail.Tls -cne 'StartTls' -or $settings.Mail.DeliveryEnabled -cnotin @('true','false') -or ([string]$settings.Mail.PolicyReference).Length -lt 5 -or [Uri]::CheckHostName($settings.Mail.Host) -eq 'Unknown'){throw 'Mail__Host/Port/Tls/DeliveryEnabled/PolicyReference: invalid configuration'}
   if([string]::IsNullOrEmpty($settings.Mail.UserName) -ne [string]::IsNullOrEmpty($settings.Mail.Password)){throw 'Mail__UserName and Mail__Password must be paired'}
  }
  if($settings.Video.Enabled -cnotin @('true','false')){throw 'Video__Enabled: expected true/false'}
  if($settings.Video.Enabled -ceq 'true'){
   foreach($field in @('FfmpegPath','FfprobePath')){[void](Get-UatSafePath ([string]$settings.Video.$field))}
   foreach($field in @('FfmpegSha256','FfprobeSha256')){if([string]$settings.Video.$field -cnotmatch '^[a-fA-F0-9]{64}$'){throw ('Required field: Video__'+$field)}}
   if($settings.Video.ProcessorApproved -cne 'true' -or ([string]$settings.Video.LicenseReference).Length -lt 5){throw 'Video__ProcessorApproved/LicenseReference required for enabled video'}
  }
 }
 return [pscustomobject]@{Settings=$settings;Metadata=$meta}
}
