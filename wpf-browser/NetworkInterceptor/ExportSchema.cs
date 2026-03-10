using System;
using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Represents the root export object for session or single-request export.
    /// Includes schema versioning for compatibility and metadata for export.
    /// </summary>
    public sealed class InterceptorExportRoot
    {
        /// <summary>The schema version of the export structure.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Export schema version.</summary>
        public int Version { get; init; } = SchemaVersion;
        /// <summary>Unique identifier for this export operation.</summary>
        public string ExportId { get; init; } = Guid.NewGuid().ToString("N");
        /// <summary>UTC timestamp of the export.</summary>
        public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;
        /// <summary>Optional: Tab/session identifier if applicable.</summary>
        public string? SessionTabId { get; init; }
        /// <summary>True if a full session was exported, otherwise partial/export of single requests.</summary>
        public bool FullSession { get; init; }
        /// <summary>The exported requests (can be empty).</summary>
        public IReadOnlyList<InterceptorExportRequest> Requests { get; init; } = Array.Empty<InterceptorExportRequest>();
        /// <summary>Optional: Aggregate session metrics, if a full session is exported.</summary>
        public InterceptorExportMetrics? SessionMetrics { get; set; }
    }

    /// <summary>
    /// Represents a single exported HTTP(S) request/response and associated analysis.
    /// </summary>
    public sealed class InterceptorExportRequest
    {
        /// <summary>Internal unique request ID, if available (empty if not).</summary>
        public string Id { get; init; } = string.Empty;
        /// <summary>Globally unique correlation ID for matching requests/responses and replaying.</summary>
        public Guid CorrelationId { get; init; }
        /// <summary>UTC timestamp of the network request.</summary>
        public DateTime Timestamp { get; init; }
        /// <summary>HTTP method (GET, POST, etc).</summary>
        public string Method { get; init; } = string.Empty;
        /// <summary>Full URL as seen on the wire, including scheme, host, path, and query.</summary>
        public string FullUrl { get; init; } = string.Empty;
        /// <summary>Domain portion of FullUrl (e.g., "example.com").</summary>
        public string Domain { get; init; } = string.Empty;
        /// <summary>Path and query, if available.</summary>
        public string Path { get; init; } = string.Empty;
        /// <summary>Inferred resource type (e.g., "script", "xhr", "image"), if detected.</summary>
        public string ResourceType { get; init; } = string.Empty;
        /// <summary>HTTP status code (e.g., 200, 404).</summary>
        public int StatusCode { get; init; }
        /// <summary>Response Content-Type header, if present.</summary>
        public string ContentType { get; init; } = string.Empty;
        /// <summary>Total bytes received in response body.</summary>
        public long ResponseSize { get; init; }
        /// <summary>Measured duration in milliseconds.</summary>
        public double DurationMs { get; init; }
        /// <summary>True if request considered third-party for the session's origin.</summary>
        public bool IsThirdParty { get; init; }
        /// <summary>True if request detected as originating from a known tracker.</summary>
        public bool IsTracker { get; init; }
        /// <summary>Risk score (numeric), if assessed by engine.</summary>
        public int RiskScore { get; init; }
        /// <summary>Risk level string label (Low/Medium/High).</summary>
        public string RiskLevel { get; init; } = string.Empty;
        /// <summary>Category label or type determined for request.</summary>
        public string Category { get; init; } = string.Empty;
        /// <summary>Optional: Explanation for assigned risk score/level.</summary>
        public string RiskExplanation { get; init; } = string.Empty;
        /// <summary>Optional: Brief privacy-related analysis (free text or brief summary).</summary>
        public string PrivacyAnalysis { get; init; } = string.Empty;
        /// <summary>Snapshot of request headers as sent to server.</summary>
        public IReadOnlyDictionary<string, string> RequestHeaders { get; init; } = new Dictionary<string, string>();
        /// <summary>Snapshot of response headers as received from server.</summary>
        public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; } = new Dictionary<string, string>();
        /// <summary>
        /// Metadata for replay, if available. CorrelationId allows request matching.
        /// </summary>
        public ReplayMetadata? Replay { get; init; }
    }

    /// <summary>
    /// Metadata attached to an exported request relevant for replay/audit scenarios.
    /// </summary>
    public sealed class ReplayMetadata
    {
        /// <summary>CorrelationId matching the original request for replay purposes.</summary>
        public Guid CorrelationId { get; init; }
        /// <summary>True if request can be safely replayed (supported method, URL, etc).</summary>
        public bool CanReplay { get; init; }
        /// <summary>HTTP method to use in replay.</summary>
        public string Method { get; init; } = string.Empty;
        /// <summary>Full URL to use in replay.</summary>
        public string FullUrl { get; init; } = string.Empty;
        /// <summary>True if request was replayed with modified headers for privacy audit.</summary>
        public bool ModifiedReplay { get; init; }
    }

    /// <summary>
    /// Aggregated session metrics calculated at export time. Fields may be zero if a single-request export.
    /// </summary>
    public sealed class InterceptorExportMetrics
    {
        /// <summary>Total requests in session/export.</summary>
        public int TotalRequests { get; init; }
        /// <summary>Number of third-party requests.</summary>
        public int ThirdPartyCount { get; init; }
        /// <summary>Number of tracker requests detected.</summary>
        public int TrackerCount { get; init; }
        /// <summary>Number of requests rated as high risk.</summary>
        public int HighRiskCount { get; init; }
        /// <summary>Average risk score (over all requests).</summary>
        public double AverageRiskScore { get; init; }
        /// <summary>Total bytes transferred to client (across all requests).</summary>
        public long TotalTransferredBytes { get; init; }
        /// <summary>Requests per second during whole session (if known).</summary>
        public double RequestsPerSecond { get; init; }
    }
}
