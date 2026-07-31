using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using log4net;

using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories.Tables
{
    public static class LootTierManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static volatile IReadOnlyDictionary<int, LootTierDefinition> definitions = new Dictionary<int, LootTierDefinition>();
        private static volatile bool loaded;

        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "LootTiers.json");

        public static LootTierContext Resolve(TreasureDeath source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            EnsureLoaded();
            if (source.Tier <= 8)
                return new LootTierContext(source, source.Tier, null);

            definitions.TryGetValue(source.Tier, out var definition);
            if (definition?.Enabled != true)
                definition = null;

            var baseTier = Math.Clamp(definition?.BaseTier ?? 8, 1, 8);
            var effective = Clone(source);
            effective.Tier = baseTier;
            effective.LootQualityMod = Math.Max(0.0f, source.LootQualityMod + (definition?.LootQualityBonus ?? 0.0f));

            return new LootTierContext(effective, source.Tier, definition);
        }

        public static IReadOnlyList<LootTierDefinition> GetDefinitions()
        {
            EnsureLoaded();
            return definitions.Values.OrderBy(entry => entry.Tier).Select(Clone).ToList();
        }

        public static int MaximumTier
        {
            get
            {
                EnsureLoaded();
                return Math.Max(8, definitions.Values.Where(entry => entry.Enabled).Select(entry => entry.Tier).DefaultIfEmpty(8).Max());
            }
        }

        public static bool IsSupportedTier(int tier)
        {
            if (tier >= 1 && tier <= 8)
                return true;

            EnsureLoaded();
            return definitions.TryGetValue(tier, out var definition) && definition.Enabled;
        }

        public static bool TrySave(IEnumerable<LootTierDefinition> requested, out string error)
        {
            error = null;
            var entries = requested?.Select(Clone).ToList() ?? new List<LootTierDefinition>();
            if (entries.Count > 92)
            {
                error = "No more than 92 custom loot tiers may be configured.";
                return false;
            }
            if (entries.GroupBy(entry => entry.Tier).Any(group => group.Count() > 1))
            {
                error = "Each custom tier number must be unique.";
                return false;
            }

            foreach (var entry in entries)
            {
                entry.Name = string.IsNullOrWhiteSpace(entry.Name) ? $"Tier {entry.Tier}" : entry.Name.Trim();
                entry.Description = entry.Description?.Trim() ?? "";
                if (entry.Tier < 9 || entry.Tier > 100)
                {
                    error = $"Custom tier {entry.Tier} must be between 9 and 100.";
                    return false;
                }
                if (entry.BaseTier < 1 || entry.BaseTier > 8)
                {
                    error = $"Tier {entry.Tier} must inherit from ACE tier 1-8.";
                    return false;
                }
                if (!InRange(entry.LootQualityBonus, 0, 1) ||
                    !InRange(entry.DropCountMultiplier, 0.1, 10) ||
                    !InRange(entry.ValueMultiplier, 0.1, 100) ||
                    entry.WorkmanshipBonus < 0 || entry.WorkmanshipBonus > 20 ||
                    entry.SpellcraftBonus < 0 || entry.SpellcraftBonus > 500 ||
                    !InRange(entry.ArmorMultiplier, 0.1, 10) ||
                    !InRange(entry.WeaponDamageMultiplier, 0.1, 10) ||
                    !InRange(entry.MaxManaMultiplier, 0.1, 10) ||
                    entry.WieldLevelRequirement < 0 || entry.WieldLevelRequirement > 10000)
                {
                    error = $"Tier {entry.Tier} contains a value outside its supported range.";
                    return false;
                }
            }

            lock (Sync)
            {
                var next = entries.ToDictionary(entry => entry.Tier, Clone);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new LootTierDocument { Tiers = entries }, JsonOptions));
                definitions = next;
                loaded = true;
            }

            return true;
        }

        public static int ScaleDropCount(int count, LootTierContext context)
        {
            var multiplier = context?.Definition?.DropCountMultiplier ?? 1.0;
            return Math.Max(0, (int)Math.Round(count * multiplier, MidpointRounding.AwayFromZero));
        }

        public static WorldObject Apply(WorldObject item, LootTierContext context)
        {
            var tier = context?.Definition;
            if (item == null || tier == null)
                return item;

            if (item.Value.HasValue)
                item.Value = ScaleInt(item.Value.Value, tier.ValueMultiplier, 0, int.MaxValue);
            if (item.ItemWorkmanship.HasValue)
                item.ItemWorkmanship = Math.Clamp(item.ItemWorkmanship.Value + tier.WorkmanshipBonus, 0, 100);
            if (item.ItemSpellcraft.HasValue)
                item.ItemSpellcraft = Math.Clamp(item.ItemSpellcraft.Value + tier.SpellcraftBonus, 0, 9999);
            if (item.ArmorLevel.HasValue)
                item.ArmorLevel = ScaleInt(item.ArmorLevel.Value, tier.ArmorMultiplier, 0, int.MaxValue);
            if (item.Damage.HasValue)
                item.Damage = ScaleInt(item.Damage.Value, tier.WeaponDamageMultiplier, 0, int.MaxValue);
            if (item.DamageMod.HasValue)
                item.DamageMod = item.DamageMod.Value * tier.WeaponDamageMultiplier;
            if (item.ElementalDamageBonus.HasValue)
                item.ElementalDamageBonus = ScaleInt(item.ElementalDamageBonus.Value, tier.WeaponDamageMultiplier, 0, int.MaxValue);
            if (item.ItemMaxMana.HasValue)
            {
                item.ItemMaxMana = ScaleInt(item.ItemMaxMana.Value, tier.MaxManaMultiplier, 0, int.MaxValue);
                item.ItemCurMana = item.ItemMaxMana;
            }
            if (tier.WieldLevelRequirement > 0)
            {
                item.WieldRequirements = WieldRequirement.Level;
                item.WieldDifficulty = Math.Max(item.WieldDifficulty ?? 0, tier.WieldLevelRequirement);
                item.WieldSkillType = 1;
            }

            var label = string.IsNullOrWhiteSpace(tier.Name) ? $"Tier {tier.Tier}" : tier.Name;
            var note = $"\n\nEndgame Loot: {label} (T{tier.Tier}, inherited from T{tier.BaseTier}).";
            if (!string.IsNullOrWhiteSpace(tier.Description))
                note += $" {tier.Description}";
            item.LongDesc = (item.LongDesc ?? "") + note;
            return item;
        }

        private static int ScaleInt(int value, double multiplier, int min, int max)
        {
            var scaled = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return (int)Math.Clamp(scaled, min, max);
        }

        private static bool InRange(double value, double min, double max)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;

        private static void EnsureLoaded()
        {
            if (loaded)
                return;

            lock (Sync)
            {
                if (loaded)
                    return;

                var next = new Dictionary<int, LootTierDefinition>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var document = JsonSerializer.Deserialize<LootTierDocument>(File.ReadAllText(FilePath), JsonOptions);
                        foreach (var entry in document?.Tiers ?? new List<LootTierDefinition>())
                        {
                            if (entry.Tier >= 9 && entry.Tier <= 100 && entry.BaseTier >= 1 && entry.BaseTier <= 8)
                                next[entry.Tier] = Clone(entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[DerpACE] Failed to load custom loot tiers from '{FilePath}': {ex.Message}");
                }

                definitions = next;
                loaded = true;
            }
        }

        private static TreasureDeath Clone(TreasureDeath source)
        {
            return new TreasureDeath
            {
                Id = source.Id,
                TreasureType = source.TreasureType,
                Tier = source.Tier,
                LootQualityMod = source.LootQualityMod,
                UnknownChances = source.UnknownChances,
                ItemChance = source.ItemChance,
                ItemMinAmount = source.ItemMinAmount,
                ItemMaxAmount = source.ItemMaxAmount,
                ItemTreasureTypeSelectionChances = source.ItemTreasureTypeSelectionChances,
                MagicItemChance = source.MagicItemChance,
                MagicItemMinAmount = source.MagicItemMinAmount,
                MagicItemMaxAmount = source.MagicItemMaxAmount,
                MagicItemTreasureTypeSelectionChances = source.MagicItemTreasureTypeSelectionChances,
                MundaneItemChance = source.MundaneItemChance,
                MundaneItemMinAmount = source.MundaneItemMinAmount,
                MundaneItemMaxAmount = source.MundaneItemMaxAmount,
                MundaneItemTypeSelectionChances = source.MundaneItemTypeSelectionChances,
                LastModified = source.LastModified
            };
        }

        private static LootTierDefinition Clone(LootTierDefinition source)
        {
            return new LootTierDefinition
            {
                Tier = source.Tier,
                Name = source.Name,
                Enabled = source.Enabled,
                BaseTier = source.BaseTier,
                LootQualityBonus = source.LootQualityBonus,
                DropCountMultiplier = source.DropCountMultiplier,
                ValueMultiplier = source.ValueMultiplier,
                WorkmanshipBonus = source.WorkmanshipBonus,
                SpellcraftBonus = source.SpellcraftBonus,
                ArmorMultiplier = source.ArmorMultiplier,
                WeaponDamageMultiplier = source.WeaponDamageMultiplier,
                MaxManaMultiplier = source.MaxManaMultiplier,
                WieldLevelRequirement = source.WieldLevelRequirement,
                Description = source.Description
            };
        }

        private sealed class LootTierDocument
        {
            public List<LootTierDefinition> Tiers { get; set; } = new List<LootTierDefinition>();
        }
    }

    public sealed class LootTierContext
    {
        public LootTierContext(TreasureDeath profile, int requestedTier, LootTierDefinition definition)
        {
            Profile = profile;
            RequestedTier = requestedTier;
            Definition = definition;
        }

        public TreasureDeath Profile { get; }
        public int RequestedTier { get; }
        public LootTierDefinition Definition { get; }
    }

    public sealed class LootTierDefinition
    {
        public int Tier { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int BaseTier { get; set; } = 8;
        public float LootQualityBonus { get; set; }
        public double DropCountMultiplier { get; set; } = 1.0;
        public double ValueMultiplier { get; set; } = 1.0;
        public int WorkmanshipBonus { get; set; }
        public int SpellcraftBonus { get; set; }
        public double ArmorMultiplier { get; set; } = 1.0;
        public double WeaponDamageMultiplier { get; set; } = 1.0;
        public double MaxManaMultiplier { get; set; } = 1.0;
        public int WieldLevelRequirement { get; set; }
        public string Description { get; set; } = "";
    }
}