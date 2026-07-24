using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.DerpAce;
using ACE.Server.DerpAce.Bank;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static partial class GlobalKillQuestManager
    {
        public enum GlobalQuestLane
        {
            HalfHour,
            Hourly,
            Daily,
            Weekly,
        }

        private static readonly object _persistentLock = new object();
        private static readonly JsonSerializerOptions _persistentJsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        private static readonly Dictionary<GlobalQuestLane, PersistentGlobalQuest> _persistentQuests = new Dictionary<GlobalQuestLane, PersistentGlobalQuest>();
        private static ConcurrentDictionary<string, PersistentGlobalQuestProgress> _persistentProgress = new ConcurrentDictionary<string, PersistentGlobalQuestProgress>();
        private static int _nextPersistentEpoch = 100000;
        private static DateTime _nextPersistentTick = DateTime.MinValue;
        private static readonly TimeSpan PersistentSaveDebounce = TimeSpan.FromSeconds(15);
        private static bool _persistentStateDirty;
        private static DateTime _nextPersistentSave = DateTime.MinValue;
        private static readonly string[] ChugQuestBoozeTerms =
        {
            "ale", "beer", "lager", "stout", "porter", "mead", "wine", "brandy", "whiskey", "whisky", "rum", "sake", "cider", "booze", "liquor", "brew", "spirits"
        };

        private static readonly string[] ChugQuestTargets =
        {
            "anything stronger than water", "booze", "beer", "ale", "stout", "wine", "mead", "spirits"
        };


        private static string PersistentStateDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "GlobalQuests");
        private static string PersistentStatePath => Path.Combine(PersistentStateDirectory, "global_quests.json");

        private static void InitializePersistentLanes()
        {
            lock (_persistentLock)
            {
                LoadPersistentState();
                EnsurePersistentLanes(DateTime.UtcNow, false);
                EnsureUniquePersistentQuestKinds(DateTime.UtcNow, false);
                SavePersistentStateNowUnsafe();
            }
        }

        private static void TickPersistentLanes()
        {
            var now = DateTime.UtcNow;
            if (now < _nextPersistentTick)
                return;

            _nextPersistentTick = now.AddSeconds(30);

            lock (_persistentLock)
            {
                var rolledQuest = false;
                foreach (var quest in _persistentQuests.Values.ToList())
                {
                    if (quest.Expiry <= now)
                    {
                        AwardAndRemoveExpiredPersistentCurrency(quest);
                        BroadcastPersistentWrapUp(quest);
                        RollPersistentQuest(quest.Lane, true, now);
                        rolledQuest = true;
                    }
                }

                rolledQuest |= EnsurePersistentLanes(now, true);
                rolledQuest |= EnsureUniquePersistentQuestKinds(now, true);
                SavePersistentStateIfDueUnsafe(now, rolledQuest);
            }
        }

        private static void OnPersistentCreatureKilled(Player player, Creature creature)
        {
            if (player == null || creature == null)
                return;

            var completions = new List<Action>();
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow))
                {
                    if (!IsPersistentKillQuest(quest.Kind) || !MatchesKillQuest(quest.Kind, player, creature) || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    var progress = AddPersistentProgress(player, quest, 1);
                    if (progress.Count == quest.Required)
                        completions.Add(() => CompletePersistentT8Luminance(player, quest));
                    else if (progress.Count == 1 || progress.Count % 10 == 0)
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {progress.Count}/{quest.Required} {quest.TargetName ?? "tier 8 creatures"} defeated.", ChatMessageType.Broadcast);
                }

                SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            foreach (var complete in completions)
                complete();
        }

        private static bool IsPersistentKillQuest(GlobalQuestKind kind)
        {
            return kind == GlobalQuestKind.T8LuminanceHunt
                || kind == GlobalQuestKind.T8MutatorHunt
                || kind == GlobalQuestKind.T8DungeonHunt;
        }

        private static void OnPersistentItemAcquired(Player player, WorldObject item)
        {
            if (player == null || item == null)
                return;

            var completions = new List<Action>();
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow))
                {
                    if (quest.Kind != GlobalQuestKind.T8CurrencyHunt || item.WeenieClassId != quest.ItemWcid || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    if (!IsValidPersistentDrop(player, item, quest))
                        continue;

                    var amount = GetUncountedCurrencyAmount(item, quest.Epoch);
                    if (amount <= 0)
                        continue;

                    MarkCurrencyCounted(item, quest.Epoch, amount);
                    var progress = AddPersistentProgress(player, quest, amount);
                    if (progress.Count >= quest.Required)
                        completions.Add(() => CompletePersistentT8Currency(player, quest));
                    else
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {progress.Count}/{quest.Required} forged Derp Coins recovered.", ChatMessageType.Broadcast);
                }

                SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            foreach (var complete in completions)
                complete();
        }

        private static void OnPersistentCurrencyStackMerged(Player player, WorldObject sourceStack, WorldObject targetStack, int amount)
        {
            if (player == null || sourceStack == null || targetStack == null || amount <= 0)
                return;

            var completions = new List<Action>();
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow))
                {
                    if (quest.Kind != GlobalQuestKind.T8CurrencyHunt || sourceStack.WeenieClassId != quest.ItemWcid || targetStack.WeenieClassId != quest.ItemWcid || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    if (!IsValidPersistentDrop(player, sourceStack, quest))
                        continue;

                    var count = GetUncountedCurrencyAmount(sourceStack, quest.Epoch, amount);
                    if (count <= 0)
                        continue;

                    CopyGlobalQuestDropStamp(sourceStack, targetStack);
                    MarkCurrencyCounted(targetStack, quest.Epoch, count);
                    var progress = AddPersistentProgress(player, quest, count);
                    if (progress.Count >= quest.Required)
                        completions.Add(() => CompletePersistentT8Currency(player, quest));
                    else
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {progress.Count}/{quest.Required} forged Derp Coins recovered.", ChatMessageType.Broadcast);
                }

                SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            foreach (var complete in completions)
                complete();
        }
        public static void OnFoodConsumed(Player player, Food food, MotionCommand motionCommand)
        {
            if (player == null || food == null || motionCommand != MotionCommand.Drink)
                return;

            var name = food.Name ?? string.Empty;
            var completions = new List<(PersistentGlobalQuest quest, int count)>();
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow))
                {
                    if (quest.Kind != GlobalQuestKind.ChugRace || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    if (!MatchesChugQuest(quest, name))
                        continue;

                    var progress = AddPersistentProgress(player, quest, 1);
                    if (progress.Count >= quest.Required)
                        completions.Add((quest, progress.Count));
                    else if (progress.Count == 1 || progress.Count % 5 == 0)
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {progress.Count}/{quest.Required} drinks chugged.", ChatMessageType.Broadcast);
                }

                SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            foreach (var completion in completions)
                CompletePersistentChugRace(player, completion.quest, completion.count);
        }

        private static bool MatchesChugQuest(PersistentGlobalQuest quest, string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return false;

            var lowered = itemName.ToLowerInvariant();
            var target = quest.ItemName?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(target) && target != "anything stronger than water" && target != "booze" && target != "spirits")
                return lowered.Contains(target);

            return ChugQuestBoozeTerms.Any(term => lowered.Contains(term));
        }

        private static void CompletePersistentChugRace(Player player, PersistentGlobalQuest quest, int drinks)
        {
            if (!TryFinishPersistentQuest(player, quest))
                return;

            var levelXp = player.GetXPToNextLevel(player.Level ?? 1);
            var bonus = Math.Max(1, (long)Math.Round(levelXp * (quest.RewardPercent / 100.0)));
            player.EarnXP(bonus, XpType.Quest);
            player.SendMessage($"[Global Quest Complete:{GetLaneLabel(quest.Lane)}] You chugged {drinks} drinks and earned {bonus:N0} XP ({quest.RewardPercent}% of level XP).", ChatMessageType.Broadcast);
            BroadcastPersistentCompletion(player, quest, $"{player.Name} won the chug race after {drinks} questionable decisions!");
            BroadcastPersistentWrapUp(quest);
            RollPersistentQuest(quest.Lane, true, DateTime.UtcNow);
        }

        private static WorldObject TryCreatePersistentT8CurrencyDrop(Player player, Creature source)
        {
            if (player == null || !IsTier8Creature(source))
                return null;

            PersistentGlobalQuest quest;
            lock (_persistentLock)
            {
                quest = ActivePersistentQuests(DateTime.UtcNow)
                    .Where(q => q.Kind == GlobalQuestKind.T8CurrencyHunt && !IsNonRepeatPersistentQuestCompleted(player, q))
                    .OrderBy(q => q.Lane == GlobalQuestLane.Daily ? 0 : 1)
                    .FirstOrDefault();
            }

            if (quest == null || ThreadSafeRandom.Next(0, 100) >= T8CurrencyDropChancePercent)
                return null;

            var currency = WorldObjectFactory.CreateNewWorldObject(quest.ItemWcid);
            if (currency == null)
                return null;

            currency.Name = quest.ItemName ?? currency.Name;
            currency.SetStackSize(1);
            currency.LongDesc = $"A corrupted forged Derp Coin recovered during the {GetLaneLabel(quest.Lane)} Correct the Corruption quest.";
            StampPersistentDrop(player, source, currency, quest);
            return currency;
        }
        private static List<GlobalQuestStatus> GetPersistentStatuses(Player player)
        {
            lock (_persistentLock)
                return _persistentQuests.Values.OrderBy(q => q.Lane).Select(q => BuildPersistentStatus(player, q)).ToList();
        }

        private static GlobalQuestStatus BuildPersistentStatus(Player player, PersistentGlobalQuest quest)
        {
            var count = 0;
            var distance = 0.0;
            var completed = false;
            if (player != null)
            {
                if (_persistentProgress.TryGetValue(MakePersistentKey(player.Guid.Full, quest.Epoch), out var progress))
                {
                    count = progress.Count;
                    distance = progress.Distance;
                }
                completed = _persistentProgress.ContainsKey(MakePersistentCompleteKey(player.Guid.Full, quest.Epoch));
            }

            return new GlobalQuestStatus
            {
                Lane = quest.Lane,
                Kind = quest.Kind,
                TargetName = quest.Kind == GlobalQuestKind.T8CurrencyHunt ? quest.ItemName : quest.TargetName ?? "Tier 8 Creature",
                RequiredKills = IsPersistentKillQuest(quest.Kind) ? quest.Required : 0,
                RequiredTurnIns = quest.Kind == GlobalQuestKind.T8CurrencyHunt || quest.Kind == GlobalQuestKind.ChugRace ? quest.Required : 0,
                MyKills = IsPersistentKillQuest(quest.Kind) ? count : 0,
                MyTurnIns = quest.Kind == GlobalQuestKind.T8CurrencyHunt || quest.Kind == GlobalQuestKind.ChugRace ? count : 0,
                Expiry = quest.Expiry,
                ItemWcid = quest.Kind == GlobalQuestKind.T8CurrencyHunt || quest.Kind == GlobalQuestKind.VendorDeliveryRace ? quest.ItemWcid : 0,
                LuminanceReward = quest.LuminanceReward,
                RewardPercent = quest.RewardPercent,
                RequiredDistance = quest.Kind == GlobalQuestKind.CardinalTrek ? quest.Required : 0,
                MyDistance = quest.Kind == GlobalQuestKind.CardinalTrek ? distance : 0,
                Completed = completed,
            };
        }

        private static void CompletePersistentT8Luminance(Player player, PersistentGlobalQuest quest)
        {
            if (!TryFinishPersistentQuest(player, quest))
                return;

            player.EarnLuminance(quest.LuminanceReward, XpType.Quest, ShareType.None);
            player.SendMessage($"[Global Quest Complete:{GetLaneLabel(quest.Lane)}] You defeated {quest.Required} {quest.TargetName ?? "tier 8 creatures"} and earned {quest.LuminanceReward:N0} luminance!", ChatMessageType.Broadcast);
            BroadcastPersistentCompletion(player, quest, $"{player.Name} completed the {GetLaneLabel(quest.Lane)} hunt for {quest.TargetName ?? "tier 8 creatures"}!");
        }

        private static void CompletePersistentT8Currency(Player player, PersistentGlobalQuest quest)
        {
            if (!TryFinishPersistentQuest(player, quest))
                return;

            RemovePersistentCurrencyCoins(player, quest);

            player.EarnLuminance(quest.LuminanceReward, XpType.Quest, ShareType.None);
            player.SendMessage($"[Global Quest Complete:{GetLaneLabel(quest.Lane)}] You corrected {quest.Required} forged Derp Coins and earned {quest.LuminanceReward:N0} luminance!", ChatMessageType.Broadcast);
            BroadcastPersistentCompletion(player, quest, $"{player.Name} helped Correct the Corruption during the {GetLaneLabel(quest.Lane)} quest!");
        }

        private static bool TryFinishPersistentQuest(Player player, PersistentGlobalQuest quest)
        {
            lock (_persistentLock)
            {
                var key = MakePersistentKey(player.Guid.Full, quest.Epoch);
                if (!_persistentProgress.TryRemove(key, out _))
                    return false;

                if (IsNonRepeatPersistentQuest(quest))
                {
                    var completedKey = MakePersistentCompleteKey(player.Guid.Full, quest.Epoch);
                    if (_persistentProgress.ContainsKey(completedKey))
                        return false;

                    _persistentProgress[completedKey] = new PersistentGlobalQuestProgress { Count = 1, Completed = true };
                    MarkPersistentStateDirtyUnsafe();
                }

                quest.CompletionCount++;
                SavePersistentStateNowUnsafe();
                return true;
            }
        }

        private static PersistentGlobalQuestProgress AddPersistentProgress(Player player, PersistentGlobalQuest quest, int amount)
        {
            var key = MakePersistentKey(player.Guid.Full, quest.Epoch);
            MarkPersistentStateDirtyUnsafe();
            return _persistentProgress.AddOrUpdate(key,
                new PersistentGlobalQuestProgress { Count = amount },
                (k, old) => { old.Count += amount; return old; });
        }

        private static IEnumerable<PersistentGlobalQuest> ActivePersistentQuests(DateTime now)
        {
            return _persistentQuests.Values.Where(q => q != null && q.Expiry > now);
        }

        private static bool EnsurePersistentLanes(DateTime now, bool announce)
        {
            var rolledQuest = false;
            foreach (var lane in new[] { GlobalQuestLane.Hourly, GlobalQuestLane.Daily, GlobalQuestLane.Weekly })
            {
                if (!_persistentQuests.TryGetValue(lane, out var quest) || quest == null || quest.Expiry <= now)
                {
                    RollPersistentQuest(lane, announce, now);
                    rolledQuest = true;
                }
            }

            return rolledQuest;
        }

        private static void RollPersistentQuest(GlobalQuestLane lane, bool announce, DateTime now)
        {
            var excludedKinds = new HashSet<GlobalQuestKind>();
            if (_persistentQuests.TryGetValue(lane, out var previousQuest) && previousQuest != null)
                excludedKinds.Add(previousQuest.Kind);

            foreach (var entry in _persistentQuests)
            {
                if (entry.Key != lane && entry.Value != null && entry.Value.Expiry > now)
                    excludedKinds.Add(entry.Value.Kind);
            }

            var quest = CreatePersistentQuest(lane, now, excludedKinds);
            _persistentQuests[lane] = quest;
            PrunePersistentProgress();
            MarkPersistentStateDirtyUnsafe(now);
            if (announce)
                BroadcastPersistentStart(quest);
        }

        public static bool TryAdminRerollPersistentQuest(GlobalQuestLane lane, out GlobalQuestStatus status, out string error)
        {
            status = null;
            error = null;

            if (lane != GlobalQuestLane.Daily && lane != GlobalQuestLane.Weekly)
            {
                error = "Only daily and weekly global quests can be rerolled with this command.";
                return false;
            }

            lock (_persistentLock)
            {
                if (_persistentQuests.TryGetValue(lane, out var current) && current != null)
                    BroadcastPersistentWrapUp(current);

                RollPersistentQuest(lane, true, DateTime.UtcNow);
                SavePersistentStateNowUnsafe();
                status = BuildPersistentStatus(null, _persistentQuests[lane]);
                return true;
            }
        }
        private static PersistentGlobalQuest CreatePersistentQuest(GlobalQuestLane lane, DateTime now, HashSet<GlobalQuestKind> excludedKinds)
        {
            var quest = new PersistentGlobalQuest
            {
                Lane = lane,
                Epoch = _nextPersistentEpoch++,
                Start = now,
                Expiry = now + GetPersistentDuration(lane),
                QuestStartTimestamp = (int)Time.GetUnixTime(),
            };
            quest.QuestExpiryTimestamp = quest.QuestStartTimestamp + (int)GetPersistentDuration(lane).TotalSeconds;

            var availableKinds = new List<GlobalQuestKind>
            {
                GlobalQuestKind.T8CurrencyHunt,
                GlobalQuestKind.T8LuminanceHunt,
                GlobalQuestKind.T8MutatorHunt,
                GlobalQuestKind.T8DungeonHunt,
                GlobalQuestKind.CardinalTrek,
                GlobalQuestKind.ChugRace,
                GlobalQuestKind.ChugRace,
            };
            if (CanRollVendorDelivery())
            {
                availableKinds.Add(GlobalQuestKind.VendorDeliveryRace);
                availableKinds.Add(GlobalQuestKind.VendorDeliveryRace);
                availableKinds.Add(GlobalQuestKind.VendorDeliveryRace);
            }

            if (excludedKinds != null)
                availableKinds.RemoveAll(excludedKinds.Contains);
            if (availableKinds.Count == 0)
                availableKinds.Add(GlobalQuestKind.CardinalTrek);

            var selectedKind = availableKinds[_rng.Next(availableKinds.Count)];
            var minRequired = lane == GlobalQuestLane.Weekly ? 100 : lane == GlobalQuestLane.Daily ? 35 : 20;
            var maxRequired = lane == GlobalQuestLane.Weekly ? 201 : lane == GlobalQuestLane.Daily ? 81 : 51;

            switch (selectedKind)
            {
                case GlobalQuestKind.T8CurrencyHunt:
                    ConfigurePersistentCurrencyQuest(quest, lane == GlobalQuestLane.Weekly ? 40 : 15, lane == GlobalQuestLane.Weekly ? 81 : 36, 5000, 50001);
                    break;
                case GlobalQuestKind.T8MutatorHunt:
                    ConfigurePersistentKillQuest(quest, selectedKind, "mutated tier 8 creatures", Math.Max(2, minRequired / 5), Math.Max(3, maxRequired / 5));
                    break;
                case GlobalQuestKind.T8DungeonHunt:
                    ConfigurePersistentKillQuest(quest, selectedKind, "tier 8 dungeon creatures", minRequired, maxRequired);
                    break;
                case GlobalQuestKind.CardinalTrek:
                    ConfigurePersistentCardinalTrek(quest);
                    break;
                case GlobalQuestKind.VendorDeliveryRace:
                    ConfigurePersistentVendorDelivery(quest);
                    break;
                case GlobalQuestKind.ChugRace:
                    ConfigurePersistentChugRace(quest);
                    break;
                default:
                    ConfigurePersistentLuminanceQuest(quest, minRequired, maxRequired, 0, 0);
                    break;
            }

            NormalizePersistentQuestReward(quest);
            return quest;
        }

        private static bool EnsureUniquePersistentQuestKinds(DateTime now, bool announce)
        {
            var changed = false;
            var seenKinds = new HashSet<GlobalQuestKind>();
            foreach (var lane in new[] { GlobalQuestLane.Hourly, GlobalQuestLane.Daily, GlobalQuestLane.Weekly })
            {
                if (!_persistentQuests.TryGetValue(lane, out var quest) || quest == null || quest.Expiry <= now)
                    continue;

                if (seenKinds.Add(quest.Kind))
                    continue;

                RollPersistentQuest(lane, announce, now);
                seenKinds.Add(_persistentQuests[lane].Kind);
                changed = true;
            }

            return changed;
        }
        private static void NormalizePersistentQuestReward(PersistentGlobalQuest quest)
        {
            if (quest == null)
                return;
            if (quest.Kind == GlobalQuestKind.T8LuminanceHunt
                || quest.Kind == GlobalQuestKind.T8MutatorHunt
                || quest.Kind == GlobalQuestKind.T8DungeonHunt)
                quest.LuminanceReward = quest.Required * 100L;
            else if (quest.LuminanceReward > 0)
                quest.LuminanceReward = Math.Max(5000, Math.Min(50000, quest.LuminanceReward));

            if (quest.Kind == GlobalQuestKind.T8CurrencyHunt)
            {
                var currency = GetT8CurrencyBankItem();
                quest.ItemName = currency.name;
                quest.ItemWcid = currency.wcid;
            }
            else if (string.IsNullOrWhiteSpace(quest.TargetName))
            {
                quest.TargetName = quest.Kind == GlobalQuestKind.T8MutatorHunt
                    ? "mutated tier 8 creatures"
                    : quest.Kind == GlobalQuestKind.T8DungeonHunt
                        ? "tier 8 dungeon creatures"
                        : "tier 8 creatures";
            }
        }

        private static bool IsNonRepeatPersistentQuestCompleted(Player player, PersistentGlobalQuest quest)
        {
            return IsNonRepeatPersistentQuest(quest) && _persistentProgress.ContainsKey(MakePersistentCompleteKey(player.Guid.Full, quest.Epoch));
        }

        private static bool IsNonRepeatPersistentQuest(PersistentGlobalQuest quest)
        {
            return quest != null;
        }
        private static void ConfigurePersistentLuminanceQuest(PersistentGlobalQuest quest, int minRequired, int maxRequired, int minLum, int maxLum)
        {
            quest.Kind = GlobalQuestKind.T8LuminanceHunt;
            quest.TargetName = "tier 8 creatures";
            quest.Required = _rng.Next(minRequired, maxRequired);
            quest.LuminanceReward = quest.Required * 100L;
        }

        private static void ConfigurePersistentKillQuest(PersistentGlobalQuest quest, GlobalQuestKind kind, string targetName, int minRequired, int maxRequired)
        {
            quest.Kind = kind;
            quest.TargetName = targetName;
            quest.Required = _rng.Next(minRequired, maxRequired);
            quest.LuminanceReward = quest.Required * 100L;
            quest.RewardPercent = 0;
            quest.Direction = null;
        }

        private static void ConfigurePersistentCardinalTrek(PersistentGlobalQuest quest)
        {
            var directions = new[] { "North", "East", "South", "West" };
            quest.Kind = GlobalQuestKind.CardinalTrek;
            quest.Direction = directions[_rng.Next(directions.Length)];
            quest.Required = _rng.Next(10, 51);
            quest.RewardPercent = Math.Min(200, quest.Required * 4);
            quest.TargetName = $"Travel {quest.Required} clicks {quest.Direction} on foot";
            quest.LuminanceReward = 0;
            quest.ItemName = null;
            quest.ItemWcid = 0;
        }
        private static void ConfigurePersistentChugRace(PersistentGlobalQuest quest)
        {
            var target = ChugQuestTargets[_rng.Next(ChugQuestTargets.Length)];
            quest.Kind = GlobalQuestKind.ChugRace;
            quest.Required = _rng.Next(5, 21);
            quest.ItemName = target;
            quest.ItemWcid = 0;
            quest.RewardPercent = Math.Clamp(quest.Required * 8, 50, 200);
            quest.LuminanceReward = 0;
            quest.Direction = null;
            quest.TargetName = $"Chug {quest.Required} {target}";
        }

        private static void ConfigurePersistentKillVariation(PersistentGlobalQuest quest, int roll, int minRequired, int maxRequired, int minLum, int maxLum)
        {
            var variation = roll % 3;
            if (variation == 0)
            {
                quest.Kind = GlobalQuestKind.T8MutatorHunt;
                quest.TargetName = "mutated tier 8 creatures";
                minRequired = Math.Max(2, minRequired / 5);
                maxRequired = Math.Max(minRequired + 1, maxRequired / 5);
            }
            else if (variation == 1)
            {
                quest.Kind = GlobalQuestKind.T8DungeonHunt;
                quest.TargetName = "tier 8 dungeon creatures";
            }
            else
            {
                quest.Kind = GlobalQuestKind.T8LuminanceHunt;
                quest.TargetName = "tier 8 creatures";
            }

            quest.Required = _rng.Next(minRequired, maxRequired);
            quest.LuminanceReward = quest.Required * 100L;
        }
        private static void ConfigurePersistentCurrencyQuest(PersistentGlobalQuest quest, int minRequired, int maxRequired, int minLum, int maxLum)
        {
            var currency = GetT8CurrencyBankItem();
            quest.Kind = GlobalQuestKind.T8CurrencyHunt;
            quest.Required = _rng.Next(minRequired, maxRequired);
            quest.LuminanceReward = _rng.Next(minLum, maxLum);
            quest.ItemName = currency.name;
            quest.ItemWcid = currency.wcid;
        }

        private static void StampPersistentDrop(Player player, Creature source, WorldObject item, PersistentGlobalQuest quest)
        {
            item.SetProperty(PropertyInt.NomadTrophyOwner, unchecked((int)player.Guid.Full));
            item.SetProperty(PropertyInt.NomadTrophySourceWcid, unchecked((int)source.WeenieClassId));
            item.SetProperty(PropertyInt.NomadTrophyQuestEpoch, quest.Epoch);
            item.SetProperty(PropertyInt.NomadTrophyFoundTimestamp, (int)Time.GetUnixTime());

            var creatureType = source.GetProperty(PropertyInt.CreatureType);
            if (creatureType != null)
                item.SetProperty(PropertyInt.NomadTrophySourceCreatureType, creatureType.Value);
        }

        private static bool IsValidPersistentDrop(Player player, WorldObject item, PersistentGlobalQuest quest)
        {
            if (!NomadQuestTrophy.IsSelfFoundFor(player, item, quest.ItemWcid))
                return false;

            if ((item.GetProperty(PropertyInt.NomadTrophyQuestEpoch) ?? -1) != quest.Epoch)
                return false;

            var found = item.GetProperty(PropertyInt.NomadTrophyFoundTimestamp);
            return found != null && found.Value >= quest.QuestStartTimestamp && found.Value <= quest.QuestExpiryTimestamp;
        }

        private static void AwardAndRemoveExpiredPersistentCurrency(PersistentGlobalQuest quest)
        {
            if (quest == null || quest.Kind != GlobalQuestKind.T8CurrencyHunt)
                return;

            var paidCount = 0;
            foreach (var player in PlayerManager.GetAllOnline())
            {
                var progressKey = MakePersistentKey(player.Guid.Full, quest.Epoch);
                var completedKey = MakePersistentCompleteKey(player.Guid.Full, quest.Epoch);
                var completed = _persistentProgress.ContainsKey(completedKey);
                var trackedCount = _persistentProgress.TryGetValue(progressKey, out var progress) ? progress.Count : 0;
                var removedCount = RemovePersistentCurrencyCoins(player, quest);
                var countForReward = Math.Max(trackedCount, removedCount);

                if (!completed && countForReward > 0 && quest.Required > 0 && quest.LuminanceReward > 0)
                {
                    var ratio = Math.Min(1.0, countForReward / (double)quest.Required);
                    var luminance = Math.Max(1, (long)Math.Round(quest.LuminanceReward * ratio));
                    player.EarnLuminance(luminance, XpType.Quest, ShareType.None);
                    player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] Correct the Corruption ended. You recovered {countForReward}/{quest.Required} forged Derp Coins and earned {luminance:N0} luminance.", ChatMessageType.Broadcast);
                    paidCount++;
                }

                if (trackedCount > 0)
                    _persistentProgress.TryRemove(progressKey, out _);
            }

            if (paidCount > 0)
            {
                MarkPersistentStateDirtyUnsafe();
                log.Info($"[GlobalKillQuest] Correct the Corruption {GetLaneLabel(quest.Lane)} expired with {paidCount} partial payout(s).");
            }
        }

        private static int RemovePersistentCurrencyCoins(Player player, PersistentGlobalQuest quest)
        {
            if (player == null || quest == null || quest.ItemWcid == 0)
                return 0;

            var removed = 0;
            var coins = player.GetAllPossessions()
                .Where(item => item != null
                    && item.WeenieClassId == quest.ItemWcid
                    && (item.GetProperty(PropertyInt.NomadTrophyQuestEpoch) ?? -1) == quest.Epoch)
                .ToList();

            foreach (var coin in coins)
            {
                var amount = Math.Max(1, coin.StackSize ?? 1);
                if (player.TryConsumeFromInventoryWithNetworking(coin, amount))
                    removed += amount;
            }

            return removed;
        }
        private static void BroadcastPersistentStart(PersistentGlobalQuest quest)
        {
            var lane = GetLaneLabel(quest.Lane);
            var msg = quest.Kind == GlobalQuestKind.ChugRace
                ? $"[Global Quest:{lane}] Chug Race: first to drink {quest.Required} {quest.ItemName ?? "booze"} wins {quest.RewardPercent}% of level XP. Type /gquest for details."
                : quest.Kind == GlobalQuestKind.VendorDeliveryRace
                ? $"[Global Quest:{lane}] Dereth Express: buy {quest.ItemName} from {quest.SourceVendorName} in {quest.SourceTown}, then be first to deliver it to an NPC in {quest.DestinationTown} for {quest.RewardPercent}% of level XP. Type /gquest for details."
                : quest.Kind == GlobalQuestKind.CardinalTrek
                    ? $"[Global Quest:{lane}] Cardinal Trek: first to travel {quest.Required} clicks {quest.Direction} on foot wins {quest.RewardPercent}% of level XP. Type /gquest for details."
                : quest.Kind == GlobalQuestKind.T8CurrencyHunt
                    ? $"[Global Quest:{lane}] Correct the Corruption: recover {quest.Required} forged Derp Coins from tier 8 creatures for {quest.LuminanceReward:N0} luminance. Type /gquest for details."
                    : $"[Global Quest:{lane}] Defeat {quest.Required} {quest.TargetName ?? "tier 8 creatures"} for {quest.LuminanceReward:N0} luminance. Type /gquest for details.";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
        }

        private static void BroadcastPersistentWrapUp(PersistentGlobalQuest quest)
        {
            var lane = GetLaneLabel(quest.Lane);
            var msg = quest.Kind == GlobalQuestKind.ChugRace
                ? $"[Global Quest:{lane}] The chug race for {quest.ItemName ?? "booze"} has ended. {quest.CompletionCount} adventurer{(quest.CompletionCount == 1 ? "" : "s")} completed it."
                : quest.Kind == GlobalQuestKind.VendorDeliveryRace
                ? $"[Global Quest:{lane}] Dereth Express from {quest.SourceTown} to {quest.DestinationTown} has ended."
                : quest.Kind == GlobalQuestKind.CardinalTrek
                    ? $"[Global Quest:{lane}] The cardinal trek {quest.Direction} has ended."
                : quest.Kind == GlobalQuestKind.T8CurrencyHunt
                    ? $"[Global Quest:{lane}] Correct the Corruption has ended. {quest.CompletionCount} adventurer{(quest.CompletionCount == 1 ? "" : "s")} completed it."
                    : $"[Global Quest:{lane}] The hunt for {quest.TargetName ?? "tier 8 creatures"} has ended. {quest.CompletionCount} adventurer{(quest.CompletionCount == 1 ? "" : "s")} completed it.";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
        }

        private static void BroadcastPersistentCompletion(Player player, PersistentGlobalQuest quest, string message)
        {
            var globalMsg = $"[Global Quest] {message}";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(globalMsg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, globalMsg);
        }

        private static void LoadPersistentState()
        {
            _persistentQuests.Clear();
            _persistentProgress = new ConcurrentDictionary<string, PersistentGlobalQuestProgress>();
            try
            {
                if (!File.Exists(PersistentStatePath))
                    return;
                var state = JsonSerializer.Deserialize<PersistentGlobalQuestState>(File.ReadAllText(PersistentStatePath), _persistentJsonOptions);
                if (state == null)
                    return;
                _nextPersistentEpoch = Math.Max(state.NextEpoch, 100000);
                _globalQuestVendors.Clear();
                _globalQuestVendors.AddRange(state.Vendors ?? new List<GlobalQuestVendorRecord>());
                foreach (var quest in state.Quests ?? new List<PersistentGlobalQuest>())
                {
                    NormalizePersistentQuestReward(quest);
                    _persistentQuests[quest.Lane] = quest;
                }
                foreach (var entry in state.Progress ?? new List<PersistentGlobalQuestProgressEntry>())
                    _persistentProgress[entry.Key] = entry.Progress ?? new PersistentGlobalQuestProgress();
            }
            catch (Exception ex)
            {
                log.Error($"[GlobalKillQuest] Failed to load persistent global quest state: {ex}");
            }
        }

        private static void MarkPersistentStateDirtyUnsafe(DateTime? now = null)
        {
            _persistentStateDirty = true;
            var saveAt = (now ?? DateTime.UtcNow) + PersistentSaveDebounce;
            if (_nextPersistentSave == DateTime.MinValue || saveAt < _nextPersistentSave)
                _nextPersistentSave = saveAt;
        }

        private static void SavePersistentStateIfDueUnsafe(DateTime now, bool force = false)
        {
            if (!force && (!_persistentStateDirty || now < _nextPersistentSave))
                return;

            SavePersistentStateNowUnsafe();
        }

        private static void SavePersistentStateNowUnsafe()
        {
            try
            {
                Directory.CreateDirectory(PersistentStateDirectory);
                var state = new PersistentGlobalQuestState
                {
                    NextEpoch = _nextPersistentEpoch,
                    Quests = _persistentQuests.Values.OrderBy(q => q.Lane).ToList(),
                    Progress = _persistentProgress.Select(kvp => new PersistentGlobalQuestProgressEntry { Key = kvp.Key, Progress = kvp.Value }).ToList(),
                    Vendors = _globalQuestVendors.ToList(),
                };
                File.WriteAllText(PersistentStatePath, JsonSerializer.Serialize(state, _persistentJsonOptions));
                _persistentStateDirty = false;
                _nextPersistentSave = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                log.Error($"[GlobalKillQuest] Failed to save persistent global quest state: {ex}");
            }
        }
        private static void PrunePersistentProgress()
        {
            var activeEpochs = _persistentQuests.Values.Select(q => q.Epoch).ToHashSet();
            foreach (var key in _persistentProgress.Keys)
            {
                var epochText = key.Split(':')[0];
                if (int.TryParse(epochText, out var epoch) && !activeEpochs.Contains(epoch))
                    _persistentProgress.TryRemove(key, out _);
            }
        }

        private static TimeSpan GetPersistentDuration(GlobalQuestLane lane)
        {
            switch (lane)
            {
                case GlobalQuestLane.Hourly:
                    return TimeSpan.FromHours(1);
                case GlobalQuestLane.Daily:
                    return TimeSpan.FromDays(1);
                case GlobalQuestLane.Weekly:
                    return TimeSpan.FromDays(7);
                default:
                    return TimeSpan.FromMinutes(30);
            }
        }

        public static string GetLaneLabel(GlobalQuestLane lane)
        {
            switch (lane)
            {
                case GlobalQuestLane.Hourly:
                    return "Hourly";
                case GlobalQuestLane.Daily:
                    return "Daily";
                case GlobalQuestLane.Weekly:
                    return "Weekly";
                default:
                    return "Half-hour";
            }
        }

        private static string MakePersistentKey(uint playerGuid, int epoch)
        {
            return $"{epoch}:{playerGuid}";
        }

        private static string MakePersistentCompleteKey(uint playerGuid, int epoch)
        {
            return $"{epoch}:{playerGuid}:complete";
        }
    }

    public class PersistentGlobalQuest
    {
        public GlobalKillQuestManager.GlobalQuestLane Lane { get; set; }
        public GlobalKillQuestManager.GlobalQuestKind Kind { get; set; }
        public int Epoch { get; set; }
        public DateTime Start { get; set; }
        public DateTime Expiry { get; set; }
        public int QuestStartTimestamp { get; set; }
        public int QuestExpiryTimestamp { get; set; }
        public int Required { get; set; }
        public string TargetName { get; set; }
        public string ItemName { get; set; }
        public uint ItemWcid { get; set; }
        public long LuminanceReward { get; set; }
        public int CompletionCount { get; set; }
        public string Direction { get; set; }
        public int RewardPercent { get; set; }
        public uint SourceVendorWcid { get; set; }
        public string SourceVendorName { get; set; }
        public string SourceTown { get; set; }
        public string DestinationTown { get; set; }
    }

    public class PersistentGlobalQuestProgress
    {
        public int Count { get; set; }
        public double Distance { get; set; }
        public bool Completed { get; set; }
    }

    public class PersistentGlobalQuestState
    {
        public int NextEpoch { get; set; }
        public List<PersistentGlobalQuest> Quests { get; set; } = new List<PersistentGlobalQuest>();
        public List<PersistentGlobalQuestProgressEntry> Progress { get; set; } = new List<PersistentGlobalQuestProgressEntry>();
        public List<GlobalQuestVendorRecord> Vendors { get; set; } = new List<GlobalQuestVendorRecord>();
    }

    public class PersistentGlobalQuestProgressEntry
    {
        public string Key { get; set; }
        public PersistentGlobalQuestProgress Progress { get; set; }
    }
}
