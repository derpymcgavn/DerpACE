using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using log4net;

using ACE.Common;
using ACE.Database.Models.Shard;
using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Physics.Extensions;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.Structure;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public sealed class BossMechanicDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public uint WeenieClassId { get; set; }
        public List<string> Mutators { get; set; } = new List<string>();
        public List<BossMechanicRule> Rules { get; set; } = new List<BossMechanicRule>();
    }

    public sealed class BossMechanicRule
    {
        public string Id { get; set; }
        public string Trigger { get; set; } = "health_below";
        public double ThresholdPercent { get; set; }
        public double IntervalSeconds { get; set; }
        public double ChancePercent { get; set; } = 100;
        public int MinPlayers { get; set; } = 1;
        public string DamageType { get; set; }
        public double DamagePercent { get; set; }
        public bool Once { get; set; } = true;
        public string Phase { get; set; }
        public List<BossMechanicAction> Actions { get; set; } = new List<BossMechanicAction>();
    }

    public sealed class BossMechanicAction
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Effect { get; set; }
        public string Channel { get; set; }
        public uint WeenieClassId { get; set; }
        public int Count { get; set; }
        public int Health { get; set; }
        public string Target { get; set; } = "trigger";
        public string Source { get; set; } = "nearby";
        public double Radius { get; set; } = WorldObject.LocalBroadcastRange;
        public double Distance { get; set; } = 10;
        public double DurationSeconds { get; set; } = 30;
        public bool NoXp { get; set; } = true;
        public bool DropItems { get; set; }
        public bool NoCorpse { get; set; } = true;
        public double Translucency { get; set; } = -1;
        public uint SpellId { get; set; }
        public string Phase { get; set; }
        public double DamageScale { get; set; } = 0.35;
    }

    public static class BossMechanicManager
    {
        private static readonly HashSet<string> BossSafeMutators = new HashSet<string>(new[] { "vampiric", "nocturnal", "exploding", "healer", "enchanter", "shaman", "tank", "reaper", "necromancer", "warder" }, StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        private static readonly ConcurrentDictionary<uint, BossMechanicDocument> Published = new ConcurrentDictionary<uint, BossMechanicDocument>();
        private static readonly ConcurrentDictionary<uint, IReadOnlyDictionary<string, BossMechanicRule[]>> CompiledRules = new ConcurrentDictionary<uint, IReadOnlyDictionary<string, BossMechanicRule[]>>();
        private static readonly ConcurrentDictionary<uint, byte> Missing = new ConcurrentDictionary<uint, byte>();
        private static readonly ConcurrentDictionary<uint, HashSet<string>> FiredRules = new ConcurrentDictionary<uint, HashSet<string>>();
        private static readonly ConcurrentDictionary<string, BossMinionEncounter> MinionEncounters = new ConcurrentDictionary<string, BossMinionEncounter>();
        private static readonly ConcurrentDictionary<uint, string> MinionOwners = new ConcurrentDictionary<uint, string>();
        private static readonly ConcurrentDictionary<uint, double> EncounterStarted = new ConcurrentDictionary<uint, double>();
        private static readonly ConcurrentDictionary<uint, string> ActivePhases = new ConcurrentDictionary<uint, string>();
        private static readonly ConcurrentDictionary<uint, List<BossAppliedEffect>> AppliedEffects = new ConcurrentDictionary<uint, List<BossAppliedEffect>>();
        private static readonly ConcurrentDictionary<string, double> LastRuleRun = new ConcurrentDictionary<string, double>();
        private static bool StorageUnavailable;
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

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
            if (document.Mutators?.Count > 3) errors.Add("A boss may have at most 3 mutator perks.");
            foreach (var mutator in document.Mutators ?? Enumerable.Empty<string>())
                if (!BossSafeMutators.Contains(CreatureMutatorManager.ResolveAlias(mutator))) errors.Add($"Unsupported boss mutator perk '{mutator}'.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in document.Rules ?? Enumerable.Empty<BossMechanicRule>())
            {
                if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) errors.Add("Every rule needs a unique non-empty ID.");
                if (rule.Phase?.Length > 40) errors.Add($"Rule {rule.Id}: required phase name must be at most 40 characters.");
                var trigger = rule.Trigger?.ToLowerInvariant();
                if (trigger != "health_below" && trigger != "combat_start" && trigger != "timer" && trigger != "spell_resisted" && trigger != "death" && trigger != "boss_evades" && trigger != "critical_hit" && trigger != "damage_type" && trigger != "large_hit")
                    errors.Add($"Rule {rule.Id}: unsupported trigger '{rule.Trigger}'.");
                if (trigger == "health_below" && (rule.ThresholdPercent < 1 || rule.ThresholdPercent > 99))
                    errors.Add($"Rule {rule.Id}: health threshold must be 1-99.");
                if (trigger == "timer" && (rule.IntervalSeconds < 1 || rule.IntervalSeconds > 3600))
                    errors.Add($"Rule {rule.Id}: timer interval must be 1-3600 seconds.");
                if (trigger == "damage_type" && !Enum.TryParse<DamageType>(rule.DamageType, true, out _))
                    errors.Add($"Rule {rule.Id}: choose a valid incoming damage type.");
                if (trigger == "large_hit" && (rule.DamagePercent <= 0 || rule.DamagePercent > 100))
                    errors.Add($"Rule {rule.Id}: large-hit percentage must be greater than 0 and at most 100%.");                if (trigger != "timer" && !rule.Once)
                    errors.Add($"Rule {rule.Id}: only timer rules may repeat.");
                if (rule.ChancePercent <= 0 || rule.ChancePercent > 100)
                    errors.Add($"Rule {rule.Id}: chance must be greater than 0 and at most 100%.");
                if (rule.MinPlayers < 1 || rule.MinPlayers > 40)
                    errors.Add($"Rule {rule.Id}: minimum nearby players must be 1-40.");                if (rule.Actions == null || rule.Actions.Count == 0) errors.Add($"Rule {rule.Id}: at least one action is required.");
                if (rule.Actions?.Count > 8) errors.Add($"Rule {rule.Id}: at most 8 actions are allowed.");
                foreach (var action in rule.Actions ?? Enumerable.Empty<BossMechanicAction>())
                {
                    if (string.Equals(action.Type, "say", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > 240) errors.Add($"Rule {rule.Id}: speech must be 1-240 characters.");
                    }
                    else if (string.Equals(action.Type, "effect", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Enum.TryParse<PlayScript>(action.Effect, true, out var playScript) || playScript == PlayScript.Invalid || !Enum.IsDefined(typeof(PlayScript), playScript)) errors.Add($"Rule {rule.Id}: unknown effect '{action.Effect}'.");
                    }
                    else if (string.Equals(action.Type, "taunt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > 240) errors.Add($"Rule {rule.Id}: taunt must be 1-240 characters.");
                        if (action.Channel != "local" && action.Channel != "fellowship") errors.Add($"Rule {rule.Id}: taunt channel must be local or fellowship.");
                    }
                    else if (action.Type == "apply_spell")
                    {
                        if (action.SpellId == 0 || new Spell(action.SpellId).NotFound) errors.Add($"Rule {rule.Id}: choose a valid spell ID.");
                        if (action.Target != "trigger" && action.Target != "nearest" && action.Target != "farthest" && action.Target != "random" && action.Target != "all") errors.Add($"Rule {rule.Id}: unknown target selector '{action.Target}'.");
                    }
                    else if (action.Type == "set_phase")
                    {
                        if (string.IsNullOrWhiteSpace(action.Phase) || action.Phase.Length > 40) errors.Add($"Rule {rule.Id}: phase name must be 1-40 characters.");
                    }
                    else if (action.Type == "push" || action.Type == "pull" || action.Type == "blink" || action.Type == "scatter" || action.Type == "knock_up")
                    {
                        if (action.Target != "trigger" && action.Target != "nearest" && action.Target != "farthest" && action.Target != "random" && action.Target != "all") errors.Add($"Rule {rule.Id}: unknown target selector '{action.Target}'.");
                        if (action.Distance < 1 || action.Distance > 40) errors.Add($"Rule {rule.Id}: movement distance must be 1-40 feet.");
                    }
                    else if (string.Equals(action.Type, "frost_rain", StringComparison.OrdinalIgnoreCase))
                    {
                        if (action.Target != "trigger" && action.Target != "nearest" && action.Target != "farthest" && action.Target != "random" && action.Target != "all") errors.Add($"Rule {rule.Id}: unknown target selector '{action.Target}'.");
                        if (action.Count < 1 || action.Count > 8) errors.Add($"Rule {rule.Id}: frost rain must use 1-8 waves.");
                        if (action.DamageScale < 0.05 || action.DamageScale > 1.0) errors.Add($"Rule {rule.Id}: frost rain damageScale must be 0.05-1.0.");                    }
                    else if (string.Equals(action.Type, "mirror_minions", StringComparison.OrdinalIgnoreCase))
                    {
                        if (action.WeenieClassId == 0) errors.Add($"Rule {rule.Id}: mirror minion shell WCID must be nonzero.");
                        if (action.Count < 1 || action.Count > 12) errors.Add($"Rule {rule.Id}: mirror minion count must be 1-12.");
                        if (action.Health < 1 || action.Health > 1000000) errors.Add($"Rule {rule.Id}: mirror minion health must be 1-1,000,000.");
                        if (action.Radius < 1 || action.Radius > 240) errors.Add($"Rule {rule.Id}: mirror source radius must be 1-240 feet.");
                        if (action.DurationSeconds < 5 || action.DurationSeconds > 3600) errors.Add($"Rule {rule.Id}: mirror minion duration must be 5-3600 seconds.");
                        if (action.Translucency > 1) errors.Add($"Rule {rule.Id}: translucency must be -1/default or 0.0-1.0.");
                        if (action.Source != "nearby" && action.Source != "fellowship" && action.Source != "trigger_fellowship") errors.Add($"Rule {rule.Id}: mirror source must be nearby, fellowship, or trigger_fellowship.");
                        if (action.WeenieClassId == document.WeenieClassId) errors.Add($"Rule {rule.Id}: a boss cannot mirror using copies of itself.");
                    }
                    else if (string.Equals(action.Type, "maintain_minions", StringComparison.OrdinalIgnoreCase))
                    {
                        if (action.WeenieClassId == 0) errors.Add($"Rule {rule.Id}: minion WCID must be nonzero.");
                        if (action.Count < 1 || action.Count > 12) errors.Add($"Rule {rule.Id}: maintained minion count must be 1-12.");
                        if (action.Health != 100) errors.Add($"Rule {rule.Id}: maintained minions must use 100 health.");
                        if (action.WeenieClassId == document.WeenieClassId) errors.Add($"Rule {rule.Id}: a boss cannot maintain copies of itself.");
                    }
                    else errors.Add($"Rule {rule.Id}: unsupported action '{action.Type}'.");
                }
            }
            return errors;
        }

        public static BossMechanicDocument GetPublished(uint wcid)
        {
            if (Published.TryGetValue(wcid, out var cached)) return cached;
            if (Missing.ContainsKey(wcid)) return null;

            var document = LoadPublishedFromDatabase(wcid) ?? LoadPublishedFromDataFolder(wcid);
            if (document == null || Validate(document).Count > 0)
            {
                Missing[wcid] = 1;
                return null;
            }

            Published[wcid] = document;
            CompileRules(document);
            return document;
        }

        private static BossMechanicDocument LoadPublishedFromDatabase(uint wcid)
        {
            if (StorageUnavailable)
                return null;

            try
            {
                using var context = new ShardDbContext();
                var row = context.BossMechanicProfile.FirstOrDefault(x => x.WeenieClassId == wcid && x.Enabled);
                return Deserialize(row?.PublishedJson);
            }
            catch
            {
                // Missing schema or unavailable storage must never interrupt combat/world ticks.
                StorageUnavailable = true;
                return null;
            }
        }

        private static BossMechanicDocument LoadPublishedFromDataFolder(uint wcid)
        {
            try
            {
                var folder = Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "BossMechanics");
                if (!Directory.Exists(folder))
                    return null;

                foreach (var path in Directory.EnumerateFiles(folder, $"{wcid}*.json"))
                {
                    var json = File.ReadAllText(path);
                    var direct = Deserialize(json);
                    if (direct?.WeenieClassId == wcid)
                        return direct;

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean())
                    {
                        if (doc.RootElement.TryGetProperty("publishedJson", out var publishedJson))
                        {
                            var wrapped = Deserialize(publishedJson.GetString());
                            if (wrapped?.WeenieClassId == wcid)
                                return wrapped;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warn($"[BossMechanic] Failed to load profile {wcid} from Data/DerpACE/BossMechanics: {ex.Message}");
            }

            return null;
        }

        public static bool TryApplyBossMutators(Creature creature)
        {
            if (creature == null) return false;
            var profile = GetPublished(creature.WeenieClassId);
            if (profile == null) return false;
            foreach (var name in (profile.Mutators ?? new List<string>()).Take(3))
            {
                var resolved = CreatureMutatorManager.ResolveAlias(name);
                if (BossSafeMutators.Contains(resolved))
                    CreatureMutatorManager.TryForceApplyMutator(creature, resolved);
            }
            return true;
        }
        public static void Invalidate(uint wcid)
        {
            Published.TryRemove(wcid, out _);
            CompiledRules.TryRemove(wcid, out _);
            Missing.TryRemove(wcid, out _);
        }

        private static void CompileRules(BossMechanicDocument document)
        {
            CompiledRules[document.WeenieClassId] = (document.Rules ?? new List<BossMechanicRule>())
                .GroupBy(rule => rule.Trigger ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<BossMechanicRule> GetRules(BossMechanicDocument profile, string trigger)
        {
            if (!CompiledRules.TryGetValue(profile.WeenieClassId, out var compiled))
            {
                CompileRules(profile);
                compiled = CompiledRules[profile.WeenieClassId];
            }

            return compiled.TryGetValue(trigger, out var rules) ? rules : Array.Empty<BossMechanicRule>();
        }

        public static void OnHealthChanged(Creature boss, uint previousHealth, bool critical = false, DamageType damageType = DamageType.Undef, uint damage = 0, Player target = null)
        {
            if (boss == null || boss.Health.MaxValue == 0 || boss.Health.Current <= 0) return;
            var profile = GetPublished(boss.WeenieClassId);
            if (profile == null) return;
            var now = Time.GetUnixTime();
            if (EncounterStarted.TryAdd(boss.Guid.Full, now))
                foreach (var rule in GetRules(profile, "combat_start")) TryExecuteRule(boss, rule, now, target);

            var before = previousHealth * 100.0 / boss.Health.MaxValue;
            var after = boss.Health.Current * 100.0 / boss.Health.MaxValue;
            if (after >= before) return;
            foreach (var rule in GetRules(profile, "health_below").Where(r => before > r.ThresholdPercent && after <= r.ThresholdPercent))
                TryExecuteRule(boss, rule, now, target);
            if (critical)
                foreach (var rule in GetRules(profile, "critical_hit")) TryExecuteRule(boss, rule, now, target);
            foreach (var rule in GetRules(profile, "damage_type").Where(r => Enum.TryParse<DamageType>(r.DamageType, true, out var dt) && damageType.HasFlag(dt)))
                TryExecuteRule(boss, rule, now, target);
            var hitPercent = damage * 100.0 / boss.Health.MaxValue;
            foreach (var rule in GetRules(profile, "large_hit").Where(r => hitPercent >= r.DamagePercent))
                TryExecuteRule(boss, rule, now, target);
        }

        public static void OnHeartbeat(Creature boss, double now)
        {
            if (boss == null || !boss.IsAlive || !EncounterStarted.TryGetValue(boss.Guid.Full, out var started)) return;
            var profile = GetPublished(boss.WeenieClassId);
            if (profile == null) return;
            foreach (var rule in GetRules(profile, "timer").Where(r => now - started >= r.IntervalSeconds))
            {
                var key = $"{boss.Guid.Full}:{rule.Id}";
                var last = LastRuleRun.GetOrAdd(key, started);
                if (now - last >= rule.IntervalSeconds && TryExecuteRule(boss, rule, now))
                    LastRuleRun[key] = now;
            }
        }

        public static void OnEvade(Creature boss, Player target = null)
        {
            if (boss == null || !boss.IsAlive) return;
            var profile = GetPublished(boss.WeenieClassId);
            if (profile == null) return;
            var now = Time.GetUnixTime();
            EncounterStarted.TryAdd(boss.Guid.Full, now);
            foreach (var rule in GetRules(profile, "boss_evades")) TryExecuteRule(boss, rule, now, target);
        }
        public static void OnSpellResisted(Creature boss)
        {
            if (boss == null || !boss.IsAlive) return;
            var profile = GetPublished(boss.WeenieClassId);
            if (profile == null) return;
            var now = Time.GetUnixTime();
            EncounterStarted.TryAdd(boss.Guid.Full, now);
            foreach (var rule in GetRules(profile, "spell_resisted")) TryExecuteRule(boss, rule, now);
        }

        private static bool TryExecuteRule(Creature boss, BossMechanicRule rule, double now, Player target = null)
        {
            var phase = ActivePhases.GetOrAdd(boss.Guid.Full, "default");
            if (!string.IsNullOrWhiteSpace(rule.Phase) && !string.Equals(rule.Phase, phase, StringComparison.OrdinalIgnoreCase)) return false;
            var nearby = PlayerManager.GetAllOnline().Count(p => p?.Location != null && boss.Location != null &&
                p.Location.Landblock == boss.Location.Landblock && boss.Location.Distance2DSquared(p.Location) <= WorldObject.LocalBroadcastRange * WorldObject.LocalBroadcastRange);
            if (nearby < rule.MinPlayers) return false;
            if (ThreadSafeRandom.Next(0.0f, 100.0f) >= rule.ChancePercent) return false;

            if (rule.Once)
            {
                var fired = FiredRules.GetOrAdd(boss.Guid.Full, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                lock (fired) { if (!fired.Add(rule.Id)) return false; }
            }
            target ??= GetNearestPlayer(boss);
            foreach (var action in rule.Actions.Take(8)) ExecuteAction(boss, action, target, nearby);
            return true;
        }
        public static void Reset(Creature boss)
        {
            if (boss == null) return;
            FiredRules.TryRemove(boss.Guid.Full, out _);
            EncounterStarted.TryRemove(boss.Guid.Full, out _);
            ActivePhases.TryRemove(boss.Guid.Full, out _);
            CleanupAppliedEffects(boss.Guid.Full);
            foreach (var key in LastRuleRun.Keys.Where(k => k.StartsWith(boss.Guid.Full + ":", StringComparison.Ordinal))) LastRuleRun.TryRemove(key, out _);
            CleanupMinions(boss.Guid.Full);
        }

        public static void OnCreatureDeath(Creature creature)
        {
            if (creature == null) return;
            var profile = GetPublished(creature.WeenieClassId);
            if (profile != null)
                foreach (var rule in GetRules(profile, "death")) TryExecuteRule(creature, rule, Time.GetUnixTime());
            if (MinionEncounters.Keys.Any(key => key.StartsWith(creature.Guid.Full + ":", StringComparison.Ordinal)))
            {
                CleanupMinions(creature.Guid.Full);
                return;
            }
            if (!MinionOwners.TryRemove(creature.Guid.Full, out var encounterKey) || !MinionEncounters.TryGetValue(encounterKey, out var encounter))
                return;
            lock (encounter.Sync)
                encounter.MinionGuids.Remove(creature.Guid.Full);
            if (encounter.Boss.TryGetTarget(out var boss) && boss.IsAlive && boss.Health.Current > 0)
                MaintainMinions(boss, encounter.Action);        }
        private static void ExecuteAction(Creature boss, BossMechanicAction action, Player target, int nearby)
        {
            try
            {
                var text = ExpandText(action.Text, boss, target, nearby);
                if (string.Equals(action.Type, "say", StringComparison.OrdinalIgnoreCase))
                    boss.EnqueueBroadcast(new GameMessageHearSpeech(text, boss.Name, boss.Guid.Full, ChatMessageType.Speech), WorldObject.LocalBroadcastRange);
                else if (string.Equals(action.Type, "effect", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<PlayScript>(action.Effect, true, out var effect))
                    boss.ApplyVisualEffects(effect);
                else if (string.Equals(action.Type, "maintain_minions", StringComparison.OrdinalIgnoreCase))
                    MaintainMinions(boss, action);
                else if (string.Equals(action.Type, "mirror_minions", StringComparison.OrdinalIgnoreCase))
                    MaintainMinions(boss, action, target);
                else if (action.Type == "push" || action.Type == "pull" || action.Type == "blink" || action.Type == "scatter" || action.Type == "knock_up")
                    ExecuteMovement(boss, action, target);
                else if (string.Equals(action.Type, "frost_rain", StringComparison.OrdinalIgnoreCase))
                    StartFrostRain(boss, action, target);
                else if (string.Equals(action.Type, "apply_spell", StringComparison.OrdinalIgnoreCase))
                    ApplyTemporarySpell(boss, action, target);
                else if (string.Equals(action.Type, "set_phase", StringComparison.OrdinalIgnoreCase))
                    ActivePhases[boss.Guid.Full] = action.Phase;
                else if (string.Equals(action.Type, "taunt", StringComparison.OrdinalIgnoreCase))
                    ExecuteTaunt(boss, action, text);
            }
            catch { }
        }

        private static void ExecuteTaunt(Creature boss, BossMechanicAction action, string text)
        {
            if (action.Channel == "local")
            {
                boss.EnqueueBroadcast(new GameMessageHearSpeech(text, boss.Name, boss.Guid.Full, ChatMessageType.Speech), WorldObject.LocalBroadcastRange);
                return;
            }

            var recipients = new Dictionary<uint, Player>();
            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null || boss.Location == null || player.Location.Landblock != boss.Location.Landblock)
                    continue;
                if (boss.Location.Distance2DSquared(player.Location) > WorldObject.LocalBroadcastRange * WorldObject.LocalBroadcastRange)
                    continue;
                recipients[player.Guid.Full] = player;
                if (player.Fellowship?.FellowshipMembers == null) continue;
                foreach (var member in player.Fellowship.FellowshipMembers.Values)
                    if (member.TryGetTarget(out var fellow) && fellow?.Session != null)
                        recipients[fellow.Guid.Full] = fellow;
            }

            foreach (var recipient in recipients.Values)
                recipient.Session?.Network.EnqueueSend(new GameMessageSystemChat($"[{boss.Name}] {text}", ChatMessageType.Fellowship));
        }
        private static Player GetNearestPlayer(Creature boss) => PlayerManager.GetAllOnline()
            .Where(p => p?.Location != null && boss?.Location != null && p.Location.Landblock == boss.Location.Landblock)
            .Where(p => boss.Location.Distance2DSquared(p.Location) <= WorldObject.LocalBroadcastRange * WorldObject.LocalBroadcastRange)
            .OrderBy(p => boss.Location.Distance2DSquared(p.Location))
            .FirstOrDefault();

        private static string ExpandText(string text, Creature boss, Player target, int nearby)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var healthPercent = boss?.Health.MaxValue > 0 ? boss.Health.Current * 100 / boss.Health.MaxValue : 0;
            return text
                .Replace("%t", target?.Name ?? "adventurer", StringComparison.OrdinalIgnoreCase)
                .Replace("%b", boss?.Name ?? "the boss", StringComparison.OrdinalIgnoreCase)
                .Replace("%h", healthPercent.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("%n", nearby.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        private static void ExecuteMovement(Creature boss, BossMechanicAction action, Player trigger)
        {
            foreach (var player in SelectTargets(boss, action.Target, trigger))
            {
                if (player.Teleporting || player.IsDead || player.Location == null) continue;
                var desired = new Position(player.Location);
                var dx = player.Location.PositionX - boss.Location.PositionX;
                var dy = player.Location.PositionY - boss.Location.PositionY;
                var length = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
                if (action.Type == "scatter")
                {
                    var angle = ThreadSafeRandom.Next(0.0f, (float)(Math.PI * 2));
                    dx = (float)Math.Cos(angle); dy = (float)Math.Sin(angle); length = 1;
                }
                var direction = action.Type == "pull" || action.Type == "blink" ? -1.0 : 1.0;
                if (action.Type == "knock_up")
                {
                    desired.PositionZ += (float)action.Distance;
                }
                else
                {
                    desired.PositionX += (float)(dx / length * action.Distance * direction);
                    desired.PositionY += (float)(dy / length * action.Distance * direction);
                }
                if (!FlutterStone.TryResolveSafeDestination(player, desired, out var safe)) continue;
                player.ApplyVisualEffects(GetMovementEffect(action.Type), 0.7f);
                player.Sequences.GetNextSequence(ACE.Server.Network.Sequence.SequenceType.ObjectForcePosition);
                player.UpdatePlayerPosition(safe, true);
            }
        }

        private static PlayScript GetMovementEffect(string actionType) => actionType switch
        {
            "push" => PlayScript.ProjectileCollision,
            "pull" => PlayScript.PortalStorm,
            "scatter" => PlayScript.Launch,
            "knock_up" => PlayScript.TransUpWhite,
            "blink" => PlayScript.PortalExit,
            _ => PlayScript.ProjectileCollision,
        };
        private static IEnumerable<Player> SelectTargets(Creature boss, string selector, Player trigger)
        {
            var players = PlayerManager.GetAllOnline().Where(p => p?.Location != null && !p.IsDead && boss?.Location != null &&
                p.Location.Landblock == boss.Location.Landblock && boss.Location.Distance2DSquared(p.Location) <= WorldObject.LocalBroadcastRange * WorldObject.LocalBroadcastRange).ToList();
            if (players.Count == 0) return players;
            return selector switch
            {
                "all" => players,
                "random" => new[] { players[ThreadSafeRandom.Next(0, players.Count)] },
                "farthest" => new[] { players.OrderByDescending(p => boss.Location.Distance2DSquared(p.Location)).First() },
                "nearest" => new[] { players.OrderBy(p => boss.Location.Distance2DSquared(p.Location)).First() },
                _ => new[] { trigger != null && players.Contains(trigger) ? trigger : players.OrderBy(p => boss.Location.Distance2DSquared(p.Location)).First() },
            };
        }
        private static void ApplyTemporarySpell(Creature boss, BossMechanicAction action, Player trigger)
        {
            var spell = new Spell(action.SpellId);
            if (spell.NotFound) return;
            foreach (var player in SelectTargets(boss, action.Target, trigger))
            {
                var result = player.EnchantmentManager.Add(spell, boss, null);
                if (result?.Enchantment == null) continue;
                player.Session?.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, result.Enchantment)));
                var effects = AppliedEffects.GetOrAdd(boss.Guid.Full, _ => new List<BossAppliedEffect>());
                lock (effects) effects.Add(new BossAppliedEffect(player, action.SpellId, boss.Guid.Full));
            }
        }

        private static void CleanupAppliedEffects(uint bossGuid)
        {
            if (!AppliedEffects.TryRemove(bossGuid, out var effects)) return;
            lock (effects)
            {
                foreach (var effect in effects)
                {
                    if (!effect.Player.TryGetTarget(out var player)) continue;
                    var enchantment = player.EnchantmentManager.GetEnchantment(effect.SpellId, effect.CasterGuid);
                    if (enchantment != null) player.EnchantmentManager.Remove(enchantment, false);
                }
            }
        }
        private static void StartFrostRain(Creature boss, BossMechanicAction action, Player trigger)
        {
            if (boss?.Location == null || !boss.IsAlive)
                return;

            var waves = Math.Clamp(action.Count, 1, 8);
            var damageScale = (float)Math.Clamp(action.DamageScale, 0.05, 1.0);
            boss.ApplyVisualEffects(PlayScript.EnchantUpBlue);
            boss.EnqueueBroadcast(new GameMessageHearSpeech("Release the rain!", boss.Name, boss.Guid.Full, ChatMessageType.Spellcasting), WorldObject.LocalBroadcastRange);

            var chain = new ActionChain();
            for (var wave = 0; wave < waves; wave++)
            {
                if (wave > 0)
                    chain.AddDelaySeconds(0.65);
                chain.AddAction(boss, () =>
                {
                    if (boss.IsDead || !boss.IsAlive || boss.Location == null)
                        return;

                    var spell = new Spell(CustomSpellManager.RainfallFrostSpellId);
                    foreach (var player in SelectTargets(boss, action.Target, trigger).ToList())
                    {
                        if (player?.Location == null || player.IsDead || player.Teleporting)
                            continue;

                        player.ApplyVisualEffects(PlayScript.EnchantUpBlue, 0.35f);
                        if (spell.NotFound)
                        {
                            player.ApplyVisualEffects(PlayScript.BreatheFrost);
                            player.TakeDamage(boss, DamageType.Cold, Math.Max(1.0f, 90.0f * damageScale), BodyPart.Chest);
                            continue;
                        }

                        // Using the victim as projectile origin makes the +9 Z offset fall vertically.
                        boss.TryCastSpell(spell, player, boss, boss, false, true, true, damageScale, player);
                    }
                });
            }
            chain.EnqueueChain();
        }
        private static string GetMinionEncounterKey(Creature boss, BossMechanicAction action)
        {
            var type = string.IsNullOrWhiteSpace(action?.Type) ? "minions" : action.Type.Trim().ToLowerInvariant();
            var source = string.IsNullOrWhiteSpace(action?.Source) ? "nearby" : action.Source.Trim().ToLowerInvariant();
            return $"{boss.Guid.Full}:{type}:{action?.WeenieClassId ?? 0}:{source}";
        }
        private static void MaintainMinions(Creature boss, BossMechanicAction action, Player trigger = null)
        {
            if (boss?.Location == null || boss.CurrentLandblock == null || !boss.IsAlive) return;
            var encounterKey = GetMinionEncounterKey(boss, action);
            var encounter = MinionEncounters.GetOrAdd(encounterKey, _ => new BossMinionEncounter(boss, action));
            encounter.Action = action;
            lock (encounter.Sync)
            {
                encounter.MinionGuids.RemoveWhere(guid =>
                {
                    var existing = boss.CurrentLandblock.GetObject(guid) as Creature;
                    if (existing != null && existing.IsAlive) return false;
                    MinionOwners.TryRemove(guid, out _);
                    return true;
                });
                while (encounter.MinionGuids.Count < Math.Clamp(action.Count, 1, 12))
                {
                    var slot = encounter.MinionGuids.Count;
                    var angle = slot * Math.PI * 2.0 / Math.Max(1, action.Count);
                    var minion = WorldObjectFactory.CreateNewWorldObject(action.WeenieClassId) as Creature;
                    if (minion == null) break;
                    var position = new Position(boss.Location);
                    position.PositionX += (float)Math.Cos(angle) * 4.0f;
                    position.PositionY += (float)Math.Sin(angle) * 4.0f;
                    position.PositionZ += 0.25f;
                    position.LandblockId = new LandblockId(position.GetCell());
                    minion.Location = position;
                    minion.GeneratorId = boss.Guid.Full;
                    if (string.Equals(action.Type, "mirror_minions", StringComparison.OrdinalIgnoreCase))
                        PrepareMirrorMinion(boss, minion, action, trigger);
                    else
                        PrepareBasicMinion(minion);
                    if (!LandblockManager.AddObject(minion, true))
                    {
                        minion.Destroy();
                        break;
                    }
                    var targetHealth = GetMinionHealth(action);
                    var delta = targetHealth - minion.Health.MaxValue;
                    minion.Health.StartingValue = (uint)Math.Max(1, (long)minion.Health.StartingValue + delta);
                    minion.Health.Current = Math.Min((uint)targetHealth, minion.Health.MaxValue);
                    if (string.Equals(action.Type, "mirror_minions", StringComparison.OrdinalIgnoreCase))
                    {
                        WakeMirrorMinion(minion, boss, trigger);
                        ScheduleMinionExpiry(encounterKey, minion, action.DurationSeconds);
                    }
                    encounter.MinionGuids.Add(minion.Guid.Full);
                    MinionOwners[minion.Guid.Full] = encounterKey;
                }
            }
        }

        private static void PrepareBasicMinion(Creature minion)
        {
            minion.SetProperty(PropertyInt.XpOverride, 0);
            minion.SetProperty(PropertyBool.NoCorpse, true);
            var endurance = minion.Attributes[PropertyAttribute.Endurance];
            endurance.StartingValue = 0;
            endurance.Ranks = 0;
            endurance.ExperienceSpent = 0;
            minion.Health.StartingValue = 100;
            minion.Health.Ranks = 0;
            minion.Health.ExperienceSpent = 0;
        }

        private static void PrepareMirrorMinion(Creature boss, Creature minion, BossMechanicAction action, Player trigger)
        {
            minion.CreatureType = ACE.Entity.Enum.CreatureType.Simulacrum;
            minion.SetProperty(PropertyBool.IsSimulacrumMob, false);
            var source = SelectMirrorSource(boss, action, trigger);
            if (source != null)
                minion.TryCopyFromPlayer(source);
            minion.Name = source != null ? $"Mirror of {source.Name}" : $"Mirror {minion.Name}";
            minion.GeneratorId = boss.Guid.Full;
            minion.TreasureCorpse = action.DropItems;
            minion.NoCorpse = action.NoCorpse || !action.DropItems;
            if (!action.DropItems)
                minion.DeathTreasureType = null;
            if (action.NoXp)
            {
                minion.SetProperty(PropertyInt.XpOverride, 0);
                minion.LuminanceAward = 0;
            }
            if (action.Translucency >= 0)
                minion.Translucency = (float)Math.Clamp(action.Translucency, 0, 1);
            minion.ApplyVisualEffects(PlayScript.EnchantUpPurple);
        }

        private static long GetMinionHealth(BossMechanicAction action)
        {
            if (string.Equals(action.Type, "mirror_minions", StringComparison.OrdinalIgnoreCase))
                return Math.Clamp(action.Health <= 0 ? 100 : action.Health, 1, 1000000);
            return 100;
        }

        private static Player SelectMirrorSource(Creature boss, BossMechanicAction action, Player trigger)
        {
            var candidates = SelectMirrorSources(boss, action, trigger).ToList();
            if (candidates.Count == 0)
                return trigger;
            return candidates[ThreadSafeRandom.Next(0, candidates.Count)];
        }

        private static IEnumerable<Player> SelectMirrorSources(Creature boss, BossMechanicAction action, Player trigger)
        {
            if (boss?.Location == null)
                return Enumerable.Empty<Player>();

            var radius = Math.Clamp(action.Radius <= 0 ? WorldObject.LocalBroadcastRange : action.Radius, 1, 240);
            var nearby = PlayerManager.GetAllOnline()
                .Where(p => p?.Location != null && !p.IsDead && p.Location.Landblock == boss.Location.Landblock)
                .Where(p => boss.Location.Distance2DSquared(p.Location) <= radius * radius)
                .ToList();

            if (action.Source == "nearby" || trigger == null)
                return nearby;

            if (trigger.Fellowship?.FellowshipMembers == null)
                return nearby.Where(p => p == trigger);

            var fellowGuids = new HashSet<uint>();
            foreach (var member in trigger.Fellowship.FellowshipMembers.Values)
                if (member.TryGetTarget(out var fellow) && fellow != null)
                    fellowGuids.Add(fellow.Guid.Full);

            return nearby.Where(p => fellowGuids.Contains(p.Guid.Full));
        }

        private static void WakeMirrorMinion(Creature minion, Creature boss, Player trigger)
        {
            if (minion?.CreatureType != ACE.Entity.Enum.CreatureType.Simulacrum)
                return;

            var target = trigger != null && trigger.Location != null && minion.Location != null && trigger.Location.Landblock == minion.Location.Landblock
                ? trigger
                : GetNearestPlayer(boss);
            minion.AttackTarget = target;
            minion.CurrentAttack = null;
            minion.MonsterState = Creature.State.Awake;
            minion.IsAwake = true;
            minion.WakeUp(false);
        }

        private static void ScheduleMinionExpiry(string encounterKey, Creature minion, double durationSeconds)
        {
            if (minion == null || durationSeconds <= 0 || durationSeconds >= 3600)
                return;

            var chain = new ActionChain();
            chain.AddDelaySeconds(Math.Max(5.0, durationSeconds));
            chain.AddAction(minion, () =>
            {
                if (minion.IsDestroyed)
                    return;
                MinionOwners.TryRemove(minion.Guid.Full, out _);
                if (MinionEncounters.TryGetValue(encounterKey, out var encounter))
                    lock (encounter.Sync)
                        encounter.MinionGuids.Remove(minion.Guid.Full);
                minion.EnqueueBroadcast(new GameMessageScript(minion.Guid, PlayScript.EnchantDownPurple, 1.0f));
                minion.Destroy();
            });
            chain.EnqueueChain();
        }
        private static void CleanupMinions(uint bossGuid)
        {
            var prefix = bossGuid + ":";
            foreach (var encounterKey in MinionEncounters.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                if (!MinionEncounters.TryRemove(encounterKey, out var encounter)) continue;
                lock (encounter.Sync)
                {
                    foreach (var guid in encounter.MinionGuids)
                    {
                        MinionOwners.TryRemove(guid, out _);
                        if (encounter.Boss.TryGetTarget(out var boss))
                            (boss.CurrentLandblock?.GetObject(guid) as Creature)?.Destroy();
                    }
                    encounter.MinionGuids.Clear();
                }
            }
        }
        private sealed class BossAppliedEffect
        {
            public BossAppliedEffect(Player player, uint spellId, uint casterGuid) { Player = new WeakReference<Player>(player); SpellId = spellId; CasterGuid = casterGuid; }
            public WeakReference<Player> Player { get; }
            public uint SpellId { get; }
            public uint CasterGuid { get; }
        }

        private sealed class BossMinionEncounter
        {
            public BossMinionEncounter(Creature boss, BossMechanicAction action) { Boss = new WeakReference<Creature>(boss); Action = action; }
            public object Sync { get; } = new object();
            public WeakReference<Creature> Boss { get; }
            public BossMechanicAction Action { get; set; }
            public HashSet<uint> MinionGuids { get; } = new HashSet<uint>();
        }    }
}
