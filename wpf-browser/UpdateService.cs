using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrivacyMonitor;

/// <summary>
/// Handles update checks, downloads, and application for PrivacyMonitor,
/// supporting robust error handling, diagnostics, and future extensibility.
/// </summary>
public static class UpdateService
{
    private static string? _baseUrl;
    private const string DefaultUrl = "http://187.77.71.151:3000";
    private const string ConfigDirName = "PrivacyMonitor";
    private const string UpdateServerFilename = "update-server.txt";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    // Used only for downloading the update zip (large file, long timeout).
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15),
    };

    /// <summary>
    /// Gets or sets the update server base URL (no trailing slash).
    /// Reads from %LocalAppData%\PrivacyMonitor\update-server.txt if present, else uses default.
    /// </summary>
    public static string BaseUrl
    {
        get
        {
            if (_baseUrl != null) return _baseUrl;
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ConfigDirName);
                var path = Path.Combine(dir, UpdateServerFilename);
                if (File.Exists(path))
                {
                    var line = File.ReadLines(path).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(line) &&
                        (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        _baseUrl = line.TrimEnd('/');
                        return _baseUrl;
                    }
                }
            }
            catch
            {
                // Logging could go here.
            }
            _baseUrl = DefaultUrl;
            return _baseUrl;
        }
        set
        {
            _baseUrl = string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
        }
    }

    /// <summary>
    /// Gets the current app version (major.minor.build) or "1.0.0" as fallback.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version
                    ?? Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    /// <summary>
    /// Compares two dot-separated version strings.
    /// Returns: &lt; 0 if current &lt; latest, 0 if equal, &gt; 0 if current &gt; latest.
    /// </summary>
    public static int CompareVersions(string current, string latest)
    {
        static int ParsePart(string? s) => int.TryParse(s, out var n) ? n : 0;

        var cur = (current ?? "").Split('.');
        var lat = (latest ?? "").Split('.');
        var length = Math.Max(cur.Length, lat.Length);

        for (var i = 0; i < length; i++)
        {
            var cv = ParsePart(i < cur.Length ? cur[i] : null);
            var lv = ParsePart(i < lat.Length ? lat[i] : null);
            var cmp = cv.CompareTo(lv);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    /// <summary>
    /// Fetches latest version info from the server. Returns null on error.
    /// </summary>
    public static async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        var (info, _) = await GetLatestWithErrorAsync(ct).ConfigureAwait(false);
        return info;
    }

    /// <summary>
    /// Fetches latest version info and returns the underlying error message if it fails.
    /// </summary>
    public static async Task<(UpdateInfo? info, string? errorMessage)> GetLatestWithErrorAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/api/latest";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(version))
                return (null, "Server response missing version.");

            return (new UpdateInfo
            {
                Version = version.Trim(),
                ReleaseDate = root.TryGetProperty("releaseDate", out var d) ? d.GetString() : null,
            }, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.InnerException != null
                ? $"{ex.Message} ({ex.InnerException.Message})"
                : ex.Message);
        }
        catch (TaskCanceledException)
        {
            return (null, "Connection timed out. Your firewall may be blocking the app — run allow-update-server-firewall.ps1 as Administrator.");
        }
        catch (Exception ex)
        {
            return (null, ex.InnerException != null
                ? $"{ex.Message} — {ex.InnerException.Message}"
                : ex.Message);
        }
    }

    /// <summary>
    /// Checks if a newer version is available on the server.
    /// </summary>
    public static async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct).ConfigureAwait(false);
        if (latest == null) return false;
        return CompareVersions(CurrentVersion, latest.Version) < 0;
    }

    /// <summary>
    /// Validates that the version string is safe for use in file paths (no path traversal, harmless chars).
    /// </summary>
    private static bool IsSafeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64)
            return false;
        return version.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
    }

    /// <summary>
    /// Downloads the update zip and extracts it to a temp folder.
    /// Returns (extract folder path, or null) and optional error message.
    /// </summary>
    public static async Task<(string? extractDir, string? errorMessage)> DownloadUpdateAsync(
        string version,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsSafeVersion(version))
            return (null, "Invalid version format from server.");

        var zipFile = Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}");
        try
        {
            var url = $"{BaseUrl}/api/download/{version}?platform=win64";
            using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, $"Server returned {(int)response.StatusCode}. Try again later.");

            var total = response.Content.Headers.ContentLength ?? 0L;
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = File.Create(zipFile))
            {
                var buffer = new byte[81920];
                long bytesRead = 0;
                int count;
                while ((count = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    bytesRead += count;
                    if (total > 0)
                        progress?.Report((double)bytesRead / total);
                }
            }

            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipFile, extractDir);
            try { File.Delete(zipFile); } catch { /* ignore, not critical */ }

            var exeInDir = FindExecutable(extractDir);
            if (exeInDir == null || !File.Exists(exeInDir))
                return (null, "Update package is missing the executable.");

            return (extractDir, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Download timed out. Check your connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
        finally
        {
            // Could later offer temp file cleanup here if error, etc.
        }
    }

    /// <summary>
    /// Finds the .exe in a folder, preferring PrivacyMonitor.exe, else any .exe.
    /// </summary>
    private static string? FindExecutable(string directory)
    {
        var preferred = Path.Combine(directory, "PrivacyMonitor.exe");
        if (File.Exists(preferred))
            return preferred;

        // Fallback: first .exe file found
        var anyExe = Directory.GetFiles(directory, "*.exe").FirstOrDefault();
        return anyExe;
    }

    /// <summary>
    /// Starts the updater: launches the new exe with --apply-update so it replaces the current exe and restarts.
    /// Call this from the UI thread; then exit the app so the updater can replace the file.
    /// </summary>
    public static void ApplyUpdateAndRestart(string extractedUpdateFolder)
    {
        var exePath = FindExecutable(extractedUpdateFolder);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return;

        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMonitor.exe");
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            return;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--apply-update");
        psi.ArgumentList.Add(exePath);
        psi.ArgumentList.Add(currentExe);
        Process.Start(psi);
    }

    /// <summary>
    /// Handles --apply-update: copy new exe over current and restart.
    /// Returns true if the app should exit (update applied or failed); false to continue normal startup.
    /// Safe against arbitrary file overwrite.
    /// </summary>
    public static bool TryHandleApplyUpdate(out bool success)
    {
        success = false;
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 4 || !args[1].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
            return false;
        var newExe = args[2];
        var currentExeArg = args[3];
        if (!File.Exists(newExe)) return true;

        // Only allow overwriting the actual running process path (prevent arbitrary file overwrite).
        var realCurrentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMonitor.exe");
        if (string.IsNullOrEmpty(realCurrentExe)) return true;
        try
        {
            var realFull = Path.GetFullPath(realCurrentExe);
            var argFull = Path.GetFullPath(currentExeArg);
            if (!string.Equals(realFull, argFull, StringComparison.OrdinalIgnoreCase))
                return true; // Mismatch: ignore updating wrong file.
        }
        catch { return true; }

        try
        {
            Thread.Sleep(1500); // wait for main process to exit
            File.Copy(newExe, realCurrentExe, true);
            Process.Start(new ProcessStartInfo
            {
                FileName = realCurrentExe,
                UseShellExecute = true,
            });
            success = true;
        }
        catch
        {
            // Could optionally log or show error here.
            success = false;
        }
        return true;
    }

    /// <summary>
    /// Notifies the server that this client installed/updated (best-effort analytics).
    /// </summary>
    public static async Task LogInstallAsync(string version, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                version,
                platform = "win64",
                client = "PrivacyMonitor"
            });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            await HttpClient.PostAsync($"{BaseUrl}/api/install-log", content, ct).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail: analytics not critical.
        }
    }

    /// <summary>
    /// Sends anonymous usage data to improve the browser (version, OS, protection level), only if user allowed.
    /// </summary>
    public static async Task SendUsageAsync(bool allowUsageData, string protectionMode, CancellationToken ct = default)
    {
        if (!allowUsageData) return;
        try
        {
            var osDesc = GetOsDescription();
            var payload = JsonSerializer.Serialize(new
            {
                version = CurrentVersion,
                platform = "win64",
                client = "PrivacyMonitor",
                os = osDesc,
                protectionDefault = protectionMode,
            });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            await HttpClient.PostAsync($"{BaseUrl}/api/usage", content, ct).ConfigureAwait(false);
        }
        catch
        {
            // Silent fail: usage telemetry not critical.
        }
    }

    private static string GetOsDescription()
    {
        try
        {
            var os = Environment.OSVersion;
            return os.Version.Major switch
            {
                >= 10 => $"Windows {os.Version.Major}",
                _ => os.ToString(),
            };
        }
        catch
        {
            return "Unknown OS";
        }
    }
}

/// <summary>
/// Info about the latest available update as reported from the update server.
/// </summary>
public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string? ReleaseDate { get; set; }
}
