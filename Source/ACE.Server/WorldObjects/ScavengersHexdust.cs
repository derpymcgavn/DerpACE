using System;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public static class ScavengersHexdust
    {
        public const uint ScavengersMortarWeenieClassId = 2000609;
        public const uint ScavengersHexdustWeenieClassId = 2000610;

        private static readonly SpellId[] ImperilByTier =
        {
            SpellId.ImperilOther1,
            SpellId.ImperilOther2,
            SpellId.ImperilOther3,
            SpellId.ImperilOther4,
            SpellId.ImperilOther5,
            SpellId.ImperilOther6,
            SpellId.ImperilOther7,
            SpellId.ImperilOther8,
        };

        public static bool TryUse(Player player, WorldObject source, WorldObject target)
        {
            if (player == null || source == null)
                return false;

            if (source.WeenieClassId == ScavengersMortarWeenieClassId)
                return TryHarvest(player, target);

            if (source.WeenieClassId == ScavengersHexdustWeenieClassId)
                return TryThrow(player, source, target);

            return false;
        }

        private static bool TryHarvest(Player player, WorldObject target)
        {
            if (!ValidateNomadAssess(player))
            {
                player.SendUseDoneEvent();
                return true;
            }

            if (!(target is Corpse corpse) || !corpse.IsMonster)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The mortar only finds useful hexdust in monster corpses."));
                player.SendUseDoneEvent();
                return true;
            }

            if (corpse.GetProperty(PropertyBool.CorpseHexdustHarvested) == true)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "That corpse has already been scraped clean."));
                player.SendUseDoneEvent();
                return true;
            }

            if (corpse.KillerId.HasValue && corpse.KillerId.Value != player.Guid.Full)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "That corpse is not yours to scavenge."));
                player.SendUseDoneEvent();
                return true;
            }

            var assess = player.GetCreatureSkill(Skill.AssessCreature).Current;
            var chance = Math.Clamp(0.60 + (assess / 1000.0), 0.60, 0.95);
            corpse.SetProperty(PropertyBool.CorpseHexdustHarvested, true);
            corpse.SaveBiotaToDatabase();

            if (ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("You scrape the corpse, but the grit will not hold a hex.", ChatMessageType.Broadcast));
                player.SendUseDoneEvent();
                return true;
            }

            var amount = GetHarvestAmount(player, corpse);
            var dust = WorldObjectFactory.CreateNewWorldObject(ScavengersHexdustWeenieClassId);
            if (dust == null)
            {
                player.SendUseDoneEvent();
                return true;
            }

            dust.SetStackSize(amount);
            dust.SetProperty(PropertyBool.IsIronmanItem, true);

            if (!player.TryCreateInInventoryWithNetworking(dust))
            {
                dust.Location = new ACE.Entity.Position(player.Location);
                ACE.Server.Managers.LandblockManager.AddObject(dust);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{dust.Name} falls to the ground because your pack is full.", ChatMessageType.Broadcast));
            }
            else
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You grind {amount} pinches of {dust.Name} from the corpse.", ChatMessageType.Broadcast));

            player.SendUseDoneEvent();
            return true;
        }

        private static bool TryThrow(Player player, WorldObject dust, WorldObject target)
        {
            if (!ValidateNomadAssess(player))
            {
                player.SendUseDoneEvent();
                return true;
            }

            if (!(target is Creature creature) || creature is Player || !creature.IsAlive)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "Hexdust needs a living monster to bite."));
                player.SendUseDoneEvent();
                return true;
            }

            var tier = GetImperilTier(player);
            var spell = new Spell((uint)ImperilByTier[tier - 1]);
            if (spell.NotFound)
            {
                player.SendUseDoneEvent();
                return true;
            }

            player.TryCastSpell(spell, creature, dust, tryResist: true);
            player.TryConsumeFromInventoryWithNetworking(dust, 1);
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You throw {dust.Name}, reading {creature.Name}'s weak points into the dust.", ChatMessageType.Broadcast));
            player.SendUseDoneEvent();
            return true;
        }

        private static bool ValidateNomadAssess(Player player)
        {
            if (player?.IsIronmanNomad != true)
            {
                player?.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "Only Ironman Nomads know how to work Scavenger's Hexdust."));
                return false;
            }

            if (player.GetCreatureSkill(Skill.AssessCreature).AdvancementClass < SkillAdvancementClass.Trained)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "You must have Assess Creature trained to work Scavenger's Hexdust."));
                return false;
            }

            return true;
        }

        private static int GetImperilTier(Player player)
        {
            var assess = player.GetCreatureSkill(Skill.AssessCreature).Current;

            if (assess >= 550) return 8;
            if (assess >= 450) return 7;
            if (assess >= 350) return 6;
            if (assess >= 250) return 5;
            if (assess >= 150) return 4;
            if (assess >= 75) return 3;
            return 2;
        }

        private static int GetHarvestAmount(Player player, Corpse corpse)
        {
            var assess = player.GetCreatureSkill(Skill.AssessCreature).Current;
            var level = corpse.Level ?? 1;
            var amount = 1 + (level / 50) + ((int)assess / 200);

            return Math.Clamp(amount, 1, 10);
        }
    }
}
