using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Entity.Mutations;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        public static WorldObject CreateMissileWeapon(TreasureDeath profile, bool isMagical, bool mutate = true)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Weapon);
            treasureRoll.WeaponType = WeaponTypeChance.MissileChances.Roll();
            treasureRoll.Wcid = WeaponWcids.Roll(profile, ref treasureRoll.WeaponType);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);

            MutateMissileWeapon(wo, profile, isMagical, treasureRoll);
            
            return wo;
        }

        private static void MutateMissileWeapon(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
            // new method / mutation scripts
            var isElemental = wo.W_DamageType != DamageType.Undef;

            var scriptName = GetMissileScript(roll.WeaponType, isElemental);

            // mutate DamageMod / ElementalDamageBonus / WieldRequirements
            var mutationFilter = MutationCache.GetMutation(scriptName);

            mutationFilter.TryMutate(wo, profile.Tier);

            // mutate WeaponDefense
            mutationFilter = MutationCache.GetMutation("MissileWeapons.weapon_defense.txt");

            mutationFilter.TryMutate(wo, profile.Tier);

            // Apply elemental UI outline (Fire/Cold/Acid/Lightning/Slashing/Piercing/Bludgeoning/Nether)
            // for elemental missile weapons — covers dartflingers (atlatls), bows, and crossbows alike.
            if (isElemental)
            {
                var ui = IronmanFactory.GetElementalUiEffect(wo.W_DamageType);
                if (ui != UiEffects.Undef)
                    wo.UiEffects = ui;
            }

            // weapon speed
            if (wo.WeaponTime != null)
            {
                var weaponSpeedMod = RollWeaponSpeedMod(profile);
                wo.WeaponTime = (int)(wo.WeaponTime * weaponSpeedMod);
            }

            // material type
            var materialType = GetMaterialType(wo, profile.Tier);
            if (materialType > 0)
                wo.MaterialType = materialType;

            // item color
            MutateColor(wo);

            // gem count / gem material
            if (wo.GemCode != null)
                wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
            else
                wo.GemCount = ThreadSafeRandom.Next(1, 5);

            wo.GemType = RollGemType(profile.Tier);

            // workmanship
            wo.ItemWorkmanship = WorkmanshipChance.Roll(profile.Tier);

            // burden
            MutateBurden(wo, profile, true);

            // missile / magic defense
            wo.WeaponMissileDefense = MissileMagicDefense.Roll(profile.Tier);
            wo.WeaponMagicDefense = MissileMagicDefense.Roll(profile.Tier);

            // spells
            if (!isMagical)
            {
                wo.ItemManaCost = null;
                wo.ItemMaxMana = null;
                wo.ItemCurMana = null;
                wo.ItemSpellcraft = null;
                wo.ItemDifficulty = null;
                wo.ManaRate = null;
            }
            else
                AssignMagic(wo, profile, roll);

            // item value
            //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
                MutateValue(wo, profile.Tier, roll);

            // long description
            wo.LongDesc = GetLongDesc(wo);

            var specialModifierApplied = false;

            // Stalker's Bow: configurable chance on T6+ bows to grant a first-strike damage bonus (see @lootconfig)
            if (TryRollWeaponModifier(
                profile,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.StalkerBowDropChance,
                ACE.Server.Managers.DerpACEConfig.StalkerBowMinTier,
                roll.WeaponType == TreasureWeaponType.Bow,
                roll.WeaponType == TreasureWeaponType.Crossbow || roll.WeaponType == TreasureWeaponType.Atlatl))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.StalkerProcMin,
                    ACE.Server.Managers.DerpACEConfig.StalkerProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.StalkerBowMinTier);
                var bonusPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.StalkerBonusMin,
                    ACE.Server.Managers.DerpACEConfig.StalkerBonusMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.StalkerBowMinTier);
                if (procPct < 1) procPct = 1;
                if (bonusPct < 1) bonusPct = 1;

                wo.Name = wo.Name + " of the Stalker";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsStalkersBow, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeProc,  procPct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeBonus, bonusPct / 100.0);
                wo.IconOverlayId = 0x06002699u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} rewards the patient hunter -- the *first* shot loosed at a target has a {procPct}% chance to strike with +{bonusPct}% bonus damage. Switching targets resets the opportunity.";
            }

            // Breacher's Crossbow: configurable chance on T6+ crossbows for an always-on armor pierce % (see @lootconfig)
            if (TryRollWeaponModifier(
                profile,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.BreacherCrossbowDropChance,
                ACE.Server.Managers.DerpACEConfig.BreacherCrossbowMinTier,
                roll.WeaponType == TreasureWeaponType.Crossbow,
                roll.WeaponType == TreasureWeaponType.Bow || roll.WeaponType == TreasureWeaponType.Atlatl))
            {
                var armorIgnoreChance = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.BreacherArmorIgnoreMin,
                    ACE.Server.Managers.DerpACEConfig.BreacherArmorIgnoreMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.BreacherCrossbowMinTier);
                if (armorIgnoreChance < 1) armorIgnoreChance = 1;

                wo.Name = wo.Name + " of the Breacher";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsBreachersCrossbow, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.BreacherArmorIgnoreChance, armorIgnoreChance / 100.0);
                wo.IconOverlayId = 0x06002878u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} pierces through armor — {armorIgnoreChance}% chance on each shot to completely ignore the target's armor for that hit.";
            }

            // Reaper's Atlatl: configurable chance on T6+ atlatls for a kill-fed self-heal proc (see @lootconfig)
            if (TryRollWeaponModifier(
                profile,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.ReaperAtlatlDropChance,
                ACE.Server.Managers.DerpACEConfig.ReaperAtlatlMinTier,
                roll.WeaponType == TreasureWeaponType.Atlatl,
                roll.WeaponType == TreasureWeaponType.Bow || roll.WeaponType == TreasureWeaponType.Crossbow))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.ReaperProcMin,
                    ACE.Server.Managers.DerpACEConfig.ReaperProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.ReaperAtlatlMinTier);
                var healPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.ReaperHealMin,
                    ACE.Server.Managers.DerpACEConfig.ReaperHealMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.ReaperAtlatlMinTier);
                if (procPct < 1) procPct = 1;
                if (healPct < 1) healPct = 1;

                wo.Name = wo.Name + " of the Reaper";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsReapersAtlatl, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillProc,    procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillHealPct, healPct / 100.0);
                wo.IconOverlayId = 0x06002860u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} feasts on the slain \u2014 a killing blow has a {procPct}% chance to instantly restore {healPct}% of your maximum health.";
            }

            // Universal blast-on-strike: rare chance for any elemental weapon T5+ to proc a level-3 blast.
            TryRollWeaponBlastProc(wo, profile);
        }

        private static string GetMissileScript(TreasureWeaponType weaponType, bool isElemental = false)
        {
            var elementalStr = isElemental ? "elemental" : "non_elemental";

            return "MissileWeapons." + weaponType.GetScriptName() + "_" + elementalStr + ".txt";
        }
    }
}
