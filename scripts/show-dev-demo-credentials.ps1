param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw 'Windows CurrentUser DPAPI required'}
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolved=[IO.Path]::GetFullPath($Path)
if(-not $resolved.StartsWith((Join-Path $project '.local\credentials\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Project private credential artifact required'}
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Windows.Forms
$data=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($resolved),[Text.Encoding]::UTF8.GetBytes('SRS1.2.DevDemo.v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
try{
 $text=[Text.Encoding]::UTF8.GetString($data)
 # Modal display only. Never send credentials to console, transcript, clipboard or files.
 [void][Windows.Forms.MessageBox]::Show($text,'Local Dev/Test credentials - private',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information)
}finally{[Array]::Clear($data,0,$data.Length);$text=$null}
