# redeploy.ps1 - kills a stuck deploy then runs the fixed install-iis-site. RUN ELEVATED. ASCII-only.
$ErrorActionPreference = 'Continue'
Get-Process robocopy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -like '*install-iis-site*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1
& "$PSScriptRoot\install-iis-site.ps1"
