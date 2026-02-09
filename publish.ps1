# Build and publish PrivacyMonitor for distribution (website + zip for any PC)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root 'PrivacyMonitor.csproj'
$publishDir = Join-Path $root 'publish'
$winDir = Join-Path $publishDir 'win-x64'
$websiteDir = Join-Path $root 'website'
$assetsDir = Join-Path $websiteDir 'assets'

# Get version from project (optional <Version> in csproj)
$versionMatch = Select-String -Path $projectPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
$version = if ($versionMatch) { $versionMatch.Matches.Groups[1].Value } else { '1.0.0' }

Write-Host "Publishing PrivacyMonitor v$version (win-x64, self-contained single-file)..."

if (-not (Test-Path $winDir)) { New-Item -ItemType Directory -Path $winDir -Force | Out-Null }

# First publish: get exe + WebView2 folder (overwrites existing)
dotnet publish $projectPath -c Release -r win-x64 -o $winDir `
  -p:SelfContained=true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

$exePath = Join-Path $winDir 'PrivacyMonitor.exe'
if (-not (Test-Path $exePath)) {
  Write-Error "Publish did not produce PrivacyMonitor.exe in $winDir"
}

# Embed WebView2 in exe so one exe works on any PC (extract on first run)
$webView2Folder = Join-Path $winDir 'WebView2'
$runtimeZip = Join-Path $root 'WebView2Runtime.zip'
if (Test-Path $webView2Folder) {
  Write-Host "Creating WebView2Runtime.zip for single-exe embedding..."
  if (Test-Path $runtimeZip) { Remove-Item $runtimeZip -Force }
  Compress-Archive -Path $webView2Folder -DestinationPath $runtimeZip -CompressionLevel Optimal -Force
  Write-Host "Re-publishing with embedded WebView2 (one exe for any PC)..."
  dotnet publish $projectPath -c Release -r win-x64 -o $winDir `
    -p:SelfContained=true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
  Remove-Item $runtimeZip -Force -ErrorAction SilentlyContinue
}

# Optional: code-sign the exe so Windows won't say "not protected" (SmartScreen).
# Set CERT_PATH (path to .pfx) and CERT_PASSWORD. See SIGNING.md for details.
# SignPath Foundation (signpath.org) offers free signing for open source.
$certPath = $env:CERT_PATH
$certPass = $env:CERT_PASSWORD
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
if ($signTool -and $certPath -and (Test-Path $certPath)) {
  Write-Host "Signing executable (Windows will not show 'not protected')..."
  try {
    $signArgs = @("sign", "/f", $certPath, "/tr", "http://timestamp.digicert.com", "/td", "sha256", "/fd", "sha256", $exePath)
    if ($certPass) { $signArgs = @("sign", "/f", $certPath, "/p", $certPass, "/tr", "http://timestamp.digicert.com", "/td", "sha256", "/fd", "sha256", $exePath) }
    & $signTool $signArgs
    if ($LASTEXITCODE -eq 0) { Write-Host "Signed successfully." } else { Write-Host "Signing failed (exit $LASTEXITCODE). Exe is still valid." }
  } catch {
    Write-Host "Signing failed: $_ . Exe is still valid."
  }
} else {
  Write-Host "Skipping code signing. To sign: set CERT_PATH and CERT_PASSWORD (and optionally SIGNTOOL_PATH). See SIGNING.md."
}

# Copy exe to website for direct download
Copy-Item $exePath (Join-Path $websiteDir 'PrivacyMonitor.exe') -Force
Write-Host "Copied exe to website/PrivacyMonitor.exe"

# SHA256 and size for verification
$hash = (Get-FileHash $exePath -Algorithm SHA256).Hash
$sizeBytes = (Get-Item $exePath).Length
$sizeMB = [math]::Round($sizeBytes / 1MB, 2)

# Distribution zip: exe + INSTALL.txt for any PC
$zipName = "PrivacyMonitor-$version-win-x64.zip"
$zipPath = Join-Path $publishDir $zipName
$installTxt = @"
Privacy Monitor - Install on any Windows PC
==========================================

VERSION: $version
BUILD: $(Get-Date -Format 'yyyy-MM-dd')

QUICK START
-----------
1. Download PrivacyMonitor.exe (or this whole folder).
2. Double-click PrivacyMonitor.exe to run.
   One exe — no installer, no .NET, no WebView2 install. Works on any Windows 10/11 (64-bit) PC.

REQUIREMENTS
------------
- Windows 10 or 11 (64-bit) only.

FIRST RUN
---------
- If you only have the exe: first run may take 20–40 seconds (one-time setup), then the app opens.
- Later runs start normally.
- Your settings and per-site protection choices are stored under:
  %LocalAppData%\PrivacyMonitor

SECURITY
--------
- The app does not phone home. All analysis runs on your machine.
- To verify the download, check the SHA256 hash on the website.

WINDOWS "NOT PROTECTED" / SMARTSCREEN MESSAGE
----------------------------------------------
If Windows says "Windows protected your PC" or "This app might harm your device",
it is because the app is not code-signed (no certificate). The app is safe to run.

What to do:
1. Click "More info".
2. Click "Run anyway".

You can verify the file is unchanged by checking its SHA256 hash (see website or
build-info). No code runs until you run the exe yourself.

Unzip this file on any Windows 10/11 (64-bit) PC and run PrivacyMonitor.exe.
"@
$installPath = Join-Path $winDir 'INSTALL.txt'
$installTxt | Set-Content $installPath -Encoding UTF8

# Create zip from win-x64 folder (exe + INSTALL + any runtimes)
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $winDir '*') -DestinationPath $zipPath
Write-Host "Created distribution zip: $zipPath"

# Copy zip to website so users can download either exe or full zip
Copy-Item $zipPath (Join-Path $websiteDir $zipName) -Force
Write-Host "Copied zip to website/$zipName"

# Build info for download page (JSON) - after zip exists
$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
$buildInfo = @{
  version   = $version
  date      = (Get-Date -Format 'yyyy-MM-dd')
  sha256    = $hash.ToLower()
  sizeMB    = $sizeMB
  sizeBytes = $sizeBytes
  zipName   = $zipName
  zipSizeMB = $zipSizeMB
} | ConvertTo-Json
$buildInfoPath = Join-Path $assetsDir 'build-info.json'
$buildInfo | Set-Content $buildInfoPath -Encoding UTF8
Write-Host "Written build info to website/assets/build-info.json"

Write-Host ""
Write-Host "Done. Outputs:"
Write-Host "  - $winDir\PrivacyMonitor.exe (single-file, run on any PC)"
Write-Host "  - $zipPath (zip for distribution)"
Write-Host "  - website\PrivacyMonitor.exe and website\$zipName (for your website)"
Write-Host "  - SHA256: $hash"
