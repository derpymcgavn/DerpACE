using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects
{
    partial class WorldObject
    {
        public DamageType? ArmorSortDamageType
        {
            get
            {
                var value = GetProperty(PropertyInt.ArmorSortDamageType);
                return value.HasValue ? (DamageType)value.Value : null;
            }
            set
            {
                if (!value.HasValue || value.Value == DamageType.Undef)
                    RemoveProperty(PropertyInt.ArmorSortDamageType);
                else
                    SetProperty(PropertyInt.ArmorSortDamageType, (int)value.Value);
            }
        }

        public int? ArmorSortDamageBonus
        {
            get => GetProperty(PropertyInt.ArmorSortDamageBonus);
            set
            {
                if (!value.HasValue || value.Value <= 0)
                    RemoveProperty(PropertyInt.ArmorSortDamageBonus);
                else
                    SetProperty(PropertyInt.ArmorSortDamageBonus, value.Value);
            }
        }
    }
}
