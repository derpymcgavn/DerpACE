using System;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public static class ScavengersHexdust
    {
        public const uint MundaneMortarAndPestleWeenieClassId = 4751;
        public const uint BoneGrinderWeenieClassId = 2000619;
        public const uint ScavengersMortarWeenieClassId = 2000609;
        public const uint ScavengersHexdustWeenieClassId = 2000610;

        private const int ScavengerTrialRequiredBones = 15;
        private const int ScavengerTrialLevelWindow = 25;

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

        private static readonly string[] HexWords =
        {
            "Vosh du kael.",
            "Mordo vek nesh.",
            "Zuthra kai mal.",
            "Drego suth var.",
            "Kesh voh duma.",
            "Nethra zal ven.",
        };

        public static bool TryUse(Player player, WorldObject source, WorldObject target)
        {
            if (player == null || source == null)
                return false;

            if (source.WeenieClassId == BoneGrinderWeenieClassId || source.WeenieClassId == MundaneMortarAndPestleWeenieClassId)
                return TryScavengerTrial(player, source, target);

            if (source.WeenieClassId == ScavengersMortarWeenieClassId)
                return TryHarvest(player, target);

            if (source.WeenieClassId == ScavengersHexdustWeenieClassId)
                return TryThrow(player, source, target);

            return false;
        }

        public static void EnsureBoneGrinderFor(Player player)
        {
            if (player?.IsIronmanNomad != true || IsScavengerTrialComplete(player))
                return;

            foreach (var item in player.GetAllPossessions())
            {
                if (item.WeenieClassId == BoneGrinderWeenieClassId || item.WeenieClassId == ScavengersMortarWeenieClassId)
                    return;
            }

            var grinder = WorldObjectFactory.CreateNewWorldObject(BoneGrinderWeenieClassId);
            if (grinder == null)
                return;

            grinder.SetProperty(PropertyBool.IsIronmanItem, true);
            grinder.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceIronman);

            if (player.TryCreateInInventoryWithNetworking(grinder))
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat("A Bone Grinder has been tucked into your pack for the Scavenger path.", ChatMessageType.Broadcast));
        }
        private static bool TryScavengerTrial(Player player, WorldObject mundaneMortar, WorldObject target)
        {
            if (!ValidateNomadAssess(player))
            {
                player.SendUseDoneEvent();
                return true;
            }

            if (IsScavengerTrialComplete(player))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "You have already earned a Scavenger's Mortar. Use that tool to grind hexdust."));
                player.SendUseDoneEvent();
                return true;
            }

            if (!TryValidateTrialCorpse(player, target, out var corpse, out var creatureType, out var creatureName))
            {
                player.SendUseDoneEvent();
                return true;
            }

            var storedType = player.GetProperty(PropertyInt.NomadScavengerTrialCreatureType);
            if (storedType == null || storedType.Value <= 0)
            {
                storedType = (int)creatureType;
                player.SetProperty(PropertyInt.NomadScavengerTrialCreatureType, storedType.Value);
                player.SetProperty(PropertyInt.NomadScavengerTrialProgress, 0);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You barter with the road: fifteen sets of {creatureName} bones for a true Scavenger's Mortar.", ChatMessageType.Broadcast));
            }
            else if (storedType.Value != (int)creatureType)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"The trial has already named its bones: bring {GetCreatureTypeName((CreatureType)storedType.Value)} remains."));
                player.SendUseDoneEvent();
                return true;
            }

            corpse.SetProperty(PropertyBool.CorpseHexdustHarvested, true);
            corpse.SaveBiotaToDatabase();

            var progress = Math.Clamp((player.GetProperty(PropertyInt.NomadScavengerTrialProgress) ?? 0) + 1, 0, ScavengerTrialRequiredBones);
            player.SetProperty(PropertyInt.NomadScavengerTrialProgress, progress);
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.NomadScavengerTrialCreatureType, storedType.Value),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.NomadScavengerTrialProgress, progress));

            if (progress < ScavengerTrialRequiredBones)
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You grind the bones into road-grit. Scavenger trial: {progress}/{ScavengerTrialRequiredBones} {creatureName} remains.", ChatMessageType.Broadcast));
                player.SendUseDoneEvent();
                return true;
            }

            CompleteScavengerTrial(player, mundaneMortar, creatureName);
            return true;
        }

        private static bool TryHarvest(Player player, WorldObject target)
        {
            if (!ValidateNomadAssess(player))
            {
                player.SendUseDoneEvent();
                return true;
            }

            if (!IsScavengerTrialComplete(player))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "This mortar has not accepted you yet. Buy a plain mortar and pestle and grind the trial bones first."));
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
            ApplyHexdustSpellcraft(player, dust);

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
            var customSpellId = CustomSpellManager.HexdustSpellIdFirst + (uint)(tier - 1);
            var spell = new Spell(customSpellId);
            if (spell.NotFound)
                spell = new Spell((uint)ImperilByTier[tier - 1]);

            if (spell.NotFound)
            {
                player.SendUseDoneEvent();
                return true;
            }

            ApplyHexdustSpellcraft(player, dust);

            var throwChain = new ActionChain();
            var prevStance = player.CurrentMotionState?.Stance ?? MotionStance.NonCombat;

            player.IsBusy = true;

            if (prevStance != MotionStance.NonCombat)
                player.EnqueueMotion_Force(throwChain, MotionStance.NonCombat, MotionCommand.Ready, (MotionCommand)prevStance);

            player.EnqueueMotion_Force(throwChain, MotionStance.ThrownWeaponCombat, MotionCommand.Ready, MotionCommand.NonCombat);
            player.EnqueueMotion_Force(throwChain, MotionStance.ThrownWeaponCombat, MotionCommand.AimLevel, null, 1.0f, 0.75f);

            throwChain.AddAction(player, () =>
            {
                if (player.IsDestroyed || dust.IsDestroyed || creature.IsDestroyed || !player.IsAlive || !creature.IsAlive)
                {
                    player.IsBusy = false;
                    player.SendUseDoneEvent();
                    return;
                }

                SayHexWords(player);
                player.ApplyVisualEffects(PlayScript.EnchantUpGreen, 0.8f);
                creature.ApplyVisualEffects(PlayScript.RestrictionEffectGreen, 1.0f);
                creature.ApplyVisualEffects(PlayScript.SkillDownGreen, 0.85f);

                player.TryCastSpell(spell, creature, dust, tryResist: true);
                player.TryConsumeFromInventoryWithNetworking(dust, 1);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You throw {dust.Name}, reading {creature.Name}'s weak points into the dust.", ChatMessageType.Broadcast));
            });

            player.EnqueueMotion_Force(throwChain, MotionStance.NonCombat, MotionCommand.Ready, MotionCommand.ThrownWeaponCombat);

            if (prevStance != MotionStance.NonCombat)
                player.EnqueueMotion_Force(throwChain, prevStance, MotionCommand.Ready, MotionCommand.NonCombat);

            throwChain.AddAction(player, () =>
            {
                player.IsBusy = false;
                player.SendUseDoneEvent();
            });
            throwChain.EnqueueChain();
            return true;
        }

        private static bool TryValidateTrialCorpse(Player player, WorldObject target, out Corpse corpse, out CreatureType creatureType, out string creatureName)
        {
            corpse = target as Corpse;
            creatureType = CreatureType.Invalid;
            creatureName = "monster";

            if (corpse == null || !corpse.IsMonster)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The trial only listens to monster bones."));
                return false;
            }

            if (corpse.GetProperty(PropertyBool.CorpseHexdustHarvested) == true)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "That corpse has already been ground clean."));
                return false;
            }

            if (corpse.KillerId.HasValue && corpse.KillerId.Value != player.Guid.Full)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "That corpse is not yours to offer."));
                return false;
            }

            if (corpse.CreatureType == null || corpse.CreatureType == CreatureType.Invalid)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The bones have no useful lineage."));
                return false;
            }

            var playerLevel = player.Level ?? 1;
            var corpseLevel = corpse.Level ?? 1;
            var minLevel = Math.Max(1, playerLevel - ScavengerTrialLevelWindow);
            var maxLevel = Math.Max(minLevel, playerLevel + ScavengerTrialLevelWindow);
            if (corpseLevel < minLevel || corpseLevel > maxLevel)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"The trial wants bones from creatures near your road: level {minLevel}-{maxLevel}."));
                return false;
            }

            creatureType = corpse.CreatureType.Value;
            creatureName = GetCreatureTypeName(creatureType);
            return true;
        }

        private static bool IsScavengerTrialComplete(Player player)
        {
            return (player.GetProperty(PropertyInt.NomadScavengerTrialComplete) ?? 0) > 0 ||
                   (player.GetProperty(PropertyInt.NomadScavengerTrialProgress) ?? 0) >= ScavengerTrialRequiredBones;
        }

        private static void CompleteScavengerTrial(Player player, WorldObject mundaneMortar, string creatureName)
        {
            if (player.IsBusy)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "You are too busy to finish the trial."));
                player.SendUseDoneEvent();
                return;
            }

            player.SetProperty(PropertyInt.NomadScavengerTrialProgress, ScavengerTrialRequiredBones);
            player.SetProperty(PropertyInt.NomadScavengerTrialComplete, 1);
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.NomadScavengerTrialProgress, ScavengerTrialRequiredBones),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.NomadScavengerTrialComplete, 1));

            var chain = new ActionChain();
            player.IsBusy = true;
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The fifteenth set of {creatureName} bones vanishes into yellow dust. The road lifts you to hear its bargain.", ChatMessageType.Broadcast));

            for (var i = 0; i < 10; i++)
            {
                chain.AddDelaySeconds(1.0f);
                chain.AddAction(player, () =>
                {
                    if (player.IsDestroyed || !player.IsAlive)
                        return;

                    player.ApplyVisualEffects(PlayScript.RestrictionEffectGold, 1.0f);
                    player.ApplyVisualEffects(PlayScript.EnchantUpYellow, 0.8f);
                    player.ApplyVisualEffects(PlayScript.SpecialStateYellow, 0.8f);
                });
            }

            chain.AddAction(player, () =>
            {
                if (!player.IsDestroyed && player.IsAlive)
                {
                    player.TryConsumeFromInventoryWithNetworking(mundaneMortar, 1);
                    GrantScavengersMortar(player);
                    player.ApplyVisualEffects(PlayScript.LevelUp, 1.0f);
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat("The barter is accepted. You receive a Scavenger's Mortar, fit to grind hex dust from worthy kills.", ChatMessageType.Broadcast));
                }

                player.IsBusy = false;
                player.SendUseDoneEvent();
            });
            chain.EnqueueChain();
        }

        private static void GrantScavengersMortar(Player player)
        {
            var mortar = WorldObjectFactory.CreateNewWorldObject(ScavengersMortarWeenieClassId);
            if (mortar == null)
                return;

            mortar.SetProperty(PropertyBool.IsIronmanItem, true);

            if (!player.TryCreateInInventoryWithNetworking(mortar))
            {
                mortar.Location = new ACE.Entity.Position(player.Location);
                ACE.Server.Managers.LandblockManager.AddObject(mortar);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{mortar.Name} falls to the ground because your pack is full.", ChatMessageType.Broadcast));
            }
        }

        private static string GetCreatureTypeName(CreatureType creatureType)
        {
            return creatureType == CreatureType.Invalid ? "monster" : creatureType.ToString();
        }

        private static void SayHexWords(Player player)
        {
            var phrase = HexWords[ThreadSafeRandom.Next(0, HexWords.Length - 1)];
            player.EnqueueBroadcast(new GameMessageHearSpeech(phrase, player.GetNameWithSuffix(), player.Guid.Full, ChatMessageType.Spellcasting), WorldObject.LocalBroadcastRange, ChatMessageType.Spellcasting);
        }

        private static void ApplyHexdustSpellcraft(Player player, WorldObject dust)
        {
            var spellcraft = GetHexdustSpellcraft(player);
            dust.ItemSpellcraft = spellcraft;
            dust.ItemDifficulty = spellcraft;
        }

        private static int GetHexdustSpellcraft(Player player)
        {
            var assess = (int)player.GetCreatureSkill(Skill.AssessCreature).Current;
            return Math.Clamp(250 + assess, 350, 800);
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
