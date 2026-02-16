# Sign PrivacyMonitor.exe so Windows SmartScreen stops showing "not protected".
# Usage:
#   1. Set env vars (once): $env:CERT_PATH = "C:\Path\To\your.pfx"; $env:CERT_PASSWORD = "YourPassword"
#   2. Run: .\sign.ps1
# Or: .\sign.ps1 -CertPath "C:\Certs\my.pfx" -CertPassword "secret"
# See SIGNING.md for how to get a certificate (SignPath free, or buy from DigiCert/Sectigo).

param(
  [string]$CertPath = $env:CERT_PATH,
  [string]$CertPassword = $env:CERT_PASSWORD,
  [string]$ExePath = $null,
  [switch]$CopyToWebsite = $true
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExePath) {
  $ExePath = Join-Path $root "publish\win-x64\PrivacyMonitor.exe"
}

if (-not (Test-Path $ExePath)) {
  Write-Error "EXE not found: $ExePath. Run .\publish.ps1 first."
}

# Find signtool
$signTool = $env:SIGNTOOL_PATH
if (-not $signTool) {
  $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
  if (Test-Path $kitsRoot) {
    $latest = Get-ChildItem $kitsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($latest) {
      $x64 = Join-Path $latest.FullName "x64\signtool.exe"
      if (Test-Path $x64) { $signTool = $x64 }
    }
  }
}

if (-not $signTool -or -not (Test-Path $signTool)) {
  Write-Error "signtool.exe not found. Install Windows SDK or Visual Studio with Windows development. See SIGNING.md."
}

if (-not $CertPath -or -not (Test-Path $CertPath)) {
  Write-Error "Certificate not found. Set CERT_PATH to your .pfx path, or pass -CertPath. See SIGNING.md."
}

Write-Host "Signing: $ExePath"
Write-Host "Cert:    $CertPath"
Write-Host ""

$signArgs = @("sign", "/f", $CertPath, "/tr", "http://timestamp.digicert.com", "/td", "sha256", "/fd", "sha256", $ExePath)
if ($CertPassword) {
  $signArgs = @("sign", "/f", $CertPath, "/p", $CertPassword, "/tr", "http://timestamp.digicert.com", "/td", "sha256", "/fd", "sha256", $ExePath)
}

& $signTool $signArgs
if ($LASTEXITCODE -ne 0) {
  Write-Error "Signing failed (exit $LASTEXITCODE)."
}

Write-Host "Signed successfully."

if ($CopyToWebsite) {
  $websiteExe = Join-Path $root "website\PrivacyMonitor.exe"
  Copy-Item $ExePath $websiteExe -Force
  Write-Host "Copied to website\PrivacyMonitor.exe"
}
