using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Represents an aggregate of a domain for the "top domains by high-risk" chart.
    /// Implements INotifyPropertyChanged for data binding and UI updates.
    /// </summary>
    public sealed class DomainRiskItem : INotifyPropertyChanged
    {
        private string _domain = string.Empty;
        private int _totalCount;
        private int _highRiskCount;

        /// <summary>
        /// The domain name.
        /// </summary>
        public string Domain
        {
            get => _domain;
            set => SetProperty(ref _domain, value ?? string.Empty);
        }

        /// <summary>
        /// Total number of entries for this domain.
        /// </summary>
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        /// <summary>
        /// Number of high-risk entries for this domain.
        /// </summary>
        public int HighRiskCount
        {
            get => _highRiskCount;
            set => SetProperty(ref _highRiskCount, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Sets the property and notifies listeners if it changed.
        /// </summary>
        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
                return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Raises the PropertyChanged event for the specified property.
        /// </summary>
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// Returns a string representation for easier debugging/logging.
        /// </summary>
        public override string ToString() =>
            $"{Domain}: Total={TotalCount}, HighRisk={HighRiskCount}";
    }
}
