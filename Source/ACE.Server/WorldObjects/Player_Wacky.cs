using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        /// <summary>
        /// Called from Player.Heartbeat. While the Wacky Loot event is active,
        /// every equipped item flagged IsWackyItem rerolls its PaletteTemplate / Shade
        /// from its own ClothingTable, and the player's ObjDesc is rebroadcast once
        /// so nearby clients see the new colors.
        /// </summary>
        private void TickWackyAppearance()
        {
            if (!ServerEvents.WackyLoot)
                return;

            var changed = false;
            foreach (var item in EquippedObjects.Values)
            {
                if (item.GetProperty(PropertyBool.IsWackyItem) != true)
                    continue;

                if (item.ClothingBase == null)
                    continue;

                if (RerollWackyPalette(item))
                    changed = true;
            }

            if (changed)
                EnqueueBroadcast(new GameMessageObjDescEvent(this));
        }

        /// <summary>
        /// Picks a new random valid PaletteTemplate (and Shade) from the item's ClothingTable.
        /// Returns true if anything actually changed.
        /// </summary>
        private static bool RerollWackyPalette(WorldObject wo)
        {
            DatLoader.FileTypes.ClothingTable clothingBase;
            try
            {
                clothingBase = DatLoader.DatManager.PortalDat.ReadFromDat<DatLoader.FileTypes.ClothingTable>((uint)wo.ClothingBase);
            }
            catch
            {
                return false;
            }

            if (clothingBase?.ClothingSubPalEffects == null || clothingBase.ClothingSubPalEffects.Count == 0)
                return false;

            // Snapshot keys so the index is stable across the random pick
            var palettes = new uint[clothingBase.ClothingSubPalEffects.Count];
            var i = 0;
            foreach (var key in clothingBase.ClothingSubPalEffects.Keys)
                palettes[i++] = key;

            var pick = palettes[ACE.Common.ThreadSafeRandom.Next(0, palettes.Length - 1)];
            var sub = clothingBase.ClothingSubPalEffects[pick];

            if (sub.Icon == 0)
                return false;

            var newPalette = (int)pick;
            var newShade = ACE.Common.ThreadSafeRandom.Next(0.0f, 1.0f);

            var oldPalette = wo.PaletteTemplate ?? -1;
            if (oldPalette == newPalette)
                return false;

            wo.IconId = sub.Icon;
            wo.PaletteTemplate = newPalette;
            wo.Shade = newShade;
            return true;
        }
    }
}
