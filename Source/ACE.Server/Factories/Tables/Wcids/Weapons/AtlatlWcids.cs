using System;
using System.Collections.Generic;

using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;

namespace ACE.Server.Factories.Tables.Wcids
{
    public static class AtlatlWcids
    {
        private static ChanceTable<WeenieClassName> T1_T4_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.atlatl,      0.50f ),
            ( WeenieClassName.atlatlroyal, 0.50f ),
        };

        private static ChanceTable<WeenieClassName> T5_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.atlatl,                         0.228f ),
            ( WeenieClassName.atlatlroyal,                    0.240f ),
            ( WeenieClassName.atlatlslashing,                 0.033f ),
            ( WeenieClassName.atlatlpiercing,                 0.033f ),
            ( WeenieClassName.atlatlblunt,                    0.033f ),
            ( WeenieClassName.atlatlacid,                     0.033f ),
            ( WeenieClassName.atlatlfire,                     0.033f ),
            ( WeenieClassName.atlatlfrost,                    0.033f ),
            ( WeenieClassName.atlatlelectric,                 0.033f ),
            ( WeenieClassName.ace31812_slashingslingshot,     0.033f ),
            ( WeenieClassName.ace31818_piercingslingshot,     0.033f ),
            ( WeenieClassName.ace31814_bluntslingshot,        0.033f ),
            ( WeenieClassName.ace31813_acidslingshot,         0.033f ),
            ( WeenieClassName.ace31816_fireslingshot,         0.033f ),
            ( WeenieClassName.ace31817_frostslingshot,        0.033f ),
            ( WeenieClassName.ace31815_electricslingshot,     0.033f ),
            ( WeenieClassName.ace5238251_slashingdartflinger, 0.010f ),
            ( WeenieClassName.ace5238250_piercingdartflinger, 0.010f ),
            ( WeenieClassName.ace5238246_bluntdartflinger,    0.010f ),
            ( WeenieClassName.ace5238245_aciddartflinger,     0.010f ),
            ( WeenieClassName.ace5238248_firedartflinger,     0.010f ),
            ( WeenieClassName.ace5238249_frostdartflinger,    0.010f ),
            ( WeenieClassName.ace5238247_electricdartflinger, 0.010f ),
        };

        private static ChanceTable<WeenieClassName> T6_T8_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.atlatlslashing,                 0.070f ),
            ( WeenieClassName.atlatlpiercing,                 0.070f ),
            ( WeenieClassName.atlatlblunt,                    0.0615f ),
            ( WeenieClassName.atlatlacid,                     0.0615f ),
            ( WeenieClassName.atlatlfire,                     0.0615f ),
            ( WeenieClassName.atlatlfrost,                    0.0615f ),
            ( WeenieClassName.atlatlelectric,                 0.0615f ),
            ( WeenieClassName.ace31812_slashingslingshot,     0.070f ),
            ( WeenieClassName.ace31818_piercingslingshot,     0.070f ),
            ( WeenieClassName.ace31814_bluntslingshot,        0.0615f ),
            ( WeenieClassName.ace31813_acidslingshot,         0.0615f ),
            ( WeenieClassName.ace31816_fireslingshot,         0.0615f ),
            ( WeenieClassName.ace31817_frostslingshot,        0.0615f ),
            ( WeenieClassName.ace31815_electricslingshot,     0.0615f ),
            ( WeenieClassName.ace5238251_slashingdartflinger, 0.015f ),
            ( WeenieClassName.ace5238250_piercingdartflinger, 0.015f ),
            ( WeenieClassName.ace5238246_bluntdartflinger,    0.015f ),
            ( WeenieClassName.ace5238245_aciddartflinger,     0.015f ),
            ( WeenieClassName.ace5238248_firedartflinger,     0.015f ),
            ( WeenieClassName.ace5238249_frostdartflinger,    0.015f ),
            ( WeenieClassName.ace5238247_electricdartflinger, 0.015f ),
        };

        private static readonly List<ChanceTable<WeenieClassName>> atlatlTiers = new List<ChanceTable<WeenieClassName>>()
        {
            T1_T4_Chances,
            T1_T4_Chances,
            T1_T4_Chances,
            T1_T4_Chances,
            T5_Chances,
            T6_T8_Chances,
            T6_T8_Chances,
            T6_T8_Chances,
        };

        public static WeenieClassName Roll(int tier)
        {
            return atlatlTiers[tier - 1].Roll();
        }

        private static readonly Dictionary<WeenieClassName, TreasureWeaponType> _combined = new Dictionary<WeenieClassName, TreasureWeaponType>();

        static AtlatlWcids()
        {
            foreach (var atlatlTier in atlatlTiers)
            {
                foreach (var entry in atlatlTier)
                    _combined.TryAdd(entry.result, TreasureWeaponType.Atlatl);
            }
        }

        public static bool TryGetValue(WeenieClassName wcid, out TreasureWeaponType weaponType)
        {
            return _combined.TryGetValue(wcid, out weaponType);
        }
    }
}
