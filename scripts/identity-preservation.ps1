[CmdletBinding()]
param([string]$Output='.local/evidence/phase2b/dev-identity-before.json',[string]$CompareTo)
. (Join-Path $PSScriptRoot 'common.ps1')
$conn=New-Object System.Data.SqlClient.SqlConnection (Get-ProjectConnectionBuilder 'LandPortal_Dev').ConnectionString
function ReadJson([string]$sql){$cmd=$conn.CreateCommand();$cmd.CommandText=$sql;$reader=$cmd.ExecuteReader();$buf=New-Object Text.StringBuilder;try{while($reader.Read()){[void]$buf.Append($reader.GetString(0))}}finally{$reader.Dispose();$cmd.Dispose()};return $buf.ToString()}
$conn.Open()
try{
 $cmd=$conn.CreateCommand();$cmd.CommandText="SELECT DB_NAME(),CAST(SERVERPROPERTY('MachineName') AS nvarchar(200))";$r=$cmd.ExecuteReader();$r.Read()|Out-Null;if($r.GetString(0) -cne 'LandPortal_Dev' -or $r.GetString(1) -ine $env:COMPUTERNAME){throw 'Identity preservation only targets local project Dev.'};$r.Dispose();$cmd.Dispose()
 $parts=@((ReadJson 'SELECT Id,UserName,Email,PasswordHash,SecurityStamp,ConcurrencyStamp,LockoutEnd,LockoutEnabled FROM dbo.AspNetUsers ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES'),(ReadJson 'SELECT Id,Name,NormalizedName,ConcurrencyStamp FROM dbo.AspNetRoles ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES'),(ReadJson 'SELECT UserId,RoleId FROM dbo.AspNetUserRoles ORDER BY UserId,RoleId FOR JSON PATH'),(ReadJson 'SELECT UserId,ClaimType,ClaimValue FROM dbo.AspNetUserClaims ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES'),(ReadJson 'SELECT RoleId,ClaimType,ClaimValue FROM dbo.AspNetRoleClaims ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES'))
 $sha=[Security.Cryptography.SHA256]::Create();try{$digest=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($parts -join "`n"))))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
 $record=[ordered]@{database='LandPortal_Dev';utc=[DateTimeOffset]::UtcNow.ToString('O');identityDigest=$digest;status='RECORDED';containsRawSecrets=$false}
 if($CompareTo){$before=Get-Content -LiteralPath (Join-Path $script:ProjectRoot $CompareTo) -Raw -Encoding UTF8|ConvertFrom-Json;$record.status=if($before.identityDigest -ceq $digest){'PASS'}else{'FAIL'}}
 $path=[IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $Output));$allow=(Join-Path $script:ProjectRoot '.local/evidence')+'\';if(-not $path.StartsWith($allow,[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence path must remain project-local.'};[IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null;[IO.File]::WriteAllText($path,(ConvertTo-Json $record),[Text.UTF8Encoding]::new($false));Write-Output ($record.status+' Dev identity/credentials/stamps/roles/claims preservation; raw values not logged.');if($record.status -eq 'FAIL'){exit 1}
}finally{$conn.Dispose()}
