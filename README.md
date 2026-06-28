<div align="center">
  <img src="https://capsule-render.vercel.app/api?type=waving&height=200&color=0:0A0015,50:7B2FF7,100:0A0015&text=PrivacyMonitor&reversal=false&fontColor=FFFFFF&fontSize=50&animation=fadeIn" width="100%" />
  <br><br>
  <img src="https://img.shields.io/github/actions/workflow/status/NullSec8/PrivacyMonitor/ci.yml?branch=main&style=for-the-badge&label=build&color=7B2FF7" alt="Build"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-7B2FF7?style=for-the-badge&logo=windows&logoColor=white" alt="Platform"/>
  <img src="https://img.shields.io/badge/.NET-9.0-7B2FF7?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/license-MIT-7B2FF7?style=for-the-badge&logo=opensourceinitiative&logoColor=white" alt="License"/>
  <img src="https://img.shields.io/badge/PRs-welcome-7B2FF7?style=for-the-badge&logo=github&logoColor=white" alt="PRs"/>
  <br><br>
  <p><strong>A Windows desktop privacy X-ray</strong><br>
  See what sites really do: trackers, fingerprinting, and GDPR-style audits.<br>
  <em>Inspect. Document. Audit.</em></p>
  <br>
  <a href="https://github.com/NullSec8/PrivacyMonitor/releases"><img src="https://img.shields.io/badge/Download%20Latest-7B2FF7?style=for-the-badge&logo=github&logoColor=white" /></a>
</div>

---

Privacy Monitor is a **diagnostic browser** built with WPF and WebView2. Use it when you want to **inspect**, **document**, or **audit** what a site is doing behind the scenes — which trackers load, what data is sent, which cookies and identifiers are set — while you keep using your normal browser day to day.

This repo includes the **Privacy Monitor** app, the **browser-update-server** (Node) for updates and optional 2FA, and scripts to build and deploy.

---

## Features

| Area | Description |
|------|-------------|
| **Browse** | Chrome-style tabs, address bar, back/forward/reload. Each tab uses Chromium (WebView2). Downloads go to your **Downloads** folder. |
| **Privacy score** | 0-100 score per page with a letter grade (A/B/C/D/F). Fewer trackers and risks = higher score. |
| **Tracker detection** | Database of ~220 known services (Google, Meta, Adobe, Hotjar, Segment, etc.). First-party, third-party, and known-tracker classification with confidence. |
| **Protection modes** | **Monitor Only** (log only), **Block Known** (confirmed trackers), **Aggressive** (known + heuristic). Per-site. |
| **Anti-fingerprinting** | Optional script injection to reduce canvas/WebGL/audio fingerprinting; attempts reported in the sidebar. |
| **Sidebar panels** | Dashboard, Network, Storage, Fingerprint, Security (headers audit), Report (HTML/CSV, screenshot), Forensics (identity stitching, data flow, timeline). |
| **Network interceptor** | Live request inspection, pause/resume (Burp-style), replay with optional header/body modification, risk scoring, session export. |
| **Reports** | Timestamped HTML audit (score, GDPR articles, trackers, cookies, security headers, recommendations). CSV export and screenshot. |
| **Update server** | Node server for in-app updates and optional 2FA; deploy to your VPS with the included script. |

---

## Who it is for

- **Privacy-conscious users** — See how specific sites track you.
- **Developers & QA** — Test how your site behaves (requests, cookies, storage, fingerprinting, security headers).
- **Privacy & compliance** — Repeatable, documented evidence for GDPR-style audits (HTML/CSV reports, timelines).

---

## Quick start

```powershell
git clone https://github.com/NullSec8/PrivacyMonitor.git
cd PrivacyMonitor
.\update-all.ps1
dotnet run --project wpf-browser\PrivacyMonitor.csproj
```

---

## Requirements

- **Windows 10/11** (64-bit)
- **.NET 9 SDK** (for building)
- **WebView2 Runtime** — [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if not already installed with Windows or Edge

---

## Repository structure

```
.github/
  workflows/ci.yml       # CI: build WPF, extension rules
  ISSUE_TEMPLATE/        # Bug report, feature request
wpf-browser/             # Privacy Monitor (WPF + WebView2)
  PrivacyMonitor.csproj
  MainWindow.xaml(.cs), BrowserTab.cs, PrivacyEngine.cs, ...
  NetworkInterceptor/    # Live interceptor, replay, risk scoring, export
  chrome-extension/      # Optional extension
  website/               # Generated site (deploy via update-vps.ps1)
browser-update-server/   # Node update server (deploy to VPS)
  server/
  builds/
update-all.ps1           # Restore packages & build
update-vps.ps1           # Deploy website/server/builds to VPS
PROJECT_STRUCTURE.md
DEPLOYMENT.md
```

---

## Tech stack

<div align="center">
  <img src="https://img.shields.io/badge/WPF-UI-7B2FF7?style=for-the-badge&logo=.net&logoColor=white" />
  <img src="https://img.shields.io/badge/.NET_9-C%23-7B2FF7?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img src="https://img.shields.io/badge/WebView2-Chromium-7B2FF7?style=for-the-badge&logo=googlechrome&logoColor=white" />
  <img src="https://img.shields.io/badge/Node.js-Server-7B2FF7?style=for-the-badge&logo=nodedotjs&logoColor=white" />
  <img src="https://img.shields.io/badge/PowerShell-Scripts-7B2FF7?style=for-the-badge&logo=powershell&logoColor=white" />
</div>

---

## Documentation

| Document | Description |
|----------|-------------|
| [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) | Folder layout and organization |
| [DEPLOYMENT.md](DEPLOYMENT.md) | GitHub (push) and VPS (`update-vps.ps1`) |
| [NetworkInterceptor/ARCHITECTURE.md](wpf-browser/NetworkInterceptor/ARCHITECTURE.md) | Interceptor, replay, pause/resume, export |
| [SIGNING.md](wpf-browser/SIGNING.md) | Code signing for distribution |

---

## Deployment

- **GitHub:** `git add -A`, `git commit -m "..."`, `git push`. See [DEPLOYMENT.md](DEPLOYMENT.md).
- **VPS:** Edit `update-vps.ps1` (e.g. `$DeployWebsite`, `$DeployServer`, `$DeployBuilds`), then run `.\update-vps.ps1`. See [DEPLOYMENT.md](DEPLOYMENT.md).

---

## License

[MIT](LICENSE) — see [LICENSE](LICENSE) for details.

---

<div align="center">
  <img src="https://capsule-render.vercel.app/api?type=soft&height=100&color=0A0015&text=Made%20by%20NullSec8&fontColor=7B2FF7&fontSize=24" width="100%" />
</div>
