using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
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
        public bool IsHordeMember => GetProperty(PropertyBool.IsHordeMember) == true;
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

            // Use the landblock world-object list directly — ObjMaint.GetVisibleObjects is only
            // populated when a player is nearby, so mobs would never merge in unobserved areas.
            var lb = CurrentLandblock;
            if (lb == null) { _lastMergerTime = currentUnixTime; return; }

            Creature victim = null;
            foreach (var wo in lb.GetAllWorldObjectsForDiagnostics())
            {
                if (wo is not Creature c) continue;
                if (c == this || c.IsDead || c.Location == null) continue;
                if (c is Player) continue;
                if (c.WeenieClassId != WeenieClassId) continue;
                if (c.GetProperty(PropertyBool.IsMergerMob) == true) continue;
                if (c.GetProperty(PropertyBool.IsIllusionistCopy) == true) continue;
                if (Location.SquaredDistanceTo(c.Location) > rangeSq) continue;
                victim = c;
                break;
            }

            if (victim == null) { _lastMergerTime = currentUnixTime; return; }

            // ---- Absorb 1/4 of victim's stats per merge (flat, no DR) ----
            // 8 merges × 25% = +200% stats max → effectively 3× base, capped at MergerMaxMerges (8).
            const float scale = 0.25f;

            // Attributes
            foreach (var kv in victim.Attributes)
            {
                if (!Attributes.TryGetValue(kv.Key, out var ours) || ours == null || kv.Value == null)
                    continue;
                var add = (ulong)(kv.Value.StartingValue * scale);
                var sum = (ulong)ours.StartingValue + add;
                if (sum > uint.MaxValue) sum = uint.MaxValue;
                ours.StartingValue = (uint)sum;
            }

            // Vitals
            AbsorbVital(Health,   victim.Health,   scale);
            AbsorbVital(Stamina,  victim.Stamina,  scale);
            AbsorbVital(Mana,     victim.Mana,     scale);
            if (Health != null) Health.Current = Health.MaxValue;
            if (Stamina != null) Stamina.Current = Stamina.MaxValue;
            if (Mana != null) Mana.Current = Mana.MaxValue;

            var absorbed = (int)(victim.Health?.MaxValue ?? 0);
            if (absorbed > 0)
                DamageHistory.OnHeal((uint)absorbed);

            // XP: add 1/4 of victim's reward.
            var theirXp = victim.XpOverride ?? 0;
            if (theirXp > 0)
            {
                var gain = (long)(theirXp * scale);
                var combined = (long)(XpOverride ?? 0) + gain;
                if (combined > int.MaxValue) combined = int.MaxValue;
                XpOverride = (int)combined;
            }

            // Visible growth + flashy tell
            ObjScale = (ObjScale ?? 1.0f) + 0.1f;
            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.LevelUp, 1.0f));
            victim.EnqueueBroadcast(new GameMessageScript(victim.Guid, PlayScript.HealthDownVoid, 1.0f));

            merges++;
            SetProperty(PropertyInt.MergerMergeCount, merges);

            var announceMsg = $"{Name} absorbs {victim.Name}! (+25% stats, {merges}/{DerpACEConfig.MergerMaxMerges}) [Merger]";
            EnqueueBroadcast(new GameMessageSystemChat(announceMsg, ChatMessageType.CombatEnemy));

            // Kill the absorbed neighbor cleanly
            victim.OnDeath(new ACE.Server.Entity.DamageHistoryInfo(this), DamageType.Nether, false);
            victim.Die();

            _lastMergerTime = currentUnixTime;
        }

        private static void AbsorbVital(ACE.Server.WorldObjects.Entity.CreatureVital ours, ACE.Server.WorldObjects.Entity.CreatureVital theirs, float scale)
        {
            if (ours == null || theirs == null) return;
            var add = (ulong)(theirs.MaxValue * scale);
            var sum = (ulong)ours.StartingValue + add;
            if (sum > uint.MaxValue) sum = uint.MaxValue;
            ours.StartingValue = (uint)sum;
        }

        // ---------- Horde (shared-health pack) ----------

        /// <summary>
        /// DerpACE: Intercept incoming damage for any Horde creature (leader or member).
        /// All damage is applied to the LEADER's health pool. If the leader is dead the
        /// member just die normally. When the shared pool is exhausted, all remaining
        /// pack bodies are killed. Members never individually die from direct combat damage;
        /// only the leader's pool governs pack survival.
        /// Called from <see cref="Monster_Combat.TakeDamage(WorldObject, DamageType, float, bool)"/>.
        /// Returns true if this method consumed the damage (caller should not apply it again).
        /// </summary>
        public bool TryHordeDamageTaken(WorldObject source, DamageType damageType, uint damageTaken)
        {
            // --- Member: route damage to leader ---
            if (IsHordeMember)
            {
                var leaderIid = GetProperty(PropertyInstanceId.HordeLeader);
                if (leaderIid.HasValue)
                {
                    var leader = CurrentLandblock?.GetObject(new ObjectGuid(leaderIid.Value)) as Creature;
                    if (leader != null && !leader.IsDead)
                    {
                        leader.TryHordeDamageTaken(source, damageType, damageTaken);
                        // Give the attacker a visual splatter on this member body
                        EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.SplatterMidLeftBack, 1.0f));
                        return true;
                    }
                }
                // Leader gone — fall through and take damage normally (die)
                return false;
            }

            // --- Leader: apply to shared pool ---
            if (!IsHordeMob || damageTaken == 0 || Health == null) return false;

            var currentSwarm = GetProperty(PropertyInt.HordeSwarmCount) ?? 1;
            var initialCount = GetProperty(PropertyInt.HordeSwarmInitialCount) ?? currentSwarm;

            // Apply damage once to our own health pool and keep normal vital notifications.
            var safeDamage = (int)Math.Min(damageTaken, int.MaxValue);
            var actualDamage = (uint)Math.Max(0, -UpdateVitalDelta(Health, -safeDamage));
            if (source != null && actualDamage > 0)
                DamageHistory.Add(source, damageType, actualDamage);

            var frac = Health.MaxValue > 0 ? (float)Health.Current / Health.MaxValue : 0f;
            var newMembers = Health.Current <= 0 ? 0 : Math.Max(0, (int)Math.Ceiling(initialCount * frac));

            if (newMembers < currentSwarm)
            {
                var killed = currentSwarm - newMembers;
                SetProperty(PropertyInt.HordeSwarmCount, newMembers);

                // Kill off the appropriate number of member bodies
                KillHordeMembers(killed, damageType);

                // Splatter effect on the leader body
                var blips = Math.Min(killed, 3);
                for (var i = 0; i < blips; i++)
                    EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.SplatterMidLeftBack, 1.0f));

                string msg;
                if (newMembers <= 0)
                {
                    msg = killed == 1
                        ? $"The last member of the {Name} pack falls! [Horde]"
                        : $"The last {killed} members of the {Name} pack fall! [Horde]";
                }
                else if (newMembers == 1)
                {
                    msg = $"Only one member of the {Name} pack remains! [Horde]";
                }
                else
                {
                    msg = killed == 1
                        ? $"A member of the {Name} pack is cut down! ({newMembers} remain) [Horde]"
                        : $"{killed} members of the {Name} pack are cut down! ({newMembers} remain) [Horde]";
                }
                EnqueueBroadcast(new GameMessageSystemChat(msg, ChatMessageType.CombatEnemy));
            }

            return true;
        }

        /// <summary>
        /// Spawn the swarm member bodies around this Horde leader.
        /// Called once after the leader is placed in the world.
        /// </summary>
        public void SpawnHordeMembers()
        {
            if (!IsHordeMob || Location == null) return;

            var count = GetProperty(PropertyInt.HordeSwarmCount) ?? 0;
            if (count <= 1) return; // leader IS the 1 body; spawn count-1 additional bodies

            var additionalCount = count - 1;
            var angles = new List<float>();
            for (int i = 0; i < additionalCount; i++)
                angles.Add((float)(2 * Math.PI * i / additionalCount));

            const float radius = 3.5f;

            for (int i = 0; i < additionalCount; i++)
            {
                try
                {
                    var member = WorldObjectFactory.CreateNewWorldObject(WeenieClassId) as Creature;
                    if (member == null) continue;

                    // Tag as member, point to leader
                    member.SetProperty(PropertyBool.IsHordeMember, true);
                    member.SetProperty(PropertyBool.IsHordeMob, false);
                    member.SetProperty(PropertyInstanceId.HordeLeader, Guid.Full);
                    member.Name = Name;

                    // Copy visual tell from leader
                    member.PaletteTemplate = PaletteTemplate;
                    member.Shade = Shade;
                    member.ObjScale = ObjScale;

                    // Members have no XP/loot — reward is on the leader
                    member.SetProperty(PropertyInt.XpOverride, 0);
                    member.SetProperty(PropertyDataId.DeathTreasureType, 0);
                    member.SetProperty(PropertyDataId.WieldedTreasureType, 0);
                    member.SetProperty(PropertyDataId.InventoryTreasureType, 0);

                    // Position offset from leader
                    float ox = (float)Math.Cos(angles[i]) * radius;
                    float oy = (float)Math.Sin(angles[i]) * radius;
                    var pos = new ACE.Entity.Position(Location);
                    pos.Pos = new System.Numerics.Vector3(Location.Pos.X + ox, Location.Pos.Y + oy, Location.Pos.Z);
                    member.Location = pos;

                    if (!member.EnterWorld())
                    {
                        member.Destroy();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"[Horde] Failed to spawn member {i} for {Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Kill exactly <paramref name="count"/> live member bodies for this pack.
        /// If the pool hits zero, kills all remaining members and then also kills the leader.
        /// </summary>
        private void KillHordeMembers(int count, DamageType damageType)
        {
            var remaining = GetProperty(PropertyInt.HordeSwarmCount) ?? 0;
            var killAll = remaining <= 0;

            // Enumerate live members on the same landblock
            var members = new List<Creature>();
            if (CurrentLandblock != null)
            {
                foreach (var wo in CurrentLandblock.GetAllWorldObjectsForDiagnostics())
                {
                    if (wo is Creature c && !c.IsDead
                        && c.GetProperty(PropertyBool.IsHordeMember) == true
                        && c.GetProperty(PropertyInstanceId.HordeLeader) == Guid.Full)
                    {
                        members.Add(c);
                    }
                }
            }

            var toKill = killAll ? members.Count : Math.Min(count, members.Count);
            for (int i = 0; i < toKill; i++)
                members[i].Die();

            if (killAll)
            {
                // Pool exhausted — kill the leader body too via normal die
                var chain = new ActionChain();
                chain.AddDelaySeconds(0.25);
                chain.AddAction(this, () =>
                {
                    OnDeath(DamageHistory.LastDamager, damageType, false);
                    Die();
                });
                chain.EnqueueChain();
            }
        }

        // ---------- Warder ----------

        /// <summary>
        /// DerpACE: returns true if the given target is currently warded by a nearby Warder mob.
        /// A Warder protects other nearby mobs but does NOT ward itself.
        /// Used by Player_Magic to block offensive casts.
        /// </summary>
        public static bool IsWardedTarget(WorldObject target)
        {
            if (target == null || target.Location == null) return false;

            // Warders do not ward themselves - they shield nearby allies instead.
            var targetCreature = target as Creature;

            var range = Math.Max(1.0f, DerpACEConfig.WarderRange);
            var rangeSq = range * range;

            var visible = target.PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null) return false;

            foreach (var c in visible)
            {
                if (c == null || c.IsDead || c.Location == null) continue;
                if (!c.IsWarderMob) continue;
                if (c == targetCreature) continue; // skip self
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
