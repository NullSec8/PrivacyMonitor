using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PrivacyMonitor
{
    // ═══════════════════════════════════════════
    //  PROTECTION MODES & BLOCKING
    // ═══════════════════════════════════════════

    public enum ProtectionMode
    {
        Monitor,        // Log everything, block nothing
        BlockKnown,     // Block confirmed trackers (confidence >= 50%)
        Aggressive      // Block known + heuristic + suspected trackers
    }

    public class SiteProfile
    {
        public ProtectionMode Mode { get; set; } = ProtectionMode.BlockKnown;
        public bool AntiFingerprint { get; set; } = true;
        public bool BlockBehavioral { get; set; } = true;
        public bool BlockAdsTrackers { get; set; } = true;
        public DateTime LastVisit { get; set; } = DateTime.UtcNow;
    }

    public class BlockDecision
    {
        public bool Blocked { get; set; }
        public string Reason { get; set; } = "";
        public string Category { get; set; } = "";
        public double Confidence { get; set; }
        public string TrackerLabel { get; set; } = "";
    }

    // ═══════════════════════════════════════════
    //  TRACKER CLASSIFICATION
    // ═══════════════════════════════════════════

    public enum TrackerCategory
    {
        Advertising,
        Analytics,
        Social,
        SessionReplay,
        Fingerprinting,
        DMP,            // Data Management Platform
        CMP,            // Consent Management Platform
        Attribution,
        Affiliate,
        AdVerification,
        CDN,            // Known-tracking CDN
        Other
    }

    public enum RiskType
    {
        Tracking,
        Fingerprinting,
        DataLeakage,
        Security,
        Network
    }

    public enum ThreatTier
    {
        SafeIsh,              // Minimal tracking, no aggressive techniques
        TypicalWebTracking,   // Standard analytics, some third-party cookies
        AggressiveTracking,   // Multiple trackers, fingerprinting, session replay
        SurveillanceGrade     // Identity stitching, DMPs, cross-device, extensive data flows
    }

    public class TrackerInfo
    {
        public string Domain { get; set; } = "";
        public string Label { get; set; } = "";
        public string Company { get; set; } = "";
        public TrackerCategory Category { get; set; }
        public int RiskWeight { get; set; } = 3; // 1-5
        public string[] DataTypes { get; set; } = Array.Empty<string>();
    }

    public class TrackerMatch
    {
        public TrackerInfo Info { get; set; } = null!;
        public string MatchType { get; set; } = "domain"; // "domain", "heuristic", "cname_suspect", "redirect_bounce"
        public double Confidence { get; set; } = 1.0;
    }

    // ═══════════════════════════════════════════
    //  DETECTION SIGNALS
    // ═══════════════════════════════════════════

    public class DetectionSignal
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string SignalType { get; set; } = "";
        // known_tracker, heuristic_tracker, cname_suspect, fingerprint,
        // cookie_sync, pixel_tracking, obfuscated_payload, high_entropy_param,
        // redirect_bounce, tracking_cookie, tracking_param, data_exfil, missing_header
        public string Source { get; set; } = "";
        public string Detail { get; set; } = "";
        public double Confidence { get; set; } = 1.0; // 0.0 - 1.0
        public RiskType Risk { get; set; } = RiskType.Tracking;
        public int Severity { get; set; } = 3; // 1-5
        public string Evidence { get; set; } = "";
        public string GdprArticle { get; set; } = "";
    }

    // ═══════════════════════════════════════════
    //  CORE DATA MODELS
    // ═══════════════════════════════════════════

    public class RequestEntry
    {
        public int Id { get; set; }
        public DateTime Time { get; set; }
        public string Method { get; set; } = "";
        public string Host { get; set; } = "";
        public string Path { get; set; } = "";
        public string FullUrl { get; set; } = "";
        public bool IsThirdParty { get; set; }
        public string ResourceContext { get; set; } = "";
        public string TrackerLabel { get; set; } = "";
        public string TrackerCompany { get; set; } = "";
        public string TrackerCategoryName { get; set; } = "";
        public bool HasBody { get; set; }
        public int StatusCode { get; set; }
        public long ResponseSize { get; set; }
        public string ContentType { get; set; } = "";
        public Dictionary<string, string> RequestHeaders { get; set; } = new();
        public Dictionary<string, string> ResponseHeaders { get; set; } = new();
        public List<string> TrackingParams { get; set; } = new();
        public List<string> DataClassifications { get; set; } = new();
        public List<DetectionSignal> Signals { get; set; } = new();
        public double ThreatConfidence { get; set; } // aggregate 0.0-1.0
        public bool IsBlocked { get; set; }
        public string BlockReason { get; set; } = "";
        public string BlockCategory { get; set; } = "";
        public double BlockConfidence { get; set; }
    }

    public class FingerprintFinding
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Severity { get; set; } = "High";
        public string GdprArticle { get; set; } = "Art. 5(1)(c)";
        public string ScriptSource { get; set; } = ""; // call-stack origin of the fingerprinting script
        public long Timestamp { get; set; }            // JS Date.now() for precision timing
    }

    public class CookieItem
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public bool IsThirdParty { get; set; }
        public string Classification { get; set; } = "";
    }

    public class StorageItem
    {
        public string Store { get; set; } = "";
        public string Key { get; set; } = "";
        public int Size { get; set; }
        public string Classification { get; set; } = "";
    }

    public class SecurityHeaderResult
    {
        public string Header { get; set; } = "";
        public string Status { get; set; } = "";
        public string Value { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string ExplanationSq { get; set; } = "";
        public int ScoreImpact { get; set; }
    }

    public class GdprFinding
    {
        public string Article { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleSq { get; set; } = "";
        public string Description { get; set; } = "";
        public string DescriptionSq { get; set; } = "";
        public string Severity { get; set; } = "";
        public int Count { get; set; }
    }

    public class WebRtcLeak
    {
        public DateTime Time { get; set; }
        public string IpAddress { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public class PrivacyScore
    {
        public int NumericScore { get; set; }
        public string Grade { get; set; } = "?";
        public SolidColorBrush GradeColor { get; set; } = Brushes.Gray;
        public string Summary { get; set; } = "";
        public string SummarySq { get; set; } = "";
        public Dictionary<string, int> Breakdown { get; set; } = new();
        public Dictionary<string, int> CategoryScores { get; set; } = new();
        public int TotalSignals { get; set; }
        public int HighConfidenceSignals { get; set; }
        public ThreatTier Tier { get; set; } = ThreatTier.SafeIsh;
        public string TierLabel { get; set; } = "Safe-ish";
    }

    public class ScanResult
    {
        public string TargetUrl { get; set; } = "";
        public DateTime ScanStart { get; set; }
        public DateTime ScanEnd { get; set; }
        public List<RequestEntry> Requests { get; set; } = new();
        public List<FingerprintFinding> Fingerprints { get; set; } = new();
        public List<CookieItem> Cookies { get; set; } = new();
        public List<StorageItem> Storage { get; set; } = new();
        public List<SecurityHeaderResult> SecurityHeaders { get; set; } = new();
        public List<GdprFinding> GdprFindings { get; set; } = new();
        public List<WebRtcLeak> WebRtcLeaks { get; set; } = new();
        public PrivacyScore Score { get; set; } = new();
        public List<DetectionSignal> AllSignals { get; set; } = new();
    }

    // ═══════════════════════════════════════════
    //  FORENSIC MODELS
    // ═══════════════════════════════════════════

    public class ForensicEvent
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Type { get; set; } = ""; // identity_stitch, identity_spread, redirect_bounce, cookie_sync, etc.
        public string Summary { get; set; } = "";
        public Dictionary<string, string> Properties { get; set; } = new();
        public int Severity { get; set; } = 3; // 1-5
    }

    public class IdentityLink
    {
        public string ParameterName { get; set; } = "";
        public string ValuePrefix { get; set; } = "";
        public List<string> Domains { get; set; } = new();
        public int DomainCount { get; set; }
        public double Confidence { get; set; }
        public string RiskLevel { get; set; } = "Medium";
    }

    public class DataFlowEdge
    {
        public string FromDomain { get; set; } = "";
        public string ToDomain { get; set; } = "";
        public string Mechanism { get; set; } = ""; // referer, redirect_bounce, cookie_propagation, post_data, get_request
        public string Detail { get; set; } = "";
        public DateTime FirstSeen { get; set; }
        public int Occurrences { get; set; } = 1;
    }

    public class BehavioralPattern
    {
        public string Name { get; set; } = "";
        public string Detail { get; set; } = "";
        public int TechniqueCount { get; set; }
        public int WindowMs { get; set; }
        public double Confidence { get; set; }
        public int Severity { get; set; }
        public List<string> Techniques { get; set; } = new();
    }

    public class CompanyCluster
    {
        public string Company { get; set; } = "";
        public List<string> Services { get; set; } = new();
        public List<string> Domains { get; set; } = new();
        public int TotalRequests { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> DataTypes { get; set; } = new();
        public bool HasPostData { get; set; }
        public double AvgConfidence { get; set; }
    }

    public class SessionRisk
    {
        public string OverallRisk { get; set; } = "Low";
        public string IdentityStitchingRisk { get; set; } = "Low";
        public string DataPropagationRisk { get; set; } = "Low";
        public string FingerprintingRisk { get; set; } = "Low";
        public string ConcentrationRisk { get; set; } = "Low";
        public string BehavioralTrackingRisk { get; set; } = "Low";
        public ThreatTier Tier { get; set; } = ThreatTier.SafeIsh;
        public string TierLabel { get; set; } = "Safe-ish";
        public string Summary { get; set; } = "";
        public int RequestBursts { get; set; }
        public int CookieSyncChains { get; set; }
        public int CrossTabLinks { get; set; }
    }

    /// <summary>
    /// Session-wide context shared across all tabs for cross-tab correlation.
    /// </summary>
    public class SessionContext
    {
        public Dictionary<string, HashSet<string>> GlobalIdentifiers { get; } = new();
        public HashSet<string> AllSeenTrackerDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllSeenCompanies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ForensicEvent> GlobalTimeline { get; } = new();
        public Dictionary<string, int> GlobalDomainCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class RequestBurst
    {
        public string Domain { get; set; } = "";
        public int Count { get; set; }
        public int WindowMs { get; set; }
        public double RequestsPerSecond { get; set; }
        public string Detail { get; set; } = "";
    }

    public class ScoreExplanation
    {
        public string Category { get; set; } = "";
        public int Penalty { get; set; }
        public string Justification { get; set; } = "";
        public string GdprRelevance { get; set; } = "";
    }

    // ═══════════════════════════════════════════
    //  FORENSIC UI VIEW MODELS
    // ═══════════════════════════════════════════

    public class IdentityLinkItem
    {
        public string ParameterName { get; set; } = "";
        public string RiskLevel { get; set; } = "";
        public string DomainsText { get; set; } = "";
    }

    public class CompanyClusterItem
    {
        public string Company { get; set; } = "";
        public string RequestsLabel { get; set; } = "";
        public string ServicesText { get; set; } = "";
        public string DataTypesText { get; set; } = "";
    }

    public class ForensicTimelineItem
    {
        public string TimeText { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public class ScoreExplanationItem
    {
        public string Category { get; set; } = "";
        public string PenaltyText { get; set; } = "";
        public string Justification { get; set; } = "";
        public string GdprRelevance { get; set; } = "";
    }

    // ═══════════════════════════════════════════
    //  UI VIEW MODELS
    // ═══════════════════════════════════════════

    public class RequestListItem
    {
        public string Host { get; set; } = "";
        public string Path { get; set; } = "";
        public string TypeLabel { get; set; } = "";
        public SolidColorBrush TypeColor { get; set; } = Brushes.Gray;
        public string Method { get; set; } = "";
        public string Status { get; set; } = "";
        public string ConfidenceLabel { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public RequestEntry Entry { get; set; } = new();
    }

    public class FingerprintListItem
    {
        public string Type { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Severity { get; set; } = "";
        public SolidColorBrush SeverityColor { get; set; } = Brushes.Gray;
        public string Time { get; set; } = "";
    }

    public class StorageListItem
    {
        public string Label { get; set; } = "";
        public string Name { get; set; } = "";
        public string Store { get; set; } = "";
        public string Classification { get; set; } = "";
        public SolidColorBrush ClassColor { get; set; } = Brushes.Gray;
        public string Size { get; set; } = "";
    }

    public class SecurityListItem
    {
        public string Header { get; set; } = "";
        public string StatusLabel { get; set; } = "";
        public SolidColorBrush StatusColor { get; set; } = Brushes.Gray;
        public string Value { get; set; } = "";
        public string Explanation { get; set; } = "";
    }

    public class GdprListItem
    {
        public string Article { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "";
        public SolidColorBrush SeverityColor { get; set; } = Brushes.Gray;
        public string Count { get; set; } = "";
    }

    public class LiveFeedItem
    {
        public string Time { get; set; } = "";
        public string Label { get; set; } = "";
        public SolidColorBrush LabelColor { get; set; } = Brushes.Gray;
        public string Host { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public class TrackerSummaryItem
    {
        public string Name { get; set; } = "";
        public string Count { get; set; } = "";
        public string SampleHost { get; set; } = "";
        public string ConfidenceLabel { get; set; } = "";
        public SolidColorBrush ConfidenceColor { get; set; } = Brushes.Gray;
    }

    public class FpSimpleItem
    {
        public string Description { get; set; } = "";
        public string ConfidenceLabel { get; set; } = "";
        public SolidColorBrush SeverityColor { get; set; } = Brushes.Gray;
        public SolidColorBrush ConfidenceColor { get; set; } = Brushes.Gray;
    }

    public class RecommendationItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "";
        public SolidColorBrush SeverityColor { get; set; } = Brushes.Gray;
    }

    public class MitigationTip
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Action { get; set; } = "";
        public string ConfidenceLabel { get; set; } = "";
        public SolidColorBrush CategoryColor { get; set; } = Brushes.Gray;
        public SolidColorBrush ConfidenceColor { get; set; } = Brushes.Gray;
    }

    public class BlockedRequestInfo
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Host { get; set; } = "";
        public string Url { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Category { get; set; } = "";
        public double Confidence { get; set; }
        public string TrackerLabel { get; set; } = "";
        public string ResourceType { get; set; } = "";
        public string Method { get; set; } = "";
    }

    // ═══════════════════════════════════════════
    //  UI VIEW MODELS: PROTECTION DISPLAY
    // ═══════════════════════════════════════════

    public class BlockedListItem
    {
        public string Host { get; set; } = "";
        public string Reason { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string ConfidenceLabel { get; set; } = "";
        public SolidColorBrush ReasonColor { get; set; } = Brushes.Gray;
    }
}
