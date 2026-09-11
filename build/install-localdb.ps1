# install-localdb.ps1 - installs SQL Server 2022 Express LocalDB. RUN ELEVATED.
# ASCII-only. Appends to artifacts\install.log.
$ErrorActionPreference = 'Continue'
$log = 'C:\Code\admanager\artifacts\install.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
function Log($m) { $line = ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m); Add-Content -Path $log -Value $line; Write-Host $line }

Log "=== install-localdb start ==="
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$work = 'C:\Code\admanager\artifacts\localdb'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# 1) SSEI bootstrapper (SQL Server 2022 Express)
$ssei = Join-Path $work 'SQL2022-SSEI-Expr.exe'
try {
  Log "downloading SSEI bootstrapper..."
  Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/?linkid=2215160' -OutFile $ssei -UseBasicParsing
  Log ("SSEI downloaded: {0} bytes" -f (Get-Item $ssei).Length)
} catch { Log "SSEI download FAIL: $($_.Exception.Message)"; Log "=== abort ==="; return }

# 2) Download LocalDB media (produces SqlLocalDB.msi)
try {
  Log "downloading LocalDB media via SSEI..."
  $p = Start-Process -FilePath $ssei -ArgumentList '/Action=Download','/MediaType=LocalDB',"/MediaPath=$work",'/Quiet' -Wait -PassThru
  Log "SSEI download action exit: $($p.ExitCode)"
} catch { Log "SSEI run FAIL: $($_.Exception.Message)" }

$msi = Get-ChildItem -Path $work -Filter 'SqlLocalDB.msi' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msi) { Log "SqlLocalDB.msi NOT found under $work"; Log "=== abort ==="; return }
Log "found MSI: $($msi.FullName)"

# 3) Install the MSI silently
try {
  Log "installing SqlLocalDB.msi..."
  $args = "/i `"$($msi.FullName)`" /qn IACCEPTSQLLOCALDBLICENSETERMS=YES"
  $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $args -Wait -PassThru
  Log "msiexec exit: $($p.ExitCode)"
} catch { Log "msiexec FAIL: $($_.Exception.Message)" }

# 4) Create + start the automatic instance
$sqllocaldb = "C:\Program Files\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe"
if (Test-Path $sqllocaldb) {
  try {
    & $sqllocaldb create MSSQLLocalDB 2>&1 | ForEach-Object { Log "sqllocaldb: $_" }
    & $sqllocaldb start  MSSQLLocalDB 2>&1 | ForEach-Object { Log "sqllocaldb: $_" }
    & $sqllocaldb info   MSSQLLocalDB 2>&1 | ForEach-Object { Log "sqllocaldb: $_" }
  } catch { Log "sqllocaldb mgmt FAIL: $($_.Exception.Message)" }
} else {
  Log "SqlLocalDB.exe not found at $sqllocaldb"
}
Log "=== install-localdb end ==="
