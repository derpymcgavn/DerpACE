using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.WorldObjects;

namespace ACE.Server.DerpAce.Mail
{
    /// <summary>
    /// Handles reading and writing a player's mailbox, which is serialised as JSON
    /// into a custom PropertyString slot on the character (slot 9010).
    /// No DB schema changes required - uses the same extensible property storage ACE
    /// already has for quests, allegiance MOTD, ironman state, etc.
    /// </summary>
    public static class MailboxManager
    {
        // Custom PropertyString slot - highest existing DerpACE slot is 9009
        public const PropertyString MailboxProperty = PropertyString.PlayerMailbox;

        // Maximum messages per player before oldest are pruned on send
        public const int MaxMessages = 50;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        // -- Read / Write ------------------------------------------------------

        public static List<MailMessage> GetMailbox(Player player)
        {
            var json = player.GetProperty(MailboxProperty);
            if (string.IsNullOrEmpty(json))
                return new List<MailMessage>();

            try
            {
                return JsonSerializer.Deserialize<List<MailMessage>>(json, JsonOpts)
                       ?? new List<MailMessage>();
            }
            catch
            {
                return new List<MailMessage>();
            }
        }

        public static void SaveMailbox(Player player, List<MailMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                player.RemoveProperty(MailboxProperty);
                return;
            }

            Prune(messages);

            player.SetProperty(MailboxProperty, JsonSerializer.Serialize(messages, JsonOpts));
        }

        // -- Deliver -----------------------------------------------------------

        /// <summary>
        /// Delivers a message to the target player's mailbox.
        /// If the player is online they are notified immediately.
        /// Returns true on success, false when the mailbox is full and can't be pruned further.
        /// </summary>
        public static bool Deliver(Player recipient, MailMessage message)
        {
            var box = GetMailbox(recipient);

            if (box.Count >= MaxMessages)
            {
                // Must have at least one prunable slot
                if (!box.Any(m => m.Read && !m.HasUnclaimed))
                    return false;
            }

            box.Add(message);
            SaveMailbox(recipient, box);

            // Notify online player immediately
            NotifyNewMail(recipient, message);

            return true;
        }

        // -- Notifications -----------------------------------------------------

        public static void NotifyNewMail(Player player, MailMessage msg)
        {
            var pyrealsNote = msg.Pyreals > 0 ? $" [{msg.Pyreals:N0} Pyreals attached]" : "";
            var itemsNote   = msg.Attachments.Count > 0 ? $" [{msg.Attachments.Count} item(s) attached]" : "";
            player.SendMessage(
                $"[MAIL] New message from {msg.SenderName}: \"{msg.Subject}\"{pyrealsNote}{itemsNote}  >> /mail list",
                ACE.Entity.Enum.ChatMessageType.Broadcast);
        }

        /// <summary>
        /// Called from PlayerEnterWorld to notify a player of any unread mail.
        /// </summary>
        public static void HandleLoginNotify(Player player)
        {
            var box = GetMailbox(player);
            if (box.Count == 0) return;

            var unread   = box.Count(m => !m.Read);
            var unclaimed = box.Count(m => m.HasUnclaimed);

            if (unread > 0 || unclaimed > 0)
            {
                player.SendMessage(
                    $"[MAIL] You have {unread} unread message(s) and {unclaimed} unclaimed attachment(s).  >> /mail list",
                    ACE.Entity.Enum.ChatMessageType.Broadcast);
            }
        }

        // -- Helpers -----------------------------------------------------------

        public static MailMessage FindById(List<MailMessage> box, string id) =>
            box.FirstOrDefault(m => m.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));

        public static string FormatTimestamp(DateTime utc)
        {
            var local = utc.ToLocalTime();
            return local.ToString("MMM dd HH:mm");
        }

        // -- Offline player helpers ---------------------------------------------

        public static List<MailMessage> GetMailboxOffline(OfflinePlayer player)
        {
            var json = player.GetProperty(MailboxProperty);
            if (string.IsNullOrEmpty(json))
                return new List<MailMessage>();

            try
            {
                return JsonSerializer.Deserialize<List<MailMessage>>(json, JsonOpts)
                       ?? new List<MailMessage>();
            }
            catch
            {
                return new List<MailMessage>();
            }
        }

        public static void SaveMailboxOffline(OfflinePlayer player, List<MailMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                player.RemoveProperty(MailboxProperty);
                return;
            }

            Prune(messages);
            player.SetProperty(MailboxProperty, JsonSerializer.Serialize(messages, JsonOpts));
            // ChangesDetected is set automatically by SetProperty;
            // PlayerManager.SaveOfflinePlayersWithChanges() handles the DB flush on its own timer.
        }

        private static void Prune(List<MailMessage> messages)
        {
            while (messages.Count > MaxMessages)
            {
                var oldest = messages
                    .Where(m => m.Read && !m.HasUnclaimed)
                    .OrderBy(m => m.SentUtc)
                    .FirstOrDefault();

                if (oldest == null)
                    break;

                messages.Remove(oldest);
            }
        }
    }
}
