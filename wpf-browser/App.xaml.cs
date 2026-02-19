using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PrivacyMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ResourceDictionary? _themeDictionary;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportError("Privacy Monitor encountered an error.", e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ReportError("Privacy Monitor encountered a fatal error.", (Exception)e.ExceptionObject);
    }

    private static void ReportError(string title, Exception ex)
    {
        string message = ex?.ToString() ?? "Unknown error";
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivacyMonitor");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {title}\r\n{message}\r\n\r\n", System.Text.Encoding.UTF8);
        }
        catch { }
        MessageBox.Show($"{title}\r\n\r\n{ex?.Message}\r\n\r\nDetails written to %LocalAppData%\\PrivacyMonitor\\error.log", "Privacy Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            OnStartupCore(e);
        }
        catch (Exception ex)
        {
            ReportError("Privacy Monitor could not start.", ex);
            Shutdown(1);
        }
    }

    private void OnStartupCore(StartupEventArgs e)
    {

        // Export blocklist for Chrome extension (same engine as browser). Run: PrivacyMonitor.exe --export-blocklist
        if (e.Args.Contains("--export-blocklist", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string? repoRoot = FindRepoRoot();
                if (repoRoot != null)
                {
                    string extensionDir = Path.Combine(repoRoot, "chrome-extension");
                    string[] domains = ProtectionEngine.GetBlocklistDomainsForExport();
                    ChromeExtensionExport.WriteAll(extensionDir, domains);
                }
            }
            catch { }
            Shutdown(0);
            return;
        }

        // Handle --apply-update: replace current exe with new and restart (then exit).
        if (UpdateService.TryHandleApplyUpdate(out _))
        {
            Shutdown(0);
            return;
        }

        static string? FindRepoRoot()
        {
            // Prefer workspace root (has both chrome-extension and wpf-browser) so export writes to repo root chrome-extension.
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "chrome-extension")) &&
                    Directory.Exists(Path.Combine(dir, "wpf-browser")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            // Fallback: first ancestor that contains chrome-extension (e.g. when repo is just wpf-browser with nested chrome-extension).
            dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "chrome-extension")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        SystemThemeDetector.WatchTheme();
        ApplySystemTheme();
        SystemThemeDetector.ThemeChanged += (_, _) => ApplySystemTheme();

        // Create desktop shortcut for easy access (idempotent: only if missing or target changed).
        DesktopShortcut.EnsureDesktopShortcut();

        // Check for updates in the background (non-blocking).
        _ = CheckForUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        base.OnExit(e);
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
        bool useLarge = GetUseLargeAccessibilityUI();
        string suffix = useLarge ? ".Large" : "";
        string themeName = (isDark ? "Dark" : "Light") + suffix;
        var uri = new System.Uri(
            $"pack://application:,,,/PrivacyMonitor;component/Themes/{themeName}.xaml",
            System.UriKind.Absolute);

        if (_themeDictionary != null)
        {
            Resources.MergedDictionaries.Remove(_themeDictionary);
        }

        _themeDictionary = new ResourceDictionary { Source = uri };
        Resources.MergedDictionaries.Add(_themeDictionary);
    }

    private static bool GetUseLargeAccessibilityUI()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrivacyMonitor");
            string path = Path.Combine(dir, "settings.json");
            if (!File.Exists(path)) return false;
            string raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("useLargeAccessibilityUI", out var val))
                return val.ValueKind == JsonValueKind.True;
        }
        catch { }
        return false;
    }

    /// <summary>Re-apply theme (e.g. after user changes "Use larger text and controls" in Settings). Call from MainWindow after SaveSettings.</summary>
    public static void ReapplyTheme()
    {
        if (Current is App app)
            app.ApplySystemTheme();
    }
}

