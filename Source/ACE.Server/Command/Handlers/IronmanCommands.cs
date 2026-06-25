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
        // GUID -> pending mode ("standard" or "nomad"). Defaults to standard if missing.
        private static readonly ConcurrentDictionary<uint, string> PendingModes = new ConcurrentDictionary<uint, string>();
        // GUID -> whether the pending commitment requested the -nh (no non-humans) restricted race pool.
        private static readonly ConcurrentDictionary<uint, bool> PendingNoNonHuman = new ConcurrentDictionary<uint, bool>();
        // GUID -> whether the pending commitment requested blind progression.
        private static readonly ConcurrentDictionary<uint, bool> PendingBlind = new ConcurrentDictionary<uint, bool>();
        private const int ConfirmWindowSeconds = 30;

        [CommandHandler("ironman", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Toggle Ironman mode (IRREVERSIBLE).",
            "on        - begin Ironman commitment (you must then run /ironman confirm within 30 seconds)\n" +
            "nomad     - begin NOMAD Ironman commitment (no weapons or casters; gauntlet/shoe damage; natural AL 420 with above-average protections while unarmored)\n" +
            "  add -nh to 'on' or 'nomad' to exclude non-human heritages, rolling only\n" +
            "            Aluvian, Gharundim, Sho, Viamontian, Umbraen, Penumbraen, Undead, or Empyrean\n" +
            "  add -blind to hide future skill milestones and auto-spend XP into skills, vitals, and attributes as the build grows\n" +
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

                player.SendMessage("Usage: /ironman on [-nh] [-blind] | nomad [-nh] [-blind] | confirm");
                return;
            }

            var sub = parameters[0].ToLowerInvariant();

            // DerpACE: detect the -nh (no non-humans) toggle anywhere in the remaining args.
            var noNonHuman = parameters.Skip(1).Any(p =>
                string.Equals(p, "-nh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "nh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "nonhuman", StringComparison.OrdinalIgnoreCase));

            var blind = parameters.Skip(1).Any(p =>
                string.Equals(p, "-blind", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "blind", StringComparison.OrdinalIgnoreCase));

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
                    PendingModes[player.Guid.Full] = "standard";
                    PendingNoNonHuman[player.Guid.Full] = noNonHuman;
                    PendingBlind[player.Guid.Full] = blind;
                    player.SendMessage(
                        $"WARNING: Ironman mode is permanent and will wipe your inventory, spellbook, " +
                        $"and reroll your attributes/skills.{(noNonHuman ? " Heritage will exclude non-humans." : "")}" +
                        $"{(blind ? " Blind progression will hide future skill milestones and auto-spend XP into skills, vitals, and attributes." : "")} " +
                        $"Type /ironman confirm within {ConfirmWindowSeconds} seconds to proceed.",
                        ChatMessageType.System);
                    break;

                case "nomad":
                    PendingConfirms[player.Guid.Full] = DateTime.UtcNow.AddSeconds(ConfirmWindowSeconds);
                    PendingModes[player.Guid.Full] = "nomad";
                    PendingNoNonHuman[player.Guid.Full] = noNonHuman;
                    PendingBlind[player.Guid.Full] = blind;
                    player.SendMessage(
                        $"WARNING: Ironman NOMAD mode is permanent. You will not be able to wield weapons or casters. " +
                        $"You will train Light Weapons and Arcane Lore (specialized), your attributes will roll at random, " +
                        $"and your damage will come from elemental gauntlets and shoes. Without armor you have a natural " +
                        $"AL of 450 (average); worn armor is only half effective.{(noNonHuman ? " Heritage will exclude non-humans." : "")}" +
                        $"{(blind ? " Blind progression will hide future skill milestones and auto-spend XP into skills, vitals, and attributes." : "")} " +
                        $"Type /ironman confirm within {ConfirmWindowSeconds} seconds to proceed.",
                        ChatMessageType.System);
                    break;

                case "confirm":
                    if (!PendingConfirms.TryRemove(player.Guid.Full, out var expires))
                    {
                        PendingNoNonHuman.TryRemove(player.Guid.Full, out _);
                        PendingBlind.TryRemove(player.Guid.Full, out _);
                        player.SendMessage("You have no pending Ironman commitment. Type /ironman on or /ironman nomad first.");
                        return;
                    }
                    if (DateTime.UtcNow > expires)
                    {
                        PendingModes.TryRemove(player.Guid.Full, out _);
                        PendingNoNonHuman.TryRemove(player.Guid.Full, out _);
                        PendingBlind.TryRemove(player.Guid.Full, out _);
                        player.SendMessage("Your Ironman commitment window has expired. Type /ironman on or /ironman nomad again.");
                        return;
                    }
                    PendingModes.TryRemove(player.Guid.Full, out var pendingMode);
                    PendingNoNonHuman.TryRemove(player.Guid.Full, out var pendingNoNonHuman);
                    PendingBlind.TryRemove(player.Guid.Full, out var pendingBlind);
                    var isNomad = string.Equals(pendingMode, "nomad", StringComparison.OrdinalIgnoreCase);

                    if (isNomad)
                        IronmanFactory.InitializeIronmanNomad(player, pendingNoNonHuman, pendingBlind);
                    else
                        IronmanFactory.InitializeIronman(player, pendingNoNonHuman, pendingBlind);

                    // Global announcement for Ironman activation
                    var pathLabel = isNomad ? "NOMAD Ironman" : "Ironman";
                    var ironmanMsg = $"[IRONMAN] {player.Name} has taken the {pathLabel} path. There is no turning back!";
                    var ironmanBroadcast = new GameMessageSystemChat(ironmanMsg, ChatMessageType.WorldBroadcast);
                    PlayerManager.BroadcastToAll(ironmanBroadcast);
                    PlayerManager.LogBroadcastChat(Channel.AllBroadcast, player, ironmanMsg);
                    break;

                default:
                    player.SendMessage("Usage: /ironman on [-nh] [-blind] | nomad [-nh] [-blind] | confirm");
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
            var isBlind = player.GetProperty(PropertyBool.IsIronmanBlind) == true;
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
            if (isBlind)
                sb.AppendLine("  Blind progression: ON");
            sb.AppendLine();

            if (applied.Count > 0)
            {
                applied.Sort((a, b) => a.displayName.CompareTo(b.displayName));
                sb.AppendLine("  Obtained skills (Level 0):");
                foreach (var (skill, displayName) in applied)
                    sb.AppendLine($"    {displayName}");
                sb.AppendLine();
            }

            if (!isBlind && pending.Count > 0)
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

            if (!isBlind && notObtainable.Count > 0)
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

            if (entries.Count == 0)
            {
                sb.AppendLine("  No Ironman player deaths recorded yet.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                    sb.AppendLine($"  {i + 1,2}. {entries[i].Name} - {entries[i].Kills:N0} IM kills");
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

            // Already hardcore - show status regardless of sub-command.
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
                    player.SendMessage("You are an Ironman - use /ironman for your status.", ChatMessageType.System);
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
                        $"WARNING: Hardcore mode is permanent. You will have 1 life - death deletes your character forever.\n" +
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

            // Pink radar blip - visible to all nearby players.
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
            var entries = LeaderboardCache.GetIronman().Take(LeaderboardSize).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== Ironman Leaderboard (Top {LeaderboardSize}) ===");

            if (entries.Count == 0)
            {
                sb.AppendLine("  No Ironman players found.");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var status = e.Lives <= 0 ? "DEAD" : (e.IsNomad ? "NOMAD" : "ALIVE");
                    // AC's chat font is proportional, so column padding never aligns.
                    // Use a separator-based line that reads cleanly at any name length.
                    sb.AppendLine($"  {i + 1,2}. {e.Name} - Lv {e.Level} | {e.Kills:N0} kills | {e.Lives} life(s) | {status}");
                }
            }

            viewer.SendMessage(sb.ToString(), ChatMessageType.System);
        }
    }
}
