using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Command.Handlers;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

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
            "/cimob info <name> - Show detailed info for a mutator\n" +
            "/cimob spawn <mutator1> [mutator2] [mutator3] <wcid> [amount] - Spawn creature(s) with specific mutator(s)")]
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

                case "spawn":
                    if (parameters.Length < 3)
                    {
                        session.Network.EnqueueSend(new GameMessageSystemChat("Usage: /cimob spawn <mutator1> [mutator2] [mutator3] <wcid> [amount]", ChatMessageType.Broadcast));
                        return;
                    }
                    HandleSpawn(session, parameters.Skip(1).ToArray());
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

        private static void HandleSpawn(Session session, string[] parameters)
        {
            // Parse parameters: mutator names followed by wcid and optional amount
            // Examples:
            //   /cimob spawn healer 1 5          -> spawn 5 of wcid 1 with healer mutator
            //   /cimob spawn tank healer 1 3     -> spawn 3 of wcid 1 with tank AND healer mutators
            //   /cimob spawn exploding 1         -> spawn 1 of wcid 1 with exploding mutator

            var mutatorNames = new List<string>();
            uint wcid = 0;
            int amount = 1;

            // Find where the wcid starts (first numeric parameter)
            int wcidIndex = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (uint.TryParse(parameters[i], out var testWcid))
                {
                    wcid = testWcid;
                    wcidIndex = i;
                    break;
                }
            }

            if (wcidIndex == -1)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Error: No valid WCID found. Usage: /cimob spawn <mutator1> [mutator2] <wcid> [amount]", ChatMessageType.Broadcast));
                return;
            }

            // Everything before wcidIndex is a mutator name
            for (int i = 0; i < wcidIndex; i++)
            {
                mutatorNames.Add(parameters[i]);
            }

            if (mutatorNames.Count == 0)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Error: At least one mutator name required. Usage: /cimob spawn <mutator1> [mutator2] <wcid> [amount]", ChatMessageType.Broadcast));
                return;
            }

            // Check if there's an amount parameter after wcid
            if (wcidIndex + 1 < parameters.Length)
            {
                if (!int.TryParse(parameters[wcidIndex + 1], out amount) || amount < 1 || amount > 100)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("Error: Amount must be between 1 and 100.", ChatMessageType.Broadcast));
                    return;
                }
            }

            // Validate all mutators exist
            var validMutators = new List<string>();
            foreach (var mutatorName in mutatorNames)
            {
                var resolved = CreatureMutatorManager.ResolveAlias(mutatorName);
                var mutator = CreatureMutatorManager.GetMutator(resolved);
                if (mutator == null)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Error: Unknown mutator '{mutatorName}'", ChatMessageType.Broadcast));
                    return;
                }
                validMutators.Add(resolved);
            }

            // Get the weenie template
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Error: WCID {wcid} not found in database.", ChatMessageType.Broadcast));
                return;
            }

            // Check if it's a creature
            if (weenie.WeenieType != WeenieType.Creature)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Error: WCID {wcid} ({weenie.GetName()}) is not a creature.", ChatMessageType.Broadcast));
                return;
            }

            // Spawn the creature(s)
            int successCount = 0;
            var player = session.Player;
            var mutatorList = string.Join(", ", validMutators);

            for (int i = 0; i < amount; i++)
            {
                var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (wo == null)
                {
                    continue;
                }

                var creature = wo as Creature;
                if (creature == null)
                {
                    wo.Destroy();
                    continue;
                }

                // Apply each mutator in order
                bool allApplied = true;
                foreach (var mutatorName in validMutators)
                {
                    if (!CreatureMutatorManager.TryForceApplyMutator(creature, mutatorName))
                    {
                        allApplied = false;
                        break;
                    }
                }

                if (!allApplied)
                {
                    creature.Destroy();
                    continue;
                }

                // Position slightly offset from player to avoid stacking
                var offset = i * 2.0f; // 2 meter spacing
                var angle = (float)(i % 8) * (float)Math.PI / 4.0f; // Arrange in circle
                var xOffset = offset * (float)Math.Cos(angle);
                var yOffset = offset * (float)Math.Sin(angle);

                creature.Location = new ACE.Entity.Position(player.Location);
                creature.Location.PositionX += xOffset;
                creature.Location.PositionY += yOffset;

                if (!creature.EnterWorld())
                {
                    creature.Destroy();
                    continue;
                }

                successCount++;
            }

            if (successCount > 0)
            {
                var msg = successCount == 1
                    ? $"Spawned {weenie.GetName()} with mutator(s): {mutatorList}"
                    : $"Spawned {successCount}x {weenie.GetName()} with mutator(s): {mutatorList}";
                session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
            }
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Failed to spawn any creatures.", ChatMessageType.Broadcast));
            }
        }
    }
}
