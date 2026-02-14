using System.Windows;

namespace PrivacyMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ResourceDictionary? _themeDictionary;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Handle --apply-update: replace current exe with new and restart (then exit).
        if (UpdateService.TryHandleApplyUpdate(out _))
        {
            Shutdown(0);
            return;
        }

        SystemThemeDetector.WatchTheme();
        ApplySystemTheme();
        SystemThemeDetector.ThemeChanged += (_, _) => ApplySystemTheme();

        // Create desktop shortcut for easy access (idempotent: only if missing or target changed).
        DesktopShortcut.EnsureDesktopShortcut();

        // Check for updates in the background (non-blocking).
        _ = CheckForUpdatesAsync();
    }

    private static async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var available = await UpdateService.IsUpdateAvailableAsync().ConfigureAwait(false);
            if (!available) return;
            var latest = await UpdateService.GetLatestAsync().ConfigureAwait(false);
            if (latest == null) return;
            Current.Dispatcher.Invoke(() =>
            {
                UpdateAvailableVersion = latest.Version;
                OnUpdateAvailable?.Invoke(null!, latest.Version);
            });
        }
        catch { }
    }

    /// <summary>Set when a newer version is available (e.g. for UI to show "Update available").</summary>
    public static string? UpdateAvailableVersion { get; private set; }

    /// <summary>Raised when a newer version is available. Subscribers can show UI or auto-download.</summary>
    public static event EventHandler<string>? OnUpdateAvailable;

    private void ApplySystemTheme()
    {
        bool isDark = SystemThemeDetector.IsDarkMode;
        var uri = new System.Uri(
            $"pack://application:,,,/PrivacyMonitor;component/Themes/{(isDark ? "Dark" : "Light")}.xaml",
            System.UriKind.Absolute);

        if (_themeDictionary != null)
        {
            Resources.MergedDictionaries.Remove(_themeDictionary);
        }

        _themeDictionary = new ResourceDictionary { Source = uri };
        Resources.MergedDictionaries.Add(_themeDictionary);
    }
}

