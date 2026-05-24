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
        public bool IsIllusionistMob => GetProperty(PropertyBool.IsIllusionistMob) == true;
        public bool IsIllusionistCopy => GetProperty(PropertyBool.IsIllusionistCopy) == true;
        public bool IsNocturnalMob => GetProperty(PropertyBool.IsNocturnalMob) == true;

        // ---------- Nocturnal landblock fog ----------

        // TryNocturnalSetFog removed — BlackFog2 landblock effect disabled by design.

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
                            && c.GetProperty(PropertyBool.IsIllusionistCopy) != true
                            && Location.SquaredDistanceTo(c.Location) <= rangeSq)
                .FirstOrDefault();

            if (victim == null) { _lastMergerTime = currentUnixTime; return; }

            // ---- Absorb victim's full stats (not just current HP) ----
            // Attributes: add victim StartingValue onto ours.
            foreach (var kv in victim.Attributes)
            {
                if (!Attributes.TryGetValue(kv.Key, out var ours) || ours == null || kv.Value == null)
                    continue;
                var sum = (ulong)ours.StartingValue + kv.Value.StartingValue;
                if (sum > uint.MaxValue) sum = uint.MaxValue;
                ours.StartingValue = (uint)sum;
            }

            // Vitals: add victim's MaxValue (full pool) onto our StartingValue, then refill.
            AbsorbVital(Health, victim.Health);
            AbsorbVital(Stamina, victim.Stamina);
            AbsorbVital(Mana, victim.Mana);
            if (Health != null) Health.Current = Health.MaxValue;
            if (Stamina != null) Stamina.Current = Stamina.MaxValue;
            if (Mana != null) Mana.Current = Mana.MaxValue;

            var absorbed = (int)(victim.Health?.MaxValue ?? 0);
            if (absorbed > 0)
                DamageHistory.OnHeal((uint)absorbed);

            // XP: add victim's reward onto ours (capped at int.MaxValue).
            var theirXp = victim.XpOverride ?? 0;
            if (theirXp > 0)
            {
                var combined = (long)(XpOverride ?? 0) + theirXp;
                if (combined > int.MaxValue) combined = int.MaxValue;
                XpOverride = (int)combined;
            }

            // Visible growth + flashy tell on both bodies
            ObjScale = (ObjScale ?? 1.0f) + 0.1f;
            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.LevelUp, 1.0f));
            victim.EnqueueBroadcast(new GameMessageScript(victim.Guid, PlayScript.HealthDownVoid, 1.0f));

            merges++;
            SetProperty(PropertyInt.MergerMergeCount, merges);

            // Announce to all nearby players — local broadcast so anyone in range sees the merge
            var announceMsg = $"{Name} absorbs {victim.Name}! ({merges}/{DerpACEConfig.MergerMaxMerges}) [Merger]";
            EnqueueBroadcast(new GameMessageSystemChat(announceMsg, ChatMessageType.CombatEnemy));

            // Kill the absorbed neighbor cleanly
            victim.OnDeath(new ACE.Server.Entity.DamageHistoryInfo(this), DamageType.Nether, false);
            victim.Die();

            _lastMergerTime = currentUnixTime;
        }

        private static void AbsorbVital(ACE.Server.WorldObjects.Entity.CreatureVital ours, ACE.Server.WorldObjects.Entity.CreatureVital theirs)
        {
            if (ours == null || theirs == null) return;
            var sum = (ulong)ours.StartingValue + theirs.MaxValue;
            if (sum > uint.MaxValue) sum = uint.MaxValue;
            ours.StartingValue = (uint)sum;
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

        // ---------- Illusionist ----------

        private double? _lastIllusionistSwap;
        private bool _illusionistCopiesSpawned;

        /// <summary>
        /// DerpACE: Spawns N 1-HP copies of this creature on first aggro.
        /// Called from <see cref="Monster_Awareness.WakeUp(bool)"/>.
        /// </summary>
        public void TryIllusionistOnAggro()
        {
            if (!IsIllusionistMob || IsIllusionistCopy) return;
            if (_illusionistCopiesSpawned) return;
            if (Location == null) return;

            _illusionistCopiesSpawned = true;

            var count = Math.Max(1, DerpACEConfig.IllusionistCopyCount);
            var radius = Math.Max(1.0f, DerpACEConfig.IllusionistCopyRadius);
            var alive = 0;

            for (var i = 0; i < count; i++)
            {
                var copy = Factories.WorldObjectFactory.CreateNewWorldObject(WeenieClassId) as Creature;
                if (copy == null) continue;

                // Stamp as illusionist copy so it can't recursively spawn or be absorbed by mergers.
                copy.SetProperty(PropertyBool.IsIllusionistCopy, true);

                // 1 HP and no mana/stamina worth of resistance — these are decoys.
                if (copy.Health != null)
                {
                    copy.Health.StartingValue = 1;
                    copy.Health.Current = 1;
                }

                // No XP, no loot — they're illusions.
                copy.XpOverride = 0;
                copy.DeathTreasureType = null;
                copy.SetProperty(PropertyBool.NpcLooksLikeObject, copy.GetProperty(PropertyBool.NpcLooksLikeObject) ?? false);

                // Visual: same purple shimmer as original
                copy.PaletteTemplate = (int)ACE.Entity.Enum.PaletteTemplate.Purple;
                copy.Shade = 0.7;
                copy.ObjScale = ObjScale;

                // Scatter in a ring around the original
                var angle = (float)(ThreadSafeRandom.Next(0, 6283) / 1000.0);
                var dist = (float)ThreadSafeRandom.Next(1.0f, radius);
                var pos = new ACE.Entity.Position(Location);
                pos.PositionX += dist * (float)Math.Cos(angle);
                pos.PositionY += dist * (float)Math.Sin(angle);
                copy.Location = pos;
                copy.Name = Name;

                if (!copy.EnterWorld())
                {
                    copy.Destroy();
                    continue;
                }

                alive++;
            }

            SetProperty(PropertyInt.IllusionistCopyCount, alive);

            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantUpPurple, 1.0f));
            if (AttackTarget is Player p && alive > 0)
            {
                p.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{Name} shimmers and {alive} illusions appear around it! [Illusionist]",
                    ChatMessageType.CombatEnemy));
            }
        }

        /// <summary>
        /// DerpACE: Periodically swaps positions with a random surviving illusionist copy.
        /// Called from Creature_Tick.Heartbeat.
        /// </summary>
        public void TryIllusionistSwap(double currentUnixTime)
        {
            if (!IsIllusionistMob || IsIllusionistCopy || IsDead || Location == null) return;
            if (!_illusionistCopiesSpawned) return;

            var cooldown = Math.Max(1.0, DerpACEConfig.IllusionistSwapCooldownSeconds);
            if (_lastIllusionistSwap.HasValue && currentUnixTime - _lastIllusionistSwap.Value < cooldown)
                return;

            _lastIllusionistSwap = currentUnixTime;

            var visible = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null || visible.Count == 0) return;

            var copies = visible
                .Where(c => c != null && !c.IsDead && c.Location != null
                            && c != this
                            && c.WeenieClassId == WeenieClassId
                            && c.GetProperty(PropertyBool.IsIllusionistCopy) == true)
                .ToList();

            // Update alive count
            SetProperty(PropertyInt.IllusionistCopyCount, copies.Count);

            if (copies.Count == 0) return;

            var pick = copies[ThreadSafeRandom.Next(0, copies.Count - 1)];

            // Swap positions
            var mine = new ACE.Entity.Position(Location);
            var theirs = new ACE.Entity.Position(pick.Location);

            Location = theirs;
            pick.Location = mine;

            // Fake-teleport both via SendUpdatePosition (no movement interpolation)
            SendUpdatePosition();
            pick.SendUpdatePosition();

            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.HealthDownVoid, 1.0f));
            pick.EnqueueBroadcast(new GameMessageScript(pick.Guid, PlayScript.HealthDownVoid, 1.0f));
        }
    }
}
