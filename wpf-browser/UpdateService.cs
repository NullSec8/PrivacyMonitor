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
/// Checks the update server for a newer version and applies updates by downloading
/// the zip and replacing the running exe with the new version.
/// </summary>
public static class UpdateService
{
    /// <summary>Update server base URL (no trailing slash).</summary>
    // Use port 3000 so the app reaches the Node server directly (no nginx required)
    public static string BaseUrl { get; set; } = "http://187.77.71.151:3000";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>Used only for downloading the update zip (large file, long timeout).</summary>
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15),
    };

    /// <summary>Current app version from assembly (e.g. 1.0.0).</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
        }
    }

    /// <summary>
    /// Compare two version strings (e.g. "1.0.0" vs "1.0.1").
    /// Returns: &lt; 0 if current &lt; latest, 0 if equal, &gt; 0 if current &gt; latest.
    /// </summary>
    public static int CompareVersions(string current, string latest)
    {
        static int ParsePart(string? s) => int.TryParse(s, out var n) ? n : 0;
        var c = (current ?? "").Split('.');
        var l = (latest ?? "").Split('.');
        for (var i = 0; i < Math.Max(c.Length, l.Length); i++)
        {
            var cv = ParsePart(i < c.Length ? c[i] : null);
            var lv = ParsePart(i < l.Length ? l[i] : null);
            if (cv != lv) return cv.CompareTo(lv);
        }
        return 0;
    }

    /// <summary>Fetches latest version info from the server. Returns null on error (e.g. 404, network).</summary>
    public static async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        var (info, _) = await GetLatestWithErrorAsync(ct).ConfigureAwait(false);
        return info;
    }

    /// <summary>Fetches latest version info and returns the underlying error message if it fails.</summary>
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
            var msg = ex.InnerException != null ? ex.Message + " (" + ex.InnerException.Message + ")" : ex.Message;
            return (null, msg);
        }
        catch (TaskCanceledException)
        {
            return (null, "Connection timed out. Your firewall may be blocking the app — run allow-update-server-firewall.ps1 as Administrator.");
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.Message + " — " + ex.InnerException.Message : ex.Message;
            return (null, msg);
        }
    }

    /// <summary>Returns true if the server has a newer version than the current app.</summary>
    public static async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct).ConfigureAwait(false);
        if (latest == null) return false;
        return CompareVersions(CurrentVersion, latest.Version) < 0;
    }

    /// <summary>Returns true if the version string is safe for use in file paths (no path traversal).</summary>
    private static bool IsSafeVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 64) return false;
        return version.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
    }

    /// <summary>Downloads the update zip and returns (extract folder path, or null) and optional error message.</summary>
    public static async Task<(string? extractDir, string? errorMessage)> DownloadUpdateAsync(string version, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!IsSafeVersion(version))
            return (null, "Invalid version format from server.");
        try
        {
            var url = $"{BaseUrl}/api/download/{version}?platform=win64";
            using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"Server returned {(int)response.StatusCode}. Try again later.");
            }
            var total = response.Content.Headers.ContentLength ?? 0L;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var zipPath = Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}.zip");
            await using (var file = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    read += count;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }
            var extractDir = Path.Combine(Path.GetTempPath(), $"PrivacyMonitor-update-{version}");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            try { File.Delete(zipPath); } catch { }
            var exeInDir = Path.Combine(extractDir, "PrivacyMonitor.exe");
            if (!File.Exists(exeInDir))
            {
                foreach (var f in Directory.GetFiles(extractDir, "*.exe"))
                {
                    exeInDir = f;
                    break;
                }
            }
            if (!File.Exists(exeInDir))
                return (null, "Update package is missing the executable.");
            return (extractDir, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Download timed out. Check your connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return (null, msg);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Starts the updater: launches the new exe with --apply-update so it replaces the current exe and restarts.
    /// Call this from the UI thread; then exit the app so the updater can replace the file.
    /// </summary>
    public static void ApplyUpdateAndRestart(string extractedUpdateFolder)
    {
        var exePath = Path.Combine(extractedUpdateFolder, "PrivacyMonitor.exe");
        if (!File.Exists(exePath))
        {
            foreach (var f in Directory.GetFiles(extractedUpdateFolder, "*.exe"))
            {
                exePath = f;
                break;
            }
        }
        if (!File.Exists(exePath)) return;
        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMonitor.exe");
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe)) return;
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
    /// Handles --apply-update: copy new exe over current and restart. Call from App.OnStartup before showing the window.
    /// Returns true if the app should exit (update applied or failed); false to continue normal startup.
    /// </summary>
    public static bool TryHandleApplyUpdate(out bool success)
    {
        success = false;
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 4 || args[1] != "--apply-update") return false;
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
                return true; // Mismatch: ignore and continue normal startup (do not copy over a different file).
        }
        catch { return true; }

        try
        {
            Thread.Sleep(1500);
            File.Copy(newExe, realCurrentExe, true);
            Process.Start(new ProcessStartInfo
            {
                FileName = realCurrentExe,
                UseShellExecute = true,
            });
            success = true;
        }
        catch { }
        return true;
    }

    /// <summary>Notify the server that this client installed/updated (optional analytics).</summary>
    public static async Task LogInstallAsync(string version, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { version, platform = "win64", client = "PrivacyMonitor" });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            await HttpClient.PostAsync($"{BaseUrl}/api/install-log", content, ct).ConfigureAwait(false);
        }
        catch { }
    }

    /// <summary>Send anonymous usage data to improve the browser (version, OS, protection level). Only call if user has allowed it.</summary>
    public static async Task SendUsageAsync(bool allowUsageData, string protectionMode, CancellationToken ct = default)
    {
        if (!allowUsageData) return;
        try
        {
            var osDesc = Environment.OSVersion.Version.Major >= 10
                ? $"Windows {Environment.OSVersion.Version.Major}"
                : Environment.OSVersion.ToString();
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
        catch { }
    }
}

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string? ReleaseDate { get; set; }
}
