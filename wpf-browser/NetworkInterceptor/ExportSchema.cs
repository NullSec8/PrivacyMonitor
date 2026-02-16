using System;
using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Structured export schema for session and single-request export. Versioned for compatibility.</summary>
    public sealed class InterceptorExportRoot
    {
        public const int SchemaVersion = 1;

        public int Version { get; set; } = SchemaVersion;
        public string ExportId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public string? SessionTabId { get; set; }
        public bool FullSession { get; set; }
        public IReadOnlyList<InterceptorExportRequest> Requests { get; set; } = Array.Empty<InterceptorExportRequest>();
        public InterceptorExportMetrics? SessionMetrics { get; set; }
    }

    public sealed class InterceptorExportRequest
    {
        public string Id { get; set; } = "";
        public Guid CorrelationId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Method { get; set; } = "";
        public string FullUrl { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "";
        public string ResourceType { get; set; } = "";
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = "";
        public long ResponseSize { get; set; }
        public double DurationMs { get; set; }
        public bool IsThirdParty { get; set; }
        public bool IsTracker { get; set; }
        public int RiskScore { get; set; }
        public string RiskLevel { get; set; } = "";
        public string Category { get; set; } = "";
        public string RiskExplanation { get; set; } = "";
        public string PrivacyAnalysis { get; set; } = "";
        public Dictionary<string, string> RequestHeaders { get; set; } = new();
        public Dictionary<string, string> ResponseHeaders { get; set; } = new();
        /// <summary>Replay metadata: CorrelationId allows re-matching; CanReplay indicates method/URL support.</summary>
        public ReplayMetadata? Replay { get; set; }
    }

    public sealed class ReplayMetadata
    {
        public Guid CorrelationId { get; set; }
        public bool CanReplay { get; set; }
        public string Method { get; set; } = "";
        public string FullUrl { get; set; } = "";
        /// <summary>True when this request was replayed with modified headers (audit).</summary>
        public bool ModifiedReplay { get; set; }
    }

    public sealed class InterceptorExportMetrics
    {
        public int TotalRequests { get; set; }
        public int ThirdPartyCount { get; set; }
        public int TrackerCount { get; set; }
        public int HighRiskCount { get; set; }
        public double AverageRiskScore { get; set; }
        public long TotalTransferredBytes { get; set; }
        public double RequestsPerSecond { get; set; }
    }
}
