using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using log4net;

using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

using WeenieTypeEnum = ACE.Entity.Enum.WeenieType;


namespace ACE.Server.Command.Handlers
{
    public static class PlayerCommands
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // pop
        [CommandHandler("pop", AccessLevel.Player, CommandHandlerFlag.None, 0,
            "Show current world population",
            "")]
        public static void HandlePop(Session session, params string[] parameters)
        {
            CommandHandlerHelper.WriteOutputInfo(session, $"Current world population: {PlayerManager.GetOnlineCount():N0}", ChatMessageType.Broadcast);
        }

        // quest info (uses GDLe formatting to match plugin expectations)
        [CommandHandler("myquests", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows your quest log")]
        public static void HandleQuests(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("quest_info_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"myquests\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            var quests = session.Player.QuestManager.GetQuests();

            if (quests.Count == 0)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Quest list is empty.", ChatMessageType.Broadcast));
                return;
            }

            foreach (var playerQuest in quests)
            {
                var text = "";
                var questName = QuestManager.GetQuestName(playerQuest.QuestName);
                var quest = DatabaseManager.World.GetCachedQuest(questName);
                if (quest == null)
                {
                    //Console.WriteLine($"Couldn't find quest {playerQuest.QuestName}");
                    continue;
                }

                var minDelta = quest.MinDelta;
                if (QuestManager.CanScaleQuestMinDelta(quest))
                    minDelta = (uint)(quest.MinDelta * PropertyManager.GetDouble("quest_mindelta_rate").Item);

                text += $"{playerQuest.QuestName.ToLower()} - {playerQuest.NumTimesCompleted} solves ({playerQuest.LastTimeCompleted})";
                text += $"\"{quest.Message}\" {quest.MaxSolves} {minDelta}";

                session.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.Broadcast));
            }
        }

        /// <summary>
        /// For characters/accounts who currently own multiple houses, used to select which house they want to keep
        /// </summary>
        [CommandHandler("house-select", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1, "For characters/accounts who currently own multiple houses, used to select which house they want to keep")]
        public static void HandleHouseSelect(Session session, params string[] parameters)
        {
            HandleHouseSelect(session, false, parameters);
        }

        public static void HandleHouseSelect(Session session, bool confirmed, params string[] parameters)
        {
            if (!int.TryParse(parameters[0], out var houseIdx))
                return;

            // ensure current multihouse owner
            if (!session.Player.IsMultiHouseOwner(false))
            {
                log.Warn($"{session.Player.Name} tried to /house-select {houseIdx}, but they are not currently a multi-house owner!");
                return;
            }

            // get house info for this index
            var multihouses = session.Player.GetMultiHouses();

            if (houseIdx < 1 || houseIdx > multihouses.Count)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Please enter a number between 1 and {multihouses.Count}.", ChatMessageType.Broadcast));
                return;
            }

            var keepHouse = multihouses[houseIdx - 1];

            // show confirmation popup
            if (!confirmed)
            {
                var houseType = $"{keepHouse.HouseType}".ToLower();
                var loc = HouseManager.GetCoords(keepHouse.SlumLord.Location);

                var msg = $"Are you sure you want to keep the {houseType} at\n{loc}?";
                if (!session.Player.ConfirmationManager.EnqueueSend(new Confirmation_Custom(session.Player.Guid, () => HandleHouseSelect(session, true, parameters)), msg))
                    session.Player.SendWeenieError(WeenieError.ConfirmationInProgress);
                return;
            }

            // house to keep confirmed, abandon the other houses
            var abandonHouses = new List<House>(multihouses);
            abandonHouses.RemoveAt(houseIdx - 1);

            foreach (var abandonHouse in abandonHouses)
            {
                var house = session.Player.GetHouse(abandonHouse.Guid.Full);

                HouseManager.HandleEviction(house, house.HouseOwner ?? 0, true);
            }

            // set player properties for house to keep
            var player = PlayerManager.FindByGuid(keepHouse.HouseOwner ?? 0, out bool isOnline);
            if (player == null)
            {
                log.Error($"{session.Player.Name}.HandleHouseSelect({houseIdx}) - couldn't find HouseOwner {keepHouse.HouseOwner} for {keepHouse.Name} ({keepHouse.Guid})");
                return;
            }

            player.HouseId = keepHouse.HouseId;
            player.HouseInstance = keepHouse.Guid.Full;

            player.SaveBiotaToDatabase();

            // update house panel for current player
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(3.0f);  // wait for slumlord inventory biotas above to save
            actionChain.AddAction(session.Player, session.Player.HandleActionQueryHouse);
            actionChain.EnqueueChain();

            Console.WriteLine("OK");
        }

        [CommandHandler("debugcast", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows debug information about the current magic casting state")]
        public static void HandleDebugCast(Session session, params string[] parameters)
        {
            var physicsObj = session.Player.PhysicsObj;

            var pendingActions = physicsObj.MovementManager.MoveToManager.PendingActions;
            var currAnim = physicsObj.PartArray.Sequence.CurrAnim;

            session.Network.EnqueueSend(new GameMessageSystemChat(session.Player.MagicState.ToString(), ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"IsMovingOrAnimating: {physicsObj.IsMovingOrAnimating}", ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"PendingActions: {pendingActions.Count}", ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"CurrAnim: {currAnim?.Value.Anim.ID:X8}", ChatMessageType.Broadcast));
        }

        [CommandHandler("fixcast", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Fixes magic casting if locked up for an extended time")]
        public static void HandleFixCast(Session session, params string[] parameters)
        {
            var magicState = session.Player.MagicState;

            if (magicState.IsCasting && DateTime.UtcNow - magicState.StartTime > TimeSpan.FromSeconds(5))
            {
                session.Network.EnqueueSend(new GameEventCommunicationTransientString(session, "Fixed casting state"));
                session.Player.SendUseDoneEvent();
                magicState.OnCastDone();
            }
        }

        [CommandHandler("castmeter", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows the fast casting efficiency meter")]
        public static void HandleCastMeter(Session session, params string[] parameters)
        {
            if (parameters.Length == 0)
            {
                session.Player.MagicState.CastMeter = !session.Player.MagicState.CastMeter;
            }
            else
            {
                if (parameters[0].Equals("on", StringComparison.OrdinalIgnoreCase))
                    session.Player.MagicState.CastMeter = true;
                else
                    session.Player.MagicState.CastMeter = false;
            }
            session.Network.EnqueueSend(new GameMessageSystemChat($"Cast efficiency meter {(session.Player.MagicState.CastMeter ? "enabled" : "disabled")}", ChatMessageType.Broadcast));
        }

        private static List<string> configList = new List<string>()
        {
            "Common settings:\nConfirmVolatileRareUse, MainPackPreferred, SalvageMultiple, SideBySideVitals, UseCraftSuccessDialog",
            "Interaction settings:\nAcceptLootPermits, AllowGive, AppearOffline, AutoAcceptFellowRequest, DragItemOnPlayerOpensSecureTrade, FellowshipShareLoot, FellowshipShareXP, IgnoreAllegianceRequests, IgnoreFellowshipRequests, IgnoreTradeRequests, UseDeception",
            "UI settings:\nCoordinatesOnRadar, DisableDistanceFog, DisableHouseRestrictionEffects, DisableMostWeatherEffects, FilterLanguage, LockUI, PersistentAtDay, ShowCloak, ShowHelm, ShowTooltips, SpellDuration, TimeStamp, ToggleRun, UseMouseTurning",
            "Chat settings:\nHearAllegianceChat, HearGeneralChat, HearLFGChat, HearRoleplayChat, HearSocietyChat, HearTradeChat, HearPKDeaths, StayInChatMode",
            "Combat settings:\nAdvancedCombatUI, AutoRepeatAttack, AutoTarget, LeadMissileTargets, UseChargeAttack, UseFastMissiles, ViewCombatTarget, VividTargetingIndicator",
            "Character display settings:\nDisplayAge, DisplayAllegianceLogonNotifications, DisplayChessRank, DisplayDateOfBirth, DisplayFishingSkill, DisplayNumberCharacterTitles, DisplayNumberDeaths"
        };

        /// <summary>
        /// Mapping of GDLE -> ACE CharacterOptions
        /// </summary>
        private static Dictionary<string, string> translateOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common
            { "ConfirmVolatileRareUse", "ConfirmUseOfRareGems" },
            { "MainPackPreferred", "UseMainPackAsDefaultForPickingUpItems" },
            { "SalvageMultiple", "SalvageMultipleMaterialsAtOnce" },
            { "SideBySideVitals", "SideBySideVitals" },
            { "UseCraftSuccessDialog", "UseCraftingChanceOfSuccessDialog" },

            // Interaction
            { "AcceptLootPermits", "AcceptCorpseLootingPermissions" },
            { "AllowGive", "LetOtherPlayersGiveYouItems" },
            { "AppearOffline", "AppearOffline" },
            { "AutoAcceptFellowRequest", "AutomaticallyAcceptFellowshipRequests" },
            { "DragItemOnPlayerOpensSecureTrade", "DragItemToPlayerOpensTrade" },
            { "FellowshipShareLoot", "ShareFellowshipLoot" },
            { "FellowshipShareXP", "ShareFellowshipExpAndLuminance" },
            { "IgnoreAllegianceRequests", "IgnoreAllegianceRequests" },
            { "IgnoreFellowshipRequests", "IgnoreFellowshipRequests" },
            { "IgnoreTradeRequests", "IgnoreAllTradeRequests" },
            { "UseDeception", "AttemptToDeceiveOtherPlayers" },

            // UI
            { "CoordinatesOnRadar", "ShowCoordinatesByTheRadar" },
            { "DisableDistanceFog", "DisableDistanceFog" },
            { "DisableHouseRestrictionEffects", "DisableHouseRestrictionEffects" },
            { "DisableMostWeatherEffects", "DisableMostWeatherEffects" },
            { "FilterLanguage", "FilterLanguage" },
            { "LockUI", "LockUI" },
            { "PersistentAtDay", "AlwaysDaylightOutdoors" },
            { "ShowCloak", "ShowYourCloak" },
            { "ShowHelm", "ShowYourHelmOrHeadGear" },
            { "ShowTooltips", "Display3dTooltips" },
            { "SpellDuration", "DisplaySpellDurations" },
            { "TimeStamp", "DisplayTimestamps" },
            { "ToggleRun", "RunAsDefaultMovement" },
            { "UseMouseTurning", "UseMouseTurning" },

            // Chat
            { "HearAllegianceChat", "ListenToAllegianceChat" },
            { "HearGeneralChat", "ListenToGeneralChat" },
            { "HearLFGChat", "ListenToLFGChat" },
            { "HearRoleplayChat", "ListentoRoleplayChat" },
            { "HearSocietyChat", "ListenToSocietyChat" },
            { "HearTradeChat", "ListenToTradeChat" },
            { "HearPKDeaths", "ListenToPKDeathMessages" },
            { "StayInChatMode", "StayInChatModeAfterSendingMessage" },

            // Combat
            { "AdvancedCombatUI", "AdvancedCombatInterface" },
            { "AutoRepeatAttack", "AutoRepeatAttacks" },
            { "AutoTarget", "AutoTarget" },
            { "LeadMissileTargets", "LeadMissileTargets" },
            { "UseChargeAttack", "UseChargeAttack" },
            { "UseFastMissiles", "UseFastMissiles" },
            { "ViewCombatTarget", "KeepCombatTargetsInView" },
            { "VividTargetingIndicator", "VividTargetingIndicator" },

            // Character Display
            { "DisplayAge", "AllowOthersToSeeYourAge" },
            { "DisplayAllegianceLogonNotifications", "ShowAllegianceLogons" },
            { "DisplayChessRank", "AllowOthersToSeeYourChessRank" },
            { "DisplayDateOfBirth", "AllowOthersToSeeYourDateOfBirth" },
            { "DisplayFishingSkill", "AllowOthersToSeeYourFishingSkill" },
            { "DisplayNumberCharacterTitles", "AllowOthersToSeeYourNumberOfTitles" },
            { "DisplayNumberDeaths", "AllowOthersToSeeYourNumberOfDeaths" },
        };

        /// <summary>
        /// Manually sets a character option on the server. Use /config list to see a list of settings.
        /// </summary>
        [CommandHandler("config", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1, "Manually sets a character option on the server.\nUse /config list to see a list of settings.", "<setting> <on/off>")]
        public static void HandleConfig(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("player_config_command").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"config\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            // /config list - show character options
            if (parameters[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in configList)
                    session.Network.EnqueueSend(new GameMessageSystemChat(line, ChatMessageType.Broadcast));

                return;
            }

            // translate GDLE CharacterOptions for existing plugins
            if (!translateOptions.TryGetValue(parameters[0], out var param) || !Enum.TryParse(param, out CharacterOption characterOption))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Unknown character option: {parameters[0]}", ChatMessageType.Broadcast));
                return;
            }

            var option = session.Player.GetCharacterOption(characterOption);

            // modes of operation:
            // on / off / toggle

            // - if none specified, default to toggle
            var mode = "toggle";

            if (parameters.Length > 1)
            {
                if (parameters[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                    mode = "on";
                else if (parameters[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                    mode = "off";
            }

            // set character option
            if (mode.Equals("on"))
                option = true;
            else if (mode.Equals("off"))
                option = false;
            else
                option = !option;

            session.Player.SetCharacterOption(characterOption, option);

            session.Network.EnqueueSend(new GameMessageSystemChat($"Character option {parameters[0]} is now {(option ? "on" : "off")}.", ChatMessageType.Broadcast));

            // update client
            session.Network.EnqueueSend(new GameEventPlayerDescription(session));
        }

        /// <summary>
        /// Force resend of all visible objects known to this player. Can fix rare cases of invisible object bugs.
        /// Can only be used once every 5 mins max.
        /// </summary>
        [CommandHandler("objsend", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Force resend of all visible objects known to this player. Can fix rare cases of invisible object bugs. Can only be used once every 5 mins max.")]
        public static void HandleObjSend(Session session, params string[] parameters)
        {
            // a good repro spot for this is the first room after the door in facility hub
            // in the portal drop / staircase room, the VisibleCells do not have the room after the door
            // however, the room after the door *does* have the portal drop / staircase room in its VisibleCells (the inverse relationship is imbalanced)
            // not sure how to fix this atm, seems like it triggers a client bug..

            if (DateTime.UtcNow - session.Player.PrevObjSend < TimeSpan.FromMinutes(5))
            {
                session.Player.SendTransientError("You have used this command too recently!");
                return;
            }

            var creaturesOnly = parameters.Length > 0 && parameters[0].Contains("creature", StringComparison.OrdinalIgnoreCase);

            var knownObjs = session.Player.GetKnownObjects();

            foreach (var knownObj in knownObjs)
            {
                if (creaturesOnly && !(knownObj is Creature))
                    continue;

                session.Player.RemoveTrackedObject(knownObj, false);
                session.Player.TrackObject(knownObj);
            }
            session.Player.PrevObjSend = DateTime.UtcNow;
        }

        // show player ace server versions
        [CommandHandler("aceversion", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows this server's version data")]
        public static void HandleACEversion(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("version_info_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"aceversion\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            var msg = ServerBuildInfo.GetVersionInfo();

            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
        }

        // reportbug < code | content > < description >
        [CommandHandler("reportbug", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 2,
            "Generate a Bug Report",
            "<category> <description>\n" +
            "This command generates a URL for you to copy and paste into your web browser to submit for review by server operators and developers.\n" +
            "Category can be the following:\n" +
            "Creature\n" +
            "NPC\n" +
            "Item\n" +
            "Quest\n" +
            "Recipe\n" +
            "Landblock\n" +
            "Mechanic\n" +
            "Code\n" +
            "Other\n" +
            "For the first three options, the bug report will include identifiers for what you currently have selected/targeted.\n" +
            "After category, please include a brief description of the issue, which you can further detail in the report on the website.\n" +
            "Examples:\n" +
            "/reportbug creature Drudge Prowler is over powered\n" +
            "/reportbug npc Ulgrim doesn't know what to do with Sake\n" +
            "/reportbug quest I can't enter the portal to the Lost City of Frore\n" +
            "/reportbug recipe I cannot combine Bundle of Arrowheads with Bundle of Arrowshafts\n" +
            "/reportbug code I was killed by a Non-Player Killer\n"
            )]
        public static void HandleReportbug(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("reportbug_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"reportbug\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            var category = parameters[0];
            var description = "";

            for (var i = 1; i < parameters.Length; i++)
                description += parameters[i] + " ";

            description.Trim();

            switch (category.ToLower())
            {
                case "creature":
                case "npc":
                case "quest":
                case "item":
                case "recipe":
                case "landblock":
                case "mechanic":
                case "code":
                case "other":
                    break;
                default:
                    category = "Other";
                    break;
            }

            var sn = ConfigManager.Config.Server.WorldName;
            var c = session.Player.Name;

            var st = "ACE";

            //var versions = ServerBuildInfo.GetVersionInfo();
            var databaseVersion = DatabaseManager.World.GetVersion();
            var sv = ServerBuildInfo.FullVersion;
            var pv = databaseVersion.PatchVersion;

            //var ct = PropertyManager.GetString("reportbug_content_type").Item;
            var cg = category.ToLower();

            var w = "";
            var g = "";

            if (cg == "creature" || cg == "npc"|| cg == "item" || cg == "item")
            {
                var objectId = new ObjectGuid();
                if (session.Player.HealthQueryTarget.HasValue || session.Player.ManaQueryTarget.HasValue || session.Player.CurrentAppraisalTarget.HasValue)
                {
                    if (session.Player.HealthQueryTarget.HasValue)
                        objectId = new ObjectGuid((uint)session.Player.HealthQueryTarget);
                    else if (session.Player.ManaQueryTarget.HasValue)
                        objectId = new ObjectGuid((uint)session.Player.ManaQueryTarget);
                    else
                        objectId = new ObjectGuid((uint)session.Player.CurrentAppraisalTarget);

                    //var wo = session.Player.CurrentLandblock?.GetObject(objectId);

                    var wo = session.Player.FindObject(objectId.Full, Player.SearchLocations.Everywhere);

                    if (wo != null)
                    {
                        w = $"{wo.WeenieClassId}";
                        g = $"0x{wo.Guid:X8}";
                    }
                }
            }

            var l = session.Player.Location.ToLOCString();

            var issue = description;

            var urlbase = $"https://www.accpp.net/bug?";

            var url = urlbase;
            if (sn.Length > 0)
                url += $"sn={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sn))}";
            if (c.Length > 0)
                url += $"&c={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(c))}";
            if (st.Length > 0)
                url += $"&st={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(st))}";
            if (sv.Length > 0)
                url += $"&sv={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sv))}";
            if (pv.Length > 0)
                url += $"&pv={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pv))}";
            //if (ct.Length > 0)
            //    url += $"&ct={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ct))}";
            if (cg.Length > 0)
            {
                if (cg == "npc")
                    cg = cg.ToUpper();
                else
                    cg = char.ToUpper(cg[0]) + cg.Substring(1);
                url += $"&cg={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cg))}";
            }
            if (w.Length > 0)
                url += $"&w={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w))}";
            if (g.Length > 0)
                url += $"&g={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(g))}";
            if (l.Length > 0)
                url += $"&l={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(l))}";
            if (issue.Length > 0)
                url += $"&i={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(issue))}";

            var msg = "\n\n\n\n";
            msg += "Bug Report - Copy and Paste the following URL into your browser to submit a bug report\n";
            msg += "-=-\n";
            msg += $"{url}\n";
            msg += "-=-\n";
            msg += "\n\n\n\n";

            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.AdminTell));
        }

        // morphic - Olthoi morphic transformation system
        [CommandHandler("morphic", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Transform into your locked creature form (Olthoi only). Creates a new morphed character at level 1 with Ironman-style stats/skills.",
            "[creature wcid or name] - Required on first use to lock your morphic form\n" +
            "Examples:\n" +
            "/morphic 7 (lock to Drudge Skulker and morph)\n" +
            "/morphic (morph into locked creature)")]
        public static void HandleMorphic(Session session, params string[] parameters)
        {
            var player = session.Player;

            // Check if player is Olthoi
            if (player.Heritage != (int)HeritageGroup.Olthoi && player.Heritage != (int)HeritageGroup.OlthoiAcid)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Only Olthoi can use the morphic transformation ability.", ChatMessageType.Broadcast));
                return;
            }

            // Check if player has a locked creature - if not, they need to specify one
            var lockedCreatureWCID = player.GetProperty(PropertyInt.MorphicLockedCreatureWCID);
            Weenie weenie = null;

            if (lockedCreatureWCID == null)
            {
                // First time morphing - need to specify a creature to lock to
                if (parameters.Length == 0)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("First-time morphic transformation: Please specify a creature WCID or name to lock to.", ChatMessageType.Broadcast));
                    session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /morphic <wcid or name>", ChatMessageType.Broadcast));
                    session.Network.EnqueueSend(new GameMessageSystemChat("Warning: This will create a NEW CHARACTER and log you out. Once locked, you cannot change!", ChatMessageType.Broadcast));
                    return;
                }

                var weenieDesc = parameters[0];

                if (uint.TryParse(weenieDesc, out var wcid))
                    weenie = DatabaseManager.World.GetCachedWeenie(wcid);
                else
                    weenie = DatabaseManager.World.GetCachedWeenie(weenieDesc);

                if (weenie == null)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Creature '{weenieDesc}' not found in database.", ChatMessageType.Broadcast));
                    return;
                }

                // Lock this creature permanently for this player
                player.SetProperty(PropertyInt.MorphicLockedCreatureWCID, (int)weenie.WeenieClassId);

                var creatureName = weenie.GetProperty(PropertyString.Name) ?? weenie.ClassName;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Locking you to the {creatureName} morphic form...", ChatMessageType.Magic));
            }
            else
            {
                // Player already has a locked creature - load it
                weenie = DatabaseManager.World.GetCachedWeenie((uint)lockedCreatureWCID.Value);

                if (weenie == null)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Error: Your locked creature (WCID {lockedCreatureWCID}) could not be loaded.", ChatMessageType.Broadcast));
                    return;
                }
            }

            // Create morphed character (similar to /morph admin command)
            session.Network.EnqueueSend(new GameMessageSystemChat($"Morphing you into {weenie.GetProperty(PropertyString.Name)} ({weenie.WeenieClassId})... You will be logged out.", ChatMessageType.Broadcast));

            var guid = GuidManager.NewPlayerGuid();
            var morphedPlayer = new Player(weenie, guid, session.AccountId);

            // Preserve original player type
            morphedPlayer.Biota.WeenieType = session.Player.WeenieType;

            // Generate character name
            var name = $"{session.Player.Name}-Morphed";
            morphedPlayer.Name = name;
            morphedPlayer.Character.Name = name;

            DatabaseManager.Shard.IsCharacterNameAvailable(name, isAvailable =>
            {
                if (!isAvailable)
                {
                    // Try with timestamp
                    name = $"{session.Player.Name}-Morphed-{DateTime.UtcNow.Ticks}";
                    morphedPlayer.Name = name;
                    morphedPlayer.Character.Name = name;
                }

                morphedPlayer.Location = session.Player.Location;
                morphedPlayer.Character.CharacterOptions1 = session.Player.Character.CharacterOptions1;
                morphedPlayer.Character.CharacterOptions2 = session.Player.Character.CharacterOptions2;

                // Equip creature's default wearables
                if (weenie.PropertiesCreateList != null)
                {
                    var wearables = weenie.PropertiesCreateList.Where(x => x.DestinationType == DestinationType.Wield || x.DestinationType == DestinationType.WieldTreasure).ToList();
                    foreach (var wearable in wearables)
                    {
                        var weenieOfWearable = DatabaseManager.World.GetCachedWeenie(wearable.WeenieClassId);
                        if (weenieOfWearable == null) continue;

                        var worldObject = WorldObjectFactory.CreateNewWorldObject(weenieOfWearable);
                        if (worldObject == null) continue;

                        if (wearable.Palette > 0)
                            worldObject.PaletteTemplate = wearable.Palette;
                        if (wearable.Shade > 0)
                            worldObject.Shade = wearable.Shade;

                        worldObject.CalculateObjDesc();
                        morphedPlayer.TryEquipObject(worldObject, worldObject.ValidLocations ?? 0);
                    }
                }

                // Set to level 1
                morphedPlayer.Level = 1;

                // Apply Ironman-style stat and skill rolls
                IronmanFactory.RollAttributes(morphedPlayer);
                IronmanFactory.RollSkills(morphedPlayer);

                // Set PK-free status
                morphedPlayer.PlayerKillerStatus = PlayerKillerStatus.Free;

                // Mark as morphic form
                morphedPlayer.SetProperty(PropertyBool.IsMorphicForm, true);
                morphedPlayer.SetProperty(PropertyInt.MorphicCreatureWCID, (int)weenie.WeenieClassId);
                morphedPlayer.SetProperty(PropertyInt.MorphicLockedCreatureWCID, (int)weenie.WeenieClassId);

                morphedPlayer.GenerateNewFace();

                var possessions = morphedPlayer.GetAllPossessions();
                var possessedBiotas = new System.Collections.ObjectModel.Collection<(Biota biota, System.Threading.ReaderWriterLockSlim rwLock)>();
                foreach (var possession in possessions)
                    possessedBiotas.Add((possession.Biota, possession.BiotaDatabaseLock));

                DatabaseManager.Shard.AddCharacterInParallel(morphedPlayer.Biota, morphedPlayer.BiotaDatabaseLock, possessedBiotas, morphedPlayer.Character, morphedPlayer.CharacterDatabaseLock, saveSuccess =>
                {
                    if (!saveSuccess)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat($"Failed to create morphed character!", ChatMessageType.Broadcast));
                        return;
                    }

                    PlayerManager.AddOfflinePlayer(morphedPlayer);
                    session.Characters.Add(morphedPlayer.Character);

                    var msg = $"Successfully created morphed character \"{morphedPlayer.Name}\" at level 1 with Ironman stats/skills!";
                    session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));

                    session.LogOffPlayer();
                });
            });
        }

        // -----------------------------------------------------------------------
        // /tp  — Player-to-player teleport with accept/decline and pyreal cost
        // -----------------------------------------------------------------------

        /// <summary>
        /// Pending /tp requests: key = target player name (case-insensitive), value = (requester guid, expiry unix time).
        /// </summary>
        private static readonly ConcurrentDictionary<string, (uint RequesterGuid, double Expiry)> _tpRequests
            = new ConcurrentDictionary<string, (uint, double)>(StringComparer.OrdinalIgnoreCase);

        /// Pyreal cost per meter of distance.
        private const double TpCostPerMeter = 2.0;
        /// Minimum fee regardless of distance.
        private const int TpMinCost = 50;
        /// How many seconds a request stays open before it expires.
        private const double TpRequestTtl = 30.0;

        [CommandHandler("tp", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1,
            "Request to teleport to another online player. Costs pyrals based on distance; target must /tpaccept.",
            "[player name]\n  /tpaccept  — Accept an incoming request\n  /tpdecline — Decline an incoming request")]
        public static void HandleTpRequest(Session session, params string[] parameters)
        {
            var targetName = string.Join(" ", parameters).Trim();
            var requester = session.Player;

            var target = PlayerManager.GetOnlinePlayer(targetName);
            if (target == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"Player '{targetName}' is not online.", ChatMessageType.Broadcast));
                return;
            }

            if (target.Guid == requester.Guid)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "You cannot teleport to yourself.", ChatMessageType.Broadcast));
                return;
            }

            if (requester.Location == null || target.Location == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "Teleport is unavailable right now.", ChatMessageType.Broadcast));
                return;
            }

            var dist = requester.Location.DistanceTo(target.Location);
            var cost = Math.Max(TpMinCost, (int)Math.Round(dist * TpCostPerMeter));

            var coinValue = requester.CoinValue ?? 0;
            if (coinValue < cost)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"You need {cost:N0} pyrals to teleport to {target.Name} (you have {coinValue:N0}). [TP]",
                    ChatMessageType.Broadcast));
                return;
            }

            var expiry = Common.Time.GetUnixTime() + TpRequestTtl;
            _tpRequests[target.Name] = (requester.Guid.Full, expiry);

            session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Teleport request sent to {target.Name}. Cost: {cost:N0} pyrals. Awaiting acceptance... (expires in {TpRequestTtl}s) [TP]",
                ChatMessageType.Broadcast));

            target.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"{requester.Name} wants to teleport to you (cost to them: {cost:N0} pyrals). Type /tpaccept or /tpdecline. (expires in {TpRequestTtl}s) [TP]",
                ChatMessageType.Broadcast));
        }

        [CommandHandler("tpaccept", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Accept an incoming /tp teleport request.")]
        public static void HandleTpAccept(Session session, params string[] parameters)
        {
            var target = session.Player;

            if (!_tpRequests.TryGetValue(target.Name, out var entry))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "You have no pending teleport request. [TP]", ChatMessageType.Broadcast));
                return;
            }

            if (Common.Time.GetUnixTime() > entry.Expiry)
            {
                _tpRequests.TryRemove(target.Name, out _);
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "The teleport request has expired. [TP]", ChatMessageType.Broadcast));
                return;
            }

            var requester = PlayerManager.GetOnlinePlayer(new ObjectGuid(entry.RequesterGuid));
            if (requester == null)
            {
                _tpRequests.TryRemove(target.Name, out _);
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "The requesting player is no longer online. [TP]", ChatMessageType.Broadcast));
                return;
            }

            if (requester.Location == null || target.Location == null)
            {
                _tpRequests.TryRemove(target.Name, out _);
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "Teleport is unavailable right now. [TP]", ChatMessageType.Broadcast));
                return;
            }

            // Recalculate cost at accept time — requester may have moved
            var dist = requester.Location.DistanceTo(target.Location);
            var cost = Math.Max(TpMinCost, (int)Math.Round(dist * TpCostPerMeter));

            var coinValue = requester.CoinValue ?? 0;
            if (coinValue < cost)
            {
                _tpRequests.TryRemove(target.Name, out _);
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{requester.Name} no longer has enough pyrals ({cost:N0} required). Teleport cancelled. [TP]",
                    ChatMessageType.Broadcast));
                requester.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                    $"Teleport to {target.Name} accepted but you no longer have enough pyrals ({cost:N0} required). [TP]",
                    ChatMessageType.Broadcast));
                return;
            }

            requester.TryConsumeFromInventoryWithNetworking(273, cost);
            _tpRequests.TryRemove(target.Name, out _);

            // --- Portal flair ---
            var tpChain = new ActionChain();

            // Step 1: EnterPortal emote, then wait 5 seconds
            requester.EnqueueMotion(tpChain, MotionCommand.EnterPortal);
            tpChain.AddDelaySeconds(5.0);

            // Step 2: Teleport, then wait 5 seconds
            tpChain.AddAction(requester, () =>
            {
                if (requester.Session == null || target.Session == null) return;
                requester.Teleport(new Position(target.Location));
            });
            tpChain.AddDelaySeconds(5.0);

            // Step 3: AetheriaLevelUp re-emerge effect, then wait 5 seconds
            tpChain.AddAction(requester, () =>
            {
                requester.EnqueueBroadcast(new GameMessageScript(requester.Guid, PlayScript.AetheriaLevelUp));
            });
            tpChain.AddDelaySeconds(5.0);

            // Step 4: ExitPortal emote
            requester.EnqueueMotion(tpChain, MotionCommand.ExitPortal);

            tpChain.EnqueueChain();

            requester.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"Teleporting to {target.Name}! {cost:N0} pyrals deducted. [TP]",
                ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat(
                $"{requester.Name} is teleporting to you. [TP]", ChatMessageType.Broadcast));
        }

        [CommandHandler("tpdecline", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Decline an incoming /tp teleport request.")]
        public static void HandleTpDecline(Session session, params string[] parameters)
        {
            var target = session.Player;

            if (!_tpRequests.TryRemove(target.Name, out var entry))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(
                    "You have no pending teleport request. [TP]", ChatMessageType.Broadcast));
                return;
            }

            var requester = PlayerManager.GetOnlinePlayer(new ObjectGuid(entry.RequesterGuid));
            requester?.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"{target.Name} declined your teleport request. [TP]", ChatMessageType.Broadcast));

            session.Network.EnqueueSend(new GameMessageSystemChat(
                $"You declined {requester?.Name ?? "that player"}'s teleport request. [TP]",
                ChatMessageType.Broadcast));
        }
    }
}

