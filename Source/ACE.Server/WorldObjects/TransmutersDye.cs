using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>
    /// Transmuter's Dye (WCID 420420423).
    ///
    /// Sister item to RandomDye. Where Random Dye only re-rolls palette/shade, this dye
    /// also re-rolls the MaterialType of the target — so a Chainmail Hauberk can come out
    /// reading as Linen, Granite, Gold, or GromnieHide, with a matching random palette
    /// applied on top so the visual shifts as well. Purely cosmetic; no stat changes.
    ///
    /// Per-use consumable, same animation/timing as RandomDye.
    /// </summary>
    public class TransmutersDye : CraftTool
    {
        private static readonly Random _random = new Random();
        private const uint TRANSMUTERS_DYE_WCID = 420420423;

        // Curated pool of materials that read as a sensible "what is this made of?" answer
        // on armor/clothing. Gems are excluded — nobody wants a "Diamond Helm" appraisal.
        // Stones are included intentionally for the fashion-lottery vibe.
        private static readonly global::ACE.Entity.Enum.MaterialType[] TransmutableMaterials = new global::ACE.Entity.Enum.MaterialType[]
        {
            // textiles
            global::ACE.Entity.Enum.MaterialType.Cloth,
            global::ACE.Entity.Enum.MaterialType.Linen,
            global::ACE.Entity.Enum.MaterialType.Satin,
            global::ACE.Entity.Enum.MaterialType.Silk,
            global::ACE.Entity.Enum.MaterialType.Velvet,
            global::ACE.Entity.Enum.MaterialType.Wool,

            // hides / leathers
            global::ACE.Entity.Enum.MaterialType.Ivory,
            global::ACE.Entity.Enum.MaterialType.Leather,
            global::ACE.Entity.Enum.MaterialType.ArmoredilloHide,
            global::ACE.Entity.Enum.MaterialType.GromnieHide,
            global::ACE.Entity.Enum.MaterialType.ReedSharkHide,

            // metals
            global::ACE.Entity.Enum.MaterialType.Metal,
            global::ACE.Entity.Enum.MaterialType.Brass,
            global::ACE.Entity.Enum.MaterialType.Bronze,
            global::ACE.Entity.Enum.MaterialType.Copper,
            global::ACE.Entity.Enum.MaterialType.Gold,
            global::ACE.Entity.Enum.MaterialType.Iron,
            global::ACE.Entity.Enum.MaterialType.Pyreal,
            global::ACE.Entity.Enum.MaterialType.Silver,
            global::ACE.Entity.Enum.MaterialType.Steel,

            // stones (rare, but fun)
            global::ACE.Entity.Enum.MaterialType.Stone,
            global::ACE.Entity.Enum.MaterialType.Alabaster,
            global::ACE.Entity.Enum.MaterialType.Granite,
            global::ACE.Entity.Enum.MaterialType.Marble,
            global::ACE.Entity.Enum.MaterialType.Obsidian,
            global::ACE.Entity.Enum.MaterialType.Sandstone,
            global::ACE.Entity.Enum.MaterialType.Serpentine,

            // woods
            global::ACE.Entity.Enum.MaterialType.Wood,
            global::ACE.Entity.Enum.MaterialType.Ebony,
            global::ACE.Entity.Enum.MaterialType.Mahogany,
            global::ACE.Entity.Enum.MaterialType.Oak,
            global::ACE.Entity.Enum.MaterialType.Pine,
            global::ACE.Entity.Enum.MaterialType.Teak,
        };

        public TransmutersDye(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public TransmutersDye(Biota biota) : base(biota)
        {
        }

        public override void HandleActionUseOnTarget(Player player, WorldObject target)
        {
            if (WeenieClassId != TRANSMUTERS_DYE_WCID)
            {
                base.HandleActionUseOnTarget(player, target);
                return;
            }

            if (target is Player)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("You cannot transmute that.", ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            var clothingBaseId = target.GetProperty(PropertyDataId.ClothingBase);
            if (clothingBaseId == null || clothingBaseId == 0)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("This item cannot be transmuted.", ChatMessageType.Tell));
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
                    var clothingTable = DatManager.PortalDat.ReadFromDat<ClothingTable>(clothingBaseId.Value);
                    if (clothingTable?.ClothingSubPalEffects == null || clothingTable.ClothingSubPalEffects.Count == 0)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat("This item has no available palettes.", ChatMessageType.Tell));
                        player.SendUseDoneEvent();
                        return;
                    }

                    // Random palette + shade (so the visual changes, same as RandomDye).
                    var validPalettes = clothingTable.ClothingSubPalEffects.Keys.ToList();
                    var randomPalette = (int)validPalettes[_random.Next(validPalettes.Count)];
                    var randomShade = _random.NextDouble();

                    // Random material — the headline feature of this dye.
                    var newMaterial = TransmutableMaterials[_random.Next(TransmutableMaterials.Length)];

                    var icon = clothingTable.GetIcon((uint)randomPalette);
                    target.SetProperty(PropertyDataId.Icon, icon);
                    target.SetProperty(PropertyInt.PaletteTemplate, randomPalette);
                    target.SetProperty(PropertyFloat.Shade, randomShade);
                    target.SetProperty(PropertyInt.MaterialType, (int)newMaterial);

                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                    if (target.CurrentWieldedLocation != null)
                        player.EnqueueBroadcast(new GameMessageObjDescEvent(player));

                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"The Transmuter's Dye seethes and reshapes the {target.Name} — it now appears wrought from {GetMaterialDisplayName(newMaterial)}.",
                        ChatMessageType.Tell));

                    player.TryConsumeFromInventoryWithNetworking(this, 1);
                }
                catch (Exception ex)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat("An error occurred while applying the dye.", ChatMessageType.Tell));
                    Console.WriteLine($"TransmutersDye error: {ex}");
                }

                player.SendUseDoneEvent();
            });

            actionChain.EnqueueChain();

            player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
        }

        private static string GetMaterialDisplayName(global::ACE.Entity.Enum.MaterialType material)
        {
            // Insert spaces before capital letters in CamelCase enum names so "ArmoredilloHide"
            // reads naturally as "Armoredillo Hide" in the chat line.
            var raw = material.ToString();
            var sb = new System.Text.StringBuilder(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

