[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][ValidateSet('inspect','offline-plan','plan','backup','verify-backup','import','verify','reconcile','rollback')][string]$Mode,
 [Parameter(Mandatory=$true)][string]$Package,
 [Parameter(Mandatory=$true)][string]$ToolPath,
 [string]$DataHome,[string]$Config,[string]$IisPublishRoot,[switch]$ReviewedCandidates,[switch]$ContentOnly,[switch]$PendingOnly,
 [string]$Output,[string]$ManifestSha,[string]$PlanSha,[string]$ConfirmTarget,[string]$Approval,[string]$BackupReceipt,[string]$OperatorCredential,[string]$OwnerDbaConfirmation,[string]$TargetMediaReceipt,[string]$TargetMediaReceiptSha,[string]$PackageMediaRoot
)
$ErrorActionPreference='Stop'
$tool=[IO.Path]::GetFullPath($ToolPath);$packet=[IO.Path]::GetFullPath($Package)
if(-not(Test-Path -LiteralPath $tool -PathType Leaf) -or -not(Test-Path -LiteralPath $packet -PathType Container)){throw 'Existing tool and package required'}
$cli=@($Mode,'--package',$packet)
if($ReviewedCandidates){$cli+='--reviewed-candidates'}
if($ContentOnly){$cli+='--content-only'}
if($PendingOnly){$cli+='--pending-only'}
if($IisPublishRoot){$cli+=@('--iis-root',[IO.Path]::GetFullPath($IisPublishRoot))}
foreach($pair in @(@('--output',$Output),@('--manifest-sha',$ManifestSha),@('--plan-sha',$PlanSha),@('--confirm-target',$ConfirmTarget),@('--approval',$Approval),@('--backup-receipt',$BackupReceipt))){if(-not [string]::IsNullOrWhiteSpace($pair[1])){$cli+=$pair}}
if($OperatorCredential){$cli+=@('--operator-credential',[IO.Path]::GetFullPath($OperatorCredential))}
if($OwnerDbaConfirmation){$cli+=@('--owner-dba-confirmation',[IO.Path]::GetFullPath($OwnerDbaConfirmation))}
if($TargetMediaReceipt){$cli+=@('--target-media-receipt',[IO.Path]::GetFullPath($TargetMediaReceipt))}
if($TargetMediaReceiptSha){$cli+=@('--target-media-receipt-sha',$TargetMediaReceiptSha)}
if($PackageMediaRoot){$cli+=@('--package-media-root',[IO.Path]::GetFullPath($PackageMediaRoot))}
$saved=@{};$names=@('LANDPORTAL_CONFIG','LANDPORTAL_HOME','LANDPORTAL_PORTABLE_UAT','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')
if($Mode -notin @('inspect','offline-plan')){
 if(-not $IisPublishRoot -and ([string]::IsNullOrWhiteSpace($DataHome) -or [string]::IsNullOrWhiteSpace($Config))){throw 'Existing IIS publish root or allocated private UAT home/config required'}
 if($IisPublishRoot -and ($DataHome -or $Config)){throw 'Use either the bound IIS config or a separately allocated operator config, never both'}
 foreach($entry in @(Get-ChildItem Env:)){if($entry.Name -match '^(ConnectionStrings|Sql|Runtime|Storage|Public|Deployment|Mail|Scanner|Media|Video|InquiryFiles|AdminSession|Captcha|SafeImport)__'){$names+=$entry.Name}}
 foreach($name in $names|Select-Object -Unique){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$null,'Process')}
 if(-not $IisPublishRoot){$env:LANDPORTAL_HOME=[IO.Path]::GetFullPath($DataHome);$env:LANDPORTAL_CONFIG=[IO.Path]::GetFullPath($Config)}
 $env:DOTNET_ENVIRONMENT='Staging';$env:ASPNETCORE_ENVIRONMENT='Staging'
}
try{
 $dotnet=(Get-Command dotnet -ErrorAction Stop).Source
 $ErrorActionPreference='Continue'
 if(Get-Command rtk -ErrorAction SilentlyContinue){& rtk proxy $dotnet $tool @cli}else{& $dotnet $tool @cli}
 if($LASTEXITCODE -ne 0){throw ('UAT data command stopped with exit code '+$LASTEXITCODE+'; reconcile before retry')}
}finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
