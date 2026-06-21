using System;
using System.Collections.Generic;

using ACE.Common;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;

namespace ACE.Server.Factories.Tables.Wcids
{
    public static class GenericWcids
    {
        private static readonly HashSet<WeenieClassName> ThrowableDinnerwareWcids = new HashSet<WeenieClassName>
        {
            WeenieClassName.bowl,
            WeenieClassName.chalice,
            WeenieClassName.cup,
            WeenieClassName.ewer,
            WeenieClassName.flagon,
            WeenieClassName.goblet,
            WeenieClassName.mug,
            WeenieClassName.ornamentalbowl,
            WeenieClassName.dinnerplate,
            WeenieClassName.stoup,
            WeenieClassName.tankard,
            WeenieClassName.platter,
            (WeenieClassName)420498,
        };

        private static ChanceTable<WeenieClassName> T1_T2_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.bowl,           0.09f ),
            ( WeenieClassName.chalice,        0.00f ),
            ( WeenieClassName.cup,            0.14f ),
            ( WeenieClassName.ewer,           0.03f ),
            ( WeenieClassName.flagon,         0.08f ),
            ( WeenieClassName.flasksimple,    0.13f ),
            ( WeenieClassName.goblet,         0.07f ),
            ( WeenieClassName.mug,            0.12f ),
            ( WeenieClassName.ornamentalbowl, 0.00f ),
            ( WeenieClassName.dinnerplate,    0.08f ),
            ( WeenieClassName.stoup,          0.13f ),
            ( WeenieClassName.tankard,        0.11f ),
            ( (WeenieClassName)420498,        0.02f ),
        };

        private static ChanceTable<WeenieClassName> T3_T4_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.bowl,           0.08f ),
            ( WeenieClassName.chalice,        0.06f ),
            ( WeenieClassName.cup,            0.05f ),
            ( WeenieClassName.ewer,           0.11f ),
            ( WeenieClassName.flagon,         0.11f ),
            ( WeenieClassName.flasksimple,    0.05f ),
            ( WeenieClassName.goblet,         0.14f ),
            ( WeenieClassName.mug,            0.12f ),
            ( WeenieClassName.ornamentalbowl, 0.08f ),
            ( WeenieClassName.dinnerplate,    0.08f ),
            ( WeenieClassName.stoup,          0.05f ),
            ( WeenieClassName.tankard,        0.05f ),
            ( (WeenieClassName)420498,        0.02f ),
        };

        private static ChanceTable<WeenieClassName> T5_T6_Chances = new ChanceTable<WeenieClassName>()
        {
            ( WeenieClassName.bowl,           0.00f ),
            ( WeenieClassName.chalice,        0.23f ),
            ( WeenieClassName.cup,            0.00f ),
            ( WeenieClassName.ewer,           0.13f ),
            ( WeenieClassName.flagon,         0.09f ),
            ( WeenieClassName.flasksimple,    0.00f ),
            ( WeenieClassName.goblet,         0.21f ),
            ( WeenieClassName.mug,            0.00f ),
            ( WeenieClassName.ornamentalbowl, 0.19f ),
            ( WeenieClassName.dinnerplate,    0.13f ),
            ( WeenieClassName.stoup,          0.00f ),
            ( WeenieClassName.tankard,        0.00f ),
            ( (WeenieClassName)420498,        0.02f ),
        };

        private static List<ChanceTable<WeenieClassName>> tierChances = new List<ChanceTable<WeenieClassName>>()
        {
            T1_T2_Chances,
            T1_T2_Chances,
            T3_T4_Chances,
            T3_T4_Chances,
            T5_T6_Chances,
            T5_T6_Chances,
        };

        public static WeenieClassName Roll(int tier)
        {
            // todo: add unique profiles for t7 / t8?
            tier = Math.Clamp(tier, 1, 6);

            return tierChances[tier - 1].Roll();
        }

        public static WeenieClassName RollThrowable(int tier)
        {
            return RollFiltered(tier, true);
        }

        public static WeenieClassName RollNonThrowable(int tier)
        {
            return RollFiltered(tier, false);
        }

        private static WeenieClassName RollFiltered(int tier, bool throwable)
        {
            tier = Math.Clamp(tier, 1, 6);

            var table = tierChances[tier - 1];
            var total = 0.0f;

            foreach (var entry in table)
            {
                if (ThrowableDinnerwareWcids.Contains(entry.result) == throwable)
                    total += entry.chance;
            }

            if (total <= 0.0f && !throwable)
                return WeenieClassName.flasksimple;

            if (total <= 0.0f)
                return table.Roll();

            var roll = ThreadSafeRandom.Next(0.0f, total);
            var current = 0.0f;

            foreach (var entry in table)
            {
                if (ThrowableDinnerwareWcids.Contains(entry.result) != throwable)
                    continue;

                current += entry.chance;

                if (roll < current)
                    return entry.result;
            }

            foreach (var entry in table)
            {
                if (ThrowableDinnerwareWcids.Contains(entry.result) == throwable)
                    return entry.result;
            }

            return table.Roll();
        }

        private static readonly HashSet<WeenieClassName> _combined = new HashSet<WeenieClassName>();

        static GenericWcids()
        {
            foreach (var tierChance in tierChances)
            {
                foreach (var entry in tierChance)
                    _combined.Add(entry.result);
            }

            _combined.Add(WeenieClassName.platter);
        }

        public static bool Contains(WeenieClassName wcid)
        {
            return _combined.Contains(wcid);
        }
    }
}
