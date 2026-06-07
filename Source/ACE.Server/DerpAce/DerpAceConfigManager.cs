using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using log4net;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.DerpAce
{
    /// <summary>
    /// Loads, saves, and hot-reloads DerpAce.json.
    /// Call <see cref="Initialize"/> once at server startup.
    /// Call <see cref="Reload"/> to pick up file changes at runtime (@derpconfig reload).
    /// </summary>
    public static class DerpAceConfigManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public const string DefaultFileName = "DerpAce.json";

        public static DerpAceConfiguration Config { get; private set; } = new DerpAceConfiguration();

        private static string _resolvedPath;

        /// <summary>Called once at server startup.</summary>
        public static void Initialize(string path = DefaultFileName)
        {
            _resolvedPath = ResolvePath(path);

            if (!File.Exists(_resolvedPath))
            {
                log.Info($"[DerpAce] Config not found at '{_resolvedPath}' — writing defaults.");
                Save();
            }
            else
            {
                LoadFromDisk();
            }

            Apply();
            log.Info($"[DerpAce] Config loaded from '{_resolvedPath}'.");
        }

        /// <summary>Reloads the file from disk and re-applies all values.</summary>
        public static string Reload()
        {
            if (_resolvedPath == null)
                return "DerpAceConfigManager has not been initialized.";

            if (!File.Exists(_resolvedPath))
                return $"Config file not found: {_resolvedPath}";

            try
            {
                LoadFromDisk();
                Apply();
                return $"DerpAce config reloaded from '{_resolvedPath}'.";
            }
            catch (Exception ex)
            {
                log.Error($"[DerpAce] Reload failed: {ex}");
                return $"Reload failed: {ex.Message}";
            }
        }

        /// <summary>Writes the current in-memory config back to disk.</summary>
        public static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Config, _jsonOptions);
                File.WriteAllText(_resolvedPath, json);
            }
            catch (Exception ex)
            {
                log.Error($"[DerpAce] Save failed: {ex}");
            }
        }

        // ── private helpers ──────────────────────────────────────────────────

        private static void LoadFromDisk()
        {
            var json = File.ReadAllText(_resolvedPath);
            Config = JsonSerializer.Deserialize<DerpAceConfiguration>(json, _jsonOptions)
                     ?? new DerpAceConfiguration();
        }

        /// <summary>
        /// Pushes every config value into the static fields of the systems that
        /// use them.  Called after every successful load/reload.
        /// </summary>
        private static void Apply()
        {
            var c = Config;

            // ── Section Master Toggles ────────────────────────────────────────
            DerpACEConfig.EnableTeleport           = c.EnableTeleport;
            DerpACEConfig.EnableMysteriousStranger = c.EnableMysteriousStranger;
            DerpACEConfig.EnableMobModifiers       = c.EnableMobModifiers;
            DerpACEConfig.EnableDerpcoin           = c.EnableDerpcoin;
            DerpACEConfig.EnableCustomWeapons      = c.EnableCustomWeapons;
            DerpACEConfig.EnableArmorEnchants      = c.EnableArmorEnchants;
            DerpACEConfig.EnableVampiricJewelry    = c.EnableVampiricJewelry;
            DerpACEConfig.EnablePrePatchVariants   = c.EnablePrePatchVariants;

            // ── Per-Mutator Toggles ───────────────────────────────────────────
            DerpACEConfig.NocturnalMobEnabled    = c.NocturnalMobEnabled;
            DerpACEConfig.ExplodingMobEnabled    = c.ExplodingMobEnabled;
            DerpACEConfig.VampiricMobEnabled     = c.VampiricMobEnabled;
            DerpACEConfig.ThiefMobEnabled        = c.ThiefMobEnabled;
            DerpACEConfig.ScoutMobEnabled        = c.ScoutMobEnabled;
            DerpACEConfig.SimulacrumMobEnabled   = c.SimulacrumMobEnabled;
            DerpACEConfig.HealerMobEnabled       = c.HealerMobEnabled;
            DerpACEConfig.TankMobEnabled         = c.TankMobEnabled;
            DerpACEConfig.ReaperMobEnabled       = c.ReaperMobEnabled;
            DerpACEConfig.NecromancerMobEnabled  = c.NecromancerMobEnabled;
            DerpACEConfig.MergerMobEnabled       = c.MergerMobEnabled;
            DerpACEConfig.HordeMobEnabled        = c.HordeMobEnabled;
            DerpACEConfig.WarderMobEnabled       = c.WarderMobEnabled;
            DerpACEConfig.IllusionistMobEnabled  = c.IllusionistMobEnabled;

            // ── Per-Custom-Weapon Toggles ─────────────────────────────────────
            DerpACEConfig.DefenderShieldEnabled   = c.DefenderShieldEnabled;
            DerpACEConfig.ArchmagiEnabled         = c.ArchmagiEnabled;
            DerpACEConfig.HierophantEnabled       = c.HierophantEnabled;
            DerpACEConfig.ThievesDaggerEnabled    = c.ThievesDaggerEnabled;
            DerpACEConfig.SentinelSpearEnabled    = c.SentinelSpearEnabled;
            DerpACEConfig.UnarmedElemEnabled      = c.UnarmedElemEnabled;
            DerpACEConfig.FencerBladeEnabled      = c.FencerBladeEnabled;
            DerpACEConfig.RavagerAxeEnabled       = c.RavagerAxeEnabled;
            DerpACEConfig.WardenMaulEnabled       = c.WardenMaulEnabled;
            DerpACEConfig.ResoluteBladeEnabled    = c.ResoluteBladeEnabled;
            DerpACEConfig.PolebreakerStaffEnabled = c.PolebreakerStaffEnabled;
            DerpACEConfig.StalkerBowEnabled       = c.StalkerBowEnabled;
            DerpACEConfig.BreacherCrossbowEnabled = c.BreacherCrossbowEnabled;
            DerpACEConfig.ReaperAtlatlEnabled     = c.ReaperAtlatlEnabled;
            DerpACEConfig.RicochetAtlatlEnabled   = c.RicochetAtlatlEnabled;
            DerpACEConfig.WeaponElemBlastEnabled  = c.WeaponElemBlastEnabled;

            // ── Teleport ─────────────────────────────────────────────────────
            TpConfig.CostPerMeter = c.TpCostPerMeter;
            TpConfig.MinCost      = c.TpMinCost;
            TpConfig.RequestTtl   = c.TpRequestTtlSeconds;

            // ── Mysterious Stranger ───────────────────────────────────────────
            MysteriousStranger.MinVitaePercent            = c.StrangerMinVitaePercent;
            MysteriousStranger.MaxVitaePercent            = c.StrangerMaxVitaePercent;
            MysteriousStranger.MinChestOpens              = c.StrangerMinChestOpens;
            MysteriousStranger.MaxChestOpens              = c.StrangerMaxChestOpens;
            MysteriousStranger.ChestDespawnSeconds        = c.StrangerChestDespawnSeconds;
            MysteriousStranger.ChestDespawnWarningSeconds = c.StrangerChestDespawnWarningSeconds;
            MysteriousStranger.ChestDespawnGraceSeconds   = c.StrangerChestDespawnGraceSeconds;
            MysteriousStranger.ChestArcDistance           = c.StrangerChestArcDistance;
            MysteriousStranger.ChestArcSweepDegrees       = c.StrangerChestArcSweepDegrees;
            MysteriousStranger.DramaticSpawnDelay         = c.StrangerDramaticSpawnDelay;
            MysteriousStranger.ObfuscatedBurdenMin        = c.StrangerObfuscatedBurdenMin;
            MysteriousStranger.ObfuscatedBurdenMax        = c.StrangerObfuscatedBurdenMax;
            MysteriousStranger.DealCooldownSeconds        = c.StrangerDealCooldownSeconds;
            MysteriousStranger.JunkPrankChance            = c.StrangerJunkPrankChance;

            // ── Mob Modifiers ─────────────────────────────────────────────────
            DerpACEConfig.MobModifierEnabled          = c.MobModifierEnabled;
            DerpACEConfig.MobModifierMinTier          = c.MobModifierMinTier;
            DerpACEConfig.NocturnalMobChance          = c.NocturnalMobChance;
            DerpACEConfig.ExplodingMobChance          = c.ExplodingMobChance;
            DerpACEConfig.ExplodingMobRadius          = c.ExplodingMobRadius;
            DerpACEConfig.ExplodingMobDamageScale     = c.ExplodingMobDamageScale;
            DerpACEConfig.VampiricMobChance           = c.VampiricMobChance;
            DerpACEConfig.VampiricLifestealMin        = c.VampiricLifestealMin;
            DerpACEConfig.VampiricLifestealMax        = c.VampiricLifestealMax;
            DerpACEConfig.ThiefMobChance              = c.ThiefMobChance;
            DerpACEConfig.ThiefStealProc              = c.ThiefStealProc;
            DerpACEConfig.ThiefChestDropChance        = c.ThiefChestDropChance;
            DerpACEConfig.ThiefChestWcid              = c.ThiefChestWcid;
            DerpACEConfig.ThiefChestDespawnSeconds    = c.ThiefChestDespawnSeconds;
            DerpACEConfig.ScoutMobChance              = c.ScoutMobChance;
            DerpACEConfig.SimulacrumMobChance         = c.SimulacrumMobChance;
            DerpACEConfig.HealerMobChance             = c.HealerMobChance;
            DerpACEConfig.HealerMobRange              = c.HealerMobRange;
            DerpACEConfig.HealerMobHealThreshold      = c.HealerMobHealThreshold;
            DerpACEConfig.HealerMobCooldownSeconds    = c.HealerMobCooldownSeconds;
            DerpACEConfig.TankMobChance               = c.TankMobChance;
            DerpACEConfig.TankMobHealthMultiplier     = c.TankMobHealthMultiplier;
            DerpACEConfig.TankMobPhysicalReduction    = c.TankMobPhysicalReduction;
            DerpACEConfig.TankMobHealBonus            = c.TankMobHealBonus;
            DerpACEConfig.TankMobSkillBonus           = c.TankMobSkillBonus;
            DerpACEConfig.ReaperMobChance             = c.ReaperMobChance;
            DerpACEConfig.ReaperDamageBonus           = c.ReaperDamageBonus;
            DerpACEConfig.ReaperLifedrainPct          = c.ReaperLifedrainPct;
            DerpACEConfig.NecromancerMobChance        = c.NecromancerMobChance;
            DerpACEConfig.NecromancerDotChance        = c.NecromancerDotChance;
            DerpACEConfig.NecromancerDotTotal         = c.NecromancerDotTotal;
            DerpACEConfig.MergerMobChance             = c.MergerMobChance;
            DerpACEConfig.MergerMaxMerges             = c.MergerMaxMerges;
            DerpACEConfig.MergerSearchRange           = c.MergerSearchRange;
            DerpACEConfig.MergerCooldownSeconds       = c.MergerCooldownSeconds;
            DerpACEConfig.HordeMobChance              = c.HordeMobChance;
            DerpACEConfig.HordeMinSize                = c.HordeMinSize;
            DerpACEConfig.HordeMaxSize                = c.HordeMaxSize;
            DerpACEConfig.WarderMobChance             = c.WarderMobChance;
            DerpACEConfig.WarderRange                 = c.WarderRange;
            DerpACEConfig.IllusionistMobChance        = c.IllusionistMobChance;
            DerpACEConfig.IllusionistCopyCount        = c.IllusionistCopyCount;
            DerpACEConfig.IllusionistCopyRadius       = c.IllusionistCopyRadius;
            DerpACEConfig.IllusionistSwapCooldownSeconds = c.IllusionistSwapCooldownSeconds;

            // ── Derpcoin ──────────────────────────────────────────────────────
            DerpACEConfig.DerpcoinWcid             = c.DerpcoinWcid;
            DerpACEConfig.DerpcoinBaseChance       = c.DerpcoinBaseChance;
            DerpACEConfig.DerpcoinMaxChance        = c.DerpcoinMaxChance;
            DerpACEConfig.DerpcoinStackMultiplier  = c.DerpcoinStackMultiplier;

            // ── Loot Modifier Balance ─────────────────────────────────────────
            DerpACEConfig.LootModifierGlobalDropMultiplier  = c.LootModifierGlobalDropMultiplier;
            DerpACEConfig.LootModifierExclusivePerItem      = c.LootModifierExclusivePerItem;
            DerpACEConfig.LootModifierInterchangeable       = c.LootModifierInterchangeable;
            DerpACEConfig.LootModifierInterchangeableMinTier = c.LootModifierInterchangeableMinTier;

            // ── Armor Enchantments ────────────────────────────────────────────
            DerpACEConfig.ArmorBaneChanceNormal            = c.ArmorBaneChanceNormal;
            DerpACEConfig.ArmorBaneChanceCovenant          = c.ArmorBaneChanceCovenant;
            DerpACEConfig.ArmorEnchantmentChanceBonus      = c.ArmorEnchantmentChanceBonus;
            DerpACEConfig.ArmorMaxEnchantments             = c.ArmorMaxEnchantments;
            DerpACEConfig.ArmorExtraEnchantmentChanceMult  = c.ArmorExtraEnchantmentChanceMult;

            // ── Custom Weapons ────────────────────────────────────────────────
            DerpACEConfig.DefenderShieldDropChance    = c.DefenderShieldDropChance;
            DerpACEConfig.DefenderShieldMinTier       = c.DefenderShieldMinTier;
            DerpACEConfig.DefenderAggroBonus          = c.DefenderAggroBonus;

            DerpACEConfig.ArchmagiDropChance          = c.ArchmagiDropChance;
            DerpACEConfig.ArchmagiMinTier             = c.ArchmagiMinTier;
            DerpACEConfig.ArchmagiProcChance          = c.ArchmagiProcChance;
            DerpACEConfig.ArchmagiAggroPenalty        = c.ArchmagiAggroPenalty;

            DerpACEConfig.HierophantDropChance        = c.HierophantDropChance;
            DerpACEConfig.HierophantMinTier           = c.HierophantMinTier;
            DerpACEConfig.HierophantHealBoostMin      = c.HierophantHealBoostMin;
            DerpACEConfig.HierophantHealBoostMax      = c.HierophantHealBoostMax;
            DerpACEConfig.HierophantHotProcChance     = c.HierophantHotProcChance;
            DerpACEConfig.HierophantHotPctMin         = c.HierophantHotPctMin;
            DerpACEConfig.HierophantHotPctMax         = c.HierophantHotPctMax;
            DerpACEConfig.HierophantHotDurationSeconds = c.HierophantHotDurationSeconds;
            DerpACEConfig.HierophantHotTickInterval   = c.HierophantHotTickInterval;
            DerpACEConfig.HierophantFellowEchoPct     = c.HierophantFellowEchoPct;
            DerpACEConfig.HierophantFellowEchoRange   = c.HierophantFellowEchoRange;
            DerpACEConfig.HierophantAggroBonus        = c.HierophantAggroBonus;

            DerpACEConfig.SneakAttackBonusPct         = c.SneakAttackBonusPct;
            DerpACEConfig.ThievesDaggerDropChance     = c.ThievesDaggerDropChance;
            DerpACEConfig.ThievesDaggerMinTier        = c.ThievesDaggerMinTier;
            DerpACEConfig.ThievesDaggerProcChance     = c.ThievesDaggerProcChance;
            DerpACEConfig.ThievesDaggerProcBonus      = c.ThievesDaggerProcBonus;
            DerpACEConfig.ThievesDaggerAggroPenalty   = c.ThievesDaggerAggroPenalty;

            DerpACEConfig.SentinelSpearDropChance     = c.SentinelSpearDropChance;
            DerpACEConfig.SentinelSpearMinTier        = c.SentinelSpearMinTier;
            DerpACEConfig.SentinelSpearProcChance     = c.SentinelSpearProcChance;
            DerpACEConfig.SentinelSpearDrainPct       = c.SentinelSpearDrainPct;
            DerpACEConfig.SentinelSpearReturnMult     = c.SentinelSpearReturnMult;
            DerpACEConfig.SentinelSpearAggroBonus     = c.SentinelSpearAggroBonus;

            DerpACEConfig.UnarmedElemDropChance       = c.UnarmedElemDropChance;
            DerpACEConfig.UnarmedElemProcMin          = c.UnarmedElemProcMin;
            DerpACEConfig.UnarmedElemProcMax          = c.UnarmedElemProcMax;

            DerpACEConfig.FencerBladeDropChance       = c.FencerBladeDropChance;
            DerpACEConfig.FencerBladeMinTier          = c.FencerBladeMinTier;
            DerpACEConfig.FencerPierceMin             = c.FencerPierceMin;
            DerpACEConfig.FencerPierceMax             = c.FencerPierceMax;
            DerpACEConfig.FencerPierceProcMin         = c.FencerPierceProcMin;
            DerpACEConfig.FencerPierceProcMax         = c.FencerPierceProcMax;
            DerpACEConfig.FencerDeflectMin            = c.FencerDeflectMin;
            DerpACEConfig.FencerDeflectMax            = c.FencerDeflectMax;

            DerpACEConfig.RavagerAxeDropChance        = c.RavagerAxeDropChance;
            DerpACEConfig.RavagerAxeMinTier           = c.RavagerAxeMinTier;
            DerpACEConfig.RavagerProcMin              = c.RavagerProcMin;
            DerpACEConfig.RavagerProcMax              = c.RavagerProcMax;
            DerpACEConfig.RavagerBleedMin             = c.RavagerBleedMin;
            DerpACEConfig.RavagerBleedMax             = c.RavagerBleedMax;
            DerpACEConfig.RavagerTwoHandMult          = c.RavagerTwoHandMult;
            DerpACEConfig.RavagerBleedTicks           = c.RavagerBleedTicks;
            DerpACEConfig.RavagerBleedInterval        = c.RavagerBleedInterval;
            DerpACEConfig.RavagerHammerCleaveChance   = c.RavagerHammerCleaveChance;
            DerpACEConfig.RavagerHammerCleaveMaxTargets = c.RavagerHammerCleaveMaxTargets;
            DerpACEConfig.RavagerHammerCleaveDamageScale = c.RavagerHammerCleaveDamageScale;
            DerpACEConfig.RavagerHammerCleaveRadius   = c.RavagerHammerCleaveRadius;
            DerpACEConfig.RavagerAxeAggroBonus        = c.RavagerAxeAggroBonus;

            DerpACEConfig.WardenMaulDropChance        = c.WardenMaulDropChance;
            DerpACEConfig.WardenMaulMinTier           = c.WardenMaulMinTier;
            DerpACEConfig.WardenProcMin               = c.WardenProcMin;
            DerpACEConfig.WardenProcMax               = c.WardenProcMax;
            DerpACEConfig.WardenPenaltyMin            = c.WardenPenaltyMin;
            DerpACEConfig.WardenPenaltyMax            = c.WardenPenaltyMax;
            DerpACEConfig.WardenDurationMin           = c.WardenDurationMin;
            DerpACEConfig.WardenDurationMax           = c.WardenDurationMax;
            DerpACEConfig.WardenTwoHandMult           = c.WardenTwoHandMult;
            DerpACEConfig.WardenMaulAggroBonus        = c.WardenMaulAggroBonus;

            DerpACEConfig.ResoluteBladeDropChance     = c.ResoluteBladeDropChance;
            DerpACEConfig.ResoluteBladeMinTier        = c.ResoluteBladeMinTier;
            DerpACEConfig.ResoluteProcMin             = c.ResoluteProcMin;
            DerpACEConfig.ResoluteProcMax             = c.ResoluteProcMax;
            DerpACEConfig.ResoluteHealMin             = c.ResoluteHealMin;
            DerpACEConfig.ResoluteHealMax             = c.ResoluteHealMax;
            DerpACEConfig.ResoluteKillBurstPct        = c.ResoluteKillBurstPct;
            DerpACEConfig.ResoluteTwoHandMult         = c.ResoluteTwoHandMult;

            DerpACEConfig.PolebreakerDropChance       = c.PolebreakerDropChance;
            DerpACEConfig.PolebreakerMinTier          = c.PolebreakerMinTier;
            DerpACEConfig.PolebreakerStackMin         = c.PolebreakerStackMin;
            DerpACEConfig.PolebreakerStackMax         = c.PolebreakerStackMax;
            DerpACEConfig.PolebreakerMaxStackMin      = c.PolebreakerMaxStackMin;
            DerpACEConfig.PolebreakerMaxStackMax      = c.PolebreakerMaxStackMax;
            DerpACEConfig.PolebreakerStaffAggroBonus  = c.PolebreakerStaffAggroBonus;

            DerpACEConfig.StalkerBowDropChance        = c.StalkerBowDropChance;
            DerpACEConfig.StalkerBowMinTier           = c.StalkerBowMinTier;
            DerpACEConfig.StalkerProcMin              = c.StalkerProcMin;
            DerpACEConfig.StalkerProcMax              = c.StalkerProcMax;
            DerpACEConfig.StalkerBonusMin             = c.StalkerBonusMin;
            DerpACEConfig.StalkerBonusMax             = c.StalkerBonusMax;
            DerpACEConfig.StalkerBowAggroPenalty      = c.StalkerBowAggroPenalty;

            DerpACEConfig.BreacherCrossbowDropChance  = c.BreacherCrossbowDropChance;
            DerpACEConfig.BreacherCrossbowMinTier     = c.BreacherCrossbowMinTier;
            DerpACEConfig.BreacherArmorIgnoreMin      = c.BreacherArmorIgnoreMin;
            DerpACEConfig.BreacherArmorIgnoreMax      = c.BreacherArmorIgnoreMax;

            DerpACEConfig.ReaperAtlatlDropChance      = c.ReaperAtlatlDropChance;
            DerpACEConfig.ReaperAtlatlMinTier         = c.ReaperAtlatlMinTier;
            DerpACEConfig.ReaperProcMin               = c.ReaperProcMin;
            DerpACEConfig.ReaperProcMax               = c.ReaperProcMax;
            DerpACEConfig.ReaperHealMin               = c.ReaperHealMin;
            DerpACEConfig.ReaperHealMax               = c.ReaperHealMax;

            DerpACEConfig.RicochetAtlatlDropChance    = c.RicochetAtlatlDropChance;
            DerpACEConfig.RicochetAtlatlMinTier       = c.RicochetAtlatlMinTier;
            DerpACEConfig.RicochetProcMin             = c.RicochetProcMin;
            DerpACEConfig.RicochetProcMax             = c.RicochetProcMax;
            DerpACEConfig.RicochetDamageScale         = c.RicochetDamageScale;
            DerpACEConfig.RicochetRadius              = c.RicochetRadius;

            DerpACEConfig.WeaponBlastProcMinTier      = c.WeaponBlastProcMinTier;
            DerpACEConfig.WeaponBlastProcChanceMin    = c.WeaponBlastProcChanceMin;
            DerpACEConfig.WeaponBlastProcChanceMax    = c.WeaponBlastProcChanceMax;
            DerpACEConfig.WeaponBlastProcRateMin      = c.WeaponBlastProcRateMin;
            DerpACEConfig.WeaponBlastProcRateMax      = c.WeaponBlastProcRateMax;

            // ── Vampiric Jewelry ──────────────────────────────────────────────
            DerpACEConfig.VampiricJewelryDropChance         = c.VampiricJewelryDropChance;
            DerpACEConfig.VampiricJewelryMinTier            = c.VampiricJewelryMinTier;
            DerpACEConfig.VampiricJewelryPointsMin          = c.VampiricJewelryPointsMin;
            DerpACEConfig.VampiricJewelryPointsMax          = c.VampiricJewelryPointsMax;
            DerpACEConfig.VampiricJewelryRegenIntervalSeconds = c.VampiricJewelryRegenIntervalSeconds;
            DerpACEConfig.VampiricJewelryOnHitProcChance    = c.VampiricJewelryOnHitProcChance;
            DerpACEConfig.VampiricJewelryOnHitMultiplier    = c.VampiricJewelryOnHitMultiplier;

            // ── Pre-Patch Variants ────────────────────────────────────────────
            DerpACEConfig.PrePatch8489Chance      = c.PrePatch8489Chance;
            DerpACEConfig.PrePatch8489SetupId     = c.PrePatch8489SetupId;
            DerpACEConfig.PrePatch8489ClothingBase = c.PrePatch8489ClothingBase;
            DerpACEConfig.PrePatch8489PaletteBase = c.PrePatch8489PaletteBase;

            // ── Vendor Random Loot ────────────────────────────────────────────
            DerpACEConfig.VendorRandomLootEnabled    = c.VendorRandomLootEnabled;
            DerpACEConfig.VendorRandomLootMinItems  = c.VendorRandomLootMinItems;
            DerpACEConfig.VendorRandomLootMaxItems  = c.VendorRandomLootMaxItems;
            DerpACEConfig.VendorRestockMinMinutes   = c.VendorRestockMinMinutes;
            DerpACEConfig.VendorRestockMaxMinutes   = c.VendorRestockMaxMinutes;

            // ── Ironman Mode ──────────────────────────────────────────────────
            DerpACEConfig.IronmanEnabled                      = c.IronmanEnabled;
            DerpACEConfig.IronmanWelcomeMessage               = c.IronmanWelcomeMessage;
            DerpACEConfig.IronmanCreditsToPlanFor             = c.IronmanCreditsToPlanFor;
            DerpACEConfig.IronmanHardcoreStartingLives        = c.IronmanHardcoreStartingLives;
            DerpACEConfig.IronmanHardcoreSecondsBetweenDeaths = c.IronmanHardcoreSecondsBetweenDeaths;

            // ── Bank ──────────────────────────────────────────────────────────
            DerpAce.Bank.BankConfig.EnableBank          = c.EnableBank;
            DerpAce.Bank.BankConfig.DirectDeposit       = c.BankDirectDeposit;
            DerpAce.Bank.BankConfig.VendorsUseBank      = c.BankVendorsUseBank;
            DerpAce.Bank.BankConfig.MaxCoinsDropped     = c.BankMaxCoinsDropped;
            DerpAce.Bank.BankConfig.ExcessSetToMax      = c.BankExcessSetToMax;
            DerpAce.Bank.BankConfig.CashProperty        = c.BankCashProperty;
        }

        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            // Try working directory first, then executable directory
            var cwd = Path.Combine(Environment.CurrentDirectory, path);
            if (File.Exists(cwd)) return cwd;

            var exe = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return exe != null ? Path.Combine(exe, path) : cwd;
        }
    }
}
