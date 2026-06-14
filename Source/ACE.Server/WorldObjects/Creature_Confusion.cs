using System;

using ACE.Entity.Enum;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        public DateTime VoidConfusionUntil { get; private set; } = DateTime.MinValue;
        public uint VoidConfusionTargetGuid { get; private set; }

        public bool IsVoidConfused => VoidConfusionUntil > DateTime.UtcNow;

        public void ApplyVoidConfusion(Creature forcedTarget, double durationSeconds)
        {
            if (forcedTarget == null || forcedTarget == this || IsDead || forcedTarget.IsDead)
                return;

            VoidConfusionUntil = DateTime.UtcNow.AddSeconds(Math.Max(1.0, durationSeconds));
            VoidConfusionTargetGuid = forcedTarget.Guid.Full;
            AttackTarget = forcedTarget;
            CurrentAttack = null;

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

            if (AttackTarget is Creature target && target.IsAlive && target.Guid.Full == VoidConfusionTargetGuid)
                return true;

            ClearVoidConfusionIfCurrent();
            return false;
        }

        private void ClearVoidConfusionIfCurrent()
        {
            if (VoidConfusionTargetGuid == 0)
                return;

            if (AttackTarget is Creature target && target.Guid.Full == VoidConfusionTargetGuid)
                AttackTarget = null;

            VoidConfusionUntil = DateTime.MinValue;
            VoidConfusionTargetGuid = 0;
        }
    }
}
