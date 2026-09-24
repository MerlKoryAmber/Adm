# install-sqlexpress.ps1 - устанавливает SQL Server Express (служба), создаёт БД AdManager,
# даёт доступ пулу IIS. RUN ELEVATED. ASCII-only. Логирует в artifacts\install-sql.log.
$ErrorActionPreference = 'Stop'
$log = 'C:\Code\admanager\artifacts\install-sql.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
function Log($m){ $l=("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),$m); Add-Content $log $l; Write-Host $l }

Log "=== install-sqlexpress start ==="
$instance = 'SQLEXPRESS'
$svc = "MSSQL`$$instance"

# 0) уже стоит?
$existing = Get-Service -Name $svc -ErrorAction SilentlyContinue
if ($existing) {
    Log "SQL Express instance '$instance' already present (service $svc, status $($existing.Status))"
} else {
    $tmp = Join-Path $env:TEMP 'sqlexpress'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $bootstrap = Join-Path $tmp 'SQL-SSEI-Expr.exe'

    # 1) скачать веб-бутстраппер Express (стабильная fwlink-ссылка Microsoft)
    if (-not (Test-Path $bootstrap)) {
        $url = 'https://go.microsoft.com/fwlink/?linkid=2216019'
        Log "downloading Express bootstrapper..."
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $url -OutFile $bootstrap -UseBasicParsing
        } catch {
            Log "download FAILED: $($_.Exception.Message)"
            Log "Manual: download SQL Server 2022 Express and run: setup /Q /ACTION=Install /FEATURES=SQLEngine /INSTANCENAME=$instance /SECURITYMODE= /SQLSYSADMINACCOUNTS=BUILTIN\Administrators /TCPENABLED=1 /IACCEPTSQLSERVERLICENSETERMS"
            throw
        }
    }

    # 2a) SSEI-бутстраппер сначала СКАЧИВАЕТ media (не ставит напрямую)
    $media = Join-Path $tmp 'media'
    New-Item -ItemType Directory -Force -Path $media | Out-Null
    Log "downloading SQL Express media (~300MB, several minutes)..."
    $dl = Start-Process -FilePath $bootstrap -ArgumentList "/ACTION=Download","/MEDIAPATH=$media","/MEDIATYPE=Core","/QUIET" -Wait -PassThru
    Log "download action exit code: $($dl.ExitCode)"

    # 2b) найти распакованный setup.exe (SSEI кладёт .exe-упаковщик или готовый setup)
    $setup = Get-ChildItem $media -Recurse -Filter 'setup.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $setup) {
        # media может быть самораспаковывающимся .exe (напр. SQLEXPR_x64_ENU.exe) — распакуем
        $pkg = Get-ChildItem $media -Recurse -Filter 'SQLEXPR*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pkg) {
            $extract = Join-Path $media 'extracted'
            Log "extracting package $($pkg.Name)..."
            Start-Process -FilePath $pkg.FullName -ArgumentList "/Q","/X:$extract" -Wait
            $setup = Get-ChildItem $extract -Recurse -Filter 'setup.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        }
    }
    if (-not $setup) { Log "setup.exe not found under $media - aborting"; throw "setup.exe not found" }

    # 2c) тихая установка SQLEngine
    Log "installing SQL Express from $($setup.FullName)..."
    $ins = Start-Process -FilePath $setup.FullName -ArgumentList `
        "/Q","/ACTION=Install","/FEATURES=SQLEngine","/INSTANCENAME=$instance",`
        "/SQLSYSADMINACCOUNTS=BUILTIN\Administrators","/TCPENABLED=1","/IACCEPTSQLSERVERLICENSETERMS" -Wait -PassThru
    Log "installer exit code: $($ins.ExitCode)"
    Start-Sleep -Seconds 5
}

# 3) запустить службу
$svcObj = Get-Service -Name $svc -ErrorAction SilentlyContinue
if ($svcObj) {
    if ($svcObj.Status -ne 'Running') { Start-Service $svc; Log "service $svc started" }
    Set-Service $svc -StartupType Automatic
} else {
    Log "ERROR: service $svc not found after install"
    exit 2
}

# 4) найти sqlcmd
$sqlcmd = (Get-Command sqlcmd -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) {
    $cand = Get-ChildItem "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cand) { $sqlcmd = $cand.FullName }
}
$server = ".\$instance"

# 5) создать БД + дать доступ пулу IIS (ApplicationPoolIdentity сайта admanager)
$poolLogin = 'IIS APPPOOL\admanager'
$tsql = @"
IF DB_ID('AdManager') IS NULL CREATE DATABASE [AdManager];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '$poolLogin')
    CREATE LOGIN [$poolLogin] FROM WINDOWS;
GO
USE [AdManager];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$poolLogin')
    CREATE USER [$poolLogin] FOR LOGIN [$poolLogin];
ALTER ROLE db_owner ADD MEMBER [$poolLogin];
GO
"@
$tsqlFile = Join-Path $env:TEMP 'admgr_dbinit.sql'
Set-Content -Path $tsqlFile -Value $tsql -Encoding ASCII

if ($sqlcmd) {
    Log "running DB init via sqlcmd ($server)..."
    & $sqlcmd -S $server -E -C -b -i $tsqlFile 2>&1 | ForEach-Object { Log "  sqlcmd: $_" }
    Log "sqlcmd exit code: $LASTEXITCODE"
} else {
    Log "sqlcmd not found - run $tsqlFile manually against $server"
}

Log "Connection string: Server=$server;Database=AdManager;Trusted_Connection=True;TrustServerCertificate=True"
Log "=== install-sqlexpress end ==="
