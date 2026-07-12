using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using ACE.Database.Models.Shard;
using ACE.Entity.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public sealed class BossMechanicDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public uint WeenieClassId { get; set; }
        public List<BossMechanicRule> Rules { get; set; } = new List<BossMechanicRule>();
    }

    public sealed class BossMechanicRule
    {
        public string Id { get; set; }
        public string Trigger { get; set; } = "health_below";
        public double ThresholdPercent { get; set; }
        public bool Once { get; set; } = true;
        public List<BossMechanicAction> Actions { get; set; } = new List<BossMechanicAction>();
    }

    public sealed class BossMechanicAction
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Effect { get; set; }
    }

    public static class BossMechanicManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        private static readonly ConcurrentDictionary<uint, BossMechanicDocument> Published = new ConcurrentDictionary<uint, BossMechanicDocument>();
        private static readonly ConcurrentDictionary<uint, byte> Missing = new ConcurrentDictionary<uint, byte>();
        private static readonly ConcurrentDictionary<uint, HashSet<string>> FiredRules = new ConcurrentDictionary<uint, HashSet<string>>();
        private static bool StorageUnavailable;

        public static BossMechanicDocument NewDocument(uint wcid) => new BossMechanicDocument { WeenieClassId = wcid };
        public static string Serialize(BossMechanicDocument document) => JsonSerializer.Serialize(document, JsonOptions);
        public static BossMechanicDocument Deserialize(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<BossMechanicDocument>(json, JsonOptions);

        public static List<string> Validate(BossMechanicDocument document)
        {
            var errors = new List<string>();
            if (document == null) { errors.Add("Profile JSON is empty or invalid."); return errors; }
            if (document.SchemaVersion != 1) errors.Add($"Unsupported schema version {document.SchemaVersion}.");
            if (document.WeenieClassId == 0) errors.Add("Boss WCID must be nonzero.");
            if (document.Rules == null) errors.Add("Rules collection is missing.");
            if (document.Rules?.Count > 32) errors.Add("A profile may contain at most 32 rules.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in document.Rules ?? Enumerable.Empty<BossMechanicRule>())
            {
                if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) errors.Add("Every rule needs a unique non-empty ID.");
                if (!string.Equals(rule.Trigger, "health_below", StringComparison.OrdinalIgnoreCase)) errors.Add($"Rule {rule.Id}: unsupported trigger '{rule.Trigger}'.");
                if (rule.ThresholdPercent < 1 || rule.ThresholdPercent > 99) errors.Add($"Rule {rule.Id}: health threshold must be 1-99.");
                if (!rule.Once) errors.Add($"Rule {rule.Id}: repeating rules are disabled in schema v1.");
                if (rule.Actions == null || rule.Actions.Count == 0) errors.Add($"Rule {rule.Id}: at least one action is required.");
                if (rule.Actions?.Count > 8) errors.Add($"Rule {rule.Id}: at most 8 actions are allowed.");
                foreach (var action in rule.Actions ?? Enumerable.Empty<BossMechanicAction>())
                {
                    if (string.Equals(action.Type, "say", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > 240) errors.Add($"Rule {rule.Id}: speech must be 1-240 characters.");
                    }
                    else if (string.Equals(action.Type, "effect", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Enum.TryParse<PlayScript>(action.Effect, true, out _)) errors.Add($"Rule {rule.Id}: unknown effect '{action.Effect}'.");
                    }
                    else errors.Add($"Rule {rule.Id}: unsupported action '{action.Type}'.");
                }
            }
            return errors;
        }

        public static BossMechanicDocument GetPublished(uint wcid)
        {
            if (Published.TryGetValue(wcid, out var cached)) return cached;
            if (StorageUnavailable || Missing.ContainsKey(wcid)) return null;
            try
            {
                using var context = new ShardDbContext();
                var row = context.BossMechanicProfile.FirstOrDefault(x => x.WeenieClassId == wcid && x.Enabled);
                var document = Deserialize(row?.PublishedJson);
                if (document == null || Validate(document).Count > 0) { Missing[wcid] = 1; return null; }
                Published[wcid] = document;
                return document;
            }
            catch
            {
                // Missing schema or unavailable storage must never interrupt combat/world ticks.
                StorageUnavailable = true;
                return null;
            }
        }

        public static void Invalidate(uint wcid)
        {
            Published.TryRemove(wcid, out _);
            Missing.TryRemove(wcid, out _);
        }

        public static void OnHealthChanged(Creature boss, uint previousHealth)
        {
            if (boss == null || boss.Health.MaxValue == 0 || boss.Health.Current <= 0) return;
            var profile = GetPublished(boss.WeenieClassId);
            if (profile == null) return;
            var before = previousHealth * 100.0 / boss.Health.MaxValue;
            var after = boss.Health.Current * 100.0 / boss.Health.MaxValue;
            if (after >= before) return;

            var fired = FiredRules.GetOrAdd(boss.Guid.Full, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var rule in profile.Rules)
            {
                if (before <= rule.ThresholdPercent || after > rule.ThresholdPercent) continue;
                lock (fired) { if (!fired.Add(rule.Id)) continue; }
                foreach (var action in rule.Actions.Take(8)) ExecuteAction(boss, action);
            }
        }

        public static void Reset(Creature boss)
        {
            if (boss != null) FiredRules.TryRemove(boss.Guid.Full, out _);
        }

        private static void ExecuteAction(Creature boss, BossMechanicAction action)
        {
            try
            {
                if (string.Equals(action.Type, "say", StringComparison.OrdinalIgnoreCase))
                    boss.EnqueueBroadcast(new GameMessageHearSpeech(action.Text, boss.Name, boss.Guid.Full, ChatMessageType.Speech), WorldObject.LocalBroadcastRange);
                else if (string.Equals(action.Type, "effect", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<PlayScript>(action.Effect, true, out var effect))
                    boss.ApplyVisualEffects(effect);
            }
            catch { /* One bad action must never break the landblock tick. */ }
        }
    }
}