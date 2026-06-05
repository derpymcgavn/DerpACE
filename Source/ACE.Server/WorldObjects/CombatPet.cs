using System;
using System.Collections.Generic;
using System.Numerics;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Pathfinding;
using ACE.Server.Physics.Animation;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Summonable monsters combat AI
    /// </summary>
    public partial class CombatPet : Pet
    {
        /// <summary>
        /// A new biota be created taking all of its values from weenie.
        /// </summary>
        public CombatPet(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        /// <summary>
        /// Restore a WorldObject from the database.
        /// </summary>
        public CombatPet(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
        }

        public override bool? Init(Player player, PetDevice petDevice)
        {
            var success = base.Init(player, petDevice);

            if (success == null || !success.Value)
                return success;

            SetCombatMode(CombatMode.Melee);
            MonsterState = State.Awake;
            IsAwake = true;

            // copy ratings from pet device
            DamageRating = petDevice.GearDamage;
            DamageResistRating = petDevice.GearDamageResist;
            CritDamageRating = petDevice.GearCritDamage;
            CritDamageResistRating = petDevice.GearCritDamageResist;
            CritRating = petDevice.GearCrit;
            CritResistRating = petDevice.GearCritResist;

            // are CombatPets supposed to attack monsters that are in the same faction as the pet owner?
            // if not, there are a couple of different approaches to this
            // the easiest way for the code would be to simply set Faction1Bits for the CombatPet to match the pet owner's
            // however, retail pcaps did not contain Faction1Bits for CombatPets

            // doing this the easiest way for the code here, and just removing during appraisal
            Faction1Bits = player.Faction1Bits;

            return true;
        }

        // How far (in world units) the pet may stray from its owner while chasing a target.
        // Beyond this distance the pet drops its target and returns to the owner.
        private const float PetLeashRange = 30.0f;

        // How far from the owner the pet needs to be before it bothers walking back when idle.
        private const float PetReturnThreshold = 6.0f;

        private const double AutoRecoverCooldownSeconds = 0.75;
        private const double IndoorRouteCooldownSeconds = 0.35;
        private const float PetIndoorPathRunRateMultiplier = 1.75f;
        private const float IndoorRouteAttackBuffer = 0.75f;
        private const float IndoorPetAttackBuffer = 1.25f;
        private const float IndoorDirectChaseRange = 12.0f;
        private double _nextAcquireTime;

        // ── Stuck detection ──────────────────────────────────────────────────
        // If the pet has a live target but hasn't moved at least this far in
        // StuckCheckSeconds it is considered stuck (wall, terrain, etc.).
        private const float  StuckMoveThreshold  = 0.5f;   // world units
        private const double StuckCheckSeconds   = 3.0;    // how long before we decide it's stuck

        private Vector3 _lastPositionForStuck;
        private double  _stuckCheckTime;        // the Time.GetUnixTime() of the last position snapshot
        private bool    _stuckCheckPending;     // true once we've armed a stuck-check snapshot

        /// <summary>
        /// Returns the flat cylinder distance between this pet and its owner, or float.MaxValue if unavailable.
        /// </summary>
        private float OwnerDistance => P_PetOwner != null ? GetCylinderDistance(P_PetOwner) : float.MaxValue;

        protected override int GetRouteBurstMin() => Location?.Indoors == true ? 1 : base.GetRouteBurstMin();
        protected override int GetRouteBurstMax() => Location?.Indoors == true ? 2 : base.GetRouteBurstMax();
        protected override double GetMaxRouteFrequency() => Location?.Indoors == true ? IndoorRouteCooldownSeconds : base.GetMaxRouteFrequency();
        protected override float GetPathAttackBuffer() => Location?.Indoors == true ? IndoorPetAttackBuffer : base.GetPathAttackBuffer();

        protected override float GetPathRunRate()
        {
            var runRate = RunRate;
            if (runRate <= 0.0f)
                runRate = GetRunRate();

            return Location?.Indoors == true ? runRate * PetIndoorPathRunRateMultiplier : runRate;
        }

        protected override float GetPathWalkRunThreshold() => 0.0f;

        protected override bool IsPathAttackVisible(WorldObject target)
        {
            if (Location?.Indoors != true)
                return base.IsPathAttackVisible(target);

            if (target == null)
                return false;

            return IsMeleeVisible(target) || IsDirectVisible(target) || GetDistanceToTarget() <= MaxRange + IndoorPetAttackBuffer;
        }

        private void SetAttackTargetFast(Creature target)
        {
            if (target == null)
                return;

            AttackTarget = target;
            CurrentAttack = null;
            MaxRange = 0.0f;
            FailedMovementCount = 0;
            FailedSightCount = 0;
            NextMoveTime = Timers.RunningTime;
            NextAttackTime = Math.Min(NextAttackTime, Timers.RunningTime);
            _stuckCheckPending = false;
        }

        private Creature GetOwnerPreferredTarget()
        {
            if (!PropertyManager.GetBool("pet_attack_selected_enabled").Item || P_PetOwner == null)
                return null;

            return (P_PetOwner.AttackTarget as Creature)
                ?? (P_PetOwner.HealthQueryTarget.HasValue
                    ? P_PetOwner.CurrentLandblock?.GetObject(P_PetOwner.HealthQueryTarget.Value) as Creature
                    : null);
        }

        private bool TryAssistOwnerTarget(bool allowSwitch)
        {
            var ownerTarget = GetOwnerPreferredTarget();
            if (ownerTarget == null || ownerTarget.IsDead || !ownerTarget.Attackable
                || SameFaction(ownerTarget) || !IsVisibleTarget(ownerTarget))
                return false;

            if (AttackTarget == ownerTarget)
                return true;

            if (!allowSwitch && AttackTarget != null)
                return false;

            SetAttackTargetFast(ownerTarget);
            return true;
        }

        private bool ShouldUseIndoorRouteToTarget()
        {
            if (!PathfindingEnabled || Location?.Indoors != true || AttackTarget?.Location == null)
                return false;

            var targetDist = GetDistanceToTarget();
            if (CurrentAttack == CombatType.Melee && targetDist <= MaxRange + IndoorPetAttackBuffer)
                return false;

            if (IsDirectVisible(AttackTarget) && targetDist <= IndoorDirectChaseRange)
                return false;

            return true;
        }

        public bool TryAbortIndoorRouteForAttack()
        {
            if (Location?.Indoors != true || AttackTarget == null || CurrentAttack == null)
                return false;

            var closeEnough = GetDistanceToTarget() <= MaxRange + IndoorRouteAttackBuffer;
            var visibleEnough = CurrentAttack == CombatType.Melee ? IsDirectVisible(AttackTarget) : IsDirectVisible(AttackTarget);
            if (!closeEnough || !visibleEnough)
                return false;

            EndRoute();
            NextMoveTime = Timers.RunningTime;
            NextAttackTime = Math.Min(NextAttackTime, Timers.RunningTime);
            return true;
        }

        public override void StartTurn()
        {
            // When indoors with a live navmesh, prefer a routed path to the target so
            // the pet navigates around dungeon walls instead of walking straight through them.
            // Fall back to the normal straight-line StartTurn if no mesh is available yet.
            if (ShouldUseIndoorRouteToTarget())
            {
                var agentW = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
                var route = Pathfinder.FindRoute(Location, AttackTarget.Location, agentW);
                if (route != null && route.Count > 0)
                {
                    TryRoute(route);
                    if (IsRouteStartPending || IsRouting)
                    {
                        IsMoving = true;
                        LastMoveTime = Timers.RunningTime;
                        return;
                    }
                }
            }

            base.StartTurn();
        }

        public override void HandleFindTarget()
        {
            if (TryAssistOwnerTarget(true))
                return;

            var creature = AttackTarget as Creature;
            var lostTarget = creature == null || creature.IsDead || !IsVisibleTarget(creature);

            if (!lostTarget)
            {
                // Leash check — drop target if we've strayed too far from the owner.
                if (OwnerDistance > PetLeashRange)
                {
                    DropTargetAndReset();
                    return;
                }

                // Stuck check — arm a position snapshot the first time we have a live target,
                // then evaluate after StuckCheckSeconds have passed.
                var now = Common.Time.GetUnixTime();
                var pos = Location?.Pos ?? Vector3.Zero;

                if (!_stuckCheckPending)
                {
                    _lastPositionForStuck = pos;
                    _stuckCheckTime       = now + StuckCheckSeconds;
                    _stuckCheckPending    = true;
                }
                else if (now >= _stuckCheckTime)
                {
                    var moved = Vector3.Distance(pos, _lastPositionForStuck);
                    _lastPositionForStuck = pos;
                    _stuckCheckTime       = now + StuckCheckSeconds;

                    if (moved < StuckMoveThreshold)
                    {
                        // Pet hasn't moved — it's stuck. Drop the target so the mob
                        // loses aggro on the pet, then walk back to the owner.
                        DropTargetAndReset();
                        return;
                    }
                }

                return;
            }

            // We lost the target — clear stuck state.
            _stuckCheckPending = false;

            if (PropertyManager.GetBool("pet_auto_recover_enabled").Item)
            {
                var now = Common.Time.GetUnixTime();
                if (creature != null && AttackTarget != null)
                {
                    _nextAcquireTime = now + AutoRecoverCooldownSeconds;
                    AttackTarget = null;
                    return;
                }

                if (now < _nextAcquireTime)
                    return;
            }

            // Only seek a new target if the owner is currently engaged in combat or
            // has explicitly targeted something. Until then the pet just follows.
            if (!IsOwnerEngaged())
                return;

            FindNextTarget();
        }

        /// <summary>
        /// Returns true when the owner is in an active combat state or has a selected target,
        /// i.e. the pet should be allowed to pick its own targets.
        /// </summary>
        private bool IsOwnerEngaged()
        {
            if (P_PetOwner == null) return false;

            // Owner is directly attacking something.
            if (P_PetOwner.AttackTarget != null) return true;

            // Owner has moused over / targeted a creature (health bar query).
            if (P_PetOwner.HealthQueryTarget.HasValue) return true;

            // Owner is in a non-peace combat mode.
            if (P_PetOwner.CombatMode != CombatMode.NonCombat) return true;

            return false;
        }

        /// <summary>
        /// Drops the current attack target, clears the mob's reference back to this pet
        /// so it stops actively chasing us, resets stuck state, and walks back to owner.
        /// </summary>
        private void DropTargetAndReset()
        {
            if (AttackTarget is Creature mob)
            {
                // If the mob was actively targeting this pet, clear it so it stops chasing.
                if (mob.AttackTarget == this)
                    mob.AttackTarget = null;
            }

            AttackTarget       = null;
            _stuckCheckPending = false;
            ReturnToOwner();
        }

        /// <summary>
        /// Moves the pet back toward its owner. Uses navmesh routing when indoors
        /// so the pet navigates around walls instead of walking straight through them.
        /// </summary>
        private void ReturnToOwner()
        {
            if (P_PetOwner?.PhysicsObj == null || P_PetOwner.Location == null)
                return;

            // Prefer navmesh routing in dungeons so the pet doesn't clip through walls.
            if (PathfindingEnabled && Location != null && Location.Indoors)
            {
                // Point the chase target at the owner so TryRoute knows where to go.
                AttackTarget = null; // clear combat target — we're just going home
                RouteAttackTarget = null;
                RoutePositionTarget = P_PetOwner.Location;
                TryRoute(Pathfinder.FindRoute(Location, P_PetOwner.Location,
                    (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow));
                if (IsRouteStartPending || IsRouting)
                    return;
                // Fallthrough: no mesh available yet, use direct path
            }

            var mvp = new MovementParameters();
            mvp.DistanceToObject = PetReturnThreshold * 0.5f;
            mvp.WalkRunThreshold = 0.0f;

            MoveTo(P_PetOwner, RunRate);
            PhysicsObj.MoveToObject(P_PetOwner.PhysicsObj, mvp);
            PhysicsObj.UpdateTime = Physics.Common.PhysicsTimer.CurrentTime;
        }

        public override bool FindNextTarget()
        {
            // Never autonomously seek targets — the pet only fights when the owner does.
            if (!IsOwnerEngaged())
                return false;

            // DerpACE: prefer whatever the owner has selected/targeted so the pet
            // always assists the owner's fight rather than running off independently.
            if (TryAssistOwnerTarget(true))
                return true;

            var nearbyMonsters = GetNearbyMonsters();
            if (nearbyMonsters.Count == 0)
                return false;

            // Sort by distance to owner so the pet attacks whatever is closest to
            // the owner rather than whatever wandered closest to the pet.
            if (P_PetOwner != null)
            {
                nearbyMonsters.Sort((a, b) =>
                {
                    var da = P_PetOwner.GetCylinderDistance(a);
                    var db = P_PetOwner.GetCylinderDistance(b);
                    return da.CompareTo(db);
                });

                SetAttackTargetFast(nearbyMonsters[0]);
                return true;
            }

            // Fallback: nearest to the pet itself.
            var nearest = BuildTargetDistance(nearbyMonsters, true);
            if (nearest[0].Distance > VisualAwarenessRangeSq)
                return false;

            SetAttackTargetFast(nearest[0].Target);
            return true;
        }

        /// <summary>
        /// Returns a list of attackable monsters in this pet's visible targets
        /// </summary>
        public List<Creature> GetNearbyMonsters()
        {
            var monsters = new List<Creature>();

            foreach (var creature in PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature())
            {
                // why does this need to be in here?
                if (creature.IsDead || !creature.Attackable || creature.Visibility)
                {
                    //Console.WriteLine($"{Name}.GetNearbyMonsters(): refusing to add dead creature {creature.Name} ({creature.Guid})");
                    continue;
                }

                // combat pets do not aggro monsters belonging to the same faction as the pet owner?
                if (SameFaction(creature))
                {
                    // unless the pet owner or the pet is being retaliated against?
                    if (!creature.HasRetaliateTarget(P_PetOwner) && !creature.HasRetaliateTarget(this))
                        continue;
                }

                monsters.Add(creature);
            }

            return monsters;
        }

        public override void Sleep()
        {
            // When the pet has no target, walk back to the owner if it has drifted.
            if (OwnerDistance > PetReturnThreshold)
                ReturnToOwner();
        }
    }
}
