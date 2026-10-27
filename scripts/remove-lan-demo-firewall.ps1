[CmdletBinding()]
param()
$name='LandPortal-IsolatedLanDemo-5188'
$rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
if(-not $rule){Write-Output 'No project-owned LAN demo firewall rule exists; nothing changed.';return}
if($rule.Description -cne 'Owner-approved isolated LandPortal LAN demo only'){throw 'Firewall rule ownership mismatch; nothing removed'}
Remove-NetFirewallRule -Name $name
Write-Output 'Only the project-owned demo firewall rule removed.'
