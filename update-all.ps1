# Update all: restore packages and build.
# Run from: c:\Users\endri\OneDrive\Desktop\work

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

Write-Host "=== Restore & build ===" -ForegroundColor Cyan

# .NET (PrivacyMonitor)
$csproj = Join-Path $here "wpf-browser\PrivacyMonitor.csproj"
if (Test-Path $csproj) {
    Write-Host "Dotnet restore + build: wpf-browser..." -ForegroundColor Yellow
    Set-Location (Join-Path $here "wpf-browser")
    dotnet restore
    dotnet build --no-restore
    Set-Location $here
    Write-Host "WPF build done." -ForegroundColor Green
} else {
    Write-Host "PrivacyMonitor.csproj not found, skipping." -ForegroundColor Yellow
}

# Node (browser-update-server)
$serverPackage = Join-Path $here "browser-update-server\server\package.json"
if (Test-Path $serverPackage) {
    Write-Host "npm install: browser-update-server/server..." -ForegroundColor Yellow
    Set-Location (Join-Path $here "browser-update-server\server")
    npm install
    Set-Location $here
    Write-Host "Server deps done." -ForegroundColor Green
} else {
    Write-Host "browser-update-server/server not found, skipping." -ForegroundColor Yellow
}

Write-Host "=== Update all finished ===" -ForegroundColor Green
Write-Host "Next: git add/commit/push (GitHub), or .\update-vps.ps1 (VPS)." -ForegroundColor Gray
