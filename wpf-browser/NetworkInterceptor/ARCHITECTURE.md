# Live Network Interceptor — Architecture (Enterprise)

## 1. Architecture diagram (textual)

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ MainWindow (WebView2 host)                                                        │
│   OnWebResourceRequested: if interceptor attached and service.IsPaused →           │
│     e.GetDeferral(), _pausedInterceptorDeferred.Add(deferral, entry, tab),        │
│     service.SetPausedPendingCount(n), return (request held at network level).     │
│   Else → build entry, blocking, RecordRequest(tabId, entry).                      │
│   OnWebResourceResponseReceived → RecordResponse(tabId, uri, status, headers…)    │
│   On Resume: service.Resumed → FlushPausedInterceptorRequests: for each deferred  │
│     tab.PendingRequests.Enqueue(entry), RecordRequest(tab.Id, entry),             │
│     deferral.Complete() (request sent; response arrives later, correlation intact).│
│   Sandboxed JS: FingerprintDetectionScript → postMessage(cat:'fp') …               │
│   MenuNetworkInterceptor: TabScopedRuntimeFingerprintProvider → service;          │
│     service.Resumed += FlushPausedInterceptorRequests.                             │
└───────────────────────────────┬───────────────────────────────────────────────────┘
                                │ INetworkInterceptorSink
                                ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ NetworkInterceptorService (IReplayCapableSink)                                    │
│   • Pause/Resume (Burp-style): IsPaused → RecordRequest returns; host holds      │
│     requests via deferral. PausedPendingCount, SetPausedPendingCount(n),          │
│     Resumed event → host flushes deferrals.                                        │
│   • Correlation: _pendingByUrl (FIFO per URL) + _byCorrelationId (replay)        │
│   • RecordReplayRequest / RecordResponse; batch timer; FlushBatch                  │
│   • Rolling buffer: TrimToMaxHistory(), IsTrimmed, _byCorrelationId cleanup       │
│   • Disposal: timer stop, clear all collections, no leaks                          │
└───────────────────────────────┬───────────────────────────────────────────────────┘
                                │ events (batch + metrics + cleared)
                                ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ NetworkInterceptorViewModel                                                       │
│   • Requests (ObservableCollection) ← BatchRequestAdded (single Dispatcher run)  │
│   • TrafficMetrics: Total, 3rd-party, Tracker, High risk, Avg score, Bytes,      │
│     Req/s, RpsMovingAverage (last 5 batches)                                      │
│   • Pause/Resume: PausedStatusText "Paused — N requests pending"; PausedPendingCount │
│     from service; OnPausedPendingCountChanged updates status; Resume flushes host. │
│   • Commands: Pause, Resume, Clear, CopyUrl, BlockDomain, Export, ReplayRequest   │
│   • Replay: RequestReplayHandler(sink, tabId) → HttpClient → RecordReplayRequest  │
│     + RecordResponseByCorrelation (same RiskScoring + pipeline)                  │
│   • Replay with modification: ReplayModifyWindow (Original read-only, Modified   │
│     editable DataGrid); changes local until Replay click; no auto-send.           │
│     Replay → ReplayOptions(OverrideHeaders, OverrideBody) → RequestReplayHandler  │
│     → RecordReplayRequest(entry, isModifiedReplay) → HttpClient → RecordResponse  │
│     ByCorrelation; RiskScore only after replay. SetReplayFailed on error.        │
│   • Batch replay: ReplaySelectedCommand; ReplaySelectedWithModifyCommand (open   │
│     ReplayModify per selected; send only on Replay). ReplayBatchProgressText     │
│     (Replaying 3/7…), ReplayBatchTooltip; toolbar "Batch replay" + context menu   │
│   • ChartSamples: last 60 MetricSample (Rps, RpsMovingAverage, HighRisk, etc.)     │
│     + RPS trendline (RpsMovingAverageNormalized), TopDomainsByHighRisk (top 10)   │
│   • Chart panel: Expander state bound to ChartPanelExpanded (remembered session); │
│     Export chart (PNG) via RenderTargetBitmap; SetStatus after export             │
│   • AlertManager: HighRiskThreshold/CriticalThreshold; HighRiskDetected/          │
│     CriticalRiskDetected events; optional PlaySoundOnCritical; Evaluate() on     │
│     BatchRequestUpdated; status bar shows "High/Critical risk: domain — explanation" │
│   • Export: full session JSON; streaming NDJSON (>10k) with progress + gzip;     │
│     CancelExportCommand + CancellationToken for cancellation                      │
└───────────────────────────────┬───────────────────────────────────────────────────┘
                                │ DataContext
                                ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│ NetworkInterceptorWindow (XAML)                                                    │
│   Toolbar: Pause | Resume (tooltips: hold/release at network); Status + ToolTip    │
│     PausedStatusText ("Paused — N requests pending"); Clear | Max | Batch ms …    │
│           | Export session                                                        │
│   Left: DataGrid (EnableRowVirtualization, Risk column ToolTip=RiskExplanation)  │
│   Right: Request/Response headers, Preview, Cookies, Timing, Privacy Analysis     │
│   Context menu: Replay, Replay with modification…, Batch replay, Batch replay   │
│     with modification…, Block domain, Export …                                    │
│   DataGrid row: HasReplayError → red tint + ToolTip ReplayStatusMessage;         │
│     IsReplayedWithModification → green tint + "Replayed with modified headers"   │
│   ReplayModifyWindow: Original (read-only) vs Modified (editable); Replay button  │
│     sends only on click; tooltip "Send this modified request; response will      │
│     update grid and recalc RiskScore." Cancel discards without sending.          │
│   High-risk rows: brief opacity animation; Risk tooltip; Fingerprint ⚠            │
└─────────────────────────────────────────────────────────────────────────────────┘

Implemented:
  RequestReplayHandler (IRequestReplayHandler): HttpClient replay → sink
  RequestModificationHandler (IRequestModificationHandler): header overrides for replay
Extensibility (interfaces): IBlockedDomainProvider, IRemoteLoggingSink, ITrackerListProvider
```

## 2. Request/response matching (correlation)

- **No WebView2 request ID** in the current API, so correlation is done inside the service.
- **Per-URL FIFO queue**: For each `RecordRequest(entry)` we push the new `InterceptedRequestItem` onto `_pendingByUrl[entry.FullUrl]`. For each `RecordResponse(uri, …)` with `correlationId == null` we dequeue from `_pendingByUrl[uri]` until we get an item with `!IsTrimmed`, then update that item and push it to `_updateQueue`.
- **Replay correlation**: `RecordReplayRequest(tabId, entry, correlationId, isModifiedReplay)` adds an item with that `CorrelationId` and does **not** enqueue to `_pendingByUrl`; sets `item.IsReplayedWithModification` when replayed with OverrideHeaders. `RecordResponseByCorrelation(...)` looks up the item in `_byCorrelationId`, updates it, runs the same response pipeline (RiskScoring, batching, _updateQueue). On replay failure, `SetReplayFailed(correlationId, errorMessage)` sets `item.ReplayStatusMessage` and enqueues to _updateQueue so the row shows "Replay failed: …".
- **Parallel identical URLs**: Multiple live requests to the same URL are matched in order (first request ↔ first response). Replayed requests never collide because they use CorrelationId.
- **Thread safety**: All access to `_items`, `_pendingByUrl`, and `_byCorrelationId` is under a single `lock`. Producer (WebView or replay) only enqueues to `_addQueue`/`_updateQueue` after releasing the lock; batch timer drains queues and raises events without holding the lock for UI work.

## 2a. Pause/Resume (Burp-style)

- **Pause**: User clicks Pause in interceptor toolbar. `NetworkInterceptorService.Pause()` sets `_paused = true` and raises `Paused`. MainWindow in `OnWebResourceRequested` (after building `RequestEntry`) checks `_interceptorSink is NetworkInterceptorService svc && svc.IsPaused && tab.Id == _interceptorTabId`. If true, calls `e.GetDeferral()`, adds `(deferral, entry, tab)` to `_pausedInterceptorDeferred`, calls `svc.SetPausedPendingCount(count)`, and returns without letting the request through. The request is **held at the network level** (no response until Resume).
- **Resume**: User clicks Resume. `NetworkInterceptorService.Resume()` sets `_paused = false` and raises `Resumed`. MainWindow (subscribed when opening interceptor window) runs `FlushPausedInterceptorRequests` on the UI thread: copies and clears `_pausedInterceptorDeferred`, sets `SetPausedPendingCount(0)`, then for each item calls `tab.PendingRequests.Enqueue(entry)`, `_interceptorSink.RecordRequest(tab.Id, entry)`, `deferral.Complete()`. The request is sent; when the response arrives, `OnWebResourceResponseReceived` matches by URL and calls `RecordResponse`, so correlation, RiskScoring, batching, and UI updates proceed as normal.
- **Metrics**: While paused, no new requests are recorded so RPS and charts stop updating. On resume, flushed requests enter the pipeline and appear in the grid with full RiskScore and metrics.

## 2b. Replay and Replay with Modification (reliable send)

- **Flow**: Click Replay (or Replay in ReplayModifyWindow) → ViewModel sets status "Replaying…" and `InvalidateRequerySuggested` → `RequestReplayHandler.ReplayAsync` validates URL (HTTP/HTTPS only), merges OverrideHeaders/OverrideBody **at send-time**, `RecordReplayRequest` → HttpClient.SendAsync (all request headers copied via TryAddWithoutValidation) → `RecordResponseByCorrelation` or on exception `SetReplayFailed(correlationId, message)` → batch pipeline updates grid; RiskScore applied after response. Response is disposed to avoid connection leaks.
- **No auto-send**: ReplayModifyWindow: changes local until Replay button; Cancel/close discards. ReplayWithModify awaits `ReplayOneAsync` so status and grid update before dialog flow completes.
- **Reliability**: Invalid or non-HTTP(S) URL → add row via RecordReplayRequest then SetReplayFailed so user sees "Replay failed: …" in row tooltip. After replay, ViewModel finally block calls `InvalidateRequerySuggested` so Replay/Replay with modification buttons re-enable.
- **Batch replay with modification**: ReplaySelectedWithModifyCommand opens ReplayModifyWindow per selected; send only when user clicks Replay in each dialog.

## 3. Batching implementation

- **Producer**: `RecordRequest` adds item to `_items` and `_pendingByUrl`, then `_addQueue.Enqueue(item)`. `RecordResponse` finds item by URL queue, updates it, then `_updateQueue.Enqueue(item)`.
- **Consumer**: Timer fires every `BatchIntervalMs` (default 80 ms). In `FlushBatch` we drain `_addQueue` and `_updateQueue` into two lists, then raise `BatchRequestAdded(adds)` and `BatchRequestUpdated(updates)`. ViewModel subscribes and runs a single `Dispatcher.BeginInvoke` to add all new items to `ObservableCollection` and refresh status.
- **Configurable**: `BatchIntervalMs` (50–500) is exposed on the service and in the UI (toolbar “Batch ms” box).

## 4. Performance strategy

- **No per-request UI work**: Request/response handlers only enqueue; UI updates happen in batches on the timer.
- **Single Dispatcher round-trip per batch**: One `BeginInvoke` adds many items instead of one per request.
- **Rolling buffer**: `MaxHistory` (e.g. 2000) caps memory; when we trim we set `IsTrimmed = true` so dequeued-but-trimmed items are skipped and not updated.
- **Metrics**: When `_items.Count < IncrementalMetricsThreshold` (10k), metrics are computed from `_items` once per batch. When above threshold, **incremental counters** are used (`_incTotal`, `_incThirdParty`, `_incTracker`, `_incHighRisk`, `_incTransferredBytes`, `_incSumRiskScore`); they are incremented in `RecordRequest`/`RecordReplayRequest`/`RecordResponse` and decremented in `TrimToMaxHistory`, so no full scan per batch at high load.

## 5. RiskScoring (unit-testable)

- **Pure function**: `RiskScoring.Compute(entry, responseHeaders, runtimeFingerprintSignals)` returns `RiskResult(Score, Level, Category, Explanation)`. No UI or I/O.
- **Runtime fingerprint signals (realtime JS sandbox)**: PrivacyEngine.FingerprintDetectionScript runs in WebView2 document context (canvas, WebGL, AudioContext, Navigator, Screen, Font, Battery, MediaDevice, timezone, Performance, Plugin, Connection, Behavioral). Script posts `{cat:'fp', type, detail}` via `chrome.webview.postMessage`. MainWindow OnWebMessage maps type to signal name via `TabScopedRuntimeFingerprintProvider.MapFingerprintTypeToSignal` (e.g. "Canvas Fingerprinting"→"canvas", "WebGL Fingerprinting"→"webgl") and stores per-tab in `_fingerprintSignalsByTab`. When opening the interceptor, MainWindow passes `TabScopedRuntimeFingerprintProvider(tab.Id, _fingerprintSignalsByTab)` to `NetworkInterceptorService`; `GetActiveSignals()` returns that tab’s signals so RiskScoring weights them. Sandbox is isolated to document context (no cross-site execution; signals are same-origin to the page).
- **Plugin patterns**: Optional `IFingerprintPatternProvider` (service ctor). `GetFingerprintPatterns()` and `GetTrackerUrlPatterns()` supply additional URL patterns; merged with built-in in `RiskScoring.Compute(..., patternProvider)`. Enables dynamic loading of new fingerprint/tracker patterns without changing core code.
- **Multi-factor weighted**: Third-party, known/heuristic tracker, **fingerprinting** (signals + URL patterns: fingerprintjs, canvas, webgl, audioctx, evercookie, creepjs, amiunique, getcontext, client-hints, entropy, deviceid, browserid), **known tracker URL patterns** (google-analytics, googletagmanager, doubleclick, facebook/tr, segment, mixpanel, hotjar, fullstory, clarity.ms, etc.), cookie flags (Secure, HttpOnly, SameSite=None without Secure), third-party cookies, tracking headers, referrer leakage. Each factor has (weight, points); score normalized to 0–100.
- **Levels**: Low (&lt;40), Medium (40–69), High (70–84), Critical (85–100).
- **RiskExplanation**: Human-readable string built from factor details (e.g. "[Fingerprinting] FingerprintJS library. [Third-party] Request domain differs from page origin.").
- **Apply**: `RiskScoring.Apply(item, entry, responseHeaders, runtimeFingerprintSignals)` writes `Score`, `Level`, `Category`, `RiskExplanation` onto `InterceptedRequestItem`. Used at request capture (no response yet) and again at response (with headers/cookies).

## 6. Export

- **Selected request**: One item → `InterceptorExportRoot` with `Requests[0]` and `FullSession = false`.
- **Full session**: All items + `SessionMetrics` in the same schema (in-memory; use streaming for large sessions).
- **Streaming export**: For >10k requests, **Export full session (streaming)** writes NDJSON. **Export (streaming)** with progress runs in `Task.Run` with `CancellationToken`; reports `ExportProgress` 0–100; **Cancel** button (toolbar, when export in progress) calls `CancelExportCommand` and cancels the run. Optional **Gzip** compresses to `.ndjson.gz`.
- **Schema**: `InterceptorExportRoot` (Version, ExportId, ExportedAtUtc, FullSession, Requests, SessionMetrics). Each request includes `RiskScore`, `RiskLevel`, `Category`, `RiskExplanation`, `PrivacyAnalysis`, and **Replay**: `ReplayMetadata { CorrelationId, CanReplay, Method, FullUrl, ModifiedReplay }` for replay tooling and audit (ModifiedReplay true when replayed with modified headers).

## 7. Memory and disposal

- **Rolling buffer**: Trim when `_items.Count > MaxHistory`; set `IsTrimmed`, remove from `_byCorrelationId`, then remove from `_items`. Pending URL queues skip trimmed items when dequeuing.
- **Replay and trim**: Replayed items are in `_items` and `_byCorrelationId`; when trimmed they are removed from both. Replay response that arrives after trim will not find the item (already removed from `_byCorrelationId`); handler ignores.
- **Event cleanup**: ViewModel unsubscribes from service events and disposes the service on window close; service stops the timer and clears all queues and dictionaries in `Dispose`.
- **No leaks**: No long-lived references to WebView or MainWindow from the interceptor after close. MaxHistory configurable up to 50k.

## 8. Scaling beyond 50k requests

- **Larger history**: Increase `MaxHistory` (e.g. 100k) only if UI remains responsive; DataGrid row virtualization is enabled (EnableRowVirtualization, VirtualizingPanel.VirtualizationMode=Recycling) so only visible rows are materialized.
- **Batch interval**: Under 500+ req/s, keep `BatchIntervalMs` at 50–80 ms so batches stay bounded (e.g. ~25–40 requests per batch); single Dispatcher run per batch keeps UI smooth.
- **Metrics**: **Incremental counters** are used when `_items.Count >= IncrementalMetricsThreshold` (10k); no full scan per batch. See §4.
- **Export**: **Full session** serializes in memory. For **>10k requests** use **Export (streaming)** (NDJSON with progress bar; optional gzip). No 50k-request in-memory spike; file is line-by-line readable.
- **Replay**: Replay is out-of-band (HttpClient); no impact on WebView or batch throughput. Replayed responses are matched by CorrelationId and go through the same pipeline; they consume one slot in `_byCorrelationId` until response or trim.

## 9. Key code snippets

**Replay (ViewModel)**  
ViewModel holds `_tabId` and creates `RequestReplayHandler((IReplayCapableSink)_service, _tabId)`. On Replay command it calls `await handler.ReplayAsync(SelectedRequest, null)`. Handler builds synthetic `RequestEntry`, calls `_sink.RecordReplayRequest(_tabId, entry, correlationId)`, sends `HttpClient` request, then `_sink.RecordResponseByCorrelation(_tabId, correlationId, uri, statusCode, headers, contentType, size)` so the replayed response is matched and scored.

**Modification before replay**  
`RequestReplayHandler` accepts optional `IRequestModificationHandler`. In `ReplayAsync`, if modifier is set it calls `TryModifyRequest(request, out newHeaders)` and merges headers into the synthetic entry; `ReplayOptions.OverrideHeaders` is applied otherwise. So replay can send modified headers without changing the grid row.

**Batch flush (service)**  
```csharp
private void FlushBatch(object? state)
{
    var adds = new List<InterceptedRequestItem>();
    while (_addQueue.TryDequeue(out var a)) adds.Add(a);
    var updates = new List<InterceptedRequestItem>();
    while (_updateQueue.TryDequeue(out var u)) updates.Add(u);
    if (adds.Count > 0) BatchRequestAdded?.Invoke(adds);
    if (updates.Count > 0) BatchRequestUpdated?.Invoke(updates);
    // ... compute metrics under lock, then MetricsUpdated?.Invoke(metrics);
}
```
ViewModel subscribes and runs a single `Dispatcher.BeginInvoke` to add `adds` to `ObservableCollection` and refresh status.

**RiskScoring (fingerprint + tracker URL)**  
Fingerprint patterns (pattern, weight, points, detail) include fingerprintjs, canvas, webgl, audioctx, evercookie, creepjs, etc. Tracker URL patterns (pattern, weight, points) include google-analytics, googletagmanager, doubleclick, segment, hotjar, clarity.ms, etc. First match in each list adds a factor; score is weighted sum / weight sum, clamped 0–100; level = Critical/High/Medium/Low; explanation = concatenation of "[Factor] Detail".
