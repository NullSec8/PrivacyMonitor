using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PrivacyMonitor
{
    /// <summary>
    /// Global tracker intelligence store.
    /// Tracks how often third-party tracker domains appear across different first-party sites.
    /// Uses SQLite for persistence and crash-safe writes.
    /// </summary>
    public static class TrackerIntelligence
    {
        public sealed class TrackerStats
        {
            public string TrackerDomain { get; set; } = "";
            public int UniqueFirstPartySites { get; set; }
            public int TotalHits { get; set; }
        }

        private static readonly string TrackerDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrivacyMonitor", "tracker-intel.db");

        private static SqliteConnection? _conn;
        private static readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

        // Cleanup policy: 90 days
        private static readonly TimeSpan CleanupThreshold = TimeSpan.FromDays(90);
        private static bool _cleanupDone = false;
        private static bool _initialized = false;
        private static readonly object _initLock = new();

        /// <summary>
        /// Ensures DB and schema exist. Runs once per process.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            lock (_initLock)
            {
                if (_initialized)
                    return;

                var dir = Path.GetDirectoryName(TrackerDbPath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                _conn = new SqliteConnection($"Data Source={TrackerDbPath};Mode=ReadWriteCreate;Cache=Shared");
                _conn.Open();

                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"CREATE TABLE IF NOT EXISTS tracker_observations (
                            tracker_domain TEXT NOT NULL,
                            first_party_site TEXT NOT NULL,
                            first_seen_utc TEXT NOT NULL,
                            last_seen_utc TEXT NOT NULL,
                            total_hits INTEGER NOT NULL,
                            PRIMARY KEY (tracker_domain, first_party_site)
                        );
                        CREATE INDEX IF NOT EXISTS idx_tracker_domain ON tracker_observations(tracker_domain);
                        ";
                    cmd.ExecuteNonQuery();
                }

                _initialized = true;
            }
        }

        /// <summary>
        /// Record that a tracker domain was observed on a given first-party site.
        /// Thread-safe, uses SQLite with UPSERT and atomic transaction.
        /// </summary>
        public static void RecordObservation(string trackerDomain, string firstPartySite)
        {
            // For API symmetry, provide sync wrapper calling the async method.
            RecordObservationAsync(trackerDomain, firstPartySite).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async backing implementation.
        /// </summary>
        private static async Task RecordObservationAsync(string trackerDomain, string firstPartySite)
        {
            if (string.IsNullOrWhiteSpace(trackerDomain) || string.IsNullOrWhiteSpace(firstPartySite))
                return;
            trackerDomain = trackerDomain.Trim().ToLowerInvariant();
            firstPartySite = firstPartySite.Trim().ToLowerInvariant();

            EnsureInitialized();

            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var tx = _conn!.BeginTransaction())
                using (var cmd = _conn.CreateCommand())
                {
                    var nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    cmd.CommandText = @"
                        INSERT INTO tracker_observations
                            (tracker_domain, first_party_site, first_seen_utc, last_seen_utc, total_hits)
                        VALUES
                            ($tracker, $site, $now, $now, 1)
                        ON CONFLICT(tracker_domain, first_party_site) DO UPDATE SET
                            last_seen_utc = $now,
                            total_hits = total_hits + 1
                    ";
                    cmd.Parameters.AddWithValue("$tracker", trackerDomain);
                    cmd.Parameters.AddWithValue("$site", firstPartySite);
                    cmd.Parameters.AddWithValue("$now", nowIso);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    tx.Commit();
                }
                // Run automatic cleanup once per app session (~first write)
                if (!_cleanupDone)
                {
                    await CleanupOldEntriesAsync().ConfigureAwait(false);
                    _cleanupDone = true;
                }
            }
            catch
            {
                // handle/log if necessary, but fail gracefully for the caller
                // Optionally: log to your application logging here.
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>
        /// Returns the most frequently seen trackers across all first-party sites.
        /// Ordered by number of unique sites, then total hits.
        /// </summary>
        public static IReadOnlyList<TrackerStats> GetTopTrackers(int limit = 50)
        {
            return GetTopTrackersAsync(limit).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static async Task<IReadOnlyList<TrackerStats>> GetTopTrackersAsync(int limit)
        {
            if (limit <= 0) limit = 50;
            EnsureInitialized();
            var list = new List<TrackerStats>();

            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var cmd = _conn!.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            tracker_domain,
                            COUNT(DISTINCT first_party_site) AS unique_sites,
                            SUM(total_hits) AS total_hits
                        FROM tracker_observations
                        GROUP BY tracker_domain
                        ORDER BY unique_sites DESC, total_hits DESC
                        LIMIT $lim
                    ";
                    cmd.Parameters.AddWithValue("$lim", limit);

                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            list.Add(new TrackerStats
                            {
                                TrackerDomain = reader.GetString(0),
                                UniqueFirstPartySites = reader.GetInt32(1),
                                TotalHits = reader.GetInt32(2)
                            });
                        }
                    }
                }
            }
            catch
            {
                // Swallow, optionally log
            }
            finally
            {
                _dbLock.Release();
            }
            return list;
        }

        /// <summary>
        /// Returns aggregate stats for a single tracker domain, or null if unseen.
        /// </summary>
        public static TrackerStats? GetTrackerStats(string trackerDomain)
        {
            return GetTrackerStatsAsync(trackerDomain).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private static async Task<TrackerStats?> GetTrackerStatsAsync(string trackerDomain)
        {
            if (string.IsNullOrWhiteSpace(trackerDomain))
                return null;

            trackerDomain = trackerDomain.Trim().ToLowerInvariant();
            EnsureInitialized();

            await _dbLock.WaitAsync().ConfigureAwait(false);

            try
            {
                using (var cmd = _conn!.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            COUNT(DISTINCT first_party_site) AS unique_sites,
                            SUM(total_hits) AS total_hits
                        FROM tracker_observations
                        WHERE tracker_domain = $domain
                    ";
                    cmd.Parameters.AddWithValue("$domain", trackerDomain);

                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                            {
                                return new TrackerStats
                                {
                                    TrackerDomain = trackerDomain,
                                    UniqueFirstPartySites = reader.GetInt32(0),
                                    TotalHits = reader.GetInt32(1)
                                };
                            }
                        }
                    }
                }
            }
            catch
            {
                // Swallow/log if desired
            }
            finally
            {
                _dbLock.Release();
            }
            return null;
        }

        /// <summary>
        /// Cleanup old entries (last_seen_utc > 90 days ago). Runs once/session.
        /// </summary>
        private static async Task CleanupOldEntriesAsync()
        {
            EnsureInitialized();
            var cutoff = DateTime.UtcNow.Subtract(CleanupThreshold);
            var cutoffIso = cutoff.ToString("o", CultureInfo.InvariantCulture);

            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = @"
                    DELETE FROM tracker_observations 
                    WHERE last_seen_utc < $cutoff
                ";
                cmd.Parameters.AddWithValue("$cutoff", cutoffIso);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// For unit testing: drop and reinit db.
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (_initLock)
            {
                if (_conn != null)
                {
                    _conn.Dispose();
                    _conn = null;
                }
                if (File.Exists(TrackerDbPath))
                {
                    File.Delete(TrackerDbPath);
                }
                _initialized = false;
                _cleanupDone = false;
            }
        }
    }
}
