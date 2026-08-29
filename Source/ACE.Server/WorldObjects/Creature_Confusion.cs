using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        private const float VoidConfusionAssistRadius = 12.0f;

        public DateTime VoidConfusionUntil { get; private set; } = DateTime.MinValue;
        public uint VoidConfusionTargetGuid { get; private set; }
        public uint VoidConfusionOwnerGuid { get; private set; }

        public bool IsVoidConfused => VoidConfusionUntil > DateTime.UtcNow;

        public void ApplyVoidConfusion(Player owner, Creature forcedTarget, double durationSeconds)
        {
            if (owner == null || forcedTarget == null || forcedTarget == this || IsDead || forcedTarget.IsDead)
                return;

            if (!owner.CanDamage(forcedTarget))
                return;

            VoidConfusionUntil = DateTime.UtcNow.AddSeconds(Math.Max(1.0, durationSeconds));
            VoidConfusionOwnerGuid = owner.Guid.Full;
            SetVoidConfusionTarget(forcedTarget);

            ApplyVisualEffects(PlayScript.BlackMadness);
            ApplyVisualEffects(PlayScript.SkillDownVoid);

            if (!IsAwake)
                WakeUp(false);
            else if (CurrentMotionState.Stance == MotionStance.NonCombat)
                DoAttackStance();
        }

        public bool MaintainVoidConfusion()
        {
            if (!IsVoidConfused)
            {
                ClearVoidConfusionIfCurrent();
                return false;
            }

            var owner = PlayerManager.GetOnlinePlayer(VoidConfusionOwnerGuid);
            if (owner == null || owner.IsDead || owner.Location == null)
            {
                ClearVoidConfusionIfCurrent();
                return false;
            }

            if (AttackTarget is Creature target
                && target.IsAlive
                && target.Guid.Full == VoidConfusionTargetGuid
                && IsValidVoidConfusionTarget(owner, target))
                return true;

            var nextTarget = PickVoidConfusionTarget(owner, new HashSet<uint> { Guid.Full });
            if (nextTarget != null)
            {
                SetVoidConfusionTarget(nextTarget);
                return true;
            }

            ClearVoidConfusionIfCurrent();
            return false;
        }

        private void SetVoidConfusionTarget(Creature target)
        {
            VoidConfusionTargetGuid = target.Guid.Full;
            AttackTarget = target;
            CurrentAttack = null;
        }

        private Creature PickVoidConfusionTarget(Player owner, HashSet<uint> excluded)
        {
            if (owner == null || Location == null)
                return null;

            excluded ??= new HashSet<uint>();
            excluded.Add(Guid.Full);

            var landblock = CurrentLandblock ?? owner.CurrentLandblock;
            if (landblock == null)
                return null;

            var radiusSq = VoidConfusionAssistRadius * VoidConfusionAssistRadius;
            var baseLandblock = Location.Cell & 0xFFFF0000;

            return landblock.GetAllWorldObjectsForDiagnostics()
                .OfType<Creature>()
                .Where(c => IsValidVoidConfusionTarget(owner, c)
                            && (c.Location.Cell & 0xFFFF0000) == baseLandblock
                            && Location.SquaredDistanceTo(c.Location) <= radiusSq
                            && !excluded.Contains(c.Guid.Full))
                .OrderBy(c => Location.SquaredDistanceTo(c.Location))
                .FirstOrDefault();
        }

        private bool IsValidVoidConfusionTarget(Player owner, Creature target)
        {
            if (owner == null || target == null || target == this || target.IsDead || target.Location == null)
                return false;

            if (!target.Attackable || !target.IsMonster || target.Teleporting)
                return false;

            if (target.IsVoidConfused && target.VoidConfusionOwnerGuid == owner.Guid.Full)
                return false;

            return owner.CanDamage(target);
        }

        private void ClearVoidConfusionIfCurrent()
        {
            if (VoidConfusionTargetGuid == 0 && VoidConfusionOwnerGuid == 0)
                return;

            if (AttackTarget is Creature target && target.Guid.Full == VoidConfusionTargetGuid)
                AttackTarget = null;

            VoidConfusionUntil = DateTime.MinValue;
            VoidConfusionTargetGuid = 0;
            VoidConfusionOwnerGuid = 0;
        }
    }
}