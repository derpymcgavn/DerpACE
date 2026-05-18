using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.DatLoader.Entity;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public enum CombatType
    {
        Melee,
        Missile,
        Magic
    };

    /// <summary>
    /// Handles combat with a Player as the attacker
    /// generalized methods for melee / missile
    /// </summary>
    partial class Player
    {
        public int AttackSequence;
        public bool Attacking;
        public bool AttackCancelled;

        // Polebreaker Staff (DerpACE) — transient consecutive-hit chain state (resets on logout/restart)
        public uint LastPolebreakerTargetGuid { get; set; } = 0;
        public int PolebreakerStackCount { get; set; } = 0;

        // DerpACE Nomad — recursion guard so Cleave Flurry extra strikes don't proc themselves
        private bool _nomadProcInProgress;

        // DerpACE: Unarmed combo system for tracking punch/kick combos
        private UnarmedComboSystem _unarmedComboSystem;
        public UnarmedComboSystem UnarmedComboSystem
        {
            get
            {
                if (_unarmedComboSystem == null)
                    _unarmedComboSystem = new UnarmedComboSystem(this);
                return _unarmedComboSystem;
            }
        }

        public DateTime NextRefillTime;

        public double LastPkAttackTimestamp
        {
            get => GetProperty(PropertyFloat.LastPkAttackTimestamp) ?? 0;
            set { if (value == 0) RemoveProperty(PropertyFloat.LastPkAttackTimestamp); else SetProperty(PropertyFloat.LastPkAttackTimestamp, value); }
        }

        public double PkTimestamp
        {
            get => GetProperty(PropertyFloat.PkTimestamp) ?? 0;
            set { if (value == 0) RemoveProperty(PropertyFloat.PkTimestamp); else SetProperty(PropertyFloat.PkTimestamp, value); }
        }

        /// <summary>
        /// Returns the current attack skill for the player
        /// </summary>
        public override Skill GetCurrentAttackSkill()
        {
            if (CombatMode == CombatMode.Magic)
                return GetCurrentMagicSkill();
            else
                return GetCurrentWeaponSkill();
        }

        /// <summary>
        /// Returns the current weapon skill for the player
        /// </summary>
        public override Skill GetCurrentWeaponSkill()
        {
            var weapon = GetEquippedWeapon();

            if (weapon?.WeaponSkill == null)
                return GetHighestMeleeSkill();

            var skill = ConvertToMoASkill(weapon.WeaponSkill);

            // DualWieldAlternate will be TRUE if *next* attack is offhand
            if (IsDualWieldAttack && !DualWieldAlternate)
            {
                var weaponSkill = GetCreatureSkill(skill);
                var dualWield = GetCreatureSkill(Skill.DualWield);

                // offhand attacks use the lower skill level between dual wield and weapon skill
                if (dualWield.Current < weaponSkill.Current)
                    skill = Skill.DualWield;
            }
            //Console.WriteLine($"{Name}.GetCurrentWeaponSkill - {skill}");
            return skill;
        }

        /// <summary>
        /// Returns the highest melee skill for the player
        /// (light / heavy / finesse)
        /// </summary>
        public Skill GetHighestMeleeSkill()
        {
            var light = GetCreatureSkill(Skill.LightWeapons);
            var heavy = GetCreatureSkill(Skill.HeavyWeapons);
            var finesse = GetCreatureSkill(Skill.FinesseWeapons);

            var maxMelee = light;
            if (heavy.Current > maxMelee.Current)
                maxMelee = heavy;
            if (finesse.Current > maxMelee.Current)
                maxMelee = finesse;

            return maxMelee.Skill;
        }

        /// <summary>
        /// Returns TRUE if the weapon is one of the specified weapon types
        /// </summary>
        private bool WeaponIsType(WorldObject weapon, params WeaponType[] types)
        {
            if (weapon == null)
                return false;

            var weaponType = weapon.W_WeaponType;
            return types.Contains(weaponType);
        }

        /// <summary>
        /// Returns TRUE if the weapon name contains any of the specified substrings (case-insensitive)
        /// </summary>
        private bool WeaponNameContains(WorldObject weapon, params string[] substrings)
        {
            if (weapon?.Name == null)
                return false;

            foreach (var substring in substrings)
            {
                if (weapon.Name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public override CombatType GetCombatType()
        {
            // this is an unsafe function, move away from this
            var weapon = GetEquippedWeapon();

            if (weapon == null || weapon.CurrentWieldedLocation != EquipMask.MissileWeapon)
                return CombatType.Melee;
            else
                return CombatType.Missile;
        }

        public DamageEvent DamageTarget(Creature target, WorldObject damageSource)
        {
            if (target.Health.Current <= 0)
                return null;

            var targetPlayer = target as Player;

            // check PK status
            var pkError = CheckPKStatusVsTarget(target, null);
            if (pkError != null)
            {
                Session.Network.EnqueueSend(new GameEventWeenieErrorWithString(Session, pkError[0], target.Name));
                if (targetPlayer != null)
                    targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieErrorWithString(targetPlayer.Session, pkError[1], Name));
                return null;
            }

            var damageEvent = DamageEvent.CalculateDamage(this, target, damageSource);

            // DerpACE: Unarmed Combo System - check for combos and apply bonuses
            ComboResult comboResult = null;
            if (damageEvent.HasDamage && damageSource == this && (AttackType == AttackType.Punch || AttackType == AttackType.Kick))
            {
                comboResult = UnarmedComboSystem.RecordAttack(AttackType, target.Guid.Full);

                // Apply combo damage multiplier
                if (comboResult.DamageMultiplier > 1.0f)
                {
                    var comboBonus = damageEvent.Damage * (comboResult.DamageMultiplier - 1.0f);
                    damageEvent.Damage += comboBonus;
                }
            }

            // DerpACE Nomad: custom unarmed procs stamped onto gauntlets/shoes (Cleave Flurry / Healing Strike).
            // Only fires on direct Punch/Kick from the wielder, and only when not already inside a proc-driven strike.
            uint nomadFlurryHits = 0;
            uint nomadFlurryDamage = 0;
            uint nomadHealApplied = 0;
            if (!_nomadProcInProgress
                && damageEvent.HasDamage
                && damageSource == this
                && (AttackType == AttackType.Punch || AttackType == AttackType.Kick))
            {
                var procSource = AttackType == AttackType.Punch ? HandArmor : FootArmor;
                var procType = procSource?.GetProperty(PropertyInt.NomadProcType) ?? 0;
                var procChance = procSource?.GetProperty(PropertyFloat.NomadProcChance) ?? 0.0;
                var procMagnitude = procSource?.GetProperty(PropertyFloat.NomadProcMagnitude) ?? 0.0;

                if (procType > 0 && procChance > 0 && ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    if (procType == 1)
                    {
                        // Cleave Flurry: 2-4 extra fast strikes at procMagnitude * base damage each.
                        var extraStrikes = ThreadSafeRandom.Next(2, 4);
                        _nomadProcInProgress = true;
                        try
                        {
                            for (var i = 0; i < extraStrikes; i++)
                            {
                                if (!target.IsAlive)
                                    break;

                                var strikeDamage = Math.Max(1.0f, damageEvent.Damage * (float)procMagnitude);
                                target.TakeDamage(this, damageEvent.DamageType, strikeDamage, false);
                                target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SplatterMidLeftBack);

                                nomadFlurryHits++;
                                nomadFlurryDamage += (uint)Math.Round(strikeDamage);
                            }
                        }
                        finally
                        {
                            _nomadProcInProgress = false;
                        }

                        if (nomadFlurryHits > 0 && !SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"Cleave Flurry! {nomadFlurryHits} extra strikes for {nomadFlurryDamage} damage [{target.Name}]",
                                ChatMessageType.CombatSelf));
                    }
                    else if (procType == 2)
                    {
                        // Healing Strike: heal the wielder for procMagnitude * damage dealt (can be >100%).
                        if (Health.Current < Health.MaxValue)
                        {
                            var heal = (int)Math.Round(damageEvent.Damage * (float)procMagnitude);
                            if (heal >= 1)
                            {
                                var restored = UpdateVitalDelta(Health, heal);
                                if (restored > 0)
                                {
                                    nomadHealApplied = (uint)restored;
                                    DamageHistory.OnHeal((uint)restored);
                                    ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpRed);

                                    if (!SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                                            $"Healing Strike! +{nomadHealApplied} health from {target.Name}",
                                            ChatMessageType.CombatSelf));
                                }
                            }
                        }
                    }
                }
            }

            // Thief's Dagger: configurable proc chance / bonus on sneak attacks (see @lootconfig)
            // Only applies to Dagger weapon type
            uint thievesDaggerBonus = 0;
            if (damageEvent.HasDamage
                && damageEvent.SneakAttackMod > 1.0f
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThievesDagger) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Dagger)
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ThievesDaggerProcChance)
            {
                var bonus = damageEvent.Damage * ACE.Server.Managers.DerpACEConfig.ThievesDaggerProcBonus;
                damageEvent.Damage += bonus;
                thievesDaggerBonus = (uint)Math.Round(bonus);
            }

            // Fencer's Blade: armor pierce proc — deals a portion of what armor blocked as bonus damage
            // Only applies to Sword weapon type with names: epee, schlager, rapier
            uint fencerPierceBonus = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsFencerBlade) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Sword)
                && WeaponNameContains(damageEvent.Weapon, "epee", "schlager", "rapier"))
            {
                var pierceProc = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPierceProc) ?? 0.0;
                if (ThreadSafeRandom.Next(0.0f, 1.0f) < pierceProc)
                {
                    var piercePct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerArmorPiercePct) ?? 0.0;
                    var bonus = Math.Max(0.0f, damageEvent.DamageMitigated) * (float)piercePct;
                    if (bonus >= 1.0f)
                    {
                        damageEvent.Damage += bonus;
                        fencerPierceBonus = (uint)Math.Round(bonus);
                    }
                }
            }

            // Sentinel's Spear: configurable proc/drain/return (see @lootconfig)
            // Only applies to Spear weapon type
            uint sentinelStaminaDrained = 0;
            uint sentinelStaminaReturned = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsSentinelSpear) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Spear)
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.SentinelSpearProcChance
                && target.Stamina.Current > 0)
            {
                var drain = (int)Math.Round(target.Stamina.Current * ACE.Server.Managers.DerpACEConfig.SentinelSpearDrainPct);
                if (drain < 1) drain = 1;
                var actualDrain = (uint)-target.UpdateVitalDelta(target.Stamina, -drain);
                sentinelStaminaDrained = actualDrain;
                var restore = (int)Math.Round(actualDrain * ACE.Server.Managers.DerpACEConfig.SentinelSpearReturnMult);
                if (restore >= 1)
                {
                    UpdateVitalDelta(Stamina, restore);
                    sentinelStaminaReturned = (uint)restore;
                }
            }

            // Ravager's Axe: configurable proc to apply a bleed DoT (see @lootconfig)
            // Only applies to Axe weapon type (or Mace types with 'hammer' in name)
            uint ravagerBleedTotal = 0;
            uint ravagerCrushBonus = 0;
            uint ravagerStaminaDrained = 0;
            uint ravagerCleaveHits = 0;
            uint ravagerCleaveTotal = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsRavagersAxe) == true
                && (WeaponIsType(damageEvent.Weapon, WeaponType.Axe) || (WeaponIsType(damageEvent.Weapon, WeaponType.Mace) && WeaponNameContains(damageEvent.Weapon, "hammer"))))
            {
                var procChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedProc) ?? 0.0;
                if (ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    var bleedPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.RavagerBleedPct) ?? 0.0;

                    var isHammerNamedAxe = damageEvent.Weapon.Name?.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isHammerNamedAxe)
                    {
                        var crushBonus = damageEvent.Damage * (float)bleedPct;
                        if (crushBonus >= 1.0f)
                        {
                            damageEvent.Damage += crushBonus;
                            ravagerCrushBonus = (uint)Math.Round(crushBonus);
                        }

                        if (target.Stamina.Current > 0)
                        {
                            var drainPct = Math.Clamp((float)bleedPct * 0.5f, 0.04f, 0.08f);
                            var drain = (int)Math.Round(target.Stamina.Current * drainPct);
                            if (drain < 1) drain = 1;
                            ravagerStaminaDrained = (uint)-target.UpdateVitalDelta(target.Stamina, -drain);
                        }

                        // Hammer proc feedback: attacker slam animation + target knock motion
                        var attackerStance = CurrentMotionState?.Stance ?? MotionStance.NonCombat;
                        EnqueueBroadcastMotion(new Motion(attackerStance, MotionCommand.AttackHigh3));

                        var targetStance = target.CurrentMotionState?.Stance ?? MotionStance.NonCombat;
                        target.EnqueueBroadcastMotion(new Motion(targetStance, MotionCommand.Knock));

                        // Play explosion particle effect on target for visual feedback
                        target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.Explode);

                        // Chance-based hammer cleave: hit nearby monsters for reduced rolled damage.
                        // Cleave count includes the primary target, so secondary count is (maxTargets - 1).
                        var cleaveChance = Math.Clamp(ACE.Server.Managers.DerpACEConfig.RavagerHammerCleaveChance, 0.0f, 1.0f);
                        var cleaveMaxSecondary = Math.Max(0, ACE.Server.Managers.DerpACEConfig.RavagerHammerCleaveMaxTargets - 1);
                        var cleaveScale = Math.Clamp(ACE.Server.Managers.DerpACEConfig.RavagerHammerCleaveDamageScale, 0.0f, 1.0f);
                        var cleaveRadius = Math.Max(1.0f, ACE.Server.Managers.DerpACEConfig.RavagerHammerCleaveRadius);

                        if (cleaveMaxSecondary > 0
                            && cleaveScale > 0.0f
                            && CurrentLandblock != null
                            && target.Location != null
                            && ThreadSafeRandom.Next(0.0f, 1.0f) < cleaveChance)
                        {
                            var cleaveDamage = Math.Max(1.0f, damageEvent.Damage * cleaveScale);
                            var radiusSq = cleaveRadius * cleaveRadius;

                            var splashTargets = CurrentLandblock.GetAllWorldObjectsForDiagnostics()
                                .OfType<Creature>()
                                .Where(c => c != null
                                            && c != target
                                            && c != this
                                            && c.IsAlive
                                            && c.Attackable
                                            && c.IsMonster
                                            && !c.Teleporting
                                            && c.Location != null
                                            && target.Location.SquaredDistanceTo(c.Location) <= radiusSq)
                                .OrderBy(c => target.Location.SquaredDistanceTo(c.Location))
                                .Take(cleaveMaxSecondary)
                                .ToList();

                            foreach (var splashTarget in splashTargets)
                            {
                                splashTarget.TakeDamage(this, damageEvent.DamageType, cleaveDamage, false);
                                splashTarget.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.Explode);
                                ravagerCleaveHits++;
                                ravagerCleaveTotal += (uint)Math.Round(cleaveDamage);
                            }
                        }
                    }
                    else
                    {
                        var totalBleed = damageEvent.Damage * (float)bleedPct;
                        var ticks = Math.Max(1, ACE.Server.Managers.DerpACEConfig.RavagerBleedTicks);
                        var interval = Math.Max(0.1f, ACE.Server.Managers.DerpACEConfig.RavagerBleedInterval);
                        var perTick = (float)(totalBleed / ticks);
                        if (perTick >= 1.0f)
                        {
                            ravagerBleedTotal = (uint)Math.Round(totalBleed);
                            var bleedType = damageEvent.DamageType;
                            var bleedTarget = target;
                            var attacker = this;

                            var chain = new ActionChain();
                            for (var i = 0; i < ticks; i++)
                            {
                                chain.AddDelaySeconds(interval);
                                chain.AddAction(this, () =>
                                {
                                    if (bleedTarget == null || !bleedTarget.IsAlive)
                                        return;

                                    bleedTarget.TakeDamage(attacker, bleedType, perTick, false);
                                    bleedTarget.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SplatterMidLeftBack);

                                    if (attacker.Session != null && !SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatSelf))
                                        attacker.Session.Network.EnqueueSend(new GameMessageSystemChat(
                                            $"-{(uint)Math.Round(perTick)} bleed [{bleedTarget.Name}] [Ravager's Axe]",
                                            ChatMessageType.CombatSelf));
                                });
                            }
                            chain.EnqueueChain();
                        }
                    }
                }
            }

            // Warden's Maul: configurable proc to apply a flat defense-skill debuff (see @lootconfig)
            // Only applies to Mace weapon type (hammers/mauls)
            uint wardenPenaltyApplied = 0;
            int wardenDurationApplied = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsWardensMaul) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Mace))
            {
                var procChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussProc) ?? 0.0;
                if (ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    var penalty = (uint)Math.Max(0, (int)(damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussPenalty) ?? 0.0));
                    var duration = (int)(damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.WardenConcussDuration) ?? 0.0);
                    if (penalty > 0 && duration > 0)
                    {
                        var newUntil = DateTime.UtcNow.AddSeconds(duration);
                        // refresh-and-take-stronger: keep the larger penalty if currently concussed
                        if (target.ConcussedUntil > DateTime.UtcNow && target.ConcussedPenalty >= penalty)
                            target.ConcussedUntil = newUntil; // just refresh duration
                        else
                        {
                            target.ConcussedPenalty = penalty;
                            target.ConcussedUntil = newUntil;
                        }
                        wardenPenaltyApplied = penalty;
                        wardenDurationApplied = duration;
                        target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthDownYellow);
                    }
                }
            }

            // Resolute Blade: heal-on-critical proc (see @lootconfig)
            // Only applies to Sword weapon type with names: tachi, ken
            uint resoluteHealApplied = 0;
            if (damageEvent.HasDamage
                && damageEvent.IsCritical
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsResoluteBlade) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Sword)
                && WeaponNameContains(damageEvent.Weapon, "tachi", "ken"))
            {
                var procChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealProc) ?? 0.0;
                if (ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    var healPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteHealPct) ?? 0.0;
                    var heal = (int)Math.Round(damageEvent.Damage * (float)healPct);
                    if (heal >= 1 && Health.Current < Health.MaxValue)
                    {
                        UpdateVitalDelta(Health, heal);
                        resoluteHealApplied = (uint)heal;
                    }
                }
            }

            // Breacher's Crossbow: proc chance to ignore all armor on a shot (see @lootconfig)
            // Only applies to Crossbow weapon type
            uint breacherArmorIgnored = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsBreachersCrossbow) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Crossbow))
            {
                var ignoreChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.BreacherArmorIgnoreChance) ?? 0.0;
                if (ignoreChance > 0 && ThreadSafeRandom.Next(0.0f, 1.0f) < ignoreChance)
                {
                    // Ignore all armor: restore the full unmitigated damage
                    var fullDamage = damageEvent.DamageBeforeMitigation;
                    breacherArmorIgnored = (uint)Math.Round(damageEvent.DamageMitigated);
                    damageEvent.Damage = fullDamage;
                }
            }

            // Stalker's Bow: opening-shot proc - first time this attacker hits a target, roll a chance for bonus damage (see @lootconfig)
            // Only applies to Bow weapon type
            uint stalkerBonusApplied = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsStalkersBow) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Bow)
                && !target.DamageHistory.TotalDamage.ContainsKey(this.Guid))
            {
                var procChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeProc) ?? 0.0;
                if (ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    var bonusPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.StalkerFirstStrikeBonus) ?? 0.0;
                    var bonus = damageEvent.Damage * (float)bonusPct;
                    if (bonus >= 1.0f)
                    {
                        damageEvent.Damage += bonus;
                        stalkerBonusApplied = (uint)Math.Round(bonus);
                    }
                }
            }

            // Armor vital affix: rare on-hit replenish proc from equipped vital armor pieces.
            uint armorVitalProcRestore = 0;
            string armorVitalProcLabel = null;
            if (damageEvent.HasDamage
                && (Health.Current < Health.MaxValue || Stamina.Current < Stamina.MaxValue || Mana.Current < Mana.MaxValue))
            {
                foreach (var equipped in EquippedObjects.Values)
                {
                    var procAmount = equipped.ArmorVitalProcAmount ?? 0;
                    var procChance = equipped.ArmorVitalProcChance ?? 0.0;

                    if (procAmount <= 0 || procChance <= 0)
                        continue;

                    if (ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                        continue;

                    int restored;
                    if ((equipped.GearMaxHealth ?? 0) > 0)
                    {
                        restored = UpdateVitalDelta(Health, procAmount);
                        armorVitalProcLabel = "health";
                    }
                    else if ((equipped.GearMaxStamina ?? 0) > 0)
                    {
                        restored = UpdateVitalDelta(Stamina, procAmount);
                        armorVitalProcLabel = "stamina";
                    }
                    else if ((equipped.GearMaxMana ?? 0) > 0)
                    {
                        restored = UpdateVitalDelta(Mana, procAmount);
                        armorVitalProcLabel = "mana";
                    }
                    else
                    {
                        continue;
                    }

                    if (restored > 0)
                    {
                        armorVitalProcRestore = (uint)restored;
                        break;
                    }
                }
            }

            // Polebreaker Staff: consecutive-hit escalation against the same target (see @lootconfig)
            // Only applies to Staff or TwoHanded weapon types (includes staff, tetsuba, etc.)
            uint polebreakerBonus = 0;
            int polebreakerStacks = 0;
            if (damageEvent.HasDamage
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsPolebreakerStaff) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Staff, WeaponType.TwoHanded))
            {
                var stackPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerStackBonus) ?? 0.0;
                var maxStacks = (int)(damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.PolebreakerMaxStacks) ?? 0.0);
                if (stackPct > 0 && maxStacks > 0)
                {
                    if (LastPolebreakerTargetGuid == target.Guid.Full)
                        PolebreakerStackCount = Math.Min(PolebreakerStackCount + 1, maxStacks);
                    else
                    {
                        LastPolebreakerTargetGuid = target.Guid.Full;
                        PolebreakerStackCount = 1;
                    }
                    polebreakerStacks = PolebreakerStackCount;
                    // bonus uses the *prior* stacks (1st hit = no bonus, 2nd hit = +stackPct, ...)
                    var bonusStacks = polebreakerStacks - 1;
                    if (bonusStacks > 0)
                    {
                        var bonus = damageEvent.Damage * (float)stackPct * bonusStacks;
                        if (bonus >= 1.0f)
                        {
                            damageEvent.Damage += bonus;
                            polebreakerBonus = (uint)Math.Round(bonus);
                        }
                    }
                }
            }
            else if (damageEvent.HasDamage)
            {
                // any non-Polebreaker hit breaks the chain
                LastPolebreakerTargetGuid = 0;
                PolebreakerStackCount = 0;
            }

            // Vampiric Jewelry: per-piece small chance on a successful hit to drink a tiny amount of health (see @lootconfig)
            uint vampiricJewelryHealed = 0;
            if (damageEvent.HasDamage)
                vampiricJewelryHealed = TryProcVampiricJewelryOnHit();

            // DerpACE: Apply combo effects
            if (comboResult != null && comboResult.ComboType != ComboType.None)
            {
                // Apply special combo effects
                switch (comboResult.BonusEffect)
                {
                    case ComboEffect.Stun:
                        // 50% chance to briefly stun the target
                        if (ThreadSafeRandom.Next(0.0f, 1.0f) < 0.5f)
                        {
                            target.EnqueueBroadcastMotion(new Motion(target.CurrentMotionState?.Stance ?? MotionStance.NonCombat, MotionCommand.Knock));
                            target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SkillUpYellow);
                        }
                        break;

                    case ComboEffect.ElementalSurge:
                        // Add bonus elemental damage based on gauntlet/boot element
                        var surgeDamage = damageEvent.Damage * 0.25f;
                        damageEvent.Damage += surgeDamage;
                        target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.BreatheFlame);
                        break;

                    case ComboEffect.Cleave:
                        // Mini cleave effect - hit nearby enemies for 30% damage
                        if (CurrentLandblock != null && target.Location != null)
                        {
                            var cleaveDamage = damageEvent.Damage * 0.3f;
                            var splashTargets = CurrentLandblock.GetAllWorldObjectsForDiagnostics()
                                .OfType<Creature>()
                                .Where(c => c != null && c != target && c != this && c.IsAlive && c.Attackable
                                            && c.IsMonster && !c.Teleporting && c.Location != null
                                            && target.Location.SquaredDistanceTo(c.Location) <= 25.0f) // 5 meter radius
                                .OrderBy(c => target.Location.SquaredDistanceTo(c.Location))
                                .Take(2)
                                .ToList();

                            foreach (var splash in splashTargets)
                            {
                                splash.TakeDamage(this, damageEvent.DamageType, cleaveDamage, false);
                                splash.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.WeddingBliss);
                            }
                        }
                        break;

                    case ComboEffect.CriticalBoost:
                        // Next attack has increased crit chance (tracked separately if needed)
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.AetheriaLevelUp);
                        break;

                    case ComboEffect.ArmorPierce:
                        // Bonus damage based on armor ignored
                        var pierceBonus = Math.Max(0.0f, damageEvent.DamageMitigated) * 0.3f;
                        if (pierceBonus > 1.0f)
                        {
                            damageEvent.Damage += pierceBonus;
                            target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.SkillDownRed);
                        }
                        break;
                }
            }

            if (damageEvent.HasDamage)
            {
                OnDamageTarget(target, damageEvent.CombatType, damageEvent.IsCritical);

                if (targetPlayer != null)
                    targetPlayer.TakeDamage(this, damageEvent);
                else
                    target.TakeDamage(this, damageEvent.DamageType, damageEvent.Damage, damageEvent.IsCritical);
            }
            else
            {
                if (damageEvent.LifestoneProtection)
                    Session.Network.EnqueueSend(new GameMessageSystemChat($"The Lifestone's magic protects {target.Name} from the attack!", ChatMessageType.Magic));

                else if (!SquelchManager.Squelches.Contains(target, ChatMessageType.CombatSelf))
                    Session.Network.EnqueueSend(new GameEventEvasionAttackerNotification(Session, target.Name));

                if (targetPlayer != null)
                    targetPlayer.OnEvade(this, damageEvent.CombatType);
            }

            if (damageEvent.HasDamage && target.IsAlive)
            {
                // notify attacker
                var intDamage = (uint)Math.Round(damageEvent.Damage);

                if (!SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                    Session.Network.EnqueueSend(new GameEventAttackerNotification(Session, target.Name, damageEvent.DamageType, (float)intDamage / target.Health.MaxValue, intDamage, damageEvent.IsCritical, damageEvent.AttackConditions));

                // Thief's Dagger: show bonus when the 10% proc fired
                if (thievesDaggerBonus > 0)
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{thievesDaggerBonus} [Thief's Dagger]",
                        ChatMessageType.CombatSelf));

                // Vampiric Jewelry: announce the on-hit drink when any health-flavor piece proc'd.
                // Combat channel renders red client-side, matching the health visual.
                if (vampiricJewelryHealed > 0)
                {
                    ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpRed);
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{vampiricJewelryHealed} health drained from {target.Name} [Vampiric Jewelry]",
                        ChatMessageType.Combat));
                }

                // DerpACE: Unarmed Combo notification
                if (comboResult != null)
                {
                    // Show combo counter for all attacks
                    if (comboResult.HitCount > 0 && comboResult.ComboType == ComboType.None)
                    {
                        var comboChain = UnarmedComboSystem.GetComboChainDisplay();
                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                            $"{comboChain} {comboResult.HitCount} hit combo",
                            ChatMessageType.CombatSelf));
                    }

                    // Show completed combo message
                    if (comboResult.ComboType != ComboType.None && !string.IsNullOrEmpty(comboResult.Message))
                    {
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.AetheriaLevelUp);
                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                            comboResult.Message,
                            ChatMessageType.Broadcast));

                        var bonusDamage = (uint)Math.Round(damageEvent.Damage * (comboResult.DamageMultiplier - 1.0f));
                        if (bonusDamage > 0)
                        {
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"+{bonusDamage} combo damage (x{comboResult.DamageMultiplier:F1})",
                                ChatMessageType.CombatSelf));
                        }
                    }
                }

                // Fencer's Blade: show pierce bonus when armor-pierce proc fired
                if (fencerPierceBonus > 0)
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{fencerPierceBonus} pierce [Fencer's Blade]",
                        ChatMessageType.CombatSelf));

                // Sentinel's Spear: show stamina drain/return when proc fired
                if (sentinelStaminaDrained > 0)
                {
                    target.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthDownYellow);
                    ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpYellow);
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"-{sentinelStaminaDrained} stamina [{target.Name}] +{sentinelStaminaReturned} [Sentinel's Spear]",
                        ChatMessageType.CombatSelf));
                }

                // Ravager's Axe: announce the bleed when proc fired (ticks deliver damage on a delay)
                if (ravagerBleedTotal > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"{target.Name} is bleeding (+{ravagerBleedTotal}) [Ravager's Axe]",
                        ChatMessageType.CombatSelf));
                }

                // Ravager's Axe (hammer-named variants): announce crushing proc when fired
                if (ravagerCrushBonus > 0 || ravagerStaminaDrained > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"{target.Name} is crushed (+{ravagerCrushBonus} dmg, -{ravagerStaminaDrained} stamina) [Ravager's Axe]",
                        ChatMessageType.CombatSelf));
                }

                if (ravagerCleaveHits > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"Hammer cleave hits {ravagerCleaveHits} nearby target(s) for {ravagerCleaveTotal} total splash [Ravager's Axe]",
                        ChatMessageType.CombatSelf));
                }

                // Warden's Maul: announce the concussion when proc fired
                if (wardenPenaltyApplied > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"crushes {target.Name}'s guard — -{wardenPenaltyApplied} defense skill for {wardenDurationApplied} sec [Warden's Maul]",
                        ChatMessageType.CombatSelf));
                }

                // Polebreaker Staff: announce current stack and bonus damage (if any) on the chained hit
                if (polebreakerStacks > 1 && polebreakerBonus > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"[Polebreaker] +{polebreakerBonus} (x{polebreakerStacks})",
                        ChatMessageType.CombatSelf));
                }

                // Stalker's Bow: announce opening-shot bonus damage when proc fired
                if (stalkerBonusApplied > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{stalkerBonusApplied} [Stalker's Bow] first strike",
                        ChatMessageType.CombatSelf));
                }

                // Breacher's Crossbow: announce armor bypass proc
                if (breacherArmorIgnored > 0)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{breacherArmorIgnored} armor bypass [Breacher's Crossbow]",
                        ChatMessageType.CombatSelf));
                }

                if (armorVitalProcRestore > 0 && armorVitalProcLabel != null)
                {
                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"+{armorVitalProcRestore} {armorVitalProcLabel} [Vital Armor]",
                        ChatMessageType.CombatSelf));
                }

                // splatter effects
                if (targetPlayer == null)
                {
                    Session.Network.EnqueueSend(new GameMessageSound(target.Guid, Sound.HitFlesh1, 0.5f));
                    if (damageEvent.Damage >= target.Health.MaxValue * 0.25f)
                    {
                        var painSound = (Sound)Enum.Parse(typeof(Sound), "Wound" + ThreadSafeRandom.Next(1, 3), true);
                        Session.Network.EnqueueSend(new GameMessageSound(target.Guid, painSound, 1.0f));
                    }
                    var splatter = (PlayScript)Enum.Parse(typeof(PlayScript), "Splatter" + GetSplatterHeight() + GetSplatterDir(target));
                    Session.Network.EnqueueSend(new GameMessageScript(target.Guid, splatter));
                }

                // handle Dirty Fighting
                if (GetCreatureSkill(Skill.DirtyFighting).AdvancementClass >= SkillAdvancementClass.Trained)
                    FightDirty(target, damageEvent.Weapon);
                
                target.EmoteManager.OnDamage(this);

                if (damageEvent.IsCritical)
                    target.EmoteManager.OnReceiveCritical(this);
            }

            // Resolute Blade: announce heal-on-crit if it fired
            if (resoluteHealApplied > 0 && !SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"+{resoluteHealApplied} health [Resolute Blade]",
                    ChatMessageType.CombatSelf));

            // Resolute Blade: bloodthirst burst on killing blow (heal + stamina)
            if (damageEvent.HasDamage
                && !target.IsAlive
                && targetPlayer == null
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsResoluteBlade) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Sword)
                && WeaponNameContains(damageEvent.Weapon, "tachi", "ken"))
            {
                var burstPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ResoluteKillBurstPct) ?? 0.0;
                if (burstPct > 0)
                {
                    var hpBurst = (int)Math.Round(Health.MaxValue * burstPct);
                    var stamBurst = (int)Math.Round(Stamina.MaxValue * burstPct);
                    if (hpBurst >= 1 && Health.Current < Health.MaxValue) UpdateVitalDelta(Health, hpBurst);
                    if (stamBurst >= 1 && Stamina.Current < Stamina.MaxValue) UpdateVitalDelta(Stamina, stamBurst);
                    ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpRed);
                    if (!SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                            $"Bloodthirst! +{(uint)Math.Max(0, hpBurst)} health, +{(uint)Math.Max(0, stamBurst)} stamina [Resolute Blade]",
                            ChatMessageType.CombatSelf));
                }
            }

            // Reaper's Atlatl: kill-fed self-heal proc on killing blow (see @lootconfig)
            // Only applies to Thrown weapon type (atlatls)
            if (damageEvent.HasDamage
                && !target.IsAlive
                && targetPlayer == null
                && damageEvent.Weapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsReapersAtlatl) == true
                && WeaponIsType(damageEvent.Weapon, WeaponType.Thrown))
            {
                var procChance = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillProc) ?? 0.0;

                // Backward compatibility: legacy Reaper drops stored this proc at 9017
                // before ReaperKillProc moved to its own unique property id.
                if (procChance <= 0.0)
                {
                    var legacyProcChance = damageEvent.Weapon.GetProperty((ACE.Entity.Enum.Properties.PropertyFloat)9017) ?? 0.0;
                    if (legacyProcChance > 0.0 && damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsBreachersCrossbow) != true)
                        procChance = legacyProcChance;
                }

                if (ThreadSafeRandom.Next(0.0f, 1.0f) < procChance)
                {
                    var healPct = damageEvent.Weapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.ReaperKillHealPct) ?? 0.0;
                    var heal = (int)Math.Round(Health.MaxValue * healPct);
                    if (heal >= 1 && Health.Current < Health.MaxValue)
                    {
                        UpdateVitalDelta(Health, heal);
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpRed);
                        if (!SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"Reaped! +{(uint)heal} health [Reaper's Atlatl]",
                                ChatMessageType.CombatSelf));
                    }
                }
            }

            if (targetPlayer == null)
                OnAttackMonster(target);

            return damageEvent;
        }

        /// <summary>
        /// Sets the creature that last attacked a player
        /// This is called when the player takes damage, evades, or resists a spell from a creature
        /// If the CurrentAttacker has changed, sends a network message to the player's client
        /// This enables the 'last attacker' functionality in the client, which is bound to the 'home' key by default
        /// </summary>
        public void SetCurrentAttacker(Creature currentAttacker)
        {
            if (currentAttacker == this || CurrentAttacker == currentAttacker.Guid.Full)
                return;

            CurrentAttacker = currentAttacker.Guid.Full;

            Session.Network.EnqueueSend(new GameMessagePrivateUpdateInstanceID(this, PropertyInstanceId.CurrentAttacker, currentAttacker.Guid.Full));
        }

        /// <summary>
        /// Called when a player hits a target
        /// </summary>
        public override void OnDamageTarget(WorldObject target, CombatType attackType, bool critical)
        {
            var attackSkill = GetCreatureSkill(GetCurrentWeaponSkill());
            var difficulty = GetTargetEffectiveDefenseSkill(target);

            Proficiency.OnSuccessUse(this, attackSkill, difficulty);
        }

        public override uint GetEffectiveAttackSkill()
        {
            var weapon = GetEquippedWeapon();
            var attackSkill = GetCreatureSkill(GetCurrentWeaponSkill()).Current;
            var offenseMod = GetWeaponOffenseModifier(this);
            var accuracyMod = GetAccuracyMod(weapon);

            attackSkill = (uint)Math.Round(attackSkill * accuracyMod * offenseMod);

            //if (IsExhausted)
                //attackSkill = GetExhaustedSkill(attackSkill);

            //var baseStr = offenseMod != 1.0f ? $" (base: {GetCreatureSkill(GetCurrentWeaponSkill()).Current})" : "";
            //Console.WriteLine("Attack skill: " + attackSkill + baseStr);

            return attackSkill;
        }

        public uint GetTargetEffectiveDefenseSkill(WorldObject target)
        {
            var creature = target as Creature;
            if (creature == null) return 0;

            var attackType = GetCombatType();
            var defenseSkill = attackType == CombatType.Missile ? Skill.MissileDefense : Skill.MeleeDefense;
            var defenseMod = defenseSkill == Skill.MeleeDefense ? GetWeaponMeleeDefenseModifier(creature) : 1.0f;
            var effectiveDefense = (uint)Math.Round(creature.GetCreatureSkill(defenseSkill).Current * defenseMod);

            // Warden's Maul: flat defense-skill penalty while concussed
            if (creature.ConcussedUntil > DateTime.UtcNow && creature.ConcussedPenalty > 0)
                effectiveDefense = effectiveDefense > creature.ConcussedPenalty ? effectiveDefense - creature.ConcussedPenalty : 0u;

            if (creature.IsExhausted) effectiveDefense = 0;

            //var baseStr = defenseMod != 1.0f ? $" (base: {creature.GetCreatureSkill(defenseSkill).Current})" : "";
            //Console.WriteLine("Defense skill: " + effectiveDefense + baseStr);

            return effectiveDefense;
        }

        /// <summary>
        /// Returns a modifier to the player's defense skill, based on current motion state
        /// </summary>
        /// <returns></returns>
        public float GetDefenseStanceMod()
        {
            if (IsJumping)
                return 0.5f;

            if (IsLoggingOut)
                return 0.8f;

            if (CombatMode != CombatMode.NonCombat)
                return 1.0f;

            var forwardCommand = CurrentMovementData.MovementType == MovementType.Invalid && CurrentMovementData.Invalid != null ?
                CurrentMovementData.Invalid.State.ForwardCommand : MotionCommand.Invalid;

            switch (forwardCommand)
            {
                // TODO: verify multipliers
                case MotionCommand.Crouch:
                    return 0.4f;
                case MotionCommand.Sitting:
                    return 0.3f;
                case MotionCommand.Sleeping:
                    return 0.2f;
                default:
                    return 1.0f;
            }
        }

        /// <summary>
        /// Called when player successfully avoids an attack
        /// </summary>
        public override void OnEvade(WorldObject attacker, CombatType attackType)
        {
            var creatureAttacker = attacker as Creature;

            if (creatureAttacker != null)
                SetCurrentAttacker(creatureAttacker);

            if (UnderLifestoneProtection)
                return;

            // http://asheron.wikia.com/wiki/Attributes

            // Endurance will also make it less likely that you use a point of stamina to successfully evade a missile or melee attack.
            // A player is required to have Melee Defense for melee attacks or Missile Defense for missile attacks trained or specialized
            // in order for this specific ability to work. This benefit is tied to Endurance only, and it caps out at around a 75% chance
            // to avoid losing a point of stamina per successful evasion.

            var defenseSkillType = attackType == CombatType.Missile ? Skill.MissileDefense : Skill.MeleeDefense;
            var defenseSkill = GetCreatureSkill(defenseSkillType);

            if (CombatMode != CombatMode.NonCombat)
            {
                if (defenseSkill.AdvancementClass >= SkillAdvancementClass.Trained)
                {
                    var enduranceBase = (int)Endurance.Base;

                    // TODO: find exact formula / where it caps out at 75%

                    // more literal / linear formula
                    //var noStaminaUseChance = (enduranceBase - 50) / 320.0f;

                    // gdle curve-based formula, caps at 300 instead of 290
                    var noStaminaUseChance = (enduranceBase * enduranceBase * 0.000005f) + (enduranceBase * 0.00124f) - 0.07f;

                    noStaminaUseChance = Math.Clamp(noStaminaUseChance, 0.0f, 0.75f);

                    //Console.WriteLine($"NoStaminaUseChance: {noStaminaUseChance}");

                    if (noStaminaUseChance <= ThreadSafeRandom.Next(0.0f, 1.0f))
                        UpdateVitalDelta(Stamina, -1);
                }
                else
                    UpdateVitalDelta(Stamina, -1);
            }
            else
            {
                // if the player is in non-combat mode, no stamina is consumed on evade
                // reference: https://youtu.be/uFoQVgmSggo?t=145
                // from the dm guide, page 147: "if you are not in Combat mode, you lose no Stamina when an attack is thrown at you"

                //UpdateVitalDelta(Stamina, -1);
            }

            if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                Session.Network.EnqueueSend(new GameEventEvasionDefenderNotification(Session, attacker.Name));

            if (creatureAttacker == null)
                return;

            var difficulty = creatureAttacker.GetCreatureSkill(creatureAttacker.GetCurrentWeaponSkill()).Current;
            // attackMod?
            Proficiency.OnSuccessUse(this, defenseSkill, difficulty);
        }

        public BaseDamageMod GetBaseDamageMod(WorldObject damageSource)
        {
            if (damageSource == this)
            {
                if (AttackType == AttackType.Punch)
                    damageSource = HandArmor;
                else if (AttackType == AttackType.Kick)
                    damageSource = FootArmor;

                // Check if the armor piece has unarmed damage properties (DerpACE feature)
                if (damageSource != null && (damageSource.UnarmedBaseDamage ?? 0) > 0)
                {
                    var damage = damageSource.UnarmedBaseDamage.Value;
                    var variance = (float)(damageSource.UnarmedDamageVariance ?? 0.7);
                    // DerpACE: Apply enchantments (Blood Drinker, etc.) to unarmed armor damage
                    var baseMod = new BaseDamageMod(new BaseDamage(damage, variance), this, damageSource);
                    ApplySteelBootDamageBonus(baseMod);
                    return baseMod;
                }

                // no weapon, no hand or foot armor (or armor has no unarmed damage)
                if (damageSource?.Damage == null)
                {
                    var baseMod = HeritageGroup == HeritageGroup.Olthoi
                        ? new BaseDamageMod(new BaseDamage(130, 0.75f))
                        : GetTrulyUnarmedBaseDamageMod();

                    ApplySteelBootDamageBonus(baseMod);
                    return baseMod;
                }
                else
                {
                    // armor has traditional weapon damage (cestus, katar, etc)
                    var baseMod = damageSource.GetDamageMod(this, damageSource);
                    ApplySteelBootDamageBonus(baseMod);
                    return baseMod;
                }
            }
            return damageSource.GetDamageMod(this);
        }

        /// <summary>
        /// Returns the BaseDamageMod for a truly-unarmed player attack (no weapon, no hand/foot armor).
        ///
        /// When the unarmed_combat_upgrades feature is enabled, base damage scales linearly with the
        /// player's Light Weapons skill from unarmed_min_base_damage at 0 skill up to
        /// unarmed_max_base_damage at unarmed_skill_for_max_damage skill. This lets bare-fisted
        /// players reach roughly tier 5 unarmed weapon damage at high skill.
        ///
        /// When the feature is disabled, returns the legacy (2, 0.75) damage.
        /// </summary>
        private BaseDamageMod GetTrulyUnarmedBaseDamageMod()
        {
            if (!PropertyManager.GetBool("unarmed_combat_upgrades").Item)
                return new BaseDamageMod(new BaseDamage(2, 0.75f));

            var minDamage = PropertyManager.GetDouble("unarmed_min_base_damage").Item;
            var maxDamage = PropertyManager.GetDouble("unarmed_max_base_damage").Item;
            var skillForMax = PropertyManager.GetDouble("unarmed_skill_for_max_damage").Item;
            var variance = (float)PropertyManager.GetDouble("unarmed_variance").Item;

            if (skillForMax <= 0)
                skillForMax = 1;

            var lightWeapons = GetCreatureSkill(Skill.LightWeapons).Current;
            var ratio = Math.Clamp(lightWeapons / skillForMax, 0.0, 1.0);
            var scaledDamage = minDamage + (maxDamage - minDamage) * ratio;

            var damage = (int)Math.Round(Math.Max(1, scaledDamage));
            return new BaseDamageMod(new BaseDamage(damage, variance));
        }

        /// <summary>
        /// Applies a damage bonus to <paramref name="baseMod"/> when the player is wearing
        /// Steel-material boots and is striking at high attack power. The bonus scales linearly
        /// with both the boots' armor level and the player's PowerLevel above the configured threshold.
        ///
        /// Disabled when unarmed_combat_upgrades is false.
        /// </summary>
        private void ApplySteelBootDamageBonus(BaseDamageMod baseMod)
        {
            if (baseMod == null || !PropertyManager.GetBool("unarmed_combat_upgrades").Item)
                return;

            var boots = FootArmor;
            if (boots == null || boots.MaterialType != ACE.Entity.Enum.MaterialType.Steel)
                return;

            var armorLevel = boots.ArmorLevel ?? 0;
            if (armorLevel <= 0)
                return;

            var threshold = (float)PropertyManager.GetDouble("unarmed_steel_boot_power_threshold").Item;
            threshold = Math.Clamp(threshold, 0.0f, 1.0f);

            if (PowerLevel <= threshold)
                return;

            var powerScale = threshold >= 1.0f ? 1.0f : (PowerLevel - threshold) / (1.0f - threshold);
            powerScale = Math.Clamp(powerScale, 0.0f, 1.0f);

            var perAl = (float)PropertyManager.GetDouble("unarmed_steel_boot_damage_per_al").Item;
            var bonus = armorLevel * perAl * powerScale;

            if (bonus > 0)
                baseMod.DamageBonus += bonus;
        }

        public override float GetPowerMod(WorldObject weapon)
        {
            if (weapon == null || !weapon.IsRanged)
                return PowerLevel + 0.5f;
            else
                return 1.0f;
        }

        public override float GetAccuracyMod(WorldObject weapon)
        {
            if (weapon != null && weapon.IsRanged)
                return AccuracyLevel + 0.6f;
            else
                return 1.0f;
        }

        public float GetPowerAccuracyBar()
        {
            return GetCombatType() == CombatType.Missile ? AccuracyLevel : PowerLevel;
        }

        public Sound GetHitSound(WorldObject source, BodyPart bodyPart)
        {
            /*var creature = source as Creature;
            var armors = creature.GetArmor(bodyPart);

            foreach (var armor in armors)
            {
                var material = armor.GetProperty(PropertyInt.MaterialType) ?? 0;
                //Console.WriteLine("Name: " + armor.Name + " | Material: " + material);
            }*/
            return Sound.HitFlesh1;
        }

        /// <summary>
        /// Simplified player take damage function, only called for DoTs currently
        /// </summary>
        public override void TakeDamageOverTime(float _amount, DamageType damageType)
        {
            if (Invincible || IsDead) return;

            // check lifestone protection
            if (UnderLifestoneProtection)
            {
                HandleLifestoneProtection();
                return;
            }

            var amount = (uint)Math.Round(_amount);
            var percent = (float)amount / Health.MaxValue;

            // update health
            var damageTaken = (uint)-UpdateVitalDelta(Health, (int)-amount);

            // update stamina
            //UpdateVitalDelta(Stamina, -1);

            //if (Fellowship != null)
                //Fellowship.OnVitalUpdate(this);

            // send damage text message
            //if (PropertyManager.GetBool("show_dot_messages").Item)
            //{
                var nether = damageType == DamageType.Nether ? "nether " : "";
                var chatMessageType = damageType == DamageType.Nether ? ChatMessageType.Magic : ChatMessageType.Combat;
                var text = $"You receive {amount} points of periodic {nether}damage.";
                SendMessage(text, chatMessageType);
            //}

            // splatter effects
            //var splatter = new GameMessageScript(Guid, (PlayScript)Enum.Parse(typeof(PlayScript), "Splatter" + creature.GetSplatterHeight() + creature.GetSplatterDir(this)));  // not sent in retail, but great visual indicator?
            var splatter = new GameMessageScript(Guid, damageType == DamageType.Nether ? PlayScript.HealthDownVoid : PlayScript.DirtyFightingDamageOverTime);
            EnqueueBroadcast(splatter);

            if (Health.Current <= 0)
            {
                // since damage over time is possibly combined from multiple sources,
                // sending a message to the last damager here could be tricky..

                // TODO: get last damager from dot stack instead? 
                OnDeath(DamageHistory.LastDamager, damageType, false);
                Die();

                return;
            }

            if (percent >= 0.1f)
                EnqueueBroadcast(new GameMessageSound(Guid, Sound.Wound1, 1.0f));
        }

        public int TakeDamage(WorldObject source, DamageEvent damageEvent)
        {
            return TakeDamage(source, damageEvent.DamageType, damageEvent.Damage, damageEvent.BodyPart, damageEvent.IsCritical, damageEvent.AttackConditions);
        }

        /// <summary>
        /// DerpACE: handles Mob-Modifier on-hit procs (Vampiric lifesteal, Thief pickpocket).
        /// Thief steal targets any item with ItemType.PromissoryNote (tradenote / MMD), covering all denominations including custom ones.
        /// Called from <see cref="TakeDamage(WorldObject, DamageType, float, BodyPart, bool, AttackConditions)"/>
        /// after the player's HP has been updated and damage history recorded.
        /// </summary>
        private void TryProcMobModifiers(Creature attacker, uint damageDealt)
        {
            // Vampiric: heal mob for a fraction of damage just dealt
            if (attacker.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsVampiricMob) == true)
            {
                var pct = attacker.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.VampiricLifestealPct) ?? 0.0;
                if (pct > 0 && attacker.Health != null && attacker.Health.Current < attacker.Health.MaxValue)
                {
                    var heal = (int)System.Math.Round(damageDealt * pct);
                    if (heal >= 1)
                    {
                        attacker.UpdateVitalDelta(attacker.Health, heal);
                        attacker.ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpRed);
                        if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"{attacker.Name} drains {heal} health from you. [Vampiric]",
                                ChatMessageType.CombatEnemy));
                    }
                }
            }

            // Thief: chance to pickpocket a tradenote stack (only if mob isn't already holding one)
            if (attacker.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsThiefMob) == true
                && attacker.StolenTradeNoteWcid == 0
                && ThreadSafeRandom.Next(0.0f, 1.0f) < ACE.Server.Managers.DerpACEConfig.ThiefStealProc)
            {
                // Pick smallest tradenote stack (least painful, still meaningful)
                // GetAllPossessions covers main pack + side packs + equipped; filter to PromissoryNote (tradenote) only.
                // Using ItemType instead of a WCID allowlist so any denomination (incl. custom server tradenotes) is fair game.
                var tradenote = GetAllPossessions()
                    .Where(i => (i.ItemType & ACE.Entity.Enum.ItemType.PromissoryNote) != 0 && i.WielderId == null)
                    .OrderBy(i => (i.StackSize ?? 1) * (i.Value ?? 0))
                    .FirstOrDefault();

                if (tradenote == null)
                {
                    // Attempted but nothing to steal
                    if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                            $"{attacker.Name} attempts to pickpocket you but finds nothing to steal. [Thief]",
                            ChatMessageType.CombatEnemy));
                }
                else
                {
                    var wcid = tradenote.WeenieClassId;
                    var stackAmount = tradenote.StackSize ?? 1;
                    var amount = System.Math.Min(stackAmount, ThreadSafeRandom.Next(1, 3));

                    // Warn the player the attempt is happening before we know if it succeeds
                    if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                        Session.Network.EnqueueSend(new GameMessageSystemChat(
                            $"{attacker.Name} reaches for your tradenotes! [Thief]",
                            ChatMessageType.CombatEnemy));

                    bool stealSuccess;
                    if (stackAmount <= amount)
                    {
                        stealSuccess = TryRemoveFromInventoryWithNetworking(tradenote.Guid, out var removed, RemoveFromInventoryAction.SpendItem);
                        if (stealSuccess)
                            removed.Destroy();
                    }
                    else
                    {
                        tradenote.SetStackSize(stackAmount - amount);
                        Session.Network.EnqueueSend(new GameMessageSetStackSize(tradenote));
                        stealSuccess = true;
                    }

                    if (stealSuccess)
                    {
                        attacker.StolenTradeNoteWcid = wcid;
                        attacker.StolenTradeNoteAmount = amount;
                        attacker.StolenFromGuid = Guid;

                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthDownYellow);
                        if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"Pickpocketed! {attacker.Name} stole {amount} tradenote(s) (max 3 per hit). Kill it to recover them. [Thief]",
                                ChatMessageType.CombatEnemy));
                    }
                    else
                    {
                        if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"{attacker.Name} tried to steal your tradenotes but failed. [Thief]",
                                ChatMessageType.CombatEnemy));
                    }
                }
            }
        }

        /// <summary>
        /// DerpACE: Check all equipped armor for vital-replenish procs (health/stam/mana) when hit.
        /// Called after the player's HP has been updated and damage history recorded.
        /// </summary>
        private void TryProcArmorVitalOnHit()
        {
            if (EquippedObjects == null)
                return;

            foreach (var (_, armorItem) in EquippedObjects)
            {
                if (armorItem == null)
                    continue;

                var procAmount = armorItem.ArmorVitalProcAmount ?? 0;
                var procChance = armorItem.ArmorVitalProcChance ?? 0.0;

                if (procAmount <= 0 || procChance <= 0.0)
                    continue;

                // Roll for proc
                if (ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                    continue;

                // Determine which vital to replenish based on armor's gear bonus type
                var vital = (Entity.CreatureVital)null;
                string vitalName = "";

                if ((armorItem.GearMaxHealth ?? 0) > 0)
                {
                    vital = Health;
                    vitalName = "health";
                }
                else if ((armorItem.GearMaxStamina ?? 0) > 0)
                {
                    vital = Stamina;
                    vitalName = "stamina";
                }
                else if ((armorItem.GearMaxMana ?? 0) > 0)
                {
                    vital = Mana;
                    vitalName = "mana";
                }

                if (vital == null || vital.Current >= vital.MaxValue)
                    continue;

                // Apply replenish
                var actualAmount = (int)Math.Min(procAmount, vital.MaxValue - vital.Current);
                if (actualAmount >= 1)
                {
                    UpdateVitalDelta(vital, actualAmount);

                    // Visual feedback
                    if (vitalName == "health")
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpBlue);
                    else if (vitalName == "stamina")
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.AetheriaLevelUp);
                    else if (vitalName == "mana")
                        ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpBlue);

                    Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"Your {armorItem.NameWithMaterial} replenishes {actualAmount} {vitalName}!",
                        ChatMessageType.Craft));
                }

                // Only proc one armor piece per hit
                break;
            }
        }

        /// <summary>
        /// Applies damages to a player from a physical damage source
        /// </summary>
        public int TakeDamage(WorldObject source, DamageType damageType, float _amount, BodyPart bodyPart, bool crit = false, AttackConditions attackConditions = AttackConditions.None)
        {
            if (Invincible || IsDead) return 0;

            if (source is Creature creatureAttacker)
                SetCurrentAttacker(creatureAttacker);

            // check lifestone protection
            if (UnderLifestoneProtection)
            {
                HandleLifestoneProtection();
                return 0;
            }

            if (_amount < 0)
            {
                log.Error($"{Name}.TakeDamage({source?.Name} ({source?.Guid}), {damageType}, {_amount}) - negative damage, this shouldn't happen");
                return 0;
            }

            var amount = (uint)Math.Round(_amount);
            var percent = (float)amount / Health.MaxValue;

            var equippedCloak = EquippedCloak;

            if (equippedCloak != null && Cloak.HasDamageProc(equippedCloak) && Cloak.RollProc(equippedCloak, percent))
            {
                var reducedAmount = Cloak.GetReducedAmount(source, amount);

                Cloak.ShowMessage(this, source, amount, reducedAmount);

                amount = reducedAmount;
                percent = (float)amount / Health.MaxValue;
            }

            // update health
            var damageTaken = (uint)-UpdateVitalDelta(Health, (int)-amount);
            DamageHistory.Add(source, damageType, damageTaken);

            // DerpACE: Mob Modifier on-hit procs (Vampiric lifesteal, Thief pickpocket)
            if (source is Creature mobAttacker && damageTaken > 0)
                TryProcMobModifiers(mobAttacker, damageTaken);

            // DerpACE: Armor Vital Proc (replenish health/stamina/mana when hit)
            if (damageTaken > 0)
                TryProcArmorVitalOnHit();

            // update stamina
            if (CombatMode != CombatMode.NonCombat)
            {
                // if the player is in non-combat mode, no stamina is consumed on evade
                // reference: https://youtu.be/uFoQVgmSggo?t=145
                // from the dm guide, page 147: "if you are not in Combat mode, you lose no Stamina when an attack is thrown at you"

                UpdateVitalDelta(Stamina, -1);
            }

            //if (Fellowship != null)
                //Fellowship.OnVitalUpdate(this);

            if (Health.Current <= 0)
            {
                OnDeath(new DamageHistoryInfo(source), damageType, crit);
                Die();
                return (int)damageTaken;
            }

            if (!BodyParts.Indices.TryGetValue(bodyPart, out var iDamageLocation))
            {
                log.Warn($"{Name}.TakeDamage({source.Name}, {damageType}, {amount}, {bodyPart}, {crit}): avoided crash for bad damage location");
                return 0;
            }
            var damageLocation = (DamageLocation)iDamageLocation;

            // send network messages
            if (source is Creature creature)
            {
                if (!SquelchManager.Squelches.Contains(source, ChatMessageType.CombatEnemy))
                    Session.Network.EnqueueSend(new GameEventDefenderNotification(Session, creature.Name, damageType, percent, amount, damageLocation, crit, attackConditions));

                var hitSound = new GameMessageSound(Guid, GetHitSound(source, bodyPart), 1.0f);
                var splatter = new GameMessageScript(Guid, (PlayScript)Enum.Parse(typeof(PlayScript), "Splatter" + creature.GetSplatterHeight() + creature.GetSplatterDir(this)));
                EnqueueBroadcast(hitSound, splatter);
            }

            if (percent >= 0.1f)
            {
                // Wound1 - Aahhh!    - elemental attacks above some threshold
                // Wound2 - Deep Ugh! - bludgeoning attacks above some threshold
                // Wound3 - Ooh!      - slashing / piercing / undef attacks above some threshold

                var woundSound = Sound.Wound3;

                if (damageType == DamageType.Bludgeon)
                    woundSound = Sound.Wound2;

                else if ((damageType & DamageType.Elemental) != 0)
                    woundSound = Sound.Wound1;

                EnqueueBroadcast(new GameMessageSound(Guid, woundSound, 1.0f));
            }

            if (equippedCloak != null && Cloak.HasProcSpell(equippedCloak))
                Cloak.TryProcSpell(this, source, equippedCloak, percent);

            // Fencer's Blade: deflect proc — reflects 10% of incoming damage back at the attacker
            if (source is Creature fencerAttacker && fencerAttacker.IsAlive && damageTaken > 0)
            {
                var fencerWeapon = GetEquippedWeapon();
                if (fencerWeapon?.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsFencerBlade) == true)
                {
                    var deflectChance = fencerWeapon.GetProperty(ACE.Entity.Enum.Properties.PropertyFloat.FencerDeflectChance) ?? 0.0;
                    if (ThreadSafeRandom.Next(0.0f, 1.0f) < deflectChance)
                    {
                        var reflectAmount = (float)Math.Round(damageTaken * 0.10);
                        if (reflectAmount >= 1.0f)
                        {
                            fencerAttacker.TakeDamage(this, DamageType.Pierce, reflectAmount);
                            Session.Network.EnqueueSend(new GameMessageSystemChat(
                                $"[Fencer's Blade] Deflected! -{(uint)reflectAmount} [{fencerAttacker.Name}]",
                                ChatMessageType.CombatSelf));
                        }
                    }
                }
            }

            // if player attacker, update PK timer
            if (source is Player attacker)
                UpdatePKTimers(attacker, this);

            return (int)damageTaken;
        }

        public string GetArmorType(BodyPart bodyPart)
        {
            // Flesh, Leather, Chain, Plate
            // for hit sounds
            return null;
        }

        /// <summary>
        /// Returns the total burden of items held in both hands
        /// (main hand and offhand)
        /// </summary>
        public int GetHeldItemBurden()
        {
            var mainhand = GetEquippedMainHand();
            var offhand = GetEquippedOffHand();

            var mainhandBurden = mainhand?.EncumbranceVal ?? 0;
            var offhandBurden = offhand?.EncumbranceVal ?? 0;

            return mainhandBurden + offhandBurden;
        }

        public float GetStaminaMod()
        {
            var endurance = (int)Endurance.Base;

            // more literal / linear formula
            var staminaMod = 1.0f - (endurance - 50) / 480.0f;

            // gdle curve-based formula, caps at 300 instead of 290
            //var staminaMod = (endurance * endurance * -0.000003175f) - (endurance * 0.0008889f) + 1.052f;

            staminaMod = Math.Clamp(staminaMod, 0.5f, 1.0f);

            // this is also specific to gdle,
            // additive luck which can send the base stamina way over 1.0
            /*var luck = ThreadSafeRandom.Next(0.0f, 1.0f);
            staminaMod += luck;*/

            return staminaMod;
        }

        /// <summary>
        /// Calculates the amount of stamina required to perform this attack
        /// </summary>
        public int GetAttackStamina(PowerAccuracy powerAccuracy)
        {
            // Stamina cost for melee and missile attacks is based on the total burden of what you are holding
            // in your hands (main hand and offhand), and your power/accuracy bar.

            // Attacking(Low power / accuracy bar)   1 point per 700 burden units
            //                                       1 point per 1200 burden units
            //                                       1.5 points per 1600 burden units
            // Attacking(Mid power / accuracy bar)   1 point per 700 burden units
            //                                       2 points per 1200 burden units
            //                                       3 points per 1600 burden units
            // Attacking(High power / accuracy bar)  2 point per 700 burden units
            //                                       4 points per 1200 burden units
            //                                       6 points per 1600 burden units

            // The higher a player's base Endurance, the less stamina one uses while attacking. This benefit is tied to Endurance only,
            // and caps out at 50% less stamina used per attack. Scaling is similar to other Endurance bonuses. Applies only to players.

            // When stamina drops to 0, your melee and missile defenses also drop to 0 and you will be incapable of attacking.
            // In addition, you will suffer a 50% penalty to your weapon skill. This applies to players and creatures.

            var burden = GetHeldItemBurden();

            var baseCost = StaminaTable.GetStaminaCost(powerAccuracy, burden);

            var staminaMod = GetStaminaMod();

            var staminaCost = Math.Max(baseCost * staminaMod, 1);

            //Console.WriteLine($"GetAttackStamina({powerAccuracy}) - burden: {burden}, baseCost: {baseCost}, staminaMod: {staminaMod}, staminaCost: {staminaCost}");

            return (int)Math.Round(staminaCost);
        }

        /// <summary>
        /// Returns the damage rating modifier for an applicable Recklessness attack
        /// </summary>
        /// <param name="powerAccuracyBar">The 0.0 - 1.0 power/accurary bar</param>
        public float GetRecklessnessMod(/*float powerAccuracyBar*/)
        {
            // ensure melee or missile combat mode
            if (CombatMode != CombatMode.Melee && CombatMode != CombatMode.Missile)
                return 1.0f;

            var skill = GetCreatureSkill(Skill.Recklessness);

            // recklessness skill must be either trained or specialized to use
            if (skill.AdvancementClass < SkillAdvancementClass.Trained)
                return 1.0f;

            // recklessness is active when attack bar is between 20% and 80% (according to wiki)
            // client attack bar range seems to indicate this might have been updated, between 10% and 90%?
            var powerAccuracyBar = GetPowerAccuracyBar();
            //if (powerAccuracyBar < 0.2f || powerAccuracyBar > 0.8f)
            if (powerAccuracyBar < 0.1f || powerAccuracyBar > 0.9f)
                return 1.0f;

            // recklessness only applies to non-critical hits,
            // which is handled outside of this method.

            // damage rating is increased by 20 for specialized, and 10 for trained.
            // incoming non-critical damage from all sources is increased by the same.
            var damageRating = skill.AdvancementClass == SkillAdvancementClass.Specialized ? 20 : 10;

            // if recklessness skill is lower than current attack skill (as determined by your equipped weapon)
            // then the damage rating is reduced proportionately. The damage rating caps at 10 for trained
            // and 20 for specialized, so there is no reason to raise the skill above your attack skill.
            var attackSkill = GetCreatureSkill(GetCurrentAttackSkill());

            if (skill.Current < attackSkill.Current)
            {
                var scale = (float)skill.Current / attackSkill.Current;
                damageRating = (int)Math.Round(damageRating * scale);
            }

            // The damage rating adjustment for incoming damage is also adjusted proportinally if your Recklessness skill
            // is lower than your active attack skill

            var recklessnessMod = GetDamageRating(damageRating);    // trained DR 1.10 = 10% additional damage
                                                                    // specialized DR 1.20 = 20% additional damage
            return recklessnessMod;
        }

        /// <summary>
        /// Returns TRUE if this player is PK and died to another player
        /// </summary>
        public bool IsPKDeath(DamageHistoryInfo topDamager)
        {
            return IsPKDeath(topDamager?.Guid.Full);
        }

        public bool IsPKDeath(uint? killerGuid)
        {
            return PlayerKillerStatus.HasFlag(PlayerKillerStatus.PK) && new ObjectGuid(killerGuid ?? 0).IsPlayer() && killerGuid != Guid.Full;
        }

        /// <summary>
        /// Returns TRUE if this player is PKLite and died to another player
        /// </summary>
        public bool IsPKLiteDeath(DamageHistoryInfo topDamager)
        {
            return IsPKLiteDeath(topDamager?.Guid.Full);
        }

        public bool IsPKLiteDeath(uint? killerGuid)
        {
            return PlayerKillerStatus.HasFlag(PlayerKillerStatus.PKLite) && new ObjectGuid(killerGuid ?? 0).IsPlayer() && killerGuid != Guid.Full;
        }

        public CombatMode LastCombatMode;

        public const float UseTimeEpsilon = 0.05f;

        /// <summary>
        /// This method processes the Game Action (F7B1) Change Combat Mode (0x0053)
        /// </summary>
        public void HandleActionChangeCombatMode(CombatMode newCombatMode, bool forceHandCombat = false, Action callback = null)
        {
            //log.Info($"{Name}.HandleActionChangeCombatMode({newCombatMode})");

            // Make sure the player doesn't have an invalid weapon setup (e.g. sword + wand)
            if (!CheckWeaponCollision(null, null, newCombatMode))
            {
                Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ActionCancelled)); // "Action cancelled!"

                // Go back to non-Combat mode
                float animTime = 0.0f, queueTime = 0.0f;
                animTime = SetCombatMode(newCombatMode, out queueTime, false, true);

                var actionChain = new ActionChain();
                actionChain.AddDelaySeconds(animTime);
                actionChain.AddAction(this, () =>
                {
                    SetCombatMode(CombatMode.NonCombat);
                });
                actionChain.EnqueueChain();

                NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
                return;
            }

            LastCombatMode = newCombatMode;
            
            if (DateTime.UtcNow >= NextUseTime.AddSeconds(UseTimeEpsilon))
                HandleActionChangeCombatMode_Inner(newCombatMode, forceHandCombat, callback);
            else
            {
                var actionChain = new ActionChain();
                actionChain.AddDelaySeconds((NextUseTime - DateTime.UtcNow).TotalSeconds + UseTimeEpsilon);
                actionChain.AddAction(this, () => HandleActionChangeCombatMode_Inner(newCombatMode, forceHandCombat, callback));
                actionChain.EnqueueChain();
            }

            if (IsAfk)
                HandleActionSetAFKMode(false);
        }

        public void HandleActionChangeCombatMode_Inner(CombatMode newCombatMode, bool forceHandCombat = false, Action callback = null)
        {
            //log.Info($"{Name}.HandleActionChangeCombatMode_Inner({newCombatMode})");

            var currentCombatStance = GetCombatStance();

            var missileWeapon = GetEquippedMissileWeapon();
            var caster = GetEquippedWand();

            if (CombatMode == CombatMode.Magic && MagicState.IsCasting)
                FailCast();

            HandleActionCancelAttack();

            float animTime = 0.0f, queueTime = 0.0f;

            switch (newCombatMode)
            {
                case CombatMode.NonCombat:
                    {
                        switch (currentCombatStance)
                        {
                            case MotionStance.BowCombat:
                            case MotionStance.CrossbowCombat:
                            case MotionStance.AtlatlCombat:
                                {
                                    var equippedAmmo = GetEquippedAmmo();
                                    if (equippedAmmo != null)
                                        ClearChild(equippedAmmo); // We must clear the placement/parent when going back to peace
                                    break;
                                }
                        }
                        break;
                    }
                case CombatMode.Melee:

                    // todo expand checks
                    if (!forceHandCombat && (missileWeapon != null || caster != null))
                    {
                        // client has already independently brought the melee bar up by this point, revert and sync everything back up
                        SetCombatMode(CombatMode.NonCombat);
                        return;
                    }

                    break;

                case CombatMode.Missile:
                    {
                        if (missileWeapon == null)
                        {
                            // client has already independently switched to missile mode by this point,
                            // so instead of simply returning here, we need to deny the request by reverting to either the current server combat state, or switching to NonCombat to maintain client sync
                            // this is especially important for missile, because the client is unable to break out of this bugged state for this mode specifically
                            // see: ClientCombatSystem::PlayerInReadyPosition

                            SetCombatMode(CombatMode.NonCombat);
                            return;
                        }

                        switch (currentCombatStance)
                        {
                            case MotionStance.BowCombat:
                            case MotionStance.CrossbowCombat:
                            case MotionStance.AtlatlCombat:
                                {
                                    var equippedAmmo = GetEquippedAmmo();
                                    if (equippedAmmo == null)
                                    {
                                        animTime = SetCombatMode(newCombatMode, out queueTime);

                                        var actionChain = new ActionChain();
                                        actionChain.AddDelaySeconds(animTime);
                                        actionChain.AddAction(this, () =>
                                        {
                                            Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, "You are out of ammunition!"));
                                            SetCombatMode(CombatMode.NonCombat);
                                        });
                                        actionChain.EnqueueChain();

                                        NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
                                        return;
                                    }
                                    else
                                    {
                                        // We must set the placement/parent when going into combat
                                        equippedAmmo.Placement = ACE.Entity.Enum.Placement.RightHandCombat;
                                        equippedAmmo.ParentLocation = ACE.Entity.Enum.ParentLocation.RightHand;
                                    }
                                    break;
                                }
                        }
                        break;
                    }

                case CombatMode.Magic:

                    // todo expand checks
                    if (caster == null)
                    {
                        // client has already independently brought the magic bar up by this point, revert and sync everything back up
                        SetCombatMode(CombatMode.NonCombat);
                        return;
                    }

                    break;

            }

            // animTime already includes queueTime
            animTime = SetCombatMode(newCombatMode, out queueTime, forceHandCombat);
            //log.Info($"{Name}.HandleActionChangeCombatMode_Inner({newCombatMode}) - animTime: {animTime}, queueTime: {queueTime}");

            NextUseTime = DateTime.UtcNow.AddSeconds(animTime);

            if (MagicState.IsCasting && RecordCast.Enabled)
                RecordCast.OnSetCombatMode(newCombatMode);

            if (callback != null)
            {
                var callbackChain = new ActionChain();
                callbackChain.AddDelaySeconds(animTime);
                callbackChain.AddAction(this, callback);
                callbackChain.EnqueueChain();
            }
        }

        public override bool CanDamage(Creature target)
        {
            return target.Attackable && !target.Teleporting && !(target is CombatPet);
        }

        // http://acpedia.org/wiki/Announcements_-_2002/04_-_Betrayal

        // Some combination of strength and endurance (the two are roughly of equivalent importance) now allows one to have a level of "natural resistances" to the 7 damage types,
        // and to partially resist drain health and harm attacks.

        // This caps out at a 50% resistance (the equivalent to level 5 life prots) to these damage types.

        // This resistance is not additive to life protections: higher level life protections will overwrite these natural resistances,
        // although life vulns will take these natural resistances into account, if the player does not have a higher level life protection cast upon them.

        // For example, a player will not get a free protective bonus from natural resistances if they have both Prot 7 and Vuln 7 cast upon them.
        // The Prot and Vuln will cancel each other out, and since the Prot has overwritten the natural resistances, there will be no resistance bonus.

        // The natural resistances, drain resistances, and regeneration rate info are now visible on the Character Information Panel, in what was once the Burden panel.

        // The 5 categories for the endurance benefits are, in order from lowest benefit to highest: Poor, Mediocre, Hardy, Resilient, and Indomitable,
        // with each range of benefits divided up equally amongst the 5 (e.g. Poor describes having anywhere from 1-10% resistance against drain health attacks, etc.).

        // A few other important notes:

        // - The abilities that Endurance or Endurance/Strength conveys are not increased by Strength or Endurance buffs.
        //   It is the raw Strength and/or Endurance scores that determine the various bonuses.
        // - For April, natural resistances will offer some protection versus hollow type damage, whether it is from a Hollow Minion or a Hollow weapon. This will be changed in May.
        // - These abilities are player-only, creatures with high endurance will not benefit from any of these changes.
        // - Come May, you can type @help endurance for a summary of the April changes to Endurance.

        public override float GetNaturalResistance(DamageType damageType)
        {
            if (damageType == DamageType.Undef)
                return 1.0f;

            // http://acpedia.org/wiki/Announcements_-_11th_Anniversary_Preview#Void_Magic_and_You.21
            // Creatures under Asheron’s protection take half damage from any nether type spell.
            if (damageType == DamageType.Nether)
                return 0.5f;

            // base strength and endurance give the player a natural resistance to damage,
            // which caps at 50% (equivalent to level 5 life prots)
            // these do not stack with life protection spells

            // - natural resistances are ignored by hollow damage

            var strAndEnd = Strength.Base + Endurance.Base;

            if (strAndEnd <= 200)
                return 1.0f;

            var naturalResistance = 1.0f - (float)(strAndEnd - 200) / 300 * 0.5f;
            naturalResistance = Math.Max(naturalResistance, 0.5f);

            return naturalResistance;
        }

        public string GetNaturalResistanceString(ResistanceType resistanceType)
        {
            var strAndEnd = Strength.Base + Endurance.Base;

            if (strAndEnd > 440)        return "Indomitable";
            else if (strAndEnd > 380)   return "Resilient";
            else if (strAndEnd > 320)   return "Hardy";
            else if (strAndEnd > 260)   return "Mediocre";
            else if (strAndEnd > 200)   return "Poor";
            else
                return "None";
        }

        public string GetRegenBonusString()
        {
            var strAndEnd = Strength.Base + 2 * Endurance.Base;

            if (strAndEnd > 690)        return "Indomitable";
            else if (strAndEnd > 580)   return "Resilient";
            else if (strAndEnd > 470)   return "Hardy";
            else if (strAndEnd > 346)   return "Mediocre";
            else if (strAndEnd > 200)   return "Poor";
            else
                return "None";
        }

        /// <summary>
        /// If a player has been involved in a PK battle this recently,
        /// logging off leaves their character in a frozen state for 20 seconds
        /// </summary>
        public static TimeSpan PKLogoffTimer = TimeSpan.FromMinutes(2);

        public void UpdatePKTimer()
        {
            //log.Info($"Updating PK timer for {Name}");

            LastPkAttackTimestamp = Time.GetUnixTime();
        }

        /// <summary>
        /// Called when a successful attack is landed in PVP
        /// The timestamp for both PKs are updated
        /// 
        /// If a physical attack is evaded, or a magic spell is resisted,
        /// this function should NOT be called.
        /// </summary>
        public static void UpdatePKTimers(Player attacker, Player defender)
        {
            if (attacker == defender) return;

            if (attacker.PlayerKillerStatus == PlayerKillerStatus.Free || defender.PlayerKillerStatus == PlayerKillerStatus.Free)
                return;

            attacker.UpdatePKTimer();
            defender.UpdatePKTimer();
        }

        public bool PKTimerActive => IsPKType && Time.GetUnixTime() - LastPkAttackTimestamp < PropertyManager.GetLong("pk_timer").Item;

        public bool PKLogoutActive => IsPKType && Time.GetUnixTime() - LastPkAttackTimestamp < PKLogoffTimer.TotalSeconds;

        public bool IsPKType => PlayerKillerStatus == PlayerKillerStatus.PK || PlayerKillerStatus == PlayerKillerStatus.PKLite;

        public bool IsPK => PlayerKillerStatus == PlayerKillerStatus.PK;

        public bool IsPKL => PlayerKillerStatus == PlayerKillerStatus.PKLite;

        public bool IsNPK => PlayerKillerStatus == PlayerKillerStatus.NPK;

        public bool CheckHouseRestrictions(Player player)
        {
            if (Location.Cell == player.Location.Cell)
                return true;

            // dealing with outdoor cell equivalents at this point, if applicable
            var cell = (CurrentLandblock?.IsDungeon ?? false) ? Location.Cell : Location.GetOutdoorCell();
            var playerCell = (player.CurrentLandblock?.IsDungeon ?? false) ? player.Location.Cell : player.Location.GetOutdoorCell();

            if (cell == playerCell)
                return true;

            HouseCell.HouseCells.TryGetValue(cell, out var houseGuid);
            HouseCell.HouseCells.TryGetValue(playerCell, out var playerHouseGuid);

            // pass if both of these players aren't in a house cell
            if (houseGuid == 0 && playerHouseGuid == 0)
                return true;

            var houses = new HashSet<House>();
            CheckHouseRestrictions_GetHouse(houseGuid, houses);
            player.CheckHouseRestrictions_GetHouse(playerHouseGuid, houses);

            foreach (var house in houses)
            {
                if (!house.HasPermission(this) || !house.HasPermission(player))
                    return false;
            }
            return true;
        }

        public void CheckHouseRestrictions_GetHouse(uint houseGuid, HashSet<House> houses)
        {
            if (houseGuid == 0)
                return;

            var house = CurrentLandblock.GetObject(houseGuid) as House;
            if (house != null)
            {
                var rootHouse = house.LinkedHouses.Count > 0 ? house.LinkedHouses[0] : house;

                if (rootHouse.HouseOwner == null || rootHouse.OpenStatus || houses.Contains(rootHouse))
                    return;

                //Console.WriteLine($"{Name}.CheckHouseRestrictions_GetHouse({houseGuid:X8}): found root house {house.Name} ({house.HouseId})");
                houses.Add(rootHouse);
            }
            else
                log.Error($"{Name}.CheckHouseRestrictions_GetHouse({houseGuid:X8}): couldn't find house from {CurrentLandblock.Id.Raw:X8}");
        }

        /// <summary>
        /// Returns the damage type for the currently equipped weapon / ammo
        /// </summary>
        /// <param name="multiple">If true, returns all of the damage types for the weapon</param>
        public override DamageType GetDamageType(bool multiple = false, CombatType? combatType = null)
        {
            // player override
            if (combatType == null)
                combatType = GetCombatType();

            var weapon = GetEquippedWeapon();
            var ammo = GetEquippedAmmo();

            if (weapon == null && combatType == CombatType.Melee)
            {
                // handle gauntlets/ boots
                if (AttackType == AttackType.Punch)
                    weapon = HandArmor;
                else if (AttackType == AttackType.Kick)
                    weapon = FootArmor;
                else
                {
                    log.Warn($"{Name}.GetDamageType(): no weapon, AttackType={AttackType}");
                    return DamageType.Undef;
                }

                // DerpACE: check for unarmed damage type property
                if (weapon != null && (weapon.UnarmedDamageType ?? 0) > 0)
                    return (DamageType)weapon.UnarmedDamageType.Value;

                if (weapon != null && weapon.W_DamageType == DamageType.Undef)
                    return DamageType.Bludgeon;
            }

            if (weapon == null)
                return DamageType.Bludgeon;

            var damageSource = combatType == CombatType.Melee || ammo == null || !weapon.IsAmmoLauncher ? weapon : ammo;

            var damageType = damageSource.W_DamageType;

            if (damageType == DamageType.Undef)
            {
                log.Warn($"{Name}.GetDamageType(): {damageSource} ({damageSource.Guid}, {damageSource.WeenieClassId}): no DamageType");
                return DamageType.Bludgeon;
            }

            // return multiple damage types
            if (multiple || !damageType.IsMultiDamage())
                return damageType;

            // get single damage type
            if (damageType == (DamageType.Pierce | DamageType.Slash))
            {
                if ((AttackType & AttackType.Punches) != 0)
                {
                    if (PowerLevel < ThrustThreshold)
                        return DamageType.Pierce;
                    else
                        return DamageType.Slash;
                }

                if ((AttackType & AttackType.Thrusts) != 0)
                    return DamageType.Pierce;
                else
                    return DamageType.Slash;
            }

            var powerLevel = combatType == CombatType.Melee ? (float?)PowerLevel : null;

            return damageType.SelectDamageType(powerLevel);
        }

        public WorldObject HandArmor => EquippedObjects.Values.FirstOrDefault(i => (i.ClothingPriority & CoverageMask.Hands) > 0);

        public WorldObject FootArmor => EquippedObjects.Values.FirstOrDefault(i => (i.ClothingPriority & CoverageMask.Feet) > 0);


        /// <summary>
        /// Determines if player can damage a target via PlayerKillerStatus
        /// </summary>
        /// <returns>null if no errors, else pk error list</returns>
        public override List<WeenieErrorWithString> CheckPKStatusVsTarget(WorldObject target, Spell spell)
        {
            if (target == null ||target == this)
                return null;

            var targetCreature = target as Creature;
            if (targetCreature == null && target.WielderId != null)
            {
                // handle casting item spells
                targetCreature = CurrentLandblock.GetObject(target.WielderId.Value) as Creature;
            }
            if (targetCreature == null)
                return null;

            if (PlayerKillerStatus == PlayerKillerStatus.Free || targetCreature.PlayerKillerStatus == PlayerKillerStatus.Free)
                return null;

            var targetPlayer = target as Player;

            if (targetPlayer != null)
            {
                if (spell == null || spell.IsHarmful)
                {
                    // Ensure that a non-PK cannot cast harmful spells on another player
                    if (PlayerKillerStatus == PlayerKillerStatus.NPK)
                        return new List<WeenieErrorWithString>() { WeenieErrorWithString.YouFailToAffect_YouAreNotPK, WeenieErrorWithString._FailsToAffectYou_TheyAreNotPK };

                    if (targetPlayer.PlayerKillerStatus == PlayerKillerStatus.NPK)
                        return new List<WeenieErrorWithString>() { WeenieErrorWithString.YouFailToAffect_TheyAreNotPK, WeenieErrorWithString._FailsToAffectYou_YouAreNotPK };

                    // Ensure not attacking across housing boundary
                    if (!CheckHouseRestrictions(targetPlayer))
                        return new List<WeenieErrorWithString>() { WeenieErrorWithString.YouFailToAffect_AcrossHouseBoundary, WeenieErrorWithString._FailsToAffectYouAcrossHouseBoundary };
                }

                // additional checks for different PKTypes
                if (PlayerKillerStatus != targetPlayer.PlayerKillerStatus)
                {
                    // require same pk status, unless beneficial spell being cast on NPK
                    // https://asheron.fandom.com/wiki/Player_Killer
                    // https://asheron.fandom.com/wiki/Player_Killer_Lite

                    if (spell == null || spell.IsHarmful || targetPlayer.PlayerKillerStatus != PlayerKillerStatus.NPK)
                        return new List<WeenieErrorWithString>() { WeenieErrorWithString.YouFailToAffect_NotSamePKType, WeenieErrorWithString._FailsToAffectYou_NotSamePKType };
                }
            }
            else
            {
                // if monster has a non-default pk status, ensure pk types match up
                if (targetCreature.PlayerKillerStatus != PlayerKillerStatus.NPK && PlayerKillerStatus != targetCreature.PlayerKillerStatus)
                {
                    return new List<WeenieErrorWithString>() { WeenieErrorWithString.YouFailToAffect_NotSamePKType, WeenieErrorWithString._FailsToAffectYou_NotSamePKType };
                }
            }
            return null;
        }
    }
}
