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

# Paths (relative to this script's directory = work)
$WebsitePath = "wpf-browser\website"
$ServerPath  = "browser-update-server\server"
$BuildsPath  = "browser-update-server\builds"

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Deploy-Website {
    if (-not (Test-Path "$here\$WebsitePath")) {
        Write-Host "Website folder not found: $here\$WebsitePath" -ForegroundColor Yellow
        return
    }
    Write-Host "Uploading website to $VPS_USER@${VPS_HOST}:$REMOTE_BASE/website/ ..." -ForegroundColor Cyan
    scp -r "$here\$WebsitePath\*" "${VPS_USER}@${VPS_HOST}:${REMOTE_BASE}/website/"
    Write-Host "Website done." -ForegroundColor Green
}

function Deploy-Server {
    if (-not (Test-Path "$here\$ServerPath\server.js")) {
        Write-Host "Server folder not found: $here\$ServerPath" -ForegroundColor Yellow
        return
    }
    Write-Host "Uploading server to $VPS_USER@${VPS_HOST}:$REMOTE_BASE/server/ ..." -ForegroundColor Cyan
    scp -r "$here\$ServerPath\*" "${VPS_USER}@${VPS_HOST}:${REMOTE_BASE}/server/"
    Write-Host "Server done. On VPS run: cd $REMOTE_BASE/server && npm install --omit=dev && sudo systemctl restart browser-update-server" -ForegroundColor Yellow
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
if ($DeployWebsite) { Deploy-Website }
if ($DeployServer)  { Deploy-Server }
if ($DeployBuilds)  { Deploy-Builds }

if (-not $DeployWebsite -and -not $DeployServer -and -not $DeployBuilds) {
    Write-Host "Nothing selected. Edit this script and set DeployWebsite/DeployServer/DeployBuilds to `$true." -ForegroundColor Yellow
} else {
    Write-Host "VPS update finished." -ForegroundColor Green
}
