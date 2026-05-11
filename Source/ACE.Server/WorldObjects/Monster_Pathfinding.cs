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
        protected const double MaxRouteFrequency = 5;

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

        /// <summary>
        /// Scout mobs continuously roam while idle, choosing random move targets
        /// within their current landblock.
        /// </summary>
        public void TryScoutHeartbeatRoam(double currentUnixTime)
        {
            if (!IsScoutMob || IsDead || Location == null)
                return;

            if (AttackTarget != null || MonsterState == State.Return)
                return;

            if (IsMoving || IsTurning || HasPendingMovement || IsWandering || IsRouting || IsGrantingPassage || IsMovingToHome)
                return;

            if (currentUnixTime < NextScoutRoamTime)
                return;

            if (MoveSpeed == 0.0f)
                GetMovementSpeed();

            var baseLandblock = Location.Cell & 0xFFFF0000;
            Position roamTarget = null;

            for (var i = 0; i < 6; i++)
            {
                var radius = (float)ThreadSafeRandom.Next(ScoutRoamRadiusMin, ScoutRoamRadiusMax);
                var angle = (float)ThreadSafeRandom.Next(0f, (float)(Math.PI * 2));
                var offset = new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);

                var candidate = new Position(Location)
                {
                    PositionX = Location.PositionX + offset.X,
                    PositionY = Location.PositionY + offset.Y,
                };

                if ((candidate.Cell & 0xFFFF0000) != baseLandblock)
                    continue;

                roamTarget = candidate;
                break;
            }

            NextScoutRoamTime = currentUnixTime + ThreadSafeRandom.Next((float)ScoutRoamIntervalMin, (float)ScoutRoamIntervalMax);

            if (roamTarget == null)
                return;

            MoveTo(roamTarget, RunRate, false, 1.0f);
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
            MoveTo(WanderTarget, RunRate, false, 1.0f);
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
            if (Time.GetUnixTime() - LastRouteTime < MaxRouteFrequency) return;
            if (Location == null) return;

            if (route != null)
            {
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

        public void StartRoute()
        {
            IsRouteStartPending = false;
            if (CurrentRoute == null || CurrentRoute.Count == 0) return;

            IsRouting = true;
            LastRouteTime = Time.GetUnixTime();
            CurrentRouteIndex = 0;
            ContinueRoute();
        }

        public void ContinueRoute(bool retry = false)
        {
            PendingContinueRoute = false;
            PendingRetryRoute = false;

            if (CurrentRoute == null || CurrentRouteIndex >= CurrentRoute.Count)
            {
                EndRoute();
                return;
            }

            if (!retry)
            {
                // Follow route entries in order and skip tiny/no-op hops.
                var nextWaypoint = CurrentRoute[CurrentRouteIndex];
                CurrentRouteIndex++;

                while (CurrentRouteIndex < CurrentRoute.Count && Location != null && Location.DistanceTo(nextWaypoint) <= 2.0f)
                {
                    nextWaypoint = CurrentRoute[CurrentRouteIndex];
                    CurrentRouteIndex++;
                }

                RoutePositionTarget = nextWaypoint;
            }

            if (RoutePositionTarget == null)
            {
                EndRoute();
                return;
            }

            LastPathMoveTarget = RoutePositionTarget;
            MoveTo(RoutePositionTarget, RunRate, false, 1.0f);
        }

        public void RetryRoute()
        {
            PendingRetryRoute = false;
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

        public void TryGrantPassage(Creature requester)
        {
            if (requester?.Location == null || Location == null) return;
            if (IsGrantingPassage || IsGrantPassagePending) return;
            if (Time.GetUnixTime() - LastGrantPassageTime < MaxGrantPassageFrequency) return;

            // Pick a sidestep position perpendicular to requester's heading
            var dir = Location.Pos - requester.Location.Pos;
            if (dir.LengthSquared() < 0.01f) return;
            var perp = Vector3.Normalize(new Vector3(-dir.Y, dir.X, 0)) * 2.0f;
            GrantPassageTarget = new Position(Location);
            GrantPassageTarget.PositionX += perp.X;
            GrantPassageTarget.PositionY += perp.Y;

            IsGrantPassagePending = true;
        }

        public void GrantPassage()
        {
            IsGrantPassagePending = false;
            if (GrantPassageTarget == null) return;

            IsGrantingPassage = true;
            LastGrantPassageTime = Time.GetUnixTime();
            MoveTo(GrantPassageTarget, RunRate, false, 1.0f);
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

            // Detect emote completion
            if (IsEmoting && currentUnixTime >= ExpectedEmoteEndTime)
                EndEmoting();

            // If actively routing/wandering/emoting/granting passage, this tick is owned
            return IsRouting || IsWandering || IsEmoting || IsGrantingPassage || IsMovingToHome;
        }
    }
}
