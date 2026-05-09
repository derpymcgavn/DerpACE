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

            // Stalker's Bow: configurable chance on T6+ bows to grant a first-strike damage bonus (see @lootconfig)
            if (roll.WeaponType == TreasureWeaponType.Bow
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.StalkerBowMinTier
                && ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.StalkerBowDropChance)
            {
                var procPct = (int)System.Math.Round(ACE.Common.ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.StalkerProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.StalkerProcMax));
                var bonusPct = (int)System.Math.Round(ACE.Common.ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.StalkerBonusMin,
                    (float)ACE.Server.Managers.DerpACEConfig.StalkerBonusMax));
                if (procPct < 1) procPct = 1;
                if (bonusPct < 1) bonusPct = 1;

                wo.Name = wo.Name + " of the Stalker";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsStalkersBow, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeProc,  procPct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeBonus, bonusPct / 100.0);
                wo.IconOverlayId = 0x06002699u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis bow rewards the patient hunter \u2014 the *first* arrow loosed at a target has a {procPct}% chance to strike with +{bonusPct}% bonus damage. Switching targets or letting the target drop resets the opportunity.";
            }

            // Breacher's Crossbow: configurable chance on T6+ crossbows for an always-on armor pierce % (see @lootconfig)
            if (roll.WeaponType == TreasureWeaponType.Crossbow
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.BreacherCrossbowMinTier
                && ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.BreacherCrossbowDropChance)
            {
                var piercePct = (int)System.Math.Round(ACE.Common.ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.BreacherPierceMin,
                    (float)ACE.Server.Managers.DerpACEConfig.BreacherPierceMax));
                if (piercePct < 1) piercePct = 1;

                wo.Name = wo.Name + " of the Breacher";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsBreachersCrossbow, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.BreacherPiercePct, piercePct / 100.0);
                wo.IconOverlayId = 0x06002878u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis crossbow drives bolts through plate \u2014 every hit ignores {piercePct}% of the damage absorbed by the target's armor and adds it back as bonus damage.";
            }

            // Reaper's Atlatl: configurable chance on T6+ atlatls for a kill-fed self-heal proc (see @lootconfig)
            if (roll.WeaponType == TreasureWeaponType.Atlatl
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.ReaperAtlatlMinTier
                && ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ReaperAtlatlDropChance)
            {
                var procPct = (int)System.Math.Round(ACE.Common.ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.ReaperProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.ReaperProcMax));
                var healPct = (int)System.Math.Round(ACE.Common.ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.ReaperHealMin,
                    (float)ACE.Server.Managers.DerpACEConfig.ReaperHealMax));
                if (procPct < 1) procPct = 1;
                if (healPct < 1) healPct = 1;

                wo.Name = wo.Name + " of the Reaper";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsReapersAtlatl, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillProc,    procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillHealPct, healPct / 100.0);
                wo.IconOverlayId = 0x06002860u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis atlatl feasts on the slain \u2014 a killing blow has a {procPct}% chance to instantly restore {healPct}% of your maximum health.";
            }
        }

        private static string GetMissileScript(TreasureWeaponType weaponType, bool isElemental = false)
        {
            var elementalStr = isElemental ? "elemental" : "non_elemental";

            return "MissileWeapons." + weaponType.GetScriptName() + "_" + elementalStr + ".txt";
        }
    }
}
