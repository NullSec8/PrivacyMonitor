# Run this ONCE in PowerShell. When prompted, enter root password: S9tone-River&Glass#Sky
# After this, the agent can run SSH commands for you without a password.

$server = "187.77.71.151"
$pubKeyPath = "$env:USERPROFILE\.ssh\id_ed25519.pub"

if (-not (Test-Path $pubKeyPath)) {
    Write-Error "SSH key not found. Run: ssh-keygen -t ed25519 -f $pubKeyPath -N '""'"
    exit 1
}

Write-Host "Adding your SSH key to root@$server ..." -ForegroundColor Cyan
Write-Host "When prompted, enter password: S9tone-River&Glass#Sky" -ForegroundColor Yellow
Write-Host ""

Get-Content $pubKeyPath | ssh -o StrictHostKeyChecking=accept-new "root@$server" "mkdir -p .ssh && chmod 700 .ssh && cat >> .ssh/authorized_keys && chmod 600 .ssh/authorized_keys && echo 'Key added. You can now use: ssh root@$server'"
