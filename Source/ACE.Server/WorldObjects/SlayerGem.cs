using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public class SlayerGem : Gem
    {
        public const uint DormantWeenieClassId = 2000602;
        public const uint ChargedWeenieClassId = 2000603;

        public const int SlayerMaxLevel = 100;
        public const long BaseXp = 1;
        public const double MinimumSlayerMod = 2.0;
        public const double MaximumSlayerMod = 2.75;
        public const double DestroyTargetChance = 0.5;
        public const int FullyTinkeredCount = 10;

        private const uint DefaultSetup = 0x0200018B;
        private const uint DefaultIcon = 0x06001036;

        private static readonly object VisualCacheLock = new object();
        private static Dictionary<ACE.Entity.Enum.CreatureType, (uint setup, uint icon)> creatureVisuals;

        public SlayerGem(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            Initialize();
        }

        public SlayerGem(Biota biota) : base(biota)
        {
            Initialize();
        }

        private void Initialize()
        {
            if (WeenieClassId != DormantWeenieClassId && WeenieClassId != ChargedWeenieClassId)
                return;

            SetProperty(PropertyBool.IsSlayerGem, true);

            if (WeenieClassId == ChargedWeenieClassId)
                SetProperty(PropertyBool.IsChargedSlayerGem, true);
        }

        public static bool IsSlayerGem(WorldObject item)
        {
            return item?.GetProperty(PropertyBool.IsSlayerGem) == true
                || item?.WeenieClassId == DormantWeenieClassId
                || item?.WeenieClassId == ChargedWeenieClassId;
        }

        public static bool IsCharged(WorldObject item)
        {
            return item?.GetProperty(PropertyBool.IsChargedSlayerGem) == true
                || item?.WeenieClassId == ChargedWeenieClassId;
        }

        public static void PrepareNewDormantGem(WorldObject gem)
        {
            if (gem == null)
                return;

            var creatureType = ACE.Server.Managers.HuntCreatureTypes.RollSlayerType();
            var slayerMod = ThreadSafeRandom.Next((float)MinimumSlayerMod, (float)MaximumSlayerMod);

            gem.SetProperty(PropertyBool.IsSlayerGem, true);
            gem.RemoveProperty(PropertyBool.IsChargedSlayerGem);
            gem.SlayerCreatureType = creatureType;
            gem.SlayerDamageBonus = Math.Round(slayerMod, 4);
            gem.ItemBaseXp = BaseXp;
            gem.ItemMaxLevel = SlayerMaxLevel;
            gem.ItemTotalXp = 0;
            gem.ItemXpStyle = ACE.Entity.Enum.ItemXpStyle.Fixed;
            gem.ItemUseable = Usable.No;
            gem.TargetType = ItemType.None;
            ApplyCreatureVisuals(gem);
            RefreshNameAndDescription(gem);
        }

        public static WorldObject CreateRandomDormantGem()
        {
            var gem = Factories.WorldObjectFactory.CreateNewWorldObject(DormantWeenieClassId);
            PrepareNewDormantGem(gem);
            return gem;
        }

        public static void ChargeGem(WorldObject gem)
        {
            if (gem == null)
                return;

            gem.SetProperty(PropertyBool.IsChargedSlayerGem, true);
            gem.ItemUseable = Usable.SourceContainedTargetContained;
            gem.TargetType = ItemType.WeaponOrCaster;
            gem.Name = GetChargedName(gem.SlayerCreatureType);
            ApplyCreatureVisuals(gem);
            RefreshNameAndDescription(gem);
        }

        public static void ApplyCreatureVisuals(WorldObject gem)
        {
            if (gem == null)
                return;

            if (gem.SlayerCreatureType == null || gem.SlayerCreatureType == ACE.Entity.Enum.CreatureType.Invalid)
            {
                gem.SetupTableId = DefaultSetup;
                gem.IconId = DefaultIcon;
                return;
            }

            if (TryGetCreatureVisuals(gem.SlayerCreatureType.Value, out var setup, out var icon))
            {
                gem.SetupTableId = setup;
                gem.IconId = icon;
            }
            else
            {
                gem.SetupTableId = DefaultSetup;
                gem.IconId = DefaultIcon;
            }
        }

        private static bool TryGetCreatureVisuals(ACE.Entity.Enum.CreatureType creatureType, out uint setup, out uint icon)
        {
            var visuals = GetCreatureVisuals();

            if (visuals.TryGetValue(creatureType, out var visual))
            {
                setup = visual.setup;
                icon = visual.icon;
                return true;
            }

            setup = 0;
            icon = 0;
            return false;
        }

        private static Dictionary<ACE.Entity.Enum.CreatureType, (uint setup, uint icon)> GetCreatureVisuals()
        {
            if (creatureVisuals != null)
                return creatureVisuals;

            lock (VisualCacheLock)
            {
                if (creatureVisuals != null)
                    return creatureVisuals;

                var creatureTypeProperty = (ushort)PropertyInt.CreatureType;
                var setupProperty = (ushort)PropertyDataId.Setup;
                var iconProperty = (ushort)PropertyDataId.Icon;

                creatureVisuals = DatabaseManager.World.GetAllWeenies()
                    .Where(weenie => weenie != null
                        && weenie.Type == (int)WeenieType.Creature
                        && weenie.WeeniePropertiesInt != null
                        && weenie.WeeniePropertiesDID != null
                        && weenie.WeeniePropertiesInt.Any(prop => prop.Type == creatureTypeProperty)
                        && weenie.WeeniePropertiesDID.Any(prop => prop.Type == setupProperty)
                        && weenie.WeeniePropertiesDID.Any(prop => prop.Type == iconProperty))
                    .GroupBy(weenie => (ACE.Entity.Enum.CreatureType)weenie.WeeniePropertiesInt.First(prop => prop.Type == creatureTypeProperty).Value)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var representative = group.OrderBy(weenie => weenie.ClassId).First();
                            return (
                                representative.WeeniePropertiesDID.First(prop => prop.Type == setupProperty).Value,
                                representative.WeeniePropertiesDID.First(prop => prop.Type == iconProperty).Value);
                        });

                return creatureVisuals;
            }
        }

        public static void RefreshNameAndDescription(WorldObject gem)
        {
            if (gem == null)
                return;

            var creatureName = GetCreatureName(gem.SlayerCreatureType);
            var slayerMod = gem.SlayerDamageBonus ?? MinimumSlayerMod;
            var level = gem.ItemLevel ?? 0;

            if (IsCharged(gem))
            {
                gem.Name = GetChargedName(gem.SlayerCreatureType);
                gem.LongDesc = $"A charged slayer gem attuned to {creatureName}. Use it on a fully tinkered weapon, wand, or missile weapon to add {creatureName} slayer x{slayerMod:0.####}. The ritual has a 50% chance to destroy the target item.";
            }
            else
            {
                gem.Name = $"{creatureName} Slayer Gem";
                gem.LongDesc = $"A rare slayer gem attuned to {creatureName}. Keep it in your inventory while killing {creatureName} creatures to feed it. Level {level}/{SlayerMaxLevel}. At level {SlayerMaxLevel}, it becomes a weapon applicator with a {creatureName} slayer modifier of x{slayerMod:0.####}.";
            }
        }

        public override void HandleActionUseOnTarget(Player player, WorldObject target)
        {
            if (!IsCharged(this))
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{Name} is not charged yet.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            if (target == null || (target.ItemType & ItemType.WeaponOrCaster) == 0)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("That gem can only be applied to a weapon, wand, or missile weapon.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            if (target.NumTimesTinkered < FullyTinkeredCount)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The {target.Name} must be fully tinkered before it can hold a slayer gem.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            if (target.SlayerCreatureType != null || target.SlayerDamageBonus != null)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The {target.Name} already has a slayer attunement.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            var actionChain = new ActionChain();
            var animTime = 0.0f;

            if (player.CombatMode != CombatMode.NonCombat)
            {
                var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
                actionChain.AddDelaySeconds(stanceTime);
                animTime += stanceTime;
            }

            animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

            actionChain.AddAction(player, () =>
            {
                if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null ||
                    player.FindObject(target.Guid.Full, Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems) == null)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat("The gem and target item must remain in your possession.", ChatMessageType.Tell));
                    player.SendUseDoneEvent();
                    return;
                }

                if (!player.TryConsumeFromInventoryWithNetworking(this, 1))
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat("The slayer gem could not be consumed.", ChatMessageType.Tell));
                    player.SendUseDoneEvent();
                    return;
                }

                var creatureType = SlayerCreatureType;
                var slayerMod = SlayerDamageBonus;

                if (ThreadSafeRandom.Next(0.0f, 1.0f) < DestroyTargetChance)
                {
                    if (!player.TryRemoveFromInventoryWithNetworking(target.Guid, out _, Player.RemoveFromInventoryAction.ConsumeItem) &&
                        !player.TryDequipObjectWithNetworking(target.Guid, out _, Player.DequipObjectAction.ConsumeItem))
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat("The ritual failed, but the target item could not be destroyed.", ChatMessageType.Tell));
                    }
                    else
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The slayer gem shatters and destroys the {target.Name}.", ChatMessageType.Broadcast));
                    }

                    player.SendUseDoneEvent();
                    return;
                }

                target.SlayerCreatureType = creatureType;
                target.SlayerDamageBonus = slayerMod;
                target.LongDesc = AppendSlayerDescription(target.LongDesc, creatureType, slayerMod);
                target.SaveBiotaToDatabase();

                player.Session.Network.EnqueueSend(
                    new GameMessagePrivateUpdatePropertyInt(target, PropertyInt.SlayerCreatureType, (int)(creatureType ?? ACE.Entity.Enum.CreatureType.Invalid)),
                    new GameMessagePrivateUpdatePropertyFloat(target, PropertyFloat.SlayerDamageBonus, slayerMod ?? MinimumSlayerMod),
                    new GameMessageSystemChat($"The {target.Name} is now attuned to {GetCreatureName(creatureType)} slayer x{slayerMod:0.####}.", ChatMessageType.Broadcast));

                player.SendUseDoneEvent();
            });

            actionChain.EnqueueChain();
        }

        private static string AppendSlayerDescription(string longDesc, CreatureType? creatureType, double? slayerMod)
        {
            var line = $"Slayer Gem: attuned to {GetCreatureName(creatureType)} slayer x{(slayerMod ?? MinimumSlayerMod):0.####}.";

            if (string.IsNullOrWhiteSpace(longDesc))
                return line;

            if (longDesc.IndexOf("Slayer Gem:", StringComparison.OrdinalIgnoreCase) >= 0)
                return longDesc;

            return longDesc.TrimEnd() + "\n\n" + line;
        }

        private static string GetChargedName(ACE.Entity.Enum.CreatureType? creatureType)
        {
            return $"Charged {GetCreatureName(creatureType)} Slayer Gem";
        }

        private static string GetCreatureName(ACE.Entity.Enum.CreatureType? creatureType)
        {
            return (creatureType ?? ACE.Entity.Enum.CreatureType.Invalid).ToString();
        }

    }
}
