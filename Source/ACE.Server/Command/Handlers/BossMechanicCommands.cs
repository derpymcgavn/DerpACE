using System;
using System.Linq;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Command.Handlers.Processors;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class BossMechanicCommands
    {
        [CommandHandler("boss", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 1,
            "Guided editor for safe boss mechanic profiles.",
            "create <profile> <wcid> [newBossWcid] | add-minions <profile> <health%> <wcid> <count> | add-taunt <profile> <health%> <local|fellowship> <text> | add-say <profile> <health%> <text> | add-effect <profile> <health%> <PlayScript> | show <profile> | validate <profile> | publish <profile> | rollback <profile>")]
        public static void HandleBoss(Session session, params string[] args)
        {
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "create": Create(session, args); break;
                    case "add-minions": AddMinions(session, args); break;
                    case "add-taunt": AddTaunt(session, args); break;
                    case "add-say": Add(session, args, true); break;
                    case "add-effect": Add(session, args, false); break;
                    case "show": Show(session, args); break;
                    case "validate": Validate(session, args); break;
                    case "publish": Publish(session, args); break;
                    case "rollback": Rollback(session, args); break;
                    default: Reply(session, "Unknown operation. Use @help boss."); break;
                }
            }
            catch (Exception ex) { Reply(session, $"Boss editor rejected the operation: {ex.Message}"); }
        }

        private static void Create(Session s, string[] a)
        {
            if (a.Length < 3 || a.Length > 4)
                throw new ArgumentException("Usage: @boss create <profile> <wcid> [newBossWcid]");

            var sourceText = a[2].TrimEnd(',');
            if (!uint.TryParse(sourceText, out var sourceWcid) || sourceWcid == 0)
                throw new ArgumentException("The source WCID must be a nonzero number.");

            var bossWcid = sourceWcid;
            var cloneRequested = a.Length == 4;
            if (cloneRequested && (!uint.TryParse(a[3].TrimStart(','), out bossWcid) || bossWcid == 0 || bossWcid == sourceWcid))
                throw new ArgumentException("The new boss WCID must be a different nonzero number.");

            var source = DatabaseManager.World.GetWeenie(sourceWcid);
            if (source == null)
                throw new InvalidOperationException($"Source WCID {sourceWcid} does not exist.");
            if (cloneRequested && DatabaseManager.World.GetWeenie(bossWcid) != null)
                throw new InvalidOperationException($"Destination WCID {bossWcid} already exists.");

            var name = Name(a[1]);
            using var db = new ShardDbContext();
            if (db.BossMechanicProfile.Any(x => x.ProfileName == name || x.WeenieClassId == bossWcid))
                throw new InvalidOperationException("That profile or boss WCID is already assigned.");

            var row = new BossMechanicProfile {
                ProfileName = name, WeenieClassId = bossWcid, DraftRevision = 1,
                DraftJson = BossMechanicManager.Serialize(BossMechanicManager.NewDocument(bossWcid)),
                Enabled = false, ModifiedBy = s.Player.Name, ModifiedAt = DateTime.UtcNow };
            db.BossMechanicProfile.Add(row);
            db.SaveChanges();

            if (!cloneRequested)
            {
                Reply(s, $"Created draft '{name}' for existing WCID {bossWcid}. Nothing is live yet.");
                return;
            }

            if (!DeveloperContentCommands.ExportClonedBossSQL(s, sourceWcid, bossWcid, row, out var path, out var error))
            {
                db.BossMechanicProfile.Remove(row);
                db.SaveChanges();
                throw new InvalidOperationException($"Clone SQL was not created; the draft was rolled back. {error}");
            }

            Reply(s, $"Created draft '{name}' for cloned boss WCID {bossWcid} from {sourceWcid}. SQL: {path}\nImport the SQL and reload world content before spawning / publishing the new boss.");
        }
        private static void AddTaunt(Session s, string[] a)
        {
            if (a.Length < 5 || !double.TryParse(a[2], out var pct))
                throw new ArgumentException("Usage: @boss add-taunt <profile> <health%> <local|fellowship> <text>");
            var channel = a[3].Trim().ToLowerInvariant();
            if (channel != "local" && channel != "fellowship")
                throw new ArgumentException("Taunt channel must be local or fellowship.");

            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            var doc = BossMechanicManager.Deserialize(row.DraftJson) ?? BossMechanicManager.NewDocument(row.WeenieClassId);
            var rule = new BossMechanicRule { Id = $"health_{pct:0.##}_{channel}_taunt_{doc.Rules.Count + 1}".Replace('.', '_'), ThresholdPercent = pct };
            rule.Actions.Add(new BossMechanicAction { Type = "taunt", Channel = channel, Text = string.Join(" ", a.Skip(4)) });
            doc.Rules.Add(rule);
            RequireValid(doc);
            row.DraftJson = BossMechanicManager.Serialize(doc);
            row.DraftRevision++;
            Touch(row, s);
            db.SaveChanges();
            Reply(s, $"Added {channel} taunt below {pct:0.##}%. It is not live until published.");
        }
        private static void AddMinions(Session s, string[] a)
        {
            if (a.Length != 5 || !double.TryParse(a[2], out var pct) ||
                !uint.TryParse(a[3], out var minionWcid) || !int.TryParse(a[4], out var count))
                throw new ArgumentException("Usage: @boss add-minions <profile> <health%> <minionWcid> <count>");
            if (DatabaseManager.World.GetWeenie(minionWcid) == null)
                throw new InvalidOperationException($"Minion WCID {minionWcid} does not exist.");

            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            var doc = BossMechanicManager.Deserialize(row.DraftJson) ?? BossMechanicManager.NewDocument(row.WeenieClassId);
            var rule = new BossMechanicRule { Id = $"health_{pct:0.##}_minions_{doc.Rules.Count + 1}".Replace('.', '_'), ThresholdPercent = pct };
            rule.Actions.Add(new BossMechanicAction { Type = "maintain_minions", WeenieClassId = minionWcid, Count = count, Health = 100 });
            doc.Rules.Add(rule);
            RequireValid(doc);
            row.DraftJson = BossMechanicManager.Serialize(doc);
            row.DraftRevision++;
            Touch(row, s);
            db.SaveChanges();
            Reply(s, $"Added maintained minions: {count} x WCID {minionWcid}, 100 health, activating below {pct:0.##}%. Publish when ready.");
        }
        private static void Add(Session s, string[] a, bool speech)
        {
            if (a.Length < 4 || !double.TryParse(a[2], out var pct)) throw new ArgumentException(speech ? "Usage: @boss add-say <profile> <health%> <text>" : "Usage: @boss add-effect <profile> <health%> <PlayScript>");
            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            var doc = BossMechanicManager.Deserialize(row.DraftJson) ?? BossMechanicManager.NewDocument(row.WeenieClassId);
            var rule = new BossMechanicRule { Id = $"health_{pct:0.##}_{doc.Rules.Count + 1}".Replace('.', '_'), ThresholdPercent = pct };
            rule.Actions.Add(speech
                ? new BossMechanicAction { Type = "say", Text = string.Join(" ", a.Skip(3)) }
                : new BossMechanicAction { Type = "effect", Effect = a[3] });
            doc.Rules.Add(rule);
            RequireValid(doc);
            row.DraftJson = BossMechanicManager.Serialize(doc);
            row.DraftRevision++;
            Touch(row, s);
            db.SaveChanges();
            Reply(s, $"Added draft rule '{rule.Id}'. It is not live until published.");
        }

        private static void Show(Session s, string[] a)
        {
            RequireTwo(a, "show");
            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            var doc = BossMechanicManager.Deserialize(row.DraftJson);
            var rules = doc?.Rules.Select(r => $"{r.Id}: below {r.ThresholdPercent:0.##}% -> {string.Join(", ", r.Actions.Select(x => x.Type))}") ?? Enumerable.Empty<string>();
            Reply(s, $"'{row.ProfileName}' WCID {row.WeenieClassId} | draft r{row.DraftRevision} | published r{row.PublishedRevision} | live: {row.Enabled}\n{string.Join("\n", rules)}");
        }

        private static void Validate(Session s, string[] a)
        {
            RequireTwo(a, "validate");
            using var db = new ShardDbContext();
            var errors = BossMechanicManager.Validate(BossMechanicManager.Deserialize(Row(db, a[1]).DraftJson));
            Reply(s, errors.Count == 0 ? "Draft is valid and may be published." : "Draft errors:\n" + string.Join("\n", errors));
        }

        private static void Publish(Session s, string[] a)
        {
            RequireTwo(a, "publish");
            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            RequireValid(BossMechanicManager.Deserialize(row.DraftJson));
            row.PreviousJson = row.PublishedJson;
            row.PreviousRevision = row.PublishedRevision;
            row.PublishedJson = row.DraftJson;
            row.PublishedRevision = row.DraftRevision;
            row.Enabled = true;
            Touch(row, s);
            db.SaveChanges();
            BossMechanicManager.Invalidate(row.WeenieClassId);
            PlayerManager.BroadcastToAuditChannel(s.Player, $"{s.Player.Name} published boss profile {row.ProfileName} r{row.PublishedRevision}.");
            Reply(s, $"Published '{row.ProfileName}' revision {row.PublishedRevision}.");
        }

        private static void Rollback(Session s, string[] a)
        {
            RequireTwo(a, "rollback");
            using var db = new ShardDbContext();
            var row = Row(db, a[1]);
            if (string.IsNullOrWhiteSpace(row.PreviousJson)) throw new InvalidOperationException("No previous published revision exists.");
            (row.PublishedJson, row.PreviousJson) = (row.PreviousJson, row.PublishedJson);
            (row.PublishedRevision, row.PreviousRevision) = (row.PreviousRevision, row.PublishedRevision);
            row.Enabled = true;
            Touch(row, s);
            db.SaveChanges();
            BossMechanicManager.Invalidate(row.WeenieClassId);
            PlayerManager.BroadcastToAuditChannel(s.Player, $"{s.Player.Name} rolled boss profile {row.ProfileName} back to r{row.PublishedRevision}.");
            Reply(s, $"Rolled back to revision {row.PublishedRevision}.");
        }

        private static BossMechanicProfile Row(ShardDbContext db, string value) => db.BossMechanicProfile.FirstOrDefault(x => x.ProfileName == Name(value)) ?? throw new InvalidOperationException("Profile not found.");
        private static void RequireValid(BossMechanicDocument doc) { var e = BossMechanicManager.Validate(doc); if (e.Count > 0) throw new InvalidOperationException(string.Join(" ", e)); }
        private static void RequireTwo(string[] a, string op) { if (a.Length != 2) throw new ArgumentException($"Usage: @boss {op} <profile>"); }
        private static void Touch(BossMechanicProfile row, Session s) { row.ModifiedBy = s.Player.Name; row.ModifiedAt = DateTime.UtcNow; }
        private static string Name(string value) { var n = (value ?? "").Trim().ToLowerInvariant(); if (n.Length < 3 || n.Length > 64 || n.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) throw new ArgumentException("Profile names use 3-64 letters, numbers, _ or -."); return n; }
        private static void Reply(Session s, string text) => s.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.Broadcast));
    }
}