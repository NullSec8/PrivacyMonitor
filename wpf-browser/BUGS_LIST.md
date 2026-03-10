# Important Bugs (Findings)

List of notable bugs and risks in the Privacy Monitor codebase, ordered by severity.

---

## High severity

### 1. ~~Update apply: arbitrary file overwrite~~ — FIXED

**File:** `UpdateService.cs`
**Status:** Already mitigated. `TryHandleApplyUpdate` validates that the target path matches `Environment.ProcessPath` via `Path.GetFullPath` comparison. `ApplyUpdateAndRestart` uses `ProcessStartInfo.ArgumentList.Add()` for safe argument passing. `IsSafeVersion` restricts version strings to `[a-zA-Z0-9.\-]`.

---

### 2. ~~Update download: path traversal via version~~ — FIXED

**File:** `UpdateService.cs`
**Status:** Already mitigated by `IsSafeVersion()` which rejects any version containing `/`, `\`, `..`, or other path-traversal characters.

---

### 3. ~~Update apply: command-line injection~~ — FIXED

**File:** `UpdateService.cs`
**Status:** Already mitigated. Uses `ProcessStartInfo.ArgumentList.Add()` instead of string concatenation, which properly handles quoting and special characters.

---

## Medium severity

### 4. ~~Export CSV: NullReferenceException on null Path~~ — ALREADY SAFE

**File:** `MainWindow.xaml.cs`
**Status:** The CSV export already uses `(r.Path ?? "").Replace(...)` and `(r.Host ?? "").Replace(...)`. No NullReferenceException possible.

---

### 5. ~~Address bar: navigation to javascript: / file:~~ — FIXED

**File:** `MainWindow.xaml.cs`
**Status:** `IsAllowedNavigationScheme()` restricts navigation to only `http://` and `https://` schemes. `LooksLikeUrl()` cleaned up to not match `file:` or arbitrary `://` schemes. Any non-http(s) input is treated as a search query.

---

### 6. ~~Blocklist load: empty or invalid JSON clears blocklist~~ — FIXED

**File:** `ProtectionEngine.cs`
**Status:** Fixed. Empty blocklist file now triggers re-generation of defaults. Failed JSON deserialization logs the error and returns without clearing the blocklist. The outer catch now falls back to loading default entries instead of clearing everything.

---

## Lower severity / robustness

### 7. Dispatcher.Invoke with async lambda (MainWindow)

**File:** `MainWindow.xaml.cs` (e.g. NewWindowRequested)
**Status:** Known pattern limitation. Using `_ = CreateNewTab(e.Uri)` inside `Dispatcher.Invoke`.

---

### 8. async void event handlers

**Files:** Multiple (e.g. `MenuCheckForUpdates_Click`, `Screenshot_Click`, `ClearSiteData_Click`)
**Status:** Known. WPF event handlers require `async void` signature. Bodies should be wrapped in try/catch.

---

### 9. ZipArchive stream ownership (MainWindow.EnsureWebView2RuntimeAsync)

**File:** `MainWindow.xaml.cs`
**Status:** Low risk. The `using` pattern handles typical disposal paths.

---

### 10. Export streaming: stream disposal when useGzip is true (NetworkInterceptorViewModel)

**File:** `NetworkInterceptorViewModel.cs`
**Status:** Low risk. Current code is safe in practice.

---

## Summary

| # | Severity | Area             | Description                                               | Status |
|---|----------|------------------|-----------------------------------------------------------|--------|
| 1 | High     | UpdateService    | Apply-update arbitrary file overwrite                     | FIXED  |
| 2 | High     | UpdateService    | Version string path traversal                             | FIXED  |
| 3 | High     | UpdateService    | Command-line injection                                    | FIXED  |
| 4 | Medium   | MainWindow       | CSV export null Path                                      | SAFE   |
| 5 | Medium   | MainWindow       | Address bar allows javascript:/file:                      | FIXED  |
| 6 | Medium   | ProtectionEngine | Bad blocklist JSON clears blocklist                       | FIXED  |
| 7 | Low      | MainWindow       | Dispatcher.Invoke + async lambda                          | Known  |
| 8 | Low      | Multiple         | async void in event handlers                              | Known  |
| 9 | Low      | MainWindow       | Zip stream disposal in WebView2 bootstrap                 | Low    |
| 10| Low      | NetworkInterceptor| Export streaming stream disposal with GZip               | Low    |
