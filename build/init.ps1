# init.ps1 - creates the solution and adds projects (project refs are already in .csproj).
# Requires .NET 8 SDK. ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path "$root/AdManager.sln")) {
    dotnet new sln -n AdManager
}

$projects = @(
    'src/AdManager.Domain/AdManager.Domain.csproj',
    'src/AdManager.Application/AdManager.Application.csproj',
    'src/AdManager.Infrastructure.Ad/AdManager.Infrastructure.Ad.csproj',
    'src/AdManager.Infrastructure.Exchange/AdManager.Infrastructure.Exchange.csproj',
    'src/AdManager.Infrastructure.Data/AdManager.Infrastructure.Data.csproj',
    'src/AdManager.Infrastructure.Automation/AdManager.Infrastructure.Automation.csproj',
    'src/AdManager.Web/AdManager.Web.csproj',
    'tests/AdManager.Domain.Tests/AdManager.Domain.Tests.csproj',
    'tests/AdManager.Integration.Tests/AdManager.Integration.Tests.csproj'
)
foreach ($p in $projects) {
    if (Test-Path $p) { dotnet sln add $p } else { Write-Warning "missing: $p" }
}
Write-Host "Done. Next: dotnet build" -ForegroundColor Green
