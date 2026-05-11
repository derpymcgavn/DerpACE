using ACE.DatLoader;
using ACE.Server.Managers;
using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ACE.Server.Pathfinding
{
    /// <summary>
    /// On first server boot, scans the AC dat files for every landblock that has terrain
    /// (CellLandblock, suffix 0xFFFF) or dungeon info (LandblockInfo, suffix 0xFFFE) and
    /// asks <see cref="Pathfinder"/> to build and persist its navmesh files to disk.
    /// On subsequent boots, the cached .mesh files are detected and the prebuild becomes
    /// a no-op for those landblocks.
    /// </summary>
    public static class PathfindingPrebuilder
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static int _running;
        private static CancellationTokenSource _cts;
        private static Task _task;

        public static bool IsRunning => Interlocked.CompareExchange(ref _running, 0, 0) == 1;

        public static int LandblocksTotal { get; private set; }
        public static int LandblocksProcessed { get; private set; }
        public static int LandblocksBuilt { get; private set; }

        public static void Initialize()
        {
            if (!PropertyManager.GetBool("pathfinding_prebuild_on_boot").Item)
            {
                log.Info("Pathfinding prebuild on boot is disabled (pathfinding_prebuild_on_boot=false). Skipping.");
                return;
            }

            Start();
        }

        public static bool Start()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                log.Warn("PathfindingPrebuilder.Start called while a prebuild is already running.");
                return false;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _task = Task.Run(() =>
            {
                try
                {
                    Run(token);
                }
                catch (Exception ex)
                {
                    log.Error("PathfindingPrebuilder background task failed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            }, token);

            return true;
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
        }

        private static void Run(CancellationToken token)
        {
            if (DatManager.CellDat == null)
            {
                log.Warn("PathfindingPrebuilder: DatManager.CellDat is not initialized; aborting.");
                return;
            }

            // Discover landblocks from the cell dat. AC stores per-landblock files keyed by
            // 0xAAAABBBB where BBBB is the per-landblock suffix.
            //   0xFFFF => CellLandblock (terrain) -> outdoor
            //   0xFFFE => LandblockInfo (dungeons / static objects) -> indoor
            var outdoor = new HashSet<uint>();
            var indoor = new HashSet<uint>();

            foreach (var key in DatManager.CellDat.AllFiles.Keys)
            {
                var suffix = key & 0x0000FFFFu;
                var lb = key & 0xFFFF0000u;
                if (lb == 0)
                    continue;

                if (suffix == 0xFFFF)
                    outdoor.Add(lb);
                else if (suffix == 0xFFFE)
                    indoor.Add(lb);
            }

            LandblocksTotal = outdoor.Count + indoor.Count;
            LandblocksProcessed = 0;
            LandblocksBuilt = 0;

            log.Info($"PathfindingPrebuilder: starting. Outdoor landblocks: {outdoor.Count}, Indoor landblocks: {indoor.Count}.");

            var sw = Stopwatch.StartNew();
            var lastReport = sw.ElapsedMilliseconds;

            // Outdoor first since they cover the open world.
            ProcessSet(outdoor, isIndoors: false, token, ref lastReport, sw);
            ProcessSet(indoor, isIndoors: true, token, ref lastReport, sw);

            sw.Stop();
            if (token.IsCancellationRequested)
                log.Info($"PathfindingPrebuilder: cancelled after {LandblocksProcessed}/{LandblocksTotal} landblocks ({LandblocksBuilt} built) in {sw.Elapsed.TotalMinutes:F1} min.");
            else
                log.Info($"PathfindingPrebuilder: complete. {LandblocksProcessed}/{LandblocksTotal} landblocks scanned, {LandblocksBuilt} new mesh sets built in {sw.Elapsed.TotalMinutes:F1} min.");
        }

        private static void ProcessSet(HashSet<uint> set, bool isIndoors, CancellationToken token, ref long lastReport, Stopwatch sw)
        {
            foreach (var landblockId in set.OrderBy(x => x))
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    if (Pathfinder.PrebuildLandblockMesh(landblockId, isIndoors))
                        LandblocksBuilt++;
                }
                catch (Exception ex)
                {
                    log.Warn($"PathfindingPrebuilder: failed to prebuild {landblockId:X8} (indoors={isIndoors}): {ex.Message}");
                }

                LandblocksProcessed++;

                var now = sw.ElapsedMilliseconds;
                if (now - lastReport >= 30_000)
                {
                    log.Info($"PathfindingPrebuilder: progress {LandblocksProcessed}/{LandblocksTotal} ({LandblocksBuilt} built so far)...");
                    lastReport = now;
                }
            }
        }
    }
}
