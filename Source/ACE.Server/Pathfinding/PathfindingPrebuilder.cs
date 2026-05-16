using ACE.DatLoader;
using ACE.Server.Managers;
using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ACE.Server.Pathfinding
{
    /// <summary>
    /// OPTIONAL performance optimization tool for pre-caching navmesh files.
    /// 
    /// The pathfinding system works fully on-demand: when a mob needs pathfinding for a landblock,
    /// the system dynamically generates a navmesh from the geometry (using Recast/Detour) and caches
    /// it to disk. On subsequent uses, the cached .mesh file is loaded instantly.
    /// 
    /// This prebuilder simply pre-generates all navmeshes at once (via /pathfind prebuild command or
    /// on boot if pathfinding_prebuild_on_boot=true) to avoid the small delay when each landblock is
    /// first visited. It scans AC dat files for every landblock with terrain or dungeon info and asks
    /// <see cref="Pathfinder"/> to build and persist navmesh files to disk.
    /// 
    /// TL;DR: Pathfinding works without prebuilding. This just pre-warms the cache.
    /// </summary>
    public static class PathfindingPrebuilder
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static int _running;
        private static CancellationTokenSource _cts;
        private static Task _task;

        public static bool IsRunning => Interlocked.CompareExchange(ref _running, 0, 0) == 1;

        public static int LandblocksTotal { get; private set; }
        private static int _processed;
        private static int _built;
        public static int LandblocksProcessed => Interlocked.CompareExchange(ref _processed, 0, 0);
        public static int LandblocksBuilt => Interlocked.CompareExchange(ref _built, 0, 0);

        public static void Initialize()
        {
            // Always try to auto-extract any shipped pack zip(s) first - that way a fresh
            // checkout/install gets a fully-warm cache without ever running the slow build.
            try
            {
                ExtractShippedPacks();
            }
            catch (Exception ex)
            {
                log.Warn($"PathfindingPrebuilder: pack extraction failed: {ex.Message}");
            }

            if (!PropertyManager.GetBool("pathfinding_prebuild_on_boot").Item)
            {
                log.Info("Pathfinding prebuild on boot is disabled (pathfinding_prebuild_on_boot=false). Navmeshes will be generated on-demand as needed.");
                return;
            }

            log.Info("Pathfinding prebuild on boot is enabled. Pre-generating all navmeshes...");
            Start();
        }

        /// <summary>
        /// Looks for *.zip files in the "Pathfinding\Pack" folder next to the executing assembly,
        /// and extracts any "Indoors/" or "Outdoors/" *.mesh entries into the active mesh root,
        /// skipping files that already exist on disk. Each pack is extracted at most once per
        /// install: a sentinel file is written next to the zip after a successful extract.
        /// </summary>
        private static void ExtractShippedPacks()
        {
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(asmDir))
                return;

            var packDir = Path.Combine(asmDir, "Pathfinding", "Pack");
            if (!Directory.Exists(packDir))
                return;

            var zips = Directory.GetFiles(packDir, "*.zip", SearchOption.TopDirectoryOnly);
            if (zips.Length == 0)
                return;

            var indoorRoot = Pathfinder.InsideMeshDirectory;
            var outdoorRoot = Pathfinder.OutsideMeshDirectory;
            Directory.CreateDirectory(indoorRoot);
            Directory.CreateDirectory(outdoorRoot);

            foreach (var zipPath in zips)
            {
                var sentinel = zipPath + ".extracted";
                try
                {
                    if (File.Exists(sentinel) && File.GetLastWriteTimeUtc(sentinel) >= File.GetLastWriteTimeUtc(zipPath))
                        continue;

                    log.Info($"PathfindingPrebuilder: extracting shipped navmesh pack '{Path.GetFileName(zipPath)}'...");

                    int extracted = 0, skipped = 0;
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
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
                                targetRoot = indoorRoot;
                                relative = normalized.Substring("Indoors/".Length);
                            }
                            else if (normalized.StartsWith("Outdoors/", StringComparison.OrdinalIgnoreCase))
                            {
                                targetRoot = outdoorRoot;
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

                            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                            {
                                skipped++;
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                            entry.ExtractToFile(targetPath, overwrite: true);
                            extracted++;
                        }
                    }

                    File.WriteAllText(sentinel, DateTime.UtcNow.ToString("o"));
                    log.Info($"PathfindingPrebuilder: pack '{Path.GetFileName(zipPath)}' extracted {extracted} new mesh file(s) ({skipped} already present).");
                }
                catch (Exception ex)
                {
                    log.Warn($"PathfindingPrebuilder: failed to extract pack '{Path.GetFileName(zipPath)}': {ex.Message}");
                }
            }
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
            Interlocked.Exchange(ref _processed, 0);
            Interlocked.Exchange(ref _built, 0);

            var configured = (int)PropertyManager.GetLong("pathfinding_prebuild_threads").Item;
            var threads = configured > 0
                ? configured
                : Math.Max(1, Environment.ProcessorCount - 2);

            log.Info($"PathfindingPrebuilder: starting on {threads} thread(s). Outdoor landblocks: {outdoor.Count}, Indoor landblocks: {indoor.Count}.");

            var sw = Stopwatch.StartNew();
            var lastReport = new long[] { sw.ElapsedMilliseconds };

            // Outdoor first since they cover the open world.
            ProcessSet(outdoor, isIndoors: false, threads, token, lastReport, sw);
            ProcessSet(indoor, isIndoors: true, threads, token, lastReport, sw);

            sw.Stop();
            if (token.IsCancellationRequested)
                log.Info($"PathfindingPrebuilder: cancelled after {LandblocksProcessed}/{LandblocksTotal} landblocks ({LandblocksBuilt} built) in {sw.Elapsed.TotalMinutes:F1} min.");
            else
                log.Info($"PathfindingPrebuilder: complete. {LandblocksProcessed}/{LandblocksTotal} landblocks scanned, {LandblocksBuilt} new mesh sets built in {sw.Elapsed.TotalMinutes:F1} min.");
        }

        private static void ProcessSet(HashSet<uint> set, bool isIndoors, int threads, CancellationToken token, long[] lastReport, Stopwatch sw)
        {
            if (set.Count == 0)
                return;

            var ordered = set.OrderBy(x => x).ToArray();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, threads),
                CancellationToken = token
            };

            try
            {
                Parallel.ForEach(ordered, options, landblockId =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    try
                    {
                        if (Pathfinder.PrebuildLandblockMesh(landblockId, isIndoors))
                            Interlocked.Increment(ref _built);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"PathfindingPrebuilder: failed to prebuild {landblockId:X8} (indoors={isIndoors}): {ex.Message}");
                    }

                    var processed = Interlocked.Increment(ref _processed);

                    var now = sw.ElapsedMilliseconds;
                    var prev = Interlocked.Read(ref lastReport[0]);
                    if (now - prev >= 30_000 && Interlocked.CompareExchange(ref lastReport[0], now, prev) == prev)
                    {
                        log.Info($"PathfindingPrebuilder: progress {processed}/{LandblocksTotal} ({LandblocksBuilt} built so far)...");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // expected when /pathfinding prebuild stop is invoked
            }
        }
    }
}
