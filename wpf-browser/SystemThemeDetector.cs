using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace PrivacyMonitor
{
    /// <summary>
    /// Detects the Windows system app color scheme (light or dark) and raises an event when it changes.
    /// Uses HKEY_CURRENT_USER\...\Themes\Personalize "AppsUseLightTheme" (1 = light, 0 = dark).
    /// Production-ready and event-driven, with thread-safety and UI-thread marshalling.
    /// </summary>
    public static class SystemThemeDetector
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ValueName = "AppsUseLightTheme";

        private static readonly object _sync = new object();

        // Cached state for fast access and change detection
        private static volatile bool _isDarkMode = !GetAppsUseLightTheme();
        private static RegistryMonitor? _monitor;
        private static ISynchronizeInvoke? _uiInvoker;
        private static bool _started = false;

        /// <summary>
        /// Returns true when the system is set to dark mode (apps use dark theme).
        /// </summary>
        public static bool IsDarkMode
        {
            get { return _isDarkMode; }
        }

        /// <summary>
        /// Raised when the user changes the system light/dark setting.
        /// </summary>
        public static event EventHandler? ThemeChanged;

        /// <summary>
        /// Starts watching for Windows theme changes. Call this once at app startup.
        /// Pass main window or application for correct UI-thread marshalling.
        /// </summary>
        public static void Start(ISynchronizeInvoke? uiInvoker = null)
        {
            lock (_sync)
            {
                if (_started) return;
                _uiInvoker = uiInvoker;
                _isDarkMode = !GetAppsUseLightTheme();

                _monitor = new RegistryMonitor(RegistryHive.CurrentUser, KeyPath, ValueName);
                _monitor.RegChanged += OnRegistryThemeChanged;
                _monitor.Start();

                _started = true;
            }
        }

        /// <summary>
        /// Stops watching for theme changes. Safe to call multiple times.
        /// </summary>
        public static void Stop()
        {
            lock (_sync)
            {
                _monitor?.Stop();
                _monitor = null;
                _started = false;
            }
        }

        private static void OnRegistryThemeChanged(object? sender, EventArgs e)
        {
            try
            {
                bool newIsDarkMode = !GetAppsUseLightTheme();
                if (newIsDarkMode == _isDarkMode)
                {
                    return; // No real change, don't fire event.
                }

                _isDarkMode = newIsDarkMode;

                // Fire event, marshalling to UI thread if possible.
                if (ThemeChanged != null)
                {
                    if (_uiInvoker != null && _uiInvoker.InvokeRequired)
                    {
                        try
                        {
                            _uiInvoker.BeginInvoke(ThemeChanged, new object[] { null, EventArgs.Empty });
                        }
                        catch (Exception ex)
                        {
                            Log("Failed to marshal ThemeChanged event: " + ex);
                        }
                    }
                    else
                    {
                        try
                        {
                            ThemeChanged.Invoke(null, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            Log("ThemeChanged event handler exception: " + ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("SystemThemeDetector.OnRegistryThemeChanged error: " + ex);
            }
        }

        private static bool GetAppsUseLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
                var value = key?.GetValue(ValueName);
                if (value is int i)
                    return i != 0;
                if (value is byte b)
                    return b != 0;
            }
            catch (Exception ex)
            {
                Log("Error reading theme from registry: " + ex);
            }
            return true; // Default to light if we can't read
        }

        private static void Log(string message)
        {
            // Replace with real logging if necessary.
            System.Diagnostics.Debug.WriteLine("[SystemThemeDetector] " + message);
        }

        /// <summary>
        /// Watches a registry value for changes using RegNotifyChangeKeyValue (no polling, native events).
        /// </summary>
        private sealed class RegistryMonitor : IDisposable
        {
            private readonly string _subKey;
            private readonly RegistryHive _hive;
            private readonly string _valueName;
            private Thread? _thread;
            private volatile bool _running;
            private AutoResetEvent? _terminateEvent;
            private readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
            private volatile object? _lastValue;

            public event EventHandler? RegChanged;

            public RegistryMonitor(RegistryHive hive, string subKey, string valueName)
            {
                _hive = hive;
                _subKey = subKey;
                _valueName = valueName;
            }

            public void Start()
            {
                if (_thread != null && _running) return;
                _running = true;
                _terminateEvent = new AutoResetEvent(false);
                _lastValue = ReadValue();

                _thread = new Thread(WatchThreadProc)
                {
                    IsBackground = true,
                    Name = "RegistryMonitor: " + _subKey
                };
                _thread.Start();
            }

            public void Stop()
            {
                _running = false;
                try { _terminateEvent?.Set(); } catch { }
                try { _thread?.Join(500); } catch { }
                Dispose();
            }

            public void Dispose()
            {
                _running = false;
                try { _terminateEvent?.Set(); } catch { }
                _terminateEvent?.Dispose();
                _terminateEvent = null;
            }

            private void WatchThreadProc()
            {
                IntPtr keyHandle = IntPtr.Zero;
                try
                {
                    int res = RegOpenKeyEx(GetHiveHandle(_hive), _subKey, 0, 0x20019 /* KEY_READ | KEY_NOTIFY */, out keyHandle);
                    if (res != 0 || keyHandle == IntPtr.Zero)
                        throw new Win32Exception(res, "Failed to open registry key: " + _subKey);

                    while (_running)
                    {
                        using var waitEvent = new AutoResetEvent(false);
                        var waitHandles = (_terminateEvent != null)
                            ? new[] { waitEvent.SafeWaitHandle.DangerousGetHandle(), _terminateEvent.SafeWaitHandle.DangerousGetHandle() }
                            : new[] { waitEvent.SafeWaitHandle.DangerousGetHandle() };

                        // Set up registry change notification.
                        int notifyResult = RegNotifyChangeKeyValue(
                            keyHandle,
                            false,
                            RegChangeFilter,
                            waitEvent.SafeWaitHandle.DangerousGetHandle(),
                            true);

                        if (notifyResult != 0)
                            throw new Win32Exception(notifyResult, "RegNotifyChangeKeyValue failed");

                        int index = WaitHandle.WaitAny(new[] { waitEvent, _terminateEvent! });
                        if (!_running || index == 1)
                            break;

                        var currentValue = ReadValue();
                        if (!object.Equals(currentValue, _lastValue))
                        {
                            _lastValue = currentValue;
                            RaiseRegChanged();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("RegistryMonitor error: " + ex);
                }
                finally
                {
                    if (keyHandle != IntPtr.Zero)
                        RegCloseKey(keyHandle);
                }
            }

            private void RaiseRegChanged()
            {
                try
                {
                    RegChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log("Error in RegChanged event handler: " + ex);
                }
            }

            private object? ReadValue()
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(_hive, RegistryView.Default);
                    using var key = baseKey.OpenSubKey(_subKey, false);
                    return key?.GetValue(_valueName);
                }
                catch (Exception ex)
                {
                    Log("RegistryMonitor.ReadValue error: " + ex);
                    return null;
                }
            }

            // P/Invoke details

            private const int RegChangeFilter = 0x00000004; // REG_NOTIFY_CHANGE_LAST_SET

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern int RegOpenKeyEx(
                IntPtr hKey,
                string lpSubKey,
                int ulOptions,
                int samDesired,
                out IntPtr phkResult);

            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern int RegCloseKey(
                IntPtr hKey);

            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern int RegNotifyChangeKeyValue(
                IntPtr hKey,
                bool bWatchSubtree,
                int dwNotifyFilter,
                IntPtr hEvent,
                bool fAsynchronous);

            private IntPtr GetHiveHandle(RegistryHive hive)
            {
                // Only support HKCU for now, can be extended if needed
                return hive == RegistryHive.CurrentUser
                    ? new IntPtr(unchecked((int)0x80000001))
                    : throw new NotSupportedException("Only RegistryHive.CurrentUser supported.");
            }
        }
    }
}
