using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using log4net;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Loads custom ClothingTable JSON overrides from Data/CustomClothingBase/ and
    /// merges them into portal.dat entries at runtime — no dat patching required.
    ///
    /// JSON files must be named {id:X8}.json (e.g. 10001234.json).
    /// Both new IDs (not in the portal.dat) and overrides for existing IDs are supported.
    ///
    /// JSON format mirrors the OptimShi/CustomClothingBase mod for file compatibility.
    /// Uint values may be decimal ("268435456") or hex ("0x10000000").
    ///
    /// Admin commands (Developer access):
    ///   @cbexport 0x10001234  — export an existing entry to JSON
    ///   @cbreload             — reload all JSON files and flush the ClothingTable cache
    /// </summary>
    public static class CustomClothingManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Path to the folder that holds JSON override files.</summary>
        public static string ContentDir { get; private set; }

        // Parsed custom tables, keyed by clothing table ID
        private static readonly ConcurrentDictionary<uint, ClothingTable> _custom
            = new ConcurrentDictionary<uint, ClothingTable>();

        // ──────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────────────────────────────

        public static void Initialize(string contentDir = null)
        {
            ContentDir = contentDir
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CustomClothingBase");

            Directory.CreateDirectory(ContentDir);
            LoadAll();

            // Wire the hook — fires inside DatDatabase.ReadFromDat<ClothingTable> before caching
            DatDatabase.ClothingTableMergeHook = MergeCustom;

            log.Info($"CustomClothingManager: Initialized. Watching {ContentDir}");
        }

        /// <summary>Reload all JSON files from disk and flush the ClothingTable cache.</summary>
        public static void Reload()
        {
            _custom.Clear();
            LoadAll();
            ClearCache();
            log.Info($"CustomClothingManager: Reloaded {_custom.Count} custom clothing table(s).");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Loading
        // ──────────────────────────────────────────────────────────────────────

        private static void LoadAll()
        {
            if (!Directory.Exists(ContentDir))
                return;

            int loaded = 0;
            foreach (var path in Directory.GetFiles(ContentDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var table = ParseJson(json);
                    if (table != null)
                    {
                        _custom[table.Id] = table;
                        loaded++;
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"CustomClothingManager: Error loading '{path}': {ex.Message}");
                }
            }

            if (loaded > 0)
                log.Info($"CustomClothingManager: Loaded {loaded} custom clothing table(s) from {ContentDir}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Hook implementation
        // ──────────────────────────────────────────────────────────────────────

        private static ClothingTable MergeCustom(uint fileId, ClothingTable existing)
        {
            if (!_custom.TryGetValue(fileId, out var custom))
                return existing;

            // For brand-new IDs the dat reader returned null so Id was never set
            if (existing.Id == 0)
                existing.Id = fileId;

            foreach (var kv in custom.ClothingBaseEffects)
            {
                if (existing.ClothingBaseEffects.ContainsKey(kv.Key))
                    existing.ClothingBaseEffects[kv.Key] = kv.Value;
                else
                    existing.ClothingBaseEffects.Add(kv.Key, kv.Value);
            }

            foreach (var kv in custom.ClothingSubPalEffects)
            {
                if (existing.ClothingSubPalEffects.ContainsKey(kv.Key))
                    existing.ClothingSubPalEffects[kv.Key] = kv.Value;
                else
                    existing.ClothingSubPalEffects.Add(kv.Key, kv.Value);
            }

            return existing;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Cache management
        // ──────────────────────────────────────────────────────────────────────

        public static uint ClearCache()
        {
            if (DatManager.PortalDat?.FileCache == null)
                return 0;

            uint count = 0;
            foreach (var kv in DatManager.PortalDat.FileCache)
            {
                if (kv.Key >= 0x10000000 && kv.Key <= 0x10FFFFFF)
                {
                    if (DatManager.PortalDat.FileCache.TryRemove(kv.Key, out _))
                        count++;
                }
            }

            return count;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Export
        // ──────────────────────────────────────────────────────────────────────

        public static string Export(uint clothingBaseId, out string outPath, string label = null)
        {
            outPath = null;

            if (clothingBaseId < 0x10000000 || clothingBaseId > 0x10FFFFFF)
                return $"0x{clothingBaseId:X8} is not a valid ClothingBase ID (range 0x10000000\u20130x10FFFFFF).";

            if (!DatManager.PortalDat.AllFiles.ContainsKey(clothingBaseId))
                return $"ClothingBase 0x{clothingBaseId:X8} not found in portal.dat.";

            var table = DatManager.PortalDat.ReadFromDat<ClothingTable>(clothingBaseId);
            var json = SerializeJson(table);

            // Sanitize label: strip path-unsafe characters, collapse whitespace to underscores
            var suffix = string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : "_" + System.Text.RegularExpressions.Regex.Replace(
                    label.Trim(),
                    @"[^\w\-. ]",
                    string.Empty).Replace(' ', '_').TrimEnd('_', '.');

            outPath = Path.Combine(ContentDir, $"{clothingBaseId:X8}{suffix}.json");
            Directory.CreateDirectory(ContentDir);
            File.WriteAllText(outPath, json);

            return null; // null = success
        }

        // ──────────────────────────────────────────────────────────────────────
        // JSON parsing
        // ──────────────────────────────────────────────────────────────────────

        private static ClothingTable ParseJson(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement;

            var table = new ClothingTable();

            if (root.TryGetProperty("Id", out var idEl))
                table.Id = ParseUint(idEl);

            if (root.TryGetProperty("ClothingBaseEffects", out var cbeEl))
            {
                foreach (var prop in cbeEl.EnumerateObject())
                {
                    uint key = ParseUintStr(prop.Name);
                    var effect = ParseClothingBaseEffect(prop.Value);
                    table.ClothingBaseEffects[key] = effect;
                }
            }

            if (root.TryGetProperty("ClothingSubPalEffects", out var cspeEl))
            {
                foreach (var prop in cspeEl.EnumerateObject())
                {
                    uint key = ParseUintStr(prop.Name);
                    var effect = ParseCloSubPalEffect(prop.Value);
                    table.ClothingSubPalEffects[key] = effect;
                }
            }

            return table;
        }

        private static ClothingBaseEffect ParseClothingBaseEffect(JsonElement el)
        {
            var effect = new ClothingBaseEffect();

            if (el.TryGetProperty("CloObjectEffects", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var coe = new CloObjectEffect
                    {
                        Index   = item.TryGetProperty("Index",   out var idx)  ? ParseUint(idx)  : 0,
                        ModelId = item.TryGetProperty("ModelId", out var mid)  ? ParseUint(mid)  : 0,
                    };

                    if (item.TryGetProperty("CloTextureEffects", out var texArr))
                    {
                        foreach (var tex in texArr.EnumerateArray())
                        {
                            coe.CloTextureEffects.Add(new CloTextureEffect
                            {
                                OldTexture = tex.TryGetProperty("OldTexture", out var ot) ? ParseUint(ot) : 0,
                                NewTexture = tex.TryGetProperty("NewTexture", out var nt) ? ParseUint(nt) : 0,
                            });
                        }
                    }

                    effect.CloObjectEffects.Add(coe);
                }
            }

            return effect;
        }

        private static CloSubPalEffect ParseCloSubPalEffect(JsonElement el)
        {
            var effect = new CloSubPalEffect();

            if (el.TryGetProperty("Icon", out var iconEl))
                effect.Icon = ParseUint(iconEl);

            if (el.TryGetProperty("CloSubPalettes", out var palArr))
            {
                foreach (var item in palArr.EnumerateArray())
                {
                    var pal = new CloSubPalette
                    {
                        PaletteSet = item.TryGetProperty("PaletteSet", out var ps) ? ParseUint(ps) : 0,
                    };

                    if (item.TryGetProperty("Ranges", out var rangesEl))
                    {
                        foreach (var r in rangesEl.EnumerateArray())
                        {
                            pal.Ranges.Add(new CloSubPaletteRange
                            {
                                Offset    = r.TryGetProperty("Offset",    out var off) ? ParseUint(off) : 0,
                                NumColors = r.TryGetProperty("NumColors", out var nc)  ? ParseUint(nc)  : 0,
                            });
                        }
                    }

                    effect.CloSubPalettes.Add(pal);
                }
            }

            return effect;
        }

        // Parses a JsonElement that may be decimal or hex string, or a plain JSON number
        private static uint ParseUint(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number)
                return el.GetUInt32();

            return ParseUintStr(el.GetString() ?? "0");
        }

        private static uint ParseUintStr(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return uint.Parse(s, CultureInfo.InvariantCulture);
        }

        // ──────────────────────────────────────────────────────────────────────
        // JSON serialization (for export)
        // ──────────────────────────────────────────────────────────────────────

        private static string SerializeJson(ClothingTable table)
        {
            var root = new JsonObject();
            root["Id"] = $"0x{table.Id:X8}";

            // ClothingBaseEffects
            var cbeNode = new JsonObject();
            foreach (var kv in table.ClothingBaseEffects)
            {
                var effectNode = new JsonObject();
                var effectsArr = new JsonArray();

                foreach (var coe in kv.Value.CloObjectEffects)
                {
                    var coeNode = new JsonObject();
                    coeNode["Index"]   = coe.Index;
                    coeNode["ModelId"] = $"0x{coe.ModelId:X8}";

                    var texArr = new JsonArray();
                    foreach (var tex in coe.CloTextureEffects)
                    {
                        texArr.Add(new JsonObject
                        {
                            ["OldTexture"] = $"0x{tex.OldTexture:X8}",
                            ["NewTexture"] = $"0x{tex.NewTexture:X8}",
                        });
                    }
                    coeNode["CloTextureEffects"] = texArr;
                    effectsArr.Add(coeNode);
                }

                effectNode["CloObjectEffects"] = effectsArr;
                cbeNode[$"0x{kv.Key:X8}"] = effectNode;
            }
            root["ClothingBaseEffects"] = cbeNode;

            // ClothingSubPalEffects
            var cspeNode = new JsonObject();
            foreach (var kv in table.ClothingSubPalEffects)
            {
                var effectNode = new JsonObject();
                effectNode["Icon"] = $"0x{kv.Value.Icon:X8}";

                var palArr = new JsonArray();
                foreach (var pal in kv.Value.CloSubPalettes)
                {
                    var palNode = new JsonObject();
                    palNode["PaletteSet"] = $"0x{pal.PaletteSet:X8}";

                    var rangesArr = new JsonArray();
                    foreach (var r in pal.Ranges)
                    {
                        rangesArr.Add(new JsonObject
                        {
                            ["Offset"]    = r.Offset,
                            ["NumColors"] = r.NumColors,
                        });
                    }
                    palNode["Ranges"] = rangesArr;
                    palArr.Add(palNode);
                }

                effectNode["CloSubPalettes"] = palArr;
                cspeNode[$"{kv.Key}"] = effectNode;
            }
            root["ClothingSubPalEffects"] = cspeNode;

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
