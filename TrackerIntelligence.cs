using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrivacyMonitor
{
    /// <summary>
    /// Global tracker intelligence store.
    /// Tracks how often third-party tracker domains appear across different first-party sites.
    /// Designed to be fast for per-request updates with occasional, throttled persistence to disk.
    /// </summary>
    public static class TrackerIntelligence
    {
        private sealed class TrackerObservationRecord
        {
            public string TrackerDomain { get; set; } = "";
            public string FirstPartySite { get; set; } = "";
            public DateTime FirstSeenUtc { get; set; }
            public DateTime LastSeenUtc { get; set; }
            public int TotalHits { get; set; }
        }

        private sealed class TrackerIntelFile
        {
            public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
            public List<TrackerObservationRecord> Entries { get; set; } = new();
        }

        public sealed class TrackerStats
        {
            public string TrackerDomain { get; set; } = "";
            public int UniqueFirstPartySites { get; set; }
            public int TotalHits { get; set; }
        }

        private static readonly ConcurrentDictionary<(string Tracker, string Site), TrackerObservationRecord> _observations = new();
        private static readonly object _saveLock = new();
        private static readonly string TrackerIntelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "tracker-intel.json");

        private static DateTime _lastSaveUtc = DateTime.MinValue;
        private static bool _loaded;

        static TrackerIntelligence()
        {
            Load();
        }

        /// <summary>
        /// Record that a tracker domain was observed on a given first-party site.
        /// Safe to call from hot paths; does only in-memory updates and infrequent disk writes.
        /// </summary>
        public static void RecordObservation(string trackerDomain, string firstPartySite)
        {
            if (string.IsNullOrWhiteSpace(trackerDomain) || string.IsNullOrWhiteSpace(firstPartySite))
                return;

            trackerDomain = trackerDomain.Trim().ToLowerInvariant();
            firstPartySite = firstPartySite.Trim().ToLowerInvariant();

            var now = DateTime.UtcNow;
            var key = (Tracker: trackerDomain, Site: firstPartySite);

            _observations.AddOrUpdate(
                key,
                _ => new TrackerObservationRecord
                {
                    TrackerDomain = trackerDomain,
                    FirstPartySite = firstPartySite,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    TotalHits = 1
                },
                (_, existing) =>
                {
                    existing.LastSeenUtc = now;
                    existing.TotalHits++;
                    return existing;
                });

            MaybeSave(now);
        }

        /// <summary>
        /// Returns the most frequently seen trackers across all first-party sites.
        /// Ordered by number of unique sites, then total hits.
        /// </summary>
        public static IReadOnlyList<TrackerStats> GetTopTrackers(int limit = 50)
        {
            if (limit <= 0) limit = 50;

            var snapshot = _observations.Values.ToArray();
            var grouped = snapshot
                .GroupBy(o => o.TrackerDomain)
                .Select(g => new TrackerStats
                {
                    TrackerDomain = g.Key,
                    UniqueFirstPartySites = g.Select(o => o.FirstPartySite).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    TotalHits = g.Sum(o => o.TotalHits)
                })
                .OrderByDescending(s => s.UniqueFirstPartySites)
                .ThenByDescending(s => s.TotalHits)
                .Take(limit)
                .ToList();

            return grouped;
        }

        /// <summary>
        /// Returns aggregate stats for a single tracker domain, or null if unseen.
        /// </summary>
        public static TrackerStats? GetTrackerStats(string trackerDomain)
        {
            if (string.IsNullOrWhiteSpace(trackerDomain)) return null;
            trackerDomain = trackerDomain.Trim().ToLowerInvariant();

            var snapshot = _observations.Values
                .Where(o => string.Equals(o.TrackerDomain, trackerDomain, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (snapshot.Length == 0) return null;

            return new TrackerStats
            {
                TrackerDomain = trackerDomain,
                UniqueFirstPartySites = snapshot.Select(o => o.FirstPartySite).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalHits = snapshot.Sum(o => o.TotalHits)
            };
        }

        private static void Load()
        {
            if (_loaded)
                return;

            _loaded = true;

            try
            {
                if (!File.Exists(TrackerIntelPath))
                    return;

                var json = File.ReadAllText(TrackerIntelPath);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var file = JsonSerializer.Deserialize<TrackerIntelFile>(json);
                if (file?.Entries == null)
                    return;

                foreach (var e in file.Entries)
                {
                    if (string.IsNullOrWhiteSpace(e.TrackerDomain) || string.IsNullOrWhiteSpace(e.FirstPartySite))
                        continue;

                    var key = (Tracker: e.TrackerDomain.Trim().ToLowerInvariant(),
                               Site: e.FirstPartySite.Trim().ToLowerInvariant());
                    _observations[key] = e;
                }
            }
            catch
            {
                // Corrupt file or other IO issue – start fresh in memory.
            }
        }

        private static void MaybeSave(DateTime nowUtc)
        {
            // Throttle disk writes to at most once every 30 seconds.
            if ((nowUtc - _lastSaveUtc).TotalSeconds < 30)
                return;

            lock (_saveLock)
            {
                if ((nowUtc - _lastSaveUtc).TotalSeconds < 30)
                    return;

                try
                {
                    var dir = Path.GetDirectoryName(TrackerIntelPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var snapshot = _observations.Values.ToArray();
                    var file = new TrackerIntelFile
                    {
                        UpdatedAt = nowUtc.ToString("o"),
                        Entries = snapshot.ToList()
                    };

                    var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(TrackerIntelPath, json);
                    _lastSaveUtc = nowUtc;
                }
                catch
                {
                    // Ignore persistence failures; in-memory data still available for the session.
                }
            }
        }
    }
}

