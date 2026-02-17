using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace PrivacyMonitor
{
    /// <summary>Per-tab state: UI refs, request/fingerprint data, and protection state. UI refs are set by MainWindow after creation.
    /// BlockedRequests and other lists are not thread-safe; RegisterBlocked and DrainPending must be called on the UI thread.
    /// Consider constructor injection for required UI refs when refactoring the creation flow.</summary>
    public class BrowserTab
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        /// <summary>Set by MainWindow after creation. Not null once tab is fully initialized.</summary>
        public WebView2 WebView { get; set; } = null!;
        /// <summary>Set by MainWindow after BuildTabHeader(tab).</summary>
        public Border TabHeader { get; set; } = null!;
        public TextBlock TitleBlock { get; set; } = null!;
        public TextBlock InitialBlock { get; set; } = null!;
        public Border BlockedBadge { get; set; } = null!;
        public TextBlock BlockedBadgeText { get; set; } = null!;
        public Border? HeavyBadge { get; set; }

        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "";
        public string CurrentHost { get; set; } = "";
        /// <summary>When the current page scan started (UTC). Use UTC for consistency with LastActivityUtc.</summary>
        public DateTime ScanStart { get; set; } = DateTime.UtcNow;
        /// <summary>Last time this tab had user or network activity (UTC). Used for idle sleep.</summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Last measured approximate memory usage for this tab (bytes, from JS APIs).</summary>
        public long? LastMemoryBytes { get; set; }
        /// <summary>Next ID for new requests. Incremented at ingestion (e.g. Interlocked.Increment in the request handler).</summary>
        public int NextRequestId;
        public bool IsReady { get; set; }
        public bool IsLoading { get; set; }
        public bool IsCrashed { get; set; }
        /// <summary>True when the underlying WebView2 has been suspended to save resources.</summary>
        public bool IsSleeping { get; set; }
        /// <summary>True when this tab is using far more resources than others (for UI hint).</summary>
        public bool IsHeavy { get; set; }
        public bool IsSecure { get; set; }
        public bool ConsentDetected { get; set; }

        // ── Thread-safe request ingestion ──
        public ConcurrentQueue<RequestEntry> PendingRequests { get; } = new();
        /// <summary>Max requests kept per tab. Consider making configurable via settings.</summary>
        public const int MaxRequests = 5000;
        /// <summary>Max blocked-request records kept. Consider making configurable.</summary>
        public const int MaxBlockedRequests = 2000;

        // ── Processed data (UI-thread only; not thread-safe) ──
        /// <summary>LinkedList for O(1) eviction when over MaxRequests; use Last/Previous to scan recent-first.</summary>
        public LinkedList<RequestEntry> Requests { get; } = new();
        public List<FingerprintFinding> Fingerprints { get; } = new();
        public List<CookieItem> Cookies { get; } = new();
        public List<StorageItem> Storage { get; } = new();
        public List<WebRtcLeak> WebRtcLeaks { get; } = new();
        public List<SecurityHeaderResult> SecurityHeaders { get; set; } = new();

        /// <summary>Raised after one or more requests are drained (UI thread). Optional for UI refresh. Subscribers doing heavy work may want to debounce.</summary>
        public event Action? RequestProcessed;
        /// <summary>Raised when a request is recorded as blocked. Must only be called from the UI thread (BlockedRequests is not thread-safe).</summary>
        public event Action<BlockedRequestInfo>? BlockedRequestAdded;

        // ── Detection context (for cross-request correlation) ──
        public HashSet<string> SeenTrackerDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenTrackerCompanies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> DomainRequestCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

        // ── Forensic state ──
        public Dictionary<string, HashSet<string>> IdentifierToDomains { get; } = new();
        public List<DataFlowEdge> DataFlowEdges { get; } = new();
        /// <summary>O(1) lookup for AddOrUpdateEdge; cleared with DataFlowEdges on ResetDetection.</summary>
        public Dictionary<string, DataFlowEdge> DataFlowEdgeLookup { get; } = new();
        public List<ForensicEvent> ForensicTimeline { get; } = new();

        // ── Protection state ──
        public int BlockedCount { get; set; }
        public List<BlockedRequestInfo> BlockedRequests { get; } = new();
        public bool AntiFingerprintInjected { get; set; }
        public string? BlockerSeedScriptId { get; set; }
        public string? AntiFpScriptId { get; set; }
        public string? FingerprintDetectScriptId { get; set; }
        public string? BehavioralMonitorScriptId { get; set; }

        /// <summary>Drain pending queue into Requests. Call from UI thread only. Eviction is O(1) via LinkedList.</summary>
        public int DrainPending()
        {
            int drained = 0;
            while (PendingRequests.TryDequeue(out var entry))
            {
                Requests.AddLast(entry);
                drained++;

                var host = entry.Host ?? "";
                DomainRequestCounts[host] = DomainRequestCounts.GetValueOrDefault(host) + 1;

                if (!string.IsNullOrEmpty(entry.TrackerLabel) && host.Length > 0)
                {
                    SeenTrackerDomains.Add(host);
                    if (!string.IsNullOrEmpty(entry.TrackerCompany))
                        SeenTrackerCompanies.Add(entry.TrackerCompany);
                }

                try
                {
                    ForensicEngine.ExtractAndRegisterIdentifiers(entry, IdentifierToDomains, ForensicTimeline);
                    ForensicEngine.AnalyzeDataFlow(entry, CurrentHost, DataFlowEdges, ForensicTimeline, DataFlowEdgeLookup);
                }
                catch
                {
                    // ForensicEngine failed; avoid breaking drain. Log in host app if needed.
                }

                if (Requests.Count > MaxRequests)
                    Requests.RemoveFirst();
            }
            if (drained > 0)
                RequestProcessed?.Invoke();
            return drained;
        }

        /// <summary>Reset all detection state (on host change). Preserves list references for bindings (e.g. SecurityHeaders.Clear()).</summary>
        public void ResetDetection()
        {
            Requests.Clear();
            Fingerprints.Clear();
            Cookies.Clear();
            Storage.Clear();
            WebRtcLeaks.Clear();
            SecurityHeaders.Clear();
            NextRequestId = 0;
            ScanStart = DateTime.UtcNow;
            ConsentDetected = false;
            SeenTrackerDomains.Clear();
            SeenTrackerCompanies.Clear();
            DomainRequestCounts.Clear();
            SeenUrls.Clear();
            IdentifierToDomains.Clear();
            DataFlowEdges.Clear();
            DataFlowEdgeLookup.Clear();
            ForensicTimeline.Clear();
            BlockedCount = 0;
            BlockedRequests.Clear();
            AntiFingerprintInjected = false;
            while (PendingRequests.TryDequeue(out _)) { }
        }

        /// <summary>Record user or network activity (updates LastActivityUtc for idle sleep).</summary>
        public void NoteActivity()
        {
            LastActivityUtc = DateTime.UtcNow;
        }

        /// <summary>Mark this tab as crashed (e.g. after ProcessFailed).</summary>
        public void MarkCrashed()
        {
            IsCrashed = true;
        }

        /// <summary>Mark this tab as sleeping (suspended) or awake.</summary>
        public void MarkSleeping(bool sleeping = true)
        {
            IsSleeping = sleeping;
        }

        /// <summary>Record a blocked request for the forensic trail and badge. Must be called on the UI thread (BlockedRequests is not thread-safe).</summary>
        public void RegisterBlocked(DateTime time, string host, string url, string reason, string category, double confidence, string trackerLabel, string resourceType, string method)
        {
            BlockedCount++;
            var info = new BlockedRequestInfo
            {
                Time = time,
                Host = host ?? "",
                Url = url ?? "",
                Reason = reason ?? "",
                Category = category ?? "",
                Confidence = confidence,
                TrackerLabel = trackerLabel ?? "",
                ResourceType = resourceType ?? "",
                Method = method ?? ""
            };
            BlockedRequests.Add(info);
            if (BlockedRequests.Count > MaxBlockedRequests)
                BlockedRequests.RemoveAt(0);
            BlockedRequestAdded?.Invoke(info);
        }
    }
}
