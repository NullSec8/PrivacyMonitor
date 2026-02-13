# Browser Update Server (Ubuntu 24.04)

Host browser builds and let clients check for updates, download builds, and log installs via a simple REST API.

## Placeholder variables

Use these everywhere and replace with your real values before running on the server:

| Variable           | Replace with (example) |
|--------------------|------------------------|
| `ROOT_USER`        | `root`                 |
| `ROOT_PASSWORD`    | Your root password     |
| `END_USER`         | `endri`                |
| `END_USER_PASSWORD`| Your user password     |

## 1. Create folder and set permissions

The setup script creates `/home/END_USER/browser_project` and subdirs, sets ownership to `END_USER`, and installs Node.js and the service.

**On your VPS (as a user with sudo):**

```bash
# Option A: Set variables and run
export END_USER=endri
export ROOT_USER=root
# ROOT_PASSWORD / END_USER_PASSWORD only needed if a step uses them (e.g. sudo -S)
./setup.sh
```

**Or edit the script** and replace at the top:

```bash
ROOT_USER="root"
ROOT_PASSWORD="your-root-password"
END_USER="endri"
END_USER_PASSWORD="your-user-password"
```

Then run:

```bash
chmod +x setup.sh
./setup.sh
```

The script is **idempotent**: safe to run again (e.g. after pulling updates or adding server files).

## 2. Install Nginx (optional)

To put Nginx in front of the Node app (port 80/443):

```bash
chmod +x scripts/install-nginx-optional.sh
./scripts/install-nginx-optional.sh
```

Then copy and edit the Nginx config, replacing `END_USER` and `YOUR_DOMAIN_OR_IP`.

## 3. REST API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/latest` | Returns latest version info (from `builds/version.json` or fallback). |
| GET | `/api/download/:version?platform=win64` | Download build. `version` can be `latest` or e.g. `1.0.0`; `platform` optional (win64, linux64, mac, etc.). |
| POST | `/api/install-log` | Log an install. Body: `{ "version": "1.0.0", "platform": "win64", "clientId": "optional" }`. Appends to `logs/install-log.jsonl`. |
| GET | `/health` | Health check. |

## 4. Copying your compiled browser files to the server safely

Use **SCP** or **RSYNC** with the **END_USER** account (no need to expose root). Replace `END_USER`, `YOUR_SERVER_IP`, and paths as needed.

### Option A: SCP (single file or folder)

From your **Windows** machine (PowerShell), using the actual user and host:

```powershell
# Copy a single build zip
scp "C:\path\to\browser-1.0.0-win64.zip" END_USER@YOUR_SERVER_IP:/home/END_USER/browser_project/builds/

# Copy entire builds folder
scp -r "C:\path\to\builds\*" END_USER@YOUR_SERVER_IP:/home/END_USER/browser_project/builds/
```

You will be prompted for **END_USER_PASSWORD** (or use SSH key).

### Option B: RSYNC (recommended for repeated uploads)

From WSL or a machine with `rsync`:

```bash
# Sync local builds to server (only changed files)
rsync -avz --progress ./builds/ END_USER@YOUR_SERVER_IP:/home/END_USER/browser_project/builds/

# With SSH key (no password prompt)
rsync -avz -e "ssh -i /path/to/your/key" ./builds/ END_USER@YOUR_SERVER_IP:/home/END_USER/browser_project/builds/
```

### Option C: SFTP (GUI)

Use WinSCP, FileZilla, or similar. Connect as **END_USER** to **YOUR_SERVER_IP**, then upload into `/home/END_USER/browser_project/builds/`.

### After uploading

1. **Add or update `version.json`** in `builds/` so `/api/latest` and `/api/download` work. Copy from `builds/version.json.example` and set `version` and `downloads` filenames to match what you uploaded.
2. Restart the server (optional; only if you changed server code):

   ```bash
   sudo systemctl restart browser-update-server
   ```

## 5. Directory layout on the server

After setup and copying server files:

```
/home/END_USER/browser_project/
├── builds/           # Put browser zips and version.json here
│   ├── version.json
│   ├── browser-1.0.0-win64.zip
│   └── ...
├── logs/
│   └── install-log.jsonl   # Created when clients POST to /api/install-log
└── server/
    ├── package.json
    ├── server.js
    └── node_modules/
```

## 6. Deploying server code to the VPS

From your PC, copy the Node app into the project (replace END_USER and YOUR_SERVER_IP):

```powershell
scp -r browser-update-server\server\* END_USER@YOUR_SERVER_IP:/home/END_USER/browser_project/server/
```

Then on the server:

```bash
cd /home/END_USER/browser_project/server
npm install --omit=dev
sudo systemctl restart browser-update-server
```

## 7. Idempotency and updates

- **Setup:** Run `setup.sh` again anytime (creates dirs, installs Node if missing, overwrites systemd unit and restarts).
- **Builds:** Overwrite or add files in `builds/` and update `builds/version.json`; no script rerun needed.
- **Server app:** Replace files in `server/`, run `npm install` if `package.json` changed, then `sudo systemctl restart browser-update-server`.

## 8. Deploying a new browser version (auto-update for users)

When you release a new version of the browser:

1. **Build and zip** the app (e.g. `publish.ps1` produces `PrivacyMonitor-1.0.1-win-x64.zip`).
2. **Upload the zip** to the server:
   ```powershell
   scp "path\to\PrivacyMonitor-1.0.1-win-x64.zip" endri@187.77.71.151:/home/endri/browser_project/builds/
   ```
   (Use root@ if you use root, and adjust the path.)
3. **Update `version.json`** on the server so `/api/latest` and `/api/download` serve the new version:
   ```bash
   ssh root@187.77.71.151 "cat /home/endri/browser_project/builds/version.json"
   ```
   Edit it to set `version` to the new number (e.g. `1.0.1`) and `downloads.win64` / `downloads.default` to the new zip filename (e.g. `PrivacyMonitor-1.0.1-win-x64.zip`). You can edit with:
   ```bash
   ssh root@187.77.71.151 "nano /home/endri/browser_project/builds/version.json"
   ```

After that, existing users will see "Update available" when they open the menu (or when they click "Check for updates") and can download and restart to get the new version.

## 9. Restrict access (Nginx + UFW)

To expose only ports 22, 80, 443 and keep Node on localhost:

1. **Node** binds to `127.0.0.1:3000` (set `BIND_ADDRESS=127.0.0.1` in the systemd unit).
2. **Nginx** listens on port 80 and proxies to `127.0.0.1:3000`. Deploy `nginx/browser-update-server.conf` to `/etc/nginx/sites-available/` and enable it.
3. **UFW:** allow 22 (SSH), 80 (HTTP), 443 (HTTPS); remove any rule for 3000. Then run `sudo ufw enable` if you use UFW.

After that, use **http://YOUR_IP/** (port 80) instead of `:3000`. The browser’s update URL is set to port 80 by default.

See `scripts/restrict-access-ufw-nginx.sh` for an automated setup.

## 10. Security notes

- Do **not** commit real `ROOT_PASSWORD` or `END_USER_PASSWORD` into git; use env vars or a secrets store.
- Prefer SSH keys over passwords for END_USER when copying files.
- If you use Nginx, consider TLS (e.g. Let’s Encrypt) for production.
