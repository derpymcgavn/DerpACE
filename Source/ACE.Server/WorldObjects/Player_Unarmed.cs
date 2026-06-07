using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: helpers and surrogate-weapon support for the "nomad" unarmed playstyle.
    ///
    /// A "nomad unarmed" player is one fighting without any weapon, missile launcher,
    /// wand, or held caster equipped. Shields are explicitly allowed (tank/block role).
    /// This is the strict gate the unarmed combo system uses to decide whether combos
    /// can be tracked at all.
    ///
    /// Adapted from the ACE.BaseMod "UnarmedWeapon" feature, but tightened so the
    /// surrogate weapon (gloves / boots) is *only* produced when the player is truly
    /// weapon-free. With a real weapon equipped, the surrogate never fires and the
    /// engine sees ordinary behavior.
    /// </summary>
    public partial class Player
    {
        public const int NomadUnarmoredArmorLevel = 420;
        public const float NomadUnarmoredProtectionMod = 1.2f;

        public bool IsIronmanNomad => GetProperty(PropertyBool.IsIronmanNomad) == true;

        /// <summary>
        /// True when the player has no melee weapon, missile weapon, two-handed weapon,
        /// or held caster (wand) equipped. Shields are intentionally permitted.
        /// </summary>
        public bool IsNomadUnarmed
        {
            get
            {
                // Any of these slots filled = not nomad unarmed. Shield slot is not in this list on purpose.
                const EquipMask DisqualifyingSlots =
                    EquipMask.MeleeWeapon |
                    EquipMask.MissileWeapon |
                    EquipMask.TwoHanded |
                    EquipMask.Held;

                foreach (var item in EquippedObjects.Values)
                {
                    if (item.CurrentWieldedLocation is EquipMask loc && (loc & DisqualifyingSlots) != 0)
                    {
                        // A real shield reports CurrentWieldedLocation == EquipMask.Shield, which is not in the
                        // disqualifying set, so it correctly slips past this check.
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Returns the equipped item that should act as the swing's "weapon" while the
        /// player is fighting nomad-unarmed. Boots when the power bar is in the kick zone,
        /// gloves otherwise. Returns null if the relevant slot is empty or if the feature
        /// or nomad gate are not satisfied.
        /// </summary>
        public WorldObject GetUnarmedSurrogateWeapon()
        {
            if (!PropertyManager.GetBool("unarmed_weapon_surrogate_enabled").Item)
                return null;

            if (!IsNomadUnarmed)
                return null;

            // PowerLevel is updated by the client power-bar state and is what Player_Melee already reads
            // when classifying punch vs. kick (see GetSwingAnimation). Mirroring that boundary here
            // keeps the surrogate slot perfectly in sync with the resolved AttackType.
            var useBoots = PowerLevel >= KickThreshold;

            return EquippedObjects.Values.FirstOrDefault(e =>
            {
                var loc = e.CurrentWieldedLocation;
                if (loc == null) return false;

                return useBoots
                    ? loc.Value.HasFlag(EquipMask.FootWear)
                    : loc.Value.HasFlag(EquipMask.HandWear);
            });
        }

        public bool IsUnarmedArmorPiece(WorldObject item)
        {
            return item != null
                && item is Clothing
                && item.CurrentWieldedLocation is EquipMask wieldLoc
                && (wieldLoc & (EquipMask.HandWear | EquipMask.FootWear)) != 0
                && ((item.UnarmedBaseDamage ?? 0) > 0
                    || item.GetProperty(PropertyInt.NomadProcType) > 0
                    || item.HasProc);
        }

        public bool CountsAgainstNomadUnarmoredProtection(WorldObject item)
        {
            if (!IsIronmanNomad || !(item is Clothing))
                return false;

            if ((item.ItemType & ItemType.Armor) == 0)
                return false;

            if (item.CurrentWieldedLocation == EquipMask.Shield)
                return false;

            return !IsUnarmedArmorPiece(item);
        }

        /// <summary>
        /// True when an Ironman Nomad is wearing no armor pieces. Shields, plain clothing,
        /// and nomad unarmed gauntlets/boots are allowed.
        /// </summary>
        public bool HasNomadUnarmoredProtection => IsIronmanNomad && !EquippedObjects.Values.Any(CountsAgainstNomadUnarmoredProtection);

        public bool IsUnarmedArmorActive(WorldObject item)
        {
            return IsNomadUnarmed && IsUnarmedArmorPiece(item);
        }

        public bool IsUnarmedFamilyAttack(WorldObject damageSource)
        {
            if (AttackType != AttackType.Punch && AttackType != AttackType.Kick)
                return false;

            if (!IsNomadUnarmed)
                return false;

            if (damageSource == this)
                return true;

            if (damageSource is Clothing
                && damageSource.CurrentWieldedLocation is EquipMask wieldLoc
                && (wieldLoc & (EquipMask.HandWear | EquipMask.FootWear)) != 0)
                return true;

            return false;
        }
    }
}
