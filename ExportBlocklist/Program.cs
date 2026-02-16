using System.Reflection;
using System.Text;
using PrivacyMonitor;

string? repoRoot = FindRepoRoot();
if (repoRoot == null)
{
    Console.Error.WriteLine("Could not find repo root (folder containing chrome-extension).");
    Environment.Exit(1);
}

string outPath = Path.Combine(repoRoot, "chrome-extension", "tracker-domains.js");
string[] domains = ProtectionEngine.GetBlocklistDomainsForExport();

var sb = new StringBuilder();
sb.AppendLine("/**");
sb.AppendLine(" * Generated from Privacy Monitor browser engines (ProtectionEngine + PrivacyEngine).");
sb.AppendLine(" * Run: dotnet run --project ExportBlocklist");
sb.AppendLine(" * Block known = Aggressive = same full blocklist as the desktop app.");
sb.AppendLine(" */");
sb.AppendLine("const BLOCK_KNOWN_DOMAINS = [");
foreach (var d in domains)
    sb.AppendLine("  \"" + d.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",");
sb.AppendLine("];");
sb.AppendLine("const AGGRESSIVE_EXTRA_DOMAINS = [];");
sb.AppendLine("function getDomainsForMode(mode) {");
sb.AppendLine("  if (mode === 'off') return [];");
sb.AppendLine("  if (mode === 'aggressive') return BLOCK_KNOWN_DOMAINS;");
sb.AppendLine("  return BLOCK_KNOWN_DOMAINS;");
sb.AppendLine("}");
sb.AppendLine("if (typeof self !== \"undefined\") {");
sb.AppendLine("  self.BLOCK_KNOWN_DOMAINS = BLOCK_KNOWN_DOMAINS;");
sb.AppendLine("  self.AGGRESSIVE_EXTRA_DOMAINS = AGGRESSIVE_EXTRA_DOMAINS;");
sb.AppendLine("  self.getDomainsForMode = getDomainsForMode;");
sb.AppendLine("}");

string dir = Path.GetDirectoryName(outPath)!;
if (!Directory.Exists(dir))
    Directory.CreateDirectory(dir);
File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
Console.WriteLine($"Wrote {domains.Length} domains to {outPath}");

static string? FindRepoRoot()
{
    string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir, "chrome-extension")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}
