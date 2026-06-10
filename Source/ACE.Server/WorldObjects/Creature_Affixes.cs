using System;

using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: Creature affix runtime behavior (Reaper / Necromancer / Warder).
    /// Spawn-time stat/visual setup lives in <see cref="ACE.Server.Factories.CreatureMutators"/>.
    /// </summary>
    partial class Creature
    {
        public bool IsReaperMob => GetProperty(PropertyBool.IsReaperMob) == true;
        public bool IsNecromancerMob => GetProperty(PropertyBool.IsNecromancerMob) == true;
        public bool IsWarderMob => GetProperty(PropertyBool.IsWarderMob) == true;
        public bool IsNocturnalMob => GetProperty(PropertyBool.IsNocturnalMob) == true;

        // TryNocturnalSetFog removed - BlackFog2 landblock effect disabled by design.

        /// <summary>
        /// DerpACE: returns true if the given target is currently warded by a nearby Warder mob.
        /// A Warder protects other nearby mobs but does not ward itself.
        /// Used by Player_Magic to block offensive casts.
        /// </summary>
        public static bool IsWardedTarget(WorldObject target)
        {
            if (target == null || target.Location == null)
                return false;

            var targetCreature = target as Creature;

            var range = Math.Max(1.0f, DerpACEConfig.WarderRange);
            var rangeSq = range * range;

            var visible = target.PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null)
                return false;

            foreach (var c in visible)
            {
                if (c == null || c.IsDead || c.Location == null)
                    continue;

                if (!c.IsWarderMob)
                    continue;

                if (c == targetCreature)
                    continue;

                if (target.Location.SquaredDistanceTo(c.Location) <= rangeSq)
                    return true;
            }

            return false;
        }
    }
}
