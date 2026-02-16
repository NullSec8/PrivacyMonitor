# PrivacyMonitor & browser project

- **PrivacyMonitor**: WPF + WebView2 desktop app (network interceptor, protection, risk scoring).
- **browser-update-server**: Node server for updates and optional 2FA (deploy to VPS).
- **Scripts**: `update-all.ps1` (restore & build), `update-vps.ps1` (deploy to VPS).

## Quick start

```powershell
cd c:\Users\endri\OneDrive\Desktop\work
.\update-all.ps1                    # restore packages, build
dotnet run --project wpf-browser\PrivacyMonitor.csproj   # run app
```

## Docs

| File | Description |
|------|-------------|
| [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) | Folder layout and organization |
| [DEPLOYMENT.md](DEPLOYMENT.md) | GitHub (push) and VPS (update-vps.ps1) |
| [wpf-browser/NetworkInterceptor/ARCHITECTURE.md](wpf-browser/NetworkInterceptor/ARCHITECTURE.md) | Interceptor, replay, pause/resume, export |

## GitHub

First time: create a repo on GitHub, then:

```powershell
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO.git
git branch -M main
git push -u origin main
```

Updates: `git add -A`, `git commit -m "..."`, `git push`.

## VPS

```powershell
.\update-vps.ps1
```

Edits in the script: `$DeployWebsite`, `$DeployServer`, `$DeployBuilds`. See [DEPLOYMENT.md](DEPLOYMENT.md).
