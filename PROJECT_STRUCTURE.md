# Project structure (work)

```
work/
├── .gitignore
├── PROJECT_STRUCTURE.md    # this file
├── DEPLOYMENT.md            # GitHub + VPS steps
├── update-vps.ps1           # deploy website/server/builds to VPS
├── update-all.ps1           # restore packages, build (run before commit/deploy)
│
├── wpf-browser/             # PrivacyMonitor (WPF + WebView2)
│   ├── PrivacyMonitor.csproj
│   ├── MainWindow.xaml(.cs)
│   ├── NetworkInterceptor/  # Live interceptor, replay, risk scoring, export
│   │   ├── ARCHITECTURE.md
│   │   ├── NetworkInterceptorService.cs
│   │   ├── NetworkInterceptorViewModel.cs
│   │   ├── NetworkInterceptorWindow.xaml(.cs)
│   │   ├── RequestReplayHandler.cs
│   │   ├── ReplayModifyWindow.xaml(.cs)
│   │   ├── RiskScoring.cs
│   │   ├── ExportSchema.cs
│   │   └── ...
│   ├── chrome-extension/    # Optional extension (options, popup)
│   ├── browser-update-server/ # Node server (inside wpf-browser)
│   ├── ExportBlocklist/
│   └── website/             # Built site (generated; deploy via update-vps.ps1)
│
└── browser-update-server/   # Standalone update server (Node)
    ├── server/              # Node app (npm install, deploy to VPS)
    └── builds/              # Published builds (optional deploy)
```

## Key folders

| Path | Purpose |
|------|--------|
| `wpf-browser/` | Main app: PrivacyMonitor (WPF, WebView2, interceptor, protection) |
| `wpf-browser/NetworkInterceptor/` | Live network interceptor, replay, RiskScoring, export, pause/resume |
| `wpf-browser/chrome-extension/` | Chrome extension assets |
| `wpf-browser/browser-update-server/` | (If present) Node server inside repo |
| `browser-update-server/` | Update server (Node); deploy with `update-vps.ps1` |
| `update-vps.ps1` | Script to upload website/server/builds to VPS via SCP |

## Organizing

- **Source code**: `wpf-browser/*.cs`, `wpf-browser/NetworkInterceptor/*.cs`, etc.
- **Docs**: `ARCHITECTURE.md`, `TESTING.md` in NetworkInterceptor; root `DEPLOYMENT.md`, `PROJECT_STRUCTURE.md`.
- **Scripts**: Root `update-vps.ps1`, `update-all.ps1`; `wpf-browser/publish.ps1` for app publish.
- **Build outputs**: Ignored by `.gitignore` (bin, obj, node_modules, WebView2Runtime.zip, etc.).
