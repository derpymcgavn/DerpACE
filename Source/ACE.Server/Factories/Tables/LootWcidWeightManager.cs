using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using log4net;

using ACE.Common;
using ACE.Server.Factories.Enum;

namespace ACE.Server.Factories.Tables
{
    public static class LootWcidWeightManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static volatile IReadOnlyDictionary<string, LootWcidPool> pools = new Dictionary<string, LootWcidPool>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool loaded;

        public static readonly string[] PoolNames = { "melee", "missile", "caster", "armor", "clothing", "jewelry", "generic", "scroll", "mana_stone", "consumable", "heal_kit", "lockpick", "spell_component", "society_armor", "cloak", "pet", "aetheria", "coalesced_mana" };
        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "LootWcidWeights.json");

        public static ACE.Server.Factories.Enum.WeenieClassName Roll(string pool, int tier, ACE.Server.Factories.Enum.WeenieClassName fallback)
            => Roll(pool, tier, fallback, out _);

        public static ACE.Server.Factories.Enum.WeenieClassName Roll(string pool, int tier, ACE.Server.Factories.Enum.WeenieClassName fallback, out TreasureWeaponType? mutationType)
        {
            mutationType = null;
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(pool) || !pools.TryGetValue(pool, out var definition))
                return fallback;

            var total = Math.Max(0.0, definition.BuiltInWeight);
            foreach (var entry in definition.Entries)
                if (entry.Enabled && tier >= entry.MinTier && tier <= entry.MaxTier && entry.Weight > 0.0)
                    total += entry.Weight;

            if (total <= 0.0)
                return fallback;

            var roll = ThreadSafeRandom.Next(0.0f, (float)total);
            if (roll < definition.BuiltInWeight)
                return fallback;
            roll -= (float)Math.Max(0.0, definition.BuiltInWeight);

            foreach (var entry in definition.Entries)
            {
                if (!entry.Enabled || tier < entry.MinTier || tier > entry.MaxTier || entry.Weight <= 0.0)
                    continue;
                if (roll < entry.Weight)
                {
                    if (TryParseMutationType(pool, entry.MutationType, out var parsed))
                        mutationType = parsed;
                    return (ACE.Server.Factories.Enum.WeenieClassName)entry.Wcid;
                }
                roll -= (float)entry.Weight;
            }
            return fallback;
        }

        public static string ResolvePool(TreasureItemType itemType, TreasureWeaponType weaponType = TreasureWeaponType.Undef)
        {
            if (itemType == TreasureItemType.Weapon)
                return weaponType.IsMissileWeapon() ? "missile" : weaponType.IsCaster() ? "caster" : "melee";
            return itemType switch
            {
                TreasureItemType.Jewelry => "jewelry",
                TreasureItemType.ArtObject => "generic",
                TreasureItemType.Armor => "armor",
                TreasureItemType.Clothing => "clothing",
                TreasureItemType.Scroll => "scroll",
                TreasureItemType.Caster => "caster",
                TreasureItemType.ManaStone => "mana_stone",
                TreasureItemType.Consumable => "consumable",
                TreasureItemType.HealKit => "heal_kit",
                TreasureItemType.Lockpick => "lockpick",
                TreasureItemType.SpellComponent => "spell_component",
                TreasureItemType.SocietyArmor or TreasureItemType.SocietyBreastplate or TreasureItemType.SocietyGauntlets or TreasureItemType.SocietyGirth or TreasureItemType.SocietyGreaves or TreasureItemType.SocietyHelm or TreasureItemType.SocietyPauldrons or TreasureItemType.SocietyTassets or TreasureItemType.SocietyVambraces or TreasureItemType.SocietySollerets => "society_armor",
                TreasureItemType.Cloak => "cloak",
                TreasureItemType.PetDevice => "pet",
                _ => null
            };
        }

        public static LootWcidPool GetSnapshot(string pool)
        {
            EnsureLoaded();
            return pools.TryGetValue(pool ?? "", out var definition)
                ? Clone(definition)
                : new LootWcidPool { Pool = pool ?? "", BuiltInWeight = 100.0 };
        }

        public static bool TryUpdate(string pool, double builtInWeight, IEnumerable<LootWcidWeight> requested, out string error)
        {
            error = null;
            pool = pool?.Trim().ToLowerInvariant() ?? "";
            if (!PoolNames.Contains(pool, StringComparer.OrdinalIgnoreCase)) { error = $"Unknown WCID pool '{pool}'."; return false; }
            if (!ValidWeight(builtInWeight)) { error = "Built-in table weight must be between zero and 1,000,000,000."; return false; }
            var entries = requested?.Select(Clone).ToList() ?? new List<LootWcidWeight>();
            if (entries.Count > 500) { error = "A WCID pool may contain at most 500 custom entries."; return false; }
            if (entries.GroupBy(entry => entry.Wcid).Any(group => group.Count() > 1)) { error = "A WCID can only appear once in a pool."; return false; }
            foreach (var entry in entries)
            {
                if (entry.Wcid == 0) { error = "Every custom loot entry needs a nonzero WCID."; return false; }
                if (entry.MinTier < 1 || entry.MaxTier > 100 || entry.MinTier > entry.MaxTier) { error = $"WCID {entry.Wcid} has an invalid tier range."; return false; }
                if (!ValidWeight(entry.Weight)) { error = $"WCID {entry.Wcid} weight must be between zero and 1,000,000,000."; return false; }
                entry.MutationType = entry.MutationType?.Trim() ?? "";
                if (!ValidateMutationType(pool, entry.MutationType, out var mutationError)) { error = $"WCID {entry.Wcid}: {mutationError}"; return false; }
            }
            if (builtInWeight <= 0.0 && entries.All(entry => !entry.Enabled || entry.Weight <= 0.0)) { error = "The pool must give weight to the built-in table or at least one enabled WCID."; return false; }

            EnsureLoaded();
            lock (Sync)
            {
                var next = pools.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.OrdinalIgnoreCase);
                next[pool] = new LootWcidPool { Pool = pool, BuiltInWeight = builtInWeight, Entries = entries };
                Save(next);
            }
            return true;
        }

        public static void Reset(string pool)
        {
            EnsureLoaded();
            lock (Sync)
            {
                var next = pools.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.OrdinalIgnoreCase);
                next.Remove(pool);
                Save(next);
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            lock (Sync)
            {
                if (loaded) return;
                var next = new Dictionary<string, LootWcidPool>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var document = JsonSerializer.Deserialize<LootWcidDocument>(File.ReadAllText(FilePath), JsonOptions);
                        foreach (var pair in document?.Pools ?? new Dictionary<string, LootWcidPool>())
                            if (PoolNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                                next[pair.Key] = Clone(pair.Value);
                    }
                }
                catch (Exception ex) { log.Error($"[DerpACE] Failed to load loot WCID weights from '{FilePath}': {ex.Message}"); }
                pools = next;
                loaded = true;
            }
        }

        private static void Save(Dictionary<string, LootWcidPool> next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new LootWcidDocument { Pools = next }, JsonOptions));
            pools = next;
            loaded = true;
        }

        private static bool ValidWeight(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0 && value <= 1_000_000_000.0;

        private static bool ValidateMutationType(string pool, string value, out string error)
        {
            error = null;
            if (pool is not ("melee" or "missile" or "caster"))
                return string.IsNullOrEmpty(value) || SetError("mutation family only applies to weapon pools.", out error);
            if (string.IsNullOrEmpty(value))
                return true;
            if (!System.Enum.TryParse<TreasureWeaponType>(value, true, out var parsed) || parsed == TreasureWeaponType.Undef)
                return SetError($"unknown weapon mutation family '{value}'.", out error);
            if (pool == "melee" && !parsed.IsMeleeWeapon())
                return SetError($"'{value}' is not a melee mutation family.", out error);
            if (pool == "missile" && !parsed.IsMissileWeapon())
                return SetError($"'{value}' is not a missile mutation family.", out error);
            if (pool == "caster" && !parsed.IsCaster())
                return SetError($"'{value}' is not the caster mutation family.", out error);
            return true;
        }

        private static bool TryParseMutationType(string pool, string value, out TreasureWeaponType mutationType)
        {
            if (!System.Enum.TryParse(value, true, out mutationType) || mutationType == TreasureWeaponType.Undef)
                return false;
            return pool == "melee" ? mutationType.IsMeleeWeapon()
                : pool == "missile" ? mutationType.IsMissileWeapon()
                : pool == "caster" && mutationType.IsCaster();
        }

        private static bool SetError(string message, out string error) { error = message; return false; }
        private static LootWcidWeight Clone(LootWcidWeight entry) => new LootWcidWeight { Wcid = entry.Wcid, Weight = entry.Weight, MinTier = entry.MinTier, MaxTier = entry.MaxTier, Enabled = entry.Enabled, MutationType = entry.MutationType ?? "" };
        private static LootWcidPool Clone(LootWcidPool pool) => new LootWcidPool { Pool = pool.Pool, BuiltInWeight = pool.BuiltInWeight, Entries = pool.Entries?.Select(Clone).ToList() ?? new List<LootWcidWeight>() };
        private sealed class LootWcidDocument { public Dictionary<string, LootWcidPool> Pools { get; set; } = new Dictionary<string, LootWcidPool>(StringComparer.OrdinalIgnoreCase); }
    }

    public sealed class LootWcidPool
    {
        public string Pool { get; set; } = "";
        public double BuiltInWeight { get; set; } = 100.0;
        public List<LootWcidWeight> Entries { get; set; } = new List<LootWcidWeight>();
    }

    public sealed class LootWcidWeight
    {
        public uint Wcid { get; set; }
        public double Weight { get; set; } = 1.0;
        public int MinTier { get; set; } = 1;
        public int MaxTier { get; set; } = 100;
        public bool Enabled { get; set; } = true;
        public string MutationType { get; set; } = "";
    }
}
