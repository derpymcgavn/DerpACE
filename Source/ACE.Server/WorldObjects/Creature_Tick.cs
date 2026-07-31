using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        /// <summary>
        /// Called every ~5 seconds for Creatures
        /// </summary>
        public override void Heartbeat(double currentUnixTime)
        {
            List<WorldObject> expireItems = null;

            // added where clause
            foreach (var wo in EquippedObjects.Values)
            {
                if (!wo.EnchantmentManager.HasEnchantments && !wo.Lifespan.HasValue)
                    continue;

                // FIXME: wo.NextHeartbeatTime is double.MaxValue here
                //if (wo.NextHeartbeatTime <= currentUnixTime)
                    //wo.Heartbeat(currentUnixTime);

                // just go by parent heartbeats, only for enchantments?
                // TODO: handle players dropping / picking up items
                wo.EnchantmentManager.HeartBeat(CachedHeartbeatInterval);

                if (wo.IsLifespanSpent)
                    (expireItems ??= new List<WorldObject>()).Add(wo);
            }

            VitalHeartBeat();

            EmoteManager.HeartBeat();

            TownAmbientTick(currentUnixTime);

            DamageHistory.TryPrune();
            BossMechanicManager.OnHeartbeat(this, currentUnixTime);

            // DerpACE: support / caster mutator heartbeats
            if (IsHealerMob)
                TryHealerHeartbeat(currentUnixTime);
            if (IsEnchanterMob)
                TryEnchanterHeartbeat(currentUnixTime);
            if (IsShamanMob)
                TryShamanHeartbeat(currentUnixTime);

            // delete items when RemainingLifespan <= 0
            if (expireItems != null)
            {
                foreach (var expireItem in expireItems)
                {
                    expireItem.DeleteObject(this);

                    if (this is Player player)
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Its lifespan finished, your {expireItem.Name} crumbles to dust.", ChatMessageType.Broadcast));
                }
            }

            base.Heartbeat(currentUnixTime);
        }
    }
}
