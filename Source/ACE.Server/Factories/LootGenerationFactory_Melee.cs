using System;

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

            // Fencer's Blade: configurable chance on T6+ épée / rapier / schlager (see @lootconfig)
            // SwordMS is exclusively these three weapon types — no additional WCID check required.
            // Pierce proc: per-weapon chance to bypass armor (deals mitigated damage × piercePct as bonus).
            // Deflect proc: per-incoming-hit chance to reflect 10% of damage back at the attacker.
            if (roll.WeaponType == TreasureWeaponType.SwordMS
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.FencerBladeMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.FencerBladeDropChance)
            {
                var piercePct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.FencerPierceMin,
                    (float)ACE.Server.Managers.DerpACEConfig.FencerPierceMax));
                var pierceProc = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.FencerPierceProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.FencerPierceProcMax));
                var deflectChance = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.FencerDeflectMin,
                    (float)ACE.Server.Managers.DerpACEConfig.FencerDeflectMax));

                wo.Name = wo.Name + " of the Fencer";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsFencerBlade, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPiercePct,  piercePct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPierceProc, pierceProc / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerDeflectChance,   deflectChance / 100.0);
                wo.IconOverlayId = 0x06002699u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis blade is perfectly balanced for dueling — each strike has a {pierceProc}% chance to find a gap in the target's defenses, bypassing {piercePct}% of their armor. There is also a {deflectChance}% chance per incoming hit to turn an attack aside and redirect 10% of its damage back at the assailant.";
            }

            // Ravager's Axe: configurable chance on T6+ axes (1H or 2H) to apply a bleed DoT (see @lootconfig)
            // Bleed total damage = bleedPct% of the triggering hit, spread evenly across RavagerBleedTicks at RavagerBleedInterval seconds.
            // Two-handed axes get the bleed total scaled by RavagerTwoHandMult.
            if ((roll.WeaponType == TreasureWeaponType.Axe || roll.WeaponType == TreasureWeaponType.TwoHandedAxe)
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.RavagerAxeMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.RavagerAxeDropChance)
            {
                var procPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.RavagerProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.RavagerProcMax));
                var bleedPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.RavagerBleedMin,
                    (float)ACE.Server.Managers.DerpACEConfig.RavagerBleedMax));

                var isTwoHanded = roll.WeaponType == TreasureWeaponType.TwoHandedAxe;
                var bleedFraction = bleedPct / 100.0;
                if (isTwoHanded)
                    bleedFraction *= ACE.Server.Managers.DerpACEConfig.RavagerTwoHandMult;

                wo.Name = wo.Name + " of the Ravager";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsRavagersAxe, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedProc, procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedPct,  bleedFraction);
                wo.IconOverlayId = 0x06002878u;

                var displayBleed = (int)Math.Round(bleedFraction * 100.0);
                var ticks = ACE.Server.Managers.DerpACEConfig.RavagerBleedTicks;
                var interval = ACE.Server.Managers.DerpACEConfig.RavagerBleedInterval;
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis axe is wickedly serrated — each strike has a {procPct}% chance to inflict a vicious bleed dealing {displayBleed}% of the hit's damage over {ticks} ticks ({interval:0.#}s apart).{(isTwoHanded ? " The two-handed grip drives the wound deeper." : "")}";
            }

            // Warden's Maul: configurable chance on T6+ maces (1H, MS, or 2H) to apply a flat defense-skill debuff (see @lootconfig)
            // Two-handed maces get the penalty scaled by WardenTwoHandMult.
            if ((roll.WeaponType == TreasureWeaponType.Mace
                    || roll.WeaponType == TreasureWeaponType.MaceJitte
                    || roll.WeaponType == TreasureWeaponType.TwoHandedMace)
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.WardenMaulMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.WardenMaulDropChance)
            {
                var procPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.WardenProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.WardenProcMax));
                var penalty = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.WardenPenaltyMin,
                    (float)ACE.Server.Managers.DerpACEConfig.WardenPenaltyMax));
                var duration = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.WardenDurationMin,
                    (float)ACE.Server.Managers.DerpACEConfig.WardenDurationMax));

                var isTwoHandedMace = roll.WeaponType == TreasureWeaponType.TwoHandedMace;
                if (isTwoHandedMace)
                    penalty = (int)Math.Round(penalty * ACE.Server.Managers.DerpACEConfig.WardenTwoHandMult);

                wo.Name = wo.Name + " of the Warden";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsWardensMaul, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussProc,     procPct  / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussPenalty,  penalty);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussDuration, duration);
                wo.IconOverlayId = 0x06002878u;

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis maul is forged for crushing guards — each strike has a {procPct}% chance to concuss the target, reducing their effective defense skill by {penalty} for {duration} seconds.{(isTwoHandedMace ? " The two-handed swing rattles bone." : "")}";
            }

            // Resolute Blade: configurable chance on T6+ swords (1H or 2H, excluding fencer SwordMS) (see @lootconfig)
            // On crit hits, restores % of damage as health. On killing blows, restores % of MaxHealth + MaxStamina.
            if ((roll.WeaponType == TreasureWeaponType.Sword || roll.WeaponType == TreasureWeaponType.TwoHandedSword)
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.ResoluteBladeMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ResoluteBladeDropChance)
            {
                var procPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.ResoluteProcMin,
                    (float)ACE.Server.Managers.DerpACEConfig.ResoluteProcMax));
                var healPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.ResoluteHealMin,
                    (float)ACE.Server.Managers.DerpACEConfig.ResoluteHealMax));

                var isTwoHandedSword = roll.WeaponType == TreasureWeaponType.TwoHandedSword;
                var killBurst = ACE.Server.Managers.DerpACEConfig.ResoluteKillBurstPct;
                if (isTwoHandedSword)
                    killBurst *= ACE.Server.Managers.DerpACEConfig.ResoluteTwoHandMult;

                wo.Name = wo.Name + " of Resolve";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsResoluteBlade, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealProc,     procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealPct,      healPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteKillBurstPct, killBurst);
                wo.IconOverlayId = 0x06002860u;

                var killBurstPct = (int)Math.Round(killBurst * 100.0);
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis blade is honed for the long fight — critical hits have a {procPct}% chance to restore {healPct}% of the damage dealt as health to the wielder. Killing blows surge with {killBurstPct}% of your maximum health and stamina.{(isTwoHandedSword ? " The two-handed grip drinks deeper from the slain." : "")}";
            }

            // Polebreaker Staff: configurable chance on T6+ staves to escalate damage on consecutive hits against the same target (see @lootconfig)
            if (roll.WeaponType == TreasureWeaponType.Staff
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.PolebreakerMinTier
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.PolebreakerDropChance)
            {
                var stackPct = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.PolebreakerStackMin,
                    (float)ACE.Server.Managers.DerpACEConfig.PolebreakerStackMax));
                var maxStacks = (int)Math.Round(ThreadSafeRandom.Next(
                    (float)ACE.Server.Managers.DerpACEConfig.PolebreakerMaxStackMin,
                    (float)ACE.Server.Managers.DerpACEConfig.PolebreakerMaxStackMax));
                if (stackPct < 1) stackPct = 1;
                if (maxStacks < 1) maxStacks = 1;

                wo.Name = wo.Name + " of the Polebreaker";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsPolebreakerStaff, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerStackBonus, stackPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerMaxStacks,  maxStacks);
                wo.IconOverlayId = 0x06002699u;

                var totalPct = stackPct * maxStacks;
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis staff finds a deadly rhythm — each consecutive hit on the same target adds +{stackPct}% bonus damage, stacking up to {maxStacks} times (+{totalPct}% at full stack). Switching targets or letting the target die resets the chain.";
            }

            // Unarmed elemental cast-on-strike: configurable % of magical elemental fist weapons roll a proc (see @lootconfig).
            // Proc rate is randomized between unarmed.procmin and unarmed.procmax to reflect weapon-to-weapon variation.
            if (roll.WeaponType == TreasureWeaponType.Unarmed && isMagical
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

                    wo.UiEffects = wo.W_DamageType switch
                    {
                        DamageType.Acid     => ACE.Entity.Enum.UiEffects.Acid,
                        DamageType.Electric => ACE.Entity.Enum.UiEffects.Lightning,
                        DamageType.Fire     => ACE.Entity.Enum.UiEffects.Fire,
                        DamageType.Cold     => ACE.Entity.Enum.UiEffects.Frost,
                        _                   => ACE.Entity.Enum.UiEffects.Undef
                    };

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
