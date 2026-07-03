using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        public void TryAdvanceSlayerGems(Creature killed)
        {
            if (killed == null || killed is Player || killed.CreatureType == ACE.Entity.Enum.CreatureType.Invalid)
                return;

            foreach (var gem in GetAllPossessions().Where(SlayerGem.IsSlayerGem).ToList())
            {
                if (SlayerGem.IsCharged(gem))
                    continue;

                var wasPrepared = false;
                if (gem.SlayerCreatureType == null)
                {
                    SlayerGem.PrepareNewDormantGem(gem);
                    wasPrepared = true;
                }

                if (gem.SlayerCreatureType != killed.CreatureType)
                {
                    if (wasPrepared)
                    {
                        gem.SaveBiotaToDatabase();
                        SendSlayerGemUpdate(gem);
                    }
                    continue;
                }

                var previousLevel = gem.ItemLevel ?? 0;
                var added = gem.AddItemXP(1);
                if (added <= 0)
                    continue;

                var newLevel = gem.ItemLevel ?? 0;
                SlayerGem.ApplyCreatureVisuals(gem);
                SlayerGem.RefreshNameAndDescription(gem);

                SendSlayerGemUpdate(gem);

                if (newLevel >= SlayerGem.SlayerMaxLevel)
                {
                    SlayerGem.ChargeGem(gem);
                    gem.SaveBiotaToDatabase();
                    ApplyVisualEffects(PlayScript.SkillUpPurple);
                    Session.Network.EnqueueSend(
                        new GameMessageCreateObject(gem),
                        new GameMessageSystemChat($"{gem.Name} is fully charged.", ChatMessageType.Broadcast));
                    SendSlayerGemUpdate(gem);
                }
                else if (newLevel > previousLevel && (newLevel % 10 == 0 || newLevel == 1))
                {
                    gem.SaveBiotaToDatabase();
                    Session.Network.EnqueueSend(new GameMessageSystemChat($"{gem.Name} has reached level {newLevel}/{SlayerGem.SlayerMaxLevel}.", ChatMessageType.Broadcast));
                }
                else
                {
                    gem.SaveBiotaToDatabase();
                }
            }
        }

        private void SendSlayerGemUpdate(WorldObject gem)
        {
            Session.Network.EnqueueSend(
                new GameMessagePrivateUpdateDataID(gem, PropertyDataId.Setup, gem.SetupTableId),
                new GameMessagePrivateUpdateDataID(gem, PropertyDataId.Icon, gem.IconId),
                new GameMessagePrivateUpdatePropertyBool(gem, PropertyBool.IsSlayerGem, true),
                new GameMessagePrivateUpdatePropertyBool(gem, PropertyBool.IsChargedSlayerGem, SlayerGem.IsCharged(gem)),
                new GameMessagePrivateUpdatePropertyInt(gem, PropertyInt.SlayerCreatureType, (int)(gem.SlayerCreatureType ?? ACE.Entity.Enum.CreatureType.Invalid)),
                new GameMessagePrivateUpdatePropertyFloat(gem, PropertyFloat.SlayerDamageBonus, gem.SlayerDamageBonus ?? SlayerGem.MinimumSlayerMod),
                new GameMessagePrivateUpdatePropertyInt(gem, PropertyInt.ItemUseable, (int)(gem.ItemUseable ?? Usable.No)),
                new GameMessagePrivateUpdatePropertyInt(gem, PropertyInt.TargetType, (int)(gem.TargetType ?? ItemType.None)),
                new GameMessagePrivateUpdatePropertyInt64(gem, PropertyInt64.ItemTotalXp, gem.ItemTotalXp ?? 0),
                new GameMessagePrivateUpdatePropertyString(gem, PropertyString.Name, gem.Name),
                new GameMessagePrivateUpdatePropertyString(gem, PropertyString.LongDesc, gem.LongDesc ?? ""));
        }
    }
}
