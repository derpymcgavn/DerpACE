using System;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public static class NomadRuneMergeTool
    {
        public const uint NomadRuneMergeToolWeenieClassId = 2000607;

        public static bool IsMergeTool(WorldObject item)
        {
            return item?.WeenieClassId == NomadRuneMergeToolWeenieClassId;
        }

        public static bool TryUse(Player player, WorldObject tool, WorldObject target)
        {
            if (player == null || !IsMergeTool(tool))
                return false;

            if (!NomadRune.IsNomadRune(target))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The Nomad Rune Loom only works on Nomad Runes."));
                player.SendUseDoneEvent();
                return true;
            }

            if ((target.StackSize ?? 1) < NomadRune.NomadRuneUses)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"The rune must have {NomadRune.NomadRuneUses} uses remaining before it can be woven."));
                player.SendUseDoneEvent();
                return true;
            }

            if (!target.SpellDID.HasValue || !NomadRune.TryGetRuneInfo(target.SpellDID.Value, out var school, out var tier))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "That rune cannot be woven into a ritual rune."));
                player.SendUseDoneEvent();
                return true;
            }

            if (!NomadRune.PlayerHasSchool(player, school))
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, $"You do not have the magic training to weave a {NomadRune.GetSchoolName(school)} ritual rune."));
                player.SendUseDoneEvent();
                return true;
            }

            var ritualRune = WorldObjectFactory.CreateNewWorldObject(NomadRitualRune.NomadRitualRuneWeenieClassId);
            if (ritualRune == null)
            {
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The rune loom sputters and fails to shape a ritual rune."));
                player.SendUseDoneEvent();
                return true;
            }

            NomadRitualRune.Prepare(ritualRune, school, tier, target.IconId);

            if (!player.TryConsumeFromInventoryWithNetworking(target, NomadRune.NomadRuneUses))
            {
                ritualRune.Destroy();
                player.Session.Network.EnqueueSend(new GameEventCommunicationTransientString(player.Session, "The source rune slipped away before it could be woven."));
                player.SendUseDoneEvent();
                return true;
            }

            if (!player.TryCreateInInventoryWithNetworking(ritualRune))
            {
                ritualRune.Location = new ACE.Entity.Position(player.Location);
                ACE.Server.Managers.LandblockManager.AddObject(ritualRune);
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"{ritualRune.Name} falls to the ground because your pack is full.", ChatMessageType.Broadcast));
                player.SendUseDoneEvent();
                return true;
            }

            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You weave {target.Name} into {ritualRune.Name}.", ChatMessageType.Broadcast));
            player.SendUseDoneEvent();
            return true;
        }
    }
}
