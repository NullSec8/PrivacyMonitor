# What data we get and how to view it

## Admin panel (secured login)

The logs viewer and API are protected by an **admin login**. Nobody can see logs without your username and password.

- **Login page:** `http://YOUR_SERVER:3000/admin` — enter your admin username and password.
- **After login:** You are redirected to `/logs.html` and can view data. Session is stored in an HTTP-only cookie (valid for 7 days).
- **Log out:** Click "Log out" on the logs page.

**Required environment variables** (set in the systemd unit or env file):

- **ADMIN_USERNAME** — admin login name (default: `admin`)
- **ADMIN_PASSWORD** — admin password (required; stored hashed in memory, never in plain text on disk)
- **SESSION_SECRET** — a long random string used to sign session cookies (e.g. `openssl rand -hex 32`)

Example: edit `/etc/systemd/system/browser-update-server.service` and set:

```
Environment=ADMIN_USERNAME=admin
Environment=ADMIN_PASSWORD=your-secure-password
Environment=SESSION_SECRET=your-64-char-random-string
```

Then `sudo systemctl daemon-reload && sudo systemctl restart browser-update-server`.

---

## When do we log?

| Event | Logged? | Log file |
|-------|--------|----------|
| **Someone downloads the browser** (via `/api/download` or in-app “Download and restart”) | Yes | `download-log.jsonl` |
| **Someone updates via the app** (clicks “Download and restart now” in the app) | Yes | `install-log.jsonl` |
| **Anonymous usage data** (app sends version, OS, protection level when user has allowed it) | Yes | `usage-log.jsonl` |

Website visitors who click “Download” and get the file via the API are recorded in **download-log**.  
Users who complete an in-app update are also recorded in **install-log** (the app sends version/platform after applying the update).

---

## Data we collect

### 1. Download log (`logs/download-log.jsonl`)

One JSON object per line, for each download via `/api/download`:

- **time** – ISO timestamp (e.g. `2025-02-12T23:15:00.000Z`)
- **ip** – Client IP (may be a proxy if you use one)
- **userAgent** – Browser/user agent string (e.g. `PrivacyMonitor/1.0` or browser name)
- **version** – Requested version (e.g. `1.0.0` or `latest`)
- **platform** – e.g. `win64`
- **filename** – File served (e.g. `PrivacyMonitor-1.0.0-win-x64.zip`)

### 2. Install / update log (`logs/install-log.jsonl`)

One JSON object per line, for each POST to `/api/install-log` (sent by the app when a user completes an in-app update):

- **time** – ISO timestamp
- **ip** – Client IP
- **userAgent** – App user agent
- **version** – Version they updated to (from request body)
- **platform** – e.g. `win64` (from body)
- **client** – e.g. `PrivacyMonitor` (from body)

Anything else the client sends in the POST body is stored as-is in that line.

### 3. Usage log (`logs/usage-log.jsonl`)

One JSON object per line, for each POST to `/api/usage` (sent by the app when the user has allowed “Help improve Privacy Monitor” in Settings):

- **time** – ISO timestamp
- **ip** – Client IP
- **userAgent** – App user agent
- **version** – App version (e.g. `1.0.0`)
- **platform** – e.g. `win64`
- **client** – `PrivacyMonitor`
- **os** – e.g. `Windows 10`
- **protectionDefault** – Default protection mode: `Monitor`, `BlockKnown`, or `Aggressive`

No URLs, no browsing history, no personal data.

---

## How to view the logs on the server

SSH in, then:

```bash
# Recent download events (last 20 lines)
tail -20 /home/endri/browser_project/logs/download-log.jsonl

# Recent install/update events
tail -20 /home/endri/browser_project/logs/install-log.jsonl

# Pretty-print last download (Linux)
tail -1 /home/endri/browser_project/logs/download-log.jsonl | python3 -m json.tool

# Watch new entries in real time
tail -f /home/endri/browser_project/logs/download-log.jsonl

# Recent usage (anonymous stats)
tail -20 /home/endri/browser_project/logs/usage-log.jsonl
```

From your Windows machine (with SSH key set up):

```powershell
ssh root@187.77.71.151 "tail -30 /home/endri/browser_project/logs/download-log.jsonl"
```

---

## Privacy note

- We do **not** log visits to the website (index, features, download page) unless they actually hit `/api/download`.
- We only log the fields above (no cookies, no extra tracking).
- If you put the server behind a reverse proxy (e.g. Nginx), consider logging the `X-Forwarded-For` header so `req.ip` reflects the real client IP; the app currently uses Express’s `req.ip`.
