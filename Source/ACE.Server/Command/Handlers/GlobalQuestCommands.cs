using System;
using System.Linq;

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
                    player.SendMessage("[Global Quests] No quests are currently active.", ChatMessageType.Broadcast);
                    return;
                }

                player.SendMessage("[Global Quests]", ChatMessageType.Broadcast);
                foreach (var status in statuses)
                    SendQuestLine(player, status);
            }
            catch (Exception ex)
            {
                player.SendMessage($"[Global Quest] Error: {ex.Message}", ChatMessageType.System);
            }
        }

        [CommandHandler("gquestreroll", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 1,
            "Reroll a stale daily or weekly global quest.",
            "Usage: /gquestreroll <daily|weekly|all>")]
        public static void HandleGQuestReroll(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null)
                return;

            var selection = parameters[0].Trim().ToLowerInvariant();
            var lanes = selection switch
            {
                "daily" => new[] { GlobalKillQuestManager.GlobalQuestLane.Daily },
                "weekly" => new[] { GlobalKillQuestManager.GlobalQuestLane.Weekly },
                "all" => new[] { GlobalKillQuestManager.GlobalQuestLane.Daily, GlobalKillQuestManager.GlobalQuestLane.Weekly },
                _ => null,
            };

            if (lanes == null)
            {
                player.SendMessage("Usage: /gquestreroll <daily|weekly|all>", ChatMessageType.System);
                return;
            }

            foreach (var lane in lanes)
            {
                if (!GlobalKillQuestManager.TryAdminRerollPersistentQuest(lane, out var status, out var error))
                {
                    player.SendMessage($"[Global Quest Admin] {error}", ChatMessageType.System);
                    continue;
                }

                player.SendMessage($"[Global Quest Admin] Rerolled {GlobalKillQuestManager.GetLaneLabel(lane)}: {GetQuestTypeLabel(status.Kind)} - {FormatTarget(status)}.", ChatMessageType.Broadcast);
            }
        }
        private static void SendQuestLine(ACE.Server.WorldObjects.Player player, GlobalQuestStatus status)
        {
            var line = $"{GlobalKillQuestManager.GetLaneLabel(status.Lane)}: {GetQuestTypeLabel(status.Kind)} | " +
                       $"{FormatTarget(status)} | " +
                       $"Progress {FormatProgress(status)} | " +
                       $"Time {FormatTime(status.Expiry)} | " +
                       $"Reward {FormatReward(status)}";

            player.SendMessage(line, ChatMessageType.Broadcast);
        }

        private static string GetQuestTypeLabel(GlobalKillQuestManager.GlobalQuestKind kind)
        {
            switch (kind)
            {
                case GlobalKillQuestManager.GlobalQuestKind.ItemRace:
                    return "Item race";
                case GlobalKillQuestManager.GlobalQuestKind.DrunkenMobHunt:
                    return "Drunken mobs";
                case GlobalKillQuestManager.GlobalQuestKind.T8LuminanceHunt:
                    return "T8 lum";
                case GlobalKillQuestManager.GlobalQuestKind.T8CurrencyHunt:
                    return "Correct corruption";
                case GlobalKillQuestManager.GlobalQuestKind.MutatorHunt:
                    return "Mutator purge";
                case GlobalKillQuestManager.GlobalQuestKind.DungeonHunt:
                    return "Dungeon delve";
                case GlobalKillQuestManager.GlobalQuestKind.HighRiskHunt:
                    return "High-risk hunt";
                case GlobalKillQuestManager.GlobalQuestKind.T8MutatorHunt:
                    return "T8 mutator purge";
                case GlobalKillQuestManager.GlobalQuestKind.T8DungeonHunt:
                    return "T8 dungeon delve";
                case GlobalKillQuestManager.GlobalQuestKind.CardinalTrek:
                    return "Cardinal trek";
                case GlobalKillQuestManager.GlobalQuestKind.VendorDeliveryRace:
                    return "Dereth Express";
                default:
                    return "Hunt";
            }
        }

        private static string FormatTarget(GlobalQuestStatus status)
        {
            if (status.ItemWcid != 0)
                return $"{status.TargetName} ({status.ItemWcid})";
            return status.TargetName;
        }

        private static string FormatProgress(GlobalQuestStatus status)
        {
            if (status.Completed)
                return "Completed";
            if (status.RequiredDistance > 0)
                return $"{status.MyDistance:0.0}/{status.RequiredDistance} clicks";

            if (status.Kind == GlobalKillQuestManager.GlobalQuestKind.ItemRace)
                return "first self-found wins";
            if (status.RequiredTurnIns > 0)
                return $"{status.MyTurnIns}/{status.RequiredTurnIns}";
            if (status.RequiredKills > 0)
                return $"{status.MyKills}/{status.RequiredKills}";
            return "active";
        }

        private static string FormatTime(DateTime expiry)
        {
            var remaining = expiry - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
                return "expired";
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays}d {remaining.Hours:00}h";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m";
            return $"{(int)remaining.TotalMinutes}m {remaining.Seconds:00}s";
        }

        private static string FormatReward(GlobalQuestStatus status)
        {
            if (status.LuminanceReward > 0)
                return $"{status.LuminanceReward:N0} lum";
            if (status.RewardPercent > 0)
                return $"{status.RewardPercent}% level XP";
            return "4x quest-kill XP";
        }
    }
}