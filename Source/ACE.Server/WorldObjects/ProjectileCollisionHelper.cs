using System;
using System.Linq;
using System.Numerics;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics.Extensions;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Helper class for arrows / bolts / thrown weapons
    /// outside of the WorldObject hierarchy
    /// </summary>
    public static class ProjectileCollisionHelper
    {
        public static void OnCollideObject(WorldObject worldObject, WorldObject target)
        {
            if (!worldObject.PhysicsObj.is_active()) return;

            //Console.WriteLine($"Projectile.OnCollideObject - {WorldObject.Name} ({WorldObject.Guid}) -> {target.Name} ({target.Guid})");

            if (worldObject.ProjectileTarget == null || worldObject.ProjectileTarget != target)
            {
                //Console.WriteLine("Unintended projectile target! (should be " + ProjectileTarget.Guid.Full.ToString("X8") + " - " + ProjectileTarget.Name + ")");
                OnCollideEnvironment(worldObject);
                return;
            }

            // take damage
            var sourceCreature = worldObject.ProjectileSource as Creature;
            var sourcePlayer = worldObject.ProjectileSource as Player;
            var targetCreature = target as Creature;

            DamageEvent damageEvent = null;

            if (targetCreature != null && targetCreature.IsAlive)
            {
                if (sourcePlayer != null)
                {
                    // player damage monster or player
                    damageEvent = sourcePlayer.DamageTarget(targetCreature, worldObject);

                    if (damageEvent != null && damageEvent.HasDamage)
                        worldObject.EnqueueBroadcast(new GameMessageSound(worldObject.Guid, Sound.Collision, 1.0f));
                }
                else if (sourceCreature != null && sourceCreature.AttackTarget != null)
                {
                    // todo: clean this up
                    var targetPlayer = sourceCreature.AttackTarget as Player;

                    damageEvent = DamageEvent.CalculateDamage(sourceCreature, targetCreature, worldObject);

                    if (targetPlayer != null)
                    {
                        // monster damage player
                        if (damageEvent.HasDamage)
                        {
                            targetPlayer.TakeDamage(sourceCreature, damageEvent);

                            // blood splatter?

                            if (damageEvent.ShieldMod != 1.0f)
                            {
                                var shieldSkill = targetPlayer.GetCreatureSkill(Skill.Shield);
                                Proficiency.OnSuccessUse(targetPlayer, shieldSkill, shieldSkill.Current);   // ??
                            }

                            // handle Dirty Fighting
                            if (sourceCreature.GetCreatureSkill(Skill.DirtyFighting).AdvancementClass >= SkillAdvancementClass.Trained)
                                sourceCreature.FightDirty(targetPlayer, damageEvent.Weapon);
                        }
                        else
                            targetPlayer.OnEvade(sourceCreature, CombatType.Missile);
                    }
                    else
                    {
                        // monster damage pet
                        if (damageEvent.HasDamage)
                        {
                            targetCreature.TakeDamage(sourceCreature, damageEvent.DamageType, damageEvent.Damage);

                            // blood splatter?

                            // handle Dirty Fighting
                            if (sourceCreature.GetCreatureSkill(Skill.DirtyFighting).AdvancementClass >= SkillAdvancementClass.Trained)
                                sourceCreature.FightDirty(targetCreature, damageEvent.Weapon);
                        }

                        if (!(targetCreature is CombatPet))
                        {
                            // faction mobs and foetype
                            sourceCreature.MonsterOnAttackMonster(targetCreature);
                        }
                    }
                }

                // handle target procs
                if (damageEvent != null && damageEvent.HasDamage)
                {
                    bool threadSafe = true;

                    if (LandblockManager.CurrentlyTickingLandblockGroupsMultiThreaded)
                    {
                        // Ok... if we got here, we're likely in the parallel landblock physics processing.
                        if (worldObject.CurrentLandblock == null || sourceCreature.CurrentLandblock == null || targetCreature.CurrentLandblock == null || worldObject.CurrentLandblock.CurrentLandblockGroup != sourceCreature.CurrentLandblock.CurrentLandblockGroup || sourceCreature.CurrentLandblock.CurrentLandblockGroup != targetCreature.CurrentLandblock.CurrentLandblockGroup)
                            threadSafe = false;
                    }

                    if (threadSafe)
                        // This can result in spell projectiles being added to either sourceCreature or targetCreature landblock.
                        // worldObject is hitting targetCreature, so they should almost always be in the same landblock
                        worldObject.TryProcEquippedItems(sourceCreature, targetCreature, false, worldObject.ProjectileLauncher);
                    else
                    {
                        // sourceCreature and creatureTarget are now in different landblock groups.
                        // What has likely happened is that sourceCreature sent a projectile toward creatureTarget. Before impact, sourceCreature was teleported away.
                        // To perform this fully thread safe, we would enqueue the work onto worldManager.
                        // WorldManager.EnqueueAction(new ActionEventDelegate(() => sourceCreature.TryProcEquippedItems(targetCreature, false)));
                        // But, to keep it simple, we will just ignore it and not bother with TryProcEquippedItems for this particular impact.
                    }

                    TryLaunchRicochet(worldObject, sourcePlayer, targetCreature);
                    TryLaunchDinnerwareBounces(worldObject, sourcePlayer, targetCreature);
                }
            }

            worldObject.CurrentLandblock?.RemoveWorldObject(worldObject.Guid, showError: !worldObject.PhysicsObj.entering_world);
            worldObject.PhysicsObj.set_active(false);

            worldObject.HitMsg = true;
        }

        private static void TryLaunchRicochet(WorldObject projectile, Player sourcePlayer, Creature firstTarget)
        {
            if (sourcePlayer == null || firstTarget == null)
                return;

            if (projectile.GetProperty(PropertyBool.IsRicochetProjectile) == true)
                return;

            var launcher = projectile.ProjectileLauncher;
            var ammo = projectile.ProjectileAmmo;
            if (ammo == null
                || (launcher?.GetProperty(PropertyBool.IsRicochetAtlatl) != true
                    && launcher?.GetProperty(PropertyBool.IsDartflingerAtlatl) != true))
                return;

            var procChance = launcher.GetProperty(PropertyFloat.RicochetProcChance) ?? 0.0;
            if (procChance <= 0.0 || ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                return;

            var radius = Math.Max(1.0f, (float)(launcher.GetProperty(PropertyFloat.RicochetRadius) ?? 10.0));
            var radiusSq = radius * radius;

            var landblock = firstTarget.CurrentLandblock ?? sourcePlayer.CurrentLandblock;
            if (landblock == null || firstTarget.Location == null)
                return;

            var firstLandblock = firstTarget.Location.Cell & 0xFFFF0000;
            var bounceTarget = landblock.GetAllWorldObjectsForDiagnostics()
                .OfType<Creature>()
                .Where(c => c != null
                            && c != firstTarget
                            && c != sourcePlayer
                            && c.IsAlive
                            && c.Attackable
                            && c.IsMonster
                            && !c.Teleporting
                            && c.Location != null
                            && (c.Location.Cell & 0xFFFF0000) == firstLandblock
                            && firstTarget.Location.SquaredDistanceTo(c.Location) <= radiusSq)
                .OrderBy(c => firstTarget.Location.SquaredDistanceTo(c.Location))
                .FirstOrDefault();

            if (bounceTarget == null)
                return;

            if (!sourcePlayer.TryStartMutatorCooldown(launcher, Player.RicochetCooldownId, Player.RicochetCooldownSeconds))
                return;

            var origin = firstTarget.Location.Pos;
            origin.Z += firstTarget.Height * 0.5f;

            var dest = bounceTarget.Location.Pos;
            dest.Z += bounceTarget.Height * 0.5f;

            var dir = Vector3.Normalize(dest - origin);
            if (!dir.IsValid())
                return;

            var angle = Math.Atan2(-dir.X, dir.Y);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)angle);
            var velocity = sourcePlayer.GetProjectileVelocity(bounceTarget, origin, dir, dest, sourcePlayer.GetProjectileSpeed(), out _);
            if (!velocity.IsValid() || velocity == Vector3.Zero)
                return;

            var ricochet = sourcePlayer.LaunchProjectile(launcher, ammo, bounceTarget, origin, rotation, velocity);
            if (ricochet == null)
                return;

            ricochet.SetProperty(PropertyBool.IsRicochetProjectile, true);

            var damageScale = Math.Clamp((float)(launcher.GetProperty(PropertyFloat.RicochetDamageScale) ?? 0.5), 0.05f, 1.0f);
            ricochet.DamageMod = (ricochet.DamageMod ?? 1.0) * damageScale;

            sourcePlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Your dart skips toward {bounceTarget.Name}.",
                ChatMessageType.CombatSelf));
        }

        private static void TryLaunchDinnerwareBounces(WorldObject projectile, Player sourcePlayer, Creature firstTarget)
        {
            if (sourcePlayer == null || firstTarget == null)
                return;

            if (projectile.GetProperty(PropertyBool.IsDinnerwareBounceProjectile) == true)
                return;

            var ammo = projectile.ProjectileAmmo;
            if (ammo == null)
                return;

            var dinnerwareSource = projectile.GetProperty(PropertyBool.IsDinnerwareWeapon) == true
                ? projectile
                : ammo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true
                    ? ammo
                    : projectile.ProjectileLauncher?.GetProperty(PropertyBool.IsDinnerwareWeapon) == true
                        ? projectile.ProjectileLauncher
                        : null;

            if (dinnerwareSource == null)
                return;

            var procChance = dinnerwareSource.GetProperty(PropertyFloat.DinnerwareSpinProcChance) ?? 0.0;
            if (procChance <= 0.0 || ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                return;

            var radius = Math.Max(1.0f, (float)(dinnerwareSource.GetProperty(PropertyFloat.DinnerwareSpinRadius) ?? 5.0));
            var radiusSq = radius * radius;

            var landblock = firstTarget.CurrentLandblock ?? sourcePlayer.CurrentLandblock;
            if (landblock == null || firstTarget.Location == null)
                return;

            var firstLandblock = firstTarget.Location.Cell & 0xFFFF0000;
            var candidates = landblock.GetAllWorldObjectsForDiagnostics()
                .OfType<Creature>()
                .Where(c => c != null
                            && c != firstTarget
                            && c != sourcePlayer
                            && c.IsAlive
                            && c.Attackable
                            && c.IsMonster
                            && !c.Teleporting
                            && c.Location != null
                            && (c.Location.Cell & 0xFFFF0000) == firstLandblock)
                .ToList();

            var bounceTargets = new System.Collections.Generic.List<Creature>();
            var currentTarget = firstTarget;
            while (bounceTargets.Count < 4 && currentTarget?.Location != null)
            {
                var nextTarget = candidates
                    .Where(c => !bounceTargets.Contains(c)
                                && currentTarget.Location.SquaredDistanceTo(c.Location) <= radiusSq)
                    .OrderBy(c => currentTarget.Location.SquaredDistanceTo(c.Location))
                    .FirstOrDefault();

                if (nextTarget == null)
                    break;

                bounceTargets.Add(nextTarget);
                currentTarget = nextTarget;
            }

            if (bounceTargets.Count == 0)
                return;

            if (!sourcePlayer.TryStartMutatorCooldown(dinnerwareSource, Player.DinnerwareCooldownId, Player.DinnerwareCooldownSeconds))
                return;

            var damageScales = new[] { 0.50, 0.25, 0.10, 0.05 };
            var launchedNames = new System.Collections.Generic.List<string>();
            var actionChain = new ActionChain();
            Creature previousTarget = firstTarget;

            for (var i = 0; i < bounceTargets.Count; i++)
            {
                var bounceTarget = bounceTargets[i];
                var bounceOrigin = previousTarget;
                var damageScale = damageScales[Math.Min(i, damageScales.Length - 1)];
                var delay = 0.16f * i;

                if (delay > 0)
                    actionChain.AddDelaySeconds(delay);

                actionChain.AddAction(sourcePlayer, () =>
                {
                    if (sourcePlayer.IsDead || bounceOrigin == null || bounceTarget == null || !bounceTarget.IsAlive || bounceOrigin.Location == null || bounceTarget.Location == null)
                        return;

                    var origin = bounceOrigin.Location.Pos;
                    origin.Z += bounceOrigin.Height + 0.25f;

                    var dest = bounceTarget.Location.Pos;
                    dest.Z += bounceTarget.Height * 0.75f;

                    var dir = Vector3.Normalize(dest - origin);
                    if (!dir.IsValid())
                        return;

                    var angle = Math.Atan2(-dir.X, dir.Y);
                    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)angle);
                    var velocity = sourcePlayer.GetProjectileVelocity(bounceTarget, origin, dir, dest, sourcePlayer.GetProjectileSpeed(), out _);
                    if (!velocity.IsValid() || velocity == Vector3.Zero)
                        return;

                    bounceOrigin.ApplyVisualEffects(PlayScript.ProjectileCollision);
                    var bounce = sourcePlayer.LaunchProjectile(projectile.ProjectileLauncher ?? ammo, ammo, bounceTarget, origin, rotation, velocity);
                    if (bounce == null)
                        return;

                    bounce.SetProperty(PropertyBool.IsDinnerwareBounceProjectile, true);
                    bounce.DamageMod = (bounce.DamageMod ?? 1.0) * damageScale;
                });

                launchedNames.Add($"{bounceTarget.Name} ({damageScale:P0})");
                previousTarget = bounceTarget;
            }

            if (launchedNames.Count > 0)
            {
                var isDiscus = dinnerwareSource.WeenieClassId == (uint)ACE.Server.Factories.Enum.WeenieClassName.discus;
                var message = isDiscus
                    ? $"Your discus answers the warrior princess's call, ricocheting toward {string.Join(", ", launchedNames)}."
                    : $"Your dinnerware caroms toward {string.Join(", ", launchedNames)}.";

                firstTarget.ApplyVisualEffects(PlayScript.ProjectileCollision);
                sourcePlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.CombatSelf));
                actionChain.EnqueueChain();
            }
        }

        public static void OnCollideEnvironment(WorldObject worldObject)
        {
            if (!worldObject.PhysicsObj.is_active()) return;

            // do not send 'Your missile attack hit the environment' messages to player,
            // if projectile is still in the process of spawning into world.
            if (worldObject.PhysicsObj.entering_world)
                return;

            //Console.WriteLine($"Projectile.OnCollideEnvironment({WorldObject.Name} - {WorldObject.Guid})");

            worldObject.CurrentLandblock?.RemoveWorldObject(worldObject.Guid, showError: !worldObject.PhysicsObj.entering_world);
            worldObject.PhysicsObj.set_active(false);

            if (worldObject.ProjectileSource is Player player)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("Your missile attack hit the environment.", ChatMessageType.Broadcast));
            }
            else if (worldObject.ProjectileSource is Creature creature)
            {
                creature.MonsterProjectile_OnCollideEnvironment();
            }

            worldObject.HitMsg = true;
        }
    }
}
