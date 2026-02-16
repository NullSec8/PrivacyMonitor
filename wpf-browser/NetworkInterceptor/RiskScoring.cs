using System;
using System.Collections.Generic;
using System.Linq;
using PrivacyMonitor;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Multi-factor weighted risk scoring. Unit-testable: pure inputs → RiskResult.
    /// Covers: third-party, tracker confidence, cookie flags, headers, fingerprinting.
    /// </summary>
    public static class RiskScoring
    {
        public const int MaxScore = 100;

        // Fingerprinting: URL/script patterns with weights (name, weight, points, detailSuffix)
        private static readonly (string Pattern, double Weight, int Points, string Detail)[] FingerprintPatterns =
        {
            ("fingerprintjs", 2.0, 48, "FingerprintJS library."),
            ("fingerprint", 1.8, 42, "Fingerprint-related script/URL."),
            ("fp.js", 1.9, 44, "Fingerprint script (fp.js)."),
            ("canvas fingerprint", 1.9, 44, "Canvas fingerprinting."),
            ("webgl fingerprint", 1.9, 44, "WebGL fingerprinting."),
            ("getcontext", 1.5, 35, "Canvas/WebGL getContext usage."),
            ("canvas", 1.4, 32, "Canvas API (fingerprinting risk)."),
            ("webgl", 1.4, 32, "WebGL (fingerprinting risk)."),
            ("audioctx", 1.6, 38, "AudioContext (audio fingerprinting)."),
            ("audiocontext", 1.6, 38, "AudioContext fingerprinting."),
            ("client-hints", 1.5, 36, "Client hints (high-entropy)."),
            ("entropy", 1.4, 32, "Entropy/fingerprint collection."),
            ("deviceid", 1.5, 35, "Device ID collection."),
            ("browserid", 1.5, 35, "Browser ID collection."),
            ("evercookie", 2.0, 48, "Evercookie persistence."),
            ("creepjs", 1.7, 40, "CreepJS fingerprinting."),
            ("amiunique", 1.6, 38, "AmIUnique-style fingerprinting.")
        };

        private static readonly (string Pattern, double Weight, int Points)[] KnownTrackerUrlPatterns =
        {
            ("google-analytics", 1.6, 36),
            ("googletagmanager", 1.7, 38),
            ("doubleclick", 1.8, 40),
            ("facebook.com/tr", 1.7, 38),
            ("pixel.", 1.4, 30),
            ("analytics", 1.3, 28),
            ("segment", 1.4, 30),
            ("mixpanel", 1.4, 30),
            ("hotjar", 1.5, 32),
            ("fullstory", 1.5, 32),
            ("mouseflow", 1.4, 30),
            ("crazyegg", 1.4, 30),
            ("clarity.ms", 1.5, 32),
            ("newrelic", 1.3, 26),
            ("sentry.io", 1.2, 24)
        };

        private static readonly string[] TrackingHeaderNames =
        {
            "x-tracking-id", "x-ad-id", "x-ga-client-id", "x-fbpr", "x-pinterest-tag",
            "x-mp-anonymous-id", "x-amzn-trace-id", "x-requested-with"
        };

        /// <summary>Compute risk from request entry and optional response cookie/header context.</summary>
        public static RiskResult Compute(
            RequestEntry entry,
            IReadOnlyDictionary<string, string>? responseHeaders = null,
            IReadOnlyList<string>? runtimeFingerprintSignals = null,
            IFingerprintPatternProvider? patternProvider = null)
        {
            if (entry == null)
                return new RiskResult(0, "Low", "Unknown", "No request data.");
            var factors = new List<(string Factor, double Weight, int Points, string Detail)>();
            double weightedSum = 0;
            double weightSum = 0;

            // Runtime fingerprint signals (e.g. from injected JS: canvas, webgl, audioctx)
            if (runtimeFingerprintSignals != null && runtimeFingerprintSignals.Count > 0)
            {
                foreach (var signal in runtimeFingerprintSignals)
                {
                    var s = (signal ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(s)) continue;
                    int pts = s switch
                    {
                        "canvas" => 38,
                        "webgl" => 38,
                        "audioctx" or "audiocontext" => 36,
                        "evercookie" => 45,
                        "client-hints" => 34,
                        _ => 30
                    };
                    factors.Add(("Runtime FP", 1.6, pts, $"Runtime API: {signal}."));
                    weightedSum += pts * 1.6;
                    weightSum += 1.6;
                }
            }

            // Third-party (base weight 1.2)
            if (entry.IsThirdParty)
            {
                factors.Add(("Third-party", 1.2, 22, "Request domain differs from page origin."));
                weightedSum += 22 * 1.2;
                weightSum += 1.2;
            }

            // Known tracker label (high weight 1.8)
            if (!string.IsNullOrEmpty(entry.TrackerLabel))
            {
                factors.Add(("Known tracker", 1.8, 38, $"Matches tracker: {entry.TrackerLabel}."));
                weightedSum += 38 * 1.8;
                weightSum += 1.8;
            }
            else if (entry.ThreatConfidence > 0.2)
            {
                var pts = (int)(entry.ThreatConfidence * 30);
                factors.Add(("Heuristic tracker", 1.4, Math.Min(35, pts), $"Confidence {entry.ThreatConfidence:P0}."));
                weightedSum += pts * 1.4;
                weightSum += 1.4;
            }

            // Fingerprinting: multi-pattern with best match
            bool fpSignal = entry.Signals?.Any(s =>
                string.Equals(s.SignalType, "fingerprint", StringComparison.OrdinalIgnoreCase) ||
                s.Risk == RiskType.Fingerprinting) ?? false;
            if (fpSignal)
            {
                factors.Add(("Fingerprinting (signal)", 2.0, 45, "Detection signal indicates fingerprinting."));
                weightedSum += 45 * 2.0;
                weightSum += 2.0;
            }
            bool fpUrlMatch = false;
            if (entry.FullUrl != null)
            {
                var fpPatterns = EnumerateFingerprintPatterns(patternProvider);
                foreach (var (pattern, weight, points, detail) in fpPatterns)
                {
                    if (entry.FullUrl.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        factors.Add(("Fingerprinting", weight, points, detail));
                        weightedSum += points * weight;
                        weightSum += weight;
                        fpUrlMatch = true;
                        break;
                    }
                }
                var trackerPatterns = EnumerateTrackerUrlPatterns(patternProvider);
                foreach (var (pattern, weight, points) in trackerPatterns)
                {
                    if (entry.FullUrl.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        factors.Add(("Known tracker URL", weight, points, $"URL matches: {pattern}."));
                        weightedSum += points * weight;
                        weightSum += weight;
                        break;
                    }
                }
            }

            // Cookie analysis from response headers
            if (responseHeaders != null)
            {
                CookieRisk(responseHeaders, entry.IsThirdParty, factors, ref weightedSum, ref weightSum);
                HeaderRisk(responseHeaders, entry.RequestHeaders, entry.IsThirdParty, factors, ref weightedSum, ref weightSum);
            }

            // Tracking params / signals
            if (entry.TrackingParams?.Count > 0)
            {
                factors.Add(("Tracking params", 1.2, 15, $"Params: {string.Join(", ", entry.TrackingParams.Take(3))}."));
                weightedSum += 15 * 1.2;
                weightSum += 1.2;
            }

            // Normalize to 0–100 with weighted average
            int score = weightSum > 0
                ? (int)Math.Round(Math.Min(MaxScore, weightedSum / weightSum + (weightedSum / weightSum) * 0.15 * Math.Min(3, factors.Count)))
                : 0;
            score = Math.Clamp(score, 0, MaxScore);

            string level = score >= 85 ? "Critical" : score >= 70 ? "High" : score >= 40 ? "Medium" : "Low";
            string category = entry.IsThirdParty ? "Third-party" : "First-party";
            if (!string.IsNullOrEmpty(entry.TrackerLabel) || (entry.ThreatConfidence > 0.3))
                category = "Tracker";
            if (fpSignal || fpUrlMatch)
                category = "Fingerprinting";

            var explanation = string.Join(" ", factors.Select(f => $"[{f.Factor}] {f.Detail}"));
            if (string.IsNullOrEmpty(explanation))
                explanation = "First-party or low-risk resource; no significant signals.";

            return new RiskResult(score, level, category, explanation);
        }

        private static IEnumerable<(string Pattern, double Weight, int Points, string Detail)> EnumerateFingerprintPatterns(IFingerprintPatternProvider? provider)
        {
            foreach (var t in FingerprintPatterns)
                yield return t;
            if (provider != null)
            {
                var extra = provider.GetFingerprintPatterns();
                if (extra != null)
                {
                    foreach (var t in extra)
                        yield return t;
                }
            }
        }

        private static IEnumerable<(string Pattern, double Weight, int Points)> EnumerateTrackerUrlPatterns(IFingerprintPatternProvider? provider)
        {
            foreach (var t in KnownTrackerUrlPatterns)
                yield return t;
            if (provider != null)
            {
                var extra = provider.GetTrackerUrlPatterns();
                if (extra != null)
                {
                    foreach (var t in extra)
                        yield return t;
                }
            }
        }

        private static void CookieRisk(
            IReadOnlyDictionary<string, string> responseHeaders,
            bool isThirdParty,
            List<(string, double, int, string)> factors,
            ref double weightedSum,
            ref double weightSum)
        {
            if (!responseHeaders.TryGetValue("set-cookie", out var setCookie) || string.IsNullOrWhiteSpace(setCookie))
                return;

            var parts = setCookie.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (parts.Count == 0) return;
            var flags = new HashSet<string>(parts.Select(p => p.Split('=')[0].Trim()), StringComparer.OrdinalIgnoreCase);

            bool missingSecure = !flags.Contains("Secure");
            bool missingHttpOnly = !flags.Contains("HttpOnly");
            bool sameSiteNone = parts.Any(p => p.StartsWith("SameSite=None", StringComparison.OrdinalIgnoreCase));
            bool sameSiteNoneWithoutSecure = sameSiteNone && missingSecure;

            if (missingSecure)
            {
                factors.Add(("Cookie missing Secure", 1.1, 8, "Cookie can be sent over HTTP."));
                weightedSum += 8 * 1.1;
                weightSum += 1.1;
            }
            if (missingHttpOnly)
            {
                factors.Add(("Cookie missing HttpOnly", 1.0, 5, "Accessible to scripts."));
                weightedSum += 5 * 1.0;
                weightSum += 1.0;
            }
            if (sameSiteNoneWithoutSecure)
            {
                factors.Add(("SameSite=None without Secure", 1.5, 18, "Cross-site cookie without Secure is invalid but often used for tracking."));
                weightedSum += 18 * 1.5;
                weightSum += 1.5;
            }
            if (isThirdParty && (sameSiteNone || sameSiteNoneWithoutSecure))
            {
                factors.Add(("Third-party cookie", 1.3, 12, "Third-party cookie increases tracking risk."));
                weightedSum += 12 * 1.3;
                weightSum += 1.3;
            }
        }

        private static void HeaderRisk(
            IReadOnlyDictionary<string, string> responseHeaders,
            IReadOnlyDictionary<string, string>? requestHeaders,
            bool isThirdParty,
            List<(string, double, int, string)> factors,
            ref double weightedSum,
            ref double weightSum)
        {
            foreach (var name in TrackingHeaderNames)
            {
                if (responseHeaders.TryGetValue(name, out _) || (requestHeaders?.TryGetValue(name, out _) == true))
                {
                    factors.Add(("Tracking header", 1.2, 10, $"Header present: {name}."));
                    weightedSum += 10 * 1.2;
                    weightSum += 1.2;
                    break;
                }
            }

            if (requestHeaders != null && requestHeaders.TryGetValue("referer", out var referer) && !string.IsNullOrEmpty(referer) && isThirdParty)
            {
                factors.Add(("Referrer leakage", 1.1, 8, "Referrer sent on third-party request."));
                weightedSum += 8 * 1.1;
                weightSum += 1.1;
            }
        }

        /// <summary>Apply computed result to an InterceptedRequestItem (for service use).</summary>
        public static void Apply(InterceptedRequestItem item, RequestEntry entry,
            IReadOnlyDictionary<string, string>? responseHeaders = null,
            IReadOnlyList<string>? runtimeFingerprintSignals = null,
            IFingerprintPatternProvider? patternProvider = null)
        {
            var result = Compute(entry, responseHeaders, runtimeFingerprintSignals, patternProvider);
            item.RiskScore = result.Score;
            item.RiskLevel = result.Level;
            item.Category = result.Category;
            item.RiskExplanation = result.Explanation;
            item.IsTracker = !string.IsNullOrEmpty(entry.TrackerLabel) || entry.ThreatConfidence > 0.2;
        }
    }

    public readonly struct RiskResult
    {
        public int Score { get; }
        public string Level { get; }
        public string Category { get; }
        public string Explanation { get; }

        public RiskResult(int score, string level, string category, string explanation)
        {
            Score = score;
            Level = level;
            Category = category;
            Explanation = explanation;
        }
    }
}
