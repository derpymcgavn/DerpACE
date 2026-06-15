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
        public static WorldObject CreateMissileWeapon(TreasureDeath profile, bool isMagical, bool mutate = true, TreasureWeaponType? forcedWeaponType = null, string forcedWeaponMutator = null)
        {
            // this function is only used by test methods, and is not part of regular lootgen
            var treasureRoll = new TreasureRoll(TreasureItemType.Weapon);
            treasureRoll.WeaponType = forcedWeaponType ?? WeaponTypeChance.MissileChances.Roll();
            treasureRoll.ForcedWeaponMutator = forcedWeaponMutator;
            treasureRoll.Wcid = WeaponWcids.Roll(profile, ref treasureRoll.WeaponType);

            var wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);

            if (treasureRoll.WeaponType == TreasureWeaponType.ThrownDinnerware
                || treasureRoll.WeaponType == TreasureWeaponType.Discus)
                MutateDinnerware(wo, profile, isMagical, treasureRoll);
            else
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
                ApplyLootUiEffects(wo, wo.W_DamageType, false);
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
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.StalkerBowEnabled
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.StalkerBowDropChance,
                ACE.Server.Managers.DerpACEConfig.StalkerBowMinTier,
                roll.WeaponType == TreasureWeaponType.Bow,
                "stalker"))
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
                wo.IconOverlayId = MutatorOverlayStalker;
                ApplyLootUiEffect(wo, UiEffects.Piercing);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} rewards the patient hunter -- the *first* shot loosed at a target has a {procPct}% chance to strike with +{bonusPct}% bonus damage. Switching targets resets the opportunity.";
            }

            // Breacher's Crossbow: configurable chance on T6+ crossbows for an always-on armor pierce % (see @lootconfig)
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.BreacherCrossbowEnabled
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.BreacherCrossbowDropChance,
                ACE.Server.Managers.DerpACEConfig.BreacherCrossbowMinTier,
                roll.WeaponType == TreasureWeaponType.Crossbow,
                "breacher"))
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
                wo.IconOverlayId = MutatorOverlayBreacher;
                ApplyLootUiEffect(wo, UiEffects.Piercing);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} pierces through armor — {armorIgnoreChance}% chance on each shot to completely ignore the target's armor for that hit.";
            }

            // Reaper's Atlatl: atlatl-only kill-fed sustain. Separate from Dartflinger.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.ReaperAtlatlEnabled
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.ReaperAtlatlDropChance,
                ACE.Server.Managers.DerpACEConfig.ReaperAtlatlMinTier,
                roll.WeaponType == TreasureWeaponType.Atlatl,
                "reaper"))
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
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillProc, procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillHealPct, healPct / 100.0);
                wo.IconOverlayId = MutatorOverlayReaper;
                ApplyLootUiEffect(wo, UiEffects.Nether);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} feeds on endings -- killing blows have a {procPct}% chance to restore {healPct}% of your maximum health.";
            }

            // Dartflinger: configurable chance on T6+ atlatls to bounce a visible dart
            // into another nearby target after a successful hit (see @lootconfig).
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons && ACE.Server.Managers.DerpACEConfig.RicochetAtlatlEnabled
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                ACE.Server.Managers.DerpACEConfig.RicochetAtlatlDropChance,
                ACE.Server.Managers.DerpACEConfig.RicochetAtlatlMinTier,
                roll.WeaponType == TreasureWeaponType.Atlatl
                    && wo.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsReapersAtlatl) != true,
                "dartflinger", "ricochet"))
            {
                var procPct = RollTierScaledInt(
                    ACE.Server.Managers.DerpACEConfig.RicochetProcMin,
                    ACE.Server.Managers.DerpACEConfig.RicochetProcMax,
                    profile.Tier,
                    ACE.Server.Managers.DerpACEConfig.RicochetAtlatlMinTier);
                if (procPct < 1) procPct = 1;

                wo.Name = wo.Name + " of the Dartflinger";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsRicochetAtlatl, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsDartflingerAtlatl, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetProcChance,  procPct / 100.0);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetDamageScale, ACE.Server.Managers.DerpACEConfig.RicochetDamageScale);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RicochetRadius,      ACE.Server.Managers.DerpACEConfig.RicochetRadius);
                wo.IconOverlayId = MutatorOverlayDartflinger;
                ApplyLootUiEffect(wo, UiEffects.Piercing);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nThis {GetWeaponNoun(roll.WeaponType)} skips death through the air -- each hit has a {procPct}% chance to send a visible second dart into another nearby foe within {ACE.Server.Managers.DerpACEConfig.RicochetRadius:0.#} yards for {ACE.Server.Managers.DerpACEConfig.RicochetDamageScale:P0} damage. Cooldown: {Player.RicochetCooldownSeconds:0.#} seconds.";
            }

            // Shadow Volley: rare missile-weapon shadow clone affix.
            if (ACE.Server.Managers.DerpACEConfig.EnableCustomWeapons
                && TryRollWeaponModifier(
                profile,
                roll,
                ref specialModifierApplied,
                0.015f,
                7,
                roll.WeaponType == TreasureWeaponType.Bow
                    || roll.WeaponType == TreasureWeaponType.Crossbow
                    || roll.WeaponType == TreasureWeaponType.Atlatl,
                "shadowclone", "shadowshot", "shadowvolley"))
            {
                const float procChance = 0.03f;
                const float cooldownSeconds = 150.0f;
                const float durationSeconds = 18.0f;
                const float damageScale = 0.25f;

                wo.Name = wo.Name + " of the Shadow Volley";
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsShadowCloneWeapon, true);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneProcChance, procChance);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneCooldownSeconds, cooldownSeconds);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneDurationSeconds, durationSeconds);
                wo.SetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ShadowCloneDamageScale, damageScale);
                wo.CooldownId = Player.ShadowCloneCasterCooldownId;
                wo.CooldownDuration = cooldownSeconds;
                wo.IconOverlayId = MutatorOverlayShadow;
                ApplyLootUiEffect(wo, UiEffects.Nether);

                wo.LongDesc = (wo.LongDesc ?? "") + $"\n\nShadow Volley: successful shots have a {procChance:P0} chance to summon a shadow archer for {durationSeconds:0}s. The shadow locks to missile combat, copies your equipped missile weapon, fights alongside your normal pet, and deals {damageScale:P0} damage. Cooldown: {cooldownSeconds:0}s.";
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
