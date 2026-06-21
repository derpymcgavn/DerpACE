namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        // DerpACE mutator overlays: keep custom loot identity markers in the 0x0600285B-0x06002888 icon range.
        private const uint MutatorOverlayThief = 0x06002865u;
        private const uint MutatorOverlayQuickening = 0x0600285Fu;
        private const uint MutatorOverlayFencer = 0x0600285Bu;
        private const uint MutatorOverlayRavager = 0x06002861u;
        private const uint MutatorOverlayWarden = 0x06002860u;
        private const uint MutatorOverlayLugianHammer = 0x06002866u;
        private const uint MutatorOverlayResolute = 0x06002862u;
        private const uint MutatorOverlayPolebreaker = 0x06002864u;
        private const uint MutatorOverlaySentinel = 0x0600285Du;
        private const uint MutatorOverlayStalker = 0x0600285Eu;
        private const uint MutatorOverlayBreacher = 0x0600285Cu;
        private const uint MutatorOverlayHandCrossbow = 0x06002882u;
        private const uint MutatorOverlayDinnerware = 0x06002868u;
        private const uint MutatorOverlayReaper = 0x06002878u;
        private const uint MutatorOverlayDartflinger = 0x06002880u;
        private const uint MutatorOverlayShadow = 0x06002883u;

        private const uint MutatorOverlayArchmagi = 0x0600287Fu;
        private const uint MutatorOverlayHierophant = 0x0600287Du;
        private const uint MutatorOverlaySkybreaker = 0x06002884u;
        private const uint MutatorOverlayStormcaller = 0x06002881u;
        private const uint MutatorOverlayOrbitweaver = 0x06002887u;
        private const uint MutatorOverlayConfusion = 0x06002888u;

        private const uint MutatorOverlayDefender = 0x0600286Du;
        private const uint MutatorOverlayThorns = 0x06002870u;
        private const uint MutatorOverlayBashing = 0x06002871u;
        private const uint MutatorOverlayReflection = 0x06002875u;
        private const uint MutatorOverlaySpellMirror = 0x06002877u;

        private const uint MutatorOverlayCulinarian = 0x0600286Au;
        private const uint MutatorOverlayAlchemist = 0x0600286Bu;
        private const uint MutatorOverlayUnarmed = 0x0600286Cu;
        private const uint MutatorOverlayHealingDance = 0x06002872u;
        private const uint MutatorOverlayRejuvenatingDance = 0x06002873u;
        private const uint MutatorOverlayReplenishingDance = 0x06002874u;
    }
}
