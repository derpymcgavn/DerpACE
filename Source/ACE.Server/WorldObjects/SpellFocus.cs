using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public enum SpellFocusAttunement
    {
        None = 0,
        Fire = 1,
        Frost = 2,
        Acid = 3,
        Lightning = 4,
        Nether = 5,
        Life = 6,
    }

    partial class WorldObject
    {
        public SpellFocusAttunement SpellFocusAttunement
        {
            get => (SpellFocusAttunement)(GetProperty(PropertyInt.SpellFocusAttunement) ?? 0);
            set
            {
                if (value == SpellFocusAttunement.None)
                    RemoveProperty(PropertyInt.SpellFocusAttunement);
                else
                    SetProperty(PropertyInt.SpellFocusAttunement, (int)value);
            }
        }

        public int SpellFocusUpgradeLevel
        {
            get => GetProperty(PropertyInt.SpellFocusUpgradeLevel) ?? 0;
            set
            {
                if (value <= 0)
                    RemoveProperty(PropertyInt.SpellFocusUpgradeLevel);
                else
                    SetProperty(PropertyInt.SpellFocusUpgradeLevel, value);
            }
        }
    }

    partial class Player
    {
        private static readonly SpellFocusAttunement[] SpellFocusAttunementOrder =
        {
            SpellFocusAttunement.Fire,
            SpellFocusAttunement.Frost,
            SpellFocusAttunement.Acid,
            SpellFocusAttunement.Lightning,
            SpellFocusAttunement.Nether,
            SpellFocusAttunement.Life,
        };

        private const uint MajorShiveringStone = 6123;
        private const uint MajorSmolderingStone = 6124;
        private const uint MajorSparkingStone = 6125;
        private const uint MajorStingingStone = 6126;
        private const uint BlackFireAtlanStone = 7469;
        private const uint EnhancedBlackFireAtlanStone = 46035;
        private const uint ArmorUpgradeKit = 40443;

        private const int SpellFocusMaxUpgradeLevel = 8;

        private static readonly SpellId[] SpellFocusImpenetrabilitySpells =
        {
            SpellId.Impenetrability1,
            SpellId.Impenetrability2,
            SpellId.Impenetrability3,
            SpellId.Impenetrability4,
            SpellId.Impenetrability5,
            SpellId.Impenetrability6,
            SpellId.Impenetrability7,
            SpellId.Impenetrability8,
        };

        public bool HasSpecializedSpellFocusSkill()
        {
            return IsSpecialized(Skill.WarMagic)
                || IsSpecialized(Skill.LifeMagic)
                || IsSpecialized(Skill.VoidMagic);
        }

        public bool CanPairSpellFocusWithMainhand(WorldObject focus, WorldObject mainhand)
        {
            if (focus?.IsSpellFocus != true)
                return true;

            if (mainhand == null)
                return true;

            if (mainhand.IsCaster)
                return true;

            return IsBattlemageLightWeapon(mainhand);
        }

        public bool CanPairMainhandWithSpellFocus(WorldObject mainhand, WorldObject focus)
        {
            if (focus?.IsSpellFocus != true)
                return true;

            return CanPairSpellFocusWithMainhand(focus, mainhand);
        }

        public bool IsUsingSpellFocusCasterStaff(WorldObject caster)
        {
            if (caster?.IsCaster != true)
                return false;

            if (caster.CurrentWieldedLocation != EquipMask.TwoHanded && caster.CurrentWieldedLocation != EquipMask.MeleeWeapon)
                return false;

            return GetEquippedOffHand()?.IsSpellFocus == true;
        }

        public bool TryBeginSpellFocusAttunement(WorldObject focus)
        {
            if (focus?.IsSpellFocus != true)
                return false;

            if (focus.SpellFocusAttunement != SpellFocusAttunement.None)
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"{focus.Name} is already attuned to {GetSpellFocusAttunementName(focus.SpellFocusAttunement)}."));
                SendUseDoneEvent();
                return true;
            }

            if (!HasSpecializedSpellFocusSkill())
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, "You must specialize War Magic, Life Magic, or Void Magic to attune a spell focus."));
                SendUseDoneEvent();
                return true;
            }

            Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, "Use a Major elemental Atlan Stone or Black Fire Atlan Stone on this spell focus to attune it."));
            SendUseDoneEvent();
            return true;
        }

        public bool TryAttuneSpellFocusWithAtlanStone(WorldObject stone, WorldObject focus)
        {
            if (stone == null || focus?.IsSpellFocus != true)
                return false;

            if (!TryGetSpellFocusStoneAttunement(stone.WeenieClassId, out var attunement, out var upgradeLevels))
                return false;

            if (focus.SpellFocusAttunement != SpellFocusAttunement.None)
            {
                if (upgradeLevels > 0 && focus.SpellFocusAttunement == attunement)
                    return TryUpgradeSpellFocusWithItem(stone, focus);

                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"{focus.Name} is already attuned to {GetSpellFocusAttunementName(focus.SpellFocusAttunement)}."));
                SendUseDoneEvent();
                return true;
            }

            if (!HasSpecializedSpellFocusSkill())
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, "You must specialize War Magic, Life Magic, or Void Magic to attune a spell focus."));
                SendUseDoneEvent();
                return true;
            }

            if (!TryConsumeFromInventoryWithNetworking(stone.WeenieClassId, 1))
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"You need a {stone.Name} in your inventory to attune {focus.Name}."));
                SendUseDoneEvent();
                return true;
            }

            ApplySpellFocusAttunement(focus, attunement);
            if (upgradeLevels > 0)
                UpgradeSpellFocusMagicalArmor(focus, upgradeLevels);

            return true;
        }

        public bool TryUpgradeSpellFocusWithItem(WorldObject source, WorldObject focus)
        {
            if (source == null || focus?.IsSpellFocus != true)
                return false;

            var levels = source.WeenieClassId switch
            {
                ArmorUpgradeKit => 1,
                BlackFireAtlanStone => 1,
                EnhancedBlackFireAtlanStone => 2,
                _ => 0,
            };

            if (levels <= 0)
                return false;

            if (focus.SpellFocusUpgradeLevel >= SpellFocusMaxUpgradeLevel)
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"{focus.Name} cannot hold any more magical armor."));
                SendUseDoneEvent();
                return true;
            }

            if (!TryConsumeFromInventoryWithNetworking(source.WeenieClassId, 1))
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, $"You need a {source.Name} in your inventory to upgrade {focus.Name}."));
                SendUseDoneEvent();
                return true;
            }

            UpgradeSpellFocusMagicalArmor(focus, levels);
            SendUseDoneEvent();
            return true;
        }

        public bool TryTailorSpellFocusAppearance(WorldObject source, WorldObject focus)
        {
            if (source == null || focus?.IsSpellFocus != true || source == focus)
                return false;

            if (!CanUseAsSpellFocusAppearance(source))
                return false;

            UpdateProperty(focus, PropertyDataId.Setup, source.SetupTableId);
            UpdateProperty(focus, PropertyDataId.Icon, source.IconId);
            UpdateProperty(focus, PropertyInt.PaletteTemplate, (int?)source.PaletteTemplate);
            UpdateProperty(focus, PropertyFloat.Shade, source.Shade);
            UpdateProperty(focus, PropertyFloat.Translucency, 0.0);
            UpdateProperty(focus, PropertyInt.SpellFocusVisualSourceWcid, (int)source.WeenieClassId);
            UpdateProperty(focus, PropertyString.LongDesc, BuildSpellFocusLongDesc(focus.SpellFocusAttunement, source.Name, focus.SpellFocusUpgradeLevel));

            Session.Network.EnqueueSend(new GameMessageSystemChat($"{focus.Name} takes on the appearance of {source.Name}. Its focus properties are unchanged.", ChatMessageType.Broadcast));
            SendUseDoneEvent();
            return true;
        }

        private bool IsSpecialized(Skill skill)
        {
            return GetCreatureSkill(skill).AdvancementClass >= SkillAdvancementClass.Specialized;
        }

        private void PromptSpellFocusAttunement(WorldObject focus, int index)
        {
            if (index >= SpellFocusAttunementOrder.Length)
            {
                Session.Network.EnqueueSend(new GameEventCommunicationTransientString(Session, "The spell focus remains unattuned."));
                SendUseDoneEvent();
                return;
            }

            var attunement = SpellFocusAttunementOrder[index];
            var prompt = $"Attune {focus.Name} to {GetSpellFocusAttunementName(attunement)}?\n\nChoose No to see the next option.";
            var confirm = new Confirmation_Custom(Guid,
                () => ApplySpellFocusAttunement(focus, attunement),
                () => PromptSpellFocusAttunement(focus, index + 1));

            if (!ConfirmationManager.EnqueueSend(confirm, prompt))
            {
                Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ConfirmationInProgress));
                SendUseDoneEvent();
            }
        }

        private void ApplySpellFocusAttunement(WorldObject focus, SpellFocusAttunement attunement)
        {
            if (focus?.IsSpellFocus != true)
            {
                SendUseDoneEvent();
                return;
            }

            UpdateProperty(focus, PropertyInt.SpellFocusAttunement, (int)attunement);
            UpdateProperty(focus, PropertyInt.UiEffects, (int)GetSpellFocusUiEffects(attunement));
            UpdateProperty(focus, PropertyString.Name, $"{GetSpellFocusAttunementName(attunement)} Spell Focus");
            UpdateProperty(focus, PropertyString.LongDesc, BuildSpellFocusLongDesc(attunement, GetSpellFocusVisualSourceName(focus), focus.SpellFocusUpgradeLevel));

            ApplySpellFocusProtectionProfile(focus, attunement);
            focus.SaveBiotaToDatabase();

            Session.Network.EnqueueSend(new GameMessageSystemChat($"{focus.Name} settles into {GetSpellFocusAttunementName(attunement)} attunement.", ChatMessageType.Broadcast));
            SendUseDoneEvent();
        }

        private void UpgradeSpellFocusMagicalArmor(WorldObject focus, int levels)
        {
            if (focus?.IsSpellFocus != true || levels <= 0)
                return;

            var previousLevel = Math.Clamp(focus.SpellFocusUpgradeLevel, 0, SpellFocusMaxUpgradeLevel);
            var newLevel = Math.Clamp(previousLevel + levels, 0, SpellFocusMaxUpgradeLevel);
            if (newLevel == previousLevel)
                return;

            UpdateProperty(focus, PropertyInt.ArmorLevel, 10);
            UpdateProperty(focus, PropertyInt.SpellFocusUpgradeLevel, newLevel);
            ApplySpellFocusImpenetrability(focus, newLevel);
            UpdateProperty(focus, PropertyString.LongDesc, BuildSpellFocusLongDesc(focus.SpellFocusAttunement, GetSpellFocusVisualSourceName(focus), newLevel));
            focus.SaveBiotaToDatabase();

            Session.Network.EnqueueSend(new GameMessageSystemChat($"{focus.Name} absorbs the upgrade. Magical armor is now rank {newLevel}. Base armor remains 10.", ChatMessageType.Broadcast));
        }

        private void ApplySpellFocusImpenetrability(WorldObject focus, int level)
        {
            foreach (var spell in SpellFocusImpenetrabilitySpells)
            {
                if (focus.Biota.TryRemoveKnownSpell((int)spell, focus.BiotaDatabaseLock))
                    RemoveItemSpell(focus, (uint)spell, true);
            }

            if (level <= 0)
                return;

            var spellId = SpellFocusImpenetrabilitySpells[Math.Clamp(level, 1, SpellFocusMaxUpgradeLevel) - 1];
            focus.Biota.GetOrAddKnownSpell((int)spellId, focus.BiotaDatabaseLock, out _, 2.0f);

            if (EquippedObjects.ContainsKey(focus.Guid))
                CreateItemSpell(focus, (uint)spellId);
        }

        private void ApplySpellFocusProtectionProfile(WorldObject focus, SpellFocusAttunement attunement)
        {
            var mods = new Dictionary<PropertyFloat, double>
            {
                [PropertyFloat.ArmorModVsSlash] = 1.0,
                [PropertyFloat.ArmorModVsPierce] = 1.0,
                [PropertyFloat.ArmorModVsBludgeon] = 1.0,
                [PropertyFloat.ArmorModVsCold] = 1.0,
                [PropertyFloat.ArmorModVsFire] = 1.0,
                [PropertyFloat.ArmorModVsAcid] = 1.0,
                [PropertyFloat.ArmorModVsElectric] = 1.0,
                [PropertyFloat.ArmorModVsNether] = 1.0,
            };

            switch (attunement)
            {
                case SpellFocusAttunement.Fire:
                    mods[PropertyFloat.ArmorModVsFire] = 1.2;
                    mods[PropertyFloat.ArmorModVsCold] = 0.95;
                    break;
                case SpellFocusAttunement.Frost:
                    mods[PropertyFloat.ArmorModVsCold] = 1.2;
                    mods[PropertyFloat.ArmorModVsFire] = 0.95;
                    break;
                case SpellFocusAttunement.Acid:
                    mods[PropertyFloat.ArmorModVsAcid] = 1.2;
                    mods[PropertyFloat.ArmorModVsPierce] = 0.95;
                    break;
                case SpellFocusAttunement.Lightning:
                    mods[PropertyFloat.ArmorModVsElectric] = 1.2;
                    mods[PropertyFloat.ArmorModVsBludgeon] = 0.95;
                    break;
                case SpellFocusAttunement.Nether:
                    mods[PropertyFloat.ArmorModVsNether] = 1.2;
                    mods[PropertyFloat.ArmorModVsSlash] = 0.95;
                    break;
                case SpellFocusAttunement.Life:
                    mods[PropertyFloat.ArmorModVsSlash] = 1.05;
                    mods[PropertyFloat.ArmorModVsPierce] = 1.05;
                    mods[PropertyFloat.ArmorModVsBludgeon] = 1.05;
                    break;
            }

            foreach (var mod in mods)
                UpdateProperty(focus, mod.Key, mod.Value);
        }

        private static bool CanUseAsSpellFocusAppearance(WorldObject source)
        {
            return source.ItemType.HasFlag(ItemType.MeleeWeapon)
                || source.ItemType.HasFlag(ItemType.MissileWeapon)
                || source.ItemType.HasFlag(ItemType.Caster)
                || source.ItemType.HasFlag(ItemType.MagicWieldable);
        }

        private static bool TryGetSpellFocusStoneAttunement(uint wcid, out SpellFocusAttunement attunement, out int upgradeLevels)
        {
            upgradeLevels = 0;
            switch (wcid)
            {
                case MajorShiveringStone:
                    attunement = SpellFocusAttunement.Frost;
                    return true;
                case MajorSmolderingStone:
                    attunement = SpellFocusAttunement.Fire;
                    return true;
                case MajorSparkingStone:
                    attunement = SpellFocusAttunement.Lightning;
                    return true;
                case MajorStingingStone:
                    attunement = SpellFocusAttunement.Acid;
                    return true;
                case BlackFireAtlanStone:
                    attunement = SpellFocusAttunement.Nether;
                    upgradeLevels = 1;
                    return true;
                case EnhancedBlackFireAtlanStone:
                    attunement = SpellFocusAttunement.Nether;
                    upgradeLevels = 2;
                    return true;
                default:
                    attunement = SpellFocusAttunement.None;
                    return false;
            }
        }

        private static string BuildSpellFocusLongDesc(SpellFocusAttunement attunement, string visualSource, int upgradeLevel)
        {
            var attunementText = attunement == SpellFocusAttunement.None ? "unattuned" : GetSpellFocusAttunementName(attunement);
            var visualText = string.IsNullOrWhiteSpace(visualSource) ? "" : $"\nCosmetic form: {visualSource}.";
            var upgradeText = upgradeLevel <= 0
                ? "No magical armor upgrades have been applied."
                : $"Magical armor rank: {Math.Clamp(upgradeLevel, 0, SpellFocusMaxUpgradeLevel)}. Base armor remains 10.";

            return $"A shield-slot spell focus for specialized War, Life, or Void mages. It may be used with a caster or caster staff, or with a Battlemage Helm and an eligible Light Weapon. Attune it by using a Major elemental Atlan Stone or Black Fire Atlan Stone on it. Armor Upgrade Kits improve only its magical armor, so hollow attacks still treat it as 10 AL. Current attunement: {attunementText}. {upgradeText}{visualText}";
        }

        private static string GetSpellFocusVisualSourceName(WorldObject focus)
        {
            return focus.GetProperty(PropertyInt.SpellFocusVisualSourceWcid) != null ? "tailored form" : null;
        }

        private static string GetSpellFocusAttunementName(SpellFocusAttunement attunement)
        {
            return attunement switch
            {
                SpellFocusAttunement.Fire => "Fire",
                SpellFocusAttunement.Frost => "Frost",
                SpellFocusAttunement.Acid => "Acid",
                SpellFocusAttunement.Lightning => "Lightning",
                SpellFocusAttunement.Nether => "Nether",
                SpellFocusAttunement.Life => "Life",
                _ => "Unattuned",
            };
        }

        private static ACE.Entity.Enum.UiEffects GetSpellFocusUiEffects(SpellFocusAttunement attunement)
        {
            return attunement switch
            {
                SpellFocusAttunement.Fire => ACE.Entity.Enum.UiEffects.Fire,
                SpellFocusAttunement.Frost => ACE.Entity.Enum.UiEffects.Frost,
                SpellFocusAttunement.Acid => ACE.Entity.Enum.UiEffects.Acid,
                SpellFocusAttunement.Lightning => ACE.Entity.Enum.UiEffects.Lightning,
                SpellFocusAttunement.Nether => ACE.Entity.Enum.UiEffects.Nether,
                SpellFocusAttunement.Life => ACE.Entity.Enum.UiEffects.BoostHealth,
                _ => ACE.Entity.Enum.UiEffects.Magical,
            };
        }
    }
}
