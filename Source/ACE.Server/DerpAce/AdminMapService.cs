using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using log4net;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Pathfinding.Geometry;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.DerpAce
{
    public static class AdminMapService
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static HttpListener listener;
        private static CancellationTokenSource cancelSource;
        private static readonly ConcurrentDictionary<uint, AdminDungeonMap> DungeonMapCache = new ConcurrentDictionary<uint, AdminDungeonMap>();
        private const float CreatureBlipRadius = 80.0f;
        private const int MaxCreatureBlips = 200;

        public static void Start()
        {
            var config = DerpAceConfigManager.Config;

            if (!config.AdminMapEnabled)
                return;

            if (listener != null)
                return;

            var host = string.IsNullOrWhiteSpace(config.AdminMapHost) ? "127.0.0.1" : config.AdminMapHost.Trim();
            var port = Math.Clamp(config.AdminMapPort, 1, 65535);
            var prefix = $"http://{host}:{port}/";

            try
            {
                cancelSource = new CancellationTokenSource();
                listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _ = Task.Run(() => ListenLoop(cancelSource.Token));

                log.Info($"[DerpACE AdminMap] Listening on {prefix}");
            }
            catch (Exception ex)
            {
                log.Error($"[DerpACE AdminMap] Failed to start on {prefix}: {ex}");
                Stop();
            }
        }

        public static void Stop()
        {
            try
            {
                cancelSource?.Cancel();
                listener?.Stop();
                listener?.Close();
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Error while stopping: {ex.Message}");
            }
            finally
            {
                listener = null;
                cancelSource?.Dispose();
                cancelSource = null;
            }
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener?.IsListening == true)
            {
                HttpListenerContext context = null;

                try
                {
                    context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), token);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.Warn($"[DerpACE AdminMap] Request loop error: {ex}");
                    CloseQuietly(context);
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";

                if (path.Length == 0 || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                {
                    WriteText(context, BuildIndexHtml(), "text/html; charset=utf-8");
                    return;
                }

                if (path.Equals("/api/players", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Missing or invalid admin map token." });
                        return;
                    }

                    WriteJson(context, BuildPlayerSnapshot());
                    return;
                }

                if (path.Equals("/api/dungeon", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Missing or invalid admin map token." });
                        return;
                    }

                    if (!TryGetLandblock(context.Request.QueryString["landblock"], out var landblock))
                    {
                        context.Response.StatusCode = 400;
                        WriteJson(context, new { error = "Missing or invalid landblock." });
                        return;
                    }

                    WriteJson(context, BuildDungeonSnapshot(landblock));
                    return;
                }

                if (path.Equals("/assets/dereth-map", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteText(context, "Missing or invalid admin map token.", "text/plain; charset=utf-8");
                        return;
                    }

                    if (!TryWriteMapImage(context))
                    {
                        context.Response.StatusCode = 404;
                        WriteText(context, "Dereth map image not found.", "text/plain; charset=utf-8");
                    }
                    return;
                }

                context.Response.StatusCode = 404;
                WriteText(context, "Not found", "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Request failed: {ex}");
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                    WriteJson(context, new { error = "Admin map request failed." });
                }
            }
        }

        private static bool IsAuthorized(HttpListenerContext context)
        {
            var token = DerpAceConfigManager.Config.AdminMapToken;

            if (string.IsNullOrWhiteSpace(token))
                return IsLocalRequest(context.Request.RemoteEndPoint?.Address);

            var provided = context.Request.Headers["X-DerpACE-Map-Token"];
            if (string.IsNullOrWhiteSpace(provided))
                provided = context.Request.QueryString["token"];

            return string.Equals(provided, token, StringComparison.Ordinal);
        }

        private static bool IsLocalRequest(IPAddress address)
        {
            return address != null && IPAddress.IsLoopback(address);
        }

        private static AdminMapSnapshot BuildPlayerSnapshot()
        {
            var config = DerpAceConfigManager.Config;
            var visiblePlayers = new List<Player>();
            var players = new List<AdminMapPlayer>();

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null)
                    continue;

                if (!config.AdminMapShowAdmins && (player.IsAdmin || player.IsSentinel || player.IsEnvoy || player.IsArch || player.IsPsr))
                    continue;

                visiblePlayers.Add(player);
                players.Add(BuildPlayer(player));
            }

            return new AdminMapSnapshot
            {
                ServerTimeUtc = DateTime.UtcNow,
                RefreshSeconds = Math.Max(1, config.AdminMapRefreshSeconds),
                OnlineCount = players.Count,
                MapImageUrl = HasMapImage() ? "/assets/dereth-map" : null,
                MapBounds = new AdminMapBounds
                {
                    Left = ClampPercent(config.AdminMapBoundsLeftPct),
                    Top = ClampPercent(config.AdminMapBoundsTopPct),
                    Right = ClampPercent(config.AdminMapBoundsRightPct),
                    Bottom = ClampPercent(config.AdminMapBoundsBottomPct)
                },
                Players = players,
                Blips = BuildNearbyMapBlips(visiblePlayers, false, 0)
            };
        }

        private static AdminDungeonSnapshot BuildDungeonSnapshot(uint landblock)
        {
            var config = DerpAceConfigManager.Config;
            var map = DungeonMapCache.GetOrAdd(landblock & 0xFFFF0000, BuildDungeonMap);
            var visiblePlayers = new List<Player>();
            var players = new List<AdminDungeonPlayer>();

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null)
                    continue;

                if ((player.Location.Cell & 0xFFFF0000) != (landblock & 0xFFFF0000))
                    continue;

                if (!config.AdminMapShowAdmins && (player.IsAdmin || player.IsSentinel || player.IsEnvoy || player.IsArch || player.IsPsr))
                    continue;

                visiblePlayers.Add(player);
                players.Add(BuildDungeonPlayer(player));
            }

            return new AdminDungeonSnapshot
            {
                Landblock = $"0x{landblock & 0xFFFF0000:X8}",
                Generated = map.Generated,
                Error = map.Error,
                MinX = map.MinX,
                MinY = map.MinY,
                MaxX = map.MaxX,
                MaxY = map.MaxY,
                MinZ = map.MinZ,
                MaxZ = map.MaxZ,
                Svg = map.Svg,
                Players = players,
                Blips = BuildNearbyMapBlips(visiblePlayers, true, landblock)
            };
        }

        private static AdminDungeonMap BuildDungeonMap(uint landblock)
        {
            try
            {
                var geometry = new LandblockGeometry(landblock);
                var cells = geometry.DungeonCells.Values.Where(c => c.HasWalkablePolys).ToList();

                if (cells.Count == 0)
                    return AdminDungeonMap.Fail("No dungeon geometry found for this landblock.");

                var exporter = new LandblockGeometryExporter(geometry, cells);
                exporter.LoadLandblockInfo();

                if (exporter.Vertices.Count == 0 || exporter.Polygons.Count == 0)
                    return AdminDungeonMap.Fail("Dungeon geometry produced no drawable polygons.");

                var points = exporter.Vertices;
                var minX = points.Min(v => v.X);
                var maxX = points.Max(v => v.X);
                var minY = points.Min(v => v.Z);
                var maxY = points.Max(v => v.Z);
                var minZ = points.Min(v => v.Y);
                var maxZ = points.Max(v => v.Y);
                var paddedMinX = minX - 8;
                var paddedMaxX = maxX + 8;
                var paddedMinY = minY - 8;
                var paddedMaxY = maxY + 8;
                var width = Math.Max(1.0f, paddedMaxX - paddedMinX);
                var height = Math.Max(1.0f, paddedMaxY - paddedMinY);

                var svg = new StringBuilder();
                svg.Append(CultureInfo.InvariantCulture,
                    $"<svg viewBox=\"{paddedMinX:0.###} {-paddedMaxY:0.###} {width:0.###} {height:0.###}\" preserveAspectRatio=\"none\" xmlns=\"http://www.w3.org/2000/svg\">");
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect x=\"{paddedMinX:0.###}\" y=\"{-paddedMaxY:0.###}\" width=\"{width:0.###}\" height=\"{height:0.###}\" fill=\"#0c1114\"/>");
                svg.Append("<g stroke=\"#6da59f\" stroke-width=\"0.35\" opacity=\"0.94\">");

                foreach (var poly in exporter.Polygons)
                {
                    if (poly.Count < 3)
                        continue;

                    var validVertices = poly
                        .Where(index => index > 0 && index <= exporter.Vertices.Count)
                        .Select(index => exporter.Vertices[index - 1])
                        .ToList();

                    if (validVertices.Count < 3)
                        continue;

                    var avgZ = validVertices.Average(v => v.Y);
                    svg.Append(CultureInfo.InvariantCulture, $"<polygon fill=\"{GetDepthFill(avgZ, minZ, maxZ)}\" points=\"");
                    foreach (var vertex in validVertices)
                    {
                        svg.Append(CultureInfo.InvariantCulture, $"{vertex.X:0.###},{-vertex.Z:0.###} ");
                    }
                    svg.Append("\"/>");
                }

                svg.Append("</g></svg>");

                return new AdminDungeonMap
                {
                    Generated = true,
                    Svg = svg.ToString(),
                    MinX = paddedMinX,
                    MaxX = paddedMaxX,
                    MinY = paddedMinY,
                    MaxY = paddedMaxY,
                    MinZ = minZ,
                    MaxZ = maxZ
                };
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Failed to build dungeon map for 0x{landblock & 0xFFFF0000:X8}: {ex}");
                return AdminDungeonMap.Fail("Dungeon map generation failed.");
            }
        }

        private static AdminDungeonPlayer BuildDungeonPlayer(Player player)
        {
            var loc = player.Location;

            return new AdminDungeonPlayer
            {
                Name = player.Name,
                Guid = $"0x{player.Guid.Full:X8}",
                Cell = $"0x{loc.Cell:X8}",
                Loc = loc.ToLOCString(),
                X = loc.PositionX,
                Y = loc.PositionY,
                Z = loc.PositionZ,
                Heading = GetHeadingDegrees(loc),
                Health = player.Health?.Current ?? 0,
                MaxHealth = player.Health?.MaxValue ?? 0,
                Stamina = player.Stamina?.Current ?? 0,
                MaxStamina = player.Stamina?.MaxValue ?? 0,
                Mana = player.Mana?.Current ?? 0,
                MaxMana = player.Mana?.MaxValue ?? 0
            };
        }

        private static string GetDepthFill(double z, double minZ, double maxZ)
        {
            if (Math.Abs(maxZ - minZ) < 0.001)
                return "#264348";

            var t = Math.Clamp((z - minZ) / (maxZ - minZ), 0.0, 1.0);

            if (t < 0.5)
                return LerpColor("#1d2f52", "#28544f", t * 2.0);

            return LerpColor("#28544f", "#766a3a", (t - 0.5) * 2.0);
        }

        private static string LerpColor(string from, string to, double amount)
        {
            amount = Math.Clamp(amount, 0.0, 1.0);
            var r1 = Convert.ToInt32(from.Substring(1, 2), 16);
            var g1 = Convert.ToInt32(from.Substring(3, 2), 16);
            var b1 = Convert.ToInt32(from.Substring(5, 2), 16);
            var r2 = Convert.ToInt32(to.Substring(1, 2), 16);
            var g2 = Convert.ToInt32(to.Substring(3, 2), 16);
            var b2 = Convert.ToInt32(to.Substring(5, 2), 16);
            var r = (int)Math.Round(r1 + (r2 - r1) * amount);
            var g = (int)Math.Round(g1 + (g2 - g1) * amount);
            var b = (int)Math.Round(b1 + (b2 - b1) * amount);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static List<AdminMapBlip> BuildNearbyMapBlips(List<Player> players, bool dungeon, uint landblock)
        {
            var blips = new List<AdminMapBlip>();
            var seen = new HashSet<uint>();
            var radiusSq = CreatureBlipRadius * CreatureBlipRadius;
            var normalizedLandblock = landblock & 0xFFFF0000;

            foreach (var player in players)
            {
                if (player?.Location == null || player.CurrentLandblock == null)
                    continue;

                foreach (var worldObject in player.CurrentLandblock.GetAllWorldObjectsForDiagnostics())
                {
                    if (worldObject == null || worldObject == player || worldObject is Player || worldObject.Location == null)
                        continue;

                    if (!TryGetMapBlipKind(worldObject, out var kind, out var radarColor))
                        continue;

                    if (worldObject is Creature creature && (!creature.IsAlive || creature.Teleporting))
                        continue;

                    if (dungeon)
                    {
                        if ((worldObject.Location.Cell & 0xFFFF0000) != normalizedLandblock)
                            continue;
                    }
                    else if (worldObject.Location.Indoors)
                        continue;

                    if (player.Location.SquaredDistanceTo(worldObject.Location) > radiusSq)
                        continue;

                    if (!seen.Add(worldObject.Guid.Full))
                        continue;

                    blips.Add(BuildMapBlip(worldObject, kind, radarColor));

                    if (blips.Count >= MaxCreatureBlips)
                        return blips;
                }
            }

            return blips;
        }

        private static bool TryGetMapBlipKind(WorldObject worldObject, out string kind, out RadarColor radarColor)
        {
            kind = null;
            radarColor = RadarColor.Default;

            switch (worldObject.WeenieType)
            {
                case WeenieType.Portal:
                case WeenieType.HousePortal:
                    kind = "portal";
                    radarColor = RadarColor.Portal;
                    return true;

                case WeenieType.LifeStone:
                    kind = "lifestone";
                    radarColor = RadarColor.LifeStone;
                    return true;

                case WeenieType.LightSource:
                    kind = "light";
                    radarColor = RadarColor.Gold;
                    return true;

                case WeenieType.Door:
                    kind = "door";
                    radarColor = RadarColor.Default;
                    return true;
            }

            if (worldObject is Vendor)
            {
                kind = "vendor";
                radarColor = RadarColor.Vendor;
                return true;
            }

            if (worldObject is Creature creature)
            {
                kind = creature.IsMonster ? "creature" : "npc";
                radarColor = creature.IsMonster ? RadarColor.Creature : RadarColor.NPC;
                return true;
            }

            return false;
        }

        private static AdminMapBlip BuildMapBlip(WorldObject worldObject, string kind, RadarColor radarColor)
        {
            var loc = worldObject.Location;
            var mapCoords = loc.GetMapCoords();

            return new AdminMapBlip
            {
                Name = worldObject.Name,
                Guid = $"0x{worldObject.Guid.Full:X8}",
                Cell = $"0x{loc.Cell:X8}",
                Landblock = loc.LandblockId.ToString(),
                Loc = loc.ToLOCString(),
                Kind = kind,
                RadarColor = radarColor.ToString(),
                IsMonster = worldObject is Creature creature && creature.IsMonster,
                MapX = mapCoords?.X,
                MapY = mapCoords?.Y,
                X = loc.PositionX,
                Y = loc.PositionY,
                Z = loc.PositionZ
            };
        }

        private static bool TryGetLandblock(string value, out uint landblock)
        {
            landblock = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);

            if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
                && !uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return false;

            landblock = parsed & 0xFFFF0000;
            return landblock != 0;
        }

        private static bool TryWriteMapImage(HttpListenerContext context)
        {
            var path = ResolveMapImagePath();
            if (path == null || !File.Exists(path))
                return false;

            var bytes = File.ReadAllBytes(path);
            context.Response.ContentType = GetImageContentType(path);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
            return true;
        }

        private static bool HasMapImage()
        {
            var path = ResolveMapImagePath();
            return path != null && File.Exists(path);
        }

        private static string ResolveMapImagePath()
        {
            var path = DerpAceConfigManager.Config.AdminMapImagePath;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        }

        private static string GetImageContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                    return "image/png";
                case ".webp":
                    return "image/webp";
                case ".gif":
                    return "image/gif";
                default:
                    return "image/jpeg";
            }
        }

        private static float ClampPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;

            return Math.Clamp(value, 0, 100);
        }

        private static AdminMapPlayer BuildPlayer(Player player)
        {
            var loc = player.Location;
            var mapCoords = loc.GetMapCoords();

            return new AdminMapPlayer
            {
                Name = player.Name,
                Guid = $"0x{player.Guid.Full:X8}",
                Landblock = loc.LandblockId.ToString(),
                Loc = loc.ToLOCString(),
                IsIndoors = loc.Indoors,
                MapX = mapCoords?.X,
                MapY = mapCoords?.Y,
                WorldX = loc.LandblockId.LandblockX * Position.BlockLength + loc.PositionX,
                WorldY = loc.LandblockId.LandblockY * Position.BlockLength + loc.PositionY,
                Z = loc.PositionZ,
                Heading = GetHeadingDegrees(loc),
                Health = player.Health?.Current ?? 0,
                MaxHealth = player.Health?.MaxValue ?? 0,
                Stamina = player.Stamina?.Current ?? 0,
                MaxStamina = player.Stamina?.MaxValue ?? 0,
                Mana = player.Mana?.Current ?? 0,
                MaxMana = player.Mana?.MaxValue ?? 0
            };
        }

        private static double GetHeadingDegrees(Position loc)
        {
            var dir = loc.GetCurrentDir();
            var radians = Math.Atan2(dir.X, dir.Y);
            var degrees = radians * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        private static void WriteJson(HttpListenerContext context, object payload)
        {
            WriteText(context, JsonSerializer.Serialize(payload, JsonOptions), "application/json; charset=utf-8");
        }

        private static void WriteText(HttpListenerContext context, string text, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static void CloseQuietly(HttpListenerContext context)
        {
            try
            {
                context?.Response?.OutputStream?.Close();
            }
            catch
            {
            }
        }

        private static string BuildIndexHtml()
        {
            var refresh = Math.Max(1, DerpAceConfigManager.Config.AdminMapRefreshSeconds);

            return $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>DerpACE Admin Map</title>
<style>
:root {{ color-scheme: dark; font-family: Segoe UI, Arial, sans-serif; background: #101314; color: #e8ece8; }}
* {{ box-sizing: border-box; }}
body {{ margin: 0; display: grid; grid-template-columns: minmax(320px, 1fr) 360px; min-height: 100vh; background: #101314; }}
#map {{ position: relative; overflow: hidden; min-height: 100vh; background:
    linear-gradient(rgba(255,255,255,.055) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255,255,255,.055) 1px, transparent 1px),
    radial-gradient(circle at 50% 50%, #2d4734 0, #1c3429 38%, #243237 64%, #172025 100%);
  background-size: 7.142857% 7.142857%, 7.142857% 7.142857%, 100% 100%; }}
#map::before {{ content: ""Dereth""; position: absolute; inset: 0; display: grid; place-items: center; color: rgba(255,255,255,.08); font-size: clamp(54px, 11vw, 160px); letter-spacing: 0; pointer-events: none; }}
#map.hasImage::before, #map.dungeonMode::before, #map.hasLayer::before {{ content: """"; }}
.axis {{ position: absolute; color: rgba(255,255,255,.44); font-size: 12px; user-select: none; }}
.north {{ top: 12px; left: 50%; transform: translateX(-50%); }}
.south {{ bottom: 12px; left: 50%; transform: translateX(-50%); }}
.west {{ left: 12px; top: 50%; transform: translateY(-50%); }}
.east {{ right: 12px; top: 50%; transform: translateY(-50%); }}
.mapLayer {{ position: absolute; inset: 0; transform-origin: 0 0; }}
.worldMapImage {{ position: absolute; inset: 0; background-color: #8fa0a8; background-position: center; background-repeat: no-repeat; background-size: 100% 100%; }}
.pin {{ position: absolute; z-index: 2; width: 13px; height: 13px; margin: -6px 0 0 -6px; border: 2px solid #ffffff; border-radius: 50%; background: #f5f7f0; box-shadow: 0 0 0 3px rgba(255,255,255,.18), 0 0 16px rgba(130,220,255,.72); cursor: pointer; }}
.pin::after {{ content: attr(data-name); position: absolute; left: 15px; top: -7px; max-width: 180px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 3px 6px; border-radius: 4px; background: rgba(8,12,14,.78); color: #fff; font-size: 12px; }}
.pin.indoor {{ background: #ffffff; box-shadow: 0 0 0 3px rgba(117,167,255,.24), 0 0 16px rgba(117,167,255,.82); }}
.dungeonSvg {{ position: absolute; inset: 0; width: 100%; height: 100%; }}
.dungeonSvg svg {{ display: block; width: 100%; height: 100%; }}
.dungeonPin {{ position: absolute; z-index: 3; width: 15px; height: 15px; margin: -7px 0 0 -7px; border: 2px solid #ffffff; border-radius: 50%; background: #f5f7f0; box-shadow: 0 0 0 4px rgba(255,255,255,.2), 0 0 18px rgba(130,220,255,.82); }}
.dungeonPin::after {{ content: attr(data-name); position: absolute; left: 17px; top: -7px; max-width: 190px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 3px 6px; border-radius: 4px; background: rgba(8,12,14,.82); color: #fff; font-size: 12px; }}
.blip {{ position: absolute; z-index: 2; width: 8px; height: 8px; margin: -4px 0 0 -4px; border-radius: 50%; pointer-events: none; background: #c9d0d0; box-shadow: 0 0 0 2px rgba(201,208,208,.2), 0 0 10px rgba(201,208,208,.65); }}
.blip.creature {{ background: #d6a53b; box-shadow: 0 0 0 2px rgba(214,165,59,.24), 0 0 11px rgba(214,165,59,.78); }}
.blip.npc, .blip.vendor {{ background: #f0dc54; box-shadow: 0 0 0 2px rgba(240,220,84,.22), 0 0 10px rgba(240,220,84,.72); }}
.blip.portal {{ width: 12px; height: 12px; margin: -6px 0 0 -6px; background: transparent; border: 2px solid #a56cff; box-shadow: 0 0 0 2px rgba(165,108,255,.18), 0 0 14px rgba(165,108,255,.86); }}
.blip.lifestone {{ width: 11px; height: 11px; margin: -5px 0 0 -5px; background: #4f8cff; box-shadow: 0 0 0 3px rgba(79,140,255,.2), 0 0 14px rgba(79,140,255,.82); }}
.blip.door {{ width: 11px; height: 4px; margin: -2px 0 0 -5px; border-radius: 1px; background: #b98b58; box-shadow: 0 0 0 2px rgba(185,139,88,.18), 0 0 8px rgba(185,139,88,.62); }}
.blip.light {{ z-index: 1; width: 34px; height: 34px; margin: -17px 0 0 -17px; background: radial-gradient(circle, rgba(255,214,116,.42) 0, rgba(255,189,87,.18) 38%, rgba(255,189,87,0) 72%); box-shadow: none; }}
.zoomControls {{ position: absolute; z-index: 5; left: 12px; bottom: 12px; display: none; grid-template-columns: repeat(4, 36px); gap: 6px; }}
.hasLayer .zoomControls {{ display: grid; }}
.zoomControls button {{ width: 36px; height: 36px; padding: 0; border-radius: 4px; font-size: 18px; font-weight: 700; }}
aside {{ border-left: 1px solid rgba(255,255,255,.12); background: #15191b; padding: 14px; overflow: auto; }}
h1 {{ margin: 0 0 12px; font-size: 20px; font-weight: 650; }}
.controls {{ display: grid; grid-template-columns: 1fr auto; gap: 8px; margin-bottom: 12px; }}
input {{ width: 100%; min-width: 0; background: #0e1112; color: #e8ece8; border: 1px solid rgba(255,255,255,.18); border-radius: 4px; padding: 8px; }}
button {{ border: 1px solid rgba(255,255,255,.18); border-radius: 4px; background: #2f5d6a; color: #fff; padding: 8px 10px; cursor: pointer; }}
#status {{ color: #abb7b2; font-size: 13px; margin-bottom: 12px; min-height: 18px; }}
.legend {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px 10px; margin: 0 0 12px; padding: 10px; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; background: rgba(0,0,0,.14); }}
.legendItem {{ display: flex; align-items: center; gap: 7px; min-width: 0; color: #c4cfca; font-size: 12px; }}
.legendDot {{ flex: 0 0 auto; width: 10px; height: 10px; border-radius: 50%; background: #c9d0d0; }}
.legendDot.player {{ background: #f5f7f0; border: 1px solid #fff; box-shadow: 0 0 8px rgba(130,220,255,.75); }}
.legendDot.creature {{ background: #d6a53b; }}
.legendDot.npc {{ background: #f0dc54; }}
.legendDot.portal {{ background: transparent; border: 2px solid #a56cff; }}
.legendDot.light {{ width: 16px; height: 16px; background: radial-gradient(circle, rgba(255,214,116,.7) 0, rgba(255,189,87,.25) 46%, rgba(255,189,87,0) 74%); }}
.legendDot.door {{ width: 13px; height: 5px; border-radius: 1px; background: #b98b58; }}
.depthKey {{ grid-column: 1 / -1; display: grid; grid-template-columns: auto 1fr auto; gap: 7px; align-items: center; }}
.depthRamp {{ height: 8px; border-radius: 999px; background: linear-gradient(90deg, #1d2f52, #28544f, #766a3a); box-shadow: inset 0 0 0 1px rgba(255,255,255,.16); }}
.player {{ border-top: 1px solid rgba(255,255,255,.12); padding: 10px 0; cursor: pointer; }}
.player strong {{ display: block; color: #fff3bf; margin-bottom: 3px; overflow-wrap: anywhere; }}
.muted {{ color: #aab4b0; font-size: 12px; }}
.bars {{ display: grid; gap: 3px; margin-top: 7px; }}
.bar {{ height: 5px; background: rgba(255,255,255,.12); border-radius: 999px; overflow: hidden; }}
.bar span {{ display: block; height: 100%; }}
.health span {{ background: #e35748; }}
.stamina span {{ background: #ead45f; }}
.mana span {{ background: #6c92ff; }}
@media (max-width: 860px) {{ body {{ grid-template-columns: 1fr; }} #map {{ min-height: 64vh; }} aside {{ border-left: 0; border-top: 1px solid rgba(255,255,255,.12); }} }}
</style>
</head>
<body>
<main id=""map"">
  <div class=""axis north"">102N</div><div class=""axis south"">102S</div><div class=""axis west"">102W</div><div class=""axis east"">102E</div>
  <div class=""zoomControls""><button id=""zoomIn"" title=""Zoom in"">+</button><button id=""zoomOut"" title=""Zoom out"">-</button><button id=""zoomReset"" title=""Reset view"">1</button><button id=""zoomFit"" title=""Fit"">□</button></div>
</main>
<aside>
  <h1>Admin Map</h1>
  <div class=""controls""><input id=""token"" type=""password"" placeholder=""token, if configured""><button id=""save"">Save</button></div>
  <div id=""status"">Loading...</div>
  <div class=""legend"">
    <div class=""legendItem""><span class=""legendDot player""></span><span>Player</span></div>
    <div class=""legendItem""><span class=""legendDot creature""></span><span>Creature</span></div>
    <div class=""legendItem""><span class=""legendDot npc""></span><span>NPC/vendor</span></div>
    <div class=""legendItem""><span class=""legendDot portal""></span><span>Portal</span></div>
    <div class=""legendItem""><span class=""legendDot light""></span><span>Light</span></div>
    <div class=""legendItem""><span class=""legendDot door""></span><span>Door</span></div>
    <div class=""legendItem depthKey""><span>Low</span><span class=""depthRamp""></span><span>High</span></div>
  </div>
  <div id=""players""></div>
</aside>
<script>
const map = document.getElementById('map');
const list = document.getElementById('players');
const status = document.getElementById('status');
const token = document.getElementById('token');
let currentDungeon = null;
let currentMode = null;
let mapLayer = null;
let view = {{ scale: 1, x: 0, y: 0 }};
let dragging = false;
let dragStart = null;
token.value = localStorage.getItem('derpace-admin-map-token') || '';
document.getElementById('save').onclick = () => {{ localStorage.setItem('derpace-admin-map-token', token.value); refresh(); }};
function pctX(x) {{ return ((x + 102) / 204) * 100; }}
function pctY(y) {{ return ((102 - y) / 204) * 100; }}
function mapPctX(x, b) {{ return b.left + ((x + 102) / 204) * (b.right - b.left); }}
function mapPctY(y, b) {{ return b.top + ((102 - y) / 204) * (b.bottom - b.top); }}
function dungeonPctX(x, d) {{ return ((x - d.minX) / Math.max(1, d.maxX - d.minX)) * 100; }}
function dungeonPctY(y, d) {{ return ((d.maxY - y) / Math.max(1, d.maxY - d.minY)) * 100; }}
function bar(cur, max, cls) {{ const p = max > 0 ? Math.max(0, Math.min(100, cur / max * 100)) : 0; return `<div class=""bar ${{cls}}""><span style=""width:${{p}}%""></span></div>`; }}
function clearMap() {{ map.querySelectorAll('.mapLayer').forEach(x => x.remove()); mapLayer = null; map.classList.remove('hasLayer'); }}
function applyView() {{ if (mapLayer) mapLayer.style.transform = `translate(${{view.x}}px, ${{view.y}}px) scale(${{view.scale}})`; }}
function resetView() {{ view = {{ scale: 1, x: 0, y: 0 }}; applyView(); }}
function blipClass(blip) {{ return (blip.kind || (blip.isMonster ? 'creature' : 'npc')).toLowerCase(); }}
function createLayer(kind) {{
  mapLayer = document.createElement('div');
  mapLayer.className = 'mapLayer ' + kind;
  map.appendChild(mapLayer);
  map.classList.add('hasLayer');
  applyView();
  return mapLayer;
}}
function zoomAt(factor, cx, cy) {{
  const old = view.scale;
  const next = Math.max(0.35, Math.min(8, old * factor));
  if (next === old) return;
  view.x = cx - ((cx - view.x) / old) * next;
  view.y = cy - ((cy - view.y) / old) * next;
  view.scale = next;
  applyView();
}}
function addBlip(layer, blip, left, top) {{
  const marker = document.createElement('span');
  const kind = blipClass(blip);
  marker.className = 'blip ' + kind;
  marker.style.left = left + '%';
  marker.style.top = top + '%';
  marker.title = `${{blip.name || kind}}\n${{blip.loc || blip.cell}}\n${{kind}}${{blip.radarColor ? ' / ' + blip.radarColor : ''}}\nz ${{Number(blip.z || 0).toFixed(2)}}`;
  layer.appendChild(marker);
}}
document.getElementById('zoomIn').onclick = () => zoomAt(1.25, map.clientWidth / 2, map.clientHeight / 2);
document.getElementById('zoomOut').onclick = () => zoomAt(0.8, map.clientWidth / 2, map.clientHeight / 2);
document.getElementById('zoomReset').onclick = resetView;
document.getElementById('zoomFit').onclick = resetView;
map.addEventListener('wheel', e => {{
  if (!mapLayer) return;
  e.preventDefault();
  const rect = map.getBoundingClientRect();
  zoomAt(e.deltaY < 0 ? 1.18 : 0.85, e.clientX - rect.left, e.clientY - rect.top);
}}, {{ passive: false }});
map.addEventListener('pointerdown', e => {{
  if (!mapLayer || e.button !== 0 || e.target.closest('button')) return;
  dragging = true;
  dragStart = {{ x: e.clientX, y: e.clientY, vx: view.x, vy: view.y }};
  map.setPointerCapture(e.pointerId);
}});
map.addEventListener('pointermove', e => {{
  if (!dragging || !dragStart) return;
  view.x = dragStart.vx + e.clientX - dragStart.x;
  view.y = dragStart.vy + e.clientY - dragStart.y;
  applyView();
}});
map.addEventListener('pointerup', e => {{ dragging = false; dragStart = null; try {{ map.releasePointerCapture(e.pointerId); }} catch {{}} }});
map.addEventListener('pointercancel', () => {{ dragging = false; dragStart = null; }});
async function load() {{
  const modeChanged = currentMode !== 'world';
  currentDungeon = null;
  currentMode = 'world';
  try {{
    const headers = token.value ? {{ 'X-DerpACE-Map-Token': token.value }} : {{}};
    const res = await fetch('/api/players', {{ headers, cache: 'no-store' }});
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || res.statusText);
    clearMap();
    map.classList.remove('dungeonMode');
    map.classList.remove('hasImage');
    map.style.backgroundImage = '';
    const layer = createLayer('worldLayer');
    if (data.mapImageUrl) {{
      map.classList.add('hasImage');
      const image = document.createElement('div');
      image.className = 'worldMapImage';
      image.style.backgroundImage = `url('${{data.mapImageUrl}}?token=${{encodeURIComponent(token.value)}}')`;
      layer.appendChild(image);
    }}
    if (modeChanged) resetView();
    list.innerHTML = '';
    const blips = data.blips || [];
    status.textContent = `${{data.onlineCount}} visible online player${{data.onlineCount === 1 ? '' : 's'}}, ${{blips.length}} nearby radar blip${{blips.length === 1 ? '' : 's'}} - updated ${{new Date(data.serverTimeUtc).toLocaleTimeString()}}`;
    for (const b of blips) {{
      if (b.mapX !== null && b.mapY !== null) addBlip(layer, b, mapPctX(b.mapX, data.mapBounds), mapPctY(b.mapY, data.mapBounds));
    }}
    for (const p of data.players) {{
      if (p.mapX !== null && p.mapY !== null) {{
        const pin = document.createElement('button');
        pin.className = 'pin' + (p.isIndoors ? ' indoor' : '');
        pin.style.left = mapPctX(p.mapX, data.mapBounds) + '%';
        pin.style.top = mapPctY(p.mapY, data.mapBounds) + '%';
        pin.dataset.name = p.name;
        pin.title = `${{p.name}}\n${{p.loc || 'indoors'}}\n${{p.landblock}}`;
        layer.appendChild(pin);
      }}
      const item = document.createElement('div');
      item.className = 'player';
      item.innerHTML = `<strong>${{p.name}}</strong><div class=""muted"">${{p.loc || 'Indoor/dungeon'}} | ${{p.landblock}}</div><div class=""bars"">${{bar(p.health,p.maxHealth,'health')}}${{bar(p.stamina,p.maxStamina,'stamina')}}${{bar(p.mana,p.maxMana,'mana')}}</div>`;
      if (p.isIndoors) item.onclick = () => loadDungeon(p.landblock);
      list.appendChild(item);
    }}
  }} catch (e) {{
    status.textContent = e.message;
  }}
}}
async function loadDungeon(landblock) {{
  const modeChanged = currentMode !== 'dungeon' || currentDungeon !== landblock;
  currentDungeon = landblock;
  currentMode = 'dungeon';
  try {{
    const headers = token.value ? {{ 'X-DerpACE-Map-Token': token.value }} : {{}};
    const res = await fetch('/api/dungeon?landblock=' + encodeURIComponent(landblock), {{ headers, cache: 'no-store' }});
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || res.statusText);
    clearMap();
    map.classList.add('dungeonMode');
    map.classList.remove('hasImage');
    map.style.backgroundImage = '';
    if (!data.generated) throw new Error(data.error || 'No dungeon geometry for ' + landblock);
    const layer = createLayer('dungeonLayer');
    const wrap = document.createElement('div');
    wrap.className = 'dungeonSvg';
    wrap.innerHTML = data.svg;
    layer.appendChild(wrap);
    if (modeChanged) resetView();
    for (const b of data.blips || []) {{
      addBlip(layer, b, dungeonPctX(b.x, data), dungeonPctY(b.y, data));
    }}
    for (const p of data.players) {{
      const pin = document.createElement('button');
      pin.className = 'dungeonPin';
      pin.style.left = dungeonPctX(p.x, data) + '%';
      pin.style.top = dungeonPctY(p.y, data) + '%';
      pin.dataset.name = p.name;
      pin.title = `${{p.name}}\n${{p.loc || p.cell}}\n${{p.cell}}\nmap xy ${{p.x.toFixed(3)}}, ${{p.y.toFixed(3)}}\nbounds x ${{data.minX.toFixed(3)}}..${{data.maxX.toFixed(3)}} y ${{data.minY.toFixed(3)}}..${{data.maxY.toFixed(3)}}`;
      layer.appendChild(pin);
    }}
    applyView();
    const blips = data.blips || [];
    status.textContent = `${{data.players.length}} visible player${{data.players.length === 1 ? '' : 's'}}, ${{blips.length}} nearby radar blip${{blips.length === 1 ? '' : 's'}} in ${{data.landblock}} | z ${{data.minZ.toFixed(1)}}..${{data.maxZ.toFixed(1)}}`;
  }} catch (e) {{
    status.textContent = e.message;
  }}
}}
load();
function refresh() {{ currentDungeon ? loadDungeon(currentDungeon) : load(); }}
setInterval(refresh, {refresh * 1000});
</script>
</body>
</html>";
        }

        private sealed class AdminMapSnapshot
        {
            public DateTime ServerTimeUtc { get; set; }
            public int RefreshSeconds { get; set; }
            public int OnlineCount { get; set; }
            public string MapImageUrl { get; set; }
            public AdminMapBounds MapBounds { get; set; }
            public List<AdminMapPlayer> Players { get; set; }
            public List<AdminMapBlip> Blips { get; set; }
        }

        private sealed class AdminMapBounds
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Right { get; set; }
            public float Bottom { get; set; }
        }

        private sealed class AdminMapPlayer
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Landblock { get; set; }
            public string Loc { get; set; }
            public bool IsIndoors { get; set; }
            public float? MapX { get; set; }
            public float? MapY { get; set; }
            public float WorldX { get; set; }
            public float WorldY { get; set; }
            public float Z { get; set; }
            public double Heading { get; set; }
            public uint Health { get; set; }
            public uint MaxHealth { get; set; }
            public uint Stamina { get; set; }
            public uint MaxStamina { get; set; }
            public uint Mana { get; set; }
            public uint MaxMana { get; set; }
        }

        private sealed class AdminMapBlip
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Cell { get; set; }
            public string Landblock { get; set; }
            public string Loc { get; set; }
            public string Kind { get; set; }
            public string RadarColor { get; set; }
            public bool IsMonster { get; set; }
            public float? MapX { get; set; }
            public float? MapY { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }

        private sealed class AdminDungeonSnapshot
        {
            public string Landblock { get; set; }
            public bool Generated { get; set; }
            public string Error { get; set; }
            public float MinX { get; set; }
            public float MinY { get; set; }
            public float MaxX { get; set; }
            public float MaxY { get; set; }
            public float MinZ { get; set; }
            public float MaxZ { get; set; }
            public string Svg { get; set; }
            public List<AdminDungeonPlayer> Players { get; set; }
            public List<AdminMapBlip> Blips { get; set; }
        }

        private sealed class AdminDungeonMap
        {
            public bool Generated { get; set; }
            public string Error { get; set; }
            public float MinX { get; set; }
            public float MinY { get; set; }
            public float MaxX { get; set; }
            public float MaxY { get; set; }
            public float MinZ { get; set; }
            public float MaxZ { get; set; }
            public string Svg { get; set; }

            public static AdminDungeonMap Fail(string error)
            {
                return new AdminDungeonMap { Generated = false, Error = error, Svg = "" };
            }
        }

        private sealed class AdminDungeonPlayer
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Cell { get; set; }
            public string Loc { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public double Heading { get; set; }
            public uint Health { get; set; }
            public uint MaxHealth { get; set; }
            public uint Stamina { get; set; }
            public uint MaxStamina { get; set; }
            public uint Mana { get; set; }
            public uint MaxMana { get; set; }
        }
    }
}
