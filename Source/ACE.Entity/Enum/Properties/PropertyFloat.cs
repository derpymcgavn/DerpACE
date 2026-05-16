using System.ComponentModel;

namespace ACE.Entity.Enum.Properties
{
    // No properties are sent to the client unless they featured an attribute.
    // SendOnLogin gets sent to players in the PlayerDescription event
    // AssessmentProperty gets sent in successful appraisal
    public enum PropertyFloat : ushort
    {
        Undef                          = 0,
        HeartbeatInterval              = 1,
        [Ephemeral]
        HeartbeatTimestamp             = 2,
        HealthRate                     = 3,
        StaminaRate                    = 4,
        [AssessmentProperty]
        ManaRate                       = 5,
        HealthUponResurrection         = 6,
        StaminaUponResurrection        = 7,
        ManaUponResurrection           = 8,
        StartTime                      = 9,
        StopTime                       = 10,
        ResetInterval                  = 11,
        Shade                          = 12,
        ArmorModVsSlash                = 13,
        ArmorModVsPierce               = 14,
        ArmorModVsBludgeon             = 15,
        ArmorModVsCold                 = 16,
        ArmorModVsFire                 = 17,
        ArmorModVsAcid                 = 18,
        ArmorModVsElectric             = 19,
        CombatSpeed                    = 20,
        WeaponLength                   = 21,
        DamageVariance                 = 22,
        CurrentPowerMod                = 23,
        AccuracyMod                    = 24,
        StrengthMod                    = 25,
        MaximumVelocity                = 26,
        RotationSpeed                  = 27,
        MotionTimestamp                = 28,
        [AssessmentProperty]
        WeaponDefense                  = 29,
        WimpyLevel                     = 30,
        VisualAwarenessRange           = 31,
        AuralAwarenessRange            = 32,
        PerceptionLevel                = 33,
        PowerupTime                    = 34,
        MaxChargeDistance              = 35,
        ChargeSpeed                    = 36,
        BuyPrice                       = 37,
        SellPrice                      = 38,
        DefaultScale                   = 39,
        LockpickMod                    = 40,
        RegenerationInterval           = 41,
        RegenerationTimestamp          = 42,
        GeneratorRadius                = 43,
        TimeToRot                      = 44,
        DeathTimestamp                 = 45,
        PkTimestamp                    = 46,
        VictimTimestamp                = 47,
        LoginTimestamp                 = 48,
        CreationTimestamp              = 49,
        MinimumTimeSincePk             = 50,
        DeprecatedHousekeepingPriority = 51,
        AbuseLoggingTimestamp          = 52,
        [Ephemeral]
        LastPortalTeleportTimestamp    = 53,
        UseRadius                      = 54,
        HomeRadius                     = 55,
        ReleasedTimestamp              = 56,
        MinHomeRadius                  = 57,
        Facing                         = 58,
        [Ephemeral]
        ResetTimestamp                 = 59,
        LogoffTimestamp                = 60,
        EconRecoveryInterval           = 61,
        WeaponOffense                  = 62,
        DamageMod                      = 63,
        ResistSlash                    = 64,
        ResistPierce                   = 65,
        ResistBludgeon                 = 66,
        ResistFire                     = 67,
        ResistCold                     = 68,
        ResistAcid                     = 69,
        ResistElectric                 = 70,
        ResistHealthBoost              = 71,
        ResistStaminaDrain             = 72,
        ResistStaminaBoost             = 73,
        ResistManaDrain                = 74,
        ResistManaBoost                = 75,
        Translucency                   = 76,
        PhysicsScriptIntensity         = 77,
        Friction                       = 78,
        Elasticity                     = 79,
        AiUseMagicDelay                = 80,
        ItemMinSpellcraftMod           = 81,
        ItemMaxSpellcraftMod           = 82,
        ItemRankProbability            = 83,
        Shade2                         = 84,
        Shade3                         = 85,
        Shade4                         = 86,
        [AssessmentProperty]
        ItemEfficiency                 = 87,
        ItemManaUpdateTimestamp        = 88,
        SpellGestureSpeedMod           = 89,
        SpellStanceSpeedMod            = 90,
        AllegianceAppraisalTimestamp   = 91,
        PowerLevel                     = 92,
        AccuracyLevel                  = 93,
        AttackAngle                    = 94,
        AttackTimestamp                = 95,
        CheckpointTimestamp            = 96,
        SoldTimestamp                  = 97,
        UseTimestamp                   = 98,
        [Ephemeral]
        UseLockTimestamp               = 99,
        [AssessmentProperty]
        HealkitMod                     = 100,
        FrozenTimestamp                = 101,
        HealthRateMod                  = 102,
        AllegianceSwearTimestamp       = 103,
        ObviousRadarRange              = 104,
        HotspotCycleTime               = 105,
        HotspotCycleTimeVariance       = 106,
        SpamTimestamp                  = 107,
        SpamRate                       = 108,
        BondWieldedTreasure            = 109,
        BulkMod                        = 110,
        SizeMod                        = 111,
        GagTimestamp                   = 112,
        GeneratorUpdateTimestamp       = 113,
        DeathSpamTimestamp             = 114,
        DeathSpamRate                  = 115,
        WildAttackProbability          = 116,
        FocusedProbability             = 117,
        CrashAndTurnProbability        = 118,
        CrashAndTurnRadius             = 119,
        CrashAndTurnBias               = 120,
        GeneratorInitialDelay          = 121,
        AiAcquireHealth                = 122,
        AiAcquireStamina               = 123,
        AiAcquireMana                  = 124,
        /// <summary>
        /// this had a default of "1" - leaving comment to investigate potential options for defaulting these things (125)
        /// </summary>
        [SendOnLogin]
        ResistHealthDrain              = 125,
        LifestoneProtectionTimestamp   = 126,
        AiCounteractEnchantment        = 127,
        AiDispelEnchantment            = 128,
        TradeTimestamp                 = 129,
        AiTargetedDetectionRadius      = 130,
        EmotePriority                  = 131,
        [Ephemeral]
        LastTeleportStartTimestamp     = 132,
        EventSpamTimestamp             = 133,
        EventSpamRate                  = 134,
        InventoryOffset                = 135,
        [AssessmentProperty]
        CriticalMultiplier             = 136,
        [AssessmentProperty]
        ManaStoneDestroyChance         = 137,
        SlayerDamageBonus              = 138,
        AllegianceInfoSpamTimestamp    = 139,
        AllegianceInfoSpamRate         = 140,
        NextSpellcastTimestamp         = 141,
        [Ephemeral]
        AppraisalRequestedTimestamp    = 142,
        AppraisalHeartbeatDueTimestamp = 143,
        [AssessmentProperty]
        ManaConversionMod              = 144,
        LastPkAttackTimestamp          = 145,
        FellowshipUpdateTimestamp      = 146,
        [AssessmentProperty]
        CriticalFrequency              = 147,
        LimboStartTimestamp            = 148,
        [AssessmentProperty]
        WeaponMissileDefense           = 149,
        [AssessmentProperty]
        WeaponMagicDefense             = 150,
        IgnoreShield                   = 151,
        [AssessmentProperty]
        ElementalDamageMod             = 152,
        StartMissileAttackTimestamp    = 153,
        LastRareUsedTimestamp          = 154,
        [AssessmentProperty]
        IgnoreArmor                    = 155,
        ProcSpellRate                  = 156,
        [AssessmentProperty]
        ResistanceModifier             = 157,
        AllegianceGagTimestamp         = 158,
        [AssessmentProperty]
        AbsorbMagicDamage              = 159,
        CachedMaxAbsorbMagicDamage     = 160,
        GagDuration                    = 161,
        AllegianceGagDuration          = 162,
        [SendOnLogin]
        GlobalXpMod                    = 163,
        HealingModifier                = 164,
        ArmorModVsNether               = 165,
        ResistNether                   = 166,
        [AssessmentProperty]
        CooldownDuration               = 167,
        [SendOnLogin]
        WeaponAuraOffense              = 168,
        [SendOnLogin]
        WeaponAuraDefense              = 169,
        [SendOnLogin]
        WeaponAuraElemental            = 170,
        [SendOnLogin]
        WeaponAuraManaConv             = 171,

        /* Custom Properties */
        PCAPRecordedWorkmanship        = 8004,
        PCAPRecordedVelocityX          = 8010,
        PCAPRecordedVelocityY          = 8011,
        PCAPRecordedVelocityZ          = 8012,
        PCAPRecordedAccelerationX      = 8013,
        PCAPRecordedAccelerationY      = 8014,
        PCAPRecordedAccelerationZ      = 8015,
        PCAPRecordeOmegaX              = 8016,
        PCAPRecordeOmegaY              = 8017,
        PCAPRecordeOmegaZ              = 8018,

        /* DerpACE Fencer's Blade — per-weapon proc values */
        FencerArmorPiercePct           = 9001,  // fraction of mitigated damage restored on pierce proc
        FencerArmorPierceProc          = 9002,  // per-hit proc chance for armor pierce
        FencerDeflectChance            = 9003,  // per-hit chance to deflect incoming damage back

        /* DerpACE Ravager's Axe — per-weapon proc values */
        RavagerBleedProc               = 9004,  // per-hit chance to apply a bleed DoT
        RavagerBleedPct                = 9005,  // total bleed damage as a fraction of the triggering hit

        /* DerpACE Warden's Maul — per-weapon proc values */
        WardenConcussProc              = 9006,  // per-hit chance to apply a defense-skill debuff
        WardenConcussPenalty           = 9007,  // flat defense skill points removed during the debuff
        WardenConcussDuration          = 9008,  // duration of the debuff in seconds

        /* DerpACE Resolute Blade — per-weapon proc values */
        ResoluteHealProc               = 9009,  // per-critical-hit chance to heal the wielder
        ResoluteHealPct                = 9010,  // fraction of crit damage restored as health on heal proc
        ResoluteKillBurstPct           = 9011,  // fraction of wielder MaxHealth/MaxStamina restored on a killing blow

        /* DerpACE Polebreaker Staff — per-weapon escalation values */
        PolebreakerStackBonus          = 9012,  // bonus damage fraction per consecutive hit stack
        PolebreakerMaxStacks           = 9013,  // maximum stack count

        /* DerpACE Stalker's Bow — per-weapon opening-shot values */
        StalkerFirstStrikeProc         = 9014,  // chance to fire when this is the first hit on the target
        StalkerFirstStrikeBonus        = 9015,  // bonus damage fraction added on a successful first strike

        /* DerpACE Breacher's Crossbow — always-on armor pierce */
        BreacherPiercePct              = 9016,  // DEPRECATED: fraction of DamageMitigated added back as bonus damage on every hit
        BreacherArmorIgnoreChance      = 9017,  // chance per shot to ignore all armor on that shot (0.0-1.0)

        /* DerpACE Reaper's Atlatl — kill-fed sustain */
        ReaperKillProc                 = 9021,  // chance the kill heal fires on a killing blow
        ReaperKillHealPct              = 9018,  // fraction of wielder MaxHealth restored when proc fires

        /* DerpACE Mob Modifiers — vampiric */
        VampiricLifestealPct           = 9019,  // fraction of damage dealt healed back to vampiric mob

        /* DerpACE armor vital proc */
        ArmorVitalProcChance           = 9020,  // chance on hit to restore ArmorVitalProcAmount of the matching vital

        /* DerpACE Hierophant Caster — support healer staff */
        HierophantHealBoostPct         = 9022,  // multiplier added to beneficial-Health spells cast by the wielder (0.01-0.10)
        HierophantHotPct               = 9023,  // fraction of fellowship pool granted as a regen HoT on heal-cast proc (0.01-0.25)
        HierophantFellowEchoPct        = 9024,  // bonus fellowship heal applied on each direct heal cast (0.0-1.0)

        /* DerpACE Unarmed — damage variance for gauntlets/boots when no weapon equipped */
        UnarmedDamageVariance          = 9025,  // variance for unarmed damage rolls (0.0-1.0)
    }
}
