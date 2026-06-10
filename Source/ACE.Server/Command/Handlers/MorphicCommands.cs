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
        private const uint StarterMorphicWcid = 31; // Drudge Skulker

        [CommandHandler("morphic", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Transforms an Olthoi player into an unlocked morphic creature form, or restores their normal form.",
            "[wcid or weenie class name | forms | restore]\n" +
            "Morphic Olthoi begin with Drudge Skulker. Killing new creatures unlocks those forms. Dying while morphed forgets that form and falls back to the previous form.")]
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
            EnsureStarterForm(player);

            if (string.IsNullOrWhiteSpace(request))
            {
                if (isMorphed)
                {
                    RestoreMorphicForm(player);
                    CommandHandlerHelper.WriteOutputInfo(session, "You return to your Olthoi form.", ChatMessageType.Broadcast);
                    return;
                }

                var currentWcid = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
                if (currentWcid.HasValue && currentWcid.Value > 0)
                {
                    ApplyMorphicForm(session, (uint)currentWcid.Value);
                    return;
                }

                ApplyMorphicForm(session, StarterMorphicWcid);
                return;
            }

            if (request.Equals("forms", StringComparison.OrdinalIgnoreCase) ||
                request.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ShowUnlockedForms(session);
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

        public static void HandleCreatureKilled(Player player, Creature creature)
        {
            if (player == null || creature == null || !player.IsOlthoiPlayer)
                return;

            EnsureStarterForm(player);

            var wcid = creature.WeenieClassId;
            if (wcid == 0 || IsFormUnlocked(player, wcid))
                return;

            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null || !IsAllowedMorphicTarget(weenie))
                return;

            AddUnlockedForm(player, wcid);
            var name = weenie.GetProperty(PropertyString.Name) ?? weenie.ClassName;
            player.SendMessage($"Morphic memory awakened: you can now /morphic {wcid} ({name}).", ChatMessageType.Broadcast);
        }

        public static void HandleMorphicDeath(Player player)
        {
            if (player == null || player.GetProperty(PropertyBool.IsMorphicForm) != true)
                return;

            var currentWcid = player.GetProperty(PropertyInt.MorphicCreatureWCID);
            RestoreMorphicForm(player);

            if (currentWcid.HasValue && currentWcid.Value > 0 && currentWcid.Value != StarterMorphicWcid)
            {
                RemoveUnlockedForm(player, (uint)currentWcid.Value);
                var lost = DatabaseManager.World.GetCachedWeenie((uint)currentWcid.Value);
                var lostName = lost?.GetProperty(PropertyString.Name) ?? lost?.ClassName ?? currentWcid.Value.ToString();
                player.SendMessage($"Your morphic memory of {lostName} is lost in death.", ChatMessageType.Broadcast);
            }

            var fallback = player.GetProperty(PropertyInt.MorphicPreviousCreatureWCID);
            if (!fallback.HasValue || fallback.Value <= 0 || !IsFormUnlocked(player, (uint)fallback.Value))
                fallback = (int)StarterMorphicWcid;

            player.SetProperty(PropertyInt.MorphicLockedCreatureWCID, fallback.Value);
            player.RemoveProperty(PropertyInt.MorphicPreviousCreatureWCID);
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

            EnsureStarterForm(player);

            if (!IsFormUnlocked(player, wcid))
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"{weenie.GetProperty(PropertyString.Name) ?? weenie.ClassName} is not unlocked yet. Defeat one first to remember its form.", ChatMessageType.Broadcast);
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

            var previousWcid = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
            if (previousWcid.HasValue && previousWcid.Value > 0 && previousWcid.Value != (int)wcid)
                player.SetProperty(PropertyInt.MorphicPreviousCreatureWCID, previousWcid.Value);

            var targetObjDesc = target.CalculateObjDesc();

            player.SetupTableId = target.SetupTableId;
            player.MotionTableId = target.MotionTableId;
            player.SoundTableId = target.SoundTableId;
            player.PaletteBaseDID = target.PaletteBaseDID ?? (targetObjDesc.PaletteID > 0 ? targetObjDesc.PaletteID : null);
            player.ClothingBase = target.ClothingBase;
            player.SetProperty(PropertyBool.IsMorphicForm, true);
            player.SetProperty(PropertyInt.MorphicCreatureWCID, (int)wcid);

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

        private static void EnsureStarterForm(Player player)
        {
            if (!IsFormUnlocked(player, StarterMorphicWcid))
                AddUnlockedForm(player, StarterMorphicWcid);

            var current = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
            if (!current.HasValue || current.Value <= 0)
                player.SetProperty(PropertyInt.MorphicLockedCreatureWCID, (int)StarterMorphicWcid);
        }

        private static bool IsFormUnlocked(Player player, uint wcid)
        {
            return GetUnlockedForms(player).Contains(wcid);
        }

        private static uint[] GetUnlockedForms(Player player)
        {
            var forms = player.GetProperty(PropertyString.MorphicUnlockedForms);
            if (string.IsNullOrWhiteSpace(forms))
                return Array.Empty<uint>();

            return forms.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(i => uint.TryParse(i, out var wcid) ? wcid : 0)
                .Where(i => i > 0)
                .Distinct()
                .ToArray();
        }

        private static void AddUnlockedForm(Player player, uint wcid)
        {
            var forms = GetUnlockedForms(player).ToList();
            if (!forms.Contains(wcid))
                forms.Add(wcid);

            player.SetProperty(PropertyString.MorphicUnlockedForms, string.Join(",", forms.OrderBy(i => i)));
        }

        private static void RemoveUnlockedForm(Player player, uint wcid)
        {
            var forms = GetUnlockedForms(player).Where(i => i != wcid).ToList();
            if (!forms.Contains(StarterMorphicWcid))
                forms.Add(StarterMorphicWcid);

            player.SetProperty(PropertyString.MorphicUnlockedForms, string.Join(",", forms.OrderBy(i => i)));
        }

        private static void ShowUnlockedForms(Session session)
        {
            var player = session.Player;
            var lines = GetUnlockedForms(player)
                .OrderBy(i => i)
                .Select(i =>
                {
                    var weenie = DatabaseManager.World.GetCachedWeenie(i);
                    var name = weenie?.GetProperty(PropertyString.Name) ?? weenie?.ClassName ?? "Unknown";
                    return $"{i}: {name}";
                })
                .ToList();

            CommandHandlerHelper.WriteOutputInfo(session, lines.Count == 0 ? "No morphic forms unlocked." : "Unlocked morphic forms:\n" + string.Join("\n", lines), ChatMessageType.Broadcast);
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
