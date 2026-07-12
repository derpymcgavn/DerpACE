using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static class LeaderboardCache
    {
        private const int LeaderboardSize = 10;
        private static readonly TimeSpan PlayerLeaderboardTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan KillerLeaderboardTtl = TimeSpan.FromSeconds(30);

        private static readonly object CacheLock = new object();
        private const int PlayerBatchSize = 50;
        private static readonly TimeSpan PlayerBatchInterval = TimeSpan.FromMilliseconds(250);
        private static List<IPlayer> playerScan;
        private static int playerScanIndex;
        private static DateTime nextPlayerBatchUtc = DateTime.MinValue;
        private static readonly List<PlayerLeaderboardEntry> pendingHardcore = new List<PlayerLeaderboardEntry>();
        private static readonly List<PlayerLeaderboardEntry> pendingIronman = new List<PlayerLeaderboardEntry>();
        private static DateTime playerSnapshotUtc = DateTime.MinValue;
        private static DateTime killerSnapshotUtc = DateTime.MinValue;
        private static IReadOnlyList<PlayerLeaderboardEntry> hardcore = Array.Empty<PlayerLeaderboardEntry>();
        private static IReadOnlyList<PlayerLeaderboardEntry> ironman = Array.Empty<PlayerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestNormal = Array.Empty<KillerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestHardcore = Array.Empty<KillerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestIronman = Array.Empty<KillerLeaderboardEntry>();

        public static IReadOnlyList<PlayerLeaderboardEntry> GetHardcore()
        {
            return hardcore;
        }

        public static IReadOnlyList<PlayerLeaderboardEntry> GetIronman()
        {
            return ironman;
        }

        public static IReadOnlyList<KillerLeaderboardEntry> GetDeadliest(PlayerKillerTracker.Category category)
        {

            switch (category)
            {
                case PlayerKillerTracker.Category.Hardcore:
                    return deadliestHardcore;
                case PlayerKillerTracker.Category.Ironman:
                    return deadliestIronman;
                default:
                    return deadliestNormal;
            }
        }

        public static void InvalidatePlayers()
        {
            lock (CacheLock)
                playerSnapshotUtc = DateTime.MinValue;
        }

        public static void InvalidateKillers()
        {
            lock (CacheLock)
                killerSnapshotUtc = DateTime.MinValue;
        }

        public static void Tick()
        {
            var now = DateTime.UtcNow;

            if (now - killerSnapshotUtc >= KillerLeaderboardTtl)
                RefreshKillerSnapshots(now);

            if (playerScan == null && now - playerSnapshotUtc >= PlayerLeaderboardTtl)
                StartPlayerScan(now);

            if (playerScan != null && now >= nextPlayerBatchUtc)
                ProcessPlayerBatch(now);
        }

        private static void StartPlayerScan(DateTime now)
        {
            // PlayerManager already holds lightweight offline summaries in memory. Copying
            // this list does not query the shard database; the expensive filtering and
            // ranking work is intentionally spread across subsequent batches.
            playerScan = PlayerManager.GetAllPlayers();
            playerScanIndex = 0;
            pendingHardcore.Clear();
            pendingIronman.Clear();
            nextPlayerBatchUtc = now;
        }

        private static void ProcessPlayerBatch(DateTime now)
        {
            var end = Math.Min(playerScanIndex + PlayerBatchSize, playerScan.Count);
            for (; playerScanIndex < end; playerScanIndex++)
            {
                var player = playerScan[playerScanIndex];
                if (player == null || player.IsDeleted)
                    continue;

                var isIronman = Player.IsIronmanFamilyPlayer(player);
                if (!isIronman && player.GetProperty(PropertyBool.IsHardcore) != true)
                    continue;

                var entry = new PlayerLeaderboardEntry
                {
                    Name = player.Name,
                    Level = player.Level ?? 0,
                    Kills = player.GetProperty(PropertyInt.CreatureKills) ?? 0,
                    Lives = player.GetProperty(PropertyInt.HardcoreLives) ?? 0,
                    IsNomad = player.GetProperty(PropertyBool.IsIronmanNomad) == true
                };

                AddTopCandidate(isIronman ? pendingIronman : pendingHardcore, entry);
            }

            if (playerScanIndex < playerScan.Count)
            {
                nextPlayerBatchUtc = now + PlayerBatchInterval;
                return;
            }

            lock (CacheLock)
            {
                hardcore = pendingHardcore.ToList();
                ironman = pendingIronman.ToList();
                playerSnapshotUtc = now;
            }

            playerScan = null;
            pendingHardcore.Clear();
            pendingIronman.Clear();
        }

        private static void AddTopCandidate(List<PlayerLeaderboardEntry> candidates, PlayerLeaderboardEntry entry)
        {
            candidates.Add(entry);
            candidates.Sort((a, b) =>
            {
                var kills = b.Kills.CompareTo(a.Kills);
                return kills != 0 ? kills : b.Level.CompareTo(a.Level);
            });

            if (candidates.Count > LeaderboardSize)
                candidates.RemoveAt(candidates.Count - 1);
        }

        private static void RefreshKillerSnapshots(DateTime now)
        {
            var normal = BuildKillerLeaderboard(PlayerKillerTracker.Category.Normal);
            var hardcoreKillers = BuildKillerLeaderboard(PlayerKillerTracker.Category.Hardcore);
            var ironmanKillers = BuildKillerLeaderboard(PlayerKillerTracker.Category.Ironman);

            lock (CacheLock)
            {
                deadliestNormal = normal;
                deadliestHardcore = hardcoreKillers;
                deadliestIronman = ironmanKillers;
                killerSnapshotUtc = now;
            }
        }
        private static IReadOnlyList<PlayerLeaderboardEntry> BuildPlayerLeaderboard(IEnumerable<IPlayer> players, bool ironmanOnly)
        {
            return players
                .Where(p => !p.IsDeleted
                    && (ironmanOnly
                        ? Player.IsIronmanFamilyPlayer(p)
                        : p.GetProperty(PropertyBool.IsHardcore) == true && !Player.IsIronmanFamilyPlayer(p)))
                .Select(p => new PlayerLeaderboardEntry
                {
                    Name = p.Name,
                    Level = p.Level ?? 0,
                    Kills = p.GetProperty(PropertyInt.CreatureKills) ?? 0,
                    Lives = p.GetProperty(PropertyInt.HardcoreLives) ?? 0,
                    IsNomad = p.GetProperty(PropertyBool.IsIronmanNomad) == true
                })
                .OrderByDescending(e => e.Kills)
                .ThenByDescending(e => e.Level)
                .Take(LeaderboardSize)
                .ToList();
        }

        private static IReadOnlyList<KillerLeaderboardEntry> BuildKillerLeaderboard(PlayerKillerTracker.Category category)
        {
            return PlayerKillerTracker.GetTopKillers(category, LeaderboardSize)
                .Select(e => new KillerLeaderboardEntry { Name = e.Name, Kills = e.Kills })
                .ToList();
        }
    }

    public sealed class PlayerLeaderboardEntry
    {
        public string Name { get; set; }
        public int Level { get; set; }
        public int Kills { get; set; }
        public int Lives { get; set; }
        public bool IsNomad { get; set; }
    }

    public sealed class KillerLeaderboardEntry
    {
        public string Name { get; set; }
        public int Kills { get; set; }
    }
}
