using System;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.GameEvent.Events;

namespace ACE.Server.WorldObjects
{
    public partial class Player
    {
        public const int GravecallerCooldownId = 2058;
        private CombatPet _activeGravecallerPet;

        public bool TryRaiseGravecallerCorpse(WorldObject caster, Corpse corpse)
        {
            if (caster?.GetProperty(PropertyBool.IsGravecallerCaster) != true || corpse == null)
                return false;

            if (!corpse.HasPermission(this))
            {
                SendMessage("The dead do not answer those without claim to their remains.", ChatMessageType.Magic);
                SendUseDoneEvent();
                return true;
            }
            if (corpse.GetProperty(PropertyBool.CorpseRaisedByGravecaller) == true)
            {
                SendMessage("That corpse has already surrendered its echo.", ChatMessageType.Magic);
                SendUseDoneEvent();
                return true;
            }
            if (_activeGravecallerPet != null && !_activeGravecallerPet.IsDestroyed && _activeGravecallerPet.IsAlive)
            {
                SendMessage("You already command a revenant.", ChatMessageType.Magic);
                SendUseDoneEvent();
                return true;
            }
            if (!TryStartMutatorCooldown(caster, GravecallerCooldownId, 45.0))
            {
                SendMessage("The Gravecaller has not yet gathered another soul-echo.", ChatMessageType.Magic);
                SendUseDoneEvent();
                return true;
            }

            var revenant = CreateShadowClonePetShell();
            if (revenant == null || !revenant.InitGravecallerRevenant(this, corpse, caster, 20.0f))
            {
                revenant?.Destroy();
                SendMessage("The corpse shudders, but its echo cannot take shape.", ChatMessageType.Magic);
                SendUseDoneEvent();
                return true;
            }

            corpse.SetProperty(PropertyBool.CorpseRaisedByGravecaller, true);
            if (corpse.VictimId.HasValue && new ObjectGuid(corpse.VictimId.Value).IsPlayer())
            {
                corpse.SetupTableId = 33560241; // spent player corpse tombstone
                corpse.Biota.PropertiesAnimPart.Clear();
                corpse.Biota.PropertiesPalette.Clear();
                corpse.Biota.PropertiesTextureMap.Clear();
                corpse.EnqueueBroadcast(new GameMessageObjDescEvent(corpse));
                corpse.SaveBiotaToDatabase();
            }
            _activeGravecallerPet = revenant;
            caster.CooldownId = GravecallerCooldownId;
            caster.CooldownDuration = 45.0;
            EnchantmentManager.StartCooldown(caster);
            SendMessage($"You call {revenant.Name} back to battle for twenty seconds.", ChatMessageType.Magic);
            SendUseDoneEvent();
            return true;
        }

        internal void ClearActiveGravecallerPet(CombatPet pet)
        {
            if (ReferenceEquals(_activeGravecallerPet, pet))
                _activeGravecallerPet = null;
        }
    }
}