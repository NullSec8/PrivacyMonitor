# Project structure

Canonical layout of the repository and where each piece lives.

## Folder tree

```
├── .github/
│   ├── workflows/
│   │   └── ci.yml              # CI: build WPF app, generate extension rules
│   └── ISSUE_TEMPLATE/         # Bug report, feature request, config
│
├── wpf-browser/                # Privacy Monitor desktop app (WPF + WebView2)
│   ├── PrivacyMonitor.csproj
│   ├── App.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   ├── BrowserTab.cs, PrivacyEngine.cs, ProtectionEngine.cs, ForensicEngine.cs
│   ├── UpdateService.cs, DesktopShortcut.cs, SystemThemeDetector.cs
│   ├── NetworkInterceptor/    # Live interceptor, replay, risk scoring, export
│   │   ├── ARCHITECTURE.md
│   │   ├── NetworkInterceptorService.cs
│   │   ├── NetworkInterceptorViewModel.cs
│   │   ├── NetworkInterceptorWindow.xaml(.cs)
│   │   ├── ReplayModifyWindow.xaml(.cs)
│   │   └── ...
│   ├── chrome-extension/      # Optional Chrome extension (rules, popup)
│   ├── website/               # ★ Canonical website (deploy source for update-vps.ps1)
│   │   ├── index.html, download.html, features.html, security.html
│   │   ├── admin.html, logs.html, setup-2fa.html
│   │   └── assets/             # styles.css, app.js, logo, screens, build-info.json
│   ├── publish.ps1            # Build single-file EXE with embedded WebView2, copy to website/
│   └── (bin/, obj/, publish/  # Build outputs — gitignored)
│
├── browser-update-server/      # Node update server (deploy to VPS)
│   ├── server/
│   │   ├── server.js           # Express: /api/latest, /api/download, /admin, static website
│   │   ├── package.json
│   │   └── .env.example        # ADMIN_PASSWORD, SESSION_SECRET, etc. (copy to .env on VPS)
│   ├── builds/
│   │   └── version.json        # Version and download filenames for /api/latest
│   ├── nginx/
│   │   └── browser-update-server.conf  # Nginx reverse proxy (80 → 3000, optional /app → 8080)
│   └── guacamole/             # Optional RDP-in-browser (Docker)
│
├── website/                   # Optional duplicate; update-vps.ps1 uses wpf-browser/website only
│
├── scripts/                   # Optional helper scripts (e.g. allow-update-server-firewall.ps1)
│
├── update-vps.ps1             # Deploy: wpf-browser/website, server, builds → VPS
├── update-all.ps1             # Restore packages and build (run before commit/deploy)
├── README.md
├── PROJECT_STRUCTURE.md       # This file
├── PROJECT-SUMMARY.md         # Quick reference (URLs, SSH, security checklist)
└── DEPLOYMENT.md              # GitHub push and VPS deploy steps
```

## Canonical paths

| What | Path | Notes |
|------|------|--------|
| **Website (deploy)** | `wpf-browser/website/` | `update-vps.ps1` uploads this folder. Put EXE here after `publish.ps1`. |
| **Update server** | `browser-update-server/server/` | Node app. Deploy with update-vps.ps1; `node_modules` is excluded (VPS runs `npm install`). |
| **Version manifest** | `browser-update-server/builds/version.json` | Defines version and download filenames for the app’s update check. |
| **WPF app** | `wpf-browser/*.cs`, `wpf-browser/NetworkInterceptor/` | Main source. Build: `dotnet build wpf-browser/PrivacyMonitor.csproj`. Publish: `wpf-browser/publish.ps1`. |

## Build outputs (gitignored)

- `wpf-browser/bin/`, `wpf-browser/obj/`, `wpf-browser/publish/`
- `wpf-browser/website/*.exe`, `wpf-browser/website/*.zip`
- `browser-update-server/server/node_modules/`
- `WebView2Runtime.zip` (generated during publish, then removed)

## Duplicate / legacy

- **Root `website/`**: If present, it is not used by `update-vps.ps1`. The deploy script only uploads `wpf-browser/website/`. You can keep root `website/` in sync manually or remove it to avoid confusion.
- **`wpf-browser/browser-update-server/`**: If it exists, it mirrors the root `browser-update-server/`; root is the one used by `update-vps.ps1`.

## Scripts

| Script | Purpose |
|--------|--------|
| `update-vps.ps1` | Upload website, server, builds to VPS; fix permissions; restart Node. |
| `update-all.ps1` | Restore NuGet/Node and build (run from repo root). |
| `wpf-browser/publish.ps1` | Build single-file EXE with embedded WebView2; copy to `wpf-browser/website/`; optional signing. |
| `browser-update-server/setup-ssh-key.ps1` | One-time: copy SSH key to VPS for passwordless deploy. |
