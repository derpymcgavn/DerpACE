using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Simulacrum mob support.
    ///
    /// A simulacrum creature (CreatureType.Simulacrum) copies the first player it aggros -
    /// visuals, attributes, vitals, skills, equipment, and combat-relevant spells - and then
    /// fights as an evil clone of that player. On death it does NOT drop the cloned player
    /// gear; the corpse uses the underlying weenie's normal DeathTreasure profile, and XP
    /// is scaled by that loot tier.
    /// </summary>
    partial class Creature
    {
        // --- in-memory state (no schema changes required) -----------------------------------

        private bool _simulacrumCopied;
        private uint _simulacrumSourcePlayerGuid;
        private const int ShadowClonePaletteTemplate = (int)ACE.Entity.Enum.PaletteTemplate.Black;
        private const double ShadowCloneShade = 0.15;
        private const float ShadowCloneTranslucency = 0.35f;

        /// <summary>
        /// True if this creature is configured as a simulacrum mob.
        /// </summary>
        public bool IsSimulacrum => CreatureType == ACE.Entity.Enum.CreatureType.Simulacrum;

        /// <summary>
        /// Guid of the player this simulacrum was copied from (0 if not yet copied).
        /// </summary>
        public uint SimulacrumSourcePlayerGuid => _simulacrumSourcePlayerGuid;

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
            _simulacrumSourcePlayerGuid = player.Guid.Full;

            try
            {
                CopyAppearanceFromPlayer(player);
                CopyAttributesAndVitalsFromPlayer(player);
                CopySkillsFromPlayer(player);
                CopySpellsFromPlayer(player);
                CopyEquipmentFromPlayer(player);

                Name = $"Simulacrum of {player.Name}";

                // Re-broadcast appearance so nearby clients see the new look.
                EnqueueBroadcast(new GameMessageObjDescEvent(this));
            }
            catch (Exception ex)
            {
                log.Error($"[Simulacrum] Failed to copy player {player.Name} onto {Name}: {ex}");
            }
        }

        /// <summary>
        /// If this simulacrum has the SimulacrumMutator flag, picks a random nearby player to copy.
        /// Otherwise, copies the specified target player.
        /// </summary>
        public void TryCopyFromPlayerOrRandom(Player targetPlayer)
        {
            if (_simulacrumCopied || !IsSimulacrum)
                return;

            // Check if this simulacrum has the mutator flag for random player selection
            var hasMutator = GetProperty(PropertyBool.IsSimulacrumMob) == true;

            if (hasMutator)
            {
                // Get all visible creatures (includes players)
                var visibleCreatures = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature();
                var nearbyPlayers = visibleCreatures.OfType<Player>().ToList();

                if (nearbyPlayers.Count > 0)
                {
                    // Pick a random player
                    var randomPlayer = nearbyPlayers[ACE.Common.ThreadSafeRandom.Next(0, nearbyPlayers.Count - 1)];
                    TryCopyFromPlayer(randomPlayer);
                }
                else if (targetPlayer != null)
                {
                    // Fallback to target if no other players nearby
                    TryCopyFromPlayer(targetPlayer);
                }
            }
            else
            {
                // Standard behavior: copy the attack target
                TryCopyFromPlayer(targetPlayer);
            }
        }

        public void CopyShadowCloneFromPlayer(Player player)
        {
            if (player == null)
                return;

            try
            {
                SetupTableId = player.SetupTableId;
                MotionTableId = player.MotionTableId;
                PhysicsTableId = player.PhysicsTableId;
                SoundTableId = player.SoundTableId;
                CombatTableDID = player.CombatTableDID;

                CopyAppearanceFromPlayer(player);
                CopyAttributesAndVitalsFromPlayer(player);
                CopySkillsFromPlayer(player);
                CopyVoidProjectileSpellsFromPlayer(player);
                ClearExistingCloneEquipment();
                CopyEquipmentFromPlayer(player);
                ApplyShadowCloneVisuals();

                PaletteTemplate = ShadowClonePaletteTemplate;
                Shade = ShadowCloneShade;
                Translucency = ShadowCloneTranslucency;

                EnqueueBroadcast(new GameMessageObjDescEvent(this));
            }
            catch (Exception ex)
            {
                log.Error($"[ShadowClone] Failed to copy player {player.Name} onto {Name}: {ex}");
            }
        }

        // --- copy helpers -------------------------------------------------------------------

        private void CopyAppearanceFromPlayer(Player p)
        {
            HeritageGroup = p.HeritageGroup;
            Gender        = p.Gender;

            PaletteBaseDID         = p.PaletteBaseDID;
            HeadObjectDID          = p.HeadObjectDID;
            EyesTextureDID         = p.EyesTextureDID;
            DefaultEyesTextureDID  = p.DefaultEyesTextureDID;
            MouthTextureDID        = p.MouthTextureDID;
            DefaultMouthTextureDID = p.DefaultMouthTextureDID;
            NoseTextureDID         = p.NoseTextureDID;
            DefaultNoseTextureDID  = p.DefaultNoseTextureDID;

            SkinPaletteDID = p.SkinPaletteDID;
            HairPaletteDID = p.HairPaletteDID;
            EyesPaletteDID = p.EyesPaletteDID;

            if (p.ObjScale.HasValue)
                ObjScale = p.ObjScale;
        }

        private void CopyAttributesAndVitalsFromPlayer(Player p)
        {
            foreach (var kv in p.Biota.PropertiesAttribute)
            {
                if (Biota.PropertiesAttribute.TryGetValue(kv.Key, out var dst))
                {
                    dst.InitLevel   = kv.Value.InitLevel;
                    dst.LevelFromCP = kv.Value.LevelFromCP;
                    dst.CPSpent     = kv.Value.CPSpent;
                }
                else
                {
                    Biota.PropertiesAttribute[kv.Key] = new PropertiesAttribute
                    {
                        InitLevel   = kv.Value.InitLevel,
                        LevelFromCP = kv.Value.LevelFromCP,
                        CPSpent     = kv.Value.CPSpent,
                    };
                }
            }

            foreach (var kv in p.Biota.PropertiesAttribute2nd)
            {
                var maxLevel = kv.Value.InitLevel + kv.Value.LevelFromCP;

                if (Biota.PropertiesAttribute2nd.TryGetValue(kv.Key, out var dst))
                {
                    dst.InitLevel    = kv.Value.InitLevel;
                    dst.LevelFromCP  = kv.Value.LevelFromCP;
                    dst.CPSpent      = kv.Value.CPSpent;
                    dst.CurrentLevel = maxLevel;
                }
                else
                {
                    Biota.PropertiesAttribute2nd[kv.Key] = new PropertiesAttribute2nd
                    {
                        InitLevel    = kv.Value.InitLevel,
                        LevelFromCP  = kv.Value.LevelFromCP,
                        CPSpent      = kv.Value.CPSpent,
                        CurrentLevel = maxLevel,
                    };
                }
            }

            // Refresh cached attribute / vital wrappers so they pick up the new biota values.
            Attributes[PropertyAttribute.Strength]     = new CreatureAttribute(this, PropertyAttribute.Strength);
            Attributes[PropertyAttribute.Endurance]    = new CreatureAttribute(this, PropertyAttribute.Endurance);
            Attributes[PropertyAttribute.Coordination] = new CreatureAttribute(this, PropertyAttribute.Coordination);
            Attributes[PropertyAttribute.Quickness]    = new CreatureAttribute(this, PropertyAttribute.Quickness);
            Attributes[PropertyAttribute.Focus]        = new CreatureAttribute(this, PropertyAttribute.Focus);
            Attributes[PropertyAttribute.Self]         = new CreatureAttribute(this, PropertyAttribute.Self);

            Vitals[PropertyAttribute2nd.MaxHealth]  = new CreatureVital(this, PropertyAttribute2nd.MaxHealth);
            Vitals[PropertyAttribute2nd.MaxStamina] = new CreatureVital(this, PropertyAttribute2nd.MaxStamina);
            Vitals[PropertyAttribute2nd.MaxMana]    = new CreatureVital(this, PropertyAttribute2nd.MaxMana);

            Health.Current  = Health.MaxValue;
            Stamina.Current = Stamina.MaxValue;
            Mana.Current    = Mana.MaxValue;
        }

        private void CopySkillsFromPlayer(Player p)
        {
            if (p.Biota.PropertiesSkill == null)
                return;

            foreach (var kv in p.Biota.PropertiesSkill)
            {
                Biota.PropertiesSkill[kv.Key] = new PropertiesSkill
                {
                    SAC                   = kv.Value.SAC,
                    InitLevel             = kv.Value.InitLevel,
                    LevelFromPP           = kv.Value.LevelFromPP,
                    PP                    = kv.Value.PP,
                    ResistanceAtLastCheck = kv.Value.ResistanceAtLastCheck,
                    LastUsedTime          = 0,
                };

                Skills[kv.Key] = new CreatureSkill(this, kv.Key, Biota.PropertiesSkill[kv.Key]);
            }
        }

        private void CopySpellsFromPlayer(Player p)
        {
            if (p.Biota.PropertiesSpellBook == null)
                return;

            // Replace spellbook with the player's known spells so monster magic AI can cast them.
            Biota.PropertiesSpellBook = new Dictionary<int, float>(p.Biota.PropertiesSpellBook);
        }

        private void CopyVoidProjectileSpellsFromPlayer(Player p)
        {
            Biota.PropertiesSpellBook = new Dictionary<int, float>();

            if (p.Biota.PropertiesSpellBook == null)
                return;

            foreach (var kv in p.Biota.PropertiesSpellBook)
            {
                var spell = new Spell((uint)kv.Key);
                if (!IsShadowCloneVoidProjectileSpell(spell))
                    continue;

                Biota.PropertiesSpellBook[kv.Key] = kv.Value;
            }
        }

        private static bool IsShadowCloneVoidProjectileSpell(Spell spell)
        {
            if (spell == null || spell.School != MagicSchool.VoidMagic || !spell.IsHarmful)
                return false;

            var spellType = SpellProjectile.GetProjectileSpellType(spell.Id);
            return spellType == ProjectileSpellType.Bolt ||
                   spellType == ProjectileSpellType.Streak ||
                   spellType == ProjectileSpellType.Arc ||
                   spellType == ProjectileSpellType.Ring;
        }

        private void ClearExistingCloneEquipment()
        {
            foreach (var item in EquippedObjects.Values.ToList())
            {
                try
                {
                    if (TryDequipObject(item.Guid, out var removed, out _))
                        removed.Destroy();
                }
                catch (Exception ex)
                {
                    log.Warn($"[ShadowClone] Failed to clear shell equipment {item.Name} ({item.WeenieClassId}): {ex.Message}");
                }
            }
        }

        private void CopyEquipmentFromPlayer(Player p)
        {
            // Snapshot equipped items and recreate them on the simulacrum. Clones are tagged
            // (SimulacrumGearGuids) so the corpse generator can skip them on death.
            var equipped = p.EquippedObjects.Values.ToList();

            foreach (var src in equipped)
            {
                try
                {
                    var clone = WorldObjectFactory.CreateNewWorldObject(src.WeenieClassId);
                    if (clone == null)
                        continue;

                    // Mark as cloned so it's filtered out of corpse loot.
                    MarkAsSimulacrumGear(clone);

                    var slot = src.CurrentWieldedLocation ?? clone.ValidLocations ?? src.ValidLocations;
                    if (slot == null || slot == 0)
                    {
                        clone.Destroy();
                        continue;
                    }

                    if (!TryEquipObjectWithBroadcasting(clone, slot.Value))
                        clone.Destroy();
                }
                catch (Exception ex)
                {
                    log.Warn($"[Simulacrum] Failed to clone equipment {src.Name} ({src.WeenieClassId}): {ex.Message}");
                }
            }
        }

        private void ApplyShadowCloneVisuals()
        {
            var armorSlots = EquipMask.Armor | EquipMask.Extremity | EquipMask.Clothing;

            foreach (var item in EquippedObjects.Values)
            {
                var slot = item.CurrentWieldedLocation ?? item.ValidLocations;
                if (slot == null || (slot.Value & armorSlots) == 0)
                    continue;

                item.PaletteTemplate = ShadowClonePaletteTemplate;
                item.Shade = ShadowCloneShade;
                item.Translucency = ShadowCloneTranslucency;
            }
        }

        // --- gear tagging (in-memory; entries removed when items are destroyed) -------------

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
        /// instead of its (player-derived) attributes.
        /// </summary>
        public long GetSimulacrumXp(long defaultXp)
        {
            var tier = DeathTreasure?.Tier ?? 1;
            // Tier curve: T1 = 1x, T2 = 1.6x, ... T8 ~ 27x
            var multiplier = Math.Pow(1.6, Math.Max(0, tier - 1));
            return (long)Math.Round(defaultXp * multiplier);
        }
    }
}
