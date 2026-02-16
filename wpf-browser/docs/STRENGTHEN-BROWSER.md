# Making Privacy Monitor Stronger

Concrete improvements to harden the browser’s privacy and security.

---

## Already in place

- **Tracker blocklist** – 220+ known services (PrivacyEngine), static + learned (ProtectionEngine).
- **Protection modes** – Monitor / Block known / Aggressive (confidence thresholds and heuristics).
- **Anti-fingerprinting** – Canvas/WebGL/Audio replay, User-Agent blend-in, Sec-CH-UA normalization.
- **Behavioral detection** – Session replay, mouse/scroll/keystroke tracking, dynamic script injection.
- **Per-site profiles** – Block ads/trackers, behavioral, anti-FP toggles; persistence.
- **Referrer stripping** – Third-party requests no longer send Referer when not in Monitor mode (reduces cross-site leakage).

---

## Quick wins (code changes)

| Improvement | Where | Status |
|-------------|--------|--------|
| **Lower BlockKnown threshold** | `ProtectionEngine.ShouldBlock` | **Done:** 0.35 → 0.28; Aggressive 0.22 → 0.18. |
| **Lower behavioral/heuristic thresholds** | `ProtectionEngine` | **Done:** Behavioral 0.65 → 0.55; heuristic 0.34 → 0.28. |
| **More tracker domains** | `PrivacyEngine.cs` | **Done:** +20 domains (Google/Adobe subdomains, LiveRamp, Adform, AdRoll, PulsePoint, etc.). |
| **Do Not Track header** | `MainWindow.OnWebResourceRequested` | **Done:** `DNT: 1` on all requests when not in Monitor mode. |
| **Referrer stripping** | `MainWindow.OnWebResourceRequested` | **Done:** Third-party requests get empty Referer when not Monitor. |
| **Stricter default mode** | `ProtectionEngine.GlobalDefaultMode` | Default remains `BlockKnown`. Option: add “Maximum protection” = Aggressive. |
| **Blocklist from file** | `ProtectionEngine.LoadBlocklist` | Allow optional user blocklist (e.g. `blocklist.json`). |
| **HTTPS-only option** | Settings + navigation | “Prefer HTTPS” / “Warn on HTTP”. |

---

## Medium effort

| Improvement | What to do |
|-------------|------------|
| **Referrer policy** | Already stripping Referer for third-party. Option: inject `<meta name="referrer" content="strict-origin-when-cross-origin">` or `Referrer-Policy` on page load for all sites. |
| **Do Not Track (DNT)** | Set a consistent header (e.g. `DNT: 1`) on all requests when user enables “Send Do Not Track” in settings. |
| **First-party isolation** | Document that WebView2 profile is per-app; optional “isolate by site” would require multiple profiles or origin-bound storage (larger change). |
| **CNAME uncloaking** | Resolve CNAMEs for request URLs and match blocklist against final host (e.g. `tracker.example.com` → `ads.evil.com`). |
| **Cosmetic filtering** | Optional “Hide known ad/social elements” by injecting CSS (e.g. `div[id^="google_ads"] { display: none !important; }`) when blocking is on. |

---

## Larger / roadmap

| Improvement | What to do |
|-------------|------------|
| **Built-in blocklist updates** | Periodic fetch of a curated blocklist (e.g. JSON from your server or a trusted list), merge with static list. |
| **Tor / proxy integration** | Existing proxy setting; document Tor Browser or system Tor as “route traffic through Tor” for max anonymity. |
| **Cookie partitioning** | WebView2 doesn’t expose CHIPS-style partitioning; track feature requests for future APIs. |
| **Fingerprint randomization** | Beyond replay: optionally randomize canvas/WebGL output per session so fingerprint changes over time (may break some sites). |
| **GDPR-style report** | One-click “Export full report” (HTML + JSON) for all requests, cookies, fingerprints, and blocks on current page. |

---

## Security (app and updates)

- **HTTPS for update server** – Use Certbot on VPS, set `UpdateService.BaseUrl` to `https://...`.
- **Code signing** – Sign the EXE so SmartScreen doesn’t warn; see `SIGNING.md`.
- **Configurable update URL** – Allow power users to point to a custom update server (e.g. enterprise).

---

## Summary

The browser is already strong on tracker blocking, anti-fingerprinting, and behavioral detection. The next steps that give the most gain for the effort are: **stricter default or lower BlockKnown threshold**, **more tracker domains**, **optional blocklist file**, **HTTPS-only option**, and **DNT header**. Referrer stripping for third-party requests is already implemented.
