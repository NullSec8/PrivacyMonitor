# Privacy Monitor (WPF)

A Windows desktop browser built with WPF and **WebView2** that monitors and protects your privacy while you browse. It shows a **privacy score** and **grade** for each site, detects trackers and fingerprinting, can **block** unwanted requests, and produces **audit reports** with GDPR-oriented findings.

Unlike a normal browser, Privacy Monitor is meant to be a **privacy X-ray tool** rather than your day‑to‑day browser. You open sites inside it when you want to **see everything they are doing behind the scenes**: which trackers they load, what data they send, which cookies and identifiers they set, and how well they comply with basic security and GDPR expectations.

## Who it is for

- **Privacy‑conscious users** who want a clearer picture of how specific sites track them, beyond what simple “ad blocker” extensions show.
- **Developers and QA teams** who need to test how their own website behaves in the real world (requests, cookies, storage, fingerprinting attempts, security headers).
- **Privacy, security, and compliance professionals** who prepare **GDPR / privacy audits** and need repeatable, documented evidence (HTML/CSV reports, screenshots, timelines).

You can think of it as a **diagnostic browser**: you keep using your normal browser every day, and use Privacy Monitor whenever you want to inspect, troubleshoot, or document what a site is really doing.

## What it does

- **Browse** — Chrome-style tabs, address bar, back/forward/reload. Each tab embeds a full Chromium-based WebView2. Downloads go to your **Downloads** folder. **Escape** stops loading; Back/Forward are disabled when there is no history.
- **Privacy score (0–100)** — One score per page: fewer trackers and risks mean a higher score and a letter grade (e.g. A/B/C/D/F).
- **Tracker detection** — Database of ~220 known services (Google, Meta, Adobe, Hotjar, Segment, etc.). Classifies requests as first-party, third-party, or known tracker with confidence.
- **Protection modes** (per site):
  - **Monitor Only** — Log everything, block nothing.
  - **Block Known** — Block confirmed trackers (default).
  - **Aggressive** — Block known + heuristic and suspected trackers.
- **Anti-fingerprinting** — Optional script injection to reduce canvas/WebGL/audio fingerprinting; fingerprint attempts are reported in the sidebar.
- **Ad/tracker blocking** — Static blocklist + adaptive learning, confidence-weighted decisions, and per-site toggles.
- **Script/element blocking** — Prevents execution of blocked scripts, iframes, pixels, and images (not just network requests).
- **Sidebar panels**:
  - **Dashboard** — Score, grade, risk by category (Tracking, Fingerprint, Data Leakage, Security, Behavioral), top trackers, live request feed, GDPR findings, recommendations, “What you can do” tips.
  - **Network** — List of requests (filter: tracking only, search), request detail (URL, headers, type, status).
  - **Storage** — Cookies and web storage for the current site.
  - **Fingerprint** — Detected fingerprinting techniques; WebRTC leak check.
  - **Security** — Audit of HTTP security headers (CSP, HSTS, etc.).
  - **Report** — Save HTML report, screenshot, export CSV.
  - **Forensics** — Identity stitching (same ID across domains), company data flow, behavioral patterns, request bursts, cross-tab correlation, timeline.
- **Reports** — Timestamped HTML audit (score, GDPR articles, trackers, fingerprints, cookies, security headers, recommendations). Optional CSV export and screenshot.
- **Forensic logging** — Blocked request trail with category, confidence, and reasons.

## Requirements

- **Windows 10/11** (64-bit)
- **.NET 9** (or the SDK you target)
- **WebView2 Runtime** (usually already installed with Windows or Edge; otherwise [download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)).
- **.NET 9 SDK** (for building and publishing)

## Build and run

```bat
cd wpf-browser
dotnet build -c Release
dotnet run -c Release
```

Or open `PrivacyMonitor.csproj` / the folder in Visual Studio and run (F5).

Output executable: `bin\Release\net9.0-windows\PrivacyMonitor.exe` (or `apphost.exe` depending on publish).

## Publish for distribution (any PC + website)

Run the publish script to build a single-file EXE, create a distribution ZIP, and update the website with the new build:

```powershell
.\publish.ps1
```

**Outputs:**

| Output | Purpose |
|--------|---------|
| `publish\win-x64\PrivacyMonitor.exe` | Single-file app (run on any Windows 10/11 64-bit PC) |
| `publish\PrivacyMonitor-1.0.0-win-x64.zip` | ZIP with EXE + INSTALL.txt for sharing |
| `website\PrivacyMonitor.exe` | Copy of EXE for direct download from your site |
| `website\PrivacyMonitor-1.0.0-win-x64.zip` | Copy of ZIP for download from your site |
| `website\assets\build-info.json` | Version, size, SHA256 for the download page |

**Install on another PC:** Unzip the ZIP (or copy the EXE), run `PrivacyMonitor.exe`. No installer or .NET required. If WebView2 is missing, the user can install it from the link shown in the app or on the download page.

**Windows “not protected” / SmartScreen:** Unsigned builds trigger a one-time “Windows protected your PC” warning. Users can click **More info** → **Run anyway**. To **publish so Windows doesn’t say that**, see **[SIGNING.md](SIGNING.md)**: use SignPath Foundation (free for open source) or buy a certificate and set `CERT_PATH` + `CERT_PASSWORD` before running `publish.ps1`.

## Tech stack

- **WPF** — UI (tabs, toolbar, sidebar, lists, cards).
- **.NET 9** — C# app and libraries.
- **Microsoft.Web.WebView2** — Embedded Chromium browser and `WebResourceRequested` for request interception.

## Main components

| File | Role |
|------|------|
| `MainWindow.xaml(.cs)` | Window, tab bar, address bar, sidebar layout, analysis tabs, UI bindings and events. |
| `BrowserTab.cs` | One tab: WebView2, request list, fingerprints, cookies, storage, forensic state, blocked count. |
| `PrivacyEngine.cs` | Tracker database (~220 entries), scoring, risk categories, GDPR mapping, threat tier, recommendations. |
| `ProtectionEngine.cs` | Per-site profiles, block decision (Monitor/Block Known/Aggressive), anti-fingerprint script. |
| `ForensicEngine.cs` | Identifier extraction, identity stitching, data flow, timeline, company clusters. |
| `ReportGenerator.cs` | HTML audit report, CSV export. |
| `Models.cs` | ProtectionMode, SiteProfile, BlockDecision, TrackerCategory, RequestEntry, etc. |

## Notes

- Blocking and anti-fingerprinting can change how some sites behave; use **Monitor Only** if you only want to observe.
- Per-site profiles are stored under `%AppData%\PrivacyMonitor\site-profiles.json`.
- Learned trackers are stored under `%AppData%\PrivacyMonitor\learned-trackers.json`.
- Static blocklist lives in `%AppData%\PrivacyMonitor\blocklist.json` (auto-created if missing).
- The app does not send your data to any external server; analysis runs locally.
