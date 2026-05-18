using System.Collections.Generic;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: in-memory "bonus stat" buffs that stack on top of a creature's biota-stored
    /// attribute / vital / skill InitLevel values, without persisting to the database.
    ///
    /// Adapted from the ACE.BaseMod "BonusStats" feature, but with the BaseMod runtime's
    /// FakeInt property bag replaced by simple per-instance dictionaries so bonuses naturally
    /// clear when the Creature is destroyed (logout, despawn, etc.).
    ///
    /// All accessors are gated by the <c>bonus_stats_enabled</c> PropertyManager bool. If the
    /// feature is disabled at the moment of a getter call, GetBonus returns 0 (i.e. no effect),
    /// but the underlying dictionaries are preserved so toggling back on restores prior bonuses.
    /// </summary>
    public partial class Creature
    {
        // Lazily allocated. null = "no bonuses ever set on this creature" -> getters short-circuit.
        private Dictionary<PropertyAttribute, int> _attributeBonuses;
        private Dictionary<PropertyAttribute2nd, int> _vitalBonuses;
        private Dictionary<Skill, int> _skillBonuses;

        private static bool BonusStatsEnabled => PropertyManager.GetBool("bonus_stats_enabled").Item;

        public int GetBonus(PropertyAttribute attribute)
        {
            if (!BonusStatsEnabled || _attributeBonuses == null)
                return 0;

            return _attributeBonuses.TryGetValue(attribute, out var v) ? v : 0;
        }

        public int GetBonus(PropertyAttribute2nd vital)
        {
            if (!BonusStatsEnabled || _vitalBonuses == null)
                return 0;

            return _vitalBonuses.TryGetValue(vital, out var v) ? v : 0;
        }

        public int GetBonus(Skill skill)
        {
            if (!BonusStatsEnabled || _skillBonuses == null)
                return 0;

            return _skillBonuses.TryGetValue(skill, out var v) ? v : 0;
        }

        public void SetBonus(PropertyAttribute attribute, int value)
        {
            if (_attributeBonuses == null)
                _attributeBonuses = new Dictionary<PropertyAttribute, int>();

            if (value == 0)
                _attributeBonuses.Remove(attribute);
            else
                _attributeBonuses[attribute] = value;
        }

        public void SetBonus(PropertyAttribute2nd vital, int value)
        {
            if (_vitalBonuses == null)
                _vitalBonuses = new Dictionary<PropertyAttribute2nd, int>();

            if (value == 0)
                _vitalBonuses.Remove(vital);
            else
                _vitalBonuses[vital] = value;
        }

        public void SetBonus(Skill skill, int value)
        {
            if (_skillBonuses == null)
                _skillBonuses = new Dictionary<Skill, int>();

            if (value == 0)
                _skillBonuses.Remove(skill);
            else
                _skillBonuses[skill] = value;
        }

        public void IncBonus(PropertyAttribute attribute, int delta) => SetBonus(attribute, GetBonus(attribute) + delta);
        public void IncBonus(PropertyAttribute2nd vital, int delta) => SetBonus(vital, GetBonus(vital) + delta);
        public void IncBonus(Skill skill, int delta) => SetBonus(skill, GetBonus(skill) + delta);

        public void ClearBonusStats()
        {
            _attributeBonuses = null;
            _vitalBonuses = null;
            _skillBonuses = null;
        }
    }
}
