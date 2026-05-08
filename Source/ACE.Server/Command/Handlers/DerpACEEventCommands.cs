using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACEEventCommands
    {
        // @start event <name>
        [CommandHandler("start", AccessLevel.Developer, CommandHandlerFlag.None, 2,
            "Starts a named custom server event.",
            "event <name>\n" +
            "Supported events:\n" +
            "  wacky — lootgen weapons and shields drop with a random scale (0.25 – 3.25)")]
        public static void HandleStartEvent(Session session, params string[] parameters)
        {
            if (!string.Equals(parameters[0], "event", System.StringComparison.OrdinalIgnoreCase))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @start event <name>", ChatMessageType.Broadcast);
                return;
            }

            var name = parameters.Length >= 2 ? parameters[1].ToLower() : "";

            switch (name)
            {
                case "wacky":
                    ServerEvents.WackyLoot = true;
                    CommandHandlerHelper.WriteOutputInfo(session, "[Event] Wacky Loot is now ON — weapons and shields will drop at random scales!", ChatMessageType.Broadcast);
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat("A strange wind sweeps through Dereth... loot will never look the same.", ChatMessageType.Broadcast));
                    break;

                default:
                    CommandHandlerHelper.WriteOutputInfo(session, $"Unknown event '{name}'. Supported: wacky", ChatMessageType.Broadcast);
                    break;
            }
        }

        // @end event <name>
        [CommandHandler("end", AccessLevel.Developer, CommandHandlerFlag.None, 2,
            "Ends a named custom server event.",
            "event <name>")]
        public static void HandleEndEvent(Session session, params string[] parameters)
        {
            if (!string.Equals(parameters[0], "event", System.StringComparison.OrdinalIgnoreCase))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Usage: @end event <name>", ChatMessageType.Broadcast);
                return;
            }

            var name = parameters.Length >= 2 ? parameters[1].ToLower() : "";

            switch (name)
            {
                case "wacky":
                    ServerEvents.WackyLoot = false;
                    CommandHandlerHelper.WriteOutputInfo(session, "[Event] Wacky Loot is now OFF.", ChatMessageType.Broadcast);
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat("The strange wind passes. Loot returns to normal.", ChatMessageType.Broadcast));
                    break;

                default:
                    CommandHandlerHelper.WriteOutputInfo(session, $"Unknown event '{name}'. Supported: wacky", ChatMessageType.Broadcast);
                    break;
            }
        }
    }
}
