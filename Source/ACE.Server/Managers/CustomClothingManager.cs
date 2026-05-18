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

            // Wire the hook BEFORE loading so any concurrent ReadFromDat<ClothingTable> call
            // that races with us still goes through MergeCustom.
            DatDatabase.ClothingTableMergeHook = MergeCustom;

            LoadAll();

            // Flush any ClothingTable entries that may already have been cached by an earlier
            // ReadFromDat<ClothingTable> call (e.g. CharGen warmup) so the merge is applied.
            var flushed = ClearCache();

            log.Info($"CustomClothingManager: Initialized. Loaded {_custom.Count} custom clothing table(s) from {ContentDir} (flushed {flushed} cached entries).");
        }

        /// <summary>Reload all JSON files from disk and flush the ClothingTable cache.</summary>
        public static void Reload()
        {
            _custom.Clear();
            LoadAll();
            var flushed = ClearCache();
            log.Info($"CustomClothingManager: Reloaded {_custom.Count} custom clothing table(s) (flushed {flushed} cached entries).");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Loading
        // ──────────────────────────────────────────────────────────────────────

        private static void LoadAll()
        {
            if (!Directory.Exists(ContentDir))
            {
                log.Warn($"CustomClothingManager: Content directory does not exist: {ContentDir}");
                return;
            }

            var files = Directory.GetFiles(ContentDir, "*.json");
            if (files.Length == 0)
            {
                log.Info($"CustomClothingManager: No *.json overrides found in {ContentDir}");
                return;
            }

            int loaded = 0;
            foreach (var path in files)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var table = ParseJson(json);
                    if (table == null)
                    {
                        log.Warn($"CustomClothingManager: '{path}' parsed to null table.");
                        continue;
                    }

                    // Resolve the table Id from the filename if the JSON didn't supply one,
                    // and reconcile decimal-vs-hex filename forms (e.g. "268437777.json" and
                    // "10004611.json" both resolve to 0x10004611 — same uint). The filename
                    // accepts: "<id>.json" or "<id>_label.json", and <id> may be:
                    //   * hex with 0x prefix:    0x10004611
                    //   * 8-char hex (no prefix): 10004611
                    //   * pure decimal:          268437777
                    // ClothingBase IDs always fall in [0x10000000, 0x10FFFFFF], which is
                    // used to disambiguate "pure decimal in the hex range" from "hex digits".
                    var fileNameId = TryParseIdFromFileName(path);

                    if (table.Id == 0 && fileNameId != 0)
                    {
                        table.Id = fileNameId;
                    }
                    else if (table.Id != 0 && fileNameId != 0 && table.Id != fileNameId)
                    {
                        log.Warn($"CustomClothingManager: '{Path.GetFileName(path)}' filename id 0x{fileNameId:X8} ({fileNameId}) does not match JSON Id 0x{table.Id:X8} ({table.Id}). Using JSON Id.");
                    }

                    if (table.Id == 0)
                    {
                        log.Warn($"CustomClothingManager: '{Path.GetFileName(path)}' has no Id field and the filename could not be parsed as a ClothingBase id. Skipped.");
                        continue;
                    }

                    if (table.Id < 0x10000000 || table.Id > 0x10FFFFFF)
                        log.Warn($"CustomClothingManager: '{Path.GetFileName(path)}' Id 0x{table.Id:X8} ({table.Id}) is outside the ClothingBase range [0x10000000, 0x10FFFFFF]; loading anyway.");

                    _custom[table.Id] = table;
                    loaded++;
                    log.Debug($"CustomClothingManager: Loaded 0x{table.Id:X8} ({table.Id}) from {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    log.Error($"CustomClothingManager: Error loading '{path}': {ex.Message}");
                }
            }

            log.Info($"CustomClothingManager: Loaded {loaded}/{files.Length} custom clothing table(s) from {ContentDir}");
        }

        /// <summary>
        /// Parses a ClothingBase id from a file name. Supports "0x10004611", "10004611"
        /// (8-char hex), and "268437777" (decimal). Any "_label" suffix is ignored.
        /// Returns 0 when the leading token can't be parsed as either form.
        /// </summary>
        private static uint TryParseIdFromFileName(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path) ?? string.Empty;

            // Take everything up to the first '_' or '-' so "10004611_male_plate" works.
            var sepIdx = stem.IndexOfAny(new[] { '_', '-', ' ' });
            var idToken = sepIdx >= 0 ? stem.Substring(0, sepIdx) : stem;

            if (string.IsNullOrEmpty(idToken))
                return 0;

            // Explicit hex prefix wins.
            if (idToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(idToken.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexId)
                    ? hexId : 0u;
            }

            // Heuristic: 8 hex digits with at least one non-decimal digit → hex.
            // Otherwise try decimal first, then 8-char hex as a fallback so plain "10004611"
            // (which is also a valid decimal) resolves to the hex value 0x10004611 — the form
            // weenies usually store the reference as a uint.
            bool hasNonDecimal = false;
            foreach (var c in idToken)
            {
                if (!char.IsDigit(c))
                {
                    hasNonDecimal = true;
                    break;
                }
            }

            if (hasNonDecimal)
            {
                return uint.TryParse(idToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexId)
                    ? hexId : 0u;
            }

            if (uint.TryParse(idToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decId))
            {
                // If decimal parse lands in the ClothingBase range, take it as-is.
                if (decId >= 0x10000000 && decId <= 0x10FFFFFF)
                    return decId;

                // Otherwise, if the token is exactly 8 digits, prefer the hex interpretation
                // since that's the canonical 0x1________ ClothingBase form.
                if (idToken.Length == 8 &&
                    uint.TryParse(idToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexId8) &&
                    hexId8 >= 0x10000000 && hexId8 <= 0x10FFFFFF)
                {
                    return hexId8;
                }

                return decId;
            }

            return 0;
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
            // Build a lookup of setupId -> (race, sex) for all player setups
            var setupIdToLabel = new Dictionary<uint, string>();
            try
            {
                var charGen = ACE.DatLoader.DatManager.PortalDat?.CharGen;
                if (charGen != null)
                {
                    foreach (var hg in charGen.HeritageGroups.Values)
                    {
                        foreach (var gender in hg.Genders.Values)
                        {
                            // e.g. "Aluvian Male"
                            var label = $"{hg.Name} {gender.Name}";
                            setupIdToLabel[gender.SetupID] = label;
                        }
                    }
                }
            }
            catch { /* ignore errors, fallback to no comments */ }

            foreach (var kv in table.ClothingBaseEffects)
            {
                var effectNode = new JsonObject();
                var effectsArr = new JsonArray();

                // If this setupId is a known player race/sex, add a _comment
                if (setupIdToLabel.TryGetValue(kv.Key, out var label))
                    effectNode["_comment"] = label;

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
