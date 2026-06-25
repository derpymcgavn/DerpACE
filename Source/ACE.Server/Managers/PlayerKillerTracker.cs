using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using log4net;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Tracks which creatures have killed the most players, bucketed by player category
    /// (Normal, Hardcore, Ironman). Used to power the `/topkillers`, `/hardcoretopkillers`,
    /// and `/ironmantopkillers` leaderboards.
    /// Data is persisted to playerKillers.json next to the server executable.
    /// </summary>
    public static class PlayerKillerTracker
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public enum Category
        {
            Normal,
            Hardcore,
            Ironman
        }

        private static readonly ConcurrentDictionary<Category, ConcurrentDictionary<string, int>> _kills =
            new ConcurrentDictionary<Category, ConcurrentDictionary<string, int>>
            {
                [Category.Normal]   = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                [Category.Hardcore] = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                [Category.Ironman]  = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            };

        private static readonly object _saveLock = new object();

        private static string DataFilePath =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "playerKillers.json");

        // -----------------------------------------------------------------------
        // Initialization — call once at server startup
        // -----------------------------------------------------------------------

        public static void Initialize()
        {
            try
            {
                var path = DataFilePath;
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json);
                if (data == null) return;

                foreach (var bucket in data)
                {
                    if (!Enum.TryParse<Category>(bucket.Key, ignoreCase: true, out var cat))
                        continue;

                    var target = _kills[cat];
                    foreach (var kv in bucket.Value)
                        target[kv.Key] = kv.Value;
                }

                log.Info($"[PlayerKillerTracker] Loaded {_kills.Sum(b => b.Value.Count)} entries from {path}");
            }
            catch (Exception ex)
            {
                log.Error($"[PlayerKillerTracker] Failed to load: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Record a kill — call from Player_Death when a creature kills a player
        // -----------------------------------------------------------------------

        public static void RecordKill(Category category, string killerName)
        {
            if (string.IsNullOrWhiteSpace(killerName)) return;

            var bucket = _kills[category];
            bucket.AddOrUpdate(killerName, 1, (_, existing) => existing + 1);
            LeaderboardCache.InvalidateKillers();

            Save();
        }

        // -----------------------------------------------------------------------
        // Leaderboard query
        // -----------------------------------------------------------------------

        public static IReadOnlyList<(string Name, int Kills)> GetTopKillers(Category category, int count = 10)
        {
            return _kills[category]
                .OrderByDescending(kv => kv.Value)
                .Take(count)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        // -----------------------------------------------------------------------
        // Persistence
        // -----------------------------------------------------------------------

        private static void Save()
        {
            try
            {
                lock (_saveLock)
                {
                    var snapshot = _kills.ToDictionary(
                        kv => kv.Key.ToString(),
                        kv => new Dictionary<string, int>(kv.Value));
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(DataFilePath, json);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[PlayerKillerTracker] Failed to save: {ex.Message}");
            }
        }
    }
}
