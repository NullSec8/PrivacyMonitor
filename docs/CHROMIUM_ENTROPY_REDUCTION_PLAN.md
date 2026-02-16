# Chromium-Level Fingerprint Entropy Reduction Plan

**Target:** EFF entropy &lt; ~10 bits, result "common / non-unique".  
**Constraints:** No per-session randomness; Chrome-like outputs; consistency across JS/CSS/WebGL/Canvas/HTTP.

---

## 1. WebGL (~16 bits) – PRIMARY

### Why it leaks
- `getParameter(UNMASKED_VENDOR/RENDERER)` varies by GPU/driver.
- `getSupportedExtensions()` varies by GPU and driver version.
- `getParameter(MAX_* )` (texture size, uniforms, etc.) varies.
- **Rendered pixel output** (readPixels after drawing) is the main fingerprint; EFF hashes this.

### Mitigations

| Layer | Action | Expected reduction |
|-------|--------|--------------------|
| **Vendor/Renderer** | Already spoofed to `Google Inc.` / `ANGLE (Generic GPU)`. | ~2–3 bits |
| **getParameter** | Normalize high-entropy params to common Chrome/ANGLE values (see table below). | ~2–4 bits |
| **getSupportedExtensions** | Return a fixed list matching Chrome on Windows with ANGLE (D3D11). | ~3–5 bits |
| **readPixels** | For small reads (e.g. ≤256×256), return a **deterministic** buffer (same gradient/noise for all users). | ~6–10 bits |

### WebGL getParameter normalization (common Chrome/ANGLE values)

| Constant | Value |
|----------|--------|
| MAX_TEXTURE_SIZE (0x0D33) | 16384 |
| MAX_RENDERBUFFER_SIZE (0x84E8) | 16384 |
| MAX_VERTEX_ATTRIBS (0x8869) | 16 |
| MAX_VERTEX_UNIFORM_VECTORS (0x8DFB) | 4096 |
| MAX_VARYING_VECTORS (0x8DFC) | 30 |
| MAX_COMBINED_TEXTURE_IMAGE_UNITS (0x8B4D) | 32 |
| MAX_VIEWPORT_DIMS (0x0D3A) | [16384, 16384] |
| ALIASED_LINE_WIDTH_RANGE | [1, 1] |
| ALIASED_POINT_SIZE_RANGE | [1, 1024] |

### readPixels override (pseudocode)

```js
// When (width * height) <= 256*256, return deterministic buffer
WebGLRenderingContext.prototype.readPixels = function(x, y, w, h, format, type, pixels) {
  if (w * h <= 65536 && format === 0x1908 && type === 0x1401) { // RGBA, UNSIGNED_BYTE
    const len = w * h * 4;
    for (let i = 0; i < len; i += 4) {
      const idx = (i/4); const px = idx % w, py = (idx/w)|0;
      pixels[i] = pixels[i+1] = pixels[i+2] = (px * 7 + py * 31 + (px*py)%17) % 256;
      pixels[i+3] = 255;
    }
    return;
  }
  return _realReadPixels.call(this, x, y, w, h, format, type, pixels);
};
```

### Risk
- Heavy WebGL apps (games, 3D) use large buffers; we only spoof small ones. **Low risk.**

---

## 2. Canvas (~6 bits) – HIGH

### Why it leaks
- Subpixel rendering, text antialiasing, and rounding differ by GPU/OS/fonts.
- Flat gray (current) is detectable and not Chrome-like.

### Mitigation
- **Deterministic Chrome-like pattern** for **all** canvas sizes (not only ≤256×256):
  - Same formula for every user: e.g. `R=G=B = (x*7 + y*31 + (x*y)%37 + 64) % 256`, A=255.
  - Slight spatial variation so the hash is stable and resembles a “noisy” Chrome output, not a perfect gradient.
- Apply in `getImageData` and before `toDataURL`/`toBlob` for **every** canvas read.

### Pseudocode

```js
function fillDeterministic(img, sx, sy, sw, sh) {
  const d = img.data;
  for (let j = 0; j < sh; j++)
    for (let i = 0; i < sw; i++) {
      const k = ((j + sy) * img.width + (i + sx)) * 4;
      const v = ((i+sx)*7 + (j+sy)*31 + ((i+sx)*(j+sy))%37 + 64) % 256;
      d[k]=d[k+1]=d[k+2]=v; d[k+3]=255;
    }
}
// Override getImageData: always fill with fillDeterministic for full canvas or subrect.
// Override toDataURL/toBlob: fill canvas with fillDeterministic then call real toDataURL/toBlob.
```

### Expected reduction
- ~6 bits (canvas hash becomes one value for all users).

### Risk
- Canvas-based CAPTCHAs or drawing tools may break. **Medium**; acceptable for privacy mode.

---

## 3. Fonts (~3.7 bits) – HIGH

### Why it leaks
- Full font enumeration via measureText(font) for many fonts, or document.fonts.
- Font set differs by OS install and Chrome version.

### Mitigation
- **Strict whitelist** matching default Chrome on Windows (Segoe UI, Arial, Times New Roman, etc.).
- **measureText**: if font is not in whitelist, return the same width as a fallback (e.g. Arial) or rounded width.
- **document.fonts.check(font)**: return true only for whitelist fonts.
- **document.fonts.size**: return whitelist length; **document.fonts** iterate only whitelist (or override to empty and rely on measureText).

### Whitelist (Chrome default Windows, ~30 fonts)

Arial, Arial Black, Calibri, Cambria, Cambria Math, Comic Sans MS, Consolas, Courier, Courier New, Georgia, Impact, Lucida Console, Lucida Sans Unicode, Microsoft Sans Serif, Microsoft YaHei, MingLiU, MS Gothic, MS PGothic, MS UI Gothic, Segoe UI, SimSun, Tahoma, Times New Roman, Trebuchet MS, Verdana, Webdings, Wingdings, etc.

### measureText override (pseudocode)

```js
const FONT_WHITELIST = ['Arial','Arial Black','Calibri',...].map(f=>f.toLowerCase());
function normalizeFont(font) {
  const f = (font||'').split(',')[0].trim().toLowerCase().replace(/['""]/g,'');
  return FONT_WHITELIST.some(w => f === w || f.startsWith(w+' ')) ? font : 'Arial';
}
CanvasRenderingContext2D.prototype.measureText = function(text) {
  const orig = this.font; this.font = normalizeFont(this.font);
  const result = _realMeasureText.call(this, text);
  this.font = orig;
  return { width: Math.round(result.width * 2) / 2 }; // round to 0.5
};
```

### Expected reduction
- ~3–4 bits.

### Risk
- Sites that rely on rare fonts may see wrong metrics. **Low** for mainstream sites.

---

## 4. AudioContext (~2.9 bits) – MEDIUM

### Why it leaks
- DynamicsCompressor and oscillator produce hardware-dependent output.
- Fingerprinters use OfflineAudioContext, run a short tone, read getChannelData().

### Mitigation
- **AudioBuffer.getChannelData**: for buffers of common fingerprint lengths (e.g. 4096, 1024, 8192), return a **deterministic** Float32Array (e.g. 0.01 * sin(i * 0.001)) instead of real data.
- Leave real-time AudioContext playback unchanged for normal use.

### Pseudocode

```js
const FP_AUDIO_LENGTHS = [1024, 4096, 8192, 16384];
const _getChannelData = AudioBuffer.prototype.getChannelData;
AudioBuffer.prototype.getChannelData = function(channel) {
  if (FP_AUDIO_LENGTHS.includes(this.length)) {
    const arr = new Float32Array(this.length);
    for (let i = 0; i < this.length; i++) arr[i] = 0.01 * Math.sin(i * 0.001);
    return arr;
  }
  return _getChannelData.call(this, channel);
};
```

### Expected reduction
- ~2–3 bits.

### Risk
- **Low**; fingerprinters use short offline buffers; real audio uses longer/live buffers.

---

## 5. Timezone & Locale

### Rule
- No partial spoofing: either **full spoof** (en-US + America/New_York) or **real**.
- We already spoof `navigator.language` / `languages` to en-US/en; so we must spoof timezone to match.

### Mitigation
- **Intl.DateTimeFormat.prototype.resolvedOptions**: return `{ locale: 'en-US', timeZone: 'America/New_York', ... }`.
- **Date.prototype.getTimezoneOffset**: return 300 (EST) or 240 (EDT); for simplicity use fixed -300 (EST).
- **Intl.DateTimeFormat().resolvedOptions().timeZone**: 'America/New_York'.

### Risk
- **Low**; sites may show Eastern time; user can disable anti-FP for local time.

---

## 6. Client Hints (Sec-CH-UA, userAgentData)

### Consistency
- User-Agent already set to Chrome 131.
- **navigator.userAgentData** (High Entropy): must match:
  - brands: `[{ brand: "Chromium", version: "131" }, { brand: "Google Chrome", version: "131" }, { brand: "Not_A Brand", version: "24" }]`
  - mobile: false
  - platform: "Windows"
  - getHighEntropyValues(): fullVersion, platformVersion, etc. – return fixed Chrome-on-Windows values.
- **HTTP headers** (when anti-FP on): set on main-frame and subresource requests:
  - `Sec-CH-UA`: `"Chromium";v="131", "Google Chrome";v="131", "Not_A Brand";v="24"`
  - `Sec-CH-UA-Mobile`: `?0`
  - `Sec-CH-UA-Platform`: `"Windows"`

### Implementation
- **JS**: Override `navigator.userAgentData` with a proxy that returns the above.
- **HTTP**: In WebResourceRequested, when anti-FP is on, call `e.Request.Headers.SetHeader("Sec-CH-UA", ...)` etc.

### Risk
- **Low**; values are standard Chrome.

---

## 7. Verification Loop

1. After each vector change, run **EFF Cover Your Tracks** and note entropy.
2. Run **FingerprintJS** demo and **BrowserLeaks** (Canvas, WebGL, Fonts).
3. Identify the **largest remaining contributor** and iterate only on that.
4. Target: WebGL + Canvas + Fonts + Audio + consistency → **&lt; ~10 bits** and “not unique” on EFF.

---

## 8. Expected Entropy Reduction (summary)

| Vector    | Before | After (target) | Method                          |
|-----------|--------|----------------|----------------------------------|
| WebGL     | ~16    | ~0–1           | Normalize params + extensions + readPixels |
| Canvas    | ~6     | ~0             | Deterministic pattern, all sizes |
| Fonts     | ~3.7   | ~0–0.5         | Whitelist + measureText          |
| Audio     | ~2.9   | ~0             | Deterministic getChannelData     |
| Other     | —      | —              | Timezone + userAgentData + Sec-CH-UA |
| **Total** | ~18.26 | **&lt; ~10**   |                                 |

---

## 9. Compatibility Risk Assessment

| Change        | Break risk | Mitigation                          |
|---------------|------------|-------------------------------------|
| WebGL readPixels | Low     | Only small buffers; games use large |
| Canvas spoof all | Medium  | Optional “allow canvas” per site   |
| Font whitelist   | Low     | Round to 0.5; Arial fallback        |
| Audio buffer     | Low     | Only common FP lengths              |
| Timezone spoof   | Low     | User can disable anti-FP            |
| Client Hints     | Low     | Standard Chrome values              |
