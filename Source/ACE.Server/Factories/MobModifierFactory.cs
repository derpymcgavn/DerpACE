using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE: rolls rare "modifiers" on freshly-spawned mobs (Vampiric, Thief, ...).
    /// Hooked from <see cref="ACE.Server.Entity.GeneratorProfile.Spawn"/>.
    /// All flags + stats are applied directly to the live WorldObject (in-memory only).
    /// </summary>
    public static class MobModifierFactory
    {
        /// <summary>
        /// Force-applies a specific modifier to a creature for admin/debug spawning.
        /// Returns false when the key is unknown or the modifier cannot be applied.
        /// </summary>
        public static bool TryApplyModifier(WorldObject wo, string modifierKey)
        {
            if (wo is not Creature creature) return false;
            if (creature is Player) return false;
            if (creature is Pet) return false;

            var key = (modifierKey ?? string.Empty).Trim().ToLowerInvariant();
            switch (key)
            {
                case "vamp":
                case "vampiric":
                    ApplyVampiric(creature);
                    return true;

                case "thief":
                case "thieving":
                    ApplyThief(creature);
                    return true;

                case "scout":
                case "scouting":
                    ApplyScout(creature);
                    return true;

                case "sim":
                case "simulacrum":
                    // ApplySimulacrum has internal eligibility checks; detect success via property.
                    ApplySimulacrum(creature);
                    return creature.GetProperty(PropertyBool.IsSimulacrumMob) == true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Entry point — call once per spawned WorldObject. Filters non-eligible
        /// objects and rolls each enabled modifier independently.
        /// </summary>
        public static void TryApplyModifiers(WorldObject wo)
        {
            if (!DerpACEConfig.MobModifierEnabled) return;
            if (wo is not Creature creature) return;
            if (creature is Player) return;
            if (creature is Pet) return;
            if (creature.IsNPC) return;

            // Real hostile mob check (mirrors Monster.IsMonster gate)
            var isMonster = creature.Attackable || creature.TargetingTactic != TargetingTactic.None;
            if (!isMonster) return;

            // Simulacrum runs BEFORE the tier gate so even low-tier CreatureType.Simulacrum (59)
            // mobs always clone a nearby player. Internal CreatureType check filters everything else.
            if (DerpACEConfig.SimulacrumMobChance > 0.0f)
                ApplySimulacrum(creature);

            // Tier gate: prefer DeathTreasure tier, fall back to Level/10
            var tier = creature.DeathTreasure?.Tier ?? ((creature.Level ?? 0) / 10);
            if (tier < DerpACEConfig.MobModifierMinTier) return;

            // Independent rolls — multiple modifiers can stack on one mob
            if (ThreadSafeRandom.Next(0.0f, 1.0f) < DerpACEConfig.VampiricMobChance)
                ApplyVampiric(creature);

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < DerpACEConfig.ThiefMobChance)
                ApplyThief(creature);

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < DerpACEConfig.ScoutMobChance)
                ApplyScout(creature);
        }

        private static void ApplyVampiric(Creature creature)
        {
            var minPct = System.Math.Max(0, DerpACEConfig.VampiricLifestealMin);
            var maxPct = System.Math.Max(minPct, DerpACEConfig.VampiricLifestealMax);
            var pct = ThreadSafeRandom.Next(minPct, maxPct) / 100.0;

            creature.SetProperty(PropertyBool.IsVampiricMob, true);
            creature.SetProperty(PropertyFloat.VampiricLifestealPct, pct);

            // Visual tells: +0.5x scale and best-effort red tint.
            // PaletteTemplate=Red + Shade=1.0 drives any palette-set-driven creatures
            // (most NPCs/mobs) toward their reddest variant; on creatures whose appearance
            // is pure CSetup/AnimPart it's a no-op, but the +0.5 ObjScale always reads.
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.5f;
            creature.PaletteTemplate = (int)PaletteTemplate.Red;
            creature.Shade = 1.0;

            PrependPrefix(creature, "Vampiric");
        }

        private static void ApplyThief(Creature creature)
        {
            creature.SetProperty(PropertyBool.IsThiefMob, true);
            PrependPrefix(creature, "Thieving");
        }

        private static void ApplyScout(Creature creature)
        {
            creature.SetProperty(PropertyBool.IsScoutMob, true);
            PrependPrefix(creature, "Scout");
        }

        /// <summary>
        /// Simulacrum: clones the appearance + name of a randomly-chosen player in the same landblock.
        /// Mirrors the corpse-cloning pattern in <see cref="Creature.CreateCorpse"/> — copies
        /// the player's setup/motion/physics/clothing IDs and snapshots their full ObjDesc
        /// (AnimPartChanges + SubPalettes + TextureChanges) into the creature's biota so the
        /// "no equipped items" branch in <see cref="Creature.CalculateObjDesc"/> renders it
        /// using the saved ObjDesc.
        /// If no player is in the spawn landblock at the moment of spawn, the modifier is skipped.
        /// </summary>
        private static void ApplySimulacrum(Creature creature)
        {
            // Only mobs whose CreatureType is Simulacrum (59) are eligible — these are
            // the in-world "doppelgänger" creatures designed to mimic adventurers.
            if (creature.CreatureType != ACE.Entity.Enum.CreatureType.Simulacrum)
                return;

            if (creature.Location == null)
                return;

            var landblockRaw = creature.Location.LandblockId.Raw;
            var candidates = PlayerManager.GetAllOnline()
                .Where(p => p != null && p.Location != null && p.Location.LandblockId.Raw == landblockRaw)
                .ToList();

            if (candidates.Count == 0)
                return;

            var target = candidates[ThreadSafeRandom.Next(0, candidates.Count - 1)];

            // Identity copies (DIDs)
            creature.SetupTableId   = target.SetupTableId;
            creature.MotionTableId  = target.MotionTableId;
            creature.PhysicsTableId = target.PhysicsTableId;
            creature.PaletteBaseDID = target.PaletteBaseDID;
            creature.ClothingBase   = target.ClothingBase;

            if (target.PaletteTemplate.HasValue)
                creature.PaletteTemplate = target.PaletteTemplate;
            if (target.Shade.HasValue)
                creature.Shade = target.Shade;
            if (target.ObjScale.HasValue)
                creature.ObjScale = target.ObjScale;

            // Snapshot the player's full ObjDesc (clothing, palettes, textures) into the creature's
            // biota collections; Creature.CalculateObjDesc will fall through to these because the
            // mob has no equipped items of its own.
            try
            {
                var objDesc = target.CalculateObjDesc();
                creature.Biota.PropertiesAnimPart   = objDesc.AnimPartChanges.Clone(creature.BiotaDatabaseLock);
                creature.Biota.PropertiesPalette    = objDesc.SubPalettes.Clone(creature.BiotaDatabaseLock);
                creature.Biota.PropertiesTextureMap = objDesc.TextureChanges.Clone(creature.BiotaDatabaseLock);
            }
            catch
            {
                // If the player ObjDesc cannot be computed for any reason, keep base appearance.
            }

            creature.SetProperty(PropertyBool.IsSimulacrumMob, true);

            // Steal the name verbatim — no prefix.
            if (!string.IsNullOrEmpty(target.Name))
                creature.Name = target.Name;

            // Broadcast the new appearance + name so any nearby client that already has the
            // mob in view re-pulls the cloned ObjDesc and updated identity. Runs slightly
            // delayed because TryApplyModifiers fires *before* EnterWorld — by the time the
            // chain executes, the mob is in the landblock and EnqueueBroadcast will reach
            // any client that visualized the spawn.
            var refreshChain = new ACE.Server.Entity.Actions.ActionChain();
            refreshChain.AddDelaySeconds(0.5);
            refreshChain.AddAction(creature, () =>
            {
                if (creature.PhysicsObj == null)
                    return;
                creature.EnqueueBroadcast(
                    new ACE.Server.Network.GameMessages.Messages.GameMessageObjDescEvent(creature),
                    new ACE.Server.Network.GameMessages.Messages.GameMessageUpdateObject(creature));
            });
            refreshChain.EnqueueChain();
        }

        /// <summary>
        /// Prepends a single-word affix to the creature's name, idempotently.
        /// Multiple modifiers chain space-separated: e.g. "Vampiric Thieving Drudge".
        /// </summary>
        private static void PrependPrefix(Creature creature, string prefix)
        {
            var name = creature.Name ?? string.Empty;
            // Idempotency — don't double-prefix if the same word already leads
            var tokens = name.Split(' ');
            if (tokens.Length > 0 && tokens.Take(2).Any(t => t.Equals(prefix, System.StringComparison.OrdinalIgnoreCase)))
                return;

            creature.Name = $"{prefix} {name}".Trim();
        }
    }
}
