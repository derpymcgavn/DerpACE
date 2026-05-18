using System;
using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: Creature affix runtime behavior (Reaper / Necromancer / Merger / Horde / Warder).
    /// Spawn-time stat/visual setup lives in <see cref="ACE.Server.Factories.CreatureMutators"/>.
    /// </summary>
    partial class Creature
    {
        public bool IsReaperMob => GetProperty(PropertyBool.IsReaperMob) == true;
        public bool IsNecromancerMob => GetProperty(PropertyBool.IsNecromancerMob) == true;
        public bool IsMergerMob => GetProperty(PropertyBool.IsMergerMob) == true;
        public bool IsHordeMob => GetProperty(PropertyBool.IsHordeMob) == true;
        public bool IsWarderMob => GetProperty(PropertyBool.IsWarderMob) == true;

        // ---------- Merger ----------

        private double? _lastMergerTime;

        /// <summary>
        /// DerpACE: Merger heartbeat — periodically absorbs a nearby same-WCID, non-Merger creature,
        /// inheriting its remaining health, growing visibly, and announcing the merge.
        /// </summary>
        public void TryMergerHeartbeat(double currentUnixTime)
        {
            if (!IsMergerMob || IsDead || Location == null) return;

            var cooldown = Math.Max(2.0, DerpACEConfig.MergerCooldownSeconds);
            if (_lastMergerTime.HasValue && currentUnixTime - _lastMergerTime.Value < cooldown)
                return;

            var merges = GetProperty(PropertyInt.MergerMergeCount) ?? 0;
            if (merges >= Math.Max(1, DerpACEConfig.MergerMaxMerges)) return;

            var range = Math.Max(2.0f, DerpACEConfig.MergerSearchRange);
            var rangeSq = range * range;

            var visible = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null || visible.Count == 0) return;

            var victim = visible
                .Where(c => c != null && c != this && !c.IsDead && c.Location != null
                            && c is not Player
                            && c.WeenieClassId == WeenieClassId
                            && c.GetProperty(PropertyBool.IsMergerMob) != true
                            && Location.SquaredDistanceTo(c.Location) <= rangeSq)
                .FirstOrDefault();

            if (victim == null) { _lastMergerTime = currentUnixTime; return; }

            // Absorb the victim's remaining HP into ours
            var absorbed = (int)(victim.Health?.Current ?? 0);
            if (absorbed > 0 && Health != null)
            {
                Health.StartingValue += (uint)absorbed;
                Health.Current = Health.MaxValue;
                DamageHistory.OnHeal((uint)absorbed);
            }

            // Visible growth + flashy tell on both bodies
            ObjScale = (ObjScale ?? 1.0f) * 1.08f;
            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.LevelUp, 1.0f));
            victim.EnqueueBroadcast(new GameMessageScript(victim.Guid, PlayScript.HealthDownVoid, 1.0f));

            merges++;
            SetProperty(PropertyInt.MergerMergeCount, merges);

            // Announce to attacker (if a player) — combat chat feedback per spec
            if (AttackTarget is Player p)
            {
                p.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{Name} absorbs {victim.Name}! ({merges}/{DerpACEConfig.MergerMaxMerges}) [Merger]",
                    ChatMessageType.CombatEnemy));
            }

            // Kill the absorbed neighbor cleanly
            victim.OnDeath(new ACE.Server.Entity.DamageHistoryInfo(this), DamageType.Nether, false);
            victim.Die();

            _lastMergerTime = currentUnixTime;
        }

        // ---------- Horde ----------

        /// <summary>
        /// DerpACE: Horde damage interception — converts incoming damage chunks into discrete
        /// "swarm member" kills, announces shrinkage, and plays a death blip per member lost.
        /// Called from <see cref="Monster_Combat.TakeDamage(WorldObject, DamageType, float, bool)"/>.
        /// </summary>
        public void OnHordeDamageTaken(WorldObject source, uint damageTaken)
        {
            if (!IsHordeMob || damageTaken == 0 || Health == null) return;

            var swarm = GetProperty(PropertyInt.HordeSwarmCount) ?? 1;
            if (swarm <= 1) return;

            // Members remaining is proportional to remaining health
            // newMembers = ceil(swarm * remainingFraction)
            var frac = Math.Clamp((float)Health.Current / Health.MaxValue, 0.0f, 1.0f);
            var startCount = GetProperty(PropertyInt.HordeSwarmCount) ?? swarm;
            // Initial fraction at full-spawn corresponds to startCount; recompute current count
            var currentMembers = Math.Max(1, (int)Math.Ceiling(startCount * frac));

            if (currentMembers >= swarm) return;

            var killed = swarm - currentMembers;
            SetProperty(PropertyInt.HordeSwarmCount, currentMembers);

            // Visual splatter per member killed (capped to avoid spam)
            var blips = Math.Min(killed, 3);
            for (var i = 0; i < blips; i++)
                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.SplatterMidLeftBack, 1.0f));

            if (source is Player player)
            {
                var msg = killed == 1
                    ? $"You cut down a member of {Name}! ({currentMembers} remain) [Horde]"
                    : $"You cut down {killed} members of {Name}! ({currentMembers} remain) [Horde]";
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.CombatEnemy));
            }
        }

        // ---------- Warder ----------

        /// <summary>
        /// DerpACE: returns true if the given target is currently warded by a nearby Warder mob
        /// (or is a Warder itself). Used by Player_Magic to block offensive casts.
        /// </summary>
        public static bool IsWardedTarget(WorldObject target)
        {
            if (target == null || target.Location == null) return false;
            if (target is Creature targetCreature && targetCreature.IsWarderMob) return true;

            var range = Math.Max(1.0f, DerpACEConfig.WarderRange);
            var rangeSq = range * range;

            var visible = target.PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null) return false;

            foreach (var c in visible)
            {
                if (c == null || c.IsDead || c.Location == null) continue;
                if (!c.IsWarderMob) continue;
                if (target.Location.SquaredDistanceTo(c.Location) <= rangeSq)
                    return true;
            }
            return false;
        }
    }
}
