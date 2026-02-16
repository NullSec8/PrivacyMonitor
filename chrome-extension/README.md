# Privacy Monitor – Chrome Extension

Chrome/Edge extension that uses the **same blocking engine** as the [Privacy Monitor](https://github.com/NullSec8/PrivacyMonitor) desktop browser (ProtectionEngine + PrivacyEngine). The blocklist is generated from the browser so behaviour matches 1:1.

## Syncing the blocklist from the browser

From the repo root, build and run the desktop app with:

```bash
dotnet build PrivacyMonitor.csproj
.\bin\Debug\net9.0-windows\win-x64\PrivacyMonitor.exe --export-blocklist
```

This overwrites `chrome-extension/tracker-domains.js` with the current engine blocklist. Then regenerate static rules:

```bash
cd chrome-extension
node generate-rules.js
```

Reload the extension in `chrome://extensions` after updating.

## Features

- **Block known** – Blocks confirmed trackers and ads only (Google, Meta, Adobe, Criteo, session replay, DMPs, etc.). Fewer false positives.
- **Aggressive** – Same as Block known plus heuristic/borderline (CMPs, some analytics, affiliate, chat widgets). Stronger protection.
- **Off** – No blocking.
- **Badge** – Toolbar badge: OFF (gray), 1 (Block known), 2 (Aggressive).
- **Options** – Link to desktop app for full privacy (anti-fingerprinting, referrer stripping, per-site profiles).

## Install (unpacked)

1. Open Chrome and go to `chrome://extensions/`.
2. Turn on **Developer mode** (top right).
3. Click **Load unpacked** and select the `chrome-extension` folder.

**If blocking doesn't work:** Remove the extension completely (Remove), then load unpacked again. Old dynamic rules can persist across reloads and cause issues.

## Optional: custom icon

To use the app icon, add PNGs to `icons/`:

- `icons/icon16.png` (16×16)
- `icons/icon32.png` (32×32)
- `icons/icon48.png` (48×48)

Then add to `manifest.json`:

```json
  "action": {
    "default_popup": "popup.html",
    "default_icon": { "16": "icons/icon16.png", "32": "icons/icon32.png", "48": "icons/icon48.png" },
    "default_title": "Privacy Monitor"
  },
  "icons": { "16": "icons/icon16.png", "32": "icons/icon32.png", "48": "icons/icon48.png" }
```

## Pack for store (optional)

Zip the contents of `chrome-extension/` (not the folder itself) and upload to Chrome Web Store, or use “Pack extension” in `chrome://extensions/`.
