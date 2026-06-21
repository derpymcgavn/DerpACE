using System.Collections.Generic;

using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
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
            WeenieClassName.discus,
            WeenieClassName.platter,
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

        private const float WarriorPrincessCallDropChance = 0.01f;
        private const float WarriorPrincessCallProcMin = 0.05f;
        private const float WarriorPrincessCallProcMax = 0.08f;
        private const float FlyingBuffetDropChance = 0.01f;
        private const float FlyingBuffetProcMin = 0.03f;
        private const float FlyingBuffetProcMax = 0.05f;
        private const float FlyingBuffetFirstBounceDamageScale = 0.60f;

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

            if (IsSpecialThrowableTemplate(wo))
                SanitizeLootThrowableTemplate(wo);

            // long desc
            wo.LongDesc = GetLongDesc(wo);
            AppendThrowableDinnerwareMutatorDesc(wo);
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
            var specialModifierApplied = false;
            var isDiscus = roll.WeaponType == TreasureWeaponType.Discus
                || wo.WeenieClassId == (uint)WeenieClassName.discus;
            var isPlatter = roll.WeaponType == TreasureWeaponType.Platter
                || wo.WeenieClassId == (uint)WeenieClassName.platter;

            if (!isDiscus && !isPlatter)
                roll.WeaponType = TreasureWeaponType.ThrownDinnerware;
            else
                SanitizeLootThrowableTemplate(wo);

            wo.UnlimitedUse = true;
            wo.ItemType |= ItemType.MissileWeapon;
            wo.ValidLocations = EquipMask.MissileWeapon;
            wo.DefaultCombatStyle = CombatStyle.ThrownWeapon;
            wo.WeaponSkill = Skill.MissileWeapons;
            wo.W_WeaponType = WeaponType.Thrown;
            wo.W_DamageType = ThrowableDinnerwareDamageTypes[ThreadSafeRandom.Next(0, ThrowableDinnerwareDamageTypes.Length - 1)];
            ApplyLootUiEffects(wo, wo.W_DamageType, false);
            wo.Biota.PropertiesSpellBook?.Clear();

            var applyDinnerwareMutator = ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons
                && ACE.Server.Managers.DerpACEConfig.DinnerwareWeaponEnabled
                && TryRollWeaponModifier(
                    profile,
                    roll,
                    ref specialModifierApplied,
                    isPlatter
                        ? FlyingBuffetDropChance
                        : isDiscus
                            ? WarriorPrincessCallDropChance
                            : ACE.Server.Managers.DerpACEConfig.DinnerwareWeaponDropChance,
                    ACE.Server.Managers.DerpACEConfig.DinnerwareWeaponMinTier,
                    true,
                    isPlatter ? "platter" : isDiscus ? "discus" : "dinnerware");

            if (applyDinnerwareMutator)
            {
                var spinProcChance = isDiscus
                    ? ThreadSafeRandom.Next(WarriorPrincessCallProcMin, WarriorPrincessCallProcMax)
                    : isPlatter
                        ? ThreadSafeRandom.Next(FlyingBuffetProcMin, FlyingBuffetProcMax)
                    : ACE.Server.Managers.DerpACEConfig.DinnerwareSpinDropChance;
                var spinDamageScale = isPlatter
                    ? FlyingBuffetFirstBounceDamageScale
                    : ACE.Server.Managers.DerpACEConfig.DinnerwareSpinDamageScale;

                wo.Name = isPlatter
                    ? "Platter of the Flying Buffet"
                    : isDiscus
                        ? "Discus of the Warrior Princess's Call"
                        : wo.Name + " of the Banquet";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsDinnerwareWeapon, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinProcChance, spinProcChance);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinDamageScale, spinDamageScale);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinRadius, ACE.Server.Managers.DerpACEConfig.DinnerwareSpinRadius);
                wo.IconOverlayId = MutatorOverlayDinnerware;
                ApplyLootUiEffect(wo, UiEffects.Bludgeoning);
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

        private static void AppendThrowableDinnerwareMutatorDesc(WorldObject wo)
        {
            if (wo?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsDinnerwareWeapon) != true)
                return;

            var isDiscus = wo.WeenieClassId == (uint)WeenieClassName.discus;
            var isPlatter = wo.WeenieClassId == (uint)WeenieClassName.platter;
            var procChance = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinProcChance) ?? 0.0) * 100.0;
            var radius = wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinRadius) ?? ACE.Server.Managers.DerpACEConfig.DinnerwareSpinRadius;
            var firstBounceDamage = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.DinnerwareSpinDamageScale) ?? ACE.Server.Managers.DerpACEConfig.DinnerwareSpinDamageScale) * 100.0;

            wo.LongDesc = (wo.LongDesc ?? "") + (isPlatter
                ? $"\n\nFlying Buffet: this serving platter has a {procChance:0.#}% chance on throw to crash through up to four additional nearby foes within {radius:0.#} yards. The first bounce deals {firstBounceDamage:0}% damage, then later bounces deal 35%, 20%, and 10% damage. Cooldown: {Player.DinnerwareCooldownSeconds:0.#} seconds."
                : isDiscus
                    ? $"\n\nThis discus carries the call of a warrior princess - each throw has a {procChance:0.#}% chance to ricochet through up to four additional nearby foes within {radius:0.#} yards. The first bounce deals {firstBounceDamage:0}% damage, then later bounces deal 25%, 10%, and 5% damage. Cooldown: {Player.DinnerwareCooldownSeconds:0.#} seconds."
                    : $"\n\nThis dinnerware was raised for the feast instead of the table - each throw has a {procChance:0.#}% chance to carom through up to four additional nearby foes within {radius:0.#} yards, ringing out with china, crockery, and bad manners. The first bounce deals {firstBounceDamage:0}% damage, then later bounces deal 25%, 10%, and 5% damage. Cooldown: {Player.DinnerwareCooldownSeconds:0.#} seconds.");
        }

        public static bool IsSpecialThrowableLootTemplate(WorldObject wo)
        {
            return IsSpecialThrowableTemplate(wo);
        }

        private static bool IsSpecialThrowableTemplate(WorldObject wo)
        {
            if (wo == null)
                return false;

            return wo.WeenieClassId == (uint)WeenieClassName.discus
                || wo.WeenieClassId == (uint)WeenieClassName.platter
                || wo.WeenieClassId == (uint)RageaRangWcid;
        }

        private static void SanitizeLootThrowableTemplate(WorldObject wo)
        {
            if (wo == null)
                return;

            wo.TsysMutationData ??= 0x11000005;

            wo.Biota.PropertiesSpellBook?.Clear();
            wo.ProcSpell = null;
            wo.ProcSpellRate = null;
            wo.ProcSpellSelfTargeted = false;

            wo.ItemMaxMana = null;
            wo.ItemCurMana = null;
            wo.ItemManaCost = null;
            wo.ManaRate = null;
            wo.ItemSpellcraft = null;
            wo.ItemDifficulty = null;
            wo.ItemUseable = null;

            wo.WeaponDefense = null;
            wo.WeaponMissileDefense = null;
            wo.WeaponMagicDefense = null;
            wo.CriticalFrequency = null;
            wo.ResistanceModifierType = null;
            wo.ResistanceModifier = null;

            RemoveLootImbues(wo);
            wo.RemoveProperty(PropertyInt.ImbueStackingBits);

            wo.WieldRequirements2 = WieldRequirement.Invalid;
            wo.WieldSkillType2 = null;
            wo.WieldDifficulty2 = null;
            wo.WieldRequirements3 = WieldRequirement.Invalid;
            wo.WieldSkillType3 = null;
            wo.WieldDifficulty3 = null;
            wo.WieldRequirements4 = WieldRequirement.Invalid;
            wo.WieldSkillType4 = null;
            wo.WieldDifficulty4 = null;

            wo.UseRequiresSkill = null;
            wo.UseRequiresSkillLevel = null;
            wo.UseRequiresSkillSpec = null;
            wo.UseRequiresLevel = null;
            wo.ActivationResponse = ActivationResponse.Use;
        }

        private static string GetThrowableClaymoreName(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Fire     => "Flaming Stormwrought Greatblade",
                DamageType.Cold     => "Frostbound Stormwrought Greatblade",
                DamageType.Acid     => "Acid-Etched Stormwrought Greatblade",
                DamageType.Electric => "Thundercharged Stormwrought Greatblade",
                DamageType.Slash    => "Slashing Stormwrought Greatblade",
                DamageType.Pierce   => "Impaling Stormwrought Greatblade",
                DamageType.Bludgeon => "Crushing Stormwrought Greatblade",
                _                   => "Stormwrought Greatblade",
            };
        }

        private static void RemoveLootImbues(WorldObject wo)
        {
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbuedEffect);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbuedEffect2);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbuedEffect3);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbuedEffect4);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbuedEffect5);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyInt.ImbueStackingBits);
            wo.RemoveProperty(ACE.Entity.Enum.Properties.PropertyString.ImbuerName);
        }
    }
}

