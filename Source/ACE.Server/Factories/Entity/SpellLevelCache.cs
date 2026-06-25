using System.Collections.Concurrent;

using ACE.Server.Entity;

namespace ACE.Server.Factories.Entity
{
    public static class SpellLevelCache
    {
        private static readonly ConcurrentDictionary<int, SpellInfo> spellInfo = new ConcurrentDictionary<int, SpellInfo>();

        public static int GetSpellLevel(int spellId)
        {
            return GetSpellInfo(spellId).FormulaLevel;
        }

        public static int GetServerSpellLevel(int spellId)
        {
            return GetSpellInfo(spellId).ServerLevel;
        }

        public static int GetBaseMana(int spellId)
        {
            return GetSpellInfo(spellId).BaseMana;
        }

        public static int GetPower(int spellId)
        {
            return GetSpellInfo(spellId).Power;
        }

        private static SpellInfo GetSpellInfo(int spellId)
        {
            return spellInfo.GetOrAdd(spellId, BuildSpellInfo);
        }

        private static SpellInfo BuildSpellInfo(int spellId)
        {
            var spell = new Spell(spellId, false);

            if (spell._spellBase == null)
                return default;

            return new SpellInfo
            {
                FormulaLevel = spell.Formula != null ? (int)spell.Formula.Level : 0,
                ServerLevel = (int)spell.Level,
                BaseMana = (int)spell.BaseMana,
                Power = (int)spell.Power,
            };
        }

        private struct SpellInfo
        {
            public int FormulaLevel;
            public int ServerLevel;
            public int BaseMana;
            public int Power;
        }
    }
}
