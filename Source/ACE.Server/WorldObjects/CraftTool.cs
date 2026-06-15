using System;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;

namespace ACE.Server.WorldObjects
{
    public class CraftTool : Stackable
    {
        private const ItemType WeaponTinkSalvageTargetType = ItemType.WeaponOrCaster | ItemType.Armor | ItemType.Clothing;

        /// <summary>
        /// A new biota be created taking all of its values from weenie.
        /// </summary>
        public CraftTool(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
            SetEphemeralValues();
        }

        /// <summary>
        /// Restore a WorldObject from the database.
        /// </summary>
        public CraftTool(Biota biota) : base(biota)
        {
            SetEphemeralValues();
        }

        private void SetEphemeralValues()
        {
            if (ItemType == ItemType.TinkeringMaterial && IsWeaponTinkSalvage())
                TargetType = WeaponTinkSalvageTargetType;
        }

        private bool IsWeaponTinkSalvage()
        {
            switch ((ACE.Entity.Enum.WeenieClassName)WeenieClassId)
            {
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALIRON100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALIRON_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALGRANITE100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALGRANITE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALGRANITEPATHWARDEN_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALVELVET100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALVELVET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_LUCKYRABBITSFOOT_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALMAHOGANY100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALMAHOGANY_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALOAK_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALOPAL100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALOPAL_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALGREENGARNET100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALGREENGARNET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALAQUAMARINE100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALAQUAMARINE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALBLACKGARNET100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALBLACKGARNET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALBLACKOPAL100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALBLACKOPAL_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALEMERALD100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALEMERALD_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALFIREOPAL100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALFIREOPAL_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALIMPERIALTOPAZ100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALIMPERIALTOPAZ_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALJET100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALJET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALREDGARNET100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALREDGARNET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALSUNSTONE100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALSUNSTONE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALWHITESAPPHIRE100_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALWHITESAPPHIRE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFAQUAMARINE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFBLACKGARNET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFBLACKOPAL_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFEMERALD_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFFIREOPAL_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFIMPERIALTOPAZ_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFJET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFREDGARNET_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFSUNSTONE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALRAREFOOLPROOFWHITESAPPHIRE_CLASS:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36619FOOLPROOFAQUAMARINE:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36620FOOLPROOFBLACKGARNET:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36621FOOLPROOFBLACKOPAL:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36622FOOLPROOFEMERALD:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36623FOOLPROOFFIREOPAL:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36624FOOLPROOFIMPERIALTOPAZ:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36625FOOLPROOFJET:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36626FOOLPROOFREDGARNET:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36627FOOLPROOFSUNSTONE:
                case ACE.Entity.Enum.WeenieClassName.W_MATERIALACE36628FOOLPROOFWHITESAPPHIRE:
                    return true;
                default:
                    return false;
            }
        }

        public override void HandleActionUseOnTarget(Player player, WorldObject target)
        {
            if (Tailoring.IsTailoringKit(WeenieClassId))
            {
                Tailoring.UseObjectOnTarget(player, this, target);
                return;
            }

            if (PetDevice.IsEncapsulatedSpirit(this) && target is PetDevice petDevice)
            {
                petDevice.Refill(player, this);
                return;
            }

            if (Aetheria.IsAetheriaManaStone(this) && Aetheria.IsAetheria(target.WeenieClassId))
            {
                Aetheria.UseObjectOnTarget(player, this, target);
                return;
            }

            if (CorePlating.IsCorePlatingDevice(this))
            {
                CorePlating.UseObjectOnTarget(player, this, target);
                return;
            }

            // fallback on recipe manager
            base.HandleActionUseOnTarget(player, target);
        }

        public override void ActOnUse(WorldObject wo)
        {
            // Do nothing
        }
    }
}
