[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportRoot)
. (Join-Path $PSScriptRoot 'dev-signing-common.ps1')
$folder=Resolve-SigningPath $ReportRoot '.local/evidence/phase3f';if(Test-Path $folder){throw 'Unique report required'};[void][IO.Directory]::CreateDirectory($folder)
$fix=Get-Content .local/phase3h-fix-current.json -Raw -Encoding UTF8|ConvertFrom-Json
$tests=@()
foreach($script in @('scripts/diagnose-phase3h-fix.ps1','scripts/inspect-phase3h-preflight.ps1')){
 $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $script:ProjectRoot $script),[ref]$tokens,[ref]$errors);if($errors.Count){throw 'Diagnostic parser failed'};$tests+=@{name=('parse '+$script);status='PASS'}
 foreach($case in @(@('existing',$fix.root,'Unique'),@('scope','.local/phase3h-fix-disallowed','escaped'))){
  $label=[IO.Path]::GetFileNameWithoutExtension($script)+'-'+$case[0];$log=$folder+'/'+$label+'.log';$saved=$ErrorActionPreference
  try{$ErrorActionPreference='Continue';& rtk proxy powershell -NoProfile -File $script -ReportRoot $case[1] *> $log;$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
  if($code -eq 0 -or -not(Select-String -LiteralPath $log -SimpleMatch -Pattern $case[2] -Quiet)){throw ('Expected no-SQL guard failed: '+$label)}
  $tests+=@{name=$label;status='PASS';exitCode=$code;sqlQueries=0}
 }
}
Write-SigningJson ($ReportRoot+'/checks.json') @{utc=[DateTimeOffset]::UtcNow.ToString('O');checks=$tests;scope='Two final diagnostic scripts parsed and four existing/out-of-scope guards; no SQL, not a regression suite'}
