[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('inspect','offline-plan','plan','backup','import','verify','reconcile','rollback')][string]$Mode,
 [Parameter(Mandatory=$true)][string]$Package,
 [Parameter(Mandatory=$true)][string]$ToolPath,
 [string]$DataHome,[string]$Config,[switch]$ReviewedCandidates,
 [string]$Output,[string]$ManifestSha,[string]$PlanSha,[string]$ConfirmTarget,[string]$Approval,[string]$BackupReceipt
)
$ErrorActionPreference='Stop'
$tool=[IO.Path]::GetFullPath($ToolPath);$packet=[IO.Path]::GetFullPath($Package)
if(-not(Test-Path -LiteralPath $tool -PathType Leaf) -or -not(Test-Path -LiteralPath $packet -PathType Container)){throw 'Existing tool and package required'}
$cli=@($Mode,'--package',$packet)
if($ReviewedCandidates){$cli+='--reviewed-candidates'}
foreach($pair in @(@('--output',$Output),@('--manifest-sha',$ManifestSha),@('--plan-sha',$PlanSha),@('--confirm-target',$ConfirmTarget),@('--approval',$Approval),@('--backup-receipt',$BackupReceipt))){if(-not [string]::IsNullOrWhiteSpace($pair[1])){$cli+=$pair}}
$saved=@{};$names=@('LANDPORTAL_CONFIG','LANDPORTAL_HOME','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')
if($Mode -notin @('inspect','offline-plan')){
 if([string]::IsNullOrWhiteSpace($DataHome) -or [string]::IsNullOrWhiteSpace($Config)){throw 'Allocated private UAT home/config required'}
 foreach($entry in @(Get-ChildItem Env:)){if($entry.Name -match '^(ConnectionStrings|Sql|Runtime|Storage|Public|Deployment|Mail|Scanner|Media|Video|InquiryFiles|AdminSession|Captcha|SafeImport)__'){$names+=$entry.Name}}
 foreach($name in $names|Select-Object -Unique){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$null,'Process')}
 $env:LANDPORTAL_HOME=[IO.Path]::GetFullPath($DataHome);$env:LANDPORTAL_CONFIG=[IO.Path]::GetFullPath($Config);$env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
}
try{
 $dotnet=(Get-Command dotnet -ErrorAction Stop).Source
 $ErrorActionPreference='Continue'
 if(Get-Command rtk -ErrorAction SilentlyContinue){& rtk proxy $dotnet $tool @cli}else{& $dotnet $tool @cli}
 if($LASTEXITCODE -ne 0){throw ('UAT data command stopped with exit code '+$LASTEXITCODE+'; reconcile before retry')}
}finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
