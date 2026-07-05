using System;
using System.Linq;
using System.Text;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class GlobalQuestCommands
    {
        [CommandHandler("gquest", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show all current global quest statuses.",
            "Usage: /gquest\n" +
            "Displays half-hour, hourly, daily, and weekly global quests with your progress.")]
        public static void HandleGQuest(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
                return;

            try
            {
                var statuses = GlobalKillQuestManager.GetStatuses(player)
                    .Where(s => s?.TargetName != null)
                    .OrderBy(s => s.Lane)
                    .ToList();

                if (statuses.Count == 0)
                {
                    player.SendMessage("[Global Quest] No quests are currently active.", ChatMessageType.Broadcast);
                    return;
                }

                var sb = new StringBuilder("[Global Quests]");
                foreach (var status in statuses)
                {
                    var remaining = status.Expiry - DateTime.UtcNow;
                    var timeStr = remaining.TotalSeconds > 0
                        ? remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m" : $"{(int)remaining.TotalMinutes}m {remaining.Seconds:00}s"
                        : "EXPIRED";

                    sb.AppendLine();
                    sb.AppendLine($"  {GlobalKillQuestManager.GetLaneLabel(status.Lane)}: {GetQuestTypeLabel(status.Kind)}");
                    sb.AppendLine($"    Target:   {FormatTarget(status)}");
                    sb.AppendLine($"    Progress: {FormatProgress(status)}");
                    sb.AppendLine($"    Time:     {timeStr}");
                    sb.Append($"    Reward:   {FormatReward(status)}");
                }

                player.SendMessage(sb.ToString(), ChatMessageType.Broadcast);
            }
            catch (Exception ex)
            {
                player.SendMessage($"[Global Quest] Error: {ex.Message}", ChatMessageType.System);
            }
        }

        private static string GetQuestTypeLabel(GlobalKillQuestManager.GlobalQuestKind kind)
        {
            switch (kind)
            {
                case GlobalKillQuestManager.GlobalQuestKind.ItemRace:
                    return "Item race";
                case GlobalKillQuestManager.GlobalQuestKind.DrunkenMobHunt:
                    return "Drunken mob hunt";
                case GlobalKillQuestManager.GlobalQuestKind.T8LuminanceHunt:
                    return "Tier 8 luminance hunt";
                case GlobalKillQuestManager.GlobalQuestKind.T8CurrencyHunt:
                    return "Tier 8 currency hunt";
                default:
                    return "Hunt";
            }
        }

        private static string FormatTarget(GlobalQuestStatus status)
        {
            if (status.ItemWcid != 0)
                return $"{status.TargetName} (WCID {status.ItemWcid})";
            return status.TargetName;
        }

        private static string FormatProgress(GlobalQuestStatus status)
        {
            if (status.Kind == GlobalKillQuestManager.GlobalQuestKind.ItemRace)
                return status.Completed ? "complete" : "first self-found copy wins";
            if (status.RequiredTurnIns > 0)
                return $"{status.MyTurnIns}/{status.RequiredTurnIns}";
            if (status.RequiredKills > 0)
                return $"{status.MyKills}/{status.RequiredKills}";
            return "active";
        }

        private static string FormatReward(GlobalQuestStatus status)
        {
            if (status.LuminanceReward > 0)
                return $"{status.LuminanceReward:N0} luminance";
            if (status.RewardPercent > 0)
                return $"{status.RewardPercent}% of level XP";
            return "4x XP from quest kills";
        }
    }
}