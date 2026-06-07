using System;
using System.Collections.Concurrent;

using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Command;
using ACE.Server.DerpAce;
using ACE.Server.DerpAce.Bank;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    public static class PlayerCommands
    {
        private static readonly ConcurrentDictionary<uint, TeleportRequest> PendingTeleportRequests = new ConcurrentDictionary<uint, TeleportRequest>();

        [CommandHandler("tp", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "Request a player teleport.",
            "/tp <player> | /tp accept | /tp decline | /tp cancel")]
        public static void HandleTeleport(Session session, params string[] parameters)
        {
            var player = session.Player;
            if (!DerpACEConfig.EnableTeleport)
            {
                player.SendMessage("Player teleport is currently disabled.");
                return;
            }

            PruneExpiredRequests();

            if (parameters.Length == 0 || parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                player.SendMessage("Usage: /tp <player> | /tp accept | /tp decline | /tp cancel");
                return;
            }

            var verb = parameters[0].ToLowerInvariant();
            switch (verb)
            {
                case "accept":
                    AcceptTeleportRequest(player);
                    return;
                case "decline":
                case "deny":
                    DeclineTeleportRequest(player);
                    return;
                case "cancel":
                    CancelTeleportRequest(player);
                    return;
            }

            RequestTeleport(player, string.Join(" ", parameters));
        }

        private static void RequestTeleport(Player requester, string targetName)
        {
            if (!CanTeleport(requester, out var reason))
            {
                requester.SendMessage(reason);
                return;
            }

            var target = PlayerManager.GetOnlinePlayer(targetName);
            if (target == null)
            {
                requester.SendMessage($"Player '{targetName}' was not found online.");
                return;
            }

            if (target.Guid == requester.Guid)
            {
                requester.SendMessage("You are already exactly where you are. Philosophically efficient, practically unnecessary.");
                return;
            }

            if (!CanReceiveTeleport(target, out reason))
            {
                requester.SendMessage($"{target.Name} cannot receive a teleport right now: {reason}");
                return;
            }

            var cost = CalculateCost(requester.Location, target.Location);
            if (!HasEnoughPyreals(requester, cost))
            {
                requester.SendMessage($"Teleport to {target.Name} costs {cost:N0} Pyreals. You do not have enough on hand or banked.");
                return;
            }

            var request = new TeleportRequest(requester.Guid.Full, target.Guid.Full, Time.GetUnixTime());
            PendingTeleportRequests[target.Guid.Full] = request;

            requester.SendMessage($"Teleport request sent to {target.Name}. Estimated cost: {cost:N0} Pyreals.");
            target.SendMessage($"{requester.Name} wants to teleport to you. Type /tp accept to allow it or /tp decline to refuse. Estimated cost to them: {cost:N0} Pyreals.");
        }

        private static void AcceptTeleportRequest(Player target)
        {
            if (!PendingTeleportRequests.TryRemove(target.Guid.Full, out var request) || IsExpired(request))
            {
                target.SendMessage("You do not have a pending teleport request.");
                return;
            }

            var requester = PlayerManager.GetOnlinePlayer(request.RequesterGuid);
            if (requester == null)
            {
                target.SendMessage("The player who requested teleport is no longer online.");
                return;
            }

            if (!CanTeleport(requester, out var reason))
            {
                requester.SendMessage($"Teleport to {target.Name} failed: {reason}");
                target.SendMessage($"{requester.Name} cannot teleport right now: {reason}");
                return;
            }

            if (!CanReceiveTeleport(target, out reason))
            {
                requester.SendMessage($"Teleport to {target.Name} failed: {reason}");
                target.SendMessage($"You cannot receive a teleport right now: {reason}");
                return;
            }

            var cost = CalculateCost(requester.Location, target.Location);
            if (!requester.SpendWithBank(cost))
            {
                requester.SendMessage($"Teleport to {target.Name} costs {cost:N0} Pyreals. You do not have enough on hand or banked.");
                target.SendMessage($"{requester.Name} could not afford the teleport.");
                return;
            }

            requester.Teleport(new Position(target.Location));
            requester.SendMessage($"Teleporting to {target.Name}. Cost: {cost:N0} Pyreals.");
            target.SendMessage($"Accepted teleport request from {requester.Name}.");
        }

        private static void DeclineTeleportRequest(Player target)
        {
            if (!PendingTeleportRequests.TryRemove(target.Guid.Full, out var request))
            {
                target.SendMessage("You do not have a pending teleport request.");
                return;
            }

            var requester = PlayerManager.GetOnlinePlayer(request.RequesterGuid);
            requester?.SendMessage($"{target.Name} declined your teleport request.");
            target.SendMessage("Teleport request declined.");
        }

        private static void CancelTeleportRequest(Player requester)
        {
            foreach (var kvp in PendingTeleportRequests)
            {
                if (kvp.Value.RequesterGuid != requester.Guid.Full)
                    continue;

                if (PendingTeleportRequests.TryRemove(kvp.Key, out var request))
                {
                    var target = PlayerManager.GetOnlinePlayer(request.TargetGuid);
                    target?.SendMessage($"{requester.Name} cancelled their teleport request.");
                    requester.SendMessage("Teleport request cancelled.");
                    return;
                }
            }

            requester.SendMessage("You do not have an outgoing teleport request.");
        }

        private static bool CanTeleport(Player player, out string reason)
        {
            if (player == null)
            {
                reason = "player not found";
                return false;
            }

            if (player.PKTimerActive)
            {
                reason = "you have been in PK battle too recently";
                return false;
            }

            if (player.RecallsDisabled)
            {
                reason = "you must exit the training academy to use this command";
                return false;
            }

            if (player.TooBusyToRecall)
            {
                reason = "you are too busy";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool CanReceiveTeleport(Player player, out string reason)
        {
            if (player == null)
            {
                reason = "player not found";
                return false;
            }

            if (player.RecallsDisabled)
            {
                reason = "they must exit the training academy first";
                return false;
            }

            reason = null;
            return true;
        }

        private static long CalculateCost(Position from, Position to)
        {
            var distance = from?.DistanceTo(to) ?? float.MaxValue;
            if (float.IsInfinity(distance) || float.IsNaN(distance) || distance == float.MaxValue)
                return TpConfig.MinCost;

            var cost = (long)Math.Ceiling(distance * TpConfig.CostPerMeter);
            return Math.Max(TpConfig.MinCost, cost);
        }

        private static bool HasEnoughPyreals(Player player, long cost)
        {
            var onHand = player.CoinValue ?? 0;
            var banked = BankConfig.EnableBank ? player.GetCash() : 0;
            return onHand + banked >= cost;
        }

        private static bool IsExpired(TeleportRequest request)
        {
            return Time.GetUnixTime() - request.CreatedAt >= TpConfig.RequestTtl;
        }

        private static void PruneExpiredRequests()
        {
            foreach (var kvp in PendingTeleportRequests)
            {
                if (IsExpired(kvp.Value))
                    PendingTeleportRequests.TryRemove(kvp.Key, out _);
            }
        }

        private readonly struct TeleportRequest
        {
            public readonly uint RequesterGuid;
            public readonly uint TargetGuid;
            public readonly double CreatedAt;
            public TeleportRequest(uint requesterGuid, uint targetGuid, double createdAt)
            {
                RequesterGuid = requesterGuid;
                TargetGuid = targetGuid;
                CreatedAt = createdAt;
            }
        }
    }
}
