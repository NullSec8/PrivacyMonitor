using System.IO;
using System.Text;
using System.Text.Json;

namespace PrivacyMonitor;

/// <summary>
/// Exports the browser blocklist to the Chrome extension folder: tracker-domains.js, rules.json, rules-stealth.json.
/// Same engine as the browser (ProtectionEngine + PrivacyEngine). No Node.js required.
/// </summary>
public static class ChromeExtensionExport
{
    // Block ALL resource types so the extension is strong: frames, images, scripts, XHR, fonts, ping, websocket, etc.
    private static readonly string[] ResourceTypesBlock =
    {
        "main_frame", "sub_frame", "stylesheet", "script", "image", "font", "object",
        "xmlhttprequest", "ping", "csp_report", "media", "websocket", "other"
    };
    // Stealth: redirect script/XHR to empty JS (lower priority so block wins when both match)
    private static readonly string[] ResourceTypesStealth = { "script", "xmlhttprequest" };
    private const string EmptyJs = "data:text/javascript,";

    /// <summary>Writes tracker-domains.js for the extension background script.</summary>
    public static void WriteTrackerDomainsJs(string extensionDir, string[] domains)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/**");
        sb.AppendLine(" * Generated from Privacy Monitor browser engines (ProtectionEngine + PrivacyEngine).");
        sb.AppendLine(" * Run: PrivacyMonitor.exe --export-blocklist or dotnet run --project wpf-browser/ExportBlocklist");
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

        var path = Path.Combine(extensionDir, "tracker-domains.js");
        if (!Directory.Exists(extensionDir))
            Directory.CreateDirectory(extensionDir);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Writes rules.json and rules-stealth.json for declarativeNetRequest (same format as generate-rules.js).</summary>
    public static void WriteDeclarativeNetRequestRules(string extensionDir, string[] domains)
    {
        if (!Directory.Exists(extensionDir))
            Directory.CreateDirectory(extensionDir);

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var rulesBlock = new List<object>();
        var rulesStealth = new List<object>();
        for (var i = 0; i < domains.Length; i++)
        {
            var urlFilter = "||" + domains[i] + "^";
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
