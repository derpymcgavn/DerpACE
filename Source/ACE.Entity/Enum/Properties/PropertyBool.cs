using System.ComponentModel;

namespace ACE.Entity.Enum.Properties
{
    // No properties are sent to the client unless they featured an attribute.
    // SendOnLogin gets sent to players in the PlayerDescription event
    // AssessmentProperty gets sent in successful appraisal
    public enum PropertyBool : ushort
    {
        Undef                            = 0,
        [Ephemeral]
        Stuck                            = 1,
        [AssessmentProperty][Ephemeral]
        Open                             = 2,
        [AssessmentProperty]
        Locked                           = 3,
        RotProof                         = 4,
        AllegianceUpdateRequest          = 5,
        AiUsesMana                       = 6,
        AiUseHumanMagicAnimations        = 7,
        AllowGive                        = 8,
        CurrentlyAttacking               = 9,
        AttackerAi                       = 10,
        IgnoreCollisions                 = 11,
        ReportCollisions                 = 12,
        Ethereal                         = 13,
        GravityStatus                    = 14,
        LightsStatus                     = 15,
        ScriptedCollision                = 16,
        Inelastic                        = 17,
        [Ephemeral]
        Visibility                       = 18,
        Attackable                       = 19,
        SafeSpellComponents              = 20,
        [SendOnLogin]
        AdvocateState                    = 21,
        Inscribable                      = 22,
        DestroyOnSell                    = 23,
        UiHidden                         = 24,
        IgnoreHouseBarriers              = 25,
        HiddenAdmin                      = 26,
        PkWounder                        = 27,
        PkKiller                         = 28,
        NoCorpse                         = 29,
        UnderLifestoneProtection         = 30,
        ItemManaUpdatePending            = 31,
        [Ephemeral]
        GeneratorStatus                  = 32,
        [Ephemeral]
        ResetMessagePending              = 33,
        DefaultOpen                      = 34,
        DefaultLocked                    = 35,
        DefaultOn                        = 36,
        OpenForBusiness                  = 37,
        IsFrozen                         = 38,
        DealMagicalItems                 = 39,
        LogoffImDead                     = 40,
        ReportCollisionsAsEnvironment    = 41,
        AllowEdgeSlide                   = 42,
        AdvocateQuest                    = 43,
        [SendOnLogin][Ephemeral]
        IsAdmin                          = 44,
        [SendOnLogin][Ephemeral]
        IsArch                           = 45,
        [SendOnLogin][Ephemeral]
        IsSentinel                       = 46,
        [SendOnLogin]
        IsAdvocate                       = 47,
        CurrentlyPoweringUp              = 48,
        [Ephemeral]
        GeneratorEnteredWorld            = 49,
        NeverFailCasting                 = 50,
        VendorService                    = 51,
        AiImmobile                       = 52,
        DamagedByCollisions              = 53,
        IsDynamic                        = 54,
        IsHot                            = 55,
        IsAffecting                      = 56,
        AffectsAis                       = 57,
        SpellQueueActive                 = 58,
        [Ephemeral]
        GeneratorDisabled                = 59,
        IsAcceptingTells                 = 60,
        LoggingChannel                   = 61,
        OpensAnyLock                     = 62,
        [AssessmentProperty]
        UnlimitedUse                     = 63,
        GeneratedTreasureItem            = 64,
        IgnoreMagicResist                = 65,
        IgnoreMagicArmor                 = 66,
        AiAllowTrade                     = 67,
        [SendOnLogin]
        SpellComponentsRequired          = 68,
        [AssessmentProperty]
        IsSellable                       = 69,
        IgnoreShieldsBySkill             = 70,
        NoDraw                           = 71,
        ActivationUntargeted             = 72,
        HouseHasGottenPriorityBootPos    = 73,
        [Ephemeral]
        GeneratorAutomaticDestruction    = 74,
        HouseHooksVisible                = 75,
        HouseRequiresMonarch             = 76,
        HouseHooksEnabled                = 77,
        HouseNotifiedHudOfHookCount      = 78,
        AiAcceptEverything               = 79,
        IgnorePortalRestrictions         = 80,
        RequiresBackpackSlot             = 81,
        DontTurnOrMoveWhenGiving         = 82,
        NpcLooksLikeObject               = 83,
        IgnoreCloIcons                   = 84,
        [AssessmentProperty]
        AppraisalHasAllowedWielder       = 85,
        ChestRegenOnClose                = 86,
        LogoffInMinigame                 = 87,
        PortalShowDestination            = 88,
        PortalIgnoresPkAttackTimer       = 89,
        NpcInteractsSilently             = 90,
        [AssessmentProperty]
        Retained                         = 91,
        IgnoreAuthor                     = 92,
        Limbo                            = 93,
        [AssessmentProperty]
        AppraisalHasAllowedActivator     = 94,
        ExistedBeforeAllegianceXpChanges = 95,
        IsDeaf                           = 96,
        [SendOnLogin][Ephemeral]
        IsPsr                            = 97,
        Invincible                       = 98,
        [AssessmentProperty]
        Ivoryable                        = 99,
        [AssessmentProperty]
        Dyable                           = 100,
        CanGenerateRare                  = 101,
        CorpseGeneratedRare              = 102,
        NonProjectileMagicImmune         = 103,
        [SendOnLogin]
        ActdReceivedItems                = 104,
        Unknown105                       = 105,
        [Ephemeral]
        FirstEnterWorldDone              = 106,
        RecallsDisabled                  = 107,
        [AssessmentProperty]
        RareUsesTimer                    = 108,
        ActdPreorderReceivedItems        = 109,
        [Ephemeral]
        Afk                              = 110,
        IsGagged                         = 111,
        ProcSpellSelfTargeted            = 112,
        IsAllegianceGagged               = 113,
        EquipmentSetTriggerPiece         = 114,
        Uninscribe                       = 115,
        WieldOnUse                       = 116,
        ChestClearedWhenClosed           = 117,
        NeverAttack                      = 118,
        SuppressGenerateEffect           = 119,
        TreasureCorpse                   = 120,
        EquipmentSetAddLevel             = 121,
        BarberActive                     = 122,
        TopLayerPriority                 = 123,
        [SendOnLogin]
        NoHeldItemShown                  = 124,
        [SendOnLogin]
        LoginAtLifestone                 = 125,
        OlthoiPk                         = 126,
        [SendOnLogin]
        Account15Days                    = 127,
        HadNoVitae                       = 128,
        NoOlthoiTalk                     = 129,
        [AssessmentProperty]
        AutowieldLeft                    = 130,

        /* Custom Properties */
        LinkedPortalOneSummon            = 9001,
        LinkedPortalTwoSummon            = 9002,
        HouseEvicted                     = 9003,
        UntrainedSkills                  = 9004,
        [Ephemeral]
        IsEnvoy                          = 9005,
        UnspecializedSkills              = 9006,
        FreeSkillResetRenewed            = 9007,
        FreeAttributeResetRenewed        = 9008,
        SkillTemplesTimerReset           = 9009,
        FreeMasteryResetRenewed          = 9010,
        IsDefendersShield                = 9011,
        IsArchmagiCaster                 = 9012,
        IsThievesDagger                  = 9013,
        IsSentinelSpear                  = 9014,
        IsFencerBlade                    = 9015,
        IsRavagersAxe                    = 9016,
        IsWardensMaul                    = 9017,
        IsResoluteBlade                  = 9018,
        IsPolebreakerStaff               = 9019,
        IsStalkersBow                    = 9020,
        IsBreachersCrossbow              = 9021,
        IsReapersAtlatl                  = 9022,

        /* DerpACE Mob Modifiers — rare affixes applied at spawn */
        IsVampiricMob                    = 9023,
        IsThiefMob                       = 9024,
        // 9025 reserved IsWardenMob (future)
        IsNocturnalMob                   = 9026,
        IsExplodingMob                   = 9027,
        IsSimulacrumMob                  = 9028,

        /* DerpACE Ironman mode (irreversible solo / hardcore character flag) */
        IsIronman                        = 9029,
        IsHardcore                       = 9030,
        IsIronmanItem                    = 9031,
        IsScoutMob                       = 9032,

        /* DerpACE Hierophant Caster — support life-staff variant */
        IsHierophantCaster               = 9033,

        /* DerpACE Wacky Loot event — marks an item that should periodically re-roll palette */
        IsWackyItem                      = 9034,

        /* DerpACE Vampiric Jewelry — ring/necklace/bracelet that grants a small lifesteal regen */
        IsVampiricJewelry                = 9035,

        /* DerpACE Healer mob — casts Heal Other on wounded nearby allies */
        IsHealerMob                      = 9036,

        /* DerpACE Tank mob — high HP, physical damage reduction, bonus to heals received */
        IsTankMob                        = 9037,

        /* DerpACE Olthoi morphic system — flag indicating player is morphed into a creature */
        IsMorphicForm                    = 9038,

        /* DerpACE Ironman Nomad submode — no weapons/casters, unarmed via gauntlets/shoes, natural AL 450 in clothes */
        IsIronmanNomad                   = 9039,
    }
}
