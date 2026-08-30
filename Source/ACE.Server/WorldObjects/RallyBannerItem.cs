using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    public class RallyBannerItem : GenericObject
    {
        public const uint DefaultWeenieClassId = 7000020;

        public RallyBannerItem(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        public RallyBannerItem(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            ItemUseable = Usable.Contained;
            ActivationResponse = ActivationResponse.Use;
            CooldownId = 2060;
            CooldownDuration = DerpACEConfig.RallyBannerCooldownSeconds;
            UseRequiresSkill = (int)Skill.Leadership;
            UseRequiresLevel = DerpACEConfig.RallyBannerRequiredLevel;
            if (DerpACEConfig.RallyBannerRequiresLeadership)
                UseRequiresSkill = (int)Skill.Leadership;
            else
                UseRequiresSkill = null;
        }

        public override void ActOnUse(WorldObject activator)
        {
            if (activator is Player player && RallyBannerManager.TryUse(player, this))
                return;

            base.ActOnUse(activator);
        }
    }
}