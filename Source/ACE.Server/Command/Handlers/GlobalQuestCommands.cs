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
            "Displays the current global quest target, requirements, time remaining, and your personal progress.")]
        public static void HandleGQuest(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
            {
                return;
            }

            try
            {
                var status = GlobalKillQuestManager.GetStatus(player);

                if (status.TargetName == null)
                {
                    player.SendMessage("[Global Quest] No quest is currently active.", ChatMessageType.Broadcast);
                    return;
                }

                var remaining = status.Expiry - DateTime.UtcNow;
                var timeStr   = remaining.TotalSeconds > 0
                    ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds:00}s"
                    : "EXPIRED";

                if (status.Kind == GlobalKillQuestManager.GlobalQuestKind.ItemRace)
                {
                    var completedText = status.Completed ? "yes" : "no";
                    player.SendMessage(
                        $"[Global Quest]\n" +
                        $"  Type:      Item race\n" +
                        $"  Target:    {status.TargetName} (WCID {status.ItemWcid})\n" +
                        $"  Rule:      First self-found copy wins. Traded copies do not count.\n" +
                        $"  Completed: {completedText}\n" +
                        $"  Time left: {timeStr}\n" +
                        $"  Reward:    {status.RewardPercent}% of level XP.",
                        ChatMessageType.Broadcast);
                }
                else if (status.Kind == GlobalKillQuestManager.GlobalQuestKind.DrunkenMobHunt)
                {
                    player.SendMessage(
                        $"[Global Quest]\n" +
                        $"  Type:      Drunken mob hunt\n" +
                        $"  Target:    Drunken mobs\n" +
                        $"  Rule:      Kill drunken mobs, loot their event beer, and use the beer on Ulgrim the Unpleasant.\n" +
                        $"  Turn-ins:  {status.MyTurnIns}/{status.RequiredTurnIns}\n" +
                        $"  Time left: {timeStr}\n" +
                        $"  Reward:    {status.RewardPercent}% of level XP.",
                        ChatMessageType.Broadcast);
                }
                else
                {
                    player.SendMessage(
                        $"[Global Quest]\n" +
                        $"  Type:      Hunt\n" +
                        $"  Target:    {status.TargetName}\n" +
                        $"  Required:  {status.RequiredKills} kills\n" +
                        $"  Your kills: {status.MyKills}/{status.RequiredKills}\n" +
                        $"  Time left:  {timeStr}\n" +
                        $"  Reward:    4x XP of your accumulated quest kills on completion.",
                        ChatMessageType.Broadcast);
                }
            }
            catch (Exception ex)
            {
                player.SendMessage($"[Global Quest] Error: {ex.Message}", ChatMessageType.System);
            }
        }
    }
}
