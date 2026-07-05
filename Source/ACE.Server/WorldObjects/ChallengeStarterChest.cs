using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.DerpAce;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public class ChallengeStarterChest : Chest
    {
        public ChallengeStarterChest(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public ChallengeStarterChest(Biota biota) : base(biota)
        {
        }

        public override void ActOnUse(WorldObject wo)
        {
            if (!(wo is Player player))
                return;

            if (!DerpACEConfig.IronmanEnabled)
            {
                player.SendMessage("Ironman mode is currently disabled on this server.", ChatMessageType.System);
                player.SendUseDoneEvent();
                return;
            }

            if (player.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsIronman) == true)
            {
                player.SendMessage("You are already on an Ironman path.", ChatMessageType.System);
                player.SendUseDoneEvent();
                return;
            }

            if (player.GetProperty(ACE.Entity.Enum.Properties.PropertyBool.IsHardcore) == true)
            {
                player.SendMessage("Hardcore characters cannot become Ironman.", ChatMessageType.System);
                player.SendUseDoneEvent();
                return;
            }

            if ((player.Level ?? 1) > 10)
            {
                player.SendMessage("Ironman paths are only available to characters at level 10 or below.", ChatMessageType.System);
                player.SendUseDoneEvent();
                return;
            }

            if (WeenieClassId == HardcodedWeenies.NomadPathwardenChestWeenieClassId)
            {
                IronmanFactory.InitializeIronmanNomad(player);
                PlayerManager.BroadcastToAll(new GameMessageSystemChat($"[IRONMAN] {player.Name} has taken the NOMAD Ironman path. There is no turning back!", ChatMessageType.WorldBroadcast));
            }
            else
            {
                IronmanFactory.InitializeIronman(player);
                PlayerManager.BroadcastToAll(new GameMessageSystemChat($"[IRONMAN] {player.Name} has taken the Ironman path. There is no turning back!", ChatMessageType.WorldBroadcast));
            }

            player.SendUseDoneEvent();
        }
    }
}