using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;

namespace PrivacyMonitor;

/// <summary>
/// Creates a Windows desktop shortcut to the current app so users have easy access after download/install.
/// Reflection-based COM interop is used for compatibility and to avoid an extra dependency.
/// </summary>
public static class DesktopShortcut
{
    private const string ShortcutName = "Privacy Monitor.lnk";

    /// <summary>
    /// Ensures a desktop shortcut exists. Creates it if missing or if the target path changed (e.g. user moved the exe).
    /// Call once at app startup (e.g. from App.OnStartup). Safe to call every run; only creates/updates when needed.
    /// For UI snappiness, consider running in a background task if startup time is critical.
    /// </summary>
    public static void EnsureDesktopShortcut()
    {
        // Note: Shortcut creation is very fast but can be put on a background thread if desired:
        // Task.Run(() => EnsureDesktopShortcutImpl());

        EnsureDesktopShortcutImpl();
    }

    private static void EnsureDesktopShortcutImpl()
    {
        try
        {
            // .NET 6+ ProcessPath; fallback just in case
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMonitor.exe");
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop))
                return;

            var shortcutPath = Path.Combine(desktop, ShortcutName);
            var exeDir = Path.GetDirectoryName(exePath) ?? "";

            // If shortcut exists and already points to this exe, skip
            var targetOfExisting = File.Exists(shortcutPath) ? GetShortcutTargetPath(shortcutPath) : null;
            if (targetOfExisting != null && string.Equals(targetOfExisting, exePath, StringComparison.OrdinalIgnoreCase))
                return;

            CreateShortcut(shortcutPath, exePath, exeDir);
        }
        catch (Exception ex)
        {
            // Non-critical: do not crash the app if shortcut creation fails (e.g. no desktop, permissions).
            // Optionally log here for diagnostics.
            // Console.Error.WriteLine($"Failed to ensure desktop shortcut: {ex}");
        }
    }

    /// <summary>
    /// Creates a .lnk shortcut using late-bound COM interop.
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
        if (shellType == null)
            return;

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return;

            // CreateShortcut returns a COM object implementing IWshShortcut
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut == null)
                return;

            var t = shortcut.GetType();
            // Reflection slightly slower than direct, but fine for infrequent calls.
            t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Privacy Monitor – Privacy-first browser" });
            // Save shortcut to disk
            t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            // Release COM objects, safe for both regular and exceptional paths.
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.ReleaseComObject(shortcut);

            if (shell is IDisposable d)
                d.Dispose();
            else if (shell != null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Gets the TargetPath of a shortcut (.lnk file), or null on error.
    /// </summary>
    private static string? GetShortcutTargetPath(string shortcutPath)
    {
        Type? shellType = null;
        object? shell = null;
        object? shortcut = null;
        try
        {
            shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
            if (shellType == null) return null;

            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });
            if (shortcut == null) return null;

            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null);
            return target as string;
        }
        catch
        {
            // Not critical: just means shortcut isn't valid or COM not available
            return null;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.ReleaseComObject(shortcut);

            if (shell is IDisposable d)
                d.Dispose();
            else if (shell != null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
    }
}
