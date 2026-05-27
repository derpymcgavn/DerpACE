using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories.Tables
{
    /// <summary>
    /// Maps a vendor's landblock (X, Y) coordinates to a loot tier (1–8) based on
    /// the town it is located in.  Used by <see cref="Vendor.LoadRandomLootInventory"/>
    /// to automatically stock tier-appropriate random loot when no explicit
    /// <c>PropertyInt.VendorLootTier</c> override has been set.
    ///
    /// As of DerpACE, the town anchor coordinates are sourced directly from the
    /// in-database PointsOfInterest table (the same data behind <c>/telepoi</c>),
    /// so any POI the server knows about can be used as a town centre.  The tier
    /// classification for each town name is defined in <see cref="TownTiers"/>.
    /// POIs that don't appear in <see cref="TownTiers"/> are still kept as valid
    /// anchors for permaload, but resolve to tier 0 for vendor stocking.
    ///
    /// Tier groupings:
    ///  Tier 1 (Levels  1–15): Holtburg, Arwic, Cragstone, Eastham, Glenden Wood,
    ///                          Lytelthorpe, Rithwic, Shoushi, Sawato, Mayoi, Yanshi,
    ///                          Tufa, Lost Wish Beach, Bluespire/Greenspire/Redspire
    ///  Tier 2 (Levels 20–30): Baishi, Yaraq, Nanto, Hebian-To, Wai Jhou, Tou-Tou
    ///  Tier 3 (Levels 40–50): Kara, Linvak Tukal, Plateau Village, Xarabydun,
    ///                          Oolutanga's Refuge, Crater Lake Village
    ///  Tier 4 (Levels 60–80): Al-Jalima, Qalaba'r, Uziz, Samsur, Danby's Outpost,
    ///                          Khayyaban, Neftet, MacNiall's Freehold
    ///  Tier 5 (Levels 100–115): Kryst, Sanamar, Timaru, Silyun, Stonehold
    ///  Tier 6 (Levels 135–160): Fiun Outpost, Dryreach, Ahurenga, Via Apt,
    ///                            Neydisa Castle, Zalphos' Retreat
    ///  Tier 7 (Levels 185+):    Fort Tethana, Zaikhal, Undercity, Candeth Keep
    ///  Tier 8 (Endgame / 200+): Ayan Baqur
    /// </summary>
    public static class VendorTownTier
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Default radius (in landblock units) around each town anchor within which
        /// a vendor is still considered to be "in" that town.  Individual towns may
        /// override this via <see cref="RadiusOverrides"/>.
        /// </summary>
        private const int DefaultRadius = 3;

        /// <summary>
        /// Per-town radius overrides (keyed by normalized name).  Larger cities and
        /// spread-out town clusters use a wider sweep so all vendor camps resolve.
        /// </summary>
        private static readonly Dictionary<string, int> RadiusOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { Normalize("Ayan Baqur"),    4 },
            { Normalize("Candeth Keep"),  4 },
            { Normalize("Zaikhal"),       4 },
            { Normalize("Holtburg"),      4 },
            { Normalize("Yaraq"),         4 },
            { Normalize("Shoushi"),       4 },
            { Normalize("Sanamar"),       4 },
            { Normalize("Fort Tethana"),  4 },
        };

        /// <summary>
        /// Town name (normalized) → loot tier.  POIs not present here are not
        /// classified as a tiered town (vendors there fall back to tier 0).
        /// </summary>
        private static readonly Dictionary<string, int> TownTiers = BuildTownTierTable();

        private static Dictionary<string, int> BuildTownTierTable()
        {
            var t = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void Add(int tier, params string[] names)
            {
                foreach (var n in names)
                    t[Normalize(n)] = tier;
            }

            // Tier 1 — starter towns
            Add(1, "Holtburg", "Arwic", "Cragstone", "Eastham", "Glenden Wood",
                   "Lytelthorpe", "Rithwic", "Shoushi", "Sawato", "Mayoi",
                   "Yanshi", "Tufa", "Lost Wish Beach",
                   "Bluespire", "Greenspire", "Redspire");

            // Tier 2 — intermediate
            Add(2, "Baishi", "Yaraq", "Nanto", "Hebian-To", "Tou-Tou", "Wai Jhou");

            // Tier 3 — mid-range
            Add(3, "Kara", "Linvak Tukal", "Plateau Village", "Xarabydun",
                   "Oolutanga's Refuge", "Crater Lake Village");

            // Tier 4 — advanced
            Add(4, "Al-Jalima", "Qalaba'r", "Uziz", "Samsur",
                   "Danby's Outpost", "Khayyaban", "Neftet", "MacNiall's Freehold");

            // Tier 5 — high-level
            Add(5, "Kryst", "Sanamar", "Timaru", "Silyun", "Stonehold");

            // Tier 6 — veteran
            Add(6, "Fiun Outpost", "Dryreach", "Ahurenga", "Via Apt",
                   "Neydisa Castle", "Zalphos' Retreat");

            // Tier 7 — endgame
            Add(7, "Fort Tethana", "Zaikhal", "Undercity", "Candeth Keep");

            // Tier 8 — pinnacle
            Add(8, "Ayan Baqur");

            return t;
        }

        // ── Internal anchor model (built from /telepoi data) ───────────────────

        private readonly struct TownAnchor
        {
            public readonly byte X;
            public readonly byte Y;
            public readonly int Tier;
            public readonly string Name;
            public readonly int Radius;

            public TownAnchor(byte x, byte y, int tier, string name, int radius)
            {
                X      = x;
                Y      = y;
                Tier   = tier;
                Name   = name;
                Radius = radius;
            }
        }

        private static readonly object _lock = new object();
        private static IReadOnlyList<TownAnchor> _anchors;

        /// <summary>
        /// Returns the cached anchor list, building it on first access from the
        /// PointsOfInterest cache.  Falls back to an empty list if the DB hasn't
        /// been populated yet (callers will simply get tier 0 / no permaload).
        /// </summary>
        private static IReadOnlyList<TownAnchor> GetAnchors()
        {
            var snapshot = _anchors;
            if (snapshot != null)
                return snapshot;

            lock (_lock)
            {
                if (_anchors != null)
                    return _anchors;

                _anchors = BuildAnchorsFromPoiCache();
                return _anchors;
            }
        }

        /// <summary>
        /// Forces the anchor list to be rebuilt from the current POI cache.  Call
        /// this if the PointsOfInterest table is re-cached at runtime.
        /// </summary>
        public static void Rebuild()
        {
            lock (_lock)
                _anchors = BuildAnchorsFromPoiCache();
        }

        private static IReadOnlyList<TownAnchor> BuildAnchorsFromPoiCache()
        {
            var anchors = new List<TownAnchor>();
            var seen    = new HashSet<uint>();

            try
            {
                DatabaseManager.World.CacheAllPointsOfInterest();
                var pois = DatabaseManager.World.GetPointsOfInterestCache();

                foreach (var kvp in pois)
                {
                    var poi = kvp.Value;
                    if (poi == null)
                        continue;

                    var weenie = DatabaseManager.World.GetCachedWeenie(poi.WeenieClassId);
                    if (weenie == null)
                        continue;

                    var dest = weenie.GetPosition(PositionType.Destination);
                    if (dest == null)
                        continue;

                    // Landblock high bytes from cell_id: X = bits 31-24, Y = bits 23-16
                    var lbHi = (ushort)(dest.LandblockId.Raw >> 16);
                    var x = (byte)(lbHi >> 8);
                    var y = (byte)(lbHi & 0xFF);

                    var normalized = Normalize(poi.Name);
                    TownTiers.TryGetValue(normalized, out var tier);

                    var radius = RadiusOverrides.TryGetValue(normalized, out var rOverride)
                        ? rOverride
                        : DefaultRadius;

                    // Dedupe by landblock — same town can have multiple POI portals.
                    var key = ((uint)x << 8) | y;
                    if (!seen.Add(key))
                        continue;

                    anchors.Add(new TownAnchor(x, y, tier, poi.Name, radius));
                }

                log.Info($"[DerpACE] VendorTownTier: built {anchors.Count} town anchors from /telepoi cache ({TownTiers.Count} tiered town names known).");
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE] VendorTownTier: failed to build anchors from POI cache — {ex.Message}");
            }

            return anchors;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the loot tier (1–8) for <paramref name="vendor"/> based on its
        /// current landblock position, or <c>0</c> if the vendor is not within
        /// the radius of any known town anchor.
        /// </summary>
        public static int GetTierForVendor(Vendor vendor)
        {
            if (vendor?.Location == null)
                return 0;

            return GetTierForLandblock((int)vendor.Location.LandblockX, (int)vendor.Location.LandblockY);
        }

        /// <summary>
        /// Returns the loot tier for the given landblock (X, Y) coordinates, or
        /// <c>0</c> if they do not fall within any known town.
        /// </summary>
        public static int GetTierForLandblock(int lbX, int lbY)
        {
            foreach (var anchor in GetAnchors())
            {
                if (anchor.Tier <= 0)
                    continue;

                if (Math.Abs(lbX - anchor.X) <= anchor.Radius && Math.Abs(lbY - anchor.Y) <= anchor.Radius)
                    return anchor.Tier;
            }

            return 0;
        }

        /// <summary>
        /// Returns the town name for the given landblock (X, Y) coordinates, or
        /// <c>null</c> if they do not match any known town.
        /// </summary>
        public static string GetTownName(int lbX, int lbY)
        {
            foreach (var anchor in GetAnchors())
            {
                if (Math.Abs(lbX - anchor.X) <= anchor.Radius && Math.Abs(lbY - anchor.Y) <= anchor.Radius)
                    return anchor.Name;
            }

            return null;
        }

        /// <summary>
        /// Returns one <c>(X, Y, Name)</c> entry per unique town anchor sourced
        /// from the POI cache.  Used by DerpACE to permaload every town landblock
        /// on server start.
        /// </summary>
        public static IReadOnlyList<(byte X, byte Y, string Name)> GetAllTownAnchors()
        {
            var anchors = GetAnchors();
            var result  = new List<(byte, byte, string)>(anchors.Count);
            foreach (var a in anchors)
                result.Add((a.X, a.Y, a.Name));
            return result;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Normalize a name for case/space/punctuation-insensitive matching.
        /// POI names in the DB are typically stored without spaces (e.g.
        /// "GlendenWood", "FortTethana"), so we strip whitespace, hyphens,
        /// apostrophes, and periods from both sides before comparing.
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return new string(name.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '\'' && c != '.').ToArray())
                .ToLowerInvariant();
        }
    }
}
