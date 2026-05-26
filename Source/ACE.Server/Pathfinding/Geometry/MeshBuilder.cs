using System;

using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;

namespace ACE.Server.Pathfinding.Geometry
{
    public class NavMeshBuilder
    {
        public DtMeshData Build(IInputGeomProvider geom, RcNavMeshBuildSettings settings)
        {
            return Build(geom,
                RcPartitionType.OfValue(settings.partitioning),
                settings.cellSize, settings.cellHeight,
                settings.agentMaxSlope, settings.agentHeight, settings.agentRadius, settings.agentMaxClimb,
                settings.minRegionSize, settings.mergedRegionSize,
                settings.edgeMaxLen, settings.edgeMaxError,
                settings.vertsPerPoly,
                settings.detailSampleDist, settings.detailSampleMaxError,
                settings.filterLowHangingObstacles, settings.filterLedgeSpans, settings.filterWalkableLowHeightSpans,
                settings.keepInterResults);
        }

        public DtMeshData Build(IInputGeomProvider geom,
            RcPartition partitionType,
            float cellSize, float cellHeight,
            float agentMaxSlope, float agentHeight, float agentRadius, float agentMaxClimb,
            int regionMinSize, int regionMergeSize,
            float edgeMaxLen, float edgeMaxError,
            int vertsPerPoly,
            float detailSampleDist, float detailSampleMaxError,
            bool filterLowHangingObstacles, bool filterLedgeSpans, bool filterWalkableLowHeightSpans,
            bool keepInterResults)
        {
            // Pass 1: normal settings
            var cfg = MakeConfig(partitionType, cellSize, cellHeight, agentMaxSlope, agentHeight, agentRadius,
                agentMaxClimb, regionMinSize, regionMergeSize, edgeMaxLen, edgeMaxError, vertsPerPoly,
                detailSampleDist, detailSampleMaxError,
                filterLowHangingObstacles, filterLedgeSpans, filterWalkableLowHeightSpans);

            RcBuilderResult rcResult;
            try
            {
                rcResult = BuildRecastResult(geom, cfg, keepInterResults);
            }
            catch (IndexOutOfRangeException)
            {
                // DotRecast 2024.3.1 bug: RcMeshDetails.TriangulateHull crashes on certain
                // polygon configurations. Retry with a coarser cell size and triangles-only
                // (vertsPerPoly=3) to force simpler polygon shapes that avoid the OOB.
                var coarseCfg = MakeConfig(partitionType, cellSize * 2f, cellHeight * 2f,
                    agentMaxSlope, agentHeight, agentRadius, agentMaxClimb,
                    regionMinSize, regionMergeSize, edgeMaxLen, edgeMaxError,
                    3 /* triangles only */,
                    0f, 0f, // no detail sampling
                    filterLowHangingObstacles, filterLedgeSpans, filterWalkableLowHeightSpans);
                try
                {
                    rcResult = BuildRecastResult(geom, coarseCfg, keepInterResults);
                    cellSize *= 2f;
                    cellHeight *= 2f;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return BuildMeshData(geom, cellSize, cellHeight, agentHeight, agentRadius, agentMaxClimb, rcResult);
        }

        private static RcConfig MakeConfig(
            RcPartition partitionType,
            float cellSize, float cellHeight,
            float agentMaxSlope, float agentHeight, float agentRadius, float agentMaxClimb,
            int regionMinSize, int regionMergeSize,
            float edgeMaxLen, float edgeMaxError,
            int vertsPerPoly,
            float detailSampleDist, float detailSampleMaxError,
            bool filterLowHangingObstacles, bool filterLedgeSpans, bool filterWalkableLowHeightSpans)
        {
            return new RcConfig(
                partitionType,
                cellSize, cellHeight,
                agentMaxSlope, agentHeight, agentRadius, agentMaxClimb,
                regionMinSize, regionMergeSize,
                edgeMaxLen, edgeMaxError,
                vertsPerPoly,
                detailSampleDist, detailSampleMaxError,
                filterLowHangingObstacles, filterLedgeSpans, filterWalkableLowHeightSpans,
                SampleAreaModifications.SAMPLE_AREAMOD_WALKABLE, true);
        }

        private DtNavMesh BuildNavMesh(DtMeshData meshData, int vertsPerPoly)
        {
            var mesh = new DtNavMesh();
            var status = mesh.Init(meshData, vertsPerPoly, 0);
            if (status.Failed())
                return null;
            return mesh;
        }

        private RcBuilderResult BuildRecastResult(IInputGeomProvider geom, RcConfig cfg, bool keepInterResults)
        {
            RcBuilderConfig bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax());
            RcBuilder rcBuilder = new RcBuilder();
            return rcBuilder.Build(geom, bcfg, keepInterResults);
        }

        public DtMeshData BuildMeshData(IInputGeomProvider geom,
            float cellSize, float cellHeight,
            float agentHeight, float agentRadius, float agentMaxClimb,
            RcBuilderResult result)
        {
            DtNavMeshCreateParams option = DemoNavMeshBuilder
                .GetNavMeshCreateParams(geom, cellSize, cellHeight, agentHeight, agentRadius, agentMaxClimb, result);

            var meshData = DtNavMeshBuilder.CreateNavMeshData(option);
            if (null == meshData)
                return null;

            return DemoNavMeshBuilder.UpdateAreaAndFlags(meshData);
        }
    }
}
