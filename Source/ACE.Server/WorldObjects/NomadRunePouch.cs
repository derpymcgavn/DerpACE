using System.Linq;

using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public class NomadRunePouch : Container
    {
        public const uint NomadRunePouchWeenieClassId = 2000606;

        public NomadRunePouch(Weenie weenie, ObjectGuid guid) : base(weenie, guid)
        {
        }

        public NomadRunePouch(Biota biota) : base(biota)
        {
        }

        public static bool IsPouch(WorldObject item)
        {
            return item?.WeenieClassId == NomadRunePouchWeenieClassId;
        }

        public static NomadRunePouch Find(Player player)
        {
            return player?.GetAllPossessions().OfType<NomadRunePouch>().FirstOrDefault();
        }

        public static NomadRunePouch EnsureFor(Player player, bool notify = true)
        {
            if (player?.IsIronmanNomad != true)
                return null;

            var existing = Find(player);
            if (existing != null)
                return existing;

            var pouch = WorldObjectFactory.CreateNewWorldObject(NomadRunePouchWeenieClassId) as NomadRunePouch;
            if (pouch == null)
                return null;

            pouch.SetProperty(PropertyBool.IsIronmanItem, true);

            if (!player.TryCreateInInventoryWithNetworking(pouch))
                return null;

            if (notify)
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat("You make room for a Nomad Rune Pouch.", ChatMessageType.Broadcast));

            return pouch;
        }

        public static bool TryStoreRune(Player player, WorldObject rune, bool notify)
        {
            if (player?.Session == null || !NomadRune.IsNomadRune(rune))
                return false;

            if (!player.HasEnoughBurdenToAddToInventory(rune))
                return false;

            var pouch = EnsureFor(player, notify);
            if (pouch == null || !pouch.TryAddToInventory(rune, out _, limitToMainPackOnly: true))
                return false;

            player.EncumbranceVal += rune.EncumbranceVal ?? 0;
            player.Value += rune.Value ?? 0;

            player.Session.Network.EnqueueSend(
                new GameMessageCreateObject(rune),
                new GameEventItemServerSaysContainId(player.Session, rune, pouch),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.EncumbranceVal, player.EncumbranceVal ?? 0));

            rune.SaveBiotaToDatabase();
            pouch.SaveBiotaToDatabase();

            return true;
        }
    }
}
