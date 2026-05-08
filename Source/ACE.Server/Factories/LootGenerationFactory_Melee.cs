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
        public static WorldObject CreateMeleeWeapon(TreasureDeath profile, bool isMagical)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Weapon);
            treasureRoll.WeaponType = WeaponTypeChance.MeleeChances.Roll();
            treasureRoll.Wcid = WeaponWcids.Roll(profile, ref treasureRoll.WeaponType);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);

            MutateMeleeWeapon(wo, profile, isMagical, treasureRoll);

            return wo;
        }

        private static void MutateMeleeWeapon(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
            // thanks to 4eyebiped for helping with the data analysis of magloot retail logs
            // that went into reversing these mutation scripts

            var weaponSkill = wo.WeaponSkill.ToMeleeWeaponSkill();

            // mutate Damage / WieldDifficulty / Variance
            var scriptName = GetDamageScript(weaponSkill, roll.WeaponType);

            var mutationFilter = MutationCache.GetMutation(scriptName);

            mutationFilter.TryMutate(wo, profile.Tier);

            // mutate WeaponOffense / WeaponDefense
            scriptName = GetOffenseDefenseScript(weaponSkill, roll.WeaponType);

            mutationFilter = MutationCache.GetMutation(scriptName);

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
                // clear base
                wo.ItemManaCost = null;
                wo.ItemMaxMana = null;
                wo.ItemCurMana = null;
                wo.ItemSpellcraft = null;
                wo.ItemDifficulty = null;
            }
            else
                AssignMagic(wo, profile, roll);

            // item value
            //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
                MutateValue(wo, profile.Tier, roll);

            // long description
            wo.LongDesc = GetLongDesc(wo);

            // Thief's Dagger: configurable chance on any T6+ dagger (see @lootconfig)
            // Equipping grants 50% translucency, -aggro weight, and +10% sneak attack damage.
            if ((roll.WeaponType == TreasureWeaponType.Dagger || roll.WeaponType == TreasureWeaponType.DaggerMS)
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.ThievesDaggerMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ThievesDaggerDropChance)
            {
                wo.Name = wo.Name + " of the Thief";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger, true);
                wo.IconUnderlayId = 0x060065FC;

                // require Specialized Sneak Attack (WieldRequirement.Training, difficulty = 3 = Specialized)
                wo.WieldRequirements = WieldRequirement.Training;
                wo.WieldSkillType = (int)Skill.SneakAttack;
                wo.WieldDifficulty = (int)SkillAdvancementClass.Specialized;

                wo.LongDesc = (wo.LongDesc ?? "") + "\n\nThis dagger was honed in shadow — while equipped, you appear translucent and monsters are less likely to notice you. Sneak attacks have a 10% chance to proc an additional 10% bonus damage.";
            }

            // Sentinel's Spear: configurable chance on any T6+ spear (see @lootconfig)
            if ((roll.WeaponType == TreasureWeaponType.Spear || roll.WeaponType == TreasureWeaponType.TwoHandedSpear)
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.SentinelSpearMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.SentinelSpearDropChance)
            {
                wo.Name = wo.Name + " of the Sentinel";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsSentinelSpear, true);
                wo.IconOverlayId = 0x06002699;
                wo.UiEffects = ACE.Entity.Enum.UiEffects.BoostStamina;

                wo.LongDesc = (wo.LongDesc ?? "") + "\n\nThis spear hums with a guardian's resolve — each strike has a 10% chance to drain 10% of the target's stamina, returning a quarter of it to the wielder.";
            }
        }

        private static string GetDamageScript(MeleeWeaponSkill weaponSkill, TreasureWeaponType weaponType)
        {
            return "MeleeWeapons.Damage_WieldDifficulty_DamageVariance." + weaponSkill.GetScriptName_Combined() + "_" + weaponType.GetScriptName() + ".txt";
        }

        private static string GetOffenseDefenseScript(MeleeWeaponSkill weaponSkill, TreasureWeaponType weaponType)
        {
            return "MeleeWeapons.WeaponOffense_WeaponDefense." + weaponType.GetScriptShortName() + "_offense_defense.txt";
        }
    }
}
