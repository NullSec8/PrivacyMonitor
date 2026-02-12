using System;
using System.Collections.Generic;
using System.Linq;

namespace PrivacyMonitor
{
    /// <summary>
    /// Compares two ScanResult snapshots (e.g. protection OFF vs ON) to show what protection removed.
    /// Pure analysis – does not perform navigation itself.
    /// </summary>
    public sealed class ThreatDiff
    {
        public int ExtraRequests { get; init; }
        public int ExtraThirdParty { get; init; }
        public int ExtraTrackers { get; init; }
        public int ExtraTrackingCookies { get; init; }
        public int ExtraStorageKeys { get; init; }
        public int ExtraCompanies { get; init; }
        public List<string> NewTrackerLabels { get; init; } = new();
        public List<string> NewCompanies { get; init; } = new();
    }

    public static class ThreatSimulation
    {
        /// <summary>
        /// Compare an unprotected scan against a protected scan.
        /// "unprotected" should be Monitor mode or blocking disabled; "protected" is with blocking on.
        /// </summary>
        public static ThreatDiff Compare(ScanResult unprotected, ScanResult protectedScan)
        {
            var unprotReqs = unprotected.Requests;
            var protReqs = protectedScan.Requests;

            int extraReqs = Math.Max(0, unprotReqs.Count - protReqs.Count);
            int extraThird = Math.Max(0,
                unprotReqs.Count(r => r.IsThirdParty) -
                protReqs.Count(r => r.IsThirdParty));

            var unprotTrackers = new HashSet<string>(
                unprotReqs.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.TrackerLabel),
                StringComparer.OrdinalIgnoreCase);
            var protTrackers = new HashSet<string>(
                protReqs.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.TrackerLabel),
                StringComparer.OrdinalIgnoreCase);

            var newTrackers = unprotTrackers.Except(protTrackers, StringComparer.OrdinalIgnoreCase).ToList();

            int extraTrackingCookies = Math.Max(
                0,
                PrivacyEngine.CountAllTrackingCookies(unprotected) -
                PrivacyEngine.CountAllTrackingCookies(protectedScan));

            int extraStorage = Math.Max(0, unprotected.Storage.Count - protectedScan.Storage.Count);

            var unprotCompanies = new HashSet<string>(
                unprotReqs.Where(r => !string.IsNullOrEmpty(r.TrackerCompany)).Select(r => r.TrackerCompany),
                StringComparer.OrdinalIgnoreCase);
            var protCompanies = new HashSet<string>(
                protReqs.Where(r => !string.IsNullOrEmpty(r.TrackerCompany)).Select(r => r.TrackerCompany),
                StringComparer.OrdinalIgnoreCase);
            var newCompanies = unprotCompanies.Except(protCompanies, StringComparer.OrdinalIgnoreCase).ToList();

            return new ThreatDiff
            {
                ExtraRequests = extraReqs,
                ExtraThirdParty = extraThird,
                ExtraTrackers = Math.Max(0,
                    unprotReqs.Count(r => !string.IsNullOrEmpty(r.TrackerLabel)) -
                    protReqs.Count(r => !string.IsNullOrEmpty(r.TrackerLabel))),
                ExtraTrackingCookies = extraTrackingCookies,
                ExtraStorageKeys = extraStorage,
                ExtraCompanies = newCompanies.Count,
                NewTrackerLabels = newTrackers,
                NewCompanies = newCompanies
            };
        }
    }
}

