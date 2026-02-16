# Privacy Monitor – Project summary

## Repo structure

```
wpf-browser/
├── App.xaml, App.xaml.cs, MainWindow.xaml, ...
├── PrivacyEngine.cs, UpdateService.cs, ...   # WPF app (Privacy Monitor)
├── website/                                   # Static site (deployed to VPS)
│   ├── index.html, features.html, download.html, security.html
│   ├── admin.html, logs.html, setup-2fa.html
│   └── assets/ (styles.css, app.js, ...)
├── browser-update-server/                    # Node server + Nginx + optional Guacamole
│   ├── server/          (server.js, package.json)
│   ├── nginx/           (browser-update-server.conf)
│   ├── guacamole/       (docker-compose, README – optional RDP-in-browser)
│   ├── builds/          (version.json; on VPS: actual build zip)
│   └── README.md, setup.sh, scripts/
├── publish.ps1, sign.ps1, SIGNING.md
└── README.md
```

---

## What’s in the project

### 1. WPF app (Privacy Monitor)

- **Privacy-focused browser** – WebView2, privacy score, tracker detection, blocking, reports, forensics.
- **Updates** – `UpdateService.cs`: `BaseUrl = "http://187.77.71.151"`; calls `/api/latest`, `/api/download`, optional `/api/install-log` and `/api/usage`.
- **Publish** – `publish.ps1` builds single-file EXE and ZIP; `sign.ps1` and SIGNING.md for code signing.

### 2. Website (in `website/`)

- **Public pages** – Home, Features, Download, Security & Privacy.
- **Design** – Apple-style layout, system fonts, blue accent, scroll reveals, animations.
- **Admin** – `/admin` (login), `/logs.html` (protected), `/setup-2fa.html` (protected). No “Try demo” or “Try real app” links.

### 3. Browser update server (in `browser-update-server/`)

- **Node app** (`server/`) – Express; serves website and APIs:
  - `GET /api/latest` – version info
  - `GET /api/download/:version?platform=win64` – build download
  - `POST /api/install-log`, `POST /api/usage` – logging
  - `GET /api/logs` – admin only (session required)
- **Admin** – Login at `/admin`; session cookie; env: `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `SESSION_SECRET`.
- **2FA** – TOTP (speakeasy + qrcode); `/setup-2fa.html`, `/api/2fa/*`, `/api/login/verify-2fa`.
- **Rate limit** – `express-rate-limit` on `/api/login`: 5 requests per 15 min per IP.
- **Protected routes** – `/logs.html` and `/setup-2fa.html` only via auth; static middleware does not serve them; Cache-Control on redirects.
- **Nginx** – Proxies port 80 → Node (3000). Optional: `/app/` → Guacamole (8080) for RDP-in-browser (see `guacamole/README.md`).

### 4. GitHub

- **Repo** – NullSec8/PrivacyMonitor. Contains WPF app + `website/` + `browser-update-server/`.

---

## VPS (187.77.71.151)

| What           | Where / How |
|----------------|-------------|
| Project root  | `/home/endri/browser_project/` |
| Website       | `website/` (HTML, assets) |
| Node server   | `server/` – systemd unit `browser-update-server` |
| Builds        | `builds/` (version.json, zip) |
| Logs          | `logs/` (download-log.jsonl, install-log.jsonl, usage-log.jsonl) |
| Admin data    | `data/` (2FA secret, etc.) |
| Nginx         | Proxies 80 → 127.0.0.1:3000 (and optionally `/app/` → 8080) |
| SSH           | `root@187.77.71.151` |

---

## Quick reference

| Item              | URL / Command |
|-------------------|---------------|
| Live site         | http://187.77.71.151 |
| Download          | http://187.77.71.151/download.html |
| Admin login       | http://187.77.71.151/admin |
| Logs (after login)| http://187.77.71.151/logs.html |
| Restart Node      | `ssh root@187.77.71.151 "systemctl restart browser-update-server"` |

---

## What else you can do

### Security

- [ ] **HTTPS** – Certbot (e.g. DuckDNS + Let’s Encrypt). Then set `UpdateService.BaseUrl` to `https://...`.
- [ ] **Strong passwords** – Change admin and (if used) Guacamole defaults. Keep secrets in env.

### Website

- [ ] **429 message** – On admin login, show the server’s rate-limit error message when status is 429.
- [ ] **Meta/OG tags** – Description and Open Graph for sharing.

### Logs & admin

- [ ] **Export CSV** – Button on logs page to download current tab as CSV.
- [ ] **Log rotation** – Rotate/archive `*.jsonl`; back up `logs/`, `data/`, `builds/version.json`.

### App & releases

- [ ] **Version bump** – Update assembly version and `builds/version.json`; run `publish.ps1`; upload build to VPS.
- [ ] **Code signing** – Use `sign.ps1` / SIGNING.md for the EXE.
- [ ] **Configurable BaseUrl** – Override update server URL (e.g. config or first-run) for HTTPS without recompile.

### Optional (Guacamole)

- [ ] **Guacamole** – In `browser-update-server/guacamole/`: Docker stack for RDP-in-browser. See `guacamole/README.md`. Not linked from the site; use `/app/` directly if needed.
