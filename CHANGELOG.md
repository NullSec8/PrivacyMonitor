# Changelog

## 2026-02-16 — Comprehensive Code Review & Fix

### Bug Fixes

**Security**
- **UpdateService.cs**: Verified all 3 high-severity security bugs are already mitigated (path traversal via `IsSafeVersion`, file overwrite via `ProcessPath` validation, injection via `ArgumentList.Add`). Added `try-catch` around `ZipFile.ExtractToDirectory` with descriptive error messages. Added diagnostic logging to apply-update failure path.

**Crash Prevention**
- **ForensicEngine.cs**: Fixed `NullReferenceException` in `ExtractIdentifiers()` and `AnalyzeDataFlow()` when `RequestHeaders` or `ResponseHeaders` are null. Both methods now use null-coalesced header dictionaries.
- **ProtectionEngine.cs**: `LoadBlocklist()` no longer clears the in-memory blocklist on JSON parse failure. Empty blocklist files trigger regeneration of defaults. Outer catch falls back to loading defaults instead of clearing state.
- **BoolToVisibilityConverter.cs**: Fixed `CS1003`/`CS1525` syntax error caused by `bool?` pattern in switch expression.
- **ThreatSimulation.cs**: Fixed `CS8618` warning — `ProtectionGrade` now initialized to `""`. Fixed `CS8604` nullable warnings in `CountAllTrackingCookies` calls.

**Build Errors (pre-existing)**
- **DebugLogger.cs**: Fixed `CS0119` — `Debug.WriteLine` now fully qualified as `System.Diagnostics.Debug.WriteLine` to avoid conflict with the class's own `Debug()` method.
- **ExportSchema.cs**: Fixed `CS8852` — `SessionMetrics` changed from `init` to `set` to allow assignment after construction.
- **SystemThemeDetector.cs**: Fixed `CS8625` nullable warning — `new object[]` changed to `new object?[]` for null sender parameter.

### Performance Improvements

- **ProtectionEngine.cs**: `GetDefaultBlocklistEntries()` deduplication changed from O(n²) `list.Any()` scan to O(1) `HashSet<string>.Add()` lookup. Significant improvement for 800+ domain blocklist.
- **PrivacyEngine.cs**: `DetectTrackerFull()` rewritten from linear `foreach` over `TrackerLookup` dictionary to O(1) `TryGetValue` with domain suffix walking. Eliminates full dictionary iteration per request.

### Security Hardening

- **ChromeExtensionExport.cs**: Added input validation (`ValidateInputs`) for `extensionDir` and `domains` parameters. Added `SanitizeDomain()` that strips control characters, validates against a safe domain regex pattern, and enforces 253-char DNS limit. All exported domains are now sanitized before being written to JS or JSON files.
- **MainWindow.xaml.cs**: Cleaned up `LooksLikeUrl()` to no longer match `file:` scheme or arbitrary `://` protocols. Only `http://` and `https://` are recognized as URLs; the existing `IsAllowedNavigationScheme()` guard remains as defense-in-depth.
- **MainWindow.xaml.cs**: Fixed null-forgiving operator (`tab!.Title`) in `Star_Click` — replaced with safe null-coalescing access.

### Resource Cleanup

- **MainWindow.xaml.cs**: `MainWindow_Closing` now stops `_uiTimer` and disposes all tab WebView2 instances on window close, preventing timer callbacks and WebView2 events from firing after shutdown.

### UI / Theme Consistency

- **MainWindow.xaml**: Replaced 6 hardcoded color values with theme-aware `DynamicResource` references:
  - `#D93025` (4 instances) → `{DynamicResource DangerBrush}`
  - `#188038` (1 instance) → `{DynamicResource SuccessBrush}`
  - `#9AA0A6` / `#202124` (address suggestions) → `{DynamicResource TextMuted}` / `{DynamicResource TextPrimary}`
- Removed unused `HeaderGradient` brush definition (dead code).
- All colors now properly adapt to Light, Dark, Light.Large, and Dark.Large themes.

### Chrome Extension

- **manifest.json**: Removed invalid `"permissions"` entry from the permissions array (Chrome ignores it but it's technically incorrect).
- **cosmetic.js**: Fixed mode change handling — cosmetic hiding now re-injects CSS when mode switches from `off` back to `blockKnown` or `aggressive`. Previously, users had to reload the page.
- **background.js**: Added `console.warn` logging to `updateBadge` catch block for diagnostic visibility.

### Error Logging

Added diagnostic `Debug.WriteLine` logging to previously silent catch blocks:
- **MainWindow.xaml.cs**: `LoadSettings`, `ApplySettingsFromJson`, `AddHostObjectToScript` (settings and history bridges)
- **ProtectionEngine.cs**: `LoadBlocklist` (parse failure and outer catch), `GetDefaultBlocklistEntries` (PrivacyEngine enrichment failure)
- **UpdateService.cs**: `TryHandleApplyUpdate` failure

### Documentation

- **BUGS_LIST.md**: Updated all 10 entries with current fix status. High-severity items 1-3 marked as already mitigated. Medium items 4-6 marked as fixed/safe. Lower items 7-10 documented as known/low-risk.
- **CHANGELOG.md**: Created this file documenting all changes.

### Files Changed

| File | Changes |
|------|---------|
| `wpf-browser/ForensicEngine.cs` | Null-safe header access in `ExtractIdentifiers` and `AnalyzeDataFlow` |
| `wpf-browser/ProtectionEngine.cs` | HashSet dedup, blocklist error handling, logging |
| `wpf-browser/PrivacyEngine.cs` | O(1) tracker lookup with suffix walking |
| `wpf-browser/ChromeExtensionExport.cs` | Input validation, domain sanitization |
| `wpf-browser/UpdateService.cs` | Zip extraction error handling, logging |
| `wpf-browser/MainWindow.xaml.cs` | Timer cleanup, LooksLikeUrl fix, Star_Click null safety, logging |
| `wpf-browser/MainWindow.xaml` | Theme-consistent colors, dead code removal |
| `wpf-browser/ThreatSimulation.cs` | Nullable warnings fixed |
| `wpf-browser/SystemThemeDetector.cs` | Nullable warning fixed |
| `wpf-browser/NetworkInterceptor/BoolToVisibilityConverter.cs` | Switch expression syntax fix |
| `wpf-browser/NetworkInterceptor/DebugLogger.cs` | Fully qualified Debug.WriteLine |
| `wpf-browser/NetworkInterceptor/ExportSchema.cs` | init → set for SessionMetrics |
| `wpf-browser/BUGS_LIST.md` | Updated with fix status |
| `chrome-extension/manifest.json` | Removed invalid permission |
| `chrome-extension/cosmetic.js` | Re-injection on mode change |
| `chrome-extension/background.js` | Badge error logging |
