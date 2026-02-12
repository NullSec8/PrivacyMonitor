using System.Collections.Generic;
using System.Linq;

namespace PrivacyMonitor
{
    /// <summary>
    /// Simple educational lesson cards built from a ScanResult.
    /// These are used in reports / UI to teach key privacy concepts.
    /// </summary>
    public sealed class LessonCard
    {
        public string Title { get; init; } = "";
        public string What { get; init; } = "";
        public string WhyItMatters { get; init; } = "";
        public string WhereToLook { get; init; } = "";
    }

    public static class LessonEngine
    {
        public static List<LessonCard> BuildLessons(ScanResult scan)
        {
            var lessons = new List<LessonCard>();

            int trackers = scan.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel));
            int uniqueCompanies = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerCompany))
                .Select(r => r.TrackerCompany).Distinct().Count();
            int fpCount = scan.Fingerprints.Count;
            int trackingCookies = PrivacyEngine.CountAllTrackingCookies(scan);

            if (trackers > 0)
            {
                lessons.Add(new LessonCard
                {
                    Title = "How third-party trackers follow you",
                    What = "This page loads code from advertising and analytics companies that can see which pages you visit and how you interact.",
                    WhyItMatters = "When the same tracker appears on many sites, it can build a long-term profile of your interests and behaviour.",
                    WhereToLook = "See the \"Who's on this page?\" section and the company data flow analysis for names like Google, Meta, and data brokers."
                });
            }

            if (fpCount > 0)
            {
                lessons.Add(new LessonCard
                {
                    Title = "What browser fingerprinting is",
                    What = "Fingerprinting combines many small details (screen size, fonts, canvas, WebGL) to recognise your device without cookies.",
                    WhyItMatters = "A unique fingerprint lets trackers recognise you even if you clear cookies or use private windows.",
                    WhereToLook = "Open the Fingerprint panel or the fingerprinting section of this report to see which techniques were detected."
                });
            }

            if (trackingCookies > 0 || scan.Storage.Count > 0)
            {
                lessons.Add(new LessonCard
                {
                    Title = "Why cookies and storage matter",
                    What = "Cookies and web storage entries can persist IDs and preferences across visits, often for months or years.",
                    WhyItMatters = "Long-lived identifiers make it easier to link visits across time and across different sites.",
                    WhereToLook = "Check the Storage panel and the cookies section of this report for tracking cookies and storage keys."
                });
            }

            if (scan.SecurityHeaders.Any(h => h.Status == "Missing" || h.Status == "Weak"))
            {
                lessons.Add(new LessonCard
                {
                    Title = "Security headers and safe browsing",
                    What = "HTTP security headers like HSTS and CSP limit how much damage a compromised or malicious script can do.",
                    WhyItMatters = "Missing or weak headers make phishing pages and injected content harder to detect and contain.",
                    WhereToLook = "Open the Security panel or the security headers table in this report to see which protections are missing."
                });
            }

            return lessons;
        }
    }
}

