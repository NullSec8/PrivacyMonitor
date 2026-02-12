using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyMonitor
{
    /// <summary>
    /// Summarises script and JS-like resources on a page for forensic / educational views.
    /// </summary>
    public sealed class ScriptResource
    {
        public string Host { get; init; } = "";
        public string Path { get; init; } = "";
        public bool IsThirdParty { get; init; }
        public bool IsTracker { get; init; }
        public string TrackerLabel { get; init; } = "";
        public string TrackerCompany { get; init; } = "";
    }

    public static class ScriptCatalog
    {
        public static List<ScriptResource> Build(ScanResult scan)
        {
            // ResourceContext is populated from WebResourceRequested; look for "Script" or "script".
            var scripts = scan.Requests
                .Where(r =>
                    (!string.IsNullOrEmpty(r.ResourceContext) &&
                     r.ResourceContext.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(r.ContentType) &&
                     r.ContentType.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(r => new { r.Host, r.Path, r.IsThirdParty, r.TrackerLabel, r.TrackerCompany })
                .Select(g => new ScriptResource
                {
                    Host = g.Key.Host,
                    Path = g.Key.Path,
                    IsThirdParty = g.Key.IsThirdParty,
                    IsTracker = !string.IsNullOrEmpty(g.Key.TrackerLabel),
                    TrackerLabel = g.Key.TrackerLabel ?? "",
                    TrackerCompany = g.Key.TrackerCompany ?? ""
                })
                .OrderByDescending(sr => sr.IsTracker)
                .ThenByDescending(sr => sr.IsThirdParty)
                .ThenBy(sr => sr.Host, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList();

            return scripts;
        }
    }
}

