using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrivacyMonitor
{
    public enum SiteRuleAction
    {
        BlockDomain,
        BlockPathContains,
        ForceHttps,
        RewriteHeader
    }

    public class SiteRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ScopeHost { get; set; } = "";
        public SiteRuleAction Action { get; set; }
        public string MatchValue { get; set; } = "";
        public string HeaderName { get; set; } = "";
        public string HeaderValue { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class SiteRuleDecision
    {
        public bool Blocked { get; set; }
        public bool ForceHttps { get; set; }
        public string Reason { get; set; } = "";
        public string RewriteHeaderName { get; set; } = "";
        public string RewriteHeaderValue { get; set; } = "";
    }

    public static class SiteRulesEngine
    {
        private static readonly ConcurrentDictionary<string, List<SiteRule>> _rulesBySite = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string RulesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "site-rules.json");

        static SiteRulesEngine()
        {
            Load();
        }

        public static IReadOnlyList<SiteRule> GetRules(string siteHost)
        {
            if (string.IsNullOrWhiteSpace(siteHost))
                return Array.Empty<SiteRule>();
            if (_rulesBySite.TryGetValue(siteHost, out var rules))
            {
                lock (rules)
                {
                    return rules.OrderByDescending(r => r.CreatedAtUtc).ToList();
                }
            }
            return Array.Empty<SiteRule>();
        }

        public static void AddRule(SiteRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.ScopeHost))
                return;
            var list = _rulesBySite.GetOrAdd(rule.ScopeHost, _ => new List<SiteRule>());
            lock (list)
            {
                list.Add(rule);
            }
            Save();
        }

        public static void RemoveRule(string siteHost, string ruleId)
        {
            if (string.IsNullOrWhiteSpace(siteHost) || string.IsNullOrWhiteSpace(ruleId))
                return;
            if (!_rulesBySite.TryGetValue(siteHost, out var list))
                return;
            lock (list)
            {
                list.RemoveAll(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            }
            Save();
        }

        public static SiteRuleDecision Evaluate(string siteHost, RequestEntry entry, Uri uri)
        {
            var decision = new SiteRuleDecision();
            if (string.IsNullOrWhiteSpace(siteHost) || !_rulesBySite.TryGetValue(siteHost, out var list))
                return decision;

            List<SiteRule> active;
            lock (list)
            {
                active = list.Where(r => r.Enabled).ToList();
            }

            foreach (var rule in active)
            {
                switch (rule.Action)
                {
                    case SiteRuleAction.BlockDomain:
                    {
                        var target = (rule.MatchValue ?? "").Trim().ToLowerInvariant();
                        var host = (uri.Host ?? "").ToLowerInvariant();
                        if (!string.IsNullOrEmpty(target) && (host == target || host.EndsWith("." + target, StringComparison.OrdinalIgnoreCase)))
                        {
                            decision.Blocked = true;
                            decision.Reason = "Rule: blocked domain (" + target + ")";
                            return decision;
                        }
                        break;
                    }
                    case SiteRuleAction.BlockPathContains:
                    {
                        var token = (rule.MatchValue ?? "").Trim();
                        if (!string.IsNullOrEmpty(token) &&
                            ((uri.PathAndQuery?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (entry.FullUrl?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)))
                        {
                            decision.Blocked = true;
                            decision.Reason = "Rule: blocked path token (" + token + ")";
                            return decision;
                        }
                        break;
                    }
                    case SiteRuleAction.ForceHttps:
                    {
                        var host = (uri.Host ?? "").ToLowerInvariant();
                        var target = (rule.MatchValue ?? "").Trim().ToLowerInvariant();
                        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrEmpty(target) || host == target || host.EndsWith("." + target, StringComparison.OrdinalIgnoreCase)))
                        {
                            decision.ForceHttps = true;
                        }
                        break;
                    }
                    case SiteRuleAction.RewriteHeader:
                    {
                        if (!string.IsNullOrWhiteSpace(rule.HeaderName))
                        {
                            decision.RewriteHeaderName = rule.HeaderName.Trim();
                            decision.RewriteHeaderValue = rule.HeaderValue ?? "";
                        }
                        break;
                    }
                }
            }
            return decision;
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(RulesPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var data = _rulesBySite.ToDictionary(kv => kv.Key, kv => kv.Value);
                File.WriteAllText(RulesPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(RulesPath))
                    return;
                var json = File.ReadAllText(RulesPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<SiteRule>>>(json);
                if (data == null)
                    return;
                _rulesBySite.Clear();
                foreach (var kv in data)
                {
                    _rulesBySite[kv.Key] = kv.Value ?? new List<SiteRule>();
                }
            }
            catch { }
        }
    }
}
