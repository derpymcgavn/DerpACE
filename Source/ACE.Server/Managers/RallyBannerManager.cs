using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers
{
    public static class RallyBannerManager
    {
        private const int RallyBannerCooldownId = 2060;

        private static readonly ConcurrentDictionary<uint, ActiveBanner> ActiveByOwner = new ConcurrentDictionary<uint, ActiveBanner>();

        public static bool TryUse(Player player, WorldObject source)
        {
            if (!DerpACEConfig.RallyBannerEnabled || source?.WeenieClassId != DerpACEConfig.RallyBannerItemWcid)
                return false;

            if (player == null || player.Location == null || player.CurrentLandblock == null || player.IsDead || player.Teleporting)
            {
                player?.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "You cannot plant a rally banner here."));
                return true;
            }

            if (!CheckRequirements(player))
                return true;

            if (ActiveByOwner.TryGetValue(player.Guid.Full, out var existing) && IsActive(existing))
            {
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "Your rally banner is already planted."));
                return true;
            }
            ActiveByOwner.TryRemove(player.Guid.Full, out _);

            var banner = CreateBanner(player);
            if (banner == null || !LandblockManager.AddObject(banner, true))
            {
                banner?.Destroy();
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The banner refuses to take hold here."));
                return true;
            }

            source.CooldownId = RallyBannerCooldownId;
            source.CooldownDuration = Math.Max(1, DerpACEConfig.RallyBannerCooldownSeconds);
            player.EnchantmentManager.StartCooldown(source);

            var active = new ActiveBanner(player.Guid.Full, banner, DateTime.UtcNow.AddSeconds(Math.Max(5, DerpACEConfig.RallyBannerDurationSeconds)));
            ActiveByOwner[player.Guid.Full] = active;

            banner.ApplyVisualEffects(PlayScript.EnchantUpYellow);
            player.Session?.Network.EnqueueSend(new GameMessageSystemChat("You plant a rally banner. Your fellowship gathers strength around it.", ChatMessageType.Broadcast));
            Pulse(active);
            ScheduleNextPulse(active);
            return true;
        }

        private static bool CheckRequirements(Player player)
        {
            var requiredLevel = Math.Max(1, DerpACEConfig.RallyBannerRequiredLevel);
            if ((player.Level ?? 1) < requiredLevel)
            {
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"You must be at least level {requiredLevel} to plant a rally banner."));
                return false;
            }

            if (DerpACEConfig.RallyBannerRequiresLeadership && player.GetCreatureSkill(Skill.Leadership).AdvancementClass < SkillAdvancementClass.Trained)
            {
                player.Session?.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "You must have Leadership trained to plant a rally banner."));
                return false;
            }

            return true;
        }

        private static WorldObject CreateBanner(Player player)
        {
            var banner = WorldObjectFactory.CreateNewWorldObject(DerpACEConfig.RallyBannerVisualWcid)
                ?? WorldObjectFactory.CreateNewWorldObject(16920);

            if (banner == null)
                return null;

            banner.Name = $"{player.Name}'s Rally Banner";
            banner.Location = player.Location.InFrontOf(1.5f, true);
            banner.GeneratorId = player.Guid.Full;
            banner.TimeToRot = Math.Max(5, DerpACEConfig.RallyBannerDurationSeconds + 5);
            banner.ItemUseable = Usable.No;
            banner.Attackable = false;
            banner.Ethereal = true;
            banner.IgnoreCollisions = true;
            banner.ReportCollisions = false;
            banner.GravityStatus = false;
            banner.Static = true;
            banner.SetProperty(PropertyInt.PhysicsState, (int)(PhysicsState.Static | PhysicsState.Ethereal | PhysicsState.IgnoreCollisions));
            banner.SetProperty(PropertyDataId.Setup, 0x02000CDB);
            banner.SetProperty(PropertyDataId.SoundTable, 0x20000014);
            banner.SetProperty(PropertyDataId.PaletteBase, 0x04001379);
            banner.SetProperty(PropertyDataId.ClothingBase, 0x100003A7);
            banner.SetProperty(PropertyDataId.Icon, 0x060023A8);
            banner.SetProperty(PropertyDataId.PhysicsEffectTable, 0x3400002B);
            banner.SetProperty(PropertyInt.PaletteTemplate, 61);
            banner.SetProperty(PropertyFloat.Shade, 0.0);

            return banner;
        }

        private static void ScheduleNextPulse(ActiveBanner active)
        {
            var delay = Math.Max(1, DerpACEConfig.RallyBannerPulseSeconds);
            var chain = new ActionChain();
            chain.AddDelaySeconds(delay);
            chain.AddAction(active.Banner, () =>
            {
                if (!IsActive(active))
                {
                    End(active);
                    return;
                }

                Pulse(active);
                ScheduleNextPulse(active);
            });
            chain.EnqueueChain();
        }

        private static bool IsActive(ActiveBanner active)
        {
            return active != null
                && active.Banner != null
                && !active.Banner.IsDestroyed
                && DateTime.UtcNow < active.ExpiresUtc
                && ActiveByOwner.TryGetValue(active.OwnerGuid, out var current)
                && ReferenceEquals(active, current);
        }

        private static void Pulse(ActiveBanner active)
        {
            var owner = PlayerManager.GetOnlinePlayer(active.OwnerGuid);
            if (owner == null || owner.IsDead)
            {
                End(active);
                return;
            }

            foreach (var target in GetEligibleTargets(owner, active.Banner))
                ApplyAura(target, active.Banner);

            active.Banner.ApplyVisualEffects(PlayScript.EnchantUpYellow, 0.35f);
        }

        private static IEnumerable<Player> GetEligibleTargets(Player owner, WorldObject banner)
        {
            if (banner?.Location == null)
                yield break;

            var radius = Math.Max(1, DerpACEConfig.RallyBannerRadius);
            var radiusSq = radius * radius;
            var fellowGuids = new HashSet<uint> { owner.Guid.Full };
            if (owner.Fellowship?.FellowshipMembers != null)
            {
                foreach (var member in owner.Fellowship.FellowshipMembers.Values)
                    if (member.TryGetTarget(out var fellow) && fellow != null)
                        fellowGuids.Add(fellow.Guid.Full);
            }

            foreach (var player in PlayerManager.GetAllOnline())
            {
                if (player?.Location == null || player.IsDead || player.Teleporting || !fellowGuids.Contains(player.Guid.Full))
                    continue;
                if (player.Location.Landblock != banner.Location.Landblock || banner.Location.Distance2DSquared(player.Location) > radiusSq)
                    continue;

                yield return player;
            }
        }

        private static void ApplyAura(Player player, WorldObject banner)
        {
            ApplyAuraSpell(player, banner, CustomSpellManager.RallyBannerMightSpellId);
            ApplyAuraSpell(player, banner, CustomSpellManager.RallyBannerGuardSpellId);
            player.ApplyVisualEffects(PlayScript.EnchantUpYellow, 0.2f);
        }

        private static void ApplyAuraSpell(Player player, WorldObject banner, uint spellId)
        {
            var spell = new Spell(spellId);
            if (spell.NotFound)
                return;

            var result = player.EnchantmentManager.Add(spell, banner, null);
            if (result?.Enchantment != null)
                player.Session?.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, result.Enchantment)));
        }

        private static void End(ActiveBanner active)
        {
            if (active == null)
                return;

            ActiveByOwner.TryRemove(active.OwnerGuid, out _);
            if (active.Banner == null || active.Banner.IsDestroyed)
                return;

            active.Banner.ApplyVisualEffects(PlayScript.PortalEntry);
            var chain = new ActionChain();
            chain.AddDelaySeconds(1.0);
            chain.AddAction(active.Banner, () =>
            {
                if (!active.Banner.IsDestroyed)
                    active.Banner.Destroy();
            });
            chain.EnqueueChain();
        }

        private sealed class ActiveBanner
        {
            public ActiveBanner(uint ownerGuid, WorldObject banner, DateTime expiresUtc)
            {
                OwnerGuid = ownerGuid;
                Banner = banner;
                ExpiresUtc = expiresUtc;
            }

            public uint OwnerGuid { get; }
            public WorldObject Banner { get; }
            public DateTime ExpiresUtc { get; }
        }
    }
}