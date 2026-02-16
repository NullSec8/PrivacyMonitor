using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Editable header row for replay; OriginalValue enables visual diff.</summary>
    public sealed class KeyValueItem : INotifyPropertyChanged
    {
        private string _key = "";
        private string _value = "";
        private string _originalValue = "";

        public string Key { get => _key; set { _key = value ?? ""; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsModified)); } }
        /// <summary>Original value for diff; set once when loading. When Value != OriginalValue, row is highlighted.</summary>
        public string OriginalValue { get => _originalValue; set { _originalValue = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsModified)); } }
        public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
