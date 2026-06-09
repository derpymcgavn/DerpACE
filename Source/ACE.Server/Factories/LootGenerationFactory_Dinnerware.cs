using System.Collections.Generic;

using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.Entity.Mutations;
using ACE.Server.WorldObjects;

using WeenieClassName = ACE.Server.Factories.Enum.WeenieClassName;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        private static readonly WeenieClassName RageaRangWcid = (WeenieClassName)420498;

        private static readonly HashSet<WeenieClassName> ThrowableDinnerwareWcids = new HashSet<WeenieClassName>
        {
            WeenieClassName.bowl,
            WeenieClassName.chalice,
            WeenieClassName.cup,
            WeenieClassName.ewer,
            WeenieClassName.flagon,
            WeenieClassName.goblet,
            WeenieClassName.mug,
            WeenieClassName.ornamentalbowl,
            WeenieClassName.dinnerplate,
            WeenieClassName.stoup,
            WeenieClassName.tankard,
            RageaRangWcid,
        };

        private static readonly DamageType[] ThrowableDinnerwareDamageTypes =
        {
            DamageType.Slash,
            DamageType.Pierce,
            DamageType.Bludgeon,
            DamageType.Fire,
            DamageType.Cold,
            DamageType.Acid,
            DamageType.Electric,
        };

        /// <summary>
        /// This is only called by /testlootgen command
        /// The actual lootgen system doesn't use this.
        /// </summary>
        private static WorldObject CreateDinnerware(TreasureDeath profile, bool isMagical)
        {
            var treasureRoll = new TreasureRoll(TreasureItemType.ArtObject);
            treasureRoll.Wcid = GenericWcids.RollNonThrowable(profile.Tier);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);
            MutateDinnerware(wo, profile, isMagical, treasureRoll);

            return wo;
        }

        private static void MutateDinnerware(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
            if (IsThrowableDinnerware(wo))
                MutateThrowableDinnerware(wo, profile, roll);

            // material type
            wo.MaterialType = GetMaterialType(wo, profile.Tier);

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

            // "Empty Flask" was the only dinnerware that never received spells
            if (isMagical && wo.WeenieClassId != (uint)WeenieClassName.flasksimple)
                AssignMagic(wo, profile, roll);

            // item value
            if (wo.HasMutateFilter(MutateFilter.Value))
                MutateValue(wo, profile.Tier, roll);

            // long desc
            wo.LongDesc = GetLongDesc(wo);
        }

        private static bool IsThrowableDinnerware(WorldObject wo)
        {
            return ThrowableDinnerwareWcids.Contains((WeenieClassName)wo.WeenieClassId);
        }

        private static void MutateThrowableDinnerware(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            // Throwable dinnerware uses light-weapon dagger damage rolls for balance, even when
            // the template is something exotic like the throwable claymore.
            const MeleeWeaponSkill statSkill = MeleeWeaponSkill.LightWeapons;
            const TreasureWeaponType statWeaponType = TreasureWeaponType.Dagger;

            roll.WeaponType = TreasureWeaponType.ThrownDinnerware;

            wo.UnlimitedUse = true;
            wo.ItemType |= ItemType.MissileWeapon;
            wo.ValidLocations = EquipMask.MissileWeapon;
            wo.DefaultCombatStyle = CombatStyle.ThrownWeapon;
            wo.WeaponSkill = Skill.MissileWeapons;
            wo.W_WeaponType = WeaponType.Thrown;
            wo.W_DamageType = ThrowableDinnerwareDamageTypes[ThreadSafeRandom.Next(0, ThrowableDinnerwareDamageTypes.Length - 1)];
            ApplyLootUiEffects(wo, wo.W_DamageType, false);
            wo.Biota.PropertiesSpellBook?.Clear();

            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.RicochetAtlatlEnabled)
            {
                var ricochetProcPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.RicochetProcMin,
                    ACE.Server.Managers.DerpACEConfig.RicochetProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.RicochetAtlatlMinTier);
                if (ricochetProcPct < 1) ricochetProcPct = 1;

                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsRicochetAtlatl, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetProcChance,  ricochetProcPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetDamageScale, ACE.Server.Managers.DerpACEConfig.RicochetDamageScale);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetRadius,      ACE.Server.Managers.DerpACEConfig.RicochetRadius);
            }

            if (wo.WeenieClassId == (uint)RageaRangWcid)
                wo.Name = GetThrowableClaymoreName(wo.W_DamageType);

            var scriptName = GetDamageScript(statSkill, statWeaponType);
            MutationCache.GetMutation(scriptName).TryMutate(wo, profile.Tier);

            scriptName = GetOffenseDefenseScript(statSkill, statWeaponType);
            MutationCache.GetMutation(scriptName).TryMutate(wo, profile.Tier);

            ApplyThrowableDinnerwareWieldRequirements(wo, profile.Tier);

            if (wo.WeaponTime != null)
            {
                var weaponSpeedMod = RollWeaponSpeedMod(profile);
                wo.WeaponTime = (int)(wo.WeaponTime * weaponSpeedMod);
            }
        }

        private static void ApplyThrowableDinnerwareWieldRequirements(WorldObject wo, int tier)
        {
            if (wo == null)
                return;

            var cap = tier switch
            {
                <= 1 => 0,
                2    => 250,
                3    => 270,
                4    => 290,
                5    => 315,
                6    => 360,
                7    => 375,
                _    => 385,
            };

            if (cap <= 0)
            {
                wo.WieldRequirements = WieldRequirement.Invalid;
                wo.WieldSkillType = null;
                wo.WieldDifficulty = null;
                return;
            }

            wo.WieldRequirements = WieldRequirement.RawSkill;
            wo.WieldSkillType = (int)Skill.MissileWeapons;
            wo.WieldDifficulty = wo.WieldDifficulty.HasValue
                ? System.Math.Min(wo.WieldDifficulty.Value, cap)
                : cap;
        }

        private static string GetThrowableClaymoreName(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Fire     => "Flaming Stormwrought Greatblade",
                DamageType.Cold     => "Frostbound Stormwrought Greatblade",
                DamageType.Acid     => "Acid-Etched Stormwrought Greatblade",
                DamageType.Electric => "Thundercharged Stormwrought Greatblade",
                DamageType.Slash    => "Rending Stormwrought Greatblade",
                DamageType.Pierce   => "Impaling Stormwrought Greatblade",
                DamageType.Bludgeon => "Crushing Stormwrought Greatblade",
                _                   => "Stormwrought Greatblade",
            };
        }
    }
}

