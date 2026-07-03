using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Hidden self-found trophy stamps for Nomad quests and global item races.
    /// </summary>
    public static class NomadQuestTrophy
    {
        public static void StampIfEligible(Player player, Creature source, WorldObject item)
        {
            if (player == null || source == null || item == null || !IsQuestTrophyCandidate(item))
                return;

            item.SetProperty(PropertyInt.NomadTrophyOwner, unchecked((int)player.Guid.Full));
            item.SetProperty(PropertyInt.NomadTrophySourceWcid, unchecked((int)source.WeenieClassId));
            item.SetProperty(PropertyInt.NomadTrophyQuestEpoch, GlobalKillQuestManager.CurrentEpoch);

            var creatureType = source.GetProperty(PropertyInt.CreatureType);
            if (creatureType != null)
                item.SetProperty(PropertyInt.NomadTrophySourceCreatureType, creatureType.Value);
        }

        public static bool IsSelfFoundFor(Player player, WorldObject item)
        {
            if (player == null || item == null)
                return false;

            var owner = item.GetProperty(PropertyInt.NomadTrophyOwner);
            return owner != null && unchecked((uint)owner.Value) == player.Guid.Full;
        }

        public static bool IsSelfFoundFor(Player player, WorldObject item, uint requiredWcid)
        {
            return IsSelfFoundFor(player, item) && item?.WeenieClassId == requiredWcid;
        }

        public static bool IsSelfFoundFromCreature(Player player, WorldObject item, uint sourceWcid)
        {
            if (!IsSelfFoundFor(player, item))
                return false;

            var stampedSource = item.GetProperty(PropertyInt.NomadTrophySourceWcid);
            return stampedSource != null && unchecked((uint)stampedSource.Value) == sourceWcid;
        }

        public static bool IsSelfFoundFromCreatureType(Player player, WorldObject item, CreatureType creatureType)
        {
            if (!IsSelfFoundFor(player, item))
                return false;

            var stampedType = item.GetProperty(PropertyInt.NomadTrophySourceCreatureType);
            return stampedType != null && stampedType.Value == (int)creatureType;
        }

        private static bool IsQuestTrophyCandidate(WorldObject item)
        {
            if (item.WeenieType == WeenieType.Coin || item.WeenieType == WeenieType.Container || item.WeenieType == WeenieType.Corpse)
                return false;

            if (Player.IsGearProvenanceTracked(item))
                return false;

            return true;
        }
    }
}
