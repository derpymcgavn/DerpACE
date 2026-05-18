using System;
using System.Collections.Generic;

using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Factories.Enum;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: The Mysterious Stranger (WCID 420355).
    ///
    /// A scripted NPC who offers a wager: bind a portion of vitae to your soul
    /// in exchange for the chance to open a handful of chests he spawns around him.
    ///
    /// On accept (rolled AFTER confirmation, so the player is committed):
    ///   * 1..40% vitae penalty is applied (random).
    ///   * 0..4 chest opens are granted (random).
    ///
    /// A 3x3 grid of 9 chests is spawned around the stranger. Slots are shuffled:
    ///   * 1 x JACKPOT  - Tier-8, max loot quality, ~15 magical items
    ///   * 2 x MID-TIER - Tier 4-6, mixed magic/mundane
    ///   * 6 x JUNK     - apples, bread, plates, mugs
    /// The player can only open up to the rolled number of chests; the rest are
    /// blocked. All chests auto-despawn after a timer.
    ///
    /// The house always wins: average outcome is ~20% vitae for 2 opens, with a
    /// 1/9 jackpot chance per open.
    /// </summary>
    public static class MysteriousStranger
    {
        public const uint WeenieClassId = 420355;

        // ---- tuning knobs ----
        public static int   MinVitaePercent     = 1;
        public static int   MaxVitaePercent     = 40;
        public static int   MinChestOpens       = 0;
        public static int   MaxChestOpens       = 4;
        public static float ChestDespawnSeconds = 90.0f;
        public static float ChestGridSpacing    = 1.5f;
        public static uint  ChestWcid           = 180; // generic chest weenie

        private const double UseCooldownSeconds = 5.0;

        private static readonly Dictionary<uint, double> _lastUse = new Dictionary<uint, double>();
        private static readonly Dictionary<uint, int>    _opensRemaining = new Dictionary<uint, int>();
        // Marks chest guids that belong to a stranger session, mapped to the player guid that owns them.
        private static readonly Dictionary<uint, uint>   _strangerChestOwner = new Dictionary<uint, uint>();

        // ---------- entry point (called from Creature.ActOnUse) ----------

        public static void OnUse(Creature stranger, Player player)
        {
            if (stranger == null || player == null)
                return;

            var now = Time.GetUnixTime();
            if (_lastUse.TryGetValue(player.Guid.Full, out var last) && now - last < UseCooldownSeconds)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    "The Mysterious Stranger eyes you patiently. Give him a moment.",
                    ChatMessageType.Tell));
                return;
            }
            _lastUse[player.Guid.Full] = now;

            if (player.IsDead)
                return;

            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                "I will bind a measure of death to your soul... in exchange for a glimpse at fortune. " +
                "Some part of you will linger as vitae. Some chests will open. How much of each? " +
                "Even I do not know.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

            var prompt =
                "The Mysterious Stranger offers a deal:\n\n" +
                $"  * You will gain {MinVitaePercent}-{MaxVitaePercent}% vitae penalty (random).\n" +
                $"  * In exchange, you may open {MinChestOpens}-{MaxChestOpens} of the 9 chests he spawns (random).\n\n" +
                "One chest is a Tier-8 jackpot. Most are junk. You will not know which is which.\n\n" +
                "Do you accept?";

            var confirm = new Confirmation_Custom(player.Guid, () => ResolveDeal(stranger, player));
            if (!player.ConfirmationManager.EnqueueSend(confirm, prompt))
                player.SendWeenieError(WeenieError.ConfirmationInProgress);
        }

        // ---------- core resolution ----------

        private static void ResolveDeal(Creature stranger, Player player)
        {
            if (stranger == null || player == null || stranger.IsDestroyed || stranger.Location == null)
                return;
            if (player.IsDead)
                return;

            var vitaePct   = ThreadSafeRandom.Next(MinVitaePercent, MaxVitaePercent);
            var chestOpens = ThreadSafeRandom.Next(MinChestOpens, MaxChestOpens);

            // 1) Apply the price: vitae is bound no matter what
            ApplyVitae(player, vitaePct / 100.0f);

            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"The Mysterious Stranger binds {vitaePct}% vitae to your soul...",
                ChatMessageType.Magic));
            player.EnqueueBroadcast(new GameMessageScript(player.Guid, PlayScript.VisionDownBlack, 1.0f));

            // 2) Reveal the reward
            if (chestOpens <= 0)
            {
                stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                    "Ahh. The dice were unkind to you tonight. No chests for you. Better luck next time, friend.",
                    stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    "You were granted 0 chest opens. The house wins.",
                    ChatMessageType.Broadcast));
                return;
            }

            _opensRemaining[player.Guid.Full] = chestOpens;
            SpawnNineChests(stranger, player, chestOpens);

            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                $"Fortune grants you {chestOpens} open{(chestOpens == 1 ? "" : "s")}. Choose wisely.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
        }

        /// <summary>
        /// Applies a custom-amount vitae penalty without touching VitaeCpPool/DeathLevel.
        /// Mirrors EnchantmentManager.UpdateVitae(), but uses the caller-supplied delta
        /// instead of the global vitae_penalty config value.
        /// </summary>
        private static void ApplyVitae(Player player, float amount)
        {
            if (player == null || amount <= 0)
                return;

            var em = player.EnchantmentManager;

            if (!em.HasVitae)
            {
                // Use the standard pathway to insert the vitae enchantment, then patch the value.
                em.UpdateVitae();
            }

            var vitae = em.GetVitae();
            if (vitae == null)
                return;

            // Subtract by our amount; clamp to the level-based floor.
            var newValue = (em.HasVitae ? vitae.StatModValue : 1.0f) - amount;

            var minVitae = em.GetMinVitae((uint)(player.Level ?? 1));
            if (newValue < minVitae) newValue = minVitae;
            if (newValue > 1.0f)     newValue = 1.0f;

            vitae.StatModValue = newValue;
            em.WorldObject.ChangesDetected = true;

            // Broadcast the update to the client so the vitae indicator refreshes.
            var spell = new ACE.Server.Entity.Spell((uint)SpellId.Vitae);
            var enchantment = new Enchantment(player, player.Guid.Full, (uint)SpellId.Vitae, 0,
                (EnchantmentMask)spell.StatModType, newValue);
            player.Session.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(player.Session, enchantment));
        }

        // ---------- chest spawning ----------

        private enum ChestSlot { Jackpot, MidTier, Junk }

        private static void SpawnNineChests(Creature stranger, Player player, int opensAllowed)
        {
            var slots = BuildRandomizedSlots();
            var positions = BuildGridPositions(stranger.Location);

            for (var i = 0; i < 9; i++)
            {
                var chest = WorldObjectFactory.CreateNewWorldObject(ChestWcid) as Chest;
                if (chest == null)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"[MysteriousStranger] Chest weenie {ChestWcid} not found in world DB.",
                        ChatMessageType.System));
                    continue;
                }

                chest.Location = positions[i];
                chest.SetProperty(PropertyBool.DefaultLocked, false);
                chest.IsLocked = false;
                chest.SetProperty(PropertyBool.ChestClearedWhenClosed, true);

                FillChest(chest, slots[i]);

                // Visual tint by slot type
                switch (slots[i])
                {
                    case ChestSlot.Jackpot:
                        chest.PaletteTemplate = (int)PaletteTemplate.Yellow;
                        chest.Shade = 1.0;
                        break;
                    case ChestSlot.MidTier:
                        chest.PaletteTemplate = (int)PaletteTemplate.Blue;
                        chest.Shade = 0.6;
                        break;
                    case ChestSlot.Junk:
                        chest.PaletteTemplate = (int)PaletteTemplate.Grey;
                        chest.Shade = 0.4;
                        break;
                }

                if (!LandblockManager.AddObject(chest))
                {
                    chest.Destroy();
                    continue;
                }

                // Tag ownership AFTER add (guid is finalized)
                _strangerChestOwner[chest.Guid.Full] = player.Guid.Full;

                chest.EnqueueBroadcast(new GameMessageScript(chest.Guid, PlayScript.EnchantUpPurple, 1.0f));

                var captured = chest;
                var chain = new ActionChain();
                chain.AddDelaySeconds(ChestDespawnSeconds);
                chain.AddAction(stranger, () =>
                {
                    _strangerChestOwner.Remove(captured.Guid.Full);
                    if (!captured.IsDestroyed)
                    {
                        captured.EnqueueBroadcast(new GameMessageScript(captured.Guid, PlayScript.Destroy, 1.0f));
                        captured.Destroy();
                    }
                });
                chain.EnqueueChain();
            }

            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Nine chests materialize around the Mysterious Stranger. You may open {opensAllowed} of them. They vanish in {ChestDespawnSeconds:N0}s.",
                ChatMessageType.Broadcast));
        }

        /// <summary>
        /// Called from Chest.ActOnUse BEFORE the normal open path. Returns true if the
        /// open was blocked (chest is part of a stranger session and player is out of opens,
        /// or the chest isn't the player's). Returns false to allow the chest to open normally.
        /// </summary>
        public static bool TryConsumeChestOpen(Chest chest, Player player)
        {
            if (chest == null || player == null) return false;
            if (!_strangerChestOwner.TryGetValue(chest.Guid.Full, out var ownerGuid)) return false;

            if (ownerGuid != player.Guid.Full)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    "This chest belongs to another patron of the Mysterious Stranger.",
                    ChatMessageType.Broadcast));
                return true;
            }

            if (!_opensRemaining.TryGetValue(player.Guid.Full, out var remaining) || remaining <= 0)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    "You have no chest opens remaining from the Mysterious Stranger.",
                    ChatMessageType.Broadcast));
                return true;
            }

            _opensRemaining[player.Guid.Full] = remaining - 1;
            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Opens remaining: {remaining - 1}.", ChatMessageType.Broadcast));
            return false;
        }

        // ---------- slot / grid helpers ----------

        private static ChestSlot[] BuildRandomizedSlots()
        {
            var slots = new List<ChestSlot>(9)
            {
                ChestSlot.Jackpot,
                ChestSlot.MidTier, ChestSlot.MidTier,
                ChestSlot.Junk, ChestSlot.Junk, ChestSlot.Junk,
                ChestSlot.Junk, ChestSlot.Junk, ChestSlot.Junk,
            };

            // Fisher-Yates shuffle
            for (var i = slots.Count - 1; i > 0; i--)
            {
                var j = ThreadSafeRandom.Next(0, i);
                var tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
            }
            return slots.ToArray();
        }

        private static Position[] BuildGridPositions(Position center)
        {
            var positions = new Position[9];
            var idx = 0;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    positions[idx++] = new Position(
                        center.LandblockId.Raw,
                        center.PositionX + dx * ChestGridSpacing,
                        center.PositionY + dy * ChestGridSpacing,
                        center.PositionZ + 0.05f,
                        0f, 0f,
                        center.RotationZ, center.RotationW);
                }
            }
            return positions;
        }

        // ---------- chest loot fills ----------

        private static void FillChest(Chest chest, ChestSlot slot)
        {
            switch (slot)
            {
                case ChestSlot.Jackpot: FillJackpot(chest); break;
                case ChestSlot.MidTier: FillMidTier(chest); break;
                default:                FillJunk(chest);    break;
            }
        }

        private static void FillJackpot(Chest chest)
        {
            var profile = new TreasureDeath
            {
                Tier = 8,
                LootQualityMod = 1.0f,
                MagicItemChance = 100, MagicItemMinAmount = 15, MagicItemMaxAmount = 15,
            };

            for (var i = 0; i < 15; i++)
            {
                var wo = LootGenerationFactory.CreateRandomLootObjects(profile, TreasureItemCategory.MagicItem);
                if (wo != null && !chest.TryAddToInventory(wo))
                    wo.Destroy();
            }
        }

        private static void FillMidTier(Chest chest)
        {
            var profile = new TreasureDeath
            {
                Tier = ThreadSafeRandom.Next(4, 6),
                LootQualityMod = 0,
                MagicItemChance = 100, MagicItemMinAmount = 1, MagicItemMaxAmount = 3,
                MundaneItemChance = 100, MundaneItemMinAmount = 1, MundaneItemMaxAmount = 2,
            };

            var count = ThreadSafeRandom.Next(2, 5);
            for (var i = 0; i < count; i++)
            {
                var category = ThreadSafeRandom.Next(0, 1) == 0
                    ? TreasureItemCategory.MagicItem
                    : TreasureItemCategory.MundaneItem;

                var wo = LootGenerationFactory.CreateRandomLootObjects(profile, category);
                if (wo != null && !chest.TryAddToInventory(wo))
                    wo.Destroy();
            }
        }

        // Common junk WCIDs (apple, bread, plate, mug). Tweak to taste.
        private static readonly uint[] JunkWcids = new uint[] { 259, 5470, 690, 9457 };

        private static void FillJunk(Chest chest)
        {
            var count = ThreadSafeRandom.Next(1, 3);
            for (var i = 0; i < count; i++)
            {
                var wcid = JunkWcids[ThreadSafeRandom.Next(0, JunkWcids.Length - 1)];
                var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                if (wo != null && !chest.TryAddToInventory(wo))
                    wo.Destroy();
            }
        }
    }
}
