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
| **Update server** | Node server for in-app updates and optional 2FA. |

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
  website/               # Generated site
browser-update-server/   # Node update server
  server/
  builds/
update-all.ps1           # Restore packages & build
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
| [DEPLOYMENT.md](DEPLOYMENT.md) | GitHub push instructions |
| [NetworkInterceptor/ARCHITECTURE.md](wpf-browser/NetworkInterceptor/ARCHITECTURE.md) | Interceptor, replay, pause/resume, export |
| [SIGNING.md](wpf-browser/SIGNING.md) | Code signing for distribution |

---

## Deployment

- **GitHub:** `git add -A`, `git commit -m "..."`, `git push`. See [DEPLOYMENT.md](DEPLOYMENT.md).

---

## API Reference

The update server provides these endpoints:

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/latest` | Returns latest version info (JSON) |
| GET | `/api/download/:version?platform=win64` | Download build. `version` can be `latest` or e.g. `1.0.0`; `platform` optional (win64, linux64, mac). |
| POST | `/api/install-log` | Log an install. Body: `{ "version": "1.0.0", "platform": "win64", "clientId": "optional" }` |
| POST | `/api/usage` | Anonymous usage data (version, OS, protection level) |
| GET | `/health` | Health check |
| POST | `/api/login` | Admin login (username/password) |
| POST | `/api/logout` | Admin logout |
| GET | `/api/logs` | Admin only: view logs (requires session) |
| GET | `/api/2fa/status` | Check 2FA status |
| GET | `/api/2fa/setup` | Start 2FA setup (QR code) |
| POST | `/api/2fa/setup` | Complete 2FA setup |
| POST | `/api/2fa/disable` | Disable 2FA |

**Rate Limits:**
- Login: 5 attempts per 15 minutes per IP
- Log ingestion: 120 requests per 15 minutes per IP
- Logs API: 100 requests per 15 minutes per IP

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Privacy Monitor System                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────┐    ┌─────────────────────────────┐    │
│  │   WPF Desktop App   │    │    Node Update Server       │    │
│  │   (Privacy Monitor) │    │    (browser-update-server)  │    │
│  │                     │    │                             │    │
│  │  • WebView2 tabs    │    │  • Express.js               │    │
│  │  • Privacy engine   │◄──►│  • REST API                 │    │
│  │  • Tracker blocking │    │  • Admin panel (2FA)        │    │
│  │  • Reports          │    │  • Build hosting            │    │
│  │  • Network monitor  │    │  • Usage analytics          │    │
│  └─────────────────────┘    └─────────────────────────────┘    │
│           │                            │                        │
│           │                            │                        │
│           ▼                            ▼                        │
│  ┌─────────────────────┐    ┌─────────────────────────────┐    │
│  │  Chrome Extension   │    │      Website (static)       │    │
│  │  (Optional)         │    │      index.html             │    │
│  │  • Tracker blocking │    │      features.html          │    │
│  │  • Privacy scores   │    │      download.html          │    │
│  │  • Cosmetic filter  │    │      admin.html             │    │
│  └─────────────────────┘    └─────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Data Flow:**
1. User browses in WPF app or Chrome extension
2. Privacy engine analyzes requests, blocks trackers, scores pages
3. Network interceptor logs all requests for inspection
4. Reports generated (HTML/CSV) for GDPR compliance
5. Optional: App checks for updates via Node server
6. Optional: Anonymous usage data sent (if user allows)

---

## License

[MIT](LICENSE) — see [LICENSE](LICENSE) for details.

---

<div align="center">
  <img src="https://capsule-render.vercel.app/api?type=soft&height=100&color=0A0015&text=Made%20by%20NullSec8&fontColor=7B2FF7&fontSize=24" width="100%" />
</div>
