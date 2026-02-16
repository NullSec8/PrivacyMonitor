using System.IO;
using System.Runtime.InteropServices;

namespace PrivacyMonitor;

/// <summary>
/// Creates a Windows desktop shortcut to the current app so users have easy access after download/install.
/// </summary>
public static class DesktopShortcut
{
    private const string ShortcutName = "Privacy Monitor.lnk";

    /// <summary>
    /// Ensures a desktop shortcut exists. Creates it if missing or if the target path changed (e.g. user moved the exe).
    /// Call once at app startup (e.g. from App.OnStartup). Safe to call every run; only creates/updates when needed.
    /// </summary>
    public static void EnsureDesktopShortcut()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMonitor.exe");
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop))
                return;

            var shortcutPath = Path.Combine(desktop, ShortcutName);
            var exeDir = Path.GetDirectoryName(exePath) ?? "";

            // If shortcut exists and already points to this exe, skip
            if (File.Exists(shortcutPath) && GetShortcutTargetPath(shortcutPath) == exePath)
                return;

            CreateShortcut(shortcutPath, exePath, exeDir);
        }
        catch
        {
            // Non-critical: do not crash the app if shortcut creation fails (e.g. no desktop, permissions).
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            return;

        object? shell = Activator.CreateInstance(shellType);
        if (shell == null)
            return;

        try
        {
            // CreateShortcut(fullPath) returns IShellLinkW / IWshShortcut
            object? shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null)
                return;

            var t = shortcut.GetType();
            t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
            t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Privacy Monitor – Privacy-first browser" });
            t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            if (shell is IDisposable d)
                d.Dispose();
            else if (Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
    }

    private static string? GetShortcutTargetPath(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            try
            {
                object? shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut == null) return null;

                var target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                return target as string;
            }
            finally
            {
                if (shell is IDisposable d)
                    d.Dispose();
                else if (Marshal.IsComObject(shell))
                    Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }
}
