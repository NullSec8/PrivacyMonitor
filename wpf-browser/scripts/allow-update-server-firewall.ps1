# Add Windows Firewall outbound rule so Privacy Monitor can reach the update server.
# Allows ONLY: PrivacyMonitor.exe -> 187.77.71.151 port 3000 (TCP).
# Must run as Administrator.

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$updateServerIp = "187.77.71.151"
$updateServerPort = 3000
$ruleName = "Privacy Monitor - Update server"

# Find ALL PrivacyMonitor.exe copies so the rule applies no matter which one you run
param(
    [string]$ExePath   # Optional: single path; if set, only that path is used
)
$candidates = @(
    (Join-Path $env:LOCALAPPDATA "PrivacyMonitor\PrivacyMonitor.exe"),
    (Join-Path $PSScriptRoot "..\publish\win-x64\PrivacyMonitor.exe"),
    (Join-Path $PSScriptRoot "..\PrivacyMonitor.exe"),
    (Join-Path $PSScriptRoot "PrivacyMonitor.exe"),
    (Join-Path $env:USERPROFILE "Downloads\PrivacyMonitor.exe"),
    (Join-Path $env:USERPROFILE "Desktop\PrivacyMonitor.exe")
)
if ($ExePath -and (Test-Path $ExePath)) {
    $pathsToAllow = @((Resolve-Path $ExePath).Path)
} else {
    $pathsToAllow = @()
    foreach ($p in $candidates) {
        if (Test-Path $p) { $pathsToAllow += (Resolve-Path $p).Path }
    }
}
if ($pathsToAllow.Count -eq 0) {
    Write-Host "PrivacyMonitor.exe not found. Run with: .\allow-update-server-firewall.ps1 -ExePath 'C:\path\to\PrivacyMonitor.exe'" -ForegroundColor Red
    exit 1
}

# Remove existing rules with same name (idempotent)
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing rules: $ruleName" -ForegroundColor Yellow
    $existing | Remove-NetFirewallRule
}

# Add one outbound rule per exe path so every copy of the app can reach the update server
foreach ($path in $pathsToAllow) {
    New-NetFirewallRule -DisplayName $ruleName `
        -Direction Outbound `
        -Program $path `
        -RemoteAddress $updateServerIp `
        -RemotePort $updateServerPort `
        -Protocol TCP `
        -Action Allow `
        -Profile Any
    Write-Host "  Allowed: $path" -ForegroundColor Gray
}

Write-Host "Firewall rules added: $ruleName" -ForegroundColor Green
Write-Host "  Outbound TCP to $updateServerIp port $updateServerPort for $($pathsToAllow.Count) executable(s)." -ForegroundColor Gray
Write-Host "`nRestart Privacy Monitor and try Check for updates again." -ForegroundColor Cyan
