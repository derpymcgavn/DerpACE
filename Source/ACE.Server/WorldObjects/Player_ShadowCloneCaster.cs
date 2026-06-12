using System;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public partial class Player
    {
        public const int ShadowCloneCasterCooldownId = 2057;

        private static readonly uint[] ShadowClonePetShellWcids =
        {
            49000, // acid zombie combat pet (50)
            49016, // fire zombie combat pet (50)
            49023, // frost zombie combat pet (50)
            49009, // lightning zombie combat pet (50)
            49164, // acid skeleton minion combat pet (50)
        };

        private DateTime _shadowCloneCasterCooldownUntil = DateTime.MinValue;
        private CombatPet _activeShadowCloneCasterPet;

        public void TryProcShadowCloneCaster(WorldObject caster, Creature target)
        {
            if (caster == null || target == null)
                return;

            var now = DateTime.UtcNow;
            if (now < _shadowCloneCasterCooldownUntil)
                return;

            var procChance = (float)(caster.GetProperty(PropertyFloat.ShadowCloneProcChance) ?? 0.04);
            if (procChance <= 0.0f || ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                return;

            var cooldownSeconds = Math.Max(1.0f, (float)(caster.GetProperty(PropertyFloat.ShadowCloneCooldownSeconds) ?? 120.0));
            var durationSeconds = Math.Max(1.0f, (float)(caster.GetProperty(PropertyFloat.ShadowCloneDurationSeconds) ?? 25.0));
            var damageScale = Math.Clamp((float)(caster.GetProperty(PropertyFloat.ShadowCloneDamageScale) ?? 0.35), 0.05f, 1.0f);

            var clone = CreateShadowClonePetShell();
            if (clone == null)
            {
                Session?.Network.EnqueueSend(new GameMessageSystemChat("Your shadow strains against the void, but no combat-pet shell can take shape.", ChatMessageType.Magic));
                return;
            }

            if (!clone.InitShadowClone(this, target, durationSeconds, damageScale))
            {
                clone.Destroy();
                Session?.Network.EnqueueSend(new GameMessageSystemChat("Your shadow strains against the void, but cannot take shape.", ChatMessageType.Magic));
                return;
            }

            _shadowCloneCasterCooldownUntil = now.AddSeconds(cooldownSeconds);
            caster.CooldownId = ShadowCloneCasterCooldownId;
            caster.CooldownDuration = cooldownSeconds;
            EnchantmentManager.StartCooldown(caster);

            Session?.Network.EnqueueSend(new GameMessageSystemChat("Your shadow tears free and joins the fight.", ChatMessageType.Magic));
        }

        internal void SetActiveShadowCloneCasterPet(CombatPet clone)
        {
            _activeShadowCloneCasterPet = clone;
        }

        internal void ClearActiveShadowCloneCasterPet(CombatPet clone)
        {
            if (ReferenceEquals(_activeShadowCloneCasterPet, clone))
                _activeShadowCloneCasterPet = null;
        }

        private bool HasActiveShadowCloneCasterPet()
        {
            return _activeShadowCloneCasterPet != null && !_activeShadowCloneCasterPet.IsDestroyed && _activeShadowCloneCasterPet.IsAlive;
        }

        public void TryMirrorShadowCloneCast(WorldObject target, Spell spell)
        {
            if (!HasActiveShadowCloneCasterPet() || target == null || spell == null)
                return;

            if (!IsShadowCloneVoidProjectileSpell(spell))
                return;

            if (!(target is Creature targetCreature) || targetCreature == this || !targetCreature.IsAlive || !targetCreature.Attackable)
                return;

            var clone = _activeShadowCloneCasterPet;
            if (clone.Location == null || clone.CurrentLandblock == null || targetCreature.CurrentLandblock == null ||
                clone.CurrentLandblock.CurrentLandblockGroup != targetCreature.CurrentLandblock.CurrentLandblockGroup)
                return;

            var caster = clone.GetEquippedWand();
            if (caster == null)
                return;

            clone.SetCombatMode(CombatMode.Magic);
            clone.TryCastSpell(spell, targetCreature, null, caster, false, true, true, clone.ShadowCloneDamageScale, clone);
        }

        private static bool IsShadowCloneVoidProjectileSpell(Spell spell)
        {
            if (spell == null || spell.School != MagicSchool.VoidMagic || !spell.IsHarmful)
                return false;

            var spellType = SpellProjectile.GetProjectileSpellType(spell.Id);
            return spellType == ProjectileSpellType.Bolt ||
                   spellType == ProjectileSpellType.Streak ||
                   spellType == ProjectileSpellType.Arc ||
                   spellType == ProjectileSpellType.Ring;
        }

        private CombatPet CreateShadowClonePetShell()
        {
            foreach (var wcid in ShadowClonePetShellWcids)
            {
                var shell = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (shell is CombatPet combatPet)
                    return combatPet;

                shell?.Destroy();
            }

            return null;
        }
    }
}
