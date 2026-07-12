using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Tables;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static partial class GlobalKillQuestManager
    {
        private static readonly List<GlobalQuestVendorRecord> _globalQuestVendors = new List<GlobalQuestVendorRecord>();

        public static void RegisterGlobalQuestVendor(Vendor vendor)
        {
            if (vendor?.Location == null || vendor.Location.Indoors || vendor.Location.InstanceId != 0)
                return;

            var town = VendorTownTier.GetTownName((int)vendor.Location.LandblockX, (int)vendor.Location.LandblockY);
            if (string.IsNullOrWhiteSpace(town))
                return;

            var items = vendor.DefaultItemsForSale.Values
                .Where(IsEligibleDeliveryItem)
                .Select(item => new GlobalQuestVendorItem { Wcid = item.WeenieClassId, Name = item.Name })
                .GroupBy(item => item.Wcid)
                .Select(group => group.First())
                .Take(100)
                .ToList();
            if (items.Count == 0)
                return;

            var record = new GlobalQuestVendorRecord
            {
                VendorWcid = vendor.WeenieClassId,
                VendorName = vendor.Name,
                TownName = town,
                Landblock = vendor.Location.Landblock,
                WorldX = vendor.Location.LandblockX * ACE.Entity.Position.BlockLength + vendor.Location.PositionX,
                WorldY = vendor.Location.LandblockY * ACE.Entity.Position.BlockLength + vendor.Location.PositionY,
                Items = items,
            };

            lock (_persistentLock)
            {
                _globalQuestVendors.RemoveAll(existing => existing.VendorWcid == record.VendorWcid && string.Equals(existing.TownName, record.TownName, StringComparison.OrdinalIgnoreCase));
                _globalQuestVendors.Add(record);
                MarkPersistentStateDirtyUnsafe();
            }
        }

        private static bool IsEligibleDeliveryItem(WorldObject item)
        {
            return item != null
                && item.GetProperty(PropertyBool.VendorService) != true
                && item.WeenieType != WeenieType.Portal
                && item.WeenieType != WeenieType.Creature
                && !item.IsAttunedOrContainsAttuned
                && !string.IsNullOrWhiteSpace(item.Name);
        }

        private static bool CanRollVendorDelivery()
        {
            return _globalQuestVendors.Select(record => record.TownName).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
        }

        private static void ConfigurePersistentVendorDelivery(PersistentGlobalQuest quest)
        {
            var sources = _globalQuestVendors.Where(record => record.Items != null && record.Items.Count > 0).ToList();
            if (sources.Count == 0)
            {
                ConfigurePersistentCardinalTrek(quest);
                return;
            }

            var source = sources[_rng.Next(sources.Count)];
            var destinations = _globalQuestVendors
                .Where(record => !string.Equals(record.TownName, source.TownName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(record => record.TownName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (destinations.Count == 0)
            {
                ConfigurePersistentCardinalTrek(quest);
                return;
            }

            var destination = destinations[_rng.Next(destinations.Count)];
            var item = source.Items[_rng.Next(source.Items.Count)];
            var dx = destination.WorldX - source.WorldX;
            var dy = destination.WorldY - source.WorldY;
            var distanceClicks = Math.Sqrt(dx * dx + dy * dy) / 240.0;

            quest.Kind = GlobalQuestKind.VendorDeliveryRace;
            quest.SourceVendorWcid = source.VendorWcid;
            quest.SourceVendorName = source.VendorName;
            quest.SourceTown = source.TownName;
            quest.DestinationTown = destination.TownName;
            quest.ItemWcid = item.Wcid;
            quest.ItemName = item.Name;
            quest.Required = 1;
            quest.RewardPercent = Math.Clamp(50 + (int)Math.Round(distanceClicks / 10.0), 50, 200);
            quest.LuminanceReward = 0;
            quest.Direction = null;
            quest.TargetName = $"Buy {item.Name} from {source.VendorName} in {source.TownName}; deliver it in {destination.TownName}";
        }

        public static void OnGlobalQuestVendorPurchase(Player player, Vendor vendor, WorldObject item)
        {
            if (player == null || vendor == null || item == null)
                return;

            RegisterGlobalQuestVendor(vendor);
            lock (_persistentLock)
            {
                foreach (var quest in ActivePersistentQuests(DateTime.UtcNow).Where(q => q.Kind == GlobalQuestKind.VendorDeliveryRace))
                {
                    if (quest.SourceVendorWcid != vendor.WeenieClassId || quest.ItemWcid != item.WeenieClassId || IsNonRepeatPersistentQuestCompleted(player, quest))
                        continue;

                    var town = VendorTownTier.GetTownName((int)vendor.Location.LandblockX, (int)vendor.Location.LandblockY);
                    if (!string.Equals(town, quest.SourceTown, StringComparison.OrdinalIgnoreCase))
                        continue;

                    item.SetProperty(PropertyInt.NomadTrophyOwner, unchecked((int)player.Guid.Full));
                    item.SetProperty(PropertyInt.NomadTrophySourceWcid, unchecked((int)vendor.WeenieClassId));
                    item.SetProperty(PropertyInt.NomadTrophyQuestEpoch, quest.Epoch);
                    item.SetProperty(PropertyInt.NomadTrophyFoundTimestamp, (int)Time.GetUnixTime());
                    item.MaxStackSize = 1;
                    item.SetStackSize(1);
                    item.Name = $"Courier's {quest.ItemName}";
                    item.LongDesc = $"Purchased from {quest.SourceVendorName} in {quest.SourceTown}. Deliver this parcel to an NPC in {quest.DestinationTown} before another courier wins the race.";
                }
            }
        }

        public static bool TryTurnInGlobalQuestDelivery(Player player, WorldObject item, WorldObject target)
        {
            if (player == null || item == null || target?.Location == null)
                return false;

            PersistentGlobalQuest completedQuest = null;
            lock (_persistentLock)
            {
                var stampedEpoch = item.GetProperty(PropertyInt.NomadTrophyQuestEpoch) ?? -1;
                var quest = ActivePersistentQuests(DateTime.UtcNow)
                    .FirstOrDefault(candidate => candidate.Kind == GlobalQuestKind.VendorDeliveryRace && candidate.Epoch == stampedEpoch);
                if (quest == null)
                    return false;

                if ((item.GetProperty(PropertyInt.NomadTrophyOwner) ?? 0) != unchecked((int)player.Guid.Full)
                    || item.WeenieClassId != quest.ItemWcid
                    || (item.GetProperty(PropertyInt.NomadTrophySourceWcid) ?? 0) != unchecked((int)quest.SourceVendorWcid))
                {
                    player.SendMessage("The receiver refuses the parcel. It was not purchased by you for this delivery.", ChatMessageType.Broadcast);
                    player.SendUseDoneEvent();
                    return true;
                }

                var destinationTown = VendorTownTier.GetTownName((int)target.Location.LandblockX, (int)target.Location.LandblockY);
                if (!string.Equals(destinationTown, quest.DestinationTown, StringComparison.OrdinalIgnoreCase))
                {
                    player.SendMessage($"The receiver checks the label. This parcel belongs in {quest.DestinationTown}.", ChatMessageType.Broadcast);
                    player.SendUseDoneEvent();
                    return true;
                }

                if (!player.TryConsumeFromInventoryWithNetworking(item, 1))
                {
                    player.SendUseDoneEvent();
                    return true;
                }

                if (!TryFinishPersistentQuest(player, quest))
                {
                    player.SendMessage("Another courier has already completed this delivery.", ChatMessageType.Broadcast);
                    player.SendUseDoneEvent();
                    return true;
                }

                completedQuest = quest;
                BroadcastPersistentWrapUp(quest);
                RollPersistentQuest(quest.Lane, true, DateTime.UtcNow);
                SavePersistentStateNowUnsafe();
            }

            var levelXp = player.GetXPToNextLevel(player.Level ?? 1);
            var bonus = Math.Max(1, (long)Math.Round(levelXp * (completedQuest.RewardPercent / 100.0)));
            player.EarnXP(bonus, XpType.Quest);
            player.SendMessage($"[Global Quest Complete:{GetLaneLabel(completedQuest.Lane)}] Delivery accepted in {completedQuest.DestinationTown}! You earned {bonus:N0} XP ({completedQuest.RewardPercent}% of level XP).", ChatMessageType.Broadcast);
            BroadcastPersistentCompletion(player, completedQuest, $"{player.Name} won Dereth Express, delivering {completedQuest.ItemName} from {completedQuest.SourceTown} to {completedQuest.DestinationTown}!");
            player.SendUseDoneEvent();
            return true;
        }
    }

    public class GlobalQuestVendorRecord
    {
        public uint VendorWcid { get; set; }
        public string VendorName { get; set; }
        public string TownName { get; set; }
        public uint Landblock { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public List<GlobalQuestVendorItem> Items { get; set; } = new List<GlobalQuestVendorItem>();
    }

    public class GlobalQuestVendorItem
    {
        public uint Wcid { get; set; }
        public string Name { get; set; }
    }
}