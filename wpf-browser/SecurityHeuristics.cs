using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyMonitor
{
    public enum SuspicionLevel
    {
        Low,
        Medium,
        High
    }

    public enum CredentialSafety
    {
        Safe,
        Caution,
        Unsafe
    }

    public sealed class SuspicionAssessment
    {
        public SuspicionLevel Level { get; init; }
        public List<string> Reasons { get; init; } = new();
    }

    public sealed class CredentialSafetyResult
    {
        public CredentialSafety Level { get; init; }
        public string Explanation { get; init; } = "";
    }

    /// <summary>
    /// Lightweight heuristics for phishing / malware suspicion and credential safety.
    /// Purely based on ScanResult; no UI dependencies.
    /// </summary>
    public static class SecurityHeuristics
    {
        public static SuspicionAssessment AssessSuspicion(ScanResult scan)
        {
            var reasons = new List<string>();
            int score = 0;

            // Many third-party domains + trackers
            int uniqueTpDomains = scan.Requests.Where(r => r.IsThirdParty).Select(r => r.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int trackerHosts = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (uniqueTpDomains >= 15)
            {
                score += 2;
                reasons.Add("Large number of external companies contacted on this page.");
            }
            else if (uniqueTpDomains >= 8)
            {
                score += 1;
                reasons.Add("Several external companies contacted on this page.");
            }

            if (trackerHosts >= 5)
            {
                score += 2;
                reasons.Add("Multiple dedicated tracking/ad services present.");
            }

            // Weak security headers
            int missingHsts = scan.SecurityHeaders.Count(h => string.Equals(h.Header, "Strict-Transport-Security", StringComparison.OrdinalIgnoreCase) && h.Status == "Missing");
            int missingCsp = scan.SecurityHeaders.Count(h => string.Equals(h.Header, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase) && h.Status == "Missing");
            int weakHeaders = scan.SecurityHeaders.Count(h => h.Status == "Weak");
            if (missingHsts > 0)
            {
                score += 1;
                reasons.Add("Site does not enforce HTTPS strictly (HSTS missing).");
            }
            if (missingCsp > 0)
            {
                score += 1;
                reasons.Add("No Content-Security-Policy header; easier for injected scripts to run.");
            }
            if (weakHeaders > 0)
            {
                score += 1;
                reasons.Add("Some security headers are weakly configured.");
            }

            // WebRTC leaks
            if (scan.WebRtcLeaks.Count > 0)
            {
                score += 1;
                reasons.Add("Page can see your real IP address via WebRTC.");
            }

            // Third-party POSTs (potential credential or form exfil)
            int postThirdParty = scan.Requests.Count(r => r.IsThirdParty && r.HasBody);
            if (postThirdParty >= 3)
            {
                score += 2;
                reasons.Add("Multiple form submissions or data posts to third-party servers.");
            }
            else if (postThirdParty > 0)
            {
                score += 1;
                reasons.Add("Some form data is sent to third-party servers.");
            }

            SuspicionLevel level = score switch
            {
                <= 2 => SuspicionLevel.Low,
                <= 5 => SuspicionLevel.Medium,
                _ => SuspicionLevel.High
            };

            return new SuspicionAssessment
            {
                Level = level,
                Reasons = reasons
            };
        }

        public static CredentialSafetyResult AssessCredentialSafety(ScanResult scan)
        {
            // Basic heuristic: HTTPS + decent headers + limited trackers => safer.
            bool anyHttp = scan.TargetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            int trackers = scan.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel));
            int postThirdParty = scan.Requests.Count(r => r.IsThirdParty && r.HasBody);
            bool hstsPresent = scan.SecurityHeaders.Any(h => string.Equals(h.Header, "Strict-Transport-Security", StringComparison.OrdinalIgnoreCase) && h.Status == "Present");
            bool cspPresent = scan.SecurityHeaders.Any(h => string.Equals(h.Header, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase) && h.Status == "Present");

            // Unsafe: HTTP, many trackers, or form posts to third parties + weak headers
            if (anyHttp || (postThirdParty > 0 && !hstsPresent))
            {
                return new CredentialSafetyResult
                {
                    Level = CredentialSafety.Unsafe,
                    Explanation = "Avoid entering passwords or payment data here. Either the connection is not fully secured or form data is sent to third-party servers."
                };
            }

            // Caution when lots of trackers or data posts exist, even over HTTPS
            if (trackers >= 5 || postThirdParty > 0 || !cspPresent)
            {
                return new CredentialSafetyResult
                {
                    Level = CredentialSafety.Caution,
                    Explanation = "Connection is encrypted, but this page has significant tracking or sends data to external services. Use extra caution with sensitive credentials."
                };
            }

            return new CredentialSafetyResult
            {
                Level = CredentialSafety.Safe,
                Explanation = "Connection is encrypted and we saw no strong signs of risky data flows. Still, only enter sensitive data on sites you trust."
            };
        }
    }
}

