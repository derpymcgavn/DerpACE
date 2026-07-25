using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing.Imaging;

using log4net;

using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Database;
using ACE.Database.Models.Auth;
using ACE.Database.Models.Shard;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Pathfinding.Geometry;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.DerpAce
{
    public static class AdminMapService
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static HttpListener listener;
        private static CancellationTokenSource cancelSource;
        private static readonly ConcurrentDictionary<uint, AdminDungeonMap> DungeonMapCache = new ConcurrentDictionary<uint, AdminDungeonMap>();
        private static readonly object FeedLock = new object();
        private static readonly List<AdminChatFeedEntry> ChatFeed = new List<AdminChatFeedEntry>();
        private static readonly List<AdminRareFeedEntry> RareFeed = new List<AdminRareFeedEntry>();
        private static readonly ConcurrentDictionary<string, AdminMapSession> Sessions = new ConcurrentDictionary<string, AdminMapSession>();
        private static readonly ConcurrentDictionary<string, AdminIconCacheEntry> IconPngCache = new ConcurrentDictionary<string, AdminIconCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private const float CreatureBlipRadius = 80.0f;
        private const int MaxCreatureBlips = 200;
        private const int MaxFeedEntries = 80;
        private const int SnapshotFeedEntries = 18;
        private const string SessionCookieName = "DerpACEAdminMapSession";
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

        public static void RecordGeneralChat(string sender, string message)
        {
            if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(message))
                return;

            lock (FeedLock)
            {
                ChatFeed.Add(new AdminChatFeedEntry
                {
                    Utc = DateTime.UtcNow,
                    Channel = "General",
                    Sender = sender.TrimStart('+'),
                    Message = message.Trim()
                });

                TrimFeed(ChatFeed);
            }
        }

        public static void RecordRareFind(string playerName, string itemName, uint weenieClassId, int tier, int chance, int luck, string corpseName, string location, string landblock)
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(itemName))
                return;

            lock (FeedLock)
            {
                RareFeed.Add(new AdminRareFeedEntry
                {
                    Utc = DateTime.UtcNow,
                    Player = playerName.TrimStart('+'),
                    Item = itemName.Trim(),
                    WeenieClassId = weenieClassId,
                    Tier = tier,
                    Chance = chance,
                    Luck = luck,
                    Corpse = corpseName,
                    Location = location,
                    Landblock = landblock
                });

                TrimFeed(RareFeed);
            }
        }

        public static void Start()
        {
            var config = DerpAceConfigManager.Config;

            if (!config.AdminMapEnabled)
                return;

            if (listener != null)
                return;

            var host = string.IsNullOrWhiteSpace(config.AdminMapHost) ? "127.0.0.1" : config.AdminMapHost.Trim();
            var port = Math.Clamp(config.AdminMapPort, 1, 65535);
            var prefix = $"http://{host}:{port}/";

            try
            {
                cancelSource = new CancellationTokenSource();
                listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                listener.Start();
                _ = Task.Run(() => ListenLoop(cancelSource.Token));

                log.Info($"[DerpACE AdminMap] Listening on {prefix}");
            }
            catch (Exception ex)
            {
                log.Error($"[DerpACE AdminMap] Failed to start on {prefix}: {ex}");
                Stop();
            }
        }

        public static void Stop()
        {
            try
            {
                cancelSource?.Cancel();
                listener?.Stop();
                listener?.Close();
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Error while stopping: {ex.Message}");
            }
            finally
            {
                listener = null;
                cancelSource?.Dispose();
                cancelSource = null;
            }
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener?.IsListening == true)
            {
                HttpListenerContext context = null;

                try
                {
                    context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), token);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.Warn($"[DerpACE AdminMap] Request loop error: {ex}");
                    CloseQuietly(context);
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";

                if (path.Length == 0 || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                {
                    WriteText(context, BuildIndexHtml(), "text/html; charset=utf-8");
                    return;
                }

                if (path.Equals("/boss-mechanics", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteText(context, "Admin map login required.", "text/plain; charset=utf-8");
                        return;
                    }

                    WriteText(context, BuildBossMechanicsHelpHtml(), "text/html; charset=utf-8");
                    return;
                }
                if (path.Equals("/spell-workshop", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteText(context, "Admin map login required.", "text/plain; charset=utf-8");
                        return;
                    }
                    WriteText(context, BuildSpellWorkshopHtml(), "text/html; charset=utf-8");
                    return;
                }
                if (path.Equals("/api/spells/draft", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    string error = null;
                    string json = null;
                    if (!uint.TryParse(context.Request.QueryString["template"], out var templateId) ||
                        !uint.TryParse(context.Request.QueryString["id"], out var targetId) ||
                        !CustomSpellManager.TryCreateWorkshopDraft(templateId, targetId, out json, out error))
                    {
                        context.Response.StatusCode = 400;
                        WriteJson(context, new { ok = false, error = error ?? "Template and custom spell ID are required." });
                        return;
                    }
                    WriteJson(context, new { ok = true, json });
                    return;
                }
                if (path.Equals("/api/spells/save", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { ok = false, error = "Use POST to save a custom spell." });
                        return;
                    }
                    var request = ReadJsonBody<AdminSpellWorkshopSaveRequest>(context);
                    string savedPath = null;
                    string saveError = null;
                    var loaded = 0;
                    var ok = request != null && CustomSpellManager.TrySaveWorkshopJson(request.Json, request.FileName, out savedPath, out loaded, out saveError);
                    if (!ok) context.Response.StatusCode = 400;
                    WriteJson(context, ok
                        ? new { ok = true, message = $"Saved {Path.GetFileName(savedPath)} and reloaded {loaded} custom spell definitions." }
                        : new { ok = false, error = saveError ?? "Invalid spell request." });
                    return;
                }
                if (path.Equals("/api/boss/draft", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { ok = false, error = "Use POST to save a boss draft." });
                        return;
                    }
                    WriteJson(context, SaveBossDraft(ReadJsonBody<AdminBossDraftRequest>(context), GetValidSession(context)?.AccountName ?? "map-token"));
                    return;
                }
                if (path.Equals("/api/boss/profiles", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    WriteJson(context, BuildBossProfileList());
                    return;
                }
                if (path.Equals("/api/boss/profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                        WriteJson(context, GetBossProfile(context.Request.QueryString["profile"]));
                    else if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                        WriteJson(context, HandleBossProfileAction(ReadJsonBody<AdminBossProfileRequest>(context), GetValidSession(context)?.AccountName ?? "map-token"));
                    else
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { ok = false, error = "Use GET or POST for boss profiles." });
                    }
                    return;
                }
                if (path.Equals("/api/boss/active", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    WriteJson(context, BuildActiveBossList());
                    return;
                }
                if (path.Equals("/api/boss/spawn", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/api/boss/despawn", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { ok = false, error = "Admin map login required." });
                        return;
                    }
                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { ok = false, error = "Use POST for boss world actions." });
                        return;
                    }
                    var adminName = GetValidSession(context)?.AccountName ?? "map-token";
                    WriteJson(context, path.EndsWith("/spawn", StringComparison.OrdinalIgnoreCase)
                        ? QueueBossSpawn(ReadJsonBody<AdminBossSpawnRequest>(context), adminName)
                        : QueueBossDespawn(ReadJsonBody<AdminBossDespawnRequest>(context), adminName));
                    return;
                }
                if (path.Equals("/api/session", StringComparison.OrdinalIgnoreCase))
                {
                    var session = GetValidSession(context);
                    WriteJson(context, new
                    {
                        authenticated = session != null,
                        accountName = session?.AccountName,
                        accessLevel = session?.AccessLevel.ToString(),
                        isAdmin = session?.AccessLevel >= AccessLevel.Admin,
                        sessionToken = GetSessionToken(context)
                    });
                    return;
                }

                if (path.Equals("/api/login", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { ok = false, error = "Use POST for login." });
                        return;
                    }

                    WriteJson(context, HandleLogin(context, ReadJsonBody<AdminMapLoginRequest>(context)));
                    return;
                }

                if (path.Equals("/api/logout", StringComparison.OrdinalIgnoreCase))
                {
                    HandleLogout(context);
                    WriteJson(context, new { ok = true });
                    return;
                }

                if (path.Equals("/api/players", StringComparison.OrdinalIgnoreCase))
                {
                    var session = GetValidSession(context);
                    var isAdmin = IsAuthorized(context);
                    if (session == null && !isAdmin)
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Map login required." });
                        return;
                    }

                    WriteJson(context, BuildPlayerSnapshot(session, isAdmin));
                    return;
                }
                if (path.Equals("/api/dungeon", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    if (!TryGetLandblock(context.Request.QueryString["landblock"], out var landblock))
                    {
                        context.Response.StatusCode = 400;
                        WriteJson(context, new { error = "Missing or invalid landblock." });
                        return;
                    }

                    WriteJson(context, BuildDungeonSnapshot(landblock));
                    return;
                }

                if (path.Equals("/api/inventory", StringComparison.OrdinalIgnoreCase))
                {
                    var session = GetValidSession(context);
                    var isAdmin = IsAuthorized(context);
                    if (session == null && !isAdmin)
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Map login required." });
                        return;
                    }

                    if (!TryGetPlayerGuid(context.Request.QueryString["player"], out var playerGuid))
                    {
                        context.Response.StatusCode = 400;
                        WriteJson(context, new { error = "Missing or invalid player." });
                        return;
                    }

                    var player = PlayerManager.GetOnlinePlayer(playerGuid);
                    if (!isAdmin && player?.Account?.AccountId != session.AccountId)
                    {
                        context.Response.StatusCode = 403;
                        WriteJson(context, new { error = "Players may only view inventory belonging to their own account." });
                        return;
                    }

                    WriteJson(context, BuildInventorySnapshot(playerGuid, isAdmin));
                    return;
                }
                if (path.Equals("/api/inventory/item", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { error = "Use POST for inventory edits." });
                        return;
                    }

                    WriteJson(context, HandleInventoryItemEdit(ReadJsonBody<AdminInventoryItemEditRequest>(context)));
                    return;
                }

                if (path.Equals("/api/inventory/property", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }
                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { error = "Use POST for property edits." });
                        return;
                    }
                    WriteJson(context, HandleInventoryPropertyEdit(ReadJsonBody<AdminInventoryPropertyEditRequest>(context)));
                    return;
                }
                if (path.Equals("/api/inventory/delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { error = "Use POST for inventory edits." });
                        return;
                    }

                    WriteJson(context, HandleInventoryItemDelete(ReadJsonBody<AdminInventoryItemDeleteRequest>(context)));
                    return;
                }

                if (path.Equals("/api/player/action", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 405;
                        WriteJson(context, new { error = "Use POST for player actions." });
                        return;
                    }

                    WriteJson(context, HandlePlayerAction(ReadJsonBody<AdminPlayerActionRequest>(context)));
                    return;
                }

                if (path.Equals("/api/loc", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    WriteJson(context, HandleMapLoc(context));
                    return;
                }

                if (path.Equals("/api/watch", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteJson(context, new { error = "Admin map login required." });
                        return;
                    }

                    WriteJson(context, BuildWatchSnapshot(context.Request.QueryString["player"]));
                    return;
                }

                if (path.Equals("/assets/dereth-map", StringComparison.OrdinalIgnoreCase))
                {
                    if (GetValidSession(context) == null && !IsAuthorized(context))
                    {
                        context.Response.StatusCode = 401;
                        WriteText(context, "Map login required.", "text/plain; charset=utf-8");
                        return;
                    }

                    if (!TryWriteMapImage(context))
                    {
                        context.Response.StatusCode = 404;
                        WriteText(context, "Dereth map image not found.", "text/plain; charset=utf-8");
                    }
                    return;
                }

                if (path.Equals("/assets/icon", StringComparison.OrdinalIgnoreCase))
                {
                    // Icons are static game assets; native image requests do not need map authorization.
                    if (!TryParseDataId(context.Request.QueryString["did"], out var did) || !TryWriteIcon(context, did))
                    {
                        context.Response.StatusCode = 404;
                        WriteText(context, "Icon not found.", "text/plain; charset=utf-8");
                    }
                    return;
                }

                context.Response.StatusCode = 404;
                WriteText(context, "Not found", "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Request failed: {ex}");
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = 500;
                    WriteJson(context, new { error = "Admin map request failed." });
                }
            }
        }

        private static object BuildBossProfileList()
        {
            using var db = new ShardDbContext();
            var rows = db.BossMechanicProfile.OrderBy(x => x.ProfileName).ToList();
            var profiles = rows.Select(row => new AdminBossProfileSummary
            {
                Profile = row.ProfileName,
                WeenieClassId = row.WeenieClassId,
                BossName = GetBossWeenieName(DatabaseManager.World.GetWeenie(row.WeenieClassId), row.WeenieClassId),
                DraftRevision = row.DraftRevision,
                PublishedRevision = row.PublishedRevision,
                PreviousRevision = row.PreviousRevision,
                Enabled = row.Enabled,
                ModifiedBy = row.ModifiedBy,
                ModifiedAt = row.ModifiedAt,
                IsTemplate = false
            }).ToList();

            var databaseNames = rows.Select(x => x.ProfileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var databaseWcids = rows.Select(x => x.WeenieClassId).ToHashSet();
            foreach (var template in LoadBossFileTemplates().Where(x => !databaseNames.Contains(x.ProfileName)))
            {
                profiles.Add(new AdminBossProfileSummary
                {
                    Profile = template.ProfileName,
                    WeenieClassId = template.WeenieClassId,
                    BossName = GetBossWeenieName(DatabaseManager.World.GetWeenie(template.WeenieClassId), template.WeenieClassId),
                    DraftRevision = template.DraftRevision,
                    PublishedRevision = template.PublishedRevision,
                    PreviousRevision = 0,
                    Enabled = template.Enabled,
                    ModifiedBy = "Data template",
                    ModifiedAt = template.ModifiedAt,
                    IsTemplate = true,
                    HasWcidConflict = databaseWcids.Contains(template.WeenieClassId),
                    TemplateError = template.Error
                });
            }

            return new { ok = true, profiles = profiles.OrderBy(x => x.Profile).ToList() };
        }

        private static object GetBossProfile(string profileValue)
        {
            if (!TryNormalizeBossProfileName(profileValue, out var profileName, out var error))
                return new { ok = false, error };

            using var db = new ShardDbContext();
            var row = db.BossMechanicProfile.FirstOrDefault(x => x.ProfileName == profileName);
            if (row != null)
            {
                var weenie = DatabaseManager.World.GetWeenie(row.WeenieClassId);
                return new
                {
                    ok = true,
                    profile = row.ProfileName,
                    weenieClassId = row.WeenieClassId,
                    bossName = GetBossWeenieName(weenie, row.WeenieClassId),
                    draftRevision = row.DraftRevision,
                    publishedRevision = row.PublishedRevision,
                    previousRevision = row.PreviousRevision,
                    enabled = row.Enabled,
                    modifiedBy = row.ModifiedBy,
                    modifiedAt = row.ModifiedAt,
                    draftJson = row.DraftJson,
                    publishedJson = row.PublishedJson,
                    previousJson = row.PreviousJson,
                    isTemplate = false,
                    hasWcidConflict = false,
                    sourceFile = (string)null
                };
            }

            var template = LoadBossFileTemplates().FirstOrDefault(x => string.Equals(x.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
            if (template == null)
                return new { ok = false, error = "Boss profile not found." };
            if (!string.IsNullOrWhiteSpace(template.Error))
                return new { ok = false, error = $"Template '{profileName}' is invalid: {template.Error}" };

            var conflict = db.BossMechanicProfile.Any(x => x.WeenieClassId == template.WeenieClassId);
            return new
            {
                ok = true,
                profile = template.ProfileName,
                weenieClassId = template.WeenieClassId,
                bossName = GetBossWeenieName(DatabaseManager.World.GetWeenie(template.WeenieClassId), template.WeenieClassId),
                draftRevision = template.DraftRevision,
                publishedRevision = template.PublishedRevision,
                previousRevision = 0,
                enabled = template.Enabled,
                modifiedBy = "Data template",
                modifiedAt = template.ModifiedAt,
                draftJson = template.DraftJson,
                publishedJson = template.PublishedJson,
                previousJson = (string)null,
                isTemplate = true,
                hasWcidConflict = conflict,
                sourceFile = template.SourceFile
            };
        }

        private static List<AdminBossFileTemplate> LoadBossFileTemplates()
        {
            var templates = new List<AdminBossFileTemplate>();
            var folder = Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "BossMechanics");
            if (!Directory.Exists(folder))
                return templates;

            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                var item = new AdminBossFileTemplate
                {
                    SourceFile = Path.GetFileName(path),
                    ModifiedAt = File.GetLastWriteTimeUtc(path)
                };
                try
                {
                    var json = File.ReadAllText(path);
                    using var wrapper = JsonDocument.Parse(json);
                    var root = wrapper.RootElement;
                    if (root.TryGetProperty("profileName", out var profileNameElement))
                    {
                        item.ProfileName = profileNameElement.GetString()?.Trim().ToLowerInvariant();
                        if (root.TryGetProperty("weenieClassId", out var wcidElement))
                            item.WeenieClassId = wcidElement.GetUInt32();
                        if (root.TryGetProperty("draftRevision", out var draftRevisionElement))
                            item.DraftRevision = draftRevisionElement.GetInt32();
                        if (root.TryGetProperty("publishedRevision", out var publishedRevisionElement))
                            item.PublishedRevision = publishedRevisionElement.GetInt32();
                        if (root.TryGetProperty("enabled", out var enabledElement))
                            item.Enabled = enabledElement.GetBoolean();
                        if (root.TryGetProperty("draftJson", out var draftElement) && draftElement.ValueKind == JsonValueKind.String)
                            item.DraftJson = draftElement.GetString();
                        if (root.TryGetProperty("publishedJson", out var publishedElement) && publishedElement.ValueKind == JsonValueKind.String)
                            item.PublishedJson = publishedElement.GetString();
                    }
                    else
                    {
                        var document = BossMechanicManager.Deserialize(json);
                        item.WeenieClassId = document?.WeenieClassId ?? 0;
                        item.ProfileName = $"template_{item.WeenieClassId}";
                        item.DraftRevision = 1;
                        item.PublishedRevision = 1;
                        item.Enabled = true;
                        item.DraftJson = json;
                        item.PublishedJson = json;
                    }

                    var draft = BossMechanicManager.Deserialize(item.DraftJson ?? item.PublishedJson);
                    var errors = BossMechanicManager.Validate(draft);
                    if (errors.Count > 0)
                        item.Error = string.Join(" ", errors);
                    else if (item.WeenieClassId == 0)
                        item.WeenieClassId = draft.WeenieClassId;
                    if (!TryNormalizeBossProfileName(item.ProfileName, out var normalized, out var nameError))
                        item.Error = nameError;
                    else
                        item.ProfileName = normalized;
                }
                catch (Exception ex)
                {
                    item.ProfileName ??= $"invalid_template_{templates.Count + 1}";
                    item.Error = ex.Message;
                }
                templates.Add(item);
            }
            return templates;
        }
        private static object HandleBossProfileAction(AdminBossProfileRequest request, string modifiedBy)
        {
            if (request == null)
                return new { ok = false, error = "Boss profile request is required." };
            if (!TryNormalizeBossProfileName(request.Profile, out var profileName, out var nameError))
                return new { ok = false, error = nameError };

            var action = request.Action?.Trim().ToLowerInvariant();
            using var db = new ShardDbContext();
            var row = db.BossMechanicProfile.FirstOrDefault(x => x.ProfileName == profileName);

            if (action == "create")
            {
                if (row != null)
                    return new { ok = false, error = "That profile name already exists." };
                if (request.WeenieClassId == 0)
                    return new { ok = false, error = "Boss WCID must be nonzero." };
                if (db.BossMechanicProfile.Any(x => x.WeenieClassId == request.WeenieClassId))
                    return new { ok = false, error = "That boss WCID is already assigned to another profile." };
                var weenie = DatabaseManager.World.GetWeenie(request.WeenieClassId);
                if (weenie == null || (WeenieType)weenie.Type != WeenieType.Creature)
                    return new { ok = false, error = $"WCID {request.WeenieClassId} is not an existing creature." };

                var document = string.IsNullOrWhiteSpace(request.Json)
                    ? BossMechanicManager.NewDocument(request.WeenieClassId)
                    : BossMechanicManager.Deserialize(request.Json);
                if (document != null)
                    document.WeenieClassId = request.WeenieClassId;
                var errors = BossMechanicManager.Validate(document);
                if (errors.Count > 0)
                    return new { ok = false, error = string.Join(" ", errors), errors };

                row = new BossMechanicProfile
                {
                    ProfileName = profileName,
                    WeenieClassId = request.WeenieClassId,
                    DraftRevision = 1,
                    DraftJson = BossMechanicManager.Serialize(document),
                    Enabled = false,
                    ModifiedBy = modifiedBy,
                    ModifiedAt = DateTime.UtcNow
                };
                db.BossMechanicProfile.Add(row);
                db.SaveChanges();
                log.Warn($"[DerpACE AdminMap] {modifiedBy} created boss profile {profileName} for WCID {row.WeenieClassId}.");
                return new { ok = true, message = $"Created draft '{profileName}' for {GetBossWeenieName(weenie, row.WeenieClassId)} ({row.WeenieClassId}).", profile = profileName };
            }

            if (row == null)
                return new { ok = false, error = "Boss profile not found." };

            switch (action)
            {
                case "validate":
                    var validateDocument = BossMechanicManager.Deserialize(string.IsNullOrWhiteSpace(request.Json) ? row.DraftJson : request.Json);
                    var validateErrors = BossMechanicManager.Validate(validateDocument);
                    if (validateDocument != null && validateDocument.WeenieClassId != row.WeenieClassId)
                        validateErrors.Add($"Profile WCID is {row.WeenieClassId}, but the draft uses {validateDocument.WeenieClassId}.");
                    return validateErrors.Count == 0
                        ? new { ok = true, message = "Draft is valid and ready to publish.", errors = validateErrors }
                        : new { ok = false, message = "Draft has validation errors.", errors = validateErrors };

                case "save":
                    if (string.IsNullOrWhiteSpace(request.Json))
                        return new { ok = false, error = "Draft JSON is required." };
                    var document = BossMechanicManager.Deserialize(request.Json);
                    var errors = BossMechanicManager.Validate(document);
                    if (errors.Count > 0)
                        return new { ok = false, error = string.Join(" ", errors), errors };
                    if (document.WeenieClassId != row.WeenieClassId)
                        return new { ok = false, error = $"Profile WCID is {row.WeenieClassId}, but the draft uses {document.WeenieClassId}." };
                    row.DraftJson = BossMechanicManager.Serialize(document);
                    row.DraftRevision++;
                    break;

                case "publish":
                    var publishDocument = BossMechanicManager.Deserialize(row.DraftJson);
                    var publishErrors = BossMechanicManager.Validate(publishDocument);
                    if (publishErrors.Count > 0)
                        return new { ok = false, error = string.Join(" ", publishErrors), errors = publishErrors };
                    if (DatabaseManager.World.GetWeenie(row.WeenieClassId) == null)
                        return new { ok = false, error = $"Boss WCID {row.WeenieClassId} is not loaded in world data." };
                    row.PreviousJson = row.PublishedJson;
                    row.PreviousRevision = row.PublishedRevision;
                    row.PublishedJson = row.DraftJson;
                    row.PublishedRevision = row.DraftRevision;
                    row.Enabled = true;
                    break;

                case "rollback":
                    if (string.IsNullOrWhiteSpace(row.PreviousJson))
                        return new { ok = false, error = "No previous published revision exists." };
                    (row.PublishedJson, row.PreviousJson) = (row.PreviousJson, row.PublishedJson);
                    (row.PublishedRevision, row.PreviousRevision) = (row.PreviousRevision, row.PublishedRevision);
                    row.Enabled = true;
                    break;

                case "set-enabled":
                    row.Enabled = request.Enabled;
                    if (row.Enabled && string.IsNullOrWhiteSpace(row.PublishedJson))
                        return new { ok = false, error = "Publish a valid revision before enabling this profile." };
                    break;

                case "restore-published":
                    if (string.IsNullOrWhiteSpace(row.PublishedJson))
                        return new { ok = false, error = "This profile has no published revision." };
                    row.DraftJson = row.PublishedJson;
                    row.DraftRevision++;
                    break;

                default:
                    return new { ok = false, error = "Supported actions are create, validate, save, publish, rollback, set-enabled, and restore-published." };
            }

            row.ModifiedBy = modifiedBy;
            row.ModifiedAt = DateTime.UtcNow;
            db.SaveChanges();
            BossMechanicManager.Invalidate(row.WeenieClassId);
            log.Warn($"[DerpACE AdminMap] {modifiedBy} performed boss profile action '{action}' on {profileName} r{row.DraftRevision}/{row.PublishedRevision}.");
            return new
            {
                ok = true,
                message = action switch
                {
                    "save" => $"Saved draft revision {row.DraftRevision}.",
                    "publish" => $"Published revision {row.PublishedRevision}; new spawns use it immediately.",
                    "rollback" => $"Rolled back to published revision {row.PublishedRevision}.",
                    "set-enabled" => row.Enabled ? "Boss mechanics enabled." : "Boss mechanics disabled for new and active encounters.",
                    _ => $"Restored published revision {row.PublishedRevision} into draft revision {row.DraftRevision}."
                }
            };
        }

        private static object BuildActiveBossList()
        {
            using var db = new ShardDbContext();
            var profiles = db.BossMechanicProfile.ToList().GroupBy(x => x.WeenieClassId).ToDictionary(x => x.Key, x => x.First());
            var bosses = new List<object>();
            foreach (var landblock in LandblockManager.GetLoadedLandblocks())
            {
                foreach (var creature in landblock.GetAllWorldObjectsForDiagnostics().OfType<Creature>())
                {
                    if (!profiles.TryGetValue(creature.WeenieClassId, out var profile))
                        continue;
                    bosses.Add(new
                    {
                        guid = $"0x{creature.Guid.Full:X8}",
                        name = creature.Name,
                        profile = profile.ProfileName,
                        weenieClassId = creature.WeenieClassId,
                        enabled = profile.Enabled,
                        health = creature.Health?.Current ?? 0,
                        maxHealth = creature.Health?.MaxValue ?? 0,
                        loc = creature.Location?.ToLOCString(),
                        landblock = creature.Location?.LandblockId.ToString()
                    });
                }
            }
            return new { ok = true, bosses };
        }

        private static object QueueBossSpawn(AdminBossSpawnRequest request, string adminName)
        {
            if (request == null)
                return new { ok = false, error = "Boss spawn request is required." };
            if (!TryNormalizeBossProfileName(request.Profile, out var profileName, out var profileError))
                return new { ok = false, error = profileError };

            using var db = new ShardDbContext();
            var row = db.BossMechanicProfile.FirstOrDefault(x => x.ProfileName == profileName);
            if (row == null)
                return new { ok = false, error = "Boss profile not found." };
            if (!row.Enabled || string.IsNullOrWhiteSpace(row.PublishedJson))
                return new { ok = false, error = "Publish and enable the boss profile before spawning it." };
            var published = BossMechanicManager.Deserialize(row.PublishedJson);
            var errors = BossMechanicManager.Validate(published);
            if (errors.Count > 0)
                return new { ok = false, error = string.Join(" ", errors), errors };
            var weenie = DatabaseManager.World.GetWeenie(row.WeenieClassId);
            if (weenie == null || (WeenieType)weenie.Type != WeenieType.Creature)
                return new { ok = false, error = $"Boss WCID {row.WeenieClassId} is not a loaded creature." };

            Position spawnPosition;
            string destination;
            if (!string.IsNullOrWhiteSpace(request.PlayerGuid))
            {
                if (!TryGetPlayerGuid(request.PlayerGuid, out var playerGuid))
                    return new { ok = false, error = "Player GUID is invalid." };
                var player = PlayerManager.GetOnlinePlayer(playerGuid);
                if (player?.Location == null)
                    return new { ok = false, error = "The selected player is not online." };
                spawnPosition = player.Location.InFrontOf(Math.Clamp(request.Distance <= 0 ? 5.0f : request.Distance, 2.0f, 30.0f), true);
                destination = $"near {player.Name}";
            }
            else if (!string.IsNullOrWhiteSpace(request.Loc))
            {
                if (!TryParseLoc(request.Loc, out spawnPosition, out var locError))
                    return new { ok = false, error = locError };
                destination = spawnPosition.ToLOCString();
            }
            else
                return new { ok = false, error = "Choose an online player or provide a full LOC string." };

            spawnPosition = new Position(spawnPosition);
            spawnPosition.LandblockId = new LandblockId(spawnPosition.GetCell());
            var count = Math.Clamp(request.Count <= 0 ? 1 : request.Count, 1, 10);
            WorldManager.EnqueueAction(new ActionEventDelegate(() =>
            {
                var spawned = 0;
                for (var i = 0; i < count; i++)
                {
                    var creature = WorldObjectFactory.CreateNewWorldObject(row.WeenieClassId) as Creature;
                    if (creature == null)
                        break;
                    var position = new Position(spawnPosition);
                    if (count > 1)
                    {
                        var angle = i * Math.PI * 2.0 / count;
                        position.PositionX += (float)Math.Cos(angle) * 3.0f;
                        position.PositionY += (float)Math.Sin(angle) * 3.0f;
                        position.LandblockId = new LandblockId(position.GetCell());
                    }
                    creature.Location = position;
                    BossMechanicManager.TryApplyBossMutators(creature);
                    if (creature.EnterWorld())
                        spawned++;
                    else
                        creature.Destroy();
                }
                log.Warn($"[DerpACE AdminMap] {adminName} spawned {spawned}/{count} '{profileName}' boss creature(s) {destination}.");
            }));

            return new { ok = true, message = $"Queued {count} x {profileName} ({row.WeenieClassId}) {destination}." };
        }

        private static object QueueBossDespawn(AdminBossDespawnRequest request, string adminName)
        {
            if (!TryGetPlayerGuid(request?.Guid, out var guid))
                return new { ok = false, error = "Boss GUID is missing or invalid." };

            Creature boss = null;
            foreach (var landblock in LandblockManager.GetLoadedLandblocks())
            {
                boss = landblock.GetAllWorldObjectsForDiagnostics().OfType<Creature>().FirstOrDefault(x => x.Guid.Full == guid);
                if (boss != null)
                    break;
            }
            if (boss == null)
                return new { ok = false, error = "That boss is no longer active." };

            using var db = new ShardDbContext();
            if (!db.BossMechanicProfile.Any(x => x.WeenieClassId == boss.WeenieClassId))
                return new { ok = false, error = "The selected creature is not assigned to a boss profile." };

            var captured = boss;
            var bossName = boss.Name;
            WorldManager.EnqueueAction(new ActionEventDelegate(() =>
            {
                if (!captured.IsDestroyed)
                {
                    BossMechanicManager.Reset(captured);
                    captured.FadeOutAndDestroy();
                }
                log.Warn($"[DerpACE AdminMap] {adminName} despawned boss {bossName} (0x{guid:X8}).");
            }));
            return new { ok = true, message = $"Queued despawn for {bossName} (0x{guid:X8})." };
        }

        private static string GetBossWeenieName(ACE.Database.Models.World.Weenie weenie, uint wcid)
        {
            return weenie?.WeeniePropertiesString?.FirstOrDefault(x => x.Type == (int)PropertyString.Name)?.Value
                ?? $"Unknown WCID {wcid}";
        }
        private static bool TryNormalizeBossProfileName(string value, out string profileName, out string error)
        {
            profileName = (value ?? string.Empty).Trim().ToLowerInvariant();
            error = null;
            if (profileName.Length < 3 || profileName.Length > 64 || profileName.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-'))
            {
                error = "Profile names use 3-64 letters, numbers, _ or -.";
                return false;
            }
            return true;
        }
        private static object SaveBossDraft(AdminBossDraftRequest request, string modifiedBy)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Profile) || string.IsNullOrWhiteSpace(request.Json))
                return new { ok = false, error = "Profile and generated JSON are required." };
            var profileName = request.Profile.Trim().ToLowerInvariant();
            var document = BossMechanicManager.Deserialize(request.Json);
            var errors = BossMechanicManager.Validate(document);
            if (errors.Count > 0)
                return new { ok = false, error = string.Join(" ", errors), errors };
            using var db = new ShardDbContext();
            var row = db.BossMechanicProfile.FirstOrDefault(x => x.ProfileName == profileName);
            if (row == null)
                return new { ok = false, error = "Create the boss profile first, then save its rules." };
            if (row.WeenieClassId != document.WeenieClassId)
                return new { ok = false, error = $"Profile WCID is {row.WeenieClassId}, but the builder JSON uses {document.WeenieClassId}." };
            row.DraftJson = BossMechanicManager.Serialize(document);
            row.DraftRevision++;
            row.ModifiedBy = modifiedBy;
            row.ModifiedAt = DateTime.UtcNow;
            db.SaveChanges();
            return new { ok = true, message = $"Saved draft '{profileName}' revision {row.DraftRevision}.", revision = row.DraftRevision };
        }
        private static bool IsAuthorized(HttpListenerContext context)
        {
            var session = GetValidSession(context);
            if (session?.AccessLevel >= AccessLevel.Admin)
                return true;

            var token = DerpAceConfigManager.Config.AdminMapToken;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var provided = context.Request.Headers["X-DerpACE-Map-Token"];
            if (string.IsNullOrWhiteSpace(provided))
                provided = context.Request.QueryString["token"];

            return string.Equals(provided, token, StringComparison.Ordinal);
        }

        private static object HandleLogin(HttpListenerContext context, AdminMapLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Password))
            {
                context.Response.StatusCode = 400;
                return new { ok = false, error = "Account and password are required." };
            }

            var accountName = request.Account.Trim().ToLowerInvariant();
            var account = DatabaseManager.Authentication.GetAccountByName(accountName);
            if (account == null || !account.PasswordMatches(request.Password))
            {
                context.Response.StatusCode = 401;
                return new { ok = false, error = "Invalid account or password." };
            }

            var accessLevel = (AccessLevel)account.AccessLevel;
            if (account.BanExpireTime.HasValue && DateTime.UtcNow < account.BanExpireTime.Value)
            {
                context.Response.StatusCode = 403;
                return new { ok = false, error = "That account is banned." };
            }

            if (accessLevel >= AccessLevel.Admin && (NetworkManager.Find(account.AccountName) != null || NetworkManager.Find(account.AccountId) != null))
            {
                context.Response.StatusCode = 409;
                return new { ok = false, error = "Log out of the game client before using that account for the admin map." };
            }

            var token = CreateSessionToken();
            var session = new AdminMapSession
            {
                AccountId = account.AccountId,
                AccountName = account.AccountName,
                AccessLevel = accessLevel,
                ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime)
            };

            Sessions[token] = session;
            SetSessionCookie(context, token, session.ExpiresUtc);

            log.Info($"[DerpACE AdminMap] {account.AccountName} logged in from {context.Request.RemoteEndPoint?.Address}");

            return new
            {
                ok = true,
                accountName = account.AccountName,
                accessLevel = accessLevel.ToString(),
                isAdmin = accessLevel >= AccessLevel.Admin,
                expiresUtc = session.ExpiresUtc,
                sessionToken = token
            };
        }

        private static void HandleLogout(HttpListenerContext context)
        {
            var token = GetSessionToken(context);
            if (!string.IsNullOrWhiteSpace(token))
                Sessions.TryRemove(token, out _);

            ClearSessionCookie(context);
        }

        private static AdminMapSession GetValidSession(HttpListenerContext context)
        {
            var token = GetSessionToken(context);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            if (!Sessions.TryGetValue(token, out var session))
                return null;

            if (session.ExpiresUtc <= DateTime.UtcNow)
            {
                Sessions.TryRemove(token, out _);
                return null;
            }

            if (session.AccessLevel >= AccessLevel.Admin && (NetworkManager.Find(session.AccountName) != null || NetworkManager.Find(session.AccountId) != null))
            {
                Sessions.TryRemove(token, out _);
                return null;
            }

            session.ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime);
            return session;
        }

        private static string GetSessionToken(HttpListenerContext context)
        {
            // Explicit session tokens from links/fetch calls should win over stale browser cookies.
            var token = context?.Request?.Headers["X-DerpACE-Map-Session"];
            if (!string.IsNullOrWhiteSpace(token))
                return token;

            token = context?.Request?.QueryString["session"];
            if (!string.IsNullOrWhiteSpace(token))
                return token;

            return context?.Request?.Cookies?[SessionCookieName]?.Value;
        }
        private static string CreateSessionToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void SetSessionCookie(HttpListenerContext context, string token, DateTime expiresUtc)
        {
            var cookie = new Cookie(SessionCookieName, token)
            {
                HttpOnly = true,
                Path = "/",
                Expires = expiresUtc
            };

            context.Response.Cookies.Add(cookie);
        }

        private static void ClearSessionCookie(HttpListenerContext context)
        {
            context.Response.Cookies.Add(new Cookie(SessionCookieName, "")
            {
                HttpOnly = true,
                Path = "/",
                Expires = DateTime.UtcNow.AddDays(-1)
            });
        }

        private static AdminMapSnapshot BuildPlayerSnapshot(AdminMapSession session, bool isAdmin)
        {
            var config = DerpAceConfigManager.Config;
            var visiblePlayers = new List<Player>();
            var players = new List<AdminMapPlayer>();
            var allOnline = PlayerManager.GetAllOnline();
            HashSet<uint> playerVisibleGuids = null;

            if (!isAdmin)
            {
                playerVisibleGuids = new HashSet<uint>();
                foreach (var accountPlayer in allOnline.Where(p => p?.Account?.AccountId == session.AccountId))
                {
                    playerVisibleGuids.Add(accountPlayer.Guid.Full);
                    if (accountPlayer.Fellowship?.FellowshipMembers == null)
                        continue;

                    foreach (var member in accountPlayer.Fellowship.FellowshipMembers.Values)
                        if (member.TryGetTarget(out var fellow) && fellow != null)
                            playerVisibleGuids.Add(fellow.Guid.Full);
                }
            }

            foreach (var player in allOnline)
            {
                if (player?.Location == null)
                    continue;

                if (!isAdmin && !playerVisibleGuids.Contains(player.Guid.Full))
                    continue;

                if (isAdmin && !config.AdminMapShowAdmins && (player.IsAdmin || player.IsSentinel || player.IsEnvoy || player.IsArch || player.IsPsr))
                    continue;

                visiblePlayers.Add(player);
                var mapPlayer = BuildPlayer(player);
                mapPlayer.IsOwnedBySession = !isAdmin && player.Account?.AccountId == session.AccountId;
                players.Add(mapPlayer);
            }

            return new AdminMapSnapshot
            {
                ServerTimeUtc = DateTime.UtcNow,
                RefreshSeconds = Math.Max(1, config.AdminMapRefreshSeconds),
                OnlineCount = players.Count,
                MapImageUrl = HasMapImage() ? "/assets/dereth-map" : null,
                MapBounds = new AdminMapBounds
                {
                    Left = ClampPercent(config.AdminMapBoundsLeftPct),
                    Top = ClampPercent(config.AdminMapBoundsTopPct),
                    Right = ClampPercent(config.AdminMapBoundsRightPct),
                    Bottom = ClampPercent(config.AdminMapBoundsBottomPct)
                },
                Players = players,
                Blips = isAdmin ? BuildNearbyMapBlips(visiblePlayers, false, 0) : new List<AdminMapBlip>(),
                Stats = isAdmin ? BuildMapStats(visiblePlayers) : null,
                Feeds = isAdmin ? BuildFeeds() : null
            };
        }
        private static AdminMapFeeds BuildFeeds()
        {
            lock (FeedLock)
            {
                return new AdminMapFeeds
                {
                    Chat = ChatFeed
                        .OrderByDescending(e => e.Utc)
                        .Take(SnapshotFeedEntries)
                        .ToList(),
                    Rares = RareFeed
                        .OrderByDescending(e => e.Utc)
                        .Take(SnapshotFeedEntries)
                        .ToList()
                };
            }
        }

        private static void TrimFeed<T>(List<T> feed)
        {
            if (feed.Count > MaxFeedEntries)
                feed.RemoveRange(0, feed.Count - MaxFeedEntries);
        }

        private static AdminDungeonSnapshot BuildDungeonSnapshot(uint landblock)
        {
            var config = DerpAceConfigManager.Config;
            var map = DungeonMapCache.GetOrAdd(landblock & 0xFFFF0000, BuildDungeonMap);
            var visiblePlayers = new List<Player>();
            var players = new List<AdminDungeonPlayer>();

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null)
                    continue;

                if ((player.Location.Cell & 0xFFFF0000) != (landblock & 0xFFFF0000))
                    continue;

                if (!config.AdminMapShowAdmins && (player.IsAdmin || player.IsSentinel || player.IsEnvoy || player.IsArch || player.IsPsr))
                    continue;

                visiblePlayers.Add(player);
                players.Add(BuildDungeonPlayer(player));
            }

            return new AdminDungeonSnapshot
            {
                Landblock = $"0x{landblock & 0xFFFF0000:X8}",
                Generated = map.Generated,
                Error = map.Error,
                MinX = map.MinX,
                MinY = map.MinY,
                MaxX = map.MaxX,
                MaxY = map.MaxY,
                MinZ = map.MinZ,
                MaxZ = map.MaxZ,
                Svg = map.Svg,
                Players = players,
                Blips = BuildNearbyMapBlips(visiblePlayers, true, landblock)
            };
        }

        private static object BuildWatchSnapshot(string playerGuidValue)
        {
            if (!TryGetPlayerGuid(playerGuidValue, out var playerGuid))
                return new { ok = false, error = "Missing or invalid player." };

            var target = PlayerManager.GetOnlinePlayer(playerGuid);
            if (target?.Location == null)
                return new { ok = false, error = "Player is not online." };

            var radius = CreatureBlipRadius;
            var radiusSq = radius * radius;
            var blips = new List<AdminWatchBlip>
            {
                BuildWatchBlip(target, target, "target", "White")
            };
            var seen = new HashSet<uint> { target.Guid.Full };
            var config = DerpAceConfigManager.Config;

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null || player == target)
                    continue;

                if (!config.AdminMapShowAdmins && (player.IsAdmin || player.IsSentinel || player.IsEnvoy || player.IsArch || player.IsPsr))
                    continue;

                if (target.Location.Distance2DSquared(player.Location) > radiusSq)
                    continue;

                if (seen.Add(player.Guid.Full))
                    blips.Add(BuildWatchBlip(player, target, "player", "White"));
            }

            if (target.CurrentLandblock != null)
            {
                foreach (var worldObject in target.CurrentLandblock.GetAllWorldObjectsForDiagnostics())
                {
                    if (worldObject == null || worldObject == target || worldObject is Player || worldObject.Location == null)
                        continue;

                    if (!TryGetMapBlipKind(worldObject, out var kind, out var radarColor))
                        continue;

                    if (worldObject is Creature creature && (!creature.IsAlive || creature.Teleporting))
                        continue;

                    if (target.Location.Distance2DSquared(worldObject.Location) > radiusSq)
                        continue;

                    if (!seen.Add(worldObject.Guid.Full))
                        continue;

                    blips.Add(BuildWatchBlip(worldObject, target, kind, radarColor.ToString()));
                }
            }

            return new AdminWatchSnapshot
            {
                Ok = true,
                ServerTimeUtc = DateTime.UtcNow,
                Radius = radius,
                Player = BuildPlayer(target),
                Blips = blips
                    .OrderBy(b => b.Kind == "target" ? 0 : b.Kind == "player" ? 1 : 2)
                    .ThenBy(b => b.Distance)
                    .Take(MaxCreatureBlips)
                    .ToList()
            };
        }

        private static AdminInventorySnapshot BuildInventorySnapshot(uint playerGuid, bool editable = true)
        {
            var player = PlayerManager.GetOnlinePlayer(playerGuid);
            if (player == null)
                return AdminInventorySnapshot.Fail("Player is not online.");

            var items = new List<AdminInventoryItem>();

            foreach (var item in player.EquippedObjects.Values.OrderBy(i => i.CurrentWieldedLocation ?? 0).ThenBy(i => i.Name))
                items.Add(BuildInventoryItem(item, "Equipped", null, true));

            AddInventoryItems(items, player, player.Inventory.Values, "Main Pack", 0);

            return new AdminInventorySnapshot
            {
                PlayerName = player.Name,
                PlayerGuid = $"0x{player.Guid.Full:X8}",
                Encumbrance = player.EncumbranceVal ?? 0,
                CoinValue = player.CoinValue ?? 0,
                Editable = editable,
                Items = items
            };
        }

        private static void AddInventoryItems(List<AdminInventoryItem> items, Player player, IEnumerable<WorldObject> inventory, string containerName, int depth)
        {
            foreach (var item in inventory.OrderBy(i => i.PlacementPosition ?? 0).ThenBy(i => i.Name))
            {
                items.Add(BuildInventoryItem(item, containerName, item.ContainerId, false, depth));

                if (item is Container container)
                    AddInventoryItems(items, player, container.Inventory.Values, item.Name, depth + 1);
            }
        }

        private static AdminInventoryItem BuildInventoryItem(WorldObject item, string containerName, uint? containerId, bool equipped, int depth = 0)
        {
            return new AdminInventoryItem
            {
                Name = item.Name,
                Guid = $"0x{item.Guid.Full:X8}",
                GuidValue = item.Guid.Full,
                WeenieClassId = item.WeenieClassId,
                WeenieClassName = item.WeenieClassName,
                WeenieType = item.WeenieType.ToString(),
                ItemType = item.ItemType.ToString(),
                IconId = item.IconId,
                IconOverlayId = item.IconOverlayId,
                IconUnderlayId = item.IconUnderlayId,
                Container = containerName,
                ContainerGuid = containerId.HasValue ? $"0x{containerId.Value:X8}" : null,
                Equipped = equipped,
                Depth = depth,
                Placement = item.PlacementPosition ?? 0,
                StackSize = item.StackSize,
                MaxStackSize = item.MaxStackSize,
                Value = item.Value,
                Encumbrance = item.EncumbranceVal,
                Workmanship = item.ItemWorkmanship,
                LongDesc = item.LongDesc,
                Material = item.MaterialType?.ToString(),
                MaterialType = item.MaterialType.HasValue ? (int)item.MaterialType.Value : null,
                PaletteTemplate = item.PaletteTemplate,
                Shade = item.Shade,
                Damage = item.Damage,
                DamageMod = item.DamageMod,
                DamageVariance = item.DamageVariance,
                ElementalDamageBonus = item.ElementalDamageBonus,
                ElementalDamageMod = item.ElementalDamageMod,
                ArmorLevel = item.ArmorLevel,
                Structure = item.Structure,
                MaxStructure = item.MaxStructure,
                ItemCurMana = item.ItemCurMana,
                ItemMaxMana = item.ItemMaxMana,
                DamageRating = item.DamageRating,
                DamageResistRating = item.DamageResistRating,
                CritDamageRating = item.CritDamageRating,
                CritDamageResistRating = item.CritDamageResistRating,
                GearDamage = item.GearDamage,
                GearDamageResist = item.GearDamageResist,
                GearCritDamage = item.GearCritDamage,
                GearCritDamageResist = item.GearCritDamageResist,
                WieldedLocation = item.CurrentWieldedLocation?.ToString(),
                IsContainer = item is Container,
                IsAttuned = item.IsAttunedOrContainsAttuned,
                IsBonded = item.Bonded != null,
                Properties = BuildItemProperties(item)
            };
        }

        private static Dictionary<string, List<AdminItemProperty>> BuildItemProperties(WorldObject item)
        {
            var result = new Dictionary<string, List<AdminItemProperty>>(StringComparer.OrdinalIgnoreCase);
            AddItemProperties(result, "bool", item.GetAllPropertyBools());
            AddItemProperties(result, "did", item.GetAllPropertyDataId());
            AddItemProperties(result, "float", item.GetAllPropertyFloat());
            AddItemProperties(result, "iid", item.GetAllPropertyInstanceId());
            AddItemProperties(result, "int", item.GetAllPropertyInt());
            AddItemProperties(result, "int64", item.GetAllPropertyInt64());
            AddItemProperties(result, "string", item.GetAllPropertyString());
            return result;
        }

        private static void AddItemProperties<TKey, TValue>(Dictionary<string, List<AdminItemProperty>> result, string family, Dictionary<TKey, TValue> values) where TKey : Enum
        {
            result[family] = values
                .OrderBy(x => Convert.ToUInt32(x.Key, CultureInfo.InvariantCulture))
                .Select(x => new AdminItemProperty
                {
                    Key = Convert.ToUInt32(x.Key, CultureInfo.InvariantCulture),
                    Name = x.Key.ToString(),
                    Value = Convert.ToString(x.Value, CultureInfo.InvariantCulture)
                })
                .ToList();
        }
        private static object HandleInventoryItemEdit(AdminInventoryItemEditRequest request)
        {
            if (!TryGetEditableItem(request?.PlayerGuid, request?.ItemGuid, out var player, out var item, out var foundInContainer, out var rootOwner, out _, out var error))
                return new { ok = false, error };

            var oldEncumbrance = item.EncumbranceVal ?? 0;
            var oldValue = item.Value ?? 0;
            var changed = false;

            if (request.StackSize.HasValue)
            {
                if (!(item is Stackable))
                    return new { ok = false, error = "This item is not stackable." };

                var max = item.MaxStackSize ?? ushort.MaxValue;
                var next = Math.Clamp(request.StackSize.Value, 1, max);
                item.SetStackSize(next);
                player.Session.Network.EnqueueSend(new GameMessageSetStackSize(item));
                changed = true;
            }

            if (request.Value.HasValue)
            {
                item.Value = Math.Max(0, request.Value.Value);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(item, PropertyInt.Value, item.Value ?? 0));
                changed = true;
            }

            if (request.Encumbrance.HasValue)
            {
                item.EncumbranceVal = Math.Max(0, request.Encumbrance.Value);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(item, PropertyInt.EncumbranceVal, item.EncumbranceVal ?? 0));
                changed = true;
            }

            if (request.Workmanship.HasValue)
            {
                item.ItemWorkmanship = Math.Max(0, request.Workmanship.Value);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(item, PropertyInt.ItemWorkmanship, item.ItemWorkmanship ?? 0));
                changed = true;
            }

            if (request.Name != null)
            {
                var name = request.Name.Trim();
                if (name.Length == 0)
                    return new { ok = false, error = "Item name cannot be blank." };

                item.Name = name.Length > 120 ? name.Substring(0, 120) : name;
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyString(item, PropertyString.Name, item.Name));
                changed = true;
            }

            if (request.LongDesc != null)
            {
                item.LongDesc = string.IsNullOrWhiteSpace(request.LongDesc) ? null : request.LongDesc.Trim();
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyString(item, PropertyString.LongDesc, item.LongDesc ?? ""));
                changed = true;
            }

            changed |= ApplyIntEdit(player, item, request.Damage, PropertyInt.Damage, value => item.Damage = Math.Max(0, value));
            changed |= ApplyFloatEdit(player, item, request.DamageMod, PropertyFloat.DamageMod, value => item.DamageMod = Math.Max(0.0, value));
            changed |= ApplyFloatEdit(player, item, request.DamageVariance, PropertyFloat.DamageVariance, value => item.DamageVariance = Math.Clamp(value, 0.0, 1.0));
            changed |= ApplyIntEdit(player, item, request.ElementalDamageBonus, PropertyInt.ElementalDamageBonus, value => item.ElementalDamageBonus = Math.Max(0, value));
            changed |= ApplyFloatEdit(player, item, request.ElementalDamageMod, PropertyFloat.ElementalDamageMod, value => item.ElementalDamageMod = Math.Max(0.0, value));
            changed |= ApplyIntEdit(player, item, request.ArmorLevel, PropertyInt.ArmorLevel, value => item.ArmorLevel = Math.Max(0, value));
            changed |= ApplyIntEdit(player, item, request.Structure, PropertyInt.Structure, value => item.Structure = (ushort)Math.Clamp(value, 0, ushort.MaxValue));
            changed |= ApplyIntEdit(player, item, request.MaxStructure, PropertyInt.MaxStructure, value => item.MaxStructure = (ushort)Math.Clamp(value, 0, ushort.MaxValue));
            changed |= ApplyIntEdit(player, item, request.ItemCurMana, PropertyInt.ItemCurMana, value => item.ItemCurMana = Math.Max(0, value));
            changed |= ApplyIntEdit(player, item, request.ItemMaxMana, PropertyInt.ItemMaxMana, value => item.ItemMaxMana = Math.Max(0, value));
            changed |= ApplyIntEdit(player, item, request.PaletteTemplate, PropertyInt.PaletteTemplate, value => item.PaletteTemplate = Math.Max(0, value));
            changed |= ApplyFloatEdit(player, item, request.Shade, PropertyFloat.Shade, value => item.Shade = Math.Clamp(value, 0.0, 1.0));
            changed |= ApplyIntEdit(player, item, request.MaterialType, PropertyInt.MaterialType, value => item.MaterialType = (MaterialType)Math.Max(0, value));
            changed |= ApplyIntEdit(player, item, request.DamageRating, PropertyInt.DamageRating, value => item.DamageRating = value);
            changed |= ApplyIntEdit(player, item, request.DamageResistRating, PropertyInt.DamageResistRating, value => item.DamageResistRating = value);
            changed |= ApplyIntEdit(player, item, request.CritDamageRating, PropertyInt.CritDamageRating, value => item.CritDamageRating = value);
            changed |= ApplyIntEdit(player, item, request.CritDamageResistRating, PropertyInt.CritDamageResistRating, value => item.CritDamageResistRating = value);
            changed |= ApplyIntEdit(player, item, request.GearDamage, PropertyInt.GearDamage, value => item.GearDamage = value);
            changed |= ApplyIntEdit(player, item, request.GearDamageResist, PropertyInt.GearDamageResist, value => item.GearDamageResist = value);
            changed |= ApplyIntEdit(player, item, request.GearCritDamage, PropertyInt.GearCritDamage, value => item.GearCritDamage = value);
            changed |= ApplyIntEdit(player, item, request.GearCritDamageResist, PropertyInt.GearCritDamageResist, value => item.GearCritDamageResist = value);

            if (!changed)
                return new { ok = false, error = "No supported item changes were provided." };

            ApplyInventoryDelta(rootOwner, foundInContainer, item, oldEncumbrance, oldValue);
            item.SaveBiotaToDatabase();
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));

            log.Info($"[DerpACE AdminMap] Edited item {item.Name} ({item.Guid}) for {player.Name} ({player.Guid})");
            return new { ok = true, inventory = BuildInventorySnapshot(player.Guid.Full) };
        }

        private static object HandleInventoryPropertyEdit(AdminInventoryPropertyEditRequest request)
        {
            if (!TryGetEditableItem(request?.PlayerGuid, request?.ItemGuid, out var player, out var item, out _, out _, out _, out var error))
                return new { ok = false, error };
            if (string.IsNullOrWhiteSpace(request.Family) || request.Value == null)
                return new { ok = false, error = "Property family and value are required." };

            try
            {
                switch (request.Family.Trim().ToLowerInvariant())
                {
                    case "bool":
                        if (!Enum.IsDefined(typeof(PropertyBool), (ushort)request.Key) || !bool.TryParse(request.Value, out var boolValue))
                            throw new ArgumentException("Invalid bool property or value.");
                        var boolProperty = (PropertyBool)(ushort)request.Key;
                        item.SetProperty(boolProperty, boolValue);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyBool(item, boolProperty, boolValue));
                        break;
                    case "did":
                        if (!Enum.IsDefined(typeof(PropertyDataId), (ushort)request.Key) || !TryParseUIntValue(request.Value, out var didValue))
                            throw new ArgumentException("Invalid DID property or value.");
                        item.SetProperty((PropertyDataId)(ushort)request.Key, didValue);
                        break;
                    case "float":
                        if (!Enum.IsDefined(typeof(PropertyFloat), (ushort)request.Key) || !double.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                            throw new ArgumentException("Invalid float property or value.");
                        var floatProperty = (PropertyFloat)(ushort)request.Key;
                        item.SetProperty(floatProperty, floatValue);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyFloat(item, floatProperty, floatValue));
                        break;
                    case "iid":
                        if (!Enum.IsDefined(typeof(PropertyInstanceId), (ushort)request.Key) || !TryParseUIntValue(request.Value, out var iidValue))
                            throw new ArgumentException("Invalid IID property or value.");
                        item.SetProperty((PropertyInstanceId)(ushort)request.Key, iidValue);
                        break;
                    case "int":
                        if (!Enum.IsDefined(typeof(PropertyInt), (ushort)request.Key) || !int.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                            throw new ArgumentException("Invalid int property or value.");
                        var intProperty = (PropertyInt)(ushort)request.Key;
                        item.SetProperty(intProperty, intValue);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(item, intProperty, intValue));
                        break;
                    case "int64":
                        if (!Enum.IsDefined(typeof(PropertyInt64), (ushort)request.Key) || !long.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64Value))
                            throw new ArgumentException("Invalid int64 property or value.");
                        var int64Property = (PropertyInt64)(ushort)request.Key;
                        item.SetProperty(int64Property, int64Value);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt64(item, int64Property, int64Value));
                        break;
                    case "string":
                        if (!Enum.IsDefined(typeof(PropertyString), (ushort)request.Key))
                            throw new ArgumentException("Invalid string property.");
                        var stringProperty = (PropertyString)(ushort)request.Key;
                        item.SetProperty(stringProperty, request.Value);
                        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyString(item, stringProperty, request.Value));
                        break;
                    default:
                        throw new ArgumentException("Supported families: bool, did, float, iid, int, int64, string.");
                }

                item.SaveBiotaToDatabase();
                log.Warn($"[DerpACE AdminMap] {player.Name}'s item {item.Guid} property {request.Family}:{request.Key} was changed by an admin map session.");
                return new { ok = true, inventory = BuildInventorySnapshot(player.Guid.Full) };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.Message };
            }
        }

        private static bool TryParseUIntValue(string value, out uint result)
        {
            var text = value?.Trim() ?? "";
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        private static bool ApplyIntEdit(Player player, WorldObject item, int? requestValue, PropertyInt property, Action<int> setter)
        {
            if (!requestValue.HasValue)
                return false;

            setter(requestValue.Value);
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(item, property, item.GetProperty(property) ?? 0));
            return true;
        }

        private static bool ApplyFloatEdit(Player player, WorldObject item, double? requestValue, PropertyFloat property, Action<double> setter)
        {
            if (!requestValue.HasValue)
                return false;

            setter(requestValue.Value);
            player.Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyFloat(item, property, item.GetProperty(property) ?? 0.0));
            return true;
        }

        private static object HandleInventoryItemDelete(AdminInventoryItemDeleteRequest request)
        {
            if (!TryGetEditableItem(request?.PlayerGuid, request?.ItemGuid, out var player, out var item, out _, out _, out var wasEquipped, out var error))
                return new { ok = false, error };

            var itemName = item.Name;
            var itemGuid = item.Guid;
            var removed = wasEquipped
                ? player.TryDequipObjectWithNetworking(item.Guid, out item, Player.DequipObjectAction.ConsumeItem)
                : player.TryRemoveFromInventoryWithNetworking(item.Guid, out item, Player.RemoveFromInventoryAction.ConsumeItem);

            if (!removed)
                return new { ok = false, error = "Could not remove item from player." };

            if (!wasEquipped)
                item.Destroy();

            log.Warn($"[DerpACE AdminMap] Deleted item {itemName} ({itemGuid}) from {player.Name} ({player.Guid})");

            return new { ok = true, inventory = BuildInventorySnapshot(player.Guid.Full) };
        }

        private static object HandlePlayerAction(AdminPlayerActionRequest request)
        {
            if (!TryGetPlayerGuid(request?.PlayerGuid, out var playerGuid))
                return new { ok = false, error = "Missing or invalid player." };

            var player = PlayerManager.GetOnlinePlayer(playerGuid);
            if (player == null)
                return new { ok = false, error = "Player is not online." };

            var action = request.Action?.Trim().ToLowerInvariant();
            switch (action)
            {
                case "teleport":
                    if (!TryBuildTeleportPosition(request, player, out var position, out var error))
                        return new { ok = false, error };

                    WorldManager.ThreadSafeTeleport(player, position);
                    log.Warn($"[DerpACE AdminMap] Teleporting {player.Name} ({player.Guid}) to {position.ToLOCString()} from admin map.");
                    return new { ok = true, message = $"Teleporting {player.Name} to {position.ToLOCString()}." };

                case "boot":
                case "kick":
                    var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Admin map action" : request.Reason.Trim();
                    player.Session?.Terminate(SessionTerminationReason.AccountBooted, new GameMessageBootAccount($" - {reason}"), null, reason);
                    log.Warn($"[DerpACE AdminMap] Booted {player.Name} ({player.Guid}) from admin map. Reason: {reason}");
                    return new { ok = true, message = $"Booted {player.Name}." };

                default:
                    return new { ok = false, error = "Unsupported player action." };
            }
        }

        private static object HandleMapLoc(HttpListenerContext context)
        {
            if (!float.TryParse(context.Request.QueryString["x"], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(context.Request.QueryString["y"], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                context.Response.StatusCode = 400;
                return new { ok = false, error = "Map x and y are required." };
            }

            if (x < -102.0f || x > 102.0f || y < -102.0f || y > 102.0f)
            {
                context.Response.StatusCode = 400;
                return new { ok = false, error = "Map coordinates are outside Dereth bounds." };
            }

            try
            {
                var position = new Position(new Vector2(x, y));
                position.AdjustMapCoords();

                return new
                {
                    ok = true,
                    loc = position.ToLOCString(),
                    map = position.GetMapCoordStr(),
                    cell = $"0x{position.Cell:X8}",
                    x = position.PositionX,
                    y = position.PositionY,
                    z = position.PositionZ
                };
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Failed to convert map coords {x:0.###}, {y:0.###} to LOC: {ex}");
                context.Response.StatusCode = 500;
                return new { ok = false, error = "Could not convert that map point to a landloc." };
            }
        }

        private static bool TryBuildTeleportPosition(AdminPlayerActionRequest request, Player player, out Position position, out string error)
        {
            position = null;
            error = null;

            if (!string.IsNullOrWhiteSpace(request.Loc) && TryParseLoc(request.Loc, out position, out error))
                return true;

            if (!string.IsNullOrWhiteSpace(request.Cell))
            {
                if (!TryParseCell(request.Cell, out var cell))
                {
                    error = "Cell must be a hex value like 0x7F0401AD.";
                    return false;
                }

                if (!request.X.HasValue || !request.Y.HasValue || !request.Z.HasValue)
                {
                    error = "Cell teleport requires x, y, and z.";
                    return false;
                }

                var qw = request.Qw ?? player?.Location?.RotationW ?? 1.0f;
                var qx = request.Qx ?? player?.Location?.RotationX ?? 0.0f;
                var qy = request.Qy ?? player?.Location?.RotationY ?? 0.0f;
                var qz = request.Qz ?? player?.Location?.RotationZ ?? 0.0f;
                position = new Position(cell, request.X.Value, request.Y.Value, request.Z.Value, qx, qy, qz, qw);
                return true;
            }

            error = "Provide a pasted LOC string or cell/x/y/z.";
            return false;
        }

        private static bool TryParseLoc(string loc, out Position position, out string error)
        {
            position = null;
            error = null;

            var tokens = loc
                .Replace("[", " ")
                .Replace("]", " ")
                .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length != 4 && tokens.Length != 8)
            {
                error = "LOC must look like: 0x7F0401AD [12.3 -28.4 0.0] qw qx qy qz.";
                return false;
            }

            if (!TryParseCell(tokens[0], out var cell))
            {
                error = "LOC cell must be a hex value like 0x7F0401AD.";
                return false;
            }

            var values = new float[7];
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 2 && tokens.Length == 4)
                {
                    values[3] = 1.0f;
                    values[4] = 0.0f;
                    values[5] = 0.0f;
                    values[6] = 0.0f;
                    break;
                }

                if (!float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    error = "LOC contains a non-numeric position or rotation value.";
                    return false;
                }
            }

            position = new Position(cell, values[0], values[1], values[2], values[4], values[5], values[6], values[3]);
            return true;
        }

        private static bool TryParseCell(string cellValue, out uint cell)
        {
            cell = 0;
            if (string.IsNullOrWhiteSpace(cellValue))
                return false;

            var value = cellValue.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);

            return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cell);
        }

        private static bool TryGetEditableItem(string playerGuidValue, string itemGuidValue, out Player player, out WorldObject item, out Container foundInContainer, out Container rootOwner, out bool wasEquipped, out string error)
        {
            player = null;
            item = null;
            foundInContainer = null;
            rootOwner = null;
            wasEquipped = false;
            error = null;

            if (!TryGetPlayerGuid(playerGuidValue, out var playerGuid))
            {
                error = "Missing or invalid player.";
                return false;
            }

            if (!TryGetPlayerGuid(itemGuidValue, out var itemGuid))
            {
                error = "Missing or invalid item.";
                return false;
            }

            player = PlayerManager.GetOnlinePlayer(playerGuid);
            if (player == null)
            {
                error = "Player is not online.";
                return false;
            }

            item = player.FindObject(new ObjectGuid(itemGuid), Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems, out foundInContainer, out rootOwner, out wasEquipped);
            if (item == null)
            {
                error = "Item is not in this player's inventory or equipment.";
                return false;
            }

            return true;
        }

        private static void ApplyInventoryDelta(Container rootOwner, Container foundInContainer, WorldObject item, int oldEncumbrance, int oldValue)
        {
            var encumbranceDelta = (item.EncumbranceVal ?? 0) - oldEncumbrance;
            var valueDelta = (item.Value ?? 0) - oldValue;

            if (foundInContainer != null)
            {
                foundInContainer.EncumbranceVal += encumbranceDelta;
                foundInContainer.Value += valueDelta;
                foundInContainer.SaveBiotaToDatabase();
            }

            if (rootOwner != null && rootOwner != foundInContainer)
            {
                rootOwner.EncumbranceVal += encumbranceDelta;
                rootOwner.Value += valueDelta;
                rootOwner.SaveBiotaToDatabase();
            }
        }

        private static AdminDungeonMap BuildDungeonMap(uint landblock)
        {
            try
            {
                var geometry = new LandblockGeometry(landblock);
                var cells = geometry.DungeonCells.Values.Where(c => c.HasWalkablePolys).ToList();

                if (cells.Count == 0)
                    return AdminDungeonMap.Fail("No dungeon geometry found for this landblock.");

                var exporter = new LandblockGeometryExporter(geometry, cells);
                exporter.LoadLandblockInfo();

                if (exporter.Vertices.Count == 0 || exporter.Polygons.Count == 0)
                    return AdminDungeonMap.Fail("Dungeon geometry produced no drawable polygons.");

                var points = exporter.Vertices;
                var minX = points.Min(v => v.X);
                var maxX = points.Max(v => v.X);
                var minY = points.Min(v => v.Z);
                var maxY = points.Max(v => v.Z);
                var minZ = points.Min(v => v.Y);
                var maxZ = points.Max(v => v.Y);
                var paddedMinX = minX - 8;
                var paddedMaxX = maxX + 8;
                var paddedMinY = minY - 8;
                var paddedMaxY = maxY + 8;
                var width = Math.Max(1.0f, paddedMaxX - paddedMinX);
                var height = Math.Max(1.0f, paddedMaxY - paddedMinY);

                var svg = new StringBuilder();
                svg.Append(CultureInfo.InvariantCulture,
                    $"<svg viewBox=\"{paddedMinX:0.###} {-paddedMaxY:0.###} {width:0.###} {height:0.###}\" preserveAspectRatio=\"none\" xmlns=\"http://www.w3.org/2000/svg\">");
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect x=\"{paddedMinX:0.###}\" y=\"{-paddedMaxY:0.###}\" width=\"{width:0.###}\" height=\"{height:0.###}\" fill=\"#0c1114\"/>");
                svg.Append("<g stroke=\"#6da59f\" stroke-width=\"0.35\" opacity=\"0.94\">");

                foreach (var poly in exporter.Polygons)
                {
                    if (poly.Count < 3)
                        continue;

                    var validVertices = poly
                        .Where(index => index > 0 && index <= exporter.Vertices.Count)
                        .Select(index => exporter.Vertices[index - 1])
                        .ToList();

                    if (validVertices.Count < 3)
                        continue;

                    var avgZ = validVertices.Average(v => v.Y);
                    svg.Append(CultureInfo.InvariantCulture, $"<polygon fill=\"{GetDepthFill(avgZ, minZ, maxZ)}\" points=\"");
                    foreach (var vertex in validVertices)
                    {
                        svg.Append(CultureInfo.InvariantCulture, $"{vertex.X:0.###},{-vertex.Z:0.###} ");
                    }
                    svg.Append("\"/>");
                }

                svg.Append("</g></svg>");

                return new AdminDungeonMap
                {
                    Generated = true,
                    Svg = svg.ToString(),
                    MinX = paddedMinX,
                    MaxX = paddedMaxX,
                    MinY = paddedMinY,
                    MaxY = paddedMaxY,
                    MinZ = minZ,
                    MaxZ = maxZ
                };
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Failed to build dungeon map for 0x{landblock & 0xFFFF0000:X8}: {ex}");
                return AdminDungeonMap.Fail("Dungeon map generation failed.");
            }
        }

        private static AdminDungeonPlayer BuildDungeonPlayer(Player player)
        {
            var loc = player.Location;

            return new AdminDungeonPlayer
            {
                Name = player.Name,
                Guid = $"0x{player.Guid.Full:X8}",
                Cell = $"0x{loc.Cell:X8}",
                Loc = loc.ToLOCString(),
                X = loc.PositionX,
                Y = loc.PositionY,
                Z = loc.PositionZ,
                Heading = GetHeadingDegrees(loc),
                Health = player.Health?.Current ?? 0,
                MaxHealth = player.Health?.MaxValue ?? 0,
                Stamina = player.Stamina?.Current ?? 0,
                MaxStamina = player.Stamina?.MaxValue ?? 0,
                Mana = player.Mana?.Current ?? 0,
                MaxMana = player.Mana?.MaxValue ?? 0
            };
        }

        private static AdminMapStats BuildMapStats(List<Player> visiblePlayers)
        {
            var online = visiblePlayers ?? new List<Player>();
            var uniqueIps = online
                .Select(p => p?.Session?.EndPointC2S?.Address?.ToString())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return new AdminMapStats
            {
                OnlineCount = online.Count,
                UniqueIpCount = uniqueIps,
                HardcoreOnlineCount = online.Count(p => p.GetProperty(PropertyBool.IsHardcore) == true && p.GetProperty(PropertyBool.IsIronman) != true),
                IronmanOnlineCount = online.Count(p => p.GetProperty(PropertyBool.IsIronman) == true),
                HardcoreLeader = ToLeaderboardEntry(LeaderboardCache.GetHardcore().FirstOrDefault()),
                IronmanLeader = ToLeaderboardEntry(LeaderboardCache.GetIronman().FirstOrDefault()),
                DeadliestNormal = ToLeaderboardEntry(LeaderboardCache.GetDeadliest(PlayerKillerTracker.Category.Normal).FirstOrDefault()),
                DeadliestHardcore = ToLeaderboardEntry(LeaderboardCache.GetDeadliest(PlayerKillerTracker.Category.Hardcore).FirstOrDefault()),
                DeadliestIronman = ToLeaderboardEntry(LeaderboardCache.GetDeadliest(PlayerKillerTracker.Category.Ironman).FirstOrDefault())
            };
        }

        private static AdminLeaderboardEntry ToLeaderboardEntry(PlayerLeaderboardEntry entry)
        {
            if (entry == null)
                return null;

            return new AdminLeaderboardEntry
            {
                Name = entry.Name,
                Level = entry.Level,
                Kills = entry.Kills,
                Lives = entry.Lives
            };
        }

        private static AdminLeaderboardEntry ToLeaderboardEntry(KillerLeaderboardEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                return null;

            return new AdminLeaderboardEntry
            {
                Name = entry.Name,
                Kills = entry.Kills
            };
        }

        private static string GetDepthFill(double z, double minZ, double maxZ)
        {
            if (Math.Abs(maxZ - minZ) < 0.001)
                return "#264348";

            var t = Math.Clamp((z - minZ) / (maxZ - minZ), 0.0, 1.0);

            if (t < 0.5)
                return LerpColor("#1d2f52", "#28544f", t * 2.0);

            return LerpColor("#28544f", "#766a3a", (t - 0.5) * 2.0);
        }

        private static string LerpColor(string from, string to, double amount)
        {
            amount = Math.Clamp(amount, 0.0, 1.0);
            var r1 = Convert.ToInt32(from.Substring(1, 2), 16);
            var g1 = Convert.ToInt32(from.Substring(3, 2), 16);
            var b1 = Convert.ToInt32(from.Substring(5, 2), 16);
            var r2 = Convert.ToInt32(to.Substring(1, 2), 16);
            var g2 = Convert.ToInt32(to.Substring(3, 2), 16);
            var b2 = Convert.ToInt32(to.Substring(5, 2), 16);
            var r = (int)Math.Round(r1 + (r2 - r1) * amount);
            var g = (int)Math.Round(g1 + (g2 - g1) * amount);
            var b = (int)Math.Round(b1 + (b2 - b1) * amount);

            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static List<AdminMapBlip> BuildNearbyMapBlips(List<Player> players, bool dungeon, uint landblock)
        {
            var blips = new List<AdminMapBlip>();
            var seen = new HashSet<uint>();
            var radiusSq = CreatureBlipRadius * CreatureBlipRadius;
            var normalizedLandblock = landblock & 0xFFFF0000;

            foreach (var player in players)
            {
                if (player?.Location == null || player.CurrentLandblock == null)
                    continue;

                foreach (var worldObject in player.CurrentLandblock.GetAllWorldObjectsForDiagnostics())
                {
                    if (worldObject == null || worldObject == player || worldObject is Player || worldObject.Location == null)
                        continue;

                    if (!TryGetMapBlipKind(worldObject, out var kind, out var radarColor))
                        continue;

                    if (worldObject is Creature creature && (!creature.IsAlive || creature.Teleporting))
                        continue;

                    if (dungeon)
                    {
                        if ((worldObject.Location.Cell & 0xFFFF0000) != normalizedLandblock)
                            continue;
                    }
                    else if (worldObject.Location.Indoors)
                        continue;

                    if (player.Location.SquaredDistanceTo(worldObject.Location) > radiusSq)
                        continue;

                    if (!seen.Add(worldObject.Guid.Full))
                        continue;

                    blips.Add(BuildMapBlip(worldObject, kind, radarColor));

                    if (blips.Count >= MaxCreatureBlips)
                        return blips;
                }
            }

            return blips;
        }

        private static bool TryGetMapBlipKind(WorldObject worldObject, out string kind, out RadarColor radarColor)
        {
            kind = null;
            radarColor = RadarColor.Default;

            switch (worldObject.WeenieType)
            {
                case WeenieType.Portal:
                case WeenieType.HousePortal:
                    kind = "portal";
                    radarColor = RadarColor.Portal;
                    return true;

                case WeenieType.LifeStone:
                    kind = "lifestone";
                    radarColor = RadarColor.LifeStone;
                    return true;

                case WeenieType.LightSource:
                    kind = "light";
                    radarColor = RadarColor.Gold;
                    return true;

                case WeenieType.Door:
                    kind = "door";
                    radarColor = RadarColor.Default;
                    return true;
            }

            if (worldObject is Vendor)
            {
                kind = "vendor";
                radarColor = RadarColor.Vendor;
                return true;
            }

            if (worldObject is Creature creature)
            {
                kind = creature.IsMonster ? "creature" : "npc";
                radarColor = creature.IsMonster ? RadarColor.Creature : RadarColor.NPC;
                return true;
            }

            return false;
        }

        private static AdminMapBlip BuildMapBlip(WorldObject worldObject, string kind, RadarColor radarColor)
        {
            var loc = worldObject.Location;
            var mapCoords = loc.GetMapCoords();

            return new AdminMapBlip
            {
                Name = worldObject.Name,
                Guid = $"0x{worldObject.Guid.Full:X8}",
                Cell = $"0x{loc.Cell:X8}",
                Landblock = loc.LandblockId.ToString(),
                Loc = loc.ToLOCString(),
                Kind = kind,
                RadarColor = radarColor.ToString(),
                IsMonster = worldObject is Creature creature && creature.IsMonster,
                MapX = mapCoords?.X,
                MapY = mapCoords?.Y,
                X = loc.PositionX,
                Y = loc.PositionY,
                Z = loc.PositionZ
            };
        }

        private static AdminWatchBlip BuildWatchBlip(WorldObject worldObject, Player target, string kind, string radarColor)
        {
            var loc = worldObject.Location;
            var targetLoc = target.Location;
            var dx = (loc.LandblockId.LandblockX - targetLoc.LandblockId.LandblockX) * Position.BlockLength + loc.PositionX - targetLoc.PositionX;
            var dy = (loc.LandblockId.LandblockY - targetLoc.LandblockId.LandblockY) * Position.BlockLength + loc.PositionY - targetLoc.PositionY;
            var dz = loc.PositionZ - targetLoc.PositionZ;

            return new AdminWatchBlip
            {
                Name = worldObject.Name,
                Guid = $"0x{worldObject.Guid.Full:X8}",
                Cell = $"0x{loc.Cell:X8}",
                Loc = loc.ToLOCString(),
                Kind = kind,
                RadarColor = radarColor,
                IsMonster = worldObject is Creature creature && creature.IsMonster,
                Dx = dx,
                Dy = dy,
                Dz = dz,
                Distance = (float)Math.Sqrt(dx * dx + dy * dy),
                Heading = GetHeadingDegrees(loc),
                Z = loc.PositionZ
            };
        }

        private static bool TryGetLandblock(string value, out uint landblock)
        {
            landblock = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);

            if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
                && !uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return false;

            landblock = parsed & 0xFFFF0000;
            return landblock != 0;
        }

        private static bool TryGetPlayerGuid(string value, out uint guid)
        {
            guid = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out guid);

            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out guid);
        }

        private static bool TryParseDataId(string value, out uint did)
        {
            did = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out did);

            // Exported DAT filenames and some API clients use bare 8-digit hexadecimal DIDs.
            if (value.Length == 8 && value.Any(c => char.IsLetter(c)))
                return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out did);

            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out did);
        }

        private static bool TryWriteMapImage(HttpListenerContext context)
        {
            var path = ResolveMapImagePath();
            if (path == null || !File.Exists(path))
                return false;

            var bytes = File.ReadAllBytes(path);
            WriteBytes(context, bytes, GetImageContentType(path));
            return true;
        }

        private static bool TryWriteIcon(HttpListenerContext context, uint did)
        {
            if (did == 0)
                return false;

            try
            {
                var configuredRoot = DerpAceConfigManager.Config.AdminMapIconPath?.Trim();
                var roots = new[]
                {
                    configuredRoot,
                    Path.Combine(AppContext.BaseDirectory, "Data", "AdminMap", "icons"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", "AdminMap", "icons"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Source", "ACE.Server", "Data", "AdminMap", "icons")
                };
                var path = roots
                    .Where(root => !string.IsNullOrWhiteSpace(root))
                    .Select(root => Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root))
                    .Select(root => Path.Combine(Path.GetFullPath(root), $"{did:X8}.png"))
                    .FirstOrDefault(File.Exists);
                if (path == null)
                    return false;

                var info = new FileInfo(path);
                var cacheKey = $"{did:X8}:{info.FullName.ToUpperInvariant()}";
                var cached = IconPngCache.GetOrAdd(cacheKey, _ => LoadIconCacheEntry(info));
                if (cached.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks || cached.Length != info.Length)
                {
                    cached = LoadIconCacheEntry(info);
                    IconPngCache[cacheKey] = cached;
                }

                context.Response.Headers["Cache-Control"] = "public, max-age=86400, immutable";
                context.Response.Headers["ETag"] = $"\"{did:X8}-{cached.LastWriteUtcTicks:X}-{cached.Length:X}\"";
                WriteBytes(context, cached.Bytes, "image/png");
                return true;
            }
            catch (Exception ex)
            {
                log.Warn($"[DerpACE AdminMap] Failed to load static icon 0x{did:X8}: {ex.Message}");
                return false;
            }
        }
        private static AdminIconCacheEntry LoadIconCacheEntry(FileInfo info)
        {
            return new AdminIconCacheEntry
            {
                Bytes = File.ReadAllBytes(info.FullName),
                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                Length = info.Length
            };
        }
        private static Texture TryReadTexture(DatDatabase database, uint did)
        {
            if (database == null)
                return null;

            if (!database.AllFiles.TryGetValue(did, out var file) || file.GetFileType(DatDatabaseType.Portal) != DatFileType.Texture)
                return null;

            var texture = database.ReadFromDat<Texture>(did);
            return texture != null && texture.Length > 0 ? texture : null;
        }

        private static bool HasMapImage()
        {
            var path = ResolveMapImagePath();
            return path != null && File.Exists(path);
        }

        private static string ResolveMapImagePath()
        {
            var path = DerpAceConfigManager.Config.AdminMapImagePath;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        }

        private static string GetImageContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                    return "image/png";
                case ".webp":
                    return "image/webp";
                case ".gif":
                    return "image/gif";
                default:
                    return "image/jpeg";
            }
        }

        private static float ClampPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;

            return Math.Clamp(value, 0, 100);
        }

        private static AdminMapPlayer BuildPlayer(Player player)
        {
            var loc = player.Location;
            var mapCoords = loc.GetMapCoords();

            return new AdminMapPlayer
            {
                Name = player.Name,
                Guid = $"0x{player.Guid.Full:X8}",
                Landblock = loc.LandblockId.ToString(),
                Loc = loc.ToLOCString(),
                IsIndoors = loc.Indoors,
                MapX = mapCoords?.X,
                MapY = mapCoords?.Y,
                WorldX = loc.LandblockId.LandblockX * Position.BlockLength + loc.PositionX,
                WorldY = loc.LandblockId.LandblockY * Position.BlockLength + loc.PositionY,
                Z = loc.PositionZ,
                Heading = GetHeadingDegrees(loc),
                Health = player.Health?.Current ?? 0,
                MaxHealth = player.Health?.MaxValue ?? 0,
                Stamina = player.Stamina?.Current ?? 0,
                MaxStamina = player.Stamina?.MaxValue ?? 0,
                Mana = player.Mana?.Current ?? 0,
                MaxMana = player.Mana?.MaxValue ?? 0
            };
        }

        private static double GetHeadingDegrees(Position loc)
        {
            var dir = loc.GetCurrentDir();
            var radians = Math.Atan2(dir.X, dir.Y);
            var degrees = radians * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        private static void WriteJson(HttpListenerContext context, object payload)
        {
            WriteText(context, JsonSerializer.Serialize(payload, JsonOptions), "application/json; charset=utf-8");
        }

        private static T ReadJsonBody<T>(HttpListenerContext context) where T : class
        {
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                var body = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(body))
                    return null;

                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
        }

        private static void WriteText(HttpListenerContext context, string text, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            WriteBytes(context, bytes, contentType);
        }

        private static void WriteBytes(HttpListenerContext context, byte[] bytes, string contentType)
        {
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static void CloseQuietly(HttpListenerContext context)
        {
            try
            {
                context?.Response?.OutputStream?.Close();
            }
            catch
            {
            }
        }

        private static string BuildBossMechanicsHelpHtml()
        {
            var playScriptOptions = string.Join(string.Empty, Enum.GetNames(typeof(ACE.Entity.Enum.PlayScript))
                .Where(name => name != nameof(ACE.Entity.Enum.PlayScript.Invalid))
                .Select(name => $"<option value=\"{name}\"></option>"));

            return $$$$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>DerpACE Boss Operations</title>
<style>
:root{color-scheme:dark;--bg:#0b0f10;--surface:#121719;--surface2:#182023;--line:#354146;--text:#edf2f3;--muted:#99a7ac;--green:#59c58c;--blue:#62aee1;--amber:#e4b95f;--red:#e06e6e}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:13px/1.45 Segoe UI,Arial,sans-serif}button,input,select,textarea{font:inherit;color:inherit}button{cursor:pointer;background:#26343a;border:1px solid #45555b;border-radius:4px;padding:7px 10px}button:hover{border-color:var(--green)}button:disabled{opacity:.45;cursor:not-allowed}.primary{background:#215239}.danger{background:#512727}.quiet{background:transparent}.topbar{height:52px;display:flex;align-items:center;gap:12px;padding:0 16px;border-bottom:1px solid var(--line);background:#101517;position:sticky;top:0;z-index:4}.topbar h1{font-size:16px;margin:0}.topbar a{color:var(--blue);text-decoration:none}.topbar .status{margin-left:auto;color:var(--muted);max-width:55vw;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.layout{display:grid;grid-template-columns:260px minmax(440px,1fr) 340px;min-height:calc(100vh - 52px)}.rail,.ops{background:var(--surface);padding:12px;overflow:auto}.rail{border-right:1px solid var(--line)}.ops{border-left:1px solid var(--line)}.workspace{padding:16px;min-width:0}.toolbar{display:flex;gap:7px;align-items:center;flex-wrap:wrap;margin-bottom:12px}.toolbar .push{margin-left:auto}.section{border-top:1px solid var(--line);padding-top:14px;margin-top:14px}.section:first-child{border-top:0;margin-top:0;padding-top:0}h2{font-size:14px;margin:0 0 10px}h3{font-size:12px;margin:0 0 8px;color:var(--muted);text-transform:uppercase}.field{display:grid;gap:4px;margin-bottom:9px}.field>span{font-size:10px;color:var(--muted);text-transform:uppercase}.field input,.field select,.field textarea{width:100%;background:#090d0e;border:1px solid #3b484d;border-radius:4px;padding:8px}.field textarea{resize:vertical}.row{display:grid;grid-template-columns:1fr 1fr;gap:8px}.search{width:100%;background:#090d0e;border:1px solid #3b484d;border-radius:4px;padding:8px;margin-bottom:8px}.profileList{display:grid;gap:4px}.profileItem{width:100%;text-align:left;padding:8px;background:transparent;border-color:transparent}.profileItem:hover,.profileItem.active{background:#202a2e;border-color:#405056}.profileItem strong,.profileItem small{display:block}.profileItem small{color:var(--muted);margin-top:2px}.dot{display:inline-block;width:7px;height:7px;border-radius:50%;margin-right:6px;background:#68757a}.dot.live{background:var(--green)}.meta{display:flex;gap:12px;flex-wrap:wrap;color:var(--muted);margin:-4px 0 12px}.jsonEditor{width:100%;min-height:390px;background:#070a0b;border:1px solid #38464b;border-radius:4px;padding:11px;font:12px/1.5 Consolas,monospace;tab-size:2;resize:vertical}.ruleList{display:grid;gap:6px;margin-top:10px}.ruleCard{border:1px solid var(--line);border-radius:4px;background:var(--surface2)}.ruleCard summary{cursor:pointer;padding:9px;display:flex;gap:8px;align-items:center}.ruleCard summary strong{flex:1}.ruleBody{padding:0 9px 9px}.ruleBody textarea{width:100%;min-height:180px;background:#090d0e;border:1px solid #3b484d;color:var(--text);font:12px/1.45 Consolas,monospace;padding:8px}.ruleActions{display:flex;gap:6px;margin-top:7px}.quick{padding:10px;background:var(--surface2);border:1px solid var(--line);border-radius:4px}.hint,.empty{color:var(--muted)}.warning{color:var(--amber)}.error{color:var(--red)}.spawnBox{border:1px solid var(--line);padding:10px;border-radius:4px;background:#101618}.activeList{display:grid;gap:6px}.bossInstance{border-top:1px solid var(--line);padding-top:8px}.bossInstance:first-child{border-top:0}.bossInstance strong,.bossInstance small{display:block}.bossInstance small{color:var(--muted)}.bossInstance .health{height:5px;background:#293237;margin:6px 0}.bossInstance .health i{display:block;height:100%;background:var(--red)}.badge{font-size:10px;border:1px solid #48585e;padding:2px 5px;border-radius:3px;color:var(--muted)}dialog{background:var(--surface);color:var(--text);border:1px solid var(--line);border-radius:6px;max-width:520px;width:calc(100% - 30px)}dialog::backdrop{background:rgba(0,0,0,.65)}@media(max-width:1050px){.layout{grid-template-columns:220px 1fr}.ops{grid-column:1/-1;border-left:0;border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 1fr;gap:16px}}@media(max-width:720px){.layout{display:block}.rail,.ops{border:0;border-bottom:1px solid var(--line)}.ops{display:block}.row{grid-template-columns:1fr}.status{display:none}}
</style>
</head>
<body>
<header class="topbar"><h1>Boss Operations</h1><a id="mapLink" href="/">Admin Map</a><a id="spellLink" href="/spell-workshop">Spell Workshop</a><span id="globalStatus" class="status">Loading profiles...</span></header>
<div class="layout">
<aside class="rail">
  <div class="toolbar"><button id="newProfile">New Profile</button><button id="refreshProfiles" title="Refresh profiles">Refresh</button></div>
  <input id="profileSearch" class="search" placeholder="Search profile, boss, WCID">
  <div id="profileList" class="profileList"></div>
</aside>
<main class="workspace">
  <div class="toolbar">
    <button id="validateDraft">Validate</button><button id="saveDraft" class="primary">Save Draft</button><button id="publishDraft">Save + Publish</button><button id="rollbackProfile">Rollback</button><button id="restorePublished">Restore Published</button>
    <button id="toggleProfile" class="push">Enable / Disable</button>
  </div>
  <section class="section">
    <div class="row"><label class="field"><span>Profile name</span><input id="profileName" placeholder="hollow_king"></label><label class="field"><span>Boss WCID</span><input id="bossWcid" type="number" min="1" placeholder="42047186"></label></div>
    <div id="profileMeta" class="meta"><span>No profile selected.</span></div>
  </section>
  <section class="section"><div class="toolbar"><h2>Profile JSON</h2><button id="formatJson" class="push">Format</button><button id="copyJson">Copy</button></div><textarea id="jsonEditor" class="jsonEditor" spellcheck="false"></textarea><p class="hint">This is the authoritative draft. All supported triggers, action arrays, phases, wildcards, mutators, and advanced fields remain editable here.</p></section>
  <section class="section"><div class="toolbar"><h2>Rules</h2><span id="ruleCount" class="badge">0 rules</span></div><div id="ruleList" class="ruleList"></div></section>
  <section class="section"><h2>Quick Add Rule</h2><div class="quick">
    <div class="row"><label class="field"><span>Trigger</span><select id="quickTrigger"><option value="health_below">Health below</option><option value="combat_start">Combat start</option><option value="timer">Timer</option><option value="spell_resisted">Spell resisted</option><option value="boss_evades">Boss evades</option><option value="critical_hit">Boss receives critical</option><option value="damage_type">Incoming damage type</option><option value="large_hit">Large hit</option><option value="death">Death</option></select></label><label class="field"><span>Action</span><select id="quickAction"><option value="taunt">Taunt</option><option value="say">Say</option><option value="effect">PlayScript effect</option><option value="maintain_minions">Maintain minions</option><option value="mirror_minions">Mirror minions</option><option value="push">Push</option><option value="pull">Pull</option><option value="blink">Blink</option><option value="scatter">Scatter</option><option value="knock_up">Knock up</option><option value="frost_rain">Frost rain</option><option value="apply_spell">Apply spell</option><option value="set_phase">Set phase</option></select></label></div>
    <div class="row"><label class="field"><span>Threshold / interval / large-hit %</span><input id="quickAmount" type="number" value="75" min="1" max="3600"></label><label class="field"><span>Chance %</span><input id="quickChance" type="number" value="100" min="1" max="100"></label></div>
    <div class="row"><label class="field"><span>Minimum players</span><input id="quickPlayers" type="number" value="1" min="1" max="40"></label><label class="field"><span>Required phase</span><input id="quickRequiredPhase" placeholder="optional"></label></div>
    <label class="field"><span>Text / PlayScript / phase / spell ID / minion shell WCID</span><input id="quickValue" list="playScripts" placeholder="Action value"><datalist id="playScripts">{{{{playScriptOptions}}}}</datalist></label>
    <div class="row"><label class="field"><span>Target</span><select id="quickTarget"><option value="trigger">Triggering player</option><option value="nearest">Nearest</option><option value="farthest">Farthest</option><option value="random">Random</option><option value="all">All nearby</option></select></label><label class="field"><span>Distance / minion count</span><input id="quickActionAmount" type="number" value="10" min="1" max="40"></label></div>
    <label class="field"><span>Damage type (damage_type trigger)</span><select id="quickDamageType"><option>Slash</option><option>Pierce</option><option>Bludgeon</option><option>Fire</option><option>Cold</option><option>Acid</option><option>Electric</option><option>Nether</option></select></label>
    <button id="addRule" class="primary">Add Rule</button>
  </div></section>
</main>
<aside class="ops">
  <section class="section"><div class="toolbar"><h2>Spawn Boss</h2><span id="selectedSpawnProfile" class="badge">No profile</span></div><div class="spawnBox">
    <label class="field"><span>At online player</span><select id="spawnPlayer"><option value="">Select player...</option></select></label>
    <div class="row"><label class="field"><span>Count</span><input id="spawnCount" type="number" min="1" max="10" value="1"></label><label class="field"><span>Distance</span><input id="spawnDistance" type="number" min="2" max="30" value="5"></label></div>
    <button id="spawnAtPlayer" class="primary">Spawn At Player</button>
    <label class="field" style="margin-top:12px"><span>Or full LOC</span><textarea id="spawnLoc" rows="3" placeholder="0x7F0401AD [12.3 -28.4 0.0] 1 0 0 0"></textarea></label><button id="spawnAtLoc">Spawn At LOC</button>
  </div></section>
  <section class="section"><div class="toolbar"><h2>Active Bosses</h2><button id="refreshActive" class="push">Refresh</button></div><div id="activeBosses" class="activeList"><span class="empty">Loading...</span></div></section>
  <section class="section"><h2>Workflow</h2><p class="hint">Create or load a profile, edit and validate its draft, publish it, then spawn it. Publishing invalidates the runtime cache immediately. Existing encounters see enabled profile changes on their next mechanic check.</p><p class="warning">New WCIDs still need their weenie SQL imported and world data reloaded before a profile can be created or spawned.</p></section>
</aside>
</div>
<script>
const $=id=>document.getElementById(id);const params=new URLSearchParams(location.search);const session=params.get('session')||'',mapToken=params.get('token')||'';let profiles=[],current=null,dirty=false;
if(session){$('mapLink').href='/?session='+encodeURIComponent(session);$('spellLink').href='/spell-workshop?session='+encodeURIComponent(session)}
function headers(json=false){const h={};if(session)h['X-DerpACE-Map-Session']=session;if(mapToken)h['X-DerpACE-Map-Token']=mapToken;if(json)h['Content-Type']='application/json';return h}
async function api(url,options={}){options.headers={...headers(!!options.body),...(options.headers||{})};options.cache='no-store';const res=await fetch(url,options);let data;try{data=await res.json()}catch{data={ok:false,error:await res.text()}}if(!res.ok||data.ok===false)throw Object.assign(new Error(data.error||data.message||res.statusText),{data});return data}
function status(message,type=''){const el=$('globalStatus');el.textContent=message;el.className='status '+type}
function safe(value){return String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function parseDraft(){try{return JSON.parse($('jsonEditor').value)}catch(e){throw new Error('JSON: '+e.message)}}
function template(wcid=0){return{schemaVersion:1,weenieClassId:+wcid||0,mutators:[],rules:[]}}
function setEditor(doc){$('jsonEditor').value=JSON.stringify(doc,null,2);dirty=false;renderRules()}
async function loadProfiles(selectName=current?.profile){try{const data=await api('/api/boss/profiles');profiles=data.profiles||[];renderProfiles();if(selectName&&profiles.some(p=>p.profile===selectName))await loadProfile(selectName);status(`${profiles.length} boss profile${profiles.length===1?'':'s'} loaded.`)}catch(e){status(e.message,'error')}}
function renderProfiles(){const term=$('profileSearch').value.trim().toLowerCase();$('profileList').innerHTML=profiles.filter(p=>!term||`${p.profile} ${p.bossName} ${p.weenieClassId}`.toLowerCase().includes(term)).map(p=>`<button class="profileItem ${current?.profile===p.profile?'active':''}" data-profile="${safe(p.profile)}"><strong><i class="dot ${p.enabled&&!p.isTemplate?'live':''}"></i>${safe(p.profile)} ${p.isTemplate?'<span class="badge">Template</span>':''}</strong><small>${safe(p.bossName)} &middot; ${p.weenieClassId} &middot; ${p.isTemplate?safe(p.templateError||'data file'):('draft r'+p.draftRevision)}</small></button>`).join('')||'<span class="empty">No matching profiles.</span>';$('profileList').querySelectorAll('button').forEach(b=>b.onclick=()=>loadProfile(b.dataset.profile))}
async function loadProfile(name){if(dirty&&!confirm('Discard unsaved draft changes?'))return;try{const data=await api('/api/boss/profile?profile='+encodeURIComponent(name));current=data;$('profileName').value=data.profile;$('profileName').readOnly=true;$('bossWcid').value=data.weenieClassId;$('bossWcid').readOnly=true;$('profileMeta').innerHTML=data.isTemplate?`<span>${safe(data.bossName)}</span><span class="badge">Data template</span><span>${safe(data.sourceFile)}</span>${data.hasWcidConflict?'<span class="error">WCID already belongs to another database profile</span>':'<span>Save Draft to import</span>'}`:`<span>${safe(data.bossName)}</span><span>Draft r${data.draftRevision}</span><span>Published r${data.publishedRevision}</span><span>${data.enabled?'Enabled':'Disabled'}</span><span>Edited by ${safe(data.modifiedBy||'unknown')}</span>`;$('selectedSpawnProfile').textContent=data.isTemplate?'Template not imported':data.profile;setEditor(JSON.parse(data.draftJson||'{}'));renderProfiles();setControls();status(data.isTemplate?`Loaded template ${data.profile}. Save Draft to import it.`:`Loaded ${data.profile}.`)}catch(e){status(e.message,'error')}}
function newProfile(){if(dirty&&!confirm('Discard unsaved draft changes?'))return;current=null;$('profileName').value='';$('profileName').readOnly=false;$('bossWcid').value='';$('bossWcid').readOnly=false;$('profileMeta').innerHTML='<span>New profile for an existing creature WCID.</span>';$('selectedSpawnProfile').textContent='No profile';setEditor(template());renderProfiles();setControls();$('profileName').focus()}
function setControls(){const has=!!current,isTemplate=!!current?.isTemplate;$('validateDraft').disabled=!has;$('saveDraft').disabled=!has;['publishDraft','rollbackProfile','restorePublished','toggleProfile','spawnAtPlayer','spawnAtLoc'].forEach(id=>$(id).disabled=!has||isTemplate);$('saveDraft').textContent=isTemplate?'Import Template':'Save Draft';$('toggleProfile').textContent=has&&!isTemplate?(current.enabled?'Disable':'Enable'):'Enable / Disable'}
function renderRules(){let doc;try{doc=parseDraft();$('ruleCount').textContent=`${(doc.rules||[]).length} rules`;$('ruleList').innerHTML=(doc.rules||[]).map((r,i)=>`<details class="ruleCard"><summary><strong>${safe(r.id||'unnamed')}</strong><span class="badge">${safe(r.trigger||'unknown')}</span><span>${(r.actions||[]).length} action${(r.actions||[]).length===1?'':'s'}</span></summary><div class="ruleBody"><textarea data-rule-json="${i}" spellcheck="false">${safe(JSON.stringify(r,null,2))}</textarea><div class="ruleActions"><button data-apply="${i}">Apply Rule JSON</button><button data-up="${i}">Move Up</button><button data-copy-rule="${i}">Duplicate</button><button data-delete="${i}" class="danger">Remove</button></div></div></details>`).join('')||'<span class="empty">No rules yet.</span>';bindRuleButtons()}catch(e){$('ruleCount').textContent='Invalid JSON';$('ruleList').innerHTML=`<span class="error">${safe(e.message)}</span>`}}
function mutateRules(fn){try{const doc=parseDraft();doc.rules=doc.rules||[];fn(doc.rules);setEditor(doc);dirty=true}catch(e){status(e.message,'error')}}
function bindRuleButtons(){$('ruleList').querySelectorAll('[data-apply]').forEach(b=>b.onclick=()=>mutateRules(r=>r[+b.dataset.apply]=JSON.parse(document.querySelector(`[data-rule-json="${b.dataset.apply}"]`).value)));$('ruleList').querySelectorAll('[data-delete]').forEach(b=>b.onclick=()=>mutateRules(r=>r.splice(+b.dataset.delete,1)));$('ruleList').querySelectorAll('[data-up]').forEach(b=>b.onclick=()=>mutateRules(r=>{const i=+b.dataset.up;if(i>0)[r[i-1],r[i]]=[r[i],r[i-1]]}));$('ruleList').querySelectorAll('[data-copy-rule]').forEach(b=>b.onclick=()=>mutateRules(r=>{const i=+b.dataset.copyRule;const copy=structuredClone(r[i]);copy.id=(copy.id||'rule')+'_copy';r.splice(i+1,0,copy)}))}
function quickRule(){mutateRules(rules=>{const trigger=$('quickTrigger').value,kind=$('quickAction').value,value=$('quickValue').value.trim(),amount=+$('quickAmount').value;let action={type:kind};if(kind==='taunt')Object.assign(action,{channel:'local',text:value||'%t, face me.'});else if(kind==='say')action.text=value||'The encounter changes.';else if(kind==='effect')action.effect=value||'EnchantUpRed';else if(kind==='maintain_minions')Object.assign(action,{weenieClassId:+value,count:Math.min(12,+$('quickActionAmount').value||1),health:100});else if(kind==='mirror_minions')Object.assign(action,{weenieClassId:+value,count:Math.min(12,+$('quickActionAmount').value||1),health:1000,source:'nearby',radius:60,durationSeconds:30,noXp:true,dropItems:false,noCorpse:true,translucency:0.35});else if(['push','pull','blink','scatter','knock_up'].includes(kind))Object.assign(action,{target:$('quickTarget').value,distance:+$('quickActionAmount').value||10});else if(kind==='frost_rain')Object.assign(action,{target:$('quickTarget').value,count:Math.min(8,+$('quickActionAmount').value||2),damageScale:0.25});else if(kind==='apply_spell')Object.assign(action,{target:$('quickTarget').value,spellId:+value});else if(kind==='set_phase')action.phase=value||'phase_2';const rule={id:`${trigger}_${Date.now().toString(36)}`,trigger,thresholdPercent:trigger==='health_below'?amount:0,intervalSeconds:trigger==='timer'?amount:0,chancePercent:+$('quickChance').value,minPlayers:+$('quickPlayers').value,damageType:trigger==='damage_type'?$('quickDamageType').value:null,damagePercent:trigger==='large_hit'?amount:0,once:trigger==='timer'?false:true,phase:$('quickRequiredPhase').value.trim()||null,actions:[action]};rules.push(rule)})}
async function profileAction(action,extra={}){if(!current)throw new Error('Select a profile first.');return api('/api/boss/profile',{method:'POST',body:JSON.stringify({action,profile:current.profile,json:$('jsonEditor').value,...extra})})}
async function createProfile(){const name=$('profileName').value.trim(),wcid=+$('bossWcid').value;const doc=parseDraft();doc.weenieClassId=wcid;$('jsonEditor').value=JSON.stringify(doc,null,2);const data=await api('/api/boss/profile',{method:'POST',body:JSON.stringify({action:'create',profile:name,weenieClassId:wcid,json:$('jsonEditor').value})});dirty=false;await loadProfiles(data.profile);status(data.message)}
async function saveProfile(){if(!current||current.isTemplate)return createProfile();const data=await profileAction('save');dirty=false;await loadProfile(current.profile);status(data.message);return data}
async function validateProfile(){try{const data=current?await profileAction('validate'):await createProfile();status(data.message)}catch(e){status((e.data?.errors||[e.message]).join(' | '),'error')}}
async function publishProfile(){if(!current)return status('Create the profile first.','error');if(!confirm(`Publish ${current.profile}? New spawns will use this revision immediately.`))return;try{await saveProfile();const data=await profileAction('publish');await loadProfiles(current.profile);status(data.message)}catch(e){status((e.data?.errors||[e.message]).join(' | '),'error')}}
async function simpleAction(action,extra={},confirmText=''){if(confirmText&&!confirm(confirmText))return;try{const data=await profileAction(action,extra);await loadProfiles(current.profile);status(data.message)}catch(e){status((e.data?.errors||[e.message]).join(' | '),'error')}}
async function loadPlayers(){try{const data=await api('/api/players');$('spawnPlayer').innerHTML='<option value="">Select player...</option>'+(data.players||[]).sort((a,b)=>a.name.localeCompare(b.name)).map(p=>`<option value="${safe(p.guid)}">${safe(p.name)} &middot; ${safe(p.loc||p.landblock)}</option>`).join('')}catch(e){status(e.message,'error')}}
async function spawn(usePlayer){if(!current)return status('Select a published profile first.','error');const body={profile:current.profile,count:+$('spawnCount').value,distance:+$('spawnDistance').value};if(usePlayer)body.playerGuid=$('spawnPlayer').value;else body.loc=$('spawnLoc').value.trim();try{const data=await api('/api/boss/spawn',{method:'POST',body:JSON.stringify(body)});status(data.message);setTimeout(loadActive,800)}catch(e){status((e.data?.errors||[e.message]).join(' | '),'error')}}
async function loadActive(){try{const data=await api('/api/boss/active');$('activeBosses').innerHTML=(data.bosses||[]).map(b=>{const pct=b.maxHealth?Math.max(0,Math.min(100,b.health*100/b.maxHealth)):0;return `<div class="bossInstance"><strong>${safe(b.name)} <span class="badge">${safe(b.profile)}</span></strong><small>${safe(b.guid)} &middot; WCID ${b.weenieClassId}</small><div class="health"><i style="width:${pct}%"></i></div><small>${b.health.toLocaleString()} / ${b.maxHealth.toLocaleString()} &middot; ${safe(b.loc||b.landblock)}</small><button data-despawn="${safe(b.guid)}" class="danger" style="margin-top:6px">Despawn</button></div>`}).join('')||'<span class="empty">No loaded boss-profile creatures.</span>';$('activeBosses').querySelectorAll('[data-despawn]').forEach(b=>b.onclick=async()=>{if(!confirm(`Despawn ${b.dataset.despawn}?`))return;try{const data=await api('/api/boss/despawn',{method:'POST',body:JSON.stringify({guid:b.dataset.despawn})});status(data.message);setTimeout(loadActive,800)}catch(e){status(e.message,'error')}})}catch(e){$('activeBosses').innerHTML=`<span class="error">${safe(e.message)}</span>`}}
$('profileSearch').oninput=renderProfiles;$('newProfile').onclick=newProfile;$('refreshProfiles').onclick=()=>loadProfiles();$('jsonEditor').oninput=()=>{dirty=true;renderRules()};$('bossWcid').oninput=()=>{try{const d=parseDraft();d.weenieClassId=+$('bossWcid').value;$('jsonEditor').value=JSON.stringify(d,null,2);dirty=true;renderRules()}catch{}};$('formatJson').onclick=()=>{try{setEditor(parseDraft());dirty=true}catch(e){status(e.message,'error')}};$('copyJson').onclick=()=>navigator.clipboard.writeText($('jsonEditor').value);$('addRule').onclick=quickRule;$('saveDraft').onclick=()=>saveProfile().catch(e=>status((e.data?.errors||[e.message]).join(' | '),'error'));$('validateDraft').onclick=validateProfile;$('publishDraft').onclick=publishProfile;$('rollbackProfile').onclick=()=>simpleAction('rollback',{},`Roll ${current?.profile} back to its previous published revision?`);$('restorePublished').onclick=()=>simpleAction('restore-published',{},'Discard the current draft and restore the published revision?');$('toggleProfile').onclick=()=>simpleAction('set-enabled',{enabled:!current.enabled},`${current?.enabled?'Disable':'Enable'} ${current?.profile}?`);$('spawnAtPlayer').onclick=()=>spawn(true);$('spawnAtLoc').onclick=()=>spawn(false);$('refreshActive').onclick=loadActive;
newProfile();Promise.all([loadProfiles(),loadPlayers(),loadActive()]);setInterval(()=>{if(!document.hidden)loadActive()},30000);
</script>
</body>
</html>
""";
        }
        private static string BuildSpellWorkshopHtml()
        {
            return """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>DerpACE Spell Workshop</title>
<style>
:root{color-scheme:dark;--bg:#0b0f10;--panel:#121719;--line:#354146;--text:#edf2f3;--muted:#9ba8ad;--blue:#65b6e8;--green:#5bc78f;--red:#e47777}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:13px/1.45 Segoe UI,Arial,sans-serif}button,input,textarea{font:inherit;color:inherit}.top{height:52px;display:flex;align-items:center;gap:14px;padding:0 16px;border-bottom:1px solid var(--line);background:#101517}.top h1{font-size:16px;margin:0}.top a{color:var(--blue);text-decoration:none}.top span{margin-left:auto;color:var(--muted)}main{display:grid;grid-template-columns:260px minmax(420px,1fr) 390px;min-height:calc(100vh - 52px)}aside,.guide{padding:16px;background:var(--panel);overflow:auto}aside{border-right:1px solid var(--line)}.guide{border-left:1px solid var(--line)}.editor{padding:16px;min-width:0}.field{display:grid;gap:4px;margin-bottom:11px}.field span{font-size:10px;color:var(--muted);text-transform:uppercase}.field input,.field textarea{width:100%;background:#080c0d;border:1px solid #3a474c;border-radius:4px;padding:8px}.actions{display:flex;gap:7px;flex-wrap:wrap}button{cursor:pointer;border:1px solid #46565c;border-radius:4px;background:#27353a;padding:8px 11px}button:hover{border-color:var(--green)}button.primary{background:#20533a}.note,.status{color:var(--muted)}.status.ok{color:var(--green)}.status.error{color:var(--red)}#spellJson{width:100%;height:calc(100vh - 145px);min-height:520px;resize:vertical;background:#070a0b;border:1px solid #38464b;border-radius:4px;padding:12px;color:#e9eff0;font:12px/1.5 Consolas,monospace;tab-size:2}.editorHead{display:flex;align-items:center;gap:8px;margin-bottom:10px}.editorHead h2{font-size:14px;margin:0}.editorHead .actions{margin-left:auto}details{border-top:1px solid var(--line);padding:9px 0}details:first-of-type{border-top:0}summary{cursor:pointer;font-weight:650}.vars{display:grid;gap:8px;margin-top:9px}.var{display:grid;grid-template-columns:145px 1fr;gap:8px}.var code{color:#b8dcf2}.var span{color:var(--muted)}h2{font-size:14px;margin:0 0 10px}.warning{margin-top:14px;color:#e3bd69}@media(max-width:1050px){main{grid-template-columns:220px 1fr}.guide{grid-column:1/-1;border-left:0;border-top:1px solid var(--line)}}@media(max-width:700px){main{display:block}aside,.guide{border:0;border-bottom:1px solid var(--line)}#spellJson{height:65vh}.top span{display:none}}
</style></head><body>
<header class="top"><h1>Spell Workshop</h1><a id="mapLink" href="/">Admin Map</a><a id="bossLink" href="/boss-mechanics">Boss Operations</a><span>Admin only</span></header>
<main><aside><h2>Clone A Spell</h2><p class="note">Start from a working spell. The template supplies every field you do not override.</p>
<label class="field"><span>Template spell ID</span><input id="templateId" type="number" min="1" value="2659"></label>
<label class="field"><span>New custom ID</span><input id="targetId" type="number" min="65001" max="65535" value="65019"></label>
<button id="clone" class="primary">Load Template</button>
<hr style="border:0;border-top:1px solid var(--line);margin:18px 0">
<label class="field"><span>JSON filename</span><input id="fileName" value="WorkshopSpell.json"></label>
<div class="actions"><button id="format">Format JSON</button><button id="save" class="primary">Save + Reload</button></div>
<p id="status" class="status">No spell loaded.</p><p class="warning">Saving reloads all custom spell JSON immediately. Existing casts are unaffected; future casts use the new definition.</p></aside>
<section class="editor"><div class="editorHead"><h2>Authoritative JSON</h2><div class="actions"><button id="copy">Copy</button></div></div><textarea id="spellJson" spellcheck="false" placeholder="Load a template or paste CustomSpells JSON here."></textarea></section>
<section class="guide"><h2>Field Reference</h2><p class="note">Top-level fields are convenient overrides. <code>SpellBase</code> and <code>DbSpell</code> expose the complete underlying records.</p>
<details open><summary>Identity and targeting</summary><div class="vars">
<div class="var"><code>Template</code><span>Existing spell ID or enum name to clone. This determines default behavior.</span></div><div class="var"><code>Id</code><span>Runtime ID, 65001-65535. Must be unique.</span></div><div class="var"><code>Name</code><span>Name shown by examine, casting, and enchantment UI.</span></div><div class="var"><code>SpellWords</code><span>Displayed incantation text; it does not choose components.</span></div><div class="var"><code>Desc</code><span>Human-readable spell description.</span></div><div class="var"><code>Icon</code><span>Spell icon DID as decimal or 0x hexadecimal.</span></div><div class="var"><code>School</code><span>WarMagic, LifeMagic, CreatureEnchantment, ItemEnchantment, or VoidMagic.</span></div><div class="var"><code>Category</code><span>Client stacking category. Matching categories can replace one another.</span></div><div class="var"><code>NonComponentTargetType</code><span>Allowed target ItemType flags, by names joined with commas or a numeric mask.</span></div></div></details>
<details><summary>Cost, power, range, and duration</summary><div class="vars">
<div class="var"><code>BaseMana</code><span>Base mana before skill and component adjustments.</span></div><div class="var"><code>Power</code><span>Spell tier/power used by resistance and dispel logic.</span></div><div class="var"><code>BaseRangeConstant</code><span>Range floor in world units.</span></div><div class="var"><code>BaseRangeMod</code><span>Additional range scaling.</span></div><div class="var"><code>Duration</code><span>Enchantment lifetime in seconds. Zero is immediate.</span></div><div class="var"><code>Bitfield</code><span>SpellFlags names or numeric mask: FastCast, Resistable, Beneficial, and related behavior.</span></div><div class="var"><code>MetaSpellType</code><span>Core spell behavior family. Keep the template value unless server handling supports the replacement.</span></div></div></details>
<details><summary>Damage and enchantments</summary><div class="vars">
<div class="var"><code>EType</code><span>Element/damage flags used by projectile and resistance logic.</span></div><div class="var"><code>DamageType</code><span>DamageType name or mask applied on impact.</span></div><div class="var"><code>BaseIntensity</code><span>Minimum damage or effect magnitude.</span></div><div class="var"><code>Variance</code><span>Random magnitude added above BaseIntensity.</span></div><div class="var"><code>StatModType</code><span>EnchantmentTypeFlags describing additive, multiplicative, skill, attribute, or defense changes.</span></div><div class="var"><code>StatModKey</code><span>Numeric property/skill/attribute targeted by the enchantment.</span></div><div class="var"><code>StatModVal</code><span>Modifier value. Multipliers generally use fractions, such as 0.10 for ten percent.</span></div><div class="var"><code>DotDuration</code><span>Total damage-over-time duration in seconds.</span></div></div></details>
<details><summary>Projectile geometry</summary><div class="vars">
<div class="var"><code>NumProjectiles</code><span>Base projectile count.</span></div><div class="var"><code>NumProjectilesVariance</code><span>Random additional projectile count.</span></div><div class="var"><code>SpreadAngle</code><span>Horizontal spread in radians.</span></div><div class="var"><code>VerticalAngle</code><span>Vertical launch angle in radians.</span></div><div class="var"><code>DefaultLaunchAngle</code><span>Fallback trajectory angle in radians.</span></div><div class="var"><code>NonTracking</code><span>True keeps the initial trajectory; false allows homing behavior.</span></div><div class="var"><code>CreateOffset</code><span><code>{X,Y,Z}</code> spawn offset from the projectile origin.</span></div><div class="var"><code>Padding</code><span><code>{X,Y,Z}</code> collision/launch padding.</span></div><div class="var"><code>Dims</code><span><code>{X,Y,Z}</code> projectile dimensions.</span></div><div class="var"><code>Peturbation</code><span><code>{X,Y,Z}</code> randomized trajectory perturbation. The source field keeps ACE's spelling.</span></div></div></details>
<details><summary>Visuals, formula, and advanced records</summary><div class="vars">
<div class="var"><code>CasterEffect</code><span>PlayScript enum run on the caster.</span></div><div class="var"><code>TargetEffect</code><span>PlayScript enum run on the target or impact.</span></div><div class="var"><code>Formula</code><span>Array of component IDs controlling the cast formula and gestures.</span></div><div class="var"><code>Wcid</code><span>Object WCID used by spell types that create or summon an object.</span></div><div class="var"><code>SpellBase</code><span>Advanced object containing any writable DAT SpellBase property. Top-level values are applied first.</span></div><div class="var"><code>DbSpell</code><span>Advanced object containing any writable world database spell property, including projectile physics fields.</span></div></div></details>
</section></main>
<script>
const q=new URLSearchParams(location.search),session=q.get('session')||'',token=q.get('token')||'';const $=id=>document.getElementById(id);if(session){$('mapLink').href='/?session='+encodeURIComponent(session);$('bossLink').href='/boss-mechanics?session='+encodeURIComponent(session)}function headers(json=false){const h={};if(session)h['X-DerpACE-Map-Session']=session;if(token)h['X-DerpACE-Map-Token']=token;if(json)h['Content-Type']='application/json';return h}function status(text,type=''){const e=$('status');e.textContent=text;e.className='status '+type}async function api(url,opt={}){opt.headers={...headers(!!opt.body),...(opt.headers||{})};const r=await fetch(url,opt),d=await r.json();if(!r.ok||d.ok===false)throw new Error(d.error||'Request failed');return d}$('clone').onclick=async()=>{try{const d=await api(`/api/spells/draft?template=${encodeURIComponent($('templateId').value)}&id=${encodeURIComponent($('targetId').value)}`);$('spellJson').value=d.json;status('Template loaded. Review every changed field before saving.','ok')}catch(e){status(e.message,'error')}};$('format').onclick=()=>{try{$('spellJson').value=JSON.stringify(JSON.parse($('spellJson').value),null,2);status('JSON is valid.','ok')}catch(e){status(e.message,'error')}};$('copy').onclick=()=>navigator.clipboard.writeText($('spellJson').value);$('save').onclick=async()=>{try{JSON.parse($('spellJson').value);const d=await api('/api/spells/save',{method:'POST',body:JSON.stringify({fileName:$('fileName').value,json:$('spellJson').value})});status(d.message,'ok')}catch(e){status(e.message,'error')}};
</script></body></html>
""";
        }
        private static string BuildIndexHtml()
        {
            var refresh = Math.Max(1, DerpAceConfigManager.Config.AdminMapRefreshSeconds);

            return $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>DerpACE Admin Map</title>
<style>
:root {{ color-scheme: dark; font-family: Segoe UI, Arial, sans-serif; background: #101314; color: #e8ece8; }}
* {{ box-sizing: border-box; }}
body {{ margin: 0; display: grid; grid-template-columns: minmax(320px, 1fr) 360px; min-height: 100vh; background: #101314; }}
#map {{ position: relative; overflow: hidden; min-height: 100vh; background:
    linear-gradient(rgba(255,255,255,.055) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255,255,255,.055) 1px, transparent 1px),
    radial-gradient(circle at 50% 50%, #2d4734 0, #1c3429 38%, #243237 64%, #172025 100%);
  background-size: 7.142857% 7.142857%, 7.142857% 7.142857%, 100% 100%; }}
#map::before {{ content: ""Dereth""; position: absolute; inset: 0; display: grid; place-items: center; color: rgba(255,255,255,.08); font-size: clamp(54px, 11vw, 160px); letter-spacing: 0; pointer-events: none; }}
#map.hasImage::before, #map.dungeonMode::before, #map.hasLayer::before {{ content: """"; }}
.axis {{ position: absolute; color: rgba(255,255,255,.44); font-size: 12px; user-select: none; }}
.north {{ top: 12px; left: 50%; transform: translateX(-50%); }}
.south {{ bottom: 12px; left: 50%; transform: translateX(-50%); }}
.west {{ left: 12px; top: 50%; transform: translateY(-50%); }}
.east {{ right: 12px; top: 50%; transform: translateY(-50%); }}
.mapLayer {{ position: absolute; inset: 0; transform-origin: 0 0; }}
.worldMapImage {{ position: absolute; inset: 0; background-color: #8fa0a8; background-position: center; background-repeat: no-repeat; background-size: 100% 100%; }}
.pin {{ position: absolute; z-index: 3; width: 4px; height: 4px; margin: -2px 0 0 -2px; border: 0; border-radius: 50%; background: #f5f7f0; box-shadow: 0 0 0 1px rgba(255,255,255,.8), 0 0 5px rgba(130,220,255,.72); cursor: pointer; }}
.pin::after {{ content: attr(data-name); position: absolute; left: 7px; top: -5px; max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #fff; font-size: 11px; text-shadow: 0 1px 3px #000, 0 0 5px #000; }}
.pin.indoor {{ background: #ffffff; box-shadow: 0 0 0 1px rgba(255,255,255,.85), 0 0 6px rgba(117,167,255,.82); }}
.dungeonSvg {{ position: absolute; inset: 0; width: 100%; height: 100%; }}
.dungeonSvg svg {{ display: block; width: 100%; height: 100%; }}
.dungeonPin {{ position: absolute; z-index: 3; width: 15px; height: 15px; margin: -7px 0 0 -7px; border: 2px solid #ffffff; border-radius: 50%; background: #f5f7f0; box-shadow: 0 0 0 4px rgba(255,255,255,.2), 0 0 18px rgba(130,220,255,.82); }}
.dungeonPin::after {{ content: attr(data-name); position: absolute; left: 17px; top: -7px; max-width: 190px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 3px 6px; border-radius: 4px; background: rgba(8,12,14,.82); color: #fff; font-size: 12px; }}
.blip {{ position: absolute; z-index: 2; width: 8px; height: 8px; margin: -4px 0 0 -4px; border-radius: 50%; pointer-events: none; background: #c9d0d0; box-shadow: 0 0 0 2px rgba(201,208,208,.2), 0 0 10px rgba(201,208,208,.65); }}
.blip.creature {{ background: #d6a53b; box-shadow: 0 0 0 2px rgba(214,165,59,.24), 0 0 11px rgba(214,165,59,.78); }}
.blip.npc, .blip.vendor {{ background: #f0dc54; box-shadow: 0 0 0 2px rgba(240,220,84,.22), 0 0 10px rgba(240,220,84,.72); }}
.blip.portal {{ width: 12px; height: 12px; margin: -6px 0 0 -6px; background: transparent; border: 2px solid #a56cff; box-shadow: 0 0 0 2px rgba(165,108,255,.18), 0 0 14px rgba(165,108,255,.86); }}
.blip.lifestone {{ width: 11px; height: 11px; margin: -5px 0 0 -5px; background: #4f8cff; box-shadow: 0 0 0 3px rgba(79,140,255,.2), 0 0 14px rgba(79,140,255,.82); }}
.blip.door {{ width: 11px; height: 4px; margin: -2px 0 0 -5px; border-radius: 1px; background: #b98b58; box-shadow: 0 0 0 2px rgba(185,139,88,.18), 0 0 8px rgba(185,139,88,.62); }}
.blip.light {{ z-index: 1; width: 34px; height: 34px; margin: -17px 0 0 -17px; background: radial-gradient(circle, rgba(255,214,116,.42) 0, rgba(255,189,87,.18) 38%, rgba(255,189,87,0) 72%); box-shadow: none; }}
.blip.npc::after, .blip.vendor::after {{ content: attr(data-name); position: absolute; left: 10px; top: -5px; max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #fff7a6; font-size: 11px; text-shadow: 0 1px 3px #000, 0 0 5px #000; }}
.blip.portal::after, .blip.lifestone::after {{ content: attr(data-name); position: absolute; left: 13px; top: -5px; max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #dcc8ff; font-size: 11px; text-shadow: 0 1px 3px #000, 0 0 5px #000; }}
.zoomControls {{ position: absolute; z-index: 5; left: 12px; bottom: 12px; display: none; grid-template-columns: repeat(4, 36px); gap: 6px; }}
.hasLayer .zoomControls {{ display: grid; }}
.zoomControls button {{ width: 36px; height: 36px; padding: 0; border-radius: 4px; font-size: 18px; font-weight: 700; }}
.bottomDock {{ position: absolute; z-index: 4; left: 170px; right: 12px; bottom: 12px; display: grid; grid-template-columns: minmax(260px, .9fr) minmax(320px, 1.1fr); gap: 8px; pointer-events: auto; }}
.dockPanel {{ min-width: 0; border: 1px solid rgba(255,255,255,.14); border-radius: 4px; background: rgba(10,14,16,.82); box-shadow: 0 8px 24px rgba(0,0,0,.24); padding: 8px 10px; }}
.dockPanel.collapsed .dockBody {{ display: none; }}
.dockTitle {{ display: flex; align-items: center; justify-content: space-between; gap: 8px; color: #fff3bf; font-size: 12px; font-weight: 650; margin-bottom: 6px; }}
.dockToggle {{ width: 24px; height: 22px; padding: 0; font-size: 12px; line-height: 1; background: rgba(255,255,255,.08); }}
.statGrid {{ display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px; }}
.statBox {{ min-width: 0; }}
.statLabel {{ color: #9eaaa5; font-size: 10px; text-transform: uppercase; }}
.statValue {{ color: #f1f5f1; font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
.onlineChips {{ display: flex; flex-wrap: wrap; gap: 5px; max-height: 74px; overflow: hidden; }}
.onlineChip {{ display: inline-flex; align-items: center; gap: 5px; max-width: 150px; padding: 2px 6px 2px 3px; border-radius: 999px; background: rgba(255,255,255,.08); color: #e8ece8; font-size: 11px; }}
.onlineDot {{ width: 10px; height: 10px; border: 1px solid #fff; border-radius: 50%; background: #f5f7f0; box-shadow: 0 0 7px rgba(130,220,255,.7); }}
.onlineName {{ overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
.feedBlock {{ margin-top: 8px; padding-top: 7px; border-top: 1px solid rgba(255,255,255,.1); }}
.feedList {{ display: grid; gap: 4px; max-height: 88px; overflow: hidden; }}
.feedRow {{ display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 6px; align-items: baseline; min-width: 0; color: #d6ddd8; font-size: 11px; }}
.feedTime {{ color: #8d9b96; font-variant-numeric: tabular-nums; }}
.feedText {{ min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
.feedName {{ color: #fff3bf; }}
.rareTier {{ color: #9bd6ff; }}
.rareItem {{ color: #f0dc54; }}
aside {{ border-left: 1px solid rgba(255,255,255,.12); background: #15191b; padding: 14px; overflow: auto; }}
h1 {{ margin: 0 0 12px; font-size: 20px; font-weight: 650; }}
.controls {{ display: grid; grid-template-columns: 1fr auto; gap: 8px; margin-bottom: 12px; }}
input {{ width: 100%; min-width: 0; background: #0e1112; color: #e8ece8; border: 1px solid rgba(255,255,255,.18); border-radius: 4px; padding: 8px; }}
button {{ border: 1px solid rgba(255,255,255,.18); border-radius: 4px; background: #2f5d6a; color: #fff; padding: 8px 10px; cursor: pointer; }}
#status {{ color: #abb7b2; font-size: 13px; margin-bottom: 12px; min-height: 18px; }}
.loginPanel {{ display: grid; gap: 8px; margin-bottom: 12px; padding: 10px; border: 1px solid rgba(255,255,255,.12); border-radius: 4px; background: rgba(0,0,0,.18); }}
.loginPanel.hidden {{ display: none; }}
.sessionBar {{ display: none; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; align-items: center; margin-bottom: 12px; color: #c7d1cc; font-size: 13px; }}
.sessionBar.active {{ display: grid; }}
.authLocked #map, .authLocked .legend, .authLocked #players {{ opacity: .22; pointer-events: none; }}
.authLocked #bottomDock {{ display: none; }}
.legend {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px 10px; margin: 0 0 12px; padding: 10px; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; background: rgba(0,0,0,.14); }}
.legendItem {{ display: flex; align-items: center; gap: 7px; min-width: 0; color: #c4cfca; font-size: 12px; }}
.legendDot {{ flex: 0 0 auto; width: 10px; height: 10px; border-radius: 50%; background: #c9d0d0; }}
.legendDot.player {{ background: #f5f7f0; border: 1px solid #fff; box-shadow: 0 0 8px rgba(130,220,255,.75); }}
.legendDot.creature {{ background: #d6a53b; }}
.legendDot.npc {{ background: #f0dc54; }}
.legendDot.portal {{ background: transparent; border: 2px solid #a56cff; }}
.legendDot.light {{ width: 16px; height: 16px; background: radial-gradient(circle, rgba(255,214,116,.7) 0, rgba(255,189,87,.25) 46%, rgba(255,189,87,0) 74%); }}
.legendDot.door {{ width: 13px; height: 5px; border-radius: 1px; background: #b98b58; }}
.depthKey {{ grid-column: 1 / -1; display: grid; grid-template-columns: auto 1fr auto; gap: 7px; align-items: center; }}
.depthRamp {{ height: 8px; border-radius: 999px; background: linear-gradient(90deg, #1d2f52, #28544f, #766a3a); box-shadow: inset 0 0 0 1px rgba(255,255,255,.16); }}
.player {{ border-top: 1px solid rgba(255,255,255,.12); padding: 10px 0; cursor: pointer; }}
.player.selected {{ background: rgba(255,255,255,.07); margin: 0 -8px; padding: 10px 8px; }}
.player strong {{ display: block; color: #fff3bf; margin-bottom: 3px; overflow-wrap: anywhere; }}
.adminPanel {{ display: none; gap: 8px; margin-bottom: 12px; padding: 10px; border: 1px solid rgba(255,255,255,.12); border-radius: 4px; background: rgba(0,0,0,.18); }}
.adminPanel.open {{ display: grid; }}
.adminPanel h2 {{ margin: 0; color: #fff3bf; font-size: 14px; }}
.adminGrid {{ display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 6px; }}
.adminGrid.wide {{ grid-template-columns: 1fr; }}
.adminPanel label {{ display: grid; gap: 4px; color: #aeb9b4; font-size: 11px; text-transform: uppercase; }}
.adminPanel input {{ padding: 7px; }}
.watchPanel {{ display: none; gap: 8px; margin-bottom: 12px; padding: 10px; border: 1px solid rgba(255,255,255,.12); border-radius: 4px; background: rgba(0,0,0,.18); }}
.watchPanel.open {{ display: grid; }}
.watchHeader {{ display: flex; align-items: center; justify-content: space-between; gap: 8px; }}
.watchTitle {{ min-width: 0; color: #fff3bf; font-size: 14px; font-weight: 650; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
.watchView {{ position: relative; aspect-ratio: 1; overflow: hidden; border: 1px solid rgba(255,255,255,.13); border-radius: 4px; background: radial-gradient(circle at center, rgba(87,117,119,.26) 0 1px, transparent 2px), linear-gradient(135deg, #0b1012, #182022); }}
.watchView::before, .watchView::after {{ content: ""; position: absolute; inset: 12.5%; border: 1px solid rgba(255,255,255,.09); border-radius: 50%; pointer-events: none; }}
.watchView::after {{ inset: 25%; }}
.watchAxis {{ position: absolute; inset: 50% 0 auto 0; height: 1px; background: rgba(255,255,255,.08); pointer-events: none; }}
.watchAxis.vertical {{ inset: 0 auto 0 50%; width: 1px; height: auto; }}
.watchBlip {{ position: absolute; z-index: 2; min-width: 6px; min-height: 6px; transform: translate(-50%, -50%); border-radius: 50%; background: #c9d0d0; box-shadow: 0 0 0 1px rgba(0,0,0,.75), 0 0 8px rgba(201,208,208,.55); }}
.watchBlip.target {{ z-index: 4; width: 12px; height: 12px; background: #f5f7f0; border: 1px solid #fff; box-shadow: 0 0 0 2px rgba(255,255,255,.22), 0 0 14px rgba(130,220,255,.9); }}
.watchBlip.player {{ width: 7px; height: 7px; background: #f5f7f0; border: 1px solid #fff; }}
.watchBlip.creature {{ width: 7px; height: 7px; background: #d6a53b; }}
.watchBlip.npc, .watchBlip.vendor {{ width: 7px; height: 7px; background: #f0dc54; }}
.watchBlip.portal {{ width: 10px; height: 10px; background: transparent; border: 2px solid #a56cff; }}
.watchBlip.light {{ z-index: 1; width: 30px; height: 30px; background: radial-gradient(circle, rgba(255,214,116,.42) 0, rgba(255,189,87,.18) 38%, rgba(255,189,87,0) 72%); box-shadow: none; }}
.watchBlip.door {{ width: 12px; height: 4px; min-height: 4px; border-radius: 1px; background: #b98b58; }}
.watchHeading {{ position: absolute; left: 50%; top: 50%; z-index: 5; width: 0; height: 0; border-left: 5px solid transparent; border-right: 5px solid transparent; border-bottom: 18px solid rgba(155,214,255,.85); transform-origin: 50% 100%; pointer-events: none; }}
.watchName {{ position: absolute; left: 8px; top: -5px; max-width: 112px; color: #dfe8e3; font-size: 10px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; text-shadow: 0 1px 2px #000, 0 0 3px #000; pointer-events: none; }}
.watchMeta {{ display: grid; gap: 3px; color: #abb7b2; font-size: 11px; }}
.muted {{ color: #aab4b0; font-size: 12px; }}
.bars {{ display: grid; gap: 3px; margin-top: 7px; }}
.bar {{ height: 5px; background: rgba(255,255,255,.12); border-radius: 999px; overflow: hidden; }}
.bar span {{ display: block; height: 100%; }}
.health span {{ background: #e35748; }}
.stamina span {{ background: #ead45f; }}
.mana span {{ background: #6c92ff; }}
.inventoryPanel {{ position: fixed; z-index: 20; inset: 28px; display: none; grid-template-columns: minmax(360px, 1fr) 300px; min-height: 0; border: 1px solid rgba(255,255,255,.18); border-radius: 6px; background: #121719; box-shadow: 0 18px 60px rgba(0,0,0,.5); overflow: hidden; }}
.inventoryPanel.open {{ display: grid; }}
.inventoryListPane, .inventoryEditPane {{ min-width: 0; min-height: 0; padding: 12px; }}
.inventoryListPane {{ display: grid; grid-template-rows: auto auto 1fr; gap: 8px; }}
.inventoryEditPane {{ border-left: 1px solid rgba(255,255,255,.12); background: #171d20; overflow: auto; }}
.inventoryTop {{ display: flex; align-items: center; justify-content: space-between; gap: 10px; }}
.inventoryTop h2 {{ margin: 0; color: #fff3bf; font-size: 16px; }}
.inventoryActions {{ display: flex; gap: 6px; }}
.iconButton {{ width: 32px; height: 32px; padding: 0; display: grid; place-items: center; }}
.inventorySearch {{ display: grid; grid-template-columns: 1fr auto; gap: 8px; }}
.inventoryTable {{ min-height: 0; overflow: auto; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; padding: 8px; background: #080a0b; }}
.inventorySectionTitle {{ grid-column: 1 / -1; color: #fff3bf; font-size: 12px; margin: 7px 0 3px; }}
.inventoryGrid {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(38px, 38px)); gap: 4px; align-content: start; }}
.inventorySlot {{ position: relative; width: 38px; height: 38px; padding: 0; border: 1px solid rgba(255,255,255,.16); background: linear-gradient(135deg, rgba(255,255,255,.06), rgba(255,255,255,.015)); cursor: pointer; overflow: hidden; }}
.inventorySlot:hover, .inventorySlot.selected {{ outline: 2px solid #fff3bf; z-index: 1; }}
.inventoryFallback {{ position: absolute; inset: 0; z-index: 0; display: grid; place-items: center; color: rgba(226,238,232,.46); font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: 0; overflow: hidden; }}
.inventoryIconLayer {{ position:absolute; inset:2px; z-index:1; width:calc(100% - 4px); height:calc(100% - 4px); object-fit:contain; image-rendering:pixelated; pointer-events:none; }}
.inventoryIconLayer.underlay {{ z-index: 1; }}
.inventoryIconLayer.icon {{ z-index: 2; }}
.inventoryIconLayer.overlay {{ z-index: 3; }}
.inventoryQty {{ position: absolute; right: 1px; bottom: 0; z-index: 4; color: #9dff74; font-size: 10px; font-weight: 700; text-shadow: 0 1px 2px #000, 0 0 3px #000; }}
.inventoryEmpty {{ color: #6e7774; font-size: 12px; padding: 8px; }}
.inventoryTag {{ display: inline-block; margin-right: 4px; padding: 1px 4px; border-radius: 3px; background: rgba(155,214,255,.12); color: #9bd6ff; font-size: 10px; }}
.inventoryForm {{ display: grid; gap: 9px; }}
.inventoryForm label {{ display: grid; gap: 4px; color: #aeb9b4; font-size: 11px; text-transform: uppercase; }}
.inventoryForm input {{ padding: 7px; }}
.inventoryForm textarea {{ width: 100%; min-height: 62px; resize: vertical; background: #0e1112; color: #e8ece8; border: 1px solid rgba(255,255,255,.18); border-radius: 4px; padding: 7px; }}
.formGrid {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }}
.inventoryDetails {{ display: grid; gap: 5px; margin-bottom: 12px; color: #c8d1cc; font-size: 12px; }}
.inventoryDetails strong {{ color: #fff; overflow-wrap: anywhere; }}
.propertyEditor {{ display:grid; gap:6px; margin-top:10px; }}
.propertyGroup {{ border:1px solid rgba(255,255,255,.12); border-radius:4px; }}
.propertyGroup summary {{ cursor:pointer; padding:7px; color:#d8e2dd; text-transform:uppercase; font-size:11px; }}
.propertyRows {{ display:grid; gap:4px; padding:6px; }}
.propertyRow {{ display:grid; grid-template-columns:minmax(110px,1fr) minmax(90px,1fr) auto; gap:5px; align-items:center; }}
.propertyRow label {{ overflow-wrap:anywhere; color:#aeb9b4; font-size:11px; }}
.propertyRow input {{ min-width:0; }}
.dangerButton {{ background: #763330; }}
.smallButton {{ padding: 5px 8px; font-size: 11px; }}
.playerView .adminOnly {{ display: none !important; }} .playerView .watchRowButton {{ display: none !important; }}
@media (max-width: 980px) {{ .bottomDock {{ left: 12px; grid-template-columns: 1fr; }} .statGrid {{ grid-template-columns: repeat(2, minmax(0, 1fr)); }} }}
@media (max-width: 860px) {{ body {{ grid-template-columns: 1fr; }} #map {{ min-height: 64vh; }} aside {{ border-left: 0; border-top: 1px solid rgba(255,255,255,.12); }} .bottomDock {{ position: static; margin: 8px 12px 12px; }} .inventoryPanel {{ inset: 10px; grid-template-columns: 1fr; }} .inventoryEditPane {{ border-left: 0; border-top: 1px solid rgba(255,255,255,.12); }} }}
/* Modern Admin Map shell */
body{{grid-template-columns:minmax(0,1fr) 390px;height:100vh;min-height:0;overflow:hidden;background:#0b0f11}}#map{{height:100vh;min-height:0;background-color:#11181a}}.mapToolbar{{position:absolute;z-index:8;left:14px;right:14px;top:14px;display:flex;align-items:center;gap:8px;min-height:48px;padding:7px 8px 7px 14px;border:1px solid rgba(255,255,255,.14);border-radius:6px;background:rgba(11,16,18,.9);box-shadow:0 12px 34px rgba(0,0,0,.32);backdrop-filter:blur(12px)}}.mapToolbar h1{{margin:0;font-size:15px;white-space:nowrap}}.toolbarStatus{{min-width:0;flex:1;color:#aebbb6;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}.toolbarActions{{display:flex;gap:5px}}.toolbarActions button{{width:34px;height:32px;padding:0;display:grid;place-items:center;background:#202b2e;font-size:16px}}aside{{height:100vh;min-height:0;padding:0;display:grid;grid-template-rows:auto auto minmax(0,1fr);overflow:hidden;background:#121719;box-shadow:-14px 0 36px rgba(0,0,0,.2)}}.sidebarHeader{{padding:14px 14px 10px;border-bottom:1px solid rgba(255,255,255,.1);background:#151c1e}}.loginPanel,.sessionBar,.controls{{margin-bottom:0}}.sessionBar{{grid-template-columns:minmax(0,1fr) auto auto}}.sidebarTabs{{display:grid;grid-template-columns:repeat(3,1fr);gap:4px;padding:8px 12px;border-bottom:1px solid rgba(255,255,255,.1);background:#101517}}.sidebarTab{{padding:7px 5px;background:transparent;color:#98a7a2;border-color:transparent;font-size:12px}}.sidebarTab.active{{color:#fff;background:#263538;border-color:#46585c}}.sidebarContent{{min-height:0;overflow:auto;padding:12px 14px 20px;scrollbar-gutter:stable}}.sidebarSearch{{position:sticky;top:-12px;z-index:3;display:grid;grid-template-columns:1fr auto;gap:6px;margin:-12px -2px 8px;padding:12px 2px 8px;background:#121719}}.rosterCount{{min-width:38px;display:grid;place-items:center;color:#9fb0aa;font-size:11px}}body[data-side-view=""players""] .sideInspect,body[data-side-view=""players""] .sideLegend,body[data-side-view=""inspect""] .sidePlayers,body[data-side-view=""inspect""] .sideLegend,body[data-side-view=""legend""] .sidePlayers,body[data-side-view=""legend""] .sideInspect{{display:none!important}}.player{{margin:0 -6px;padding:10px 8px;border-top:0;border-bottom:1px solid rgba(255,255,255,.08);border-radius:3px}}.player:hover{{background:rgba(255,255,255,.045)}}.player.selected{{margin:0 -6px;padding:10px 8px;background:#23363a;box-shadow:inset 3px 0 #71b7e8}}.player .inventoryActions{{opacity:.62}}.player:hover .inventoryActions,.player.selected .inventoryActions{{opacity:1}}.adminPanel,.watchPanel,.legend{{border-radius:5px;background:#171e20}}.bottomDock{{left:14px;right:14px;bottom:14px;gap:10px}}.dockPanel{{border-radius:6px;background:rgba(10,15,17,.9);backdrop-filter:blur(10px)}}.zoomControls{{left:14px;bottom:150px;grid-template-columns:repeat(4,34px)}}.zoomControls button{{width:34px;height:34px;background:rgba(15,22,24,.92)}}.inventoryPanel{{inset:18px;grid-template-columns:minmax(430px,1fr) minmax(340px,420px);border-radius:7px}}.inventoryListPane,.inventoryEditPane{{padding:16px}}.inventorySlot{{width:42px;height:42px}}.inventoryGrid{{grid-template-columns:repeat(auto-fill,42px);gap:5px}}body.sidebarCollapsed{{grid-template-columns:minmax(0,1fr) 0}}body.sidebarCollapsed aside{{visibility:hidden}}.emptyRoster{{padding:24px 8px;text-align:center;color:#7f8d88;font-size:12px}}@media(max-width:860px){{body{{height:auto;overflow:auto;grid-template-columns:1fr}}#map{{height:65vh;min-height:480px}}aside{{height:auto;min-height:520px}}body.sidebarCollapsed{{grid-template-columns:1fr}}body.sidebarCollapsed aside{{display:none}}.mapToolbar{{left:8px;right:8px;top:8px}}.toolbarStatus{{display:none}}.bottomDock{{left:8px;right:8px;bottom:8px}}}}
/* Login/sidebar click safety */
aside {{ position:relative; z-index:30; pointer-events:auto; }}
.sidebarHeader {{ position:relative; z-index:40; pointer-events:auto; }}
.loginPanel, .loginPanel * {{ pointer-events:auto; }}
#loginButton {{ position:relative; z-index:41; }}/* Inventory workspace refinement */
.inventoryPanel {{ inset:24px; grid-template-columns:minmax(520px,1.35fr) minmax(380px,.85fr); max-width:1500px; margin:auto; border-color:rgba(155,214,255,.25); background:#0d1214; }}
.inventoryListPane {{ grid-template-rows:auto auto minmax(0,1fr); gap:12px; padding:18px; background:#0d1214; }}
.inventoryTop {{ min-height:38px; padding-bottom:10px; border-bottom:1px solid rgba(255,255,255,.1); }}
.inventoryTop h2 {{ font-size:17px; color:#f2f5f3; }}
.inventorySearch input {{ height:36px; }}
.inventoryTable {{ padding:12px; border-color:rgba(255,255,255,.12); background:#080c0d; }}
.inventoryGrid {{ grid-template-columns:repeat(auto-fill,48px); gap:6px; }}
.inventorySlot {{ width:48px; height:48px; border-color:#334145; background:#111719; }}
.inventorySlot:hover {{ border-color:#71b7e8; outline:1px solid #71b7e8; }}
.inventorySlot.selected {{ border-color:#efc36b; outline:2px solid #efc36b; }}
.inventoryIconLayer {{ inset:3px; }}
.inventoryFallback {{ color:#81908b; font-size:11px; }}
.inventoryQty {{ right:3px; bottom:2px; font-size:11px; }}
.inventorySectionTitle {{ margin:12px 0 5px; padding-bottom:4px; border-bottom:1px solid rgba(255,255,255,.08); color:#aebbb6; text-transform:uppercase; font-size:10px; }}
.inventoryEditPane {{ padding:18px; background:#151c1e; }}
.inventoryDetails {{ position:sticky; top:-18px; z-index:3; margin:-18px -18px 14px; padding:16px 18px 12px; border-bottom:1px solid rgba(255,255,255,.12); background:#151c1e; }}
.inventoryDetailsHead {{ display:grid; grid-template-columns:46px minmax(0,1fr); gap:10px; align-items:center; }}
.inventoryDetailsHead div {{ display:grid; gap:4px; min-width:0; }}
.inventoryDetailsHead span {{ min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }}
.inventoryForm {{ grid-template-columns:repeat(2,minmax(0,1fr)); gap:10px; }}
.inventoryForm>label:nth-of-type(5),.inventoryForm>label:nth-of-type(6),.inventoryForm>.formGrid,.inventoryForm>#inventorySave,.inventoryForm>.propertyEditor,.inventoryForm>#inventoryDelete {{ grid-column:1/-1; }}
.inventoryForm label {{ gap:5px; color:#94a39e; font-size:10px; }}
.inventoryForm input,.inventoryForm textarea {{ background:#0c1113; border-color:#344145; }}
.formGrid {{ padding:10px; border:1px solid rgba(255,255,255,.09); border-radius:5px; background:#111719; }}
.propertyGroup {{ background:#111719; }}
#inventorySave {{ background:#2d6650; }}
/* Inventory bag layout */
.inventoryPanel {{ inset:22px; grid-template-columns:minmax(420px,.95fr) minmax(430px,.75fr); max-width:1420px; }}
.inventoryTable {{ display:grid; gap:10px; align-content:start; }}
.inventoryBag {{ border:1px solid rgba(255,255,255,.11); border-radius:6px; background:#090d0f; overflow:hidden; }}
.inventoryBagHeader {{ display:flex; align-items:center; justify-content:space-between; gap:8px; min-height:30px; padding:6px 9px; border-bottom:1px solid rgba(255,255,255,.08); background:#121a1d; color:#f1e3aa; font-size:12px; font-weight:650; }}
.inventoryBagMeta {{ color:#8fa09a; font-size:10px; font-weight:500; }}
.inventoryGrid {{ padding:8px; grid-template-columns:repeat(auto-fill,44px); gap:5px; align-content:start; }}
.inventorySlot {{ width:44px; height:44px; display:grid; place-items:center; padding:0; border-radius:4px; border-color:#26363a; background:linear-gradient(180deg,#11191b,#0a0f11); }}
.inventorySlot:hover {{ background:#172428; transform:translateY(-1px); border-color:#71b7e8; }}
.inventorySlot.selected {{ border-color:#efc36b; outline:2px solid #efc36b; }}
.inventoryIconStack {{ position:relative; width:38px; height:38px; display:block; }}
.inventoryIconLayer {{ inset:0; width:100%; height:100%; }}
.inventoryIconLayer.underlay {{ z-index:1; opacity:1; }}
.inventoryIconLayer.icon {{ z-index:2; }}
.inventoryIconLayer.overlay {{ z-index:3; }}
.inventoryFallback {{ inset:0; z-index:0; font-size:10px; }}
.inventoryItemName {{ display:none; }}
.inventoryQty {{ right:-3px; bottom:-2px; min-width:15px; padding:0 3px; border-radius:7px; background:rgba(2,5,6,.82); color:#b7ff8a; font-size:10px; line-height:14px; }}
.inventorySectionTitle {{ display:none; }}
.editGroup {{ border:1px solid rgba(255,255,255,.11); border-radius:6px; background:#101719; overflow:hidden; }}
.editGroup + .editGroup, .editGroup + button, button + .editGroup {{ margin-top:8px; }}
.editGroup summary {{ cursor:pointer; padding:8px 10px; color:#f1e3aa; background:#141d20; border-bottom:1px solid rgba(255,255,255,.08); font-size:11px; font-weight:650; text-transform:uppercase; }}
.editGroup:not([open]) summary {{ border-bottom:0; }}
.editGrid {{ display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:10px; padding:10px; }}
.editGrid label:has(textarea) {{ grid-column:1/-1; }}
.inventoryForm {{ display:block; }}
.inventoryForm label {{ gap:5px; color:#94a39e; font-size:10px; }}
.inventoryForm input,.inventoryForm textarea {{ background:#0c1113; border-color:#344145; }}
.propertyEditGroup .propertyEditor {{ padding:8px; }}
.propertyGroup {{ background:#0d1315; }}
@media(min-width:641px) {{ .inventoryPanel.open {{ display:grid; grid-template-columns:minmax(500px,1.35fr) minmax(360px,.85fr); overflow:hidden; }} .inventoryListPane,.inventoryEditPane {{ min-height:0; overflow:auto; }} .inventoryEditPane {{ border-left:1px solid rgba(255,255,255,.12); border-top:0; }} }}
@media(max-width:640px) {{ .inventoryPanel {{ inset:8px; grid-template-columns:1fr; overflow:auto; }} .inventoryListPane {{ min-height:55vh; }} .inventoryEditPane {{ border-left:0; border-top:1px solid rgba(255,255,255,.12); }} .inventoryForm {{ grid-template-columns:1fr; }} .inventoryForm>* {{ grid-column:1!important; }} }}
</style>
</head>
<body>
<main id=""map""><div class=""mapToolbar""><h1 id=""mapTitle"">DerpACE Map</h1><div id=""status"" class=""toolbarStatus"">Loading...</div><div class=""toolbarActions""><button id=""worldButton"" title=""Return to overworld"">&#8962;</button><button id=""refreshButton"" title=""Refresh now"">&#8635;</button><button id=""sidebarButton"" title=""Toggle sidebar"">&#9776;</button></div></div>
  <div class=""axis north"">102N</div><div class=""axis south"">102S</div><div class=""axis west"">102W</div><div class=""axis east"">102E</div>
  <div class=""zoomControls""><button id=""zoomIn"" title=""Zoom in"">+</button><button id=""zoomOut"" title=""Zoom out"">-</button><button id=""zoomReset"" title=""Reset view"">1</button><button id=""zoomFit"" title=""Fit"">&#9633;</button></div>
  <div id=""bottomDock"" class=""bottomDock""></div>
</main>
<aside><div class=""sidebarHeader"">
<div id=""loginPanel"" class=""loginPanel"">
    <input id=""loginAccount"" autocomplete=""username"" placeholder=""account"">
    <input id=""loginPassword"" type=""password"" autocomplete=""current-password"" placeholder=""password"">
    <button id=""loginButton"">Log In</button>
  </div>
  <div id=""sessionBar"" class=""sessionBar""><span id=""sessionText""></span><a id=""bossMechanicsLink"" class=""adminOnly"" href=""/boss-mechanics"" target=""_blank"">Boss Mechanics</a><a id=""spellWorkshopLink"" class=""adminOnly"" href=""/spell-workshop"" target=""_blank"">Spells</a><button id=""logoutButton"">Log Out</button></div>
  <div class=""controls adminOnly""><input id=""token"" type=""password"" placeholder=""backup token, optional""><button id=""save"">Save</button></div></div><nav class=""sidebarTabs""><button class=""sidebarTab active"" data-side-tab=""players"">Players</button><button class=""sidebarTab adminOnly"" data-side-tab=""inspect"">Inspect</button><button class=""sidebarTab adminOnly"" data-side-tab=""legend"">Legend</button></nav><div class=""sidebarContent""><div class=""sidebarSearch sidePlayers""><input id=""playerSearch"" placeholder=""Search players or locations""><span id=""rosterCount"" class=""rosterCount"">0</span></div>
  <section id=""adminPanel"" class=""adminPanel adminOnly sideInspect"">
    <h2 id=""adminPlayerName"">Player</h2>
    <div class=""muted"" id=""adminPlayerLoc""></div>
    <div class=""adminGrid wide""><label>Paste LOC<input id=""teleLoc"" placeholder=""0x7F0401AD [x y z] qw qx qy qz""></label></div>
    <div class=""adminGrid""><label>Cell<input id=""teleCell"" placeholder=""0x00000000""></label><label>X<input id=""teleX"" type=""number"" step=""0.001""></label><label>Y<input id=""teleY"" type=""number"" step=""0.001""></label><label>Z<input id=""teleZ"" type=""number"" step=""0.001""></label></div>
    <button id=""teleportButton"">Teleport Player</button>
    <div class=""adminGrid wide""><label>Reason<input id=""bootReason"" placeholder=""optional boot reason""></label></div>
    <button id=""bootButton"" class=""dangerButton"">Boot Player</button>
    <button id=""watchButton"">Watch Player</button>
  </section>
  <section id=""watchPanel"" class=""watchPanel adminOnly sideInspect"">
    <div class=""watchHeader""><div id=""watchTitle"" class=""watchTitle"">Watching</div><button id=""watchStop"" class=""smallButton"">Stop</button></div>
    <div id=""watchView"" class=""watchView""><span class=""watchAxis""></span><span class=""watchAxis vertical""></span></div>
    <div id=""watchMeta"" class=""watchMeta""></div>
  </section>
  <div class=""legend adminOnly sideLegend"">
    <div class=""legendItem""><span class=""legendDot player""></span><span>Player</span></div>
    <div class=""legendItem""><span class=""legendDot creature""></span><span>Creature</span></div>
    <div class=""legendItem""><span class=""legendDot npc""></span><span>NPC/vendor</span></div>
    <div class=""legendItem""><span class=""legendDot portal""></span><span>Portal</span></div>
    <div class=""legendItem""><span class=""legendDot light""></span><span>Light</span></div>
    <div class=""legendItem""><span class=""legendDot door""></span><span>Door</span></div>
    <div class=""legendItem depthKey""><span>Low</span><span class=""depthRamp""></span><span>High</span></div>
  </div>
  <div id=""players"" class=""sidePlayers""></div></div>
</aside>
<section id=""inventoryPanel"" class=""inventoryPanel"" aria-live=""polite"">
  <div class=""inventoryListPane"">
    <div class=""inventoryTop""><h2 id=""inventoryTitle"">Inventory</h2><div class=""inventoryActions""><button id=""inventoryRefresh"" class=""iconButton"" title=""Refresh"">&#8635;</button><button id=""inventoryClose"" class=""iconButton"" title=""Close"">&times;</button></div></div>
    <div class=""inventorySearch""><input id=""inventoryFilter"" placeholder=""search name, type, wcid, container""><button id=""inventoryClear"" class=""smallButton"">Clear</button></div>
    <div id=""inventoryRows"" class=""inventoryTable""></div>
  </div>
  <div class=""inventoryEditPane adminOnly"">
    <div id=""inventoryDetails"" class=""inventoryDetails""><span class=""muted"">Select an item</span></div>
    <div class=""inventoryForm"">
      <details class=""editGroup"" open><summary>Identity</summary><div class=""editGrid"">
        <label>Name<input id=""editName""></label>
        <label>Description<textarea id=""editLongDesc""></textarea></label>
      </div></details>
      <details class=""editGroup"" open><summary>Stack, Value, Craft</summary><div class=""editGrid"">
        <label>Stack<input id=""editStack"" type=""number"" min=""1""></label>
        <label>Value<input id=""editValue"" type=""number"" min=""0""></label>
        <label>Burden<input id=""editBurden"" type=""number"" min=""0""></label>
        <label>Workmanship<input id=""editWorkmanship"" type=""number"" min=""0""></label>
      </div></details>
      <details class=""editGroup""><summary>Combat</summary><div class=""editGrid"">
        <label>Damage<input id=""editDamage"" type=""number""></label>
        <label>Damage Mod<input id=""editDamageMod"" type=""number"" step=""0.001""></label>
        <label>Variance<input id=""editDamageVariance"" type=""number"" step=""0.001""></label>
        <label>Elem Bonus<input id=""editElementalDamageBonus"" type=""number""></label>
        <label>Elem Mod<input id=""editElementalDamageMod"" type=""number"" step=""0.001""></label>
        <label>Armor<input id=""editArmorLevel"" type=""number""></label>
      </div></details>
      <details class=""editGroup""><summary>Durability and Mana</summary><div class=""editGrid"">
        <label>Structure<input id=""editStructure"" type=""number""></label>
        <label>Max Structure<input id=""editMaxStructure"" type=""number""></label>
        <label>Mana<input id=""editItemCurMana"" type=""number""></label>
        <label>Max Mana<input id=""editItemMaxMana"" type=""number""></label>
      </div></details>
      <details class=""editGroup""><summary>Appearance</summary><div class=""editGrid"">
        <label>Material<input id=""editMaterialType"" type=""number""></label>
        <label>Palette<input id=""editPaletteTemplate"" type=""number""></label>
        <label>Shade<input id=""editShade"" type=""number"" step=""0.001""></label>
      </div></details>
      <details class=""editGroup""><summary>Ratings</summary><div class=""editGrid"">
        <label>DR<input id=""editDamageRating"" type=""number""></label>
        <label>CDR<input id=""editCritDamageRating"" type=""number""></label>
        <label>Resist<input id=""editDamageResistRating"" type=""number""></label>
        <label>Crit Resist<input id=""editCritDamageResistRating"" type=""number""></label>
        <label>Gear DR<input id=""editGearDamage"" type=""number""></label>
        <label>Gear Resist<input id=""editGearDamageResist"" type=""number""></label>
        <label>Gear Crit<input id=""editGearCritDamage"" type=""number""></label>
        <label>Gear Crit Resist<input id=""editGearCritDamageResist"" type=""number""></label>
      </div></details>
      <button id=""inventorySave"">Save Common Fields</button>
      <details class=""editGroup propertyEditGroup""><summary>Raw Properties</summary><div id=""propertyEditor"" class=""propertyEditor""></div></details>
      <button id=""inventoryDelete"" class=""dangerButton"">Delete Item</button>
    </div>
  </div>
</section>
<script>
const map = document.getElementById('map');
const list = document.getElementById('players');
const status = document.getElementById('status');
const token = document.getElementById('token');
const loginPanel = document.getElementById('loginPanel');
const loginAccount = document.getElementById('loginAccount');
const loginPassword = document.getElementById('loginPassword');
const sessionBar = document.getElementById('sessionBar');
const sessionText = document.getElementById('sessionText');
const bossMechanicsLink = document.getElementById('bossMechanicsLink');
const spellWorkshopLink = document.getElementById('spellWorkshopLink');
const bottomDock = document.getElementById('bottomDock');
const adminPanel = document.getElementById('adminPanel');
const adminPlayerName = document.getElementById('adminPlayerName');
const adminPlayerLoc = document.getElementById('adminPlayerLoc');
const watchPanel = document.getElementById('watchPanel');
const watchTitle = document.getElementById('watchTitle');
const watchView = document.getElementById('watchView');
const watchMeta = document.getElementById('watchMeta');
const inventoryPanel = document.getElementById('inventoryPanel');
const inventoryRows = document.getElementById('inventoryRows');
const inventoryTitle = document.getElementById('inventoryTitle');
const inventoryFilter = document.getElementById('inventoryFilter');
const inventoryDetails = document.getElementById('inventoryDetails');
const propertyEditor = document.getElementById('propertyEditor');
const playerSearch = document.getElementById('playerSearch');
const rosterCount = document.getElementById('rosterCount');
let currentDungeon = null;
let currentMode = null;
let mapLayer = null;
let view = {{ scale: 1, x: 0, y: 0 }};
let dragging = false;
let dragStart = null;
let inventoryState = {{ playerGuid: null, data: null, selected: null }};
let selectedPlayer = null;
let playerIndex = {{}};
let collapsedDock = {{}};
let currentMapBounds = null;
let watchPlayerGuid = null;
let authenticated = false;
let isAdminSession = false;
let refreshTimer = null;
let loginInProgress = false;
let mapSessionToken = null;
try {{ collapsedDock = JSON.parse(localStorage.getItem('derpace-admin-map-collapsed') || '{{}}') || {{}}; }} catch {{ collapsedDock = {{}}; }}
window.addEventListener('error', e => {{ status.textContent = e.message || 'Admin map script error'; }});
token.value = localStorage.getItem('derpace-admin-map-token') || '';
document.getElementById('save').onclick = () => {{ localStorage.setItem('derpace-admin-map-token', token.value); setAuthenticated(!!token.value, null, !!token.value); refresh(); }};
const loginButton = document.getElementById('loginButton');
loginButton.addEventListener('click', e => {{ e.preventDefault(); login(); }});
document.getElementById('logoutButton').onclick = logout;
loginPassword.addEventListener('keydown', e => {{ if (e.key === 'Enter') {{ e.preventDefault(); login(); }} }});
loginAccount.addEventListener('keydown', e => {{ if (e.key === 'Enter') {{ e.preventDefault(); login(); }} }});
function pctX(x) {{ return ((x + 102) / 204) * 100; }}
function pctY(y) {{ return ((102 - y) / 204) * 100; }}
function setSideView(view) {{
  document.body.dataset.sideView = view;
  document.querySelectorAll('[data-side-tab]').forEach(button => button.classList.toggle('active', button.dataset.sideTab === view));
}}
function applyPlayerFilter() {{
  const q = (playerSearch?.value || '').trim().toLowerCase();
  let shown = 0;
  list.querySelectorAll('.player').forEach(row => {{ const visible = !q || (row.dataset.search || '').includes(q); row.hidden = !visible; if (visible) shown++; }});
  if (rosterCount) rosterCount.textContent = String(shown);
  let empty = list.querySelector('.emptyRoster');
  if (!shown && list.children.length) {{ if (!empty) {{ empty = document.createElement('div'); empty.className = 'emptyRoster'; empty.textContent = 'No players match this search.'; list.appendChild(empty); }} }} else if (empty) empty.remove();
}}function mapPctX(x, b) {{ return b.left + ((x + 102) / 204) * (b.right - b.left); }}
function mapPctY(y, b) {{ return b.top + ((102 - y) / 204) * (b.bottom - b.top); }}
function pctToMapX(xPct, b) {{ return ((xPct - b.left) / Math.max(0.0001, b.right - b.left)) * 204 - 102; }}
function pctToMapY(yPct, b) {{ return 102 - ((yPct - b.top) / Math.max(0.0001, b.bottom - b.top)) * 204; }}
function dungeonPctX(x, d) {{ return ((x - d.minX) / Math.max(1, d.maxX - d.minX)) * 100; }}
function dungeonPctY(y, d) {{ return ((d.maxY - y) / Math.max(1, d.maxY - d.minY)) * 100; }}
function bar(cur, max, cls) {{ const p = max > 0 ? Math.max(0, Math.min(100, cur / max * 100)) : 0; return `<div class=""bar ${{cls}}""><span style=""width:${{p}}%""></span></div>`; }}
function esc(v) {{ return String(v ?? '').replace(/[&<>""']/g, ch => ({{'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}}[ch])); }}
function fmtNum(v) {{ return Number(v || 0).toLocaleString(); }}
function leaderText(entry, suffix) {{ return entry ? `${{esc(entry.name)}} ${{suffix ? suffix(entry) : fmtNum(entry.kills)}}` : 'none'; }}
function feedTime(utc) {{ const d = new Date(utc); return isNaN(d) ? '--:--' : d.toLocaleTimeString([], {{ hour: '2-digit', minute: '2-digit' }}); }}
function renderChatFeed(chat) {{
  const rows = (chat || []).map(e => `<div class=""feedRow""><span class=""feedTime"">${{feedTime(e.utc)}}</span><span class=""feedText""><span class=""feedName"">${{esc(e.sender)}}</span>: ${{esc(e.message)}}</span></div>`).join('');
  return rows || '<div class=""muted"">No General chat yet</div>';
}}
function renderRareFeed(rares) {{
  const rows = (rares || []).map(e => `<div class=""feedRow"" title=""${{esc((e.location || e.landblock || '') + ' ' + (e.corpse || ''))}}""><span class=""feedTime"">${{feedTime(e.utc)}}</span><span class=""feedText""><span class=""feedName"">${{esc(e.player)}}</span> found <span class=""rareItem"">${{esc(e.item)}}</span> <span class=""rareTier"">T${{fmtNum(e.tier)}}</span></span></div>`).join('');
  return rows || '<div class=""muted"">No rare finds yet</div>';
}}
function setBossMechanicsLink() {{
  if (!bossMechanicsLink) return;
  bossMechanicsLink.href = mapSessionToken ? `/boss-mechanics?session=${{encodeURIComponent(mapSessionToken)}}` : '/boss-mechanics';
  if (spellWorkshopLink) spellWorkshopLink.href = mapSessionToken ? `/spell-workshop?session=${{encodeURIComponent(mapSessionToken)}}` : '/spell-workshop';
}}
function setAuthenticated(value, accountName, isAdmin) {{
  authenticated = !!value || !!token.value;
  isAdminSession = !!isAdmin || (!!token.value && !value);
  document.body.classList.toggle('playerView', authenticated && !isAdminSession);
  document.getElementById('mapTitle').textContent = isAdminSession ? 'Admin Map' : 'My Characters';
  document.body.classList.toggle('authLocked', !authenticated);
  loginPanel.classList.toggle('hidden', !!value);
  sessionBar.classList.toggle('active', !!value);
  sessionText.textContent = value ? `Logged in as ${{accountName || 'admin'}}` : '';
  setBossMechanicsLink();
  if (!authenticated) {{
    clearMap();
    list.innerHTML = '';
    bottomDock.innerHTML = '';
    watchPlayerGuid = null;
    watchPanel.classList.remove('open');
  }}
}}
async function checkSession() {{
  try {{
    const res = await fetch('/api/session', {{ cache: 'no-store' }});
    const data = await res.json();
    mapSessionToken = data.sessionToken || mapSessionToken;
    setAuthenticated(!!data.authenticated, data.accountName, !!data.isAdmin);
    if (authenticated) {{
      load();
      if (!refreshTimer) refreshTimer = setInterval(refresh, {refresh * 1000});
    }} else {{
      status.textContent = 'Log in with your game account.';
      loginAccount.focus();
    }}
  }} catch (e) {{
    setAuthenticated(false);
    status.textContent = e.message;
  }}
}}
async function login() {{
  if (loginInProgress) return;
  loginInProgress = true;
  loginButton.disabled = true;
  status.textContent = 'Logging in...';
  try {{
    const res = await fetch('/api/login', {{
      method: 'POST',
      headers: {{ 'Content-Type': 'application/json' }},
      body: JSON.stringify({{ account: loginAccount.value, password: loginPassword.value }})
    }});
    const data = await res.json();
    if (!res.ok || data.ok === false) {{
      setAuthenticated(false);
      status.textContent = data.error || res.statusText;
      return;
    }}
    loginPassword.value = '';
    mapSessionToken = data.sessionToken || mapSessionToken;
    setAuthenticated(true, data.accountName, !!data.isAdmin);
    load();
    if (!refreshTimer) refreshTimer = setInterval(refresh, {refresh * 1000});
  }} catch (e) {{
    setAuthenticated(false);
    status.textContent = e.message || 'Login failed.';
  }} finally {{
    loginInProgress = false;
    loginButton.disabled = false;
  }}
}}
async function logout() {{
  await fetch('/api/logout', {{ method: 'POST' }});
  setAuthenticated(false);
  status.textContent = 'Logged out.';
  loginPassword.value = '';
  loginAccount.focus();
}}
function clearMap() {{ map.querySelectorAll('.mapLayer').forEach(x => x.remove()); mapLayer = null; map.classList.remove('hasLayer'); }}
function applyView() {{ if (mapLayer) mapLayer.style.transform = `translate(${{view.x}}px, ${{view.y}}px) scale(${{view.scale}})`; }}
function resetView() {{ view = {{ scale: 1, x: 0, y: 0 }}; applyView(); }}
function blipClass(blip) {{ return (blip.kind || (blip.isMonster ? 'creature' : 'npc')).toLowerCase(); }}
function createLayer(kind) {{
  mapLayer = document.createElement('div');
  mapLayer.className = 'mapLayer ' + kind;
  map.appendChild(mapLayer);
  map.classList.add('hasLayer');
  applyView();
  return mapLayer;
}}
function zoomAt(factor, cx, cy) {{
  const old = view.scale;
  const next = Math.max(0.35, Math.min(8, old * factor));
  if (next === old) return;
  view.x = cx - ((cx - view.x) / old) * next;
  view.y = cy - ((cy - view.y) / old) * next;
  view.scale = next;
  applyView();
}}
function addBlip(layer, blip, left, top) {{
  const marker = document.createElement('span');
  const kind = blipClass(blip);
  marker.className = 'blip ' + kind;
  marker.style.left = left + '%';
  marker.style.top = top + '%';
  marker.dataset.name = blip.name || '';
  marker.title = `${{blip.name || kind}}\n${{blip.loc || blip.cell}}\n${{kind}}${{blip.radarColor ? ' / ' + blip.radarColor : ''}}\nz ${{Number(blip.z || 0).toFixed(2)}}`;
  layer.appendChild(marker);
}}
function renderDock(data) {{
  if (!isAdminSession) {{
    const players = data.players || [];
    const chips = players.map(p => `<span class=""onlineChip"" title=""${{esc(p.loc || p.landblock)}}""><span class=""onlineDot""></span><span class=""onlineName"">${{esc(p.name)}}</span></span>`).join('');
    bottomDock.innerHTML = `<section class=""dockPanel""><div class=""dockTitle"">Your online characters and fellowship</div><div class=""dockBody""><div class=""onlineChips"">${{chips || '<span class=""muted"">No account characters are currently online</span>'}}</div></div></section>`;
    return;
  }}
  const stats = data.stats || {{}};
  const feeds = data.feeds || {{}};
  const players = data.players || [];
  const collapsed = id => collapsedDock[id] ? ' collapsed' : '';
  const glyph = id => collapsedDock[id] ? '+' : '-';
  const playerChips = players
    .slice()
    .sort((a, b) => String(a.name).localeCompare(String(b.name)))
    .map(p => `<span class=""onlineChip"" title=""${{esc(p.loc || p.landblock)}}""><span class=""onlineDot""></span><span class=""onlineName"">${{esc(p.name)}}</span></span>`)
    .join('');
  bottomDock.innerHTML = `
    <section class=""dockPanel${{collapsed('stats')}}"">
      <div class=""dockTitle""><span>Stats</span><button class=""dockToggle"" data-dock=""stats"">${{glyph('stats')}}</button></div>
      <div class=""dockBody""><div class=""statGrid"">
        <div class=""statBox""><div class=""statLabel"">Online</div><div class=""statValue"">${{fmtNum(stats.onlineCount ?? data.onlineCount)}}</div></div>
        <div class=""statBox""><div class=""statLabel"">Unique IPs</div><div class=""statValue"">${{fmtNum(stats.uniqueIpCount)}}</div></div>
        <div class=""statBox""><div class=""statLabel"">Hardcore</div><div class=""statValue"">${{fmtNum(stats.hardcoreOnlineCount)}}</div></div>
        <div class=""statBox""><div class=""statLabel"">Ironman</div><div class=""statValue"">${{fmtNum(stats.ironmanOnlineCount)}}</div></div>
        <div class=""statBox""><div class=""statLabel"">HC leader</div><div class=""statValue"">${{leaderText(stats.hardcoreLeader, e => fmtNum(e.kills) + ' kills')}}</div></div>
        <div class=""statBox""><div class=""statLabel"">IM leader</div><div class=""statValue"">${{leaderText(stats.ironmanLeader, e => fmtNum(e.kills) + ' kills')}}</div></div>
        <div class=""statBox""><div class=""statLabel"">Deadliest</div><div class=""statValue"">${{leaderText(stats.deadliestNormal, e => fmtNum(e.kills))}}</div></div>
        <div class=""statBox""><div class=""statLabel"">HC killer</div><div class=""statValue"">${{leaderText(stats.deadliestHardcore, e => fmtNum(e.kills))}}</div></div>
        <div class=""statBox""><div class=""statLabel"">IM killer</div><div class=""statValue"">${{leaderText(stats.deadliestIronman, e => fmtNum(e.kills))}}</div></div>
      </div>
      <div class=""feedBlock""><div class=""dockTitle"">Rare Finds</div><div class=""feedList"">${{renderRareFeed(feeds.rares)}}</div></div>
      </div>
    </section>
    <section class=""dockPanel${{collapsed('online')}}"">
      <div class=""dockTitle""><span>Online Players</span><button class=""dockToggle"" data-dock=""online"">${{glyph('online')}}</button></div>
      <div class=""dockBody"">
      <div class=""onlineChips"">${{playerChips || '<span class=""muted"">No visible players</span>'}}</div>
      <div class=""feedBlock""><div class=""dockTitle"">General Chat</div><div class=""feedList"">${{renderChatFeed(feeds.chat)}}</div></div>
      </div>
    </section>`;
  bottomDock.querySelectorAll('.dockToggle').forEach(btn => btn.onclick = e => {{
    const id = e.currentTarget.dataset.dock;
    collapsedDock[id] = !collapsedDock[id];
    localStorage.setItem('derpace-admin-map-collapsed', JSON.stringify(collapsedDock));
    renderDock(data);
  }});
}}
function authHeaders(extra) {{ return Object.assign(token.value ? {{ 'X-DerpACE-Map-Token': token.value }} : {{}}, extra || {{}}); }}
function numberOrNull(id) {{
  const value = document.getElementById(id).value;
  return value === '' ? null : Number(value);
}}
const numOrNull = numberOrNull;
async function readJsonResponse(res) {{
  const text = await res.text();
  if (!text) return {{}};
  try {{ return JSON.parse(text); }} catch {{ return {{ ok: false, error: text }}; }}
}}
async function copyText(text) {{
  if (navigator.clipboard?.writeText) {{
    await navigator.clipboard.writeText(text);
    return;
  }}

  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.style.position = 'fixed';
  textarea.style.left = '-9999px';
  document.body.appendChild(textarea);
  textarea.select();
  document.execCommand('copy');
  textarea.remove();
}}
async function copyMapLocAt(event) {{
  if (currentMode !== 'world' || !currentMapBounds || !mapLayer) {{ status.textContent = 'LOC copy is available on the overworld map.'; return; }}
  if (event.target.closest('button, input, textarea, select, aside, .bottomDock, .inventoryPanel')) return;

  const rect = map.getBoundingClientRect();
  const localX = (event.clientX - rect.left - view.x) / view.scale;
  const localY = (event.clientY - rect.top - view.y) / view.scale;
  const xPct = localX / Math.max(1, map.clientWidth) * 100;
  const yPct = localY / Math.max(1, map.clientHeight) * 100;
  const b = currentMapBounds;
  if (xPct < b.left || xPct > b.right || yPct < b.top || yPct > b.bottom) {{
    status.textContent = 'Right-click inside the mapped Dereth area to copy a landloc.';
    return;
  }}

  const x = pctToMapX(xPct, b);
  const y = pctToMapY(yPct, b);
  try {{
    status.textContent = 'Copying cursor landloc...';
    const res = await fetch(`/api/loc?x=${{encodeURIComponent(x.toFixed(4))}}&y=${{encodeURIComponent(y.toFixed(4))}}`, {{ headers: authHeaders(), cache: 'no-store' }});
    const data = await readJsonResponse(res);
    if (!res.ok || data.ok === false) throw new Error(data.error || res.statusText);
    await copyText(data.loc);
    status.textContent = `Copied ${{data.loc}}${{data.map ? ' (' + data.map + ')' : ''}}`;
  }} catch (e) {{
    status.textContent = e.message || 'Could not copy cursor landloc.';
  }}
}}
function iconUrl(did) {{ return did ? `/assets/icon?did=${{encodeURIComponent(did)}}&v=6` : ''; }}
function iconLayer(did, cls) {{ return did ? `<img class=""inventoryIconLayer ${{cls || ''}}"" src=""${{iconUrl(did)}}"" alt="""" loading=""lazy"" decoding=""async"">` : ''; }}
function itemFallback(item) {{
  const name = String(item?.name || item?.weenieClassName || '?').trim();
  const words = name.split(/\s+/).filter(Boolean);
  const text = words.length > 1 ? words.slice(0, 2).map(w => w[0]).join('') : name.slice(0, 2);
  return esc(text || '?');
}}
function selectPlayer(playerGuid) {{
  selectedPlayer = playerIndex[playerGuid] || null;
  if (!selectedPlayer || !isAdminSession) return;
  setSideView('inspect');
  adminPanel.classList.add('open');
  adminPlayerName.textContent = selectedPlayer.name;
  adminPlayerLoc.textContent = selectedPlayer.loc || selectedPlayer.landblock || '';
  document.getElementById('teleLoc').value = selectedPlayer.loc || '';
  document.getElementById('teleCell').value = selectedPlayer.landblock || '';
  document.getElementById('teleX').value = '';
  document.getElementById('teleY').value = '';
  document.getElementById('teleZ').value = '';
  document.querySelectorAll('.player').forEach(row => row.classList.toggle('selected', row.dataset.guid === playerGuid));
}}
async function playerAction(action) {{
  if (!selectedPlayer) {{ status.textContent = 'Select a player first.'; return; }}
  try {{
    status.textContent = `${{action === 'boot' ? 'Booting' : 'Teleporting'}} ${{selectedPlayer.name}}...`;
    const payload = {{
      playerGuid: selectedPlayer.guid,
      action,
      reason: document.getElementById('bootReason').value,
      loc: document.getElementById('teleLoc').value,
      cell: document.getElementById('teleCell').value,
      x: numberOrNull('teleX'),
      y: numberOrNull('teleY'),
      z: numberOrNull('teleZ')
    }};
    const res = await fetch('/api/player/action', {{ method: 'POST', headers: authHeaders({{ 'Content-Type': 'application/json' }}), body: JSON.stringify(payload) }});
    const data = await readJsonResponse(res);
    status.textContent = data.message || data.error || res.statusText;
    if (res.ok && data.ok !== false) refresh();
  }} catch (e) {{
    status.textContent = e.message || 'Player action failed.';
  }}
}}
function watchClass(kind) {{ return String(kind || 'default').toLowerCase().replace(/[^a-z0-9_-]/g, ''); }}
function setWatchPlayer(playerGuid) {{
  watchPlayerGuid = playerGuid;
  watchPanel.classList.toggle('open', !!watchPlayerGuid);
  if (!watchPlayerGuid) {{
    watchTitle.textContent = 'Watching';
    watchMeta.innerHTML = '';
    watchView.querySelectorAll('.watchBlip, .watchHeading').forEach(x => x.remove());
    return;
  }}
  refreshWatch();
}}
async function refreshWatch() {{
  if (!watchPlayerGuid || !authenticated) return;
  try {{
    const res = await fetch('/api/watch?player=' + encodeURIComponent(watchPlayerGuid), {{ headers: authHeaders(), cache: 'no-store' }});
    const data = await readJsonResponse(res);
    if (!res.ok || data.ok === false) throw new Error(data.error || res.statusText);
    renderWatch(data);
  }} catch (e) {{
    status.textContent = e.message || 'Watch view failed.';
    setWatchPlayer(null);
  }}
}}
function renderWatch(data) {{
  const radius = Math.max(1, data.radius || 80);
  watchTitle.textContent = `Watching ${{data.player?.name || 'player'}}`;
  watchView.querySelectorAll('.watchBlip, .watchHeading').forEach(x => x.remove());
  const heading = document.createElement('span');
  heading.className = 'watchHeading';
  heading.style.transform = `translate(-50%, -100%) rotate(${{data.player?.heading || 0}}deg)`;
  watchView.appendChild(heading);

  for (const b of data.blips || []) {{
    const left = 50 + (b.dx / radius) * 50;
    const top = 50 - (b.dy / radius) * 50;
    if (left < -5 || left > 105 || top < -5 || top > 105) continue;
    const dot = document.createElement('span');
    const kind = watchClass(b.kind);
    dot.className = 'watchBlip ' + kind;
    dot.style.left = left + '%';
    dot.style.top = top + '%';
    dot.style.transform = `translate(-50%, -50%) rotate(${{b.heading || 0}}deg)`;
    dot.title = `${{b.name || kind}}\n${{b.loc || b.cell}}\n${{Math.round(b.distance || 0)}}m, z ${{Number(b.z || 0).toFixed(1)}}`;
    if (kind !== 'light' && kind !== 'door') {{
      const label = document.createElement('span');
      label.className = 'watchName';
      label.textContent = b.name || kind;
      dot.appendChild(label);
    }}
    watchView.appendChild(dot);
  }}

  const counts = (data.blips || []).reduce((acc, b) => {{
    const key = b.kind || 'other';
    acc[key] = (acc[key] || 0) + 1;
    return acc;
  }}, {{}});
  watchMeta.innerHTML = `
    <span>${{esc(data.player?.loc || '')}}</span>
    <span>${{fmtNum(radius)}}m radius | ${{fmtNum((data.blips || []).length)}} visible | players ${{fmtNum((counts.player || 0) + (counts.target || 0))}} | NPCs ${{fmtNum((counts.npc || 0) + (counts.vendor || 0))}} | creatures ${{fmtNum(counts.creature || 0)}}</span>
    <span>Updated ${{new Date(data.serverTimeUtc).toLocaleTimeString()}}</span>`;
}}
async function openInventory(playerGuid) {{
  inventoryState.playerGuid = playerGuid;
  inventoryState.selected = null;
  inventoryPanel.classList.add('open');
  inventoryTitle.textContent = 'Inventory';
  inventoryRows.innerHTML = '<div class=""muted"" style=""padding:10px"">Loading...</div>';
  await loadInventory();
}}
async function loadInventory() {{
  if (!inventoryState.playerGuid) return;
  const res = await fetch('/api/inventory?player=' + encodeURIComponent(inventoryState.playerGuid), {{ headers: authHeaders(), cache: 'no-store' }});
  const data = await res.json();
  loginInProgress = false;
  loginButton.disabled = false;
  if (!res.ok || data.ok === false) {{
    inventoryRows.innerHTML = `<div class=""muted"" style=""padding:10px"">${{esc(data.error || res.statusText)}}</div>`;
    return;
  }}
  inventoryState.data = data;
  inventoryTitle.textContent = `${{data.playerName}} Inventory`;
  renderInventoryRows();
  renderInventoryDetails(null);
}}
function inventoryGroupKey(item) {{
  if (item.equipped) return 'Equipped';
  const depth = Number(item.depth || 0);
  const container = String(item.container || 'Main Pack').trim() || 'Main Pack';
  return depth > 0 ? '  '.repeat(Math.min(depth, 4)) + container : container;
}}
function inventorySortValue(item) {{
  return (Number(item.depth || 0) * 1000000) + Number(item.placement || 0);
}}
function renderIconStack(i) {{
  return `<span class=""inventoryIconStack"">
    <span class=""inventoryFallback"">${{itemFallback(i)}}</span>
    ${{iconLayer(i.iconUnderlayId, 'underlay')}}${{iconLayer(i.iconId, 'icon')}}${{iconLayer(i.iconOverlayId, 'overlay')}}
    ${{i.stackSize ? `<span class=""inventoryQty"">${{fmtNum(i.stackSize)}}</span>` : ''}}
  </span>`;
}}
function renderInventoryRows() {{
  const q = inventoryFilter.value.trim().toLowerCase();
  const allItems = inventoryState.data?.items || [];
  const items = allItems.filter(i => {{
    if (!q) return true;
    return [i.name, i.guid, i.weenieClassId, i.weenieClassName, i.weenieType, i.itemType, i.container].some(v => String(v ?? '').toLowerCase().includes(q));
  }});
  if (!items.length) {{
    inventoryRows.innerHTML = '<div class=""inventoryEmpty"">No matching items</div>';
    return;
  }}

  const groupMap = new Map();
  for (const item of items) {{
    const key = inventoryGroupKey(item);
    if (!groupMap.has(key)) groupMap.set(key, []);
    groupMap.get(key).push(item);
  }}

  const orderedGroups = [...groupMap.entries()].sort((a, b) => {{
    if (a[0] === 'Equipped') return -1;
    if (b[0] === 'Equipped') return 1;
    if (a[0].trim() === 'Main Pack') return -1;
    if (b[0].trim() === 'Main Pack') return 1;
    return a[0].localeCompare(b[0]);
  }});

  inventoryRows.innerHTML = orderedGroups.map(([title, group]) => {{
    const sorted = group.slice().sort((a, b) => inventorySortValue(a) - inventorySortValue(b) || String(a.name || '').localeCompare(String(b.name || '')));
    const cleanTitle = title.trim();
    return `<section class=""inventoryBag"">
      <div class=""inventoryBagHeader""><span>${{esc(cleanTitle)}}</span><span class=""inventoryBagMeta"">${{fmtNum(sorted.length)}} item${{sorted.length === 1 ? '' : 's'}}</span></div>
      <div class=""inventoryGrid"">${{sorted.map(i => `
        <button class=""inventorySlot${{inventoryState.selected?.guid === i.guid ? ' selected' : ''}}"" data-guid=""${{esc(i.guid)}}"" title=""${{esc(i.name)}}\n${{esc(i.guid)}}\nWCID ${{esc(i.weenieClassId)}}\n${{esc(i.container || i.wieldedLocation || '')}}"">
          ${{renderIconStack(i)}}
        </button>`).join('')}}</div>
    </section>`;
  }}).join('');

  inventoryRows.querySelectorAll('.inventorySlot').forEach(row => row.onclick = () => {{
    const item = (inventoryState.data?.items || []).find(i => i.guid === row.dataset.guid);
    inventoryState.selected = item;
    renderInventoryRows();
    renderInventoryDetails(item);
  }});
}}
function renderInventoryDetails(item) {{
  const ids = ['editStack', 'editValue', 'editBurden', 'editWorkmanship', 'editName', 'editLongDesc', 'editDamage', 'editDamageMod', 'editDamageVariance', 'editElementalDamageBonus', 'editElementalDamageMod', 'editArmorLevel', 'editStructure', 'editMaxStructure', 'editItemCurMana', 'editItemMaxMana', 'editMaterialType', 'editPaletteTemplate', 'editShade', 'editDamageRating', 'editDamageResistRating', 'editCritDamageRating', 'editCritDamageResistRating', 'editGearDamage', 'editGearDamageResist', 'editGearCritDamage', 'editGearCritDamageResist'];
  if (!item) {{
    inventoryDetails.innerHTML = '<span class=""muted"">Select an item</span>';
    ids.forEach(id => {{ const el = document.getElementById(id); el.value = ''; el.disabled = true; }});
    document.getElementById('inventorySave').disabled = true;
    document.getElementById('inventoryDelete').disabled = true;
    propertyEditor.innerHTML = '';
    return;
  }}
  inventoryDetails.innerHTML = `
    <div class=""inventoryDetailsHead"">${{renderIconStack(item)}}<div><strong>${{esc(item.name)}}</strong><span>${{esc(item.guid)}} | WCID ${{esc(item.weenieClassId)}} | ${{esc(item.weenieType)}} / ${{esc(item.itemType)}}</span><span>${{item.equipped ? 'Equipped: ' + esc(item.wieldedLocation || '') : 'Container: ' + esc(item.container || 'Main Pack')}}</span><span>Value ${{fmtNum(item.value)}} | Burden ${{fmtNum(item.encumbrance)}}${{item.material ? ' | ' + esc(item.material) : ''}}</span></div></div>`;
  const stack = document.getElementById('editStack');
  stack.value = item.stackSize ?? '';
  stack.max = item.maxStackSize ?? '';
  stack.disabled = !item.stackSize;
  document.getElementById('editValue').value = item.value ?? '';
  document.getElementById('editBurden').value = item.encumbrance ?? '';
  document.getElementById('editWorkmanship').value = item.workmanship ?? '';
  document.getElementById('editName').value = item.name ?? '';
  document.getElementById('editLongDesc').value = item.longDesc ?? '';
  document.getElementById('editDamage').value = item.damage ?? '';
  document.getElementById('editDamageMod').value = item.damageMod ?? '';
  document.getElementById('editDamageVariance').value = item.damageVariance ?? '';
  document.getElementById('editElementalDamageBonus').value = item.elementalDamageBonus ?? '';
  document.getElementById('editElementalDamageMod').value = item.elementalDamageMod ?? '';
  document.getElementById('editArmorLevel').value = item.armorLevel ?? '';
  document.getElementById('editStructure').value = item.structure ?? '';
  document.getElementById('editMaxStructure').value = item.maxStructure ?? '';
  document.getElementById('editItemCurMana').value = item.itemCurMana ?? '';
  document.getElementById('editItemMaxMana').value = item.itemMaxMana ?? '';
  document.getElementById('editMaterialType').value = item.materialType ?? '';
  document.getElementById('editPaletteTemplate').value = item.paletteTemplate ?? '';
  document.getElementById('editShade').value = item.shade ?? '';
  document.getElementById('editDamageRating').value = item.damageRating ?? '';
  document.getElementById('editDamageResistRating').value = item.damageResistRating ?? '';
  document.getElementById('editCritDamageRating').value = item.critDamageRating ?? '';
  document.getElementById('editCritDamageResistRating').value = item.critDamageResistRating ?? '';
  document.getElementById('editGearDamage').value = item.gearDamage ?? '';
  document.getElementById('editGearDamageResist').value = item.gearDamageResist ?? '';
  document.getElementById('editGearCritDamage').value = item.gearCritDamage ?? '';
  document.getElementById('editGearCritDamageResist').value = item.gearCritDamageResist ?? '';
  ids.forEach(id => document.getElementById(id).disabled = false);
  document.getElementById('inventorySave').disabled = false;
  document.getElementById('inventoryDelete').disabled = false;
  renderPropertyEditor(item);
}}
function renderPropertyEditor(item) {{
  const groups = item.properties || {{}};
  propertyEditor.innerHTML = Object.entries(groups).map(([family, rows]) => `<details class=""propertyGroup""><summary>${{esc(family)}} (${{rows.length}})</summary><div class=""propertyRows"">${{rows.map(p => `<div class=""propertyRow""><label title=""Property ${{p.key}}"">${{esc(p.name)}} (${{p.key}})</label><input data-family=""${{esc(family)}}"" data-key=""${{p.key}}"" value=""${{esc(p.value)}}""><button class=""smallButton propertySave"">Save</button></div>`).join('')}}</div></details>`).join('');
  propertyEditor.querySelectorAll('.propertySave').forEach(button => button.onclick = () => saveItemProperty(button.previousElementSibling));
}}
async function saveItemProperty(input) {{
  const item = inventoryState.selected;
  if (!item || !isAdminSession) return;
  const payload = {{ playerGuid: inventoryState.playerGuid, itemGuid: item.guid, family: input.dataset.family, key: Number(input.dataset.key), value: input.value }};
  const res = await fetch('/api/inventory/property', {{ method:'POST', headers:authHeaders({{ 'Content-Type':'application/json' }}), body:JSON.stringify(payload) }});
  const data = await readJsonResponse(res);
  if (!res.ok || data.ok === false) {{ status.textContent = data.error || res.statusText; return; }}
  inventoryState.data = data.inventory;
  inventoryState.selected = (data.inventory.items || []).find(i => i.guid === item.guid) || null;
  status.textContent = `Saved ${{input.dataset.family}} property ${{input.dataset.key}}.`;
  renderInventoryDetails(inventoryState.selected);
}}
async function saveInventoryItem() {{
  const item = inventoryState.selected;
  if (!item) return;
  try {{
    status.textContent = `Saving ${{item.name}}...`;
    const payload = {{
      playerGuid: inventoryState.playerGuid,
      itemGuid: item.guid,
      stackSize: item.stackSize ? numOrNull('editStack') : null,
      value: numOrNull('editValue'),
      encumbrance: numOrNull('editBurden'),
      workmanship: numOrNull('editWorkmanship'),
      name: document.getElementById('editName').value,
      longDesc: document.getElementById('editLongDesc').value,
      damage: numOrNull('editDamage'),
      damageMod: numOrNull('editDamageMod'),
      damageVariance: numOrNull('editDamageVariance'),
      elementalDamageBonus: numOrNull('editElementalDamageBonus'),
      elementalDamageMod: numOrNull('editElementalDamageMod'),
      armorLevel: numOrNull('editArmorLevel'),
      structure: numOrNull('editStructure'),
      maxStructure: numOrNull('editMaxStructure'),
      itemCurMana: numOrNull('editItemCurMana'),
      itemMaxMana: numOrNull('editItemMaxMana'),
      materialType: numOrNull('editMaterialType'),
      paletteTemplate: numOrNull('editPaletteTemplate'),
      shade: numOrNull('editShade'),
      damageRating: numOrNull('editDamageRating'),
      damageResistRating: numOrNull('editDamageResistRating'),
      critDamageRating: numOrNull('editCritDamageRating'),
      critDamageResistRating: numOrNull('editCritDamageResistRating'),
      gearDamage: numOrNull('editGearDamage'),
      gearDamageResist: numOrNull('editGearDamageResist'),
      gearCritDamage: numOrNull('editGearCritDamage'),
      gearCritDamageResist: numOrNull('editGearCritDamageResist')
    }};
    const res = await fetch('/api/inventory/item', {{ method: 'POST', headers: authHeaders({{ 'Content-Type': 'application/json' }}), body: JSON.stringify(payload) }});
    const data = await readJsonResponse(res);
    if (!res.ok || data.ok === false) {{ status.textContent = data.error || res.statusText; return; }}
    inventoryState.data = data.inventory;
    inventoryState.selected = (inventoryState.data.items || []).find(i => i.guid === item.guid) || null;
    status.textContent = `Saved ${{item.name}}.`;
    renderInventoryRows();
    renderInventoryDetails(inventoryState.selected);
  }} catch (e) {{
    status.textContent = e.message || 'Save item failed.';
  }}
}}
async function deleteInventoryItem() {{
  const item = inventoryState.selected;
  if (!item || !confirm(`Delete ${{item.name}} from ${{inventoryState.data?.playerName || 'player'}}?`)) return;
  const res = await fetch('/api/inventory/delete', {{ method: 'POST', headers: authHeaders({{ 'Content-Type': 'application/json' }}), body: JSON.stringify({{ playerGuid: inventoryState.playerGuid, itemGuid: item.guid }}) }});
  const data = await res.json();
  loginInProgress = false;
  loginButton.disabled = false;
  if (!res.ok || data.ok === false) {{ status.textContent = data.error || res.statusText; return; }}
  inventoryState.data = data.inventory;
  inventoryState.selected = null;
  renderInventoryRows();
  renderInventoryDetails(null);
}}
document.getElementById('inventoryClose').onclick = () => inventoryPanel.classList.remove('open');
document.getElementById('inventoryRefresh').onclick = loadInventory;
document.getElementById('inventoryClear').onclick = () => {{ inventoryFilter.value = ''; renderInventoryRows(); }};
document.getElementById('inventorySave').onclick = saveInventoryItem;
document.getElementById('inventoryDelete').onclick = deleteInventoryItem;
document.getElementById('teleportButton').onclick = () => playerAction('teleport');
document.getElementById('bootButton').onclick = () => playerAction('boot');
document.getElementById('watchButton').onclick = () => selectedPlayer ? setWatchPlayer(selectedPlayer.guid) : status.textContent = 'Select a player first.';
document.getElementById('watchStop').onclick = () => setWatchPlayer(null);
inventoryFilter.addEventListener('input', renderInventoryRows);
document.getElementById('zoomIn').onclick = () => zoomAt(1.25, map.clientWidth / 2, map.clientHeight / 2);
document.getElementById('zoomOut').onclick = () => zoomAt(0.8, map.clientWidth / 2, map.clientHeight / 2);
document.getElementById('zoomReset').onclick = resetView;
document.getElementById('zoomFit').onclick = resetView;
map.addEventListener('contextmenu', e => {{
  if (e.target.closest('button, input, textarea, select, aside, .bottomDock, .inventoryPanel')) return;
  e.preventDefault();
  copyMapLocAt(e);
}});
map.addEventListener('wheel', e => {{
  if (!mapLayer) return;
  e.preventDefault();
  const rect = map.getBoundingClientRect();
  zoomAt(e.deltaY < 0 ? 1.18 : 0.85, e.clientX - rect.left, e.clientY - rect.top);
}}, {{ passive: false }});
map.addEventListener('pointerdown', e => {{
  if (!mapLayer || e.button !== 0 || e.target.closest('button')) return;
  dragging = true;
  dragStart = {{ x: e.clientX, y: e.clientY, vx: view.x, vy: view.y }};
  map.setPointerCapture(e.pointerId);
}});
map.addEventListener('pointermove', e => {{
  if (!dragging || !dragStart) return;
  view.x = dragStart.vx + e.clientX - dragStart.x;
  view.y = dragStart.vy + e.clientY - dragStart.y;
  applyView();
}});
map.addEventListener('pointerup', e => {{ dragging = false; dragStart = null; try {{ map.releasePointerCapture(e.pointerId); }} catch {{}} }});
map.addEventListener('pointercancel', () => {{ dragging = false; dragStart = null; }});
async function load() {{
  const modeChanged = currentMode !== 'world';
  currentDungeon = null;
  currentMode = 'world';
  try {{
    const headers = token.value ? {{ 'X-DerpACE-Map-Token': token.value }} : {{}};
    const res = await fetch('/api/players', {{ headers, cache: 'no-store' }});
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || res.statusText);
    clearMap();
    map.classList.remove('dungeonMode');
    map.classList.remove('hasImage');
    map.style.backgroundImage = '';
    currentMapBounds = data.mapBounds;
    const layer = createLayer('worldLayer');
    if (data.mapImageUrl) {{
      map.classList.add('hasImage');
      const image = document.createElement('div');
      image.className = 'worldMapImage';
      const imageUrl = token.value ? `${{data.mapImageUrl}}?token=${{encodeURIComponent(token.value)}}` : data.mapImageUrl;
      image.style.backgroundImage = `url('${{imageUrl}}')`;
      layer.appendChild(image);
    }}
    if (modeChanged) resetView();
    list.innerHTML = '';
    playerIndex = Object.fromEntries((data.players || []).map(p => [p.guid, p]));
    renderDock(data);
    const blips = data.blips || [];
    status.textContent = `${{data.onlineCount}} visible online player${{data.onlineCount === 1 ? '' : 's'}}, ${{blips.length}} nearby radar blip${{blips.length === 1 ? '' : 's'}} - updated ${{new Date(data.serverTimeUtc).toLocaleTimeString()}}`;
    for (const b of blips) {{
      if (b.mapX !== null && b.mapY !== null) addBlip(layer, b, mapPctX(b.mapX, data.mapBounds), mapPctY(b.mapY, data.mapBounds));
    }}
    for (const p of data.players) {{
      if (p.mapX !== null && p.mapY !== null) {{
        const pin = document.createElement('button');
        pin.className = 'pin' + (p.isIndoors ? ' indoor' : '');
        pin.style.left = mapPctX(p.mapX, data.mapBounds) + '%';
        pin.style.top = mapPctY(p.mapY, data.mapBounds) + '%';
        pin.dataset.name = p.name;
        pin.title = `${{p.name}}\n${{p.loc || 'indoors'}}\n${{p.landblock}}`;
        pin.onclick = () => selectPlayer(p.guid);
        layer.appendChild(pin);
      }}
      const item = document.createElement('div');
      item.className = 'player';
      item.dataset.guid = p.guid;
      item.dataset.search = `${{p.name}} ${{p.loc || ''}} ${{p.landblock || ''}}`.toLowerCase();
      item.innerHTML = `<strong>${{esc(p.name)}}</strong><div class=""muted"">${{esc(p.loc || 'Indoor/dungeon')}} | ${{esc(p.landblock)}}</div><div class=""inventoryActions""><button class=""smallButton watchRowButton"" data-guid=""${{esc(p.guid)}}"">Watch</button><button class=""smallButton invButton"" data-guid=""${{esc(p.guid)}}"">Inventory</button></div><div class=""bars"">${{bar(p.health,p.maxHealth,'health')}}${{bar(p.stamina,p.maxStamina,'stamina')}}${{bar(p.mana,p.maxMana,'mana')}}</div>`;
      item.onclick = () => selectPlayer(p.guid);
      item.ondblclick = () => {{ if (p.isIndoors) loadDungeon(p.landblock); }};
      item.querySelector('.watchRowButton').onclick = e => {{ e.stopPropagation(); selectPlayer(p.guid); setWatchPlayer(p.guid); }};
      const invButton = item.querySelector('.invButton');
      if (invButton && (isAdminSession || p.isOwnedBySession)) invButton.onclick = e => {{ e.stopPropagation(); openInventory(p.guid); }};
      else if (invButton) invButton.remove();
      list.appendChild(item);
    }}
      applyPlayerFilter();
  }} catch (e) {{
    status.textContent = e.message;
    if (/login|required|unauthorized|invalid/i.test(e.message || '')) setAuthenticated(false);
  }}
}}
async function loadDungeon(landblock) {{
  const modeChanged = currentMode !== 'dungeon' || currentDungeon !== landblock;
  currentDungeon = landblock;
  currentMode = 'dungeon';
  try {{
    const headers = token.value ? {{ 'X-DerpACE-Map-Token': token.value }} : {{}};
    const res = await fetch('/api/dungeon?landblock=' + encodeURIComponent(landblock), {{ headers, cache: 'no-store' }});
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || res.statusText);
    clearMap();
    map.classList.add('dungeonMode');
    map.classList.remove('hasImage');
    map.style.backgroundImage = '';
    currentMapBounds = null;
    if (!data.generated) throw new Error(data.error || 'No dungeon geometry for ' + landblock);
    bottomDock.innerHTML = '';
    playerIndex = Object.fromEntries((data.players || []).map(p => [p.guid, {{ guid: p.guid, name: p.name, loc: p.loc, landblock: p.cell }}]));
    const layer = createLayer('dungeonLayer');
    const wrap = document.createElement('div');
    wrap.className = 'dungeonSvg';
    wrap.innerHTML = data.svg;
    layer.appendChild(wrap);
    if (modeChanged) resetView();
    for (const b of data.blips || []) {{
      addBlip(layer, b, dungeonPctX(b.x, data), dungeonPctY(b.y, data));
    }}
    for (const p of data.players) {{
      const pin = document.createElement('button');
      pin.className = 'dungeonPin';
      pin.style.left = dungeonPctX(p.x, data) + '%';
      pin.style.top = dungeonPctY(p.y, data) + '%';
      pin.dataset.name = p.name;
      pin.title = `${{p.name}}\n${{p.loc || p.cell}}\n${{p.cell}}\nmap xy ${{p.x.toFixed(3)}}, ${{p.y.toFixed(3)}}\nbounds x ${{data.minX.toFixed(3)}}..${{data.maxX.toFixed(3)}} y ${{data.minY.toFixed(3)}}..${{data.maxY.toFixed(3)}}`;
      pin.onclick = () => selectPlayer(p.guid);
      layer.appendChild(pin);
    }}
    applyView();
    const blips = data.blips || [];
    status.textContent = `${{data.players.length}} visible player${{data.players.length === 1 ? '' : 's'}}, ${{blips.length}} nearby radar blip${{blips.length === 1 ? '' : 's'}} in ${{data.landblock}} | z ${{data.minZ.toFixed(1)}}..${{data.maxZ.toFixed(1)}}`;
  }} catch (e) {{
    status.textContent = e.message;
    if (/login|required|unauthorized|invalid/i.test(e.message || '')) setAuthenticated(false);
  }}
}}
function refresh() {{ currentDungeon ? loadDungeon(currentDungeon) : load(); refreshWatch(); }}
setSideView('players');
document.querySelectorAll('[data-side-tab]').forEach(button => button.onclick = () => setSideView(button.dataset.sideTab));
playerSearch?.addEventListener('input', applyPlayerFilter);
document.getElementById('refreshButton').onclick = refresh;
document.getElementById('worldButton').onclick = () => load();
document.getElementById('sidebarButton').onclick = () => document.body.classList.toggle('sidebarCollapsed');
checkSession();
</script>
</body>
</html>";
        }

        private sealed class AdminMapSnapshot
        {
            public DateTime ServerTimeUtc { get; set; }
            public int RefreshSeconds { get; set; }
            public int OnlineCount { get; set; }
            public string MapImageUrl { get; set; }
            public AdminMapBounds MapBounds { get; set; }
            public List<AdminMapPlayer> Players { get; set; }
            public List<AdminMapBlip> Blips { get; set; }
            public AdminMapStats Stats { get; set; }
            public AdminMapFeeds Feeds { get; set; }
        }

        private sealed class AdminSpellWorkshopSaveRequest
        {
            public string FileName { get; set; }
            public string Json { get; set; }
        }
        private sealed class AdminBossProfileSummary
        {
            public string Profile { get; set; }
            public uint WeenieClassId { get; set; }
            public string BossName { get; set; }
            public int DraftRevision { get; set; }
            public int PublishedRevision { get; set; }
            public int PreviousRevision { get; set; }
            public bool Enabled { get; set; }
            public string ModifiedBy { get; set; }
            public DateTime ModifiedAt { get; set; }
            public bool IsTemplate { get; set; }
            public bool HasWcidConflict { get; set; }
            public string TemplateError { get; set; }
        }

        private sealed class AdminBossFileTemplate
        {
            public string ProfileName { get; set; }
            public uint WeenieClassId { get; set; }
            public int DraftRevision { get; set; }
            public string DraftJson { get; set; }
            public int PublishedRevision { get; set; }
            public string PublishedJson { get; set; }
            public bool Enabled { get; set; }
            public string SourceFile { get; set; }
            public DateTime ModifiedAt { get; set; }
            public string Error { get; set; }
        }
        private sealed class AdminBossProfileRequest
        {
            public string Action { get; set; }
            public string Profile { get; set; }
            public uint WeenieClassId { get; set; }
            public string Json { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class AdminBossSpawnRequest
        {
            public string Profile { get; set; }
            public string PlayerGuid { get; set; }
            public string Loc { get; set; }
            public int Count { get; set; }
            public float Distance { get; set; }
        }

        private sealed class AdminBossDespawnRequest
        {
            public string Guid { get; set; }
        }
        private sealed class AdminBossDraftRequest
        {
            public string Profile { get; set; }
            public string Json { get; set; }
        }
        private sealed class AdminMapLoginRequest
        {
            public string Account { get; set; }
            public string Password { get; set; }
        }

        private sealed class AdminMapSession
        {
            public uint AccountId { get; set; }
            public string AccountName { get; set; }
            public AccessLevel AccessLevel { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private sealed class AdminMapBounds
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Right { get; set; }
            public float Bottom { get; set; }
        }

        private sealed class AdminMapPlayer
        {
            public string Name { get; set; }
            public bool IsOwnedBySession { get; set; }
            public string Guid { get; set; }
            public string Landblock { get; set; }
            public string Loc { get; set; }
            public bool IsIndoors { get; set; }
            public float? MapX { get; set; }
            public float? MapY { get; set; }
            public float WorldX { get; set; }
            public float WorldY { get; set; }
            public float Z { get; set; }
            public double Heading { get; set; }
            public uint Health { get; set; }
            public uint MaxHealth { get; set; }
            public uint Stamina { get; set; }
            public uint MaxStamina { get; set; }
            public uint Mana { get; set; }
            public uint MaxMana { get; set; }
        }

        private sealed class AdminMapBlip
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Cell { get; set; }
            public string Landblock { get; set; }
            public string Loc { get; set; }
            public string Kind { get; set; }
            public string RadarColor { get; set; }
            public bool IsMonster { get; set; }
            public float? MapX { get; set; }
            public float? MapY { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }

        private sealed class AdminWatchSnapshot
        {
            public bool Ok { get; set; }
            public DateTime ServerTimeUtc { get; set; }
            public float Radius { get; set; }
            public AdminMapPlayer Player { get; set; }
            public List<AdminWatchBlip> Blips { get; set; }
        }

        private sealed class AdminWatchBlip
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Cell { get; set; }
            public string Loc { get; set; }
            public string Kind { get; set; }
            public string RadarColor { get; set; }
            public bool IsMonster { get; set; }
            public float Dx { get; set; }
            public float Dy { get; set; }
            public float Dz { get; set; }
            public float Distance { get; set; }
            public double Heading { get; set; }
            public float Z { get; set; }
        }

        private sealed class AdminMapStats
        {
            public int OnlineCount { get; set; }
            public int UniqueIpCount { get; set; }
            public int HardcoreOnlineCount { get; set; }
            public int IronmanOnlineCount { get; set; }
            public AdminLeaderboardEntry HardcoreLeader { get; set; }
            public AdminLeaderboardEntry IronmanLeader { get; set; }
            public AdminLeaderboardEntry DeadliestNormal { get; set; }
            public AdminLeaderboardEntry DeadliestHardcore { get; set; }
            public AdminLeaderboardEntry DeadliestIronman { get; set; }
        }

        private sealed class AdminLeaderboardEntry
        {
            public string Name { get; set; }
            public int Level { get; set; }
            public int Kills { get; set; }
            public int Lives { get; set; }
        }

        private sealed class AdminMapFeeds
        {
            public List<AdminChatFeedEntry> Chat { get; set; }
            public List<AdminRareFeedEntry> Rares { get; set; }
        }

        private sealed class AdminChatFeedEntry
        {
            public DateTime Utc { get; set; }
            public string Channel { get; set; }
            public string Sender { get; set; }
            public string Message { get; set; }
        }

        private sealed class AdminRareFeedEntry
        {
            public DateTime Utc { get; set; }
            public string Player { get; set; }
            public string Item { get; set; }
            public uint WeenieClassId { get; set; }
            public int Tier { get; set; }
            public int Chance { get; set; }
            public int Luck { get; set; }
            public string Corpse { get; set; }
            public string Location { get; set; }
            public string Landblock { get; set; }
        }

        private sealed class AdminIconCacheEntry
        {
            public byte[] Bytes { get; set; }
            public long LastWriteUtcTicks { get; set; }
            public long Length { get; set; }
        }
        private sealed class AdminInventorySnapshot
        {
            public bool Ok { get; set; } = true;
            public string Error { get; set; }
            public string PlayerName { get; set; }
            public string PlayerGuid { get; set; }
            public int Encumbrance { get; set; }
            public int CoinValue { get; set; }
            public bool Editable { get; set; }
            public List<AdminInventoryItem> Items { get; set; }

            public static AdminInventorySnapshot Fail(string error)
            {
                return new AdminInventorySnapshot { Ok = false, Error = error, Items = new List<AdminInventoryItem>() };
            }
        }

        private sealed class AdminInventoryItem
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public uint GuidValue { get; set; }
            public uint WeenieClassId { get; set; }
            public string WeenieClassName { get; set; }
            public string WeenieType { get; set; }
            public string ItemType { get; set; }
            public uint IconId { get; set; }
            public uint? IconOverlayId { get; set; }
            public uint? IconUnderlayId { get; set; }
            public string Container { get; set; }
            public string ContainerGuid { get; set; }
            public bool Equipped { get; set; }
            public int Depth { get; set; }
            public int Placement { get; set; }
            public int? StackSize { get; set; }
            public ushort? MaxStackSize { get; set; }
            public int? Value { get; set; }
            public int? Encumbrance { get; set; }
            public int? Workmanship { get; set; }
            public string LongDesc { get; set; }
            public string Material { get; set; }
            public int? MaterialType { get; set; }
            public int? PaletteTemplate { get; set; }
            public double? Shade { get; set; }
            public int? Damage { get; set; }
            public double? DamageMod { get; set; }
            public double? DamageVariance { get; set; }
            public int? ElementalDamageBonus { get; set; }
            public double? ElementalDamageMod { get; set; }
            public int? ArmorLevel { get; set; }
            public ushort? Structure { get; set; }
            public ushort? MaxStructure { get; set; }
            public int? ItemCurMana { get; set; }
            public int? ItemMaxMana { get; set; }
            public int? DamageRating { get; set; }
            public int? DamageResistRating { get; set; }
            public int? CritDamageRating { get; set; }
            public int? CritDamageResistRating { get; set; }
            public int? GearDamage { get; set; }
            public int? GearDamageResist { get; set; }
            public int? GearCritDamage { get; set; }
            public int? GearCritDamageResist { get; set; }
            public string WieldedLocation { get; set; }
            public bool IsContainer { get; set; }
            public bool IsAttuned { get; set; }
            public bool IsBonded { get; set; }
            public Dictionary<string, List<AdminItemProperty>> Properties { get; set; }
        }

        private sealed class AdminItemProperty
        {
            public uint Key { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }

        private sealed class AdminInventoryPropertyEditRequest
        {
            public string PlayerGuid { get; set; }
            public string ItemGuid { get; set; }
            public string Family { get; set; }
            public uint Key { get; set; }
            public string Value { get; set; }
        }
        private sealed class AdminInventoryItemEditRequest
        {
            public string PlayerGuid { get; set; }
            public string ItemGuid { get; set; }
            public int? StackSize { get; set; }
            public int? Value { get; set; }
            public int? Encumbrance { get; set; }
            public int? Workmanship { get; set; }
            public string Name { get; set; }
            public string LongDesc { get; set; }
            public int? MaterialType { get; set; }
            public int? PaletteTemplate { get; set; }
            public double? Shade { get; set; }
            public int? Damage { get; set; }
            public double? DamageMod { get; set; }
            public double? DamageVariance { get; set; }
            public int? ElementalDamageBonus { get; set; }
            public double? ElementalDamageMod { get; set; }
            public int? ArmorLevel { get; set; }
            public int? Structure { get; set; }
            public int? MaxStructure { get; set; }
            public int? ItemCurMana { get; set; }
            public int? ItemMaxMana { get; set; }
            public int? DamageRating { get; set; }
            public int? DamageResistRating { get; set; }
            public int? CritDamageRating { get; set; }
            public int? CritDamageResistRating { get; set; }
            public int? GearDamage { get; set; }
            public int? GearDamageResist { get; set; }
            public int? GearCritDamage { get; set; }
            public int? GearCritDamageResist { get; set; }
        }

        private sealed class AdminInventoryItemDeleteRequest
        {
            public string PlayerGuid { get; set; }
            public string ItemGuid { get; set; }
        }

        private sealed class AdminPlayerActionRequest
        {
            public string PlayerGuid { get; set; }
            public string Action { get; set; }
            public string Reason { get; set; }
            public string Loc { get; set; }
            public string Cell { get; set; }
            public float? X { get; set; }
            public float? Y { get; set; }
            public float? Z { get; set; }
            public float? Qw { get; set; }
            public float? Qx { get; set; }
            public float? Qy { get; set; }
            public float? Qz { get; set; }
        }

        private sealed class AdminDungeonSnapshot
        {
            public string Landblock { get; set; }
            public bool Generated { get; set; }
            public string Error { get; set; }
            public float MinX { get; set; }
            public float MinY { get; set; }
            public float MaxX { get; set; }
            public float MaxY { get; set; }
            public float MinZ { get; set; }
            public float MaxZ { get; set; }
            public string Svg { get; set; }
            public List<AdminDungeonPlayer> Players { get; set; }
            public List<AdminMapBlip> Blips { get; set; }
        }

        private sealed class AdminDungeonMap
        {
            public bool Generated { get; set; }
            public string Error { get; set; }
            public float MinX { get; set; }
            public float MinY { get; set; }
            public float MaxX { get; set; }
            public float MaxY { get; set; }
            public float MinZ { get; set; }
            public float MaxZ { get; set; }
            public string Svg { get; set; }

            public static AdminDungeonMap Fail(string error)
            {
                return new AdminDungeonMap { Generated = false, Error = error, Svg = "" };
            }
        }

        private sealed class AdminDungeonPlayer
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Cell { get; set; }
            public string Loc { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public double Heading { get; set; }
            public uint Health { get; set; }
            public uint MaxHealth { get; set; }
            public uint Stamina { get; set; }
            public uint MaxStamina { get; set; }
            public uint Mana { get; set; }
            public uint MaxMana { get; set; }
        }
    }
}
