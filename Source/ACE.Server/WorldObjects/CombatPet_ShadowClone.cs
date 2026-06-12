using System;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public partial class CombatPet
    {
        private bool _isShadowClone;
        internal bool IsShadowClone => _isShadowClone;
        internal float ShadowCloneDamageScale { get; private set; } = 1.0f;

        public bool InitShadowClone(Player player, Creature target, float durationSeconds, float damageScale)
        {
            if (player?.Location == null || player.PhysicsObj == null)
                return false;

            _isShadowClone = true;
            ShadowCloneDamageScale = Math.Clamp(damageScale, 0.05f, 1.0f);

            Location = player.Location.InFrontOf(1.5f, false);
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
    }
}
