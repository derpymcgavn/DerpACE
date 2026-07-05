using ACE.Entity.Enum;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        // DerpACE mutator overlays sourced from exported PFID_A8R8G8B8 overlay icons.
        // Keep these centralized so visual identity can be tuned without hunting through loot logic.
        private const uint MutatorOverlayThief = 0x06005B43u;          // green blade
        private const uint MutatorOverlayQuickening = 0x06005B23u;     // blue spark/drop
        private const uint MutatorOverlayFencer = 0x06005B33u;         // crossed blades
        private const uint MutatorOverlayRavager = 0x06005B63u;        // black claw marks
        private const uint MutatorOverlayWarden = 0x06005B38u;         // armored guard mark
        private const uint MutatorOverlayLugianHammer = 0x06005B29u;   // hammer/tool mark
        private const uint MutatorOverlayResolute = 0x06005B52u;       // blue heart/shield
        private const uint MutatorOverlayPolebreaker = 0x06005B56u;    // broken shield
        private const uint MutatorOverlaySentinel = 0x06005B35u;       // sentinel silhouette
        private const uint MutatorOverlayStalker = 0x06005B34u;        // fine piercer
        private const uint MutatorOverlayBreacher = 0x06005B41u;       // split shield
        private const uint MutatorOverlayDinnerware = 0x06005B45u;     // plate shard
        private const uint MutatorOverlayReaper = 0x06005B59u;         // dark slash
        private const uint MutatorOverlayDartflinger = 0x06005B62u;    // dart
        private const uint MutatorOverlayShadow = 0x06005B48u;         // purple shadow figure

        private const uint MutatorOverlayArchmagi = 0x06005B24u;       // spellbook
        private const uint MutatorOverlayHierophant = 0x06005B25u;     // silver ward
        private const uint MutatorOverlaySkybreaker = 0x06005B39u;     // flame burst
        private const uint MutatorOverlayStormcaller = 0x06005B36u;    // storm shard
        private const uint MutatorOverlayOrbitweaver = 0x06005B51u;    // orbit seal
        private const uint MutatorOverlayConfusion = 0x06005B60u;      // crooked sigil

        private const uint MutatorOverlayDefender = 0x06005B53u;       // bright shield
        private const uint MutatorOverlayThorns = 0x06005B26u;         // green shield
        private const uint MutatorOverlayBashing = 0x06005B27u;        // impact mark
        private const uint MutatorOverlayReflection = 0x06005B32u;     // teal mirror shard
        private const uint MutatorOverlaySpellMirror = 0x06005B44u;    // reflective ward

        private const uint MutatorOverlayCulinarian = 0x06005B46u;     // food mark
        private const uint MutatorOverlayAlchemist = 0x06005B58u;      // vial
        private const uint MutatorOverlayUnarmed = 0x06005B61u;        // fist
        private const uint MutatorOverlayHealingDance = 0x06005B40u;   // red heart
        private const uint MutatorOverlayRejuvenatingDance = 0x06005B50u; // green renewal
        private const uint MutatorOverlayReplenishingDance = 0x06005B66u; // blue-green mana shard
        private const uint MutatorOverlayBattlemage = 0x06005B64u;     // battle focus

        private static uint GetArmorResonanceOverlay(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Slash => 0x06002AC2u,
                DamageType.Pierce => 0x06002AC3u,
                DamageType.Bludgeon => 0x06002AC9u,
                DamageType.Cold => 0x06002AC5u,
                DamageType.Fire => 0x06002AC4u,
                DamageType.Acid => 0x06002AC7u,
                DamageType.Electric => 0x06002AC6u,
                DamageType.Health => 0x06002AC1u,
                DamageType.Stamina => 0x06002AC8u,
                DamageType.Mana => 0x06002ACAu,
                DamageType.Nether => 0x06002ACBu,
                _ => 0x06002ACAu,
            };
        }
    }
}
