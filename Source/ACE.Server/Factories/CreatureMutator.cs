using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE: Base class for creature affixes/mutators (ported from aquafir's Expansion mod).
    /// Each mutator checks eligibility (tier, chance) and applies stat/behavior changes to spawned creatures.
    /// </summary>
    public abstract class CreatureMutator
    {
        /// <summary>
        /// Unique identifier for this mutator (used in config + commands).
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Human-readable description for admin help text.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Minimum DeathTreasure tier (or Level/10) required for this mutator to roll.
        /// </summary>
        public virtual int MinTier { get; set; } = 5;

        /// <summary>
        /// Per-spawn chance (0-1) that this mutator applies to an eligible creature.
        /// </summary>
        public virtual float Chance { get; set; } = 0.02f;

        /// <summary>
        /// If true, this mutator is active and will roll on eligible spawns.
        /// </summary>
        public virtual bool Enabled { get; set; } = true;

        /// <summary>
        /// PropertyBool flag set on creatures that have this mutator applied.
        /// Override if your mutator uses a custom property.
        /// </summary>
        public virtual PropertyBool? MutatorFlag { get; } = null;

        /// <summary>
        /// Name prefix prepended to the creature's display name (e.g., "Vampiric", "Thieving").
        /// </summary>
        public virtual string NamePrefix { get; } = "";

        /// <summary>
        /// Checks if the creature is eligible for this mutator (tier gate + NPC/Pet/Player exclusion).
        /// </summary>
        public virtual bool CanApply(Creature creature, int tier)
        {
            if (!Enabled) return false;
            if (creature == null) return false;
            if (creature is Player) return false;
            if (creature is Pet) return false;
            if (creature.IsNPC) return false;

            // Must be a real hostile mob
            var isMonster = creature.Attackable || creature.TargetingTactic != TargetingTactic.None;
            if (!isMonster) return false;

            if (tier < MinTier) return false;

            // Check if already applied (idempotency)
            if (MutatorFlag.HasValue && creature.GetProperty(MutatorFlag.Value) == true)
                return false;

            return true;
        }

        /// <summary>
        /// Rolls the configured chance. Returns true if the mutator should apply.
        /// </summary>
        public virtual bool RollChance()
        {
            if (Chance <= 0) return false;
            if (Chance >= 1.0f) return true;
            return ThreadSafeRandom.Next(0.0f, 1.0f) < Chance;
        }

        /// <summary>
        /// Attempts to apply this mutator to a creature. Returns true if applied.
        /// </summary>
        public bool TryApply(Creature creature, int tier)
        {
            if (!CanApply(creature, tier)) return false;
            if (!RollChance()) return false;

            Apply(creature, tier);

            // Set flag + name prefix
            if (MutatorFlag.HasValue)
                creature.SetProperty(MutatorFlag.Value, true);

            if (!string.IsNullOrEmpty(NamePrefix))
                PrependPrefix(creature, NamePrefix);

            return true;
        }

        /// <summary>
        /// Override this to apply your mutator's stat changes / behavior flags.
        /// </summary>
        protected abstract void Apply(Creature creature, int tier);

        /// <summary>
        /// Prepends a prefix to the creature's name, idempotently (checks for duplicates).
        /// </summary>
        protected void PrependPrefix(Creature creature, string prefix)
        {
            var name = creature.Name ?? string.Empty;
            var tokens = name.Split(' ');
            if (tokens.Length > 0 && tokens[0].Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return; // Already prefixed

            creature.Name = $"{prefix} {name}".Trim();
        }

        /// <summary>
        /// Called once at server startup to initialize mutator-specific resources.
        /// </summary>
        public virtual void Initialize() { }

        /// <summary>
        /// Called when the mutator is disabled or server shuts down.
        /// </summary>
        public virtual void Shutdown() { }
    }
}
