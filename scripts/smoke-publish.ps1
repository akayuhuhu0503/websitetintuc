[CmdletBinding()]
param([string]$PackageDirectory='.local/publish/phase2b/web',[ValidateRange(1024,65535)][int]$Port=5191,[ValidateSet('phase2a','phase2b','v04')][string]$EvidencePhase='phase2b')
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'test-profile.ps1')
$ownedProcess=$null
$connection=$null
try {
    $packagePath=if([IO.Path]::IsPathRooted($PackageDirectory)){$PackageDirectory}else{Join-Path $script:ProjectRoot $PackageDirectory}
    $package=[IO.Path]::GetFullPath($packagePath)
    $prefix=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/publish'))+'\'
    if(-not $package.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Package must stay inside this project .local/publish.'}
    $dll=Join-Path $package 'Web.dll';if(-not (Test-Path -LiteralPath $dll)){throw 'Publish package is missing.'}
    $files=@(Get-ChildItem -LiteralPath $package -File -Recurse)
    $forbidden=@($files|Where-Object {$_.Name -eq '.env' -or $_.Extension -in @('.bak','.mdf','.ldf') -or $_.FullName -match '\\(snapshots|keys|node_modules|uploads)\\'})
    if($forbidden.Count){throw 'Private/runtime data found in package.'}
    $required=@('wwwroot/vendor/jodit/jodit.min.js','wwwroot/vendor/jodit/jodit.min.css','wwwroot/js/editor.js','licenses/media/SkiaSharp-LICENSE.txt','licenses/media/PdfPig-LICENSE.txt','THIRD_PARTY_NOTICES.md')
    foreach($relative in $required){if(-not (Test-Path -LiteralPath (Join-Path $package $relative))){throw ('Missing package asset '+$relative)}}
    Initialize-ProjectTestProfile
    $baseUrl='http://127.0.0.1:'+$Port
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Development','Process')
    [Environment]::SetEnvironmentVariable('Public__BaseUrl',$baseUrl,'Process')
    $evidence=Join-Path $script:ProjectRoot ('.local/evidence/'+$EvidencePhase)
    [IO.Directory]::CreateDirectory($evidence)|Out-Null
    $ownedProcess=Start-Process -FilePath $script:DotnetPath -ArgumentList @($dll,'--contentRoot',$package) -WorkingDirectory $script:ProjectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'publish-web.log') -RedirectStandardError (Join-Path $evidence 'publish-web-error.log')
    $ready=$false
    for($attempt=0;$attempt -lt 40;$attempt++){
        if($ownedProcess.HasExited){throw 'Owned publish process exited before readiness.'}
        try{$probe=Invoke-WebRequest -UseBasicParsing -Uri ($baseUrl+'/ready') -TimeoutSec 2;if($probe.StatusCode -eq 200){$ready=$true;break}}catch{}
        Start-Sleep -Milliseconds 200
    }
    if(-not $ready){throw 'Published package did not become ready.'}
    $connection=New-Object System.Data.SqlClient.SqlConnection (Get-ProjectConnectionBuilder 'LandPortal_Test').ConnectionString
    $connection.Open();$command=$connection.CreateCommand()
    $command.CommandText="SELECT TOP (1) CONVERT(varchar(36),c.Id) FROM Contents c JOIN SourceRecords s ON s.ContentId=c.Id WHERE s.Source=N'qldd.mae.gov.vn' AND s.SourceId NOT LIKE 'QA-%' AND c.Kind=0 AND c.IsPublic=1 AND c.IsTrashed=0 ORDER BY c.Id"
    $article=[string]$command.ExecuteScalar();if(-not $article){throw 'No real source article in SQL Test.'}
    $checks=@()
    foreach($path in @('/','/ready','/tin-tuc','/van-ban','/du-thao','/video','/admin/login',('/bai-viet/'+$article),'/vendor/jodit/jodit.min.js','/vendor/jodit/jodit.min.css','/js/editor.js')){
        $response=Invoke-WebRequest -UseBasicParsing -Uri ($baseUrl+$path) -TimeoutSec 30
        $checks+=[ordered]@{path=$path;status=[int]$response.StatusCode;contentType=$response.Headers['Content-Type'];bytes=$response.RawContentLength;cacheControl=$response.Headers['Cache-Control'];pass=($response.StatusCode -eq 200)}
    }
    $privateChecks=@()
    Add-Type -AssemblyName System.Net.Http
    $client=New-Object System.Net.Http.HttpClient (New-Object System.Net.Http.HttpClientHandler -Property @{AllowAutoRedirect=$false})
    try {
        foreach($path in @('/admin/site','/admin/users','/admin/schedules','/.env','/snapshots/sample-manifest.json')){
            $response=$client.GetAsync($baseUrl+$path).GetAwaiter().GetResult()
            $status=[int]$response.StatusCode
            $pass=if($path.StartsWith('/admin/')){$status -in @(302,401,403)}else{$status -eq 404}
            $privateChecks+=[ordered]@{path=$path;status=$status;pass=$pass}
            $response.Dispose()
            if(-not $pass){throw 'Published private route policy failed.'}
        }
    }finally{$client.Dispose()}
    if(-not $ownedProcess.HasExited){Stop-Process -Id $ownedProcess.Id; $ownedProcess.WaitForExit()}
    $report=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('O');database='LandPortal_Test';baseUrl=$baseUrl;package=$PackageDirectory;files=$files.Count;bytes=($files|Measure-Object Length -Sum).Sum;privateFiles=$forbidden.Count;requiredAssets=$required;checks=$checks;privateChecks=$privateChecks;limitations=@('Loopback Development with explicit test unscanned-media exception; not production acceptance.','No deployment, source network fetch, database reset or user-account change.');ownedProcessStopped=$ownedProcess.HasExited}
    $reportName=if($EvidencePhase -eq 'phase2a'){'publish-2a-smoke.json'}elseif($EvidencePhase -eq 'v04'){'publish-v04-smoke.json'}else{'publish-2b-smoke.json'}
    [IO.File]::WriteAllText((Join-Path $script:ProjectRoot ('docs/evidence/'+$reportName)),(ConvertTo-Json -InputObject $report -Depth 12),[Text.UTF8Encoding]::new($false))
    Write-Output ('Publish smoke PASS '+$checks.Count+' HTTP routes; files='+$files.Count+'; private package files='+$forbidden.Count+'; SQL Test only.')
}finally{
    if($null -ne $connection){$connection.Dispose()}
    if($null -ne $ownedProcess -and -not $ownedProcess.HasExited){Stop-Process -Id $ownedProcess.Id; $ownedProcess.WaitForExit()}
}
