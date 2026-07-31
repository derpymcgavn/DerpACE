using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using log4net;

using ACE.Entity.Enum;
using ACE.Server.Factories.Entity;

namespace ACE.Server.Factories.Tables
{
    public static class LootSpellWeightManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static volatile IReadOnlyDictionary<string, ChanceTable<SpellId>> activeTables = new Dictionary<string, ChanceTable<SpellId>>(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyDictionary<string, List<LootSpellWeight>> savedWeights = new Dictionary<string, List<LootSpellWeight>>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool loaded;

        public static readonly string[] PoolNames = { "armor", "melee", "missile", "caster", "jewelry" };

        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "LootSpellWeights.json");

        public static SpellId Roll(string pool, ChanceTable<SpellId> defaults)
        {
            EnsureLoaded();
            return activeTables.TryGetValue(pool, out var table) ? table.Roll() : defaults.Roll();
        }

        public static object GetSnapshot(string pool, ChanceTable<SpellId> defaults)
        {
            EnsureLoaded();
            var source = savedWeights.TryGetValue(pool, out var saved)
                ? saved
                : defaults.Select(entry => new LootSpellWeight { SpellId = (uint)entry.result, Weight = entry.chance }).ToList();
            var total = source.Sum(entry => Math.Max(0.0, entry.Weight));

            return new
            {
                pool,
                customized = savedWeights.ContainsKey(pool),
                entries = source.Select(entry => new
                {
                    spellId = entry.SpellId,
                    name = ((SpellId)entry.SpellId).ToString(),
                    weight = entry.Weight,
                    chance = total > 0.0 ? Math.Max(0.0, entry.Weight) / total : 0.0
                }).OrderByDescending(entry => entry.weight).ThenBy(entry => entry.name).ToList()
            };
        }

        public static bool TryUpdate(string pool, IEnumerable<LootSpellWeight> entries, out string error)
        {
            error = null;
            if (!PoolNames.Contains(pool, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Unknown loot spell pool '{pool}'.";
                return false;
            }

            var weights = entries?.ToList() ?? new List<LootSpellWeight>();
            if (weights.Count == 0 || weights.Count > 250)
            {
                error = "A spell pool must contain between 1 and 250 entries.";
                return false;
            }
            if (weights.Any(entry => entry.Weight < 0.0 || double.IsNaN(entry.Weight) || double.IsInfinity(entry.Weight)))
            {
                error = "Spell weights must be finite values greater than or equal to zero.";
                return false;
            }
            if (weights.GroupBy(entry => entry.SpellId).Any(group => group.Count() > 1))
            {
                error = "A spell can only appear once in a pool.";
                return false;
            }
            foreach (var entry in weights)
            {
                var levels = SpellLevelProgression.GetSpellLevels((SpellId)entry.SpellId);
                if (levels == null || levels.Count != 4)
                {
                    error = $"Spell {entry.SpellId} is not the base of a four-level cantrip family.";
                    return false;
                }
            }
            if (weights.Sum(entry => entry.Weight) <= 0.0)
            {
                error = "At least one spell must have a weight greater than zero.";
                return false;
            }

            EnsureLoaded();
            lock (Sync)
            {
                var next = savedWeights.ToDictionary(entry => entry.Key, entry => entry.Value.Select(Clone).ToList(), StringComparer.OrdinalIgnoreCase);
                next[pool] = weights.Select(Clone).ToList();
                ApplyAndSave(next);
            }
            return true;
        }

        public static void Reset(string pool)
        {
            EnsureLoaded();
            lock (Sync)
            {
                var next = savedWeights.ToDictionary(entry => entry.Key, entry => entry.Value.Select(Clone).ToList(), StringComparer.OrdinalIgnoreCase);
                next.Remove(pool);
                ApplyAndSave(next);
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded)
                return;

            lock (Sync)
            {
                if (loaded)
                    return;

                var next = new Dictionary<string, List<LootSpellWeight>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var document = JsonSerializer.Deserialize<LootSpellWeightDocument>(File.ReadAllText(FilePath), JsonOptions);
                        foreach (var pool in document?.Pools ?? new Dictionary<string, List<LootSpellWeight>>())
                        {
                            if (PoolNames.Contains(pool.Key, StringComparer.OrdinalIgnoreCase) && pool.Value?.Sum(entry => entry.Weight) > 0.0)
                                next[pool.Key] = pool.Value.Select(Clone).ToList();
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[DerpACE] Failed to load loot spell weights from '{FilePath}': {ex.Message}");
                }

                ReplaceActiveTables(next);
                loaded = true;
            }
        }

        private static void ApplyAndSave(Dictionary<string, List<LootSpellWeight>> next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new LootSpellWeightDocument { Pools = next }, JsonOptions));
            ReplaceActiveTables(next);
        }

        private static void ReplaceActiveTables(Dictionary<string, List<LootSpellWeight>> next)
        {
            var tables = new Dictionary<string, ChanceTable<SpellId>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pool in next)
            {
                var total = pool.Value.Sum(entry => Math.Max(0.0, entry.Weight));
                if (total <= 0.0)
                    continue;

                var enabled = pool.Value.Where(entry => entry.Weight > 0.0).ToList();
                var table = new ChanceTable<SpellId>();
                var accumulated = 0.0f;
                for (var i = 0; i < enabled.Count; i++)
                {
                    var chance = i == enabled.Count - 1
                        ? 1.0f - accumulated
                        : (float)(enabled[i].Weight / total);
                    table.Add(((SpellId)enabled[i].SpellId, chance));
                    accumulated += chance;
                }
                tables[pool.Key] = table;
            }

            savedWeights = next;
            activeTables = tables;
        }

        private static LootSpellWeight Clone(LootSpellWeight entry) => new LootSpellWeight { SpellId = entry.SpellId, Weight = entry.Weight };

        private sealed class LootSpellWeightDocument
        {
            public Dictionary<string, List<LootSpellWeight>> Pools { get; set; } = new Dictionary<string, List<LootSpellWeight>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class LootSpellWeight
    {
        public uint SpellId { get; set; }
        public double Weight { get; set; }
    }
}