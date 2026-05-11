using System;
using System.IO;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Pathfinding;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Admin commands for the DotRecast-backed monster pathfinding system.
    ///
    /// Usage:
    ///   /pathfinding status            - Show whether pathfinding is enabled and how many meshes are cached
    ///   /pathfinding on | off          - Toggle the "pathfinding" server property at runtime
    ///   /pathfinding load              - Build/load the navmesh for the landblock you're standing in
    ///   /pathfinding rebuild           - Delete and rebuild the navmesh for your current landblock
    ///   /pathfinding unload            - Drop the cached navmesh for your current landblock
    ///   /pathfinding list              - List all currently cached navmesh ids
    ///   /pathfinding prebuild          - Start the boot-time prebuild scan now (background)
    ///   /pathfinding prebuild stop     - Cancel an in-progress prebuild scan
    /// </summary>
    public static class PathfindingCommands
    {
        [CommandHandler("pathfinding", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Manage the DotRecast monster pathfinding navmesh system.",
            "status | on | off | load | rebuild | unload | list | prebuild [stop]")]
        public static void HandlePathfinding(Session session, params string[] parameters)
        {
            var sub = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "status";

            switch (sub)
            {
                case "status":
                    HandleStatus(session);
                    break;
                case "on":
                case "enable":
                    HandleToggle(session, true);
                    break;
                case "off":
                case "disable":
                    HandleToggle(session, false);
                    break;
                case "load":
                    HandleLoad(session, rebuild: false);
                    break;
                case "rebuild":
                    HandleLoad(session, rebuild: true);
                    break;
                case "unload":
                    HandleUnload(session);
                    break;
                case "list":
                    HandleList(session);
                    break;
                case "prebuild":
                    HandlePrebuild(session, parameters);
                    break;
                default:
                    Send(session, "Usage: /pathfinding status | on | off | load | rebuild | unload | list | prebuild [stop]");
                    break;
            }
        }

        private static void HandlePrebuild(Session session, string[] parameters)
        {
            if (parameters.Length > 1 && string.Equals(parameters[1], "stop", StringComparison.OrdinalIgnoreCase))
            {
                if (!PathfindingPrebuilder.IsRunning)
                {
                    Send(session, "No pathfinding prebuild is currently running.");
                    return;
                }
                PathfindingPrebuilder.Stop();
                Send(session, "Pathfinding prebuild cancellation requested.");
                return;
            }

            if (PathfindingPrebuilder.IsRunning)
            {
                Send(session, $"Pathfinding prebuild already running: {PathfindingPrebuilder.LandblocksProcessed}/{PathfindingPrebuilder.LandblocksTotal} processed, {PathfindingPrebuilder.LandblocksBuilt} built.");
                return;
            }

            if (PathfindingPrebuilder.Start())
                Send(session, "Pathfinding prebuild started in the background. Use '/pathfinding prebuild' again to check progress.");
            else
                Send(session, "Failed to start pathfinding prebuild.");
        }

        private static void HandleStatus(Session session)
        {
            var enabled = Pathfinder.PathfindingEnabled;
            var meshCount = Pathfinder.Meshes.Count(kvp => kvp.Value != null);
            var pendingCount = Pathfinder.Meshes.Count(kvp => kvp.Value == null);
            Send(session, $"Pathfinding: {(enabled ? "ENABLED" : "DISABLED")}");
            Send(session, $"Cached navmeshes: {meshCount} loaded, {pendingCount} pending");
            Send(session, $"Indoor mesh dir:  {Pathfinder.InsideMeshDirectory}");
            Send(session, $"Outdoor mesh dir: {Pathfinder.OutsideMeshDirectory}");
            if (PathfindingPrebuilder.IsRunning)
                Send(session, $"Prebuild: RUNNING - {PathfindingPrebuilder.LandblocksProcessed}/{PathfindingPrebuilder.LandblocksTotal} ({PathfindingPrebuilder.LandblocksBuilt} built)");
            else if (PathfindingPrebuilder.LandblocksTotal > 0)
                Send(session, $"Prebuild: idle - last run processed {PathfindingPrebuilder.LandblocksProcessed}/{PathfindingPrebuilder.LandblocksTotal} ({PathfindingPrebuilder.LandblocksBuilt} built)");
        }

        private static void HandleToggle(Session session, bool enable)
        {
            PropertyManager.ModifyBool("pathfinding", enable);
            Send(session, $"Pathfinding has been {(enable ? "enabled" : "disabled")}.");
        }

        private static void HandleLoad(Session session, bool rebuild)
        {
            var pos = session?.Player?.Location;
            if (pos == null)
            {
                Send(session, "Could not determine your current position.");
                return;
            }

            Send(session, $"{(rebuild ? "Rebuilding" : "Loading")} navmesh for landblock {pos.Cell >> 16:X4} ({(pos.Indoors ? "indoor" : "outdoor")})...");
            try
            {
                Pathfinder.TryLoadMesh(pos, rebuildMesh: rebuild);
                var meshCount = Pathfinder.Meshes.Count(kvp => kvp.Value != null);
                Send(session, $"Done. Cached navmeshes loaded: {meshCount}");
            }
            catch (Exception ex)
            {
                Send(session, $"Failed to load mesh: {ex.Message}");
            }
        }

        private static void HandleUnload(Session session)
        {
            var pos = session?.Player?.Location;
            if (pos == null)
            {
                Send(session, "Could not determine your current position.");
                return;
            }

            Pathfinder.TryUnloadMesh(pos);
            Send(session, $"Unloaded navmesh(es) for landblock {pos.Cell >> 16:X4}.");
        }

        private static void HandleList(Session session)
        {
            var entries = Pathfinder.Meshes
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"  {kvp.Key:X8}  {(kvp.Value != null ? "loaded" : "pending")}")
                .ToList();

            if (entries.Count == 0)
            {
                Send(session, "No navmeshes cached.");
                return;
            }

            Send(session, $"{entries.Count} cached navmesh entries:");
            foreach (var line in entries)
                Send(session, line);
        }

        private static void Send(Session session, string message)
        {
            if (session?.Network != null)
                session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Broadcast));
        }
    }
}
