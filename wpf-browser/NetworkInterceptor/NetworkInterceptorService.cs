using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrivacyMonitor;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Production-grade interceptor: correlation by URL+FIFO queue, batched UI updates,
    /// rolling buffer, metrics, and safe disposal.
    /// </summary>
    public sealed class NetworkInterceptorService : IReplayCapableSink, IDisposable
    {
        private readonly object _lock = new object();
        private readonly List<InterceptedRequestItem> _items = new List<InterceptedRequestItem>();
        /// <summary>Per-URL FIFO queue for reliable request/response matching (parallel identical URLs).</summary>
        private readonly Dictionary<string, Queue<InterceptedRequestItem>> _pendingByUrl = new Dictionary<string, Queue<InterceptedRequestItem>>(StringComparer.Ordinal);
        /// <summary>Replay correlation: match response by CorrelationId when not matching by URL.</summary>
        private readonly Dictionary<Guid, InterceptedRequestItem> _byCorrelationId = new Dictionary<Guid, InterceptedRequestItem>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<InterceptedRequestItem> _addQueue = new System.Collections.Concurrent.ConcurrentQueue<InterceptedRequestItem>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<InterceptedRequestItem> _updateQueue = new System.Collections.Concurrent.ConcurrentQueue<InterceptedRequestItem>();
        private readonly Timer _batchTimer;
        private readonly IInterceptorLogger? _logger;
        private readonly IRuntimeFingerprintSignalProvider? _runtimeFingerprintProvider;
        private readonly IFingerprintPatternProvider? _patternProvider;
        private string? _attachedTabId;
        private bool _paused;
        private int _maxHistory = 2000;
        private int _idCounter;
        private int _batchIntervalMs = 80;
        private bool _disposed;
        private DateTime _sessionStartUtc = DateTime.UtcNow;
        private int _requestsInCurrentWindow;
        private DateTime _rpsWindowStart = DateTime.UtcNow;

        /// <summary>When item count exceeds this, use incremental counters instead of full scan (avoids O(n) per batch).</summary>
        private const int IncrementalMetricsThreshold = 10_000;
        private long _incTotal;
        private long _incThirdParty;
        private long _incTracker;
        private long _incHighRisk;
        private long _incTransferredBytes;
        private double _incSumRiskScore;

        public NetworkInterceptorService(IInterceptorLogger? logger = null, IRuntimeFingerprintSignalProvider? runtimeFingerprintProvider = null, IFingerprintPatternProvider? patternProvider = null)
        {
            _logger = logger;
            _runtimeFingerprintProvider = runtimeFingerprintProvider;
            _patternProvider = patternProvider;
            _batchTimer = new Timer(FlushBatch, null, _batchIntervalMs, _batchIntervalMs);
        }

        /// <summary>Batch interval in ms (50–500). Higher = fewer UI updates, lower CPU.</summary>
        public int BatchIntervalMs
        {
            get => _batchIntervalMs;
            set
            {
                _batchIntervalMs = Math.Clamp(value, 50, 500);
                _batchTimer?.Change(_batchIntervalMs, _batchIntervalMs);
            }
        }

        public int MaxHistory
        {
            get => _maxHistory;
            set => _maxHistory = Math.Clamp(value, 100, 50_000);
        }

        public bool IsPaused => _paused;
        public string? AttachedTabId => _attachedTabId;

        /// <summary>Number of requests held at network level (host reports this when pause is active).</summary>
        public int PausedPendingCount { get; private set; }

        /// <summary>Raised when PausedPendingCount changes (marshal to UI for tooltip/status).</summary>
        public event Action? PausedPendingCountChanged;

        /// <summary>Called by host when it holds a request (deferral). Host calls this so UI can show "Paused — X pending".</summary>
        public void SetPausedPendingCount(int count)
        {
            if (PausedPendingCount == count) return;
            PausedPendingCount = count;
            PausedPendingCountChanged?.Invoke();
        }

        /// <summary>Raised when Resume() is called so host can flush held requests (complete deferrals).</summary>
        public event Action? Resumed;

        /// <summary>Raised when Pause() is called (optional for host).</summary>
        public event Action? Paused;

        /// <summary>Raised on batch timer with new items. Subscribe and marshal to UI thread.</summary>
        public event Action<IReadOnlyList<InterceptedRequestItem>>? BatchRequestAdded;

        /// <summary>Raised on batch timer with updated items. Marshal to UI thread.</summary>
        public event Action<IReadOnlyList<InterceptedRequestItem>>? BatchRequestUpdated;

        /// <summary>Raised when the log is cleared.</summary>
        public event Action? Cleared;

        /// <summary>Raised every batch with current metrics. Marshal to UI thread.</summary>
        public event Action<TrafficMetrics>? MetricsUpdated;

        public void AttachToTab(string? tabId)
        {
            lock (_lock)
            {
                _attachedTabId = tabId;
                _sessionStartUtc = DateTime.UtcNow;
                _rpsWindowStart = DateTime.UtcNow;
                _requestsInCurrentWindow = 0;
            }
        }

        public void Pause()
        {
            _paused = true;
            Paused?.Invoke();
        }

        public void Resume()
        {
            _paused = false;
            Resumed?.Invoke();
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
                _pendingByUrl.Clear();
                _byCorrelationId.Clear();
                _incTotal = _incThirdParty = _incTracker = _incHighRisk = 0;
                _incTransferredBytes = 0;
                _incSumRiskScore = 0;
                while (_addQueue.TryDequeue(out _)) { }
                while (_updateQueue.TryDequeue(out _)) { }
                _sessionStartUtc = DateTime.UtcNow;
                _rpsWindowStart = DateTime.UtcNow;
                _requestsInCurrentWindow = 0;
            }
            Cleared?.Invoke();
            _logger?.Debug("Interceptor log cleared.");
        }

        public IReadOnlyList<InterceptedRequestItem> Snapshot()
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }

        public void RecordRequest(string tabId, RequestEntry entry)
        {
            if (_paused || _attachedTabId == null || tabId != _attachedTabId || _disposed)
                return;

            try
            {
                var item = BuildItemFromEntry(entry);
                item.CorrelationId = Guid.NewGuid();

                lock (_lock)
                {
                    if (_disposed) return;
                    item.Id = "r" + Interlocked.Increment(ref _idCounter);
                    _items.Add(item);
                    if (!_pendingByUrl.TryGetValue(entry.FullUrl, out var queue))
                    {
                        queue = new Queue<InterceptedRequestItem>();
                        _pendingByUrl[entry.FullUrl] = queue;
                    }
                    queue.Enqueue(item);
                    _incTotal++;
                    if (item.IsThirdParty) _incThirdParty++;
                    if (item.IsTracker) _incTracker++;
                    if (item.RiskLevel == "High" || item.RiskLevel == "Critical") _incHighRisk++;
                    _incSumRiskScore += item.RiskScore;
                    TrimToMaxHistory();
                    _requestsInCurrentWindow++;
                }

                _addQueue.Enqueue(item);
            }
            catch (Exception ex)
            {
                _logger?.Error("RecordRequest failed", ex);
            }
        }

        /// <summary>Record a replayed request (no URL queue; response must be recorded via RecordResponseByCorrelation).</summary>
        public void RecordReplayRequest(string tabId, RequestEntry entry, Guid correlationId, bool isModifiedReplay = false)
        {
            if (_attachedTabId == null || tabId != _attachedTabId || _disposed)
                return;
            try
            {
                var item = BuildItemFromEntry(entry);
                item.CorrelationId = correlationId;
                item.IsReplayedWithModification = isModifiedReplay;
                lock (_lock)
                {
                    if (_disposed) return;
                    item.Id = "replay-" + Interlocked.Increment(ref _idCounter);
                    _items.Add(item);
                    _byCorrelationId[correlationId] = item;
                    _incTotal++;
                    if (item.IsThirdParty) _incThirdParty++;
                    if (item.IsTracker) _incTracker++;
                    if (item.RiskLevel == "High" || item.RiskLevel == "Critical") _incHighRisk++;
                    _incSumRiskScore += item.RiskScore;
                    TrimToMaxHistory();
                    _requestsInCurrentWindow++;
                }
                _addQueue.Enqueue(item);
                _logger?.Debug($"Replay request recorded: {entry.FullUrl}");
            }
            catch (Exception ex)
            {
                _logger?.Error("RecordReplayRequest failed", ex);
            }
        }

        public void RecordResponse(string tabId, string requestUri, int statusCode,
            IReadOnlyDictionary<string, string> responseHeaders,
            string contentType, long responseSize,
            Guid? correlationId = null)
        {
            if (_attachedTabId == null || tabId != _attachedTabId || _disposed)
                return;

            var safeHeaders = responseHeaders ?? new Dictionary<string, string>();
            try
            {
                InterceptedRequestItem? toUpdate = null;
                DateTime requestTime = default;

                if (correlationId.HasValue)
                {
                    lock (_lock)
                    {
                        if (_byCorrelationId.TryGetValue(correlationId.Value, out var replayed))
                        {
                            toUpdate = replayed;
                            requestTime = replayed.Timestamp;
                            _byCorrelationId.Remove(correlationId.Value);
                        }
                    }
                }

                if (toUpdate == null)
                {
                    lock (_lock)
                    {
                        if (!_pendingByUrl.TryGetValue(requestUri, out var queue) || queue.Count == 0)
                        {
                            _logger?.Debug($"No pending request for response: {requestUri}");
                            return;
                        }

                        while (queue.Count > 0)
                        {
                            var candidate = queue.Dequeue();
                            if (candidate.IsTrimmed)
                                continue;
                            requestTime = candidate.Timestamp;
                            toUpdate = candidate;
                            break;
                        }

                        if (queue != null && queue.Count == 0)
                            _pendingByUrl.Remove(requestUri);
                    }
                }

                if (toUpdate == null) return;

                toUpdate.StatusCode = statusCode;
                toUpdate.ContentType = contentType ?? "";
                toUpdate.ResponseSize = responseSize;
                toUpdate.ResponseHeaders = new Dictionary<string, string>(safeHeaders);
                if (requestTime != default)
                    toUpdate.DurationMs = (DateTime.Now - requestTime).TotalMilliseconds;
                toUpdate.ResponsePreview = BuildSafePreview(contentType ?? "", responseSize);
                toUpdate.CookiesRaw = BuildCookiesDisplay(safeHeaders);

                var entry = new RequestEntry
                {
                    FullUrl = toUpdate.FullUrl,
                    IsThirdParty = toUpdate.IsThirdParty,
                    TrackerLabel = toUpdate.IsTracker ? "tracker" : "",
                    ThreatConfidence = toUpdate.IsTracker ? 0.5 : 0,
                    RequestHeaders = toUpdate.RequestHeaders,
                    ResponseHeaders = toUpdate.ResponseHeaders
                };
                lock (_lock)
                {
                    _incTransferredBytes += responseSize;
                    var wasHighRisk = toUpdate.RiskLevel == "High" || toUpdate.RiskLevel == "Critical";
                    _incSumRiskScore -= toUpdate.RiskScore;
                    var runtimeSignals = _runtimeFingerprintProvider?.GetActiveSignals();
                    RiskScoring.Apply(toUpdate, entry, safeHeaders, runtimeSignals, _patternProvider);
                    _incSumRiskScore += toUpdate.RiskScore;
                    var isHighRisk = toUpdate.RiskLevel == "High" || toUpdate.RiskLevel == "Critical";
                    if (!wasHighRisk && isHighRisk) _incHighRisk++;
                }
                toUpdate.PrivacyAnalysis = toUpdate.RiskExplanation;

                _updateQueue.Enqueue(toUpdate);
            }
            catch (Exception ex)
            {
                _logger?.Error("RecordResponse failed", ex);
            }
        }

        private void FlushBatch(object? state)
        {
            if (_disposed) return;

            var adds = new List<InterceptedRequestItem>();
            while (_addQueue.TryDequeue(out var a))
                adds.Add(a);

            var updates = new List<InterceptedRequestItem>();
            while (_updateQueue.TryDequeue(out var u))
                updates.Add(u);

            if (adds.Count > 0)
            {
                try { BatchRequestAdded?.Invoke(adds); }
                catch (Exception ex) { _logger?.Error("BatchRequestAdded subscriber threw", ex); }
            }
            if (updates.Count > 0)
            {
                try { BatchRequestUpdated?.Invoke(updates); }
                catch (Exception ex) { _logger?.Error("BatchRequestUpdated subscriber threw", ex); }
            }

            TrafficMetrics? metrics = null;
            lock (_lock)
            {
                if (_items.Count > 0)
                {
                    bool useIncremental = _items.Count >= IncrementalMetricsThreshold;
                    metrics = new TrafficMetrics
                    {
                        TotalRequests = useIncremental ? (int)_incTotal : _items.Count,
                        ThirdPartyCount = useIncremental ? (int)_incThirdParty : _items.Count(i => i.IsThirdParty),
                        TrackerCount = useIncremental ? (int)_incTracker : _items.Count(i => i.IsTracker),
                        HighRiskCount = useIncremental ? (int)_incHighRisk : _items.Count(i => i.RiskLevel == "High" || i.RiskLevel == "Critical"),
                        AverageRiskScore = useIncremental && _incTotal > 0 ? _incSumRiskScore / _incTotal : _items.Average(i => i.RiskScore),
                        TotalTransferredBytes = useIncremental ? _incTransferredBytes : _items.Sum(i => i.ResponseSize)
                    };
                    var elapsed = (DateTime.UtcNow - _rpsWindowStart).TotalSeconds;
                    if (elapsed >= 1.0)
                    {
                        metrics.RequestsPerSecond = _requestsInCurrentWindow / elapsed;
                        _requestsInCurrentWindow = 0;
                        _rpsWindowStart = DateTime.UtcNow;
                    }
                    else
                    {
                        metrics.RequestsPerSecond = _requestsInCurrentWindow / Math.Max(0.1, elapsed);
                    }
                }
            }
            if (metrics != null)
            {
                try { MetricsUpdated?.Invoke(metrics); }
                catch (Exception ex) { _logger?.Error("MetricsUpdated subscriber threw", ex); }
            }
        }

        private InterceptedRequestItem BuildItemFromEntry(RequestEntry entry)
        {
            var item = new InterceptedRequestItem
            {
                Timestamp = entry.Time,
                Method = entry.Method ?? "",
                FullUrl = entry.FullUrl ?? "",
                Domain = entry.Host ?? "",
                Path = entry.Path ?? "",
                ResourceType = MapResourceType(entry.ResourceContext ?? ""),
                StatusCode = 0,
                ContentType = entry.ContentType ?? "",
                RequestHeaders = new Dictionary<string, string>(entry.RequestHeaders ?? new Dictionary<string, string>()),
                ResponseHeaders = new Dictionary<string, string>(entry.ResponseHeaders ?? new Dictionary<string, string>()),
                IsThirdParty = entry.IsThirdParty,
                IsTracker = !string.IsNullOrEmpty(entry.TrackerLabel) || (entry.ThreatConfidence > 0.2),
            };
            var runtimeSignals = _runtimeFingerprintProvider?.GetActiveSignals();
            RiskScoring.Apply(item, entry, null, runtimeSignals, _patternProvider);
            return item;
        }

        private static string MapResourceType(string context)
        {
            if (string.IsNullOrEmpty(context)) return "Other";
            var c = context.ToLowerInvariant();
            if (c.Contains("script")) return "JS";
            if (c.Contains("stylesheet")) return "CSS";
            if (c.Contains("image") || c.Contains("media")) return "Image";
            if (c.Contains("document")) return "Document";
            if (c.Contains("xhr") || c.Contains("fetch")) return "XHR";
            if (c.Contains("font")) return "Font";
            return context.Length > 12 ? context.Substring(0, 12) : context;
        }

        private void TrimToMaxHistory()
        {
            while (_items.Count > MaxHistory)
            {
                var first = _items[0];
                first.IsTrimmed = true;
                _byCorrelationId.Remove(first.CorrelationId);
                _incTotal--;
                if (first.IsThirdParty) _incThirdParty--;
                if (first.IsTracker) _incTracker--;
                if (first.RiskLevel == "High" || first.RiskLevel == "Critical") _incHighRisk--;
                _incTransferredBytes -= first.ResponseSize;
                _incSumRiskScore -= first.RiskScore;
                _items.RemoveAt(0);
            }
        }

        private static string BuildSafePreview(string contentType, long size)
        {
            if (size <= 0) return "";
            var ct = contentType.ToLowerInvariant();
            if (ct.Contains("json") || ct.Contains("text/") || ct.Contains("xml"))
                return $"[Text/JSON response, {size} bytes — body not captured]";
            if (ct.Contains("image") || ct.Contains("font") || ct.Contains("binary"))
                return $"[Binary, {size} bytes]";
            return $"[{size} bytes]";
        }

        private static string BuildCookiesDisplay(IReadOnlyDictionary<string, string>? headers)
        {
            if (headers == null) return "";
            if (headers.TryGetValue("set-cookie", out var setCookie) && !string.IsNullOrEmpty(setCookie))
                return setCookie;
            if (headers.TryGetValue("cookie", out var c) && !string.IsNullOrEmpty(c))
                return c;
            return "";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _batchTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _batchTimer?.Dispose();
            lock (_lock)
            {
                _items.Clear();
                _pendingByUrl.Clear();
                _byCorrelationId.Clear();
            }
            while (_addQueue.TryDequeue(out _)) { }
            while (_updateQueue.TryDequeue(out _)) { }
            _logger?.Debug("NetworkInterceptorService disposed.");
        }

        /// <summary>Match response to a replayed request by CorrelationId. Uses same pipeline as RecordResponse.</summary>
        public void RecordResponseByCorrelation(string tabId, Guid correlationId, string requestUri, int statusCode,
            IReadOnlyDictionary<string, string> responseHeaders,
            string contentType, long responseSize)
        {
            RecordResponse(tabId, requestUri, statusCode, responseHeaders, contentType, responseSize, correlationId);
        }

        /// <summary>Mark a replayed request (already in grid) as failed; no partial updates.</summary>
        public void SetReplayFailed(Guid correlationId, string errorMessage)
        {
            if (_attachedTabId == null || _disposed) return;
            try
            {
                InterceptedRequestItem? item = null;
                lock (_lock)
                {
                    if (_byCorrelationId.TryGetValue(correlationId, out var replayed))
                    {
                        item = replayed;
                        _byCorrelationId.Remove(correlationId);
                    }
                }
                if (item != null)
                {
                    var msg = string.IsNullOrWhiteSpace(errorMessage) ? "Replay failed." : "Replay failed: " + errorMessage.Trim();
                    item.ReplayStatusMessage = msg;
                    _updateQueue.Enqueue(item);
                    _logger?.Debug($"Replay failed marked: {item.FullUrl} - {msg}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("SetReplayFailed failed", ex);
            }
        }

        void INetworkInterceptorSink.RecordResponse(string tabId, string requestUri, int statusCode,
            IReadOnlyDictionary<string, string> responseHeaders,
            string contentType, long responseSize)
        {
            RecordResponse(tabId, requestUri, statusCode, responseHeaders, contentType, responseSize, null);
        }
    }
}
