using System;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class GlobalQuestCommands
    {
        [CommandHandler("gquest", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the current global kill quest status.",
            "Usage: /gquest\n" +
            "Displays the current global hunt target, kills required, time remaining, and your personal progress.")]
        public static void HandleGQuest(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
            {
                return;
            }

            try
            {
                var (name, required, expiry, myKills) = GlobalKillQuestManager.GetStatus(player);

                if (name == null || required == 0)
                {
                    player.SendMessage("[Global Quest] No quest is currently active.", ChatMessageType.Broadcast);
                    return;
                }

                var remaining = expiry - DateTime.UtcNow;
                var timeStr   = remaining.TotalSeconds > 0
                    ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds:00}s"
                    : "EXPIRED";

                player.SendMessage(
                    $"[Global Quest]\n" +
                    $"  Target:    {name}\n" +
                    $"  Required:  {required} kills\n" +
                    $"  Your kills: {myKills}/{required}\n" +
                    $"  Time left:  {timeStr}\n" +
                    $"  Reward:    4x XP of your accumulated quest kills on completion.",
                    ChatMessageType.Broadcast);
            }
            catch (Exception ex)
            {
                player.SendMessage($"[Global Quest] Error: {ex.Message}", ChatMessageType.System);
            }
        }
    }
}
