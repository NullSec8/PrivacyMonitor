# Uninstall Privacy Monitor from this PC.
# Run on each computer where you want to remove the old app before installing the new EXE.
#
# Usage:
#   .\uninstall-privacymonitor.ps1           # Stop process, remove app data, remove EXE from script folder
#   .\uninstall-privacymonitor.ps1 -Search   # Also search this user's profile for PrivacyMonitor.exe and remove

param(
    [switch]$Search   # Search for PrivacyMonitor.exe under user profile and remove found folders
)

$ErrorActionPreference = 'Stop'

Write-Host "Privacy Monitor - Uninstall (this PC)" -ForegroundColor Cyan

# 1. Stop running process
$proc = Get-Process -Name "PrivacyMonitor" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Stopping PrivacyMonitor process..." -ForegroundColor Yellow
    Stop-Process -Name "PrivacyMonitor" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "Stopped." -ForegroundColor Green
} else {
    Write-Host "No PrivacyMonitor process running." -ForegroundColor Gray
}

# 2. Remove app data (settings, learned data)
$appData = Join-Path $env:LocalAppData "PrivacyMonitor"
if (Test-Path $appData) {
    Write-Host "Removing app data: $appData" -ForegroundColor Yellow
    Remove-Item -Path $appData -Recurse -Force
    Write-Host "App data removed." -ForegroundColor Green
} else {
    Write-Host "No app data folder found." -ForegroundColor Gray
}

# 3. Remove EXE from script directory (if script is next to PrivacyMonitor.exe)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exeHere = Join-Path $scriptDir "PrivacyMonitor.exe"
if (Test-Path $exeHere) {
    Write-Host "Removing: $exeHere" -ForegroundColor Yellow
    Remove-Item -Path $exeHere -Force
    Write-Host "EXE removed." -ForegroundColor Green
}

# 4. Optional: search common locations for PrivacyMonitor.exe and remove
if ($Search) {
    $locations = @(
        $env:USERPROFILE,
        (Join-Path $env:USERPROFILE "Desktop"),
        (Join-Path $env:USERPROFILE "Downloads"),
        (Join-Path $env:USERPROFILE "Documents")
    )
    Write-Host "`nSearching for PrivacyMonitor.exe in Desktop, Downloads, Documents..." -ForegroundColor Yellow
    $found = Get-ChildItem -Path $locations -Filter "PrivacyMonitor.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue -Force
    foreach ($f in $found) {
        $dir = $f.DirectoryName
        Write-Host "  Found: $($f.FullName)" -ForegroundColor Gray
        $confirm = Read-Host "  Remove this folder? (y/n)"
        if ($confirm -eq 'y') {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  Removed." -ForegroundColor Green
        }
    }
    if (-not $found) { Write-Host "  None found." -ForegroundColor Gray }
}

Write-Host "`nDone. You can now install the new Privacy Monitor EXE." -ForegroundColor Green
