using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    public static class NomadRune
    {
        public const uint NomadRuneWeenieClassId = 2000605;
        public const int NomadRuneUses = 10;

        private const float BaseDropChance = 0.006f;
        private const float TierDropChance = 0.002f;
        private const float MaxDropChance = 0.025f;

        private static readonly SpellId[][] CreatureRuneSpells =
        {
            new[] { SpellId.StrengthSelf1, SpellId.StrengthSelf2, SpellId.StrengthSelf3, SpellId.StrengthSelf4, SpellId.StrengthSelf5, SpellId.StrengthSelf6, SpellId.StrengthSelf7, SpellId.StrengthSelf8 },
            new[] { SpellId.EnduranceSelf1, SpellId.EnduranceSelf2, SpellId.EnduranceSelf3, SpellId.EnduranceSelf4, SpellId.EnduranceSelf5, SpellId.EnduranceSelf6, SpellId.EnduranceSelf7, SpellId.EnduranceSelf8 },
            new[] { SpellId.CoordinationSelf1, SpellId.CoordinationSelf2, SpellId.CoordinationSelf3, SpellId.CoordinationSelf4, SpellId.CoordinationSelf5, SpellId.CoordinationSelf6, SpellId.CoordinationSelf7, SpellId.CoordinationSelf8 },
            new[] { SpellId.QuicknessSelf1, SpellId.QuicknessSelf2, SpellId.QuicknessSelf3, SpellId.QuicknessSelf4, SpellId.QuicknessSelf5, SpellId.QuicknessSelf6, SpellId.QuicknessSelf7, SpellId.QuicknessSelf8 },
            new[] { SpellId.FocusSelf1, SpellId.FocusSelf2, SpellId.FocusSelf3, SpellId.FocusSelf4, SpellId.FocusSelf5, SpellId.FocusSelf6, SpellId.FocusSelf7, SpellId.FocusSelf8 },
            new[] { SpellId.WillpowerSelf1, SpellId.WillpowerSelf2, SpellId.WillpowerSelf3, SpellId.WillpowerSelf4, SpellId.WillpowerSelf5, SpellId.WillpowerSelf6, SpellId.WillpowerSelf7, SpellId.WillpowerSelf8 },
            new[] { SpellId.CreatureEnchantmentMasterySelf1, SpellId.CreatureEnchantmentMasterySelf2, SpellId.CreatureEnchantmentMasterySelf3, SpellId.CreatureEnchantmentMasterySelf4, SpellId.CreatureEnchantmentMasterySelf5, SpellId.CreatureEnchantmentMasterySelf6, SpellId.CreatureEnchantmentMasterySelf7, SpellId.CreatureEnchantmentMasterySelf8 },
        };

        private static readonly SpellId[][] LifeRuneSpells =
        {
            new[] { SpellId.ArmorSelf1, SpellId.ArmorSelf2, SpellId.ArmorSelf3, SpellId.ArmorSelf4, SpellId.ArmorSelf5, SpellId.ArmorSelf6, SpellId.ArmorSelf7, SpellId.ArmorSelf8 },
            new[] { SpellId.InvulnerabilitySelf1, SpellId.InvulnerabilitySelf2, SpellId.InvulnerabilitySelf3, SpellId.InvulnerabilitySelf4, SpellId.InvulnerabilitySelf5, SpellId.InvulnerabilitySelf6, SpellId.InvulnerabilitySelf7, SpellId.InvulnerabilitySelf8 },
            new[] { SpellId.MagicResistanceSelf1, SpellId.MagicResistanceSelf2, SpellId.MagicResistanceSelf3, SpellId.MagicResistanceSelf4, SpellId.MagicResistanceSelf5, SpellId.MagicResistanceSelf6, SpellId.MagicResistanceSelf7, SpellId.MagicResistanceSelf8 },
            new[] { SpellId.RejuvenationSelf1, SpellId.RejuvenationSelf2, SpellId.RejuvenationSelf3, SpellId.RejuvenationSelf4, SpellId.RejuvenationSelf5, SpellId.RejuvenationSelf6, SpellId.RejuvenationSelf7, SpellId.RejuvenationSelf8 },
            new[] { SpellId.RegenerationSelf1, SpellId.RegenerationSelf2, SpellId.RegenerationSelf3, SpellId.RegenerationSelf4, SpellId.RegenerationSelf5, SpellId.RegenerationSelf6, SpellId.RegenerationSelf7, SpellId.RegenerationSelf8 },
            new[] { SpellId.ManaRenewalSelf1, SpellId.ManaRenewalSelf2, SpellId.ManaRenewalSelf3, SpellId.ManaRenewalSelf4, SpellId.ManaRenewalSelf5, SpellId.ManaRenewalSelf6, SpellId.ManaRenewalSelf7, SpellId.ManaRenewalSelf8 },
            new[] { SpellId.LifeMagicMasterySelf1, SpellId.LifeMagicMasterySelf2, SpellId.LifeMagicMasterySelf3, SpellId.LifeMagicMasterySelf4, SpellId.LifeMagicMasterySelf5, SpellId.LifeMagicMasterySelf6, SpellId.LifeMagicMasterySelf7, SpellId.LifeMagicMasterySelf8 },
        };

        private static readonly SpellId[][] ItemRuneSpells =
        {
            new[] { SpellId.BloodDrinkerSelf1, SpellId.BloodDrinkerSelf2, SpellId.BloodDrinkerSelf3, SpellId.BloodDrinkerSelf4, SpellId.BloodDrinkerSelf5, SpellId.BloodDrinkerSelf6, SpellId.BloodDrinkerSelf7, SpellId.BloodDrinkerSelf8 },
            new[] { SpellId.SwiftKillerSelf1, SpellId.SwiftKillerSelf2, SpellId.SwiftKillerSelf3, SpellId.SwiftKillerSelf4, SpellId.SwiftKillerSelf5, SpellId.SwiftKillerSelf6, SpellId.SwiftKillerSelf7, SpellId.SwiftKillerSelf8 },
            new[] { SpellId.HeartSeekerSelf1, SpellId.HeartSeekerSelf2, SpellId.HeartSeekerSelf3, SpellId.HeartSeekerSelf4, SpellId.HeartSeekerSelf5, SpellId.HeartSeekerSelf6, SpellId.HeartSeekerSelf7, SpellId.HeartSeekerSelf8 },
            new[] { SpellId.DefenderSelf1, SpellId.DefenderSelf2, SpellId.DefenderSelf3, SpellId.DefenderSelf4, SpellId.DefenderSelf5, SpellId.DefenderSelf6, SpellId.DefenderSelf7, SpellId.DefenderSelf8 },
            new[] { SpellId.Impenetrability1, SpellId.Impenetrability2, SpellId.Impenetrability3, SpellId.Impenetrability4, SpellId.Impenetrability5, SpellId.Impenetrability6, SpellId.Impenetrability7, SpellId.Impenetrability8 },
            new[] { SpellId.BladeBane1, SpellId.BladeBane2, SpellId.BladeBane3, SpellId.BladeBane4, SpellId.BladeBane5, SpellId.BladeBane6, SpellId.BladeBane7, SpellId.BladeBane8 },
            new[] { SpellId.PiercingBane1, SpellId.PiercingBane2, SpellId.PiercingBane3, SpellId.PiercingBane4, SpellId.PiercingBane5, SpellId.PiercingBane6, SpellId.PiercingBane7, SpellId.PiercingBane8 },
            new[] { SpellId.FlameBane1, SpellId.FlameBane2, SpellId.FlameBane3, SpellId.FlameBane4, SpellId.FlameBane5, SpellId.FlameBane6, SpellId.FlameBane7, SpellId.FlameBane8 },
            new[] { SpellId.FrostBane1, SpellId.FrostBane2, SpellId.FrostBane3, SpellId.FrostBane4, SpellId.FrostBane5, SpellId.FrostBane6, SpellId.FrostBane7, SpellId.FrostBane8 },
            new[] { SpellId.AcidBane1, SpellId.AcidBane2, SpellId.AcidBane3, SpellId.AcidBane4, SpellId.AcidBane5, SpellId.AcidBane6, SpellId.AcidBane7, SpellId.AcidBane8 },
            new[] { SpellId.LightningBane1, SpellId.LightningBane2, SpellId.LightningBane3, SpellId.LightningBane4, SpellId.LightningBane5, SpellId.LightningBane6, SpellId.LightningBane7, SpellId.LightningBane8 },
            new[] { SpellId.ItemEnchantmentMasterySelf1, SpellId.ItemEnchantmentMasterySelf2, SpellId.ItemEnchantmentMasterySelf3, SpellId.ItemEnchantmentMasterySelf4, SpellId.ItemEnchantmentMasterySelf5, SpellId.ItemEnchantmentMasterySelf6, SpellId.ItemEnchantmentMasterySelf7, SpellId.ItemEnchantmentMasterySelf8 },
        };

        public static void TryDropForNomad(Player player, Creature killed)
        {
            if (player?.IsIronmanNomad != true || killed == null || killed is Player)
                return;

            var spellPool = BuildSpellPool(player);
            if (spellPool.Count == 0)
                return;

            var tier = Math.Clamp(killed.DeathTreasure?.Tier ?? ((killed.Level ?? 1) / 25) + 1, 1, 8);
            var chance = Math.Min(MaxDropChance, BaseDropChance + (tier * TierDropChance));
            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= chance)
                return;

            var spellLevel = Math.Clamp(((player.Level ?? 1) + 24) / 25, 1, 8);
            var family = spellPool[ThreadSafeRandom.Next(0, spellPool.Count - 1)];
            var spellId = family[spellLevel - 1];

            var rune = WorldObjectFactory.CreateNewWorldObject(NomadRuneWeenieClassId);
            if (rune == null)
                return;

            PrepareRune(rune, spellId);

            if (NomadRunePouch.TryStoreRune(player, rune, notify: true))
            {
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat($"You recover {rune.Name} from {killed.Name}.", ChatMessageType.Broadcast));
                return;
            }

            if (!player.TryCreateInInventoryWithNetworking(rune))
            {
                rune.Location = new ACE.Entity.Position(killed.Location);
                ACE.Server.Managers.LandblockManager.AddObject(rune);
                player.Session?.Network.EnqueueSend(new GameMessageSystemChat($"{rune.Name} falls to the ground because your pack is full.", ChatMessageType.Broadcast));
                return;
            }

            player.Session?.Network.EnqueueSend(new GameMessageSystemChat($"You recover {rune.Name} from {killed.Name}.", ChatMessageType.Broadcast));
        }

        public static bool IsNomadRune(WorldObject item)
        {
            return item?.WeenieClassId == NomadRuneWeenieClassId;
        }

        public static bool TryGetRuneInfo(uint spellId, out NomadRuneSchool school, out int tier)
        {
            if (TryFindSpell(CreatureRuneSpells, spellId, out tier))
            {
                school = NomadRuneSchool.Creature;
                return true;
            }

            if (TryFindSpell(LifeRuneSpells, spellId, out tier))
            {
                school = NomadRuneSchool.Life;
                return true;
            }

            if (TryFindSpell(ItemRuneSpells, spellId, out tier))
            {
                school = NomadRuneSchool.Item;
                return true;
            }

            school = NomadRuneSchool.None;
            tier = 0;
            return false;
        }

        public static List<SpellId> GetRitualSpells(NomadRuneSchool school, int tier)
        {
            tier = Math.Clamp(tier, 1, 8);
            var families = school switch
            {
                NomadRuneSchool.Creature => CreatureRuneSpells,
                NomadRuneSchool.Life     => LifeRuneSpells,
                NomadRuneSchool.Item     => ItemRuneSpells,
                _                        => null,
            };

            if (families == null)
                return new List<SpellId>();

            return families.Select(family => family[tier - 1]).ToList();
        }

        public static List<SpellId> GetStarterSpells(Player player)
        {
            var spells = new List<SpellId>();

            if (player == null)
                return spells;

            if (PlayerHasSchool(player, NomadRuneSchool.Creature))
            {
                spells.Add(SpellId.StrengthSelf1);
                spells.Add(SpellId.EnduranceSelf1);
                spells.Add(SpellId.CoordinationSelf1);
                spells.Add(SpellId.QuicknessSelf1);
            }

            if (PlayerHasSchool(player, NomadRuneSchool.Life))
            {
                spells.Add(SpellId.ArmorSelf1);
                spells.Add(SpellId.InvulnerabilitySelf1);
                spells.Add(SpellId.RegenerationSelf1);
                spells.Add(SpellId.RejuvenationSelf1);
            }

            if (PlayerHasSchool(player, NomadRuneSchool.Item))
            {
                spells.Add(SpellId.BloodDrinkerSelf1);
                spells.Add(SpellId.DefenderSelf1);
                spells.Add(SpellId.Impenetrability1);
            }

            return spells;
        }

        public static bool PlayerHasSchool(Player player, NomadRuneSchool school)
        {
            if (player == null)
                return false;

            Skill? skill = school switch
            {
                NomadRuneSchool.Creature => Skill.CreatureEnchantment,
                NomadRuneSchool.Life     => Skill.LifeMagic,
                NomadRuneSchool.Item     => Skill.ItemEnchantment,
                _                        => null,
            };

            return skill.HasValue && player.GetCreatureSkill(skill.Value).AdvancementClass >= SkillAdvancementClass.Trained;
        }

        public static string GetSchoolName(NomadRuneSchool school)
        {
            return school switch
            {
                NomadRuneSchool.Creature => "Creature",
                NomadRuneSchool.Life     => "Life",
                NomadRuneSchool.Item     => "Item",
                _                        => "Unknown",
            };
        }

        public static void NormalizeExistingRunes(Player player)
        {
            if (player == null)
                return;

            foreach (var rune in player.GetAllPossessions().Where(IsNomadRune))
            {
                var upgradedOldSingleUseRune = (rune.MaxStackSize ?? 1) < NomadRuneUses;

                rune.MaxStackSize = NomadRuneUses;
                rune.StackUnitEncumbrance ??= 1;
                if (rune.GetProperty(PropertyInt.StackUnitMass) == null)
                    rune.SetProperty(PropertyInt.StackUnitMass, 1);
                rune.StackUnitValue ??= Math.Max(1, (rune.Value ?? 5000) / NomadRuneUses);

                if (upgradedOldSingleUseRune && (rune.StackSize ?? 1) < NomadRuneUses)
                    rune.SetStackSize(NomadRuneUses);

                if (rune.SpellDID.HasValue)
                    ApplySpellIcon(rune, new Spell(rune.SpellDID.Value));

                rune.SaveBiotaToDatabase();
            }
        }

        private static List<SpellId[]> BuildSpellPool(Player player)
        {
            var spells = new List<SpellId[]>();

            if (player.GetCreatureSkill(Skill.CreatureEnchantment).AdvancementClass >= SkillAdvancementClass.Trained)
                spells.AddRange(CreatureRuneSpells);

            if (player.GetCreatureSkill(Skill.LifeMagic).AdvancementClass >= SkillAdvancementClass.Trained)
                spells.AddRange(LifeRuneSpells);

            if (player.GetCreatureSkill(Skill.ItemEnchantment).AdvancementClass >= SkillAdvancementClass.Trained)
                spells.AddRange(ItemRuneSpells);

            return spells;
        }

        public static void PrepareRune(WorldObject rune, SpellId spellId)
        {
            var spell = new Spell((uint)spellId);
            var spellName = spell.NotFound ? spellId.ToString() : spell.Name;

            rune.SpellDID = (uint)spellId;
            rune.Name = $"Nomad Rune of {spellName}";
            rune.LongDesc = $"A brittle rune scavenged from battle. It holds {NomadRuneUses} releases of {spellName}. Creature and Life runes buff you directly; Item runes redirect to your eligible equipped gear. Nomads can only find runes for magic schools they rolled trained or specialized.";
            rune.Use = $"Use this rune to release {spellName}.";
            rune.SetProperty(PropertyBool.IsIronmanItem, true);
            rune.MaxStackSize = NomadRuneUses;
            rune.StackUnitEncumbrance ??= 1;
            if (rune.GetProperty(PropertyInt.StackUnitMass) == null)
                rune.SetProperty(PropertyInt.StackUnitMass, 1);
            rune.StackUnitValue ??= 500;
            rune.SetStackSize(NomadRuneUses);
            rune.SetProperty(PropertyInt.UiEffects, (int)GetUiEffect(spellId));
            ApplySpellIcon(rune, spell);
        }

        private static void ApplySpellIcon(WorldObject rune, Spell spell)
        {
            if (rune == null || spell?._spellBase == null || spell._spellBase.Icon == 0)
                return;

            rune.SetProperty(PropertyDataId.Icon, spell._spellBase.Icon);
        }

        private static bool TryFindSpell(SpellId[][] families, uint spellId, out int tier)
        {
            foreach (var family in families)
            {
                for (var i = 0; i < family.Length; i++)
                {
                    if ((uint)family[i] == spellId)
                    {
                        tier = i + 1;
                        return true;
                    }
                }
            }

            tier = 0;
            return false;
        }

        private static UiEffects GetUiEffect(SpellId spellId)
        {
            var name = spellId.ToString();

            if (name.Contains("Regeneration", StringComparison.OrdinalIgnoreCase))
                return UiEffects.BoostHealth;
            if (name.Contains("Rejuvenation", StringComparison.OrdinalIgnoreCase))
                return UiEffects.BoostStamina;
            if (name.Contains("Mana", StringComparison.OrdinalIgnoreCase))
                return UiEffects.BoostMana;

            return UiEffects.Magical;
        }
    }
}
