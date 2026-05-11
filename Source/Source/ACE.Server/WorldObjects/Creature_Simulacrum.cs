using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Simulacrum mob support.
    ///
    /// A "simulacrum" creature copies the first player it aggros - visuals, attributes, vitals,
    /// skills, equipment, and (combat-relevant) spells - and then fights as an evil clone of
    /// that player. On death it does NOT drop the cloned player gear; the corpse uses the
    /// underlying weenie's normal DeathTreasure profile, and XP is scaled by that loot tier.
    ///
    /// Wiring required (3 one-liners elsewhere in the codebase - see WIREUP comments below):
    ///
    ///   1) ACE.Entity/Enum/CreatureType.cs
    ///        Add: Simulacrum = &lt;next free value&gt;,
    ///
    ///   2) ACE.Server/WorldObjects/Monster_Awareness.cs
    ///        Inside SetCombatTarget(...) (or wherever AttackTarget is first set to a Player):
    ///          if (target is Player p) TryCopyFromPlayer(p);
    ///
    ///   3) ACE.Server/WorldObjects/Creature_Death.cs (corpse / treasure generation)
    ///        Right before iterating EquippedObjects to drop them on the corpse:
    ///          if (IsSimulacrum) return;   // skip dropping the cloned gear
    ///        And in the XP-grant path:
    ///          if (IsSimulacrum) xp = GetSimulacrumXp(xp);
    /// </summary>
    partial class Creature
    {
        // --- in-memory state (no schema changes required) -----------------------------------

        private bool _simulacrumCopied;
        private uint _simulacrumSourceGuid;

        /// <summary>
        /// True if this creature's weenie is configured as a simulacrum.
        /// Uses CreatureType.Simulacrum if available; falls back to a name-tag check
        /// so the feature works even before the enum value is added.
        /// </summary>
        public bool IsSimulacrum
        {
            get
            {
                var ct = CreatureType;
                if (ct.HasValue && ((int)ct.Value) == SimulacrumCreatureTypeId)
                    return true;

                // Fallback: weenie marked via name convention (until CreatureType.Simulacrum is added)
                return WeenieClassName != null &&
                       WeenieClassName.Equals("sim", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Numeric id of CreatureType.Simulacrum once added to the enum.
        /// Update this constant to match the enum value you choose.
        /// </summary>
        private const int SimulacrumCreatureTypeId = 200;

        // --- public entry point -------------------------------------------------------------

        /// <summary>
        /// Copies the given player onto this creature, the first time it is called.
        /// Safe to call repeatedly; subsequent calls are no-ops.
        /// </summary>
        public void TryCopyFromPlayer(Player player)
        {
            if (_simulacrumCopied || player == null || !IsSimulacrum)
                return;

            _simulacrumCopied = true;
            _simulacrumSourceGuid = player.Guid.Full;

            try
            {
                CopyAppearanceFromPlayer(player);
                CopyAttributesAndVitalsFromPlayer(player);
                CopySkillsFromPlayer(player);
                CopySpellsFromPlayer(player);
                CopyEquipmentFromPlayer(player);

                Name = $"Simulacrum of {player.Name}";

                // Re-broadcast appearance + full update so nearby clients see the new look.
                EnqueueBroadcast(new GameMessageObjDescEvent(this));
                EnqueueBroadcast(new GameMessageUpdateObject(this));
            }
            catch (Exception ex)
            {
                log.Error($"[Simulacrum] Failed to copy player {player.Name} onto {Name}: {ex}");
            }
        }

        // --- copy helpers -------------------------------------------------------------------

        private void CopyAppearanceFromPlayer(Player p)
        {
            HeritageGroup    = p.HeritageGroup;
            Gender           = p.Gender;

            PaletteBaseDID   = p.PaletteBaseDID;
            HeadObjectDID    = p.HeadObjectDID;
            HairTextureDID   = p.HairTextureDID;
            DefaultHairTextureDID = p.DefaultHairTextureDID;
            EyesTextureDID   = p.EyesTextureDID;
            DefaultEyesTextureDID = p.DefaultEyesTextureDID;
            MouthTextureDID  = p.MouthTextureDID;
            DefaultMouthTextureDID = p.DefaultMouthTextureDID;
            NoseTextureDID   = p.NoseTextureDID;
            DefaultNoseTextureDID = p.DefaultNoseTextureDID;

            SkinPalette      = p.SkinPalette;
            HairPalette      = p.HairPalette;
            EyesPalette      = p.EyesPalette;

            if (p.ObjScale.HasValue)
                ObjScale = p.ObjScale;
        }

        private void CopyAttributesAndVitalsFromPlayer(Player p)
        {
            foreach (var kv in p.Biota.PropertiesAttribute)
            {
                if (Biota.PropertiesAttribute.TryGetValue(kv.Key, out var dst))
                {
                    dst.InitLevel = kv.Value.InitLevel;
                    dst.LevelFromCP = kv.Value.LevelFromCP;
                    dst.CPSpent = kv.Value.CPSpent;
                }
                else
                {
                    Biota.PropertiesAttribute[kv.Key] = new PropertiesAttribute
                    {
                        InitLevel = kv.Value.InitLevel,
                        LevelFromCP = kv.Value.LevelFromCP,
                        CPSpent = kv.Value.CPSpent,
                    };
                }
            }

            foreach (var kv in p.Biota.PropertiesAttribute2nd)
            {
                if (Biota.PropertiesAttribute2nd.TryGetValue(kv.Key, out var dst))
                {
                    dst.InitLevel = kv.Value.InitLevel;
                    dst.LevelFromCP = kv.Value.LevelFromCP;
                    dst.CPSpent = kv.Value.CPSpent;
                    dst.CurrentLevel = kv.Value.InitLevel + kv.Value.LevelFromCP; // full vitals
                }
                else
                {
                    Biota.PropertiesAttribute2nd[kv.Key] = new PropertiesAttribute2nd
                    {
                        InitLevel = kv.Value.InitLevel,
                        LevelFromCP = kv.Value.LevelFromCP,
                        CPSpent = kv.Value.CPSpent,
                        CurrentLevel = kv.Value.InitLevel + kv.Value.LevelFromCP,
                    };
                }
            }

            // Refresh cached vital/attribute objects.
            Attributes.Clear();
            Vitals.Clear();
        }

        private void CopySkillsFromPlayer(Player p)
        {
            if (p.Biota.PropertiesSkill == null) return;

            foreach (var kv in p.Biota.PropertiesSkill)
            {
                Biota.PropertiesSkill[kv.Key] = new PropertiesSkill
                {
                    SAC = kv.Value.SAC,
                    InitLevelFromPP = kv.Value.InitLevelFromPP,
                    LevelFromPP = kv.Value.LevelFromPP,
                    PP = kv.Value.PP,
                    LastUsedTime = 0,
                };
            }

            Skills.Clear();
        }

        private void CopySpellsFromPlayer(Player p)
        {
            if (p.Biota.PropertiesSpellBook == null) return;

            // Replace spellbook with the player's known spells so monster magic AI can cast them.
            Biota.PropertiesSpellBook = new Dictionary<int, float>(p.Biota.PropertiesSpellBook);
        }

        private void CopyEquipmentFromPlayer(Player p)
        {
            // Snapshot current equipped wcids/properties off the player and recreate them on the
            // simulacrum. The clones are tagged so the corpse generator can skip them.
            var equipped = p.EquippedObjects.Values.ToList();

            foreach (var src in equipped)
            {
                try
                {
                    var clone = WorldObjectFactory.CreateNewWorldObject(src.WeenieClassId);
                    if (clone == null) continue;

                    // Mirror the meaningful combat / visual properties.
                    clone.CurrentWieldedLocation = src.CurrentWieldedLocation ?? src.ValidLocations;
                    clone.WielderId = Guid.Full;

                    // Tag as cloned gear so it won't drop and won't leak to players.
                    clone.SetProperty(PropertyBool.Attackable, false);
                    clone.SetProperty(PropertyString.LongDesc, "A spectral copy.");
                    MarkAsSimulacrumGear(clone);

                    if (clone.ValidLocations != null &&
                        TryEquipObjectWithNetworking(clone, clone.ValidLocations.Value))
                    {
                        // success
                    }
                    else
                    {
                        clone.Destroy();
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"[Simulacrum] Failed to clone equipment {src.Name} ({src.WeenieClassId}): {ex.Message}");
                }
            }
        }

        // --- gear tagging (uses an in-memory weak set; survives item lifetime) --------------

        private static readonly HashSet<uint> SimulacrumGearGuids = new HashSet<uint>();

        private static void MarkAsSimulacrumGear(WorldObject wo)
        {
            lock (SimulacrumGearGuids)
                SimulacrumGearGuids.Add(wo.Guid.Full);
        }

        public static bool IsSimulacrumGear(WorldObject wo)
        {
            if (wo == null) return false;
            lock (SimulacrumGearGuids)
                return SimulacrumGearGuids.Contains(wo.Guid.Full);
        }

        // --- XP scaling by loot tier --------------------------------------------------------

        /// <summary>
        /// Returns the XP this simulacrum should grant, scaled to its DeathTreasure tier
        /// rather than its (player-derived) attribute totals.
        /// </summary>
        public long GetSimulacrumXp(long defaultXp)
        {
            var tier = DeathTreasure?.Tier ?? 1;
            // Tier-based curve - tweak to taste.
            //   T1  ->  1x base
            //   T8  -> ~30x base
            var multiplier = Math.Pow(1.6, Math.Max(0, tier - 1));
            return (long)Math.Round(defaultXp * multiplier);
        }
    }
}
