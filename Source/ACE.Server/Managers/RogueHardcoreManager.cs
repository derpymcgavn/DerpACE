using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ACE.Common;
using ACE.Common.Extensions;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Database.Models.World;
using ACE.Server.Factories;
using ACE.Server.Factories.Enum;
using ACE.Server.DerpAce;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Hardcore Rogue is an optional Hardcore submode: proficiency advances faster,
    /// and each level offers a persistent choice of one boon. Boons are intentionally
    /// stored on character properties so the mode survives logout/restart without a
    /// schema change.
    /// </summary>
    public static class RogueHardcoreManager
    {
        private sealed class BoonDef
        {
            public BoonDef(string id, string name, string description, Func<Player, bool> eligible, Action<Player> apply, bool oneOff = false)
            {
                Id = id;
                Name = name;
                Description = description;
                Eligible = eligible;
                Apply = apply;
                OneOff = oneOff;
            }

            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public Func<Player, bool> Eligible { get; }
            public Action<Player> Apply { get; }
            public bool OneOff { get; }
        }

        private static readonly Skill[] MeleeGrowth = { Skill.LightWeapons, Skill.HeavyWeapons, Skill.FinesseWeapons, Skill.TwoHandedCombat, Skill.DualWield };
        private static readonly Skill[] MagicGrowth = { Skill.WarMagic, Skill.LifeMagic, Skill.VoidMagic, Skill.CreatureEnchantment, Skill.ItemEnchantment, Skill.ManaConversion };
        private static readonly Skill[] DefenseGrowth = { Skill.MeleeDefense, Skill.MissileDefense, Skill.MagicDefense, Skill.Shield };
        private static readonly Skill[] CraftGrowth = { Skill.Alchemy, Skill.Cooking, Skill.Fletching, Skill.ArmorTinkering, Skill.ItemTinkering, Skill.MagicItemTinkering, Skill.WeaponTinkering, Skill.Salvaging };

        private static readonly List<BoonDef> Boons = new List<BoonDef>
        {
            new BoonDef("road_legs", "Road Legs", "+1 rank to Run and Jump, if trained.", p => IsUsable(p, Skill.Run) || IsUsable(p, Skill.Jump), p => AwardSkills(p, Skill.Run, Skill.Jump)),
            new BoonDef("blade_memory", "Blade Memory", "+1 rank to your most developed trained melee weapon skill.", p => BestSkill(p, MeleeGrowth) != null, p => AwardBestSkill(p, MeleeGrowth)),
            new BoonDef("arcane_spark", "Arcane Spark", "+1 rank to your most developed trained magic skill.", p => BestSkill(p, MagicGrowth) != null, p => AwardBestSkill(p, MagicGrowth)),
            new BoonDef("guarded_breath", "Guarded Breath", "+1 rank to your weakest trained defensive skill.", p => WeakestSkill(p, DefenseGrowth) != null, p => AwardWeakestSkill(p, DefenseGrowth)),
            new BoonDef("maker_hands", "Maker's Hands", "+1 rank to your most developed trained craft skill.", p => BestSkill(p, CraftGrowth) != null, p => AwardBestSkill(p, CraftGrowth)),
            new BoonDef("dirty_instinct", "Dirty Instinct", "+1 rank to Dirty Fighting or Sneak Attack, whichever is trained and lower.", p => IsUsable(p, Skill.DirtyFighting) || IsUsable(p, Skill.SneakAttack), p => AwardWeakestSkill(p, Skill.DirtyFighting, Skill.SneakAttack)),
            new BoonDef("field_medic", "Field Medic", "+1 rank to Healing, if trained.", p => IsUsable(p, Skill.Healing), p => AwardSkills(p, Skill.Healing)),
            new BoonDef("summoners_thread", "Summoner's Thread", "+1 rank to Summoning, if trained.", p => IsUsable(p, Skill.Summoning), p => AwardSkills(p, Skill.Summoning)),
            new BoonDef("lockstep", "Lockstep", "+1 rank to Lockpick, if trained.", p => IsUsable(p, Skill.Lockpick), p => AwardSkills(p, Skill.Lockpick)),
            new BoonDef("wartorn_focus", "Wartorn Focus", "+1 rank to Arcane Lore and Mana Conversion, if trained.", p => IsUsable(p, Skill.ArcaneLore) || IsUsable(p, Skill.ManaConversion), p => AwardSkills(p, Skill.ArcaneLore, Skill.ManaConversion)),

            new BoonDef("borrowed_pulse", "Borrowed Pulse", "One-off fate perk: gain +1 Hardcore life, capped at 2 for Rogue Hardcore.", p => (p.GetProperty(PropertyInt.HardcoreLives) ?? 0) < 2, p => AddHardcoreLife(p, 2), oneOff: true),
            new BoonDef("red_pocket", "Red Pocket", "One-off survival kit: receive healing supplies scaled to your level.", p => true, p => GrantFixedItems(p, "Red Pocket", 273, 2, 2643, 1), oneOff: true),
            new BoonDef("blue_pocket", "Blue Pocket", "One-off arcane kit: receive mana supplies scaled to your level.", p => true, p => GrantFixedItems(p, "Blue Pocket", 273, 1, 2597, 2), oneOff: true),
            new BoonDef("utility_belt", "Utility Belt", "One-off utility kit: receive lockpick and field supplies.", p => true, p => GrantFixedItems(p, "Utility Belt", 8328, 1, 273, 1), oneOff: true),

            new BoonDef("weapon_cache", "Weapon Cache", "Item roll: receive a level-appropriate mutated weapon for your run.", p => true, p => GrantRolledWeapon(p, "Weapon Cache", null)),
            new BoonDef("blade_cache", "Blade Cache", "Item roll: receive a level-appropriate melee weapon.", p => BestSkill(p, MeleeGrowth) != null, p => GrantRolledWeapon(p, "Blade Cache", "melee")),
            new BoonDef("missile_cache", "Missile Cache", "Item roll: receive a level-appropriate missile weapon.", p => IsUsable(p, Skill.MissileWeapons), p => GrantRolledWeapon(p, "Missile Cache", "missile")),
            new BoonDef("caster_cache", "Caster Cache", "Item roll: receive a level-appropriate caster.", p => BestSkill(p, MagicGrowth) != null, p => GrantRolledWeapon(p, "Caster Cache", "caster")),
            new BoonDef("armor_patch", "Armor Patch", "Item roll: receive a level-appropriate armor or shield piece.", p => true, p => GrantRolledArmor(p, "Armor Patch")),
        };

        public static bool IsActive(Player player)
        {
            return player != null
                && DerpACEConfig.HardcoreRogueEnabled
                && player.GetProperty(PropertyBool.IsHardcoreRogue) == true;
        }

        public static void Enable(Player player)
        {
            if (player == null)
                return;

            player.SetProperty(PropertyBool.IsHardcoreRogue, true);
            player.SetModeTitle("ROGUE HC");
            player.SendMessage("Hardcore Rogue mode is active. Your used skills grow faster, and each level offers a boon choice.", ChatMessageType.Advancement);
            GrantGuideBook(player);
            OnLevelUp(player, player.Level ?? 1);
        }

        public static void OnLevelUp(Player player, int level)
        {
            if (!IsActive(player) || level <= 1)
                return;

            if (HasBoonForLevel(player, level))
                return;

            var pending = ParsePending(player);
            if (pending.Level > 0)
                return;

            var choices = RollChoices(player, Math.Clamp(DerpACEConfig.HardcoreRogueBoonChoices, 1, 5));
            if (choices.Count == 0)
                return;

            player.SetProperty(PropertyString.HardcoreRoguePendingChoices, $"{level}|{string.Join(',', choices.Select(c => c.Id))}");
            player.SendMessage($"[Rogue] Level {level} boon ready. Use /rogue choices, then /rogue pick 1-{choices.Count}.", ChatMessageType.Advancement);
        }

        public static IReadOnlyList<string> GetChosenBoonLabels(Player player)
        {
            if (player == null)
                return Array.Empty<string>();

            return ParseChosen(player)
                .OrderBy(entry => entry.Level)
                .Select(entry =>
                {
                    var boon = FindBoon(entry.Id);
                    return $"Level {entry.Level}: {boon?.Name ?? entry.Id}";
                })
                .ToList();
        }

        public static string GetPendingBoonLabel(Player player)
        {
            if (player == null)
                return null;

            var pending = ParsePending(player);
            return pending.Level > 0 ? $"Level {pending.Level}" : null;
        }

        public static void ShowStatus(Player player)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Rogue is not active on this character.", ChatMessageType.System);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Hardcore Rogue ===");
            sb.AppendLine($"  Proficiency window: {DerpACEConfig.HardcoreRogueProficiencyMinutes:0.##} minutes");
            sb.AppendLine($"  Proficiency multiplier: {DerpACEConfig.HardcoreRogueProficiencyXpMultiplier:0.##}x");
            var chosen = ParseChosen(player).ToList();
            sb.AppendLine($"  Chosen boons: {chosen.Count}");
            foreach (var entry in chosen.OrderBy(e => e.Level))
            {
                var boon = FindBoon(entry.Id);
                sb.AppendLine($"    Level {entry.Level}: {boon?.Name ?? entry.Id}");
            }

            var pending = ParsePending(player);
            if (pending.Level > 0)
                sb.AppendLine($"  Pending level {pending.Level} boon: use /rogue choices");

            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        public static void ShowChoices(Player player)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Rogue is not active on this character.", ChatMessageType.System);
                return;
            }

            var pending = ParsePending(player);
            if (pending.Level <= 0 || pending.Ids.Count == 0)
            {
                player.SendMessage("You do not have a pending Rogue boon choice.", ChatMessageType.System);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Rogue Boon Choice: Level {pending.Level} ===");
            for (var i = 0; i < pending.Ids.Count; i++)
            {
                var boon = FindBoon(pending.Ids[i]);
                if (boon == null)
                    continue;
                sb.AppendLine($"  {i + 1}. {boon.Name} - {boon.Description}");
            }
            sb.AppendLine("Use /rogue pick <number> to choose. This cannot be undone for this life.");
            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        public static void PickChoice(Player player, int index)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Rogue is not active on this character.", ChatMessageType.System);
                return;
            }

            var pending = ParsePending(player);
            if (pending.Level <= 0 || pending.Ids.Count == 0)
            {
                player.SendMessage("You do not have a pending Rogue boon choice.", ChatMessageType.System);
                return;
            }

            if (index < 1 || index > pending.Ids.Count)
            {
                player.SendMessage($"Pick a boon from 1 to {pending.Ids.Count}.", ChatMessageType.System);
                return;
            }

            var boon = FindBoon(pending.Ids[index - 1]);
            if (boon == null)
            {
                player.SendMessage("That boon is no longer valid. Ask an admin to clear your Rogue pending choice.", ChatMessageType.System);
                return;
            }

            if (HasBoonForLevel(player, pending.Level))
            {
                player.RemoveProperty(PropertyString.HardcoreRoguePendingChoices);
                player.SendMessage("You already chose a boon for that level.", ChatMessageType.System);
                return;
            }

            boon.Apply(player);
            AddChosen(player, pending.Level, boon.Id);
            player.RemoveProperty(PropertyString.HardcoreRoguePendingChoices);
            player.SendMessage($"[Rogue] You chose {boon.Name}.", ChatMessageType.Advancement);
            player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
        }

        private static void GrantGuideBook(Player player)
        {
            var book = WorldObjectFactory.CreateNewWorldObject(HardcodedWeenies.RogueHardcoreGuideBookWeenieClassId);
            if (book == null)
            {
                player.SendMessage("[Rogue] Failed to create the Rogue Hardcore guide book.", ChatMessageType.System);
                return;
            }

            book.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceHardcore);
            if (player.TryCreateInInventoryWithNetworking(book))
                player.SendMessage($"[Rogue] A guide book has been placed in your pack: {book.Name}.", ChatMessageType.Advancement);
            else
            {
                player.SendMessage($"[Rogue] Failed to place {book.Name} in your pack.", ChatMessageType.System);
                book.Destroy();
            }
        }

        private static List<BoonDef> RollChoices(Player player, int count)
        {
            var takenOneOffs = ParseChosen(player).Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pool = Boons.Where(b => b.Eligible(player) && (!b.OneOff || !takenOneOffs.Contains(b.Id))).ToList();
            var choices = new List<BoonDef>();
            while (pool.Count > 0 && choices.Count < count)
            {
                var index = ThreadSafeRandom.Next(0, pool.Count - 1);
                choices.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return choices;
        }


        private static void AddHardcoreLife(Player player, int cap)
        {
            var lives = Math.Max(0, player.GetProperty(PropertyInt.HardcoreLives) ?? 0);
            if (lives >= cap)
            {
                player.SendMessage($"[Rogue] Your borrowed pulse is already at its limit ({cap}).", ChatMessageType.System);
                return;
            }

            player.SetProperty(PropertyInt.HardcoreLives, lives + 1);
            player.SendMessage($"[Rogue] Something ugly decides you are not finished yet. Hardcore lives: {lives + 1}/{cap}.", ChatMessageType.Advancement);
        }

        private static int GetLootTier(Player player)
        {
            var level = player?.Level ?? 1;
            if (level >= 240) return 8;
            if (level >= 200) return 7;
            if (level >= 160) return 6;
            if (level >= 120) return 5;
            if (level >= 80) return 4;
            if (level >= 45) return 3;
            if (level >= 20) return 2;
            return 1;
        }

        private static TreasureDeath BuildProfile(Player player)
        {
            return new TreasureDeath
            {
                Tier = GetLootTier(player),
                LootQualityMod = 0.15f,
                ItemChance = 100,
                ItemMinAmount = 1,
                ItemMaxAmount = 1,
                ItemTreasureTypeSelectionChances = 9,
                MagicItemChance = 100,
                MagicItemMinAmount = 1,
                MagicItemMaxAmount = 1,
                MagicItemTreasureTypeSelectionChances = 9,
                MundaneItemChance = 100,
                MundaneItemMinAmount = 1,
                MundaneItemMaxAmount = 1,
                MundaneItemTypeSelectionChances = 9,
            };
        }

        private static void GrantRolledWeapon(Player player, string source, string family)
        {
            var profile = BuildProfile(player);
            WorldObject item = family switch
            {
                "melee" => LootGenerationFactory.CreateMeleeWeapon(profile, true, requestedTier: profile.Tier),
                "missile" => LootGenerationFactory.CreateMissileWeapon(profile, true, requestedTier: profile.Tier),
                "caster" => LootGenerationFactory.CreateCaster(profile, true, requestedTier: profile.Tier),
                _ => LootGenerationFactory.CreateWeapon(profile, true, requestedTier: profile.Tier),
            };
            GrantItem(player, item, source);
        }

        private static void GrantRolledArmor(Player player, string source)
        {
            var profile = BuildProfile(player);
            var item = LootGenerationFactory.CreateRandomLootObjects(profile, TreasureItemCategory.MagicItem, TreasureItemType.Armor)
                ?? LootGenerationFactory.CreateRandomLootObjects(profile, TreasureItemCategory.Item, TreasureItemType.Armor);
            GrantItem(player, item, source);
        }

        private static void GrantFixedItems(Player player, string source, params int[] wcidAndCountPairs)
        {
            for (var i = 0; i + 1 < wcidAndCountPairs.Length; i += 2)
            {
                var wcid = (uint)Math.Max(0, wcidAndCountPairs[i]);
                var count = Math.Max(1, wcidAndCountPairs[i + 1]);
                for (var n = 0; n < count; n++)
                {
                    var item = WorldObjectFactory.CreateNewWorldObject(wcid);
                    GrantItem(player, item, source);
                }
            }
        }

        private static void GrantItem(Player player, WorldObject item, string source)
        {
            if (item == null)
            {
                player.SendMessage($"[Rogue] {source} failed to produce an item.", ChatMessageType.System);
                return;
            }

            item.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceHardcore);
            if (!player.TryCreateInInventoryWithNetworking(item))
            {
                player.SendMessage($"[Rogue] {source} could not fit {item.Name} in your pack.", ChatMessageType.System);
                item.Destroy();
                return;
            }

            player.SendMessage($"[Rogue] {source} grants {item.Name}.", ChatMessageType.Advancement);
        }
        private static bool IsUsable(Player player, Skill skill)
        {
            var creatureSkill = player.GetCreatureSkill(skill, false);
            return creatureSkill != null && creatureSkill.AdvancementClass >= SkillAdvancementClass.Trained && !creatureSkill.IsMaxRank;
        }

        private static CreatureSkill BestSkill(Player player, params Skill[] skills)
        {
            return skills.Select(skill => player.GetCreatureSkill(skill, false))
                .Where(skill => skill != null && skill.AdvancementClass >= SkillAdvancementClass.Trained && !skill.IsMaxRank)
                .OrderByDescending(skill => skill.Current)
                .ThenBy(skill => skill.Skill)
                .FirstOrDefault();
        }

        private static CreatureSkill WeakestSkill(Player player, params Skill[] skills)
        {
            return skills.Select(skill => player.GetCreatureSkill(skill, false))
                .Where(skill => skill != null && skill.AdvancementClass >= SkillAdvancementClass.Trained && !skill.IsMaxRank)
                .OrderBy(skill => skill.Current)
                .ThenBy(skill => skill.Skill)
                .FirstOrDefault();
        }

        private static void AwardBestSkill(Player player, params Skill[] skills)
        {
            var best = BestSkill(player, skills);
            if (best != null)
                AwardSkills(player, best.Skill);
        }

        private static void AwardWeakestSkill(Player player, params Skill[] skills)
        {
            var weakest = WeakestSkill(player, skills);
            if (weakest != null)
                AwardSkills(player, weakest.Skill);
        }

        private static void AwardSkills(Player player, params Skill[] skills)
        {
            foreach (var skill in skills)
            {
                if (!IsUsable(player, skill))
                    continue;
                player.AwardSkillPoints(skill, 1);
            }
        }

        private static BoonDef FindBoon(string id)
        {
            return Boons.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static (int Level, List<string> Ids) ParsePending(Player player)
        {
            var raw = player.GetProperty(PropertyString.HardcoreRoguePendingChoices) ?? string.Empty;
            var parts = raw.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var level))
                return (0, new List<string>());
            return (level, parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList());
        }

        private static IEnumerable<(int Level, string Id)> ParseChosen(Player player)
        {
            var raw = player.GetProperty(PropertyString.HardcoreRogueBoons) ?? string.Empty;
            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], out var level))
                    continue;
                yield return (level, parts[1]);
            }
        }

        private static bool HasBoonForLevel(Player player, int level)
        {
            return ParseChosen(player).Any(entry => entry.Level == level);
        }

        private static void AddChosen(Player player, int level, string id)
        {
            var entries = ParseChosen(player).Where(entry => entry.Level != level).ToList();
            entries.Add((level, id));
            player.SetProperty(PropertyString.HardcoreRogueBoons, string.Join(";", entries.OrderBy(entry => entry.Level).Select(entry => $"{entry.Level}:{entry.Id}")));
        }
    }
}





