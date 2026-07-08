using ACE.Entity.Enum;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        // DerpACE mutator overlays sourced from exported PFID_A8R8G8B8 overlay icons.
        // Keep these centralized so visual identity can be tuned without hunting through loot logic.
        private const uint MutatorOverlayThief = 0x06002665u;          // purple socket gem
        private const uint MutatorOverlayQuickening = 0x06002663u;     // blue socket gem
        private const uint MutatorOverlayFencer = 0x06002667u;         // cyan socket gem
        private const uint MutatorOverlayRavager = 0x06002661u;        // red socket gem
        private const uint MutatorOverlayWarden = 0x06002669u;         // dark socket gem
        private const uint MutatorOverlayLugianHammer = 0x06002662u;   // gold socket gem
        private const uint MutatorOverlayResolute = 0x06002660u;       // green socket gem
        private const uint MutatorOverlayPolebreaker = 0x06002666u;    // white socket gem
        private const uint MutatorOverlaySentinel = 0x0600265Fu;       // magenta socket gem
        private const uint MutatorOverlayStalker = 0x0600267Du;        // cyan missile/crossbow
        private const uint MutatorOverlayBreacher = 0x0600267Fu;       // iron missile/crossbow
        private const uint MutatorOverlayDinnerware = 0x06002654u;     // Bael'zharon mark
        private const uint MutatorOverlayReaper = 0x0600267Au;         // dark missile/crossbow
        private const uint MutatorOverlayDartflinger = 0x0600267Cu;    // white missile/crossbow
        private const uint MutatorOverlayShadow = 0x060026A6u;         // dark Virindi mask

        private const uint MutatorOverlayArchmagi = 0x06002684u;       // blue crystal shard
        private const uint MutatorOverlayHierophant = 0x06002687u;     // white crystal shard
        private const uint MutatorOverlaySkybreaker = 0x06002682u;     // red crystal shard
        private const uint MutatorOverlayStormcaller = 0x06002688u;    // cyan crystal shard
        private const uint MutatorOverlayOrbitweaver = 0x06002681u;    // green crystal shard
        private const uint MutatorOverlayConfusion = 0x060026A7u;      // purple Virindi mask

        private const uint MutatorOverlayDefender = 0x06005B53u;       // bright shield
        private const uint MutatorOverlayThorns = 0x06005B26u;         // green shield
        private const uint MutatorOverlayBashing = 0x06002643u;        // white crafter/impact sigil
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
