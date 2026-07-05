using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACEClothingBaseCommands
    {
        [CommandHandler("cbexport", AccessLevel.Developer, CommandHandlerFlag.None, 1,
            "Exports a ClothingBase entry from portal.dat as an intentional base override JSON.",
            "<id> [label]")]
        [CommandHandler("clothingbase-export", AccessLevel.Developer, CommandHandlerFlag.None, 1,
            "Exports a ClothingBase entry from portal.dat as an intentional base override JSON.",
            "<id> [label]")]
        public static void HandleCbExport(Session session, params string[] parameters)
        {
            if (!TryParseId(parameters[0], out var id))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid ID. Usage: @cbexport 0x10001234 or @cbexport 268468276", ChatMessageType.Broadcast);
                return;
            }

            var error = CustomClothingManager.Export(id, out var outPath,
                parameters.Length > 1 ? string.Join(" ", parameters, 1, parameters.Length - 1) : null);

            if (error != null)
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] {error}", ChatMessageType.Broadcast);
            else
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] Exported base override 0x{id:X8} to:\n{outPath}\nThis file includes AllowBaseOverride=true and will affect all items using that base.", ChatMessageType.Broadcast);
        }

        [CommandHandler("cbclone", AccessLevel.Developer, CommandHandlerFlag.None, 2,
            "Clones a portal.dat ClothingBase to a new custom ClothingBase JSON without affecting base items.",
            "<sourceId> <newCustomId> [label]")]
        [CommandHandler("clothingbase-clone", AccessLevel.Developer, CommandHandlerFlag.None, 2,
            "Clones a portal.dat ClothingBase to a new custom ClothingBase JSON without affecting base items.",
            "<sourceId> <newCustomId> [label]")]
        public static void HandleCbClone(Session session, params string[] parameters)
        {
            if (!TryParseId(parameters[0], out var sourceId) || !TryParseId(parameters[1], out var newId))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid ID. Usage: @cbclone 0x10001234 0x10FF1234 optional label", ChatMessageType.Broadcast);
                return;
            }

            var error = CustomClothingManager.ExportClone(sourceId, newId, out var outPath,
                parameters.Length > 2 ? string.Join(" ", parameters, 2, parameters.Length - 2) : null);

            if (error != null)
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] {error}", ChatMessageType.Broadcast);
            else
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] Cloned 0x{sourceId:X8} to isolated custom ClothingBase 0x{newId:X8}:\n{outPath}\nSet only the custom item's ClothingBase to 0x{newId:X8}.", ChatMessageType.Broadcast);
        }

        [CommandHandler("cbreload", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Reloads all custom ClothingBase JSON files from Data/CustomClothingBase/ and clears the ClothingTable cache.")]
        public static void HandleCbReload(Session session, params string[] parameters)
        {
            CustomClothingManager.Reload();
            CommandHandlerHelper.WriteOutputInfo(session,
                "[ClothingBase] Reloaded JSON files and cleared ClothingTable cache.",
                ChatMessageType.Broadcast);
        }

        [CommandHandler("cbclear", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Clears only the ClothingTable entries from the portal.dat file cache (forces a fresh re-read on next use).")]
        [CommandHandler("clear-clothing-cache", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Clears only the ClothingTable entries from the portal.dat file cache (forces a fresh re-read on next use).")]
        public static void HandleCbClear(Session session, params string[] parameters)
        {
            var count = CustomClothingManager.ClearCache();
            CommandHandlerHelper.WriteOutputInfo(session,
                $"[ClothingBase] Cleared {count} ClothingTable entries from cache.",
                ChatMessageType.Broadcast);
        }

        private static bool TryParseId(string raw, out uint id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();
            if (raw.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(raw.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out id);

            if (raw.Length == 8 && uint.TryParse(raw, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hexId) && hexId >= 0x10000000 && hexId <= 0x10FFFFFF)
            {
                id = hexId;
                return true;
            }

            return uint.TryParse(raw, out id);
        }
    }
}