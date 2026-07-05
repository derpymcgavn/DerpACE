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
    /// Loads CustomClothingBase-compatible JSON files from Data/CustomClothingBase.
    /// New ClothingBase IDs are isolated. Existing portal.dat ClothingBase IDs are ignored
    /// unless the JSON explicitly sets AllowBaseOverride to true.
    /// </summary>
    public static class CustomClothingManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private const string ContentDirectoryEnvVar = "DERPACE_CUSTOM_CLOTHING_DIR";
        private const uint ClothingBaseMinId = 0x10000000;
        private const uint ClothingBaseMaxId = 0x10FFFFFF;

        public static string ContentDir { get; private set; }

        private static readonly ConcurrentDictionary<uint, ClothingTable> _custom = new ConcurrentDictionary<uint, ClothingTable>();
        private static readonly ConcurrentDictionary<uint, bool> _allowBaseOverride = new ConcurrentDictionary<uint, bool>();

        public static void Initialize(string contentDir = null)
        {
            ContentDir = ResolveContentDir(contentDir);
            Directory.CreateDirectory(ContentDir);

            DatDatabase.ClothingTableMergeHook = MergeCustom;

            _custom.Clear();
            _allowBaseOverride.Clear();
            LoadAll();

            var flushed = ClearCache();
            log.Info($"CustomClothingManager: Initialized. Loaded {_custom.Count} custom clothing table(s) from {ContentDir} (flushed {flushed} cached entries).");
        }

        public static void Reload()
        {
            _custom.Clear();
            _allowBaseOverride.Clear();
            LoadAll();

            var flushed = ClearCache();
            log.Info($"CustomClothingManager: Reloaded {_custom.Count} custom clothing table(s) (flushed {flushed} cached entries).");
        }

        private static string ResolveContentDir(string contentDir)
        {
            if (!string.IsNullOrWhiteSpace(contentDir))
                return Path.GetFullPath(contentDir);

            var envDir = System.Environment.GetEnvironmentVariable(ContentDirectoryEnvVar);
            if (!string.IsNullOrWhiteSpace(envDir))
                return Path.GetFullPath(envDir);

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CustomClothingBase");
        }

        private static void LoadAll()
        {
            if (!Directory.Exists(ContentDir))
            {
                log.Warn($"CustomClothingManager: Content directory does not exist: {ContentDir}");
                return;
            }

            var files = Directory.GetFiles(ContentDir, "*.json", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                log.Info($"CustomClothingManager: No *.json overrides found in {ContentDir}");
                return;
            }

            var loaded = 0;
            foreach (var path in files)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var table = ParseJson(json, out var allowBaseOverride);
                    if (table == null)
                    {
                        log.Warn($"CustomClothingManager: '{path}' parsed to null table.");
                        continue;
                    }

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

                    if (!IsClothingBaseId(table.Id))
                    {
                        log.Warn($"CustomClothingManager: '{Path.GetFileName(path)}' Id 0x{table.Id:X8} ({table.Id}) is outside the ClothingBase range [0x10000000, 0x10FFFFFF]. Skipped.");
                        continue;
                    }

                    if (DatManager.PortalDat?.AllFiles?.ContainsKey(table.Id) == true && !allowBaseOverride)
                    {
                        log.Warn($"CustomClothingManager: '{Path.GetFileName(path)}' targets existing portal.dat ClothingBase 0x{table.Id:X8}. Skipped to avoid changing every item that uses that base. Use @cbclone with a new custom ID, or set AllowBaseOverride=true if this is intentional.");
                        continue;
                    }

                    _custom[table.Id] = table;
                    _allowBaseOverride[table.Id] = allowBaseOverride;
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

        private static ClothingTable MergeCustom(uint fileId, ClothingTable existing)
        {
            if (!_custom.TryGetValue(fileId, out var custom))
                return existing;

            if (DatManager.PortalDat?.AllFiles?.ContainsKey(fileId) == true
                && (!_allowBaseOverride.TryGetValue(fileId, out var allowBaseOverride) || !allowBaseOverride))
                return existing;

            if (existing.Id == 0)
                existing.Id = fileId;

            foreach (var kv in custom.ClothingBaseEffects)
                existing.ClothingBaseEffects[kv.Key] = kv.Value;

            foreach (var kv in custom.ClothingSubPalEffects)
                existing.ClothingSubPalEffects[kv.Key] = kv.Value;

            return existing;
        }

        public static uint ClearCache()
        {
            if (DatManager.PortalDat?.FileCache == null)
                return 0;

            uint count = 0;
            foreach (var kv in DatManager.PortalDat.FileCache)
            {
                if (kv.Key >= ClothingBaseMinId && kv.Key <= ClothingBaseMaxId)
                {
                    if (DatManager.PortalDat.FileCache.TryRemove(kv.Key, out _))
                        count++;
                }
            }

            return count;
        }

        public static string Export(uint clothingBaseId, out string outPath, string label = null)
        {
            return Export(clothingBaseId, clothingBaseId, out outPath, label, true);
        }

        public static string ExportClone(uint sourceClothingBaseId, uint newClothingBaseId, out string outPath, string label = null)
        {
            return Export(sourceClothingBaseId, newClothingBaseId, out outPath, label, false);
        }

        private static string Export(uint sourceClothingBaseId, uint outputClothingBaseId, out string outPath, string label, bool allowBaseOverride)
        {
            outPath = null;

            if (!IsClothingBaseId(sourceClothingBaseId))
                return $"0x{sourceClothingBaseId:X8} is not a valid ClothingBase ID (range 0x10000000-0x10FFFFFF).";

            if (!IsClothingBaseId(outputClothingBaseId))
                return $"0x{outputClothingBaseId:X8} is not a valid ClothingBase ID (range 0x10000000-0x10FFFFFF).";

            if (!DatManager.PortalDat.AllFiles.ContainsKey(sourceClothingBaseId))
                return $"ClothingBase 0x{sourceClothingBaseId:X8} not found in portal.dat.";

            if (!allowBaseOverride && DatManager.PortalDat.AllFiles.ContainsKey(outputClothingBaseId))
                return $"New ClothingBase 0x{outputClothingBaseId:X8} already exists in portal.dat. Pick an unused custom ID so base items are not changed.";

            var table = DatManager.PortalDat.ReadFromDat<ClothingTable>(sourceClothingBaseId);
            table.Id = outputClothingBaseId;
            var json = SerializeJson(table, allowBaseOverride);

            var suffix = string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : "_" + System.Text.RegularExpressions.Regex.Replace(label.Trim(), @"[^\w\-. ]", string.Empty).Replace(' ', '_').TrimEnd('_', '.');

            outPath = Path.Combine(ContentDir, $"{outputClothingBaseId:X8}{suffix}.json");
            Directory.CreateDirectory(ContentDir);
            File.WriteAllText(outPath, json);

            return null;
        }

        private static ClothingTable ParseJson(string json, out bool allowBaseOverride)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = doc.RootElement;

            allowBaseOverride = false;
            if (TryGetProperty(root, "AllowBaseOverride", out var allowEl))
                allowBaseOverride = allowEl.ValueKind == JsonValueKind.True || (allowEl.ValueKind == JsonValueKind.String && bool.TryParse(allowEl.GetString(), out var parsed) && parsed);

            var table = new ClothingTable();

            if (TryGetProperty(root, "Id", out var idEl))
                table.Id = ParseClothingBaseId(idEl);

            if (TryGetProperty(root, "ClothingBaseEffects", out var cbeEl))
            {
                foreach (var prop in cbeEl.EnumerateObject())
                    table.ClothingBaseEffects[ParseUintStr(prop.Name)] = ParseClothingBaseEffect(prop.Value);
            }

            if (TryGetProperty(root, "ClothingSubPalEffects", out var cspeEl))
            {
                foreach (var prop in cspeEl.EnumerateObject())
                    table.ClothingSubPalEffects[ParseUintStr(prop.Name)] = ParseCloSubPalEffect(prop.Value);
            }

            return table;
        }

        private static ClothingBaseEffect ParseClothingBaseEffect(JsonElement el)
        {
            var effect = new ClothingBaseEffect();

            if (TryGetProperty(el, "CloObjectEffects", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var coe = new CloObjectEffect
                    {
                        Index = TryGetProperty(item, "Index", out var idx) ? ParseUint(idx) : 0,
                        ModelId = TryGetProperty(item, "ModelId", out var mid) ? ParseUint(mid) : 0,
                    };

                    if (TryGetProperty(item, "CloTextureEffects", out var texArr))
                    {
                        foreach (var tex in texArr.EnumerateArray())
                        {
                            coe.CloTextureEffects.Add(new CloTextureEffect
                            {
                                OldTexture = TryGetProperty(tex, "OldTexture", out var ot) ? ParseUint(ot) : 0,
                                NewTexture = TryGetProperty(tex, "NewTexture", out var nt) ? ParseUint(nt) : 0,
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

            if (TryGetProperty(el, "Icon", out var iconEl))
                effect.Icon = ParseUint(iconEl);

            if (TryGetProperty(el, "CloSubPalettes", out var palArr))
            {
                foreach (var item in palArr.EnumerateArray())
                {
                    var pal = new CloSubPalette
                    {
                        PaletteSet = TryGetProperty(item, "PaletteSet", out var ps) ? ParseUint(ps) : 0,
                    };

                    if (TryGetProperty(item, "Ranges", out var rangesEl))
                    {
                        foreach (var r in rangesEl.EnumerateArray())
                        {
                            pal.Ranges.Add(new CloSubPaletteRange
                            {
                                Offset = TryGetProperty(r, "Offset", out var off) ? ParseUint(off) : 0,
                                NumColors = TryGetProperty(r, "NumColors", out var nc) ? ParseUint(nc) : 0,
                            });
                        }
                    }

                    effect.CloSubPalettes.Add(pal);
                }
            }

            return effect;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static uint TryParseIdFromFileName(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var sepIdx = stem.IndexOfAny(new[] { '_', '-', ' ' });
            var idToken = sepIdx >= 0 ? stem.Substring(0, sepIdx) : stem;
            return string.IsNullOrEmpty(idToken) ? 0 : ParseClothingBaseIdToken(idToken);
        }

        private static bool IsClothingBaseId(uint id)
            => id >= ClothingBaseMinId && id <= ClothingBaseMaxId;

        private static uint ParseUint(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number)
                return el.GetUInt32();

            return ParseUintStr(el.GetString() ?? "0");
        }

        private static uint ParseClothingBaseId(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number)
                return el.GetUInt32();

            return ParseClothingBaseIdToken(el.GetString() ?? "0");
        }

        private static uint ParseClothingBaseIdToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;

            s = s.Trim();

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var prefixedHex) ? prefixedHex : 0u;

            foreach (var c in s)
            {
                if (!char.IsDigit(c))
                    return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : 0u;
            }

            if (!uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                return 0;

            if (IsClothingBaseId(dec))
                return dec;

            if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex8) && IsClothingBaseId(hex8))
                return hex8;

            return dec;
        }

        private static uint ParseUintStr(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            foreach (var c in s)
            {
                if (!char.IsDigit(c))
                    return uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return uint.Parse(s, CultureInfo.InvariantCulture);
        }

        private static string SerializeJson(ClothingTable table, bool allowBaseOverride = false)
        {
            var root = new JsonObject
            {
                ["Id"] = $"0x{table.Id:X8}"
            };

            if (allowBaseOverride)
                root["AllowBaseOverride"] = true;

            var cbeNode = new JsonObject();
            var setupIdToLabel = new Dictionary<uint, string>();
            try
            {
                var charGen = DatManager.PortalDat?.CharGen;
                if (charGen != null)
                {
                    foreach (var hg in charGen.HeritageGroups.Values)
                    foreach (var gender in hg.Genders.Values)
                        setupIdToLabel[gender.SetupID] = $"{hg.Name} {gender.Name}";
                }
            }
            catch { }

            foreach (var kv in table.ClothingBaseEffects)
            {
                var effectNode = new JsonObject();
                if (setupIdToLabel.TryGetValue(kv.Key, out var label))
                    effectNode["_comment"] = label;

                var effectsArr = new JsonArray();
                foreach (var coe in kv.Value.CloObjectEffects)
                {
                    var coeNode = new JsonObject
                    {
                        ["Index"] = coe.Index,
                        ["ModelId"] = $"0x{coe.ModelId:X8}",
                    };

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

            var cspeNode = new JsonObject();
            foreach (var kv in table.ClothingSubPalEffects)
            {
                var effectNode = new JsonObject
                {
                    ["Icon"] = $"0x{kv.Value.Icon:X8}"
                };

                var palArr = new JsonArray();
                foreach (var pal in kv.Value.CloSubPalettes)
                {
                    var palNode = new JsonObject
                    {
                        ["PaletteSet"] = $"0x{pal.PaletteSet:X8}"
                    };

                    var rangesArr = new JsonArray();
                    foreach (var r in pal.Ranges)
                    {
                        rangesArr.Add(new JsonObject
                        {
                            ["Offset"] = r.Offset,
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