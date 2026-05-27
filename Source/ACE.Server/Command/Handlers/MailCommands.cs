using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Command;
using ACE.Server.DerpAce.Bank;
using ACE.Server.DerpAce.Mail;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    /// <summary>
    /// Player mail system - send MMDs and items to other players by name.
    ///
    /// Usage:
    ///   /mail list                                       - list inbox
    ///   /mail read    &lt;id&gt;                                 - read a single message
    ///   /mail send    &lt;name&gt; &lt;subject&gt; | &lt;body&gt;             - send text-only message
    ///   /mail pay     &lt;name&gt; &lt;mmds&gt; [note]                 - send MMDs (250k Pyreals each)
    ///   /mail ship    &lt;name&gt; &lt;wcid|item name&gt; [stack]      - ship an item
    ///   /mail cod     &lt;name&gt; &lt;wcid|item name&gt; &lt;stack&gt; &lt;mmds&gt; - ship an item, recipient pays MMDs to claim
    ///   /mail take    &lt;id&gt;                                 - claim MMDs and items (pays COD if any)
    ///   /mail decline &lt;id&gt;                                 - return a COD package to its sender
    ///   /mail delete  &lt;id&gt;                                 - delete a read message (auto-declines COD)
    ///   /mail help                                       - show this summary
    /// </summary>
    public class MailCommands
    {
        // MMD currency constants - WCID 20630, worth 250,000 Pyreals each
        private const uint MmdWcid     = 20630;
        private const long MmdValue    = 250_000;
        private const int  MmdMaxStack = 1000;

        /// <summary>PropertyInt64 slot for banked MMDs (from BankConfig.Items, defaults to 40000).</summary>
        private static int MmdBankProp =>
            BankConfig.Items.FirstOrDefault(i => i.Id == MmdWcid)?.Prop ?? 40000;

        // -- /mail -------------------------------------------------------------

        [CommandHandler("mail", AccessLevel.Player, CommandHandlerFlag.RequiresWorld,
            "Player mail - send MMDs/items between players.",
            "Usage: /mail list|read|send|pay|ship|cod|take|decline|delete|help [args]")]
        public static void HandleMail(Session session, params string[] parameters)
        {
            var player = session.Player;
            var sub = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "help";

            switch (sub)
            {
                case "list":    CmdList(player, parameters);    break;
                case "read":    CmdRead(player, parameters);    break;
                case "send":    CmdSend(player, parameters);    break;
                case "pay":     CmdPay(player, parameters);     break;
                case "ship":    CmdShip(player, parameters);    break;
                case "cod":     CmdCod(player, parameters);     break;
                case "take":    CmdTake(player, parameters);    break;
                case "decline": CmdDecline(player, parameters); break;
                case "delete":
                case "del":     CmdDelete(player, parameters);  break;
                default:        SendHelp(player);               break;
            }
        }

        // -- list --------------------------------------------------------------

        private static void CmdList(Player player, string[] parameters)
        {
            var box = MailboxManager.GetMailbox(player);
            if (box.Count == 0)
            {
                player.SendMessage("[MAIL] Your mailbox is empty.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[MAIL] Inbox ({box.Count}/{MailboxManager.MaxMessages}):");
            sb.AppendLine($"  {"ID",-8}  {"Date",-12}  {"From",-20}  {"Subject",-30}  Flags");

            foreach (var m in box.OrderBy(m => m.SentUtc))
            {
                var flags = new StringBuilder();
                if (!m.Read)                 flags.Append("[NEW]");
                if (m.CodMmd > 0)            flags.Append($"[COD {m.CodMmd:N0}MMD]");
                else if (m.Pyreals > 0)      flags.Append($"[{m.Pyreals / MmdValue}MMD]");
                if (m.Attachments.Count > 0) flags.Append($"[{m.Attachments.Count}item]");
                if (m.Claimed)               flags.Append("[claimed]");

                sb.AppendLine($"  {m.Id,-8}  {MailboxManager.FormatTimestamp(m.SentUtc),-12}  {m.SenderName,-20}  {Truncate(m.Subject, 30),-30}  {flags}");
            }

            sb.Append("  Use: /mail read <id> | /mail take <id> | /mail delete <id>");
            player.SendMessage(sb.ToString());
        }

        // -- read --------------------------------------------------------------

        private static void CmdRead(Player player, string[] parameters)
        {
            if (parameters.Length < 2) { player.SendMessage("[MAIL] Usage: /mail read <id>"); return; }

            var box = MailboxManager.GetMailbox(player);
            var msg = MailboxManager.FindById(box, parameters[1]);

            if (msg == null) { player.SendMessage("[MAIL] Message not found."); return; }

            msg.Read = true;
            MailboxManager.SaveMailbox(player, box);

            var sb = new StringBuilder();
            sb.AppendLine($"[MAIL] -- Message {msg.Id} --------------------------");
            sb.AppendLine($"  From   : {msg.SenderName}");
            sb.AppendLine($"  Date   : {MailboxManager.FormatTimestamp(msg.SentUtc)}");
            sb.AppendLine($"  Subject: {msg.Subject}");
            sb.AppendLine($"  ----------------------------------------------------");
            sb.AppendLine($"  {msg.Body}");

            if (msg.Pyreals > 0 || msg.Attachments.Count > 0 || msg.CodMmd > 0)
            {
                sb.AppendLine($"  -- Attachments --------------------------------------");
                if (msg.Pyreals > 0)
                    sb.AppendLine($"  MMDs    : {msg.Pyreals / MmdValue:N0}  ({msg.Pyreals:N0} Pyreals)");
                foreach (var att in msg.Attachments)
                    sb.AppendLine($"  Item    : {att.Name} x{att.StackSize} (wcid {att.Wcid})");
                if (msg.CodMmd > 0)
                    sb.AppendLine($"  COD     : {msg.CodMmd:N0} MMD payable to {msg.CodSenderName}");

                if (!msg.Claimed)
                {
                    if (msg.CodMmd > 0)
                        sb.AppendLine($"  >> Use /mail take {msg.Id} to pay and claim, or /mail decline {msg.Id} to return.");
                    else
                        sb.AppendLine($"  >> Use /mail take {msg.Id} to claim.");
                }
                else
                    sb.AppendLine("  [Attachments already claimed]");
            }

            player.SendMessage(sb.ToString());
        }

        // -- send (text only) --------------------------------------------------

        private static void CmdSend(Player player, string[] parameters)
        {
            // /mail send <name> <subject> | <body>
            if (parameters.Length < 4)
            {
                player.SendMessage("[MAIL] Usage: /mail send <name> <subject> | <body>");
                return;
            }

            var toName = parameters[1];
            var rest   = string.Join(" ", parameters.Skip(2));
            var parts  = rest.Split('|', 2);
            var subject = parts[0].Trim();
            var body    = parts.Length > 1 ? parts[1].Trim() : "";

            DeliverMessage(player, toName, subject, body, 0, null);
        }

        // -- pay (MMDs) --------------------------------------------------------

        private static void CmdPay(Player player, string[] parameters)
        {
            // /mail pay <name> <mmds> [note]
            if (parameters.Length < 3)
            {
                player.SendMessage("[MAIL] Usage: /mail pay <name> <mmds> [optional note]");
                return;
            }

            var toName = parameters[1];
            if (!int.TryParse(parameters[2], out var mmdCount) || mmdCount <= 0)
            {
                player.SendMessage("[MAIL] MMD count must be a positive whole number.");
                return;
            }

            var note = parameters.Length > 3
                ? string.Join(" ", parameters.Skip(3))
                : $"Payment from {player.Name}";

            // Try inventory first, then pull the remainder from the bank.
            var heldInv    = player.GetNumInventoryItemsOfWCID(MmdWcid);
            var heldBank   = BankConfig.EnableBank ? player.GetBanked(MmdBankProp) : 0;
            var totalAvail = heldInv + heldBank;

            if (totalAvail < mmdCount)
            {
                player.SendMessage($"[MAIL] You have {heldInv:N0} MMDs on hand and {heldBank:N0} banked but need {mmdCount:N0}.");
                return;
            }

            var fromInv  = (int)Math.Min(heldInv, mmdCount);
            var fromBank = mmdCount - fromInv;

            if (fromInv > 0 && !player.TryConsumeFromInventoryWithNetworking(MmdWcid, fromInv))
            {
                player.SendMessage("[MAIL] Failed to consume MMDs from your inventory.");
                return;
            }
            if (fromBank > 0)
                player.IncBanked(MmdBankProp, -fromBank);

            if (fromBank > 0)
                player.SendMessage($"[MAIL] Pulled {fromBank:N0} MMD from bank ({player.GetBanked(MmdBankProp):N0} remaining).");

            var pyrealValue = (long)mmdCount * MmdValue;
            DeliverMessage(player, toName, $"Payment: {mmdCount:N0} MMD", note, pyrealValue, null);
        }

        // -- ship (items) ------------------------------------------------------

        private static void CmdShip(Player player, string[] parameters)
        {
            // /mail ship <name> <wcid|item name> [stack]
            if (parameters.Length < 3)
            {
                player.SendMessage("[MAIL] Usage: /mail ship <name> <wcid|item name> [stack]");
                return;
            }

            var toName = parameters[1];
            if (!ParseItemAndStack(parameters, 2, out var itemQuery, out var requested))
            {
                player.SendMessage("[MAIL] Usage: /mail ship <name> <wcid|item name> [stack]");
                return;
            }

            if (!TryConsumeItemForShipping(player, itemQuery, requested, out var attachment))
                return; // helper already sent error

            DeliverMessage(player, toName,
                $"Package: {attachment.Name} x{attachment.StackSize}",
                $"Sent by {player.Name}.", 0,
                new List<MailAttachment> { attachment });
        }

        // -- cod (cash on delivery) -------------------------------------------

        private static void CmdCod(Player player, string[] parameters)
        {
            // /mail cod <name> <wcid|item name> <stack> <mmds>
            if (parameters.Length < 5)
            {
                player.SendMessage("[MAIL] Usage: /mail cod <name> <wcid|item name> <stack> <mmds>");
                return;
            }

            var toName = parameters[1];

            // Trailing two numeric args are <stack> <mmds>
            if (!int.TryParse(parameters[^1], out var codMmd) || codMmd <= 0)
            {
                player.SendMessage("[MAIL] COD MMD price must be a positive whole number.");
                return;
            }
            if (!int.TryParse(parameters[^2], out var stack) || stack <= 0)
            {
                player.SendMessage("[MAIL] Stack must be a positive whole number.");
                return;
            }

            // Item name spans parameters[2] .. parameters[^3]
            var nameEnd = parameters.Length - 2;
            if (nameEnd <= 2)
            {
                player.SendMessage("[MAIL] Usage: /mail cod <name> <wcid|item name> <stack> <mmds>");
                return;
            }
            var itemQuery = string.Join(" ", parameters, 2, nameEnd - 2).Trim();
            if (string.IsNullOrEmpty(itemQuery))
            {
                player.SendMessage("[MAIL] Usage: /mail cod <name> <wcid|item name> <stack> <mmds>");
                return;
            }

            if (!TryConsumeItemForShipping(player, itemQuery, stack, out var attachment))
                return;

            // Build COD-flagged message manually so DeliverMessage refund path stays clean.
            var subject = $"COD: {attachment.Name} x{attachment.StackSize} for {codMmd:N0} MMD";
            var body    = $"From {player.Name}. Use /mail take <id> to pay {codMmd:N0} MMD and claim, or /mail decline <id> to return.";

            DeliverMessage(player, toName, subject, body, 0,
                new List<MailAttachment> { attachment },
                codMmd: codMmd);
        }

        // -- take --------------------------------------------------------------

        private static void CmdTake(Player player, string[] parameters)
        {
            if (parameters.Length < 2) { player.SendMessage("[MAIL] Usage: /mail take <id>"); return; }

            var box = MailboxManager.GetMailbox(player);
            var msg = MailboxManager.FindById(box, parameters[1]);

            if (msg == null)    { player.SendMessage("[MAIL] Message not found."); return; }
            if (msg.Claimed)    { player.SendMessage("[MAIL] Attachments already claimed."); return; }
            if (!msg.HasUnclaimed) { player.SendMessage("[MAIL] No attachments to claim."); return; }

            // COD: charge the recipient before delivering anything
            if (msg.CodMmd > 0)
            {
                var owed     = (int)Math.Min(int.MaxValue, msg.CodMmd);
                var heldInv  = player.GetNumInventoryItemsOfWCID(MmdWcid);
                var heldBank = BankConfig.EnableBank ? player.GetBanked(MmdBankProp) : 0;
                var totalAvail = heldInv + heldBank;

                if (totalAvail < owed)
                {
                    player.SendMessage($"[MAIL] COD requires {owed:N0} MMD. You have {heldInv:N0} on hand and {heldBank:N0} banked. Message held in inbox.");
                    return;
                }

                var payInv  = (int)Math.Min(heldInv, owed);
                var payBank = owed - payInv;

                if (payInv > 0 && !player.TryConsumeFromInventoryWithNetworking(MmdWcid, payInv))
                {
                    player.SendMessage("[MAIL] Failed to take MMD payment from your inventory.");
                    return;
                }
                if (payBank > 0)
                    player.IncBanked(MmdBankProp, -payBank);

                // Mail the MMDs back to the original sender as a payment message.
                var paymentPyreals = (long)owed * MmdValue;
                var fromName       = msg.CodSenderName ?? msg.SenderName;
                DeliverMessage(player, fromName,
                    $"COD payment: {owed:N0} MMD",
                    $"Payment for '{msg.Subject}' delivered by {player.Name}.",
                    paymentPyreals, null);

                player.SendMessage($"[MAIL] Paid {owed:N0} MMD COD ({payInv:N0} inventory + {payBank:N0} bank) to {fromName}.");
                msg.CodMmd = 0;
            }

            var claimed = new List<string>();
            var useBank = BankConfig.EnableBank && player.UseDirectDeposit();

            // Deliver MMDs (the Pyreal field stores the gross Pyreal value)
            if (msg.Pyreals > 0)
            {
                var mmdsToGive = (int)(msg.Pyreals / MmdValue);
                if (mmdsToGive > 0)
                {
                    if (useBank)
                    {
                        player.IncBanked(MmdBankProp, mmdsToGive);
                        claimed.Add($"{mmdsToGive:N0} MMD -> bank");
                    }
                    else
                    {
                        GiveMmds(player, mmdsToGive);
                        claimed.Add($"{mmdsToGive:N0} MMD");
                    }
                }
            }

            // Deliver items - bankable attachments deposit directly when direct deposit is on
            foreach (var att in msg.Attachments)
            {
                var bankSlot = BankConfig.EnableBank
                    ? BankConfig.Items.FirstOrDefault(i => i.Id == att.Wcid)
                    : null;

                if (useBank && bankSlot != null)
                {
                    player.IncBanked(bankSlot.Prop, att.StackSize);
                    claimed.Add($"{att.Name} x{att.StackSize} -> bank");
                    continue;
                }

                try
                {
                    var wo = CreateAttachmentItem(att);
                    if (wo == null) continue;

                    if (player.TryCreateInInventoryWithNetworking(wo))
                        claimed.Add($"{att.Name} x{att.StackSize}");
                }
                catch { /* item WCID no longer valid */ }
            }

            msg.Claimed = true;
            msg.Read    = true;
            MailboxManager.SaveMailbox(player, box);

            player.SendMessage($"[MAIL] Claimed: {string.Join(", ", claimed)}");
        }

        // -- decline (return COD package to sender) ----------------------------

        private static void CmdDecline(Player player, string[] parameters)
        {
            if (parameters.Length < 2) { player.SendMessage("[MAIL] Usage: /mail decline <id>"); return; }

            var box = MailboxManager.GetMailbox(player);
            var msg = MailboxManager.FindById(box, parameters[1]);

            if (msg == null)     { player.SendMessage("[MAIL] Message not found."); return; }
            if (msg.Claimed)     { player.SendMessage("[MAIL] Message already claimed."); return; }
            if (msg.CodMmd <= 0) { player.SendMessage("[MAIL] Only COD messages can be declined. Use /mail delete instead."); return; }

            ReturnCodToSender(player, msg, "declined");

            box.Remove(msg);
            MailboxManager.SaveMailbox(player, box);
            player.SendMessage($"[MAIL] Declined COD {msg.Id}; package returned to {msg.CodSenderName ?? msg.SenderName}.");
        }

        // -- delete ------------------------------------------------------------

        private static void CmdDelete(Player player, string[] parameters)
        {
            if (parameters.Length < 2) { player.SendMessage("[MAIL] Usage: /mail delete <id>"); return; }

            var box = MailboxManager.GetMailbox(player);
            var msg = MailboxManager.FindById(box, parameters[1]);

            if (msg == null) { player.SendMessage("[MAIL] Message not found."); return; }

            // Auto-decline unclaimed COD so the sender doesn't lose the item.
            if (msg.CodMmd > 0 && !msg.Claimed)
            {
                ReturnCodToSender(player, msg, "deleted by recipient");
                box.Remove(msg);
                MailboxManager.SaveMailbox(player, box);
                player.SendMessage($"[MAIL] Deleted; COD package returned to {msg.CodSenderName ?? msg.SenderName}.");
                return;
            }

            if (msg.HasUnclaimed)
            {
                player.SendMessage("[MAIL] Claim your attachments first (/mail take).");
                return;
            }

            box.Remove(msg);
            MailboxManager.SaveMailbox(player, box);
            player.SendMessage($"[MAIL] Message {msg.Id} deleted.");
        }

        // -- help --------------------------------------------------------------

        private static void SendHelp(Player player)
        {
            player.SendMessage(
                "[MAIL] Commands:\n" +
                "  /mail list                                          - view inbox\n" +
                "  /mail read    <id>                                  - read a message\n" +
                "  /mail send    <name> <subject> | <body>             - send text mail\n" +
                "  /mail pay     <name> <mmds> [note]                  - send MMDs (pulls from bank if short)\n" +
                "  /mail ship    <name> <wcid|item name> [stack]       - ship an item (pulls from bank if short)\n" +
                "  /mail cod     <name> <wcid|item name> <stack> <mmds> - ship an item, recipient pays MMDs to claim\n" +
                "  /mail take    <id>                                  - claim attachments (pays COD if any)\n" +
                "  /mail decline <id>                                  - return a COD package to its sender\n" +
                "  /mail delete  <id>                                  - delete a message (auto-declines COD)");
        }

        // -- Shared helpers ----------------------------------------------------

        private static void DeliverMessage(Player sender, string recipientName, string subject, string body,
            long pyreals, List<MailAttachment> attachments, long codMmd = 0)
        {
            var target = PlayerManager.FindByName(recipientName, out var isOnline);

            if (target == null)
            {
                sender.SendMessage($"[MAIL] Player '{recipientName}' not found.");
                RefundAttachments(sender, pyreals, attachments);
                return;
            }

            var msg = new MailMessage
            {
                SenderName  = sender.Name,
                SenderId    = sender.Guid.Full,
                Subject     = subject,
                Body        = body,
                Pyreals     = pyreals,
                Attachments = attachments ?? new List<MailAttachment>(),
                CodMmd      = codMmd,
                CodSenderName = codMmd > 0 ? sender.Name      : null,
                CodSenderId   = codMmd > 0 ? sender.Guid.Full : 0u
            };

            bool delivered;

            if (isOnline)
            {
                var onlineRecipient = PlayerManager.GetOnlinePlayer(recipientName);
                delivered = onlineRecipient != null && MailboxManager.Deliver(onlineRecipient, msg);
            }
            else
            {
                var offlineRecipient = PlayerManager.GetOfflinePlayer(target.Guid);
                if (offlineRecipient == null)
                {
                    delivered = false;
                }
                else
                {
                    var box = MailboxManager.GetMailboxOffline(offlineRecipient);
                    if (box.Count >= MailboxManager.MaxMessages && !box.Any(m => m.Read && !m.HasUnclaimed))
                    {
                        delivered = false;
                    }
                    else
                    {
                        box.Add(msg);
                        MailboxManager.SaveMailboxOffline(offlineRecipient, box);
                        delivered = true;
                    }
                }
            }

            if (!delivered)
            {
                sender.SendMessage($"[MAIL] Could not deliver message to {recipientName} (mailbox full or unavailable).");
                RefundAttachments(sender, pyreals, attachments);
                return;
            }

            var attachNote = pyreals > 0 ? $" with {pyreals / MmdValue:N0} MMD" : "";
            if (attachments != null && attachments.Count > 0)
                attachNote += $" and {attachments.Count} item(s)";
            if (codMmd > 0)
                attachNote += $" (COD {codMmd:N0} MMD)";
            sender.SendMessage($"[MAIL] Message sent to {target.Name}{attachNote}.");
        }

        // -- item-shipping helpers --------------------------------------------

        /// <summary>
        /// Parses [&lt;name&gt;] &lt;wcid|item name (multi-word)&gt; [stack] starting at <paramref name="queryStart"/>.
        /// The optional final numeric argument becomes the stack count (default 1).
        /// </summary>
        private static bool ParseItemAndStack(string[] parameters, int queryStart, out string itemQuery, out int requested)
        {
            requested = 1;
            var nameEnd = parameters.Length;
            if (parameters.Length - queryStart >= 2 &&
                int.TryParse(parameters[^1], out var n) && n > 0)
            {
                requested = n;
                nameEnd   = parameters.Length - 1;
            }
            itemQuery = string.Join(" ", parameters, queryStart, nameEnd - queryStart).Trim();
            return !string.IsNullOrEmpty(itemQuery);
        }

        /// <summary>
        /// Resolves an item by WCID or name, pulling from inventory first and the player's bank slot for the
        /// shortfall when the item is a bankable type. Sends an error to the player and returns false on failure.
        /// On success, the item has been removed from the player's holdings and a populated
        /// <see cref="MailAttachment"/> is returned.
        /// </summary>
        private static bool TryConsumeItemForShipping(Player player, string itemQuery, int requested, out MailAttachment attachment)
        {
            attachment = null;

            WorldObject item = null;
            BankItem    bank = null;
            uint        wcid = 0;

            if (uint.TryParse(itemQuery, out wcid))
            {
                item = player.GetInventoryItemsOfWCID(wcid).FirstOrDefault();
                if (BankConfig.EnableBank)
                    bank = BankConfig.Items.FirstOrDefault(i => i.Id == wcid);
            }
            else
            {
                if (BankConfig.EnableBank)
                    bank = BankConfig.Items.FirstOrDefault(i => i.Name.Equals(itemQuery, StringComparison.OrdinalIgnoreCase));
                if (bank != null)
                {
                    wcid = bank.Id;
                    item = player.GetInventoryItemsOfWCID(wcid).FirstOrDefault();
                }
                else
                {
                    item = player.Inventory.Values.FirstOrDefault(i =>
                        i.Name != null && i.Name.Equals(itemQuery, StringComparison.OrdinalIgnoreCase));
                    if (item != null) wcid = item.WeenieClassId;
                }
            }

            var heldInv    = item != null ? (item.StackSize ?? 1) : 0;
            var heldBank   = bank != null ? (int)Math.Min(int.MaxValue, player.GetBanked(bank.Prop)) : 0;
            var totalAvail = heldInv + heldBank;

            if (totalAvail <= 0)
            {
                player.SendMessage($"[MAIL] No item matching '{itemQuery}' found in inventory or bank.");
                return false;
            }

            if (item != null &&
                (item.Attuned == AttunedStatus.Attuned ||
                 item.Attuned == AttunedStatus.Sticky))
            {
                player.SendMessage("[MAIL] That item is attuned and cannot be mailed.");
                return false;
            }

            var stackSize = Math.Min(requested, totalAvail);
            var fromInv   = Math.Min(heldInv, stackSize);
            var fromBank  = stackSize - fromInv;

            if (fromInv > 0 && !player.TryConsumeFromInventoryWithNetworking(item, fromInv))
            {
                player.SendMessage("[MAIL] Failed to remove item from your inventory.");
                return false;
            }
            if (fromBank > 0)
                player.IncBanked(bank.Prop, -fromBank);

            if (fromBank > 0)
                player.SendMessage($"[MAIL] Pulled {fromBank:N0} {bank.Name} from bank ({player.GetBanked(bank.Prop):N0} remaining).");

            // Snapshot the inventory item's mutable state so the recipient gets back
            // the exact same rolled stats / enchantments instead of a fresh template item.
            // Items coming purely from the bank (fromInv == 0) are bankable stackables
            // with no per-instance state, so a WCID + stack is sufficient.
            var snapshot = (fromInv > 0 && item != null) ? CaptureSnapshot(item) : null;

            attachment = new MailAttachment
            {
                Wcid      = wcid,
                Name      = item?.Name ?? bank?.Name ?? $"wcid {wcid}",
                StackSize = stackSize,
                Snapshot  = snapshot
            };
            return true;
        }

        // -- biota snapshot helpers --------------------------------------------

        /// <summary>
        /// Captures the mutable parts of an item's biota into a JSON-friendly snapshot
        /// that can survive serialization in the mailbox blob.
        /// </summary>
        private static ItemSnapshot CaptureSnapshot(WorldObject wo)
        {
            if (wo?.Biota == null) return null;
            var b = wo.Biota;
            var snap = new ItemSnapshot();

            if (b.PropertiesBool   != null && b.PropertiesBool.Count   > 0) snap.Bools       = b.PropertiesBool.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesInt    != null && b.PropertiesInt.Count    > 0) snap.Ints        = b.PropertiesInt.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesInt64  != null && b.PropertiesInt64.Count  > 0) snap.Int64s      = b.PropertiesInt64.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesFloat  != null && b.PropertiesFloat.Count  > 0) snap.Floats      = b.PropertiesFloat.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesString != null && b.PropertiesString.Count > 0) snap.Strings     = b.PropertiesString.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesDID    != null && b.PropertiesDID.Count    > 0) snap.DataIds     = b.PropertiesDID.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesIID    != null && b.PropertiesIID.Count    > 0) snap.InstanceIds = b.PropertiesIID.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
            if (b.PropertiesSpellBook != null && b.PropertiesSpellBook.Count > 0) snap.SpellBook = b.PropertiesSpellBook.Keys.ToList();

            return snap;
        }

        /// <summary>
        /// Builds a WorldObject for a mail attachment, restoring snapshot biota properties
        /// when available so the recipient gets the original item state back.
        /// </summary>
        private static WorldObject CreateAttachmentItem(MailAttachment att)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(att.Wcid);
            if (wo == null) return null;

            if (att.Snapshot != null)
                ApplySnapshot(wo, att.Snapshot);

            if (att.StackSize > 1 && wo.MaxStackSize > 1)
                wo.SetStackSize(Math.Min(att.StackSize, wo.MaxStackSize ?? att.StackSize));

            return wo;
        }

        /// <summary>
        /// Applies a snapshot's properties onto a freshly-created WorldObject, skipping
        /// instance/container/positional fields that must stay owned by the new GUID.
        /// </summary>
        private static void ApplySnapshot(WorldObject wo, ItemSnapshot snap)
        {
            if (snap.Bools != null)
                foreach (var kv in snap.Bools)
                    wo.SetProperty((PropertyBool)kv.Key, kv.Value);

            if (snap.Ints != null)
            {
                foreach (var kv in snap.Ints)
                {
                    var p = (PropertyInt)kv.Key;
                    // CoinValue is recomputed from stack contents; skip to avoid drift.
                    if (p == PropertyInt.CoinValue) continue;
                    wo.SetProperty(p, kv.Value);
                }
            }

            if (snap.Int64s != null)
                foreach (var kv in snap.Int64s)
                    wo.SetProperty((PropertyInt64)kv.Key, kv.Value);

            if (snap.Floats != null)
                foreach (var kv in snap.Floats)
                    wo.SetProperty((PropertyFloat)kv.Key, kv.Value);

            if (snap.Strings != null)
                foreach (var kv in snap.Strings)
                    wo.SetProperty((PropertyString)kv.Key, kv.Value);

            if (snap.DataIds != null)
                foreach (var kv in snap.DataIds)
                    wo.SetProperty((PropertyDataId)kv.Key, kv.Value);

            if (snap.InstanceIds != null)
            {
                foreach (var kv in snap.InstanceIds)
                {
                    var p = (PropertyInstanceId)kv.Key;
                    // Never restore ownership/container/wielder ids from a stale snapshot;
                    // those get re-stamped when the item enters the recipient's inventory.
                    if (p == PropertyInstanceId.Container ||
                        p == PropertyInstanceId.Wielder   ||
                        p == PropertyInstanceId.Owner     ||
                        p == PropertyInstanceId.AllowedActivator)
                        continue;
                    wo.SetProperty(p, kv.Value);
                }
            }

            if (snap.SpellBook != null && wo.Biota != null)
            {
                wo.Biota.PropertiesSpellBook ??= new Dictionary<int, float>();
                foreach (var spellId in snap.SpellBook)
                    wo.Biota.PropertiesSpellBook[spellId] = 2.0f;
            }
        }

        /// <summary>
        /// Mails the attachments of a COD message back to the original sender as a no-cost shipment.
        /// </summary>
        private static void ReturnCodToSender(Player recipient, MailMessage msg, string reasonLabel)
        {
            var fromName = msg.CodSenderName ?? msg.SenderName;
            if (string.IsNullOrEmpty(fromName)) return;

            DeliverMessage(recipient, fromName,
                $"Returned: {msg.Subject}",
                $"{recipient.Name} {reasonLabel} the COD package.",
                0,
                msg.Attachments != null ? new List<MailAttachment>(msg.Attachments) : null);
        }

        private static void RefundAttachments(Player sender, long pyreals, List<MailAttachment> attachments)
        {
            var useBank = BankConfig.EnableBank && sender.UseDirectDeposit();

            if (pyreals > 0)
            {
                var mmds = (int)(pyreals / MmdValue);
                if (mmds > 0)
                {
                    if (useBank) sender.IncBanked(MmdBankProp, mmds);
                    else         GiveMmds(sender, mmds);
                }
            }

            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    var bankSlot = BankConfig.EnableBank
                        ? BankConfig.Items.FirstOrDefault(i => i.Id == att.Wcid)
                        : null;

                    if (useBank && bankSlot != null)
                    {
                        sender.IncBanked(bankSlot.Prop, att.StackSize);
                        continue;
                    }

                    var wo = CreateAttachmentItem(att);
                    if (wo == null) continue;
                    sender.TryCreateInInventoryWithNetworking(wo);
                }
            }
        }

        /// <summary>
        /// Creates MMD stacks (up to MmdMaxStack each) and gives them to the player.
        /// </summary>
        private static void GiveMmds(Player player, int count)
        {
            while (count > 0)
            {
                var stackSize = Math.Min(count, MmdMaxStack);
                var stack = WorldObjectFactory.CreateNewWorldObject(MmdWcid);
                if (stack == null) break;
                if (stack.MaxStackSize > 1)
                    stack.SetStackSize(Math.Min(stackSize, stack.MaxStackSize ?? stackSize));
                if (!player.TryCreateInInventoryWithNetworking(stack))
                {
                    player.SendMessage("[MAIL] Inventory full - remaining MMDs could not be delivered.");
                    break;
                }
                count -= stackSize;
            }
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..(max - 1)] + "...";
    }
}
