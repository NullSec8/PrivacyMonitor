using System.Reflection;
using PrivacyMonitor;

string? repoRoot = FindRepoRoot();
if (repoRoot == null)
{
    Console.Error.WriteLine("Could not find repo root (folder containing chrome-extension).");
    Environment.Exit(1);
}

string extensionDir = Path.Combine(repoRoot, "chrome-extension");
string[] domains = ProtectionEngine.GetBlocklistDomainsForExport();

ChromeExtensionExport.WriteAll(extensionDir, domains);

Console.WriteLine($"Wrote {domains.Length} domains to tracker-domains.js, rules.json, rules-stealth.json in {extensionDir}");

static string? FindRepoRoot()
{
    // Prefer workspace root (has both chrome-extension and wpf-browser) so export writes to repo root chrome-extension.
    string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, "chrome-extension")) &&
            Directory.Exists(Path.Combine(dir, "wpf-browser")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, "chrome-extension")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}
