using System;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    public static class AdminWiFlagCommands
    {
        [CommandHandler("wiflag", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 1,
            "Shows a player's current hidden WI loot bias.",
            "<on | off | status | online player name>")]
        public static void HandleWiFlag(Session session, params string[] parameters)
        {
            var playerName = string.Join(" ", parameters ?? Array.Empty<string>());
            if (playerName.Equals("on", StringComparison.OrdinalIgnoreCase) || playerName.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                var enabled = playerName.Equals("on", StringComparison.OrdinalIgnoreCase);
                PropertyManager.ModifyBool("wi_name_loot_bias", enabled);
                var stateMessage = $"[WI Flag] Name-based loot bias is now {(enabled ? "ON" : "OFF")}.";
                session.Network.EnqueueSend(new GameMessageSystemChat(stateMessage, ChatMessageType.Broadcast));
                PlayerManager.BroadcastToAuditChannel(session.Player, $"{session.Player.Name} turned the WI name loot bias {(enabled ? "on" : "off")}.");
                return;
            }

            if (playerName.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                var enabled = PropertyManager.GetBool("wi_name_loot_bias").Item;
                var configuredMaxBias = PropertyManager.GetDouble("wi_name_loot_bias_max").Item;
                var windowHours = PropertyManager.GetDouble("wi_name_loot_bias_hours").Item;
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[WI Flag] System: {(enabled ? "ON" : "OFF")} | Range: +/-{configuredMaxBias:P1} | Window: {windowHours:0.##} hours",
                    ChatMessageType.Broadcast));
                return;
            }

            var player = PlayerManager.GetOnlinePlayer(playerName);
            if (player == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Player {playerName} was not found online.", ChatMessageType.Broadcast));
                return;
            }

            if (!Creature.TryGetWiNameLootBias(player, out var nameRoll, out var windowRoll, out var bias, out var maxBias, out var remaining))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The WI name loot bias is currently disabled.", ChatMessageType.Broadcast));
                return;
            }

            var state = bias >= maxBias * 0.35 ? "HOT" : bias <= -maxBias * 0.35 ? "COLD" : "NEUTRAL";
            var message = $"[WI Flag] {player.Name}\n" +
                $"State: {state} | Quality adjustment: {bias:+0.000%;-0.000%;0.000%}\n" +
                $"Name tendency: {nameRoll:+0.000;-0.000;0.000} | Window roll: {windowRoll:+0.000;-0.000;0.000}\n" +
                $"Configured range: +/-{maxBias:P1} | Rotates in: {(int)remaining.TotalHours:00}h {remaining.Minutes:00}m {remaining.Seconds:00}s";

            session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Broadcast));
        }
    }
}