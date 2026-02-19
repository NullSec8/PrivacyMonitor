using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyMonitor
{
    /// <summary>
    /// Centralizes weights and risk boundaries for privacy scoring. 
    /// Update to expand supported risk categories/scoring in future.
    /// </summary>
    public static class PrivacyImpactConfig
    {
        public static readonly int UniqueTrackersWeight = 5;
        public static readonly int TrackingCookiesWeight = 3;
        public static readonly int ThirdPartyRequestsWeight = 1;
        public static readonly int NewCompaniesWeight = 4;
        // Future weights: public static int FingerprintingWeight = 10;
        // Add more weights here for extensibility.

        // Risk boundaries
        public static readonly int LowRiskUpper = 10;
        public static readonly int ModerateRiskUpper = 30;
        public static readonly int HighRiskUpper = 70;
        // >HighRiskUpper is Critical
    }

    /// <summary>
    /// Represents a differential privacy and threat report between two scans.
    /// Designed for extensibility (more metrics/risk dimensions can be added non-breaking).
    /// </summary>
    public sealed class ThreatDiff
    {
        public int ExtraRequests { get; init; }
        public int ExtraThirdParty { get; init; }
        public int ExtraTrackers { get; init; } // Unique tracker label/domain
        public int ExtraTrackingCookies { get; init; }
        public int ExtraStorageKeys { get; init; } // Unique storage keys (see ComputeStorageDifference)
        public int ExtraCompanies { get; init; }   // New tracker companies (unique)
        public List<string> NewTrackerLabels { get; init; } = new();
        public List<string> NewCompanies { get; init; } = new();
        public List<string> NewStorageKeys { get; init; } = new();

        // Extensible: Future risk metrics & categories
        public int ExtraFingerprintingVectors { get; init; } = 0;    // e.g., future
        public int ExtraCanvasAccesses { get; init; } = 0;           // e.g., future
        public int ExtraWebRTCLeaks { get; init; } = 0;              // e.g., future

        public int PrivacyImpactScore { get; init; }
        public string ProtectionGrade { get; init; }
    }

    public static class ThreatSimulation
    {
        /// <summary>
        /// Compare an unprotected scan against a protected scan.
        /// "unprotected" should be Monitor mode or blocking disabled; "protected" is with blocking on.
        /// Pure function: returns analysis result. No IO or static state.
        /// </summary>
        public static ThreatDiff Compare(ScanResult unprotected, ScanResult protectedScan)
        {
            // Defensive null handling
            var unprotReqs = unprotected?.Requests ?? Enumerable.Empty<Request>();
            var protReqs = protectedScan?.Requests ?? Enumerable.Empty<Request>();
            var unprotStorage = unprotected?.Storage ?? Enumerable.Empty<StorageKey>();
            var protStorage = protectedScan?.Storage ?? Enumerable.Empty<StorageKey>();

            int extraRequests = Math.Max(0, unprotReqs.Count() - protReqs.Count());
            int extraThirdParty = Math.Max(
                0,
                unprotReqs.Count(r => r != null && r.IsThirdParty) - protReqs.Count(r => r != null && r.IsThirdParty)
            );

            var (uniqueRemovedTrackers, removedTrackerLabels) = ComputeUniqueTrackers(unprotReqs, protReqs);
            int extraTrackers = uniqueRemovedTrackers;

            int extraTrackingCookies = Math.Max(
                0,
                PrivacyEngine.CountAllTrackingCookies(unprotected) - PrivacyEngine.CountAllTrackingCookies(protectedScan)
            );

            var (diffStorage, diffStorageKeys) = ComputeStorageDifference(unprotStorage, protStorage);
            int extraStorageKeys = diffStorage;

            var (uniqueRemovedCompanies, removedCompanies) = ComputeNewCompanies(unprotReqs, protReqs);
            int extraCompanies = uniqueRemovedCompanies;

            int privacyImpactScore = ComputePrivacyImpactScore(
                uniqueRemovedTrackers, 
                extraTrackingCookies, 
                extraThirdParty, 
                uniqueRemovedCompanies
                // extensible: add future metrics here
            );
            string protectionGrade = ComputeProtectionGrade(privacyImpactScore);

            // Extensible: Prepare fields for future metrics
            // int extraFingerprintingVectors = ComputeExtraFingerprinting(unprotReqs, protReqs);

            return new ThreatDiff
            {
                ExtraRequests = extraRequests,
                ExtraThirdParty = extraThirdParty,
                ExtraTrackers = extraTrackers,
                ExtraTrackingCookies = extraTrackingCookies,
                ExtraStorageKeys = extraStorageKeys,
                ExtraCompanies = extraCompanies,
                NewTrackerLabels = removedTrackerLabels,
                NewCompanies = removedCompanies,
                NewStorageKeys = diffStorageKeys,
                PrivacyImpactScore = privacyImpactScore,
                ProtectionGrade = protectionGrade,
                //ExtraFingerprintingVectors = extraFingerprintingVectors // for future
            };
        }

        internal static (int uniqueCount, List<string> diffLabels) ComputeUniqueTrackers(
            IEnumerable<Request> unprotectedReqs,
            IEnumerable<Request> protectedReqs)
        {
            // Project unique tracker labels or domains
            var unprotLabels = new HashSet<string>(
                unprotectedReqs
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.TrackerLabel))
                    .Select(r => r.TrackerLabel),
                StringComparer.OrdinalIgnoreCase);

            var protLabels = new HashSet<string>(
                protectedReqs
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.TrackerLabel))
                    .Select(r => r.TrackerLabel),
                StringComparer.OrdinalIgnoreCase);

            // Unique tracker labels present only in unprotected set
            var diff = unprotLabels.Except(protLabels, StringComparer.OrdinalIgnoreCase).ToList();
            return (diff.Count, diff);
        }

        internal static (int uniqueCount, List<string> diffCompanies) ComputeNewCompanies(
            IEnumerable<Request> unprotectedReqs,
            IEnumerable<Request> protectedReqs)
        {
            var unprotCompanies = new HashSet<string>(
                unprotectedReqs
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.TrackerCompany))
                    .Select(r => r.TrackerCompany),
                StringComparer.OrdinalIgnoreCase);

            var protCompanies = new HashSet<string>(
                protectedReqs
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.TrackerCompany))
                    .Select(r => r.TrackerCompany),
                StringComparer.OrdinalIgnoreCase);

            var diff = unprotCompanies.Except(protCompanies, StringComparer.OrdinalIgnoreCase).ToList();
            return (diff.Count, diff);
        }

        /// <summary>
        /// Computes unique storage keys present in unprotected but not protected.
        /// TODO: Add filter parameter to restrict to tracking-related keys when desired.
        /// </summary>
        internal static (int uniqueCount, List<string> newKeys) ComputeStorageDifference(
            IEnumerable<StorageKey> unprotectedStorage,
            IEnumerable<StorageKey> protectedStorage
        )
        {
            // Defensive: null guard and use key identity or value. Adapt key extractor as needed.
            Func<StorageKey, string> keySelector = k => k?.Key ?? "";

            var unprotKeys = new HashSet<string>(
                unprotectedStorage.Where(k => k != null && !string.IsNullOrWhiteSpace(keySelector(k))).Select(keySelector),
                StringComparer.OrdinalIgnoreCase);

            var protKeys = new HashSet<string>(
                protectedStorage.Where(k => k != null && !string.IsNullOrWhiteSpace(keySelector(k))).Select(keySelector),
                StringComparer.OrdinalIgnoreCase);

            var diff = unprotKeys.Except(protKeys, StringComparer.OrdinalIgnoreCase).ToList();
            return (diff.Count, diff);
        }

        /// <summary>
        /// Computes the privacy impact score based on current configuration weights.
        /// Future: Add extra dimensions by overloading and/or adding params.
        /// </summary>
        internal static int ComputePrivacyImpactScore(
            int uniqueTrackers,
            int trackingCookies,
            int thirdPartyRequests,
            int newCompanies
            // extensible params...
        )
        {
            int score =
                Math.Max(0, uniqueTrackers) * PrivacyImpactConfig.UniqueTrackersWeight +
                Math.Max(0, trackingCookies) * PrivacyImpactConfig.TrackingCookiesWeight +
                Math.Max(0, thirdPartyRequests) * PrivacyImpactConfig.ThirdPartyRequestsWeight +
                Math.Max(0, newCompanies) * PrivacyImpactConfig.NewCompaniesWeight;
            // + future risk metrics * their weights...
            return score;
        }

        /// <summary>
        /// Computes protection grade by score, using configured boundaries.
        /// Extensible: Use enum or structured return in future.
        /// </summary>
        internal static string ComputeProtectionGrade(int privacyImpactScore)
        {
            if (privacyImpactScore <= PrivacyImpactConfig.LowRiskUpper)
                return "Low Risk";
            if (privacyImpactScore <= PrivacyImpactConfig.ModerateRiskUpper)
                return "Moderate Risk";
            if (privacyImpactScore <= PrivacyImpactConfig.HighRiskUpper)
                return "High Risk";
            return "Critical Risk";
        }
    }
}
