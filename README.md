# Privacy Monitor

**A Windows desktop privacy X-ray: see what sites really do—trackers, fingerprinting, and GDPR-style audits.**

Privacy Monitor is a WPF + WebView2 app that acts as a **diagnostic browser**: use it when you want to **inspect, document, or audit** what a site is doing behind the scenes—which trackers load, what data is sent, which cookies and identifiers are set—while you keep using your normal browser day to day.

This repository also includes the **browser-update-server** (Node) for app updates and optional 2FA, plus scripts to build and deploy.

---

## Table of contents

- [Features](#features)
- [Who it is for](#who-it-is-for)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Build and run](#build-and-run)
- [Publishing](#publishing)
- [Repository structure](#repository-structure)
- [Documentation](#documentation)
- [Deployment](#deployment)
- [Tech stack](#tech-stack)
- [License](#license)

---

## Features

- **Browse** — Chrome-style tabs, address bar, back/forward/reload. Each tab uses Chromium (WebView2). Downloads go to your **Downloads** folder.
- **Privacy score (0–100)** — One score per page with a letter grade (A/B/C/D/F). Fewer trackers and risks mean a higher score.
- **Tracker detection** — Database of ~220 known services (Google, Meta, Adobe, Hotjar, Segment, etc.). Classifies requests as first-party, third-party, or known tracker with confidence.
- **Protection modes** (per site):
  - **Monitor Only** — Log everything, block nothing.
  - **Block Known** — Block confirmed trackers (default).
  - **Aggressive** — Block known + heuristic and suspected trackers.
- **Anti-fingerprinting** — Optional script injection to reduce canvas/WebGL/audio fingerprinting; attempts are reported in the sidebar.
- **Sidebar panels** — Dashboard (score, risk categories, GDPR findings), Network (requests, filters), Storage (cookies, web storage), Fingerprint, Security (headers audit), Report (HTML/CSV, screenshot), Forensics (identity stitching, data flow, timeline).
- **Network interceptor** — Live request inspection, pause/resume (Burp-style), replay with optional header/body modification, risk scoring, session export. See [NetworkInterceptor ARCHITECTURE](wpf-browser/NetworkInterceptor/ARCHITECTURE.md).
- **Reports** — Timestamped HTML audit (score, GDPR articles, trackers, cookies, security headers, recommendations). CSV export and screenshot.
- **Update server** — Node server for in-app updates and optional 2FA; deploy to your VPS with the included script.

---

## Who it is for

- **Privacy-conscious users** who want a clear picture of how specific sites track them.
- **Developers and QA** who need to test how their site behaves (requests, cookies, storage, fingerprinting, security headers).
- **Privacy and compliance** teams who need repeatable, documented evidence for GDPR-style audits (HTML/CSV reports, timelines).

---

## Requirements

- **Windows 10/11** (64-bit)
- **.NET 9 SDK**
- **WebView2 Runtime** ([download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if not already installed with Windows/Edge)

---

## Quick start

From the repository root:

```powershell
git clone https://github.com/NullSec8/PrivacyMonitor.git
cd PrivacyMonitor
.\update-all.ps1
dotnet run --project wpf-browser\PrivacyMonitor.csproj
```

---

## Build and run

**From repo root:**

```powershell
.\update-all.ps1
dotnet run --project wpf-browser\PrivacyMonitor.csproj
```

**From the app folder:**

```powershell
cd wpf-browser
dotnet build -c Release
dotnet run -c Release
```

Or open `wpf-browser\PrivacyMonitor.csproj` in Visual Studio and run (F5).

Output: `wpf-browser\bin\Release\net9.0-windows\PrivacyMonitor.exe`

---

## Publishing

To build a single-file EXE and distribution ZIP (and optionally update the website):

```powershell
cd wpf-browser
.\publish.ps1
```

See `wpf-browser\SIGNING.md` for code signing (e.g. SignPath Foundation for open source) to avoid SmartScreen warnings on unsigned builds.

---

## Repository structure

```
├── .github/workflows/     # CI (build WPF, extension rules)
├── wpf-browser/           # PrivacyMonitor (WPF + WebView2)
│   ├── PrivacyMonitor.csproj
│   ├── MainWindow.xaml(.cs), BrowserTab.cs, PrivacyEngine.cs, ...
│   ├── NetworkInterceptor/ # Live interceptor, replay, risk scoring, export
│   ├── chrome-extension/  # Optional extension (options, popup, rules)
│   └── website/           # Generated site (deploy via update-vps.ps1)
├── browser-update-server/ # Node update server (deploy to VPS)
│   ├── server/
│   └── builds/
├── update-all.ps1         # Restore packages & build
├── update-vps.ps1         # Deploy website/server/builds to VPS
├── PROJECT_STRUCTURE.md   # Folder layout
└── DEPLOYMENT.md          # GitHub and VPS steps
```

---

## Documentation

| Document | Description |
|----------|-------------|
| [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) | Folder layout and organization |
| [DEPLOYMENT.md](DEPLOYMENT.md) | GitHub (push) and VPS (`update-vps.ps1`) |
| [wpf-browser/NetworkInterceptor/ARCHITECTURE.md](wpf-browser/NetworkInterceptor/ARCHITECTURE.md) | Interceptor, replay, pause/resume, export |
| [wpf-browser/SIGNING.md](wpf-browser/SIGNING.md) | Code signing for distribution |

---

## Deployment

- **GitHub:** After cloning, add your remote, then `git add -A`, `git commit -m "..."`, `git push`. See [DEPLOYMENT.md](DEPLOYMENT.md).
- **VPS:** Edit `update-vps.ps1` (e.g. `$DeployWebsite`, `$DeployServer`, `$DeployBuilds`), then run `.\update-vps.ps1`. See [DEPLOYMENT.md](DEPLOYMENT.md).

---

## Tech stack

- **WPF** — UI (tabs, toolbar, sidebar).
- **.NET 9** — C# app and libraries.
- **Microsoft.Web.WebView2** — Embedded Chromium and request interception.
- **Node** — Update server (browser-update-server).

---

## License

MIT. See [LICENSE](LICENSE) for details.
