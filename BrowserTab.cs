using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace PrivacyMonitor
{
    public class BrowserTab
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public WebView2 WebView { get; set; } = null!;
        public Border TabHeader { get; set; } = null!;
        public TextBlock TitleBlock { get; set; } = null!;
        public TextBlock InitialBlock { get; set; } = null!;
        public Border BlockedBadge { get; set; } = null!;
        public TextBlock BlockedBadgeText { get; set; } = null!;

        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "";
        public string CurrentHost { get; set; } = "";
        public DateTime ScanStart { get; set; } = DateTime.Now;
        public int NextRequestId;
        public bool IsReady { get; set; }
        public bool IsLoading { get; set; }
        public bool IsSecure { get; set; }
        public bool ConsentDetected { get; set; }

        // ── Thread-safe request ingestion ──
        public ConcurrentQueue<RequestEntry> PendingRequests { get; } = new();
        public const int MaxRequests = 5000;
        public const int MaxBlockedRequests = 2000;

        // ── Processed data (UI-thread only) ──
        public List<RequestEntry> Requests { get; } = new();
        public List<FingerprintFinding> Fingerprints { get; } = new();
        public List<CookieItem> Cookies { get; } = new();
        public List<StorageItem> Storage { get; } = new();
        public List<WebRtcLeak> WebRtcLeaks { get; } = new();
        public List<SecurityHeaderResult> SecurityHeaders { get; set; } = new();

        // ── Detection context (for cross-request correlation) ──
        public HashSet<string> SeenTrackerDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenTrackerCompanies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> DomainRequestCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

        // ── Forensic state ──
        public Dictionary<string, HashSet<string>> IdentifierToDomains { get; } = new();
        public List<DataFlowEdge> DataFlowEdges { get; } = new();
        public List<ForensicEvent> ForensicTimeline { get; } = new();

        // ── Protection state ──
        public int BlockedCount { get; set; }
        public List<BlockedRequestInfo> BlockedRequests { get; } = new();
        public bool AntiFingerprintInjected { get; set; }
        public string? BlockerSeedScriptId { get; set; }
        public string? AntiFpScriptId { get; set; }
        public string? FingerprintDetectScriptId { get; set; }
        public string? BehavioralMonitorScriptId { get; set; }

        /// <summary>Drain pending queue into Requests list. Call from UI thread.</summary>
        public int DrainPending()
        {
            int drained = 0;
            while (PendingRequests.TryDequeue(out var entry))
            {
                Requests.Add(entry);
                drained++;

                // Track domain counts
                DomainRequestCounts[entry.Host] = DomainRequestCounts.GetValueOrDefault(entry.Host) + 1;

                // Track seen trackers
                if (!string.IsNullOrEmpty(entry.TrackerLabel))
                {
                    SeenTrackerDomains.Add(entry.Host);
                    if (!string.IsNullOrEmpty(entry.TrackerCompany))
                        SeenTrackerCompanies.Add(entry.TrackerCompany);
                }

                // Forensic correlation (runs on UI thread, lightweight per-request)
                ForensicEngine.ExtractAndRegisterIdentifiers(entry, IdentifierToDomains, ForensicTimeline);
                ForensicEngine.AnalyzeDataFlow(entry, CurrentHost, DataFlowEdges, ForensicTimeline);

                // Evict oldest if over limit
                if (Requests.Count > MaxRequests)
                    Requests.RemoveAt(0);
            }
            return drained;
        }

        /// <summary>Reset all detection state (on host change).</summary>
        public void ResetDetection()
        {
            Requests.Clear(); Fingerprints.Clear(); Cookies.Clear();
            Storage.Clear(); WebRtcLeaks.Clear(); SecurityHeaders = new();
            NextRequestId = 0; ScanStart = DateTime.Now; ConsentDetected = false;
            SeenTrackerDomains.Clear(); SeenTrackerCompanies.Clear();
            DomainRequestCounts.Clear(); SeenUrls.Clear();
            IdentifierToDomains.Clear(); DataFlowEdges.Clear(); ForensicTimeline.Clear();
            BlockedCount = 0; BlockedRequests.Clear(); AntiFingerprintInjected = false;
            // Drain any pending
            while (PendingRequests.TryDequeue(out _)) { }
        }
    }
}
