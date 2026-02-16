# Fingerprint Replay Architecture

**Goal:** Make the browser fingerprint **match a real Chrome-on-Windows fingerprint** that exists in the EFF dataset by **replaying captured pixel/parameter data**, not synthetic formulas.

**Why replay beats synthetic:** EFF and similar systems compare hashes against a corpus of real browsers. Mathematically generated outputs (noise, gradients, formulas) produce hashes that **do not appear in that corpus**, so the browser is flagged as unique or synthetic. Replaying **exact bytes** from a real Chrome session yields a hash that **exists in the wild** and blends into the Chrome population.

---

## 1. Step-by-Step Replay Architecture

1. **Capture** – On a clean Chrome Stable (Windows, no flags, no extensions), run the capture tool to record:
   - Canvas ImageData (RGBA) for the exact sizes used by EFF/BrowserLeaks (e.g. 220×30, 16×16, 115×30).
   - WebGL readPixels output for the fingerprint test (e.g. 256×256 or the size the test uses).
   - WebGL getParameter values and getSupportedExtensions() list.
   - (Optional) AudioContext buffer for common FP lengths (4096, etc.).

2. **Store** – Save as `fingerprint-artifacts.json` in `%AppData%\PrivacyMonitor\` (or embed in build). Format below.

3. **Load** – At runtime, the app loads the JSON (if present) and passes it into the anti-fingerprint script.

4. **Inject** – The script receives artifact data as `window.__pmArtifacts` (injected before the rest of the script runs).

5. **Replay** – On intercept:
   - **Canvas getImageData(sx,sy,sw,sh):** If artifact has key `"swxsh"`, decode base64 and copy into the returned ImageData; else **pass-through** (no spoof).
   - **Canvas toDataURL / toBlob:** If artifact has key `"widthxheight"` for this canvas, fill canvas with artifact pixels then call real toDataURL/toBlob; else pass-through.
   - **WebGL readPixels:** If artifact has key for this width×height and format/type match, copy artifact bytes into the destination buffer; else pass-through.
   - **WebGL getParameter / getSupportedExtensions:** If artifact has `webgl.params` / `webgl.extensions`, return those; else pass-through.

6. **Detection heuristics (optional):** Only replay when the read “looks like” fingerprinting (e.g. small canvas, full-frame read, offscreen). For simplicity, v1 replays for **all** matching sizes when artifact exists.

---

## 2. Storage Format for Captured Artifacts

```json
{
  "version": 1,
  "source": "Chrome Stable Windows, no extensions",
  "canvas": {
    "220x30": "<base64 RGBA, 220*30*4 = 26400 bytes>",
    "16x16": "<base64 RGBA, 16*16*4 = 1024 bytes>",
    "115x30": "<base64 RGBA>"
  },
  "webgl": {
    "256x256": "<base64 RGBA>",
    "params": {
      "0x9245": "Google Inc.",
      "0x9246": "ANGLE (Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0)",
      "0x0D33": 16384,
      "0x84E8": 16384
    },
    "extensions": ["ANGLE_instanced_arrays", "EXT_blend_minmax", ...]
  },
  "audio": {
    "4096": "<base64 Float32, 4096 bytes>"
  }
}
```

- **canvas:** Keys are `"WxH"`. Value is base64 of raw RGBA (width×height×4 bytes).
- **webgl:** Keys are `"WxH"` for readPixels buffers. `params` keys are hex strings of getParameter() enum; values are as returned by Chrome. `extensions` is the array from getSupportedExtensions().
- **audio:** Keys are buffer length (e.g. "4096"); value is base64 of Float32Array (little-endian).

---

## 3. Capture Tool (Run in Real Chrome)

Use the provided **docs/capture-fingerprint-artifacts.html**:

1. Open in **Chrome Stable on Windows** (no extensions, default settings).
2. Click “Capture canvas” and “Capture WebGL” (and optionally “Capture audio”).
3. Copy the JSON from the textarea and save as `fingerprint-artifacts.json` in `%AppData%\PrivacyMonitor\`.
4. Restart Privacy Monitor; it will load the file and replay these artifacts.

The capture page:
- Draws the **same** text/shapes used by EFF/BrowserLeaks-style tests (e.g. Cwm fjordbank glyphs, rectangle).
- Reads ImageData and exports base64.
- For WebGL: creates a small context, draws the typical FP scene, readPixels, exports base64.
- Optionally runs OfflineAudioContext and exports getChannelData as base64.

---

## 4. Chromium Hooks / Override Points

| API | Hook | Replay behavior |
|-----|------|------------------|
| CanvasRenderingContext2D.getImageData | Override on prototype | If __pmArtifacts.canvas["swxsh"] exists, decode base64 into img.data and return img. Else call original. |
| HTMLCanvasElement.toDataURL | Override on prototype | If artifact for this.width×this.height exists, ctx.putImageData(decoded), then original toDataURL(). Else original. |
| HTMLCanvasElement.toBlob | Same as toDataURL | Same. |
| WebGLRenderingContext.readPixels | Override on prototype | If artifact for w×h exists and format/type are RGBA/UNSIGNED_BYTE, decode into pixels. Else original. |
| WebGLRenderingContext.getParameter | Override | If __pmArtifacts.webgl.params exists, return params[param] or original. |
| WebGLRenderingContext.getSupportedExtensions | Override | If __pmArtifacts.webgl.extensions exists, return that array. Else original. |
| AudioBuffer.getChannelData | Override | If artifact for this.length exists, return decoded Float32Array. Else original. |

---

## 5. Pseudocode for Replay Logic

```js
// Injection (C#): window.__pmArtifacts = <loaded JSON>;
const A = window.__pmArtifacts;
if (!A) return; // no spoof

// Canvas getImageData
const key = sw + 'x' + sh;
if (A.canvas && A.canvas[key]) {
  const bytes = atob(A.canvas[key]);
  const arr = new Uint8ClampedArray(bytes.length);
  for (let i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i);
  img.data.set(arr);
  return img;
}
return _getImageData.apply(this, arguments);

// WebGL readPixels
const key = w + 'x' + h;
if (A.webgl && A.webgl[key] && format === 0x1908 && type === 0x1401) {
  const bytes = atob(A.webgl[key]);
  for (let i = 0; i < bytes.length; i++) pixels[i] = bytes.charCodeAt(i);
  return;
}
_readPixels.apply(this, arguments);
```

---

## 6. Known Compatibility Breakage

- **Canvas:** Any site that relies on the **actual** drawn content (e.g. canvas-based drawing app, signature pad) will see replayed pixels instead when size matches. Mitigation: only replay for sizes that are known fingerprint sizes (e.g. 220×30, 16×16) or when a heuristic suggests FP.
- **WebGL:** Same: only replay for small, full-frame readPixels typical of FP; leave large buffers to real GPU.
- **Tor-level trade-offs:** Acceptable for maximum blend-in; optional “disable replay” per site if needed later.

---

## 7. Why This Approach Beats Synthetic Spoofing

- **EFF dataset:** Built from real browsers. Hashes are from real Canvas/WebGL output. A synthetic pattern (formula, noise) almost never collides with a hash in the dataset → classifier labels the browser as unique or non-human.
- **Replay:** The hash we produce is **identical** to the hash of the machine that was captured. That hash **is** in the dataset (or in the same population). So the browser **blends** into the Chrome anonymity set.
- **Realism:** Pixel layout, antialiasing, subpixel rendering, and driver quirks are preserved because we do not generate pixels—we replay them.

---

## 8. Verification

- Run **EFF Cover Your Tracks** after replay: entropy should drop and/or result should move toward “common” if the replayed hash is in their corpus.
- **BrowserLeaks Canvas/WebGL:** Hashes should match the Chrome instance that was captured.
- If the result is still “unique,” possible causes: (1) replayed hash is rare in EFF’s sample, (2) other vectors (fonts, audio, etc.) still differ, (3) dataset limits. Iterate by capturing from a **different** common Chrome version or machine to get a more frequent hash.
