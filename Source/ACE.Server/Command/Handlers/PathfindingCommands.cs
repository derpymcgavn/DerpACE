using System;
using System.IO;
using System.IO.Compression;
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
    ///   /pathfinding export <zipPath>  - Zip the current Indoors+Outdoors mesh cache to <zipPath>
    ///   /pathfinding import <zipPath>  - Extract a navmesh pack zip into the current mesh root
    /// </summary>
    public static class PathfindingCommands
    {
        [CommandHandler("pathfinding", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Manage the DotRecast monster pathfinding navmesh system.",
            "status | on | off | load | rebuild | unload | list | prebuild [stop] | export <zipPath> | import <zipPath>")]
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
                case "export":
                    HandleExport(session, parameters);
                    break;
                case "import":
                    HandleImport(session, parameters);
                    break;
                default:
                    Send(session, "Usage: /pathfinding status | on | off | load | rebuild | unload | list | prebuild [stop] | export <zipPath> | import <zipPath>");
                    break;
            }
        }

        private static void HandleExport(Session session, string[] parameters)
        {
            if (parameters.Length < 2 || string.IsNullOrWhiteSpace(parameters[1]))
            {
                Send(session, "Usage: /pathfinding export <zipPath>");
                return;
            }

            var zipPath = parameters[1];
            try
            {
                var indoor = Pathfinder.InsideMeshDirectory;
                var outdoor = Pathfinder.OutsideMeshDirectory;
                var indoorParent = Path.GetDirectoryName(indoor);
                var outdoorParent = Path.GetDirectoryName(outdoor);
                if (!string.Equals(indoorParent, outdoorParent, StringComparison.OrdinalIgnoreCase))
                {
                    Send(session, "Indoor and outdoor mesh dirs do not share a common parent; cannot export.");
                    return;
                }

                var indoorCount = Directory.Exists(indoor) ? Directory.EnumerateFiles(indoor, "*.mesh", SearchOption.AllDirectories).Count() : 0;
                var outdoorCount = Directory.Exists(outdoor) ? Directory.EnumerateFiles(outdoor, "*.mesh", SearchOption.AllDirectories).Count() : 0;
                if (indoorCount + outdoorCount == 0)
                {
                    Send(session, "No mesh files found to export.");
                    return;
                }

                Send(session, $"Exporting {indoorCount + outdoorCount} mesh files ({indoorCount} indoor, {outdoorCount} outdoor) to {zipPath} ...");

                var dir = Path.GetDirectoryName(zipPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    AddDirectoryToZip(zip, indoor, "Indoors");
                    AddDirectoryToZip(zip, outdoor, "Outdoors");
                }

                var info = new FileInfo(zipPath);
                Send(session, $"Export complete: {zipPath} ({info.Length / (1024d * 1024d):F1} MB)");
            }
            catch (Exception ex)
            {
                Send(session, $"Export failed: {ex.Message}");
            }
        }

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryRoot)
        {
            if (!Directory.Exists(sourceDir))
                return;

            foreach (var file in Directory.EnumerateFiles(sourceDir, "*.mesh", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var entryName = entryRoot + "/" + rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }

        private static void HandleImport(Session session, string[] parameters)
        {
            if (parameters.Length < 2 || string.IsNullOrWhiteSpace(parameters[1]))
            {
                Send(session, "Usage: /pathfinding import <zipPath>");
                return;
            }

            var zipPath = parameters[1];
            if (!File.Exists(zipPath))
            {
                Send(session, $"Zip not found: {zipPath}");
                return;
            }

            try
            {
                var indoor = Pathfinder.InsideMeshDirectory;
                var outdoor = Pathfinder.OutsideMeshDirectory;
                var indoorParent = Path.GetDirectoryName(indoor);
                Directory.CreateDirectory(indoor);
                Directory.CreateDirectory(outdoor);

                int extracted = 0;
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        var normalized = entry.FullName.Replace('\\', '/');
                        if (!normalized.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string targetRoot;
                        string relative;
                        if (normalized.StartsWith("Indoors/", StringComparison.OrdinalIgnoreCase))
                        {
                            targetRoot = indoor;
                            relative = normalized.Substring("Indoors/".Length);
                        }
                        else if (normalized.StartsWith("Outdoors/", StringComparison.OrdinalIgnoreCase))
                        {
                            targetRoot = outdoor;
                            relative = normalized.Substring("Outdoors/".Length);
                        }
                        else
                        {
                            continue;
                        }

                        var targetPath = Path.GetFullPath(Path.Combine(targetRoot, relative));
                        var fullRoot = Path.GetFullPath(targetRoot) + Path.DirectorySeparatorChar;
                        if (!targetPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                            continue; // zip-slip guard

                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        entry.ExtractToFile(targetPath, overwrite: true);
                        extracted++;
                    }
                }

                Send(session, $"Import complete: {extracted} mesh file(s) extracted into {indoorParent}.");
            }
            catch (Exception ex)
            {
                Send(session, $"Import failed: {ex.Message}");
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
