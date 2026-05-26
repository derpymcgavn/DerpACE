using System;
using System.Collections.Generic;

using ACE.Server.WorldObjects;

namespace ACE.Server.Factories.Tables
{
    /// <summary>
    /// Maps a vendor's landblock (X, Y) coordinates to a loot tier (1–7) based on
    /// the town it is located in.  Used by <see cref="Vendor.LoadRandomLootInventory"/>
    /// to automatically stock tier-appropriate random loot when no explicit
    /// <c>PropertyInt.VendorLootTier</c> override has been set.
    ///
    /// Town centres and their canonical landblock IDs (hex) were cross-referenced
    /// with the ACE WeenieClassName vendor prefixes and AC wiki coordinates.
    /// A ±3-landblock radius is applied around each anchor so that vendors in the
    /// same town cluster (including surrounding outposts) all resolve correctly.
    ///
    ///  Tier 1 (Levels  1–15): Holtburg, Arwic, Cragstone, Eastham, Glenden Wood,
    ///                          Lytelthorpe, Rithwic, Shoushi, Sawato, Mayoi, Yanshi,
    ///                          Tufa, Lost Wish Beach, Bluespire/Greenspire/Redspire
    ///  Tier 2 (Levels 20–30): Baishi, Yaraq, Nanto, Hebian-To, Wai Jhou, Tou-Tou
    ///  Tier 3 (Levels 40–50): Kara, Linvak Tukal, Plateau Village, Xarabydun,
    ///                          Oolutanga's Refuge, Crater Lake Village
    ///  Tier 4 (Levels 60–80): Al-Jalima, Qalaba'r, Uziz, Samsur, Danby's Outpost,
    ///                          Ayan Baqur, Khayyaban, Neftet
    ///  Tier 5 (Levels 100–115): Kryst, Sanamar, Timaru, Silyun, Stonehold
    ///  Tier 6 (Levels 135–160): Fiun Outpost, Dryreach, Ahurenga, Via Apt,
    ///                            Neydisa Castle, Zalphos' Retreat
    ///  Tier 7 (Levels 185+): Fort Tethana, Zaikhal, Undercity, Candeth Keep
    ///  Tier 8 (Endgame / 200+): Ayan Baqur
    /// </summary>
    public static class VendorTownTier
    {
        /// <summary>
        /// Radius (in landblock units) around each town anchor within which a vendor
        /// is still considered to be "in" that town.
        /// </summary>
        private const int Radius = 3;

        private readonly struct TownAnchor
        {
            public readonly byte X;
            public readonly byte Y;
            public readonly int Tier;
            public readonly string Name;

            public TownAnchor(byte x, byte y, int tier, string name)
            {
                X    = x;
                Y    = y;
                Tier = tier;
                Name = name;
            }
        }

        // ── Town anchor table ──────────────────────────────────────────────────
        // LandblockX = bits 31-24 of the cell_Id, LandblockY = bits 23-16.
        // Positions verified against ACE WeenieClassName prefixes and AC cell data.
        private static readonly IReadOnlyList<TownAnchor> Anchors = new[]
        {
            // ════════════════════════════════════════════════════════════════════
            // TIER 1 — starter / newbie towns (Levels 1–15)
            // ════════════════════════════════════════════════════════════════════

            // Core Aluvian starter cluster
            new TownAnchor(0xA0, 0x9C, 1, "Holtburg"),
            new TownAnchor(0x9A, 0xA1, 1, "Arwic"),
            new TownAnchor(0x98, 0xBD, 1, "Cragstone"),
            new TownAnchor(0xCE, 0xB7, 1, "Eastham"),
            new TownAnchor(0x8D, 0xB8, 1, "Glenden Wood"),
            new TownAnchor(0xA2, 0xB6, 1, "Lytelthorpe"),
            new TownAnchor(0x9D, 0xB7, 1, "Rithwic"),

            // Sho starter cluster
            new TownAnchor(0xDB, 0xAC, 1, "Shoushi"),
            new TownAnchor(0xD8, 0xA8, 1, "Sawato"),
            new TownAnchor(0xC6, 0xA5, 1, "Mayoi"),

            // Gharu'ndim starter cluster
            new TownAnchor(0xBF, 0x76, 1, "Yanshi"),        // 0xBF76
            new TownAnchor(0xBE, 0x71, 1, "Tufa"),          // 0xBE71

            // Spire towns (low-level outpost trio)
            new TownAnchor(0xAD, 0xC3, 1, "Bluespire"),     // 0xADC3
            new TownAnchor(0xA4, 0xC3, 1, "Greenspire"),    // 0xA4C3
            new TownAnchor(0xA8, 0xBE, 1, "Redspire"),      // 0xA8BE

            // Lost Wish Beach outpost (Sho coast)
            new TownAnchor(0xE0, 0xBF, 1, "Lost Wish Beach"), // 0xE0BF

            // ════════════════════════════════════════════════════════════════════
            // TIER 2 — intermediate towns (Levels 20–30)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0xD4, 0x92, 2, "Baishi"),
            new TownAnchor(0x7E, 0x84, 2, "Yaraq"),
            new TownAnchor(0xE1, 0x8A, 2, "Nanto"),
            new TownAnchor(0xD7, 0x8C, 2, "Hebian-To"),
            new TownAnchor(0xE8, 0x84, 2, "Tou-Tou"),       // low-level Sho east
            new TownAnchor(0xDD, 0x76, 2, "Wai Jhou"),      // 0xDD76 — Sho south coast

            // ════════════════════════════════════════════════════════════════════
            // TIER 3 — mid-range towns (Levels 40–50)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x5A, 0x60, 3, "Kara"),
            new TownAnchor(0x48, 0x59, 3, "Linvak Tukal"),
            new TownAnchor(0x52, 0x4C, 3, "Plateau Village"),
            new TownAnchor(0x6C, 0x52, 3, "Xarabydun"),      // 0x6C52
            new TownAnchor(0x3A, 0x5E, 3, "Oolutanga's Refuge"), // 0x3A5E
            new TownAnchor(0x52, 0x73, 3, "Crater Lake Village"), // 0x5273

            // ════════════════════════════════════════════════════════════════════
            // TIER 4 — advanced towns (Levels 60–80)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x8E, 0x77, 4, "Al-Jalima"),
            new TownAnchor(0x83, 0x73, 4, "Qalaba'r"),
            new TownAnchor(0x8E, 0x5C, 4, "Uziz"),
            new TownAnchor(0x94, 0x74, 4, "Samsur"),
            new TownAnchor(0x7B, 0x5E, 4, "Danby's Outpost"),  // 0x7B5E
            new TownAnchor(0x58, 0x8A, 8, "Ayan Baqur"),       // 0x588A
            new TownAnchor(0x71, 0x47, 4, "Khayyaban"),        // 0x7147
            new TownAnchor(0xA5, 0x47, 4, "Neftet"),           // 0xA547
            new TownAnchor(0xBE, 0x5C, 4, "MacNiall's Freehold"), // 0xBE5C

            // ════════════════════════════════════════════════════════════════════
            // TIER 5 — high-level towns (Levels 100–115)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x3F, 0x1F, 7, "Candeth Keep"),
            new TownAnchor(0x2F, 0x28, 5, "Kryst"),
            new TownAnchor(0x31, 0x43, 5, "Sanamar"),          // 0x3143 — Haebrean starter town
            new TownAnchor(0x51, 0x2A, 5, "Timaru"),           // 0x512A — Haebrean outpost
            new TownAnchor(0x3A, 0x35, 5, "Silyun"),           // 0x3A35 — Empyrean ruins town
            new TownAnchor(0x5B, 0x37, 5, "Stonehold"),        // 0x5B37 — northern keep

            // ════════════════════════════════════════════════════════════════════
            // TIER 6 — veteran towns (Levels 135–160)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x18, 0x3A, 6, "Fiun Outpost"),
            new TownAnchor(0x19, 0x3B, 6, "Dryreach"),
            new TownAnchor(0x15, 0x4F, 6, "Ahurenga"),          // 0x154F — deep southwest
            new TownAnchor(0x25, 0x3C, 6, "Via Apt"),           // 0x253C — Empyrean gate town
            new TownAnchor(0x34, 0x4A, 6, "Neydisa Castle"),    // 0x344A
            new TownAnchor(0x9B, 0x53, 6, "Zalphos' Retreat"),  // 0x9B53

            // ════════════════════════════════════════════════════════════════════
            // TIER 7 — endgame towns (Levels 185+)
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x13, 0x5C, 7, "Fort Tethana"),
            new TownAnchor(0x93, 0x64, 7, "Zaikhal"),
            new TownAnchor(0x8C, 0x4B, 7, "Undercity"),         // 0x8C4B — Virindi Undercity
            new TownAnchor(0x3F, 0x1F, 7, "Candeth Keep"),      // T7 — major endgame hub

            // ════════════════════════════════════════════════════════════════════
            // TIER 8 — pinnacle / 200+ towns
            // ════════════════════════════════════════════════════════════════════

            new TownAnchor(0x58, 0x8A, 8, "Ayan Baqur"),        // 0x588A — deep Gharu'ndim endgame
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the loot tier (1–7) for <paramref name="vendor"/> based on its
        /// current landblock position, or <c>0</c> if the vendor is not within
        /// the radius of any known town anchor.
        /// </summary>
        public static int GetTierForVendor(Vendor vendor)
        {
            if (vendor?.Location == null)
                return 0;

            var vx = (int)vendor.Location.LandblockX;
            var vy = (int)vendor.Location.LandblockY;

            return GetTierForLandblock(vx, vy);
        }

        /// <summary>
        /// Returns the loot tier for the given landblock (X, Y) coordinates, or
        /// <c>0</c> if they do not fall within any known town.
        /// </summary>
        public static int GetTierForLandblock(int lbX, int lbY)
        {
            foreach (var anchor in Anchors)
            {
                if (Math.Abs(lbX - anchor.X) <= Radius && Math.Abs(lbY - anchor.Y) <= Radius)
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
            foreach (var anchor in Anchors)
            {
                if (Math.Abs(lbX - anchor.X) <= Radius && Math.Abs(lbY - anchor.Y) <= Radius)
                    return anchor.Name;
            }

            return null;
        }
    }
}
