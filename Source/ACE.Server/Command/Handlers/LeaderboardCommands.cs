using System.Linq;
using System.Text;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// DerpACE: public leaderboard commands for non-Ironman categories
    /// (general player base, Hardcore players, and per-category "deadliest mobs").
    /// </summary>
    public static class LeaderboardCommands
    {
        private const int LeaderboardSize = 10;

        // ----------------------------------------------------------------
        //  /hardcoretop — top Hardcore players by mob kills
        // ----------------------------------------------------------------

        [CommandHandler("hardcoretop", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the Hardcore leaderboard (top players by mob kills).")]
        public static void HandleHardcoreTop(Session session, params string[] parameters)
        {
            if (session?.Player == null) return;

            var entries = LeaderboardCache.GetHardcore().Take(LeaderboardSize).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== Hardcore Leaderboard (Top {LeaderboardSize}) ===");

            if (entries.Count == 0)
            {
                sb.AppendLine("  No Hardcore players found.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var status = e.Lives <= 0 ? "DEAD" : "ALIVE";
                    sb.AppendLine($"  {i + 1,2}. {e.Name} - Lv {e.Level} | {e.Kills:N0} kills | {e.Lives} life(s) | {status}");
                }
            }

            session.Player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        // ----------------------------------------------------------------
        //  /topkillers — top creatures that have killed the most NORMAL players
        // ----------------------------------------------------------------

        [CommandHandler("topkillers", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the top 10 creatures that have killed the most (non-Ironman, non-Hardcore) players.")]
        public static void HandleTopKillers(Session session, params string[] parameters)
        {
            if (session?.Player == null) return;
            RenderKillerLeaderboard(session, PlayerKillerTracker.Category.Normal, "Top 10 Deadliest Mobs (Normal Players)");
        }

        // ----------------------------------------------------------------
        //  /hardcoretopkillers — top creatures that have killed Hardcore players
        // ----------------------------------------------------------------

        [CommandHandler("hardcoretopkillers", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the top 10 creatures that have killed the most Hardcore players.")]
        public static void HandleHardcoreTopKillers(Session session, params string[] parameters)
        {
            if (session?.Player == null) return;
            RenderKillerLeaderboard(session, PlayerKillerTracker.Category.Hardcore, "Top 10 Deadliest Mobs (Hardcore Players)");
        }

        // ----------------------------------------------------------------

        private static void RenderKillerLeaderboard(Session session, PlayerKillerTracker.Category category, string title)
        {
            var entries = LeaderboardCache.GetDeadliest(category).Take(LeaderboardSize).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");

            if (entries.Count == 0)
            {
                sb.AppendLine("  No player deaths recorded yet.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                    sb.AppendLine($"  {i + 1,2}. {entries[i].Name} - {entries[i].Kills:N0} player kills");
            }

            session.Player.SendMessage(sb.ToString(), ChatMessageType.System);
        }
    }
}
