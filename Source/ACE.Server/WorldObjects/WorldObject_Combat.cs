using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    partial class WorldObject
    {
        /// <summary>
        /// Determines if WorldObject can damage a target via PlayerKillerStatus
        /// </summary>
        /// <returns>null if no errors, else pk error list</returns>
        public virtual List<WeenieErrorWithString> CheckPKStatusVsTarget(WorldObject target, Spell spell)
        {
            // no restrictions here
            // player attacker restrictions handled in override
            return null;
        }

        /// <summary>
        /// Tries to proc any relevant items for the attack
        /// </summary>
        public void TryProcEquippedItems(WorldObject attacker, Creature target, bool selfTarget, WorldObject weapon)
        {
            // handle procs directly on this item -- ie. phials
            // this could also be monsters with the proc spell directly on the creature
            if (HasProc && ProcSpellSelfTargeted == selfTarget)
            {
                // projectile
                // monster
                TryProcItem(attacker, target, selfTarget);
            }

            // handle proc spells for weapon
            // this could be a melee weapon, or a missile launcher
            if (weapon != null && weapon.HasProc && weapon.ProcSpellSelfTargeted == selfTarget
                && (!(attacker is Player procPlayer) || !procPlayer.IsUnarmedArmorPiece(weapon) || procPlayer.IsUnarmedArmorActive(weapon)))
            {
                // weapon
                weapon.TryProcItem(attacker, target, selfTarget);
            }

            if (attacker != this && attacker.HasProc && attacker.ProcSpellSelfTargeted == selfTarget)
            {
                // handle special case -- missile projectiles from monsters w/ a proc directly on the mob
                // monster
                attacker.TryProcItem(attacker, target, selfTarget);
            }

            if (attacker is Creature wielder)
            {
                // DerpACE proc_on_attack_enabled: roll *every* equipped proc-bearing item on attack,
                // not just the swing weapon + aetheria. Excludes items we've already rolled above
                // (this, weapon, attacker) and the defender cloak (handled on proc-on-hit).
                if (PropertyManager.GetBool("proc_on_attack_enabled").Item)
                {
                    // DerpACE: when a player has a real weapon equipped, unarmed gauntlets and boots
                    // must not contribute procs — they are unarmed-only modifiers.
                    var playerWielder = wielder as Player;

                    foreach (var item in wielder.EquippedObjects.Values)
                    {
                        if (item == this || item == weapon || item == attacker)
                            continue;

                        if (!item.HasProc || item.ProcSpellSelfTargeted != selfTarget)
                            continue;

                        // Skip unarmed gauntlet/boot procs unless the player is truly weapon-free.
                        if (playerWielder != null && playerWielder.IsUnarmedArmorPiece(item) && !playerWielder.IsUnarmedArmorActive(item))
                            continue;

                        item.TryProcItem(attacker, target, selfTarget);
                    }
                }
                else
                {
                    // retail-style: only aetheria gets the bonus on-attack proc roll
                    var equippedAetheria = wielder.EquippedObjects.Values.Where(i => Aetheria.IsAetheria(i.WeenieClassId) && i.HasProc && i.ProcSpellSelfTargeted == selfTarget);

                    foreach (var aetheria in equippedAetheria)
                        aetheria.TryProcItem(attacker, target, selfTarget);
                }
            }
        }
    }
}
