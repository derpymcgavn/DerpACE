//
// This code is based on Trevis' pathfinding proof of concept at https://gitlab.com/trevis/ace.mods.pathfinding/-/tree/master
//

using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Pathfinding.Geometry;
using ACE.Server.WorldObjects;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Tools;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace ACE.Server.Pathfinding
{
    public enum AgentWidth
    {
        Narrow,
        Wide
    }

    public static class Pathfinder
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private const int VERTS_PER_POLY = 6;
        private const int MAX_POLYS = 256;

        public static string InsideMeshDirectory => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Pathfinding", "Meshes", "Indoors");
        public static string OutsideMeshDirectory => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Pathfinding", "Meshes", "Outdoors");
        public static readonly ConcurrentDictionary<uint, DtNavMesh> Meshes = new ConcurrentDictionary<uint, DtNavMesh>();

        static Pathfinder()
        {
            if (!Directory.Exists(InsideMeshDirectory))
            {
                Directory.CreateDirectory(InsideMeshDirectory);
            }
            if (!Directory.Exists(OutsideMeshDirectory))
            {
                Directory.CreateDirectory(OutsideMeshDirectory);
            }
        }

        public static bool PathfindingEnabled { get => PropertyManager.GetBool("pathfinding").Item; }

        public static WorldObject CreateMarker(Position position, string customName = "")
        {
            var marker = WorldObjectFactory.CreateNewWorldObject((uint)Factories.Enum.WeenieClassName.pathfinderHelper);

            if (marker == null)
                return null;

            if (customName != "")
                marker.Name = customName;

            marker.Location = position;
            marker.Location.LandblockId = new LandblockId(marker.Location.GetCell());
            var landblock = LandblockManager.GetLandblock(marker.Location.LandblockId, false);

            if (marker.EnterWorld())
                return marker;

            marker.Destroy();
            return null;
        }

        public static void DrawRoute(List<Position> route)
        {
            if (route == null || route.Count == 0)
                return;

            foreach (var entry in route)
            {
                CreateMarker(entry);
            }
        }

        /// <summary>
        /// Find a route to the end position.
        /// Supports cross-landblock paths by chaining per-landblock segment queries.
        /// </summary>
        public static List<Position> FindRoute(Position start, Position end, AgentWidth agentWidth, bool drawRoute = false)
        {
            // Same landblock => single fast-path query.
            if ((start.Cell & 0xFFFF0000) == (end.Cell & 0xFFFF0000))
                return FindRouteSegment(start, end, start.Cell, agentWidth, drawRoute);

            return FindRouteAcrossLandblocks(start, end, agentWidth, drawRoute);
        }

        private const int MAX_LANDBLOCK_HOPS = 12;

        private static List<Position> FindRouteAcrossLandblocks(Position start, Position end, AgentWidth agentWidth, bool drawRoute)
        {
            // Outdoor only: indoor meshes don't tile across landblocks the same way.
            if (start.Indoors || end.Indoors)
            {
                log.Warn($"FindRoute: cross-landblock routing requires both endpoints outdoors. start.Indoors={start.Indoors} end.Indoors={end.Indoors}");
                return null;
            }

            var combined = new List<Position>();
            var current = start;
            var hops = 0;

            while (hops++ < MAX_LANDBLOCK_HOPS)
            {
                var currentLb = current.Cell & 0xFFFF0000;
                var endLb = end.Cell & 0xFFFF0000;

                if (currentLb == endLb)
                {
                    var tail = FindRouteSegment(current, end, currentLb, agentWidth, drawRoute: false);
                    if (tail == null || tail.Count == 0)
                        return combined.Count > 0 ? combined : null;

                    AppendSegment(combined, tail);
                    if (drawRoute) DrawRoute(combined);
                    return combined;
                }

                if (!TryComputeBorderHop(current, end, out var exitPos, out var entryPos))
                {
                    log.Warn($"FindRoute: failed to compute border hop from {current.ToLOCString()} toward {end.ToLOCString()}");
                    return combined.Count > 0 ? combined : null;
                }

                var seg = FindRouteSegment(current, exitPos, currentLb, agentWidth, drawRoute: false);
                if (seg == null || seg.Count == 0)
                    return combined.Count > 0 ? combined : null;

                AppendSegment(combined, seg);

                // Hand off to the neighbor landblock at the equivalent world position.
                current = entryPos;
            }

            log.Warn($"FindRoute: hop limit {MAX_LANDBLOCK_HOPS} exceeded between {start.ToLOCString()} and {end.ToLOCString()}");
            return combined.Count > 0 ? combined : null;
        }

        private static void AppendSegment(List<Position> combined, List<Position> seg)
        {
            int startIndex = 0;
            // Avoid stacking duplicate waypoints at landblock seams.
            if (combined.Count > 0 && combined[combined.Count - 1].DistanceTo(seg[0]) < 0.5f)
                startIndex = 1;

            for (int i = startIndex; i < seg.Count; i++)
                combined.Add(seg[i]);
        }

        /// <summary>
        /// In landblock-local 2D space (0..192) compute where the line from
        /// <paramref name="current"/> toward <paramref name="end"/> exits the current
        /// landblock, and the equivalent entry point in the neighboring landblock.
        /// </summary>
        private static bool TryComputeBorderHop(Position current, Position end, out Position exitPos, out Position entryPos)
        {
            exitPos = null;
            entryPos = null;

            // World offset from current to end (in AC ground units, X east, Y north).
            var dx = (end.LandblockId.LandblockX - current.LandblockId.LandblockX) * 192f + (end.PositionX - current.PositionX);
            var dy = (end.LandblockId.LandblockY - current.LandblockId.LandblockY) * 192f + (end.PositionY - current.PositionY);
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
                return false;

            // Solve t (>0) at which the local position leaves [0,192] in either axis.
            float tx = float.PositiveInfinity, ty = float.PositiveInfinity;
            int dirX = 0, dirY = 0;
            const float edgeEps = 0.25f;

            if (dx > 0) { tx = (192f - current.PositionX) / dx; dirX = +1; }
            else if (dx < 0) { tx = (0f - current.PositionX) / dx; dirX = -1; }

            if (dy > 0) { ty = (192f - current.PositionY) / dy; dirY = +1; }
            else if (dy < 0) { ty = (0f - current.PositionY) / dy; dirY = -1; }

            float t = Math.Min(tx, ty);
            if (!(t > 0f) || float.IsInfinity(t))
                return false;

            int hopX = 0, hopY = 0;
            if (Math.Abs(tx - t) < 1e-4f) hopX = dirX;
            if (Math.Abs(ty - t) < 1e-4f) hopY = dirY;
            if (hopX == 0 && hopY == 0)
                return false;

            // Exit point inside current landblock, nudged just inside the boundary.
            var exitX = Math.Max(0f, Math.Min(192f, current.PositionX + dx * t));
            var exitY = Math.Max(0f, Math.Min(192f, current.PositionY + dy * t));
            var exitZ = current.PositionZ + (end.PositionZ - current.PositionZ) * t;

            // Entry point in neighbor landblock. Translate by 192 in the hop direction
            // and pull a tiny step away from the seam to land cleanly on a poly.
            var entryX = exitX - hopX * 192f;
            var entryY = exitY - hopY * 192f;

            if (hopX > 0) { exitX -= edgeEps; entryX += edgeEps; }
            else if (hopX < 0) { exitX += edgeEps; entryX -= edgeEps; }

            if (hopY > 0) { exitY -= edgeEps; entryY += edgeEps; }
            else if (hopY < 0) { exitY += edgeEps; entryY -= edgeEps; }

            byte newX, newY;
            try
            {
                newX = (byte)(current.LandblockId.LandblockX + hopX);
                newY = (byte)(current.LandblockId.LandblockY + hopY);
            }
            catch
            {
                return false;
            }

            var exitLb = current.LandblockId;
            var newLb = new LandblockId(newX, newY);
            uint exitCell = (exitLb.Raw & 0xFFFF0000) | 1u;
            uint entryCell = (newLb.Raw & 0xFFFF0000) | 1u;

            exitPos = new Position(exitCell, new Vector3(exitX, exitY, exitZ), Quaternion.Identity);
            entryPos = new Position(entryCell, new Vector3(entryX, entryY, exitZ), Quaternion.Identity);
            return true;
        }

        private static List<Position> FindRouteSegment(Position start, Position end, uint cellForOutput, AgentWidth agentWidth, bool drawRoute)
        {
            if (!TryGetMesh(start, agentWidth, out var mesh) || mesh is null)
                return null;

            var rc = new RcTestNavMeshTool();
            var halfExtents = new RcVec3f(1.25f, 1.25f, 1.25f);
            var query = new DtNavMeshQuery(mesh);
            var m_filter = new DtQueryDefaultFilter();

            query.FindNearestPoly(new RcVec3f(start.PositionX, start.PositionZ, start.PositionY), halfExtents, m_filter, out long startRef, out var startPt, out _);
            query.FindNearestPoly(new RcVec3f(end.PositionX, end.PositionZ, end.PositionY), halfExtents, m_filter, out long endRef, out var endPt, out _);

            var polys = new List<long>();
            DtStraightPath[] path = new DtStraightPath[MAX_POLYS];
            rc.FindStraightPath(query, startRef, endRef, startPt, endPt, m_filter, true, ref polys, path, out var straightPathCount, MAX_POLYS, 0);

            var positionList = new List<Position>();
            for (int i = 0; i < straightPathCount; i++)
            {
                var entry = path[i];
                positionList.Add(new Position(cellForOutput, new Vector3(entry.pos.X, entry.pos.Z, entry.pos.Y), Quaternion.Identity));
            }

            if (drawRoute)
                DrawRoute(positionList);

            return positionList;
        }

        public static bool GetRouteDistance(Position start, Position end, AgentWidth agentWidth, out float distance)
        {
            distance = 0f;

            var route = FindRoute(start, end, agentWidth);
            if (route != null && route.Count > 0)
            {
                if (route.Last().DistanceTo(end) > 1)
                    return false;

                var previousRouteEntry = start;
                foreach (var routeEntry in route)
                {
                    distance += previousRouteEntry.DistanceTo(routeEntry);
                    previousRouteEntry = routeEntry;
                }
            }
            else
                return false;
            return true;
        }

        /// <summary>
        /// Get a random point on the navmesh
        /// </summary>
        public static Position GetRandomPointOnMesh(Position start, AgentWidth agentWidth)
        {
            if (!TryGetMesh(start, agentWidth, out var mesh) || mesh is null)
            {
                return null;
            }

            var query = new DtNavMeshQuery(mesh);
            var m_filter = new DtQueryDefaultFilter();
            var frand = new RcRand(DateTime.Now.Ticks);

            query.FindRandomPoint(m_filter, frand, out long randomRef, out var randomPt);

            return new Position(start.Cell, new Vector3(randomPt.X, randomPt.Z, randomPt.Y), Quaternion.Identity);
        }

        public static Position GetRandomPointWithinCircle(Position location, float radius, AgentWidth agentWidth)
        {
            if (!TryGetMesh(location, agentWidth, out var mesh) || mesh is null)
            {
                return null;
            }

            var query = new DtNavMeshQuery(mesh);
            var m_filter = new DtQueryDefaultFilter();
            var frand = new RcRand(DateTime.Now.Ticks);

            var halfExtents = new RcVec3f(1.25f, 1.25f, 1.25f);

            var startStatus = query.FindNearestPoly(new RcVec3f(location.PositionX, location.PositionZ, location.PositionY), halfExtents, m_filter, out long startRef, out var startPt, out bool isStartOverPoly);

            query.FindRandomPointWithinCircle(startRef, startPt, radius, m_filter, frand, out long randomRef, out var randomPt);

            if (randomPt.X == 0 && randomPt.Y == 0 && randomPt.Z == 0)
                return null;

            return new Position(location.Cell, new Vector3(randomPt.X, randomPt.Z, randomPt.Y), Quaternion.Identity);
        }

        public static Position GetClosestPointOnMesh(Position location, AgentWidth agentWidth)
        {
            if (!TryGetMesh(location, agentWidth, out var mesh) || mesh is null)
            {
                return null;
            }

            var query = new DtNavMeshQuery(mesh);
            var m_filter = new DtQueryDefaultFilter();
            var frand = new RcRand(DateTime.Now.Ticks);

            var halfExtents = new RcVec3f(1.25f, 1.25f, 1.25f);

            var startStatus = query.FindNearestPoly(new RcVec3f(location.PositionX, location.PositionZ, location.PositionY), halfExtents, m_filter, out long startRef, out var startPt, out bool isStartOverPoly);

            if (startPt.X == 0 && startPt.Y == 0 && startPt.Z == 0)
                return null;

            return new Position(location.Cell, new Vector3(startPt.X, startPt.Z, startPt.Y), Quaternion.Identity);
        }

        public static Position GetNearestWallPosition(Position location, float radius, AgentWidth agentWidth, out float distance, bool inverseNormal = false)
        {
            if (!TryGetMesh(location, agentWidth, out var mesh) || mesh is null)
            {
                distance = float.MaxValue;
                return null;
            }

            var query = new DtNavMeshQuery(mesh);
            var m_filter = new DtQueryDefaultFilter();
            var frand = new RcRand(DateTime.Now.Ticks);

            var halfExtents = new RcVec3f(1.25f, 1.25f, 1.25f);

            var startStatus = query.FindNearestPoly(new RcVec3f(location.PositionX, location.PositionZ, location.PositionY), halfExtents, m_filter, out long startRef, out var startPt, out bool isStartOverPoly);

            query.FindDistanceToWall(startRef, startPt, radius, m_filter, out distance, out var wallPt, out var wallNormal);

            if (wallPt.X == 0 && wallPt.Y == 0 && wallPt.Z == 0 && wallNormal.X == 0 && wallNormal.Y == 0 && wallNormal.Z == 0)
                return null;

            var position = new Position(location.Cell, new Vector3(wallPt.X, wallPt.Z, wallPt.Y), Quaternion.Identity);

            if (inverseNormal)
                position.Rotate(new Vector3(-wallNormal.X, -wallNormal.Z, -wallNormal.Y));
            else
                position.Rotate(new Vector3(wallNormal.X, wallNormal.Z, wallNormal.Y));

            return position;
        }

        private static bool TryGetMesh(Position pos, AgentWidth agentWidth, out DtNavMesh mesh)
        {
            var meshId = (pos.Cell & 0xFFFF0000) + (uint)agentWidth;

            if (Meshes.TryGetValue(meshId, out mesh))
                return mesh is not null;

            TryLoadMesh(pos);
            return false;
        }

        public static void TryUnloadMesh(Position pos)
        {
            foreach (var agentWidth in Enum.GetValues(typeof(AgentWidth)).Cast<AgentWidth>())
            {
                var meshId = (pos.Cell & 0xFFFF0000) + (uint)agentWidth;
                Meshes.TryRemove(meshId, out _);
            }
        }

        public static void TryUnloadMesh(Landblock landblock)
        {
            foreach (var agentWidth in Enum.GetValues(typeof(AgentWidth)).Cast<AgentWidth>())
            {
                var meshId = (landblock.Id.Raw & 0xFFFF0000) + (uint)agentWidth;
                Meshes.TryRemove(meshId, out _);
            }
        }

        /// <summary>
        /// Build and persist navmeshes for a single landblock to disk without keeping them in memory.
        /// Used by <see cref="PathfindingPrebuilder"/> to warm the on-disk cache on first boot.
        /// Returns true if at least one new mesh file was written, false if everything was already cached
        /// or no buildable geometry was found.
        /// </summary>
        public static bool PrebuildLandblockMesh(uint landblockId, bool isIndoors)
        {
            landblockId &= 0xFFFF0000u;
            var meshDir = isIndoors ? InsideMeshDirectory : OutsideMeshDirectory;
            var builtAny = false;

            LandblockGeometry geometry = null;
            List<CellGeometry> cells = null;

            try
            {
                if (isIndoors)
                {
                    geometry = new LandblockGeometry(landblockId);
                    var dungeonCells = geometry.DungeonCells;
                    if (dungeonCells == null || dungeonCells.IsEmpty)
                        return false;
                    cells = dungeonCells.Values.ToList();
                }

                foreach (var agentWidth in Enum.GetValues(typeof(AgentWidth)).Cast<AgentWidth>())
                {
                    var meshId = landblockId + (uint)agentWidth;
                    var meshPath = Path.Combine(meshDir, $"{meshId:X8}.mesh");

                    if (File.Exists(meshPath))
                    {
                        var fi = new FileInfo(meshPath);
                        if (fi.Length > 0)
                            continue;
                        File.Delete(meshPath);
                    }

                    DotRecast.Recast.Geom.IInputGeomProvider geom;
                    if (isIndoors)
                        geom = CellGeometryProvider.LoadGeometry(geometry, cells);
                    else
                        geom = TerrainGeometryProvider.LoadGeometry(meshId);

                    if (geom is null)
                        continue;

                    var builder = new NavMeshBuilder();
                    var settings = isIndoors ? GetMeshSettings(agentWidth) : GetOutdoorMeshSettings(agentWidth);
                    var res = builder.Build(geom, settings);
                    if (res is null)
                        continue;

                    var meshWriter = new DtMeshDataWriter();
                    using (var stream = File.OpenWrite(meshPath))
                    using (var writer = new BinaryWriter(stream))
                    {
                        meshWriter.Write(writer, res, RcByteOrder.LITTLE_ENDIAN, false);
                    }
                    builtAny = true;
                }
            }
            catch (Exception ex)
            {
                log.Warn($"PrebuildLandblockMesh failed for {landblockId:X8} (indoors={isIndoors}): {ex.Message}");
            }

            return builtAny;
        }

        public static void TryLoadMesh(Position pos, bool rebuildMesh = false)
        {
            try
            {
                if (rebuildMesh)
                    TryUnloadMesh(pos);

                var isIndoors = pos.Indoors;
                var meshDir = isIndoors ? InsideMeshDirectory : OutsideMeshDirectory;

                foreach (var agentWidth in Enum.GetValues(typeof(AgentWidth)).Cast<AgentWidth>())
                {
                    var meshId = (pos.Cell & 0xFFFF0000) + (uint)agentWidth;

                    if (!Meshes.TryAdd(meshId, null))
                        continue;

                    LandblockGeometry geometry = null;
                    List<CellGeometry> cells = null;
                    if (isIndoors)
                    {
                        geometry = new LandblockGeometry(meshId);
                        if (!geometry.DungeonCells.TryGetValue(pos.Cell, out var cellGeometry))
                        {
                            log.Warn($"Could not load cell geometry! {pos} cellGeometry:{cellGeometry}");
                            return;
                        }
                        cells = geometry.DungeonCells.Values.ToList();
                    }

                    var meshPath = Path.Combine(meshDir, $"{meshId:X8}.mesh");
                    if (File.Exists(meshPath))
                    {
                        if (!rebuildMesh)
                        {
                            var meshReader = new DtMeshDataReader();

                            using (var stream = File.OpenRead(meshPath))
                            using (var reader = new BinaryReader(stream))
                            {
                                if (stream.Length > 0)
                                {
                                    var rcBytes = new RcByteBuffer(reader.ReadBytes((int)stream.Length));
                                    var meshData = meshReader.Read(rcBytes, VERTS_PER_POLY, true);

                                    var mesh = new DtNavMesh();
                                    mesh.Init(meshData, VERTS_PER_POLY, 0);
                                    Meshes.TryUpdate(meshId, mesh, null);
                                    return;
                                }
                            }
                        }
                        else
                            File.Delete(meshPath);
                    }

                    DotRecast.Recast.Geom.IInputGeomProvider geom;
                    if (isIndoors)
                    {
                        geom = CellGeometryProvider.LoadGeometry(geometry, cells);
                        if (geom is null)
                        {
                            log.Warn($"Could not load cell geometry provider! {pos} neighbors:{string.Join(",", cells.Select(n => $"{n.CellId:X8}"))}");
                            return;
                        }
                    }
                    else
                    {
                        geom = TerrainGeometryProvider.LoadGeometry(meshId);
                        if (geom is null)
                        {
                            log.Warn($"Could not load terrain geometry provider for outdoor landblock {meshId:X8}");
                            return;
                        }
                    }

                    var builder = new NavMeshBuilder();
                    var settings = isIndoors ? GetMeshSettings(agentWidth) : GetOutdoorMeshSettings(agentWidth);
                    var res = builder.Build(geom, settings);
                    if (res is null)
                    {
                        log.Warn($"Could not build the nav mesh! {pos} indoors:{isIndoors}");
                        return;
                    }

                    var meshWriter = new DtMeshDataWriter();
                    using (var stream = File.OpenWrite(meshPath))
                    using (var writer = new BinaryWriter(stream))
                    {
                        meshWriter.Write(writer, res, RcByteOrder.LITTLE_ENDIAN, false);
                    }

                    var meshNew = new DtNavMesh();
                    meshNew.Init(res, VERTS_PER_POLY, 0);
                    Meshes.TryUpdate(meshId, meshNew, null);
                }
            }
            catch (Exception)
            {
                if (!rebuildMesh)
                    TryLoadMesh(pos, true);
                else
                    log.Warn($"Failed to load mesh for pathfinding at: {pos.ToLOCString()}");
            }
        }

        private static RcNavMeshBuildSettings GetMeshSettings(AgentWidth type)
        {
            switch (type)
            {
                case AgentWidth.Narrow:
                    return new RcNavMeshBuildSettings()
                    {
                        agentHeight = 2f,
                        agentMaxClimb = 0.95f,
                        agentMaxSlope = 50f,
                        cellHeight = 0.1f,
                        cellSize = 0.1f,
                        agentRadius = 0.7f,
                        detailSampleDist = 6.0f,
                        detailSampleMaxError = 1.0f,
                        edgeMaxError = 1f,
                        edgeMaxLen = 12.0f,
                        mergedRegionSize = 20,
                        minRegionSize = 8,
                        vertsPerPoly = VERTS_PER_POLY,
                        partitioning = (int)RcPartition.WATERSHED
                    };
                case AgentWidth.Wide:
                default:
                    return new RcNavMeshBuildSettings()
                    {
                        agentHeight = 2f,
                        agentMaxClimb = 0.95f,
                        agentMaxSlope = 50f,
                        cellHeight = 0.1f,
                        cellSize = 0.1f,
                        agentRadius = 1.4f,
                        detailSampleDist = 6.0f,
                        detailSampleMaxError = 1.0f,
                        edgeMaxError = 1f,
                        edgeMaxLen = 12.0f,
                        mergedRegionSize = 20,
                        minRegionSize = 8,
                        vertsPerPoly = VERTS_PER_POLY,
                        partitioning = (int)RcPartition.WATERSHED
                    };
            }
        }

        /// <summary>
        /// Outdoor terrain meshes cover an entire 192x192 landblock, so we use a coarser
        /// voxelization to keep build time and memory reasonable. Slope is also tightened
        /// to keep monsters off cliffs.
        /// </summary>
        private static RcNavMeshBuildSettings GetOutdoorMeshSettings(AgentWidth type)
        {
            switch (type)
            {
                case AgentWidth.Narrow:
                    return new RcNavMeshBuildSettings()
                    {
                        agentHeight = 2f,
                        agentMaxClimb = 1.0f,
                        agentMaxSlope = 45f,
                        cellHeight = 0.4f,
                        cellSize = 0.5f,
                        agentRadius = 0.7f,
                        detailSampleDist = 6.0f,
                        detailSampleMaxError = 1.0f,
                        edgeMaxError = 1.3f,
                        edgeMaxLen = 24.0f,
                        mergedRegionSize = 20,
                        minRegionSize = 8,
                        vertsPerPoly = VERTS_PER_POLY,
                        partitioning = (int)RcPartition.WATERSHED
                    };
                case AgentWidth.Wide:
                default:
                    return new RcNavMeshBuildSettings()
                    {
                        agentHeight = 2f,
                        agentMaxClimb = 1.0f,
                        agentMaxSlope = 45f,
                        cellHeight = 0.4f,
                        cellSize = 0.5f,
                        agentRadius = 1.4f,
                        detailSampleDist = 6.0f,
                        detailSampleMaxError = 1.0f,
                        edgeMaxError = 1.3f,
                        edgeMaxLen = 24.0f,
                        mergedRegionSize = 20,
                        minRegionSize = 8,
                        vertsPerPoly = VERTS_PER_POLY,
                        partitioning = (int)RcPartition.WATERSHED
                    };
            }
        }
    }
}
