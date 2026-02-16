# Important Bugs (Findings)

List of notable bugs and risks in the Privacy Monitor codebase, ordered by severity.

---

## High severity

### 1. Update apply: arbitrary file overwrite (UpdateService.TryHandleApplyUpdate)

**File:** `UpdateService.cs`  
**Issue:** `TryHandleApplyUpdate` uses `args[2]` as the new exe and `args[3]` as the target path, then does `File.Copy(newExe, currentExe, true)` without checking that `currentExe` is the actual running process. Anyone who can run the app with custom arguments could overwrite any file, e.g.:

```text
PrivacyMonitor.exe --apply-update "C:\path\to\good.exe" "C:\Windows\System32\sensitive.dll"
```

**Fix:** Validate that `currentExe` is the real process path (e.g. `Environment.ProcessPath` or `Process.GetCurrentProcess().MainModule?.FileName`) before copying. Reject and exit if it does not match.

---

### 2. Update download: path traversal via version (UpdateService)

**File:** `UpdateService.cs`  
**Issue:** The `version` string from the server is used in file paths:

- `Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}.zip")`
- `Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}")`

If the server (or an attacker) returns a version like `..\..\..\evil` or `1.0.0\..\..\sensitive`, the zip could be written or extracted outside the temp folder.

**Fix:** Sanitize `version` before use: allow only alphanumerics, dots, and hyphens (e.g. regex `^[0-9a-zA-Z.\-]+$`), or use `Path.GetFileName(version)` and reject if the result is empty or changed.

---

### 3. Update apply: command-line injection (UpdateService.ApplyUpdateAndRestart)

**File:** `UpdateService.cs`  
**Issue:** Arguments are built as:

```csharp
Arguments = $"--apply-update \"{exePath}\" \"{currentExe}\""
```

If `exePath` or `currentExe` contain a double-quote character, the command line can be broken and other commands can be injected.

**Fix:** Escape quotes in both paths (e.g. `\"` → `\\\"` or use a proper escaping helper / pass arguments via an array or API that avoids manual quoting).

---

## Medium severity

### 4. Export CSV: NullReferenceException on null Path (MainWindow)

**File:** `MainWindow.xaml.cs` (Export CSV, ~line 2921)  
**Issue:** The CSV export does:

```csharp
$"{r.Path.Replace("\"", "\"\"")}"
```

If `r.Path` is null (e.g. from JSON or edge-case request data), this throws.

**Fix:** Use `(r.Path ?? "").Replace("\"", "\"\"")`. Optionally guard `r.TrackingParams` and `r.DataClassifications` with `?? Array.Empty<string>()` or similar for `string.Join`.

---

### 5. Address bar: navigation to javascript: / file: (MainWindow.Navigate)

**File:** `MainWindow.xaml.cs`  
**Issue:** `LooksLikeUrl` returns true for anything containing `://`, so `javascript:alert(1)` or `file:///C:/sensitive` are treated as URLs and passed to `CoreWebView2.Navigate(url)`. The app then navigates to that URL. For a desktop app this is low impact but can be surprising or used in social engineering (e.g. “paste this in the address bar”).

**Fix:** Restrict allowed schemes (e.g. only `http`, `https`, and optionally `file`) before calling `Navigate`. Reject or treat as search terms anything like `javascript:`, `data:`, `vbscript:`, etc.

---

### 6. Blocklist load: empty or invalid JSON clears blocklist (ProtectionEngine)

**File:** `ProtectionEngine.cs` (LoadBlocklist)  
**Issue:** If `BlocklistPath` exists but its content is empty or invalid JSON, `JsonSerializer.Deserialize<BlocklistFile>(json)` or the fallback deserialize can throw. The catch block then clears `_staticBlocklist` and leaves the user with no blocklist until defaults are re-merged or the file is fixed.

**Fix:** If `string.IsNullOrWhiteSpace(json)` or deserialization fails, keep the existing in-memory blocklist instead of clearing it, or load a built-in default and optionally merge with file contents when valid.

---

## Lower severity / robustness

### 7. Dispatcher.Invoke with async lambda (MainWindow)

**File:** `MainWindow.xaml.cs` (e.g. NewWindowRequested)  
**Issue:** `Dispatcher.Invoke(async () => await CreateNewTab(e.Uri))` schedules an async void-like delegate. Exceptions and completion of the async work are not observed; the Invoke returns as soon as the first await is hit.

**Fix:** Use `Dispatcher.Invoke(() => { _ = CreateNewTab(e.Uri); })` or run the async method without Invoke and marshal only the UI updates to the dispatcher when needed.

---

### 8. async void event handlers

**Files:** Multiple (e.g. `MenuCheckForUpdates_Click`, `Screenshot_Click`, `ClearSiteData_Click`, `ExportSessionStreamingAsync` in ViewModel)  
**Issue:** `async void` prevents exceptions from being caught by the caller and can crash the process if an exception is thrown.

**Fix:** Prefer `async Task` and call with `_ = HandlerAsync();` or attach continuation/catch in a central place. If keeping `async void`, wrap the body in try/catch and log or show a message for errors.

---

### 9. ZipArchive stream ownership (MainWindow.EnsureWebView2RuntimeAsync)

**File:** `MainWindow.xaml.cs`  
**Issue:** `zipStream` is obtained from `GetManifestResourceStream` and then passed to `new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false)`. If an exception occurs before the using block disposes the archive, the stream might not be disposed in all code paths (depending on ZipArchive behavior).

**Fix:** Ensure the stream is always disposed, e.g. with `await using var zipStream = ...` (if async) or a nested using so that both the archive and the stream are disposed.

---

### 10. Export streaming: stream disposal when useGzip is true (NetworkInterceptorViewModel)

**File:** `NetworkInterceptorViewModel.cs`  
**Issue:** When `useGzip` is true, `stream = new GZipStream(stream, ...)` and the `StreamWriter` is created with `leaveOpen: false`. If an exception occurs after opening the file but before the writer is fully disposed, the underlying file stream might not be closed in all paths. Current code is likely safe in practice but is a bit fragile.

**Fix:** Use explicit `using` for both the file stream and the GZipStream (or a single `await using` chain) so disposal order and ownership are obvious and exceptions don’t leave handles open.

---

## Summary

| # | Severity | Area            | One-line description                                      |
|---|----------|-----------------|-----------------------------------------------------------|
| 1 | High     | UpdateService   | Apply-update can overwrite any file via args[3]           |
| 2 | High     | UpdateService   | Version string can cause path traversal in temp           |
| 3 | High     | UpdateService   | Unescaped quotes in apply-update command line             |
| 4 | Medium   | MainWindow      | CSV export crashes if request Path is null                |
| 5 | Medium   | MainWindow      | Address bar allows javascript:/file: navigation           |
| 6 | Medium   | ProtectionEngine| Bad blocklist JSON clears in-memory blocklist              |
| 7 | Low      | MainWindow      | Dispatcher.Invoke + async lambda                          |
| 8 | Low      | Multiple        | async void in event handlers                              |
| 9 | Low      | MainWindow      | Zip stream disposal in WebView2 bootstrap                 |
| 10| Low      | NetworkInterceptor | Export streaming stream disposal with GZip             |

Fix order suggestion: **1 → 2 → 3** (security), then **4** (crash), then **5, 6** (behavior/robustness), then **7–10** (cleanup and resilience).
