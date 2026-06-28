using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static class DungeonInstanceManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static uint nextInstanceId;
        private static readonly ConcurrentDictionary<uint, DungeonInstanceInfo> instances = new ConcurrentDictionary<uint, DungeonInstanceInfo>();

        public static DungeonInstanceInfo Create(LandblockId templateLandblockId, string createdBy, string sourceName = null, DungeonInstanceDefinition definition = null)
        {
            var templateLandblock = LandblockManager.GetLandblock(templateLandblockId, 0, false);
            if (!templateLandblock.IsDungeon)
                return null;

            var instanceId = Interlocked.Increment(ref nextInstanceId);
            var instance = new DungeonInstanceInfo(instanceId, templateLandblock.Id, createdBy, DateTime.UtcNow, sourceName);
            instances[instanceId] = instance;

            var landblock = LandblockManager.GetLandblock(templateLandblock.Id, instanceId, false);
            if (definition?.Objects != null && definition.Objects.Count > 0)
                EnqueueDecorationLoad(landblock, definition.Objects);

            return instance;
        }

        public static DungeonInstanceInfo CreateFromSaved(string name, string createdBy, out DungeonInstanceDefinition definition)
        {
            definition = LoadDefinition(name);
            if (definition == null)
                return null;

            return Create(new LandblockId(((uint)definition.TemplateLandblock << 16) | 0xFFFF), createdBy, definition.Name, definition);
        }

        public static DungeonInstanceInfo Get(uint instanceId)
        {
            instances.TryGetValue(instanceId, out var instance);
            return instance;
        }

        public static IReadOnlyCollection<DungeonInstanceInfo> List()
        {
            return instances.Values.OrderBy(i => i.InstanceId).ToList();
        }

        public static IReadOnlyCollection<DungeonInstanceSavedSummary> ListSaved()
        {
            EnsureStorageDirectory();

            return Directory.GetFiles(StorageDirectory, "*.json")
                .Select(path => LoadDefinition(Path.GetFileNameWithoutExtension(path)))
                .Where(definition => definition != null)
                .OrderBy(definition => definition.Name)
                .Select(definition => new DungeonInstanceSavedSummary(definition.Name, definition.TemplateLandblock, definition.Objects?.Count ?? 0, definition.SavedAt, definition.SavedBy))
                .ToList();
        }

        public static DungeonInstanceDefinition LoadDefinition(string name)
        {
            var path = GetDefinitionPath(name);
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<DungeonInstanceDefinition>(File.ReadAllText(path), JsonOptions);
        }

        public static DungeonInstanceDefinition Save(uint instanceId, string name, string savedBy, Position entryPosition)
        {
            if (!instances.TryGetValue(instanceId, out var instance))
                return null;

            var landblock = LandblockManager.GetLandblock(instance.TemplateLandblockId, instanceId, false);
            var objects = landblock.GetAllWorldObjectsForDiagnostics()
                .Where(wo => IsPersistableDecoration(landblock, wo))
                .Select(CreateRecord)
                .Where(record => record != null)
                .OrderBy(record => record.WeenieClassId)
                .ThenBy(record => record.Position.Cell)
                .ThenBy(record => record.Position.X)
                .ThenBy(record => record.Position.Y)
                .ToList();

            var definition = new DungeonInstanceDefinition
            {
                Name = SanitizeName(name),
                TemplateLandblock = instance.TemplateLandblockId.Landblock,
                SavedBy = savedBy,
                SavedAt = DateTime.UtcNow,
                Entry = PositionRecord.FromPosition(entryPosition),
                Objects = objects
            };

            EnsureStorageDirectory();
            File.WriteAllText(GetDefinitionPath(definition.Name), JsonSerializer.Serialize(definition, JsonOptions));
            return definition;
        }

        private static bool IsPersistableDecoration(Landblock landblock, WorldObject worldObject)
        {
            if (worldObject == null || worldObject.Location == null)
                return false;

            if (worldObject is Player || worldObject is SpellProjectile || worldObject is Corpse)
                return false;

            if (landblock.IsInstanceTemplateObject(worldObject))
                return false;

            return worldObject.Location.InstanceId == landblock.InstanceId;
        }

        private static DungeonInstanceObjectRecord CreateRecord(WorldObject worldObject)
        {
            if (worldObject.Location == null)
                return null;

            return new DungeonInstanceObjectRecord
            {
                WeenieClassId = worldObject.WeenieClassId,
                WeenieType = worldObject.WeenieType.ToString(),
                Name = worldObject.Name,
                Position = PositionRecord.FromPosition(worldObject.Location),
                PaletteTemplate = worldObject.PaletteTemplate,
                Shade = worldObject.Shade,
                StackSize = worldObject.StackSize,
                CreateList = worldObject.Biota.PropertiesCreateList?.Select(i => i.Clone()).ToList(),
                GeneratorProfiles = worldObject.Biota.PropertiesGenerator?.Select(i => i.Clone()).ToList(),
                Inventory = worldObject is Container container
                    ? container.Inventory.Values.Select(CreateInventoryRecord).Where(record => record != null).ToList()
                    : null
            };
        }

        private static DungeonInstanceObjectRecord CreateInventoryRecord(WorldObject worldObject)
        {
            if (worldObject == null)
                return null;

            return new DungeonInstanceObjectRecord
            {
                WeenieClassId = worldObject.WeenieClassId,
                WeenieType = worldObject.WeenieType.ToString(),
                Name = worldObject.Name,
                PaletteTemplate = worldObject.PaletteTemplate,
                Shade = worldObject.Shade,
                StackSize = worldObject.StackSize,
                CreateList = worldObject.Biota.PropertiesCreateList?.Select(i => i.Clone()).ToList(),
                GeneratorProfiles = worldObject.Biota.PropertiesGenerator?.Select(i => i.Clone()).ToList(),
                Inventory = worldObject is Container container
                    ? container.Inventory.Values.Select(CreateInventoryRecord).Where(record => record != null).ToList()
                    : null
            };
        }

        private static void EnqueueDecorationLoad(Landblock landblock, IReadOnlyCollection<DungeonInstanceObjectRecord> records)
        {
            landblock.EnqueueAction(new Entity.Actions.ActionEventDelegate(() =>
            {
                foreach (var record in records)
                {
                    var worldObject = WorldObjectFactory.CreateNewWorldObject(record.WeenieClassId);
                    if (worldObject == null)
                        continue;

                    ApplySavedState(worldObject, record);
                    worldObject.Location = record.Position.ToPosition(landblock.InstanceId);

                    landblock.AddWorldObject(worldObject);
                }
            }));
        }

        private static void ApplySavedState(WorldObject worldObject, DungeonInstanceObjectRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.Name))
                worldObject.Name = record.Name;
            if (record.PaletteTemplate.HasValue)
                worldObject.PaletteTemplate = record.PaletteTemplate.Value;
            if (record.Shade.HasValue)
                worldObject.Shade = record.Shade.Value;
            if (record.StackSize.HasValue && record.StackSize.Value > 0)
                worldObject.SetStackSize(record.StackSize.Value);

            if (record.CreateList != null)
                worldObject.Biota.PropertiesCreateList = new Collection<PropertiesCreateList>(record.CreateList.Select(i => i.Clone()).ToList());

            if (record.GeneratorProfiles != null)
            {
                worldObject.Biota.PropertiesGenerator = record.GeneratorProfiles.Select(i => i.Clone()).ToList();
                worldObject.InitializeGenerator();
                worldObject.ReinitializeHeartbeats();
            }

            if (worldObject is Container container && record.Inventory != null)
            {
                foreach (var childRecord in record.Inventory)
                {
                    var child = WorldObjectFactory.CreateNewWorldObject(childRecord.WeenieClassId);
                    if (child == null)
                        continue;

                    ApplySavedState(child, childRecord);
                    container.TryAddToInventory(child, burdenCheck: false);
                }
            }
        }

        private static void EnsureStorageDirectory()
        {
            Directory.CreateDirectory(StorageDirectory);
        }

        private static string GetDefinitionPath(string name)
        {
            return Path.Combine(StorageDirectory, $"{SanitizeName(name)}.json");
        }

        private static string SanitizeName(string name)
        {
            var safe = string.Join("_", (name ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
        }

        private static string StorageDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "DungeonInstances");
    }

    public class DungeonInstanceInfo
    {
        public DungeonInstanceInfo(uint instanceId, LandblockId templateLandblockId, string createdBy, DateTime createdAt, string sourceName = null)
        {
            InstanceId = instanceId;
            TemplateLandblockId = templateLandblockId;
            CreatedBy = createdBy;
            CreatedAt = createdAt;
            SourceName = sourceName;
        }

        public uint InstanceId { get; }
        public LandblockId TemplateLandblockId { get; }
        public string CreatedBy { get; }
        public DateTime CreatedAt { get; }
        public string SourceName { get; }
    }

    public class DungeonInstanceDefinition
    {
        public string Name { get; set; }
        public ushort TemplateLandblock { get; set; }
        public string SavedBy { get; set; }
        public DateTime SavedAt { get; set; }
        public PositionRecord Entry { get; set; }
        public List<DungeonInstanceObjectRecord> Objects { get; set; } = new List<DungeonInstanceObjectRecord>();
    }

    public class DungeonInstanceObjectRecord
    {
        public uint WeenieClassId { get; set; }
        public string WeenieType { get; set; }
        public string Name { get; set; }
        public PositionRecord Position { get; set; }
        public int? PaletteTemplate { get; set; }
        public double? Shade { get; set; }
        public int? StackSize { get; set; }
        public List<PropertiesCreateList> CreateList { get; set; }
        public List<PropertiesGenerator> GeneratorProfiles { get; set; }
        public List<DungeonInstanceObjectRecord> Inventory { get; set; }
    }

    public class PositionRecord
    {
        public uint Cell { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationW { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }

        public static PositionRecord FromPosition(Position position)
        {
            return new PositionRecord
            {
                Cell = position.Cell,
                X = position.PositionX,
                Y = position.PositionY,
                Z = position.PositionZ,
                RotationW = position.RotationW,
                RotationX = position.RotationX,
                RotationY = position.RotationY,
                RotationZ = position.RotationZ
            };
        }

        public Position ToPosition(uint instanceId)
        {
            return new Position(Cell, X, Y, Z, RotationX, RotationY, RotationZ, RotationW)
            {
                InstanceId = instanceId
            };
        }
    }

    public class DungeonInstanceSavedSummary
    {
        public DungeonInstanceSavedSummary(string name, ushort templateLandblock, int objectCount, DateTime savedAt, string savedBy)
        {
            Name = name;
            TemplateLandblock = templateLandblock;
            ObjectCount = objectCount;
            SavedAt = savedAt;
            SavedBy = savedBy;
        }

        public string Name { get; }
        public ushort TemplateLandblock { get; }
        public int ObjectCount { get; }
        public DateTime SavedAt { get; }
        public string SavedBy { get; }
    }
}
