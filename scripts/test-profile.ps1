# Call only after common.ps1. Fixtures are copied from project stores, never fetched.
function Initialize-ProjectTestProfile {
    $testRoot=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot '.local/tests/phase2a'))
    foreach($name in @('MediaRoot','SnapshotRoot','TempRoot','KeysRoot')) {
        $key='Storage__'+$name;$source=[Environment]::GetEnvironmentVariable($key,'Process')
        $folder=switch($name){'MediaRoot' {'media'} 'SnapshotRoot' {'snapshots'} 'TempRoot' {'temp'} 'KeysRoot' {'keys'}}
        $destination=Join-Path $testRoot $folder
        if($name -in @('MediaRoot','SnapshotRoot')) {
            if([string]::IsNullOrWhiteSpace($source)){throw "Missing $key for isolated test fixtures."}
            if(-not [IO.Path]::IsPathRooted($source)){$source=Join-Path $script:ProjectRoot $source};$source=[IO.Path]::GetFullPath($source)
            if(-not $source.StartsWith((Join-Path $script:ProjectRoot '.local')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test fixture source escaped project .local.'}
            if($source -cne $destination) {
                [IO.Directory]::CreateDirectory($destination)|Out-Null
                foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
                    if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing reparse test fixture.'}
                    $relative=$file.FullName.Substring($source.TrimEnd('\').Length+1);$target=Join-Path $destination $relative
                    [IO.Directory]::CreateDirectory((Split-Path $target))|Out-Null
                    if(-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -cne (Get-FileHash -LiteralPath $file.FullName).Hash){Copy-Item -LiteralPath $file.FullName -Destination $target}
                }
            }
        }
        [IO.Directory]::CreateDirectory($destination)|Out-Null
        [Environment]::SetEnvironmentVariable($key,$destination,'Process')
    }
    $builder=Get-ProjectConnectionBuilder 'LandPortal_Test'
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Portal',$builder.ConnectionString,'Process')
    [Environment]::SetEnvironmentVariable('Sql__ExpectedDatabase','LandPortal_Test','Process')
    [Environment]::SetEnvironmentVariable('Media__AllowUnscannedDevelopmentUploads','true','Process')
    Write-Output 'Test profile: LandPortal_Test and isolated .local/tests/phase2a/{media,snapshots,temp,keys}; source fixture copies only.'
}
