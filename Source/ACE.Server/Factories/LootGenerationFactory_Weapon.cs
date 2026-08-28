using ACE.Common;
using ACE.Database.Models.World;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        /// <summary>
        /// This is only called by /testlootgen command
        /// The actual lootgen system doesn't use this.
        /// </summary>
        public static WorldObject CreateWeapon(TreasureDeath profile, bool isMagical, string forcedWeaponMutator = null, int requestedTier = 0)
        {
            var tierContext = LootTierManager.Resolve(profile);
            profile = tierContext.Profile;
            var rollTier = requestedTier > 0 ? requestedTier : tierContext.RequestedTier;
            var weaponType = WeaponTypeChance.Roll(profile.Tier);

            if (TryResolveWeaponMutator(forcedWeaponMutator, out var canonicalMutator)
                && TryGetWeaponMutatorTestType(canonicalMutator, out var forcedWeaponType))
                weaponType = forcedWeaponType;

            WorldObject weapon;
            if (weaponType.IsMeleeWeapon())
                weapon = CreateMeleeWeapon(profile, isMagical, weaponType, forcedWeaponMutator, rollTier);
            else if (weaponType.IsMissileWeapon())
                weapon = CreateMissileWeapon(profile, isMagical, forcedWeaponType: weaponType, forcedWeaponMutator: forcedWeaponMutator, requestedTier: rollTier);
            else
                weapon = CreateCaster(profile, isMagical, forcedWeaponMutator, rollTier);

            return LootTierManager.Apply(weapon, tierContext);
        }
        private static float RollWeaponSpeedMod(TreasureDeath treasureDeath)
        {
            var qualityLevel = QualityChance.Roll(treasureDeath);

            if (qualityLevel == 0)
                return 1.0f;    // no bonus

            var rng = (float)ThreadSafeRandom.Next(-0.025f, 0.025f);

            // min/max range: 67.5% - 100%
            var weaponSpeedMod = 1.0f - (qualityLevel * 0.025f + rng);

            //Console.WriteLine($"WeaponSpeedMod: {weaponSpeedMod}");

            return weaponSpeedMod;
        }
    }
}
