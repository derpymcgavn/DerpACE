using System;
using System.Collections.Concurrent;

using log4net;

using ACE.Common;
using ACE.Common.Performance;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Rolls a global quest every 30 minutes. Quests can be kill hunts, self-found item races, or event mob turn-ins.
    /// </summary>
    public static class GlobalKillQuestManager
    {
        public enum GlobalQuestKind
        {
            Hunt,
            ItemRace,
            DrunkenMobHunt,
        }

        public const uint DrunkenBeerWcid = 2469; // smallbeer
        public const int DrunkenBeerRequiredTurnIns = 10;

        private static readonly string[] DrunkenBeerNames =
        {
            "Ulgrim's Missing Breakfast",
            "Suspicious Tusker Lager",
            "Warm Emergency Stout",
            "Half-Full Victory Ale",
            "Bottle of Absolutely Not Portal Fuel",
            "Questionable Lugian Brown Ale",
            "Ulgrim's Emergency Spare",
            "Foamy Tusker Regret"
        };

        private static readonly string[] DrunkenMobPrefixes =
        {
            "Drunken",
            "Wobbling",
            "Ale-Soaked",
            "Belligerent",
            "Tipsy",
            "Soused"
        };

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

        public static bool TryApplyDrunkenMob(Creature creature)
        {
            if (CurrentKind != GlobalQuestKind.DrunkenMobHunt || DateTime.UtcNow > QuestExpiry)
                return false;

            if (creature == null || creature is Player || creature is Pet || creature.IsNPC)
                return false;

            if (creature.GetProperty(PropertyBool.IsDrunkenMob) == true)
                return false;

            var isMonster = creature.Attackable || creature.TargetingTactic != TargetingTactic.None;
            if (!isMonster)
                return false;

            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= 0.03f)
                return false;

            creature.SetProperty(PropertyBool.IsDrunkenMob, true);
            creature.Name = $"{DrunkenMobPrefixes[ThreadSafeRandom.Next(0, DrunkenMobPrefixes.Length - 1)]} {creature.Name}";
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.05f;
            creature.Shade = Math.Clamp((creature.Shade ?? 1.0) * 0.85, 0.25, 1.0);
            creature.SetProperty(PropertyInt.GearDamage, 1);

            if (creature.Health?.Current > 0)
            {
                var boost = (uint)Math.Max(1, Math.Round(creature.Health.MaxValue * 0.20));
                creature.Health.StartingValue += boost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            return true;
        }

        public static WorldObject TryCreateDrunkenBeerDrop(Player player, Creature source)
        {
            if (CurrentKind != GlobalQuestKind.DrunkenMobHunt || DateTime.UtcNow > QuestExpiry)
                return null;

            if (player == null || source?.GetProperty(PropertyBool.IsDrunkenMob) != true)
                return null;

            var beer = WorldObjectFactory.CreateNewWorldObject(DrunkenBeerWcid);
            if (beer == null)
                return null;

            beer.Name = DrunkenBeerNames[ThreadSafeRandom.Next(0, DrunkenBeerNames.Length - 1)];
            beer.LongDesc = "A bottle recovered from a drunken creature during Ulgrim's global beer emergency. Bring it to Ulgrim the Unpleasant before the event ends.";
            NomadQuestTrophy.StampIfEligible(player, source, beer);
            return beer;
        }

        public static bool TryTurnInDrunkenBeer(Player player, WorldObject beer, WorldObject target)
        {
            if (player == null || beer == null || target == null || beer.WeenieClassId != DrunkenBeerWcid)
                return false;

            if (!IsUlgrim(target))
                return false;

            if (CurrentKind != GlobalQuestKind.DrunkenMobHunt || DateTime.UtcNow > QuestExpiry)
            {
                player.SendMessage("Ulgrim squints at the bottle. 'Too late. The emergency has matured into history.'", ChatMessageType.Broadcast);
                player.SendUseDoneEvent();
                return true;
            }

            if (!IsValidEventBeer(player, beer))
            {
                player.SendMessage("Ulgrim refuses the bottle. 'This one doesn't smell like today's disaster.'", ChatMessageType.Broadcast);
                player.SendUseDoneEvent();
                return true;
            }

            if (!player.TryConsumeFromInventoryWithNetworking(beer, 1))
            {
                player.SendMessage("Ulgrim reaches for the beer, but it slips away somehow.", ChatMessageType.Broadcast);
                player.SendUseDoneEvent();
                return true;
            }

            var key = MakeKey(player.Guid.Full, CurrentEpoch);
            var newEntry = _progress.AddOrUpdate(key,
                addValue: (1, 0),
                updateValueFactory: (k, old) => (old.kills + 1, old.xp));

            if (newEntry.kills >= DrunkenBeerRequiredTurnIns)
                CompleteDrunkenMobHunt(player, newEntry.kills);
            else
                player.SendMessage($"[Global Quest] Ulgrim accepts the beer. {newEntry.kills}/{DrunkenBeerRequiredTurnIns} recovered.", ChatMessageType.Broadcast);

            player.SendUseDoneEvent();
            return true;
        }

        public static GlobalQuestStatus GetStatus(Player player)
        {
            var kind = CurrentKind;
            var epoch = CurrentEpoch;

            var myProgress = 0;
            if ((kind == GlobalQuestKind.Hunt || kind == GlobalQuestKind.DrunkenMobHunt) && player != null)
            {
                if (_progress.TryGetValue(MakeKey(player.Guid.Full, epoch), out var entry))
                    myProgress = entry.kills;
            }

            return new GlobalQuestStatus
            {
                Kind = kind,
                TargetName = kind == GlobalQuestKind.ItemRace ? CurrentItemName : CurrentCreatureName,
                RequiredKills = kind == GlobalQuestKind.Hunt ? RequiredKills : 0,
                RequiredTurnIns = kind == GlobalQuestKind.DrunkenMobHunt ? DrunkenBeerRequiredTurnIns : 0,
                MyKills = kind == GlobalQuestKind.Hunt ? myProgress : 0,
                MyTurnIns = kind == GlobalQuestKind.DrunkenMobHunt ? myProgress : 0,
                Expiry = QuestExpiry,
                ItemWcid = kind == GlobalQuestKind.ItemRace ? CurrentItemWcid : 0,
                RewardPercent = kind == GlobalQuestKind.ItemRace || kind == GlobalQuestKind.DrunkenMobHunt ? ItemRewardPercent : 0,
                Completed = kind == GlobalQuestKind.ItemRace && _itemRaceCompletedEpochs.ContainsKey(epoch),
            };
        }

        private static bool IsValidEventBeer(Player player, WorldObject beer)
        {
            if (!NomadQuestTrophy.IsSelfFoundFor(player, beer, DrunkenBeerWcid))
                return false;

            if ((beer.GetProperty(PropertyInt.NomadTrophyQuestEpoch) ?? -1) != CurrentEpoch)
                return false;

            var foundTimestamp = beer.GetProperty(PropertyInt.NomadTrophyFoundTimestamp);
            return foundTimestamp != null && foundTimestamp.Value >= QuestStartTimestamp && foundTimestamp.Value <= QuestExpiryTimestamp;
        }

        private static bool IsUlgrim(WorldObject target)
        {
            return target is Creature && (target.Name ?? string.Empty).Contains("Ulgrim", StringComparison.OrdinalIgnoreCase);
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

        private static void CompleteDrunkenMobHunt(Player player, int turnedIn)
        {
            if (!_itemRaceCompletedEpochs.TryAdd(CurrentEpoch, 1))
                return;

            System.Threading.Interlocked.Increment(ref _completionCount);

            var levelXp = player.GetXPToNextLevel(player.Level ?? 1);
            var bonus = (long)Math.Round((double)levelXp * (ItemRewardPercent / 100.0));
            if (bonus < 1)
                bonus = 1;

            player.EarnXP(bonus, XpType.Quest);
            player.SendMessage($"[Global Quest Complete!] Ulgrim accepts your {turnedIn}th event beer and awards {bonus:N0} XP ({ItemRewardPercent}% of level XP)!", ChatMessageType.Broadcast);

            var globalMsg = $"[Global Quest] {player.Name} sobered up Dereth by bringing {DrunkenBeerRequiredTurnIns} dubious beers to Ulgrim!";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(globalMsg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, globalMsg);

            log.Info($"[GlobalKillQuest] {player.Name} completed drunken mob hunt, reward {ItemRewardPercent}% level XP ({bonus:N0})");

            RollNewQuest(announceToAll: true);
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

            if (ShouldRollDrunkenMobHunt())
                RollNewDrunkenMobHunt(announceToAll);
            else if (ShouldRollItemRace())
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

        private static void RollNewDrunkenMobHunt(bool announceToAll)
        {
            CurrentKind = GlobalQuestKind.DrunkenMobHunt;
            CurrentCreatureName = "Drunken Mob";
            CurrentItemName = "Ulgrim's event beer";
            CurrentItemWcid = DrunkenBeerWcid;
            ItemRewardPercent = _rng.Next(50, 151);

            if (announceToAll)
            {
                var msg = $"[Global Quest] Ulgrim has misplaced his emergency beer stash! Find drunken mobs, recover {DrunkenBeerRequiredTurnIns} event beers, and bring them to Ulgrim for {ItemRewardPercent}% of level XP. Type /gquest for details.";
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
                PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
            }

            log.Info($"[GlobalKillQuest] New drunken mob hunt rolled: {DrunkenBeerRequiredTurnIns} beers, reward {ItemRewardPercent}% level XP");
        }

        private static void BroadcastWrapUp()
        {
            var count = _completionCount;
            string wrapUp;

            if (CurrentKind == GlobalQuestKind.ItemRace)
                wrapUp = count == 0
                    ? $"[Global Quest] The race for {CurrentItemName} has ended. Nobody recovered one in time."
                    : $"[Global Quest] The race for {CurrentItemName} has ended.";
            else if (CurrentKind == GlobalQuestKind.DrunkenMobHunt)
                wrapUp = count == 0
                    ? "[Global Quest] Ulgrim's beer emergency has ended. Nobody brought enough of the evidence."
                    : "[Global Quest] Ulgrim's beer emergency has ended.";
            else if (count == 0)
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. No adventurers completed the task this time.";
            else if (count == 1)
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. 1 adventurer answered the call!";
            else
                wrapUp = $"[Global Quest] The hunt for {RequiredKills} {CurrentCreatureName}s has ended. {count} adventurers answered the call!";

            PlayerManager.BroadcastToAll(new GameMessageSystemChat(wrapUp, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, wrapUp);
        }

        private static bool ShouldRollDrunkenMobHunt()
        {
            return _rng.Next(0, 5) == 0;
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
        public int RequiredTurnIns { get; set; }
        public int MyKills { get; set; }
        public int MyTurnIns { get; set; }
        public DateTime Expiry { get; set; }
        public uint ItemWcid { get; set; }
        public int RewardPercent { get; set; }
        public bool Completed { get; set; }
    }
}
