using System;
using System.Numerics;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Pathfinding;

namespace ACE.Server.WorldObjects
{
    public partial class CombatPet
    {
        private bool _isShadowClone;
        private const float ShadowCloneBackOffset = 2.25f;
        private const float ShadowCloneRightOffset = 1.25f;
        private const float ShadowCloneFormationTolerance = 1.0f;

        internal bool IsShadowClone => _isShadowClone;
        internal float ShadowCloneDamageScale { get; private set; } = 1.0f;

        public bool InitShadowClone(Player player, Creature target, float durationSeconds, float damageScale)
        {
            if (player?.Location == null || player.PhysicsObj == null)
                return false;

            _isShadowClone = true;
            ShadowCloneDamageScale = Math.Clamp(damageScale, 0.05f, 1.0f);

            Location = GetShadowCloneFormationPosition(player);
            Location.LandblockId = new LandblockId(Location.Cell);

            CopyShadowCloneFromPlayer(player);
            Name = $"{player.Name}'s Shadow";

            PetOwner = player.Guid.Full;
            P_PetOwner = player;
            NoCorpse = true;
            TimeToRot = -1;
            SuppressGenerateEffect = true;

            SetCombatMode(GetShadowCloneCombatMode(player));
            MonsterState = State.Awake;
            IsAwake = true;

            CopyShadowCloneRatingsFromPlayer(player);
            Faction1Bits = player.Faction1Bits;

            if (!EnterWorld())
                return false;

            player.SetActiveShadowCloneCasterPet(this);

            ApplyVisualEffects(PlayScript.SpecialStateBlack);
            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantUpPurple, 1.0f));

            if (target != null && target.IsAlive && target.Attackable && !SameFaction(target))
                SetAttackTargetFast(target);

            var expireChain = new ActionChain();
            expireChain.AddDelaySeconds(durationSeconds);
            expireChain.AddAction(this, () =>
            {
                if (IsDestroyed)
                    return;

                player.Session?.Network.EnqueueSend(new GameMessageSystemChat("Your shadow folds back into the void.", ChatMessageType.Magic));
                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantDownPurple, 1.0f));
                player.ClearActiveShadowCloneCasterPet(this);
                Destroy();
            });
            expireChain.EnqueueChain();

            return true;
        }

        private static CombatMode GetShadowCloneCombatMode(Player player)
        {
            if (player.CombatMode == CombatMode.Magic && player.GetEquippedWand() != null)
                return CombatMode.Magic;

            if (player.CombatMode == CombatMode.Missile && player.GetEquippedMissileWeapon() != null)
                return CombatMode.Missile;

            if (player.GetEquippedWand() != null)
                return CombatMode.Magic;

            if (player.GetEquippedMissileWeapon() != null)
                return CombatMode.Missile;

            return CombatMode.Melee;
        }

        private void CopyShadowCloneRatingsFromPlayer(Player player)
        {
            DamageRating = player.GetDamageRating();
            DamageResistRating = player.GetDamageResistRating();
            CritRating = player.GetCritRating();
            CritDamageRating = player.GetCritDamageRating();
            CritResistRating = player.GetCritResistRating();
            CritDamageResistRating = player.GetCritDamageResistRating();
            PKDamageRating = player.GetPKDamageRating();
            PKDamageResistRating = player.GetPKDamageResistRating();
            HealingBoostRating = player.GetHealingBoostRating();
        }

        private Position GetShadowCloneFormationPosition(Player player)
        {
            var ownerLocation = player?.Location;
            if (ownerLocation == null)
                return Location;

            var formation = new Position(ownerLocation);
            var forward = ownerLocation.GetCurrentDir();
            if (float.IsNaN(forward.X) || float.IsNaN(forward.Y) || forward.LengthSquared() <= 0.001f)
                forward = Vector3.UnitY;

            forward = Vector3.Normalize(new Vector3(forward.X, forward.Y, 0.0f));
            var right = new Vector3(forward.Y, -forward.X, 0.0f);

            formation.PositionX += (-forward.X * ShadowCloneBackOffset) + (right.X * ShadowCloneRightOffset);
            formation.PositionY += (-forward.Y * ShadowCloneBackOffset) + (right.Y * ShadowCloneRightOffset);
            formation.PositionZ += 0.05f;
            formation.LandblockId = new LandblockId(formation.GetCell());

            return formation;
        }

        internal bool ShouldReturnToShadowCloneFormation()
        {
            if (!_isShadowClone || P_PetOwner?.Location == null || Location == null)
                return false;

            var formation = GetShadowCloneFormationPosition(P_PetOwner);
            if (formation == null)
                return false;

            return Location.Distance2DSquared(formation) > ShadowCloneFormationTolerance * ShadowCloneFormationTolerance;
        }

        internal bool TryReturnToShadowCloneFormation()
        {
            if (!_isShadowClone || P_PetOwner?.Location == null || Location == null)
                return false;

            var formation = GetShadowCloneFormationPosition(P_PetOwner);
            if (formation == null)
                return false;

            AttackTarget = null;
            RouteAttackTarget = null;

            if (Location.Distance2DSquared(formation) <= ShadowCloneFormationTolerance * ShadowCloneFormationTolerance)
                return true;

            if (PathfindingEnabled && Location.Indoors)
            {
                var sameLandblock = (Location.Cell & 0xFFFF0000) == (formation.Cell & 0xFFFF0000);
                if (sameLandblock)
                {
                    RoutePositionTarget = formation;
                    var agentWidth = (PhysicsObj?.GetRadius() ?? 0.5f) > 0.7f ? AgentWidth.Wide : AgentWidth.Narrow;
                    TryRoute(Pathfinder.FindRoute(Location, formation, agentWidth));
                    if (IsRouteStartPending || IsRouting)
                    {
                        IsMoving = true;
                        LastMoveTime = Timers.RunningTime;
                        return true;
                    }
                }
            }

            RoutePositionTarget = null;
            MoveTo(formation, GetPathRunRate(), true, 0.0f);
            IsMoving = true;
            LastMoveTime = Timers.RunningTime;
            return true;
        }
    }
}
