using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
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
        private static readonly System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName> LifeCasterWcids = new System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName>
        {
            ACE.Server.Factories.Enum.WeenieClassName.ace420420421_martyrstaff,
        };

        private static readonly System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName> LifeCasterMutationWcids = new System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName>
        {
            ACE.Server.Factories.Enum.WeenieClassName.sceptre,
            ACE.Server.Factories.Enum.WeenieClassName.wand,
            ACE.Server.Factories.Enum.WeenieClassName.staff,
            ACE.Server.Factories.Enum.WeenieClassName.wandacid,
            ACE.Server.Factories.Enum.WeenieClassName.wandblunt,
            ACE.Server.Factories.Enum.WeenieClassName.wandelectric,
            ACE.Server.Factories.Enum.WeenieClassName.wandfire,
            ACE.Server.Factories.Enum.WeenieClassName.wandfrost,
            ACE.Server.Factories.Enum.WeenieClassName.wandpiercing,
            ACE.Server.Factories.Enum.WeenieClassName.wandslashing,
            ACE.Server.Factories.Enum.WeenieClassName.ace43381_nethersceptre,
            ACE.Server.Factories.Enum.WeenieClassName.ace31819_slashingbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31825_piercingbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31821_bluntbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31820_acidbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31823_firebaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31824_frostbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace31822_electricbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace43382_netherbaton,
            ACE.Server.Factories.Enum.WeenieClassName.ace37223_slashingstaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37222_piercingstaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37225_bluntstaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37224_acidstaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37220_firestaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37221_froststaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace37219_electricstaff,
            ACE.Server.Factories.Enum.WeenieClassName.ace43383_netherstaff,
        };

        public static WorldObject CreateCaster(TreasureDeath profile, bool isMagical, string forcedWeaponMutator = null)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Caster);
            treasureRoll.WeaponType = TreasureWeaponType.Caster;
            treasureRoll.ForcedWeaponMutator = forcedWeaponMutator;
            treasureRoll.Wcid = CasterWcids.Roll(profile.Tier);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);
            MutateCaster(wo, profile, isMagical, treasureRoll);

            return wo;
        }

        private static void MutateCaster(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll)
        {
            // Ensure custom life caster templates always use the life-damage mutation path.
            if (LifeCasterWcids.Contains((ACE.Server.Factories.Enum.WeenieClassName)wo.WeenieClassId) && wo.W_DamageType == DamageType.Undef)
                wo.W_DamageType = DamageType.Health;
            else
                TryMutateLifeCaster(wo, profile, roll);

            // mutate ManaConversionMod
            var mutationFilter = MutationCache.GetMutation("Casters.caster.txt");
            mutationFilter.TryMutate(wo, profile.Tier);

            // mutate ElementalDamageMod / WieldRequirements
            var isElemental = wo.W_DamageType != DamageType.Undef;
            var scriptName = GetCasterScript(isElemental);

            mutationFilter = MutationCache.GetMutation(scriptName);
            mutationFilter.TryMutate(wo, profile.Tier);

            // this part was not handled by mutation filter
            if (wo.WieldRequirements == WieldRequirement.RawSkill)
            {
                if (wo.W_DamageType == DamageType.Nether)
                    wo.WieldSkillType = (int)Skill.VoidMagic;
                else if (wo.W_DamageType == DamageType.Health)
                    wo.WieldSkillType = (int)Skill.LifeMagic;
                else
                    wo.WieldSkillType = (int)Skill.WarMagic;
            }

            ApplyLootUiEffects(wo, wo.W_DamageType, isMagical);

            // mutate WeaponDefense
            mutationFilter = MutationCache.GetMutation("Casters.weapon_defense.txt");
            mutationFilter.TryMutate(wo, profile.Tier);

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

            // burden?

            // missile defense / magic defense
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
            }
            else
            {
                // if a caster was from a MagicItem profile, it always had a SpellDID
                MutateCaster_SpellDID(wo, profile);

                AssignMagic(wo, profile, roll);
            }

            // item value
            //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
                MutateValue(wo, profile.Tier, roll);

            // long description
            wo.LongDesc = GetLongDesc(wo);
            if (wo.W_DamageType == DamageType.Health)
                wo.LongDesc = $"Life Spells\r\n\r\n{wo.LongDesc}";

            // Archmagi: 5% chance on any T6+ magical caster with a bound spell.
            // Runtime rolls this item's 1-5% ProcSpellRate to chain the same valid
            // harmful single-target spell from the player to a different nearby target.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.ArchmagiEnabled
                && isMagical && wo.SpellDID.HasValue
                && (IsForcedWeaponModifier(roll, "archmagi")
                    || (!HasForcedWeaponModifier(roll) && profile.Tier >= ACE.Server.Managers.DerpACEConfig.ArchmagiMinTier && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ArchmagiDropChance)))
            {
                var procChance = ThreadSafeRandom.Next(0.01f, 0.05f);

                wo.Name = wo.Name + " of the Archmagi";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsArchmagiCaster, true);
                wo.ProcSpell = null;
                wo.ProcSpellRate = procChance;
                wo.IconOverlayId = 0x06002860;
                ApplyLootUiEffects(wo, wo.W_DamageType, true);
                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis caster pulses with ancient arcane memory - when you cast a harmful single-target spell, it has a {procChance:P0} chance to chain the same spell from you to a different nearby target at reduced damage.";

            }

            // Hierophant: support-healer variant for life casters (Martyr Staff family).
            // Only rolls if the caster is a life caster, didn't already become Archmagi, and meets tier.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.HierophantEnabled
                && isMagical
                && wo.W_DamageType == DamageType.Health
                && wo.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsArchmagiCaster) != true
                && (IsForcedWeaponModifier(roll, "hierophant")
                    || (!HasForcedWeaponModifier(roll) && profile.Tier >= ACE.Server.Managers.DerpACEConfig.HierophantMinTier && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.HierophantDropChance)))
            {
                var healBoost = ThreadSafeRandom.Next(
                    ACE.Server.Managers.DerpACEConfig.HierophantHealBoostMin,
                    ACE.Server.Managers.DerpACEConfig.HierophantHealBoostMax);

                var hotPct = ThreadSafeRandom.Next(
                    ACE.Server.Managers.DerpACEConfig.HierophantHotPctMin,
                    ACE.Server.Managers.DerpACEConfig.HierophantHotPctMax);

                var fellowEcho = ACE.Server.Managers.DerpACEConfig.HierophantFellowEchoPct;

                wo.Name = wo.Name + " of the Hierophant";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsHierophantCaster, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.HierophantHealBoostPct, healBoost);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.HierophantHotPct, hotPct);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.HierophantFellowEchoPct, fellowEcho);
                wo.IconOverlayId = 0x06002CB7;
                ApplyLootUiEffects(wo, wo.W_DamageType, true);

                wo.LongDesc = (wo.LongDesc ?? "")
                    + $"\n\nBlessed by the Hierophants — beneficial healing cast through this staff is amplified by {healBoost:P0}."
                    + $"\nWhen you heal yourself or an ally, there is a {ACE.Server.Managers.DerpACEConfig.HierophantHotProcChance:P0} chance to bless the target with a regenerating ward restoring up to {hotPct:P0} of their health over {ACE.Server.Managers.DerpACEConfig.HierophantHotDurationSeconds:0}s."
                    + $"\nEach heal also echoes a {fellowEcho:P0} bonus heal to nearby fellowship members within {ACE.Server.Managers.DerpACEConfig.HierophantFellowEchoRange:0}m.";
            }

            // Universal blast-on-strike: rare chance for any elemental caster T5+ to proc a level-3 blast.
            TryRollWeaponBlastProc(wo, profile);
        }

        private static void MutateCaster_SpellDID(WorldObject wo, TreasureDeath profile)
        {
            var firstSpell = CasterSlotSpells.Roll(wo);

            var spellLevels = SpellLevelProgression.GetSpellLevels(firstSpell);

            if (spellLevels == null)
            {
                log.Error($"MutateCaster_SpellDID: couldn't find {firstSpell}");
                return;
            }

            if (spellLevels.Count != 8)
            {
                log.Error($"MutateCaster_SpellDID: found {spellLevels.Count} spell levels for {firstSpell}, expected 8");
                return;
            }

            var spellLevel = SpellLevelChance.Roll(profile.Tier);

            wo.SpellDID = (uint)spellLevels[spellLevel - 1];

            var spell = new Server.Entity.Spell(wo.SpellDID.Value);

            var castableMod = CasterSlotSpells.IsOrb(wo) ? 5.0f : 2.5f;

            wo.ItemManaCost = (int)(spell.BaseMana * castableMod);

            wo.ItemUseable = Usable.SourceWieldedTargetRemoteNeverWalk;
        }

        private static string GetCasterScript(bool isElemental = false)
        {
            var elementalStr = isElemental ? "elemental" : "non_elemental";

            return $"Casters.caster_{elementalStr}.txt";
        }

        private static void TryMutateLifeCaster(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
        {
            if (!DerpACEConfig.EnableCustomWeapons || !DerpACEConfig.LifeCasterEnabled)
                return;

            var forcedLifeCaster = IsForcedWeaponModifier(roll, "hierophant");

            if (!forcedLifeCaster && profile.Tier < DerpACEConfig.LifeCasterMinTier)
                return;

            var wcid = (ACE.Server.Factories.Enum.WeenieClassName)wo.WeenieClassId;
            if (!LifeCasterMutationWcids.Contains(wcid))
                return;

            if (!forcedLifeCaster && ThreadSafeRandom.Next(0.0f, 1.0f) >= DerpACEConfig.LifeCasterDropChance)
                return;

            var family = GetLifeCasterFamily(wcid);

            wo.W_DamageType = DamageType.Health;
            wo.WieldSkillType = (int)Skill.LifeMagic;
            wo.Name = $"Martyr {family.Name}";
            wo.Use = $"Life Spells: This {family.Name} has been sanctified to aid Life Magic.";

            if (family.Setup != 0)
                wo.SetupTableId = family.Setup;
        }

        private static (string Name, uint Setup) GetLifeCasterFamily(ACE.Server.Factories.Enum.WeenieClassName wcid)
        {
            switch (wcid)
            {
                case ACE.Server.Factories.Enum.WeenieClassName.ace31819_slashingbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31825_piercingbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31821_bluntbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31820_acidbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31823_firebaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31824_frostbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace31822_electricbaton:
                case ACE.Server.Factories.Enum.WeenieClassName.ace43382_netherbaton:
                    return ("Baton", 0x02001637);

                case ACE.Server.Factories.Enum.WeenieClassName.staff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37223_slashingstaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37222_piercingstaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37225_bluntstaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37224_acidstaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37220_firestaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37221_froststaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace37219_electricstaff:
                case ACE.Server.Factories.Enum.WeenieClassName.ace43383_netherstaff:
                    return ("Staff", 0x0200184B);

                case ACE.Server.Factories.Enum.WeenieClassName.wand:
                    return ("Wand", 0);

                default:
                    return ("Sceptre", 0x020012BF);
            }
        }
    }
}
