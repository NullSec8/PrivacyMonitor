using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Optional plugin to supply additional fingerprint and tracker URL patterns for RiskScoring.
    /// Enables dynamic loading of new patterns without changing core code.
    /// </summary>
    public interface IFingerprintPatternProvider
    {
        /// <summary>Additional fingerprint URL/script patterns (pattern, weight, points, detail).</summary>
        IEnumerable<(string Pattern, double Weight, int Points, string Detail)> GetFingerprintPatterns();

        /// <summary>Additional known tracker URL patterns (pattern, weight, points).</summary>
        IEnumerable<(string Pattern, double Weight, int Points)> GetTrackerUrlPatterns();
    }
}
