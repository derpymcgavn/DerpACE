using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using log4net;

using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Persistent, bounded NPC/player relationships. Personas are deterministic from WCID;
    /// only compact relationship memories are persisted.
    /// </summary>
    public static class NpcPersonaManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(NpcPersonaManager));
        private static readonly ConcurrentDictionary<string, NpcRelationship> Relationships = new ConcurrentDictionary<string, NpcRelationship>();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RelationshipRetention = TimeSpan.FromDays(180);
        private static DateTime nextSaveUtc = DateTime.MaxValue;
        private static int saveInProgress;
        private static bool dirty;

        private static string StateDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "NpcPersonas");
        private static string StatePath => Path.Combine(StateDirectory, "relationships.json");

        public static void Initialize()
        {
            if (!PropertyManager.GetBool("npc_personas_enabled").Item)
                return;

            try
            {
                if (!File.Exists(StatePath))
                    return;

                var state = JsonSerializer.Deserialize<NpcPersonaState>(File.ReadAllText(StatePath), JsonOptions);
                foreach (var relationship in state?.Relationships ?? new List<NpcRelationship>())
                {
                    if (relationship.LastInteractionUtc >= DateTime.UtcNow - RelationshipRetention)
                        Relationships[BuildKey(relationship.PlayerGuid, relationship.NpcWcid)] = relationship;
                }
                log.Info($"NpcPersonaManager: loaded {Relationships.Count:N0} NPC relationships.");
            }
            catch (Exception ex)
            {
                log.Error("NpcPersonaManager: failed to load persona state.", ex);
            }
        }

        public static void Tick()
        {
            if (!dirty || DateTime.UtcNow < nextSaveUtc || Interlocked.CompareExchange(ref saveInProgress, 1, 0) != 0)
                return;

            dirty = false;
            nextSaveUtc = DateTime.MaxValue;
            Task.Run(() =>
            {
                try
                {
                    var cutoff = DateTime.UtcNow - RelationshipRetention;
                    foreach (var entry in Relationships)
                        if (entry.Value.LastInteractionUtc < cutoff)
                            Relationships.TryRemove(entry.Key, out _);

                    var snapshot = new NpcPersonaState
                    {
                        Relationships = Relationships.Values.Select(CloneRelationship).ToList(),
                    };

                    Directory.CreateDirectory(StateDirectory);
                    var tempPath = StatePath + ".tmp";
                    File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, JsonOptions));
                    File.Move(tempPath, StatePath, true);
                }
                catch (Exception ex)
                {
                    dirty = true;
                    nextSaveUtc = DateTime.UtcNow + SaveDebounce;
                    log.Error("NpcPersonaManager: failed to save persona state.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref saveInProgress, 0);
                }
            });
        }

        public static NpcPersona GetPersona(Creature npc)
        {
            var seed = unchecked((int)(npc.WeenieClassId * 1103515245u + 12345u));
            return new NpcPersona
            {
                Warmth = Trait(seed, 0),
                Caution = Trait(seed, 8),
                Greed = Trait(seed, 16),
                Curiosity = Trait(seed, 24),
                Generosity = Trait(seed ^ 0x5A17, 12),
            };
        }

        public static void OnVisit(Player player, Creature npc)
        {
            if (!CanRemember(player, npc))
                return;

            var relationship = GetOrCreate(player, npc);
            string milestone = null;
            lock (relationship)
            {
                var previousVisits = relationship.Visits;
                relationship.Visits++;
                relationship.LastInteractionUtc = DateTime.UtcNow;
                AddAffinity(relationship, 0.20, "Returned to visit", NpcMemoryKind.Visit);

                if (previousVisits < 2 && relationship.Visits >= 2)
                    milestone = GetPersona(npc).Warmth >= 55 ? $"{npc.Name} remembers you from your last visit." : $"{npc.Name} gives you a small nod of recognition.";
                else if (previousVisits < 10 && relationship.Visits >= 10)
                    milestone = GetPersona(npc).Caution >= 60 ? $"{npc.Name} seems to have decided you are reliable." : $"{npc.Name} greets you as a familiar face.";
            }
            MarkDirty();

            if (milestone != null)
                player.SendMessage(milestone);
        }

        public static void OnPurchase(Player player, Vendor vendor, uint amount)
        {
            if (!CanRemember(player, vendor))
                return;

            var relationship = GetOrCreate(player, vendor);
            lock (relationship)
            {
                relationship.Purchases++;
                relationship.TotalSpent += amount;
                relationship.LastInteractionUtc = DateTime.UtcNow;
                var gain = Math.Min(1.25, 0.15 + Math.Log10(Math.Max(10, amount)) * 0.15);
                AddAffinity(relationship, gain, $"Made a purchase worth {amount:N0}", NpcMemoryKind.Purchase);
            }
            MarkDirty();
        }

        public static void OnSale(Player player, Vendor vendor, int amount)
        {
            if (!CanRemember(player, vendor))
                return;

            var relationship = GetOrCreate(player, vendor);
            lock (relationship)
            {
                relationship.Sales++;
                relationship.TotalSold += Math.Max(0, amount);
                relationship.LastInteractionUtc = DateTime.UtcNow;
                var gain = Math.Min(0.75, 0.10 + Math.Log10(Math.Max(10, amount)) * 0.10);
                AddAffinity(relationship, gain, $"Brought goods worth {amount:N0}", NpcMemoryKind.Sale);
            }
            MarkDirty();
        }

        public static VendorPersonaRates GetVendorRates(Player player, Vendor vendor)
        {
            var rates = new VendorPersonaRates(vendor.BuyPrice ?? 1.0, vendor.SellPrice ?? 1.0, 0.0);
            if (!PropertyManager.GetBool("npc_personas_enabled").Item
                || !PropertyManager.GetBool("npc_persona_vendor_discounts").Item
                || vendor.AlternateCurrency != null)
                return rates;

            if (!Relationships.TryGetValue(BuildKey(player.Guid.Full, vendor.WeenieClassId), out var relationship))
                return rates;

            double affinity;
            lock (relationship)
                affinity = relationship.Affinity;

            var threshold = PropertyManager.GetDouble("npc_persona_discount_affinity_threshold").Item;
            var maximum = Math.Clamp(PropertyManager.GetDouble("npc_persona_vendor_discount_max").Item, 0.0, 0.25);
            if (affinity <= threshold || maximum <= 0)
                return rates;

            var discount = Math.Clamp((affinity - threshold) / Math.Max(1.0, 100.0 - threshold), 0.0, 1.0) * maximum;
            return new VendorPersonaRates(rates.BuyRate * (1.0 + discount * 0.5), rates.SellRate * (1.0 - discount), discount);
        }

        public static NpcRelationship GetRelationship(Player player, Creature npc)
        {
            Relationships.TryGetValue(BuildKey(player.Guid.Full, npc.WeenieClassId), out var relationship);
            return relationship;
        }

        private static bool CanRemember(Player player, Creature npc) => PropertyManager.GetBool("npc_personas_enabled").Item && player != null && npc?.IsNPC == true;

        private static NpcRelationship GetOrCreate(Player player, Creature npc)
        {
            return Relationships.GetOrAdd(BuildKey(player.Guid.Full, npc.WeenieClassId), _ => new NpcRelationship
            {
                PlayerGuid = player.Guid.Full,
                PlayerName = player.Name,
                NpcWcid = npc.WeenieClassId,
                NpcName = npc.Name,
                LastInteractionUtc = DateTime.UtcNow,
            });
        }

        private static void AddAffinity(NpcRelationship relationship, double amount, string summary, NpcMemoryKind kind)
        {
            var today = DateTime.UtcNow.Date;
            if (relationship.AffinityDayUtc != today)
            {
                relationship.AffinityDayUtc = today;
                relationship.AffinityGainedToday = 0;
            }

            var dailyCap = Math.Max(0, PropertyManager.GetDouble("npc_persona_affinity_daily_cap").Item);
            var allowed = Math.Min(Math.Max(0, amount), Math.Max(0, dailyCap - relationship.AffinityGainedToday));
            relationship.Affinity = Math.Clamp(relationship.Affinity + allowed, -100, 100);
            relationship.AffinityGainedToday += allowed;

            var existing = relationship.Memories.FirstOrDefault(memory => memory.Kind == kind && memory.Summary == summary);
            if (existing != null)
            {
                existing.Count++;
                existing.LastSeenUtc = DateTime.UtcNow;
                existing.Salience = Math.Min(100, existing.Salience + 1);
            }
            else
            {
                relationship.Memories.Add(new NpcMemory { Kind = kind, Summary = summary, Count = 1, Salience = kind == NpcMemoryKind.Visit ? 10 : 25, LastSeenUtc = DateTime.UtcNow });
            }

            relationship.Memories = relationship.Memories
                .OrderByDescending(memory => memory.Salience)
                .ThenByDescending(memory => memory.LastSeenUtc)
                .Take(8)
                .ToList();
        }

        private static void MarkDirty()
        {
            dirty = true;
            if (nextSaveUtc == DateTime.MaxValue)
                nextSaveUtc = DateTime.UtcNow + SaveDebounce;
        }

        private static string BuildKey(uint playerGuid, uint npcWcid) => $"{playerGuid:X8}:{npcWcid}";
        private static int Trait(int seed, int shift) => Math.Abs((seed >> shift) ^ (seed * 397)) % 101;

        private static NpcRelationship CloneRelationship(NpcRelationship source)
        {
            lock (source)
            {
                return new NpcRelationship
                {
                    PlayerGuid = source.PlayerGuid,
                    PlayerName = source.PlayerName,
                    NpcWcid = source.NpcWcid,
                    NpcName = source.NpcName,
                    Affinity = source.Affinity,
                    Visits = source.Visits,
                    Purchases = source.Purchases,
                    Sales = source.Sales,
                    TotalSpent = source.TotalSpent,
                    TotalSold = source.TotalSold,
                    LastInteractionUtc = source.LastInteractionUtc,
                    AffinityDayUtc = source.AffinityDayUtc,
                    AffinityGainedToday = source.AffinityGainedToday,
                    Memories = source.Memories.Select(memory => new NpcMemory { Kind = memory.Kind, Summary = memory.Summary, Count = memory.Count, Salience = memory.Salience, LastSeenUtc = memory.LastSeenUtc }).ToList(),
                };
            }
        }
    }

    public readonly struct VendorPersonaRates
    {
        public double BuyRate { get; }
        public double SellRate { get; }
        public double Discount { get; }
        public VendorPersonaRates(double buyRate, double sellRate, double discount) { BuyRate = buyRate; SellRate = sellRate; Discount = discount; }
    }

    public sealed class NpcPersonaState { public List<NpcRelationship> Relationships { get; set; } = new List<NpcRelationship>(); }
    public sealed class NpcPersona { public int Warmth { get; set; } public int Caution { get; set; } public int Greed { get; set; } public int Curiosity { get; set; } public int Generosity { get; set; } }
    public sealed class NpcRelationship
    {
        public uint PlayerGuid { get; set; }
        public string PlayerName { get; set; }
        public uint NpcWcid { get; set; }
        public string NpcName { get; set; }
        public double Affinity { get; set; }
        public int Visits { get; set; }
        public int Purchases { get; set; }
        public int Sales { get; set; }
        public long TotalSpent { get; set; }
        public long TotalSold { get; set; }
        public DateTime LastInteractionUtc { get; set; }
        public DateTime AffinityDayUtc { get; set; }
        public double AffinityGainedToday { get; set; }
        public List<NpcMemory> Memories { get; set; } = new List<NpcMemory>();
    }
    public sealed class NpcMemory { public NpcMemoryKind Kind { get; set; } public string Summary { get; set; } public int Count { get; set; } public int Salience { get; set; } public DateTime LastSeenUtc { get; set; } }
    public enum NpcMemoryKind { Visit, Purchase, Sale, Quest, Gift, Help, Offense }
}