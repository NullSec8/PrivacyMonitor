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

if (Test-Path $winDir) { Remove-Item $winDir -Recurse -Force }
New-Item -ItemType Directory -Path $winDir -Force | Out-Null

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
1. Copy this ENTIRE folder to your PC (the WebView2 folder must stay next to the exe).
2. Double-click PrivacyMonitor.exe to run.
   No installer, no .NET, no internet, no WebView2 install required — everything is bundled.

REQUIREMENTS
------------
- Windows 10 or 11 (64-bit) only.

FIRST RUN
---------
- The first start may take a few seconds (single-file unpacking).
- Your settings and per-site protection choices are stored under:
  %LocalAppData%\PrivacyMonitor

SECURITY
--------
- The app does not phone home. All analysis runs on your machine.
- To verify the download, check the SHA256 hash on the website.

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
