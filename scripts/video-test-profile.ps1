# Project-local optional tools record, created after operator installation/review. No .env writes.
function Initialize-VideoTestTools {
    $path=Join-Path $script:ProjectRoot '.local/video-tools.json'
    if(-not (Test-Path -LiteralPath $path)){Write-Output 'Video processor tools not configured; dependent E2E remains BLOCKED.';return}
    $tools=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
    foreach($pair in @(@('FfmpegPath',$tools.ffmpegPath),@('FfprobePath',$tools.ffprobePath),@('FfmpegSha256',$tools.ffmpegSha256),@('FfprobeSha256',$tools.ffprobeSha256),@('LicenseReference',$tools.licenseReference),@('ProcessorApproved','true'))){[Environment]::SetEnvironmentVariable(('Video__'+$pair[0]),$pair[1],'Process')}
    Write-Output 'Using hash-pinned installed tools for authorized anonymous SQL Test only; no binary redistribution or Production approval.'
}
