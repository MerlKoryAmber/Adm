# install-server.ps1 - installs IIS + ASP.NET Core 8 Hosting Bundle. RUN ELEVATED.
# ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI). Logs to artifacts\install.log.
$ErrorActionPreference = 'Continue'
$log = 'C:\Code\admanager\artifacts\install.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
function Log($m) { $line = ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m); Add-Content -Path $log -Value $line; Write-Host $line }

Log "=== install-server start ==="
Log ("elevated: " + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

# --- IIS features ---
$features = @(
  'IIS-WebServerRole','IIS-WebServer','IIS-CommonHttpFeatures','IIS-StaticContent',
  'IIS-DefaultDocument','IIS-HttpErrors','IIS-RequestFiltering','IIS-HttpCompressionStatic',
  'IIS-ISAPIExtensions','IIS-ISAPIFilter','IIS-NetFxExtensibility45','IIS-ASPNET45',
  'IIS-WindowsAuthentication','IIS-ManagementConsole'
)
foreach ($f in $features) {
  try {
    $st = (Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction Stop).State
    if ($st -eq 'Enabled') { Log "IIS feature already enabled: $f" }
    else { Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart -ErrorAction Stop | Out-Null; Log "IIS feature enabled: $f" }
  } catch { Log "IIS feature FAIL $f : $($_.Exception.Message)" }
}

# --- ASP.NET Core 8 Hosting Bundle ---
try {
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  $url = 'https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe'
  $exe = Join-Path $env:TEMP 'dotnet-hosting-8-win.exe'
  Log "downloading hosting bundle: $url"
  Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
  Log ("downloaded: {0} bytes" -f (Get-Item $exe).Length)
  Log "installing hosting bundle (quiet)"
  $p = Start-Process -FilePath $exe -ArgumentList '/quiet','/norestart' -Wait -PassThru
  Log "hosting bundle exit code: $($p.ExitCode)"
} catch { Log "HOSTING BUNDLE FAIL: $($_.Exception.Message)" }

# --- restart IIS ---
try { & iisreset | Out-Null; Log "iisreset done" } catch { Log "iisreset FAIL: $($_.Exception.Message)" }

Log "=== install-server end ==="
