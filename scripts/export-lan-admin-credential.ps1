[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CredentialPath)
$ErrorActionPreference='Stop'
# Local handoff only: decode JSON without showing the secret or altering Identity.
$who=[Security.Principal.WindowsIdentity]::GetCurrent();$sid=$who.User
if(-not $who.IsAuthenticated -or -not $who.Name.EndsWith('\ADMIN',[StringComparison]::OrdinalIgnoreCase)){throw 'Windows ADMIN required'}
$base=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) 'LandPortal-LanDemo-Credentials')).TrimEnd('\')+'\'
$inputPath=[IO.Path]::GetFullPath($CredentialPath)
if(-not $inputPath.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Private ADMIN credential folder required'}
for($node=$inputPath;$node;$node=[IO.Path]::GetDirectoryName($node)){
 if((Test-Path -LiteralPath $node) -and ((Get-Item -LiteralPath $node -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse paths refused'}
}
function RequirePrivateAcl([string]$Path){
 $acl=Get-Acl -LiteralPath $Path
 $rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
 if(-not $acl.AreAccessRulesProtected -or -not $sid.Equals($acl.GetOwner([Security.Principal.SecurityIdentifier])) -or $rules.Count -ne 1 -or -not $sid.Equals($rules[0].IdentityReference) -or $rules[0].AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $rules[0].FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl){throw 'Only current ADMIN may access credential'}
}
RequirePrivateAcl $inputPath
RequirePrivateAcl ([IO.Path]::GetDirectoryName($inputPath))
$credential=Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8|ConvertFrom-Json
if($credential.userName -cne 'lan-ba-admin' -or $credential.database -cne 'LandPortal_LanDemo'){throw 'Exact LAN admin credential required'}
if($credential.password -isnot [string] -or $credential.password.Length -ne 24 -or $credential.password -match '\s' -or $credential.password -cnotmatch '[A-Z]' -or $credential.password -cnotmatch '[a-z]' -or $credential.password -notmatch '[0-9]' -or $credential.password -notmatch '[^a-zA-Z0-9]'){throw 'Expected 24-character BA temporary password'}
$output=Join-Path ([IO.Path]::GetDirectoryName($inputPath)) 'mat-khau-dang-nhap.txt'
$file=[IO.File]::Open($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);$file.Dispose()
$acl=[Security.AccessControl.FileSecurity]::new();$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $output -AclObject $acl
RequirePrivateAcl $output
# Exactly one decoded password line, no JSON quotes/escapes or trailing newline.
[IO.File]::WriteAllText($output,$credential.password,[Text.UTF8Encoding]::new($false))
if([IO.File]::ReadAllText($output,[Text.Encoding]::UTF8) -cne $credential.password){throw 'Decoded export readback mismatch'}
$credential=$null
Write-Output ('EXPORTED: '+$output+' | ACL ADMIN only | 24 characters | no password reset')
