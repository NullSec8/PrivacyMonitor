using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Single sample for metrics chart (RPS, high-risk, 3rd-party, tracker).</summary>
    public sealed class MetricSample : INotifyPropertyChanged
    {
        private DateTime _time;
        private double _rps;
        private int _highRisk;
        private int _thirdParty;
        private int _tracker;
        private int _total;
        private double _rpsMovingAverage;

        public DateTime Time { get => _time; set { _time = value; OnPropertyChanged(); } }
        public double Rps { get => _rps; set { _rps = value; OnPropertyChanged(); } }
        /// <summary>Moving average RPS at this sample (for trendline).</summary>
        public double RpsMovingAverage { get => _rpsMovingAverage; set { _rpsMovingAverage = value; OnPropertyChanged(); } }
        public int HighRisk { get => _highRisk; set { _highRisk = value; OnPropertyChanged(); } }
        public int ThirdParty { get => _thirdParty; set { _thirdParty = value; OnPropertyChanged(); } }
        public int Tracker { get => _tracker; set { _tracker = value; OnPropertyChanged(); } }
        public int Total { get => _total; set { _total = value; OnPropertyChanged(); } }

        /// <summary>Normalized 0–1 for bar height (RPS scale).</summary>
        public double RpsNormalized => Math.Min(1.0, Rps / 100.0);
        /// <summary>Normalized 0–1 for trendline (same scale as RPS).</summary>
        public double RpsMovingAverageNormalized => Math.Min(1.0, RpsMovingAverage / 100.0);
        public double HighRiskNormalized => Total > 0 ? Math.Min(1.0, (double)HighRisk / Total) : 0;
        public double ThirdPartyNormalized => Total > 0 ? Math.Min(1.0, (double)ThirdParty / Total) : 0;
        public double TrackerNormalized => Total > 0 ? Math.Min(1.0, (double)Tracker / Total) : 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
