# One-time: copy your SSH public key to the VPS so deploy no longer asks for a password.
# Run this in a real PowerShell window (not from IDE), then type your VPS password when asked.
# After this, .\update-vps.ps1 will work without a password.

$VPS_USER = "endri"
$VPS_HOST = "187.77.71.151"
$keyPath = "$env:USERPROFILE\.ssh\id_ed25519.pub"
if (-not (Test-Path $keyPath)) { $keyPath = "$env:USERPROFILE\.ssh\id_rsa.pub" }
if (-not (Test-Path $keyPath)) {
    Write-Host "No SSH public key found. Run: ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\id_ed25519" -ForegroundColor Yellow
    exit 1
}
Write-Host "Copying your public key to ${VPS_USER}@${VPS_HOST} ..." -ForegroundColor Cyan
Write-Host "You will be asked for your VPS password once." -ForegroundColor Gray
Get-Content $keyPath | ssh "${VPS_USER}@${VPS_HOST}" "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && echo 'Key added.'"
if ($LASTEXITCODE -eq 0) {
    Write-Host "Done. From now on, .\update-vps.ps1 will not ask for a password." -ForegroundColor Green
} else {
    Write-Host "Failed. Check that you typed the correct password and the VPS is reachable." -ForegroundColor Red
}
