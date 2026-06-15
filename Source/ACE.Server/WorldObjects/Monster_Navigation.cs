using System;
using System.Collections.Generic;
using System.Numerics;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Pathfinding;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Common;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        /// <summary>
        /// Return to home if target distance exceeds this range
        /// </summary>
        public const float MaxChaseRange = 96.0f;
        public const float MaxChaseRangeSq = MaxChaseRange * MaxChaseRange;

        /// <summary>
        /// Determines if a monster is within melee range of target
        /// </summary>
        //public const float MaxMeleeRange = 1.5f;
        public const float MaxMeleeRange = 0.75f;
        //public const float MaxMeleeRange = 1.5f + 0.6f + 0.1f;    // max melee range + distance from + buffer

        /// <summary>
        /// The maximum range for a monster missile attack
        /// </summary>
        //public const float MaxMissileRange = 80.0f;
        //public const float MaxMissileRange = 40.0f;   // for testing

        /// <summary>
        /// The distance per second from running animation
        /// </summary>
        public float MoveSpeed;

        /// <summary>
        /// The run skill via MovementSystem GetRunRate()
        /// </summary>
        public float RunRate;

        /// <summary>
        /// Flag indicates monster is turning towards target
        /// </summary>
        public bool IsTurning = false;

        /// <summary>
        /// Flag indicates monster is moving towards target
        /// </summary>
        public bool IsMoving = false;

        /// <summary>
        /// The last time a movement tick was processed
        /// </summary>
        public double LastMoveTime;

        public bool DebugMove;

        public double NextMoveTime;
        public double NextCancelTime;

        // Smart chase recovery state (wall-stuck detection + course correction).
        private Vector3 lastStuckSamplePos = new Vector3(float.NaN, float.NaN, float.NaN);
        private float lastStuckSampleTargetDist = float.MaxValue;
        private double nextStuckSampleTime;
        private int stuckStrikeCount;
        private int courseCorrectionAttemptCount;
        private double nextCourseCorrectionTime;
        private double nextCrowdUnstickTime;
        private double nextDoorOpenAttemptTime;
        private double nextMeleeCornerRecoveryTime;

        private const double StuckSampleInterval = 0.25;
        private const float StuckMinTravelDistance = 0.18f;
        private const float StuckMinDistanceImprovement = 0.10f;
        private const int StuckCourseCorrectionThreshold = 2;
        private const double CourseCorrectionCooldownMin = 0.15;
        private const double CourseCorrectionCooldownMax = 0.30;
        private const double CrowdUnstickCooldown = 0.45;
        private const double DoorOpenAttemptCooldown = 0.30;
        private const double MeleeCornerRecoveryCooldown = 0.35;
        private const float MeleeCornerRecoveryRangeBuffer = 2.0f;
        private const float DoorOpenScanRadius = 3.5f;
        private const float DoorOpenForwardBias = -0.25f;
        private const float CrowdBlockerScanRadius = 2.6f;
        private const float CrowdBlockerClearance = 1.35f;
        private const float CrowdEscapeStepMin = 1.4f;
        private const float CrowdEscapeStepMax = 3.4f;

        /// <summary>
        /// Starts the process of monster turning towards target
        /// </summary>
        public virtual void StartTurn()
        {
            //if (Timers.RunningTime < NextMoveTime)
            //return;
            if (!MoveReady())
                return;

            if (DebugMove)
                Console.WriteLine($"{Name} ({Guid}) - StartTurn, ranged={IsRanged}");

            if (MoveSpeed == 0.0f)
                GetMovementSpeed();

            //Console.WriteLine($"[{Timers.RunningTime}] - {Name} ({Guid}) - starting turn");

            IsTurning = true;
            ResetStuckTracking();

            // send network actions
            var targetDist = GetDistanceToTarget();
            var turnTo = (IsRanged && targetDist <= MaxRange) || (CurrentAttack == CombatType.Magic && targetDist <= GetSpellMaxRange()) || AiImmobile;
            if (turnTo)
                TurnTo(AttackTarget);
            else
                MoveTo(AttackTarget, RunRate);

            // need turning listener?
            IsTurning = false;
            IsMoving = true;
            LastMoveTime = Timers.RunningTime;
            NextCancelTime = LastMoveTime + ThreadSafeRandom.Next(2, 4);
            moveBit = false;

            var mvp = GetMovementParameters();
            if (turnTo)
                PhysicsObj.TurnToObject(AttackTarget.PhysicsObj, mvp);
            else
                PhysicsObj.MoveToObject(AttackTarget.PhysicsObj, mvp);

            // prevent initial snap
            PhysicsObj.UpdateTime = PhysicsTimer.CurrentTime;
        }

        private void ResetStuckTracking()
        {
            lastStuckSamplePos = new Vector3(float.NaN, float.NaN, float.NaN);
            lastStuckSampleTargetDist = float.MaxValue;
            nextStuckSampleTime = 0;
            stuckStrikeCount = 0;
            courseCorrectionAttemptCount = 0;
            nextCrowdUnstickTime = 0;
            nextDoorOpenAttemptTime = 0;
            nextMeleeCornerRecoveryTime = 0;
        }

        private bool IsMeleeGeometryBlocked(float targetDist, bool isMeleeVisible)
        {
            return CurrentAttack == CombatType.Melee
                && !isMeleeVisible
                && targetDist <= MaxRange + MeleeCornerRecoveryRangeBuffer;
        }

        private bool TryRecoverMeleeGeometryBlock(double currentUnixTime)
        {
            if (AttackTarget?.Location == null || Location == null)
                return false;

            if (currentUnixTime < nextMeleeCornerRecoveryTime)
                return IsRouting || IsRouteStartPending || HasPendingMovement;

            nextMeleeCornerRecoveryTime = currentUnixTime + MeleeCornerRecoveryCooldown;

            if (TryOpenNearbyDoor())
                return true;

            if (PathfindingEnabled && CanAttemptRouteAfterNullRoute(currentUnixTime))
            {
                TryRoute();
                if (IsRouteStartPending || IsRouting)
                    return true;
            }

            return TrySmartCourseCorrection(escalate: true);
        }

        private bool TrySmartCourseCorrection(bool escalate = false)
        {
            if (AttackTarget?.Location == null || Location == null)
                return false;

            var now = Timers.RunningTime;
            if (!escalate && now < nextCourseCorrectionTime)
                return false;

            if (PathfindingEnabled && Location != null)
            {
                TryRoute();
                if (IsRouteStartPending || IsRouting)
                    return true;
            }

            var toTarget = AttackTarget.Location.ToGlobal() - Location.ToGlobal();
            toTarget.Z = 0;
            if (toTarget.LengthSquared() <= 0.0001f)
                return false;

            var forward = Vector3.Normalize(toTarget);
            var side = Vector3.Normalize(new Vector3(-forward.Y, forward.X, 0));

            // Alternate left/right strafing and gradually widen sidesteps if still blocked.
            var sideSign = (courseCorrectionAttemptCount % 2 == 0) ? 1.0f : -1.0f;
            var widen = Math.Min(3, courseCorrectionAttemptCount / 2);
            var lateralDistance = 1.8f + (widen * 0.6f);
            var forwardDistance = 1.2f + (widen * 0.4f);

            var sidestep = side * sideSign * lateralDistance;
            var advance = forward * forwardDistance;

            var candidate = new ACE.Entity.Position(Location)
            {
                PositionX = Location.PositionX + sidestep.X + advance.X,
                PositionY = Location.PositionY + sidestep.Y + advance.Y,
            };

            if ((candidate.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
                return false;

            MoveTo(candidate, RunRate, true, 1.0f);
            IsMoving = true;
            courseCorrectionAttemptCount++;
            nextCourseCorrectionTime = now + ThreadSafeRandom.Next((float)CourseCorrectionCooldownMin, (float)CourseCorrectionCooldownMax);
            NextCancelTime = now + 1.25;
            return true;
        }

        private void TrackAndHandleStuck()
        {
            if (AttackTarget?.Location == null || !IsMoving)
                return;

            var now = Timers.RunningTime;
            if (now < nextStuckSampleTime)
                return;

            nextStuckSampleTime = now + StuckSampleInterval;

            var pos = Location.ToGlobal();
            var targetDist = GetDistanceToTarget();

            if (float.IsNaN(lastStuckSamplePos.X))
            {
                lastStuckSamplePos = pos;
                lastStuckSampleTargetDist = targetDist;
                return;
            }

            var traveled = Vector3.Distance(lastStuckSamplePos, pos);
            var improved = lastStuckSampleTargetDist - targetDist;

            if (traveled < StuckMinTravelDistance && improved < StuckMinDistanceImprovement)
                stuckStrikeCount++;
            else
                stuckStrikeCount = Math.Max(0, stuckStrikeCount - 1);

            lastStuckSamplePos = pos;
            lastStuckSampleTargetDist = targetDist;

            if (stuckStrikeCount >= StuckCourseCorrectionThreshold)
            {
                stuckStrikeCount = 0;
                if (!TryOpenNearbyDoor() && !TryCrowdUnstick())
                    TrySmartCourseCorrection(escalate: true);
            }
        }

        private bool TryOpenNearbyDoor()
        {
            if (Location == null || PhysicsObj?.ObjMaint == null)
                return false;

            var now = Timers.RunningTime;
            if (now < nextDoorOpenAttemptTime)
                return false;

            nextDoorOpenAttemptTime = now + DoorOpenAttemptCooldown;

            var scanSq = DoorOpenScanRadius * DoorOpenScanRadius;
            var forward = GetDoorSearchForward();

            Door nearestDoor = null;
            var nearestDoorDistanceSq = float.MaxValue;
            var currentPos = Location.ToGlobal();

            foreach (var visible in PhysicsObj.ObjMaint.GetVisibleObjectsValuesWhere(o => o?.WeenieObj?.WorldObject is Door))
            {
                if (!(visible.WeenieObj.WorldObject is Door door) || door.Location == null || door.IsOpen)
                    continue;

                if ((door.Location.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
                    continue;

                var distanceSq = Location.SquaredDistanceTo(door.Location);
                if (distanceSq > scanSq || distanceSq >= nearestDoorDistanceSq)
                    continue;

                var offset = door.Location.ToGlobal() - currentPos;
                offset.Z = 0;
                if (forward != Vector3.Zero && offset.LengthSquared() > 0.001f
                    && Vector3.Dot(Vector3.Normalize(offset), forward) < DoorOpenForwardBias)
                    continue;

                nearestDoor = door;
                nearestDoorDistanceSq = distanceSq;
            }

            if (nearestDoor != null && nearestDoor.TryOpenForCreature(this))
            {
                FailedMovementCount = 0;
                nextDoorOpenAttemptTime = now + DoorOpenAttemptCooldown;
                NextCancelTime = Math.Max(NextCancelTime, now + 1.0);
                return true;
            }

            return false;
        }

        private Vector3 GetDoorSearchForward()
        {
            Vector3 forward;
            if (AttackTarget?.Location != null)
                forward = AttackTarget.Location.ToGlobal() - Location.ToGlobal();
            else if (LastPathMoveTarget != null)
                forward = LastPathMoveTarget.ToGlobal() - Location.ToGlobal();
            else
                forward = Location.GetCurrentDir();

            forward.Z = 0;
            return forward.LengthSquared() > 0.001f ? Vector3.Normalize(forward) : Vector3.Zero;
        }

        private bool TryCrowdUnstick()
        {
            if (AttackTarget?.Location == null || Location == null || PhysicsObj?.ObjMaint == null)
                return false;

            var now = Timers.RunningTime;
            if (now < nextCrowdUnstickTime)
                return false;

            var blockers = GetNearbyCreatureBlockers();
            if (blockers.Count == 0)
                return false;

            // Ask nearby movers to sidestep first. This is the "communication" piece:
            // large packs can make room for each other instead of every mob fighting
            // the same blocked navpath.
            var asked = 0;
            foreach (var blocker in blockers)
            {
                if (asked >= 3)
                    break;

                if (blocker.TryGrantPassage(this))
                    asked++;
            }

            if (!TryFindCrowdEscapePosition(blockers, out var escape))
            {
                nextCrowdUnstickTime = now + CrowdUnstickCooldown;
                return asked > 0;
            }

            if (HasPendingMovement && PhysicsObj?.MovementManager?.MoveToManager != null)
                PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ObjectGone);

            LastPathMoveTarget = escape;
            MoveAlongPath(escape);
            IsMoving = true;
            nextCrowdUnstickTime = now + CrowdUnstickCooldown;
            NextCancelTime = now + 1.25;
            return true;
        }

        private List<Creature> GetNearbyCreatureBlockers()
        {
            var result = new List<Creature>();
            var myGuid = Guid.Full;
            var scanSq = CrowdBlockerScanRadius * CrowdBlockerScanRadius;
            var toTarget = AttackTarget.Location.ToGlobal() - Location.ToGlobal();
            toTarget.Z = 0;
            var hasTargetDir = toTarget.LengthSquared() > 0.001f;
            var targetDir = hasTargetDir ? Vector3.Normalize(toTarget) : Vector3.Zero;

            foreach (var visible in PhysicsObj.ObjMaint.GetVisibleObjectsValuesOfTypeCreature())
            {
                if (visible == null || visible.Guid.Full == myGuid || visible.IsDead || visible is Player)
                    continue;

                if (visible.Location == null || (visible.Location.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
                    continue;

                var offset = visible.Location.ToGlobal() - Location.ToGlobal();
                offset.Z = 0;
                var distSq = offset.LengthSquared();
                if (distSq > scanSq)
                    continue;

                // Prefer blockers that are in front of this creature's path to target.
                if (hasTargetDir && distSq > 0.001f)
                {
                    var dot = Vector3.Dot(Vector3.Normalize(offset), targetDir);
                    if (dot < -0.25f)
                        continue;
                }

                result.Add(visible);
            }

            result.Sort((a, b) => Location.SquaredDistanceTo(a.Location).CompareTo(Location.SquaredDistanceTo(b.Location)));
            return result;
        }

        private bool TryFindCrowdEscapePosition(List<Creature> blockers, out ACE.Entity.Position escape)
        {
            escape = null;
            if (Location == null || AttackTarget?.Location == null)
                return false;

            var toTarget = AttackTarget.Location.ToGlobal() - Location.ToGlobal();
            toTarget.Z = 0;
            if (toTarget.LengthSquared() <= 0.001f)
                toTarget = Location.GetCurrentDir();

            var forward = Vector3.Normalize(toTarget);
            var side = Vector3.Normalize(new Vector3(-forward.Y, forward.X, 0));

            // Try perpendicular openings first, then slight forward/back offsets.
            var directions = new[]
            {
                side,
                -side,
                Vector3.Normalize(side + forward * 0.45f),
                Vector3.Normalize(-side + forward * 0.45f),
                Vector3.Normalize(side - forward * 0.35f),
                Vector3.Normalize(-side - forward * 0.35f),
                forward,
                -forward,
            };

            var bestScore = float.MinValue;
            ACE.Entity.Position best = null;

            for (var i = 0; i < directions.Length; i++)
            {
                var step = CrowdEscapeStepMin + (i / 2) * 0.55f;
                if (step > CrowdEscapeStepMax)
                    step = CrowdEscapeStepMax;

                var candidate = new ACE.Entity.Position(Location)
                {
                    PositionX = Location.PositionX + directions[i].X * step,
                    PositionY = Location.PositionY + directions[i].Y * step,
                };

                if ((candidate.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
                    continue;

                candidate = SnapToMeshIfAvailable(candidate);
                if (!IsValidPathPosition(candidate))
                    continue;

                if ((candidate.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
                    continue;

                var nearestBlockerSq = float.MaxValue;
                foreach (var blocker in blockers)
                    nearestBlockerSq = Math.Min(nearestBlockerSq, candidate.SquaredDistanceTo(blocker.Location));

                if (nearestBlockerSq < CrowdBlockerClearance * CrowdBlockerClearance)
                    continue;

                var targetImprovement = Location.DistanceTo(AttackTarget.Location) - candidate.DistanceTo(AttackTarget.Location);
                var score = nearestBlockerSq + targetImprovement * 0.75f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            escape = best;
            return escape != null;
        }

        private ACE.Entity.Position SnapToMeshIfAvailable(ACE.Entity.Position candidate)
        {
            if (!PathfindingEnabled || candidate == null)
                return candidate;

            var agentWidth = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
            return Pathfinder.GetClosestPointOnMesh(candidate, agentWidth) ?? candidate;
        }

        /// <summary>
        /// Called when the TurnTo process has completed
        /// </summary>
        public void OnTurnComplete()
        {
            var dir = Vector3.Normalize(AttackTarget.Location.ToGlobal() - Location.ToGlobal());
            Location.Rotate(dir);

            IsTurning = false;

            if (!IsRanged)
                StartMove();
        }

        /// <summary>
        /// Starts the process of monster moving towards target
        /// </summary>
        public void StartMove()
        {
            LastMoveTime = Timers.RunningTime;
            IsMoving = true;
        }

        /// <summary>
        /// Called when the MoveTo process has completed
        /// </summary>
        public override void OnMoveComplete(WeenieError status)
        {
            if (DebugMove)
                Console.WriteLine($"{Name} ({Guid}) - OnMoveComplete({status})");

            if (IsDestroyed || CurrentLandblock == null || PhysicsObj == null || Location == null)
            {
                IsMoving = false;
                FailedMovementCount = 0;
                ResetStuckTracking();
                return;
            }

            OnMovementStopped();

            if (PhysicsObj != null && Location != null)
            {
                SyncMoveToPosition();
                PhysicsObj.CachedVelocity = Vector3.Zero;
            }

            if (status != WeenieError.ActionCancelled)
                IsMoving = false;

            if (IsMovingToHome)
                PendingEndMoveToHome = true;
            if (IsGrantingPassage)
                PendingEndGrantPassage = true;
            if (IsWandering)
                PendingEndWandering = true;

            switch (status)
            {
                case WeenieError.ObjectGone:
                    FailedMovementCount = 0;
                    ResetStuckTracking();
                    return;
                case WeenieError.None:
                    FailedMovementCount = 0;
                    ResetStuckTracking();

                    if (IsRouting)
                    {
                        PendingContinueRoute = true;
                        return;
                    }
                    break;
                default:
                    // Don't recurse: ActionCancelled here can be caused by our own
                    // TrySmartCourseCorrection issuing a new MoveTo, which cancels the
                    // prior one and synchronously re-enters OnMoveComplete, leading to
                    // a StackOverflowException.
                    if (status == WeenieError.ActionCancelled)
                        return;

                    FailedMovementCount++;

                    if (MonsterState == State.Awake && AttackTarget != null && TrySmartCourseCorrection())
                        return;

                    if (IsRouting)
                    {
                        if (FailedMovementCount >= FailedRoutingThreshold)
                            PendingEndRoute = true;
                        else
                            PendingRetryRoute = true;
                    }
                    return;
            }

            if (AiImmobile && CurrentAttack == CombatType.Melee)
            {
                var targetDist = GetDistanceToTarget();
                if (targetDist > MaxRange)
                    ResetAttack();
            }

            if (MonsterState == State.Return)
                Sleep();
        }

        /// <summary>
        /// Estimates the time it will take the monster to turn and move towards target
        /// </summary>
        public float EstimateTargetTime()
        {
            return EstimateTurnTo() + EstimateMoveTo();
        }

        /// <summary>
        /// Estimates the time it will take the monster to turn towards target
        /// </summary>
        public float EstimateTurnTo()
        {
            return GetRotateDelay(AttackTarget);
        }

        /// <summary>
        /// Estimates the time it will take the monster to move towards target
        /// </summary>
        public float EstimateMoveTo()
        {
            return GetDistanceToTarget() / MoveSpeed;
        }

        /// <summary>
        /// Returns TRUE if monster is within target melee range
        /// </summary>
        public bool IsMeleeRange()
        {
            return GetDistanceToTarget() <= MaxMeleeRange;
        }

        /// <summary>
        /// Returns TRUE if monster in range for current attack type
        /// </summary>
        public bool IsAttackRange()
        {
            return GetDistanceToTarget() <= MaxRange;
        }

        /// <summary>
        /// Gets the distance to target, with radius excluded
        /// </summary>
        public float GetDistanceToTarget()
        {
            if (AttackTarget == null)
                return float.MaxValue;

            //var matchIndoors = Location.Indoors == AttackTarget.Location.Indoors;
            //var targetPos = matchIndoors ? AttackTarget.Location.ToGlobal() : AttackTarget.Location.Pos;
            //var sourcePos = matchIndoors ? Location.ToGlobal() : Location.Pos;

            //var dist = (targetPos - sourcePos).Length();
            //var radialDist = dist - (AttackTarget.PhysicsObj.GetRadius() + PhysicsObj.GetRadius());

            // always use spheres?
            var cylDist = (float)Physics.Common.Position.CylinderDistance(PhysicsObj.GetRadius(), PhysicsObj.GetHeight(), PhysicsObj.Position,
                AttackTarget.PhysicsObj.GetRadius(), AttackTarget.PhysicsObj.GetHeight(), AttackTarget.PhysicsObj.Position);

            if (DebugMove)
                Console.WriteLine($"{Name}.DistanceToTarget: {cylDist}");

            //return radialDist;
            return cylDist;
        }

        /// <summary>
        /// Returns the destination position the monster is attempting to move to
        /// to perform a melee attack
        /// </summary>
        public Vector3 GetDestination()
        {
            var dir = Vector3.Normalize(Location.ToGlobal() - AttackTarget.Location.ToGlobal());
            return AttackTarget.Location.Pos + dir * (AttackTarget.PhysicsObj.GetRadius() + PhysicsObj.GetRadius());
        }

        /// <summary>
        /// Primary movement handler, determines if target in range
        /// </summary>
        public void Movement()
        {
            //if (!IsRanged)
                UpdatePosition();

            if (MonsterState == State.Awake && AttackTarget != null)
                TryOpenNearbyDoor();

            TrackAndHandleStuck();

            if (MonsterState == State.Awake && GetDistanceToTarget() >= MaxChaseRange)
            {
                CancelMoveTo();
                FindNextTarget();
                return;
            }

            if (PhysicsObj.MovementManager.MoveToManager.FailProgressCount > 0 && Timers.RunningTime > NextCancelTime)
            {
                if (!TrySmartCourseCorrection())
                    CancelMoveTo();
            }
        }

        public void UpdatePosition(bool netsend = true)
        {
            //stopwatch.Restart();
            PhysicsObj.update_object();
            //ServerPerformanceMonitor.AddToCumulativeEvent(ServerPerformanceMonitor.CumulativeEventHistoryType.Monster_Navigation_UpdatePosition_PUO, stopwatch.Elapsed.TotalSeconds);
            UpdatePosition_SyncLocation();

            if (netsend)
                SendUpdatePosition();

            if (DebugMove)
                //Console.WriteLine($"{Name} ({Guid}) - UpdatePosition (velocity: {PhysicsObj.CachedVelocity.Length()})");
                Console.WriteLine($"{Name} ({Guid}) - UpdatePosition: {Location.ToLOCString()}");

            if (MonsterState == State.Return && PhysicsObj.MovementManager.MoveToManager.PendingActions.Count == 0)
                Sleep();

            if (MonsterState == State.Awake && IsMoving && PhysicsObj.MovementManager.MoveToManager.PendingActions.Count == 0)
                IsMoving = false;
        }

        /// <summary>
        /// Synchronizes the WorldObject Location with the Physics Location
        /// </summary>
        public void UpdatePosition_SyncLocation()
        {
            // was the position successfully moved to?
            // use the physics position as the source-of-truth?
            var newPos = PhysicsObj.Position;

            if (Location.LandblockId.Raw != newPos.ObjCellID)
            {
                var prevBlockCell = Location.LandblockId.Raw;
                var prevBlock = Location.LandblockId.Raw >> 16;
                var prevCell = Location.LandblockId.Raw & 0xFFFF;

                var newBlockCell = newPos.ObjCellID;
                var newBlock = newPos.ObjCellID >> 16;
                var newCell = newPos.ObjCellID & 0xFFFF;

                Location.LandblockId = new LandblockId(newPos.ObjCellID);

                if (prevBlock != newBlock)
                {
                    LandblockManager.RelocateObjectForPhysics(this, true);
                    //Console.WriteLine($"Relocating {Name} from {prevBlockCell:X8} to {newBlockCell:X8}");
                    //Console.WriteLine("Old position: " + Location.Pos);
                    //Console.WriteLine("New position: " + newPos.Frame.Origin);
                }
                //else
                    //Console.WriteLine("Moving " + Name + " to " + Location.LandblockId.Raw.ToString("X8"));
            }

            // skip ObjCellID check when updating from physics
            // TODO: update to newer version of ACE.Entity.Position
            Location.PositionX = newPos.Frame.Origin.X;
            Location.PositionY = newPos.Frame.Origin.Y;
            Location.PositionZ = newPos.Frame.Origin.Z;

            Location.Rotation = newPos.Frame.Orientation;

            if (DebugMove)
                DebugDistance();
        }

        public void DebugDistance()
        {
            if (AttackTarget == null) return;

            var dist = GetDistanceToTarget();
            var angle = GetAngle(AttackTarget);
            //Console.WriteLine("Dist: " + dist);
            //Console.WriteLine("Angle: " + angle);
        }

        public void GetMovementSpeed()
        {
            var moveSpeed = MotionTable.GetRunSpeed(MotionTableId);
            if (moveSpeed == 0) moveSpeed = 2.5f;
            var scale = ObjScale ?? 1.0f;

            RunRate = GetRunRate();

            MoveSpeed = moveSpeed * RunRate * scale;

            //Console.WriteLine(Name + " - Run: " + runSkill + " - RunRate: " + RunRate + " - Move: " + MoveSpeed + " - Scale: " + scale);
        }

        /// <summary>
        /// Returns the RunRate that is sent to the client as myRunRate
        /// </summary>
        public float GetRunRate()
        {
            var burden = 0.0f;

            // assuming burden only applies to players...
            if (this is Player player)
            {
                var strength = Strength.Current;

                var capacity = EncumbranceSystem.EncumbranceCapacity((int)strength, player.AugmentationIncreasedCarryingCapacity);
                burden = EncumbranceSystem.GetBurden(capacity, EncumbranceVal ?? 0);

                // TODO: find this exact formula in client
                // technically this would be based on when the player releases / presses the movement key after stamina > 0
                if (player.IsExhausted)
                    burden = 3.0f;
            }

            var runSkill = GetCreatureSkill(Skill.Run).Current;
            var runRate = MovementSystem.GetRunRate(burden, (int)runSkill, 1.5f);

            return (float)runRate;
        }

        /// <summary>
        /// Sets the corpse to the final position
        /// </summary>
        public void SetFinalPosition()
        {
            if (AttackTarget == null) return;

            var playerDir = AttackTarget.Location.GetCurrentDir();
            Location.Pos = AttackTarget.Location.Pos + playerDir * (AttackTarget.PhysicsObj.GetRadius() + PhysicsObj.GetRadius());
            SendUpdatePosition();
        }

        /// <summary>
        /// Returns TRUE if monster is facing towards the target
        /// </summary>
        public bool IsFacing(WorldObject target)
        {
            if (target?.Location == null) return false;

            var angle = GetAngle(target);
            var dist = Math.Max(0, GetDistanceToTarget());

            // rotation accuracy?
            var threshold = 5.0f;

            var minDist = 10.0f;

            if (dist < minDist)
                threshold += (minDist - dist) * 1.5f;

            if (DebugMove)
                Console.WriteLine($"{Name}.IsFacing({target.Name}): Angle={angle}, Dist={dist}, Threshold={threshold}, {angle < threshold}");

            return angle < threshold;
        }

        public MovementParameters GetMovementParameters()
        {
            var mvp = new MovementParameters();

            // set non-default params for monster movement
            mvp.Flags &= ~MovementParamFlags.CanWalk;

            var turnTo = IsRanged || (CurrentAttack == CombatType.Magic && GetDistanceToTarget() <= GetSpellMaxRange()) || AiImmobile;

            if (!turnTo)
                mvp.Flags |= MovementParamFlags.FailWalk | MovementParamFlags.UseFinalHeading | MovementParamFlags.Sticky | MovementParamFlags.MoveAway;

            return mvp;
        }

        /// <summary>
        /// The maximum distance a monster can travel outside of its home position
        /// </summary>
        public double? HomeRadius
        {
            get => GetProperty(PropertyFloat.HomeRadius);
            set { if (!value.HasValue) RemoveProperty(PropertyFloat.HomeRadius); else SetProperty(PropertyFloat.HomeRadius, value.Value); }
        }

        public static float DefaultHomeRadius = 192.0f;
        public static float DefaultHomeRadiusSq = DefaultHomeRadius * DefaultHomeRadius;

        private float? homeRadiusSq;

        public float HomeRadiusSq
        {
            get
            {
                if (homeRadiusSq == null)
                {
                    var homeRadius = HomeRadius ?? DefaultHomeRadius;
                    homeRadiusSq = (float)(homeRadius * homeRadius);
                }
                return homeRadiusSq.Value;
            }
        }

        public void CheckMissHome()
        {
            if (MonsterState == State.Return)
                return;

            var homePosition = GetPosition(PositionType.Home);
            //var matchIndoors = Location.Indoors == homePosition.Indoors;

            //var globalPos = matchIndoors ? Location.ToGlobal() : Location.Pos;
            //var globalHomePos = matchIndoors ? homePosition.ToGlobal() : homePosition.Pos;
            var globalPos = Location.ToGlobal();
            var globalHomePos = homePosition.ToGlobal();

            var homeDistSq = Vector3.DistanceSquared(globalHomePos, globalPos);

            if (homeDistSq > HomeRadiusSq)
                MoveToHome();
        }

        public void MoveToHome()
        {
            if (DebugMove)
                Console.WriteLine($"{Name}.MoveToHome()");

            var prevAttackTarget = AttackTarget;

            MonsterState = State.Return;
            AttackTarget = null;

            var home = GetPosition(PositionType.Home);

            if (Location.Equals(home))
            {
                Sleep();
                return;
            }

            NextCancelTime = Timers.RunningTime + 5.0f;

            MoveTo(home, RunRate, false, 1.0f);

            var homePos = new Physics.Common.Position(home);

            var mvp = GetMovementParameters();
            mvp.DistanceToObject = 0.6f;
            mvp.DesiredHeading = homePos.Frame.get_heading();

            PhysicsObj.MoveToPosition(homePos, mvp);
            IsMoving = true;

            EmoteManager.OnHomeSick(prevAttackTarget);
        }

        public void CancelMoveTo()
        {
            //Console.WriteLine($"{Name}.CancelMoveTo()");

            PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ActionCancelled);
            PhysicsObj.MovementManager.MoveToManager.FailProgressCount = 0;

            if (MonsterState == State.Return)
                ForceHome();

            EnqueueBroadcastMotion(new Motion(CurrentMotionState.Stance, MotionCommand.Ready));

            IsMoving = false;
            ResetStuckTracking();
            NextMoveTime = Timers.RunningTime + 1.0f;

            ResetAttack();

            FindNextTarget();
        }

        public void ForceHome()
        {
            var homePos = GetPosition(PositionType.Home);

            if (DebugMove)
                Console.WriteLine($"{Name} ({Guid}) - ForceHome({homePos.ToLOCString()})");

            var setPos = new SetPosition();
            setPos.Pos = new Physics.Common.Position(homePos);
            setPos.Flags = SetPositionFlags.Teleport;

            PhysicsObj.SetPosition(setPos);

            UpdatePosition_SyncLocation();

            SendUpdatePosition();

            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(0.5f);
            actionChain.AddAction(this, Sleep);
            actionChain.EnqueueChain();
        }
    }
}
