# Deployment

## GitHub

- Push to `main`/`master`: `git push origin main`
- CI runs on push/PR (see `.github/workflows/ci.yml`).

## VPS (manual)

1. On VPS: `git clone <repo-url> && cd <repo-name>`
2. **Desktop app (optional):** `dotnet publish PrivacyMonitor.csproj -c Release -o ./publish`
3. **Update server (optional):** `cd browser-update-server/server && npm ci && npm start`
4. **Chrome extension:** No server deploy; pack from `chrome-extension/` and publish to store or distribute zip.

## Extension pack

From repo root:
```bash
cd chrome-extension
node generate-rules.js
# Zip contents of chrome-extension/ (not the folder) for store upload
```
