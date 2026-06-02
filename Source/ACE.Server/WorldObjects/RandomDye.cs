using System;
using System.Linq;

using ACE.Common;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public class RandomDye : CraftTool
    {
        private const uint RANDOM_DYE_WCID = 420420420;

        public RandomDye(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public RandomDye(Biota biota) : base(biota)
        {
        }

        public override void HandleActionUseOnTarget(Player player, WorldObject target)
        {
            if (WeenieClassId != RANDOM_DYE_WCID)
            {
                base.HandleActionUseOnTarget(player, target);
                return;
            }

            if (target is Player)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("You cannot dye that.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            var clothingBaseId = target.GetProperty(PropertyDataId.ClothingBase);
            if (clothingBaseId == null || clothingBaseId == 0)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("This item cannot be dyed.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            var animTime = 0.0f;

            var actionChain = new ActionChain();

            if (player.CombatMode != CombatMode.NonCombat)
            {
                var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
                actionChain.AddDelaySeconds(stanceTime);
                animTime += stanceTime;
            }

            animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

            actionChain.AddAction(player, () =>
            {
                try
                {
                    if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null ||
                        player.FindObject(target.Guid.Full, Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems) == null)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat("The dye and target item must remain in your possession.", ChatMessageType.Tell));
                        player.SendUseDoneEvent();
                        return;
                    }

                    var clothingTable = DatManager.PortalDat.ReadFromDat<ClothingTable>(clothingBaseId.Value);
                    if (clothingTable?.ClothingSubPalEffects == null || clothingTable.ClothingSubPalEffects.Count == 0)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat("This item has no available palettes.", ChatMessageType.Tell));
                        player.SendUseDoneEvent();
                        return;
                    }

                    var validPalettes = clothingTable.ClothingSubPalEffects.Keys.ToList();
                    var randomPalette = (int)validPalettes[ThreadSafeRandom.Next(0, validPalettes.Count - 1)];
                    var randomShade = ThreadSafeRandom.Next(0.0f, 1.0f);

                    if (!player.TryConsumeFromInventoryWithNetworking(this, 1))
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat("The dye could not be consumed.", ChatMessageType.Tell));
                        player.SendUseDoneEvent();
                        return;
                    }

                    var icon = clothingTable.GetIcon((uint)randomPalette);
                    target.SetProperty(PropertyDataId.Icon, icon);
                    target.SetProperty(PropertyInt.PaletteTemplate, randomPalette);
                    target.SetProperty(PropertyFloat.Shade, randomShade);

                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                    if (target.CurrentWieldedLocation != null)
                        player.EnqueueBroadcast(new GameMessageObjDescEvent(player));

                    player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You apply the dye to the {target.Name}.", ChatMessageType.Tell));
                }
                catch (Exception ex)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat("An error occurred while applying the dye.", ChatMessageType.Tell));
                    Console.WriteLine($"RandomDye error: {ex}");
                }

                player.SendUseDoneEvent();
            });

            actionChain.EnqueueChain();

            player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
        }
    }
}
