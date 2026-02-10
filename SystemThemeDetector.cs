using System;
using Microsoft.Win32;

namespace PrivacyMonitor
{
    /// <summary>
    /// Detects the Windows system app color scheme (light or dark) and raises an event when it changes.
    /// Uses HKEY_CURRENT_USER\...\Themes\Personalize "AppsUseLightTheme" (1 = light, 0 = dark).
    /// </summary>
    public static class SystemThemeDetector
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ValueName = "AppsUseLightTheme";

        /// <summary>True when the system is set to dark mode (apps use dark theme).</summary>
        public static bool IsDarkMode => !GetAppsUseLightTheme();

        /// <summary>Raised when the user changes the system light/dark setting.</summary>
        public static event EventHandler? ThemeChanged;

        private static bool GetAppsUseLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
                var value = key?.GetValue(ValueName);
                if (value is int i)
                    return i != 0;
            }
            catch { }
            return true; // default to light if we can't read
        }

        private static RegistryKeyWatcher? _watcher;

        /// <summary>Start watching the registry for theme changes. Call once from App startup.</summary>
        public static void WatchTheme()
        {
            try
            {
                _watcher = new RegistryKeyWatcher(Registry.CurrentUser, KeyPath, ValueName);
                _watcher.Changed += (_, _) => ThemeChanged?.Invoke(null, EventArgs.Empty);
                _watcher.Start();
            }
            catch { }
        }

        /// <summary>Simple registry value watcher using a timer (registry events are not available in .NET).</summary>
        private sealed class RegistryKeyWatcher
        {
            private readonly RegistryKey _root;
            private readonly string _subKeyPath;
            private readonly string _valueName;
            private readonly System.Windows.Threading.DispatcherTimer _timer;
            private object? _lastValue;

            public event EventHandler? Changed;

            public RegistryKeyWatcher(RegistryKey root, string subKeyPath, string valueName)
            {
                _root = root;
                _subKeyPath = subKeyPath;
                _valueName = valueName;
                _lastValue = ReadValue();
                _timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _timer.Tick += Timer_Tick;
            }

            public void Start() => _timer.Start();

            private object? ReadValue()
            {
                try
                {
                    using var key = _root.OpenSubKey(_subKeyPath, writable: false);
                    return key?.GetValue(_valueName);
                }
                catch { return null; }
            }

            private void Timer_Tick(object? sender, EventArgs e)
            {
                var current = ReadValue();
                if (current == null && _lastValue == null) return;
                if (current?.Equals(_lastValue) == true) return;
                _lastValue = current;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
