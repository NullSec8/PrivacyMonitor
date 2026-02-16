using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Domain aggregate for "top domains by high-risk" chart.</summary>
    public sealed class DomainRiskItem : INotifyPropertyChanged
    {
        private string _domain = "";
        private int _totalCount;
        private int _highRiskCount;

        public string Domain { get => _domain; set { _domain = value ?? ""; OnPropertyChanged(); } }
        public int TotalCount { get => _totalCount; set { _totalCount = value; OnPropertyChanged(); } }
        public int HighRiskCount { get => _highRiskCount; set { _highRiskCount = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
