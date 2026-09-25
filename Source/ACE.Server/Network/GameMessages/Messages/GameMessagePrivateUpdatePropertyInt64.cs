using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Network.GameMessages.Messages
{
    public class GameMessagePrivateUpdatePropertyInt64 : GameMessage
    {
        public GameMessagePrivateUpdatePropertyInt64(WorldObject worldObject, PropertyInt64 property, long value)
            : base(GameMessageOpcode.PrivateUpdatePropertyInt64, GameMessageGroup.UIQueue, 17)
        {
            if (ShouldMaskCrawlerExperience(worldObject, property))
                value = 0;

            Writer.Write(worldObject.Sequences.GetNextSequence(Sequence.SequenceType.UpdatePropertyInt64, property));
            Writer.Write((uint)property);
            Writer.Write(value);
        }

        private static bool ShouldMaskCrawlerExperience(WorldObject worldObject, PropertyInt64 property)
        {
            return (property == PropertyInt64.TotalExperience || property == PropertyInt64.AvailableExperience)
                && worldObject is Player player
                && HardcoreCrawlerManager.IsActive(player);
        }
    }
}
