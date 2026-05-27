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
        private static readonly System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName> LifeCasterWcids = new System.Collections.Generic.HashSet<ACE.Server.Factories.Enum.WeenieClassName>
        {
            ACE.Server.Factories.Enum.WeenieClassName.ace420420421_martyrstaff,
        };

        public static WorldObject CreateCaster(TreasureDeath profile, bool isMagical)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Caster);
            treasureRoll.WeaponType = TreasureWeaponType.Caster;
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

            // Archmagi: 5% chance on any T6+ magical caster with a bound spell.
            // ProcSpell is set at loot time (element-matched bolt, or HealSelf for life casters).
            // The actual proc is driven at runtime by TryProcArchmagi in Player_Magic.cs —
            // it only fires when the player casts a spell whose element matches the caster,
            // and never on ring, wall, volley, or blast AoE spells.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.ArchmagiEnabled
                && isMagical && profile.Tier >= ACE.Server.Managers.DerpACEConfig.ArchmagiMinTier && wo.SpellDID.HasValue && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ArchmagiDropChance)
            {
                var isLifeCaster = wo.W_DamageType == DamageType.Health;

                var archagiSpellLevels = isLifeCaster
                    ? SpellLevelProgression.GetSpellLevels(SpellId.HealSelf1)
                    : SpellLevelProgression.GetSpellLevels((SpellId)wo.SpellDID.Value);

                if (archagiSpellLevels != null && archagiSpellLevels.Count >= 5)
                {
                    // maxIdx is the highest level index (0-based) available for this tier
                    var maxIdx = profile.Tier >= 8 ? 4 : profile.Tier >= 7 ? 2 : 1;

                    // squaring the roll biases heavily toward level 1; higher levels are progressively rarer
                    var rawRoll = ThreadSafeRandom.Next(0.0f, 1.0f);
                    var procIdx = (int)(rawRoll * rawRoll * (maxIdx + 1));
                    if (procIdx > maxIdx) procIdx = maxIdx;

                    var procSpellLevel = procIdx + 1;
                    var procDesc = isLifeCaster
                        ? $"a level {procSpellLevel} heal upon yourself"
                        : $"a level {procSpellLevel} duplicate of its bound spell against your target";

                    wo.Name = wo.Name + " of the Archmagi";
                    wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsArchmagiCaster, true);
                    wo.IconOverlayId = 0x06002860;
                    wo.UiEffects = ACE.Entity.Enum.UiEffects.Frost;
                    wo.ProcSpell = (uint)archagiSpellLevels[procIdx]; // stored for appraisal display; fired by TryProcArchmagi
                    wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis caster pulses with ancient arcane memory — when you cast a matching spell, it has a {ACE.Server.Managers.DerpACEConfig.ArchmagiProcChance:P0} chance to echo {procDesc}.";
                }
            }

            // Hierophant: support-healer variant for life casters (Martyr Staff family).
            // Only rolls if the caster is a life caster, didn't already become Archmagi, and meets tier.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.HierophantEnabled
                && isMagical
                && wo.W_DamageType == DamageType.Health
                && profile.Tier >= ACE.Server.Managers.DerpACEConfig.HierophantMinTier
                && wo.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsArchmagiCaster) != true
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.HierophantDropChance)
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
                wo.UiEffects = ACE.Entity.Enum.UiEffects.Magical;

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
    }
}
