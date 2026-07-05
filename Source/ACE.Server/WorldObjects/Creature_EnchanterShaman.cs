using System;
using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        public bool IsEnchanterMob => GetProperty(PropertyBool.IsEnchanterMob) == true;
        public bool IsShamanMob => GetProperty(PropertyBool.IsShamanMob) == true;

        private double? _lastEnchanterPulseTime;
        private double? _lastShamanRingTime;
        private int _enchanterPaletteStep;

        public void TryEnchanterHeartbeat(double currentUnixTime)
        {
            if (!IsEnchanterMob || IsDead || Location == null)
                return;

            if (_lastEnchanterPulseTime.HasValue && currentUnixTime - _lastEnchanterPulseTime.Value < 10.0)
                return;

            _lastEnchanterPulseTime = currentUnixTime;
            RotateEnchanterPalette();

            var visible = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null || visible.Count == 0)
                return;

            const float range = 18.0f;
            var rangeSq = range * range;
            var allies = visible
                .Where(c => c != null
                            && c != this
                            && !c.IsDead
                            && c.Location != null
                            && c is not Player
                            && SameFaction(c)
                            && Location.SquaredDistanceTo(c.Location) <= rangeSq)
                .Take(12)
                .ToList();

            if (allies.Count == 0)
                return;

            CurrentSpell = new Spell(GetProtectionSpellForTier(GetCreatureTreasureTier()));
            var preCastTime = PreCastMotion(this, fallback: true);
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(preCastTime);
            actionChain.AddAction(this, () =>
            {
                if (IsDead)
                    return;

                EnqueueBroadcast(new GameMessageHearSpeech($"{Name} threads warding magic through nearby allies.", Name, Guid.Full, ChatMessageType.Magic), WorldObject.LocalBroadcastRange);

                foreach (var ally in allies.Where(a => a != null && !a.IsDead && a.Location != null && Location.SquaredDistanceTo(a.Location) <= rangeSq))
                {
                    ApplyEnchanterWard(ally);
                    ally.EnqueueBroadcast(new GameMessageScript(ally.Guid, PlayScript.ShieldUpPurple, 1.0f));
                }
            });
            actionChain.EnqueueChain();
        }

        public void TryShamanHeartbeat(double currentUnixTime)
        {
            if (!IsShamanMob || IsDead || Location == null || AttackTarget == null)
                return;

            if (_lastShamanRingTime.HasValue && currentUnixTime - _lastShamanRingTime.Value < 9.0)
                return;

            _lastShamanRingTime = currentUnixTime;

            var element = (DamageType)(GetProperty(PropertyInt.ExplodingMobElement) ?? (int)DamageType.Fire);
            var tier = GetCreatureTreasureTier();
            var ringSpell = new Spell(GetRingSpell(element));
            CurrentSpell = ringSpell;

            var preCastTime = PreCastMotion(AttackTarget, fallback: true);
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(preCastTime);
            actionChain.AddAction(this, () =>
            {
                if (IsDead || Location == null)
                    return;

                var effect = GetElementEffect(element);
                EnqueueBroadcast(new GameMessageScript(Guid, effect, 1.5f));
                EnqueueBroadcast(new GameMessageHearSpeech($"{Name} calls an elemental ring!", Name, Guid.Full, ChatMessageType.Magic), WorldObject.LocalBroadcastRange);

                PulseShamanRing(element, tier, effect);

                if (element == DamageType.Electric && AttackTarget is Creature target && ThreadSafeRandom.Next(0.0f, 1.0f) < 0.25f)
                    PulseShamanChainLightning(target, tier);
            });
            actionChain.EnqueueChain();
        }

        private void RotateEnchanterPalette()
        {
            var palette = _enchanterPaletteStep++ % 5;
            PaletteTemplate = palette switch
            {
                0 => (int)ACE.Entity.Enum.PaletteTemplate.Purple,
                1 => (int)ACE.Entity.Enum.PaletteTemplate.Blue,
                2 => (int)ACE.Entity.Enum.PaletteTemplate.Green,
                3 => (int)ACE.Entity.Enum.PaletteTemplate.Yellow,
                _ => (int)ACE.Entity.Enum.PaletteTemplate.Red,
            };
            Shade = 0.55 + (palette * 0.08);
            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantUpPurple, 0.75f));
        }

        private void ApplyEnchanterWard(Creature ally)
        {
            SetResistIfWeaker(ally, PropertyFloat.ResistSlash, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistPierce, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistBludgeon, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistFire, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistCold, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistAcid, 0.80f);
            SetResistIfWeaker(ally, PropertyFloat.ResistElectric, 0.80f);
        }

        private static void SetResistIfWeaker(Creature target, PropertyFloat property, double value)
        {
            var current = target.GetProperty(property) ?? 1.0;
            if (current > value)
                target.SetProperty(property, value);
        }

        private void PulseShamanRing(DamageType element, int tier, PlayScript effect)
        {
            var visible = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visible == null || visible.Count == 0)
                return;

            const float radius = 7.0f;
            var radiusSq = radius * radius;
            var baseDamage = Math.Max(8, (int)Math.Round(Health.MaxValue * (0.035f + tier * 0.006f)));

            foreach (var target in visible.Where(c => c is Player && !c.IsDead && c.Location != null && Location.SquaredDistanceTo(c.Location) <= radiusSq))
            {
                var resistMod = target.GetResistanceMod(element, this, null);
                var damage = (float)Math.Max(1, Math.Round(baseDamage * resistMod * ThreadSafeRandom.Next(0.85f, 1.15f)));
                var taken = target.TakeDamage(this, element, damage);
                if (taken > 0)
                    target.EnqueueBroadcast(new GameMessageScript(target.Guid, effect, 1.0f));
            }
        }

        private void PulseShamanChainLightning(Creature firstTarget, int tier)
        {
            if (firstTarget == null || firstTarget.IsDead || firstTarget.Location == null)
                return;

            var baseDamage = Math.Max(12, (int)Math.Round(Health.MaxValue * (0.025f + tier * 0.005f)));
            var visible = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            var targets = visible == null
                ? new[] { firstTarget }.ToList()
                : visible.Where(c => c is Player && !c.IsDead && c.Location != null && firstTarget.Location.SquaredDistanceTo(c.Location) <= 12.0f * 12.0f)
                    .OrderBy(c => firstTarget.Location.SquaredDistanceTo(c.Location))
                    .Take(4)
                    .ToList();

            var falloff = 1.0f;
            foreach (var target in targets)
            {
                var resistMod = target.GetResistanceMod(DamageType.Electric, this, null);
                var damage = (float)Math.Max(1, Math.Round(baseDamage * falloff * resistMod));
                var taken = target.TakeDamage(this, DamageType.Electric, damage);
                if (taken > 0)
                    target.EnqueueBroadcast(new GameMessageScript(target.Guid, PlayScript.BreatheLightning, 1.0f));
                falloff *= 0.65f;
            }
        }

        private int GetCreatureTreasureTier()
        {
            if (DeathTreasure != null)
                return Math.Clamp(DeathTreasure.Tier, 1, 8);

            if (Level.HasValue)
                return Math.Clamp((int)Math.Ceiling(Level.Value / 10.0), 1, 8);

            return 1;
        }

        private static SpellId GetProtectionSpellForTier(int tier)
        {
            if (tier >= 8) return SpellId.FireProtectionOther8;
            if (tier >= 7) return SpellId.FireProtectionOther6;
            if (tier >= 5) return SpellId.FireProtectionOther5;
            if (tier >= 3) return SpellId.FireProtectionOther3;
            return SpellId.FireProtectionOther1;
        }

        private static SpellId GetRingSpell(DamageType element)
        {
            return element switch
            {
                DamageType.Cold => SpellId.FrostRing,
                DamageType.Acid => SpellId.AcidRing,
                DamageType.Electric => SpellId.LightningRing,
                _ => SpellId.FlameRing,
            };
        }

        private static PlayScript GetElementEffect(DamageType element)
        {
            return element switch
            {
                DamageType.Cold => PlayScript.BreatheFrost,
                DamageType.Acid => PlayScript.BreatheAcid,
                DamageType.Electric => PlayScript.BreatheLightning,
                _ => PlayScript.Explode,
            };
        }
    }
}