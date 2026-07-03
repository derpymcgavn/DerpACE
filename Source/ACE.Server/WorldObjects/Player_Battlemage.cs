using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        public bool HasBattlemageHelm => EquippedObjects.Values.Any(item =>
            item?.GetProperty(PropertyBool.IsBattlemageHelm) == true
            && (item.CurrentWieldedLocation & EquipMask.HeadWear) != 0);

        public bool IsBattlemageLightWeapon(WorldObject weapon)
        {
            if (!HasBattlemageHelm || weapon == null)
                return false;

            return IsBattlemageEligibleLightWeapon(weapon);
        }

        public bool IsBattlemageEligibleLightWeapon(WorldObject weapon)
        {
            if (weapon == null)
                return false;

            return ConvertToMoASkill(weapon.WeaponSkill) == Skill.LightWeapons;
        }

        public Skill GetBattlemageAdjustedItemSkill(WorldObject item, Skill skill)
        {
            var moaSkill = ConvertToMoASkill(skill);

            if (moaSkill == Skill.LightWeapons && IsBattlemageLightWeapon(item))
                return Skill.WarMagic;

            return moaSkill;
        }

        public bool TryGetBattlemageWarMagicRequirement(WorldObject item, out uint current, out uint required, out bool meetsRequirement)
        {
            current = GetCreatureSkill(Skill.WarMagic).Current;
            required = 0;
            meetsRequirement = true;

            if (!IsBattlemageLightWeapon(item))
                return false;

            AddBattlemageWieldRequirement(item.WieldRequirements, item.WieldSkillType, item.WieldDifficulty, ref required);
            AddBattlemageWieldRequirement(item.WieldRequirements2, item.WieldSkillType2, item.WieldDifficulty2, ref required);
            AddBattlemageWieldRequirement(item.WieldRequirements3, item.WieldSkillType3, item.WieldDifficulty3, ref required);
            AddBattlemageWieldRequirement(item.WieldRequirements4, item.WieldSkillType4, item.WieldDifficulty4, ref required);

            if (item.ItemSkillLimit.HasValue && item.ItemSkillLevelLimit.HasValue && ConvertToMoASkill(item.ItemSkillLimit.Value) == Skill.LightWeapons)
                required = System.Math.Max(required, (uint)item.ItemSkillLevelLimit.Value);

            if (item.UseRequiresSkill.HasValue && item.UseRequiresSkillLevel.HasValue && ConvertToMoASkill((Skill)item.UseRequiresSkill.Value) == Skill.LightWeapons)
                required = System.Math.Max(required, (uint)item.UseRequiresSkillLevel.Value);

            if (item.UseRequiresSkillSpec.HasValue && item.UseRequiresSkillLevel.HasValue && ConvertToMoASkill((Skill)item.UseRequiresSkillSpec.Value) == Skill.LightWeapons)
                required = System.Math.Max(required, (uint)item.UseRequiresSkillLevel.Value);

            if (required == 0)
                return false;

            meetsRequirement = current >= required;
            return true;
        }

        private void AddBattlemageWieldRequirement(WieldRequirement wieldRequirement, int? wieldSkillType, int? wieldDifficulty, ref uint required)
        {
            if (wieldRequirement != WieldRequirement.Skill && wieldRequirement != WieldRequirement.RawSkill)
                return;

            if (!wieldSkillType.HasValue || !wieldDifficulty.HasValue || wieldDifficulty.Value <= 0)
                return;

            if (ConvertToMoASkill((Skill)wieldSkillType.Value) == Skill.LightWeapons)
                required = System.Math.Max(required, (uint)wieldDifficulty.Value);
        }

        public uint GetBattlemageFocusDamageSkill()
        {
            return Focus.Current;
        }
    }
}
