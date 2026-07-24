using System;

using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Mutations;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        private static void TryMutateShieldAffixes(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (!ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons || wo == null || profile == null || !wo.IsShield)
                return;

            var rolledAffixes = new System.Collections.Generic.List<string>();
            var hasForcedShieldMutator = TryResolveShieldMutator(roll?.ForcedWeaponMutator, out var forcedShieldMutator);

            if (hasForcedShieldMutator)
            {
                switch (forcedShieldMutator)
                {
                    case "defender":
                        ApplyDefenderShield(wo);
                        break;
                    case "thorns":
                        ApplyThornsShield(wo);
                        AddShieldSuffix(wo, "of Thorns");
                        break;
                    case "bashing":
                        ApplyBashingShield(wo);
                        AddShieldSuffix(wo, "of Bashing");
                        break;
                    case "reflection":
                        ApplyProjectileReflectShield(wo);
                        AddShieldSuffix(wo, "of Reflection");
                        break;
                    case "spellmirror":
                        ApplySpellMirrorShield(wo);
                        AddShieldSuffix(wo, "of Spell Mirroring");
                        break;
                }

                return;
            }

            if (ACE.Server.Managers.DerpACEConfig.DefenderShieldEnabled
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.DefenderShieldMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.DefenderShieldDropChance)
            {
                ApplyDefenderShield(wo);
            }

            if (profile.Tier < 3)
                return;

            var reactiveAffixes = new System.Collections.Generic.List<string>();

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.10f)
                reactiveAffixes.Add("thorns");

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.10f)
                reactiveAffixes.Add("bashing");

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.06f)
                reactiveAffixes.Add("reflection");

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.04f)
                reactiveAffixes.Add("spellmirror");

            if (reactiveAffixes.Count == 0)
                return;

            var maxReactiveAffixes = profile.Tier >= 6 ? 2 : 1;
            if (profile.Tier >= 8 && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.15f)
                maxReactiveAffixes = 3;

            while (reactiveAffixes.Count > maxReactiveAffixes)
                reactiveAffixes.RemoveAt(ThreadSafeRandom.Next(0, reactiveAffixes.Count - 1));

            foreach (var affix in reactiveAffixes)
            {
                switch (affix)
                {
                    case "thorns":
                        ApplyThornsShield(wo);
                        rolledAffixes.Add("of Thorns");
                        break;
                    case "bashing":
                        ApplyBashingShield(wo);
                        rolledAffixes.Add("of Bashing");
                        break;
                    case "reflection":
                        ApplyProjectileReflectShield(wo);
                        rolledAffixes.Add("of Reflection");
                        break;
                    case "spellmirror":
                        ApplySpellMirrorShield(wo);
                        rolledAffixes.Add("of Spell Mirroring");
                        break;
                }
            }

            if (rolledAffixes.Count == 0)
                return;

            AddShieldSuffix(wo, rolledAffixes.Count > 1 ? "of Layered Wards" : rolledAffixes[0]);
        }

        private static void ApplyDefenderShield(WorldObject wo)
        {
            if (!wo.Name.StartsWith("Defender's ", StringComparison.OrdinalIgnoreCase))
                wo.Name = "Defender's " + wo.Name;

            wo.SetProperty(PropertyBool.IsDefendersShield, true);
            wo.IconOverlayId = MutatorOverlayDefender;
            ApplyLootUiEffect(wo, UiEffects.BoostHealth);

            if ((wo.LongDesc ?? "").Contains("protective challenge", StringComparison.OrdinalIgnoreCase))
                return;

            wo.LongDesc = (wo.LongDesc ?? "") + "\n\nThis shield resonates with a protective challenge - while equipped, nearby enemies weigh its bearer as a more tempting target when choosing who to attack.";
        }

        private static void ApplyThornsShield(WorldObject wo)
        {
            var reflectPct = ThreadSafeRandom.Next(2, 6) / 100.0;

            wo.SetProperty(PropertyBool.IsThornsShield, true);
            wo.SetProperty(PropertyFloat.ShieldThornsReflectPct, reflectPct);
            wo.IconOverlayId = MutatorOverlayThorns;
            ApplyLootUiEffect(wo, UiEffects.Poisoned);

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis shield answers violence with splinters of its own - when you take damage with the shield equipped, it reflects {reflectPct:P0} of the damage taken back at the attacker. Cooldown: 1 second.";
        }

        private static void ApplyBashingShield(WorldObject wo)
        {
            wo.SetProperty(PropertyBool.IsBashingShield, true);
            wo.SetProperty(PropertyFloat.ShieldBashingProcChance, 0.10);
            wo.SetProperty(PropertyFloat.ShieldBashingHealthPct, 0.10);
            wo.IconOverlayId = MutatorOverlayBashing;
            ApplyLootUiEffect(wo, UiEffects.Bludgeoning);

            wo.LongDesc = (wo.LongDesc ?? "") + "\n\nWith specialized Shield, this shield has a 10% chance on block or melee evade to bash the attacker. A bash deals bludgeoning damage based on shield armor level, capped at 10% of your current health, pushes monsters back 10 feet, and can interrupt monster spell windups. Cooldown: 8 seconds.";
        }

        private static void ApplyProjectileReflectShield(WorldObject wo)
        {
            var reflectChance = ThreadSafeRandom.Next(8, 13) / 100.0;

            wo.SetProperty(PropertyBool.IsProjectileReflectShield, true);
            wo.SetProperty(PropertyFloat.ShieldProjectileReflectChance, reflectChance);
            wo.IconOverlayId = MutatorOverlayReflection;
            ApplyLootUiEffect(wo, UiEffects.Piercing);

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis shield catches the line of a flying shot - when you take missile damage, it has a {reflectChance:P0} chance to negate that hit and reflect the damage back at the attacker. Cooldown: 6 seconds.";
        }

        private static void ApplySpellMirrorShield(WorldObject wo)
        {
            var mirrorChance = ThreadSafeRandom.Next(5, 11) / 100.0;

            wo.SetProperty(PropertyBool.IsSpellMirrorShield, true);
            wo.SetProperty(PropertyFloat.ShieldSpellMirrorChance, mirrorChance);
            wo.IconOverlayId = MutatorOverlaySpellMirror;
            ApplyLootUiEffect(wo, UiEffects.Magical);

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis shield holds a thin mirrored ward - when you take harmful spell damage, it has a {mirrorChance:P0} chance to reduce that spell hit by 50% and reflect the reduced damage back at the caster. Cooldown: 10 seconds.";
        }

        private static void AddShieldSuffix(WorldObject wo, string suffix)
        {
            if (!wo.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                wo.Name = $"{wo.Name} {suffix}";
        }

        /// <summary>
        /// This is only called by /testlootgen command
        /// The actual lootgen system doesn't use this.
        /// </summary>
        private static WorldObject CreateArmor(TreasureDeath profile, bool isMagical, bool isArmor)
        {
            var itemType = isArmor ? TreasureItemType.Armor : TreasureItemType.Clothing;
            var treasureRoll = new TreasureRoll(itemType);

            if (isArmor)
            {
                treasureRoll.ArmorType = ArmorTypeChance.Roll(profile.Tier);
                treasureRoll.Wcid = ArmorWcids.Roll(profile, ref treasureRoll.ArmorType);
            }
            else
                treasureRoll.Wcid = ClothingWcids.Roll(profile);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);
            treasureRoll.BaseArmorLevel = wo.ArmorLevel ?? 0;

            MutateArmor(wo, profile, isMagical, treasureRoll);

            return wo;
        }

        private static void MutateArmor(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
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
                wo.GemCount = ThreadSafeRandom.Next(1, 6);

            wo.GemType = RollGemType(profile.Tier);

            // workmanship
            wo.ItemWorkmanship = WorkmanshipChance.Roll(profile.Tier);

            // burden
            if (wo.HasMutateFilter(MutateFilter.EncumbranceVal))  // fixme: data
                MutateBurden(wo, profile, false);

            if (profile.Tier > 6 && !wo.HasArmorLevel())
            {
                // normally this is handled in the mutation script for armor
                // for clothing, just calling the generic method here
                RollWieldLevelReq_T7_T8(wo, profile);
            }

            AssignArmorLevel(wo, profile, roll);

            if (wo.HasMutateFilter(MutateFilter.ArmorModVsType))
                MutateArmorModVsType(wo, profile);

            if (isMagical)
            {
                AssignMagic(wo, profile, roll, true);
            }
            else
            {
                wo.ItemManaCost = null;
                wo.ItemMaxMana = null;
                wo.ItemCurMana = null;
                wo.ItemSpellcraft = null;
                wo.ItemDifficulty = null;
            }

            if (profile.Tier > 6 && !roll.ArmorType.IsSocietyArmor())
                wo.EquipmentSetId = EquipmentSetChance.Roll(wo, profile, roll);

            if (profile.Tier == 8)
                TryMutateGearRating(wo, profile, roll);

            // item value
            //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
                MutateValue(wo, profile.Tier, roll);

            wo.LongDesc = GetLongDesc(wo);

            if (roll.ItemType == TreasureItemType.Armor || roll.ItemType == TreasureItemType.SocietyArmor || roll.ItemType == TreasureItemType.Clothing)
            {
                TryMutateUnarmedDamage(wo, profile, roll);
                TryMutateCookingGloves(wo, profile, roll);
                TryMutateAlchemistGloves(wo, profile, roll);
                TryMutateDanceBoots(wo, profile, roll);
                TryMutateArmorSort(wo, profile, roll);
                TryMutateBattlemageHelm(wo, profile, roll);
            }

            TryMutateShieldAffixes(wo, profile, roll);
        }

        private static void TryMutateBattlemageHelm(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (!ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons || wo == null || profile == null)
                return;

            var forced = IsForcedArmorMutator(roll, "battlemage");

            var validLocs = (EquipMask)(wo.ValidLocations ?? 0);
            if (!validLocs.HasFlag(EquipMask.HeadWear))
                return;

            if (!forced && profile.Tier < 5)
                return;

            var rollChance = profile.Tier >= 8 ? 0.08f : profile.Tier >= 7 ? 0.06f : 0.04f;
            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= rollChance)
                return;

            wo.SetProperty(PropertyBool.IsBattlemageHelm, true);

            const string suffix = "of the Battlemage";
            if (!wo.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                wo.Name = $"{wo.Name} {suffix}";

            wo.IconOverlayId = MutatorOverlayBattlemage;
            ApplyLootUiEffect(wo, UiEffects.Magical);

            wo.LongDesc = (wo.LongDesc ?? "") + "\n\nBattlemage: while this helm is equipped, War Magic can satisfy Light Weapons wield requirements. Your light weapon attacks use War Magic for attack checks and Focus for physical damage scaling. Casters may also be paired with a spell focus for one-handed battlemage casting.";
        }

        private static readonly DamageType[] ArmorSortDamageTypes =
        {
            DamageType.Slash,
            DamageType.Pierce,
            DamageType.Bludgeon,
            DamageType.Cold,
            DamageType.Fire,
            DamageType.Acid,
            DamageType.Electric,
            DamageType.Health,
            DamageType.Stamina,
            DamageType.Mana,
            DamageType.Nether,
        };

        private static void TryMutateArmorSort(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (!ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons || wo == null || profile == null)
                return;

            if (wo.IsShield)
                return;

            var forced = IsForcedArmorMutator(roll, "armorsort");

            if (!forced && profile.Tier < 4)
                return;

            var rollChance = profile.Tier >= 8 ? 0.10f : profile.Tier >= 7 ? 0.08f : profile.Tier >= 6 ? 0.06f : 0.04f;
            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= rollChance)
                return;

            var bonus = forced ? 3 : RollArmorSortBonus(profile);
            var damageType = ArmorSortDamageTypes[ThreadSafeRandom.Next(0, ArmorSortDamageTypes.Length - 1)];
            var damageName = GetArmorSortDamageName(damageType);

            wo.ArmorSortDamageType = damageType;
            wo.ArmorSortDamageBonus = bonus;
            wo.IconOverlayId = GetArmorResonanceOverlay(damageType);

            var suffix = $"of {damageName} Resonance +{bonus}";
            if (!wo.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                wo.Name = $"{wo.Name} {suffix}";

            ApplyLootUiEffect(wo, GetArmorSortUiEffect(damageType));
            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nResonant Weave: while equipped, this piece adds +{bonus} {damageName} damage to matching outgoing melee, missile, and spell projectile hits. Matching equipped pieces harmonize with diminishing returns: strongest resonance full value, then half value, then quarter value, rounded to the nearest whole damage.";
        }

        private static int RollArmorSortBonus(TreasureDeath profile)
        {
            var roll = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (profile.Tier >= 8)
                return roll < 0.12f ? 3 : roll < 0.42f ? 2 : 1;

            if (profile.Tier >= 7)
                return roll < 0.08f ? 3 : roll < 0.32f ? 2 : 1;

            return roll < 0.20f ? 2 : 1;
        }

        private static string GetArmorSortDamageName(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Bludgeon => "Bludgeon",
                DamageType.Cold => "Frost",
                DamageType.Electric => "Lightning",
                _ => damageType.ToString(),
            };
        }

        private static UiEffects GetArmorSortUiEffect(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Slash => UiEffects.Slashing,
                DamageType.Pierce => UiEffects.Piercing,
                DamageType.Bludgeon => UiEffects.Bludgeoning,
                DamageType.Cold => UiEffects.Frost,
                DamageType.Fire => UiEffects.Fire,
                DamageType.Acid => UiEffects.Acid,
                DamageType.Electric => UiEffects.Lightning,
                DamageType.Health => UiEffects.BoostHealth,
                DamageType.Stamina => UiEffects.BoostStamina,
                DamageType.Mana => UiEffects.BoostMana,
                DamageType.Nether => UiEffects.Magical,
                _ => UiEffects.Magical,
            };
        }

        private static void TryMutateCookingGloves(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (wo == null || profile == null)
                return;

            if (TryResolveArmorMutator(roll?.ForcedWeaponMutator, out var forcedName) && !string.Equals(forcedName, "culinarian", StringComparison.OrdinalIgnoreCase))
                return;

            if (wo.GetProperty(PropertyBool.IsAlchemistGloves) == true)
                return;

            var forced = IsForcedArmorMutator(roll, "culinarian");

            var validLocs = (EquipMask)(wo.ValidLocations ?? 0);
            if (!validLocs.HasFlag(EquipMask.HandWear))
                return;

            if (!forced && profile.Tier < 4)
                return;

            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= 0.08f)
                return;

            var restoreBonus = GetCulinarianRestoreBonus(profile);

            wo.SetProperty(PropertyBool.IsCookingGloves, true);
            wo.SetProperty(PropertyFloat.CulinarianRestoreBonusPct, restoreBonus);
            wo.Name = $"{wo.Name} of the Culinarian";
            wo.IconOverlayId = MutatorOverlayCulinarian;
            wo.IconUnderlayId = 0x06001B3Cu;
            ApplyLootUiEffect(wo, UiEffects.BoostStamina);
            AddSpecializedCookingWieldRequirement(wo);
            wo.CooldownId = Food.WellFedCooldownId;
            wo.CooldownDuration = Food.WellFedDurationSeconds;

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nRequires specialized Cooking to wear.\nWhile worn, food and drink restore {restoreBonus:P0} more health, stamina, or mana. These gloves track each food item you eat; every tenth meal grants Well Fed for 2 hours, increasing all primary attributes by 5. The triggering meal restores 25% more. Removing the gloves or logging out removes the buff, but the gloves continue cooling down in real time.";
        }

        private static double GetCulinarianRestoreBonus(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;

            if (tier >= 8)
                return ThreadSafeRandom.Next(0.0f, 1.0f) < 0.10f ? 0.25 : 0.20;

            if (tier >= 6)
                return 0.15;

            return 0.10;
        }

        private static void TryMutateAlchemistGloves(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (wo == null || profile == null)
                return;

            if (wo.GetProperty(PropertyBool.IsCookingGloves) == true)
                return;

            var forced = IsForcedArmorMutator(roll, "alchemist");
            var forcedInstability = IsForcedArmorMutator(roll, "alchemicalinstability");
            forced |= forcedInstability;

            var validLocs = (EquipMask)(wo.ValidLocations ?? 0);
            if (!validLocs.HasFlag(EquipMask.HandWear))
                return;

            if (!forced && profile.Tier < 4)
                return;

            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= 0.08f)
                return;

            var potionBonus = GetAlchemistPotionBonus(profile);
            var splashChance = GetAlchemistSplashChance(profile);
            var splashTargets = GetAlchemistSplashTargets(profile);
            var hasInstability = forcedInstability || ShouldRollAlchemicalInstability(profile);
            var instabilityChance = hasInstability ? GetAlchemicalInstabilityChance(profile) : 0.0;

            wo.SetProperty(PropertyBool.IsAlchemistGloves, true);
            wo.SetProperty(PropertyFloat.AlchemistPotionBonusPct, potionBonus);
            wo.SetProperty(PropertyFloat.AlchemistSplashProcChance, splashChance);
            wo.SetProperty(PropertyFloat.AlchemistSplashTargetCount, splashTargets);
            if (hasInstability)
            {
                wo.SetProperty(PropertyBool.IsAlchemicalInstabilityGloves, true);
                wo.SetProperty(PropertyFloat.AlchemicalInstabilityProcChance, instabilityChance);
            }

            wo.Name = hasInstability ? $"{wo.Name} of Alchemical Instability" : $"{wo.Name} of the Alchemist";
            wo.IconOverlayId = MutatorOverlayAlchemist;
            ApplyLootUiEffect(wo, hasInstability ? UiEffects.Acid | UiEffects.Magical | UiEffects.Poisoned : UiEffects.Acid | UiEffects.Magical);
            AddSpecializedAlchemyWieldRequirement(wo);

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nRequires specialized Alchemy to wear.\nWhile worn, potions restore {potionBonus:P0} more health, stamina, or mana. Targeted alchemy phials have a {splashChance:P0} chance to splash their spell onto up to {(int)splashTargets} nearby monster target{(splashTargets == 1 ? "" : "s")} within 10 yards. Splash casts do not consume extra phials.";
            if (hasInstability)
                wo.LongDesc += $"\nAlchemical Instability: drinking potions has a {instabilityChance:P0} chance to backfire on you, either applying one random debuff or staining your hair and/or skin with Tumerok palette colors. Harmful thrown phials can also trigger an extra random debuff at half chance on their primary target.";
        }

        private static double GetAlchemistPotionBonus(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;

            if (tier >= 8)
                return ThreadSafeRandom.Next(13, 16) / 100.0;

            if (tier >= 6)
                return ThreadSafeRandom.Next(12, 15) / 100.0;

            return ThreadSafeRandom.Next(10, 13) / 100.0;
        }

        private static double GetAlchemistSplashChance(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;

            if (tier >= 8)
                return 0.18;

            if (tier >= 6)
                return 0.14;

            return 0.10;
        }

        private static int GetAlchemistSplashTargets(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;

            if (tier >= 8)
                return 3;

            if (tier >= 6)
                return 2;

            return 1;
        }

        private static bool ShouldRollAlchemicalInstability(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;
            if (tier < 6)
                return false;

            var chance = tier >= 8 ? 0.25f : 0.15f;
            return ThreadSafeRandom.Next(0.0f, 1.0f) < chance;
        }

        private static double GetAlchemicalInstabilityChance(TreasureDeath profile)
        {
            var tier = profile?.Tier ?? 1;
            if (tier >= 8)
                return ThreadSafeRandom.Next(6, 9) / 100.0;

            return ThreadSafeRandom.Next(4, 7) / 100.0;
        }

        private static void AddSpecializedCookingWieldRequirement(WorldObject wo)
        {
            var skill = (int)Skill.Cooking;
            var difficulty = (int)SkillAdvancementClass.Specialized;

            if (IsSpecializedCookingRequirement(wo.WieldRequirements, wo.WieldSkillType, wo.WieldDifficulty) ||
                IsSpecializedCookingRequirement(wo.WieldRequirements2, wo.WieldSkillType2, wo.WieldDifficulty2) ||
                IsSpecializedCookingRequirement(wo.WieldRequirements3, wo.WieldSkillType3, wo.WieldDifficulty3) ||
                IsSpecializedCookingRequirement(wo.WieldRequirements4, wo.WieldSkillType4, wo.WieldDifficulty4))
                return;

            if (wo.WieldRequirements == WieldRequirement.Invalid)
            {
                wo.WieldRequirements = WieldRequirement.Training;
                wo.WieldSkillType = skill;
                wo.WieldDifficulty = difficulty;
            }
            else if (wo.WieldRequirements2 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements2 = WieldRequirement.Training;
                wo.WieldSkillType2 = skill;
                wo.WieldDifficulty2 = difficulty;
            }
            else if (wo.WieldRequirements3 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements3 = WieldRequirement.Training;
                wo.WieldSkillType3 = skill;
                wo.WieldDifficulty3 = difficulty;
            }
            else if (wo.WieldRequirements4 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements4 = WieldRequirement.Training;
                wo.WieldSkillType4 = skill;
                wo.WieldDifficulty4 = difficulty;
            }
        }

        private static bool IsSpecializedCookingRequirement(WieldRequirement requirement, int? skillType, int? difficulty)
        {
            return requirement == WieldRequirement.Training &&
                   skillType == (int)Skill.Cooking &&
                   difficulty >= (int)SkillAdvancementClass.Specialized;
        }

        private static void AddSpecializedAlchemyWieldRequirement(WorldObject wo)
        {
            var skill = (int)Skill.Alchemy;
            var difficulty = (int)SkillAdvancementClass.Specialized;

            if (IsSpecializedAlchemyRequirement(wo.WieldRequirements, wo.WieldSkillType, wo.WieldDifficulty) ||
                IsSpecializedAlchemyRequirement(wo.WieldRequirements2, wo.WieldSkillType2, wo.WieldDifficulty2) ||
                IsSpecializedAlchemyRequirement(wo.WieldRequirements3, wo.WieldSkillType3, wo.WieldDifficulty3) ||
                IsSpecializedAlchemyRequirement(wo.WieldRequirements4, wo.WieldSkillType4, wo.WieldDifficulty4))
                return;

            if (wo.WieldRequirements == WieldRequirement.Invalid)
            {
                wo.WieldRequirements = WieldRequirement.Training;
                wo.WieldSkillType = skill;
                wo.WieldDifficulty = difficulty;
            }
            else if (wo.WieldRequirements2 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements2 = WieldRequirement.Training;
                wo.WieldSkillType2 = skill;
                wo.WieldDifficulty2 = difficulty;
            }
            else if (wo.WieldRequirements3 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements3 = WieldRequirement.Training;
                wo.WieldSkillType3 = skill;
                wo.WieldDifficulty3 = difficulty;
            }
            else if (wo.WieldRequirements4 == WieldRequirement.Invalid)
            {
                wo.WieldRequirements4 = WieldRequirement.Training;
                wo.WieldSkillType4 = skill;
                wo.WieldDifficulty4 = difficulty;
            }
        }

        private static bool IsSpecializedAlchemyRequirement(WieldRequirement requirement, int? skillType, int? difficulty)
        {
            return requirement == WieldRequirement.Training &&
                   skillType == (int)Skill.Alchemy &&
                   difficulty >= (int)SkillAdvancementClass.Specialized;
        }

        private static void TryMutateDanceBoots(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (wo == null || profile == null)
                return;

            var validLocs = (EquipMask)(wo.ValidLocations ?? 0);
            if (!validLocs.HasFlag(EquipMask.FootWear))
                return;

            var forced = TryResolveArmorMutator(roll?.ForcedWeaponMutator, out var forcedName) && IsDanceBootMutator(forcedName);

            if (!forced && profile.Tier < 4)
                return;

            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= 0.06f)
                return;

            var mutator = forced ? forcedName : RollDanceBootMutator();
            ApplyDanceBootMutator(wo, profile, mutator);
        }

        private static bool IsDanceBootMutator(string mutatorName)
        {
            return string.Equals(mutatorName, "healingdance", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mutatorName, "rejuvenatingdance", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mutatorName, "replenishingdance", StringComparison.OrdinalIgnoreCase);
        }

        private static string RollDanceBootMutator()
        {
            return ThreadSafeRandom.Next(0, 3) switch
            {
                0 => "healingdance",
                1 => "rejuvenatingdance",
                _ => "replenishingdance",
            };
        }

        private static void ApplyDanceBootMutator(WorldObject wo, TreasureDeath profile, string mutator)
        {
            var amount = GetDanceBootRestoreAmount(profile);
            var interval = GetDanceBootPulseInterval(profile);

            wo.SetProperty(PropertyFloat.DanceBootRestoreAmount, amount);
            wo.SetProperty(PropertyFloat.DanceBootPulseIntervalSeconds, interval);

            string suffix;
            string ability;
            string vital;
            UiEffects uiEffect;

            switch (mutator)
            {
                case "healingdance":
                    wo.SetProperty(PropertyBool.IsHealingDanceBoots, true);
                    suffix = "of Healing Dance";
                    ability = "Healing Dance";
                    vital = "health";
                    uiEffect = UiEffects.BoostHealth;
                    wo.IconOverlayId = MutatorOverlayHealingDance;
                    break;
                case "replenishingdance":
                    wo.SetProperty(PropertyBool.IsReplenishingDanceBoots, true);
                    suffix = "of Replenishing Dance";
                    ability = "Replenishing Dance";
                    vital = "mana";
                    uiEffect = UiEffects.BoostMana;
                    wo.IconOverlayId = MutatorOverlayReplenishingDance;
                    break;
                case "rejuvenatingdance":
                default:
                    wo.SetProperty(PropertyBool.IsRejuvenatingDanceBoots, true);
                    suffix = "of Rejuvenating Dance";
                    ability = "Rejuvenating Dance";
                    vital = "stamina";
                    uiEffect = UiEffects.BoostStamina;
                    wo.IconOverlayId = MutatorOverlayRejuvenatingDance;
                    break;
            }

            if (!wo.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                wo.Name = $"{wo.Name} {suffix}";

            ApplyLootUiEffect(wo, uiEffect);

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\n{ability}: while wearing these boots, perform /dance for 10 uninterrupted seconds to begin restoring {amount:0} {vital} every {interval:0.#} seconds to nearby fellowship members. If no fellows are nearby, the dance restores you instead. The dance ends when you stop dancing, enter combat, die, or remove the boots.";
        }

        private static int GetDanceBootRestoreAmount(TreasureDeath profile)
        {
            return profile?.Tier switch
            {
                >= 8 => ThreadSafeRandom.Next(7, 10),
                >= 7 => ThreadSafeRandom.Next(5, 8),
                >= 6 => ThreadSafeRandom.Next(3, 6),
                >= 5 => ThreadSafeRandom.Next(2, 5),
                _ => ThreadSafeRandom.Next(1, 3),
            };
        }

        private static double GetDanceBootPulseInterval(TreasureDeath profile)
        {
            return profile?.Tier switch
            {
                >= 8 => ThreadSafeRandom.Next(2.5f, 4.0f),
                >= 6 => ThreadSafeRandom.Next(3.5f, 5.0f),
                _ => ThreadSafeRandom.Next(4.5f, 6.0f),
            };
        }



        /// <summary>
        /// DerpACE: Rolls unarmed damage properties on gauntlets and boots.
        /// These properties only apply when the player has no weapon equipped (truly unarmed).
        /// </summary>
        private static void TryMutateUnarmedDamage(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (wo == null || profile == null)
                return;

            var forced = IsForcedArmorMutator(roll, "unarmed");

            // Only applies to gauntlets (HandWear) and boots (FootWear)
            var validLocs = (EquipMask)(wo.ValidLocations ?? 0);
            var isGauntlet = validLocs.HasFlag(EquipMask.HandWear);
            var isBoot = validLocs.HasFlag(EquipMask.FootWear);

            if (!isGauntlet && !isBoot)
                return;

            // Only T5+ can roll unarmed damage (keep it rare/high-tier)
            if (!forced && profile.Tier < 5)
                return;

            // 15% chance to roll unarmed damage properties
            var rollChance = 0.15f;
            if (!forced && ThreadSafeRandom.Next(0.0f, 1.0f) >= rollChance)
                return;

            // Base damage scales with tier: T5=8-12, T6=12-18, T7=18-25, T8=25-35
            int minDamage, maxDamage;
            switch (profile.Tier)
            {
                case 5:
                    minDamage = 8;
                    maxDamage = 12;
                    break;
                case 6:
                    minDamage = 12;
                    maxDamage = 18;
                    break;
                case 7:
                    minDamage = 18;
                    maxDamage = 25;
                    break;
                case 8:
                default:
                    minDamage = 25;
                    maxDamage = 35;
                    break;
            }

            var baseDamage = ThreadSafeRandom.Next(minDamage, maxDamage);
            // Store in both the custom property (used as a detection flag) and the standard
            // Damage/DamageVariance/DamageType that the client weapon-panel reads natively.
            wo.UnarmedBaseDamage = baseDamage;
            wo.Damage = baseDamage;

            // Variance: 0.6-0.8 (fairly tight, like quality weapons)
            var variance = ThreadSafeRandom.Next(0.6f, 0.8f);
            wo.UnarmedDamageVariance = variance;
            wo.DamageVariance = variance;

            // Roll a damage type for unarmed combat
            // All 7 melee damage types available: Fire, Cold, Acid, Electric, Pierce, Bludgeon, Slash
            var damageRoll = ThreadSafeRandom.Next(0, 7);
            DamageType damageType;
            string damageTypeName;

            switch (damageRoll)
            {
                case 0:
                    damageType = DamageType.Fire;
                    damageTypeName = "Fire";
                    wo.UiEffects = UiEffects.Fire;
                    break;
                case 1:
                    damageType = DamageType.Cold;
                    damageTypeName = "Frost";
                    wo.UiEffects = UiEffects.Frost;
                    break;
                case 2:
                    damageType = DamageType.Acid;
                    damageTypeName = "Acid";
                    wo.UiEffects = UiEffects.Acid;
                    break;
                case 3:
                    damageType = DamageType.Electric;
                    damageTypeName = "Lightning";
                    wo.UiEffects = UiEffects.Lightning;
                    break;
                case 4:
                    damageType = DamageType.Pierce;
                    damageTypeName = "Pierce";
                    wo.UiEffects = UiEffects.Piercing;
                    break;
                case 5:
                    damageType = DamageType.Bludgeon;
                    damageTypeName = "Bludgeon";
                    wo.UiEffects = UiEffects.Bludgeoning;
                    break;
                case 6:
                default:
                    damageType = DamageType.Slash;
                    damageTypeName = "Slash";
                    wo.UiEffects = UiEffects.Slashing;
                    break;
            }

            wo.UnarmedDamageType = (int)damageType;
            // Also store in standard DamageType so WeaponProfile and client panel pick it up
            wo.SetProperty(PropertyInt.DamageType, (int)damageType);

            // WeaponSkill must be set so the appraisal panel shows the correct skill line
            wo.WeaponSkill = Skill.UnarmedCombat;

            // Weapon combat properties so the surrogate reads identically to a melee weapon
            // WeaponOffense / WeaponDefense. Endgame content leans heavily on evades,
            // so high-tier unarmed armor needs attack mods in the same band as real melee weapons.
            float offMin, offMax, defMin, defMax;
            switch (profile.Tier)
            {
                case 5:  offMin = 1.03f; offMax = 1.08f; defMin = 1.01f; defMax = 1.04f; break;
                case 6:  offMin = 1.06f; offMax = 1.12f; defMin = 1.02f; defMax = 1.06f; break;
                case 7:  offMin = 1.09f; offMax = 1.17f; defMin = 1.04f; defMax = 1.09f; break;
                default: offMin = 1.12f; offMax = 1.22f; defMin = 1.06f; defMax = 1.12f; break;
            }
            wo.WeaponOffense = ThreadSafeRandom.Next(offMin, offMax);
            wo.WeaponDefense = ThreadSafeRandom.Next(defMin, defMax);

            // WeaponTime (attack speed): T5=60, T6=50, T7=40, T8=30 (lower = faster, same as a fast unarmed weapon)
            wo.WeaponTime = profile.Tier switch
            {
                5 => ThreadSafeRandom.Next(55, 65),
                6 => ThreadSafeRandom.Next(45, 55),
                7 => ThreadSafeRandom.Next(35, 45),
                _ => ThreadSafeRandom.Next(25, 35),
            };

            // Rare off-axis defenses. The item's main value is unarmed damage,
            // offense, and melee defense; missile/magic defense should be a bonus roll.
            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.10f)
                wo.WeaponMissileDefense = MissileMagicDefense.Roll(profile.Tier);

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.10f)
                wo.WeaponMagicDefense = MissileMagicDefense.Roll(profile.Tier);

            // Update item name and description
            var slotType = isGauntlet ? "Gauntlets" : "Boots";
            var attackName = isGauntlet ? "Punches" : "Kicks";
            var attackStyle = isGauntlet ? "light" : "heavy";
            wo.Name = $"{wo.Name} of {damageTypeName} {attackName}";

            wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThese {slotType.ToLower()} grant {baseDamage} {damageTypeName} damage for {attackStyle} unarmed attacks (no weapon equipped).";

            // Add visual overlay for unarmed-enabled items
            wo.IconOverlayId = MutatorOverlayUnarmed;
        }

        private static bool IsForcedArmorMutator(TreasureRoll roll, string mutatorName)
        {
            return roll != null
                && TryResolveArmorMutator(roll.ForcedWeaponMutator, out var forcedName)
                && string.Equals(forcedName, mutatorName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AssignArmorLevel(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            // retail was only divied up into a few different mutation scripts here
            // anything with ArmorLevel ran these mutation scripts
            // anything that covered extremities (head / hand / foot wear) started with a slightly higher base AL,
            // but otherwise used the same mutation as anything that covered non-extremities
            // shields also had their own mutation script

            // only exceptions found: covenant armor, olthoi armor, metal cap

            if (!roll.HasArmorLevel(wo))
                return false;

            var scriptName = GetMutationScript_ArmorLevel(wo, roll);

            if (scriptName == null)
            {
                log.Error($"AssignArmorLevel({wo.Name}, {profile.TreasureType}, {roll.ItemType}) - unknown item type");
                return false;
            }

            // persist original values for society armor
            var wieldRequirements = wo.WieldRequirements;
            var wieldSkillType = wo.WieldSkillType;
            var wieldDifficulty = wo.WieldDifficulty;

            //Console.WriteLine($"Mutating {wo.Name} with {scriptName}");

            var mutationFilter = MutationCache.GetMutation(scriptName);

            var success = mutationFilter.TryMutate(wo, profile.Tier);

            if (roll.ArmorType.IsSocietyArmor())
            {
                wo.WieldRequirements = wieldRequirements;
                wo.WieldSkillType = wieldSkillType;
                wo.WieldDifficulty = wieldDifficulty;
            }

            return success;
        }

        private static string GetMutationScript_ArmorLevel(WorldObject wo, TreasureRoll roll)
        {
            switch (roll.ArmorType)
            {
                case TreasureArmorType.Covenant:

                    if (wo.IsShield)
                        return "ArmorLevel.covenant_shield.txt";
                    else
                        return "ArmorLevel.covenant_armor.txt";

                case TreasureArmorType.Olthoi:

                    if (wo.IsShield)
                        return "ArmorLevel.olthoi_shield.txt";
                    else
                        return "ArmorLevel.olthoi_armor.txt";
            }

            if (wo.IsShield)
                return "ArmorLevel.shield_level.txt";

            var coverage = wo.ClothingPriority ?? 0;

            if ((coverage & (CoverageMask)CoverageMaskHelper.Extremities) != 0)
                return "ArmorLevel.armor_level_extremity.txt";
            else if ((coverage & (CoverageMask)CoverageMaskHelper.Outerwear) != 0)
                return "ArmorLevel.armor_level_non_extremity.txt";
            else
                return null;
        }

        private static void MutateArmorModVsType(WorldObject wo, TreasureDeath profile)
        {
            // for the PropertyInt.MutateFilters found in py16 data,
            // items either had all of these, or none of these

            // only the elemental types could mutate
            TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsFire);
            TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsCold);
            TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsAcid);
            TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsElectric);
        }

        private static bool TryMutateArmorModVsType(WorldObject wo, TreasureDeath profile, PropertyFloat prop)
        {
            var armorModVsType = wo.GetProperty(prop);

            if (armorModVsType == null)
                return false;

            // perform the initial roll to determine if this ArmorModVsType will mutate
            var mutate = ArmorModVsTypeChance.Roll(profile.Tier);

            if (!mutate)
                return false;

            // get quality level 1-5 for tier
            var qualityLevel = ArmorModVsTypeChance.RollQualityLevel(profile);

            // add in rng
            // for t6+ / max quality level 5, the highest bonus found in eor data was ~0.9
            var rng = ThreadSafeRandom.Next(-0.05f, 0.15f);

            var bonusRL = qualityLevel * 0.15f + rng;

            //Console.WriteLine($"Boosting {wo.Name}.{prop} by {bonusRL}");

            armorModVsType += bonusRL;

            // ensure between -2.0 / 2.0?
            armorModVsType = Math.Clamp(armorModVsType.Value, -2.0f, 2.0f);

            wo.SetProperty(prop, armorModVsType.Value);

            return true;
        }
        private static void MutateValue_Armor(WorldObject wo)
        {
            var bulkMod = wo.BulkMod ?? 1.0f;
            var sizeMod = wo.SizeMod ?? 1.0f;

            var armorLevel = wo.ArmorLevel ?? 0;

            // from the py16 mutation scripts
            //wo.Value += (int)(armorLevel * armorLevel / 10.0f * bulkMod * sizeMod);

            // still probably not how retail did it
            // modified for armor values to match closer to retail pcaps
            var minRng = (float)Math.Min(bulkMod, sizeMod);
            var maxRng = (float)Math.Max(bulkMod, sizeMod);

            var rng = ThreadSafeRandom.Next(minRng, maxRng);

            wo.Value += (int)(armorLevel * armorLevel / 10.0f * rng);
        }

        private static bool TryMutateGearRating(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (profile.Tier != 8)
                return false;

            // shields don't have gear ratings
            if (wo.IsShield) return false;

            var gearRating = GearRatingChance.Roll(wo, profile, roll);

            if (gearRating == 0)
                return false;

            //Console.WriteLine($"TryMutateGearRating({wo.Name}, {profile.TreasureType}, {roll.ItemType}): rolled gear rating {gearRating}");

            var rng = ThreadSafeRandom.Next(0, 1);

            if (roll.HasArmorLevel(wo))
            {
                // clothing w/ al, and crowns would be included in this group
                if (rng == 0)
                    wo.GearCritDamage = gearRating;
                else
                    wo.GearCritDamageResist = gearRating;
            }
            else if (roll.IsClothing || roll.IsCloak)
            {
                if (rng == 0)
                    wo.GearDamage = gearRating;
                else
                    wo.GearDamageResist = gearRating;
            }
            else if (roll.IsJewelry)
            {
                if (rng == 0)
                    wo.GearHealingBoost = gearRating;
                else
                    wo.GearMaxHealth = gearRating;
            }
            else
            {
                log.Error($"TryMutateGearRating({wo.Name}, {profile.TreasureType}, {roll.ItemType}): unknown item type");
                return false;
            }

            // ensure wield requirement is level 180?
            if (roll.ArmorType != TreasureArmorType.Society)
                SetWieldLevelReq(wo, 180);

            return true;
        }

        private static void SetWieldLevelReq(WorldObject wo, int level)
        {
            if (wo.WieldRequirements == WieldRequirement.Invalid)
            {
                wo.WieldRequirements = WieldRequirement.Level;
                wo.WieldSkillType = (int)Skill.Axe;  // set from examples in pcap data
                wo.WieldDifficulty = level;
            }
            else if (wo.WieldRequirements == WieldRequirement.Level)
            {
                if (wo.WieldDifficulty < level)
                    wo.WieldDifficulty = level;
            }
            else
            {
                // this can either be empty, or in the case of covenant / olthoi armor,
                // it could already contain a level requirement of 180, or possibly 150 in tier 8

                // we want to set this level requirement to 180, in all cases

                // magloot logs indicated that even if covenant / olthoi armor was not upgraded to 180 in its mutation script,
                // a gear rating could still drop on it, and would "upgrade" the 150 to a 180

                wo.WieldRequirements2 = WieldRequirement.Level;
                wo.WieldSkillType2 = (int)Skill.Axe;  // set from examples in pcap data
                wo.WieldDifficulty2 = level;
            }
        }

        /// <summary>
        /// This is only called by /testlootgen command
        /// The actual lootgen system doesn't use this.
        /// </summary>
        private static WorldObject CreateCloak(TreasureDeath profile, bool mutate = true)
        {
            var cloakWeenie = CloakWcids.Roll();

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)cloakWeenie);

            if (wo != null && mutate)
                MutateCloak(wo, profile);

            return wo;
        }

        private static void MutateCloak(WorldObject wo, TreasureDeath profile, TreasureRoll roll = null)
        {
            wo.ItemMaxLevel = CloakChance.Roll_ItemMaxLevel(profile);

            // wield difficulty, based on ItemMaxLevel
            switch (wo.ItemMaxLevel)
            {
                case 1:
                    wo.WieldDifficulty = 30;
                    break;
                case 2:
                    wo.WieldDifficulty = 60;
                    break;
                case 3:
                    wo.WieldDifficulty = 90;
                    break;
                case 4:
                    wo.WieldDifficulty = 120;
                    break;
                case 5:
                    wo.WieldDifficulty = 150;
                    break;
            }

            wo.IconOverlayId = IconOverlay_ItemMaxLevel[wo.ItemMaxLevel.Value - 1];

            // equipment set
            wo.EquipmentSetId = CloakChance.RollEquipmentSet();

            // proc spell
            var surgeSpell = CloakChance.RollProcSpell();

            if (surgeSpell != SpellId.Undef)
            {
                wo.ProcSpell = (uint)surgeSpell;

                // Cloaked In Skill is the only self-targeted spell
                if (wo.ProcSpell == (uint)SpellId.CloakAllSkill)
                    wo.ProcSpellSelfTargeted = true;
                else
                    wo.ProcSpellSelfTargeted = false;

                wo.CloakWeaveProc = 1;
            }
            else
            {
                // Damage Reduction proc
                wo.CloakWeaveProc = 2;
            }

            // material type
            wo.MaterialType = GetMaterialType(wo, profile.Tier);

            // workmanship
            wo.Workmanship = WorkmanshipChance.Roll(profile.Tier);

            if (roll != null && profile.Tier == 8)
                TryMutateGearRating(wo, profile, roll);

            // item value
            //if (wo.HasMutateFilter(MutateFilter.Value))
            MutateValue(wo, profile.Tier, roll);
        }
    }
}

