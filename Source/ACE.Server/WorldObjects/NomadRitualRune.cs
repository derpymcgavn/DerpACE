using System;
using System.Collections.Generic;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public enum NomadRuneSchool
    {
        None = 0,
        Creature = 1,
        Life = 2,
        Item = 3,
    }

    public class NomadRitualRune : Gem
    {
        public const uint NomadRitualRuneWeenieClassId = 2000608;
        public const int RitualRuneUses = 5;

        public NomadRitualRune(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public NomadRitualRune(Biota biota) : base(biota)
        {
        }

        public override void ActOnUse(WorldObject activator)
        {
            if (!(activator is Player player))
                return;

            if (player.IsBusy || player.Teleporting || player.suicideInProgress)
            {
                player.SendWeenieError(WeenieError.YoureTooBusy);
                return;
            }

            if (player.IsJumping)
            {
                player.SendWeenieError(WeenieError.YouCantDoThatWhileInTheAir);
                return;
            }

            if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null)
            {
                player.SendTransientError($"Cannot find the {Name}");
                return;
            }

            var school = (NomadRuneSchool)(GetProperty(PropertyInt.NomadRitualSchool) ?? 0);
            var tier = Math.Clamp(GetProperty(PropertyInt.NomadRitualTier) ?? 0, 1, 8);

            if (!NomadRune.PlayerHasSchool(player, school))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"You do not have the magic training to release {Name}."));
                return;
            }

            var spells = NomadRune.GetRitualSpells(school, tier);
            if (spells.Count == 0)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"{Name} has no bound ritual."));
                return;
            }

            foreach (var spellId in spells)
            {
                var spell = new Spell((uint)spellId);
                if (spell.NotFound)
                    continue;

                if (spell.IsImpenBaneType || spell.IsItemRedirectableType)
                    player.TryCastItemEnchantment_WithRedirects(spell, player, this);
                else
                    player.TryCastSpell(spell, player, this, tryResist: false);
            }

            if (UseSound > 0)
                player.Session.Network.EnqueueSend(new GameMessageSound(player.Guid, UseSound));

            player.TryConsumeFromInventoryWithNetworking(this, 1);
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{Name} releases its {NomadRune.GetSchoolName(school)} ritual.", ChatMessageType.Broadcast));
        }

        public static void Prepare(WorldObject rune, NomadRuneSchool school, int tier, uint iconId)
        {
            if (rune == null)
                return;

            tier = Math.Clamp(tier, 1, 8);
            var schoolName = NomadRune.GetSchoolName(school);

            rune.Name = $"{schoolName} Ritual Rune {tier}";
            rune.LongDesc = $"A woven Nomad rune with {RitualRuneUses} releases. Use it to cast all {schoolName} school Nomad buffs at tier {tier}.";
            rune.Use = $"Use this rune to release all {schoolName} school buffs at tier {tier}.";
            rune.SetProperty(PropertyInt.NomadRitualSchool, (int)school);
            rune.SetProperty(PropertyInt.NomadRitualTier, tier);
            rune.SetProperty(PropertyBool.IsIronmanItem, true);
            rune.MaxStackSize = RitualRuneUses;
            rune.StackUnitEncumbrance ??= 5;
            rune.StackUnitValue ??= 5000;
            if (rune.GetProperty(PropertyInt.StackUnitMass) == null)
                rune.SetProperty(PropertyInt.StackUnitMass, 5);
            rune.SetStackSize(RitualRuneUses);
            rune.SetProperty(PropertyInt.UiEffects, (int)(GetSchoolUiEffect(school) | ACE.Entity.Enum.UiEffects.Frost));
            if (iconId != 0)
                rune.SetProperty(PropertyDataId.Icon, iconId);
        }

        private static ACE.Entity.Enum.UiEffects GetSchoolUiEffect(NomadRuneSchool school)
        {
            return school switch
            {
                NomadRuneSchool.Creature => ACE.Entity.Enum.UiEffects.BoostStamina,
                NomadRuneSchool.Life     => ACE.Entity.Enum.UiEffects.BoostHealth,
                NomadRuneSchool.Item     => ACE.Entity.Enum.UiEffects.Magical,
                _                        => ACE.Entity.Enum.UiEffects.Magical,
            };
        }
    }
}
