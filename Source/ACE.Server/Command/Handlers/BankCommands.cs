using System;
using System.Linq;
using System.Text;
using ACE.Server.Command;
using ACE.Server.DerpAce.Bank;
using ACE.Server.Network;
using ACE.Server.WorldObjects;
using ACE.Entity.Enum;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// DerpACE Bank commands.
    ///
    ///   /bank list                          -- show all bankable items and your holdings
    ///   /bank store [name|id] [amount|*]    -- deposit item(s) into bank
    ///   /bank take  [name|id] [amount|*]    -- withdraw item(s) from bank
    ///
    ///   /cash list                          -- show pyreal balance + trade-note values
    ///   /cash give                          -- deposit all currency stacks from inventory
    ///   /cash take [amount]                 -- withdraw pyreals from bank to inventory
    ///
    ///   /ddt                                -- toggle per-player direct-deposit opt-out
    /// </summary>
    public static class BankCommands
    {
        // -- /bank --

        [CommandHandler("bank", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "DerpACE bank: store/take bankable items.\n" +
            "  /bank list\n" +
            "  /bank store [name|id] [amount|*]\n" +
            "  /bank take  [name|id] [amount|*]")]
        public static void HandleBank(Session session, params string[] parameters)
        {
            if (!BankConfig.EnableBank)
            {
                session.Player.SendMessage("The bank is currently disabled.");
                return;
            }

            var verb = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "list";

            switch (verb)
            {
                case "list":
                    HandleBankList(session.Player);
                    break;
                case "store":
                case "give":
                    HandleBankStore(session.Player, parameters);
                    break;
                case "take":
                    HandleBankTake(session.Player, parameters);
                    break;
                default:
                    session.Player.SendMessage(
                        "Usage: /bank list | /bank store [name|id] [amount|*] | /bank take [name|id] [amount|*]");
                    break;
            }
        }

        private static void HandleBankList(Player player)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Bank] Your stored items:");
            sb.AppendLine($"  {"Item",-28} {"Banked",12} {"Held",10}");

            var any = false;
            foreach (var item in BankConfig.Items)
            {
                var banked = player.GetBanked(item.Prop);
                var held   = player.GetNumInventoryItemsOfWCID(item.Id);
                if (banked == 0 && held == 0) continue;
                any = true;
                sb.AppendLine($"  {item.Name,-28} {banked,12:N0} {held,10:N0}");
            }
            if (!any)
                sb.AppendLine("  (nothing banked or in inventory)");

            sb.Append("Use: /bank store <name> <amt|*>  |  /bank take <name> <amt|*>");
            player.SendMessage(sb.ToString());
        }

        private static void HandleBankStore(Player player, string[] parameters)
        {
            // /bank store [name|id] [amount|*]
            if (parameters.Length < 2)
            {
                player.SendMessage("Usage: /bank store [name|id] [amount|*]");
                return;
            }

            var nameOrId = ParseName(parameters, skip: 1, eatLast: TryParseAmountArg(parameters, out _));
            if (!BankExtensions.TryFindBankItem(nameOrId, out var item))
            {
                player.SendMessage($"Unknown bankable item '{nameOrId}'. Use /bank list to see options.");
                return;
            }

            var held = player.GetNumInventoryItemsOfWCID(item.Id);
            if (held <= 0)
            {
                player.SendMessage($"You don't have any {item.Name} to store.");
                return;
            }

            TryParseAmountArg(parameters, out var amount);
            amount = amount == int.MaxValue ? held : Math.Min(amount, held);
            if (BankConfig.ExcessSetToMax)
                amount = Math.Min(amount, held);

            if (!player.TryConsumeFromInventoryWithNetworking(item.Id, amount))
            {
                player.SendMessage($"Unable to store {amount:N0} {item.Name}.");
                return;
            }

            player.IncBanked(item.Prop, amount);
            player.SendMessage($"Stored {amount:N0} {item.Name}. Banked: {player.GetBanked(item.Prop):N0}  Held: {player.GetNumInventoryItemsOfWCID(item.Id):N0}");
        }

        private static void HandleBankTake(Player player, string[] parameters)
        {
            // /bank take [name|id] [amount|*]
            if (parameters.Length < 2)
            {
                player.SendMessage("Usage: /bank take [name|id] [amount|*]");
                return;
            }

            var nameOrId = ParseName(parameters, skip: 1, eatLast: TryParseAmountArg(parameters, out _));
            if (!BankExtensions.TryFindBankItem(nameOrId, out var item))
            {
                player.SendMessage($"Unknown bankable item '{nameOrId}'. Use /bank list to see options.");
                return;
            }

            var banked = player.GetBanked(item.Prop);
            if (banked <= 0)
            {
                player.SendMessage($"You have no {item.Name} banked.");
                return;
            }

            TryParseAmountArg(parameters, out var amount);
            amount = amount == int.MaxValue ? (int)Math.Min(banked, int.MaxValue) : (int)Math.Min(amount, banked);

            // Create the items and try to give them to the player
            for (int i = 0; i < amount; i++)
            {
                var wo = Factories.WorldObjectFactory.CreateNewWorldObject(item.Id);
                if (wo == null) break;
                if (!player.TryCreateInInventoryWithNetworking(wo))
                {
                    // Ran out of inventory space -- stop and refund the remainder
                    player.SendMessage($"Inventory full -- only withdrew {i:N0} {item.Name}.");
                    return;
                }
            }

            player.IncBanked(item.Prop, -amount);
            player.SendMessage($"Withdrew {amount:N0} {item.Name}. Banked: {player.GetBanked(item.Prop):N0}  Held: {player.GetNumInventoryItemsOfWCID(item.Id):N0}");
        }

        // -- /cash --

        [CommandHandler("cash", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "DerpACE bank: deposit/withdraw pyreals.\n" +
            "  /cash list\n" +
            "  /cash give        -- deposit all currency stacks\n" +
            "  /cash take [amt]  -- withdraw pyreals to inventory")]
        public static void HandleCash(Session session, params string[] parameters)
        {
            if (!BankConfig.EnableBank)
            {
                session.Player.SendMessage("The bank is currently disabled.");
                return;
            }

            var verb = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "list";

            switch (verb)
            {
                case "list":
                    HandleCashList(session.Player);
                    break;
                case "give":
                    HandleCashGive(session.Player);
                    break;
                case "take":
                    HandleCashTake(session.Player, parameters);
                    break;
                default:
                    session.Player.SendMessage("Usage: /cash list | /cash give | /cash take [amount]");
                    break;
            }
        }

        private static void HandleCashList(Player player)
        {
            var cash = player.GetCash();
            var inv  = player.CoinValue ?? 0;

            var sb = new StringBuilder();
            sb.AppendLine("[Bank] Cash balance:");
            sb.AppendLine($"  Banked Pyreals : {cash,14:N0}");
            sb.AppendLine($"  On hand        : {inv,14:N0}");
            sb.AppendLine($"  Total          : {cash + inv,14:N0}");

            var hasCurrency = false;
            foreach (var cur in BankConfig.Currencies)
            {
                var held = player.GetNumInventoryItemsOfWCID(cur.Id);
                if (held <= 0) continue;
                if (!hasCurrency) { sb.AppendLine("  Inventory currency stacks:"); hasCurrency = true; }
                sb.AppendLine($"    {cur.Name,-14} x{held,-6} = {(long)held * cur.Value,12:N0} Pyreals");
            }

            sb.Append("Use: /cash give (deposit all)  |  /cash take <amt|*>");
            player.SendMessage(sb.ToString());
        }

        private static void HandleCashGive(Player player)
        {
            var deposited = player.DepositAllCurrency();
            if (deposited <= 0)
                player.SendMessage("No currency found in your inventory to deposit.");
            else
                player.SendMessage($"Deposited {deposited:N0} Pyreals into your bank. Balance: {player.GetCash():N0}");
        }

        private static void HandleCashTake(Player player, string[] parameters)
        {
            // /cash take [amount|*]
            TryParseAmountArg(parameters, out var requestedRaw, skip: 1);
            var banked = player.GetCash();
            if (banked <= 0)
            {
                player.SendMessage("You have no Pyreals banked.");
                return;
            }

            var requested = requestedRaw == int.MaxValue
                ? banked
                : Math.Min((long)requestedRaw, banked);

            // Check inventory space by trying to create one coin stack
            var testStack = Factories.WorldObjectFactory.CreateNewWorldObject("coinstack");
            if (testStack == null)
            {
                player.SendMessage("Could not create coin stack.");
                return;
            }
            testStack.Destroy();

            // Create coin stacks up to MaxStackSize
            long remaining = requested;
            const int maxStack = 25000; // standard pyreal stack cap
            while (remaining > 0)
            {
                var stackAmt = (int)Math.Min(remaining, maxStack);
                var stack = Factories.WorldObjectFactory.CreateNewWorldObject("coinstack");
                if (stack == null) break;
                stack.SetStackSize(stackAmt);
                if (!player.TryCreateInInventoryWithNetworking(stack))
                {
                    stack.Destroy();
                    player.SendMessage($"Inventory full -- partially withdrew. Remaining banked: {player.GetCash():N0}");
                    return;
                }
                remaining -= stackAmt;
                player.IncCash(-stackAmt);
            }

            player.SendMessage($"Withdrew {requested - remaining:N0} Pyreals. Balance: {player.GetCash():N0}");
        }

        // -- /ddt --

        [CommandHandler("ddt", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "Toggle per-player direct deposit opt-out.\n" +
            "When direct deposit is enabled server-wide, /ddt lets you opt out\n" +
            "so vendor sell proceeds go to your inventory instead of the bank.")]
        public static void HandleDdt(Session session, params string[] parameters)
        {
            if (!BankConfig.EnableBank)
            {
                session.Player.SendMessage("The bank is currently disabled.");
                return;
            }

            session.Player.ToggleDirectDeposit();
            var now = session.Player.IsDirectDepositDisabled();
            session.Player.SendMessage(now
                ? "Direct deposit disabled -- sell proceeds will go to your inventory."
                : "Direct deposit enabled -- sell proceeds will go to your bank.");
        }

        // -- Helpers --

        /// <summary>
        /// Returns true and sets <paramref name="amount"/> if the last element of
        /// <paramref name="parameters"/> (starting at index <paramref name="skip"/>) is a
        /// valid integer or '*'. Returns false and amount=1 otherwise.
        /// </summary>
        private static bool TryParseAmountArg(string[] parameters, out int amount, int skip = 1)
        {
            amount = 1;
            if (parameters.Length <= skip) return false;

            var last = parameters[parameters.Length - 1];
            if (last == "*") { amount = int.MaxValue; return true; }
            if (int.TryParse(last, out var n)) { amount = n; return true; }
            return false;
        }

        /// <summary>
        /// Joins parameters[skip..^eatLast] into a name string.
        /// </summary>
        private static string ParseName(string[] parameters, int skip = 1, bool eatLast = false)
        {
            var end = eatLast ? parameters.Length - 1 : parameters.Length;
            if (end <= skip) return "";
            return string.Join(" ", parameters, skip, end - skip);
        }
    }
}

