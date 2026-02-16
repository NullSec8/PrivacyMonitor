using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Live traffic statistics for the metrics panel. Immutable snapshot.</summary>
    public sealed class TrafficMetrics : INotifyPropertyChanged
    {
        private int _totalRequests;
        private int _thirdPartyCount;
        private int _trackerCount;
        private int _highRiskCount;
        private double _averageRiskScore;
        private long _totalTransferredBytes;
        private double _requestsPerSecond;
        private double _rpsMovingAverage;

        public int TotalRequests { get => _totalRequests; set { _totalRequests = value; OnPropertyChanged(); } }
        public int ThirdPartyCount { get => _thirdPartyCount; set { _thirdPartyCount = value; OnPropertyChanged(); } }
        public int TrackerCount { get => _trackerCount; set { _trackerCount = value; OnPropertyChanged(); } }
        public int HighRiskCount { get => _highRiskCount; set { _highRiskCount = value; OnPropertyChanged(); } }
        public double AverageRiskScore { get => _averageRiskScore; set { _averageRiskScore = value; OnPropertyChanged(); } }
        public long TotalTransferredBytes { get => _totalTransferredBytes; set { _totalTransferredBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalTransferredFormatted)); } }
        public double RequestsPerSecond { get => _requestsPerSecond; set { _requestsPerSecond = value; OnPropertyChanged(); } }
        /// <summary>Moving average of requests/sec over recent batches.</summary>
        public double RpsMovingAverage { get => _rpsMovingAverage; set { _rpsMovingAverage = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string TotalTransferredFormatted =>
            TotalTransferredBytes < 1024 ? $"{TotalTransferredBytes} B"
            : TotalTransferredBytes < 1024 * 1024 ? $"{TotalTransferredBytes / 1024.0:F1} KB"
            : $"{TotalTransferredBytes / (1024.0 * 1024.0):F2} MB";
    }
}
