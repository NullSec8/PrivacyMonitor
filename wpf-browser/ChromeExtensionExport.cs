using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrivacyMonitor;

/// <summary>
/// Exports the browser blocklist to the Chrome extension folder: tracker-domains.js, rules.json, rules-stealth.json.
/// Same engine as the browser (ProtectionEngine + PrivacyEngine). No Node.js required.
/// </summary>
public static class ChromeExtensionExport
{
    private static readonly string[] ResourceTypesBlock =
    {
        "main_frame", "sub_frame", "stylesheet", "script", "image", "font", "object",
        "xmlhttprequest", "ping", "csp_report", "media", "websocket", "other"
    };
    private static readonly string[] ResourceTypesStealth = { "script", "xmlhttprequest" };
    private const string EmptyJs = "data:text/javascript,";

    private static readonly Regex SafeDomainPattern = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);

    private static void ValidateInputs(string extensionDir, string[] domains)
    {
        if (string.IsNullOrWhiteSpace(extensionDir))
            throw new ArgumentException("Extension directory cannot be empty.", nameof(extensionDir));
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));

        var fullPath = Path.GetFullPath(extensionDir);
        if (!fullPath.Equals(extensionDir, StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFullPath(extensionDir).Contains("chrome-extension", StringComparison.OrdinalIgnoreCase))
        {
            // Allow any valid resolved path but log a warning for unexpected locations
            System.Diagnostics.Debug.WriteLine($"[ChromeExtensionExport] Writing to non-standard path: {fullPath}");
        }
    }

    private static string SanitizeDomain(string domain)
    {
        var d = domain.Trim().ToLowerInvariant();
        d = d.Replace("\r", "").Replace("\n", "").Replace("\"", "").Replace("\\", "");
        if (d.Length > 253 || !SafeDomainPattern.IsMatch(d))
            return "";
        return d;
    }

    /// <summary>Writes tracker-domains.js for the extension background script.</summary>
    public static void WriteTrackerDomainsJs(string extensionDir, string[] domains)
    {
        ValidateInputs(extensionDir, domains);
        var safeDomains = domains.Select(SanitizeDomain).Where(d => d.Length > 0).Distinct().ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("/**");
        sb.AppendLine(" * Generated from Privacy Monitor browser engines (ProtectionEngine + PrivacyEngine).");
        sb.AppendLine(" * Run: PrivacyMonitor.exe --export-blocklist or dotnet run --project wpf-browser/ExportBlocklist");
        sb.AppendLine(" */");
        sb.AppendLine("const BLOCK_KNOWN_DOMAINS = [");
        foreach (var d in safeDomains)
            sb.AppendLine("  \"" + d + "\",");
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

        var path = Path.Combine(extensionDir, "tracker-domains.js");
        if (!Directory.Exists(extensionDir))
            Directory.CreateDirectory(extensionDir);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Writes rules.json and rules-stealth.json for declarativeNetRequest (same format as generate-rules.js).</summary>
    public static void WriteDeclarativeNetRequestRules(string extensionDir, string[] domains)
    {
        ValidateInputs(extensionDir, domains);
        var safeDomains = domains.Select(SanitizeDomain).Where(d => d.Length > 0).Distinct().ToArray();

        if (!Directory.Exists(extensionDir))
            Directory.CreateDirectory(extensionDir);

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var rulesBlock = new List<object>();
        var rulesStealth = new List<object>();
        for (var i = 0; i < safeDomains.Length; i++)
        {
            var urlFilter = "||" + safeDomains[i] + "^";
            rulesBlock.Add(new
            {
                id = i + 1,
                priority = 2,
                action = new { type = "block" },
                condition = new
                {
                    urlFilter,
                    resourceTypes = ResourceTypesBlock,
                    isUrlFilterCaseSensitive = false
                }
            });
            rulesStealth.Add(new
            {
                id = i + 1,
                priority = 1,
                action = new { type = "redirect", redirect = new { url = EmptyJs } },
                condition = new
                {
                    urlFilter,
                    resourceTypes = ResourceTypesStealth,
                    isUrlFilterCaseSensitive = false
                }
            });
        }

        File.WriteAllText(Path.Combine(extensionDir, "rules.json"), JsonSerializer.Serialize(rulesBlock, opts));
        File.WriteAllText(Path.Combine(extensionDir, "rules-stealth.json"), JsonSerializer.Serialize(rulesStealth, opts));
    }

    /// <summary>Writes all three files: tracker-domains.js, rules.json, rules-stealth.json.</summary>
    public static void WriteAll(string extensionDir, string[] domains)
    {
        WriteTrackerDomainsJs(extensionDir, domains);
        WriteDeclarativeNetRequestRules(extensionDir, domains);
    }
}
