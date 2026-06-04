using System;
using System.Linq;

using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

using EntityWeenie = ACE.Entity.Models.Weenie;

namespace ACE.Server.Command.Handlers
{
    public static class MorphicCommands
    {
        [CommandHandler("morphic", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Transforms an Olthoi player into their locked morphic creature form, or restores their normal form.",
            "[wcid or weenie class name | restore]\n" +
            "The first creature selected becomes locked to this character. Use /morphic again to toggle the locked form.")]
        public static void HandleMorphic(Session session, params string[] parameters)
        {
            var player = session.Player;

            if (player == null)
                return;

            if (!player.IsOlthoiPlayer)
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Only Olthoi characters can use /morphic.", ChatMessageType.Broadcast);
                return;
            }

            var isMorphed = player.GetProperty(PropertyBool.IsMorphicForm) == true;
            var request = parameters.Length > 0 ? string.Join(' ', parameters).Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(request))
            {
                if (isMorphed)
                {
                    RestoreMorphicForm(player);
                    CommandHandlerHelper.WriteOutputInfo(session, "You return to your Olthoi form.", ChatMessageType.Broadcast);
                    return;
                }

                var lockedWcid = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
                if (lockedWcid.HasValue && lockedWcid.Value > 0)
                {
                    ApplyMorphicForm(session, (uint)lockedWcid.Value);
                    return;
                }

                CommandHandlerHelper.WriteOutputInfo(session, "Usage: /morphic <creature wcid or class name>. After your first morph, /morphic toggles that form.", ChatMessageType.Broadcast);
                return;
            }

            if (request.Equals("restore", StringComparison.OrdinalIgnoreCase) ||
                request.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
                request.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                if (!isMorphed)
                    CommandHandlerHelper.WriteOutputInfo(session, "You are already in your Olthoi form.", ChatMessageType.Broadcast);
                else
                {
                    RestoreMorphicForm(player);
                    CommandHandlerHelper.WriteOutputInfo(session, "You return to your Olthoi form.", ChatMessageType.Broadcast);
                }

                return;
            }

            var target = ResolveWeenie(request);
            if (target == null)
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"Weenie '{request}' was not found.", ChatMessageType.Broadcast);
                return;
            }

            ApplyMorphicForm(session, target.WeenieClassId);
        }

        private static EntityWeenie ResolveWeenie(string request)
        {
            if (uint.TryParse(request, out var wcid))
                return DatabaseManager.World.GetCachedWeenie(wcid);

            return DatabaseManager.World.GetCachedWeenie(request);
        }

        private static void ApplyMorphicForm(Session session, uint wcid)
        {
            var player = session.Player;
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);

            if (weenie == null)
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"Weenie {wcid} was not found.", ChatMessageType.Broadcast);
                return;
            }

            if (!IsAllowedMorphicTarget(weenie))
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"{weenie.GetProperty(PropertyString.Name) ?? weenie.ClassName} is not a valid morphic creature.", ChatMessageType.Broadcast);
                return;
            }

            var lockedWcid = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
            if (lockedWcid.HasValue && lockedWcid.Value > 0 && lockedWcid.Value != (int)wcid)
            {
                var locked = DatabaseManager.World.GetCachedWeenie((uint)lockedWcid.Value);
                var lockedName = locked?.GetProperty(PropertyString.Name) ?? locked?.ClassName ?? lockedWcid.Value.ToString();
                CommandHandlerHelper.WriteOutputInfo(session, $"Your morphic form is already locked to {lockedName}.", ChatMessageType.Broadcast);
                return;
            }

            var target = WorldObjectFactory.CreateNewWorldObject(weenie);
            if (target == null)
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"Unable to create morphic form from {weenie.ClassName}.", ChatMessageType.Broadcast);
                return;
            }

            if (player.GetProperty(PropertyBool.IsMorphicForm) != true)
                StoreOriginalVisuals(player);

            var targetObjDesc = target.CalculateObjDesc();

            player.SetupTableId = target.SetupTableId;
            player.MotionTableId = target.MotionTableId;
            player.SoundTableId = target.SoundTableId;
            player.PaletteBaseDID = target.PaletteBaseDID ?? (targetObjDesc.PaletteID > 0 ? targetObjDesc.PaletteID : null);
            player.ClothingBase = target.ClothingBase;
            player.SetProperty(PropertyBool.IsMorphicForm, true);
            player.SetProperty(PropertyInt.MorphicCreatureWCID, (int)wcid);

            if (!lockedWcid.HasValue || lockedWcid.Value <= 0)
                player.SetProperty(PropertyInt.MorphicLockedCreatureWCID, (int)wcid);

            player.Biota.PropertiesAnimPart = targetObjDesc.AnimPartChanges.Clone(player.BiotaDatabaseLock);
            player.Biota.PropertiesPalette = targetObjDesc.SubPalettes.Clone(player.BiotaDatabaseLock);
            player.Biota.PropertiesTextureMap = targetObjDesc.TextureChanges.Clone(player.BiotaDatabaseLock);

            RefreshAppearance(player);

            var targetName = weenie.GetProperty(PropertyString.Name) ?? weenie.ClassName;
            CommandHandlerHelper.WriteOutputInfo(session, $"You assume the morphic form of {targetName}.", ChatMessageType.Broadcast);
        }

        private static bool IsAllowedMorphicTarget(EntityWeenie weenie)
        {
            return weenie.WeenieType == WeenieType.Creature ||
                   weenie.WeenieType == WeenieType.Cow ||
                   weenie.WeenieType == WeenieType.Pet ||
                   weenie.WeenieType == WeenieType.CombatPet;
        }

        private static void StoreOriginalVisuals(Player player)
        {
            player.SetProperty(PropertyInt.MorphicOriginalSetup, (int)player.SetupTableId);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalMotionTable, player.MotionTableId);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalSoundTable, player.SoundTableId);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalPaletteBase, player.PaletteBaseDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalClothingBase, player.ClothingBase);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalEyesTexture, player.EyesTextureDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalNoseTexture, player.NoseTextureDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalMouthTexture, player.MouthTextureDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalHairPalette, player.HairPaletteDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalEyesPalette, player.EyesPaletteDID);
            StoreOriginalDataId(player, PropertyDataId.MorphicOriginalSkinPalette, player.SkinPaletteDID);
        }

        private static void StoreOriginalDataId(Player player, PropertyDataId property, uint value)
        {
            if (value > 0)
                player.SetProperty(property, value);
            else
                player.RemoveProperty(property);
        }

        private static void StoreOriginalDataId(Player player, PropertyDataId property, uint? value)
        {
            if (value.HasValue && value.Value > 0)
                player.SetProperty(property, value.Value);
            else
                player.RemoveProperty(property);
        }

        private static void RestoreMorphicForm(Player player)
        {
            var originalSetup = player.GetProperty(PropertyInt.MorphicOriginalSetup);
            if (originalSetup.HasValue && originalSetup.Value > 0)
                player.SetupTableId = (uint)originalSetup.Value;

            RestoreDataId(player, PropertyDataId.MorphicOriginalMotionTable, value => player.MotionTableId = value);
            RestoreDataId(player, PropertyDataId.MorphicOriginalSoundTable, value => player.SoundTableId = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalPaletteBase, value => player.PaletteBaseDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalClothingBase, value => player.ClothingBase = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalEyesTexture, value => player.EyesTextureDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalNoseTexture, value => player.NoseTextureDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalMouthTexture, value => player.MouthTextureDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalHairPalette, value => player.HairPaletteDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalEyesPalette, value => player.EyesPaletteDID = value);
            RestoreNullableDataId(player, PropertyDataId.MorphicOriginalSkinPalette, value => player.SkinPaletteDID = value);

            player.RemoveProperty(PropertyBool.IsMorphicForm);
            player.RemoveProperty(PropertyInt.MorphicCreatureWCID);
            player.RemoveProperty(PropertyInt.MorphicOriginalSetup);
            ClearOriginalDataIds(player);

            player.Biota.PropertiesAnimPart = Enumerable.Empty<PropertiesAnimPart>().ToList();
            player.Biota.PropertiesPalette = Enumerable.Empty<PropertiesPalette>().ToList();
            player.Biota.PropertiesTextureMap = Enumerable.Empty<PropertiesTextureMap>().ToList();

            RefreshAppearance(player);
        }

        private static void RestoreDataId(Player player, PropertyDataId originalProperty, Action<uint> restore)
        {
            var value = player.GetProperty(originalProperty);
            if (value.HasValue && value.Value > 0)
                restore(value.Value);
        }

        private static void RestoreNullableDataId(Player player, PropertyDataId originalProperty, Action<uint?> restore)
        {
            var value = player.GetProperty(originalProperty);
            restore(value.HasValue && value.Value > 0 ? value.Value : null);
        }

        private static void ClearOriginalDataIds(Player player)
        {
            player.RemoveProperty(PropertyDataId.MorphicOriginalMotionTable);
            player.RemoveProperty(PropertyDataId.MorphicOriginalSoundTable);
            player.RemoveProperty(PropertyDataId.MorphicOriginalPaletteBase);
            player.RemoveProperty(PropertyDataId.MorphicOriginalClothingBase);
            player.RemoveProperty(PropertyDataId.MorphicOriginalEyesTexture);
            player.RemoveProperty(PropertyDataId.MorphicOriginalNoseTexture);
            player.RemoveProperty(PropertyDataId.MorphicOriginalMouthTexture);
            player.RemoveProperty(PropertyDataId.MorphicOriginalHairPalette);
            player.RemoveProperty(PropertyDataId.MorphicOriginalEyesPalette);
            player.RemoveProperty(PropertyDataId.MorphicOriginalSkinPalette);
        }

        private static void RefreshAppearance(Player player)
        {
            if (player.PhysicsObj != null)
                player.PhysicsObj.SetMotionTableID(player.MotionTableId);

            player.EnqueueBroadcast(new GameMessageUpdateObject(player));
            player.EnqueueBroadcast(new GameMessageObjDescEvent(player));
        }
    }
}
