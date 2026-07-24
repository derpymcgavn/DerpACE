using System;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public partial class CombatPet
    {

        public bool InitGravecallerRevenant(Player owner, Corpse corpse, WorldObject caster, float durationSeconds)
        {
            if (owner?.Location == null || corpse?.Location == null || corpse.IsDestroyed)
                return false;
            Location = new Position(corpse.Location);
            SetupTableId = corpse.SetupTableId;
            MotionTableId = corpse.MotionTableId;
            PhysicsTableId = corpse.PhysicsTableId;
            PaletteBaseDID = corpse.PaletteBaseDID;
            ClothingBase = corpse.ClothingBase;
            ObjScale = corpse.ObjScale;
            PaletteTemplate = corpse.PaletteTemplate;
            Shade = corpse.Shade;

            var sound = corpse.GetProperty(PropertyDataId.GravecallerCorpseSoundTable);
            if (sound.HasValue)
                SoundTableId = sound.Value;
            var combat = corpse.GetProperty(PropertyDataId.GravecallerCorpseCombatTable);
            if (combat.HasValue)
                CombatTableDID = combat.Value;

            var objDesc = corpse.CalculateObjDesc();
            Biota.PropertiesAnimPart = objDesc.AnimPartChanges.Clone(BiotaDatabaseLock);
            Biota.PropertiesPalette = objDesc.SubPalettes.Clone(BiotaDatabaseLock);
            Biota.PropertiesTextureMap = objDesc.TextureChanges.Clone(BiotaDatabaseLock);

            Name = $"Revenant of {corpse.Name.Replace("Corpse of ", "")}";
            Level = Math.Clamp(corpse.Level ?? owner.Level ?? 1, 1, owner.Level ?? 275);
            PetOwner = owner.Guid.Full;
            P_PetOwner = owner;
            Faction1Bits = owner.Faction1Bits;
            NoCorpse = true;
            TimeToRot = -1;
            SuppressGenerateEffect = true;
            MonsterState = State.Awake;
            IsAwake = true;
            SetCombatMode(CombatMode.Melee);

            if (caster?.W_DamageType == DamageType.Health)
            {
                Name = $"Guardian {Name}";
                DamageResistRating = Math.Max(DamageResistRating ?? 0, 20);
            }
            else if (caster?.W_DamageType == DamageType.Nether)
            {
                Name = $"Hollow {Name}";
                DamageRating = Math.Max(DamageRating ?? 0, 15);
            }
            else
                Name = $"Elemental {Name}";

            if (!EnterWorld())
                return false;

            EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantUpPurple, 1.0f));
            var expire = new ActionChain();
            expire.AddDelaySeconds(Math.Max(1.0f, durationSeconds));
            expire.AddAction(this, () =>
            {
                if (IsDestroyed)
                    return;
                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.EnchantDownPurple, 1.0f));
                owner.ClearActiveGravecallerPet(this);
                Destroy();
            });
            expire.EnqueueChain();
            return true;
        }
    }
}