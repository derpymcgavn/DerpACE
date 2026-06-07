using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using ACE.Common;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Pathfinding;
using ACE.Server.Physics.Animation;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Pathfinding, wandering, idle emoting, route-following, and passage-granting
    /// behavior ported from ClassicACE. Always-on in DerpACE (no WorldRuleset gate).
    /// </summary>
    partial class Creature
    {
        // ===== Pathfinding state =====
        public bool PathfindingEnabled = false;

        /// <summary>
        /// Hook called from EnterWorld override below to initialize pathfinding state.
        /// </summary>
        public override bool EnterWorld()
        {
            var result = base.EnterWorld();
            if (result)
            {
                PathfindingEnabled = Pathfinder.PathfindingEnabled;
                if (PathfindingEnabled && Location != null)
                    Pathfinder.TryLoadMesh(Location);
            }
            return result;
        }

        // ===== Failure counters =====
        public int FailedMovementCount;
        public const int FailedMovementThreshold = 3;
        public const int FailedRoutingThreshold = 3;
        public int FailedSightCount;
        public const int FailedSightThreshold = 10;
        public double NextFailureCountersDecayTime = 0;
        protected const double FailureCountersDecayInterval = 5.0;

        // ===== Combat tracking =====
        public bool IsAttacking = false;
        public bool PendingEndAttack = false;
        public MotionCommand CurrentAttackMotionCommand = MotionCommand.Invalid;
        public int AttacksReceivedWithoutBeingAbleToCounter = 0;
        public double NextNoCounterResetTime = double.MaxValue;
        public const double NoCounterInterval = 60;

        public void EndAttack(bool forced = true)
        {
            IsAttacking = false;
            PendingEndAttack = false;
            CurrentAttackMotionCommand = MotionCommand.Invalid;
        }

        // ===== Move-to-home state machine (parallel to existing MonsterState.Return) =====
        public bool IsMoveToHomePending = false;
        public bool IsMovingToHome = false;
        public bool PendingEndMoveToHome = false;

        // ===== Wandering =====
        protected Position WanderTarget = null;
        protected double LastWanderTime = 0;
        public bool IsWanderingPending = false;
        public bool IsWandering = false;
        protected const double MaxWanderFrequency = 5;
        protected const double WanderChance = 0.5;
        public bool PendingEndWandering = false;

        // ===== Idle emoting =====
        protected MotionCommand DesiredEmote = MotionCommand.Invalid;
        protected double LastEmoteTime = 0;
        protected double ExpectedEmoteEndTime = 0;
        public bool IsEmotePending = false;
        public bool IsEmoting = false;
        public bool PendingEndEmoting = false;
        protected const double MaxEmoteFrequency = 30;
        protected const double EmoteChance = 0.5;
        protected IEnumerable<MotionCommand> IdleMotionsList = null;

        // ===== Routing =====
        protected double LastRouteTime = 0;
        protected WorldObject RouteAttackTarget = null;
        protected Position RoutePositionTarget;
        protected List<Position> CurrentRoute;
        protected int CurrentRouteIndex;
        public bool IsRouting = false;
        protected bool LastRouteStartAttemptWasNullRoute = false;
        public bool IsRouteStartPending = false;
        public bool PendingEndRoute = false;
        public bool PendingRetryRoute = false;
        public bool PendingContinueRoute = false;
        protected const double MaxRouteFrequency = 2.5;
        protected const double IndoorRouteFrequency = 0.75;
        protected virtual double GetMaxRouteFrequency() => Location?.Indoors == true ? IndoorRouteFrequency : MaxRouteFrequency;

        // ===== Route patrol burst =====
        // To make routed movement look like scouting (rather than restarting on each
        // waypoint, which can produce a circling appearance), each route start commits
        // to a burst of 3-5 consecutive waypoints. After the burst is consumed the
        // route either continues with the next burst or ends naturally.
        protected const int RouteBurstMin = 3;
        protected const int RouteBurstMax = 5;
        protected int CurrentRouteBurstRemaining = 0;

        protected virtual int GetRouteBurstMin() => RouteBurstMin;
        protected virtual int GetRouteBurstMax() => RouteBurstMax;

        protected const float IndoorPathRunRateMultiplier = 1.35f;
        protected virtual float GetPathRunRate()
        {
            var runRate = RunRate;
            if (runRate <= 0.0f)
                runRate = GetRunRate();

            return Location?.Indoors == true ? runRate * IndoorPathRunRateMultiplier : runRate;
        }

        protected virtual float GetPathWalkRunThreshold() => Location?.Indoors == true ? 0.0f : 1.0f;
        protected virtual float GetPathAttackBuffer() => Location?.Indoors == true ? 0.75f : 0.25f;
        protected virtual bool IsPathAttackVisible(WorldObject target) => CurrentAttack == CombatType.Melee ? IsMeleeVisible(target) : IsDirectVisible(target);

        // ===== Passage granting =====
        protected double LastRequestPassageTime = 0;
        protected const double MaxRequestPassageFrequency = 5;
        protected Position GrantPassageTarget = null;
        protected double LastGrantPassageTime = 0;
        public bool IsGrantPassagePending = false;
        public bool IsGrantingPassage = false;
        protected const double MaxGrantPassageFrequency = 5;
        public bool PendingEndGrantPassage = false;
        public bool AwakeJustToGrantPassage = false;

        // ===== Other helpers =====
        protected Position LastPathMoveTarget = null;
        protected double NextScoutRoamTime = 0;
        protected const double ScoutRoamIntervalMin = 0.6;
        protected const double ScoutRoamIntervalMax = 1.4;
        protected const float ScoutRoamRadiusMin = 10.0f;
        protected const float ScoutRoamRadiusMax = 32.0f;

        // ===== Scout patrol (multi-waypoint roam) =====
        // Instead of picking one random destination per tick (which produces an
        // orbit-like wander), each scout plans a short 3-5 waypoint patrol that
        // generally continues in one direction with a little jitter, walks the
        // waypoints in order, then takes a short rest before planning the next.
        protected const int ScoutPatrolMinWaypoints = 3;
        protected const int ScoutPatrolMaxWaypoints = 5;
        protected const float ScoutPatrolHeadingJitterRad = 0.6f; // ~34 degrees
        protected const double ScoutPatrolRestMin = 1.5;
        protected const double ScoutPatrolRestMax = 4.0;

        private readonly Queue<Position> _scoutPatrolQueue = new Queue<Position>();
        private Position _scoutPatrolCurrent = null;

        /// <summary>
        /// Scout mobs continuously roam while idle. They plan a short 3-5 waypoint
        /// patrol biased to continue along a heading (with light jitter), then walk
        /// it in order so they look like they are scouting an area instead of
        /// circling around their spawn.
        /// </summary>
        public void TryScoutHeartbeatRoam(double currentUnixTime)
        {
            if (!IsScoutMob || IsDead || Location == null)
                return;

            if (AttackTarget != null || MonsterState == State.Return)
            {
                // Combat / returning to home preempts patrols.
                if (_scoutPatrolQueue.Count > 0) _scoutPatrolQueue.Clear();
                _scoutPatrolCurrent = null;
                return;
            }

            if (IsMoving || IsTurning || HasPendingMovement || IsWandering || IsRouting || IsGrantingPassage || IsMovingToHome)
                return;

            if (currentUnixTime < NextScoutRoamTime)
                return;

            if (MoveSpeed == 0.0f)
                GetMovementSpeed();

            // Step 1: if we still have patrol waypoints, walk the next one.
            if (_scoutPatrolQueue.Count > 0)
            {
                _scoutPatrolCurrent = _scoutPatrolQueue.Dequeue();
                NextScoutRoamTime = currentUnixTime + ThreadSafeRandom.Next((float)ScoutRoamIntervalMin, (float)ScoutRoamIntervalMax);
                MoveAlongPath(_scoutPatrolCurrent);
                return;
            }

            // Step 2: no patrol queued - rest a beat, then plan a new one.
            _scoutPatrolCurrent = null;
            PlanScoutPatrol();

            // After planning, wait the patrol-rest interval before stepping off so the
            // mob visibly pauses at the end of one patrol before starting the next.
            NextScoutRoamTime = currentUnixTime + ThreadSafeRandom.Next((float)ScoutPatrolRestMin, (float)ScoutPatrolRestMax);
        }

        /// <summary>
        /// Build a 3-5 waypoint patrol starting from the creature's current location.
        /// Each waypoint extends the previous heading with a small random jitter and
        /// random step length, and must stay within the current landblock.
        /// </summary>
        private void PlanScoutPatrol()
        {
            _scoutPatrolQueue.Clear();
            if (Location == null)
                return;

            var baseLandblock = Location.Cell & 0xFFFF0000;
            var waypointCount = ThreadSafeRandom.Next(ScoutPatrolMinWaypoints, ScoutPatrolMaxWaypoints);

            // Start with a random base heading; subsequent waypoints rotate this by
            // a small amount each step so the patrol curves smoothly.
            var heading = (float)ThreadSafeRandom.Next(0f, (float)(Math.PI * 2));

            var cursor = new Position(Location);
            for (var step = 0; step < waypointCount; step++)
            {
                heading += (float)ThreadSafeRandom.Next(-ScoutPatrolHeadingJitterRad, ScoutPatrolHeadingJitterRad);

                Position next = null;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var radius = (float)ThreadSafeRandom.Next(ScoutRoamRadiusMin, ScoutRoamRadiusMax);
                    var candidate = new Position(cursor)
                    {
                        PositionX = cursor.PositionX + (float)Math.Cos(heading) * radius,
                        PositionY = cursor.PositionY + (float)Math.Sin(heading) * radius,
                    };

                    if ((candidate.Cell & 0xFFFF0000) != baseLandblock)
                    {
                        // Reflect the heading away from the landblock edge and try again.
                        heading += (float)Math.PI * 0.5f;
                        continue;
                    }

                    next = candidate;
                    break;
                }

                if (next == null)
                    break;

                _scoutPatrolQueue.Enqueue(next);
                cursor = next;
            }
        }

        // ===== Pending state checks =====
        public bool HasPendingMovement
        {
            get
            {
                if (PhysicsObj?.MovementManager?.MoveToManager == null)
                    return false;
                return PhysicsObj.MovementManager.MoveToManager.PendingActions.Count > 0
                    || IsMoving
                    || IsTurning;
            }
        }

        public bool HasPendingAnimations
        {
            get
            {
                return IsAttacking || IsEmoting || IsEmotePending;
            }
        }

        // ===== StartMovement alias =====
        /// <summary>
        /// Combined turn/move (alias for StartTurn).
        /// </summary>
        public void StartMovement()
        {
            StartTurn();
        }

        // ===== CancelMoveTo overload with WeenieError =====
        public void CancelMoveTo(WeenieError error)
        {
            if (PhysicsObj?.MovementManager?.MoveToManager == null)
                return;

            PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(error);
            PhysicsObj.MovementManager.MoveToManager.FailProgressCount = 0;
            EnqueueBroadcastMotion(new Motion(CurrentMotionState.Stance, MotionCommand.Ready));
            IsMoving = false;
        }

        // ===== Movement lifecycle hooks =====
        public void OnMovementStarted(bool isTurn = false)
        {
            // No-op currently. Could track movement start time, etc.
        }

        public void OnMovementStopped()
        {
            // Decay failure counts when movement explicitly stopped
            if (PhysicsObj?.MovementManager?.MoveToManager != null
                && PhysicsObj.MovementManager.MoveToManager.FailProgressCount > 0)
            {
                FailedMovementCount++;
            }
        }

        // ===== UpdateMovementSpeed alias =====
        public void UpdateMovementSpeed()
        {
            GetMovementSpeed();
        }

        // ===== TryMoveToHome - new entrypoint that cancels pending pathfinding states =====
        public void TryMoveToHome()
        {
            // Cancel pending pathfinding
            IsEmotePending = false;
            IsWanderingPending = false;
            IsRouteStartPending = false;
            if (IsEmoting) PendingEndEmoting = true;
            if (IsWandering) PendingEndWandering = true;
            if (IsRouting) PendingEndRoute = true;
            if (IsGrantingPassage) PendingEndGrantPassage = true;

            IsMoveToHomePending = true;

            // Use existing MoveToHome implementation
            MoveToHome();
            IsMoveToHomePending = false;
            IsMovingToHome = true;
        }

        public void EndMoveToHome(bool forced = true)
        {
            IsMoveToHomePending = false;
            IsMovingToHome = false;
            PendingEndMoveToHome = false;
        }

        // ===== FindNewHome =====
        /// <summary>
        /// Pick a new home position offset by random direction/distance from current location.
        /// </summary>
        public void FindNewHome(float directionMinAngle, float directionMaxAngle, float distance)
        {
            if (Location == null) return;

            var angleDeg = (float)ThreadSafeRandom.Next(directionMinAngle, directionMaxAngle);
            var angleRad = angleDeg * (float)Math.PI / 180f;
            var dir = Vector3.Transform(Location.GetCurrentDir(), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angleRad));
            var newPos = new Position(Location);
            newPos.PositionX += dir.X * distance;
            newPos.PositionY += dir.Y * distance;

            SetPosition(PositionType.Home, newPos);
        }

        // ===== Wandering =====
        /// <summary>
        /// Try to start wandering: pick a random destination (using pathfinder if indoors, else random offset).
        /// </summary>
        public void TryWandering(float radiusMin = 5f, float radiusMax = 15f, float chance = 1.0f)
        {
            if (IsWandering || IsWanderingPending) return;
            if (Time.GetUnixTime() - LastWanderTime < MaxWanderFrequency) return;
            if (ThreadSafeRandom.Next(0f, 1f) > chance) return;

            var radius = (float)ThreadSafeRandom.Next(radiusMin, radiusMax);

            if (PathfindingEnabled && Location != null)
            {
                var agentWidthW = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
                WanderTarget = Pathfinder.GetRandomPointWithinCircle(Location, radius, agentWidthW);
            }
            else if (Location != null)
            {
                var angle = (float)ThreadSafeRandom.Next(0f, (float)(Math.PI * 2));
                var offset = new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
                WanderTarget = new Position(Location);
                WanderTarget.PositionX += offset.X;
                WanderTarget.PositionY += offset.Y;
            }

            if (WanderTarget == null) return;
            IsWanderingPending = true;
        }

        public void Wander()
        {
            IsWanderingPending = false;
            if (WanderTarget == null) return;

            IsWandering = true;
            LastWanderTime = Time.GetUnixTime();
            MoveAlongPath(WanderTarget);
        }

        public void EndWandering(bool forced = true)
        {
            IsWandering = false;
            IsWanderingPending = false;
            PendingEndWandering = false;
            WanderTarget = null;
            if (forced && PhysicsObj?.MovementManager?.MoveToManager != null)
            {
                PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ActionCancelled);
                IsMoving = false;
            }
        }

        // ===== Idle emoting =====
        public void BuildIdleMotionsList()
        {
            IdleMotionsList = new List<MotionCommand>();
            if (MotionTableId == 0) return;

            try
            {
                // Use a curated list of common idle emotes that most monsters have
                var commonIdle = new[]
                {
                    MotionCommand.YawnStretch,
                    MotionCommand.Cheer,
                    MotionCommand.ClapHands,
                    MotionCommand.Laugh,
                    MotionCommand.Nod,
                    MotionCommand.BowDeep,
                    MotionCommand.Wave,
                    MotionCommand.HeartyLaugh,
                };
                IdleMotionsList = commonIdle.ToList();
            }
            catch
            {
                IdleMotionsList = new List<MotionCommand>();
            }
        }

        public void TryEmoting(MotionCommand motion = MotionCommand.Invalid)
        {
            if (IsEmoting || IsEmotePending) return;
            if (Time.GetUnixTime() - LastEmoteTime < MaxEmoteFrequency) return;
            if (ThreadSafeRandom.Next(0f, 1f) > EmoteChance) return;

            if (IdleMotionsList == null)
                BuildIdleMotionsList();

            if (motion == MotionCommand.Invalid)
            {
                var list = IdleMotionsList?.ToList();
                if (list == null || list.Count == 0) return;
                motion = list[ThreadSafeRandom.Next(0, list.Count - 1)];
            }

            DesiredEmote = motion;
            IsEmotePending = true;
        }

        public void Emote()
        {
            IsEmotePending = false;
            if (DesiredEmote == MotionCommand.Invalid) return;

            IsEmoting = true;
            LastEmoteTime = Time.GetUnixTime();
            ExpectedEmoteEndTime = Time.GetUnixTime() + 5.0; // safe upper bound

            var motion = new Motion(this, DesiredEmote);
            EnqueueBroadcastMotion(motion);
        }

        public void EndEmoting(bool forced = true)
        {
            IsEmoting = false;
            IsEmotePending = false;
            PendingEndEmoting = false;
            DesiredEmote = MotionCommand.Invalid;
        }

        // ===== Routing =====
        /// <summary>
        /// Try to compute a route to the current attack target (or the supplied route).
        /// </summary>
        public void TryRoute(List<Position> route = null)
        {
            if (!PathfindingEnabled) return;
            if (IsRouting || IsRouteStartPending) return;
            if (Time.GetUnixTime() - LastRouteTime < GetMaxRouteFrequency()) return;
            if (Location == null) return;

            if (route != null)
            {
                LastRouteStartAttemptWasNullRoute = false;
                CurrentRoute = route;
                CurrentRouteIndex = 0;
                IsRouteStartPending = true;
                return;
            }

            if (AttackTarget?.Location == null) return;

            // Allow cross-landblock routes outdoors only.
            var sameLandblock = (Location.Cell & 0xFFFF0000) == (AttackTarget.Location.Cell & 0xFFFF0000);
            if (!sameLandblock && (Location.Indoors || AttackTarget.Location.Indoors))
                return;

            var agentWidth = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
            var newRoute = Pathfinder.FindRoute(Location, AttackTarget.Location, agentWidth);
            if (newRoute == null || newRoute.Count == 0)
            {
                LastRouteStartAttemptWasNullRoute = true;
                LastRouteTime = Time.GetUnixTime();
                return;
            }

            LastRouteStartAttemptWasNullRoute = false;
            RouteAttackTarget = AttackTarget;
            RoutePositionTarget = AttackTarget.Location;
            CurrentRoute = newRoute;
            CurrentRouteIndex = 0;
            IsRouteStartPending = true;
        }

        protected bool CanAttemptRouteAfterNullRoute(double currentUnixTime)
        {
            if (!LastRouteStartAttemptWasNullRoute)
                return true;

            if (currentUnixTime - LastRouteTime < GetMaxRouteFrequency() * 2)
                return false;

            LastRouteStartAttemptWasNullRoute = false;
            return true;
        }

        public void StartRoute()
        {
            IsRouteStartPending = false;
            if (CurrentRoute == null || CurrentRoute.Count == 0) return;

            IsRouting = true;
            LastRouteTime = Time.GetUnixTime();
            CurrentRouteIndex = 0;
            CurrentRouteBurstRemaining = ThreadSafeRandom.Next(GetRouteBurstMin(), GetRouteBurstMax());
            ContinueRoute();
        }

        public void ContinueRoute(bool retry = false)
        {
            PendingContinueRoute = false;
            PendingRetryRoute = false;

            if (TryAbortPathForAttack())
                return;

            if (CurrentRoute == null || CurrentRouteIndex >= CurrentRoute.Count)
            {
                EndRoute();
                return;
            }

            if (!retry)
            {
                // Refill the scouting burst whenever it runs out so the creature
                // re-commits to another 3-5 waypoint segment of the route.
                if (CurrentRouteBurstRemaining <= 0)
                    CurrentRouteBurstRemaining = ThreadSafeRandom.Next(GetRouteBurstMin(), GetRouteBurstMax());

                // Follow route entries in order and skip tiny/no-op hops.
                var nextWaypoint = CurrentRoute[CurrentRouteIndex];
                CurrentRouteIndex++;

                while (CurrentRouteIndex < CurrentRoute.Count && Location != null && Location.DistanceTo(nextWaypoint) <= 2.0f)
                {
                    nextWaypoint = CurrentRoute[CurrentRouteIndex];
                    CurrentRouteIndex++;
                }

                RoutePositionTarget = nextWaypoint;
                CurrentRouteBurstRemaining--;
            }

            if (RoutePositionTarget == null)
            {
                EndRoute();
                return;
            }

            LastPathMoveTarget = RoutePositionTarget;
            MoveAlongPath(RoutePositionTarget);
        }

        protected bool TryAbortPathForAttack()
        {
            if (!IsAwake || AttackTarget == null || Location == null)
                return false;

            if (CurrentAttack == null)
            {
                CurrentAttack = GetNextAttackType();
                MaxRange = GetMaxRange();
            }

            var attackBuffer = GetPathAttackBuffer();
            if (GetDistanceToTarget() > MaxRange + attackBuffer)
                return false;

            if (!IsPathAttackVisible(AttackTarget))
                return false;

            FailedSightCount = 0;
            FailedMovementCount = 0;

            if (IsRouting || IsRouteStartPending)
                EndRoute();
            if (IsWandering || IsWanderingPending)
                EndWandering();
            if (IsGrantingPassage || IsGrantPassagePending)
                EndGrantPassage();

            NextMoveTime = Timers.RunningTime;
            NextAttackTime = Math.Min(NextAttackTime, Timers.RunningTime);
            return true;
        }

        public void RetryRoute()
        {
            PendingRetryRoute = false;

            // -- Ported from ClassicACE CustomDM --
            // Rewind to the previous significant waypoint and try to step around the
            // nearest wall geometry instead of re-issuing the same failed MoveTo.
            if (CurrentRoute != null && CurrentRoute.Count > 0 && Location != null)
            {
                Position retryPos = RoutePositionTarget;
                for (var retryIndex = Math.Max(CurrentRouteIndex - 1, 0); retryIndex > 0; retryIndex--)
                {
                    retryPos = CurrentRoute[retryIndex];
                    if (Location.DistanceTo(retryPos) > 2.0f)
                        break;
                }

                if (retryPos != null)
                {
                    var nearbyWallPos = Pathfinder.GetNearestWallPosition(retryPos, 1.0f, AgentWidth.Narrow, out _, false);
                    if (nearbyWallPos != null)
                    {
                        var wallAvoidingPos = nearbyWallPos.InFrontOf(1.2f);
                        if (HasPendingMovement && PhysicsObj?.MovementManager?.MoveToManager != null)
                            PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ObjectGone);
                        FailedSightCount = 0;

                        LastPathMoveTarget = wallAvoidingPos;
                        MoveAlongPath(wallAvoidingPos);
                        return;
                    }
                }
            }

            ContinueRoute(retry: true);
        }

        public void EndRoute(bool forced = true)
        {
            IsRouting = false;
            IsRouteStartPending = false;
            PendingEndRoute = false;
            PendingContinueRoute = false;
            PendingRetryRoute = false;
            CurrentRoute = null;
            CurrentRouteIndex = 0;
            CurrentRouteBurstRemaining = 0;
            RouteAttackTarget = null;

            if (forced && PhysicsObj?.MovementManager?.MoveToManager != null)
            {
                PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ActionCancelled);
                IsMoving = false;
            }
        }

        // ===== Passage granting =====
        /// <summary>
        /// Request that another monster grant passage so this creature can move through.
        /// </summary>
        public void TryRequestPassage(Creature target)
        {
            if (target == null) return;
            if (Time.GetUnixTime() - LastRequestPassageTime < MaxRequestPassageFrequency) return;
            LastRequestPassageTime = Time.GetUnixTime();
            target.TryGrantPassage(this);
        }

        public bool TryGrantPassage(Creature requester)
        {
            if (requester?.Location == null || Location == null)
                return false;

            if (IsGrantingPassage || IsGrantPassagePending || IsAttacking)
                return false;

            if (Time.GetUnixTime() - LastGrantPassageTime < MaxGrantPassageFrequency)
                return false;

            if (AttackTarget != null && CurrentAttack != null && TryAbortPathForAttack())
                return false;

            // Pick a sidestep position perpendicular to requester's heading
            var dir = Location.Pos - requester.Location.Pos;
            if (dir.LengthSquared() < 0.01f)
                return false;

            var perp = Vector3.Normalize(new Vector3(-dir.Y, dir.X, 0)) * 2.0f;
            GrantPassageTarget = new Position(Location);
            GrantPassageTarget.PositionX += perp.X;
            GrantPassageTarget.PositionY += perp.Y;

            if (!IsValidPathPosition(GrantPassageTarget) || (GrantPassageTarget.Cell & 0xFFFF0000) != (Location.Cell & 0xFFFF0000))
            {
                GrantPassageTarget = null;
                return false;
            }

            IsGrantPassagePending = true;
            return true;
        }

        public void GrantPassage()
        {
            IsGrantPassagePending = false;
            if (GrantPassageTarget == null) return;

            IsGrantingPassage = true;
            LastGrantPassageTime = Time.GetUnixTime();
            MoveAlongPath(GrantPassageTarget);
        }

        private void MoveAlongPath(Position target)
        {
            if (!IsValidPathPosition(target))
            {
                log.Warn($"{Name} ({Guid}) ignored invalid path target: {(target == null ? "null" : target.ToLOCString())}");
                LastPathMoveTarget = null;
                if (IsRouting || IsRouteStartPending)
                    EndRoute();
                if (IsGrantingPassage || IsGrantPassagePending)
                    EndGrantPassage();
                IsWandering = false;
                return;
            }

            LastPathMoveTarget = target;
            MoveTo(target, GetPathRunRate(), true, GetPathWalkRunThreshold());
            StartMove();
        }

        private static bool IsValidPathPosition(Position target)
        {
            if (target == null)
                return false;

            return !float.IsNaN(target.PositionX) && !float.IsNaN(target.PositionY) && !float.IsNaN(target.PositionZ)
                && !float.IsInfinity(target.PositionX) && !float.IsInfinity(target.PositionY) && !float.IsInfinity(target.PositionZ);
        }

        public void EndGrantPassage(bool forced = true)
        {
            IsGrantingPassage = false;
            IsGrantPassagePending = false;
            PendingEndGrantPassage = false;
            GrantPassageTarget = null;
            AwakeJustToGrantPassage = false;
        }

        // ===== Tick dispatch - called from Monster_Tick.Monster_Tick =====
        /// <summary>
        /// Per-tick dispatcher for pending pathfinding state transitions.
        /// Returns true if pathfinding is "owning" this tick (skip normal movement).
        /// </summary>
        public bool TickPathfinding(double currentUnixTime)
        {
            if (!PathfindingEnabled)
                return false;

            // Decay failure counters periodically
            if (NextFailureCountersDecayTime <= currentUnixTime)
            {
                NextFailureCountersDecayTime = currentUnixTime + FailureCountersDecayInterval;
                if (FailedMovementCount > 0) FailedMovementCount--;
                if (FailedSightCount > 0) FailedSightCount--;
            }

            // -- Ported from ClassicACE CustomDM --
            // If we regain sight/range of the target while emoting/wandering/routing, abort
            // the path action immediately so combat can resume this tick. Without this, mobs
            // appear "stuck" while finishing a wander/route the player is already standing in.
            if ((IsRouting || IsRouteStartPending || IsWandering || IsWanderingPending || IsGrantingPassage || IsGrantPassagePending)
                && TryAbortPathForAttack())
                return false;

            if (IsAwake && AttackTarget != null && (IsEmoting || IsEmotePending
                || IsWandering || IsWanderingPending
                || IsRouting || IsRouteStartPending))
            {
                var isMelee = CurrentAttack == CombatType.Melee;
                var inSight = isMelee ? IsMeleeVisible(AttackTarget) : IsDirectVisible(AttackTarget);
                if (inSight)
                {
                    FailedSightCount = 0;

                    IsEmotePending = false;
                    IsWanderingPending = false;
                    IsRouteStartPending = false;

                    if (IsWandering) PendingEndWandering = true;
                    if (IsRouting) PendingEndRoute = true;
                    if (IsEmoting) PendingEndEmoting = true;
                }
            }

            // Reset attack-counter-without-counter if no attacks for a while
            if (currentUnixTime > NextNoCounterResetTime)
            {
                AttacksReceivedWithoutBeingAbleToCounter = 0;
                NextNoCounterResetTime = double.MaxValue;
            }

            // Dispatch pending state transitions
            if (PendingEndEmoting) EndEmoting();
            if (PendingEndWandering) EndWandering();
            if (PendingEndRoute) EndRoute();
            if (PendingEndGrantPassage) EndGrantPassage();
            if (PendingEndMoveToHome) EndMoveToHome();

            if (IsEmotePending) Emote();
            if (IsWanderingPending) Wander();
            if (IsRouteStartPending) StartRoute();
            if (IsGrantPassagePending) GrantPassage();
            if (PendingContinueRoute) ContinueRoute();
            if (PendingRetryRoute) RetryRoute();

            // Failsafe: never hold routing forever if no move is pending.
            if (IsRouting && !HasPendingMovement && !PendingContinueRoute && !PendingRetryRoute && !IsRouteStartPending)
                PendingEndRoute = true;

            // Failsafe: wandering / grant-passage / move-to-home don't have explicit completion
            // callbacks from the physics MoveTo. If no move is pending and the creature isn't
            // actively turning/moving, treat the action as complete so the AI can resume.
            if (IsWandering && !IsWanderingPending && !HasPendingMovement && !IsMoving && !IsTurning)
                PendingEndWandering = true;

            if (IsGrantingPassage && !IsGrantPassagePending && !HasPendingMovement && !IsMoving && !IsTurning)
                PendingEndGrantPassage = true;

            if (IsMovingToHome && !IsMoveToHomePending && !HasPendingMovement && !IsMoving && !IsTurning)
                PendingEndMoveToHome = true;

            // Detect emote completion
            if (IsEmoting && currentUnixTime >= ExpectedEmoteEndTime)
                EndEmoting();

            // If actively routing/wandering/emoting/granting passage, this tick is owned
            return IsRouting || IsWandering || IsEmoting || IsGrantingPassage || IsMovingToHome;
        }
    }
}
