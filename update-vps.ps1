# Update VPS: upload website, optional server and builds.
# Run from: c:\Users\endri\OneDrive\Desktop\work
# Requires: ssh/scp (e.g. OpenSSH client on Windows).

$VPS_USER = "endri"
$VPS_HOST = "187.77.71.151"
$REMOTE_BASE = "/home/$VPS_USER/browser_project"

# What to deploy (set $true to include)
$DeployWebsite = $true
$DeployServer  = $true
$DeployBuilds  = $true
# Fix ownership on VPS before upload (avoids "Permission denied") and restart server after
$FixPermissionsFirst = $true
$RestartServerAfter  = $true
# Optional: set $env:VPS_SUDO_PW in your session so sudo on VPS can run non-interactively (never commit the password)

# Paths (relative to this script's directory = work)
$WebsitePath = "wpf-browser\website"
$ServerPath  = "browser-update-server\server"
$BuildsPath  = "browser-update-server\builds"

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Fix-RemotePermissions {
    Write-Host "Fixing ownership on VPS ($REMOTE_BASE) ..." -ForegroundColor Cyan
    $dirs = @()
    if ($DeployWebsite) { $dirs += "website" }
    if ($DeployServer)  { $dirs += "server" }
    if ($DeployBuilds)  { $dirs += "builds" }
    if ($dirs.Count -eq 0) { return }
    $chownDirs = ($dirs | ForEach-Object { "$REMOTE_BASE/$_" }) -join " "
    if ($env:VPS_SUDO_PW) {
        $env:VPS_SUDO_PW | ssh "${VPS_USER}@${VPS_HOST}" "sudo -S chown -R ${VPS_USER}:${VPS_USER} $chownDirs"
    } else {
        ssh "${VPS_USER}@${VPS_HOST}" "sudo chown -R ${VPS_USER}:${VPS_USER} $chownDirs"
    }
    if ($LASTEXITCODE -eq 0) { Write-Host "Permissions fixed." -ForegroundColor Green } else { Write-Host "Fix permissions failed (continue anyway)." -ForegroundColor Yellow }
}

function Restart-RemoteServer {
    Write-Host "Restarting browser-update-server on VPS ..." -ForegroundColor Cyan
    if ($env:VPS_SUDO_PW) {
        $env:VPS_SUDO_PW | ssh "${VPS_USER}@${VPS_HOST}" "cd $REMOTE_BASE/server && npm install --omit=dev && sudo -S systemctl restart browser-update-server"
    } else {
        ssh "${VPS_USER}@${VPS_HOST}" "cd $REMOTE_BASE/server && npm install --omit=dev && sudo systemctl restart browser-update-server"
    }
    if ($LASTEXITCODE -eq 0) { Write-Host "Server restarted." -ForegroundColor Green } else { Write-Host "Restart failed." -ForegroundColor Yellow }
}

function Deploy-Website {
    if (-not (Test-Path "$here\$WebsitePath")) {
        Write-Host "Website folder not found: $here\$WebsitePath" -ForegroundColor Yellow
        return
    }
    # Copy to temp excluding *.zip (saves ~600 MB upload; site only offers EXE)
    $tempWeb = Join-Path $env:TEMP "vps_website_deploy"
    if (Test-Path $tempWeb) { Remove-Item $tempWeb -Recurse -Force }
    New-Item -ItemType Directory -Force $tempWeb | Out-Null
    robocopy "$here\$WebsitePath" $tempWeb /E /XF *.zip /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host "Robocopy failed." -ForegroundColor Red; return }
    Write-Host "Uploading website to $VPS_USER@${VPS_HOST}:$REMOTE_BASE/website/ ..." -ForegroundColor Cyan
    scp -r "$tempWeb\*" "${VPS_USER}@${VPS_HOST}:${REMOTE_BASE}/website/"
    Remove-Item $tempWeb -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Website done." -ForegroundColor Green
}

function Deploy-Server {
    if (-not (Test-Path "$here\$ServerPath\server.js")) {
        Write-Host "Server folder not found: $here\$ServerPath" -ForegroundColor Yellow
        return
    }
    # Copy to temp excluding node_modules (native modules are OS-specific; VPS runs npm install on restart)
    $tempSrv = Join-Path $env:TEMP "vps_server_deploy"
    if (Test-Path $tempSrv) { Remove-Item $tempSrv -Recurse -Force }
    New-Item -ItemType Directory -Force $tempSrv | Out-Null
    robocopy "$here\$ServerPath" $tempSrv /E /XD node_modules /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host "Robocopy failed." -ForegroundColor Red; return }
    Write-Host "Uploading server to $VPS_USER@${VPS_HOST}:$REMOTE_BASE/server/ ..." -ForegroundColor Cyan
    scp -r "$tempSrv\*" "${VPS_USER}@${VPS_HOST}:${REMOTE_BASE}/server/"
    Remove-Item $tempSrv -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Server done." -ForegroundColor Green
}

function Deploy-Builds {
    if (-not (Test-Path "$here\$BuildsPath")) {
        Write-Host "Builds folder not found: $here\$BuildsPath" -ForegroundColor Yellow
        return
    }
    Write-Host "Uploading builds to $VPS_USER@${VPS_HOST}:$REMOTE_BASE/builds/ ..." -ForegroundColor Cyan
    scp -r "$here\$BuildsPath\*" "${VPS_USER}@${VPS_HOST}:${REMOTE_BASE}/builds/"
    Write-Host "Builds done." -ForegroundColor Green
}

# Run
if ($FixPermissionsFirst -and ($DeployWebsite -or $DeployServer -or $DeployBuilds)) { Fix-RemotePermissions }
if ($DeployWebsite) { Deploy-Website }
if ($DeployServer)  { Deploy-Server }
if ($DeployBuilds)  { Deploy-Builds }
if ($RestartServerAfter -and $DeployServer) { Restart-RemoteServer }

if (-not $DeployWebsite -and -not $DeployServer -and -not $DeployBuilds) {
    Write-Host "Nothing selected. Edit this script and set DeployWebsite/DeployServer/DeployBuilds to `$true." -ForegroundColor Yellow
} else {
    Write-Host "VPS update finished." -ForegroundColor Green
}
