using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Single captured request/response for the interceptor UI. Built from RequestEntry + response data.
    /// </summary>
    public sealed class InterceptedRequestItem : INotifyPropertyChanged
    {
        /// <summary>Unique correlation ID for request/response matching (e.g. GUID).</summary>
        public Guid CorrelationId { get; set; }

        /// <summary>True when item was evicted from the rolling buffer; used to skip stale queue entries.</summary>
        internal bool IsTrimmed { get; set; }

        private string _id = "";
        private DateTime _timestamp;
        private string _method = "";
        private string _fullUrl = "";
        private string _domain = "";
        private string _path = "";
        private string _resourceType = "";
        private int _statusCode;
        private string _contentType = "";
        private long _responseSize;
        private double _durationMs;
        private bool _isThirdParty;
        private bool _isTracker;
        private int _riskScore;
        private string _riskLevel = "";
        private string _category = "";
        private Dictionary<string, string> _requestHeaders = new();
        private Dictionary<string, string> _responseHeaders = new();
        private string _responsePreview = "";
        private string _cookiesRaw = "";
        private string _privacyAnalysis = "";
        private string _riskExplanation = "";
        private string _replayStatusMessage = "";
        private bool _isReplayedWithModification;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }
        public string Method { get => _method; set { _method = value ?? ""; OnPropertyChanged(); } }
        public string FullUrl { get => _fullUrl; set { _fullUrl = value ?? ""; OnPropertyChanged(); } }
        public string Domain { get => _domain; set { _domain = value ?? ""; OnPropertyChanged(); } }
        public string Path { get => _path; set { _path = value ?? ""; OnPropertyChanged(); } }
        public string ResourceType { get => _resourceType; set { _resourceType = value ?? ""; OnPropertyChanged(); } }
        public int StatusCode { get => _statusCode; set { _statusCode = value; OnPropertyChanged(); } }
        public string ContentType { get => _contentType; set { _contentType = value ?? ""; OnPropertyChanged(); } }
        public long ResponseSize { get => _responseSize; set { _responseSize = value; OnPropertyChanged(); } }
        public double DurationMs { get => _durationMs; set { _durationMs = value; OnPropertyChanged(); } }
        public bool IsThirdParty { get => _isThirdParty; set { _isThirdParty = value; OnPropertyChanged(); } }
        public bool IsTracker { get => _isTracker; set { _isTracker = value; OnPropertyChanged(); } }
        public int RiskScore { get => _riskScore; set { _riskScore = value; OnPropertyChanged(); } }
        public string RiskLevel { get => _riskLevel; set { _riskLevel = value ?? ""; OnPropertyChanged(); } }
        public string Category { get => _category; set { _category = value ?? ""; OnPropertyChanged(); } }
        public Dictionary<string, string> RequestHeaders { get => _requestHeaders; set { _requestHeaders = value ?? new(); OnPropertyChanged(); } }
        public Dictionary<string, string> ResponseHeaders { get => _responseHeaders; set { _responseHeaders = value ?? new(); OnPropertyChanged(); } }
        public string ResponsePreview { get => _responsePreview; set { _responsePreview = value ?? ""; OnPropertyChanged(); } }
        public string CookiesRaw { get => _cookiesRaw; set { _cookiesRaw = value ?? ""; OnPropertyChanged(); } }
        public string PrivacyAnalysis { get => _privacyAnalysis; set { _privacyAnalysis = value ?? ""; OnPropertyChanged(); } }
        /// <summary>Human-readable risk explanation (multi-factor, weighted).</summary>
        public string RiskExplanation { get => _riskExplanation; set { _riskExplanation = value ?? ""; OnPropertyChanged(); } }
        /// <summary>When non-empty, replay failed; message shown in row tooltip.</summary>
        public string ReplayStatusMessage { get => _replayStatusMessage; set { _replayStatusMessage = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasReplayError)); } }
        /// <summary>True when ReplayStatusMessage is non-empty (for DataTrigger).</summary>
        public bool HasReplayError => !string.IsNullOrEmpty(_replayStatusMessage);
        /// <summary>True when this row was created by replay with modified headers.</summary>
        public bool IsReplayedWithModification { get => _isReplayedWithModification; set { _isReplayedWithModification = value; OnPropertyChanged(); } }
    }
}
