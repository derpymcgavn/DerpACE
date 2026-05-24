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

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int DefenderShieldMinTier { get; set; } = 2;

        /// <summary>Extra targeting weight added to the shield-bearer. Default 0.5.</summary>
        public static float DefenderAggroBonus { get; set; } = 0.5f;

        // ──────────────────────────────────────────────────────────────────────
        // Archmagi Caster
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ArchmagiDropChance { get; set; } = 0.05f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int ArchmagiMinTier { get; set; } = 2;

        /// <summary>Chance per cast to fire the echo proc (0–1). Default 0.06 = 6%.</summary>
        public static float ArchmagiProcChance { get; set; } = 0.06f;

        /// <summary>Targeting weight subtracted from the caster (fragile burst). Default 0.2.</summary>
        public static float ArchmagiAggroPenalty { get; set; } = 0.2f;

        // ──────────────────────────────────────────────────────────────────────
        // Hierophant Caster (support life-staff variant of Martyr Staff)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Chance per eligible life caster (e.g. Martyr Staff) to roll the Hierophant variant. Default 0.10 = 10%.</summary>
        public static float HierophantDropChance { get; set; } = 0.10f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int HierophantMinTier { get; set; } = 2;

        /// <summary>Minimum heal-boost multiplier rolled at loot time (added to 1.0). Default 0.01 = +1%.</summary>
        public static float HierophantHealBoostMin { get; set; } = 0.01f;

        /// <summary>Maximum heal-boost multiplier rolled at loot time (added to 1.0). Default 0.10 = +10%.</summary>
        public static float HierophantHealBoostMax { get; set; } = 0.10f;

        /// <summary>Chance per beneficial heal cast to fire the regenerating HoT proc on the target. Default 0.15 = 15%.</summary>
        public static float HierophantHotProcChance { get; set; } = 0.15f;

        /// <summary>Minimum HoT magnitude rolled at loot time (fraction of target MaxHealth granted over the duration). Default 0.01 = 1%.</summary>
        public static float HierophantHotPctMin { get; set; } = 0.01f;

        /// <summary>Maximum HoT magnitude rolled at loot time. Default 0.25 = 25%.</summary>
        public static float HierophantHotPctMax { get; set; } = 0.25f;

        /// <summary>Total HoT duration in seconds.</summary>
        public static float HierophantHotDurationSeconds { get; set; } = 12.0f;

        /// <summary>HoT tick interval in seconds.</summary>
        public static float HierophantHotTickInterval { get; set; } = 3.0f;

        /// <summary>Bonus fellowship-echo heal as fraction of the primary heal applied to each fellow-in-range. Default 0.20 = 20%.</summary>
        public static float HierophantFellowEchoPct { get; set; } = 0.20f;

        /// <summary>Maximum range (meters) within which fellowship members receive the echo heal. Default 30.</summary>
        public static float HierophantFellowEchoRange { get; set; } = 30.0f;

        /// <summary>Targeting weight added to the hierophant-bearer (healer pull). Default 0.35.</summary>
        public static float HierophantAggroBonus { get; set; } = 0.35f;

        // ──────────────────────────────────────────────────────────────────────
        // Thief's Dagger
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ThievesDaggerDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int ThievesDaggerMinTier { get; set; } = 5;

        /// <summary>Chance per sneak-attack hit to fire the damage bonus proc (0–1). Default 0.06 = 6%.</summary>
        public static float ThievesDaggerProcChance { get; set; } = 0.05f;

        /// <summary>Fraction of damage added as a bonus when the proc fires (0–1). Default 0.10 = 10%.</summary>
        public static float ThievesDaggerProcBonus { get; set; } = 0.08f;

        /// <summary>Targeting weight subtracted from the dagger-bearer. Default 0.4.</summary>
        public static float ThievesDaggerAggroPenalty { get; set; } = 0.3f;

        // ──────────────────────────────────────────────────────────────────────
        // Sentinel's Spear
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float SentinelSpearDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int SentinelSpearMinTier { get; set; } = 5;

        /// <summary>Chance per hit to fire the stamina drain proc (0–1). Default 0.10 = 10%.</summary>
        public static float SentinelSpearProcChance { get; set; } = 0.08f;

        /// <summary>Fraction of target's current stamina drained per proc (0–1). Default 0.10 = 10%.</summary>
        public static float SentinelSpearDrainPct { get; set; } = 0.08f;

        /// <summary>Multiplier applied to drained stamina before restoring it to the wielder. Default 1.25 = 125%.</summary>
        public static float SentinelSpearReturnMult { get; set; } = 1.0f;

        /// <summary>Targeting weight added to the spear-bearer (off-tank). Default 0.25.</summary>
        public static float SentinelSpearAggroBonus { get; set; } = 0.25f;

        // ──────────────────────────────────────────────────────────────────────
        // Elemental Unarmed (cast-on-strike)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Chance that a magical elemental unarmed weapon rolls the cast-on-strike proc (0–1). Default 0.05 = 5%.</summary>
        public static float UnarmedElemDropChance { get; set; } = 0.05f;

        /// <summary>Minimum proc rate (integer %) rolled at loot time. Default 1.</summary>
        public static int UnarmedElemProcMin { get; set; } = 1;

        /// <summary>Maximum proc rate (integer %) rolled at loot time. Default 5.</summary>
        public static int UnarmedElemProcMax { get; set; } = 5;

        // ──────────────────────────────────────────────────────────────────────
        // Fencer's Blade (Épée / Rapier / Schlager)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float FencerBladeDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int FencerBladeMinTier { get; set; } = 5;

        /// <summary>Minimum armor pierce % rolled at loot time (integer). Default 1.</summary>
        public static int FencerPierceMin { get; set; } = 2;

        /// <summary>Maximum armor pierce % rolled at loot time (integer). Default 5.</summary>
        public static int FencerPierceMax { get; set; } = 8;

        /// <summary>Minimum pierce proc chance % rolled at loot time (integer). Default 1.</summary>
        public static int FencerPierceProcMin { get; set; } = 2;

        /// <summary>Maximum pierce proc chance % rolled at loot time (integer). Default 4.</summary>
        public static int FencerPierceProcMax { get; set; } = 6;

        /// <summary>Minimum deflect proc chance % rolled at loot time (integer). Default 1.</summary>
        public static int FencerDeflectMin { get; set; } = 1;

        /// <summary>Maximum deflect proc chance % rolled at loot time (integer). Default 2.</summary>
        public static int FencerDeflectMax { get; set; } = 2;

        // ──────────────────────────────────────────────────────────────────────
        // Ravager's Axe (Axe / TwoHandedAxe)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float RavagerAxeDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int RavagerAxeMinTier { get; set; } = 5;

        /// <summary>Minimum bleed proc chance % rolled at loot time (integer). Default 2.</summary>
        public static int RavagerProcMin { get; set; } = 2;

        /// <summary>Maximum bleed proc chance % rolled at loot time (integer). Default 5.</summary>
        public static int RavagerProcMax { get; set; } = 4;

        /// <summary>Minimum bleed damage % (of the triggering hit) rolled at loot time (integer). Default 30.</summary>
        public static int RavagerBleedMin { get; set; } = 20;

        /// <summary>Maximum bleed damage % (of the triggering hit) rolled at loot time (integer). Default 60.</summary>
        public static int RavagerBleedMax { get; set; } = 40;

        /// <summary>Multiplier applied to the bleed total when the weapon is two-handed. Default 1.5.</summary>
        public static float RavagerTwoHandMult { get; set; } = 1.35f;

        /// <summary>Number of bleed ticks. Default 3.</summary>
        public static int RavagerBleedTicks { get; set; } = 3;

        /// <summary>Seconds between bleed ticks. Default 2.</summary>
        public static float RavagerBleedInterval { get; set; } = 2.5f;

        /// <summary>Chance per hammer crush proc to cleave nearby monsters (0-1). Default 0.15 = 15%.</summary>
        public static float RavagerHammerCleaveChance { get; set; } = 0.15f;

        /// <summary>Maximum total targets affected by hammer cleave including the primary target. Default 5.</summary>
        public static int RavagerHammerCleaveMaxTargets { get; set; } = 5;

        /// <summary>Secondary-target damage scale for hammer cleave. Default 0.50 = 50%.</summary>
        public static float RavagerHammerCleaveDamageScale { get; set; } = 0.50f;

        /// <summary>Targeting weight added to the axe-wielder (berserker pull). Default 0.3.</summary>
        public static float RavagerAxeAggroBonus { get; set; } = 0.3f;

        /// <summary>Radius around the primary target for hammer cleave secondary hits. Default 10 meters.</summary>
        public static float RavagerHammerCleaveRadius { get; set; } = 10.0f;

        // ──────────────────────────────────────────────────────────────────────
        // Warden's Maul (Mace / MaceJitte / TwoHandedMace)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float WardenMaulDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int WardenMaulMinTier { get; set; } = 5;

        /// <summary>Minimum concussion proc chance % rolled at loot time (integer). Default 4.</summary>
        public static int WardenProcMin { get; set; } = 3;

        /// <summary>Maximum concussion proc chance % rolled at loot time (integer). Default 8.</summary>
        public static int WardenProcMax { get; set; } = 6;

        /// <summary>Minimum flat defense-skill penalty rolled at loot time (integer). Default 10.</summary>
        public static int WardenPenaltyMin { get; set; } = 8;

        /// <summary>Maximum flat defense-skill penalty rolled at loot time (integer). Default 30.</summary>
        public static int WardenPenaltyMax { get; set; } = 20;

        /// <summary>Minimum debuff duration in seconds rolled at loot time (integer). Default 5.</summary>
        public static int WardenDurationMin { get; set; } = 4;

        /// <summary>Maximum debuff duration in seconds rolled at loot time (integer). Default 10.</summary>
        public static int WardenDurationMax { get; set; } = 8;

        /// <summary>Multiplier applied to the penalty when the weapon is two-handed. Default 1.5.</summary>
        public static float WardenTwoHandMult { get; set; } = 1.3f;

        /// <summary>Targeting weight added to the maul-wielder (guardian pull). Default 0.3.</summary>
        public static float WardenMaulAggroBonus { get; set; } = 0.3f;

        // ──────────────────────────────────────────────────────────────────────
        // Resolute Blade (Sword / TwoHandedSword)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ResoluteBladeDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int ResoluteBladeMinTier { get; set; } = 5;

        /// <summary>Minimum heal-on-crit proc chance % rolled at loot time (integer). Default 25.</summary>
        public static int ResoluteProcMin { get; set; } = 15;

        /// <summary>Maximum heal-on-crit proc chance % rolled at loot time (integer). Default 50.</summary>
        public static int ResoluteProcMax { get; set; } = 30;

        /// <summary>Minimum heal % of crit damage rolled at loot time (integer). Default 2.</summary>
        public static int ResoluteHealMin { get; set; } = 2;

        /// <summary>Maximum heal % of crit damage rolled at loot time (integer). Default 5.</summary>
        public static int ResoluteHealMax { get; set; } = 4;

        /// <summary>Killing-blow burst % of MaxHealth and MaxStamina restored. Default 0.10 = 10%.</summary>
        public static float ResoluteKillBurstPct { get; set; } = 0.06f;

        /// <summary>Multiplier applied to the kill burst when the weapon is two-handed. Default 1.5.</summary>
        public static float ResoluteTwoHandMult { get; set; } = 1.25f;

        // ──────────────────────────────────────────────────────────────────────
        // Polebreaker Staff (Staff)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float PolebreakerDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int PolebreakerMinTier { get; set; } = 5;

        /// <summary>Minimum per-stack damage bonus % rolled at loot time (integer). Default 1.</summary>
        public static int PolebreakerStackMin { get; set; } = 1;

        /// <summary>Maximum per-stack damage bonus % rolled at loot time (integer). Default 3.</summary>
        public static int PolebreakerStackMax { get; set; } = 2;

        /// <summary>Minimum max-stack count rolled at loot time (integer). Default 4.</summary>
        public static int PolebreakerMaxStackMin { get; set; } = 3;

        /// <summary>Maximum max-stack count rolled at loot time (integer). Default 6.</summary>
        public static int PolebreakerMaxStackMax { get; set; } = 5;

        /// <summary>Targeting weight added to the staff-wielder (stacking DPS pull). Default 0.2.</summary>
        public static float PolebreakerStaffAggroBonus { get; set; } = 0.2f;

        // ───────────────────────────────────────────────────────────────────
        // Stalker's Bow (Bow)
        // ───────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float StalkerBowDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int StalkerBowMinTier { get; set; } = 5;

        /// <summary>Minimum first-strike proc chance % rolled at loot time (integer). Default 30.</summary>
        public static int StalkerProcMin { get; set; } = 20;

        /// <summary>Maximum first-strike proc chance % rolled at loot time (integer). Default 50.</summary>
        public static int StalkerProcMax { get; set; } = 35;

        /// <summary>Minimum first-strike bonus damage % rolled at loot time (integer). Default 25.</summary>
        public static int StalkerBonusMin { get; set; } = 15;

        /// <summary>Maximum first-strike bonus damage % rolled at loot time (integer). Default 50.</summary>
        public static int StalkerBonusMax { get; set; } = 30;

        /// <summary>Targeting weight subtracted from the stalker bow-wielder (stealth sniper). Default 0.2.</summary>
        public static float StalkerBowAggroPenalty { get; set; } = 0.2f;

        // ───────────────────────────────────────────────────────────────────
        // Breacher's Crossbow (Crossbow)
        // ───────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float BreacherCrossbowDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int BreacherCrossbowMinTier { get; set; } = 5;

        /// <summary>Minimum armor ignore chance % rolled at loot time (integer). Default 5.</summary>
        public static int BreacherArmorIgnoreMin { get; set; } = 4;

        /// <summary>Maximum armor ignore chance % rolled at loot time (integer). Default 15.</summary>
        public static int BreacherArmorIgnoreMax { get; set; } = 10;

        // ───────────────────────────────────────────────────────────────────
        // Reaper's Atlatl (Atlatl)
        // ───────────────────────────────────────────────────────────────────

        /// <summary>Loot drop chance (0–1). Default 0.05 = 5%.</summary>
        public static float ReaperAtlatlDropChance { get; set; } = 0.02f;

        /// <summary>Minimum treasure tier required. Default 2.</summary>
        public static int ReaperAtlatlMinTier { get; set; } = 5;

        /// <summary>Minimum kill-heal proc % rolled at loot time (integer). Default 30.</summary>
        public static int ReaperProcMin { get; set; } = 20;

        /// <summary>Maximum kill-heal proc % rolled at loot time (integer). Default 60.</summary>
        public static int ReaperProcMax { get; set; } = 40;

        /// <summary>Minimum heal % of MaxHealth rolled at loot time (integer). Default 5.</summary>
        public static int ReaperHealMin { get; set; } = 4;

        /// <summary>Maximum heal % of MaxHealth rolled at loot time (integer). Default 15.</summary>
        public static int ReaperHealMax { get; set; } = 10;

        // ---------- Loot Modifier Balance Controls ----------

        /// <summary>Global multiplier applied to custom loot modifier drop chances. Default 1.0.</summary>
        public static float LootModifierGlobalDropMultiplier { get; set; } = 1.0f;

        /// <summary>When true, only one custom weapon modifier can be applied to a generated weapon. Default true.</summary>
        public static bool LootModifierExclusivePerItem { get; set; } = true;

        /// <summary>When true, eligible weapon types may roll alternate themed modifiers for variety. Default true.</summary>
        public static bool LootModifierInterchangeable { get; set; } = true;

        /// <summary>Minimum tier required before interchangeable modifier logic is allowed. Default 6.</summary>
        public static int LootModifierInterchangeableMinTier { get; set; } = 6;

        // ---------- Armor Item-Spell Roll Chances ----------

        /// <summary>Per-bane roll chance on normal (non-Covenant) armor. Retail default was 0.15. Default 0.20 (slight bump).</summary>
        public static float ArmorBaneChanceNormal { get; set; } = 0.20f;

        /// <summary>Per-bane roll chance on Covenant armor. Default 0.60 (significant bump).</summary>
        public static float ArmorBaneChanceCovenant { get; set; } = 0.60f;

        /// <summary>
        /// Flat bonus added to the base tier enchantment roll chance for armor and melee/missile weapons.
        /// Retail base caps at 0.60 for T6-T8. Setting this to 0.25 means T6 armor goes from 60% to 85% chance for the first critter/life spell.
        /// Default 0.25.
        /// </summary>
        public static float ArmorEnchantmentChanceBonus { get; set; } = 0.25f;

        /// <summary>
        /// Maximum number of critter/life enchantments that can roll onto a single armor piece.
        /// Retail cap is 1. Setting this to 2 lets high-tier armor occasionally roll two critter or life spells.
        /// Default 2.
        /// </summary>
        public static int ArmorMaxEnchantments { get; set; } = 2;

        /// <summary>
        /// For each additional enchantment beyond the first (up to ArmorMaxEnchantments), the base roll chance is
        /// multiplied by this value. Default 0.50 = 50% of the first-spell chance.
        /// </summary>
        public static float ArmorExtraEnchantmentChanceMult { get; set; } = 0.50f;

        // ──────────────────────────────────────────────────────────────────────
        // Weapon Elemental Blast-on-Strike (all weapon classes)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimum treasure tier before a weapon can roll a cast-on-strike elemental blast proc.
        /// Default 5 (T5 and above only).
        /// </summary>
        public static int WeaponBlastProcMinTier { get; set; } = 5;

        /// <summary>
        /// Chance (0–1) at the minimum tier for a weapon to receive an elemental blast-on-strike proc.
        /// Scales linearly up to WeaponBlastProcChanceMax at T8. Default 0.01 (1%).
        /// </summary>
        public static float WeaponBlastProcChanceMin { get; set; } = 0.01f;

        /// <summary>
        /// Chance (0–1) at T8 for a weapon to receive an elemental blast-on-strike proc.
        /// Default 0.04 (4%).
        /// </summary>
        public static float WeaponBlastProcChanceMax { get; set; } = 0.04f;

        /// <summary>
        /// Minimum ProcSpellRate (0–1) rolled onto the weapon for the blast proc.
        /// Default 0.00001 (0.001%, effectively a rare per-hit roll).
        /// </summary>
        public static float WeaponBlastProcRateMin { get; set; } = 0.00001f;

        /// <summary>
        /// Maximum ProcSpellRate (0–1) rolled onto the weapon for the blast proc.
        /// Default 0.001 (0.1%).
        /// </summary>
        public static float WeaponBlastProcRateMax { get; set; } = 0.001f;

        // ---------- Mob Modifiers (rare spawn affixes) ----------

        /// <summary>Master switch for the mob modifier system. Default true.</summary>
        public static bool MobModifierEnabled { get; set; } = true;

        /// <summary>Minimum DeathTreasure tier (or Level/10) for a mob to be eligible for any modifier. Default 2 (starts at tier 2).</summary>
        public static int MobModifierMinTier { get; set; } = 2;

        /// <summary>Per-spawn chance (0-1) for the Nocturnal modifier to land on an eligible mob (only at night). Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float NocturnalMobChance { get; set; } = 0.01f;

        /// <summary>Per-spawn chance (0-1) for the Exploding modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float ExplodingMobChance { get; set; } = 0.01f;

        /// <summary>Radius (meters) of the Exploding modifier's on-death AoE. Default 6.</summary>
        public static float ExplodingMobRadius { get; set; } = 6.0f;

        /// <summary>Fraction of the mob's MaxHealth dealt as on-death AoE damage. Default 0.25.</summary>
        public static float ExplodingMobDamageScale { get; set; } = 0.25f;

        /// <summary>Per-spawn chance (0-1) for the Vampiric modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float VampiricMobChance { get; set; } = 0.01f;

        /// <summary>Minimum vampiric lifesteal % rolled at spawn (integer). Default 10.</summary>
        public static int VampiricLifestealMin { get; set; } = 10;

        /// <summary>Maximum vampiric lifesteal % rolled at spawn (integer). Default 25.</summary>
        public static int VampiricLifestealMax { get; set; } = 25;

        /// <summary>Per-spawn chance (0-1) for the Thief modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float ThiefMobChance { get; set; } = 0.01f;

        /// <summary>Per-spawn chance (0-1) for the Scout modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float ScoutMobChance { get; set; } = 0.01f;

        /// <summary>Per-hit chance (0-1) the Thief modifier steals a tradenote stack from a player. Default 0.10.</summary>
        public static float ThiefStealProc { get; set; } = 0.10f;

        /// <summary>Chance (0-1) a slain Thieving mob drops a Chest of Tradenotes (WCID 80524) on its corpse. Default 0.10.</summary>
        public static float ThiefChestDropChance { get; set; } = 0.10f;

        /// <summary>WCID of the chest spawned by the Thief mob death drop. Default 80524.</summary>
        public static uint ThiefChestWcid { get; set; } = 80524;

        /// <summary>Seconds before the spawned Thief chest auto-despawns. Default 30.</summary>
        public static float ThiefChestDespawnSeconds { get; set; } = 30.0f;

        /// <summary>Per-spawn chance (0-1) for the Simulacrum modifier to land on eligible mobs (only CreatureType.Simulacrum). Default 0 (disabled - only applied via /cimob spawn command).</summary>
        public static float SimulacrumMobChance { get; set; } = 0.0f;

        /// <summary>Per-spawn chance (0-1) for the Healer modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float HealerMobChance { get; set; } = 0.01f;

        /// <summary>Maximum range (meters) a Healer mob will look for wounded allies to mend. Default 25.</summary>
        public static float HealerMobRange { get; set; } = 25.0f;

        /// <summary>Allies whose Health/MaxHealth ratio is below this value are eligible to be healed. Default 0.75.</summary>
        public static float HealerMobHealThreshold { get; set; } = 0.75f;

        /// <summary>Seconds between Heal Other casts by a Healer mob. Default 8.</summary>
        public static float HealerMobCooldownSeconds { get; set; } = 8.0f;

        /// <summary>Per-spawn chance (0-1) for the Tank modifier to land on an eligible mob. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float TankMobChance { get; set; } = 0.01f;

        /// <summary>Health multiplier for Tank mobs. Default 2.5 (250%).</summary>
        public static float TankMobHealthMultiplier { get; set; } = 2.5f;

        /// <summary>Physical damage reduction for Tank mobs (0-1). Default 0.3 (30% reduction).</summary>
        public static float TankMobPhysicalReduction { get; set; } = 0.3f;

        /// <summary>Healing effectiveness multiplier for Tank mobs. Default 1.2 (20% more effective).</summary>
        public static float TankMobHealBonus { get; set; } = 1.2f;

        /// <summary>Skill bonus added to Light Weapons and Shield for Tank mobs. Default 200.</summary>
        public static int TankMobSkillBonus { get; set; } = 200;

        // ---------- Creature Affixes (ported from ACE.BaseMod Expansion) ----------

        /// <summary>Per-spawn chance (0-1) for the Reaper affix. Default 0.01 (1% at tier 2, scales to 4% at tier 8).</summary>
        public static float ReaperMobChance { get; set; } = 0.01f;

        /// <summary>Bonus damage multiplier applied to a Reaper's outgoing melee hits (on top of its base damage). Default 1.35.</summary>
        public static float ReaperDamageBonus { get; set; } = 1.35f;

        /// <summary>Fraction (0-1) of damage dealt that a Reaper drains back as health. Default 0.25.</summary>
        public static float ReaperLifedrainPct { get; set; } = 0.25f;

        /// <summary>Per-spawn chance (0-1) for the Necromancer affix. Default 0.01.</summary>
        public static float NecromancerMobChance { get; set; } = 0.01f;

        /// <summary>Per-hit chance (0-1) a Necromancer applies a nether DoT to its target. Default 0.30.</summary>
        public static float NecromancerDotChance { get; set; } = 0.30f;

        /// <summary>Total nether DoT damage dealt over its duration (split across ticks). Default 60.</summary>
        public static float NecromancerDotTotal { get; set; } = 60.0f;

        /// <summary>Per-spawn chance (0-1) for the Merger affix. Default 0.01.</summary>
        public static float MergerMobChance { get; set; } = 0.01f;

        /// <summary>Max times a single Merger may absorb a same-WCID neighbor. Default 5.</summary>
        public static int MergerMaxMerges { get; set; } = 5;

        /// <summary>Radius (meters) a Merger searches for a same-WCID neighbor on heartbeat. Default 10.</summary>
        public static float MergerSearchRange { get; set; } = 10.0f;

        /// <summary>Seconds between Merger absorb attempts. Default 12.</summary>
        public static float MergerCooldownSeconds { get; set; } = 12.0f;

        /// <summary>Per-spawn chance (0-1) for the Horde affix. Default 0.01.</summary>
        public static float HordeMobChance { get; set; } = 0.01f;

        /// <summary>Minimum starting swarm size for a Horde. Default 3.</summary>
        public static int HordeMinSize { get; set; } = 3;

        /// <summary>Maximum starting swarm size for a Horde. Default 6.</summary>
        public static int HordeMaxSize { get; set; } = 6;

        /// <summary>Per-spawn chance (0-1) for the Warder affix. Default 0.01.</summary>
        public static float WarderMobChance { get; set; } = 0.01f;

        /// <summary>Radius (meters) a Warder's ward extends to nearby creatures. Default 8.</summary>
        public static float WarderRange { get; set; } = 8.0f;

        /// <summary>Per-spawn chance (0-1) for the Illusionist affix. Default 0.01.</summary>
        public static float IllusionistMobChance { get; set; } = 0.01f;

        /// <summary>Number of 1-HP copies an Illusionist spawns on first sight of a player. Default 10.</summary>
        public static int IllusionistCopyCount { get; set; } = 10;

        /// <summary>Radius (meters) around the Illusionist that copies are scattered into. Default 6.</summary>
        public static float IllusionistCopyRadius { get; set; } = 6.0f;

        /// <summary>Seconds between Illusionist swap attempts. Default 6.</summary>
        public static float IllusionistSwapCooldownSeconds { get; set; } = 6.0f;

        // ---------- Mutator Derpcoin Reward System ----------

        /// <summary>WCID of the Derpcoin item. Default 7000011.</summary>
        public static uint DerpcoinWcid { get; set; } = 7000011;

        /// <summary>Base chance (0-1) for a mutator mob to drop a derpcoin at MinTier (tier 2). Default 0.001 (0.1%).</summary>
        public static float DerpcoinBaseChance { get; set; } = 0.001f;

        /// <summary>Maximum derpcoin drop chance (0-1) at MaxTier (tier 8). Default 0.06 (6%).</summary>
        public static float DerpcoinMaxChance { get; set; } = 0.06f;

        /// <summary>Multiplier applied per additional mutator stacked beyond the first. Default 1.5 (50% increase per stack).</summary>
        public static float DerpcoinStackMultiplier { get; set; } = 1.5f;

        // ---------- Ironman Mode (irreversible solo / hardcore character) ----------

        /// <summary>Master switch for the Ironman mode opt-in command. Default true.</summary>
        public static bool IronmanEnabled { get; set; } = true;

        /// <summary>Welcome line shown to a player who has just become an Ironman.</summary>
        public static string IronmanWelcomeMessage { get; set; } = "You have committed to the Ironman path. There is no turning back.";

        /// <summary>Skill credit budget the Ironman roller will plan for when distributing trains/specs across the secondary pool. Default 50.</summary>
        public static int IronmanCreditsToPlanFor { get; set; } = 50;

        /// <summary>Number of hardcore lives an Ironman starts with. Final death (lives reaches 0) marks the character as deleted. Default 1.</summary>
        public static int IronmanHardcoreStartingLives { get; set; } = 1;

        /// <summary>Cooldown (seconds) between deaths that count toward life loss. Default 7 days.</summary>
        public static float IronmanHardcoreSecondsBetweenDeaths { get; set; } = 60f * 60f * 24f * 7f;

        // ---------- Vampiric Jewelry (rings / necklaces / bracelets) ----------

        /// <summary>Per-jewelry-piece chance (0-1) at lootgen to roll the Vampiric affix. Default 0.04.</summary>
        public static float VampiricJewelryDropChance { get; set; } = 0.04f;

        /// <summary>Minimum profile tier required for a piece to roll Vampiric. Default 4.</summary>
        public static int VampiricJewelryMinTier { get; set; } = 4;

        /// <summary>Minimum points granted by a single Vampiric piece. Default 1.</summary>
        public static int VampiricJewelryPointsMin { get; set; } = 1;

        /// <summary>Maximum points granted by a single Vampiric piece. Default 3.</summary>
        public static int VampiricJewelryPointsMax { get; set; } = 3;

        /// <summary>Seconds between passive Vampiric heartbeat heals. Default 5 (matches Player heartbeat cadence).</summary>
        public static float VampiricJewelryRegenIntervalSeconds { get; set; } = 5.0f;

        /// <summary>Diminishing-returns multiplier applied to the *summed* points based on equipped piece count.
        /// Index 0 = 0 pieces, 1 = 1 piece (full), 2 = 2 pieces (~85%), 3 = 3 pieces (~70%), 4 = 4 pieces (~55%), 5+ caps at 5.</summary>
        public static float[] VampiricJewelryDiminishingReturns { get; set; } = new[] { 0.0f, 1.0f, 0.85f, 0.70f, 0.55f, 0.45f };

        /// <summary>Per-piece chance (0-1) on a successful melee/missile hit to immediately heal the wielder for a small burst. Default 0.04.</summary>
        public static float VampiricJewelryOnHitProcChance { get; set; } = 0.04f;

        /// <summary>Multiplier applied to the piece's points value to compute the on-hit heal. Default 2.0 (1–3 points => 2–6 hp burst).</summary>
        public static float VampiricJewelryOnHitMultiplier { get; set; } = 2.0f;

        // ──────────────────────────────────────────────────────────────────────
        // Pre-Patch (PP) Variants — WCID 8489
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Chance (0–1) that a freshly-created WCID 8489 rolls as the legacy "pre-patch" visual. Default 0.10 (10%).</summary>
        public static float PrePatch8489Chance { get; set; } = 0.10f;

        /// <summary>SetupTableId applied to a [PP] WCID 8489 variant. Default 33555248.</summary>
        public static uint PrePatch8489SetupId { get; set; } = 33555248u;

        /// <summary>ClothingBase applied to a [PP] WCID 8489 variant. Default 268435629.</summary>
        public static uint PrePatch8489ClothingBase { get; set; } = 268435629u;

        /// <summary>PaletteBase applied to a [PP] WCID 8489 variant. Default 67108990.</summary>
        public static uint PrePatch8489PaletteBase { get; set; } = 67108990u;
    }
}

