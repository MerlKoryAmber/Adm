# install-iis-site.ps1 - deploys publish\ to IIS as site 'admanager' on port 8080. RUN ELEVATED.
# ASCII-only. Reads lab creds from ..\.env (not in git). Appends to artifacts\install.log.
$ErrorActionPreference = 'Continue'
$log = 'C:\Code\admanager\artifacts\install.log'
function Log($m){ $l=("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),$m); Add-Content $log $l; Write-Host $l }

Log "=== install-iis-site start ==="
$src = 'C:\Code\admanager\publish'
$dst = 'C:\inetpub\admanager'
$site = 'admanager'
$pool = 'admanager'
$port = 8080

# creds from .env
$envMap=@{}; Get-Content 'C:\Code\admanager\.env' | Where-Object {$_ -match '^\s*[^#].*='} | ForEach-Object { $k,$v=$_ -split '=',2; $envMap[$k.Trim()]=$v.Trim() }
$u=$envMap['AD_USER']; $p=$envMap['AD_PASSWORD']
$sam = ($u -split '\\')[-1]
$netbios = ($u -split '\\')[0].Split('.')[0]
$aclUser = "$netbios\$sam"

# 0) stop existing pool/site to release file locks (иначе robocopy виснет на залоченных DLL)
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Test-Path "IIS:\AppPools\$pool") { Stop-WebAppPool -Name $pool -ErrorAction SilentlyContinue }
if (Test-Path "IIS:\Sites\$site")   { Stop-Website -Name $site -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
Get-Process w3wp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dst*" -or $true } | Out-Null
Log "stopped pool/site (if existed)"

# 1) copy publish -> inetpub (ограниченные ретраи, чтобы не виснуть на локах)
New-Item -ItemType Directory -Force -Path $dst | Out-Null
robocopy $src $dst /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
Log "copied publish -> $dst (robocopy rc=$LASTEXITCODE)"
New-Item -ItemType Directory -Force -Path "$dst\logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$dst\App_Data" | Out-Null

# 2) enable stdout log in web.config
try {
  $wc = "$dst\web.config"; [xml]$xml = Get-Content $wc
  $anc = $xml.SelectSingleNode('//aspNetCore')
  if ($anc) { $anc.SetAttribute('stdoutLogEnabled','true'); $anc.SetAttribute('stdoutLogFile','.\logs\stdout'); $xml.Save($wc); Log "web.config stdout log enabled" }
} catch { Log "web.config edit FAIL: $($_.Exception.Message)" }

# 3) ACLs
& icacls $dst /grant "IIS_IUSRS:(OI)(CI)RX" /T /C | Out-Null
& icacls "$dst\logs" /grant "IIS_IUSRS:(OI)(CI)M" /C | Out-Null
& icacls "$dst\App_Data" /grant "IIS_IUSRS:(OI)(CI)M" /C | Out-Null
Log "ACLs set for IIS_IUSRS (RX on app, M on logs+App_Data)"

Import-Module WebAdministration -ErrorAction SilentlyContinue

# 4) (re)create app pool
if (Test-Path "IIS:\AppPools\$pool") { Remove-WebAppPool -Name $pool; Log "removed old app pool" }
New-WebAppPool -Name $pool | Out-Null
Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''      # No Managed Code (ANCM)
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.identityType -Value 4   # ApplicationPoolIdentity
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.loadUserProfile -Value $true
Log "app pool '$pool' created (ApplicationPoolIdentity)"

# 5) app pool environment variables
$appcmd = "$env:windir\System32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='$pool'].environmentVariables.[name='ADMGR_AUTH',value='IIS']" /commit:apphost | Out-Null
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='$pool'].environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='Production']" /commit:apphost | Out-Null
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='$pool'].environmentVariables.[name='ADMGR_AUDIT',value='File']" /commit:apphost | Out-Null
Log "app pool env vars set (ADMGR_AUTH=IIS, ASPNETCORE_ENVIRONMENT=Production, ADMGR_AUDIT=File)"

# 6) (re)create site
if (Test-Path "IIS:\Sites\$site") { Remove-Website -Name $site; Log "removed old site" }
New-Website -Name $site -PhysicalPath $dst -ApplicationPool $pool -Port $port -Force | Out-Null
Log "site '$site' created on port $port -> $dst"

# 7) Windows Auth on, Anonymous off
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false -PSPath 'IIS:\' -Location $site
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true  -PSPath 'IIS:\' -Location $site
Log "auth: Windows enabled, Anonymous disabled"

# 8) start
Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
Start-Website -Name $site -ErrorAction SilentlyContinue
Log "started pool + site"
Log "=== install-iis-site end ==="
