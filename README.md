<p align="center">
  <img src="https://img.shields.io/github/actions/workflow/status/NullSec8/PrivacyMonitor/ci.yml?branch=main&style=flat-square" alt="Build"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d6?style=flat-square" alt="Platform"/>
  <img src="https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="License"/>
</p>

<h1 align="center">Privacy Monitor</h1>

<p align="center">
  <strong>A Windows desktop privacy X-ray</strong> — see what sites really do: trackers, fingerprinting, and GDPR-style audits.
</p>

<p align="center">
  Privacy Monitor is a <strong>diagnostic browser</strong> built with WPF and WebView2. Use it when you want to <strong>inspect</strong>, <strong>document</strong>, or <strong>audit</strong> what a site is doing behind the scenes — which trackers load, what data is sent, which cookies and identifiers are set — while you keep using your normal browser day to day.
</p>

<p align="center">
  This repo includes the <strong>Privacy Monitor</strong> app, the <strong>browser-update-server</strong> (Node) for updates and optional 2FA, and scripts to build and deploy.
</p>

---

## Table of contents

- [Features](#-features)
- [Who it is for](#-who-it-is-for)
- [Requirements](#-requirements)
- [Quick start](#-quick-start)
- [Build and run](#-build-and-run)
- [Publishing](#-publishing)
- [Repository structure](#-repository-structure)
- [Documentation](#-documentation)
- [Deployment](#-deployment)
- [Tech stack](#-tech-stack)
- [License](#-license)

---

## Features

| Area | Description |
|------|-------------|
| **Browse** | Chrome-style tabs, address bar, back/forward/reload. Each tab uses Chromium (WebView2). Downloads go to your **Downloads** folder. |
| **Privacy score** | 0–100 score per page with a letter grade (A/B/C/D/F). Fewer trackers and risks → higher score. |
| **Tracker detection** | Database of ~220 known services (Google, Meta, Adobe, Hotjar, Segment, etc.). First-party, third-party, and known-tracker classification with confidence. |
| **Protection modes** | **Monitor Only** (log only), **Block Known** (confirmed trackers), **Aggressive** (known + heuristic). Per-site. |
| **Anti-fingerprinting** | Optional script injection to reduce canvas/WebGL/audio fingerprinting; attempts reported in the sidebar. |
| **Sidebar panels** | Dashboard, Network, Storage, Fingerprint, Security (headers audit), Report (HTML/CSV, screenshot), Forensics (identity stitching, data flow, timeline). |
| **Network interceptor** | Live request inspection, pause/resume (Burp-style), replay with optional header/body modification, risk scoring, session export. See [ARCHITECTURE](wpf-browser/NetworkInterceptor/ARCHITECTURE.md). |
| **Reports** | Timestamped HTML audit (score, GDPR articles, trackers, cookies, security headers, recommendations). CSV export and screenshot. |
| **Update server** | Node server for in-app updates and optional 2FA; deploy to your VPS with the included script. |

---

## Who it is for

- **Privacy-conscious users** — See how specific sites track you.
- **Developers & QA** — Test how your site behaves (requests, cookies, storage, fingerprinting, security headers).
- **Privacy & compliance** — Repeatable, documented evidence for GDPR-style audits (HTML/CSV reports, timelines).

---

## Requirements

- **Windows 10/11** (64-bit)
- **.NET 9 SDK** (for building)
- **WebView2 Runtime** — [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if not already installed with Windows or Edge

---

## Quick start

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

Build a single-file EXE and distribution ZIP (and optionally update the website):

```powershell
cd wpf-browser
.\publish.ps1
```

See [wpf-browser/SIGNING.md](wpf-browser/SIGNING.md) for code signing (e.g. SignPath Foundation for open source) to avoid SmartScreen warnings.

---

## Repository structure

```
├── .github/
│   ├── workflows/ci.yml    # CI: build WPF, extension rules
│   └── ISSUE_TEMPLATE/     # Bug report, feature request
├── wpf-browser/            # Privacy Monitor (WPF + WebView2)
│   ├── PrivacyMonitor.csproj
│   ├── MainWindow.xaml(.cs), BrowserTab.cs, PrivacyEngine.cs, ...
│   ├── NetworkInterceptor/ # Live interceptor, replay, risk scoring, export
│   ├── chrome-extension/   # Optional extension
│   └── website/            # Generated site (deploy via update-vps.ps1)
├── browser-update-server/  # Node update server (deploy to VPS)
│   ├── server/
│   └── builds/
├── update-all.ps1          # Restore packages & build
├── update-vps.ps1          # Deploy website/server/builds to VPS
├── PROJECT_STRUCTURE.md
└── DEPLOYMENT.md
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

- **GitHub:** `git add -A`, `git commit -m "..."`, `git push`. See [DEPLOYMENT.md](DEPLOYMENT.md).
- **VPS:** Edit `update-vps.ps1` (e.g. `$DeployWebsite`, `$DeployServer`, `$DeployBuilds`), then run `.\update-vps.ps1`. See [DEPLOYMENT.md](DEPLOYMENT.md).

---

## Tech stack

- **WPF** — UI (tabs, toolbar, sidebar)
- **.NET 9** — C# app and libraries
- **Microsoft.Web.WebView2** — Embedded Chromium and request interception
- **Node** — Update server (browser-update-server)

---

## License

[MIT](LICENSE) — see [LICENSE](LICENSE) for details.
