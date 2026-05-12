using System;
using System.Linq;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        /// <summary>
        /// DerpACE: Healer mob behavior.
        /// Returns true if this creature is a healer mob, false otherwise.
        /// </summary>
        public bool IsHealerMob => GetProperty(PropertyBool.IsHealerMob) == true;

        /// <summary>
        /// Timestamp (Stopwatch seconds) of the last Heal Other cast.
        /// </summary>
        private double? _lastHealerCastTime;

        /// <summary>
        /// DerpACE: Healer mob heartbeat — scan for wounded allies and cast Heal Other on them.
        /// Called from Creature_Tick.Heartbeat if IsHealerMob is true.
        /// </summary>
        public void TryHealerHeartbeat(double currentUnixTime)
        {
            if (!IsHealerMob) return;

            // Respect configured cooldown
            var cooldown = Math.Max(1.0, DerpACEConfig.HealerMobCooldownSeconds);
            if (_lastHealerCastTime.HasValue)
            {
                var elapsed = currentUnixTime - _lastHealerCastTime.Value;
                if (elapsed < cooldown)
                    return;
            }

            // Must have enough mana and not be in combat state that would prevent casting
            if (Mana == null || Mana.Current < 10) return;
            if (IsDead || IsBusy) return;
            if (PhysicsObj?.IsMovingTo() == true) return;

            // Find wounded allies within range
            var range = Math.Max(5.0f, DerpACEConfig.HealerMobRange);
            var threshold = Math.Clamp(DerpACEConfig.HealerMobHealThreshold, 0.0f, 1.0f);
            var rangeSq = range * range;

            var visibleCreatures = PhysicsObj?.ObjMaint?.GetVisibleObjectsValuesOfTypeCreature();
            if (visibleCreatures == null || visibleCreatures.Count == 0) return;

            var woundedAllies = visibleCreatures
                .Where(c => c != null
                            && c != this
                            && c.Location != null
                            && !c.IsDead
                            && c is not Player  // never heal players
                            && SameFaction(c)   // must share faction
                            && c.Health != null
                            && c.Health.MaxValue > 0
                            && c.Health.Percent < threshold
                            && Location.SquaredDistanceTo(c.Location) <= rangeSq)
                .OrderBy(c => c.Health.Percent)  // most-wounded first
                .ToList();

            if (woundedAllies.Count == 0) return;

            var target = woundedAllies.First();

            // Choose a Heal Other spell based on tier
            var tier = 1;
            if (DeathTreasure != null)
                tier = DeathTreasure.Tier;
            else if (Level.HasValue)
                tier = (int)Math.Ceiling(Level.Value / 10.0);

            var spellId = GetHealOtherSpellForTier(tier);
            var spell = new Spell(spellId);
            if (spell == null || spell.NotFound) return;

            // Check mana cost
            var manaCost = CalculateManaUsage(this, spell, target);
            if (Mana.Current < manaCost) return;

            // Deduct mana
            UpdateVital(Mana, Mana.Current - manaCost);

            // Broadcast cast message
            EnqueueBroadcast(new GameMessageHearSpeech($"{Name} casts {spell.Name} on {target.Name}!", Name, Guid.Full, ChatMessageType.Magic), WorldObject.LocalBroadcastRange);

            // Trigger the actual heal with pre-cast motion
            var preCastTime = PreCastMotion_Human(target);
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(preCastTime);
            actionChain.AddAction(this, () =>
            {
                if (IsDead || target.IsDead) return;

                // Actually apply the heal
                target.HandleCastSpell(spell, target, this);

                // Show health-up animation on the target
                target.EnqueueBroadcast(new GameMessageScript(target.Guid, PlayScript.HealthUpBlue, 1.0f));
            });
            actionChain.EnqueueChain();

            _lastHealerCastTime = currentUnixTime;
        }

        /// <summary>
        /// Returns the SpellId for Heal Other appropriate to the given tier.
        /// Tier 1-2 => HealOther1, Tier 3-4 => HealOther3, Tier 5-6 => HealOther5, Tier 7+ => HealOther7.
        /// </summary>
        private SpellId GetHealOtherSpellForTier(int tier)
        {
            if (tier <= 2) return SpellId.HealOther1;
            if (tier <= 4) return SpellId.HealOther3;
            if (tier <= 6) return SpellId.HealOther5;
            return SpellId.HealOther7;
        }
    }
}
