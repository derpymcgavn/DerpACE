using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class IronmanCommands
    {
        [CommandHandler("ironmanmode", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 0,
            "Enable or disable Ironman opt-in server-wide.",
            "[ on | off | toggle | status ]")]
        public static void HandleIronmanMode(Session session, params string[] parameters)
        {
            var sub = (parameters != null && parameters.Length > 0)
                ? parameters[0].ToLowerInvariant()
                : "status";

            switch (sub)
            {
                case "on":
                case "enable":
                    DerpACEConfig.IronmanEnabled = true;
                    break;

                case "off":
                case "disable":
                    DerpACEConfig.IronmanEnabled = false;
                    break;

                case "toggle":
                    DerpACEConfig.IronmanEnabled = !DerpACEConfig.IronmanEnabled;
                    break;

                case "status":
                    break;

                default:
                    session.Player.SendMessage("Usage: @ironmanmode [on|off|toggle|status]", ChatMessageType.Broadcast);
                    return;
            }

            var state = DerpACEConfig.IronmanEnabled ? "ENABLED" : "DISABLED";
            session.Player.SendMessage($"Ironman mode is now {state}.", ChatMessageType.Broadcast);
        }

        // GUID -> UTC time at which the pending /ironman on request expires.
        // 30-second confirm window matches the IronmanConfirmationSeconds intent.
        private static readonly ConcurrentDictionary<uint, DateTime> PendingConfirms = new ConcurrentDictionary<uint, DateTime>();
        private const int ConfirmWindowSeconds = 30;

        [CommandHandler("ironman", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Toggle Ironman mode (IRREVERSIBLE).",
            "on        - begin Ironman commitment (you must then run /ironman confirm within 30 seconds)\n" +
            "confirm   - finalize Ironman conversion. Cannot be undone.\n" +
            "char      - view your character progression milestones\n" +
            "top       - show the Ironman leaderboard\n" +
            "topkillers - show top creatures that have killed Ironman players\n\n" +
            "Becoming an Ironman wipes your inventory and spellbook, rerolls your attributes\n" +
            "and skills, marks the character as hardcore (final death = permanent), and bars\n" +
            "you from fellowships, allegiances, external buffs, and using items not flagged\n" +
            "as Ironman items.")]
        public static void HandleIronman(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null) return;

            if (!DerpACEConfig.IronmanEnabled)
            {
                player.SendMessage("Ironman mode is currently disabled on this server.");
                return;
            }

            if (parameters == null || parameters.Length == 0)
            {
                // If the player is already an Ironman, /ironman with no args shows leaderboard commands.
                if (player.GetProperty(PropertyBool.IsIronman) == true)
                {
                    ShowIronmanHelp(player);
                    return;
                }

                player.SendMessage("Usage: /ironman on | confirm");
                return;
            }

            var sub = parameters[0].ToLowerInvariant();

            // Handle read-only commands first (bypass enrollment checks)
            switch (sub)
            {
                case "char":
                    if (player.GetProperty(PropertyBool.IsIronman) == true)
                    {
                        ShowIronmanStatus(player);
                        return;
                    }
                    player.SendMessage("You must be an Ironman to view character progression.");
                    return;

                case "top":
                    ShowIronmanLeaderboard(player);
                    return;

                case "topkillers":
                    HandleIronmanTopKillers(session);
                    return;
            }

            // Show help for Ironman players using other commands
            if (player.GetProperty(PropertyBool.IsIronman) == true)
            {
                ShowIronmanHelp(player);
                return;
            }

            // Hardcore characters cannot also become Ironman.
            if (player.GetProperty(PropertyBool.IsHardcore) == true)
            {
                player.SendMessage("Hardcore characters cannot become Ironman.", ChatMessageType.System);
                return;
            }

            // Only allow commitment at level 10 or below
            if ((player.Level ?? 1) > 10)
            {
                player.SendMessage("Ironman mode is only available to characters at level 10 or below.");
                return;
            }

            switch (sub)
            {

                case "on":
                    PendingConfirms[player.Guid.Full] = DateTime.UtcNow.AddSeconds(ConfirmWindowSeconds);
                    player.SendMessage(
                        $"WARNING: Ironman mode is permanent and will wipe your inventory, spellbook, " +
                        $"and reroll your attributes/skills. Type /ironman confirm within {ConfirmWindowSeconds} seconds to proceed.",
                        ChatMessageType.System);
                    break;

                case "confirm":
                    if (!PendingConfirms.TryRemove(player.Guid.Full, out var expires))
                    {
                        player.SendMessage("You have no pending Ironman commitment. Type /ironman on first.");
                        return;
                    }
                    if (DateTime.UtcNow > expires)
                    {
                        player.SendMessage("Your Ironman commitment window has expired. Type /ironman on again.");
                        return;
                    }
                    IronmanFactory.InitializeIronman(player);
                    
                    // Global announcement for Ironman activation
                    var ironmanMsg = $"[IRONMAN] {player.Name} has taken the Ironman path. There is no turning back!";
                    var ironmanBroadcast = new GameMessageSystemChat(ironmanMsg, ChatMessageType.WorldBroadcast);
                    PlayerManager.BroadcastToAll(ironmanBroadcast);
                    PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, ironmanMsg);
                    break;

                default:
                    player.SendMessage("Usage: /ironman on | confirm");
                    break;
            }
        }

        private static void ShowIronmanHelp(ACE.Server.WorldObjects.Player player)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Ironman Commands ===");
            sb.AppendLine("  /ironman char       - View your character progression milestones");
            sb.AppendLine("  /ironman top        - Show the Ironman leaderboard (top 10 players)");
            sb.AppendLine("  /ironman topkillers - Show top creatures that have killed Ironman players");
            sb.AppendLine();
            sb.AppendLine("Current Status:");
            var lives = player.GetProperty(PropertyInt.HardcoreLives) ?? 0;
            sb.AppendLine($"  Hardcore lives remaining: {lives}");

            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        private static string FormatSkillName(Skill skill, bool isSpecialized = false)
        {
            var name = skill.ToSentence();
            if (isSpecialized)
                name += " [Spec]";
            return name;
        }

        private static void ShowIronmanStatus(ACE.Server.WorldObjects.Player player)
        {
            var lives = player.GetProperty(PropertyInt.HardcoreLives) ?? 0;
            var planStr = player.GetProperty(PropertyString.IronmanPlan) ?? "";
            var lifeMilestones = IronmanFactory.GetHardcoreLifeMilestones();
            var claimedLifeMilestones = IronmanFactory.GetClaimedHardcoreLifeMilestones(player);
            var currentLevel = (int)(player.Level ?? 1);

            var applied      = new List<(Skill skill, string displayName)>();
            var pending      = new SortedDictionary<int, List<(Skill skill, string displayName)>>();
            var notObtainable = new List<(Skill skill, string displayName)>();

            foreach (var entry in planStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;
                if (!System.Enum.TryParse<Skill>(parts[0], out var sk)) continue;
                if (!int.TryParse(parts[1], out var lvl)) continue;

                var currentSkill = player.GetCreatureSkill(sk);
                var isSpecialized = currentSkill != null && currentSkill.AdvancementClass == SkillAdvancementClass.Specialized;
                var displayName = FormatSkillName(sk, isSpecialized);

                if (lvl == 0 || lvl == -1)
                    applied.Add((sk, displayName));
                else if (lvl == -2)
                    notObtainable.Add((sk, FormatSkillName(sk, false)));
                else if (lvl > 0)
                {
                    if (!pending.ContainsKey(lvl))
                        pending[lvl] = new List<(Skill, string)>();
                    pending[lvl].Add((sk, FormatSkillName(sk, false)));
                }
            }

            foreach (var milestone in lifeMilestones)
            {
                if (milestone <= currentLevel || claimedLifeMilestones.Contains(milestone))
                    continue;

                if (!pending.ContainsKey(milestone))
                    pending[milestone] = new List<(Skill, string)>();

                pending[milestone].Add((Skill.None, "+1 Hardcore life (max 3)"));
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Ironman Status ===");
            sb.AppendLine($"  Hardcore lives remaining: {lives}");
            sb.AppendLine();

            if (applied.Count > 0)
            {
                applied.Sort((a, b) => a.displayName.CompareTo(b.displayName));
                sb.AppendLine("  Obtained skills (Level 0):");
                foreach (var (skill, displayName) in applied)
                    sb.AppendLine($"    {displayName}");
                sb.AppendLine();
            }

            if (pending.Count > 0)
            {
                sb.AppendLine("  Upcoming milestones:");
                foreach (var kv in pending)
                {
                    kv.Value.Sort((a, b) => a.displayName.CompareTo(b.displayName));
                    foreach (var (skill, displayName) in kv.Value)
                        sb.AppendLine($"    Level {kv.Key}: {displayName}");
                }
                sb.AppendLine();
            }

            if (notObtainable.Count > 0)
            {
                notObtainable.Sort((a, b) => a.displayName.CompareTo(b.displayName));
                sb.AppendLine("  Not obtainable:");
                foreach (var (skill, displayName) in notObtainable)
                    sb.AppendLine($"    {displayName}");
            }

            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        [CommandHandler("ironmantopkillers", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the top 10 creatures that have killed the most Ironman players.")]
        public static void HandleIronmanTopKillers(Session session, params string[] parameters)
        {
            if (session?.Player == null) return;
            if (!DerpACEConfig.IronmanEnabled)
            {
                session.Player.SendMessage("Ironman mode is currently disabled on this server.");
                return;
            }

            var entries = IronmanKillerTracker.GetTopKillers(10);

            var sb = new StringBuilder();
            sb.AppendLine("=== Top 10 Ironman Killers (Creatures) ===");
            sb.AppendLine($"  {"#",-3} {"Creature",-32} {"IM Kills",8}");
            sb.AppendLine($"  {new string('-', 46)}");

            if (entries.Count == 0)
            {
                sb.AppendLine("  No Ironman player deaths recorded yet.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                    sb.AppendLine($"  {i + 1,-3} {entries[i].Name,-32} {entries[i].Kills,8:N0}");
            }

            session.Player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        private const int LeaderboardSize = 10;

        // ----------------------------------------------------------------
        //  /hardcore command
        // ----------------------------------------------------------------

        private static readonly ConcurrentDictionary<uint, DateTime> PendingHardcoreConfirms = new ConcurrentDictionary<uint, DateTime>();

        [CommandHandler("hardcore", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Toggle Hardcore self-found mode (IRREVERSIBLE).",
            "on      - begin Hardcore commitment (you must then run /hardcore confirm within 30 seconds)\n" +
            "confirm - finalize Hardcore conversion. Cannot be undone.\n\n" +
            "Hardcore characters have 1 life. On death, the character is permanently deleted.\n" +
            "Your radar blip will appear pink. No other restrictions apply.")]
        public static void HandleHardcore(Session session, params string[] parameters)
        {
            var player = session?.Player;
            if (player == null) return;

            // Already hardcore — show status regardless of sub-command.
            if (player.GetProperty(PropertyBool.IsHardcore) == true)
            {
                // Only show the status message if triggered directly; Ironman already
                // shows lives in ShowIronmanStatus, so skip duplicate output there.
                if (player.GetProperty(PropertyBool.IsIronman) != true)
                {
                    var lives = player.GetProperty(PropertyInt.HardcoreLives) ?? 0;
                    player.SendMessage(
                        $"Hardcore lives remaining: {lives}\n" +
                        (lives <= 0 ? "Your character is pending deletion." : ""),
                        ChatMessageType.System);
                }
                else
                {
                    player.SendMessage("You are an Ironman — use /ironman for your status.", ChatMessageType.System);
                }
                return;
            }

            // Ironman characters cannot also become standalone Hardcore.
            if (player.GetProperty(PropertyBool.IsIronman) == true)
            {
                player.SendMessage("Ironman characters cannot become Hardcore.", ChatMessageType.System);
                return;
            }

            if (parameters == null || parameters.Length == 0)
            {
                player.SendMessage("Usage: /hardcore on | confirm", ChatMessageType.System);
                return;
            }

            var sub = parameters[0].ToLowerInvariant();

            switch (sub)
            {
                case "on":
                    PendingHardcoreConfirms[player.Guid.Full] = DateTime.UtcNow.AddSeconds(ConfirmWindowSeconds);
                    player.SendMessage(
                        $"WARNING: Hardcore mode is permanent. You will have 1 life — death deletes your character forever.\n" +
                        $"Type /hardcore confirm within {ConfirmWindowSeconds} seconds to proceed.",
                        ChatMessageType.System);
                    break;

                case "confirm":
                    if (!PendingHardcoreConfirms.TryRemove(player.Guid.Full, out var expires))
                    {
                        player.SendMessage("No pending Hardcore commitment. Type /hardcore on first.", ChatMessageType.System);
                        return;
                    }
                    if (DateTime.UtcNow > expires)
                    {
                        player.SendMessage("Your Hardcore commitment window expired. Type /hardcore on again.", ChatMessageType.System);
                        return;
                    }
                    ApplyHardcoreStandalone(player);
                    
                    // Global announcement for Hardcore activation
                    var hardcoreMsg = $"[HARDCORE] {player.Name} has entered Hardcore mode. One life remains.";
                    var hardcoreBroadcast = new GameMessageSystemChat(hardcoreMsg, ChatMessageType.WorldBroadcast);
                    PlayerManager.BroadcastToAll(hardcoreBroadcast);
                    PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, hardcoreMsg);
                    break;

                default:
                    player.SendMessage("Usage: /hardcore on | confirm", ChatMessageType.System);
                    break;
            }
        }

        private static void ApplyHardcoreStandalone(ACE.Server.WorldObjects.Player player)
        {
            player.SetProperty(PropertyBool.IsHardcore, true);
            player.SetProperty(PropertyInt.HardcoreLives, 1);
            player.SetModeTitle("HARDCORE");

            // Pink radar blip — visible to all nearby players.
            player.RadarColor = RadarColor.Pink;
            player.EnqueueBroadcast(true,
                new GameMessagePublicUpdatePropertyInt(player, PropertyInt.RadarBlipColor, (int)RadarColor.Pink));

            player.SendMessage(
                "You have entered Hardcore mode. You have 1 life. Good luck.",
                ChatMessageType.System);
        }

        [CommandHandler("ironmantop", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Show the Ironman leaderboard (top players by mob kills).")]
        public static void HandleIronmanTop(Session session, params string[] parameters)
        {
            if (session?.Player == null) return;
            if (!DerpACEConfig.IronmanEnabled)
            {
                session.Player.SendMessage("Ironman mode is currently disabled on this server.");
                return;
            }
            ShowIronmanLeaderboard(session.Player);
        }

        private static void ShowIronmanLeaderboard(ACE.Server.WorldObjects.Player viewer)
        {
            var entries = PlayerManager.GetAllPlayers()
                .Where(p => !p.IsDeleted && p.GetProperty(PropertyBool.IsIronman) == true)
                .Select(p => (
                    Name:   p.Name,
                    Level:  p.Level ?? 0,
                    Kills:  p.GetProperty(PropertyInt.CreatureKills) ?? 0
                ))
                .OrderByDescending(e => e.Kills)
                .ThenByDescending(e => e.Level)
                .Take(LeaderboardSize)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== Ironman Leaderboard (Top {LeaderboardSize}) ===");
            sb.AppendLine($"  {"#",-3} {"Name",-28} {"Level",5}  {"Kills",7}");
            sb.AppendLine($"  {new string('-', 48)}");

            if (entries.Count == 0)
            {
                sb.AppendLine("  No Ironman players found.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    sb.AppendLine($"  {i + 1,-3} {e.Name,-28} {e.Level,5}  {e.Kills,7:N0}");
                }
            }

            viewer.SendMessage(sb.ToString(), ChatMessageType.System);
        }
    }
}
