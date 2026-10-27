[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$pointer=Join-Path $script:ProjectRoot '.local/lan-demo/current.json'
if(-not(Test-Path $pointer)){throw 'LAN demo is not prepared; configuration/database must be provisioned separately.'}
$state=Get-Content $pointer -Raw -Encoding UTF8|ConvertFrom-Json
$c=Get-Content $state.config -Raw -Encoding UTF8|ConvertFrom-Json
if($c.Deployment.Profile -cne 'LAN_DEMO' -or $c.Sql.ExpectedDatabase -cne 'LandPortal_LanDemo' -or [DateTimeOffset]$c.LanDemo.ExpiresUtc -le [DateTimeOffset]::UtcNow){throw 'Invalid/expired isolated LAN demo configuration'}
$uri=[Uri]$c.Public.BaseUrl
if(-not(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $uri.Host -ErrorAction SilentlyContinue)){throw 'Configured LAN IPv4 is no longer assigned; review config before starting'}
if(Get-NetTCPConnection -LocalAddress $uri.Host -LocalPort $uri.Port -State Listen -ErrorAction SilentlyContinue){Write-Output ('A process already listens at '+$uri.AbsoluteUri+'; no duplicate instance started.');return}
$env:LANDPORTAL_CONFIG=$state.config;$env:LANDPORTAL_HOME=$state.home
# External isolated config must win over inherited developer settings. No persistent environment changes.
foreach($key in @('ConnectionStrings__Portal','Sql__ExpectedDatabase','Runtime__RolePassword','Storage__MediaRoot','Storage__SnapshotRoot','Storage__TempRoot','Storage__KeysRoot','Public__BaseUrl','Public__CanonicalHost','Deployment__Profile','Mail__Transport','Scanner__Provider','Media__AllowUnscannedDevelopmentUploads','AdminSession__IdleMinutes','Captcha__TestScope')){[Environment]::SetEnvironmentVariable($key,$null,'Process')}
$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development'
$stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$stdout=Join-Path $state.home ('web-'+$stamp+'.out.log');$stderr=Join-Path $state.home ('web-'+$stamp+'.err.log')
$hostScript=Join-Path $PSScriptRoot 'lan-demo-host.ps1'
$powershell=Join-Path $PSHOME 'powershell.exe'
$startup=New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0}
$command='"'+$powershell+'" -NoProfile -NonInteractive -WindowStyle Hidden -File "'+$hostScript+'" -RtkPath "'+(Get-Command rtk).Source+'"'
# WMI creates a normal current-user process independent of the calling terminal.
# No scheduled task, installed service, policy change or security bypass.
$created=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$command;CurrentDirectory=$script:ProjectRoot;ProcessStartupInformation=$startup}
if($created.ReturnValue -ne 0){throw ('Detached process creation failed: '+$created.ReturnValue+'; no fallback security changes')}
$proc=Get-Process -Id $created.ProcessId
$record=@{pid=$proc.Id;startedUtc=$proc.StartTime.ToUniversalTime().ToString('O');web=$state.web;hostScript=$hostScript;launch='CurrentUser-Win32Process';url=$uri.AbsoluteUri;database='LandPortal_LanDemo'}
[IO.File]::WriteAllText((Join-Path $state.home 'process.json'),($record|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$ready=$false
for($i=0;$i -lt 40;$i++){Start-Sleep -Milliseconds 250;$proc.Refresh();if($proc.HasExited){throw 'LAN Web stopped during startup; inspect private log (no secrets in console).'};try{$response=Invoke-WebRequest ($uri.AbsoluteUri+'health') -UseBasicParsing -TimeoutSec 2;if($response.StatusCode -eq 200){$ready=$true;break}}catch{}}
if(-not $ready){throw 'LAN process started but HTTP readiness not confirmed; inspect private log before retry'}
Write-Output ('RUNNING '+$uri.AbsoluteUri+' | Admin: '+$uri.AbsoluteUri+'admin/login')
Write-Output ('Database: LandPortal_LanDemo | PID '+$proc.Id+' | Workers/SMTP disabled; password/CAPTCHA login retained.')
Write-Output 'LAN HOST READY - CLIENT NOT VERIFIED. Firewall/profile were not changed.'
