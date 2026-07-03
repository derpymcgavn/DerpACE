using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using log4net;

using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Rolls a global kill quest every 30 minutes.
    /// All online players receive the same target creature and kill count.
    /// On completion each player earns a 4x XP bonus based on the XP they personally
    /// accumulated from quest kills.
    /// </summary>
    public static class GlobalKillQuestManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Fires once every 30 minutes
        private static readonly RateLimiter _ticker = new RateLimiter(1, TimeSpan.FromMinutes(30));

        // Quest state — written only from the main world thread; read from any landblock thread.
        public static volatile string   CurrentCreatureName = null;
        public static volatile uint     CurrentCreatureTypeId = 0;
        public static volatile int      RequiredKills       = 0;
        public static          DateTime QuestExpiry         = DateTime.UtcNow;
        public static volatile int      CurrentEpoch        = 0;

        // Count of players who finished the current quest (for the wrap-up message)
        private static volatile int _completionCount = 0;

        // Key = (epoch << 32 | playerGuid) so old-epoch progress is silently orphaned when a new quest rolls.
        private static ConcurrentDictionary<ulong, (int kills, long xp)> _progress = new ConcurrentDictionary<ulong, (int kills, long xp)>();

        private static readonly Random _rng = new Random();

        // ----------------------------------------------------------------
        //  Initialization
        // ----------------------------------------------------------------

        public static void Initialize()
        {
            RollNewQuest(announceToAll: false); // Prime immediately with no announcement at startup
            log.Info("[GlobalKillQuest] Initialized. First quest active.");
        }

        // ----------------------------------------------------------------
        //  Tick — called from WorldManager.UpdateGameWorld()
        // ----------------------------------------------------------------

        public static void Tick()
        {
            if (_ticker.GetSecondsToWaitBeforeNextEvent() > 0)
                return;

            _ticker.RegisterEvent();
            RollNewQuest(announceToAll: true);
        }

        // ----------------------------------------------------------------
        //  Called from Creature_Death.OnDeath_GrantXP() on any kill
        // ----------------------------------------------------------------

        /// <param name="player">The player who earned XP for this kill.</param>
        /// <param name="creature">The creature that died.</param>
        /// <param name="xpEarned">The raw XP the player earned from this kill (before EarnXP modifiers).</param>
        public static void OnCreatureKilled(Player player, Creature creature, long xpEarned)
        {
            var name   = CurrentCreatureName;
            var typeId = CurrentCreatureTypeId;
            var target = RequiredKills;
            var epoch  = CurrentEpoch;

            if (name == null || typeId == 0 || target == 0)
                return;

            // Don't accept kills after the quest window has closed
            if (DateTime.UtcNow > QuestExpiry)
                return;

            // Match against the actual CreatureType id chosen for the quest.
            if (creature?.CreatureType == null || (uint)creature.CreatureType.Value != typeId)
                return;

            var key      = MakeKey(player.Guid.Full, epoch);
            var newEntry = _progress.AddOrUpdate(key,
                addValue:         (1, xpEarned),
                updateValueFactory: (k, old) => (old.kills + 1, old.xp + xpEarned));

            // Progress message every 5 kills, or at the final kill
            if (newEntry.kills == target)
            {
                CompleteQuest(player, name, target, newEntry.xp, epoch);
            }
            else if (newEntry.kills % 5 == 0 || newEntry.kills == 1)
            {
                player.SendMessage($"[Global Quest] {newEntry.kills}/{target} {name}s slain.", ChatMessageType.Broadcast);
            }
        }

        // ----------------------------------------------------------------
        //  Query — used by /gquest command
        // ----------------------------------------------------------------

        public static (string name, int required, DateTime expiry, int myKills) GetStatus(Player player)
        {
            var name  = CurrentCreatureName;
            var kills = RequiredKills;
            var epoch = CurrentEpoch;

            int myKills = 0;
            if (name != null && player != null)
            {
                if (_progress.TryGetValue(MakeKey(player.Guid.Full, epoch), out var entry))
                    myKills = entry.kills;
            }

            return (name, kills, QuestExpiry, myKills);
        }

        // ----------------------------------------------------------------
        //  Private helpers
        // ----------------------------------------------------------------

        private static void CompleteQuest(Player player, string creatureName, int target, long totalXp, int epoch)
        {
            // Remove so they can't trigger completion again (race-safe: only the first caller who lands kills == target does this)
            if (!_progress.TryRemove(MakeKey(player.Guid.Full, epoch), out _))
                return; // another thread beat us — already completed

            System.Threading.Interlocked.Increment(ref _completionCount);

            var bonus = totalXp * 4;
            player.EarnXP(bonus, XpType.Quest);

            player.SendMessage(
                $"[Global Quest Complete!] You slew {target} {creatureName}s and earned {bonus:N0} bonus XP!",
                ChatMessageType.Broadcast);

            var globalMsg = $"[Global Quest] {player.Name} has completed the hunt and slain {target} {creatureName}s!";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(globalMsg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(ACE.Entity.Enum.Channel.AllBroadcast, player, globalMsg);

            log.Info($"[GlobalKillQuest] {player.Name} completed quest: kill {target} {creatureName}s, bonus XP {bonus:N0}");
        }

        private static void RollNewQuest(bool announceToAll)
        {
            // Wrap up the previous quest before rolling a new one
            if (announceToAll && CurrentCreatureName != null)
            {
                var count = _completionCount;
                string wrapUp;
                if (count == 0)
                    wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. No adventurers completed the task this time.";
                else if (count == 1)
                    wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. 1 adventurer answered the call!";
                else
                    wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. {count} adventurers answered the call!";

                PlayerManager.BroadcastToAll(new GameMessageSystemChat(wrapUp, ChatMessageType.WorldBroadcast));
                PlayerManager.LogBroadcastChat(ACE.Entity.Enum.Channel.AllBroadcast, null, wrapUp);
            }

            var entry = HuntCreatureTypes.GlobalQuestPool[_rng.Next(HuntCreatureTypes.GlobalQuestPool.Length)];
            var kills = _rng.Next(entry.minKills, entry.maxKills + 1);

            // Atomically bump epoch — old in-flight progress becomes orphaned under the old epoch key
            CurrentEpoch++;
            _completionCount = 0;
            _progress = new ConcurrentDictionary<ulong, (int kills, long xp)>();

            CurrentCreatureName   = entry.name;
            CurrentCreatureTypeId = (uint)entry.type;
            RequiredKills         = kills;
            QuestExpiry           = DateTime.UtcNow.AddMinutes(30);

            if (announceToAll)
            {
                var msg = $"[Global Quest] A new hunt has begun! Slay {kills} {entry.name}s within the next 30 minutes for a 4x XP bonus! Type /gquest for details.";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
                PlayerManager.LogBroadcastChat(ACE.Entity.Enum.Channel.AllBroadcast, null, msg);
            }

            log.Info($"[GlobalKillQuest] New quest rolled: kill {kills} {entry.name}s (CreatureType {entry.type})");
        }

        private static ulong MakeKey(uint playerGuid, int epoch) =>
            ((ulong)(uint)epoch << 32) | playerGuid;
    }
}
