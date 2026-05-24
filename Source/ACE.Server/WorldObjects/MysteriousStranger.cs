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
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const uint WeenieClassId = 420355;

        // ---- tuning knobs ----
        public static int    MinVitaePercent       = 1;
        public static int    MaxVitaePercent       = 40;
        public static int    MinChestOpens         = 0;
        public static int    MaxChestOpens         = 4;
        public static float  ChestDespawnSeconds   = 120.0f;
        // Seconds before despawn that the player is warned and the Stranger speaks.
        public static float  ChestDespawnWarningSeconds = 10.0f;
        // If a chest is still open (player is browsing it) when its despawn timer fires,
        // we push the despawn back this many seconds and re-check. Prevents yanking
        // the inventory window out from under the player mid-loot.
        public static float  ChestDespawnGraceSeconds   = 5.0f;
        public static float  ChestGridSpacing      = 1.5f;
        public static uint   ChestWcid             = 143; // chest weenie
        // Obfuscated burden range (stones) shown on appraisal so players can't weigh-check the jackpot.
        public static int    ObfuscatedBurdenMin   = 50;
        public static int    ObfuscatedBurdenMax   = 950;
        public static float  DramaticSpawnDelay    = 0.9f; // seconds between each rotate+spawn
        // Distance in meters in front of the stranger to drop each chest. 10 ft ~= 3.048 m.
        public static float  ChestArcDistance      = 3.048f;
        // Total arc swept across all 9 chests (degrees). 8 gaps * 22.5deg = 180deg.
        public static float  ChestArcSweepDegrees  = 180.0f;
        public static int    DealCooldownSeconds   = 86400; // 24 hours between deals (per player)
        public const  string DealQuestStamp        = "MysteriousStrangerDeal";

        // Palettes that look good on the generic chest mesh. Picked at random per spawn.
        private static readonly PaletteTemplate[] RandomChestPalettes = new[]
        {
            PaletteTemplate.Yellow,
            PaletteTemplate.Blue,
            PaletteTemplate.Red,
            PaletteTemplate.Green,
            PaletteTemplate.Purple,
            PaletteTemplate.Rose,
            PaletteTemplate.Copper,
            PaletteTemplate.Silver,
            PaletteTemplate.Gold,
            PaletteTemplate.Bronze,
            PaletteTemplate.AquaBlue,
            PaletteTemplate.BluePurple,
            PaletteTemplate.RedPurple,
            PaletteTemplate.YellowBrown,
            PaletteTemplate.DarkBlue,
            PaletteTemplate.DeepGreen,
            PaletteTemplate.Maroon,
            PaletteTemplate.Navy,
            PaletteTemplate.LightBlue,
            PaletteTemplate.Black,
            PaletteTemplate.White,
            PaletteTemplate.SandyYellow,
            PaletteTemplate.TanRed,
            PaletteTemplate.PalePurple,
        };

        private const double UseCooldownSeconds = 5.0;

        private enum ChestSlot { Jackpot, MidTier, Junk }

        // ---- prank infrastructure ----

        /// <summary>
        /// Chance (0..1) that opening a JUNK chest triggers a prank in addition to
        /// the standard junk reaction. Defaults to 1.0 — every junk is a prank.
        /// </summary>
        public static double JunkPrankChance = 1.0;

        private sealed class Prank
        {
            public string Name;
            public Action<Creature, Player> Run;
        }

        // Wcids used by pranks. Adjust to taste.
        private const uint CheeseWcid = 261;   // cheese
        private const uint MiteWcid   = 10;    // mite (per user request)
        private const uint RatWcid    = 30;    // small rat
        private const uint DrudgeWcid = 31;    // drudge skulker (filler creature)

        // 20+ heckles, picked at random whenever the Stranger mocks a junk pull.
        // Mixed insults, puns, and theatrical taunts so it doesn't get stale.
        private static readonly string[] JunkOneLiners = new string[]
        {
            "AHAHAHA! Oh, that one stings, doesn't it? Junk! Pure junk!",
            "Hah! The dice spit on you tonight, friend.",
            "Oof. Even the chest is embarrassed for you.",
            "Tell me, do you bring this much luck to your friends, too?",
            "Behold! The legendary haul of... absolutely nothing.",
            "I'd say better luck next time, but I'd be lying.",
            "Cheese it up, hero. Cheese it up.",
            "Some men chase fortune. You... apparently chase mites.",
            "Don't be sad. Be sad-DER. It motivates me.",
            "That's not loot, that's a participation trophy.",
            "I almost feel bad. Almost.",
            "Brave! Stupid, but brave.",
            "Ho ho! The house always wins. Especially against you.",
            "You may keep the splinters. As a souvenir.",
            "Your ancestors are watching, and they are disappointed.",
            "Curd you believe it? Another flop!",
            "I told the chest to go easy on you. It refused.",
            "Tell your friends! Tell EVERYONE!",
            "Was that your gambling money or your grocery money? I forget.",
            "The chest opens, and a single tear rolls down the world's cheek.",
            "Beautiful technique. Terrible result. Five out of ten.",
            "Don't worry, the gods aren't laughing AT you. ...They're laughing nearby.",
            "I have seen rocks with more luck. Literal rocks.",
            "Try squinting. It might appraise better.",
        };

        private static readonly List<Prank> Pranks = new List<Prank>
        {
            new Prank
            {
                Name = "CheeseRain",
                Run = (s, p) => DropFromSky(s, p, CheeseWcid, count: 300, heightFt: 200f, durationSeconds: 20f,
                    line: "Behold! It is RAINING. ...Cheese. It is raining cheese. Catch them all, gouda boy!"),
            },
            new Prank
            {
                Name = "MiteSwarm",
                Run = (s, p) => SpawnSwarmAroundPlayer(s, p, MiteWcid, count: 30, radius: 5f,
                    line: "Oh! What's that on your boot? And your other boot? AND THAT ONE?"),
            },
            new Prank
            {
                Name = "RatStampede",
                Run = (s, p) => SpawnSwarmAroundPlayer(s, p, RatWcid, count: 20, radius: 6f,
                    line: "Rats! I knew I forgot something. Have a few dozen, on the house."),
            },
            new Prank
            {
                Name = "DrudgeAmbush",
                Run = (s, p) => SpawnSwarmAroundPlayer(s, p, DrudgeWcid, count: 8, radius: 5f,
                    line: "The little ones smelled the chest. Now they smell YOU."),
            },
            new Prank
            {
                Name = "CheeseSprinkle",
                Run = (s, p) => DropFromSky(s, p, CheeseWcid, count: 60, heightFt: 80f, durationSeconds: 8f,
                    line: "A modest cheese-shower. For your... troubles."),
            },
        };

        private static readonly Dictionary<uint, double> _lastUse = new Dictionary<uint, double>();
        private static readonly Dictionary<uint, int>    _opensRemaining = new Dictionary<uint, int>();
        // Per-chest ownership + slot type, keyed by chest guid.
        private static readonly Dictionary<uint, ChestSession> _strangerChestOwner = new Dictionary<uint, ChestSession>();
        // Per-player list of currently-spawned chest guids, used for cleanup when opens are exhausted.
        private static readonly Dictionary<uint, List<uint>> _playerChests = new Dictionary<uint, List<uint>>();

        private sealed class ChestSession
        {
            public uint PlayerGuid;
            public ChestSlot Slot;
            public Creature Stranger;
            public bool Opened;
            public bool LaughOnClose;
        }

        // ---------- entry point (called from Creature.ActOnUse) ----------

        public static void OnUse(Creature stranger, Player player)
        {
            if (stranger == null || player == null)
                return;

            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"[MysteriousStranger] OnUse fired by {player.Name} on {stranger.Name} (wcid {stranger.WeenieClassId}).",
                ChatMessageType.System));

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

            // 24-hour per-player cooldown on the deal itself (separate from the 5s anti-spam).
            // Tracked as a custom quest stamp on the player so it survives logout / server restart.
            var qm = player.QuestManager;
            if (qm != null)
            {
                var stamp = qm.GetQuest(DealQuestStamp);
                if (stamp != null)
                {
                    var elapsed = (long)Time.GetUnixTime() - (long)stamp.LastTimeCompleted;
                    var remaining = DealCooldownSeconds - elapsed;
                    if (remaining > 0)
                    {
                        var hours = remaining / 3600;
                        var minutes = (remaining % 3600) / 60;
                        stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                            "Patience. Fortune does not visit the same soul twice in one day. Return on the morrow.",
                            stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                            $"You may bargain with the Mysterious Stranger again in {hours}h {minutes}m.",
                            ChatMessageType.Broadcast));
                        return;
                    }
                }
            }

            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                "I will bind a measure of death to your soul... in exchange for a glimpse at fortune. " +
                "Some part of you will linger as vitae. Some chests will open. How much of each? " +
                "Even I do not know.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

            // Turn the stranger to face the player while he makes the offer.
            stranger.TurnTo(player.Location);

            var prompt =
                "The Mysterious Stranger offers a deal:\n\n" +
                $"  * You will gain {MinVitaePercent}-{MaxVitaePercent}% vitae penalty (random).\n" +
                $"  * In exchange, you may open {MinChestOpens}-{MaxChestOpens} of the 9 chests he spawns (random).\n\n" +
                "One chest is a Tier-8 jackpot. Most are junk. You will not know which is which.\n\n" +
                "Do you accept?";

            var confirm = new Confirmation_Custom(
                player.Guid,
                () => ResolveDeal(stranger, player),
                () => DeclineDeal(stranger, player));
            if (!player.ConfirmationManager.EnqueueSend(confirm, prompt))
                player.SendWeenieError(WeenieError.ConfirmationInProgress);
        }

        // ---------- decline ----------

        private static void DeclineDeal(Creature stranger, Player player)
        {
            if (stranger == null || stranger.IsDestroyed)
                return;

            // Make sure he's still looking at the one who turned him down.
            if (player != null && player.Location != null)
                stranger.TurnTo(player.Location);

            stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.Shrug));
            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                "Heh. Run along then. You'll be back. They always come back.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
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

            // Stamp the 24-hour cooldown the moment the player commits, win or lose.
            player.QuestManager?.Stamp(DealQuestStamp);

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

        /// <summary>
        /// Dramatically reveals the 9 chests one at a time. The stranger plays a purple
        /// effect, then for each chest he points down and a chest pops into existence
        /// roughly 10 feet in front of him. Between chests he rotates by an even slice
        /// of <see cref="ChestArcSweepDegrees"/> so the chests sweep out a fan in front.
        /// </summary>
        private static void SpawnNineChests(Creature stranger, Player player, int opensAllowed)
        {
            var slots = BuildRandomizedSlots();

            // Pre-allocate the player's chest list so cleanup hooks always have a bucket.
            _playerChests[player.Guid.Full] = new List<uint>(9);

            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"[MysteriousStranger] Conjuring {opensAllowed} of 9 chests {(player.IsAdmin ? "(admin: [JACKPOT]/[MID]/[JUNK] tags enabled) " : "")}...",
                ChatMessageType.System));

            // Capture the stranger's starting orientation so the whole sweep is relative
            // to where he was looking when the deal closed (which is at the player).
            var startQuat = stranger.Location.Rotation;

            // 9 placements means 8 rotation steps; spread the full sweep across those steps.
            var stepDegrees = ChestArcSweepDegrees / 8.0f;
            // Start the arc at -sweep/2 so the middle chest lands straight ahead.
            var startOffsetDeg = -ChestArcSweepDegrees / 2.0f;

            // Big purple flourish kicks off the show.
            stranger.EnqueueBroadcast(new GameMessageScript(stranger.Guid, PlayScript.EnchantUpPurple, 1.5f));

            var chain = new ActionChain();
            for (var i = 0; i < 9; i++)
            {
                var captureIndex = i;
                var offsetDeg = startOffsetDeg + stepDegrees * captureIndex;

                chain.AddDelaySeconds(DramaticSpawnDelay);
                chain.AddAction(stranger, () =>
                {
                    if (stranger.IsDestroyed || stranger.Location == null) return;

                    // Rotate the stranger to the next slice of the arc.
                    var sliceQuat = RotateYaw(startQuat, offsetDeg);
                    stranger.Location.Rotation = sliceQuat;
                    if (stranger.PhysicsObj != null)
                        stranger.PhysicsObj.Position.Frame.Orientation = sliceQuat;
                    stranger.EnqueueBroadcast(new GameMessageUpdatePosition(stranger));

                    // Point down at the spot where the chest will appear.
                    stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.PointDown));

                    var slotPos = ComputeArcSlotPosition(stranger.Location, ChestArcDistance);
                    var slotKind = slots[captureIndex];
                    SpawnOneChest(stranger, player, slotPos, slotKind, captureIndex + 1);
                });
            }
            // After the last chest, the stranger straightens up and announces.
            chain.AddDelaySeconds(DramaticSpawnDelay);
            chain.AddAction(stranger, () =>
            {
                if (stranger.IsDestroyed) return;
                stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.BowDeep));
                stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                    $"The board is set. {opensAllowed} chest{(opensAllowed == 1 ? "" : "s")} await your touch... before they fade.",
                    stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[MysteriousStranger] You may open {opensAllowed} chest{(opensAllowed == 1 ? "" : "s")}. They vanish in {ChestDespawnSeconds:N0}s.",
                    ChatMessageType.Broadcast));
            });
            chain.EnqueueChain();
        }

        /// <summary>Rotates a quaternion around world Z (yaw) by the given degrees.</summary>
        private static System.Numerics.Quaternion RotateYaw(System.Numerics.Quaternion start, float degrees)
        {
            var radians = (float)(degrees * Math.PI / 180.0);
            var yaw = System.Numerics.Quaternion.CreateFromYawPitchRoll(0f, 0f, radians);
            return start * yaw;
        }

        /// <summary>
        /// Returns a Position the given distance directly in front of <paramref name="from"/>
        /// using its current rotation. Used to drop a chest "10 feet in front of" the stranger.
        /// </summary>
        private static Position ComputeArcSlotPosition(Position from, float distance)
        {
            var p = from.InFrontOf(distance);
            p.SetLandblock();
            p.SetLandCell();
            return p;
        }

        private static void SpawnOneChest(Creature stranger, Player player, Position slotPos, ChestSlot slotKind, int slotNum)
        {
            var raw = WorldObjectFactory.CreateNewWorldObject(ChestWcid);
            if (raw == null)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[MysteriousStranger] WCID {ChestWcid} not found in world DB. Aborting chest #{slotNum}.",
                    ChatMessageType.System));
                return;
            }

            var target = raw as Container;
            if (target == null)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[MysteriousStranger] WCID {ChestWcid} ('{raw.GetType().Name}') is not a Container. Cannot fill chest #{slotNum}.",
                    ChatMessageType.System));
                raw.Destroy();
                return;
            }

            target.Location = new Position(slotPos);
            target.SetProperty(PropertyBool.DefaultLocked, false);
            target.SetProperty(PropertyBool.Stuck, false);
            if (target is Chest tc)
            {
                tc.IsLocked = false;
                tc.SetProperty(PropertyBool.ChestClearedWhenClosed, true);
            }

            FillChest(target, slotKind);

            // Hide the real burden so players can't weigh-check which chest is the jackpot.
            ObfuscateBurden(target);

            // Randomize palette per chest so the grid pops visually.
            target.PaletteTemplate = (int)RandomChestPalettes[ThreadSafeRandom.Next(0, RandomChestPalettes.Length - 1)];
            target.Shade = (float)ThreadSafeRandom.Next(0, 1000) / 1000f;

            // Admin debug: tag the chest's name with its slot type so staff can verify spawns.
            if (player.IsAdmin)
            {
                var tag = slotKind == ChestSlot.Jackpot ? "[JACKPOT]"
                        : slotKind == ChestSlot.MidTier ? "[MID]"
                                                        : "[JUNK]";
                target.Name = $"{target.Name} {tag}";
            }

            if (!LandblockManager.AddObject(target, true))
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"[MysteriousStranger] LandblockManager.AddObject failed for chest #{slotNum} at {target.Location.ToLOCString()}",
                    ChatMessageType.System));
                target.Destroy();
                return;
            }

            // Tag ownership AFTER add (guid is finalized)
            _strangerChestOwner[target.Guid.Full] = new ChestSession
            {
                PlayerGuid = player.Guid.Full,
                Slot       = slotKind,
                Stranger   = stranger,
            };
            if (_playerChests.TryGetValue(player.Guid.Full, out var bucket))
                bucket.Add(target.Guid.Full);

            target.EnqueueBroadcast(new GameMessageScript(target.Guid, PlayScript.EnchantUpPurple, 1.0f));

            // Small theatrical flourish on the stranger as each chest pops.
            stranger.EnqueueBroadcast(new GameMessageScript(stranger.Guid, PlayScript.SkillUpPurple, 1.0f));

            var captured = target;
            var capturedStranger = stranger;
            var capturedPlayer = player;

            // Warning fires N seconds before despawn so the player has time to grab loot.
            var warnAt = Math.Max(0f, ChestDespawnSeconds - ChestDespawnWarningSeconds);
            var wchain = new ActionChain();
            wchain.AddDelaySeconds(warnAt);
            wchain.AddAction(stranger, () => WarnChestDespawn(capturedStranger, capturedPlayer, captured));
            wchain.EnqueueChain();

            var dchain = new ActionChain();
            dchain.AddDelaySeconds(ChestDespawnSeconds);
            dchain.AddAction(stranger, () => TryDespawnChestSafely(captured));
            dchain.EnqueueChain();
        }

        /// <summary>
        /// Plays a "this chest is about to vanish" cue: small puff on the chest and a
        /// short heads-up line from the Stranger to the chest's owner.
        /// </summary>
        private static void WarnChestDespawn(Creature stranger, Player owner, WorldObject chest)
        {
            if (chest == null || chest.IsDestroyed) return;

            // Visual puff on the chest itself so it's obvious which chest is going.
            chest.EnqueueBroadcast(new GameMessageScript(chest.Guid, PlayScript.EnchantDownPurple, 1.0f));

            if (stranger == null || stranger.IsDestroyed) return;

            // Tell everyone nearby (the chest is visible to anyone), but only fire
            // the line once per chest by gating on a property bool that we set here.
            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                $"Tick-tock, friend... {(int)ChestDespawnWarningSeconds} seconds and the chest is mine again.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

            if (owner?.Session != null)
            {
                owner.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"A Mysterious Stranger's chest will vanish in {(int)ChestDespawnWarningSeconds} seconds!",
                    ChatMessageType.Magic));
            }
        }

        /// <summary>
        /// Despawn entry point with grace handling. If the chest is currently OPEN
        /// (a player is browsing its inventory) we defer the despawn briefly so we
        /// don't yank the UI out from under them mid-loot.
        /// </summary>
        private static void TryDespawnChestSafely(WorldObject chest)
        {
            if (chest == null || chest.IsDestroyed)
            {
                if (chest != null) _strangerChestOwner.Remove(chest.Guid.Full);
                return;
            }

            if (chest is Container c && c.IsOpen)
            {
                var rechain = new ActionChain();
                rechain.AddDelaySeconds(ChestDespawnGraceSeconds);
                rechain.AddAction(chest, () => TryDespawnChestSafely(chest));
                rechain.EnqueueChain();
                return;
            }

            DespawnChest(chest);
        }

        private static void DespawnChest(WorldObject chest)
        {
            if (chest == null) return;
            _strangerChestOwner.Remove(chest.Guid.Full);
            if (!chest.IsDestroyed)
            {
                chest.EnqueueBroadcast(new GameMessageScript(chest.Guid, PlayScript.Destroy, 1.0f));
                chest.Destroy();
            }
        }

        private static void CleanupPlayerChests(uint playerGuid, Chest excludeChest = null)
        {
            if (!_playerChests.TryGetValue(playerGuid, out var bucket)) return;

            foreach (var guid in bucket)
            {
                if (excludeChest != null && excludeChest.Guid.Full == guid) continue;
                if (!_strangerChestOwner.TryGetValue(guid, out var sess)) continue;

                // Resolve the chest world object via the stranger's landblock if possible.
                var lb = sess.Stranger?.CurrentLandblock;
                var wo = lb?.GetObject(new ObjectGuid(guid));
                if (wo != null)
                    DespawnChest(wo);
                else
                    _strangerChestOwner.Remove(guid);
            }
            _playerChests.Remove(playerGuid);
        }

        /// <summary>
        /// Called from Chest.ActOnUse BEFORE the normal open path. Returns true if the
        /// open was blocked (chest is part of a stranger session and player is out of opens,
        /// or the chest isn't the player's). Returns false to allow the chest to open normally.
        ///
        /// When the call succeeds (returns false), the stranger reacts in-character based on
        /// the chest slot type — junk gets a hearty laugh, mid gets a nod, jackpot gets a cheer.
        /// When the player exhausts their opens, every remaining chest in their session despawns.
        /// </summary>
        public static bool TryConsumeChestOpen(Chest chest, Player player)
        {
            if (chest == null || player == null) return false;
            if (!_strangerChestOwner.TryGetValue(chest.Guid.Full, out var session)) return false;

            if (session.PlayerGuid != player.Guid.Full)
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

            // Mark this chest as opened and arm a "laugh-on-close" reaction. The Stranger waits
            // for the player to finish poking through their winnings before he heckles them.
            session.Opened = true;
            session.LaughOnClose = true;

            // Stranger reacts to what the player just opened.
            var stranger = session.Stranger;
            if (stranger != null && !stranger.IsDestroyed)
            {
                switch (session.Slot)
                {
                    case ChestSlot.Jackpot:
                        stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.Cheer));
                        stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                            "Hah! The dice love you, friend. The dice LOVE you!",
                            stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                        break;
                    case ChestSlot.MidTier:
                        stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.Nod));
                        stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                            "A respectable haul. The night is still young.",
                            stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                        break;
                    default:
                        stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.HeartyLaugh));
                        var heckle = JunkOneLiners[ThreadSafeRandom.Next(0, JunkOneLiners.Length - 1)];
                        stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                            heckle,
                            stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

                        // Roll for a prank on top of the heckle. Pranks fire on a small delay
                        // so the player has a beat to read the line before the chaos hits.
                        if (ThreadSafeRandom.Next(0.0f, 1.0f) < JunkPrankChance && Pranks.Count > 0)
                        {
                            var prank = Pranks[ThreadSafeRandom.Next(0, Pranks.Count - 1)];
                            var capturedStranger = stranger;
                            var capturedPlayer = player;
                            var pchain = new ActionChain();
                            pchain.AddDelaySeconds(1.2);
                            pchain.AddAction(stranger, () =>
                            {
                                if (capturedPlayer == null || capturedPlayer.Session == null) return;
                                try { prank.Run(capturedStranger, capturedPlayer); }
                                catch (Exception ex) { log.Error($"MysteriousStranger prank '{prank.Name}' failed: {ex}"); }
                            });
                            pchain.EnqueueChain();
                        }
                        break;
                }
            }

            var nowLeft = remaining - 1;
            player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Opens remaining: {nowLeft}.", ChatMessageType.Broadcast));

            // If the player still has opens left, the Stranger reshuffles the rest of the
            // unopened chests so the next pick is a fresh gamble.
            if (nowLeft > 0)
            {
                if (stranger != null && !stranger.IsDestroyed)
                {
                    stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                        "Oooo, not so quick. Let me give the rest a little... stir.",
                        stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                }

                var capturedPlayer = player.Guid.Full;
                var rchain = new ActionChain();
                rchain.AddDelaySeconds(1.0);
                rchain.AddAction(stranger ?? (WorldObject)chest, () =>
                {
                    ReshuffleRemainingChests(capturedPlayer, stranger);
                });
                rchain.EnqueueChain();
            }

            // When opens hit zero, the remaining chests vanish — this is the big "show's over" moment.
            if (nowLeft <= 0)
            {
                var capturedPlayer = player.Guid.Full;
                var capturedChest = chest;
                var chain = new ActionChain();
                chain.AddDelaySeconds(1.5); // let the just-opened chest's loot animation play first
                chain.AddAction(stranger ?? (WorldObject)chest, () =>
                {
                    if (stranger != null && !stranger.IsDestroyed)
                    {
                        stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.BowDeep));
                        stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                            "Our business is concluded. The rest belong to no one now.",
                            stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
                    }
                    CleanupPlayerChests(capturedPlayer, capturedChest);
                });
                chain.EnqueueChain();
            }

            return false;
        }

        /// <summary>
        /// Called from Chest.FinishClose for any stranger-owned chest. Lets the Stranger
        /// react when a player closes a chest he just opened (the "laugh after junk"
        /// payoff for the reshuffle bit).
        /// </summary>
        public static void OnStrangerChestClosed(Chest chest, Player player)
        {
            if (chest == null || player == null) return;
            if (!_strangerChestOwner.TryGetValue(chest.Guid.Full, out var session)) return;
            if (!session.LaughOnClose) return;
            if (session.PlayerGuid != player.Guid.Full) return;

            session.LaughOnClose = false;

            var stranger = session.Stranger;
            if (stranger == null || stranger.IsDestroyed)
                return;

            stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.HeartyLaugh));
            stranger.EnqueueBroadcast(new GameMessageHearSpeech(
                "Hehehe... go on then. Try another. Fortune is a fickle dance partner.",
                stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));
        }

        /// <summary>
        /// Re-rolls slot types (jackpot/mid/junk) and contents across all of the player's
        /// remaining UNOPENED stranger chests. Number of jackpots/mids/junks scales down
        /// with how many have already been opened so the distribution stays fair-ish.
        /// </summary>
        private static void ReshuffleRemainingChests(uint playerGuid, Creature stranger)
        {
            if (!_playerChests.TryGetValue(playerGuid, out var bucket) || bucket.Count == 0)
                return;

            var landblock = stranger?.CurrentLandblock;

            // Collect the still-spawned, still-unopened chests for this player.
            var unopened = new List<(Chest chest, ChestSession session)>();
            foreach (var guid in bucket)
            {
                if (!_strangerChestOwner.TryGetValue(guid, out var sess)) continue;
                if (sess.Opened) continue;

                var wo = landblock?.GetObject(new ObjectGuid(guid)) as Chest;
                if (wo == null || wo.IsDestroyed) continue;

                unopened.Add((wo, sess));
            }

            if (unopened.Count == 0)
                return;

            // Build a fresh slot distribution sized to whatever is still on the board.
            var newSlots = BuildRandomizedSlots(unopened.Count);

            for (var i = 0; i < unopened.Count; i++)
            {
                var (chest, sess) = unopened[i];
                var newKind = newSlots[i];
                sess.Slot = newKind;

                // Drop the existing contents so the next open rolls cleanly.
                var existing = new List<WorldObject>(chest.Inventory.Values);
                foreach (var item in existing)
                {
                    if (chest.TryRemoveFromInventory(item.Guid))
                        item.Destroy();
                }

                FillChest(chest, newKind);

                // Re-obfuscate burden so the reshuffled chest doesn't betray its new contents.
                ObfuscateBurden(chest);

                // Re-randomize the palette and admin debug tag so the visual hint matches.
                chest.PaletteTemplate = (int)RandomChestPalettes[ThreadSafeRandom.Next(0, RandomChestPalettes.Length - 1)];
                chest.Shade = (float)ThreadSafeRandom.Next(0, 1000) / 1000f;
                RetagAdminChestName(chest, newKind, playerGuid);

                // Visual puff so the player can SEE the stranger meddling with it.
                chest.EnqueueBroadcast(new GameMessageScript(chest.Guid, PlayScript.EnchantUpPurple, 1.0f));
            }

            // Now perform the cinematic cup-shuffle: visibly swap chest positions back
            // and forth a few times so the player can SEE the Stranger meddling with
            // them. The slot kinds were already re-rolled above, so wherever a chest
            // physically ends up after the shuffle is wherever its NEW contents live.
            PerformCupShuffle(stranger, unopened);
        }

        // ---- tuning for the cup-shuffle visual ----
        public static int   CupShuffleSwapCount     = 8;     // how many pair-swaps to perform
        public static float CupShuffleSwapInterval  = 0.35f; // seconds between swaps

        /// <summary>
        /// Cinematic "follow the cup" shuffle. Picks two random chests, swaps their
        /// positions with a small visual puff, and repeats a few times. Each swap
        /// fires on an ActionChain so the player sees the chests sliding around.
        /// </summary>
        private static void PerformCupShuffle(Creature stranger, List<(Chest chest, ChestSession session)> unopened)
        {
            if (stranger == null || unopened == null || unopened.Count < 2)
                return;

            var chests = new List<Chest>(unopened.Count);
            foreach (var entry in unopened)
                chests.Add(entry.chest);

            var chain = new ActionChain();

            // Opening flourish on the Stranger so the player knows something's coming.
            chain.AddAction(stranger, () =>
            {
                stranger.EnqueueBroadcast(new GameMessageScript(stranger.Guid, PlayScript.EnchantUpPurple, 1.0f));
                stranger.EnqueueBroadcastMotion(new Motion(stranger, MotionCommand.Point));
            });
            chain.AddDelaySeconds(0.4);

            for (var i = 0; i < CupShuffleSwapCount; i++)
            {
                chain.AddAction(stranger, () => SwapTwoChests(chests));
                chain.AddDelaySeconds(CupShuffleSwapInterval);
            }

            // Closing flourish on every remaining chest.
            chain.AddAction(stranger, () =>
            {
                foreach (var c in chests)
                {
                    if (c == null || c.IsDestroyed) continue;
                    c.EnqueueBroadcast(new GameMessageScript(c.Guid, PlayScript.EnchantUpPurple, 1.0f));
                }
            });

            chain.EnqueueChain();
        }

        /// <summary>
        /// Picks two distinct chests from <paramref name="chests"/> and swaps their
        /// world positions, emitting a small puff so the swap reads visually.
        /// </summary>
        private static void SwapTwoChests(List<Chest> chests)
        {
            if (chests == null || chests.Count < 2) return;

            var live = new List<Chest>(chests.Count);
            foreach (var c in chests)
                if (c != null && !c.IsDestroyed && c.Location != null) live.Add(c);
            if (live.Count < 2) return;

            var a = live[ThreadSafeRandom.Next(0, live.Count - 1)];
            Chest b;
            int guard = 0;
            do
            {
                b = live[ThreadSafeRandom.Next(0, live.Count - 1)];
            } while (b == a && ++guard < 8);
            if (b == a) return;

            var posA = new Position(a.Location);
            var posB = new Position(b.Location);

            a.Location = new Position(posB);
            b.Location = new Position(posA);

            a.SendUpdatePosition(true);
            b.SendUpdatePosition(true);

            a.EnqueueBroadcast(new GameMessageScript(a.Guid, PlayScript.EnchantUpPurple, 1.0f));
            b.EnqueueBroadcast(new GameMessageScript(b.Guid, PlayScript.EnchantUpPurple, 1.0f));
        }

        /// <summary>
        /// Strips any previous [JACKPOT]/[MID]/[JUNK] suffix from a chest's name and
        /// re-applies the correct one for admin players. No-op for non-admin owners.
        /// </summary>
        private static void RetagAdminChestName(Chest chest, ChestSlot kind, uint playerGuid)
        {
            // Only retag if the original opener was admin (the name already carries a tag).
            var name = chest.Name ?? string.Empty;
            var hadTag = name.EndsWith("[JACKPOT]") || name.EndsWith("[MID]") || name.EndsWith("[JUNK]");
            if (!hadTag) return;

            var idx = name.LastIndexOf('[');
            var baseName = idx > 0 ? name.Substring(0, idx).TrimEnd() : name;
            var tag = kind == ChestSlot.Jackpot ? "[JACKPOT]"
                    : kind == ChestSlot.MidTier ? "[MID]"
                                                : "[JUNK]";
            chest.Name = $"{baseName} {tag}";
        }

        /// <summary>
        /// Overrides the chest's reported burden with a random value within
        /// [<see cref="ObfuscatedBurdenMin"/>, <see cref="ObfuscatedBurdenMax"/>] so
        /// players can't tell jackpot from junk by appraising encumbrance.
        ///
        /// The chest itself is <see cref="PropertyBool.Stuck"/> = true (it can't be
        /// picked up), so changing the burden has no gameplay side effects beyond
        /// what the player sees in the appraisal panel.
        /// </summary>
        private static void ObfuscateBurden(WorldObject chest)
        {
            if (chest == null) return;
            var fake = ThreadSafeRandom.Next(ObfuscatedBurdenMin, ObfuscatedBurdenMax);
            chest.EncumbranceVal = fake;
            chest.ChangesDetected = true;
        }

        // ---------- slot / grid helpers ----------

        private static ChestSlot[] BuildRandomizedSlots()
        {
            return BuildRandomizedSlots(9);
        }

        /// <summary>
        /// Builds a shuffled slot distribution sized to <paramref name="count"/>, scaling
        /// the jackpot/mid/junk counts proportionally so reshuffles of a partial board
        /// still feel fair. Always guarantees at least one jackpot eligibility if count &gt;= 3.
        /// </summary>
        private static ChestSlot[] BuildRandomizedSlots(int count)
        {
            if (count <= 0) return Array.Empty<ChestSlot>();

            // Base ratio: 1 jackpot / 2 mid / 6 junk out of 9.
            var jackpot = Math.Max(count >= 3 ? 1 : 0, (int)Math.Round(count * (1.0 / 9.0)));
            var mid     = Math.Max(0, (int)Math.Round(count * (2.0 / 9.0)));
            var junk    = count - jackpot - mid;
            if (junk < 0) { mid += junk; junk = 0; if (mid < 0) { jackpot += mid; mid = 0; } }

            var slots = new List<ChestSlot>(count);
            for (var i = 0; i < jackpot; i++) slots.Add(ChestSlot.Jackpot);
            for (var i = 0; i < mid;     i++) slots.Add(ChestSlot.MidTier);
            for (var i = 0; i < junk;    i++) slots.Add(ChestSlot.Junk);

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
                    var p = new Position(center);
                    p.PositionX = center.PositionX + dx * ChestGridSpacing;
                    p.PositionY = center.PositionY + dy * ChestGridSpacing;
                    p.PositionZ = center.PositionZ + 0.05f;
                    p.SetLandblock();
                    p.SetLandCell();
                    positions[idx++] = p;
                }
            }
            return positions;
        }

        // ---------- chest loot fills ----------

        private static void FillChest(Container chest, ChestSlot slot)
        {
            switch (slot)
            {
                case ChestSlot.Jackpot: FillJackpot(chest); break;
                case ChestSlot.MidTier: FillMidTier(chest); break;
                default:                FillJunk(chest);    break;
            }
        }

        private static void FillJackpot(Container chest)
        {
            var profile = new TreasureDeath
            {
                Tier = 8,
                LootQualityMod = 1.0f,
                MagicItemChance = 100, MagicItemMinAmount = 15, MagicItemMaxAmount = 15,
                // Required so RollItemType picks an actual TreasureItemType instead of Undef.
                // Selection-chance tables are indexed by tier (1..8) in the same way live
                // treasure_death rows are configured.
                MagicItemTreasureTypeSelectionChances = 8,
                ItemTreasureTypeSelectionChances = 8,
                MundaneItemTypeSelectionChances = 8,
            };

            for (var i = 0; i < 15; i++)
            {
                var wo = LootGenerationFactory.CreateRandomLootObjects(profile, TreasureItemCategory.MagicItem);
                if (wo != null && !chest.TryAddToInventory(wo))
                    wo.Destroy();
            }
        }

        private static void FillMidTier(Container chest)
        {
            var tier = ThreadSafeRandom.Next(4, 6);
            var profile = new TreasureDeath
            {
                Tier = tier,
                LootQualityMod = 0,
                MagicItemChance = 100, MagicItemMinAmount = 1, MagicItemMaxAmount = 3,
                MundaneItemChance = 100, MundaneItemMinAmount = 1, MundaneItemMaxAmount = 2,
                // Required so RollItemType picks an actual TreasureItemType instead of Undef.
                MagicItemTreasureTypeSelectionChances = tier,
                ItemTreasureTypeSelectionChances = tier,
                MundaneItemTypeSelectionChances = tier,
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

        private static void FillJunk(Container chest)
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

        // ---------- prank spawn helpers ----------

        /// <summary>
        /// Spawns <paramref name="count"/> copies of <paramref name="wcid"/> high above the
        /// player, spread out over <paramref name="durationSeconds"/>, so they rain down on
        /// the player's head. Used for the cheese-rain prank.
        /// </summary>
        private static void DropFromSky(Creature stranger, Player player, uint wcid, int count, float heightFt, float durationSeconds, string line)
        {
            if (player == null || stranger == null) return;

            if (!string.IsNullOrEmpty(line))
                stranger.EnqueueBroadcast(new GameMessageHearSpeech(line, stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

            // Convert feet to meters. 1 ft = 0.3048 m.
            var heightMeters = heightFt * 0.3048f;
            var interval = count > 0 ? durationSeconds / count : 0f;

            for (var i = 0; i < count; i++)
            {
                var idx = i;
                var chain = new ActionChain();
                chain.AddDelaySeconds(idx * interval);
                chain.AddAction(stranger, () =>
                {
                    if (player == null || player.Session == null || player.Location == null) return;
                    SpawnAtOffset(player.Location, wcid,
                        (float)ThreadSafeRandom.Next(-2.0f, 2.0f),
                        (float)ThreadSafeRandom.Next(-2.0f, 2.0f),
                        heightMeters);
                });
                chain.EnqueueChain();
            }
        }

        /// <summary>
        /// Spawns a swarm of <paramref name="wcid"/> in a ring around the player. Used for
        /// the mites/rats/drudges pranks.
        /// </summary>
        private static void SpawnSwarmAroundPlayer(Creature stranger, Player player, uint wcid, int count, float radius, string line)
        {
            if (player == null || stranger == null) return;

            if (!string.IsNullOrEmpty(line))
                stranger.EnqueueBroadcast(new GameMessageHearSpeech(line, stranger.Name, stranger.Guid.Full, ChatMessageType.Speech));

            for (var i = 0; i < count; i++)
            {
                var angle = (float)(i * (Math.PI * 2.0 / Math.Max(1, count)));
                var jitter = (float)ThreadSafeRandom.Next(0.5f, 1.0f);
                var dx = (float)Math.Cos(angle) * radius * jitter;
                var dy = (float)Math.Sin(angle) * radius * jitter;
                SpawnAtOffset(player.Location, wcid, dx, dy, 0.5f);
            }
        }

        /// <summary>
        /// Creates a WorldObject from a WCID and places it at an offset from
        /// <paramref name="anchor"/>. Returns null on failure.
        /// </summary>
        private static WorldObject SpawnAtOffset(Position anchor, uint wcid, float dx, float dy, float dz)
        {
            if (anchor == null) return null;

            WorldObject wo;
            try { wo = WorldObjectFactory.CreateNewWorldObject(wcid); }
            catch (Exception ex) { log.Error($"MysteriousStranger SpawnAtOffset WCID {wcid} create failed: {ex}"); return null; }

            if (wo == null) return null;

            var pos = new Position(anchor);
            pos.PositionX += dx;
            pos.PositionY += dy;
            pos.PositionZ += dz;
            pos.LandblockId = new LandblockId(pos.GetCell());

            wo.Location = pos;

            // Creatures should be considered "generated" so they clean up naturally; we also
            // tag them with a short rot so the world doesn't get cluttered with cheese.
            wo.TimeToRot = 60.0;

            if (!LandblockManager.AddObject(wo, true))
            {
                wo.Destroy();
                return null;
            }
            return wo;
        }
    }
}
