using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;

namespace ACE.Server.WorldObjects
{
    public class RallyBannerDeployed : GenericObject
    {
        public const uint DefaultWeenieClassId = 7000021;

        public RallyBannerDeployed(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        public RallyBannerDeployed(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            ItemUseable = Usable.No;
            Attackable = false;
            Ethereal = true;
            IgnoreCollisions = true;
            ReportCollisions = false;
            GravityStatus = false;
            Static = true;
            NoDraw = false;
            SetProperty(PropertyInt.PhysicsState, (int)(PhysicsState.Static | PhysicsState.Ethereal | PhysicsState.IgnoreCollisions));
            SetProperty(PropertyDataId.Setup, 0x02000CDB);
            SetProperty(PropertyDataId.SoundTable, 0x20000014);
            SetProperty(PropertyDataId.PaletteBase, 0x04001379);
            SetProperty(PropertyDataId.ClothingBase, 0x100003A7);
            SetProperty(PropertyDataId.Icon, 0x060023A8);
            SetProperty(PropertyDataId.PhysicsEffectTable, 0x3400002B);
            SetProperty(PropertyInt.PaletteTemplate, 61);
            SetProperty(PropertyFloat.Shade, 0.0);
        }
    }
}