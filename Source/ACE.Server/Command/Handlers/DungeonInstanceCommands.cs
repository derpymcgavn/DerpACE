using System;
using System.Globalization;
using System.Linq;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    public static class DungeonInstanceCommands
    {
        [CommandHandler("dungeoninstance", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1,
            "Creates, saves, and enters experimental blank dungeon instances.", "create [landblock], enter <id>, leave, list, save <name>, load <name>, saved")]
        public static void HandleDungeonInstance(Session session, params string[] parameters)
        {
            switch (parameters[0].ToLowerInvariant())
            {
                case "create":
                    HandleCreate(session, parameters.Skip(1).ToArray());
                    break;
                case "enter":
                    HandleEnter(session, parameters.Skip(1).ToArray());
                    break;
                case "leave":
                    HandleLeave(session);
                    break;
                case "list":
                    HandleList(session);
                    break;
                case "save":
                    HandleSave(session, parameters.Skip(1).ToArray());
                    break;
                case "load":
                    HandleLoad(session, parameters.Skip(1).ToArray());
                    break;
                case "saved":
                    HandleSaved(session);
                    break;
                case "where":
                    HandleWhere(session);
                    break;
                default:
                    Send(session, "Usage: @dungeoninstance create [landblock], enter <id>, leave, list, save <name>, load <name>, saved, where");
                    break;
            }
        }

        [CommandHandler("di", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1,
            "Creates, saves, and enters experimental blank dungeon instances.", "create [landblock], enter <id>, leave, list, save <name>, load <name>, saved, where")]
        public static void HandleDungeonInstanceAlias(Session session, params string[] parameters)
        {
            HandleDungeonInstance(session, parameters);
        }

        private static void HandleCreate(Session session, string[] parameters)
        {
            var templateLandblockId = session.Player.Location.LandblockId;

            if (parameters.Length > 0)
            {
                if (!TryParseLandblock(parameters[0], out templateLandblockId))
                {
                    Send(session, $"Invalid landblock '{parameters[0]}'. Use a hex landblock like 0x010A or 010AFFFF.");
                    return;
                }
            }

            var instance = DungeonInstanceManager.Create(templateLandblockId, session.Player.Name);
            if (instance == null)
            {
                Send(session, $"Landblock 0x{templateLandblockId.Landblock:X4} is not a dungeon.");
                return;
            }

            var destination = new Position(session.Player.Location)
            {
                InstanceId = instance.InstanceId
            };

            if (destination.Landblock != instance.TemplateLandblockId.Landblock)
                destination.LandblockId = new LandblockId((uint)(instance.TemplateLandblockId.Landblock << 16) | (destination.Cell & 0xFFFF));

            WorldObject.AdjustDungeon(destination);
            session.Player.Teleport(destination);

            Send(session, $"Created dungeon instance {instance.InstanceId} from 0x{instance.TemplateLandblockId.Landblock:X4}. Use @di save <name> to persist decorations.");
        }

        private static void HandleEnter(Session session, string[] parameters)
        {
            if (parameters.Length == 0 || !uint.TryParse(parameters[0], out var instanceId))
            {
                Send(session, "Usage: @dungeoninstance enter <id>");
                return;
            }

            var instance = DungeonInstanceManager.Get(instanceId);
            if (instance == null)
            {
                Send(session, $"Dungeon instance {instanceId} does not exist.");
                return;
            }

            if (session.Player.Location.Landblock != instance.TemplateLandblockId.Landblock)
            {
                Send(session, $"Stand inside template dungeon 0x{instance.TemplateLandblockId.Landblock:X4} before entering instance {instance.InstanceId}.");
                return;
            }

            var destination = new Position(session.Player.Location)
            {
                InstanceId = instance.InstanceId
            };

            WorldObject.AdjustDungeon(destination);
            session.Player.Teleport(destination);

            Send(session, $"Entering dungeon instance {instance.InstanceId}.");
        }

        private static void HandleLeave(Session session)
        {
            if (session.Player.Location.InstanceId == 0)
            {
                Send(session, "You are already in the base world.");
                return;
            }

            var destination = new Position(session.Player.Location)
            {
                InstanceId = 0
            };

            WorldObject.AdjustDungeon(destination);
            session.Player.Teleport(destination);

            Send(session, "Returning to the base world.");
        }

        private static void HandleList(Session session)
        {
            var instances = DungeonInstanceManager.List();
            if (instances.Count == 0)
            {
                Send(session, "No dungeon instances are active.");
                return;
            }

            var lines = instances.Select(i => $"{i.InstanceId}: 0x{i.TemplateLandblockId.Landblock:X4}{(string.IsNullOrWhiteSpace(i.SourceName) ? "" : $" ({i.SourceName})")}, by {i.CreatedBy}, {i.CreatedAt:u}");
            Send(session, string.Join("\n", lines));
        }

        private static void HandleSave(Session session, string[] parameters)
        {
            if (session.Player.Location.InstanceId == 0)
            {
                Send(session, "You must be inside a dungeon instance before saving.");
                return;
            }

            var name = string.Join(" ", parameters).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Send(session, "Usage: @dungeoninstance save <name>");
                return;
            }

            var definition = DungeonInstanceManager.Save(session.Player.Location.InstanceId, name, session.Player.Name, session.Player.Location);
            if (definition == null)
            {
                Send(session, $"Could not save dungeon instance {session.Player.Location.InstanceId}.");
                return;
            }

            Send(session, $"Saved dungeon instance '{definition.Name}' from 0x{definition.TemplateLandblock:X4} with {definition.Objects.Count} decoration object(s).");
        }

        private static void HandleLoad(Session session, string[] parameters)
        {
            var name = string.Join(" ", parameters).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Send(session, "Usage: @dungeoninstance load <name>");
                return;
            }

            var instance = DungeonInstanceManager.CreateFromSaved(name, session.Player.Name, out var definition);
            if (instance == null || definition == null)
            {
                Send(session, $"Saved dungeon instance '{name}' was not found.");
                return;
            }

            var destination = definition.Entry?.ToPosition(instance.InstanceId) ?? new Position(session.Player.Location)
            {
                InstanceId = instance.InstanceId
            };

            WorldObject.AdjustDungeon(destination);
            session.Player.Teleport(destination);

            Send(session, $"Loaded saved dungeon '{definition.Name}' as instance {instance.InstanceId} with {definition.Objects.Count} decoration object(s).");
        }

        private static void HandleSaved(Session session)
        {
            var saved = DungeonInstanceManager.ListSaved();
            if (saved.Count == 0)
            {
                Send(session, "No saved dungeon instances found.");
                return;
            }

            var lines = saved.Select(i => $"{i.Name}: 0x{i.TemplateLandblock:X4}, {i.ObjectCount} object(s), saved by {i.SavedBy}, {i.SavedAt:u}");
            Send(session, string.Join("\n", lines));
        }

        private static void HandleWhere(Session session)
        {
            var loc = session.Player.Location;
            var current = loc.InstanceId == 0 ? "base world" : $"instance {loc.InstanceId}";
            Send(session, $"You are in {current} at {loc.ToLOCString()}.");
        }

        private static bool TryParseLandblock(string value, out LandblockId landblockId)
        {
            value = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
            {
                landblockId = default;
                return false;
            }

            if (raw <= 0xFFFF)
                raw <<= 16;

            landblockId = new LandblockId(raw | 0xFFFF);
            return true;
        }

        private static void Send(Session session, string message)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Broadcast));
        }
    }
}
