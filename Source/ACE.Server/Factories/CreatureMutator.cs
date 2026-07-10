using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
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
        public abstract string Identifier { get; }

        /// <summary>
        /// Display name for the mutator.
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
        /// This is the BASE chance at MinTier; scales with tier if UseTierScaling is true.
        /// </summary>
        public virtual float Chance { get; set; } = 0.02f;

        /// <summary>
        /// If true, mutator chance scales from Chance at MinTier to MaxChance at MaxTier.
        /// Default true to match new DerpACE tier-scaling behavior.
        /// </summary>
        public virtual bool UseTierScaling { get; set; } = true;

        /// <summary>
        /// Maximum chance at MaxTier when UseTierScaling is true. Default 0.0015 (0.15%).
        /// </summary>
        public virtual float MaxChance { get; set; } = 0.0015f;

        /// <summary>
        /// Maximum tier for scaling. Default 8.
        /// </summary>
        public virtual int MaxTier { get; set; } = 8;

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
        /// Rolls the configured chance for the given tier. Returns true if the mutator should apply.
        /// If UseTierScaling is true, scales from Chance at MinTier to MaxChance at MaxTier.
        /// </summary>
        public virtual bool RollChance(int tier)
        {
            float effectiveChance = Chance;

            if (UseTierScaling && tier >= MinTier)
            {
                if (tier >= MaxTier)
                    effectiveChance = Math.Max(Chance, MaxChance);
                else
                {
                    // Linear interpolation from Chance at MinTier to MaxChance at MaxTier
                    float tierRange = MaxTier - MinTier;
                    float tierProgress = (tier - MinTier) / tierRange;
                    var maxChance = Math.Max(Chance, MaxChance);
                    effectiveChance = Chance + (maxChance - Chance) * tierProgress;
                }
            }

            if (effectiveChance <= 0) return false;
            if (effectiveChance >= 1.0f) return true;
            return ThreadSafeRandom.Next(0.0f, 1.0f) < effectiveChance;
        }

        /// <summary>
        /// Attempts to apply this mutator to a creature. Returns true if applied.
        /// </summary>
        public bool TryApply(Creature creature, int tier)
        {
            if (!CanApply(creature, tier)) return false;
            if (!RollChance(tier)) return false;

            ApplyInternal(creature, tier);
            return true;
        }

        /// <summary>
        /// Force-applies this mutator to a creature regardless of MinTier / Chance / Enabled
        /// (NPC, Pet, Player, and idempotency checks still apply). Used by admin summon commands.
        /// Returns true if applied.
        /// </summary>
        public bool ForceApply(Creature creature)
        {
            if (creature == null) return false;
            if (creature is Player) return false;
            if (creature is Pet) return false;
            if (creature.IsNPC) return false;

            if (MutatorFlag.HasValue && creature.GetProperty(MutatorFlag.Value) == true)
                return false;

            // Use the mutator's effective tier when forcing, falling back to MinTier
            int tier = MinTier;
            if (creature.DeathTreasure != null)
                tier = Math.Max(tier, creature.DeathTreasure.Tier);
            else if (creature.Level.HasValue)
                tier = Math.Max(tier, (int)Math.Ceiling(creature.Level.Value / 10.0));

            ApplyInternal(creature, tier);
            return true;
        }

        private void ApplyInternal(Creature creature, int tier)
        {
            Apply(creature, tier);

            // Set flag + name prefix
            if (MutatorFlag.HasValue)
                creature.SetProperty(MutatorFlag.Value, true);

            if (!string.IsNullOrEmpty(NamePrefix))
                PrependPrefix(creature, NamePrefix);

            // Increment mutator count for derpcoin drop tracking
            var currentCount = creature.GetProperty(PropertyInt.MutatorCount) ?? 0;
            creature.SetProperty(PropertyInt.MutatorCount, currentCount + 1);

            // DerpACE: per-modifier stat scaling.
            //   * +50% to every attribute and vital StartingValue (multiplicative, 1.5x)
            //   * +0.1 to ObjScale
            //   * +50% to XpOverride
            // Stacks per mutator so a creature with N mutators ends up at 1.5^N stats
            // and +0.1*N scale.
            ApplyDerpAceStatBoost(creature);
        }

        private static void ApplyDerpAceStatBoost(Creature creature)
        {
            if (creature == null) return;

            const float statMult = 1.5f;
            const float scaleAdd = 0.1f;

            // Attributes
            foreach (var attr in creature.Attributes.Values)
            {
                if (attr == null) continue;
                var boosted = (uint)Math.Min(uint.MaxValue, Math.Round(attr.StartingValue * statMult));
                attr.StartingValue = boosted;
            }

            // Vitals (health/stamina/mana). Boost StartingValue then refill.
            BoostVital(creature.Health, statMult);
            BoostVital(creature.Stamina, statMult);
            BoostVital(creature.Mana, statMult);

            ApplyMutatorDefenseSkillCap(creature);

            // Visual scale
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + scaleAdd;

            // XP reward
            var xp = creature.XpOverride ?? 0;
            if (xp > 0)
            {
                long scaled = (long)Math.Round(xp * statMult);
                if (scaled > int.MaxValue) scaled = int.MaxValue;
                creature.XpOverride = (int)scaled;
            }
        }

        private static void BoostVital(ACE.Server.WorldObjects.Entity.CreatureVital vital, float mult)
        {
            if (vital == null) return;
            var boosted = (uint)Math.Min(uint.MaxValue, Math.Round(vital.StartingValue * mult));
            vital.StartingValue = boosted;
            vital.Current = vital.MaxValue;
        }
        private static void ApplyMutatorDefenseSkillCap(Creature creature)
        {
            var cap = DerpACEConfig.MobModifierDefenseSkillCap;
            if (cap <= 0)
                return;

            CapDefenseSkill(creature, Skill.MeleeDefense, (uint)cap);
            CapDefenseSkill(creature, Skill.MissileDefense, (uint)cap);
            CapDefenseSkill(creature, Skill.MagicDefense, (uint)cap);
        }

        private static void CapDefenseSkill(Creature creature, Skill skillType, uint cap)
        {
            var skill = creature.GetCreatureSkill(skillType, false);
            if (skill == null || skill.Current <= cap)
                return;

            var excess = skill.Current - cap;
            var rankReduction = (ushort)Math.Min(skill.Ranks, excess);
            skill.Ranks -= rankReduction;
            excess -= rankReduction;

            if (excess > 0)
                skill.InitLevel = skill.InitLevel > excess ? skill.InitLevel - excess : 0;
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
