using System.IO;
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
                    string outPath = Path.Combine(repoRoot, "chrome-extension", "tracker-domains.js");
                    string[] domains = ProtectionEngine.GetBlocklistDomainsForExport();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("/**");
                    sb.AppendLine(" * Generated from Privacy Monitor browser engines (ProtectionEngine + PrivacyEngine).");
                    sb.AppendLine(" * Run: PrivacyMonitor.exe --export-blocklist");
                    sb.AppendLine(" */");
                    sb.AppendLine("const BLOCK_KNOWN_DOMAINS = [");
                    foreach (var d in domains)
                        sb.AppendLine("  \"" + d.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",");
                    sb.AppendLine("];");
                    sb.AppendLine("const AGGRESSIVE_EXTRA_DOMAINS = [];");
                    sb.AppendLine("function getDomainsForMode(mode) {");
                    sb.AppendLine("  if (mode === 'off') return [];");
                    sb.AppendLine("  return BLOCK_KNOWN_DOMAINS;");
                    sb.AppendLine("}");
                    sb.AppendLine("if (typeof self !== \"undefined\") {");
                    sb.AppendLine("  self.BLOCK_KNOWN_DOMAINS = BLOCK_KNOWN_DOMAINS;");
                    sb.AppendLine("  self.AGGRESSIVE_EXTRA_DOMAINS = AGGRESSIVE_EXTRA_DOMAINS;");
                    sb.AppendLine("  self.getDomainsForMode = getDomainsForMode;");
                    sb.AppendLine("}");
                    string? dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
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
            string? dir = AppContext.BaseDirectory;
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

