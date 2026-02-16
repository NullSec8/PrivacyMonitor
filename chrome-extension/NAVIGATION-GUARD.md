# Navigation guard – logic and edge cases

## Goal

Stop sites from opening external browser/app links or forcing navigation when the user clicks, while keeping normal http/https navigation intact.

## Delivered files

- **navigation-guard.js** – Single content script (runs at `document_start`, first in the list). Intercepts clicks in capture phase, checks link protocol, injects the page script, logs blocks.
- **navigation-guard-page.js** – Injected into the page to override `window.open` and `location.assign` / `location.replace`.

## Logic

### Protocol rule

- **Allowed:** `http:`, `https:`, and relative URLs (no scheme, or starting with `#`, `/`, `?`).
- **Blocked:** Any other scheme, including: `intent:`, `javascript:`, `data:`, `blob:`, `file:`, `mailto:`, `tel:`, `custom:`, `chrome:`, `msedge:`, etc.

Implementation: normalize `href` (trim, lower case), then allow only if it is relative or starts with `http://` or `https://`. No allowlist of bad schemes; anything that is not http(s) or relative is blocked.

### Content script (navigation-guard.js)

1. **Inject page script** once at load so overrides run in page context before site scripts.
2. **Mousedown (capture):** Store the current `href` of the link under the pointer for rewrite detection.
3. **Click (capture):**
   - Resolve target to the nearest `<a>`; if none or no `href`, do nothing (allow).
   - Read `href` via `getAttribute('href')` (raw value) so `javascript:`, `intent:`, etc. are seen before resolution.
   - If protocol is not allowed → `preventDefault`, `stopPropagation`, `stopImmediatePropagation`, log, return.
   - If `href` changed since mousedown and the new value is not allowed → same block (hidden link hijacking / rewrite).
4. **privacyMonitorNavBlocked (capture):** Listen for events from the page script and log them (script-driven blocks).

Stopping propagation only when we block keeps normal clicks untouched.

### Page script (navigation-guard-page.js)

1. **Trusted click:** On each trusted click, store timestamp (used as “recent user gesture”).
2. **window.open:** Replace with a wrapper that:
   - If URL is not allowed protocol → report and return `null`.
   - If there was no recent trusted click → report and return `null` (blocks script-driven popups).
   - Otherwise call original `window.open`.
3. **location.replace / location.assign:** Replace with wrappers that:
   - If URL is not allowed protocol → report and return.
   - If no recent trusted click → report and return (blocks script-driven redirects).
   - Otherwise call the original.

So script-triggered redirects and popups are blocked; user-initiated http(s) navigation is allowed.

## Edge cases

| Case | Handling |
|------|----------|
| **Relative URLs** (`/path`, `#anchor`, `?q=`) | Treated as allowed (no scheme or leading `#`/`/`/`?`). |
| **Empty or missing href** | Treated as allowed (no navigation to a bad protocol). |
| **javascript:void(0)` / `javascript:...`** | Not http(s) or relative → blocked. |
| **intent:// (Android / app links)** | Not http(s) → blocked. |
| **mailto:, tel:** | Not http(s) → blocked. |
| **data:, blob:, file:** | Not http(s) → blocked. |
| **Same-tab vs target="_blank"** | Not distinguished; only protocol is checked. Both allowed if http(s). |
| **Link rewritten between mousedown and click** | Mousedown href stored; at click, if href changed and new value is not allowed → block and log as `nav.rewrite`. |
| **Script calls window.open without user click** | No recent trusted click → block (return `null`). |
| **Script sets location.replace without user click** | No recent trusted click → block (override does not call original). |
| **Legitimate form submit or button navigation** | No `<a href="...">` involved; handler does not run; no block. |
| **Click on non-link (div, span)** | No anchor; handler returns without doing anything; no block. |
| **CSP / strict pages** | Page script is loaded via `<script src="chrome-extension://...">` (extension URL), so it is allowed by CSP. |
| **Performance** | One mousedown and one click listener (capture), one small injected script; no timers, no polling. |

## Logging

Blocked attempts are appended to `chrome.storage.local` under `privacyMonitorBlockLog` (same key as existing click guard), up to 50 entries. Types:

- `nav.protocol` – Click on a link with a blocked protocol.
- `nav.rewrite` – Link href changed to a blocked protocol between mousedown and click.
- `nav.script` / `window.open` / `location.replace` / `location.assign` – Blocked by the page script (bad protocol or no trusted gesture).

No UI; log is for debugging and can be viewed/cleared in Options → Click protection → Blocked redirects log.

## Integration

- **Manifest:** `navigation-guard.js` is the first content script so it runs before other scripts. `navigation-guard-page.js` is in `web_accessible_resources`.
- **No options:** Always on; no toggle and no dependency on other extension settings.
- **Compatibility:** Works alongside existing click-guard and redirect-blocker; this guard only enforces protocol and script-triggered navigation rules.
