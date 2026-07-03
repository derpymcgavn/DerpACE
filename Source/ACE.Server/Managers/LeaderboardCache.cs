using System;
using System.Collections.Generic;
using System.Linq;

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
        private static DateTime playerSnapshotUtc = DateTime.MinValue;
        private static DateTime killerSnapshotUtc = DateTime.MinValue;
        private static IReadOnlyList<PlayerLeaderboardEntry> hardcore = Array.Empty<PlayerLeaderboardEntry>();
        private static IReadOnlyList<PlayerLeaderboardEntry> ironman = Array.Empty<PlayerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestNormal = Array.Empty<KillerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestHardcore = Array.Empty<KillerLeaderboardEntry>();
        private static IReadOnlyList<KillerLeaderboardEntry> deadliestIronman = Array.Empty<KillerLeaderboardEntry>();

        public static IReadOnlyList<PlayerLeaderboardEntry> GetHardcore()
        {
            EnsurePlayerSnapshot();
            return hardcore;
        }

        public static IReadOnlyList<PlayerLeaderboardEntry> GetIronman()
        {
            EnsurePlayerSnapshot();
            return ironman;
        }

        public static IReadOnlyList<KillerLeaderboardEntry> GetDeadliest(PlayerKillerTracker.Category category)
        {
            EnsureKillerSnapshot();

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

        private static void EnsurePlayerSnapshot()
        {
            if (DateTime.UtcNow - playerSnapshotUtc < PlayerLeaderboardTtl)
                return;

            lock (CacheLock)
            {
                if (DateTime.UtcNow - playerSnapshotUtc < PlayerLeaderboardTtl)
                    return;

                var players = PlayerManager.GetAllPlayers();
                hardcore = BuildPlayerLeaderboard(players, ironmanOnly: false);
                ironman = BuildPlayerLeaderboard(players, ironmanOnly: true);
                playerSnapshotUtc = DateTime.UtcNow;
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

        private static void EnsureKillerSnapshot()
        {
            if (DateTime.UtcNow - killerSnapshotUtc < KillerLeaderboardTtl)
                return;

            lock (CacheLock)
            {
                if (DateTime.UtcNow - killerSnapshotUtc < KillerLeaderboardTtl)
                    return;

                deadliestNormal = BuildKillerLeaderboard(PlayerKillerTracker.Category.Normal);
                deadliestHardcore = BuildKillerLeaderboard(PlayerKillerTracker.Category.Hardcore);
                deadliestIronman = BuildKillerLeaderboard(PlayerKillerTracker.Category.Ironman);
                killerSnapshotUtc = DateTime.UtcNow;
            }
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
