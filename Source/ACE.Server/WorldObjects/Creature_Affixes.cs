using System;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: Creature affix runtime behavior (Reaper / Necromancer / Warden).
    /// Spawn-time stat/visual setup lives in <see cref="ACE.Server.Factories.CreatureMutators"/>.
    /// </summary>
    partial class Creature
    {
        public bool IsReaperMob => GetProperty(PropertyBool.IsReaperMob) == true;
        public bool IsNecromancerMob => GetProperty(PropertyBool.IsNecromancerMob) == true;
        public bool IsWarderMob => GetProperty(PropertyBool.IsWarderMob) == true;
        public bool IsNocturnalMob => GetProperty(PropertyBool.IsNocturnalMob) == true;

        // TryNocturnalSetFog removed - BlackFog2 landblock effect disabled by design.

        public static bool TryRedirectWardenDamage(Creature target, WorldObject source, DamageType damageType, float amount, BodyPart bodyPart, bool crit, AttackConditions attackConditions, out int damageTaken)
        {
            damageTaken = 0;

            if (target == null || target.IsDead || target.IsWarderMob || target.Location == null || amount <= 0)
                return false;

            var warden = FindProtectingWarden(target);
            if (warden == null)
                return false;

            target.EnqueueBroadcast(new GameMessageScript(target.Guid, PlayScript.ShieldUpBlue, 0.75f));
            warden.EnqueueBroadcast(new GameMessageScript(warden.Guid, PlayScript.HealthDownBlue, 0.75f));

            damageTaken = (int)warden.TakeDamage(source, damageType, amount, crit);
            if (damageTaken > 0)
            {
                warden.EnqueueBroadcast(new GameMessageHearSpeech($"{warden.Name} absorbs the blow meant for {target.Name}!", warden.Name, warden.Guid.Full, ChatMessageType.Combat), WorldObject.LocalBroadcastRange);
            }

            return true;
        }

        private static Creature FindProtectingWarden(Creature target)
        {
            if (target == null || target.Location == null)
                return null;

            var range = Math.Max(1.0f, DerpACEConfig.WarderRange);
            var rangeSq = range * range;
            var visible = target.PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null)
                return null;

            return visible
                .Where(c => c != null
                            && !c.IsDead
                            && c.IsWarderMob
                            && c != target
                            && c.Location != null
                            && target.SameFaction(c)
                            && target.Location.SquaredDistanceTo(c.Location) <= rangeSq)
                .OrderBy(c => target.Location.SquaredDistanceTo(c.Location))
                .FirstOrDefault();
        }
        /// <summary>
        /// DerpACE: returns true if the given target is currently warded by a nearby Warden mob.
        /// A Warden protects other nearby mobs but does not ward itself.
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
