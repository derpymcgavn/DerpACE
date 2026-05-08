using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACEClothingBaseCommands
    {
        // @cbexport 0x10001234  (hex) or @cbexport 268435456 (decimal)
        // Optional: @cbexport 0x10001234 my cool robe  → saved as 10001234_my_cool_robe.json
        [CommandHandler("cbexport", AccessLevel.Developer, CommandHandlerFlag.None, 1,
            "Exports a ClothingBase entry from portal.dat to a JSON file in Data/CustomClothingBase/.",
            "<id> [label]   e.g. @cbexport 0x10001234 male plate   → 10001234_male_plate.json")]
        public static void HandleCbExport(Session session, params string[] parameters)
        {
            var raw = parameters[0];
            uint id;

            if (raw.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!uint.TryParse(raw[2..], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out id))
                {
                    CommandHandlerHelper.WriteOutputInfo(session, "Invalid hex ID. Usage: @cbexport 0x10001234  or  @cbexport 268468276", ChatMessageType.Broadcast);
                    return;
                }
            }
            else if (!uint.TryParse(raw, out id))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid ID. Usage: @cbexport 0x10001234  or  @cbexport 268468276", ChatMessageType.Broadcast);
                return;
            }

            var error = CustomClothingManager.Export(id, out var outPath,
                parameters.Length > 1 ? string.Join(" ", parameters, 1, parameters.Length - 1) : null);
            if (error != null)
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] {error}", ChatMessageType.Broadcast);
            else
                CommandHandlerHelper.WriteOutputInfo(session, $"[ClothingBase] Exported 0x{id:X8} to:\n{outPath}", ChatMessageType.Broadcast);
        }

        // @cbreload
        [CommandHandler("cbreload", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Reloads all custom ClothingBase JSON files from Data/CustomClothingBase/ and clears the ClothingTable cache.")]
        public static void HandleCbReload(Session session, params string[] parameters)
        {
            CustomClothingManager.Reload();
            CommandHandlerHelper.WriteOutputInfo(session,
                "[ClothingBase] Reloaded JSON files and cleared ClothingTable cache.",
                ChatMessageType.Broadcast);
        }

        // @cbclear
        [CommandHandler("cbclear", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Clears only the ClothingTable entries from the portal.dat file cache (forces a fresh re-read on next use).")]
        public static void HandleCbClear(Session session, params string[] parameters)
        {
            var count = CustomClothingManager.ClearCache();
            CommandHandlerHelper.WriteOutputInfo(session,
                $"[ClothingBase] Cleared {count} ClothingTable entries from cache.",
                ChatMessageType.Broadcast);
        }
    }
}
