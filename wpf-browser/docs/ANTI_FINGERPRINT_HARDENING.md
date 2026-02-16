# Anti-Fingerprinting Hardening Plan

**Goal:** Reduce browser fingerprint uniqueness so the browser blends into a large anonymity set (similar to Firefox ESR / Brave / Tor Browser), while preserving usability.

**Strategy:** *Blend-in* — emulate a common real-world profile (Chrome on Windows) with **consistent, non-unique values** across all APIs. No randomization of fingerprintable data.

---

## 1. Fingerprinting Vectors & Mitigations

| Vector | Entropy contribution | Mitigation | Implementation |
|--------|----------------------|------------|-----------------|
| **User-Agent** | High (browser, version, OS) | Use single fixed UA: Chrome on Windows | `CoreWebView2.Settings.UserAgent` + JS override |
| **navigator.platform** | Medium | Fix to `Win32` | `Object.defineProperty(Navigator.prototype, 'platform', …)` |
| **navigator.vendor** | Low | Fix to `Google Inc.` | Same |
| **navigator.hardwareConcurrency** | Medium (~3–4 bits) | Fix to common value: `8` | No random; use constant |
| **navigator.deviceMemory** | Low (~2 bits) | Fix to `8` | Constant |
| **Canvas (toDataURL / getImageData)** | Very high (10+ bits) | **Static spoofed output** shared by all users | Return fixed ImageData / data URL for fingerprint-sized canvases |
| **WebGL (UNMASKED_VENDOR / RENDERER)** | High | Static: `Google Inc.` / `ANGLE (Generic GPU)` | Already done; ensure WebGL2 same |
| **WebGL extensions** | Medium | Return same extension list as generic ANGLE | Optional: spoof getSupportedExtensions |
| **AudioContext (DynamicsCompressor)** | High | Static output or block fingerprint path | Return deterministic getChannelData (e.g. zeros) for offline context |
| **Fonts (measureText / offsetWidth)** | Very high | **Minimal common font set**; block full enumeration | Spoof font list to ~20 common Windows fonts; cap measured families |
| **Screen (width, height, colorDepth, avail*)** | Medium | **Bucket** to common: 1920×1080, 24-bit | Fix to 1920, 1080, 1920, 1040, 24 |
| **devicePixelRatio** | Low | Fix to `1` or `1.25` (common) | `window.devicePixelRatio` |
| **Timezone (Intl)** | Medium | Coherent with language: e.g. `America/New_York` + `en-US` | Optional: spoof; or leave for compatibility |
| **Locale / language** | Medium | Fix to `en-US`, `['en-US','en']` | Align with UA and timezone |
| **navigator.plugins / mimeTypes** | Medium | Empty or minimal (Chrome-like: PDF) | Empty `[]` or 1 PDF plugin |
| **navigator.mediaDevices** | High | Return empty or generic 1 device | Empty list |
| **Battery API** | Low | Block or generic (charging, 100%) | Generic object |
| **Connection (NetworkInformation)** | Low | Generic: `4g`, downlink 10, rtt 50 | Already done |
| **Performance timing** | Medium | Reduce precision or spoof | Optional: round to 100ms |
| **Touch / maxTouchPoints** | Low | Fix to `0` (desktop profile) | Constant |
| **Permissions API** | Low | Deny or generic | Leave default |
| **CSS media queries** | Medium | Match spoofed screen (matchMedia) | Ensure window.innerWidth/Height consistent with screen |

---

## 2. Blend-In Profile (Chrome on Windows)

Single **fixed** profile used for all users:

- **User-Agent:** `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36`
- **platform:** `Win32`
- **vendor:** `Google Inc.`
- **hardwareConcurrency:** `8`
- **deviceMemory:** `8`
- **Screen:** width 1920, height 1080, availWidth 1920, availHeight 1040, colorDepth 24, pixelDepth 24
- **devicePixelRatio:** `1`
- **language:** `en-US`, **languages:** `['en-US','en']`
- **plugins / mimeTypes:** `[]` (or PDF-only to match Chrome)
- **WebGL:** vendor `Google Inc.`, renderer `ANGLE (Generic GPU)`
- **Canvas:** static shared output (same data URL / ImageData for fingerprint-sized canvases)
- **AudioContext:** deterministic or blocked for fingerprint pattern
- **Timezone:** leave system (or spoof to `America/New_York` for consistency with en-US)
- **maxTouchPoints:** `0`
- **Connection:** effectiveType `4g`, downlink 10, rtt 50
- **Battery:** generic (charging, level 1)

---

## 3. Canvas & WebGL Handling

### Canvas
- **Do NOT** expose raw hardware/rendering differences.
- **Strategy (a) Static spoofed output:** For any canvas where `getImageData` or `toDataURL` is used in a typical fingerprint way (e.g. canvas size in common fingerprint range 16×16 to 256×256), return a **fixed** ImageData and data URL shared by all users.
- Implementation: override `getImageData` and `toDataURL`/`toBlob`; for canvases with width×height ≤ 256×256, return a precomputed constant (e.g. 32×32 gray gradient or solid). Same bytes for every user → same hash → **zero entropy** from canvas.
- **Hash stability:** Same on every reload and every site → maximum anonymity set.

### WebGL
- Already spoofed: `getParameter(UNMASKED_VENDOR)` → `Google Inc.`, `getParameter(UNMASKED_RENDERER)` → `ANGLE (Generic GPU)`.
- Ensure WebGL2 has the same overrides.
- Optional: `getExtension()` return same supported list as generic ANGLE to avoid extension-based fingerprinting.

---

## 4. Fonts
- **Expose minimal common set:** When font enumeration is detected (e.g. many measureText/offsetWidth with different font families), cap the effective list to a **whitelist** of ~20 common Windows fonts (Arial, Helvetica, Times New Roman, Courier New, Verdana, etc.).
- **Prevent full enumeration:** Override `document.fonts.check()` / font-measure path to return true only for whitelisted fonts; for others return false so enumerators see a limited set.

---

## 5. Screen & Device Metrics
- **Bucket** to 1920×1080 (most common desktop).
- **Normalize:** screen.width=1920, screen.height=1080, screen.availWidth=1920, screen.availHeight=1040, colorDepth=24, pixelDepth=24.
- **DPR:** 1 or 1.25. Ensure `window.innerWidth` / `outerWidth` are not spoofed to avoid breaking layout; only `screen` and `devicePixelRatio` are normalized so fingerprinters see a common resolution.

---

## 6. Time & Locale
- **Align:** timezone ↔ language. If language is `en-US`, timezone should be a US zone (e.g. America/New_York). Avoid e.g. en-US + Europe/London.
- **Implementation:** Either leave system (simplest) or spoof `Intl.DateTimeFormat.prototype.resolvedOptions` to return `timeZone: 'America/New_York'` and `locale: 'en-US'` when anti-FP is on.

---

## 7. Consistency Rules
- **No contradictions:** Same value must not differ across JS, CSS, and HTTP. So:
  - User-Agent in HTTP headers = `navigator.userAgent` (set both: WebView2 Settings.UserAgent and, if needed, JS override for late reads).
  - Screen size in JS = what matchMedia would see (if we spoof screen, ensure innerWidth/outerWidth are consistent or leave layout-critical ones real and only spoof `screen` object).
- **Same value everywhere:** e.g. `hardwareConcurrency` is 8 in all code paths; no random choice.

---

## 8. Verification
- **EFF Cover Your Tracks:** Run before/after; aim for “unique” → “not unique” or lower entropy.
- **FingerprintJS demo:** Compare entropy bits before vs after; aim for &lt; 1 bit from our vectors.
- **BrowserLeaks:** Check Canvas, WebGL, Fonts, Screen; all should show common/shared values.

---

## 9. Risk Trade-offs

| Trade-off | Privacy | Compatibility |
|-----------|---------|---------------|
| Static canvas | High (same hash for all) | Some canvas CAPTCHAs or drawing apps may break; whitelist by size to allow large canvases |
| Empty plugins | High | Rare sites check for PDF plugin; can add 1 fake PDF plugin |
| Fixed screen | High | Very rare layout bugs if site uses screen.* for layout; 1920×1080 is safe |
| Spoofed UA | High | Must match real Chrome version range to avoid “old browser” blocks |
| Block Battery | High | Almost no sites need it |
| Empty mediaDevices | High | WebRTC/video call sites may need camera; can allow after user gesture |

---

## 10. Implementation Checklist

- [x] Remove all **random** values from anti-FP script (no `Math.random()` for fingerprintable APIs).
- [x] Use **fixed** navigator/spoofs (hardwareConcurrency=8, deviceMemory=8, platform=Win32, vendor=Google Inc.).
- [x] **Static canvas:** return fixed ImageData/toDataURL for small canvases.
- [x] **WebGL:** keep Google Inc. / ANGLE (Generic GPU).
- [x] **Screen:** bucket to 1920×1080, colorDepth 24, optional DPR 1.
- [x] **User-Agent:** set via CoreWebView2.Settings.UserAgent when anti-FP on.
- [ ] **Fonts:** minimal set (optional follow-up).
- [ ] **Timezone/locale:** optional spoof to America/New_York + en-US.
- [ ] **Verification:** run Cover Your Tracks and FingerprintJS before/after.
