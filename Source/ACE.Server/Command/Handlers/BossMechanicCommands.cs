using System;
using System.Linq;
using ACE.Database.Models.Shard;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class BossMechanicCommands
    {
        [CommandHandler("boss", AccessLevel.Admin, CommandHandlerFlag.RequiresWorld, 1,
            "Guided editor for safe boss mechanic profiles.",
            "create <profile> <wcid> | add-say <profile> <health%> <text> | add-effect <profile> <health%> <PlayScript> | show <profile> | validate <profile> | publish <profile> | rollback <profile>")]
        public static void HandleBoss(Session session, params string[] args)
        {
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "create": Create(session, args); break;
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
            if (a.Length != 3 || !uint.TryParse(a[2], out var wcid) || wcid == 0) throw new ArgumentException("Usage: @boss create <profile> <wcid>");
            var name = Name(a[1]);
            using var db = new ShardDbContext();
            if (db.BossMechanicProfile.Any(x => x.ProfileName == name || x.WeenieClassId == wcid)) throw new InvalidOperationException("That profile or WCID is already assigned.");
            db.BossMechanicProfile.Add(new BossMechanicProfile {
                ProfileName = name, WeenieClassId = wcid, DraftRevision = 1,
                DraftJson = BossMechanicManager.Serialize(BossMechanicManager.NewDocument(wcid)),
                Enabled = false, ModifiedBy = s.Player.Name, ModifiedAt = DateTime.UtcNow });
            db.SaveChanges();
            Reply(s, $"Created draft '{name}' for WCID {wcid}. Nothing is live yet.");
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