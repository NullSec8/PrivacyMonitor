using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Defines a provider interface for supplying custom fingerprint and tracker URL patterns.
    /// Enables extending RiskScoring heuristics with dynamically loaded patterns.
    /// </summary>
    public interface IFingerprintPatternProvider
    {
        /// <summary>
        /// Returns additional fingerprint detection patterns.
        /// Tuple: (Pattern, Weight, Points, Detail).
        /// </summary>
        IEnumerable<(string Pattern, double Weight, int Points, string Detail)> GetFingerprintPatterns();

        /// <summary>
        /// Returns additional known tracker URL patterns.
        /// Tuple: (Pattern, Weight, Points).
        /// </summary>
        IEnumerable<(string Pattern, double Weight, int Points)> GetTrackerUrlPatterns();
    }
}
