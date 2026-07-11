using System;
using System.Collections.Generic;

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
        // Hammer WCIDs eligible for Bonebreak crush variant (instead of Ravager bleed)
        private static readonly HashSet<int> HammerWcids = new HashSet<int>
        {
            359,    // War Hammer
            542,    // Lugian Hammer
            1436,   // Hammer of Lightning
            2018,   // Trothyr's War Hammer
            3905,   // Acid War Hammer
            3906,   // Lightning War Hammer
            3907,   // Flaming War Hammer
            3908,   // Frost War Hammer
            4982,   // Hammer of Frore
            7522,   // Scroll of Hammering Crawler
            12465,  // Hammer of Might
            14508,  // Hammer of Acid
            14509,  // Hammer of Fire
            14510,  // Hammer of Ice
            14511,  // Hammer of Lightning
            14862,  // Hammer of Olthoi Slaying
            22846,  // The Hammer
            23753,  // Lugian Hammer
            23754,  // Lugian Hammer
            23755,  // Lugian Hammer
            23756,  // Lugian Hammer
            26009,  // Hammer of Frore
            30866,  // Hammer of the Fallen
            31151,  // War Hammer
            31152,  // War Hammer
            31153,  // War Hammer
            31154,  // War Hammer
            31155,  // War Hammer
            31156,  // War Hammer
            31157,  // War Hammer
            31158,  // War Hammer
            31159,  // War Hammer
            31338,  // Gronk the Hammer
            31763,  // Frost Lugian Hammer
            31764,  // Lugian Hammer
            31765,  // Acid Lugian Hammer
            31766,  // Lightning Lugian Hammer
            31767,  // Flaming Lugian Hammer
            31838,  // Hammer of Discipline
            35462,  // Jarvis Hammerstone
            35535,  // "Doom Hammer" Summoning Gem
            35547,  // Doom Hammer
            35598,  // Bonecrunch's Hammer
            36659,  // Hammer of the Ages
            38267,  // Gavin Hammerstone
            38421,  // Kieran Stronghammer
            38935,  // Lugian Hammer
            41420,  // Hammer
            45113,  // Hammer
            45114,  // Acid Hammer
            45115,  // Lightning Hammer
            45116,  // Flaming Hammer
            45117,  // Frost Hammer
            51460,  // (name not provided in list)
            73081   // Shade Iron Ore Hammer
        };

        private static readonly HashSet<int> LugianHammerThrowWcids = new HashSet<int>
        {
            542,    // Lugian Hammer
            23753,  // Lugian Hammer
            23754,  // Lugian Hammer
            23755,  // Lugian Hammer
            23756,  // Lugian Hammer
            31763,  // Frost Lugian Hammer
            31764,  // Lugian Hammer
            31765,  // Acid Lugian Hammer
            31766,  // Lightning Lugian Hammer
            31767,  // Flaming Lugian Hammer
            38935   // Lugian Hammer
        };

        // Returns a flavor noun for the rolled weapon type so long descriptions match the actual weapon.
        private static string GetWeaponNoun(TreasureWeaponType weaponType)
        {
            switch (weaponType)
            {
                case TreasureWeaponType.Sword:           return "sword";
                case TreasureWeaponType.SwordMS:         return "blade";
                case TreasureWeaponType.TwoHandedSword:  return "greatsword";
                case TreasureWeaponType.Dagger:          return "dagger";
                case TreasureWeaponType.DaggerMS:        return "dagger";
                case TreasureWeaponType.Axe:             return "axe";
                case TreasureWeaponType.TwoHandedAxe:    return "greataxe";
                case TreasureWeaponType.Mace:            return "mace";
                case TreasureWeaponType.MaceJitte:       return "jitte";
                case TreasureWeaponType.TwoHandedMace:   return "maul";
                case TreasureWeaponType.Spear:           return "spear";
                case TreasureWeaponType.TwoHandedSpear:  return "spear";
                case TreasureWeaponType.Staff:           return "staff";
                case TreasureWeaponType.Unarmed:         return "weapon";
                case TreasureWeaponType.Bow:             return "bow";
                case TreasureWeaponType.Crossbow:        return "crossbow";
                case TreasureWeaponType.Atlatl:          return "atlatl";
                case TreasureWeaponType.Discus:          return "discus";
                case TreasureWeaponType.Platter:         return "platter";
                default:                                 return "weapon";
            }
        }

        private static int RollTierScaledInt(int min, int max, int tier, int minTier)
        {
            if (max <= min)
                return min;

            // Bias rolls upward as tier rises so higher-tier drops feel stronger.
            var clampedTier = Math.Max(tier, minTier);
            var t = Math.Clamp((clampedTier - minTier) / 6.0f, 0.0f, 1.0f); // tier 2..8 => 0..1
            var scaledMin = min + (int)Math.Round((max - min) * t * 0.6f);
            if (scaledMin > max)
                scaledMin = max;

            return (int)Math.Round(ThreadSafeRandom.Next((float)scaledMin, (float)max));
        }

        private static float GetAdjustedModifierChance(float baseChance)
        {
            var adjusted = baseChance * ACE.Server.Managers.DerpACEConfig.LootModifierGlobalDropMultiplier;
            return Math.Clamp(adjusted, 0.0f, 1.0f);
        }

        /// <summary>
        /// Attempts to roll a cast-on-strike elemental blast proc onto a weapon.
        /// Only fires when the weapon has an elemental damage type, no ProcSpell is already set,
        /// and the tier/chance check passes.  Can stack on top of any named modifier because the
        /// proc is handled via the engine's existing ProcSpell/ProcSpellRate fields.
        /// </summary>
        private static void TryRollWeaponBlastProc(WorldObject wo, TreasureDeath profile)
        {
            if (!ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons || !ACE.Server.Managers.DerpACEConfig.WeaponElemBlastEnabled)
                return;

            var minTier = ACE.Server.Managers.DerpACEConfig.WeaponBlastProcMinTier;
            if (profile == null || profile.Tier < minTier)
                return;

            // Don't overwrite an existing proc (Archmagi, item-native procs, etc.)
            if (wo.ProcSpell.HasValue && wo.ProcSpell.Value != 0)
                return;

            // Tier-scaled drop chance: lerps from ChanceMin at minTier to ChanceMax at T8.
            var chanceMin = ACE.Server.Managers.DerpACEConfig.WeaponBlastProcChanceMin;
            var chanceMax = ACE.Server.Managers.DerpACEConfig.WeaponBlastProcChanceMax;
            var t = Math.Clamp((profile.Tier - minTier) / (float)(8 - minTier), 0f, 1f);
            var rollChance = chanceMin + (chanceMax - chanceMin) * t;

            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= rollChance)
                return;

            // Map damage type to a level-3 blast spell ID.
            ACE.Entity.Enum.SpellId? blastSpell = wo.W_DamageType switch
            {
                DamageType.Fire     => ACE.Entity.Enum.SpellId.FlameBlast3,
                DamageType.Cold     => ACE.Entity.Enum.SpellId.FrostBolt3,
                DamageType.Acid     => ACE.Entity.Enum.SpellId.AcidBlast3,
                DamageType.Electric => ACE.Entity.Enum.SpellId.LightningBlast3,
                _                   => null
            };

            if (blastSpell == null)
                return;

            var procRate = ThreadSafeRandom.Next(
                ACE.Server.Managers.DerpACEConfig.WeaponBlastProcRateMin,
                ACE.Server.Managers.DerpACEConfig.WeaponBlastProcRateMax);

            wo.ProcSpell = (uint)blastSpell.Value;
            wo.ProcSpellRate = procRate;
            wo.ProcSpellSelfTargeted = false;

            ApplyLootUiEffects(wo, wo.W_DamageType, true);

            wo.IconOverlayId = wo.W_DamageType switch
            {
                DamageType.Acid     => 0x0600667Bu,
                DamageType.Electric => 0x06006680u,
                DamageType.Fire     => 0x06005B3Au,
                DamageType.Cold     => 0x06005B3Eu,
                _                   => wo.IconOverlayId
            };

            var nameSuffix = wo.W_DamageType switch
            {
                DamageType.Fire     => "of Cinders",
                DamageType.Cold     => "of Rime",
                DamageType.Acid     => "of Vitriol",
                DamageType.Electric => "of Tempests",
                _                   => null
            };
            if (nameSuffix != null && (wo.Name == null || !wo.Name.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase)))
                wo.Name = wo.Name + " " + nameSuffix;

            var elemName = wo.W_DamageType switch
            {
                DamageType.Fire     => "flame",
                DamageType.Cold     => "frost",
                DamageType.Acid     => "acid",
                DamageType.Electric => "lightning",
                _                   => "elemental"
            };

            var pctDisplay = (procRate * 100.0).ToString("0.###");
            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis weapon occasionally releases a burst of {elemName} — each strike carries a {pctDisplay}% chance to discharge a level 3 {elemName} blast.";
        }

        private static bool TryRollWeaponModifier(TreasureDeath profile, ref bool specialModifierApplied, float baseChance, int minTier, bool primaryEligible)
        {
            return TryRollWeaponModifier(profile, null, ref specialModifierApplied, baseChance, minTier, primaryEligible);
        }

        private static bool TryRollWeaponModifier(TreasureDeath profile, TreasureRoll roll, ref bool specialModifierApplied, float baseChance, int minTier, bool primaryEligible, params string[] forcedAliases)
        {
            if (profile == null || profile.Tier < minTier)
            {
                if (!IsForcedWeaponModifier(roll, forcedAliases))
                    return false;
            }

            if (ACE.Server.Managers.DerpACEConfig.LootModifierExclusivePerItem && specialModifierApplied)
                return false;

            if (!primaryEligible)
                return false;

            if (IsForcedWeaponModifier(roll, forcedAliases))
            {
                specialModifierApplied = true;
                return true;
            }

            if (HasForcedWeaponModifier(roll))
                return false;

            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= GetAdjustedModifierChance(baseChance))
                return false;

            specialModifierApplied = true;
            return true;
        }

        private static bool IsForcedWeaponModifier(TreasureRoll roll, params string[] aliases)
        {
            if (roll == null || string.IsNullOrWhiteSpace(roll.ForcedWeaponMutator) || aliases == null)
                return false;

            if (!TryResolveWeaponMutator(roll.ForcedWeaponMutator, out var forcedName))
                forcedName = NormalizeWeaponMutatorName(roll.ForcedWeaponMutator);

            foreach (var alias in aliases)
            {
                if (TryResolveWeaponMutator(alias, out var canonicalAlias))
                {
                    if (string.Equals(forcedName, canonicalAlias, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (string.Equals(forcedName, NormalizeWeaponMutatorName(alias), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasForcedWeaponModifier(TreasureRoll roll)
        {
            return roll != null && !string.IsNullOrWhiteSpace(roll.ForcedWeaponMutator);
        }

        public static WorldObject CreateMeleeWeapon(TreasureDeath profile, bool isMagical, TreasureWeaponType? forcedWeaponType = null, string forcedWeaponMutator = null)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Weapon);
            treasureRoll.WeaponType = forcedWeaponType ?? WeaponTypeChance.MeleeChances.Roll();
            treasureRoll.ForcedWeaponMutator = forcedWeaponMutator;
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

            var specialModifierApplied = false;

            // Thief's Dagger: configurable chance on any T6+ dagger (see @lootconfig)
            // Equipping grants 50% translucency, -aggro weight, and +10% sneak attack damage.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.ThievesDaggerDropChance,
                ACE.Server.Managers.DerpACEConfig.ThievesDaggerMinTier,
                roll.WeaponType == TreasureWeaponType.Dagger || roll.WeaponType == TreasureWeaponType.DaggerMS,
                "thief"))
            {
                wo.Name = wo.Name + " of the Thief";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger, true);
                wo.IconOverlayId = MutatorOverlayThief;
                wo.IconUnderlayId = 0x060065FC;
                ApplyLootUiEffect(wo, UiEffects.Poisoned);

                // require Specialized Sneak Attack (WieldRequirement.Training, difficulty = 3 = Specialized)
                wo.WieldRequirements = WieldRequirement.Training;
                wo.WieldSkillType = (int)Skill.SneakAttack;
                wo.WieldDifficulty = (int)SkillAdvancementClass.Specialized;

                var procPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.ThievesDaggerProcChance, 0.0f, 1.0f) * 100.0f);
                var bonusPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.ThievesDaggerProcBonus, 0.0f, 1.0f) * 100.0f);
                var seamPenalty = ACE.Server.Managers.DerpACEConfig.ThievesDaggerSeamPenalty;
                var seamDuration = Math.Max(1, ACE.Server.Managers.DerpACEConfig.ThievesDaggerSeamDuration);
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} was honed in shadow - while equipped, you appear translucent and monsters are less likely to notice you. Successful attacks have a {procPct}% chance to shadowstep behind the target and become a guaranteed critical sneak attack, dealing 1.05x to 2.25x damage and opening a hidden seam in the target's guard. The seam lowers defense by {seamPenalty} for {seamDuration} seconds.";
            }

            // Quickening Dagger: dagger hits can grant a short attack-animation haste window.
            if (ACE.Server.Managers.DerpACEConfig.QuickeningDaggerEnabled
                && TryRollWeaponModifier(
                    profile,
                    roll,
                    ref specialModifierApplied,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerDropChance,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerMinTier,
                    roll.WeaponType == TreasureWeaponType.Dagger || roll.WeaponType == TreasureWeaponType.DaggerMS,
                    "quickening"))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerProcMin,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerMinTier);
                var speedPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerSpeedMin,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerSpeedMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerMinTier);
                var duration = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerDurationMin,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerDurationMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.QuickeningDaggerMinTier);

                wo.Name = wo.Name + " of Quickening";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsQuickeningDagger, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.QuickeningDaggerProcChance, procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.QuickeningDaggerSpeedMultiplier, 1.0 + speedPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.QuickeningDaggerDuration, duration);
                wo.IconOverlayId = MutatorOverlayQuickening;
                ApplyLootUiEffect(wo, UiEffects.Lightning | UiEffects.BoostStamina);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} twitches ahead of the hand — each hit has a {procPct}% chance to quicken your attacks by {speedPct}% for {duration} seconds.";
            }

            // Fencer's Blade: configurable chance on T6+ épée / rapier / schlager (see @lootconfig)
            // SwordMS is exclusively these three weapon types — no additional WCID check required.
            // Pierce proc: per-weapon chance to bypass armor (deals mitigated damage × piercePct as bonus).
            // Deflect proc: per-incoming-hit chance to reflect 10% of damage back at the attacker.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.FencerBladeDropChance,
                ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier,
                roll.WeaponType == TreasureWeaponType.SwordMS,
                "fencer"))
            {
                var piercePct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.FencerPierceMin,
                    ACE.Server.Managers.DerpACEConfig.FencerPierceMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier);
                var pierceProc = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.FencerPierceProcMin,
                    ACE.Server.Managers.DerpACEConfig.FencerPierceProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier);
                var deflectChance = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.FencerDeflectMin,
                    ACE.Server.Managers.DerpACEConfig.FencerDeflectMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier);
                var parryPct = RollTierScaledInt(
                    5,
                    15,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier);

                wo.Name = wo.Name + " of the Fencer";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsFencerBlade, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPiercePct,  piercePct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPierceProc, pierceProc / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerDeflectChance,   deflectChance / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerParryPct,         parryPct / 100.0);
                wo.IconOverlayId = MutatorOverlayFencer;
                ApplyLootUiEffect(wo, UiEffects.Piercing);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} is perfectly balanced for dueling — each strike has a {pierceProc}% chance to find a gap in the target's defenses, bypassing {piercePct}% of their armor. There is also a {deflectChance}% chance per incoming hit to turn an attack aside and redirect 10% of its damage back at the assailant.";
            }

            if (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsFencerBlade) == true)
            {
                var fencerPierceProc = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPierceProc) ?? 0.0) * 100.0;
                var fencerPiercePct = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPiercePct) ?? 0.0) * 100.0;
                var fencerRiposteChance = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerDeflectChance) ?? 0.0) * 100.0;
                var fencerParryPct = (wo.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerParryPct) ?? 0.0) * 100.0;
                wo.LongDesc = GetLongDesc(wo) + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} is perfectly balanced for dueling -- each strike has a {fencerPierceProc:0}% chance to exploit an opening, dealing bonus damage equal to {fencerPiercePct:0}% of what the target's armor stopped. It also has a {fencerRiposteChance:0}% chance to riposte incoming melee pressure with a precise counterthrust. When held offhand, it becomes a parry sword: {fencerParryPct:0}% chance to reduce and reflect {fencerParryPct:0}% of incoming melee damage with a point-down flourish and stamina-down effect. Pierce, riposte, and parry use separate short cooldowns.";
            }

            // Pugilist: unarmed weapons get a family-specific proc.
            // Cestus/knuckles/handwraps punch with Iron Flurry; katars pierce; nekodes/claws rake with slash/pierce.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                0.05f,
                5,
                roll.WeaponType == TreasureWeaponType.Unarmed,
                "pugilist", "combo", "flurry", "rake"))
            {
                var name = wo.Name ?? "";
                var isKatar = name.IndexOf("katar", StringComparison.OrdinalIgnoreCase) >= 0;
                var isNekode = name.IndexOf("nekode", StringComparison.OrdinalIgnoreCase) >= 0;
                var isClaw = name.IndexOf("claw", StringComparison.OrdinalIgnoreCase) >= 0;
                var isRake = isKatar || isNekode || isClaw;

                var procPct = RollTierScaledInt(6, 10, profile.Tier, 5);
                var style = isKatar ? 3 : isNekode && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.5f ? 3 : isRake ? 2 : 1;
                var scale = isRake ? 0.45 : 0.35;
                var duration = isRake ? 6.0 : 0.0;
                var rakeDamageType = style == 3 ? "piercing" : "slashing";

                wo.Name = wo.Name + (isRake ? " of the Raking Hand" : " of the Iron Flurry");
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsPugilistUnarmedWeapon, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyInt.PugilistStyle, style);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PugilistProcChance, procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PugilistDamageScale, scale);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PugilistDurationSeconds, duration);
                wo.CooldownId = Player.PugilistCooldownId;
                wo.CooldownDuration = Player.PugilistCooldownSeconds;
                wo.IconOverlayId = isRake ? 0x06002886u : 0x06002867u;
                ApplyLootUiEffect(wo, isRake ? (style == 3 ? UiEffects.Piercing : UiEffects.Slashing) : UiEffects.Bludgeoning);

                if (isRake)
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nRaking Hand: successful strikes have a {procPct}% chance to tear the target for {scale:P0} of the hit's damage as {rakeDamageType} trauma over {duration:0.#} seconds. Cooldown: {Player.PugilistCooldownSeconds:0.#} seconds.";
                else
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nIron Flurry: successful strikes have a {procPct}% chance to snap in a second short-range blow for {scale:P0} of the original hit as bludgeoning damage. Cooldown: {Player.PugilistCooldownSeconds:0.#} seconds.";
            }

            // Ravager's Axe: configurable chance on T6+ axes (1H or 2H) to apply a bleed DoT (see @lootconfig)
            // Bleed total damage = bleedPct% of the triggering hit, spread evenly across RavagerBleedTicks at RavagerBleedInterval seconds.
            // Two-handed axes get the bleed total scaled by RavagerTwoHandMult.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.RavagerAxeDropChance,
                ACE.Server.Managers.DerpACEConfig.RavagerAxeMinTier,
                roll.WeaponType == TreasureWeaponType.Axe || roll.WeaponType == TreasureWeaponType.TwoHandedAxe,
                "ravager"))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.RavagerProcMin,
                    ACE.Server.Managers.DerpACEConfig.RavagerProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.RavagerAxeMinTier);
                var bleedPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.RavagerBleedMin,
                    ACE.Server.Managers.DerpACEConfig.RavagerBleedMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.RavagerAxeMinTier);

                var isTwoHanded = roll.WeaponType == TreasureWeaponType.TwoHandedAxe;
                var bleedFraction = bleedPct / 100.0;
                if (isTwoHanded)
                    bleedFraction *= ACE.Server.Managers.DerpACEConfig.RavagerTwoHandMult;

                // Check if this is a hammer weapon by WCID
                var isHammer = HammerWcids.Contains((int)roll.Wcid) || (wo.Name?.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0);

                wo.Name = wo.Name + (isHammer ? " of Bonebreak" : " of the Ravager");
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsRavagersAxe, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedProc, procPct / 100.0);
                wo.IconOverlayId = MutatorOverlayRavager;
                ApplyLootUiEffect(wo, isHammer ? UiEffects.Bludgeoning : UiEffects.Slashing);

                if (isHammer)
                {
                    // Hammer-named axes get a crushing mechanic instead of serrated bleed.
                    var crushBonusPct = Math.Clamp(bleedFraction * 0.4, 0.08, 0.15);
                    var stamDrainPct = Math.Clamp(crushBonusPct * 0.5, 0.04, 0.08);
                    wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedPct, crushBonusPct);

                    var displayCrush = (int)Math.Round(crushBonusPct * 100.0);
                    var displayDrain = (int)Math.Round(stamDrainPct * 100.0);
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis hammer-headed {GetWeaponNoun(roll.WeaponType)} crushes through guard — each strike has a {procPct}% chance to slam for +{displayCrush}% bonus damage and drain {displayDrain}% of the target's current stamina.{(isTwoHanded ? " The two-handed leverage amplifies the impact." : "")}";
                }
                else
                {
                    wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedPct, bleedFraction);

                    var displayBleed = (int)Math.Round(bleedFraction * 100.0);
                    var ticks = ACE.Server.Managers.DerpACEConfig.RavagerBleedTicks;
                    var interval = ACE.Server.Managers.DerpACEConfig.RavagerBleedInterval;
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} is wickedly serrated — each strike has a {procPct}% chance to inflict a vicious bleed dealing {displayBleed}% of the hit's damage over {ticks} ticks ({interval:0.#}s apart).{(isTwoHanded ? " The two-handed grip drives the wound deeper." : "")}";
                }
            }

            // Warden's Maul: configurable chance on T6+ maces (1H, MS, or 2H) to apply a flat defense-skill debuff (see @lootconfig)
            // Two-handed maces get the penalty scaled by WardenTwoHandMult.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.WardenMaulDropChance,
                ACE.Server.Managers.DerpACEConfig.WardenMaulMinTier,
                roll.WeaponType == TreasureWeaponType.Mace
                    || roll.WeaponType == TreasureWeaponType.MaceJitte
                    || roll.WeaponType == TreasureWeaponType.TwoHandedMace,
                "warden"))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.WardenProcMin,
                    ACE.Server.Managers.DerpACEConfig.WardenProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.WardenMaulMinTier);
                var penalty = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.WardenPenaltyMin,
                    ACE.Server.Managers.DerpACEConfig.WardenPenaltyMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.WardenMaulMinTier);
                var duration = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.WardenDurationMin,
                    ACE.Server.Managers.DerpACEConfig.WardenDurationMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.WardenMaulMinTier);

                var isTwoHandedMace = roll.WeaponType == TreasureWeaponType.TwoHandedMace;
                if (isTwoHandedMace)
                    penalty = (int)Math.Round(penalty * ACE.Server.Managers.DerpACEConfig.WardenTwoHandMult);

                wo.Name = wo.Name + " of the Warden";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsWardensMaul, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussProc,     procPct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussPenalty,  penalty);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussDuration, duration);
                wo.IconOverlayId = MutatorOverlayWarden;
                ApplyLootUiEffect(wo, UiEffects.Bludgeoning);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} is forged for crushing guards — each strike has a {procPct}% chance to concuss the target, reducing their effective defense skill by {penalty} for {duration} seconds.{(isTwoHandedMace ? " The two-handed swing rattles bone." : "")}";
            }

            // Lugian Hammer Throw: rare Heavy Weapons Lugian hammer proc to hammer a second foe.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                0.04f,
                6,
                IsLugianHammerThrowEligible(wo),
                "lugianhammer", "hammerthrow", "thrownhammer"))
            {
                const float procChance = 0.08f;
                const float damageScale = 0.75f;
                const float radius = 10.0f;
                const float cooldown = 4.0f;

                wo.Name = wo.Name + " of the Stonehand";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsLugianHammerThrowWeapon, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.LugianHammerThrowProcChance, procChance);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.LugianHammerThrowDamageScale, damageScale);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.LugianHammerThrowRadius, radius);
                wo.CooldownId = Player.LugianHammerThrowCooldownId;
                wo.CooldownDuration = cooldown;
                wo.IconOverlayId = MutatorOverlayLugianHammer;
                ApplyLootUiEffect(wo, UiEffects.Bludgeoning);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nStonehand Throw: successful strikes have a {procChance:P0} chance to hurl a spectral hammer into another nearby foe within {radius:0.#} yards, dealing {damageScale:P0} of the original hit as bludgeoning damage. Cooldown: {cooldown:0.#} seconds.";
            }

            // Resolute Blade: configurable chance on T6+ swords (1H or 2H, excluding fencer SwordMS) (see @lootconfig)
            // On crit hits, restores % of damage as health. On killing blows, restores % of MaxHealth + MaxStamina.
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.ResoluteBladeDropChance,
                ACE.Server.Managers.DerpACEConfig.ResoluteBladeMinTier,
                roll.WeaponType == TreasureWeaponType.Sword || roll.WeaponType == TreasureWeaponType.TwoHandedSword,
                "resolute"))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.ResoluteProcMin,
                    ACE.Server.Managers.DerpACEConfig.ResoluteProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.ResoluteBladeMinTier);
                var healPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.ResoluteHealMin,
                    ACE.Server.Managers.DerpACEConfig.ResoluteHealMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.ResoluteBladeMinTier);

                var isTwoHandedSword = roll.WeaponType == TreasureWeaponType.TwoHandedSword;
                var killBurst = ACE.Server.Managers.DerpACEConfig.ResoluteKillBurstPct;
                if (isTwoHandedSword)
                    killBurst *= ACE.Server.Managers.DerpACEConfig.ResoluteTwoHandMult;

                wo.Name = wo.Name + " of Resolve";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsResoluteBlade, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealProc,     procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealPct,      healPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteKillBurstPct, killBurst);
                wo.IconOverlayId = MutatorOverlayResolute;
                ApplyLootUiEffect(wo, UiEffects.BoostHealth);

                var killBurstPct = (int)Math.Round(killBurst * 100.0);
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} is honed for the long fight — critical hits have a {procPct}% chance to restore {healPct}% of the damage dealt as health to the wielder. Killing blows surge with {killBurstPct}% of your maximum health and stamina.{(isTwoHandedSword ? " The two-handed grip drinks deeper from the slain." : "")}";
            }

            // Polebreaker: configurable chance on T6+ staves to escalate
            // damage on consecutive hits against the same target (see @lootconfig).
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.PolebreakerDropChance,
                ACE.Server.Managers.DerpACEConfig.PolebreakerMinTier,
                roll.WeaponType == TreasureWeaponType.Staff,
                "polebreaker"))
            {
                var stackPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.PolebreakerStackMin,
                    ACE.Server.Managers.DerpACEConfig.PolebreakerStackMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.PolebreakerMinTier);
                var maxStacks = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.PolebreakerMaxStackMin,
                    ACE.Server.Managers.DerpACEConfig.PolebreakerMaxStackMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.PolebreakerMinTier);
                if (stackPct < 1) stackPct = 1;
                if (maxStacks < 1) maxStacks = 1;

                wo.Name = wo.Name + " of the Polebreaker";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsPolebreakerStaff, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerStackBonus, stackPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerMaxStacks,  maxStacks);
                wo.CooldownId = Player.PolebreakerBreakGuardCooldownId;
                wo.CooldownDuration = 12.0;
                wo.IconOverlayId = MutatorOverlayPolebreaker;
                ApplyLootUiEffect(wo, UiEffects.BoostMana | UiEffects.BoostStamina);

                var totalPct = stackPct * maxStacks;
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} finds a deadly rhythm -- attacks made at 70% power or higher add +{stackPct}% bonus damage on consecutive hits against the same target, stacking up to {maxStacks} times (+{totalPct}% at full stack). At full rhythm, Break Guard drives the staff down in an overhead slam, lowering the target's defense by 15 for 5 seconds, then resets the rhythm and starts a 12 second cooldown. Non-qualifying attacks, switching targets, or letting the target die resets the chain.";
            }

            // Legacy unarmed-only elemental proc is disabled; universal elemental procs are rolled below.
            // Kept in place for now so old tuning references can be retired separately.
            if (false && roll.WeaponType == TreasureWeaponType.Unarmed && isMagical
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.UnarmedElemDropChance)
            {
                var elemSpell = wo.W_DamageType switch
                {
                    DamageType.Acid     => ACE.Entity.Enum.SpellId.AcidBlast3,
                    DamageType.Electric => ACE.Entity.Enum.SpellId.LightningBlast3,
                    DamageType.Fire     => ACE.Entity.Enum.SpellId.FlameBlast3,
                    DamageType.Cold     => ACE.Entity.Enum.SpellId.FrostBolt3,
                    _                   => (ACE.Entity.Enum.SpellId?)null
                };

                if (elemSpell.HasValue)
                {
                    var procPct = (int)Math.Round(ThreadSafeRandom.Next(
                        (float)ACE.Server.Managers.DerpACEConfig.UnarmedElemProcMin,
                        (float)ACE.Server.Managers.DerpACEConfig.UnarmedElemProcMax));
                    wo.ProcSpell = (uint)elemSpell.Value;
                    wo.ProcSpellRate = procPct / 100.0;
                    wo.ProcSpellSelfTargeted = false;

                    ApplyLootUiEffects(wo, wo.W_DamageType, true);

                    wo.IconOverlayId = wo.W_DamageType switch
                    {
                        DamageType.Acid     => 0x0600667Bu,
                        DamageType.Electric => 0x06006680u,
                        DamageType.Fire     => 0x06005B3Au,
                        DamageType.Cold     => 0x06005B3Eu,
                        _                   => (uint?)null
                    };

                    var nameSuffix = wo.W_DamageType switch
                    {
                        DamageType.Fire     => "of Cinders",
                        DamageType.Cold     => "of Rime",
                        DamageType.Acid     => "of Vitriol",
                        DamageType.Electric => "of Tempests",
                        _                   => (string)null
                    };
                    if (nameSuffix != null)
                        wo.Name = wo.Name + " " + nameSuffix;

                    var elemName = wo.W_DamageType switch
                    {
                        DamageType.Acid     => "acid",
                        DamageType.Electric => "lightning",
                        DamageType.Fire     => "flame",
                        DamageType.Cold     => "frost",
                        _                   => "elemental"
                    };
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis weapon crackles with {elemName} energy — each strike has a {procPct}% chance to discharge a {elemName} blast.";
                }
            }

            // Sentinel's Spear: configurable chance on any T6+ spear (see @lootconfig)
            if (TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.SentinelSpearDropChance,
                ACE.Server.Managers.DerpACEConfig.SentinelSpearMinTier,
                roll.WeaponType == TreasureWeaponType.Spear || roll.WeaponType == TreasureWeaponType.TwoHandedSpear,
                "sentinel"))
            {
                wo.Name = wo.Name + " of the Goldleaf Sentinel";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsSentinelSpear, true);
                wo.CooldownId = Player.GoldleafSentinelCooldownId;
                wo.CooldownDuration = Math.Max(1, ACE.Server.Managers.DerpACEConfig.SentinelSpearCooldownSeconds);
                wo.IconOverlayId = MutatorOverlaySentinel;
                ApplyLootUiEffect(wo, UiEffects.BoostStamina);

                var powerPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.SentinelSpearPowerThreshold, 0.0f, 1.0f) * 100.0f);
                var maxStacks = Math.Max(1, ACE.Server.Managers.DerpACEConfig.SentinelSpearMaxStacks);
                var drainPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.SentinelSpearDrainPct, 0.0f, 1.0f) * 100.0f);
                var returnPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.SentinelSpearReturnMult, 0.0f, 2.0f) * 100.0f);
                var poiseDuration = Math.Max(1, ACE.Server.Managers.DerpACEConfig.SentinelSpearPoiseDurationSeconds);
                var reductionPct = (int)Math.Round(Math.Clamp(ACE.Server.Managers.DerpACEConfig.SentinelSpearPoiseDamageReduction, 0.0f, 0.5f) * 100.0f);
                var cooldown = Math.Max(1, ACE.Server.Managers.DerpACEConfig.SentinelSpearCooldownSeconds);
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} rewards proper form - attacks made at {powerPct}% power or higher build Goldleaf Poise on consecutive hits against the same target. At {maxStacks} stacks, it drains {drainPct}% of the target's current stamina, restores {returnPct}% of the drained stamina to the wielder, grants {poiseDuration} seconds of {reductionPct}% damage reduction, then starts a {cooldown} second cooldown.";
            }

            // Second Shadow: rare melee-weapon shadow clone affix.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                0.0125f,
                7,
                true,
                "shadowclone", "secondshadow", "shadowblade"))
            {
                const float procChance = 0.03f;
                const float cooldownSeconds = 150.0f;
                const float durationSeconds = 16.0f;
                const float damageScale = 0.25f;

                wo.Name = wo.Name + " of the Second Shadow";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsShadowCloneWeapon, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneProcChance, procChance);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneCooldownSeconds, cooldownSeconds);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneDurationSeconds, durationSeconds);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneDamageScale, damageScale);
                wo.CooldownId = Player.ShadowCloneCasterCooldownId;
                wo.CooldownDuration = cooldownSeconds;
                wo.IconOverlayId = MutatorOverlayShadow;
                ApplyLootUiEffect(wo, UiEffects.Nether);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nSecond Shadow: successful strikes have a {procChance:P0} chance to summon a melee shadow for {durationSeconds:0}s. The shadow locks to melee combat, copies your equipped weapon style, fights alongside your normal pet, and deals {damageScale:P0} damage. Cooldown: {cooldownSeconds:0}s.";
            }

            // Universal blast-on-strike: rare chance for any elemental weapon T5+ to proc a level-3 blast.
            TryRollWeaponBlastProc(wo, profile);
        }

        private static string GetDamageScript(MeleeWeaponSkill weaponSkill, TreasureWeaponType weaponType)
        {
            return "MeleeWeapons.Damage_WieldDifficulty_DamageVariance." + weaponSkill.GetScriptName_Combined() + "_" + weaponType.GetScriptName() + ".txt";
        }

        private static string GetOffenseDefenseScript(MeleeWeaponSkill weaponSkill, TreasureWeaponType weaponType)
        {
            return "MeleeWeapons.WeaponOffense_WeaponDefense." + weaponType.GetScriptShortName() + "_offense_defense.txt";
        }

        private static bool IsLugianHammerThrowEligible(WorldObject wo)
        {
            if (wo == null || wo.WeaponSkill != Skill.HeavyWeapons)
                return false;

            if (!LugianHammerThrowWcids.Contains((int)wo.WeenieClassId))
                return false;

            return wo.W_WeaponType == WeaponType.Mace
                || (wo.Name?.IndexOf("lugian hammer", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
