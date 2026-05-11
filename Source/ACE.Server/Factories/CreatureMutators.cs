using System;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE: Vampiric mob mutator (lifesteals on hit).
    /// Ported from MobModifierFactory.
    /// </summary>
    public class VampiricMutator : CreatureMutator
    {
        public override string Name => "Vampiric";
        public override string Description => "Lifesteals a percentage of damage dealt to players.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsVampiricMob;
        public override string NamePrefix => "Vampiric";

        public VampiricMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.VampiricMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Roll lifesteal % from configured range (matches legacy MobModifierFactory behavior)
            var minPct = Math.Max(0, DerpACEConfig.VampiricLifestealMin);
            var maxPct = Math.Max(minPct, DerpACEConfig.VampiricLifestealMax);
            var pct = ThreadSafeRandom.Next(minPct, maxPct) / 100.0;
            creature.SetProperty(PropertyFloat.VampiricLifestealPct, pct);

            // Boost HP slightly (use biota properties for persistent stat changes)
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.15);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Visual tells: scale and color
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.5f;
            creature.PaletteTemplate = (int)PaletteTemplate.Red;
            creature.Shade = 1.0;
        }
    }

    /// <summary>
    /// DerpACE: Thief mob mutator (steals tradenotes on hit, drops chest on death).
    /// Ported from MobModifierFactory.
    /// </summary>
    public class ThiefMutator : CreatureMutator
    {
        public override string Name => "Thieving";
        public override string Description => "Steals tradenotes from players and drops a loot chest on death.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsThiefMob;
        public override string NamePrefix => "Thieving";

        public ThiefMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ThiefMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Boost HP + speed
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.10);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Small speed boost
            var runSkill = creature.GetCreatureSkill(Skill.Run);
            if (runSkill != null && runSkill.Base > 0)
            {
                var runBoost = (uint)(runSkill.Base * 0.05);
                runSkill.Ranks += (ushort)runBoost;
            }
        }
    }

    /// <summary>
    /// DerpACE: Scout mob mutator (increases aggro range and perception).
    /// Ported from MobModifierFactory.
    /// </summary>
    public class ScoutMutator : CreatureMutator
    {
        public override string Name => "Scout";
        public override string Description => "Increases aggro range, perception, and announces player presence.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsScoutMob;
        public override string NamePrefix => "Scout";

        public ScoutMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ScoutMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Increase visual/aggro range
            if (creature.VisualAwarenessRange.HasValue)
                creature.VisualAwarenessRange *= 1.5f;
            else
                creature.VisualAwarenessRange = 30.0f;

            // Boost attributes via base values
            if (creature.Coordination != null)
            {
                creature.Coordination.StartingValue += 20;
            }
            if (creature.Quickness != null)
            {
                creature.Quickness.StartingValue += 20;
            }

            // Small speed boost
            var runSkill = creature.GetCreatureSkill(Skill.Run);
            if (runSkill != null && runSkill.Base > 0)
            {
                var runBoost = (uint)(runSkill.Base * 0.10);
                runSkill.Ranks += (ushort)runBoost;
            }

            // TODO: Add alert broadcast on player detection (requires AI hook)
        }
    }

    /// <summary>
    /// DerpACE: Simulacrum mob mutator (summons duplicate on low HP).
    /// Ported from MobModifierFactory.
    /// </summary>
    public class SimulacrumMutator : CreatureMutator
    {
        public override string Name => "Simulacrum";
        public override string Description => "Summons a weaker duplicate when HP drops below 50%.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsSimulacrumMob;
        public override string NamePrefix => "Simulacrum";

        public SimulacrumMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.SimulacrumMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Boost HP to compensate for summon mechanic
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.20);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // TODO: Hook TakeDamage or low-HP event to spawn duplicate
        }
    }

    /// <summary>
    /// DerpACE: Nocturnal mob mutator — boosts DamageRating + Overpower at spawn.
    /// In random spawn flow, only rolls at night; force-apply (admin) bypasses time-of-day.
    /// </summary>
    public class NocturnalMutator : CreatureMutator
    {
        public override string Name => "Nocturnal";
        public override string Description => "Hunts after dark — boosted damage and overpower.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsNocturnalMob;
        public override string NamePrefix => "Nocturnal";

        public NocturnalMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.NocturnalMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        public override bool CanApply(Creature creature, int tier)
        {
            if (!base.CanApply(creature, tier)) return false;

            // Random-spawn path: only at night. Force-apply uses ForceApply() and bypasses CanApply().
            // Use the same in-game day/night clock that spawners use (GeneratorTimeType.Night).
            return !ACE.Server.Entity.Timers.CurrentInGameTime.IsDay;
        }

        protected override void Apply(Creature creature, int tier)
        {
            creature.DamageRating = (creature.DamageRating ?? 0) + ThreadSafeRandom.Next(1, 50);
            creature.Overpower = (creature.Overpower ?? 0) + ThreadSafeRandom.Next(1, 5);
        }
    }

    /// <summary>
    /// DerpACE: Exploding mob mutator — detonates on death dealing AoE Fire damage to nearby players.
    /// Death-side AoE is handled in Creature_Death.cs by checking PropertyBool.IsExplodingMob.
    /// </summary>
    public class ExplodingMutator : CreatureMutator
    {
        public override string Name => "Exploding";
        public override string Description => "Explodes on death, dealing fire damage to nearby players.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsExplodingMob;
        public override string NamePrefix => "Exploding";

        public ExplodingMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ExplodingMobChance;
            Enabled = DerpACEConfig.MobModifierEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Visual tell: orange-red tint and a slight scale-up.
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.2f;
            creature.PaletteTemplate = (int)PaletteTemplate.Red;
        }
    }
}
