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
            if (string.IsNullOrEmpty(host)) return new SiteProfile { Mode = GlobalDefaultMode };
            // Check exact host, then parent domain
            if (_profiles.TryGetValue(host, out var p)) return p;
            string parent = GetParentDomain(host);
            if (!string.IsNullOrEmpty(parent) && _profiles.TryGetValue(parent, out var pp)) return pp;
            return new SiteProfile { Mode = GlobalDefaultMode };
        }

        public static void SetProfile(string host, SiteProfile profile)
        {
            _profiles[host] = profile;
            SaveProfiles();
        }

        public static ProtectionMode GetEffectiveMode(string host)
        {
            return GetProfile(host).Mode;
        }

        public static void SetMode(string host, ProtectionMode mode)
        {
            var p = GetProfile(host);
            p.Mode = mode;
            _profiles[host] = p;
            SaveProfiles();
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

            // 3) Known tracker detection (confidence-weighted)
            double trackerConfidence = MaxSignalConfidence(entry.Signals, "known_tracker", "heuristic_tracker");
            if (blockAdsTrackers && !string.IsNullOrEmpty(entry.TrackerLabel))
            {
                if (mode == ProtectionMode.BlockKnown && trackerConfidence >= 0.80)
                {
                    return new BlockDecision
                    {
                        Blocked = true,
                        Reason = $"Confirmed tracker: {entry.TrackerLabel}",
                        Category = MapTrackerCategory(entry.TrackerCategoryName),
                        Confidence = trackerConfidence,
                        TrackerLabel = entry.TrackerLabel
                    };
                }

                if (mode == ProtectionMode.Aggressive && trackerConfidence >= 0.50)
                {
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

            // 4) Behavioral blocking (JS injection / session replay signatures)
            if (blockBehavioral)
            {
                var behavioral = entry.Signals.FirstOrDefault(s =>
                    s.SignalType == "js_injection" ||
                    s.SignalType == "session_replay" ||
                    s.SignalType == "behavioral_tracking");
                if (behavioral != null && behavioral.Confidence >= 0.70)
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

            // 5) Aggressive heuristic layer
            if (mode == ProtectionMode.Aggressive && blockAdsTrackers)
            {
                if (isMedia)
                    return new BlockDecision { Blocked = false, Reason = "Media request" };
                var heuristic = entry.Signals
                    .Where(s => s.SignalType == "high_entropy_param" ||
                                s.SignalType == "pixel_tracking" ||
                                s.SignalType == "obfuscated_payload" ||
                                s.SignalType == "redirect_bounce" ||
                                s.SignalType == "cookie_sync")
                    .OrderByDescending(s => s.Confidence)
                    .FirstOrDefault();

                if (heuristic != null)
                {
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

                // Promote to learned tracker when: 5+ tracker signals
                if (!entry.IsConfirmedTracker && entry.ObservationCount >= 5)
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
                    // If appears on 3+ different sites, strong signal
                    if (entry.SeenOnSites.Count >= 3 && !entry.IsConfirmedTracker)
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

        private static List<BlocklistEntry> GetDefaultBlocklistEntries() => new()
        {
            new BlocklistEntry { Domain = "doubleclick.net", Label = "Google DoubleClick", Category = "Ad" },
            new BlocklistEntry { Domain = "googlesyndication.com", Label = "Google AdSense", Category = "Ad" },
            new BlocklistEntry { Domain = "googleadservices.com", Label = "Google Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "adservice.google.com", Label = "Google Ad Service", Category = "Ad" },
            new BlocklistEntry { Domain = "pagead2.googlesyndication.com", Label = "Google Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "ads-twitter.com", Label = "Twitter Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "static.ads-twitter.com", Label = "Twitter Ads SDK", Category = "Ad" },
            new BlocklistEntry { Domain = "ads.linkedin.com", Label = "LinkedIn Ads", Category = "Ad" },
            new BlocklistEntry { Domain = "px.ads.linkedin.com", Label = "LinkedIn Conversion", Category = "Ad" },
            new BlocklistEntry { Domain = "analytics.tiktok.com", Label = "TikTok Analytics", Category = "Tracking" },
            new BlocklistEntry { Domain = "mon.tiktokv.com", Label = "TikTok Monitoring", Category = "Tracking" },
            new BlocklistEntry { Domain = "facebook.com", Label = "Facebook Pixel", Category = "Ad" },
            new BlocklistEntry { Domain = "pixel.facebook.com", Label = "Facebook Pixel", Category = "Ad" },
            new BlocklistEntry { Domain = "connect.facebook.net", Label = "Facebook Connect", Category = "Tracking" },
            new BlocklistEntry { Domain = "criteo.com", Label = "Criteo", Category = "Ad" },
            new BlocklistEntry { Domain = "criteo.net", Label = "Criteo", Category = "Ad" },
            new BlocklistEntry { Domain = "adnxs.com", Label = "Xandr/AppNexus", Category = "Ad" },
            new BlocklistEntry { Domain = "rubiconproject.com", Label = "Rubicon Project", Category = "Ad" },
            new BlocklistEntry { Domain = "pubmatic.com", Label = "PubMatic", Category = "Ad" },
            new BlocklistEntry { Domain = "openx.net", Label = "OpenX", Category = "Ad" },
            new BlocklistEntry { Domain = "bidswitch.net", Label = "BidSwitch", Category = "Ad" },
            new BlocklistEntry { Domain = "taboola.com", Label = "Taboola", Category = "Ad" },
            new BlocklistEntry { Domain = "outbrain.com", Label = "Outbrain", Category = "Ad" },
            new BlocklistEntry { Domain = "sharethrough.com", Label = "Sharethrough", Category = "Ad" },
            new BlocklistEntry { Domain = "33across.com", Label = "33Across", Category = "Ad" },
            new BlocklistEntry { Domain = "triplelift.com", Label = "TripleLift", Category = "Ad" },
            new BlocklistEntry { Domain = "yieldmo.com", Label = "Yieldmo", Category = "Ad" },
            new BlocklistEntry { Domain = "media.net", Label = "Media.net", Category = "Ad" },
            new BlocklistEntry { Domain = "amazon-adsystem.com", Label = "Amazon Advertising", Category = "Ad" },
            new BlocklistEntry { Domain = "aax.amazon-adsystem.com", Label = "Amazon AAX", Category = "Ad" }
        };

        // ════════════════════════════════════════════
        //  ANTI-FINGERPRINTING JAVASCRIPT
        // ════════════════════════════════════════════

        /// <summary>
        /// Generate a JavaScript snippet that overrides/randomizes browser APIs
        /// to prevent fingerprinting. Injected on every page load when anti-FP is enabled.
        /// Uses a session-stable random seed so pages work consistently within a session.
        /// </summary>
        public static string AntiFingerPrintScript => @"
(function() {
    'use strict';
    // Session-stable seed (changes per page load, consistent within page)
    const _seed = Math.floor(Math.random() * 2147483647);
    function _hash(s) { let h=_seed; for(let i=0;i<s.length;i++) h=((h<<5)-h)+s.charCodeAt(i)|0; return h; }
    function _rand(min,max) { const x=Math.sin(_seed*9301+49297)%1; return min+Math.abs(x)*(max-min); }

    // ── Canvas: Add subtle noise to pixel data ──
    const _toDataURL = HTMLCanvasElement.prototype.toDataURL;
    const _toBlob = HTMLCanvasElement.prototype.toBlob;
    const _getImageData = CanvasRenderingContext2D.prototype.getImageData;

    function _noiseCanvas(ctx) {
        try {
            const w = ctx.canvas.width, h = ctx.canvas.height;
            if (w < 2 || h < 2) return;
            const img = _getImageData.call(ctx, 0, 0, Math.min(w,4), Math.min(h,4));
            for (let i = 0; i < img.data.length; i += 4) {
                img.data[i] = (img.data[i] + (_hash('c'+i) % 3) - 1) & 0xFF;
            }
            ctx.putImageData(img, 0, 0);
        } catch(e) {}
    }

    HTMLCanvasElement.prototype.toDataURL = function() {
        try { const ctx = this.getContext('2d'); if (ctx) _noiseCanvas(ctx); } catch(e) {}
        return _toDataURL.apply(this, arguments);
    };
    HTMLCanvasElement.prototype.toBlob = function() {
        try { const ctx = this.getContext('2d'); if (ctx) _noiseCanvas(ctx); } catch(e) {}
        return _toBlob.apply(this, arguments);
    };
    CanvasRenderingContext2D.prototype.getImageData = function() {
        const data = _getImageData.apply(this, arguments);
        try {
            for (let i = 0; i < Math.min(data.data.length, 64); i += 4) {
                data.data[i] = (data.data[i] + (_hash('g'+i) % 3) - 1) & 0xFF;
            }
        } catch(e) {}
        return data;
    };

    // ── WebGL: Return generic vendor/renderer for debug info ──
    try {
        const wgl = WebGLRenderingContext.prototype;
        const _getParam = wgl.getParameter;
        const UNMASKED_VENDOR = 0x9245;
        const UNMASKED_RENDERER = 0x9246;
        wgl.getParameter = function(param) {
            if (param === UNMASKED_VENDOR) return 'Google Inc.';
            if (param === UNMASKED_RENDERER) return 'ANGLE (Generic GPU)';
            return _getParam.apply(this, arguments);
        };
        if (typeof WebGL2RenderingContext !== 'undefined') {
            const _getParam2 = WebGL2RenderingContext.prototype.getParameter;
            WebGL2RenderingContext.prototype.getParameter = function(param) {
                if (param === UNMASKED_VENDOR) return 'Google Inc.';
                if (param === UNMASKED_RENDERER) return 'ANGLE (Generic GPU)';
                return _getParam2.apply(this, arguments);
            };
        }
    } catch(e) {}

    // ── AudioContext: Add tiny noise to DynamicsCompressor output ──
    try {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (AC) {
            const _createOsc = AC.prototype.createOscillator;
            AC.prototype.createOscillator = function() {
                const osc = _createOsc.apply(this, arguments);
                try {
                    const gain = this.createGain();
                    gain.gain.value = 1.0 + (_hash('audio') % 100) / 1000000;
                    osc._pmGain = gain;
                } catch(e) {}
                return osc;
            };
        }
    } catch(e) {}

    // ── Navigator: Spoof hardware properties ──
    try {
        const spoofs = {
            hardwareConcurrency: [2, 4, 8][Math.abs(_hash('hc')) % 3],
            deviceMemory: [4, 8][Math.abs(_hash('dm')) % 2],
            platform: 'Win32',
            vendor: 'Google Inc.'
        };
        for (const [prop, val] of Object.entries(spoofs)) {
            try {
                Object.defineProperty(Navigator.prototype, prop, {
                    get: function() { return val; },
                    configurable: true
                });
            } catch(e) {}
        }
    } catch(e) {}

    // ── Screen: Slight randomization to avoid exact matches ──
    try {
        const realWidth = screen.width, realHeight = screen.height;
        // Don't change actual dimensions (breaks layouts), but randomize colorDepth/pixelDepth
        Object.defineProperty(Screen.prototype, 'colorDepth', { get: () => 24, configurable: true });
        Object.defineProperty(Screen.prototype, 'pixelDepth', { get: () => 24, configurable: true });
    } catch(e) {}

    // ── Timezone: Limit precision of resolvedOptions ──
    try {
        const _ro = Intl.DateTimeFormat.prototype.resolvedOptions;
        Intl.DateTimeFormat.prototype.resolvedOptions = function() {
            const opts = _ro.apply(this, arguments);
            // Keep timezone but remove narrowing locale variants
            return opts;
        };
    } catch(e) {}

    // ── Battery API: Block entirely ──
    try {
        if (navigator.getBattery) {
            navigator.getBattery = function() {
                return Promise.resolve({ charging: true, chargingTime: Infinity, dischargingTime: Infinity, level: 1.0,
                    addEventListener: function(){}, removeEventListener: function(){} });
            };
        }
    } catch(e) {}

    // ── Media Devices: Return empty list ──
    try {
        if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
            navigator.mediaDevices.enumerateDevices = function() { return Promise.resolve([]); };
        }
    } catch(e) {}

    // ── Plugin list: Return empty ──
    try {
        Object.defineProperty(Navigator.prototype, 'plugins', { get: () => [], configurable: true });
        Object.defineProperty(Navigator.prototype, 'mimeTypes', { get: () => [], configurable: true });
    } catch(e) {}

    // ── Connection API: Return generic values ──
    try {
        const conn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (conn) {
            Object.defineProperty(conn, 'effectiveType', { get: () => '4g', configurable: true });
            Object.defineProperty(conn, 'downlink', { get: () => 10, configurable: true });
            Object.defineProperty(conn, 'rtt', { get: () => 50, configurable: true });
        }
    } catch(e) {}
})();";

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
            try { el.setAttribute('data-pm-blocked', '1'); } catch(e) {}
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
                    val && _isBlockedUrl(val)) return node;
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
                    val && _isBlockedUrl(val)) return node;
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
