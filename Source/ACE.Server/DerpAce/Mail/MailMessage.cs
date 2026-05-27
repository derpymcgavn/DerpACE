using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ACE.Server.DerpAce.Mail
{
    /// <summary>
    /// A single piece of mail stored in a player's mailbox.
    /// Serialised as part of the mailbox JSON blob on the character.
    /// </summary>
    public class MailMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        [JsonPropertyName("from")]
        public string SenderName { get; set; }

        [JsonPropertyName("from_id")]
        public uint SenderId { get; set; }

        [JsonPropertyName("sent")]
        public DateTime SentUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("read")]
        public bool Read { get; set; } = false;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        /// <summary>Pyreals attached to this message (0 = none).</summary>
        [JsonPropertyName("pyreals")]
        public long Pyreals { get; set; } = 0;

        /// <summary>
        /// Items attached - each entry is a WCID + optional stack size.
        /// Items are held in a hidden per-player storage container and referenced by index.
        /// </summary>
        [JsonPropertyName("items")]
        public List<MailAttachment> Attachments { get; set; } = new List<MailAttachment>();

        /// <summary>True when this message has any unclaimed Pyreals or items.</summary>
        [JsonIgnore]
        public bool HasUnclaimed => !Claimed && (Pyreals > 0 || Attachments.Count > 0);

        [JsonPropertyName("claimed")]
        public bool Claimed { get; set; } = false;

        // -- COD (Cash On Delivery) -------------------------------------------

        /// <summary>MMDs the recipient must pay to claim this package (0 = no COD).</summary>
        [JsonPropertyName("cod_mmd")]
        public long CodMmd { get; set; } = 0;

        /// <summary>Original sender's display name (used to refund a declined COD).</summary>
        [JsonPropertyName("cod_sender_name")]
        public string CodSenderName { get; set; }

        /// <summary>Original sender's guid (used to refund a declined COD).</summary>
        [JsonPropertyName("cod_sender_id")]
        public uint CodSenderId { get; set; }
    }

    public class MailAttachment
    {
        [JsonPropertyName("wcid")]
        public uint Wcid { get; set; }

        [JsonPropertyName("stack")]
        public int StackSize { get; set; } = 1;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// Optional snapshot of the item's mutable biota properties at the time it was
        /// mailed. When present, the receiver gets back an item with the same rolled
        /// stats / enchantments rather than a fresh template item built from WCID.
        /// Bankable stackables (pulled out of the bank, etc.) won't have a snapshot.
        /// </summary>
        [JsonPropertyName("snap")]
        public ItemSnapshot Snapshot { get; set; }
    }

    /// <summary>
    /// A trimmed, JSON-friendly snapshot of the mutable parts of a WorldObject's biota.
    /// Stored inside a <see cref="MailAttachment"/> so mailed items keep their stats.
    /// Enum keys are stored as their integer values so the snapshot survives enum
    /// renames between releases.
    /// </summary>
    public class ItemSnapshot
    {
        [JsonPropertyName("b")]
        public Dictionary<int, bool> Bools { get; set; }

        [JsonPropertyName("i")]
        public Dictionary<int, int> Ints { get; set; }

        [JsonPropertyName("i64")]
        public Dictionary<int, long> Int64s { get; set; }

        [JsonPropertyName("f")]
        public Dictionary<int, double> Floats { get; set; }

        [JsonPropertyName("s")]
        public Dictionary<int, string> Strings { get; set; }

        [JsonPropertyName("did")]
        public Dictionary<int, uint> DataIds { get; set; }

        [JsonPropertyName("iid")]
        public Dictionary<int, uint> InstanceIds { get; set; }

        [JsonPropertyName("spells")]
        public List<int> SpellBook { get; set; }
    }
}
