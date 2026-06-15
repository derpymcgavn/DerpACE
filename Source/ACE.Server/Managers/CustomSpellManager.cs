using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using log4net;

using ACE.Database;
using ACE.Database.SQLFormatters.World;
using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;

using DbSpell = ACE.Database.Models.World.Spell;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Loads Aquafir-style custom spell JSON overrides into the runtime DAT spell table
    /// and world spell cache. This lets server code create named/iconed custom spells
    /// without a permanent world database migration for every experiment.
    /// </summary>
    public static class CustomSpellManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const uint WellFedSpellId = 65001;
        public const uint MeteorSquallSpellId = 65002;
        public const uint SpiralStarSpellId = 65003;
        public const uint ChainLightningSpellId = 65004;
        public const uint VoidConfusionSpellId = 65005;
        public const uint MeteorFragmentSpellId = 65006;
        public const uint SpiralStarLashSpellId = 65007;
        public const uint ChainLightningArcSpellId = 65008;

        public static bool IsCustomWarProjectileSpell(uint spellId)
        {
            return spellId == MeteorSquallSpellId
                || spellId == SpiralStarSpellId
                || spellId == ChainLightningSpellId
                || spellId == VoidConfusionSpellId
                || spellId == MeteorFragmentSpellId
                || spellId == SpiralStarLashSpellId
                || spellId == ChainLightningArcSpellId;
        }

        private const string ContentDirectoryEnvVar = "DERPACE_CUSTOM_SPELLS_DIR";
        private const uint FirstCustomSpellId = 65001;
        private const uint LastCustomSpellId = ushort.MaxValue;
        private const string SqlJsonBeginMarker = "-- DERPACE_CUSTOM_SPELL_JSON_BEGIN";
        private const string SqlJsonEndMarker = "-- DERPACE_CUSTOM_SPELL_JSON_END";

        public static string ContentDir { get; private set; }

        private static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        public static void Initialize(string contentDir = null)
        {
            ContentDir = ResolveContentDir(contentDir);
            Directory.CreateDirectory(ContentDir);
            EnsureDefaultWellFedSpell();
            EnsureDefaultWarMageSpells();
            EnsureDefaultVoidConfusionSpell();

            var loaded = LoadAll();
            ForceLoadDefaultWellFedSpell();
            ForceLoadDefaultWarMageVisualSpells();
            EnsureWarMageSpecialTrajectories();
            EnsureVoidConfusionVisuals();
            log.Info($"CustomSpellManager: Initialized. Loaded {loaded} custom spell definition(s) from {ContentDir}.");
        }

        public static int Reload()
        {
            var loaded = LoadAll();
            ForceLoadDefaultWellFedSpell();
            ForceLoadDefaultWarMageVisualSpells();
            EnsureWarMageSpecialTrajectories();
            EnsureVoidConfusionVisuals();
            log.Info($"CustomSpellManager: Reloaded {loaded} custom spell definition(s) from {ContentDir}.");
            return loaded;
        }

        public static bool EnsureWellFedSpellLoaded()
        {
            if (!new Spell(WellFedSpellId).NotFound)
                return true;

            if (string.IsNullOrWhiteSpace(ContentDir))
                ContentDir = ResolveContentDir(null);

            Directory.CreateDirectory(ContentDir);
            EnsureDefaultWellFedSpell();

            var path = Path.Combine(ContentDir, "WellFed.json");
            if (File.Exists(path))
                LoadFile(path);

            ForceLoadDefaultWellFedSpell();

            return !new Spell(WellFedSpellId).NotFound;
        }

        private static void ForceLoadDefaultWellFedSpell()
        {
            using var doc = JsonDocument.Parse(DefaultWellFedJson, JsonOptions);
            if (TryGet(doc.RootElement, "CustomSpells", out var customSpells) && customSpells.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in customSpells.EnumerateArray())
                    TryApply(entry, "built-in WellFed.json");
            }
        }

        private static void ForceLoadDefaultWarMageVisualSpells()
        {
            using var doc = JsonDocument.Parse(DefaultWarMageVisualSpellsJson, JsonOptions);
            if (TryGet(doc.RootElement, "CustomSpells", out var customSpells) && customSpells.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in customSpells.EnumerateArray())
                    TryApply(entry, "built-in WarMageVisuals.json");
            }
        }

        public static bool TryExportSql(uint spellId, bool asCopy, out string path, out uint exportedId, out string error)
        {
            path = null;
            error = null;
            exportedId = spellId;

            EnsureContentDirectory();

            var sourceDbSpell = DatabaseManager.World.GetCachedSpell(spellId);
            var sourceSpellBase = new Spell(spellId)._spellBase;

            if (sourceDbSpell == null || sourceSpellBase == null)
            {
                error = $"Spell {spellId} was not found.";
                return false;
            }

            if (asCopy && !TryGetNextUnusedCustomSpellId(Math.Max(spellId + 1, FirstCustomSpellId), out exportedId))
            {
                error = $"No unused custom spell ID was found from {FirstCustomSpellId} to {LastCustomSpellId}.";
                return false;
            }

            var dbSpell = CloneDbSpell(sourceDbSpell);
            var spellBase = CloneSpellBase(sourceSpellBase);

            dbSpell.Id = exportedId;
            SetSpellBase(spellBase, nameof(SpellBase.MetaSpellId), exportedId);

            var sqlDir = GetSqlDirectory();
            Directory.CreateDirectory(sqlDir);

            path = Path.Combine(sqlDir, GetSqlFileName(exportedId, spellBase.Name, asCopy));
            var json = CreateExportJson(spellId, exportedId, spellBase, dbSpell);

            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("-- DerpACE custom spell export");
                writer.WriteLine($"-- Source spell: {spellId}");
                writer.WriteLine($"-- Exported spell: {exportedId}");
                writer.WriteLine("-- Edit the JSON block for runtime DAT-side spell fields; edit the SQL INSERT for world spell table fields.");
                writer.WriteLine("-- Re-import with: @customspells import " + Path.GetFileName(path));
                writer.WriteLine(SqlJsonBeginMarker);
                foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    writer.WriteLine("-- " + line);
                writer.WriteLine(SqlJsonEndMarker);
                writer.WriteLine();

                var sqlWriter = new SpellSQLWriter();
                sqlWriter.CreateSQLDELETEStatement(dbSpell, writer);
                writer.WriteLine();
                sqlWriter.CreateSQLINSERTStatement(dbSpell, writer);
            }

            if (asCopy)
                ApplyExportedClone(exportedId, spellBase, dbSpell);

            return true;
        }

        public static bool TryImportSql(string fileName, out int loaded, out string path, out string error)
        {
            loaded = 0;
            path = ResolveImportPath(fileName);
            error = null;

            if (!File.Exists(path))
            {
                error = $"Custom spell import file was not found: {path}";
                return false;
            }

            var json = ExtractCustomSpellJson(File.ReadAllLines(path));
            if (string.IsNullOrWhiteSpace(json))
            {
                error = $"Custom spell import file does not contain a {SqlJsonBeginMarker} block.";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json, JsonOptions);
                loaded = LoadJsonRoot(doc.RootElement, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                log.Error($"CustomSpellManager: Error importing '{path}': {ex.Message}", ex);
                return false;
            }
        }

        private static string ResolveContentDir(string contentDir)
        {
            if (!string.IsNullOrWhiteSpace(contentDir))
                return Path.GetFullPath(contentDir);

            var envDir = Environment.GetEnvironmentVariable(ContentDirectoryEnvVar);
            if (!string.IsNullOrWhiteSpace(envDir))
                return Path.GetFullPath(envDir);

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "CustomSpells");
        }

        private static void EnsureContentDirectory()
        {
            if (string.IsNullOrWhiteSpace(ContentDir))
                ContentDir = ResolveContentDir(null);

            Directory.CreateDirectory(ContentDir);
        }

        private static string GetSqlDirectory()
        {
            EnsureContentDirectory();
            return Path.Combine(ContentDir, "Sql");
        }

        private static int LoadAll()
        {
            if (!Directory.Exists(ContentDir))
                return 0;

            var loaded = 0;
            foreach (var path in Directory.GetFiles(ContentDir, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p))
            {
                try
                {
                    loaded += LoadFile(path);
                }
                catch (Exception ex)
                {
                    log.Error($"CustomSpellManager: Error loading '{path}': {ex.Message}", ex);
                }
            }

            return loaded;
        }

        private static int LoadFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
            return LoadJsonRoot(doc.RootElement, path);
        }

        private static int LoadJsonRoot(JsonElement root, string sourcePath)
        {
            var loaded = 0;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root.EnumerateArray())
                    if (TryApply(entry, sourcePath))
                        loaded++;
            }
            else if (TryGet(root, "CustomSpells", out var customSpells) && customSpells.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in customSpells.EnumerateArray())
                    if (TryApply(entry, sourcePath))
                        loaded++;
            }
            else if (root.ValueKind == JsonValueKind.Object && TryApply(root, sourcePath))
            {
                loaded++;
            }

            return loaded;
        }

        private static bool TryApply(JsonElement element, string sourcePath)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryGet(element, "Template", out var templateElement) || !TryReadSpellId(templateElement, out var templateId))
            {
                log.Warn($"CustomSpellManager: '{Path.GetFileName(sourcePath)}' skipped entry without a valid Template.");
                return false;
            }

            var targetId = TryGet(element, "Id", out var idElement) && TryReadSpellId(idElement, out var parsedId)
                ? parsedId
                : templateId;

            var templateDbSpell = DatabaseManager.World.GetCachedSpell(templateId);
            var templateSpellBase = new Spell(templateId)._spellBase;

            if (templateDbSpell == null || templateSpellBase == null)
            {
                log.Warn($"CustomSpellManager: '{Path.GetFileName(sourcePath)}' skipped {templateId} -> {targetId}; template spell not found.");
                return false;
            }

            var dbSpell = CloneDbSpell(templateDbSpell);
            var spellBase = CloneSpellBase(templateSpellBase);

            Apply(element, targetId, spellBase, dbSpell);

            DatManager.PortalDat.SpellTable.Spells[targetId] = spellBase;
            GetSpellCache()[targetId] = dbSpell;

            log.Info($"CustomSpellManager: Loaded spell {targetId} '{spellBase.Name}' from template {templateId} ({Path.GetFileName(sourcePath)}).");
            return true;
        }

        private static void Apply(JsonElement element, uint targetId, SpellBase spellBase, DbSpell dbSpell)
        {
            SetSpellBase(spellBase, nameof(SpellBase.MetaSpellId), targetId);
            dbSpell.Id = targetId;

            if (TryReadString(element, "Name", out var name))
            {
                SetSpellBase(spellBase, nameof(SpellBase.Name), name);
                dbSpell.Name = name;
            }

            if (TryReadString(element, "SpellWords", out var spellWords))
                SetSpellWords(spellBase, spellWords);

            if (TryReadString(element, "Desc", out var desc))
                SetSpellBase(spellBase, nameof(SpellBase.Desc), desc);

            if (TryReadEnum(element, "School", out MagicSchool school))
                SetSpellBase(spellBase, nameof(SpellBase.School), school);

            if (TryReadUInt(element, "Icon", out var icon))
                SetSpellBase(spellBase, nameof(SpellBase.Icon), icon);

            if (TryReadEnum(element, "Category", out SpellCategory category))
                SetSpellBase(spellBase, nameof(SpellBase.Category), category);
            else if (TryReadUInt(element, "Category", out var categoryId))
                SetSpellBase(spellBase, nameof(SpellBase.Category), (SpellCategory)categoryId);

            if (TryReadEnumFlags(element, "Bitfield", out SpellFlags bitfield))
                SetSpellBase(spellBase, nameof(SpellBase.Bitfield), (uint)bitfield);
            else if (TryReadUInt(element, "Bitfield", out var rawBitfield))
                SetSpellBase(spellBase, nameof(SpellBase.Bitfield), rawBitfield);

            if (TryReadEnum(element, "MetaSpellType", out SpellType metaSpellType))
                SetSpellBase(spellBase, nameof(SpellBase.MetaSpellType), metaSpellType);

            if (TryReadUInt(element, "BaseMana", out var baseMana))
                SetSpellBase(spellBase, nameof(SpellBase.BaseMana), baseMana);

            if (TryReadFloat(element, "BaseRangeConstant", out var baseRangeConstant))
                SetSpellBase(spellBase, nameof(SpellBase.BaseRangeConstant), baseRangeConstant);

            if (TryReadFloat(element, "BaseRangeMod", out var baseRangeMod))
                SetSpellBase(spellBase, nameof(SpellBase.BaseRangeMod), baseRangeMod);

            if (TryReadUInt(element, "Power", out var power))
                SetSpellBase(spellBase, nameof(SpellBase.Power), power);

            if (TryReadDouble(element, "Duration", out var duration))
                SetSpellBase(spellBase, nameof(SpellBase.Duration), duration);

            if (TryReadEnum(element, "CasterEffect", out PlayScript casterEffect))
                SetSpellBase(spellBase, nameof(SpellBase.CasterEffect), (uint)casterEffect);

            if (TryReadEnum(element, "TargetEffect", out PlayScript targetEffect))
                SetSpellBase(spellBase, nameof(SpellBase.TargetEffect), (uint)targetEffect);

            if (TryReadEnumFlags(element, "NonComponentTargetType", out ItemType targetType))
                SetSpellBase(spellBase, nameof(SpellBase.NonComponentTargetType), (uint)targetType);
            else if (TryReadUInt(element, "NonComponentTargetType", out var rawTargetType))
                SetSpellBase(spellBase, nameof(SpellBase.NonComponentTargetType), rawTargetType);

            if (TryReadEnumFlags(element, "StatModType", out EnchantmentTypeFlags statModType))
                dbSpell.StatModType = (uint)statModType;
            else if (TryReadUInt(element, "StatModType", out var rawStatModType))
                dbSpell.StatModType = rawStatModType;

            if (TryReadUInt(element, "StatModKey", out var statModKey))
                dbSpell.StatModKey = statModKey;

            if (TryReadFloat(element, "StatModVal", out var statModVal))
                dbSpell.StatModVal = statModVal;

            if (TryReadEnumFlags(element, "EType", out DamageType eType))
                dbSpell.EType = (uint)eType;
            else if (TryReadUInt(element, "EType", out var rawEType))
                dbSpell.EType = rawEType;

            if (TryReadEnumFlags(element, "DamageType", out DamageType damageType))
                dbSpell.DamageType = (int)damageType;
            else if (TryReadInt(element, "DamageType", out var rawDamageType))
                dbSpell.DamageType = rawDamageType;

            if (TryReadInt(element, "BaseIntensity", out var baseIntensity))
                dbSpell.BaseIntensity = baseIntensity;

            if (TryReadInt(element, "Variance", out var variance))
                dbSpell.Variance = variance;

            if (TryReadInt(element, "NumProjectiles", out var numProjectiles))
                dbSpell.NumProjectiles = numProjectiles;

            if (TryReadInt(element, "NumProjectilesVariance", out var numProjectilesVariance))
                dbSpell.NumProjectilesVariance = numProjectilesVariance;

            if (TryReadFloat(element, "SpreadAngle", out var spreadAngle))
                dbSpell.SpreadAngle = spreadAngle;

            if (TryReadFloat(element, "VerticalAngle", out var verticalAngle))
                dbSpell.VerticalAngle = verticalAngle;

            if (TryReadFloat(element, "DefaultLaunchAngle", out var defaultLaunchAngle))
                dbSpell.DefaultLaunchAngle = defaultLaunchAngle;

            if (TryReadBool(element, "NonTracking", out var nonTracking))
                dbSpell.NonTracking = nonTracking;

            if (TryReadVector3(element, "CreateOffset", out var createOffset))
            {
                dbSpell.CreateOffsetOriginX = createOffset.X;
                dbSpell.CreateOffsetOriginY = createOffset.Y;
                dbSpell.CreateOffsetOriginZ = createOffset.Z;
            }

            if (TryReadVector3(element, "Padding", out var padding))
            {
                dbSpell.PaddingOriginX = padding.X;
                dbSpell.PaddingOriginY = padding.Y;
                dbSpell.PaddingOriginZ = padding.Z;
            }

            if (TryReadVector3(element, "Dims", out var dims))
            {
                dbSpell.DimsOriginX = dims.X;
                dbSpell.DimsOriginY = dims.Y;
                dbSpell.DimsOriginZ = dims.Z;
            }

            if (TryReadVector3(element, "Peturbation", out var peturbation))
            {
                dbSpell.PeturbationOriginX = peturbation.X;
                dbSpell.PeturbationOriginY = peturbation.Y;
                dbSpell.PeturbationOriginZ = peturbation.Z;
            }

            if (TryReadDouble(element, "DotDuration", out var dotDuration))
                dbSpell.DotDuration = dotDuration;

            if (TryReadUInt(element, "Wcid", out var wcid))
                dbSpell.Wcid = wcid;

            if (TryReadUIntList(element, "Formula", out var formula))
                SetSpellBase(spellBase, nameof(SpellBase.Formula), formula);

            if (TryGet(element, "SpellBase", out var spellBaseElement) && spellBaseElement.ValueKind == JsonValueKind.Object)
                ApplySpellBaseProperties(spellBaseElement, spellBase);

            if (TryGet(element, "DbSpell", out var dbSpellElement) && dbSpellElement.ValueKind == JsonValueKind.Object)
                ApplyDbSpellProperties(dbSpellElement, dbSpell);

            SetSpellBase(spellBase, nameof(SpellBase.MetaSpellId), targetId);
            dbSpell.Id = targetId;
        }

        private static SpellBase CloneSpellBase(SpellBase source)
        {
            var clone = new SpellBase();
            foreach (var property in typeof(SpellBase).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var setter = property.GetSetMethod(true);
                if (setter == null)
                    continue;

                var value = property.GetValue(source);
                if (value is List<uint> formula)
                    value = formula.ToList();

                setter.Invoke(clone, new[] { value });
            }

            return clone;
        }

        private static DbSpell CloneDbSpell(DbSpell source)
        {
            var clone = new DbSpell();
            foreach (var property in typeof(DbSpell).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                property.SetValue(clone, property.GetValue(source));
            }

            return clone;
        }

        private static void SetSpellBase(SpellBase spellBase, string propertyName, object value)
        {
            var property = typeof(SpellBase).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var setter = property?.GetSetMethod(true);
            if (setter == null)
                throw new InvalidOperationException($"SpellBase.{propertyName} has no setter.");

            setter.Invoke(spellBase, new[] { value });
        }

        private static void SetSpellWords(SpellBase spellBase, string spellWords)
        {
            var field = typeof(SpellBase).GetField("spellWords", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("SpellBase.spellWords backing field was not found.");

            field.SetValue(spellBase, spellWords);
        }

        private static ConcurrentDictionary<uint, DbSpell> GetSpellCache()
        {
            var type = DatabaseManager.World.GetType();
            while (type != null)
            {
                var field = type.GetField("spellCache", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(DatabaseManager.World) is ConcurrentDictionary<uint, DbSpell> cache)
                    return cache;
                type = type.BaseType;
            }

            throw new InvalidOperationException("Unable to find world spell cache.");
        }

        private static void ApplyExportedClone(uint spellId, SpellBase spellBase, DbSpell dbSpell)
        {
            DatManager.PortalDat.SpellTable.Spells[spellId] = spellBase;
            GetSpellCache()[spellId] = dbSpell;
            log.Info($"CustomSpellManager: Exported and loaded cloned spell {spellId} '{spellBase.Name}'.");
        }

        private static bool TryGetNextUnusedCustomSpellId(uint startAt, out uint spellId)
        {
            for (spellId = Math.Max(startAt, FirstCustomSpellId); spellId <= LastCustomSpellId; spellId++)
            {
                var inPortalTable = DatManager.PortalDat.SpellTable.Spells.ContainsKey(spellId);
                var inWorldTable = DatabaseManager.World.GetCachedSpell(spellId) != null;

                if (!inPortalTable && !inWorldTable)
                    return true;
            }

            spellId = 0;
            return false;
        }

        private static string ResolveImportPath(string fileName)
        {
            EnsureContentDirectory();

            if (Path.IsPathRooted(fileName))
                return Path.GetFullPath(fileName);

            var sqlPath = Path.Combine(GetSqlDirectory(), fileName);
            if (File.Exists(sqlPath))
                return sqlPath;

            return Path.Combine(ContentDir, fileName);
        }

        private static string ExtractCustomSpellJson(string[] lines)
        {
            var sb = new StringBuilder();
            var inBlock = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

                if (line.Equals(SqlJsonBeginMarker, StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = true;
                    continue;
                }

                if (line.Equals(SqlJsonEndMarker, StringComparison.OrdinalIgnoreCase))
                    break;

                if (!inBlock)
                    continue;

                if (line.StartsWith("-- "))
                    line = line.Substring(3);
                else if (line.StartsWith("--"))
                    line = line.Substring(2);

                sb.AppendLine(line);
            }

            return sb.ToString();
        }

        private static string CreateExportJson(uint templateId, uint targetId, SpellBase spellBase, DbSpell dbSpell)
        {
            var export = new Dictionary<string, object>
            {
                ["Template"] = templateId,
                ["Id"] = targetId,
                ["Name"] = spellBase.Name,
                ["SpellWords"] = spellBase.GetSpellWords(DatManager.PortalDat.SpellComponentsTable),
                ["Desc"] = spellBase.Desc,
                ["Icon"] = $"0x{spellBase.Icon:X8}",
                ["SpellBase"] = CreateSpellBaseExport(spellBase),
                ["DbSpell"] = CreateDbSpellExport(dbSpell)
            };

            return JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            });
        }

        private static Dictionary<string, object> CreateSpellBaseExport(SpellBase spellBase)
        {
            var values = new Dictionary<string, object>();

            foreach (var property in typeof(SpellBase).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.Name == nameof(SpellBase.MetaSpellId))
                    continue;

                values[property.Name] = FormatJsonValue(property.GetValue(spellBase));
            }

            values["SpellWords"] = spellBase.GetSpellWords(DatManager.PortalDat.SpellComponentsTable);
            return values;
        }

        private static Dictionary<string, object> CreateDbSpellExport(DbSpell dbSpell)
        {
            var values = new Dictionary<string, object>();

            foreach (var property in typeof(DbSpell).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.Name == nameof(DbSpell.Id))
                    continue;

                var value = property.GetValue(dbSpell);
                if (value != null)
                    values[property.Name] = FormatJsonValue(value);
            }

            return values;
        }

        private static object FormatJsonValue(object value)
        {
            if (value == null)
                return null;

            var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

            if (type.IsEnum)
                return value.ToString();

            return value;
        }

        private static string GetSqlFileName(uint spellId, string spellName, bool asCopy)
        {
            var suffix = asCopy ? " copy" : "";
            var fileName = $"{spellId:00000} {spellName}{suffix}.sql";

            foreach (var c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            return fileName;
        }

        private static void ApplySpellBaseProperties(JsonElement element, SpellBase spellBase)
        {
            foreach (var jsonProperty in element.EnumerateObject())
            {
                if (jsonProperty.NameEquals("SpellWords") || jsonProperty.NameEquals(nameof(SpellBase.MetaSpellId)))
                    continue;

                var property = typeof(SpellBase).GetProperty(jsonProperty.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                var setter = property?.GetSetMethod(true);
                if (setter == null)
                    continue;

                if (TryConvertJsonValue(jsonProperty.Value, property.PropertyType, out var value))
                    setter.Invoke(spellBase, new[] { value });
            }
        }

        private static void ApplyDbSpellProperties(JsonElement element, DbSpell dbSpell)
        {
            foreach (var jsonProperty in element.EnumerateObject())
            {
                if (jsonProperty.NameEquals(nameof(DbSpell.Id)))
                    continue;

                var property = typeof(DbSpell).GetProperty(jsonProperty.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property == null || !property.CanWrite)
                    continue;

                if (TryConvertJsonValue(jsonProperty.Value, property.PropertyType, out var value))
                    property.SetValue(dbSpell, value);
            }
        }

        private static bool TryConvertJsonValue(JsonElement element, Type targetType, out object value)
        {
            var nullableType = Nullable.GetUnderlyingType(targetType);
            var actualType = nullableType ?? targetType;

            if (element.ValueKind == JsonValueKind.Null)
            {
                value = null;
                return nullableType != null || !actualType.IsValueType;
            }

            if (actualType == typeof(string))
            {
                value = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                return true;
            }

            if (actualType == typeof(bool))
            {
                if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    value = element.GetBoolean();
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsedBool))
                {
                    value = parsedBool;
                    return true;
                }
            }

            if (actualType.IsEnum)
            {
                if (element.ValueKind == JsonValueKind.String && Enum.TryParse(actualType, element.GetString(), true, out value))
                    return true;

                if (TryReadUInt(element, out var enumRaw))
                {
                    value = Enum.ToObject(actualType, enumRaw);
                    return true;
                }
            }

            if (actualType == typeof(uint) && TryReadUInt(element, out var uintValue))
            {
                value = uintValue;
                return true;
            }

            if (actualType == typeof(int) && TryReadUInt(element, out var intValue) && intValue <= int.MaxValue)
            {
                value = (int)intValue;
                return true;
            }

            if (actualType == typeof(float))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var floatValue))
                {
                    value = floatValue;
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String && float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                {
                    value = floatValue;
                    return true;
                }
            }

            if (actualType == typeof(double))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                {
                    value = doubleValue;
                    return true;
                }
            }

            if (actualType == typeof(DateTime))
            {
                if (element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
                {
                    value = dateTime;
                    return true;
                }
            }

            if (actualType == typeof(List<uint>) && element.ValueKind == JsonValueKind.Array)
            {
                var list = new List<uint>();
                foreach (var item in element.EnumerateArray())
                {
                    if (TryReadUInt(item, out var parsed))
                        list.Add(parsed);
                }

                value = list;
                return true;
            }

            value = null;
            return false;
        }

        private struct JsonVector3
        {
            public float X;
            public float Y;
            public float Z;
        }

        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
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

        private static bool TryReadString(JsonElement element, string name, out string value)
        {
            if (TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }

            value = null;
            return false;
        }

        private static bool TryReadUInt(JsonElement element, string name, out uint value)
        {
            if (!TryGet(element, name, out var property))
            {
                value = 0;
                return false;
            }

            return TryReadUInt(property, out value);
        }

        private static bool TryReadInt(JsonElement element, string name, out int value)
        {
            if (TryReadUInt(element, name, out var parsed) && parsed <= int.MaxValue)
            {
                value = (int)parsed;
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryReadBool(JsonElement element, string name, out bool value)
        {
            if (!TryGet(element, name, out var property))
            {
                value = false;
                return false;
            }

            if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out value))
                return true;

            if (TryReadUInt(property, out var raw))
            {
                value = raw != 0;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryReadUInt(JsonElement property, out uint value)
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out value))
                return true;

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

                    return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }
            }

            value = 0;
            return false;
        }

        private static bool TryReadDouble(JsonElement element, string name, out double value)
        {
            if (!TryGet(element, name, out var property))
            {
                value = 0;
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
                return true;

            if (property.ValueKind == JsonValueKind.String)
                return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            value = 0;
            return false;
        }

        private static bool TryReadFloat(JsonElement element, string name, out float value)
        {
            if (TryReadDouble(element, name, out var parsed))
            {
                value = (float)parsed;
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryReadUIntList(JsonElement element, string name, out List<uint> value)
        {
            if (TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                value = new List<uint>();
                foreach (var item in property.EnumerateArray())
                {
                    if (TryReadUInt(item, out var parsed))
                        value.Add(parsed);
                }

                return true;
            }

            value = null;
            return false;
        }

        private static bool TryReadVector3(JsonElement element, string name, out JsonVector3 value)
        {
            if (!TryGet(element, name, out var property))
            {
                value = default;
                return false;
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                var parts = property.EnumerateArray().ToList();
                if (parts.Count >= 3
                    && TryReadFloat(parts[0], out var x)
                    && TryReadFloat(parts[1], out var y)
                    && TryReadFloat(parts[2], out var z))
                {
                    value = new JsonVector3 { X = x, Y = y, Z = z };
                    return true;
                }
            }

            if (property.ValueKind == JsonValueKind.Object
                && TryReadFloat(property, "X", out var objX)
                && TryReadFloat(property, "Y", out var objY)
                && TryReadFloat(property, "Z", out var objZ))
            {
                value = new JsonVector3 { X = objX, Y = objY, Z = objZ };
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryReadFloat(JsonElement property, out float value)
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out value))
                return true;

            if (property.ValueKind == JsonValueKind.String)
                return float.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            value = 0;
            return false;
        }

        private static bool TryReadSpellId(JsonElement property, out uint value)
        {
            if (TryReadUInt(property, out value))
                return true;

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString()?.Trim();
                if (Enum.TryParse(text, true, out SpellId spellId))
                {
                    value = (uint)spellId;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static bool TryReadEnum<TEnum>(JsonElement element, string name, out TEnum value)
            where TEnum : struct, Enum
        {
            if (!TryGet(element, name, out var property))
            {
                value = default;
                return false;
            }

            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse(property.GetString(), true, out value))
                return true;

            if (TryReadUInt(property, out var raw))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryReadEnumFlags<TEnum>(JsonElement element, string name, out TEnum value)
            where TEnum : struct, Enum
        {
            return TryReadEnum(element, name, out value);
        }

        private static void EnsureDefaultWellFedSpell()
        {
            var path = Path.Combine(ContentDir, "WellFed.json");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, DefaultWellFedJson);
        }

        private static void EnsureDefaultWarMageSpells()
        {
            var path = Path.Combine(ContentDir, "WarMageSpecials.json");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, DefaultWarMageSpecialsJson);
        }

        private static void EnsureDefaultVoidConfusionSpell()
        {
            var path = Path.Combine(ContentDir, "WarMageSpecials.json");
            if (File.Exists(path)
                && File.ReadAllText(path).Contains("\"Id\": 65005")
                && File.ReadAllText(path).Contains("\"BlackMadness\""))
                return;

            var fallbackPath = Path.Combine(ContentDir, "VoidConfusion.json");
            if (File.Exists(fallbackPath) && File.ReadAllText(fallbackPath).Contains("\"BlackMadness\""))
                return;

            File.WriteAllText(fallbackPath, DefaultVoidConfusionJson);
        }

        private static void EnsureWarMageSpecialTrajectories()
        {
            NormalizeProjectileSpell(
                MeteorSquallSpellId,
                SpellId.FlameBolt7,
                "Meteor Squall",
                DamageType.Fire,
                baseIntensity: 125,
                variance: 55,
                numProjectiles: 1,
                spreadAngle: 0.0f,
                nonTracking: true,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 4.5f },
                padding: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });

            NormalizeProjectileSpell(
                SpiralStarSpellId,
                SpellId.ForceRing,
                "Spiral Star",
                DamageType.Bludgeon,
                baseIntensity: 105,
                variance: 45,
                numProjectiles: 5,
                spreadAngle: 360.0f,
                nonTracking: true,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.25f },
                padding: new JsonVector3 { X = 0.75f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });

            NormalizeProjectileSpell(
                ChainLightningSpellId,
                SpellId.LightningBolt7,
                "Chain Lightning",
                DamageType.Electric,
                baseIntensity: 115,
                variance: 45,
                numProjectiles: 1,
                spreadAngle: 0.0f,
                nonTracking: false,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.35f },
                padding: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });

            NormalizeProjectileSpell(
                MeteorFragmentSpellId,
                SpellId.FlameBolt7,
                "Meteor Fragment",
                DamageType.Fire,
                baseIntensity: 75,
                variance: 25,
                numProjectiles: 1,
                spreadAngle: 0.0f,
                nonTracking: true,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 6.0f },
                padding: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });

            NormalizeProjectileSpell(
                SpiralStarLashSpellId,
                SpellId.ForceBolt7,
                "Spiral Force",
                DamageType.Bludgeon,
                baseIntensity: 65,
                variance: 20,
                numProjectiles: 1,
                spreadAngle: 0.0f,
                nonTracking: false,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.75f },
                padding: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });

            NormalizeProjectileSpell(
                ChainLightningArcSpellId,
                SpellId.LightningBolt7,
                "Chain Lightning Arc",
                DamageType.Electric,
                baseIntensity: 80,
                variance: 25,
                numProjectiles: 1,
                spreadAngle: 0.0f,
                nonTracking: false,
                createOffset: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.35f },
                padding: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f },
                peturbation: new JsonVector3 { X = 0.0f, Y = 0.0f, Z = 0.0f });
        }

        private static void NormalizeProjectileSpell(uint spellId, SpellId templateSpellId, string name, DamageType damageType, int baseIntensity, int variance, int numProjectiles, float spreadAngle, bool nonTracking, JsonVector3 createOffset, JsonVector3 padding, JsonVector3 peturbation)
        {
            var dbSpell = DatabaseManager.World.GetCachedSpell(spellId);
            if (dbSpell == null || !DatManager.PortalDat.SpellTable.Spells.TryGetValue(spellId, out _))
                return;

            var templateDbSpell = DatabaseManager.World.GetCachedSpell((uint)templateSpellId);

            dbSpell.Name = name;
            dbSpell.Wcid = templateDbSpell?.Wcid ?? dbSpell.Wcid;
            dbSpell.EType = (uint)damageType;
            dbSpell.DamageType = (int)damageType;
            dbSpell.BaseIntensity = baseIntensity;
            dbSpell.Variance = variance;
            dbSpell.NumProjectiles = numProjectiles;
            dbSpell.NumProjectilesVariance = 0;
            dbSpell.SpreadAngle = spreadAngle;
            dbSpell.NonTracking = nonTracking;
            dbSpell.CreateOffsetOriginX = createOffset.X;
            dbSpell.CreateOffsetOriginY = createOffset.Y;
            dbSpell.CreateOffsetOriginZ = createOffset.Z;
            dbSpell.PaddingOriginX = padding.X;
            dbSpell.PaddingOriginY = padding.Y;
            dbSpell.PaddingOriginZ = padding.Z;
            dbSpell.PeturbationOriginX = peturbation.X;
            dbSpell.PeturbationOriginY = peturbation.Y;
            dbSpell.PeturbationOriginZ = peturbation.Z;

            GetSpellCache()[spellId] = dbSpell;
        }

        private static void EnsureVoidConfusionVisuals()
        {
            if (new Spell(VoidConfusionSpellId).NotFound)
            {
                using var doc = JsonDocument.Parse(DefaultVoidConfusionJson, JsonOptions);
                if (TryGet(doc.RootElement, "CustomSpells", out var customSpells) && customSpells.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in customSpells.EnumerateArray())
                        TryApply(entry, "built-in VoidConfusion.json");
                }
            }

            if (!DatManager.PortalDat.SpellTable.Spells.TryGetValue(VoidConfusionSpellId, out var spellBase))
                return;

            var dbSpell = DatabaseManager.World.GetCachedSpell(VoidConfusionSpellId);
            if (dbSpell == null)
                return;

            SetSpellBase(spellBase, nameof(SpellBase.Name), "Void Confusion");
            SetSpellWords(spellBase, "Void Confusion");
            SetSpellBase(spellBase, nameof(SpellBase.Desc), "A weak nether bolt that erupts into Bedlam, briefly turning nearby monsters against each other.");
            SetSpellBase(spellBase, nameof(SpellBase.Icon), 0x06002860u);
            SetSpellBase(spellBase, nameof(SpellBase.CasterEffect), (uint)PlayScript.SpecialStatePurple);
            SetSpellBase(spellBase, nameof(SpellBase.TargetEffect), (uint)PlayScript.BlackMadness);

            dbSpell.Name = "Void Confusion";
            dbSpell.Wcid = DatabaseManager.World.GetCachedSpell((uint)SpellId.NetherBolt7)?.Wcid ?? dbSpell.Wcid;
            dbSpell.EType = (uint)DamageType.Nether;
            dbSpell.DamageType = (int)DamageType.Nether;
            dbSpell.BaseIntensity = 30;
            dbSpell.Variance = 15;
            dbSpell.NumProjectiles = 1;
            dbSpell.NumProjectilesVariance = 0;
            dbSpell.SpreadAngle = 0;
            dbSpell.NonTracking = false;

            GetSpellCache()[VoidConfusionSpellId] = dbSpell;
        }

        private const string DefaultWellFedJson =
@"{
  ""CustomSpells"": [
    {
      ""Template"": ""SetSocietyAttributeAll1"",
      ""Id"": 65001,
      ""Name"": ""WELL FED"",
      ""SpellWords"": ""WELL FED"",
      ""Desc"": ""Increases all primary attributes by 5."",
      ""Icon"": ""0x06001B3C"",
      ""Category"": 9040,
      ""Duration"": 7200,
      ""StatModVal"": 5,
      ""CasterEffect"": ""EnchantUpYellow"",
      ""TargetEffect"": ""EnchantUpYellow""
    }
  ]
}
";

        private const string DefaultWarMageVisualSpellsJson =
@"{
  ""CustomSpells"": [
    {
      ""Template"": ""FlameBolt7"",
      ""Id"": 65006,
      ""Name"": ""Meteor Fragment"",
      ""SpellWords"": ""Meteor Fragment"",
      ""Desc"": ""A falling ember called by Meteor Squall."",
      ""Icon"": ""0x06001036"",
      ""EType"": ""Fire"",
      ""DamageType"": ""Fire"",
      ""BaseMana"": 1,
      ""BaseIntensity"": 75,
      ""Variance"": 25,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": true,
      ""CreateOffset"": [0, 0, 6.0],
      ""Padding"": [0, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpRed"",
      ""TargetEffect"": ""BreatheFlame""
    },
    {
      ""Template"": ""ForceBolt7"",
      ""Id"": 65007,
      ""Name"": ""Spiral Force"",
      ""SpellWords"": ""Spiral Force"",
      ""Desc"": ""A visible bludgeoning pulse from Spiral Star."",
      ""Icon"": ""0x0600103C"",
      ""EType"": ""Bludgeon"",
      ""DamageType"": ""Bludgeon"",
      ""BaseMana"": 1,
      ""BaseIntensity"": 65,
      ""Variance"": 20,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": false,
      ""CreateOffset"": [0, 0, 0.75],
      ""Padding"": [0, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpYellow"",
      ""TargetEffect"": ""HealthDownYellow""
    },
    {
      ""Template"": ""LightningBolt7"",
      ""Id"": 65008,
      ""Name"": ""Chain Lightning Arc"",
      ""SpellWords"": ""Chain Lightning Arc"",
      ""Desc"": ""A visible jumping arc from Chain Lightning."",
      ""Icon"": ""0x06001039"",
      ""EType"": ""Electric"",
      ""DamageType"": ""Electric"",
      ""BaseMana"": 1,
      ""BaseIntensity"": 80,
      ""Variance"": 25,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": false,
      ""CreateOffset"": [0, 0, 0.35],
      ""Padding"": [0, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpBlue"",
      ""TargetEffect"": ""BreatheLightning""
    }
  ]
}
";

        private const string DefaultWarMageSpecialsJson =
@"{
  ""CustomSpells"": [
    {
      ""Template"": ""FlameBolt7"",
      ""Id"": 65002,
      ""Name"": ""Meteor Squall"",
      ""SpellWords"": ""Meteor Squall"",
      ""Desc"": ""Calls a burning projectile from the sky. Outdoors only; on impact, embers rain over nearby foes."",
      ""Icon"": ""0x06001036"",
      ""EType"": ""Fire"",
      ""DamageType"": ""Fire"",
      ""BaseMana"": 72,
      ""BaseIntensity"": 125,
      ""Variance"": 55,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": true,
      ""CreateOffset"": [0, 0, 4.5],
      ""Padding"": [0, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpRed"",
      ""TargetEffect"": ""BreatheFlame""
    },
    {
      ""Template"": ""ForceRing"",
      ""Id"": 65003,
      ""Name"": ""Spiral Star"",
      ""SpellWords"": ""Spiral Star"",
      ""Desc"": ""Launches a circling bludgeoning force that unwinds outward from the caster to nearby foes."",
      ""Icon"": ""0x0600103C"",
      ""EType"": ""Bludgeon"",
      ""DamageType"": ""Bludgeon"",
      ""BaseMana"": 68,
      ""BaseIntensity"": 105,
      ""Variance"": 45,
      ""NumProjectiles"": 5,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 360,
      ""NonTracking"": true,
      ""CreateOffset"": [0, 0, 0.25],
      ""Padding"": [0.75, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpYellow"",
      ""TargetEffect"": ""HealthDownYellow""
    },
    {
      ""Template"": ""LightningBolt7"",
      ""Id"": 65004,
      ""Name"": ""Chain Lightning"",
      ""SpellWords"": ""Chain Lightning"",
      ""Desc"": ""A lightning bolt that leaps through nearby foes, losing strength with each jump."",
      ""Icon"": ""0x06001039"",
      ""EType"": ""Electric"",
      ""DamageType"": ""Electric"",
      ""BaseMana"": 75,
      ""BaseIntensity"": 115,
      ""Variance"": 45,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": false,
      ""CreateOffset"": [0, 0, 0.35],
      ""Padding"": [0, 0, 0],
      ""Peturbation"": [0, 0, 0],
      ""CasterEffect"": ""EnchantUpBlue"",
      ""TargetEffect"": ""BreatheLightning""
    },
    {
      ""Template"": ""NetherBolt7"",
      ""Id"": 65005,
      ""Name"": ""Void Confusion"",
      ""SpellWords"": ""Void Confusion"",
      ""Desc"": ""A weak nether bolt that briefly turns nearby monsters against each other."",
      ""Icon"": ""0x06002860"",
      ""EType"": ""Nether"",
      ""DamageType"": ""Nether"",
      ""BaseMana"": 80,
      ""BaseIntensity"": 30,
      ""Variance"": 15,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": false,
      ""CasterEffect"": ""SpecialStatePurple"",
      ""TargetEffect"": ""BlackMadness""
    }
  ]
}
";

        private const string DefaultVoidConfusionJson =
@"{
  ""CustomSpells"": [
    {
      ""Template"": ""NetherBolt7"",
      ""Id"": 65005,
      ""Name"": ""Void Confusion"",
      ""SpellWords"": ""Void Confusion"",
      ""Desc"": ""A weak nether bolt that briefly turns nearby monsters against each other."",
      ""Icon"": ""0x06002860"",
      ""EType"": ""Nether"",
      ""DamageType"": ""Nether"",
      ""BaseMana"": 80,
      ""BaseIntensity"": 30,
      ""Variance"": 15,
      ""NumProjectiles"": 1,
      ""NumProjectilesVariance"": 0,
      ""SpreadAngle"": 0,
      ""NonTracking"": false,
      ""CasterEffect"": ""SpecialStatePurple"",
      ""TargetEffect"": ""BlackMadness""
    }
  ]
}
";
    }
}
