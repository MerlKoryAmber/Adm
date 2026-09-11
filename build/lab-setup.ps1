# lab-setup.ps1 - creates AdManagerLab test objects in merl.loc (idempotent).
# Reads creds from ..\.env (gitignored). Lab only. No RSAT needed (System.DirectoryServices).
# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI.
param([string]$EnvFile = "$PSScriptRoot\..\.env")
$ErrorActionPreference = 'Stop'

$envMap = @{}
Get-Content $EnvFile | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2; $envMap[$k.Trim()] = $v.Trim()
}
$dc = $envMap['AD_DC']; $u = $envMap['AD_USER']; $p = $envMap['AD_PASSWORD']
$testPwd = if ($envMap['LAB_TEST_PWD']) { $envMap['LAB_TEST_PWD'] } else { 'Lab!User-2026' }
$base = 'DC=Merl,DC=loc'
$auth = [System.DirectoryServices.AuthenticationTypes]::Secure

function DE($path) { New-Object System.DirectoryServices.DirectoryEntry("LDAP://$dc/$path", $u, $p, $auth) }
function Exists($path) { try { $e = DE $path; $e.RefreshCache(); return $true } catch { return $false } }

function Ensure-OU($ouName, $parentDn) {
    $dn = "OU=$ouName,$parentDn"
    if (Exists $dn) { Write-Host "OU exists:  $dn" } else {
        $o = (DE $parentDn).Children.Add("OU=$ouName", "organizationalUnit"); $o.CommitChanges()
        Write-Host "OU created: $dn"
    }
    return $dn
}
function Ensure-User($cn, $sam, $parentDn) {
    $dn = "CN=$cn,$parentDn"
    if (Exists $dn) { Write-Host "USER exists: $dn"; return }
    $nu = (DE $parentDn).Children.Add("CN=$cn", "user")
    $nu.Properties['sAMAccountName'].Value = $sam
    $nu.Properties['userPrincipalName'].Value = "$sam@merl.loc"
    $nu.Properties['displayName'].Value = $cn
    $nu.CommitChanges()
    try { $nu.Invoke("SetPassword", $testPwd) | Out-Null; $nu.CommitChanges() }
    catch { Write-Warning "SetPassword failed for $sam : $($_.Exception.InnerException.Message); user stays disabled"; return }
    $nu.Properties['userAccountControl'].Value = 512  # NORMAL_ACCOUNT, enabled
    $nu.CommitChanges()
    Write-Host "USER created: $dn (sam=$sam, enabled)"
}
function Ensure-Group($cn, $sam, $parentDn) {
    $dn = "CN=$cn,$parentDn"
    if (Exists $dn) { Write-Host "GROUP exists: $dn"; return }
    $g = (DE $parentDn).Children.Add("CN=$cn", "group")
    $g.Properties['sAMAccountName'].Value = $sam
    $g.Properties['groupType'].Value = -2147483646  # Global Security
    $g.CommitChanges()
    Write-Host "GROUP created: $dn"
}

Write-Host "== AdManagerLab setup on $dc ($base) =="
$root     = Ensure-OU 'AdManagerLab' $base
$ouUsers  = Ensure-OU 'Users' $root
$ouGroups = Ensure-OU 'Groups' $root
$null     = Ensure-OU 'ServiceAccounts' $root
Ensure-User 'Test User1' 'test.user1' $ouUsers
Ensure-User 'Test User2' 'test.user2' $ouUsers
Ensure-Group 'HelpDesk-L1' 'HelpDesk-L1' $ouGroups
Write-Host "== DONE =="
