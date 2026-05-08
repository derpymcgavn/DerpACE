namespace ACE.Server.Managers
{
    /// <summary>
    /// Runtime-adjustable configuration for all DerpACE custom loot items.
    /// Adjust values in-game with @lootconfig set &lt;key&gt; &lt;value&gt;.
    /// Changes take effect immediately on the next loot roll or combat hit.
    /// </summary>
    public static class DerpACEConfig
    {
        // ──────────────────────────────────────────────────────────────────────
        // Defender's Shield
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float DefenderShieldDropChance { get; set; } = 0.05f;

        /// <summary>Minimum treasure tier required. Default 6.</summary>
        public static int DefenderShieldMinTier { get; set; } = 6;

        /// <summary>Extra targeting weight added to the shield-bearer. Default 0.5.</summary>
        public static float DefenderAggroBonus { get; set; } = 0.5f;

        // ──────────────────────────────────────────────────────────────────────
        // Archmagi Caster
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ArchmagiDropChance { get; set; } = 0.05f;

        /// <summary>Minimum treasure tier required. Default 6.</summary>
        public static int ArchmagiMinTier { get; set; } = 6;

        /// <summary>Chance per cast to fire the echo proc (0–1). Default 0.06 = 6%.</summary>
        public static float ArchmagiProcChance { get; set; } = 0.06f;

        // ──────────────────────────────────────────────────────────────────────
        // Thief's Dagger
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ThievesDaggerDropChance { get; set; } = 0.05f;

        /// <summary>Minimum treasure tier required. Default 6.</summary>
        public static int ThievesDaggerMinTier { get; set; } = 6;

        /// <summary>Chance per sneak-attack hit to fire the damage bonus proc (0–1). Default 0.06 = 6%.</summary>
        public static float ThievesDaggerProcChance { get; set; } = 0.06f;

        /// <summary>Fraction of damage added as a bonus when the proc fires (0–1). Default 0.10 = 10%.</summary>
        public static float ThievesDaggerProcBonus { get; set; } = 0.10f;

        /// <summary>Targeting weight subtracted from the dagger-bearer. Default 0.4.</summary>
        public static float ThievesDaggerAggroPenalty { get; set; } = 0.4f;

        // ──────────────────────────────────────────────────────────────────────
        // Sentinel's Spear
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float SentinelSpearDropChance { get; set; } = 0.05f;

        /// <summary>Minimum treasure tier required. Default 6.</summary>
        public static int SentinelSpearMinTier { get; set; } = 6;

        /// <summary>Chance per hit to fire the stamina drain proc (0–1). Default 0.10 = 10%.</summary>
        public static float SentinelSpearProcChance { get; set; } = 0.10f;

        /// <summary>Fraction of target's current stamina drained per proc (0–1). Default 0.10 = 10%.</summary>
        public static float SentinelSpearDrainPct { get; set; } = 0.10f;

        /// <summary>Multiplier applied to drained stamina before restoring it to the wielder. Default 1.25 = 125%.</summary>
        public static float SentinelSpearReturnMult { get; set; } = 1.25f;
    }
}
