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
    /// Tracks which creatures have killed the most Ironman players.
    /// Data is persisted to ironmanKillers.json next to the server executable.
    /// </summary>
    public static class IronmanKillerTracker
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly ConcurrentDictionary<string, int> _kills = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _saveLock = new object();

        private static string DataFilePath =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "ironmanKillers.json");

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
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (data == null) return;

                foreach (var kv in data)
                    _kills[kv.Key] = kv.Value;

                log.Info($"[IronmanKillerTracker] Loaded {_kills.Count} entries from {path}");
            }
            catch (Exception ex)
            {
                log.Error($"[IronmanKillerTracker] Failed to load: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Record a kill — call from Player_Death when an Ironman is killed by a creature
        // -----------------------------------------------------------------------

        public static void RecordKill(string killerName)
        {
            if (string.IsNullOrWhiteSpace(killerName)) return;

            _kills.AddOrUpdate(killerName, 1, (_, existing) => existing + 1);

            Save();
        }

        // -----------------------------------------------------------------------
        // Leaderboard query
        // -----------------------------------------------------------------------

        public static IReadOnlyList<(string Name, int Kills)> GetTopKillers(int count = 10)
        {
            return _kills
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
                    var snapshot = new Dictionary<string, int>(_kills);
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(DataFilePath, json);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[IronmanKillerTracker] Failed to save: {ex.Message}");
            }
        }
    }
}
