using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Server.WorldObjects;
using log4net;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE: Central registry and controller for creature mutators.
    /// Manages lifecycle, spawn-time application, and runtime configuration.
    /// </summary>
    public static class CreatureMutatorManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Dictionary<string, CreatureMutator> _mutators = new Dictionary<string, CreatureMutator>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        /// <summary>
        /// All registered mutators.
        /// </summary>
        public static IReadOnlyDictionary<string, CreatureMutator> Mutators => _mutators;

        /// <summary>
        /// Initializes the mutator registry and all registered mutators.
        /// Called once at server startup.
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return;

                log.Info("Initializing CreatureMutatorManager...");

                // Register DerpACE mob modifiers
                RegisterMutator(new VampiricMutator());
                RegisterMutator(new ThiefMutator());
                RegisterMutator(new ScoutMutator());
                RegisterMutator(new SimulacrumMutator());
                RegisterMutator(new NocturnalMutator());
                RegisterMutator(new ExplodingMutator());
                RegisterMutator(new HealerMutator());
                RegisterMutator(new TankMutator());
                RegisterMutator(new ReaperMutator());
                RegisterMutator(new NecromancerMutator());
                RegisterMutator(new MergerMutator());
                RegisterMutator(new HordeMutator());
                RegisterMutator(new WarderMutator());
                RegisterMutator(new IllusionistMutator());

                // TODO: Port Expansion creature types
                // RegisterMutator(new DrainerMutator());
                // ... etc.

                foreach (var mutator in _mutators.Values)
                {
                    try
                    {
                        mutator.Initialize();
                        log.Info($"  Registered mutator: {mutator.Name} (Enabled={mutator.Enabled}, MinTier={mutator.MinTier}, Chance={mutator.Chance:P1})");
                    }
                    catch (Exception ex)
                    {
                        log.Error($"  Failed to initialize mutator {mutator.Name}: {ex.Message}");
                    }
                }

                _initialized = true;
                log.Info($"CreatureMutatorManager initialized with {_mutators.Count} mutators.");
            }
        }

        /// <summary>
        /// Shuts down all mutators. Called at server shutdown.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;

                log.Info("Shutting down CreatureMutatorManager...");
                foreach (var mutator in _mutators.Values)
                {
                    try
                    {
                        mutator.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        log.Error($"  Error shutting down mutator {mutator.Name}: {ex.Message}");
                    }
                }

                _mutators.Clear();
                _initialized = false;
                log.Info("CreatureMutatorManager shut down.");
            }
        }

        /// <summary>
        /// Registers a mutator into the manager.
        /// </summary>
        private static void RegisterMutator(CreatureMutator mutator)
        {
            if (mutator == null)
                throw new ArgumentNullException(nameof(mutator));

            if (_mutators.ContainsKey(mutator.Name))
                throw new InvalidOperationException($"Mutator '{mutator.Name}' is already registered.");

            _mutators[mutator.Name] = mutator;
        }

        /// <summary>
        /// Attempts to apply all eligible mutators to a spawned creature.
        /// Called from GeneratorProfile.Spawn after creation.
        /// </summary>
        public static void TryApplyMutators(Creature creature)
        {
            if (creature == null) return;

            // Compute tier from DeathTreasure when available, otherwise approximate from level
            int tier = 1;
            if (creature.DeathTreasure != null)
                tier = creature.DeathTreasure.Tier;
            else if (creature.Level.HasValue)
                tier = (int)Math.Ceiling(creature.Level.Value / 10.0);

            lock (_lock)
            {
                foreach (var mutator in _mutators.Values.OrderBy(m => ThreadSafeRandom.Next(0, 999999)))
                {
                    if (!mutator.Enabled) continue;

                    try
                    {
                        if (mutator.TryApply(creature, tier))
                        {
                            // Only one mutator per creature for now (matches old behavior)
                            // Future: allow stacking mutators with a priority system
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Error applying mutator {mutator.Name} to {creature.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Finds a mutator by name (case-insensitive).
        /// </summary>
        public static CreatureMutator GetMutator(string name)
        {
            lock (_lock)
            {
                _mutators.TryGetValue(name, out var mutator);
                return mutator;
            }
        }

        /// <summary>
        /// Force-applies a named mutator to a creature regardless of tier/chance/enabled.
        /// Used by admin summon commands. Returns true if applied.
        /// </summary>
        public static bool TryForceApplyMutator(Creature creature, string name)
        {
            if (creature == null || string.IsNullOrWhiteSpace(name)) return false;

            var mutator = GetMutator(ResolveAlias(name));
            if (mutator == null) return false;

            try
            {
                return mutator.ForceApply(creature);
            }
            catch (Exception ex)
            {
                log.Error($"Error force-applying mutator {mutator.Name} to {creature.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Maps short / legacy aliases to canonical mutator names.
        /// </summary>
        public static string ResolveAlias(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            switch (name.Trim().ToLowerInvariant())
            {
                case "vamp":
                case "vampire":
                case "vampiric":
                    return "Vampiric";
                case "thief":
                case "thieving":
                    return "Thieving";
                case "scout":
                case "scouting":
                    return "Scout";
                case "sim":
                case "simulacrum":
                    return "Simulacrum";
                case "noc":
                case "nocturnal":
                    return "Nocturnal";
                case "boom":
                case "explode":
                case "exploding":
                    return "Exploding";
                case "heal":
                case "healer":
                case "medic":
                    return "Healer";
                case "tank":
                case "guardian":
                case "defender":
                    return "Tank";
                case "reap":
                case "reaper":
                    return "Reaper";
                case "necro":
                case "necromancer":
                    return "Necromancer";
                case "merge":
                case "merger":
                case "assimilating":
                    return "Merger";
                case "horde":
                case "swarm":
                case "swarming":
                    return "Horde";
                case "ward":
                case "warder":
                case "warding":
                    return "Warder";
                case "illusion":
                case "illusionist":
                case "illusory":
                    return "Illusionist";
                default:
                    return name;
            }
        }

        /// <summary>
        /// Enables or disables a mutator by name.
        /// </summary>
        public static bool SetMutatorEnabled(string name, bool enabled)
        {
            var mutator = GetMutator(name);
            if (mutator == null) return false;

            mutator.Enabled = enabled;
            return true;
        }

        /// <summary>
        /// Sets the minimum tier requirement for a mutator.
        /// </summary>
        public static bool SetMutatorMinTier(string name, int tier)
        {
            var mutator = GetMutator(name);
            if (mutator == null) return false;

            mutator.MinTier = tier;
            return true;
        }

        /// <summary>
        /// Sets the spawn chance for a mutator.
        /// </summary>
        public static bool SetMutatorChance(string name, float chance)
        {
            var mutator = GetMutator(name);
            if (mutator == null) return false;

            mutator.Chance = Math.Clamp(chance, 0f, 1f);
            return true;
        }

        /// <summary>
        /// Returns a summary of all registered mutators for admin display.
        /// </summary>
        public static string GetMutatorSummary()
        {
            lock (_lock)
            {
                if (_mutators.Count == 0)
                    return "No mutators registered.";

                var lines = new List<string> { $"Registered Mutators ({_mutators.Count}):" };
                foreach (var mutator in _mutators.Values.OrderBy(m => m.Name))
                {
                    var status = mutator.Enabled ? "ENABLED" : "DISABLED";
                    lines.Add($"  [{status}] {mutator.Name}: {mutator.Description}");
                    lines.Add($"       MinTier={mutator.MinTier}, Chance={mutator.Chance:P1}");
                }

                return string.Join("\n", lines);
            }
        }
    }
}
