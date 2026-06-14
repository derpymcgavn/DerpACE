using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACECustomSpellCommands
    {
        [CommandHandler("customspells", AccessLevel.Admin, CommandHandlerFlag.None, 0,
            "Manages DerpACE custom spell JSON/SQL files.",
            "reload\nexport <spellId>\nexportcopy <spellId>\nimport <file.sql>")]
        [CommandHandler("customspell", AccessLevel.Admin, CommandHandlerFlag.None, 0,
            "Manages DerpACE custom spell JSON/SQL files.",
            "reload\nexport <spellId>\nexportcopy <spellId>\nimport <file.sql>")]
        public static void HandleCustomSpells(Session session, params string[] parameters)
        {
            if (parameters.Length == 0)
            {
                WriteUsage(session);
                return;
            }

            switch (parameters[0].ToLowerInvariant())
            {
                case "reload":
                    var loaded = CustomSpellManager.Reload();
                    CommandHandlerHelper.WriteOutputInfo(session,
                        $"[CustomSpells] Reloaded {loaded} custom spell definition(s) from:\n{CustomSpellManager.ContentDir}",
                        ChatMessageType.Broadcast);
                    return;

                case "export":
                    HandleExport(session, parameters, false);
                    return;

                case "exportcopy":
                case "copy":
                    HandleExport(session, parameters, true);
                    return;

                case "import":
                    HandleImport(session, parameters);
                    return;

                default:
                    WriteUsage(session);
                    return;
            }
        }

        private static void HandleExport(Session session, string[] parameters, bool asCopy)
        {
            if (parameters.Length < 2 || !uint.TryParse(parameters[1], out var spellId))
            {
                CommandHandlerHelper.WriteOutputInfo(session,
                    $"[CustomSpells] Usage: @customspells {(asCopy ? "exportcopy" : "export")} <spellId>",
                    ChatMessageType.Broadcast);
                return;
            }

            if (!CustomSpellManager.TryExportSql(spellId, asCopy, out var path, out var exportedId, out var error))
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"[CustomSpells] Export failed: {error}", ChatMessageType.Broadcast);
                return;
            }

            var copyNote = asCopy ? $" as cloned spell {exportedId}" : "";
            CommandHandlerHelper.WriteOutputInfo(session,
                $"[CustomSpells] Exported spell {spellId}{copyNote}:\n{path}",
                ChatMessageType.Broadcast);
        }

        private static void HandleImport(Session session, string[] parameters)
        {
            if (parameters.Length < 2)
            {
                CommandHandlerHelper.WriteOutputInfo(session,
                    "[CustomSpells] Usage: @customspells import <file.sql>",
                    ChatMessageType.Broadcast);
                return;
            }

            if (!CustomSpellManager.TryImportSql(parameters[1], out var loaded, out var path, out var error))
            {
                CommandHandlerHelper.WriteOutputInfo(session, $"[CustomSpells] Import failed: {error}", ChatMessageType.Broadcast);
                return;
            }

            CommandHandlerHelper.WriteOutputInfo(session,
                $"[CustomSpells] Imported {loaded} custom spell definition(s) from:\n{path}",
                ChatMessageType.Broadcast);
        }

        private static void WriteUsage(Session session)
        {
            CommandHandlerHelper.WriteOutputInfo(session,
                $"[CustomSpells] Path: {CustomSpellManager.ContentDir}\n" +
                "Usage:\n" +
                "@customspells reload\n" +
                "@customspells export <spellId>\n" +
                "@customspells exportcopy <spellId>\n" +
                "@customspells import <file.sql>",
                ChatMessageType.Broadcast);
        }
    }
}
