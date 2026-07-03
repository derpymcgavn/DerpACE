using System;
using System.Collections.Concurrent;

using log4net;

using ACE.Common;
using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Rolls a global quest every 30 minutes. Quests can be kill hunts or self-found item races.
    /// </summary>
    public static class GlobalKillQuestManager
    {
        public enum GlobalQuestKind
        {
            Hunt,
            ItemRace,
        }

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly RateLimiter _ticker = new RateLimiter(1, TimeSpan.FromMinutes(30));

        public static volatile GlobalQuestKind CurrentKind = GlobalQuestKind.Hunt;
        public static volatile string CurrentCreatureName = null;
        public static volatile uint CurrentCreatureTypeId = 0;
        public static volatile int RequiredKills = 0;
        public static volatile string CurrentItemName = null;
        public static volatile uint CurrentItemWcid = 0;
        public static volatile int ItemRewardPercent = 0;
        public static DateTime QuestExpiry = DateTime.UtcNow;
        public static volatile int QuestStartTimestamp = 0;
        public static volatile int QuestExpiryTimestamp = 0;
        public static volatile int CurrentEpoch = 0;

        private static volatile int _completionCount = 0;
        private static ConcurrentDictionary<ulong, (int kills, long xp)> _progress = new ConcurrentDictionary<ulong, (int kills, long xp)>();
        private static ConcurrentDictionary<int, byte> _itemRaceCompletedEpochs = new ConcurrentDictionary<int, byte>();

        private static readonly Random _rng = new Random();

        public static void Initialize()
        {
            RollNewQuest(announceToAll: false);
            log.Info("[GlobalKillQuest] Initialized. First quest active.");
        }

        public static void Tick()
        {
            if (_ticker.GetSecondsToWaitBeforeNextEvent() > 0)
                return;

            _ticker.RegisterEvent();
            RollNewQuest(announceToAll: true);
        }

        public static void OnCreatureKilled(Player player, Creature creature, long xpEarned)
        {
            var name = CurrentCreatureName;
            var typeId = CurrentCreatureTypeId;
            var target = RequiredKills;
            var epoch = CurrentEpoch;

            if (CurrentKind != GlobalQuestKind.Hunt || name == null || typeId == 0 || target == 0)
                return;

            if (DateTime.UtcNow > QuestExpiry)
                return;

            if (creature?.CreatureType == null || (uint)creature.CreatureType.Value != typeId)
                return;

            var key = MakeKey(player.Guid.Full, epoch);
            var newEntry = _progress.AddOrUpdate(key,
                addValue: (1, xpEarned),
                updateValueFactory: (k, old) => (old.kills + 1, old.xp + xpEarned));

            if (newEntry.kills == target)
                CompleteHunt(player, name, target, newEntry.xp, epoch);
            else if (newEntry.kills % 5 == 0 || newEntry.kills == 1)
                player.SendMessage($"[Global Quest] {newEntry.kills}/{target} {name}s slain.", ChatMessageType.Broadcast);
        }

        public static void OnItemAcquired(Player player, WorldObject item)
        {
            var itemName = CurrentItemName;
            var itemWcid = CurrentItemWcid;
            var rewardPercent = ItemRewardPercent;
            var epoch = CurrentEpoch;

            if (CurrentKind != GlobalQuestKind.ItemRace || itemName == null || itemWcid == 0 || rewardPercent <= 0)
                return;

            if (player == null || item == null || item.WeenieClassId != itemWcid)
                return;

            if (DateTime.UtcNow > QuestExpiry)
                return;

            if (!NomadQuestTrophy.IsSelfFoundFor(player, item, itemWcid))
                return;

            if ((item.GetProperty(PropertyInt.NomadTrophyQuestEpoch) ?? -1) != epoch)
                return;

            var foundTimestamp = item.GetProperty(PropertyInt.NomadTrophyFoundTimestamp);
            if (foundTimestamp == null || foundTimestamp.Value < QuestStartTimestamp || foundTimestamp.Value > QuestExpiryTimestamp)
                return;

            if (!_itemRaceCompletedEpochs.TryAdd(epoch, 1))
                return;

            CompleteItemRace(player, itemName, rewardPercent);
        }

        public static GlobalQuestStatus GetStatus(Player player)
        {
            var kind = CurrentKind;
            var epoch = CurrentEpoch;

            var myKills = 0;
            if (kind == GlobalQuestKind.Hunt && CurrentCreatureName != null && player != null)
            {
                if (_progress.TryGetValue(MakeKey(player.Guid.Full, epoch), out var entry))
                    myKills = entry.kills;
            }

            return new GlobalQuestStatus
            {
                Kind = kind,
                TargetName = kind == GlobalQuestKind.ItemRace ? CurrentItemName : CurrentCreatureName,
                RequiredKills = kind == GlobalQuestKind.Hunt ? RequiredKills : 0,
                MyKills = myKills,
                Expiry = QuestExpiry,
                ItemWcid = kind == GlobalQuestKind.ItemRace ? CurrentItemWcid : 0,
                RewardPercent = kind == GlobalQuestKind.ItemRace ? ItemRewardPercent : 0,
                Completed = kind == GlobalQuestKind.ItemRace && _itemRaceCompletedEpochs.ContainsKey(epoch),
            };
        }

        private static void CompleteHunt(Player player, string creatureName, int target, long totalXp, int epoch)
        {
            if (!_progress.TryRemove(MakeKey(player.Guid.Full, epoch), out _))
                return;

            System.Threading.Interlocked.Increment(ref _completionCount);

            var bonus = totalXp * 4;
            player.EarnXP(bonus, XpType.Quest);

            player.SendMessage(
                $"[Global Quest Complete!] You slew {target} {creatureName}s and earned {bonus:N0} bonus XP!",
                ChatMessageType.Broadcast);

            var globalMsg = $"[Global Quest] {player.Name} has completed the hunt and slain {target} {creatureName}s!";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(globalMsg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, globalMsg);

            log.Info($"[GlobalKillQuest] {player.Name} completed quest: kill {target} {creatureName}s, bonus XP {bonus:N0}");
        }

        private static void CompleteItemRace(Player player, string itemName, int rewardPercent)
        {
            System.Threading.Interlocked.Increment(ref _completionCount);

            var levelXp = player.GetXPToNextLevel(player.Level ?? 1);
            var bonus = (long)Math.Round((double)levelXp * (rewardPercent / 100.0));
            if (bonus < 1)
                bonus = 1;

            player.EarnXP(bonus, XpType.Quest);

            player.SendMessage(
                $"[Global Quest Complete!] You recovered {itemName} first and earned {bonus:N0} bonus XP ({rewardPercent}% of level XP)!",
                ChatMessageType.Broadcast);

            var globalMsg = $"[Global Quest] {player.Name} recovered {itemName} first and wins the item race!";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(globalMsg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, globalMsg);

            log.Info($"[GlobalKillQuest] {player.Name} completed item race: {itemName}, reward {rewardPercent}% level XP ({bonus:N0})");
        }

        private static void RollNewQuest(bool announceToAll)
        {
            if (announceToAll && GetCurrentTargetName() != null)
                BroadcastWrapUp();

            CurrentEpoch++;
            _completionCount = 0;
            _progress = new ConcurrentDictionary<ulong, (int kills, long xp)>();
            _itemRaceCompletedEpochs = new ConcurrentDictionary<int, byte>();
            CurrentCreatureName = null;
            CurrentCreatureTypeId = 0;
            RequiredKills = 0;
            CurrentItemName = null;
            CurrentItemWcid = 0;
            ItemRewardPercent = 0;
            QuestStartTimestamp = (int)Time.GetUnixTime();
            QuestExpiryTimestamp = QuestStartTimestamp + (30 * 60);
            QuestExpiry = DateTime.UtcNow.AddMinutes(30);

            if (ShouldRollItemRace())
                RollNewItemRace(announceToAll);
            else
                RollNewHunt(announceToAll);
        }

        private static void RollNewHunt(bool announceToAll)
        {
            var entry = HuntCreatureTypes.GlobalQuestPool[_rng.Next(HuntCreatureTypes.GlobalQuestPool.Length)];
            var kills = _rng.Next(entry.minKills, entry.maxKills + 1);

            CurrentKind = GlobalQuestKind.Hunt;
            CurrentCreatureName = entry.name;
            CurrentCreatureTypeId = (uint)entry.type;
            RequiredKills = kills;

            if (announceToAll)
            {
                var msg = $"[Global Quest] A new hunt has begun! Slay {kills} {entry.name}s within the next 30 minutes for a 4x XP bonus! Type /gquest for details.";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
                PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
            }

            log.Info($"[GlobalKillQuest] New quest rolled: kill {kills} {entry.name}s (CreatureType {entry.type})");
        }

        private static void RollNewItemRace(bool announceToAll)
        {
            var entry = HuntCreatureTypes.GlobalItemQuestPool[_rng.Next(HuntCreatureTypes.GlobalItemQuestPool.Length)];
            var rewardPercent = _rng.Next(10, 201);

            CurrentKind = GlobalQuestKind.ItemRace;
            CurrentItemName = entry.name;
            CurrentItemWcid = entry.wcid;
            ItemRewardPercent = rewardPercent;

            if (announceToAll)
            {
                var msg = $"[Global Quest] A new scavenger race has begun! First adventurer to personally recover {entry.name} earns {rewardPercent}% of level XP! Traded copies do not count. Type /gquest for details.";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
                PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
            }

            log.Info($"[GlobalKillQuest] New quest rolled: first self-found {entry.name} (WCID {entry.wcid}), reward {rewardPercent}% level XP");
        }

        private static void BroadcastWrapUp()
        {
            var count = _completionCount;
            string wrapUp;

            if (CurrentKind == GlobalQuestKind.ItemRace)
                wrapUp = count == 0
                    ? $"[Global Quest] The race for {CurrentItemName} has ended. Nobody recovered one in time."
                    : $"[Global Quest] The race for {CurrentItemName} has ended.";
            else if (count == 0)
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. No adventurers completed the task this time.";
            else if (count == 1)
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. 1 adventurer answered the call!";
            else
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. {count} adventurers answered the call!";

            PlayerManager.BroadcastToAll(new GameMessageSystemChat(wrapUp, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, wrapUp);
        }

        private static bool ShouldRollItemRace()
        {
            return HuntCreatureTypes.GlobalItemQuestPool.Length > 0 && _rng.Next(0, 4) == 0;
        }

        private static string GetCurrentTargetName()
        {
            return CurrentKind == GlobalQuestKind.ItemRace ? CurrentItemName : CurrentCreatureName;
        }

        private static ulong MakeKey(uint playerGuid, int epoch)
        {
            return ((ulong)(uint)epoch << 32) | playerGuid;
        }
    }

    public class GlobalQuestStatus
    {
        public GlobalKillQuestManager.GlobalQuestKind Kind { get; set; }
        public string TargetName { get; set; }
        public int RequiredKills { get; set; }
        public int MyKills { get; set; }
        public DateTime Expiry { get; set; }
        public uint ItemWcid { get; set; }
        public int RewardPercent { get; set; }
        public bool Completed { get; set; }
    }
}
