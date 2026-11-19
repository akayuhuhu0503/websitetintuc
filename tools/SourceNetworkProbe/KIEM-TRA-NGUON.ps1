$ErrorActionPreference='Stop'
# Published CLI only; no application configuration is loaded. No administrator required.
& (Join-Path $PSScriptRoot 'SourceNetworkProbe.exe') --compare-system-proxy
if($LASTEXITCODE -ne 0){throw 'Source diagnostic tool failed; no application changes made.'}
