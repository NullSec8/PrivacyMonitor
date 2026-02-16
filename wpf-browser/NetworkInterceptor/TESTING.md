# Live Network Interceptor — Testing & Reliability

## Unit tests (RiskScoring)

`RiskScoring.Compute(entry, responseHeaders)` is pure and synchronous. Example test (xUnit/NUnit style):

```csharp
[Fact]
public void Compute_FirstParty_NoSignals_ReturnsLowRisk()
{
    var entry = new RequestEntry
    {
        FullUrl = "https://example.com/page",
        IsThirdParty = false,
        TrackerLabel = "",
        ThreatConfidence = 0,
        RequestHeaders = new Dictionary<string, string>(),
        ResponseHeaders = new Dictionary<string, string>()
    };
    var result = RiskScoring.Compute(entry, null);
    Assert.InRange(result.Score, 0, 39);
    Assert.Equal("Low", result.Level);
    Assert.Contains("First-party", result.Category);
}

[Fact]
public void Compute_FingerprintUrl_ReturnsHighRisk()
{
    var entry = new RequestEntry
    {
        FullUrl = "https://cdn.example.com/fingerprintjs/v3.js",
        IsThirdParty = true,
        TrackerLabel = "",
        ThreatConfidence = 0
    };
    var result = RiskScoring.Compute(entry, null);
    Assert.True(result.Score >= 40);
    Assert.Equal("Fingerprinting", result.Category);
    Assert.Contains("Fingerprint", result.Explanation);
}
```

## Integration test plan

1. **Correlation (URL FIFO)**  
   - Record N requests to the same URL in order; record N responses to that URL in order.  
   - Assert: items in grid match 1:1 (first request with first response, etc.).  
   - Use a test sink that captures `RecordRequest` and `RecordResponse` order and correlates by URL.

2. **Correlation (Replay by CorrelationId)**  
   - Call `RecordReplayRequest(tabId, entry, guid)`.  
   - Call `RecordResponseByCorrelation(tabId, guid, uri, status, headers, contentType, size)`.  
   - Assert: the item with that `CorrelationId` has `StatusCode`, `ResponseHeaders`, and updated `RiskExplanation`; no other item is updated.

3. **Batching**  
   - Fire 100 `RecordRequest` calls from multiple threads; wait 2× BatchIntervalMs.  
   - Assert: `BatchRequestAdded` is invoked with a list of length ≤ 100; total items in the list equals 100; UI thread sees a single batch (or a small number of batches), not 100 individual updates.

4. **Rolling buffer + IsTrimmed**  
   - Set `MaxHistory = 5`. Record 10 requests (same URL or different).  
   - Assert: only 5 items remain; when responses arrive for the first 5, they update the first 5 items (or the 5 that remain after trim). Responses for the trimmed items do not update any visible item (trimmed items skipped in URL queue).

5. **Replay end-to-end**  
   - Create `RequestReplayHandler(sink, tabId)`, `ReplayAsync(item, null)` for a GET URL.  
   - Assert: one new item appears (replay request); after response, that item has status and headers; `RiskScoring` was applied (e.g. `RiskExplanation` non-empty).

6. **Batch replay + progress**  
   - Select multiple items; run ReplaySelectedCommand.  
   - Assert: `ReplayBatchProgressText` shows "Replaying 1/N…" through "Replaying N/N…"; status and tooltip update; after completion, "Replay done: N/N requests sent."

7. **Export cancellation**  
   - Start streaming export for a large session; call CancelExportCommand (or equivalent) before completion.  
   - Assert: export stops; status shows "Export cancelled."; no crash.

8. **RiskScoring with pattern provider**  
   - Implement `IFingerprintPatternProvider` returning custom patterns; pass to service ctor and to `RiskScoring.Compute(..., patternProvider)`.  
   - Assert: URLs matching plugin patterns are scored with the plugin’s weight/points; built-in patterns still apply when provider is null.

9. **Realtime JS fingerprint → RiskScoring**  
   - MainWindow OnWebMessage(cat=='fp') adds mapped signal to _fingerprintSignalsByTab[tab.Id]. Open interceptor with TabScopedRuntimeFingerprintProvider(tab.Id, _fingerprintSignalsByTab). Assert: GetActiveSignals() returns that tab's signals; RiskScoring factors include runtime signals.

10. **AlertManager**  
   - Set HighRiskThreshold=70, CriticalThreshold=85. Feed item with RiskScore 75 then 90. Assert: HighRiskDetected and CriticalRiskDetected fire; status shows domain/explanation; optional beep on Critical.

11. **Chart export PNG**  
   - Expand chart, click Export chart (PNG), save. Assert: PNG exists; status shows path.

12. **Disposal**  
   - Open interceptor window, attach to tab, record some requests, close window.  
   - Assert: no further `BatchRequestAdded`/`MetricsUpdated` after close; calling `RecordRequest` on the same service instance does not throw and does not add to UI (sink can be null or no-op after dispose).  
   - Optional: weak reference to service; GC collect; assert service is collectable (no leaks).


## Error handling

- **RecordRequest / RecordResponse**: All logic in try/catch; exceptions logged via `IInterceptorLogger`; no throw to WebView or timer callback.
- **Replay**: `ReplayAsync` catches exceptions, returns `false`, logs; ViewModel shows "Replay failed." and does not crash.
- **Export**: File write in try/catch; ViewModel shows "Export failed: {message}". Streaming export supports cancellation via `CancelExportCommand`; on cancel, status shows "Export cancelled."
- **Alerts**: AlertManager.Evaluate runs on BatchRequestUpdated; events are raised on the same thread (timer callback). ViewModel subscribes and marshals status update via Dispatcher.BeginInvoke so UI shows "High/Critical risk: domain — explanation." Optional beep on Critical (SystemSounds.Beep).
- **Batch timer**: `FlushBatch` catches nothing (no I/O); if event handlers throw, they run on timer thread — ensure ViewModel uses `Dispatcher.BeginInvoke` and does not throw in the delegate.

## Performance (no UI freezes)

- **500+ req/s**: Producers (WebView callbacks) only enqueue; batch timer runs every 50–80 ms and drains queues in one go; ViewModel adds all new items in one `Dispatcher.BeginInvoke`. No per-request UI work.
- **DataGrid**: `EnableRowVirtualization="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"` so only visible rows are created.
- **Metrics**: Computed once per batch under lock; then `MetricsUpdated` invoked; ViewModel updates `TrafficMetrics` on UI thread. RPS moving average is a small ring buffer (e.g. 5 values) in ViewModel.
