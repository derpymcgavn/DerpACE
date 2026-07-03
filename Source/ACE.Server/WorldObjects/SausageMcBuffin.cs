using System;
using System.Collections.Generic;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// DerpACE: Sausage McBuffin (WCID 2000500) — A pancake golem buff-bot NPC.
    ///
    /// When a player uses him, he casts the full Level 7 life / creature / item buff
    /// suite on them (attributes, vitals, regens, masteries, protections, plus
    /// Impenetrability 7 and all 7th-level banes on their armor). Speaks in old internet
    /// leet speak because he's a sentient breakfast item with an attitude.
    ///
    /// Cooldown is enforced per-player so spamming him doesn't flood the player
    /// with thousands of cast messages.
    /// </summary>
    public static class SausageMcBuffin
    {
        public const uint WeenieClassId = 2000500;

        // Per-player cooldown so the NPC can't be spam-activated.
        public static double CooldownSeconds = 10.0;

        // Time between individual spell casts (in seconds). Keeps the chat log
        // from being a single wall of text and gives the FX time to play.
        private const double CastInterval = 0.10;

        // Maximum distance a player can be from Sausage to still get buffed.
        private const float UseRange = 6.0f;

        private static readonly Dictionary<uint, double> _lastUseTime = new Dictionary<uint, double>();

        // Level-7 creature-school buffs (attributes, vitals, regens, masteries).
        private static readonly SpellId[] CreatureBuffs = new[]
        {
            // Attributes
            SpellId.StrengthOther7,
            SpellId.EnduranceOther7,
            SpellId.CoordinationOther7,
            SpellId.QuicknessOther7,
            SpellId.FocusOther7,
            SpellId.WillpowerOther7,

            // Vital regeneration
            SpellId.RegenerationOther7,
            SpellId.RejuvenationOther7,
            SpellId.ManaRenewalOther7,

            // Defense
            SpellId.ArmorOther7,
            SpellId.ImpregnabilityOther7,
            SpellId.InvulnerabilityOther7,
            SpellId.MagicResistanceOther7,

            // Weapon / combat masteries
            SpellId.LightWeaponsMasteryOther7,
            SpellId.HeavyWeaponsMasteryOther7,
            SpellId.FinesseWeaponsMasteryOther7,
            SpellId.MissileWeaponsMasteryOther7,
            SpellId.ThrownWeaponMasteryOther7,
            SpellId.MaceMasteryOther7,
            SpellId.SpearMasteryOther7,
            SpellId.StaffMasteryOther7,
            SpellId.UnarmedCombatMasteryOther7,
            SpellId.CrossbowMasteryOther7,
            SpellId.WeaponExpertiseOther7,

            // Magic schools
            SpellId.WarMagicMasteryOther7,
            SpellId.LifeMagicMasteryOther7,
            SpellId.CreatureEnchantmentMasteryOther7,
            SpellId.ItemEnchantmentMasteryOther7,
            SpellId.ArcaneEnlightenmentOther7,
            SpellId.ManaMasteryOther7,

            // Utility / awareness
            SpellId.ArmorExpertiseOther7,
            SpellId.ItemExpertiseOther7,
            SpellId.MagicItemExpertiseOther7,
            SpellId.MonsterAttunementOther7,
            SpellId.PersonAttunementOther7,
            SpellId.DeceptionMasteryOther7,
            SpellId.LeadershipMasteryOther7,
            SpellId.FealtyOther7,
            SpellId.SprintOther7,
            SpellId.JumpingMasteryOther7,
            SpellId.HealingMasteryOther7,
            SpellId.AlchemyMasteryOther7,
            SpellId.CookingMasteryOther7,
            SpellId.FletchingMasteryOther7,
            SpellId.LockpickMasteryOther7,
        };

        // Level-7 life-school buffs (instant restoration + protections).
        private static readonly SpellId[] LifeBuffs = new[]
        {
            SpellId.HealOther7,
            SpellId.RevitalizeOther7,
            SpellId.ManaBoostOther7,

            SpellId.AcidProtectionOther7,
            SpellId.BladeProtectionOther7,
            SpellId.BludgeonProtectionOther7,
            SpellId.ColdProtectionOther7,
            SpellId.FireProtectionOther7,
            SpellId.LightningProtectionOther7,
            SpellId.PiercingProtectionOther7,
        };

        // Level-7 item-enchantment buffs that apply to armor (Impen + Banes).
        // These rely on the broadened cross-target handling in WorldObject_Magic
        // so a single cast walks every equipped vestment piece on the target.
        private static readonly SpellId[] ItemArmorBuffs = new[]
        {
            SpellId.Impenetrability7,
            SpellId.AcidBane7,
            SpellId.BladeBane7,
            SpellId.BludgeonBane7,
            SpellId.FlameBane7,
            SpellId.FrostBane7,
            SpellId.LightningBane7,
            SpellId.PiercingBane7,
        };

        // Level-7 weapon imbues (apply to the target's equipped weapon).
        // Blood Drinker (damage), Heart Seeker (accuracy), Defender (defense),
        // Swift Killer (attack speed), Spirit Drinker (mana-weapon damage),
        // and Hermetic Link (the mana-seeker imbue for casters).
        private static readonly SpellId[] WeaponBuffs = new[]
        {
            SpellId.BloodDrinkerOther7,
            SpellId.HeartSeekerOther7,
            SpellId.DefenderOther7,
            SpellId.SwiftKillerOther7,
            SpellId.SpiritDrinkerOther7,
            SpellId.HermeticLinkOther7,
        };

        public static void OnUse(Creature npc, Player player)
        {
            if (npc == null || player == null) return;

            if (player.IsDead || npc.IsDead)
                return;

            // Reject hardcore and ironman characters
            if (player.GetProperty(PropertyBool.IsHardcore) ?? false)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{npc.Name} flips dramatically. \"w00t!!! ur h4rdcore br0? i c4nnot h3lp d00dz l1k3 j00!!!!\"",
                    ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            if (player.IsIronmanFamily)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{npc.Name} melts slightly. \"n4h n4h n4h... ur ironm4n d00d. gr1nd it y0urself!!!!!!11one\"",
                    ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }

            // Range check — use radius might not be set on the weenie.
            if (npc.Location != null && player.Location != null)
            {
                var distSq = npc.Location.SquaredDistanceTo(player.Location);
                if (distSq > UseRange * UseRange)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                        $"{npc.Name} jiggles indignantly. \"g3t clos3r n00b!! i c4nnot buff j00 from th3 n3xt c0unt33\"",
                        ChatMessageType.Tell));
                    player.SendUseDoneEvent();
                    return;
                }
            }

            // Per-player cooldown
            var now = Time.GetUnixTime();
            if (_lastUseTime.TryGetValue(player.Guid.Full, out var last) && now - last < CooldownSeconds)
            {
                var remaining = Math.Max(1, (int)Math.Ceiling(CooldownSeconds - (now - last)));
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{npc.Name} steams angrily. \"dood!!! w41t {remaining} m0r3 s3c0ndz ur buff3d m0nst3r!!!!!11\"",
                    ChatMessageType.Tell));
                player.SendUseDoneEvent();
                return;
            }
            _lastUseTime[player.Guid.Full] = now;

            npc.EnqueueBroadcast(new GameMessageHearSpeech(
                $"h4v3 sum buffz n00b!!! p4nc4k3z 0f p0w3r!!!11oneone",
                npc.Name, npc.Guid.Full, ChatMessageType.Speech), WorldObject.LocalBroadcastRange);

            // Stagger the casts so the client doesn't drown in spell FX in a single frame.
            var chain = new ActionChain();
            var delay = 0.0;

            void EnqueueCast(SpellId spellId)
            {
                chain.AddDelaySeconds(CastInterval);
                delay += CastInterval;
                chain.AddAction(npc, () =>
                {
                    if (player.IsDead || npc.IsDead) return;

                    var spell = new Spell(spellId);
                    if (spell.NotFound) return;

                    // Use TryCastItemEnchantment_WithRedirects for armor/impen/bane spells
                    // to ensure they apply to all equipped armor pieces, not just the player
                    if (spell.IsImpenBaneType)
                        npc.TryCastItemEnchantment_WithRedirects(spell, player, npc);
                    else
                        npc.TryCastSpell_WithRedirects(spell, player, npc, null, false, false, false);
                });
            }

            foreach (var s in LifeBuffs)
                EnqueueCast(s);

            foreach (var s in CreatureBuffs)
                EnqueueCast(s);

            foreach (var s in ItemArmorBuffs)
                EnqueueCast(s);

            foreach (var s in WeaponBuffs)
                EnqueueCast(s);

            chain.AddAction(npc, () =>
            {
                if (player.IsDead) return;
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $"{npc.Name} gives a crispy thumbs up. \"n0 h4x n33d3d, j00 iz buff br0zk11!!!oneone n0w g0 pwn s0m3 d00dz!!\"",
                    ChatMessageType.Tell));
            });

            chain.EnqueueChain();

            player.SendUseDoneEvent();
        }
    }
}
