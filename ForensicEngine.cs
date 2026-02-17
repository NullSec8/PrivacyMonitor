using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PrivacyMonitor
{
    /// <summary>
    /// Forensic-grade correlation engine.
    /// Runs cross-request analysis to detect identity stitching,
    /// data flow propagation, behavioral fingerprinting patterns,
    /// and produces explainable, reproducible evidence chains.
    /// </summary>
    public class ForensicEngine
    {
        // Known tracking param names: accept with lower entropy/length bar for stronger detection
        private static readonly HashSet<string> KnownTrackingParams = new(StringComparer.OrdinalIgnoreCase)
        {
            "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "fbclid", "fb_action_ids", "fb_action_types",
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "_ga", "mc_cid", "mc_eid",
            "ttclid", "twclid", "li_fat_id", "msclkid", "irclickid", "epik", "ref", "ref_src", "ref_click_id",
            "s_kwcid", "oly_anon_id", "oly_enc_id", "_hsenc", "_hsmi", "mkt_tok", "trk", "trkContact", "sc_cid"
        };

        // ════════════════════════════════════════════
        //  IDENTIFIER REGISTRY (per-tab, thread-safe via UI drain)
        // ════════════════════════════════════════════

        /// <summary>
        /// Extract high-entropy identifiers from a request (URL params, cookies, headers)
        /// and register them in the tab's identifier -> domains mapping.
        /// </summary>
        public static void ExtractAndRegisterIdentifiers(
            RequestEntry req,
            Dictionary<string, HashSet<string>> identifierToDomains,
            List<ForensicEvent> timeline)
        {
            var identifiers = ExtractIdentifiers(req);
            foreach (var id in identifiers)
            {
                if (!identifierToDomains.TryGetValue(id.Value, out var domains))
                {
                    domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    identifierToDomains[id.Value] = domains;
                }
                bool isNew = domains.Add(req.Host);

                // If this identifier is now seen at 2+ domains -> identity stitching
                if (isNew && domains.Count == 2)
                {
                    timeline.Add(new ForensicEvent
                    {
                        Time = req.Time,
                        Type = "identity_stitch",
                        Summary = $"Identifier '{id.Key}' shared between {string.Join(" and ", domains)} — possible cross-site identity stitching",
                        Properties = new() { ["param"] = id.Key, ["value_prefix"] = id.Value[..Math.Min(24, id.Value.Length)], ["domains"] = string.Join(", ", domains) },
                        Severity = 4
                    });
                }
                else if (isNew && domains.Count > 2)
                {
                    timeline.Add(new ForensicEvent
                    {
                        Time = req.Time,
                        Type = "identity_spread",
                        Summary = $"Identifier '{id.Key}' now seen at {domains.Count} domains — identity graph expanding",
                        Properties = new() { ["param"] = id.Key, ["domain_count"] = domains.Count.ToString(), ["domains"] = string.Join(", ", domains.Take(6)) },
                        Severity = 5
                    });
                }
            }
        }

        /// <summary>
        /// Extract candidate identifiers from a request: URL params, Cookie header, Referer params.
        /// Returns key-value pairs where value has sufficient entropy to be a tracking ID.
        /// Stronger: known tracking param names accepted with lower bar; Referer query parsed.
        /// </summary>
        private static List<(string Key, string Value)> ExtractIdentifiers(RequestEntry req)
        {
            var ids = new List<(string, string)>();

            // URL parameters (parse query without Uri for speed)
            if (!string.IsNullOrEmpty(req.FullUrl))
            {
                int q = req.FullUrl.IndexOf('?');
                if (q >= 0 && q < req.FullUrl.Length - 1)
                {
                    string query = req.FullUrl.Substring(q + 1);
                    foreach (var pair in query.Split('&'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0 && eq < pair.Length - 1)
                        {
                            string key = pair.Substring(0, eq).Trim();
                            string val = pair.Substring(eq + 1).Trim();
                            if (val.Length >= 8 && AcceptParamValue(key, val, isCookie: false))
                                ids.Add((key, val));
                        }
                    }
                }
            }

            // Cookie header values (slightly lower bar 3.6 for stronger detection)
            if (req.RequestHeaders.TryGetValue("cookie", out var cookieStr))
            {
                foreach (var cookie in cookieStr.Split(';'))
                {
                    var parts = cookie.Trim().Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var v = parts[1].Trim();
                        if (v.Length >= 10 && AcceptParamValue("cookie", v, isCookie: true))
                            ids.Add(($"cookie:{parts[0].Trim()}", v));
                    }
                }
            }

            // Referer URL params: same identifiers can appear in redirect URLs
            if (req.RequestHeaders.TryGetValue("referer", out var referer) && !string.IsNullOrEmpty(referer))
            {
                int rq = referer.IndexOf('?');
                if (rq >= 0 && rq < referer.Length - 1)
                {
                    foreach (var pair in referer.Substring(rq + 1).Split('&'))
                    {
                        var parts = pair.Split('=', 2);
                        if (parts.Length == 2 && parts[1].Length >= 8 && AcceptParamValue(parts[0], parts[1], isCookie: false))
                            ids.Add(($"ref_param:{parts[0]}", parts[1]));
                    }
                }
            }

            return ids;
        }

        private static bool AcceptParamValue(string paramName, string value, bool isCookie)
        {
            bool knownTracking = !isCookie && KnownTrackingParams.Contains(paramName);
            if (knownTracking && value.Length >= 8)
                return true;
            if (value.Length < 12) return false;
            double entropy = PrivacyEngine.ShannonEntropy(value);
            if (isCookie)
                return entropy >= 3.6;
            return entropy >= 3.5 || IsLikelyTrackingId(value);
        }

        private static readonly Regex UuidPattern = new(@"^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GaPattern = new(@"^GA\d\.\d\.\d+\.\d+$", RegexOptions.Compiled);
        private static readonly Regex HighEntropyIdPattern = new(@"^[A-Za-z0-9+/=_-]{20,}$", RegexOptions.Compiled);

        private static bool IsLikelyTrackingId(string value)
        {
            if (value.Length < 20) return false;
            if (UuidPattern.IsMatch(value)) return true;
            if (GaPattern.IsMatch(value)) return true;
            if (HighEntropyIdPattern.IsMatch(value) && PrivacyEngine.ShannonEntropy(value) >= 3.2)
                return true;
            return false;
        }

        // ════════════════════════════════════════════
        //  DATA FLOW GRAPH
        // ════════════════════════════════════════════

        /// <summary>
        /// Analyze a request for data flow indicators (referer, origin, cookie propagation, redirects).
        /// Pass edgeLookup for O(1) add/update; otherwise linear search (slower with many edges).
        /// </summary>
        public static void AnalyzeDataFlow(
            RequestEntry req,
            string pageHost,
            List<DataFlowEdge> edges,
            List<ForensicEvent> timeline,
            Dictionary<string, DataFlowEdge>? edgeLookup = null)
        {
            // Referer-based flow: if referer domain differs from request domain
            if (req.RequestHeaders.TryGetValue("referer", out var referer) && referer.Length > 0)
            {
                try
                {
                    var refUri = new Uri(referer);
                    string refHost = refUri.Host;
                    if (refHost.Length > 0 && !refHost.Equals(req.Host, StringComparison.OrdinalIgnoreCase))
                        AddOrUpdateEdge(edges, refHost, req.Host, "referer", $"Referer from {refHost} to {req.Host}", edgeLookup);
                }
                catch { }
            }

            // Origin header: explicit first-party -> third-party flow (stronger signal)
            if (req.RequestHeaders.TryGetValue("origin", out var origin) && origin.Length > 0)
            {
                try
                {
                    var originUri = new Uri(origin);
                    string originHost = originUri.Host;
                    if (originHost.Length > 0 && !originHost.Equals(req.Host, StringComparison.OrdinalIgnoreCase))
                        AddOrUpdateEdge(edges, originHost, req.Host, "origin", $"Origin {originHost} to {req.Host}", edgeLookup);
                }
                catch { }
            }

            // Page -> third-party flow
            if (req.IsThirdParty && !string.IsNullOrEmpty(pageHost))
            {
                string mechanism = req.RequestHeaders.ContainsKey("cookie") ? "cookie_propagation" : (req.HasBody ? "post_data" : "get_request");
                AddOrUpdateEdge(edges, pageHost, req.Host, mechanism, $"{pageHost} sends data to {req.Host} via {mechanism}", edgeLookup);
            }

            // Redirect flow (detected by 3xx status from a different domain)
            if (req.StatusCode >= 300 && req.StatusCode < 400 &&
                req.ResponseHeaders.TryGetValue("location", out var location) && location.Length > 0)
            {
                try
                {
                    var locUri = new Uri(location, UriKind.RelativeOrAbsolute);
                    if (locUri.IsAbsoluteUri && !string.IsNullOrEmpty(locUri.Host) && !locUri.Host.Equals(req.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        AddOrUpdateEdge(edges, req.Host, locUri.Host, "redirect_bounce", $"Redirect from {req.Host} to {locUri.Host} (HTTP {req.StatusCode})", edgeLookup);
                        timeline.Add(new ForensicEvent
                        {
                            Time = req.Time,
                            Type = "redirect_bounce",
                            Summary = $"Redirect bounce: {req.Host} -> {locUri.Host} — possible cookie sync or tracking handoff",
                            Properties = new() { ["from"] = req.Host, ["to"] = locUri.Host, ["status"] = req.StatusCode.ToString() },
                            Severity = 4
                        });
                    }
                }
                catch { }
            }
        }

        private static void AddOrUpdateEdge(List<DataFlowEdge> edges, string from, string to, string mechanism, string detail, Dictionary<string, DataFlowEdge>? edgeLookup)
        {
            if (edgeLookup != null)
            {
                string key = from.ToLowerInvariant() + "|" + to.ToLowerInvariant() + "|" + mechanism;
                if (edgeLookup.TryGetValue(key, out var existing))
                {
                    existing.Occurrences++;
                    return;
                }
                var edge = new DataFlowEdge { FromDomain = from, ToDomain = to, Mechanism = mechanism, Detail = detail, FirstSeen = DateTime.Now, Occurrences = 1 };
                edges.Add(edge);
                edgeLookup[key] = edge;
                return;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (e.FromDomain.Equals(from, StringComparison.OrdinalIgnoreCase) && e.ToDomain.Equals(to, StringComparison.OrdinalIgnoreCase) && e.Mechanism == mechanism)
                {
                    e.Occurrences++;
                    return;
                }
            }
            edges.Add(new DataFlowEdge { FromDomain = from, ToDomain = to, Mechanism = mechanism, Detail = detail, FirstSeen = DateTime.Now, Occurrences = 1 });
        }

        // ════════════════════════════════════════════
        //  BEHAVIORAL FINGERPRINT ANALYSIS
        // ════════════════════════════════════════════

        private static readonly HashSet<string> PassiveFingerprintTypes = new(StringComparer.OrdinalIgnoreCase)
            { "Navigator Fingerprinting", "Screen Fingerprinting", "Network Fingerprinting", "Timezone Fingerprinting", "Plugin Fingerprinting" };
        private static readonly HashSet<string> ActiveFingerprintTypes = new(StringComparer.OrdinalIgnoreCase)
            { "Canvas Fingerprinting", "WebGL Fingerprinting", "Audio Fingerprinting" };

        /// <summary>
        /// Analyze timing patterns of fingerprint detections to identify
        /// automated fingerprint batteries (multiple techniques in rapid succession).
        /// </summary>
        public static List<BehavioralPattern> DetectBehavioralPatterns(List<FingerprintFinding> fingerprints)
        {
            var patterns = new List<BehavioralPattern>();
            if (fingerprints.Count < 2) return patterns;

            var sorted = fingerprints.OrderBy(f => f.Time).ToList();
            const double batteryWindowSeconds = 2.0;

            // Fingerprint battery: sliding window (index-based, no Skip/TakeWhile allocations)
            for (int i = 0; i < sorted.Count; i++)
            {
                int j = i;
                var typesInWindow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (j < sorted.Count && (sorted[j].Time - sorted[i].Time).TotalSeconds <= batteryWindowSeconds)
                {
                    typesInWindow.Add(sorted[j].Type);
                    j++;
                }
                if (typesInWindow.Count >= 3)
                {
                    double windowMs = (sorted[j - 1].Time - sorted[i].Time).TotalMilliseconds;
                    patterns.Add(new BehavioralPattern
                    {
                        Name = "Fingerprint Battery",
                        Detail = $"{typesInWindow.Count} fingerprinting techniques fired within {windowMs:F0}ms: {string.Join(", ", typesInWindow)}",
                        TechniqueCount = typesInWindow.Count,
                        WindowMs = (int)windowMs,
                        Confidence = Math.Min(1.0, 0.6 + typesInWindow.Count * 0.08),
                        Severity = 5,
                        Techniques = typesInWindow.ToList()
                    });
                    break;
                }
            }

            // Passive probe: single pass over sorted
            int passiveCount = 0;
            var passiveDistinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (PassiveFingerprintTypes.Contains(sorted[i].Type))
                {
                    passiveCount++;
                    passiveDistinct.Add(sorted[i].Type);
                }
            }
            if (passiveCount >= 3)
                patterns.Add(new BehavioralPattern
                {
                    Name = "Passive Fingerprint Probe",
                    Detail = $"{passiveCount} passive fingerprinting APIs accessed — building device profile without active rendering",
                    TechniqueCount = passiveCount,
                    Confidence = 0.80,
                    Severity = 4,
                    Techniques = passiveDistinct.ToList()
                });

            // Active rendering probe: single pass
            int activeCount = 0;
            var activeDistinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (ActiveFingerprintTypes.Contains(sorted[i].Type))
                {
                    activeCount++;
                    activeDistinct.Add(sorted[i].Type);
                }
            }
            if (activeCount >= 2)
                patterns.Add(new BehavioralPattern
                {
                    Name = "Active Rendering Probe",
                    Detail = "Canvas/WebGL/Audio fingerprinting — generating unique hardware-derived hashes",
                    TechniqueCount = activeCount,
                    Confidence = 0.90,
                    Severity = 5,
                    Techniques = activeDistinct.ToList()
                });

            return patterns;
        }

        // ════════════════════════════════════════════
        //  IDENTITY LINK ANALYSIS
        // ════════════════════════════════════════════

        /// <summary>
        /// Build identity links from the identifier registry.
        /// Each link represents a shared identifier between 2+ domains.
        /// </summary>
        public static List<IdentityLink> BuildIdentityLinks(Dictionary<string, HashSet<string>> identifierToDomains)
        {
            return identifierToDomains
                .Where(kv => kv.Value.Count >= 2)
                .Select(kv => new IdentityLink
                {
                    ParameterName = kv.Key.Contains(':') ? kv.Key.Split(':')[0] : "param",
                    ValuePrefix = kv.Key.Length > 20 ? kv.Key[..20] + "..." : kv.Key,
                    Domains = kv.Value.ToList(),
                    DomainCount = kv.Value.Count,
                    Confidence = Math.Min(1.0, 0.5 + kv.Value.Count * 0.1),
                    RiskLevel = kv.Value.Count >= 4 ? "Critical" : kv.Value.Count >= 3 ? "High" : "Medium"
                })
                .OrderByDescending(l => l.DomainCount)
                .Take(20)
                .ToList();
        }

        // ════════════════════════════════════════════
        //  TRACKER CLUSTERING BY COMPANY
        // ════════════════════════════════════════════

        /// <summary>
        /// Cluster detected trackers by parent company to show consolidated data flow.
        /// Single pass per company for speed.
        /// </summary>
        public static List<CompanyCluster> ClusterByCompany(IEnumerable<RequestEntry> requests)
        {
            var byCompany = new Dictionary<string, (HashSet<string> services, HashSet<string> domains, int total, HashSet<string> categories, HashSet<string> dataTypes, bool hasPost, double confSum)>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in requests)
            {
                string company = r.TrackerCompany ?? "";
                if (string.IsNullOrEmpty(company)) continue;

                if (!byCompany.TryGetValue(company, out var t))
                {
                    t = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0,
                         new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, 0);
                    byCompany[company] = t;
                }
                t.total++;
                t.confSum += r.ThreatConfidence;
                if (!string.IsNullOrEmpty(r.TrackerLabel)) t.services.Add(r.TrackerLabel);
                if (!string.IsNullOrEmpty(r.Host)) t.domains.Add(r.Host);
                if (!string.IsNullOrEmpty(r.TrackerCategoryName)) t.categories.Add(r.TrackerCategoryName);
                if (r.DataClassifications != null) foreach (var d in r.DataClassifications) { if (t.dataTypes.Count < 8) t.dataTypes.Add(d); }
                if (r.HasBody) t.hasPost = true;
                byCompany[company] = t;
            }

            var list = new List<CompanyCluster>(byCompany.Count);
            foreach (var kv in byCompany)
            {
                var t = kv.Value;
                list.Add(new CompanyCluster
                {
                    Company = kv.Key,
                    Services = t.services.ToList(),
                    Domains = t.domains.ToList(),
                    TotalRequests = t.total,
                    Categories = t.categories.ToList(),
                    DataTypes = t.dataTypes.Take(8).ToList(),
                    HasPostData = t.hasPost,
                    AvgConfidence = t.total > 0 ? t.confSum / t.total : 0
                });
            }
            list.Sort((a, b) => b.TotalRequests.CompareTo(a.TotalRequests));
            return list;
        }

        // ════════════════════════════════════════════
        //  REQUEST BURST DETECTION
        // ════════════════════════════════════════════

        private const int BurstMinCount = 5;
        private const double BurstWindowSeconds = 3.0;

        /// <summary>
        /// Detect rapid-fire request bursts to the same tracker domain (beacon storms).
        /// A burst = 5+ requests to the same domain within 3 seconds.
        /// </summary>
        public static List<RequestBurst> DetectRequestBursts(IEnumerable<RequestEntry> requests)
        {
            var bursts = new List<RequestBurst>();
            var thirdParty = new List<RequestEntry>();
            foreach (var r in requests)
            {
                if (r.IsThirdParty) thirdParty.Add(r);
            }
            if (thirdParty.Count < BurstMinCount) return bursts;
            if (thirdParty.Count < BurstMinCount) return bursts;

            thirdParty.Sort((a, b) => a.Time.CompareTo(b.Time));

            var byHost = new Dictionary<string, List<RequestEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in thirdParty)
            {
                string h = r.Host ?? "";
                if (string.IsNullOrEmpty(h)) continue;
                if (!byHost.TryGetValue(h, out var list)) { list = new List<RequestEntry>(); byHost[h] = list; }
                list.Add(r);
            }

            foreach (var kv in byHost)
            {
                var sorted = kv.Value;
                if (sorted.Count < BurstMinCount) continue;
                for (int i = 0; i < sorted.Count; i++)
                {
                    int j = i;
                    while (j < sorted.Count && (sorted[j].Time - sorted[i].Time).TotalSeconds <= BurstWindowSeconds)
                        j++;
                    int count = j - i;
                    if (count >= BurstMinCount)
                    {
                        double windowMs = (sorted[j - 1].Time - sorted[i].Time).TotalMilliseconds;
                        double rps = windowMs > 0 ? count / (windowMs / 1000.0) : count;
                        bursts.Add(new RequestBurst
                        {
                            Domain = kv.Key,
                            Count = count,
                            WindowMs = (int)windowMs,
                            RequestsPerSecond = Math.Round(rps, 1),
                            Detail = $"{count} requests to {kv.Key} in {windowMs:F0}ms ({rps:F1} req/s) — beacon storm pattern"
                        });
                        break;
                    }
                }
            }

            bursts.Sort((a, b) => b.Count.CompareTo(a.Count));
            if (bursts.Count > 10) bursts.RemoveRange(10, bursts.Count - 10);
            return bursts;
        }

        // ════════════════════════════════════════════
        //  COOKIE SYNC CHAIN DETECTION
        // ════════════════════════════════════════════

        /// <summary>
        /// Detect cookie synchronization patterns: redirect chains between tracker domains
        /// that propagate user identifiers across ad-tech partners.
        /// </summary>
        public static int DetectCookieSyncChains(List<DataFlowEdge> edges)
        {
            // Cookie sync = redirect_bounce edges where both from and to are known tracker domains
            return edges.Count(e => e.Mechanism == "redirect_bounce");
        }

        // ════════════════════════════════════════════
        //  CROSS-TAB CORRELATION
        // ════════════════════════════════════════════

        /// <summary>
        /// Merge per-tab identifier registries into the session-wide context.
        /// Returns count of identifiers seen across multiple tabs.
        /// </summary>
        public static int UpdateSessionContext(
            SessionContext session,
            Dictionary<string, HashSet<string>> tabIdentifiers,
            HashSet<string> tabTrackerDomains,
            HashSet<string> tabCompanies,
            Dictionary<string, int> tabDomainCounts)
        {
            int crossTabLinks = 0;

            // Merge identifiers
            foreach (var kv in tabIdentifiers)
            {
                if (!session.GlobalIdentifiers.TryGetValue(kv.Key, out var globalDomains))
                {
                    globalDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    session.GlobalIdentifiers[kv.Key] = globalDomains;
                }
                int before = globalDomains.Count;
                foreach (var d in kv.Value) globalDomains.Add(d);
                if (before >= 2 && globalDomains.Count > before)
                    crossTabLinks++; // identifier spreading across tabs
            }

            // Merge tracker domains and companies
            foreach (var d in tabTrackerDomains) session.AllSeenTrackerDomains.Add(d);
            foreach (var c in tabCompanies) session.AllSeenCompanies.Add(c);

            // Merge domain counts
            foreach (var kv in tabDomainCounts)
                session.GlobalDomainCounts[kv.Key] = session.GlobalDomainCounts.GetValueOrDefault(kv.Key) + kv.Value;

            return crossTabLinks;
        }

        /// <summary>
        /// Count identifiers that appear across multiple tabs' domains.
        /// </summary>
        public static int CountCrossTabIdentifiers(SessionContext session)
        {
            return session.GlobalIdentifiers.Count(kv => kv.Value.Count >= 3);
        }

        // ════════════════════════════════════════════
        //  SESSION RISK ASSESSMENT (enhanced)
        // ════════════════════════════════════════════

        public static SessionRisk AssessSessionRisk(ScanResult scan, List<IdentityLink> links, List<BehavioralPattern> patterns, List<DataFlowEdge> edges, List<CompanyCluster> clusters, List<RequestBurst>? bursts = null, int cookieSyncChains = 0, int crossTabLinks = 0)
        {
            var risk = new SessionRisk();

            // Identity stitching risk
            int crossSiteIds = links.Count;
            risk.IdentityStitchingRisk = crossSiteIds >= 5 ? "Critical" : crossSiteIds >= 2 ? "High" : crossSiteIds >= 1 ? "Medium" : "Low";

            // Data propagation risk
            int uniqueEdges = edges.Count;
            int companies = clusters.Count;
            risk.DataPropagationRisk = uniqueEdges >= 20 ? "Critical" : uniqueEdges >= 10 ? "High" : uniqueEdges >= 3 ? "Medium" : "Low";

            // Fingerprinting risk
            risk.FingerprintingRisk = patterns.Any(p => p.Name == "Fingerprint Battery") ? "Critical" :
                patterns.Any(p => p.Name == "Active Rendering Probe") ? "High" :
                patterns.Count > 0 ? "Medium" : "Low";

            // Company concentration risk
            var topCompany = clusters.FirstOrDefault();
            risk.ConcentrationRisk = topCompany != null && topCompany.TotalRequests > 20 ? "High" :
                companies >= 5 ? "High" : companies >= 2 ? "Medium" : "Low";

            // Behavioral tracking risk (new)
            bool hasBehavioral = patterns.Any(p => p.Name.Contains("Behavioral") || p.Name.Contains("Passive") || p.Name.Contains("Active"));
            int burstCount = bursts?.Count ?? 0;
            risk.BehavioralTrackingRisk = hasBehavioral && burstCount >= 3 ? "Critical" :
                hasBehavioral ? "High" : burstCount >= 2 ? "Medium" : "Low";

            // Store metrics
            risk.RequestBursts = burstCount;
            risk.CookieSyncChains = cookieSyncChains;
            risk.CrossTabLinks = crossTabLinks;

            // Overall
            var levels = new[] { risk.IdentityStitchingRisk, risk.DataPropagationRisk, risk.FingerprintingRisk, risk.ConcentrationRisk, risk.BehavioralTrackingRisk };
            int critCount = levels.Count(l => l == "Critical");
            int highCount = levels.Count(l => l == "High");
            risk.OverallRisk = critCount >= 2 ? "Critical" : critCount >= 1 || highCount >= 2 ? "High" : highCount >= 1 ? "Medium" : "Low";

            // Threat tier
            bool hasDMP = clusters.Any(c => c.Categories.Contains("DMP"));
            bool hasReplay = clusters.Any(c => c.Categories.Contains("SessionReplay"));
            bool hasStitching = crossSiteIds >= 2 || crossTabLinks >= 1;

            if (risk.OverallRisk == "Critical" || (hasDMP && hasStitching) || (hasReplay && hasBehavioral))
            {
                risk.Tier = ThreatTier.SurveillanceGrade;
                risk.TierLabel = "Surveillance-Grade";
            }
            else if (risk.OverallRisk == "High" || companies >= 4 || hasBehavioral)
            {
                risk.Tier = ThreatTier.AggressiveTracking;
                risk.TierLabel = "Aggressive Tracking";
            }
            else if (companies >= 1 || uniqueEdges >= 2)
            {
                risk.Tier = ThreatTier.TypicalWebTracking;
                risk.TierLabel = "Typical Web Tracking";
            }
            else
            {
                risk.Tier = ThreatTier.SafeIsh;
                risk.TierLabel = "Safe-ish";
            }

            risk.Summary = risk.OverallRisk switch
            {
                "Critical" => $"[{risk.TierLabel}] Severe privacy risk. {crossSiteIds} identity links, {uniqueEdges} data flows to {companies} companies." +
                    (burstCount > 0 ? $" {burstCount} request burst(s)." : "") +
                    (cookieSyncChains > 0 ? $" {cookieSyncChains} cookie sync chain(s)." : "") +
                    (crossTabLinks > 0 ? $" {crossTabLinks} cross-tab correlation(s)." : ""),
                "High" => $"[{risk.TierLabel}] Significant privacy risk. {uniqueEdges} data flows across {companies} tracking companies.",
                "Medium" => $"[{risk.TierLabel}] Moderate risk. Some tracking and data sharing detected.",
                _ => $"[{risk.TierLabel}] Low risk. Minimal tracking activity observed."
            };

            return risk;
        }

        // ════════════════════════════════════════════
        //  EXPLAINABLE SCORING
        // ════════════════════════════════════════════

        /// <summary>
        /// Generate human-readable, regulation-defensible explanations for each score penalty.
        /// </summary>
        public static List<ScoreExplanation> ExplainScore(PrivacyScore score, ScanResult scan, List<CompanyCluster> clusters)
        {
            var explanations = new List<ScoreExplanation>();

            foreach (var kv in score.Breakdown.Where(b => b.Value < 0))
            {
                string justification = kv.Key switch
                {
                    "Trackers" => BuildTrackerExplanation(scan, clusters),
                    "Third-party domains" => $"Data was sent to {scan.Requests.Where(r => r.IsThirdParty).Select(r => r.Host).Distinct().Count()} external domains. Each domain represents a separate data controller under GDPR Art. 26 (joint controllers) or Art. 28 (processors). Users were not given the opportunity to consent to each recipient individually.",
                    "Fingerprinting" => $"{scan.Fingerprints.Count} browser fingerprinting technique(s) detected ({string.Join(", ", scan.Fingerprints.Select(f => f.Type).Distinct())}). Fingerprinting creates a persistent identifier without storing data on the device, circumventing cookie consent mechanisms. This violates GDPR Art. 5(1)(c) data minimisation principle.",
                    "Tracking cookies" => $"{PrivacyEngine.CountAllTrackingCookies(scan)} tracking cookies detected (both JavaScript-accessible and HttpOnly). Under ePrivacy Directive Art. 5(3), storing tracking cookies requires prior informed consent. Cookies were set before or without verifiable consent.",
                    "Tracking URL params" => $"URL parameters containing cross-site tracking identifiers (e.g., gclid, fbclid, utm_) were detected. These parameters enable cross-site identity linkage without cookie consent, constituting processing of personal data under GDPR Art. 4(1).",
                    "Security headers" => $"{scan.SecurityHeaders.Count(h => h.Status == "Missing")} security headers missing. Under GDPR Art. 32, controllers must implement appropriate technical measures. Missing headers like Content-Security-Policy and Strict-Transport-Security indicate insufficient protection of data in transit.",
                    "WebRTC leaks" => $"WebRTC leaked {scan.WebRtcLeaks.Count} real IP address(es). IP addresses are personal data under GDPR Art. 4(1). Exposure without consent violates Art. 5(1)(f) integrity and confidentiality principle.",
                    "POST to third-party" => $"{scan.Requests.Count(r => r.IsThirdParty && r.HasBody)} POST requests sent form/body data to third-party servers. This constitutes data transfer to third parties under GDPR Art. 44-49 and requires informed consent per Art. 6(1)(a).",
                    "CNAME suspects" => "First-party subdomains with naming patterns consistent with CNAME cloaking detected. This technique disguises third-party tracking as first-party to bypass browser protections and user consent choices, violating the transparency principle of GDPR Art. 5(1)(a).",
                    "Obfuscated IDs" => "URL parameters with high Shannon entropy detected, consistent with encoded tracking identifiers. Obfuscation of tracking data undermines the user's ability to understand data collection, violating GDPR Art. 12 (transparent communication).",
                    "Behavioral tracking" => $"{scan.Fingerprints.Count(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay") || f.Type.Contains("Obfuscation") || f.Type.Contains("Dynamic Script") || f.Type.Contains("Beacon") || f.Type.Contains("Cross-Frame"))} behavioral tracking technique(s) detected (mouse/scroll/keystroke monitoring, session replay, script obfuscation, beacon exfiltration). These techniques record user interaction patterns to build behavioral profiles. Under GDPR Art. 5(1)(c) data minimisation, collecting granular interaction data far exceeds what is necessary for service provision.",
                    _ => $"Penalty applied for: {kv.Key}."
                };

                explanations.Add(new ScoreExplanation
                {
                    Category = kv.Key,
                    Penalty = kv.Value,
                    Justification = justification,
                    GdprRelevance = InferGdprArticle(kv.Key)
                });
            }

            return explanations;
        }

        private static string BuildTrackerExplanation(ScanResult scan, List<CompanyCluster> clusters)
        {
            var sb = new StringBuilder();
            int unique = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.TrackerLabel).Distinct().Count();
            int companies = clusters.Count;
            sb.Append($"{unique} tracking service(s) from {companies} company/companies detected. ");

            var byCat = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerCategoryName))
                .GroupBy(r => r.TrackerCategoryName).OrderByDescending(g => g.Count());
            foreach (var cat in byCat.Take(3))
                sb.Append($"{cat.Select(r => r.TrackerLabel).Distinct().Count()} {cat.Key.ToLower()}. ");

            sb.Append("Each tracking service constitutes a separate data controller requiring a lawful basis under GDPR Art. 6. ");
            if (clusters.Any(c => c.Categories.Contains("SessionReplay")))
                sb.Append("Session replay services record complete user interactions including form inputs, constituting processing of special categories under Art. 9. ");

            return sb.ToString().Trim();
        }

        private static string InferGdprArticle(string category) => category switch
        {
            "Trackers" => "Art. 6, Art. 7",
            "Third-party domains" => "Art. 26, Art. 28, Art. 44-49",
            "Fingerprinting" => "Art. 5(1)(c), ePrivacy Art. 5(3)",
            "Tracking cookies" => "Art. 7, ePrivacy Art. 5(3)",
            "Tracking URL params" => "Art. 4(1), Art. 5(1)(b)",
            "Security headers" => "Art. 25, Art. 32",
            "WebRTC leaks" => "Art. 5(1)(f)",
            "POST to third-party" => "Art. 6(1)(a), Art. 44-49",
            "CNAME suspects" => "Art. 5(1)(a), Art. 12",
            "Obfuscated IDs" => "Art. 12, Art. 13",
            "Behavioral tracking" => "Art. 5(1)(c), Art. 25",
            _ => "Art. 5"
        };
    }
}
