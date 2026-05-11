using System;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Command.Handlers;
using ACE.Server.Factories;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Command.Handlers
{
    public static class CreatureMutatorCommands
    {
        // /cimob list - Show all registered mutators and their status
        [CommandHandler("cimob", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0,
            "Manage creature mutators in realtime.",
            "/cimob list - Show all mutators\n" +
            "/cimob enable <name> - Enable a mutator\n" +
            "/cimob disable <name> - Disable a mutator\n" +
            "/cimob tier <name> <value> - Set minimum tier for a mutator\n" +
            "/cimob chance <name> <value> - Set spawn chance (0-1) for a mutator\n" +
            "/cimob info <name> - Show detailed info for a mutator")]
        public static void HandleCreatureMutator(Session session, params string[] parameters)
        {
            if (parameters.Length == 0 || parameters[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                HandleList(session);
                return;
            }

            var command = parameters[0].ToLower();
            switch (command)
            {
                case "enable":
                    if (parameters.Length < 2)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob enable <name>", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleEnable(session, parameters[1], true);
                    break;

                case "disable":
                    if (parameters.Length < 2)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob disable <name>", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleEnable(session, parameters[1], false);
                    break;

                case "tier":
                    if (parameters.Length < 3)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob tier <name> <value>", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleTier(session, parameters[1], parameters[2]);
                    break;

                case "chance":
                    if (parameters.Length < 3)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob chance <name> <value>", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleChance(session, parameters[1], parameters[2]);
                    break;

                case "info":
                    if (parameters.Length < 2)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob info <name>", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleInfo(session, parameters[1]);
                    break;

                default:
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Unknown /cimob command: {command}. Type '/cimob' for help.", ChatMessageType.Broadcast));
                    break;
            }
        }

        private static void HandleList(Session session)
        {
            var summary = CreatureMutatorManager.GetMutatorSummary();
            session.Network.EnqueueSend(new GameMessageSystemChat(summary, ChatMessageType.System));
        }

        private static void HandleEnable(Session session, string name, bool enabled)
        {
            var success = CreatureMutatorManager.SetMutatorEnabled(name, enabled);
            if (success)
            {
                var status = enabled ? "enabled" : "disabled";
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' has been {status}.", ChatMessageType.Broadcast));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' not found.", ChatMessageType.Broadcast));
            }
        }

        private static void HandleTier(Session session, string name, string valueStr)
        {
            if (!int.TryParse(valueStr, out var tier) || tier < 1)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Tier must be a positive integer.", ChatMessageType.Broadcast));
                return;
            }

            var success = CreatureMutatorManager.SetMutatorMinTier(name, tier);
            if (success)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' MinTier set to {tier}.", ChatMessageType.Broadcast));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' not found.", ChatMessageType.Broadcast));
            }
        }

        private static void HandleChance(Session session, string name, string valueStr)
        {
            if (!float.TryParse(valueStr, out var chance) || chance < 0f || chance > 1f)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Chance must be a number between 0 and 1.", ChatMessageType.Broadcast));
                return;
            }

            var success = CreatureMutatorManager.SetMutatorChance(name, chance);
            if (success)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' Chance set to {chance:P1}.", ChatMessageType.Broadcast));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' not found.", ChatMessageType.Broadcast));
            }
        }

        private static void HandleInfo(Session session, string name)
        {
            var mutator = CreatureMutatorManager.GetMutator(name);
            if (mutator == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Mutator '{name}' not found.", ChatMessageType.Broadcast));
                return;
            }

            var status = mutator.Enabled ? "ENABLED" : "DISABLED";
            var info = $"Mutator: {mutator.Name}\n" +
                       $"Description: {mutator.Description}\n" +
                       $"Status: {status}\n" +
                       $"MinTier: {mutator.MinTier}\n" +
                       $"Chance: {mutator.Chance:P1}\n" +
                       $"NamePrefix: {mutator.NamePrefix}";

            session.Network.EnqueueSend(new GameMessageSystemChat(info, ChatMessageType.System));
        }
    }
}
