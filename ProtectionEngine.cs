using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrivacyMonitor
{
    /// <summary>
    /// Active protection engine: request blocking, per-site profiles, adaptive tracker learning,
    /// and anti-fingerprinting script generation.
    /// </summary>
    public static class ProtectionEngine
    {
        // ════════════════════════════════════════════
        //  PROFILES: Per-site privacy settings
        // ════════════════════════════════════════════

        private static readonly ConcurrentDictionary<string, SiteProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string ProfilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "site-profiles.json");

        private static readonly ConcurrentDictionary<string, BlocklistEntry> _staticBlocklist = new(StringComparer.OrdinalIgnoreCase);
        private static string[] _blocklistSuffixes = Array.Empty<string>();
        private static readonly string BlocklistPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "blocklist.json");

        /// <summary>Default mode applied when no per-site profile exists.</summary>
        public static ProtectionMode GlobalDefaultMode { get; set; } = ProtectionMode.BlockKnown;

        static ProtectionEngine()
        {
            LoadProfiles();
            LoadLearnedTrackers();
            LoadBlocklist();
        }

        public static SiteProfile GetProfile(string host)
        {
            return GetProfile(host, null);
        }

        /// <param name="ephemeral">When non-null (private window), use this store and never persist.</param>
        public static SiteProfile GetProfile(string host, ConcurrentDictionary<string, SiteProfile>? ephemeral)
        {
            if (string.IsNullOrEmpty(host)) return new SiteProfile { Mode = GlobalDefaultMode };
            var store = ephemeral ?? _profiles;
            if (store.TryGetValue(host, out var p)) return p;
            string parent = GetParentDomain(host);
            if (!string.IsNullOrEmpty(parent) && store.TryGetValue(parent, out var pp)) return pp;
            return new SiteProfile { Mode = GlobalDefaultMode };
        }

        public static void SetProfile(string host, SiteProfile profile)
        {
            SetProfile(host, profile, null);
        }

        public static void SetProfile(string host, SiteProfile profile, ConcurrentDictionary<string, SiteProfile>? ephemeral)
        {
            var store = ephemeral ?? _profiles;
            store[host] = profile;
            if (ephemeral == null) SaveProfiles();
        }

        public static ProtectionMode GetEffectiveMode(string host)
        {
            return GetProfile(host).Mode;
        }

        public static ProtectionMode GetEffectiveMode(string host, ConcurrentDictionary<string, SiteProfile>? ephemeral)
        {
            return GetProfile(host, ephemeral).Mode;
        }

        public static void SetMode(string host, ProtectionMode mode)
        {
            SetMode(host, mode, null);
        }

        public static void SetMode(string host, ProtectionMode mode, ConcurrentDictionary<string, SiteProfile>? ephemeral)
        {
            var p = GetProfile(host, ephemeral);
            p.Mode = mode;
            var store = ephemeral ?? _profiles;
            store[host] = p;
            if (ephemeral == null) SaveProfiles();
        }

        /// <summary>
        /// Notify the global tracker intelligence system that a likely tracker was seen on this page.
        /// This is lightweight and safe to call from hot blocking paths.
        /// </summary>
        private static void ObserveTrackerAppearance(string trackerHost, string pageHost)
        {
            if (string.IsNullOrWhiteSpace(trackerHost) || string.IsNullOrWhiteSpace(pageHost))
                return;
            try
            {
                TrackerIntelligence.RecordObservation(trackerHost, pageHost);
            }
            catch
            {
                // Never let analytics impact blocking decisions.
            }
        }

        // ════════════════════════════════════════════
        //  BLOCKING DECISION
        // ════════════════════════════════════════════

        /// <summary>
        /// Evaluate whether a request should be blocked.
        /// Called from WebResourceRequested (UI thread or IO thread).
        /// Must be fast and thread-safe.
        /// </summary>
        public static BlockDecision ShouldBlock(RequestEntry entry, string pageHost, SiteProfile profile)
        {
            var mode = profile.Mode;
            bool isMedia = IsMediaContext(entry.ResourceContext);

            // Monitor mode never blocks
            if (mode == ProtectionMode.Monitor)
                return new BlockDecision { Blocked = false, Reason = "Monitor mode" };

            // First-party requests are never blocked
            if (!entry.IsThirdParty)
                return new BlockDecision { Blocked = false, Reason = "First-party" };

            bool blockAdsTrackers = profile.BlockAdsTrackers;
            bool blockBehavioral = profile.BlockBehavioral;

            if (!blockAdsTrackers && !blockBehavioral)
                return new BlockDecision { Blocked = false, Reason = "Blocking disabled for this site" };

            // 1) Static blocklist layer
            if (blockAdsTrackers && TryGetBlocklistEntry(entry.Host, out var bl))
            {
                ObserveTrackerAppearance(entry.Host, pageHost);
                return new BlockDecision
                {
                    Blocked = true,
                    Reason = $"Blocklist: {bl.Label}",
                    Category = bl.Category,
                    Confidence = bl.Confidence,
                    TrackerLabel = bl.Label
                };
            }

            // 2) Learned trackers
            if (blockAdsTrackers && IsLearnedTracker(entry.Host))
            {
                ObserveTrackerAppearance(entry.Host, pageHost);
                if (isMedia)
                    return new BlockDecision { Blocked = false, Reason = "Media request" };
                return new BlockDecision
                {
                    Blocked = true,
                    Reason = $"Learned tracker: {entry.Host}",
                    Category = "Tracking",
                    Confidence = 0.80,
                    TrackerLabel = "Learned Tracker"
                };
            }

            // 3) Known tracker detection (confidence-weighted) — stronger: lower thresholds block more
            double trackerConfidence = MaxSignalConfidence(entry.Signals, "known_tracker", "heuristic_tracker");
            if (blockAdsTrackers && !string.IsNullOrEmpty(entry.TrackerLabel))
            {
                if (mode == ProtectionMode.BlockKnown && trackerConfidence >= 0.28)
                {
                    ObserveTrackerAppearance(entry.Host, pageHost);
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = $"Confirmed tracker: {entry.TrackerLabel}",
                        Category = MapTrackerCategory(entry.TrackerCategoryName),
                        Confidence = trackerConfidence,
                        TrackerLabel = entry.TrackerLabel
                    };
                }

                if (mode == ProtectionMode.Aggressive && trackerConfidence >= 0.18)
                {
                    ObserveTrackerAppearance(entry.Host, pageHost);
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = $"Likely tracker: {entry.TrackerLabel}",
                        Category = MapTrackerCategory(entry.TrackerCategoryName),
                        Confidence = trackerConfidence,
                        TrackerLabel = entry.TrackerLabel
                    };
                }
            }

            // 4) Behavioral blocking (JS injection / session replay signatures) — stronger threshold
            if (blockBehavioral)
            {
                var behavioral = entry.Signals.FirstOrDefault(s =>
                    s.SignalType == "js_injection" ||
                    s.SignalType == "session_replay" ||
                    s.SignalType == "behavioral_tracking");
                if (behavioral != null && behavioral.Confidence >= 0.55)
                {
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = $"Behavioral: {behavioral.SignalType}",
                        Category = "Behavioral",
                        Confidence = behavioral.Confidence,
                        TrackerLabel = "Behavioral"
                    };
                }
            }

            // 5) Aggressive heuristic layer (includes third_party_script + etag_tracking)
            if (mode == ProtectionMode.Aggressive && blockAdsTrackers)
            {
                if (isMedia)
                    return new BlockDecision { Blocked = false, Reason = "Media request" };
                var heuristic = entry.Signals
                    .Where(s => s.SignalType == "high_entropy_param" ||
                                s.SignalType == "pixel_tracking" ||
                                s.SignalType == "obfuscated_payload" ||
                                s.SignalType == "redirect_bounce" ||
                                s.SignalType == "cookie_sync" ||
                                s.SignalType == "third_party_script" ||
                                s.SignalType == "etag_tracking")
                    .OrderByDescending(s => s.Confidence)
                    .FirstOrDefault();

                if (heuristic != null && heuristic.Confidence >= 0.28)
                {
                    ObserveTrackerAppearance(entry.Host, pageHost);
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = $"Heuristic: {heuristic.SignalType}",
                        Category = heuristic.SignalType == "js_injection" ? "Behavioral" : "Tracking",
                        Confidence = heuristic.Confidence,
                        TrackerLabel = "Heuristic Detection"
                    };
                }
            }

            // 5b) BlockKnown: also block strong ad-like domains; Aggressive: block all ad-like
            if (blockAdsTrackers && !isMedia && IsAdLikeDomain(entry.Host))
            {
                if (mode == ProtectionMode.Aggressive)
                {
                    ObserveTrackerAppearance(entry.Host, pageHost);
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = "Ad-like domain name",
                        Category = "Tracking",
                        Confidence = 0.65,
                        TrackerLabel = "Domain heuristic"
                    };
                }
                if (mode == ProtectionMode.BlockKnown && IsStrongAdLikeDomain(entry.Host))
                {
                    ObserveTrackerAppearance(entry.Host, pageHost);
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = "Ad/tracker domain name",
                        Category = "Tracking",
                        Confidence = 0.70,
                        TrackerLabel = "Domain heuristic"
                    };
                }
            }

            // 6) Aggressive path-based blocking: third-party URLs with ad/tracker path patterns
            if (mode == ProtectionMode.Aggressive && blockAdsTrackers && !isMedia)
            {
                if (IsAdOrTrackerPath(entry.Path) || IsAdOrTrackerPath(entry.FullUrl))
                {
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = "Ad/tracker path pattern",
                        Category = "Tracking",
                        Confidence = 0.75,
                        TrackerLabel = "Path heuristic"
                    };
                }
            }

            return new BlockDecision { Blocked = false, Reason = "Allowed" };
        }

        // ════════════════════════════════════════════
        //  ADAPTIVE LEARNING
        // ════════════════════════════════════════════

        private static readonly ConcurrentDictionary<string, LearnedTrackerEntry> _learnedTrackers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string LearnedTrackersPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "learned-trackers.json");

        /// <summary>
        /// Record an observation of a potential tracker domain.
        /// When enough evidence accumulates (cross-site appearances, high-entropy params,
        /// cookie sync behavior), the domain is promoted to "learned tracker."
        /// </summary>
        public static void ObserveTrackerSignal(string host, string signalType, double confidence)
        {
            if (string.IsNullOrEmpty(host)) return;

            var entry = _learnedTrackers.GetOrAdd(host, _ => new LearnedTrackerEntry { Domain = host });
            bool promoted = false;
            lock (entry.Sync)
            {
                entry.ObservationCount++;
                entry.MaxConfidence = Math.Max(entry.MaxConfidence, confidence);
                entry.LastSeen = DateTime.UtcNow;
                if (!entry.SignalTypes.Contains(signalType))
                    entry.SignalTypes.Add(signalType);

                // Promote to learned tracker when: 3+ tracker signals (faster learning for stronger protection)
                if (!entry.IsConfirmedTracker && entry.ObservationCount >= 3)
                {
                    entry.IsConfirmedTracker = true;
                    entry.ConfirmedAt = DateTime.UtcNow;
                    promoted = true;
                }
            }
            if (promoted) SaveLearnedTrackers();
        }

        /// <summary>Record that a domain has been seen on a specific site.</summary>
        public static void ObserveCrossSiteAppearance(string trackerHost, string siteHost)
        {
            if (string.IsNullOrEmpty(trackerHost) || string.IsNullOrEmpty(siteHost)) return;
            var entry = _learnedTrackers.GetOrAdd(trackerHost, _ => new LearnedTrackerEntry { Domain = trackerHost });
            bool promoted = false;
            lock (entry.Sync)
            {
                    if (!entry.SeenOnSites.Contains(siteHost))
                    {
                        entry.SeenOnSites.Add(siteHost);
                        // If appears on 2+ different sites, strong cross-site signal
                        if (entry.SeenOnSites.Count >= 2 && !entry.IsConfirmedTracker)
                    {
                        entry.IsConfirmedTracker = true;
                        entry.ConfirmedAt = DateTime.UtcNow;
                        promoted = true;
                    }
                }
            }
            if (promoted) SaveLearnedTrackers();
        }

        public static bool IsLearnedTracker(string host)
        {
            if (_learnedTrackers.TryGetValue(host, out var entry))
                return entry.IsConfirmedTracker;
            // Check parent domain
            string parent = GetParentDomain(host);
            if (!string.IsNullOrEmpty(parent) && _learnedTrackers.TryGetValue(parent, out var pe))
                return pe.IsConfirmedTracker;
            return false;
        }

        public static int LearnedTrackerCount => _learnedTrackers.Count(kv => kv.Value.IsConfirmedTracker);

        // ════════════════════════════════════════════
        //  STATIC BLOCKLIST
        // ════════════════════════════════════════════

        public static IEnumerable<string> GetBlockerSeedHosts()
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in _staticBlocklist.Keys) hosts.Add(h);
            return hosts;
        }

        public static bool TryGetBlocklistEntry(string host, out BlocklistEntry entry)
        {
            entry = new BlocklistEntry();
            if (string.IsNullOrEmpty(host) || _staticBlocklist.Count == 0) return false;
            if (_staticBlocklist.TryGetValue(host, out var direct) && direct != null)
            {
                entry = direct;
                return true;
            }
            foreach (var suffix in _blocklistSuffixes)
            {
                if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (_staticBlocklist.TryGetValue(suffix, out var matched) && matched != null)
                    {
                        entry = matched;
                        return true;
                    }
                }
            }
            return false;
        }

        public static void LoadBlocklist()
        {
            try
            {
                var dir = Path.GetDirectoryName(BlocklistPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(BlocklistPath))
                {
                    var defaults = GetDefaultBlocklistEntries();
                    SaveBlocklist(defaults);
                }

                var json = File.ReadAllText(BlocklistPath);
                var file = JsonSerializer.Deserialize<BlocklistFile>(json);
                var entries = file?.Entries ?? JsonSerializer.Deserialize<List<BlocklistEntry>>(json) ?? new List<BlocklistEntry>();

                _staticBlocklist.Clear();
                foreach (var e in entries.Where(e => !string.IsNullOrWhiteSpace(e.Domain)))
                {
                    var domain = e.Domain.Trim().ToLowerInvariant();
                    _staticBlocklist[domain] = new BlocklistEntry
                    {
                        Domain = domain,
                        Label = string.IsNullOrWhiteSpace(e.Label) ? domain : e.Label,
                        Category = NormalizeCategory(e.Category),
                        Confidence = e.Confidence <= 0 ? 0.95 : Math.Clamp(e.Confidence, 0.5, 1.0)
                    };
                }
                // Merge in any default entries not yet in list (10x stronger: existing users get new domains)
                var defaultEntries = GetDefaultBlocklistEntries();
                bool merged = false;
                foreach (var e in defaultEntries)
                {
                    var domain = e.Domain.Trim().ToLowerInvariant();
                    if (!_staticBlocklist.ContainsKey(domain))
                    {
                        _staticBlocklist[domain] = new BlocklistEntry
                        {
                            Domain = domain,
                            Label = string.IsNullOrWhiteSpace(e.Label) ? domain : e.Label,
                            Category = NormalizeCategory(e.Category),
                            Confidence = e.Confidence <= 0 ? 0.95 : Math.Clamp(e.Confidence, 0.5, 1.0)
                        };
                        merged = true;
                    }
                }
                if (merged)
                    SaveBlocklist(_staticBlocklist.Select(kv => new BlocklistEntry { Domain = kv.Value.Domain, Label = kv.Value.Label, Category = kv.Value.Category, Confidence = kv.Value.Confidence }).ToList());

                _blocklistSuffixes = _staticBlocklist.Keys.OrderByDescending(k => k.Length).ToArray();
            }
            catch
            {
                _staticBlocklist.Clear();
                _blocklistSuffixes = Array.Empty<string>();
            }
        }

        private static void SaveBlocklist(List<BlocklistEntry> entries)
        {
            try
            {
                var file = new BlocklistFile { UpdatedAt = DateTime.UtcNow.ToString("o"), Entries = entries };
                File.WriteAllText(BlocklistPath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static List<BlocklistEntry> GetDefaultBlocklistEntries()
        {
            var list = new List<BlocklistEntry>
            {
            // Google / Alphabet
            new BlocklistEntry { Domain = "doubleclick.net", Label = "Google DoubleClick", Category = "Ad" },
            new BlocklistEntry { Domain = "googlesyndication.com", Label = "Google AdSense", Category = "Ad" },
            new BlocklistEntry { Domain = "pagead2.googlesyndication.com", Label = "Google Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "googleadservices.com", Label = "Google Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "adservice.google.com", Label = "Google Ad Service", Category = "Ad" },
            new BlocklistEntry { Domain = "google-analytics.com", Label = "Google Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "googletagmanager.com", Label = "Google Tag Manager", Category = "Tracking" },
            new BlocklistEntry { Domain = "googletagservices.com", Label = "Google Tag Services", Category = "Tracking" },
            new BlocklistEntry { Domain = "googleoptimize.com", Label = "Google Optimize", Category = "Tracking" },
            new BlocklistEntry { Domain = "app-measurement.com", Label = "Firebase Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "firebaseinstallations.googleapis.com", Label = "Firebase", Category = "Tracking" },
            new BlocklistEntry { Domain = "firebaselogging.googleapis.com", Label = "Firebase Logging", Category = "Tracking" },
            // Meta / Facebook
            new BlocklistEntry { Domain = "facebook.net", Label = "Facebook SDK", Category = "Tracking" },
            new BlocklistEntry { Domain = "connect.facebook.net", Label = "Facebook Connect", Category = "Tracking" },
            new BlocklistEntry { Domain = "pixel.facebook.com", Label = "Facebook Pixel", Category = "Ad" },
            new BlocklistEntry { Domain = "an.facebook.com", Label = "Facebook Audience Network", Category = "Ad" },
            new BlocklistEntry { Domain = "graph.facebook.com", Label = "Facebook Graph API", Category = "Tracking" },
            // Microsoft
            new BlocklistEntry { Domain = "clarity.ms", Label = "Microsoft Clarity", Category = "Tracking" },
            new BlocklistEntry { Domain = "bat.bing.com", Label = "Bing UET", Category = "Ad" },
            new BlocklistEntry { Domain = "c.bing.com", Label = "Bing Tracking", Category = "Tracking" },
            // Twitter / X, TikTok, LinkedIn
            new BlocklistEntry { Domain = "ads-twitter.com", Label = "Twitter Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "static.ads-twitter.com", Label = "Twitter Ads SDK", Category = "Ad" },
            new BlocklistEntry { Domain = "analytics.tiktok.com", Label = "TikTok Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "analytics-sg.tiktok.com", Label = "TikTok Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "mon.tiktokv.com", Label = "TikTok Monitoring", Category = "Tracking" },
            new BlocklistEntry { Domain = "ads.linkedin.com", Label = "LinkedIn Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "px.ads.linkedin.com", Label = "LinkedIn Conversion", Category = "Ad" },
            new BlocklistEntry { Domain = "snap.licdn.com", Label = "LinkedIn Insight", Category = "Tracking" },
            // Session replay / heatmaps
            new BlocklistEntry { Domain = "hotjar.com", Label = "Hotjar", Category = "Tracking" },
            new BlocklistEntry { Domain = "hotjar.io", Label = "Hotjar", Category = "Tracking" },
            new BlocklistEntry { Domain = "fullstory.com", Label = "FullStory", Category = "Tracking" },
            new BlocklistEntry { Domain = "rs.fullstory.com", Label = "FullStory", Category = "Tracking" },
            new BlocklistEntry { Domain = "mouseflow.com", Label = "Mouseflow", Category = "Tracking" },
            new BlocklistEntry { Domain = "crazyegg.com", Label = "Crazy Egg", Category = "Tracking" },
            new BlocklistEntry { Domain = "inspectlet.com", Label = "Inspectlet", Category = "Tracking" },
            new BlocklistEntry { Domain = "logrocket.io", Label = "LogRocket", Category = "Tracking" },
            new BlocklistEntry { Domain = "smartlook.com", Label = "Smartlook", Category = "Tracking" },
            new BlocklistEntry { Domain = "luckyorange.com", Label = "Lucky Orange", Category = "Tracking" },
            new BlocklistEntry { Domain = "luckyorange.net", Label = "Lucky Orange", Category = "Tracking" },
            // Analytics
            new BlocklistEntry { Domain = "segment.io", Label = "Segment", Category = "Tracking" },
            new BlocklistEntry { Domain = "segment.com", Label = "Segment", Category = "Tracking" },
            new BlocklistEntry { Domain = "api.segment.io", Label = "Segment", Category = "Tracking" },
            new BlocklistEntry { Domain = "mixpanel.com", Label = "Mixpanel", Category = "Tracking" },
            new BlocklistEntry { Domain = "api.mixpanel.com", Label = "Mixpanel", Category = "Tracking" },
            new BlocklistEntry { Domain = "amplitude.com", Label = "Amplitude", Category = "Tracking" },
            new BlocklistEntry { Domain = "api.amplitude.com", Label = "Amplitude", Category = "Tracking" },
            new BlocklistEntry { Domain = "heapanalytics.com", Label = "Heap", Category = "Tracking" },
            new BlocklistEntry { Domain = "chartbeat.com", Label = "Chartbeat", Category = "Tracking" },
            new BlocklistEntry { Domain = "scorecardresearch.com", Label = "comScore", Category = "Tracking" },
            new BlocklistEntry { Domain = "quantserve.com", Label = "Quantcast", Category = "Tracking" },
            new BlocklistEntry { Domain = "omtrdc.net", Label = "Adobe Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "demdex.net", Label = "Adobe Audience Manager", Category = "Tracking" },
            new BlocklistEntry { Domain = "2o7.net", Label = "Adobe Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "everesttech.net", Label = "Adobe Ad Cloud", Category = "Ad" },
            // Ad exchanges / SSPs / DSPs
            new BlocklistEntry { Domain = "criteo.com", Label = "Criteo", Category = "Ad" },
            new BlocklistEntry { Domain = "criteo.net", Label = "Criteo", Category = "Ad" },
            new BlocklistEntry { Domain = "adnxs.com", Label = "Xandr/AppNexus", Category = "Ad" },
            new BlocklistEntry { Domain = "ib.adnxs.com", Label = "AppNexus", Category = "Ad" },
            new BlocklistEntry { Domain = "rubiconproject.com", Label = "Rubicon/Magnite", Category = "Ad" },
            new BlocklistEntry { Domain = "pubmatic.com", Label = "PubMatic", Category = "Ad" },
            new BlocklistEntry { Domain = "ads.pubmatic.com", Label = "PubMatic", Category = "Ad" },
            new BlocklistEntry { Domain = "openx.net", Label = "OpenX", Category = "Ad" },
            new BlocklistEntry { Domain = "casalemedia.com", Label = "Index Exchange", Category = "Ad" },
            new BlocklistEntry { Domain = "indexexchange.com", Label = "Index Exchange", Category = "Ad" },
            new BlocklistEntry { Domain = "bidswitch.net", Label = "BidSwitch", Category = "Ad" },
            new BlocklistEntry { Domain = "taboola.com", Label = "Taboola", Category = "Ad" },
            new BlocklistEntry { Domain = "outbrain.com", Label = "Outbrain", Category = "Ad" },
            new BlocklistEntry { Domain = "outbrainimg.com", Label = "Outbrain", Category = "Ad" },
            new BlocklistEntry { Domain = "sharethrough.com", Label = "Sharethrough", Category = "Ad" },
            new BlocklistEntry { Domain = "33across.com", Label = "33Across", Category = "Ad" },
            new BlocklistEntry { Domain = "triplelift.com", Label = "TripleLift", Category = "Ad" },
            new BlocklistEntry { Domain = "yieldmo.com", Label = "Yieldmo", Category = "Ad" },
            new BlocklistEntry { Domain = "media.net", Label = "Media.net", Category = "Ad" },
            new BlocklistEntry { Domain = "sovrn.com", Label = "Sovrn", Category = "Ad" },
            new BlocklistEntry { Domain = "smartadserver.com", Label = "Smart AdServer", Category = "Ad" },
            new BlocklistEntry { Domain = "advertising.com", Label = "Verizon Advertising", Category = "Ad" },
            new BlocklistEntry { Domain = "adsrvr.org", Label = "The Trade Desk", Category = "Ad" },
            new BlocklistEntry { Domain = "mathtag.com", Label = "MediaMath", Category = "Ad" },
            new BlocklistEntry { Domain = "rfihub.com", Label = "Sizmek", Category = "Ad" },
            // DMPs / data
            new BlocklistEntry { Domain = "bluekai.com", Label = "Oracle BlueKai", Category = "Tracking" },
            new BlocklistEntry { Domain = "addthis.com", Label = "AddThis", Category = "Tracking" },
            new BlocklistEntry { Domain = "krxd.net", Label = "Salesforce Krux", Category = "Tracking" },
            new BlocklistEntry { Domain = "rlcdn.com", Label = "LiveRamp", Category = "Tracking" },
            new BlocklistEntry { Domain = "lotame.com", Label = "Lotame", Category = "Tracking" },
            new BlocklistEntry { Domain = "crwdcntrl.net", Label = "Lotame", Category = "Tracking" },
            new BlocklistEntry { Domain = "tapad.com", Label = "Tapad", Category = "Tracking" },
            new BlocklistEntry { Domain = "agkn.com", Label = "Neustar", Category = "Tracking" },
            // Ad verification / misc
            new BlocklistEntry { Domain = "moatads.com", Label = "Moat", Category = "Ad" },
            new BlocklistEntry { Domain = "doubleverify.com", Label = "DoubleVerify", Category = "Ad" },
            new BlocklistEntry { Domain = "adsafeprotected.com", Label = "IAS", Category = "Ad" },
            new BlocklistEntry { Domain = "flashtalking.com", Label = "Flashtalking", Category = "Ad" },
            new BlocklistEntry { Domain = "serving-sys.com", Label = "Sizmek", Category = "Ad" },
            // Amazon, Snap, Pinterest
            new BlocklistEntry { Domain = "amazon-adsystem.com", Label = "Amazon Advertising", Category = "Ad" },
            new BlocklistEntry { Domain = "aax.amazon-adsystem.com", Label = "Amazon AAX", Category = "Ad" },
            new BlocklistEntry { Domain = "sc-static.net", Label = "Snapchat", Category = "Tracking" },
            new BlocklistEntry { Domain = "tr.snapchat.com", Label = "Snapchat Tracking", Category = "Ad" },
            new BlocklistEntry { Domain = "ct.pinterest.com", Label = "Pinterest Tag", Category = "Ad" },
            new BlocklistEntry { Domain = "trk.pinterest.com", Label = "Pinterest Tracking", Category = "Ad" },
            // Attribution / mobile
            new BlocklistEntry { Domain = "appsflyer.com", Label = "AppsFlyer", Category = "Tracking" },
            new BlocklistEntry { Domain = "adjust.com", Label = "Adjust", Category = "Tracking" },
            new BlocklistEntry { Domain = "app.adjust.com", Label = "Adjust", Category = "Tracking" },
            new BlocklistEntry { Domain = "kochava.com", Label = "Kochava", Category = "Tracking" },
            new BlocklistEntry { Domain = "singular.net", Label = "Singular", Category = "Tracking" },
            // A/B testing
            new BlocklistEntry { Domain = "optimizely.com", Label = "Optimizely", Category = "Tracking" },
            new BlocklistEntry { Domain = "cdn.optimizely.com", Label = "Optimizely", Category = "Tracking" },
            new BlocklistEntry { Domain = "vwo.com", Label = "VWO", Category = "Tracking" },
            // Social / widgets
            new BlocklistEntry { Domain = "sharethis.com", Label = "ShareThis", Category = "Tracking" },
            new BlocklistEntry { Domain = "disqus.com", Label = "Disqus", Category = "Tracking" },
            new BlocklistEntry { Domain = "disquscdn.com", Label = "Disqus", Category = "Tracking" },
            // WordPress / Yandex
            new BlocklistEntry { Domain = "pixel.wp.com", Label = "WordPress Pixel", Category = "Tracking" },
            new BlocklistEntry { Domain = "stats.wp.com", Label = "WordPress Stats", Category = "Tracking" },
            new BlocklistEntry { Domain = "mc.yandex.ru", Label = "Yandex Metrica", Category = "Tracking" },
            // Marketing / chat
            new BlocklistEntry { Domain = "hs-analytics.net", Label = "HubSpot", Category = "Tracking" },
            new BlocklistEntry { Domain = "hs-scripts.com", Label = "HubSpot", Category = "Tracking" },
            new BlocklistEntry { Domain = "pardot.com", Label = "Pardot", Category = "Tracking" },
            new BlocklistEntry { Domain = "intercom.io", Label = "Intercom", Category = "Tracking" },
            new BlocklistEntry { Domain = "intercomcdn.com", Label = "Intercom", Category = "Tracking" },
            new BlocklistEntry { Domain = "drift.com", Label = "Drift", Category = "Tracking" },
            new BlocklistEntry { Domain = "tealiumiq.com", Label = "Tealium", Category = "Tracking" },
            new BlocklistEntry { Domain = "tags.tiqcdn.com", Label = "Tealium", Category = "Tracking" },
            // Affiliate
            new BlocklistEntry { Domain = "awin1.com", Label = "Awin", Category = "Ad" },
            new BlocklistEntry { Domain = "shareasale.com", Label = "ShareASale", Category = "Ad" },
            new BlocklistEntry { Domain = "dpbolvw.net", Label = "CJ Affiliate", Category = "Ad" },
            new BlocklistEntry { Domain = "impact.com", Label = "Impact", Category = "Ad" },
            // ─── 10x stronger: EasyList-style and major ad/tracking domains ───
            new BlocklistEntry { Domain = "2mdn.net", Label = "DoubleClick 2mdn", Category = "Ad" },
            new BlocklistEntry { Domain = "tpc.googlesyndication.com", Label = "Google Syndication", Category = "Ad" },
            new BlocklistEntry { Domain = "partner.googleadservices.com", Label = "Google Ad Services", Category = "Ad" },
            new BlocklistEntry { Domain = "www.googleadservices.com", Label = "Google Ad Services", Category = "Ad" },
            new BlocklistEntry { Domain = "static.doubleclick.net", Label = "DoubleClick Static", Category = "Ad" },
            new BlocklistEntry { Domain = "ad.doubleclick.net", Label = "DoubleClick Ad", Category = "Ad" },
            new BlocklistEntry { Domain = "securepubads.g.doubleclick.net", Label = "Google Pub Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "pagead.l.doubleclick.net", Label = "DoubleClick Pagead", Category = "Ad" },
            new BlocklistEntry { Domain = "pagead46.l.doubleclick.net", Label = "DoubleClick Pagead", Category = "Ad" },
            new BlocklistEntry { Domain = "cm.g.doubleclick.net", Label = "DoubleClick CM", Category = "Ad" },
            new BlocklistEntry { Domain = "static.googleadsserving.com", Label = "Google Ad Serving", Category = "Ad" },
            new BlocklistEntry { Domain = "video-ad-stats.googlesyndication.com", Label = "Google Video Ad Stats", Category = "Ad" },
            new BlocklistEntry { Domain = "adclick.g.doubleclick.net", Label = "DoubleClick Adclick", Category = "Ad" },
            new BlocklistEntry { Domain = "googleads.g.doubleclick.net", Label = "Google Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "ads-api.twitter.com", Label = "Twitter Ads API", Category = "Ad" },
            new BlocklistEntry { Domain = "analytics.twitter.com", Label = "Twitter Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "cdn.amplitude.com", Label = "Amplitude CDN", Category = "Tracking" },
            new BlocklistEntry { Domain = "adroll.com", Label = "AdRoll", Category = "Ad" },
            new BlocklistEntry { Domain = "d.adroll.com", Label = "AdRoll", Category = "Ad" },
            new BlocklistEntry { Domain = "adform.net", Label = "Adform", Category = "Ad" },
            new BlocklistEntry { Domain = "adform.com", Label = "Adform", Category = "Ad" },
            new BlocklistEntry { Domain = "adsymptotic.com", Label = "Adsymptotic", Category = "Ad" },
            new BlocklistEntry { Domain = "adgrx.com", Label = "AdGear", Category = "Ad" },
            new BlocklistEntry { Domain = "adhigh.net", Label = "AdHigh", Category = "Ad" },
            new BlocklistEntry { Domain = "adtech.com", Label = "AdTech", Category = "Ad" },
            new BlocklistEntry { Domain = "appnexus.com", Label = "AppNexus", Category = "Ad" },
            new BlocklistEntry { Domain = "atomx.com", Label = "Atomx", Category = "Ad" },
            new BlocklistEntry { Domain = "bidvertiser.com", Label = "Bidvertiser", Category = "Ad" },
            new BlocklistEntry { Domain = "braze.com", Label = "Braze", Category = "Tracking" },
            new BlocklistEntry { Domain = "bounceexchange.com", Label = "Bounce Exchange", Category = "Tracking" },
            new BlocklistEntry { Domain = "dotomi.com", Label = "Dotomi", Category = "Ad" },
            new BlocklistEntry { Domain = "exelator.com", Label = "eXelate", Category = "Tracking" },
            new BlocklistEntry { Domain = "eyeviewdigital.com", Label = "EyeView", Category = "Ad" },
            new BlocklistEntry { Domain = "fastclick.net", Label = "FastClick", Category = "Ad" },
            new BlocklistEntry { Domain = "fwmrm.net", Label = "FreeWheel", Category = "Ad" },
            new BlocklistEntry { Domain = "gigya.com", Label = "Gigya", Category = "Tracking" },
            new BlocklistEntry { Domain = "imrworldwide.com", Label = "Nielsen IMR", Category = "Tracking" },
            new BlocklistEntry { Domain = "insightexpressai.com", Label = "Insight Express", Category = "Ad" },
            new BlocklistEntry { Domain = "lijit.com", Label = "Sovrn Lijit", Category = "Ad" },
            new BlocklistEntry { Domain = "liveintent.com", Label = "LiveIntent", Category = "Ad" },
            new BlocklistEntry { Domain = "livere.com", Label = "LiveRe", Category = "Tracking" },
            new BlocklistEntry { Domain = "lkqd.net", Label = "Lkqd", Category = "Ad" },
            new BlocklistEntry { Domain = "loopme.com", Label = "LoopMe", Category = "Ad" },
            new BlocklistEntry { Domain = "mediamath.com", Label = "MediaMath", Category = "Ad" },
            new BlocklistEntry { Domain = "mgid.com", Label = "MGID", Category = "Ad" },
            new BlocklistEntry { Domain = "mookie1.com", Label = "Mookie", Category = "Ad" },
            new BlocklistEntry { Domain = "nexage.com", Label = "Nexage", Category = "Ad" },
            new BlocklistEntry { Domain = "perfectaudience.com", Label = "Perfect Audience", Category = "Ad" },
            new BlocklistEntry { Domain = "pulsead.ai", Label = "PulseAd", Category = "Ad" },
            new BlocklistEntry { Domain = "reachlocal.com", Label = "ReachLocal", Category = "Ad" },
            new BlocklistEntry { Domain = "revcontent.com", Label = "Revcontent", Category = "Ad" },
            new BlocklistEntry { Domain = "richaudience.com", Label = "Rich Audience", Category = "Ad" },
            new BlocklistEntry { Domain = "simpli.fi", Label = "Simpli.fi", Category = "Ad" },
            new BlocklistEntry { Domain = "sonobi.com", Label = "Sonobi", Category = "Ad" },
            new BlocklistEntry { Domain = "spotxchange.com", Label = "SpotX", Category = "Ad" },
            new BlocklistEntry { Domain = "spotx.tv", Label = "SpotX", Category = "Ad" },
            new BlocklistEntry { Domain = "stackadapt.com", Label = "StackAdapt", Category = "Ad" },
            new BlocklistEntry { Domain = "teads.tv", Label = "Teads", Category = "Ad" },
            new BlocklistEntry { Domain = "theadhost.com", Label = "The Ad Host", Category = "Ad" },
            new BlocklistEntry { Domain = "tribalfusion.com", Label = "Tribal Fusion", Category = "Ad" },
            new BlocklistEntry { Domain = "turn.com", Label = "Turn", Category = "Ad" },
            new BlocklistEntry { Domain = "undertone.com", Label = "Undertone", Category = "Ad" },
            new BlocklistEntry { Domain = "viglink.com", Label = "VigLink", Category = "Ad" },
            new BlocklistEntry { Domain = "yieldoptimizer.com", Label = "Yield Optimizer", Category = "Ad" },
            new BlocklistEntry { Domain = "zergnet.com", Label = "ZergNet", Category = "Ad" },
            new BlocklistEntry { Domain = "zqtk.net", Label = "Zqtk", Category = "Ad" },
            new BlocklistEntry { Domain = "pippio.com", Label = "LiveRamp Pippio", Category = "Tracking" },
            new BlocklistEntry { Domain = "liadm.com", Label = "LiveIntent", Category = "Tracking" },
            new BlocklistEntry { Domain = "intentiq.com", Label = "Intent IQ", Category = "Tracking" },
            new BlocklistEntry { Domain = "eyeota.net", Label = "Eyeota", Category = "Tracking" },
            new BlocklistEntry { Domain = "moatpixel.com", Label = "Moat Pixel", Category = "Ad" },
            new BlocklistEntry { Domain = "nr-data.net", Label = "New Relic Data", Category = "Tracking" },
            new BlocklistEntry { Domain = "bam.nr-data.net", Label = "New Relic Browser", Category = "Tracking" },
            new BlocklistEntry { Domain = "parsely.com", Label = "Parse.ly", Category = "Tracking" },
            new BlocklistEntry { Domain = "sb.scorecardresearch.com", Label = "comScore Beacon", Category = "Tracking" },
            new BlocklistEntry { Domain = "static.chartbeat.com", Label = "Chartbeat", Category = "Tracking" },
            new BlocklistEntry { Domain = "newrelic.com", Label = "New Relic", Category = "Tracking" },
            new BlocklistEntry { Domain = "sentry.io", Label = "Sentry", Category = "Tracking" },
            new BlocklistEntry { Domain = "bugsnag.com", Label = "Bugsnag", Category = "Tracking" },
            new BlocklistEntry { Domain = "branch.io", Label = "Branch", Category = "Tracking" },
            new BlocklistEntry { Domain = "app.link", Label = "Branch Links", Category = "Tracking" },
            new BlocklistEntry { Domain = "marketo.net", Label = "Marketo", Category = "Tracking" },
            new BlocklistEntry { Domain = "mktoresp.com", Label = "Marketo", Category = "Tracking" },
            new BlocklistEntry { Domain = "hubspot.com", Label = "HubSpot", Category = "Tracking" },
            new BlocklistEntry { Domain = "ensighten.com", Label = "Ensighten", Category = "Tracking" },
            new BlocklistEntry { Domain = "tt.omtrdc.net", Label = "Adobe Target", Category = "Tracking" },
            new BlocklistEntry { Domain = "addtoany.com", Label = "AddToAny", Category = "Tracking" },
            new BlocklistEntry { Domain = "cookiebot.com", Label = "Cookiebot", Category = "Tracking" },
            new BlocklistEntry { Domain = "onetrust.com", Label = "OneTrust", Category = "Tracking" },
            new BlocklistEntry { Domain = "cookielaw.org", Label = "OneTrust Cookielaw", Category = "Tracking" },
            new BlocklistEntry { Domain = "trustarc.com", Label = "TrustArc", Category = "Tracking" },
            new BlocklistEntry { Domain = "partnerize.com", Label = "Partnerize", Category = "Ad" },
            new BlocklistEntry { Domain = "clickbank.net", Label = "ClickBank", Category = "Ad" },
            new BlocklistEntry { Domain = "crisp.chat", Label = "Crisp", Category = "Tracking" },
            new BlocklistEntry { Domain = "tawk.to", Label = "Tawk.to", Category = "Tracking" },
            new BlocklistEntry { Domain = "fls-na.amazon.com", Label = "Amazon Tracking", Category = "Tracking" },
            new BlocklistEntry { Domain = "assoc-amazon.com", Label = "Amazon Associates", Category = "Ad" },
            };

            // Enrich blocklist with all structured tracker domains from PrivacyEngine (global tracker DB).
            try
            {
                foreach (var t in PrivacyEngine.GetTrackerDatabase())
                {
                    if (string.IsNullOrWhiteSpace(t.Domain)) continue;
                    string domain = t.Domain.Trim().ToLowerInvariant();
                    if (list.Any(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    list.Add(new BlocklistEntry
                    {
                        Domain = domain,
                        Label = string.IsNullOrWhiteSpace(t.Label) ? domain : t.Label,
                        Category = t.Category == TrackerCategory.Advertising ? "Ad" : "Tracking",
                        Confidence = 0.97
                    });
                }
            }
            catch
            {
                // If anything goes wrong, we still have the built-in list.
            }

            return list;
        }

        /// <summary>Returns all blocklist domains for export (e.g. Chrome extension). Host-only, deduped. Same engine as the browser.</summary>
        public static string[] GetBlocklistDomainsForExport()
        {
            var entries = GetDefaultBlocklistEntries();
            return entries
                .Select(e => (e.Domain ?? "").Split('/')[0].Trim().ToLowerInvariant())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToArray();
        }

        // ════════════════════════════════════════════
        //  ANTI-FINGERPRINTING (BLEND-IN)
        // ════════════════════════════════════════════

        /// <summary>Fixed User-Agent for blend-in when anti-FP is on. Must match JS spoofs (Chrome on Windows).</summary>
        public const string BlendInUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        /// <summary>
        /// Blend-in anti-fingerprinting: Chrome-on-Windows profile. Uses real Chrome replay when artifacts are loaded; otherwise pass-through for canvas/WebGL.
        /// See docs/FINGERPRINT_REPLAY_ARCHITECTURE.md.
        /// </summary>
        public static string AntiFingerPrintScript => GetAntiFingerPrintScript(LoadFingerprintArtifacts());

        private static readonly string FingerprintArtifactsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "fingerprint-artifacts.json");

        /// <summary>Load captured Chrome fingerprint artifacts from %AppData%\\PrivacyMonitor\\fingerprint-artifacts.json. Returns null on missing/invalid.</summary>
        public static string? LoadFingerprintArtifacts()
        {
            try
            {
                if (!File.Exists(FingerprintArtifactsPath)) return null;
                var json = File.ReadAllText(FingerprintArtifactsPath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                _ = JsonSerializer.Deserialize<JsonElement>(json);
                return json;
            }
            catch { return null; }
        }

        private static string EscapeForEmbeddedJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string GetAntiFingerPrintScript(string? artifactsJson)
        {
            var artifactsJs = string.IsNullOrWhiteSpace(artifactsJson) ? "{}" : EscapeForEmbeddedJson(artifactsJson!);
            const string script = @"
(function() {
    'use strict';
    var __pmArtifacts = JSON.parse(""__PM_ARTIFACTS_JSON__"");
    var A = __pmArtifacts;

    // ── Canvas: Replay real Chrome pixel buffer when artifact exists; else pass-through ──
    const _toDataURL = HTMLCanvasElement.prototype.toDataURL;
    const _toBlob = HTMLCanvasElement.prototype.toBlob;
    const _getImageData = CanvasRenderingContext2D.prototype.getImageData;
    function _b64ToU8(b64) {
        var bin = atob(b64);
        var arr = new Uint8ClampedArray(bin.length);
        for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        return arr;
    }
    CanvasRenderingContext2D.prototype.getImageData = function(sx, sy, sw, sh) {
        var img = _getImageData.call(this, sx, sy, sw, sh);
        var key = sw + 'x' + sh;
        if (A.canvas && A.canvas[key]) {
            var bytes = _b64ToU8(A.canvas[key]);
            if (bytes.length >= img.data.length) img.data.set(bytes.subarray(0, img.data.length));
            return img;
        }
        return img;
    };
    HTMLCanvasElement.prototype.toDataURL = function(type) {
        try {
            var ctx = this.getContext('2d');
            if (ctx && A.canvas) {
                var key = this.width + 'x' + this.height;
                if (A.canvas[key]) {
                    var bytes = _b64ToU8(A.canvas[key]);
                    var img = ctx.createImageData(this.width, this.height);
                    if (bytes.length >= img.data.length) img.data.set(bytes.subarray(0, img.data.length));
                    ctx.putImageData(img, 0, 0);
                }
            }
        } catch(e) {}
        return _toDataURL.apply(this, arguments);
    };
    HTMLCanvasElement.prototype.toBlob = function(cb, type, quality) {
        try {
            var ctx = this.getContext('2d');
            if (ctx && A.canvas) {
                var key = this.width + 'x' + this.height;
                if (A.canvas[key]) {
                    var bytes = _b64ToU8(A.canvas[key]);
                    var img = ctx.createImageData(this.width, this.height);
                    if (bytes.length >= img.data.length) img.data.set(bytes.subarray(0, img.data.length));
                    ctx.putImageData(img, 0, 0);
                }
            }
        } catch(e) {}
        return _toBlob.apply(this, arguments);
    };

    // ── WebGL: Replay real Chrome framebuffer/params/extensions when artifact exists; else pass-through ──
    try {
        var wgl = WebGLRenderingContext.prototype;
        var _getParam = wgl.getParameter;
        var _getExt = wgl.getExtension;
        var _readPixels = wgl.readPixels;
        wgl.getParameter = function(p) {
            // Normalize vendor/renderer so many users share the same WebGL identity.
            if (p === 0x1F00 || p === 0x9245) return 'Google Inc.'; // VENDOR / UNMASKED_VENDOR_WEBGL
            if (p === 0x1F01 || p === 0x9246) return 'ANGLE (Intel(R) HD Graphics 620 Direct3D11 vs_5_0 ps_5_0)'; // RENDERER / UNMASKED_RENDERER_WEBGL
            if (A.webgl && A.webgl.params) {
                var hex = '0x' + (p >>> 0).toString(16).toUpperCase();
                if (A.webgl.params[hex] !== undefined) return A.webgl.params[hex];
            }
            return _getParam.apply(this, arguments);
        };
        var _getSupportedExtensions = wgl.getSupportedExtensions;
        wgl.getSupportedExtensions = function() {
            if (A.webgl && A.webgl.extensions && Array.isArray(A.webgl.extensions)) return A.webgl.extensions.slice();
            return _getSupportedExtensions.apply(this, arguments);
        };
        wgl.getExtension = function(name) {
            if (name === 'WEBGL_debug_renderer_info') return { UNMASKED_VENDOR_WEBGL: 0x9245, UNMASKED_RENDERER_WEBGL: 0x9246 };
            return _getExt.apply(this, arguments);
        };
        wgl.readPixels = function(x, y, w, h, format, type, pixels) {
            if (pixels && format === 0x1908 && type === 0x1401 && A.webgl) {
                var key = w + 'x' + h;
                if (A.webgl[key]) {
                    var bytes = _b64ToU8(A.webgl[key]);
                    if (bytes.length >= pixels.length) pixels.set(bytes.subarray(0, pixels.length));
                    return;
                }
            }
            return _readPixels.apply(this, arguments);
        };
        if (typeof WebGL2RenderingContext !== 'undefined') {
            var wgl2 = WebGL2RenderingContext.prototype;
            var _getParam2 = wgl2.getParameter;
            wgl2.getParameter = function(p) {
                if (p === 0x1F00 || p === 0x9245) return 'Google Inc.';
                if (p === 0x1F01 || p === 0x9246) return 'ANGLE (Intel(R) HD Graphics 620 Direct3D11 vs_5_0 ps_5_0)';
                if (A.webgl && A.webgl.params) {
                    var hex = '0x' + (p >>> 0).toString(16).toUpperCase();
                    if (A.webgl.params[hex] !== undefined) return A.webgl.params[hex];
                }
                return _getParam2.apply(this, arguments);
            };
            var _getSupportedExtensions2 = wgl2.getSupportedExtensions;
            wgl2.getSupportedExtensions = function() {
                if (A.webgl && A.webgl.extensions && Array.isArray(A.webgl.extensions)) return A.webgl.extensions.slice();
                return _getSupportedExtensions2.apply(this, arguments);
            };
        }
    } catch(e) {}

    // ── Fonts: Whitelist (Chrome default Windows) + measureText round ──
    try {
        const FONT_W = 'arial,arial black,calibri,cambria,comic sans ms,consolas,courier,courier new,georgia,impact,lucida console,lucida sans unicode,microsoft sans serif,segoe ui,tahoma,times new roman,trebuchet ms,verdana'.split(',');
        function _normFont(f) {
            const s = (f||'').split(',')[0].trim().toLowerCase().replace(/['""]/g,'');
            return FONT_W.some(w => s === w || s.startsWith(w + ' ')) ? f : 'Arial';
        }
        const _measureText = CanvasRenderingContext2D.prototype.measureText;
        CanvasRenderingContext2D.prototype.measureText = function(text) {
            const orig = this.font;
            this.font = _normFont(this.font);
            const r = _measureText.call(this, text);
            this.font = orig;
            return { width: Math.round(r.width * 2) / 2 };
        };
        if (document.fonts && document.fonts.check) {
            const _check = document.fonts.check.bind(document.fonts);
            document.fonts.check = function(font) {
                const s = (font||'').split(' ').slice(0,2).join(' ').toLowerCase();
                if (!FONT_W.some(w => s.indexOf(w) >= 0)) return false;
                return _check(font);
            };
        }
    } catch(e) {}

    // ── AudioContext: Replay real Chrome buffer when artifact exists; else pass-through ──
    try {
        var _getChannelData = AudioBuffer.prototype.getChannelData;
        function _b64ToF32(b64) {
            var bin = atob(b64);
            var u8 = new Uint8Array(bin.length);
            for (var i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
            return new Float32Array(u8.buffer);
        }
        AudioBuffer.prototype.getChannelData = function(channel) {
            if (A.audio && A.audio[String(this.length)]) {
                var f32 = _b64ToF32(A.audio[String(this.length)]);
                if (f32.length >= this.length) return f32.subarray(0, this.length);
            }
            return _getChannelData.call(this, channel);
        };
    } catch(e) {}

    // ── Timezone: en-US + America/New_York (consistent with language) ──
    try {
        const _resolved = Intl.DateTimeFormat.prototype.resolvedOptions;
        Intl.DateTimeFormat.prototype.resolvedOptions = function() {
            const o = _resolved.apply(this, arguments);
            return { locale: 'en-US', calendar: o.calendar || 'gregory', numberingSystem: o.numberingSystem || 'latn', timeZone: 'America/New_York' };
        };
        const _getTZ = Date.prototype.getTimezoneOffset;
        Date.prototype.getTimezoneOffset = function() { return 300; };
    } catch(e) {}

    // ── userAgentData (Client Hints) match UA ──
    try {
        const uaData = { brands: [{ brand: 'Chromium', version: '131' }, { brand: 'Google Chrome', version: '131' }, { brand: 'Not_A Brand', version: '24' }], mobile: false, platform: 'Windows', getHighEntropyValues: function() { return Promise.resolve({ brands: uaData.brands, mobile: false, platform: 'Windows', fullVersionList: uaData.brands, platformVersion: '10.0.0' }); } };
        Object.defineProperty(Navigator.prototype, 'userAgentData', { get: function() { return uaData; }, configurable: true });
    } catch(e) {}

    // ── Navigator/Screen/Battery/Media/Plugins/Connection (unchanged from before) ──
    try {
        Object.defineProperty(Navigator.prototype, 'hardwareConcurrency', { get: () => 8, configurable: true });
        Object.defineProperty(Navigator.prototype, 'deviceMemory', { get: () => 8, configurable: true });
        Object.defineProperty(Navigator.prototype, 'platform', { get: () => 'Win32', configurable: true });
        Object.defineProperty(Navigator.prototype, 'vendor', { get: () => 'Google Inc.', configurable: true });
        Object.defineProperty(Navigator.prototype, 'maxTouchPoints', { get: () => 0, configurable: true });
        Object.defineProperty(Navigator.prototype, 'languages', { get: () => ['en-US', 'en'], configurable: true });
        Object.defineProperty(Navigator.prototype, 'language', { get: () => 'en-US', configurable: true });
    } catch(e) {}
    try {
        Object.defineProperty(Screen.prototype, 'width', { get: () => 1920, configurable: true });
        Object.defineProperty(Screen.prototype, 'height', { get: () => 1080, configurable: true });
        Object.defineProperty(Screen.prototype, 'availWidth', { get: () => 1920, configurable: true });
        Object.defineProperty(Screen.prototype, 'availHeight', { get: () => 1040, configurable: true });
        Object.defineProperty(Screen.prototype, 'colorDepth', { get: () => 24, configurable: true });
        Object.defineProperty(Screen.prototype, 'pixelDepth', { get: () => 24, configurable: true });
    } catch(e) {}
    try { Object.defineProperty(window, 'devicePixelRatio', { get: () => 1, configurable: true }); } catch(e) {}
    try {
        if (navigator.getBattery) navigator.getBattery = function() { return Promise.resolve({ charging: true, chargingTime: Infinity, dischargingTime: Infinity, level: 1.0, addEventListener: function(){}, removeEventListener: function(){} }); };
    } catch(e) {}
    try {
        if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) navigator.mediaDevices.enumerateDevices = function() { return Promise.resolve([]); };
    } catch(e) {}
    try {
        Object.defineProperty(Navigator.prototype, 'plugins', { get: () => [], configurable: true });
        Object.defineProperty(Navigator.prototype, 'mimeTypes', { get: () => [], configurable: true });
    } catch(e) {}
    try {
        const conn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (conn) {
            Object.defineProperty(conn, 'effectiveType', { get: () => '4g', configurable: true });
            Object.defineProperty(conn, 'downlink', { get: () => 10, configurable: true });
            Object.defineProperty(conn, 'rtt', { get: () => 50, configurable: true });
        }
    } catch(e) {}
})();";
            return script.Replace("__PM_ARTIFACTS_JSON__", artifactsJs);
        }

        // ════════════════════════════════════════════
        //  SCRIPT / ELEMENT BLOCKER (pre-page JS)
        // ════════════════════════════════════════════

        public static string ElementBlockerBootstrapScript => @"
(function() {
    if (window.__pmBlocker) return;
    const _blocked = new Set();

    function _norm(h) { return (h || '').toLowerCase(); }
    function _isBlockedUrl(u) {
        try {
            const host = new URL(u, location.href).hostname.toLowerCase();
            if (_blocked.has(host)) return true;
            for (const b of _blocked) { if (host.endsWith('.' + b)) return true; }
        } catch(e) {}
        return false;
    }
    function _blockEl(el, attr, val) {
        if (!val) return false;
        if (_isBlockedUrl(val)) {
            try { el.setAttribute('data-pm-blocked', '1'); el.style.setProperty('display', 'none', 'important'); } catch(e) {}
            return true;
        }
        return false;
    }

    window.__pmBlocker = {
        setBlockedHosts: function(list) { _blocked.clear(); (list || []).forEach(h => { if (h) _blocked.add(_norm(h)); }); },
        addBlockedHost: function(h) { if (h) _blocked.add(_norm(h)); },
        isBlockedUrl: _isBlockedUrl
    };

    const _setAttr = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function(name, value) {
        const tag = this.tagName ? this.tagName.toLowerCase() : '';
        const attr = name.toLowerCase();
        if ((tag === 'script' || tag === 'iframe' || tag === 'img' || tag === 'link' || tag === 'video' || tag === 'audio' || tag === 'source') &&
            (attr === 'src' || attr === 'href')) {
            if (_blockEl(this, attr, value)) return;
        }
        return _setAttr.apply(this, arguments);
    };

    function _hookProp(proto, prop) {
        const desc = Object.getOwnPropertyDescriptor(proto, prop);
        if (!desc || !desc.set) return;
        Object.defineProperty(proto, prop, {
            set: function(v) { if (_blockEl(this, prop, v)) return; return desc.set.call(this, v); },
            get: desc.get ? desc.get : undefined,
            configurable: true
        });
    }

    _hookProp(HTMLScriptElement.prototype, 'src');
    _hookProp(HTMLIFrameElement.prototype, 'src');
    _hookProp(HTMLImageElement.prototype, 'src');
    _hookProp(HTMLLinkElement.prototype, 'href');
    _hookProp(HTMLVideoElement.prototype, 'src');
    _hookProp(HTMLAudioElement.prototype, 'src');
    if (window.HTMLSourceElement) _hookProp(HTMLSourceElement.prototype, 'src');

    const _append = Node.prototype.appendChild;
    Node.prototype.appendChild = function(node) {
        try {
            if (node && node.tagName) {
                const tag = node.tagName.toLowerCase();
                const val = node.getAttribute && (node.getAttribute('src') || node.getAttribute('href'));
                if ((tag === 'script' || tag === 'iframe' || tag === 'img' || tag === 'link' || tag === 'video' || tag === 'audio' || tag === 'source') &&
                    val && _isBlockedUrl(val)) { try { node.setAttribute('data-pm-blocked','1'); node.style.setProperty('display','none','important'); } catch(e){} return node; }
            }
        } catch(e) {}
        return _append.call(this, node);
    };

    const _insert = Node.prototype.insertBefore;
    Node.prototype.insertBefore = function(node, ref) {
        try {
            if (node && node.tagName) {
                const tag = node.tagName.toLowerCase();
                const val = node.getAttribute && (node.getAttribute('src') || node.getAttribute('href'));
                if ((tag === 'script' || tag === 'iframe' || tag === 'img' || tag === 'link' || tag === 'video' || tag === 'audio' || tag === 'source') &&
                    val && _isBlockedUrl(val)) { try { node.setAttribute('data-pm-blocked','1'); node.style.setProperty('display','none','important'); } catch(e){} return node; }
            }
        } catch(e) {}
        return _insert.call(this, node, ref);
    };

    const _fetch = window.fetch;
    if (_fetch) {
        window.fetch = function(input, init) {
            try {
                const url = typeof input === 'string' ? input : (input && input.url);
                if (url && _isBlockedUrl(url)) return Promise.reject(new Error('Blocked by PrivacyMonitor'));
            } catch(e) {}
            return _fetch.apply(this, arguments);
        };
    }

    const _open = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
        if (url && _isBlockedUrl(url)) { try { this.abort(); } catch(e) {} return; }
        return _open.apply(this, arguments);
    };

    function _hideBlocked(el) {
        try {
            if (el.setAttribute && el.getAttribute('data-pm-blocked') === '1') { el.style.setProperty('display', 'none', 'important'); return; }
            var src = el.src || el.getAttribute('src') || el.href || el.getAttribute('href') || '';
            if (src && _isBlockedUrl(src)) { el.setAttribute('data-pm-blocked', '1'); el.style.setProperty('display', 'none', 'important'); }
        } catch(e) {}
    }
    function _runHide() {
        try {
            document.querySelectorAll(""[data-pm-blocked='1']"").forEach(function(el){ el.style.setProperty('display', 'none', 'important'); });
            document.querySelectorAll('script[src], iframe[src], img[src], link[href]').forEach(_hideBlocked);
        } catch(e) {}
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', _runHide);
    else _runHide();
    var _obs = new MutationObserver(function(mutations) {
        mutations.forEach(function(m) {
            if (m.addedNodes) m.addedNodes.forEach(function(n) {
                if (n.nodeType === 1) { _hideBlocked(n); if (n.querySelectorAll) n.querySelectorAll('script[src], iframe[src], img[src], link[href]').forEach(_hideBlocked); }
            });
        });
    });
    try { _obs.observe(document.documentElement, { childList: true, subtree: true }); } catch(e) {}
})();";

        /// <summary>Hides in-page ad containers and sponsored links by common id/class/data patterns. Reduces pop-ups and href/display ads.</summary>
        public static string CosmeticFilterScript => @"
(function() {
    if (window.__pmCosmetic) return;
    window.__pmCosmetic = true;
    const _hidden = new WeakSet();
    const _idClassPatterns = ['adsbygoogle','ad-container','ad_container','adcontainer','advertisement','ad-slot','ad_slot','adslot','ad-wrapper','ad_wrapper','adbox','ad-box','ad_area','ad-area','adplaceholder','ad-placeholder','ad-sense','adsense','google_ad','ins.adsbygoogle','sponsored','commercial','banner-ad','banner_ad','sidebar-ad','sidebar_ad','ad-placement','adplacement','ad-unit','adunit','ad-superbanner','ad-sky','ad-leaderboard','ad-interstitial','ad-overlay','ad-popup','adblock','ad-block','adblocker','outbrain','taboola','revcontent','content-ad','native-ad','promoted-content','dfp-ad','doubleclick','advertisement-label','ad-label','adlabel'];
    const _dataAttrs = ['data-ad', 'data-ad-slot', 'data-google-query-id', 'data-ad-status', 'data-ad-unit', 'data-ad-format'];
    function _matchesAd(el) {
        if (!el || el.nodeType !== 1) return false;
        try {
            var id = (el.id || '').toLowerCase(), cls = (el.className && typeof el.className === 'string' ? el.className : '').toLowerCase();
            for (var i = 0; i < _idClassPatterns.length; i++)
                if (id.indexOf(_idClassPatterns[i]) >= 0 || cls.indexOf(_idClassPatterns[i]) >= 0) return true;
            for (var j = 0; j < _dataAttrs.length; j++)
                if (el.getAttribute && el.getAttribute(_dataAttrs[j]) !== null) return true;
            if (el.tagName === 'IFRAME' && el.src) {
                var s = (el.src || '').toLowerCase();
                if (s.indexOf('doubleclick') >= 0 || s.indexOf('googlesyndication') >= 0 || s.indexOf('adservice') >= 0 || s.indexOf('/pagead/') >= 0 || s.indexOf('adsbygoogle') >= 0 || s.indexOf('adnxs') >= 0 || s.indexOf('criteo') >= 0 || s.indexOf('outbrain') >= 0 || s.indexOf('taboola') >= 0 || s.indexOf('revcontent') >= 0) return true;
            }
        } catch(e) {}
        return false;
    }
    function _hideAdLike(root) {
        try {
            var list = (root || document).querySelectorAll ? (root || document).querySelectorAll('*') : [];
            for (var i = 0; i < list.length; i++) {
                var el = list[i];
                if (_hidden.has(el)) continue;
                if (_matchesAd(el)) {
                    el.style.setProperty('display', 'none', 'important');
                    el.setAttribute('data-pm-hidden', '1');
                    _hidden.add(el);
                }
            }
        } catch(e) {}
    }
    function _run() {
        _hideAdLike(document);
        try {
            var obs = new MutationObserver(function(mutations) {
                for (var m = 0; m < mutations.length; m++)
                    for (var n = 0; n < (mutations[m].addedNodes || []).length; n++)
                        _hideAdLike(mutations[m].addedNodes[n]);
            });
            obs.observe(document.documentElement, { childList: true, subtree: true });
        } catch(e) {}
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', _run);
    else _run();
})();";

        public static string BuildBlockerSeedScript(IEnumerable<string> hosts)
        {
            var list = hosts?.Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            var json = JsonSerializer.Serialize(list);
            return $"(function(){{ if (window.__pmBlocker && window.__pmBlocker.setBlockedHosts) {{ window.__pmBlocker.setBlockedHosts({json}); }} }})();";
        }

        public static string BuildBlockerAddHostScript(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "";
            var json = JsonSerializer.Serialize(host.Trim().ToLowerInvariant());
            return $"(function(){{ if (window.__pmBlocker && window.__pmBlocker.addBlockedHost) {{ window.__pmBlocker.addBlockedHost({json}); }} }})();";
        }

        // ════════════════════════════════════════════
        //  PERSISTENCE
        // ════════════════════════════════════════════

        public static void SaveProfiles()
        {
            try
            {
                var dir = Path.GetDirectoryName(ProfilesPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var data = _profiles.ToDictionary(kv => kv.Key, kv => new SiteProfileDto
                {
                    Mode = (int)kv.Value.Mode,
                    AntiFingerprint = kv.Value.AntiFingerprint,
                    BlockBehavioral = kv.Value.BlockBehavioral,
                    BlockAdsTrackers = kv.Value.BlockAdsTrackers,
                    LastVisit = kv.Value.LastVisit.ToString("o")
                });
                File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static void LoadProfiles()
        {
            try
            {
                if (!File.Exists(ProfilesPath)) return;
                var json = File.ReadAllText(ProfilesPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, SiteProfileDto>>(json);
                if (data == null) return;
                foreach (var kv in data)
                {
                    _profiles[kv.Key] = new SiteProfile
                    {
                        Mode = (ProtectionMode)kv.Value.Mode,
                        AntiFingerprint = kv.Value.AntiFingerprint,
                        BlockBehavioral = kv.Value.BlockBehavioral,
                        BlockAdsTrackers = kv.Value.BlockAdsTrackers,
                        LastVisit = DateTime.TryParse(kv.Value.LastVisit, out var dt) ? dt : DateTime.UtcNow
                    };
                }
            }
            catch { }
        }

        public static void SaveLearnedTrackers()
        {
            try
            {
                var dir = Path.GetDirectoryName(LearnedTrackersPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var data = _learnedTrackers.Where(kv => kv.Value.IsConfirmedTracker)
                    .ToDictionary(kv => kv.Key, kv =>
                    {
                        lock (kv.Value.Sync)
                        {
                            return new LearnedTrackerDto
                            {
                                Observations = kv.Value.ObservationCount,
                                MaxConf = kv.Value.MaxConfidence,
                                Signals = kv.Value.SignalTypes.ToList(),
                                Sites = kv.Value.SeenOnSites.ToList(),
                                ConfirmedAt = kv.Value.ConfirmedAt.ToString("o")
                            };
                        }
                    });
                File.WriteAllText(LearnedTrackersPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static void LoadLearnedTrackers()
        {
            try
            {
                if (!File.Exists(LearnedTrackersPath)) return;
                var json = File.ReadAllText(LearnedTrackersPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, LearnedTrackerDto>>(json);
                if (data == null) return;
                foreach (var kv in data)
                {
                    _learnedTrackers[kv.Key] = new LearnedTrackerEntry
                    {
                        Domain = kv.Key,
                        ObservationCount = kv.Value.Observations,
                        MaxConfidence = kv.Value.MaxConf,
                        IsConfirmedTracker = true,
                        SignalTypes = new List<string>(kv.Value.Signals),
                        SeenOnSites = new List<string>(kv.Value.Sites),
                        ConfirmedAt = DateTime.TryParse(kv.Value.ConfirmedAt, out var dt) ? dt : DateTime.UtcNow
                    };
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════

        private static string GetParentDomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            var parts = host.Split('.');
            if (parts.Length > 2)
                return string.Join('.', parts.Skip(1));
            return "";
        }

        private static double MaxSignalConfidence(List<DetectionSignal> signals, params string[] types)
        {
            if (signals.Count == 0 || types.Length == 0) return 0;
            double max = 0;
            foreach (var s in signals)
            {
                if (types.Contains(s.SignalType) && s.Confidence > max) max = s.Confidence;
            }
            return max;
        }

        private static string MapTrackerCategory(string trackerCategoryName)
        {
            if (string.IsNullOrEmpty(trackerCategoryName)) return "Tracking";
            return trackerCategoryName switch
            {
                "Advertising" or "AdVerification" or "Affiliate" => "Ad",
                _ => "Tracking"
            };
        }

        private static bool IsMediaContext(string resourceContext)
        {
            if (string.IsNullOrWhiteSpace(resourceContext)) return false;
            return resourceContext.Equals("Media", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] AdTrackerPathSegments = new[]
        {
            "/pagead/", "/pagead2.", "/ads/", "/ad.", "/adservice", "/adsystem",
            "/analytics", "/ga.js", "/gtm.js", "/gtag/", "/collect", "/g/collect", "/j/collect", "/r/collect", "/s/collect",
            "/track", "/tracking", "/pixel", "/tr?", "/beacon", "/log", "/event",
            "/conversion", "/impression", "/click", "/view?", "/view.",
            "/doubleclick", "/googlesyndication", "/googleadservices",
            "/fbq", "/fbevents", "/tr?id=", "/px.", "/sync?", "/match",
            "/segment", "/mixpanel", "/amplitude", "/heapanalytics",
            "/gtm/", "/gtag/js", "/ga/", "/analytics.js", "/stats.",
            "/pixel.", "/tracking.", "/tracker.", "/tag.", "/script/",
            "/b/ss", "/b/collect", "/everesttech", "/demdex", "/dpm.",
            "/moat", "/doubleverify", "/adsafe", "/viewability",
            "/gtm-", "/fbevents", "/bat.js", "/smartsync", "/sync.", "/id/", "/idsync", "/matchid", "/user-match",
            "/pagead/", "/ads?", "/ad?", "/view.", "/imp?", "/impression", "/click?", "/conv?", "/conversion",
            "/delivery/", "/sync?", "/match?", "/bid?", "/prebid", "/hb_pb", "/hb_bidder", "/hb_cv", "/hb_bidder", "/gdpr", "/consent",
            "/vast", "/vpaid", "/video-ad", "/ad.js", "/ads.js", "/adscript", "/adsystem", "/getuid",
            "/rta", "/rtb", "/prebid.", "/adsrvr", "/pxl.", "/px/", "/trk.", "/tracker.", "/pixel.", "/beacon.",
            "/collect?", "/event?", "/log?", "/stats?", "/metric", "/telemetry", "/__utm", "/utm_",
            "/fbq", "/fbevents", "/gtag", "/ga.js", "/analytics.js", "/gtm.js", "/g/collect", "/j/collect",
            "/imp?", "/impression", "/view?", "/click?", "/conv?", "/conversion", "/sync?", "/match?",
            "/b/collect", "/s/collect", "/r/collect", "/__imp", "/__n", "/pagead/", "/pagead2/",
            "/ads?", "/adview", "/adrequest", "/adcall", "/adsystem", "/adserver", "/delivery",
            "/tr?", "/pixel?", "/beacon?", "/1x1", "/blank.gif", "/transparent.gif", "/pixel.gif",
            "/gtm-", "/gtag/js", "/fbevents.js", "/ga.js", "/ua-", "/gid/", "/gtm.js",
            "/v2/collect", "/v2/e", "/ingest", "/e/", "/i.ve", "/identity", "/sync/identity", "/gdpr_consent", "/__n", "/__imp",
            "/hb_", "/setuid", "/usersync", "/match/id", "/id/sync", "/cm/sync", "/csync", "/ups"
        };

        private static bool IsAdOrTrackerPath(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl) || pathOrUrl.Length < 4) return false;
            var lower = pathOrUrl.ToLowerInvariant();
            foreach (var seg in AdTrackerPathSegments)
            {
                if (lower.Contains(seg, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Strong ad-block: block 3p domains whose hostname looks like ad/tracking infrastructure
        private static readonly string[] AdLikeDomainParts = new[]
        {
            "ad.", "ads.", "adservice", "adserver", "adsystem", "ad-", "-ad.", "track.", "tracking.", "tracker.",
            "pixel.", "pixels.", "analytics.", "metric", "metrics.", "telemetry.", "beacon.", "tag.", "tags.",
            "collect.", "collector.", "tracking", "doubleclick", "googlesyndication", "adnxs", "pubmatic",
            "criteo", "outbrain", "taboola", "mgid", "revcontent", "contentad", "adform", "media.net",
            "adroll", "perfectaudience", "adsymptotic", "exelator", "demdex", "bluekai", "krxd", "rlcdn",
            "scorecardresearch", "quantserve", "2mdn", "pagead", "syndication", "adtech", "adform",
            "adsrvr", "mathtag", "rfihub", "rubicon", "openx", "casalemedia", "indexexchange", "bidswitch",
            "adsymptotic", "moatads", "doubleverify", "adsafe", "flashtalking", "serving-sys", "amazon-adsystem",
            "app-measurement", "firebase", "gtag", "gtm.", "googletag", "fbevents", "facebook.com/tr", "bat.bing",
            "clarity.ms", "hotjar", "fullstory", "mouseflow", "segment.", "mixpanel", "amplitude", "heapanalytics",
            "smartadserver", "improvedigital", "synacor", "undertone", "teads", "spotx",
            "liveramp", "pippio", "tapad", "lotame", "eyeota", "zeotap", "audiencescience", "omnicom"
        };

        /// <summary>High-confidence ad/tracker domain patterns — blocked even in BlockKnown mode.</summary>
        private static readonly string[] StrongAdLikeDomainParts = new[]
        {
            "doubleclick", "googlesyndication", "googleadservices", "adservice.google", "pagead", "2mdn",
            "adnxs", "appnexus", "pubmatic", "criteo", "outbrain", "taboola", "adsrvr", "mathtag",
            "pixel.facebook", "facebook.net", "an.facebook", "bat.bing", "clarity.ms",
            "demdex", "bluekai", "krxd", "rlcdn", "lotame", "crwdcntrl", "tapad", "agkn",
            "scorecardresearch", "quantserve", "hotjar", "fullstory", "mouseflow", "smartlook", "logrocket",
            "segment.io", "segment.com", "mixpanel", "amplitude", "heapanalytics", "googletagmanager", "gtm",
            "liveramp", "pippio", "eyeota", "zeotap", "improvedigital", "smartadserver", "adsymptotic",
            "tiktok.com/i18n", "ads.tiktok", "analytics.tiktok", "tr.snapchat", "ads.linkedin", "snap.licdn"
        };

        private static bool IsStrongAdLikeDomain(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length < 6) return false;
            var lower = host.ToLowerInvariant();
            foreach (var part in StrongAdLikeDomainParts)
            {
                if (lower.Contains(part) || lower.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsAdLikeDomain(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length < 6) return false;
            var lower = host.ToLowerInvariant();
            foreach (var part in AdLikeDomainParts)
            {
                if (lower.Contains(part) || lower.StartsWith(part, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "Tracking";
            var c = category.Trim().ToLowerInvariant();
            return c.Contains("ad") ? "Ad" : c.Contains("behavior") ? "Behavioral" : "Tracking";
        }

        // ════════════════════════════════════════════
        //  INTERNAL DTOs for JSON serialization
        // ════════════════════════════════════════════

        private class SiteProfileDto
        {
            public int Mode { get; set; }
            public bool AntiFingerprint { get; set; }
            public bool BlockBehavioral { get; set; }
            public bool BlockAdsTrackers { get; set; } = true;
            public string LastVisit { get; set; } = "";
        }

        private class LearnedTrackerDto
        {
            public int Observations { get; set; }
            public double MaxConf { get; set; }
            public List<string> Signals { get; set; } = new();
            public List<string> Sites { get; set; } = new();
            public string ConfirmedAt { get; set; } = "";
        }

        private class LearnedTrackerEntry
        {
            public object Sync { get; } = new();
            public string Domain { get; set; } = "";
            public int ObservationCount { get; set; }
            public double MaxConfidence { get; set; }
            public bool IsConfirmedTracker { get; set; }
            public DateTime ConfirmedAt { get; set; }
            public DateTime LastSeen { get; set; } = DateTime.UtcNow;
            public List<string> SignalTypes { get; set; } = new();
            public List<string> SeenOnSites { get; set; } = new();
        }

        private class BlocklistFile
        {
            public string UpdatedAt { get; set; } = "";
            public List<BlocklistEntry> Entries { get; set; } = new();
        }

        public class BlocklistEntry
        {
            public string Domain { get; set; } = "";
            public string Label { get; set; } = "";
            public string Category { get; set; } = "Tracking";
            public double Confidence { get; set; } = 0.95;
        }
    }
}
