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

        private static string PersistentStateDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "GlobalQuests");
        private static string PersistentStatePath => Path.Combine(PersistentStateDirectory, "global_quests.json");

        private static void InitializePersistentLanes()
        {
            lock (_persistentLock)
            {
                LoadPersistentState();
                EnsurePersistentLanes(DateTime.UtcNow, false);
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
                        BroadcastPersistentWrapUp(quest);
                        RollPersistentQuest(quest.Lane, true, now);
                        rolledQuest = true;
                    }
                }

                rolledQuest |= EnsurePersistentLanes(now, true);
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
                    if (quest.Kind != GlobalQuestKind.T8LuminanceHunt || !IsTier8Creature(creature) || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    var progress = AddPersistentProgress(player, quest, 1);
                    if (progress.Count == quest.Required)
                        completions.Add(() => CompletePersistentT8Luminance(player, quest));
                    else if (progress.Count == 1 || progress.Count % 10 == 0)
                        player.SendMessage($"[Global Quest:{GetLaneLabel(quest.Lane)}] {progress.Count}/{quest.Required} tier 8 creatures slain.", ChatMessageType.Broadcast);
                }

                SavePersistentStateIfDueUnsafe(DateTime.UtcNow);
            }

            foreach (var complete in completions)
                complete();
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

                    if ((item.GetProperty(PropertyInt.GlobalQuestCurrencyCountedEpoch) ?? -1) == quest.Epoch)
                        continue;

                    item.SetProperty(PropertyInt.GlobalQuestCurrencyCountedEpoch, quest.Epoch);
                    var progress = AddPersistentProgress(player, quest, Math.Max(1, item.StackSize ?? 1));
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

        private static WorldObject TryCreatePersistentT8CurrencyDrop(Player player, Creature source)
        {
            if (player == null || !IsTier8Creature(source))
                return null;

            PersistentGlobalQuest quest;
            lock (_persistentLock)
                quest = ActivePersistentQuests(DateTime.UtcNow).FirstOrDefault(q => q.Kind == GlobalQuestKind.T8CurrencyHunt);

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
            var completed = false;
            if (player != null)
            {
                if (_persistentProgress.TryGetValue(MakePersistentKey(player.Guid.Full, quest.Epoch), out var progress))
                    count = progress.Count;
                completed = _persistentProgress.ContainsKey(MakePersistentCompleteKey(player.Guid.Full, quest.Epoch));
            }

            return new GlobalQuestStatus
            {
                Lane = quest.Lane,
                Kind = quest.Kind,
                TargetName = quest.Kind == GlobalQuestKind.T8CurrencyHunt ? quest.ItemName : "Tier 8 Creature",
                RequiredKills = quest.Kind == GlobalQuestKind.T8LuminanceHunt ? quest.Required : 0,
                RequiredTurnIns = quest.Kind == GlobalQuestKind.T8CurrencyHunt ? quest.Required : 0,
                MyKills = quest.Kind == GlobalQuestKind.T8LuminanceHunt ? count : 0,
                MyTurnIns = quest.Kind == GlobalQuestKind.T8CurrencyHunt ? count : 0,
                Expiry = quest.Expiry,
                ItemWcid = quest.Kind == GlobalQuestKind.T8CurrencyHunt ? quest.ItemWcid : 0,
                LuminanceReward = quest.LuminanceReward,
                Completed = completed,
            };
        }

        private static void CompletePersistentT8Luminance(Player player, PersistentGlobalQuest quest)
        {
            if (!TryFinishPersistentQuest(player, quest))
                return;

            player.EarnLuminance(quest.LuminanceReward, XpType.Quest, ShareType.None);
            player.SendMessage($"[Global Quest Complete:{GetLaneLabel(quest.Lane)}] You slew {quest.Required} tier 8 creatures and earned {quest.LuminanceReward:N0} luminance!", ChatMessageType.Broadcast);
            BroadcastPersistentCompletion(player, quest, $"{player.Name} completed the {GetLaneLabel(quest.Lane)} tier 8 luminance hunt!");
        }

        private static void CompletePersistentT8Currency(Player player, PersistentGlobalQuest quest)
        {
            if (!TryFinishPersistentQuest(player, quest))
                return;

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
            var quest = CreatePersistentQuest(lane, now);
            _persistentQuests[lane] = quest;
            PrunePersistentProgress();
            MarkPersistentStateDirtyUnsafe(now);
            if (announce)
                BroadcastPersistentStart(quest);
        }

        private static PersistentGlobalQuest CreatePersistentQuest(GlobalQuestLane lane, DateTime now)
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

            if (lane == GlobalQuestLane.Hourly && ShouldRollT8CurrencyHunt())
                ConfigurePersistentCurrencyQuest(quest, 8, 17, 5000, 50001);
            else if (lane == GlobalQuestLane.Weekly)
                ConfigurePersistentCurrencyQuest(quest, 40, 81, 5000, 50001);
            else if (lane == GlobalQuestLane.Daily)
                ConfigurePersistentLuminanceQuest(quest, 75, 151, 5000, 50001);
            else
                ConfigurePersistentLuminanceQuest(quest, 30, 61, 5000, 50001);
            NormalizePersistentQuestReward(quest);
            return quest;
        }

        private static void NormalizePersistentQuestReward(PersistentGlobalQuest quest)
        {
            if (quest == null)
                return;

            if (quest.LuminanceReward > 0)
                quest.LuminanceReward = Math.Max(5000, Math.Min(50000, quest.LuminanceReward));

            if (quest.Kind == GlobalQuestKind.T8CurrencyHunt)
            {
                var currency = GetT8CurrencyBankItem();
                quest.ItemName = currency.name;
                quest.ItemWcid = currency.wcid;
            }
        }

        private static bool IsNonRepeatPersistentQuestCompleted(Player player, PersistentGlobalQuest quest)
        {
            return IsNonRepeatPersistentQuest(quest) && _persistentProgress.ContainsKey(MakePersistentCompleteKey(player.Guid.Full, quest.Epoch));
        }

        private static bool IsNonRepeatPersistentQuest(PersistentGlobalQuest quest)
        {
            return quest.Lane == GlobalQuestLane.Daily || quest.Lane == GlobalQuestLane.Weekly;
        }
        private static void ConfigurePersistentLuminanceQuest(PersistentGlobalQuest quest, int minRequired, int maxRequired, int minLum, int maxLum)
        {
            quest.Kind = GlobalQuestKind.T8LuminanceHunt;
            quest.Required = _rng.Next(minRequired, maxRequired);
            quest.LuminanceReward = _rng.Next(minLum, maxLum);
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

        private static void BroadcastPersistentStart(PersistentGlobalQuest quest)
        {
            var lane = GetLaneLabel(quest.Lane);
            var msg = quest.Kind == GlobalQuestKind.T8CurrencyHunt
                ? $"[Global Quest:{lane}] Correct the Corruption: recover {quest.Required} forged Derp Coins from tier 8 creatures for {quest.LuminanceReward:N0} luminance. Type /gquest for details."
                : $"[Global Quest:{lane}] Slay {quest.Required} tier 8 creatures for {quest.LuminanceReward:N0} luminance. Type /gquest for details.";
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
            PlayerManager.LogBroadcastChat(Channel.AllBroadcast, null, msg);
        }

        private static void BroadcastPersistentWrapUp(PersistentGlobalQuest quest)
        {
            var lane = GetLaneLabel(quest.Lane);
            var msg = quest.Kind == GlobalQuestKind.T8CurrencyHunt
                ? $"[Global Quest:{lane}] Correct the Corruption has ended. {quest.CompletionCount} adventurer{(quest.CompletionCount == 1 ? "" : "s")} completed it."
                : $"[Global Quest:{lane}] The tier 8 luminance hunt has ended. {quest.CompletionCount} adventurer{(quest.CompletionCount == 1 ? "" : "s")} completed it.";
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
        public string ItemName { get; set; }
        public uint ItemWcid { get; set; }
        public long LuminanceReward { get; set; }
        public int CompletionCount { get; set; }
    }

    public class PersistentGlobalQuestProgress
    {
        public int Count { get; set; }
        public bool Completed { get; set; }
    }

    public class PersistentGlobalQuestState
    {
        public int NextEpoch { get; set; }
        public List<PersistentGlobalQuest> Quests { get; set; } = new List<PersistentGlobalQuest>();
        public List<PersistentGlobalQuestProgressEntry> Progress { get; set; } = new List<PersistentGlobalQuestProgressEntry>();
    }

    public class PersistentGlobalQuestProgressEntry
    {
        public string Key { get; set; }
        public PersistentGlobalQuestProgress Progress { get; set; }
    }
}
