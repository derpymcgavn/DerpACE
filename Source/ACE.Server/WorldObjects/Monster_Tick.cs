using System;
using System.Diagnostics;

using ACE.Entity.Enum;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        protected const double monsterTickInterval = 0.2;

        public double NextMonsterTickTime;

        private bool firstUpdate = true;

        /// <summary>
        /// Primary dispatch for monster think
        /// </summary>
        public void Monster_Tick(double currentUnixTime)
        {
            if (IsChessPiece && this is GamePiece gamePiece)
            {
                // faster than vtable?
                gamePiece.Tick(currentUnixTime);
                return;
            }

            if (IsPassivePet && this is Pet pet)
            {
                pet.Tick(currentUnixTime);
                return;
            }

            NextMonsterTickTime = currentUnixTime + monsterTickInterval;

            // Pathfinding tick: dispatches pending wander/route/emote/passage state transitions.
            // Returns true if a pathfinding action is currently in progress (skip normal movement this tick).
            if (TickPathfinding(currentUnixTime))
                return;

            if (!IsAwake)
            {
                if (IsScoutMob)
                    TryScoutHeartbeatRoam(currentUnixTime);

                if (MonsterState == State.Return)
                    MonsterState = State.Idle;

                if (IsFactionMob || HasFoeType)
                    FactionMob_CheckMonsters();

                return;
            }

            if (IsDead) return;

            if (EmoteManager.IsBusy) return;

            HandleFindTarget();

            CheckMissHome();    // tickrate?

            if (AttackTarget == null && MonsterState != State.Return)
            {
                Sleep();
                return;
            }

            if (MonsterState == State.Return)
            {
                Movement();
                return;
            }

            var combatPet = this as CombatPet;

            var creatureTarget = AttackTarget as Creature;

            if (creatureTarget != null && (creatureTarget.IsDead || (combatPet == null && !IsVisibleTarget(creatureTarget))))
            {
                FindNextTarget();
                return;
            }

            if (firstUpdate)
            {
                if (CurrentMotionState.Stance == MotionStance.NonCombat)
                    DoAttackStance();

                if (IsAnimating)
                {
                    //PhysicsObj.ShowPendingMotions();
                    PhysicsObj.update_object();
                    return;
                }

                firstUpdate = false;
            }

            // -- Ported from ClassicACE CustomDM --
            // If awake and the target has fled past MaxChaseRange, give up the chase
            // and re-acquire instead of sticking to a target we'll never catch.
            if (MonsterState == State.Awake && AttackTarget != null
                && GetDistanceToTarget() >= MaxChaseRange)
            {
                if (HasPendingMovement)
                    CancelMoveTo(WeenieError.ObjectGone);
                FindNextTarget();
                return;
            }

            // select a new weapon if missile launcher is out of ammo
            var weapon = GetEquippedWeapon();
            /*if (weapon != null && weapon.IsAmmoLauncher)
            {
                var ammo = GetEquippedAmmo();
                if (ammo == null)
                    SwitchToMeleeAttack();
            }*/

            if (weapon == null && CurrentAttack != null && CurrentAttack == CombatType.Missile)
            {
                EquipInventoryItems(true, false, true, false);
                DoAttackStance();
                CurrentAttack = null;
            }

            // decide current type of attack
            if (CurrentAttack == null)
            {
                CurrentAttack = GetNextAttackType();
                MaxRange = GetMaxRange();

                //if (CurrentAttack == AttackType.Magic)
                //MaxRange = MaxMeleeRange;   // FIXME: server position sync
            }

            if (PhysicsObj.IsSticky)
                UpdatePosition(false);

            // get distance to target
            var targetDist = GetDistanceToTarget();
            //Console.WriteLine($"{Name} ({Guid}) - Dist: {targetDist}");

            // -- Ported from ClassicACE CustomDM ruleset (Monster_Tick.cs) --
            // Unified movement/attack logic so missile mobs also reposition,
            // switch weapons, emote, wander, and route on threshold failures.
            if (CurrentAttack != null)
            {
                var isMeleeVisible = IsMeleeVisible(AttackTarget);
                var isDirectVisible = IsDirectVisible(AttackTarget);
                var canStick = PhysicsObj.IsSticky && CurrentAttack == CombatType.Melee && isMeleeVisible;
                var aiImmobile = AiImmobile;

                // -- Ported from ClassicACE CustomDM --
                // If we lost sight of the target while not in any recovery state, accumulate
                // FailedSightCount so the threshold logic below can trigger weapon-swap /
                // emote / wander / route recovery instead of running blind forever.
                var isInSight = CurrentAttack == CombatType.Melee ? isMeleeVisible : isDirectVisible;
                if (!isInSight && !IsRouting && !IsWandering && !IsEmoting && !SwitchWeaponsPending && !IsAttacking)
                {
                    FailedSightCount++;
                    if (FailedSightCount >= FailedSightThreshold && HasPendingMovement)
                        CancelMoveTo(WeenieError.ObjectGone);
                }
                else if (isInSight)
                {
                    FailedSightCount = 0;
                }

                if ((!canStick && targetDist > MaxRange) || (!IsFacing(AttackTarget) && !IsSelfCast()))
                {
                    bool failedThresholds = FailedMovementCount >= FailedMovementThreshold || FailedSightCount >= FailedSightThreshold;

                    if (!IsTurning && !IsMoving && !failedThresholds && !aiImmobile)
                    {
                        StartTurn();
                    }
                    else
                    {
                        if (failedThresholds)
                        {
                            // Reset and try to recover with another target / weapon swap / emote / wander / route
                            FailedMovementCount = 0;
                            FailedSightCount = 0;

                            var currentTarget = AttackTarget;
                            FindNextTarget();

                            if (currentTarget == AttackTarget)
                            {
                                if (HasRangedWeapon && CurrentAttack == CombatType.Melee
                                    && !SwitchWeaponsPending && LastWeaponSwitchTime + 5 < currentUnixTime
                                    && isDirectVisible)
                                {
                                    TrySwitchToMissileAttack();
                                }
                                else
                                {
                                    if (LastEmoteTime + MaxEmoteFrequency < currentUnixTime
                                        && EmoteChance > ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f))
                                    {
                                        TryEmoting();
                                    }

                                    if (LastWanderTime + MaxWanderFrequency < currentUnixTime
                                        && WanderChance > ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f))
                                    {
                                        if (PathfindingEnabled && Location != null && Location.Indoors && !LastRouteStartAttemptWasNullRoute)
                                            TryWandering(160, 200, 5);
                                        else
                                            TryWandering(100, 260, 7);
                                    }

                                    if (PathfindingEnabled && Location != null && Location.Indoors)
                                        TryRoute();
                                }
                            }
                        }
                        else if (HasRangedWeapon && CurrentAttack == CombatType.Melee && targetDist > 20
                                 && !SwitchWeaponsPending && LastWeaponSwitchTime + 5 < currentUnixTime
                                 && isDirectVisible)
                        {
                            TrySwitchToMissileAttack();
                        }
                        else
                        {
                            Movement();
                        }
                    }
                }
                else
                {
                    // -- Ported from ClassicACE CustomDM --
                    // Missile mob entered the "in-range-to-stop" band (80% of MaxRange):
                    // cancel any chase MoveTo so we plant and fire instead of running into melee.
                    var inRangeToStop = targetDist < MaxRange * 0.8f;
                    if (CurrentAttack == CombatType.Missile && inRangeToStop && IsMoving && !IsTurning && !IsWandering && HasPendingMovement)
                        CancelMoveTo(WeenieError.ObjectGone);

                    // In range and facing: attack. Missile mobs at extreme range still try to switch back to melee.
                    if (CurrentAttack == CombatType.Missile && targetDist > MaxRange)
                    {
                        // should ranged mobs only get CurrentTargets within MaxRange?
                        TrySwitchToMeleeAttack();
                    }
                    else if (AttackReady())
                    {
                        Attack();
                    }
                }
            }

            // pets drawing aggro
            if (combatPet != null)
                combatPet.PetCheckMonsters();
        }
    }
}
