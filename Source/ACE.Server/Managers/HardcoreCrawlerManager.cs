using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ACE.Common;
using ACE.Common.Extensions;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Database.Models.World;
using ACE.Server.Factories;
using ACE.Server.Factories.Enum;
using ACE.Server.DerpAce;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.Managers
{
    /// <summary>
    /// Hardcore Crawler is an optional Hardcore submode: proficiency advances faster,
    /// and each level offers a persistent choice of one boon. Boons are intentionally
    /// stored on character properties so the mode survives logout/restart without a
    /// schema change.
    /// </summary>
    public static class HardcoreCrawlerManager
    {
        private const double SkillPracticeCooldownSeconds = 5.0;
        private static readonly ConcurrentDictionary<string, double> _skillPracticeCooldowns = new ConcurrentDictionary<string, double>();

        private enum BoonRarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary,
            Mythical,
        }


        private enum TrialKind
        {
            Hunt,
            HighRiskHunt,
            MutatedHunt,
            FieldMedicine,
            Rations,
            SkillGrowth,
        }

        private sealed class TrialState
        {
            public int Level { get; set; }
            public TrialKind Kind { get; set; }
            public int Required { get; set; }
            public int Progress { get; set; }
            public bool RewardClaimed { get; set; }
        }

        private sealed class TrialDef
        {
            public TrialDef(TrialKind kind, string name, string objective, Func<Player, bool> eligible)
            {
                Kind = kind;
                Name = name;
                Objective = objective;
                Eligible = eligible;
            }

            public TrialKind Kind { get; }
            public string Name { get; }
            public string Objective { get; }
            public Func<Player, bool> Eligible { get; }
        }

        private sealed class BoonDef
        {
            public BoonDef(string id, string name, string description, Func<Player, bool> eligible, Action<Player> apply, BoonRarity rarity = BoonRarity.Common, bool oneOff = false)
            {
                Id = id;
                Name = name;
                Description = description;
                Eligible = eligible;
                Apply = apply;
                Rarity = rarity;
                OneOff = oneOff;
            }

            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public Func<Player, bool> Eligible { get; }
            public Action<Player> Apply { get; }
            public BoonRarity Rarity { get; }
            public bool OneOff { get; }
        }

        private static readonly Skill[] MeleeGrowth = { Skill.LightWeapons, Skill.HeavyWeapons, Skill.FinesseWeapons, Skill.TwoHandedCombat, Skill.DualWield };
        private static readonly Skill[] MagicGrowth = { Skill.WarMagic, Skill.LifeMagic, Skill.VoidMagic, Skill.CreatureEnchantment, Skill.ItemEnchantment, Skill.ManaConversion };
        private static readonly Skill[] DefenseGrowth = { Skill.MeleeDefense, Skill.MissileDefense, Skill.MagicDefense, Skill.Shield };
        private static readonly Skill[] CraftGrowth = { Skill.Alchemy, Skill.Cooking, Skill.Fletching, Skill.ArmorTinkering, Skill.ItemTinkering, Skill.MagicItemTinkering, Skill.WeaponTinkering, Skill.Salvaging };
        private static int CrawlerTrialInterval => Math.Max(0, DerpACEConfig.HardcoreCrawlerTrialInterval);

        private static readonly List<TrialDef> Trials = new List<TrialDef>
        {
            new TrialDef(TrialKind.Hunt, "Crowd Wants Blood", "Defeat creatures that still award kill XP.", p => true),
            new TrialDef(TrialKind.HighRiskHunt, "Bad Idea Hunt", "Defeat creatures at least 10 levels above you.", p => true),
            new TrialDef(TrialKind.MutatedHunt, "Mutation Insurance", "Defeat mutated creatures.", p => (p.Level ?? 1) >= 100),
            new TrialDef(TrialKind.FieldMedicine, "Back-Alley Surgeon", "Successfully heal with healing kits.", p => IsUsable(p, Skill.Healing) || p.GetCreatureSkill(Skill.Healing, false) != null),
            new TrialDef(TrialKind.Rations, "Eat Through It", "Eat food while surviving the road.", p => true),
            new TrialDef(TrialKind.SkillGrowth, "Road Lessons", "Gain skill ranks through use.", p => true),
        };

        private static readonly List<BoonDef> Boons = new List<BoonDef>
        {
            new BoonDef("road_legs", "Road Legs", "Conditioning perk: Run and Jump use hardens the stats behind movement.", p => IsUsable(p, Skill.Run) || IsUsable(p, Skill.Jump), p => AwardSkills(p, Skill.Run, Skill.Jump)),
            new BoonDef("blade_memory", "Blade Memory", "Conditioning perk: your most developed melee style hardens its related stats.", p => BestSkill(p, MeleeGrowth) != null, p => AwardBestSkill(p, MeleeGrowth)),
            new BoonDef("arcane_spark", "Arcane Spark", "Conditioning perk: your most developed magic style sharpens its related stats.", p => BestSkill(p, MagicGrowth) != null, p => AwardBestSkill(p, MagicGrowth)),
            new BoonDef("guarded_breath", "Guarded Breath", "Conditioning perk: your weakest trained defense reinforces its related stats.", p => WeakestSkill(p, DefenseGrowth) != null, p => AwardWeakestSkill(p, DefenseGrowth)),
            new BoonDef("maker_hands", "Maker's Hands", "Conditioning perk: your most developed craft improves its related stats.", p => BestSkill(p, CraftGrowth) != null, p => AwardBestSkill(p, CraftGrowth)),
            new BoonDef("dirty_instinct", "Dirty Instinct", "Conditioning perk: Dirty Fighting or Sneak Attack instincts shape their related stats.", p => IsUsable(p, Skill.DirtyFighting) || IsUsable(p, Skill.SneakAttack), p => AwardWeakestSkill(p, Skill.DirtyFighting, Skill.SneakAttack)),
            new BoonDef("field_medic", "Field Medic", "Conditioning perk: Healing practice reinforces survival stats.", p => IsUsable(p, Skill.Healing), p => AwardSkills(p, Skill.Healing)),
            new BoonDef("summoners_thread", "Summoner's Thread", "Conditioning perk: Summoning practice reinforces its related stats.", p => IsUsable(p, Skill.Summoning), p => AwardSkills(p, Skill.Summoning)),
            new BoonDef("lockstep", "Lockstep", "Conditioning perk: Lockpick practice sharpens its related stats.", p => IsUsable(p, Skill.Lockpick), p => AwardSkills(p, Skill.Lockpick)),
            new BoonDef("wartorn_focus", "Wartorn Focus", "Conditioning perk: Arcane Lore and Mana Conversion sharpen their related stats.", p => IsUsable(p, Skill.ArcaneLore) || IsUsable(p, Skill.ManaConversion), p => AwardSkills(p, Skill.ArcaneLore, Skill.ManaConversion)),

            new BoonDef("borrowed_pulse", "Borrowed Pulse", "One-off fate perk: gain +1 Hardcore life, capped at 2 for Hardcore Crawler.", p => (p.GetProperty(PropertyInt.HardcoreLives) ?? 0) < 2, p => AddHardcoreLife(p, 2), rarity: BoonRarity.Mythical, oneOff: true),
            new BoonDef("red_pocket", "Red Pocket", "One-off survival kit: receive healing supplies scaled to your level.", p => true, p => GrantFixedItems(p, "Red Pocket", 273, 2, 2643, 1), rarity: BoonRarity.Uncommon, oneOff: true),
            new BoonDef("blue_pocket", "Blue Pocket", "One-off arcane kit: receive mana supplies scaled to your level.", p => true, p => GrantFixedItems(p, "Blue Pocket", 273, 1, 2597, 2), rarity: BoonRarity.Uncommon, oneOff: true),
            new BoonDef("utility_belt", "Utility Belt", "One-off utility kit: receive lockpick and field supplies.", p => true, p => GrantFixedItems(p, "Utility Belt", 8328, 1, 273, 1), rarity: BoonRarity.Uncommon, oneOff: true),

            new BoonDef("brawn_debt", "Brawn Debt", "Flaw boon: +5 Strength and Coordination ranks, but -6 Max Mana ranks.", p => true, ApplyBrawnDebt, BoonRarity.Rare, oneOff: true),
            new BoonDef("glass_ritual", "Glass Ritual", "Flaw boon: +5 Focus and Self ranks, but -6 Max Health ranks.", p => true, ApplyGlassRitual, BoonRarity.Rare, oneOff: true),
            new BoonDef("runner_tax", "Runner's Tax", "Flaw boon: +5 Quickness ranks and movement conditioning, but -4 Strength and -3 Max Health ranks.", p => true, ApplyRunnersTax, BoonRarity.Rare, oneOff: true),
            new BoonDef("blood_for_sparks", "Blood for Sparks", "Flaw boon: +8 Max Mana ranks, but -5 Max Health and Max Stamina ranks.", p => true, ApplyBloodForSparks, BoonRarity.Epic, oneOff: true),
            new BoonDef("iron_stomach", "Iron Stomach", "Flaw boon: +6 Max Health and Max Stamina ranks, but -4 Focus and Self ranks.", p => true, ApplyIronStomach, BoonRarity.Epic, oneOff: true),
            new BoonDef("bottomless_engine", "Bottomless Engine", "Flaw boon: +10 Max Stamina ranks, but you must keep eating food or hunger drains stamina every minute.", p => true, ApplyBottomlessEngine, BoonRarity.Epic, oneOff: true),
            new BoonDef("field_surgeon_oath", "Field Surgeon Oath", "Flaw boon: healing kits restore 35% more when you use them, but restoration spells landing on you restore 35% less.", p => true, ApplyFieldSurgeonOath, BoonRarity.Rare, oneOff: true),
        };

        public static bool IsActive(Player player)
        {
            return player != null
                && DerpACEConfig.HardcoreCrawlerEnabled
                && player.GetProperty(PropertyBool.IsHardcoreCrawler) == true;
        }

        public static void Enable(Player player)
        {
            if (player == null)
                return;

            player.SetProperty(PropertyBool.IsHardcoreCrawler, true);
            player.SetModeTitle("CRAWLER HC");
            InitializeCrawlerBaseline(player);
            player.SendMessage("Hardcore Crawler mode is active. You start with nothing trained; skills, stats, levels, and boons now come from use.", ChatMessageType.Advancement);
            GrantGuideBook(player);
            OnLevelUp(player, player.Level ?? 1);
        }

        public static bool EnsureConverted(Player player, bool notify = false)
        {
            if (player == null || player.GetProperty(PropertyBool.IsHardcoreCrawler) != true)
                return false;

            player.SetModeTitle("CRAWLER HC");
            if (player.GetProperty(PropertyBool.IsHardcore) != true)
                player.SetProperty(PropertyBool.IsHardcore, true);
            if (!player.GetProperty(PropertyInt.HardcoreLives).HasValue)
                player.SetProperty(PropertyInt.HardcoreLives, 1);

            var creditsAdjusted = NormalizeCrawlerSkillCredits(player);
            QueueMissingBoonChoices(player, player.Level ?? 1, notify: false);
            QueueMissingTrials(player, player.Level ?? 1, notify: false);
            player.ChangesDetected = true;

            if (notify)
            {
                var message = creditsAdjusted
                    ? "[Crawler] This character has been converted to Hardcore Crawler naming/title state, and skill credits have been repaired."
                    : "[Crawler] This character has been converted to Hardcore Crawler naming and title state.";
                player.SendMessage(message, ChatMessageType.Advancement);
            }

            return true;
        }

        public static void OnLevelUp(Player player, int level)
        {
            if (!IsActive(player) || level <= 1)
                return;

            QueueMissingBoonChoices(player, level, notify: true);
            QueueMissingTrials(player, level, notify: true);
            AwardMilestoneCache(player, level);
        }

        public static bool TryConvertQuestExperience(Player player, long amount)
        {
            if (!IsActive(player) || amount <= 0)
                return false;

            player.EnqueueAction(new ActionEventDelegate(() => ConvertQuestExperience(player, amount)));
            return true;
        }

        private static void ConvertQuestExperience(Player player, long amount)
        {
            if (!IsActive(player) || amount <= 0)
                return;

            var threshold = GetQuestFavorThreshold(player);
            var favor = Math.Max(0, player.GetProperty(PropertyInt64.HardcoreCrawlerQuestFavor) ?? 0) + amount;
            var caches = 0;

            while (favor >= threshold && caches < 10)
            {
                favor -= threshold;
                caches++;
            }

            player.SetProperty(PropertyInt64.HardcoreCrawlerQuestFavor, favor);
            player.ChangesDetected = true;

            if (caches <= 0)
            {
                player.SendMessage($"[Crawler] Quest XP converted into Crawler Favor: {favor:N0}/{threshold:N0}.", ChatMessageType.Advancement);
                return;
            }

            var plural = caches == 1 ? "cache" : "caches";
            player.SendMessage($"[Crawler] Quest XP converted into Crawler Favor. The crowd sends {caches:N0} quest {plural}; banked favor: {favor:N0}/{threshold:N0}.", ChatMessageType.Advancement);
            for (var i = 0; i < caches; i++)
                GrantQuestFavorCache(player);
        }

        private static long GetQuestFavorThreshold(Player player)
        {
            var level = Math.Clamp(player?.Level ?? 1, 1, (int)Player.GetMaxLevel());
            var xpToNext = player?.GetXPToNextLevel(level) ?? 0;
            return Math.Max(1L, (long)Math.Min((ulong)long.MaxValue, xpToNext));
        }

        private static void GrantQuestFavorCache(Player player)
        {
            GrantMilestoneCache(player, Math.Max(1, player.Level ?? 1));
        }

        private static void QueueMissingBoonChoices(Player player, int currentLevel, bool notify)
        {
            var pending = ParsePendingQueue(player);
            var pendingLevels = pending.Select(entry => entry.Level).ToHashSet();
            var chosenLevels = ParseChosen(player).Select(entry => entry.Level).ToHashSet();
            var added = 0;

            for (var level = 2; level <= currentLevel; level++)
            {
                if (chosenLevels.Contains(level) || pendingLevels.Contains(level))
                    continue;

                var choices = RollChoices(player, Math.Clamp(DerpACEConfig.HardcoreCrawlerBoonChoices, 1, 5));
                if (choices.Count == 0)
                    continue;

                pending.Add((level, choices.Select(c => c.Id).ToList()));
                pendingLevels.Add(level);
                added++;
            }

            if (added <= 0)
                return;

            SavePendingQueue(player, pending);
            player.ChangesDetected = true;

            var next = pending.OrderBy(entry => entry.Level).First();
            var plural = pending.Count == 1 ? "boon" : "boons";
            if (notify)
                player.SendMessage($"[Crawler] {pending.Count} pending {plural} ready. Next: level {next.Level}. Use /crawler choices, then /crawler pick 1-{next.Ids.Count}.", ChatMessageType.Advancement);
        }

        private static void InitializeCrawlerBaseline(Player player)
        {
            var originalLevel = Math.Max(1, player.Level ?? 1);
            var startingCredits = GetCrawlerSkillCreditEntitlement(player, originalLevel);

            player.Level = 1;
            player.TotalExperience = 0;
            player.AvailableExperience = 0;
            player.AvailableSkillCredits = startingCredits;
            player.TotalSkillCredits = startingCredits;
            player.RemoveProperty(PropertyInt.HardcoreCrawlerUsageRankProgress);
            player.RemoveProperty(PropertyString.HardcoreCrawlerAutoTrainProgress);
            player.RemoveProperty(PropertyString.HardcoreCrawlerAutoSpecProgress);
            player.RemoveProperty(PropertyString.HardcoreCrawlerLastProgressSkill);

            foreach (var skill in player.Skills.Values)
            {
                skill.AdvancementClass = SkillAdvancementClass.Untrained;
                skill.InitLevel = 0;
                skill.Ranks = 0;
                skill.ExperienceSpent = 0;
            }

            foreach (var attribute in player.Attributes.Values)
            {
                attribute.StartingValue = DerpACEConfig.HardcoreCrawlerBaseAttribute;
                attribute.Ranks = 0;
                attribute.ExperienceSpent = 0;
            }

            foreach (var vital in player.Vitals.Values)
            {
                vital.StartingValue = DerpACEConfig.HardcoreCrawlerBaseVital;
                vital.Ranks = 0;
                vital.ExperienceSpent = 0;
            }

            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, player.Level ?? 1),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0),
                new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.TotalExperience, player.TotalExperience ?? 0),
                new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.AvailableExperience, player.AvailableExperience ?? 0));

            foreach (var skill in player.Skills.Values)
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(player, skill));
            foreach (var attribute in player.Attributes.Values)
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attribute));
            foreach (var vital in player.Vitals.Values)
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));

            player.SetMaxVitals();
            player.ChangesDetected = true;
        }

        public static void OnUntrainedSkillUsed(Player player, CreatureSkill skill, uint difficulty)
        {
            if (!IsActive(player) || skill == null || skill.AdvancementClass != SkillAdvancementClass.Untrained)
                return;

            var threshold = Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoTrainUses);
            var progress = ParseSkillProgress(player.GetProperty(PropertyString.HardcoreCrawlerAutoTrainProgress));
            progress.TryGetValue(skill.Skill, out var current);
            if (current >= threshold)
                return;

            current++;
            progress[skill.Skill] = current;
            player.SetProperty(PropertyString.HardcoreCrawlerLastProgressSkill, skill.Skill.ToSentence());
            SaveSkillProgress(player, PropertyString.HardcoreCrawlerAutoTrainProgress, progress);
            player.ChangesDetected = true;

            if (current >= threshold)
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"[Crawler] {skill.Skill.ToSentence()} is ready to train. Use /crawler train {skill.Skill.ToSentence()} if you want it.", ChatMessageType.Advancement));
        }

        public static void OnSkillPracticed(Player player, CreatureSkill skill, uint difficulty)
        {
            if (!IsActive(player) || skill == null || difficulty == 0)
                return;

            if (skill.AdvancementClass == SkillAdvancementClass.Untrained)
            {
                OnUntrainedSkillUsed(player, skill, difficulty);
                return;
            }

            if (skill.AdvancementClass < SkillAdvancementClass.Trained || skill.IsMaxRank)
                return;

            var now = Time.GetUnixTime();
            var cooldownKey = $"{player.Guid.Full}:{(uint)skill.Skill}";
            if (_skillPracticeCooldowns.TryGetValue(cooldownKey, out var nextAllowed) && nextAllowed > now)
                return;

            var xpToNextRank = player.GetXpToNextRank(skill);
            if (xpToNextRank == null || xpToNextRank.Value == 0)
                return;

            var difficultyFactor = Math.Clamp(difficulty / (double)Math.Max(1, skill.Current), 0.25, 2.0);
            var amount = (uint)Math.Round(xpToNextRank.Value * 0.015 * difficultyFactor);
            amount = Math.Min(Math.Max(amount, (uint)Math.Min(difficulty, 250)), 25000u);
            amount = Math.Min(amount, skill.ExperienceLeft);
            if (amount == 0)
                return;

            _skillPracticeCooldowns[cooldownKey] = now + SkillPracticeCooldownSeconds;
            player.RefundXP(amount);
            var raiseChain = new ActionChain();
            raiseChain.AddDelayForOneTick();
            raiseChain.AddAction(player, () => player.HandleActionRaiseSkillFromUse(skill.Skill, amount));
            raiseChain.EnqueueChain();
        }

        public static void OnSkillRankGained(Player player, CreatureSkill skill, int ranksGained)
        {
            if (!IsActive(player) || skill == null || ranksGained <= 0)
                return;

            player.SetProperty(PropertyString.HardcoreCrawlerLastProgressSkill, skill.Skill.ToSentence());
            AdvanceTrials(player, trial => trial.Kind == TrialKind.SkillGrowth, ranksGained);

            if (skill.AdvancementClass == SkillAdvancementClass.Trained)
            {
                TryAutoSpecializeFromUse(player, skill, ranksGained);
                return;
            }

            if (skill.AdvancementClass != SkillAdvancementClass.Specialized)
                return;

            var threshold = Math.Max(1, DerpACEConfig.HardcoreCrawlerSkillRanksPerLevel);
            var progress = Math.Max(0, player.GetProperty(PropertyInt.HardcoreCrawlerUsageRankProgress) ?? 0) + ranksGained;
            while (progress >= threshold && (player.Level ?? 1) < Player.GetMaxLevel())
            {
                progress -= threshold;
                GrantUsageLevel(player, skill.Skill, threshold);
            }
            player.SetProperty(PropertyInt.HardcoreCrawlerUsageRankProgress, progress);
            player.ChangesDetected = true;
        }

        private static void GrantUsageLevel(Player player, Skill sourceSkill, int threshold)
        {
            var oldLevel = player.Level ?? 1;
            var newLevel = Math.Min((int)Player.GetMaxLevel(), oldLevel + 1);
            if (newLevel <= oldLevel)
                return;

            player.Level = newLevel;
            AwardNormalLevelSkillCredits(player, newLevel);
            var levelXp = (long)Player.GetTotalXP(newLevel);
            if ((player.TotalExperience ?? 0) < levelXp)
                player.TotalExperience = levelXp;

            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.Level, newLevel),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0),
                new GameMessagePrivateUpdatePropertyInt64(player, PropertyInt64.TotalExperience, player.TotalExperience ?? 0));

            player.SetMaxVitals();
            player.PlayParticleEffect(PlayScript.LevelUp, player.Guid);
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"[Crawler] {sourceSkill.ToSentence()} has shaped you through {threshold} specialized rank gains. You are now level {newLevel}.", ChatMessageType.Advancement));
            OnLevelUp(player, newLevel);
        }

        private static void TryAutoSpecializeFromUse(Player player, CreatureSkill skill, int ranksGained)
        {
            if ((player.Level ?? 1) < Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecMinLevel))
                return;

            var threshold = Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecRanks);
            var progress = ParseSkillProgress(player.GetProperty(PropertyString.HardcoreCrawlerAutoSpecProgress));
            progress.TryGetValue(skill.Skill, out var current);
            if (current >= threshold)
                return;

            current += ranksGained;
            progress[skill.Skill] = Math.Min(current, threshold);
            SaveSkillProgress(player, progress);
            player.ChangesDetected = true;

            if (current >= threshold)
                player.Session.Network.EnqueueSend(new GameMessageSystemChat($"[Crawler] {skill.Skill.ToSentence()} is ready to specialize. Use /crawler spec {skill.Skill.ToSentence()} if you want it.", ChatMessageType.Advancement));
        }

        public static void TrainReadySkill(Player player, string skillName)
        {
            if (!TryGetReadyCrawlerSkill(player, skillName, PropertyString.HardcoreCrawlerAutoTrainProgress, Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoTrainUses), out var skill, out var progress))
                return;

            if (skill.AdvancementClass != SkillAdvancementClass.Untrained)
            {
                progress.Remove(skill.Skill);
                SaveSkillProgress(player, PropertyString.HardcoreCrawlerAutoTrainProgress, progress);
                player.SendMessage($"[Crawler] {skill.Skill.ToSentence()} is already trained or better.", ChatMessageType.System);
                return;
            }

            if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill.Skill, out var skillBase))
                return;

            var trainedCost = skillBase.TrainedCost;
            var trainedCap = Math.Max(0, DerpACEConfig.HardcoreCrawlerMaxTrainedSkills);
            if (trainedCap > 0 && GetTrainedSkillCount(player) >= trainedCap)
            {
                player.SendMessage($"[Crawler] {skill.Skill.ToSentence()} is ready, but your trained skill limit is full ({trainedCap}). Untrain something first.", ChatMessageType.System);
                return;
            }

            if ((player.AvailableSkillCredits ?? 0) < trainedCost)
            {
                player.SendMessage($"[Crawler] {skill.Skill.ToSentence()} is ready, but needs {trainedCost} skill credits. Free credits or earn more Crawler levels.", ChatMessageType.System);
                return;
            }

            if (!player.TrainSkill(skill.Skill, trainedCost))
            {
                player.SendMessage($"[Crawler] Failed to train {skill.Skill.ToSentence()}.", ChatMessageType.System);
                return;
            }

            progress.Remove(skill.Skill);
            SaveSkillProgress(player, PropertyString.HardcoreCrawlerAutoTrainProgress, progress);
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdateSkill(player, skill),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0));
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"[Crawler] {skill.Skill.ToSentence()} has become trained by choice.", ChatMessageType.Advancement));
            player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
            AwardRelatedStatProgress(player, skill.Skill, 1);
            player.ChangesDetected = true;
        }

        public static void SpecializeReadySkill(Player player, string skillName)
        {
            if (!TryGetReadyCrawlerSkill(player, skillName, PropertyString.HardcoreCrawlerAutoSpecProgress, Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecRanks), out var skill, out var progress))
                return;

            if (skill.AdvancementClass != SkillAdvancementClass.Trained)
            {
                progress.Remove(skill.Skill);
                SaveSkillProgress(player, progress);
                player.SendMessage($"[Crawler] {skill.Skill.ToSentence()} is not currently trained.", ChatMessageType.System);
                return;
            }

            if (!CanAutoSpecialize(player, skill, out var specializeCost, out var blockedReason))
            {
                if (!string.IsNullOrWhiteSpace(blockedReason))
                    player.SendMessage(blockedReason, ChatMessageType.System);
                return;
            }

            if (!player.SpecializeSkill(skill.Skill, specializeCost, false))
            {
                player.SendMessage($"[Crawler] Failed to specialize {skill.Skill.ToSentence()}.", ChatMessageType.System);
                return;
            }

            progress.Remove(skill.Skill);
            SaveSkillProgress(player, progress);
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdateSkill(player, skill),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0));
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"[Crawler] {skill.Skill.ToSentence()} has become specialized by choice.", ChatMessageType.Advancement));
            player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
            player.ChangesDetected = true;
        }

        public static void ShowReadyTraining(Player player, bool specialize)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            var property = specialize ? PropertyString.HardcoreCrawlerAutoSpecProgress : PropertyString.HardcoreCrawlerAutoTrainProgress;
            var threshold = specialize ? Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecRanks) : Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoTrainUses);
            var ready = GetReadySkillNames(player, property, threshold).ToList();
            if (ready.Count == 0)
            {
                player.SendMessage(specialize ? "[Crawler] No skills are ready to specialize." : "[Crawler] No skills are ready to train.", ChatMessageType.System);
                return;
            }

            var command = specialize ? "/crawler spec" : "/crawler train";
            player.SendMessage($"[Crawler] Ready to {(specialize ? "specialize" : "train")}: {string.Join(", ", ready)}. Use {command} <skill>.", ChatMessageType.System);
        }

        private static bool TryGetReadyCrawlerSkill(Player player, string skillName, PropertyString property, int threshold, out CreatureSkill skill, out Dictionary<Skill, int> progress)
        {
            skill = null;
            progress = ParseSkillProgress(player?.GetProperty(property));

            if (!IsActive(player))
            {
                player?.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return false;
            }

            if (string.IsNullOrWhiteSpace(skillName))
            {
                player.SendMessage("Usage: /crawler train <skill> or /crawler spec <skill>", ChatMessageType.System);
                return false;
            }

            if (!TryParseSkillName(skillName, out var parsedSkill))
            {
                player.SendMessage($"[Crawler] Unknown skill: {skillName}.", ChatMessageType.System);
                return false;
            }

            if (!progress.TryGetValue(parsedSkill, out var current) || current < threshold)
            {
                player.SendMessage($"[Crawler] {parsedSkill.ToSentence()} is not ready yet.", ChatMessageType.System);
                return false;
            }

            skill = player.GetCreatureSkill(parsedSkill, false);
            if (skill == null)
            {
                player.SendMessage($"[Crawler] {parsedSkill.ToSentence()} is not available on this character.", ChatMessageType.System);
                return false;
            }

            return true;
        }

        private static void AppendReadySkillStatus(StringBuilder sb, Player player, bool specialize)
        {
            var property = specialize ? PropertyString.HardcoreCrawlerAutoSpecProgress : PropertyString.HardcoreCrawlerAutoTrainProgress;
            var threshold = specialize ? Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecRanks) : Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoTrainUses);
            var ready = GetReadySkillNames(player, property, threshold).ToList();
            if (ready.Count == 0)
                return;

            var label = specialize ? "Ready to specialize" : "Ready to train";
            var command = specialize ? "/crawler spec" : "/crawler train";
            var visible = ready.Take(12).ToList();
            var extra = ready.Count > visible.Count ? $" (+{ready.Count - visible.Count} more)" : string.Empty;
            sb.AppendLine($"  {label}: {string.Join(", ", visible)}{extra} - use {command} <skill>");
        }

        private static IEnumerable<string> GetReadySkillNames(Player player, PropertyString property, int threshold)
        {
            var progress = ParseSkillProgress(player.GetProperty(property));
            foreach (var entry in progress.Where(entry => entry.Value >= threshold).OrderBy(entry => entry.Key))
            {
                var skill = player.GetCreatureSkill(entry.Key, false);
                if (skill != null)
                    yield return entry.Key.ToSentence();
            }
        }

        private static bool TryParseSkillName(string raw, out Skill skill)
        {
            var normalized = new string((raw ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            foreach (Skill candidate in Enum.GetValues(typeof(Skill)))
            {
                if (candidate == Skill.None)
                    continue;

                var enumName = new string(candidate.ToString().Where(char.IsLetterOrDigit).ToArray());
                var sentenceName = new string(candidate.ToSentence().Where(char.IsLetterOrDigit).ToArray());
                if (string.Equals(normalized, enumName, StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, sentenceName, StringComparison.OrdinalIgnoreCase))
                {
                    skill = candidate;
                    return true;
                }
            }

            skill = Skill.None;
            return false;
        }

        private static int GetStartingSkillCredits(Player player)
        {
            if (DatManager.PortalDat.CharGen.HeritageGroups.TryGetValue((uint)player.Heritage, out var heritageGroup))
                return (int)heritageGroup.SkillCredits;

            return Math.Max(0, player.TotalSkillCredits ?? player.AvailableSkillCredits ?? 0);
        }

        private static void AwardNormalLevelSkillCredits(Player player, int level)
        {
            var credits = (int)DatManager.PortalDat.XpTable.CharacterLevelSkillCreditList[level];
            if (credits <= 0)
                return;

            player.AvailableSkillCredits += credits;
            player.TotalSkillCredits += credits;
        }

        private static bool NormalizeCrawlerSkillCredits(Player player)
        {
            var oldAvailable = player.AvailableSkillCredits ?? 0;
            var oldTotal = player.TotalSkillCredits ?? 0;
            var entitledTotal = GetCrawlerSkillCreditEntitlement(player, player.Level ?? 1);
            var spentCredits = GetSpentSkillCredits(player);
            var targetAvailable = Math.Max(0, entitledTotal - spentCredits);

            if (oldAvailable == targetAvailable && oldTotal == entitledTotal)
                return false;

            player.AvailableSkillCredits = targetAvailable;
            player.TotalSkillCredits = entitledTotal;
            player.Session?.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, targetAvailable));
            player.ChangesDetected = true;
            return true;
        }

        private static int GetCrawlerSkillCreditEntitlement(Player player, int level)
        {
            var total = Math.Max(0, GetStartingSkillCredits(player));
            var cappedLevel = Math.Clamp(level, 1, (int)Player.GetMaxLevel());

            for (var i = 2; i <= cappedLevel; i++)
                total += (int)DatManager.PortalDat.XpTable.CharacterLevelSkillCreditList[i];

            return Math.Max(total, player.TotalSkillCredits ?? 0);
        }

        private static int GetSpentSkillCredits(Player player)
        {
            var spent = 0;
            foreach (var kvp in player.Skills)
            {
                var skill = kvp.Value;
                if (skill == null || skill.AdvancementClass < SkillAdvancementClass.Trained)
                    continue;

                if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)kvp.Key, out var skillBase))
                    continue;

                spent += skillBase.TrainedCost;
                if (skill.AdvancementClass == SkillAdvancementClass.Specialized)
                    spent += skillBase.UpgradeCostFromTrainedToSpecialized;
            }

            return spent;
        }

        private static bool CanAutoSpecialize(Player player, CreatureSkill skill, out int specializeCost, out string blockedReason)
        {
            specializeCost = 0;
            blockedReason = null;

            if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill.Skill, out var skillBase))
                return false;

            specializeCost = skillBase.UpgradeCostFromTrainedToSpecialized;
            if ((player.AvailableSkillCredits ?? 0) < specializeCost)
            {
                blockedReason = $"[Crawler] {skill.Skill.ToSentence()} is ready to specialize through use, but needs {specializeCost} skill credits. Lower another skill to free credits.";
                return false;
            }

            var specializedCost = GetAdjustedSpecializedCost(player, skill.Skill, skillBase.SpecializedCost);
            var specBudget = Math.Max(0, DerpACEConfig.HardcoreCrawlerSpecializedCreditBudget);
            if (specBudget > 0 && GetTotalSpecializedCredits(player) + specializedCost > specBudget)
            {
                blockedReason = $"[Crawler] {skill.Skill.ToSentence()} is ready to specialize through use, but your specialized skill budget is full ({GetTotalSpecializedCredits(player)}/{specBudget}). Unspecialize something first.";
                return false;
            }

            return true;
        }

        private static int GetTrainedSkillCount(Player player)
        {
            var total = 0;
            foreach (var skill in player.Skills.Values)
            {
                if (skill?.AdvancementClass >= SkillAdvancementClass.Trained)
                    total++;
            }
            return total;
        }

        private static int GetSpecializedSkillCount(Player player)
        {
            var total = 0;
            foreach (var skill in player.Skills.Values)
            {
                if (skill?.AdvancementClass == SkillAdvancementClass.Specialized)
                    total++;
            }
            return total;
        }

        private static int GetTotalSpecializedCredits(Player player)
        {
            var total = 0;
            foreach (var kvp in player.Skills)
            {
                if (kvp.Value.AdvancementClass != SkillAdvancementClass.Specialized)
                    continue;

                switch (kvp.Key)
                {
                    case Skill.None:
                    case Skill.ArmorTinkering:
                    case Skill.ItemTinkering:
                    case Skill.MagicItemTinkering:
                    case Skill.WeaponTinkering:
                    case Skill.Salvaging:
                        continue;
                }

                if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)kvp.Key, out var skillBase))
                    continue;

                total += GetAdjustedSpecializedCost(player, kvp.Key, skillBase.SpecializedCost);
            }

            return total;
        }

        private static int GetAdjustedSpecializedCost(Player player, Skill skill, int defaultCost)
        {
            if (DatManager.PortalDat.CharGen.HeritageGroups.TryGetValue((uint)player.Heritage, out var heritageGroup))
            {
                var adjustedCost = heritageGroup.Skills.FirstOrDefault(i => i.SkillNum == (int)skill);
                if (adjustedCost != null)
                    return adjustedCost.PrimaryCost;
            }

            return defaultCost;
        }

        private static void AwardRelatedStatProgress(Player player, Skill skill, int ranksGained)
        {
            if (ranksGained <= 0)
                return;

            if (Player.MeleeSkills.Contains(skill) || skill == Skill.DualWield || skill == Skill.Shield)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Strength, PropertyAttribute.Coordination);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina);
            }
            else if (Player.MissileSkills.Contains(skill))
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Coordination, PropertyAttribute.Quickness, PropertyAttribute.Focus);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxStamina);
            }
            else if (Player.MagicSkills.Contains(skill) || skill == Skill.ManaConversion || skill == Skill.ArcaneLore)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Focus, PropertyAttribute.Self);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxMana);
            }
            else if (skill == Skill.MeleeDefense || skill == Skill.MissileDefense || skill == Skill.MagicDefense)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Quickness, PropertyAttribute.Coordination);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina);
            }
            else if (CraftGrowth.Contains(skill) || skill == Skill.AssessCreature || skill == Skill.AssessPerson || skill == Skill.Deception)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Focus, PropertyAttribute.Coordination);
            }
            else if (skill == Skill.Healing)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Focus, PropertyAttribute.Coordination);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxHealth);
            }
            else if (skill == Skill.Run || skill == Skill.Jump)
            {
                AwardAttributeRanks(player, ranksGained, PropertyAttribute.Quickness, PropertyAttribute.Strength);
                AwardVitalRanks(player, ranksGained, PropertyAttribute2nd.MaxStamina);
            }

            player.SetMaxVitals();
            player.ChangesDetected = true;
        }

        private static void AwardAttributeRanks(Player player, int ranks, params PropertyAttribute[] attributes)
        {
            foreach (var attributeType in attributes)
            {
                if (!player.Attributes.TryGetValue(attributeType, out var attribute) || attribute.IsMaxRank)
                    continue;

                var xpTable = DatManager.PortalDat.XpTable.AttributeXpList;
                var targetRank = Math.Min(xpTable.Count - 1, (int)attribute.Ranks + ranks);
                attribute.ExperienceSpent = xpTable[targetRank];
                attribute.Ranks = (ushort)Player.CalcAttributeRank(attribute.ExperienceSpent);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attribute));
            }
        }

        private static void AwardVitalRanks(Player player, int ranks, params PropertyAttribute2nd[] vitals)
        {
            foreach (var vitalType in vitals)
            {
                if (!player.Vitals.TryGetValue(vitalType, out var vital) || vital.IsMaxRank)
                    continue;

                var xpTable = DatManager.PortalDat.XpTable.VitalXpList;
                var targetRank = Math.Min(xpTable.Count - 1, (int)vital.Ranks + ranks);
                vital.ExperienceSpent = xpTable[targetRank];
                vital.Ranks = (ushort)Player.CalcVitalRank(vital.ExperienceSpent);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));
            }
        }

        private static Dictionary<Skill, int> ParseSkillProgress(string raw)
        {
            var result = new Dictionary<Skill, int>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                if (parts.Length != 2 || !Enum.TryParse(parts[0], out Skill skill) || !int.TryParse(parts[1], out var count))
                    continue;
                if (count > 0)
                    result[skill] = count;
            }
            return result;
        }

        private static void SaveSkillProgress(Player player, Dictionary<Skill, int> progress)
        {
            SaveSkillProgress(player, PropertyString.HardcoreCrawlerAutoSpecProgress, progress);
        }

        private static void SaveSkillProgress(Player player, PropertyString property, Dictionary<Skill, int> progress)
        {
            var raw = string.Join(";", progress.Where(entry => entry.Value > 0).OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:{entry.Value}"));
            if (raw.Length == 0)
                player.RemoveProperty(property);
            else
                player.SetProperty(property, raw);
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
                    return boon != null ? $"Level {entry.Level}: [{boon.Rarity}] {boon.Name}" : $"Level {entry.Level}: {entry.Id}";
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
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            var sb = new StringBuilder();
            var rankProgress = Math.Max(0, player.GetProperty(PropertyInt.HardcoreCrawlerUsageRankProgress) ?? 0);
            var rankThreshold = Math.Max(1, DerpACEConfig.HardcoreCrawlerSkillRanksPerLevel);
            var lastSkill = player.GetProperty(PropertyString.HardcoreCrawlerLastProgressSkill);
            sb.AppendLine("=== Hardcore Crawler ===");
            sb.AppendLine($"  Skill training: untrained skills become ready after {Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoTrainUses)} uses");
            sb.AppendLine($"  Skill specialization: trained skills become ready at level {Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecMinLevel)} after {Math.Max(1, DerpACEConfig.HardcoreCrawlerAutoSpecRanks)} ranks");
            sb.AppendLine($"  Level progress: {rankProgress}/{rankThreshold} specialized rank gains");
            var trainedCount = GetTrainedSkillCount(player);
            var trainedCap = Math.Max(0, DerpACEConfig.HardcoreCrawlerMaxTrainedSkills);
            var specCount = GetSpecializedSkillCount(player);
            var specCredits = GetTotalSpecializedCredits(player);
            var specBudget = Math.Max(0, DerpACEConfig.HardcoreCrawlerSpecializedCreditBudget);
            sb.AppendLine($"  Skill credits: {player.AvailableSkillCredits ?? 0}/{player.TotalSkillCredits ?? 0} available");
            sb.AppendLine(trainedCap > 0 ? $"  Trained skills: {trainedCount}/{trainedCap}" : $"  Trained skills: {trainedCount} (uncapped)");
            sb.AppendLine(specBudget > 0 ? $"  Specialized skills: {specCount}, budget {specCredits}/{specBudget}" : $"  Specialized skills: {specCount}, budget {specCredits} (uncapped)");
            AppendReadySkillStatus(sb, player, specialize: false);
            AppendReadySkillStatus(sb, player, specialize: true);
            sb.AppendLine($"  Quest favor: {Math.Max(0, player.GetProperty(PropertyInt64.HardcoreCrawlerQuestFavor) ?? 0):N0}/{GetQuestFavorThreshold(player):N0}");
            sb.AppendLine("  Spell foci: built in");
            if (!string.IsNullOrWhiteSpace(lastSkill))
                sb.AppendLine($"  Last growth: {lastSkill}");
            sb.AppendLine($"  Proficiency window: {DerpACEConfig.HardcoreCrawlerProficiencyMinutes:0.##} minutes");
            sb.AppendLine($"  Proficiency multiplier: {DerpACEConfig.HardcoreCrawlerProficiencyXpMultiplier:0.##}x");
            var chosen = ParseChosen(player).ToList();
            sb.AppendLine($"  Chosen boons: {chosen.Count}");
            foreach (var entry in chosen.OrderBy(e => e.Level))
            {
                var boon = FindBoon(entry.Id);
                sb.AppendLine(boon != null ? $"    Level {entry.Level}: [{boon.Rarity}] {boon.Name}" : $"    Level {entry.Level}: {entry.Id}");
            }

            QueueMissingBoonChoices(player, player.Level ?? 1, notify: false);
            var pendingQueue = ParsePendingQueue(player);
            if (pendingQueue.Count > 0)
                sb.AppendLine($"  Pending boons: {pendingQueue.Count} (next: level {pendingQueue.OrderBy(entry => entry.Level).First().Level})");

            QueueMissingTrials(player, player.Level ?? 1, notify: false);
            var activeTrial = ParseTrials(player).OrderBy(trial => trial.Level).FirstOrDefault();
            if (activeTrial != null)
            {
                var trialDef = FindTrial(activeTrial.Kind);
                var ready = activeTrial.Progress >= activeTrial.Required ? " READY" : string.Empty;
                sb.AppendLine($"  Active trial: Level {activeTrial.Level} {trialDef?.Name ?? activeTrial.Kind.ToString()} - {Math.Min(activeTrial.Progress, activeTrial.Required)}/{activeTrial.Required}{ready}");
            }

            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        public static void ShowChoices(Player player)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            QueueMissingBoonChoices(player, player.Level ?? 1, notify: false);
            var pendingQueue = ParsePendingQueue(player);
            var pending = pendingQueue.OrderBy(entry => entry.Level).FirstOrDefault();
            if (pending.Level <= 0 || pending.Ids.Count == 0)
            {
                player.SendMessage("You do not have a pending Crawler boon choice.", ChatMessageType.System);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Crawler Boon Choice: Level {pending.Level} ===");
            if (pendingQueue.Count > 1)
                sb.AppendLine($"You have {pendingQueue.Count} pending boon choices. Older levels must be chosen first.");
            for (var i = 0; i < pending.Ids.Count; i++)
            {
                var boon = FindBoon(pending.Ids[i]);
                if (boon == null)
                    continue;
                sb.AppendLine($"  {i + 1}. [{boon.Rarity}] {boon.Name} - {boon.Description}");
            }
            sb.AppendLine("Use /crawler pick <number> to choose. This cannot be undone for this life.");
            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        public static void PickChoice(Player player, int index)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            QueueMissingBoonChoices(player, player.Level ?? 1, notify: false);
            var pendingQueue = ParsePendingQueue(player);
            var pending = pendingQueue.OrderBy(entry => entry.Level).FirstOrDefault();
            if (pending.Level <= 0 || pending.Ids.Count == 0)
            {
                player.SendMessage("You do not have a pending Crawler boon choice.", ChatMessageType.System);
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
                player.SendMessage("That boon is no longer valid. Ask an admin to clear your Crawler pending choice.", ChatMessageType.System);
                return;
            }

            if (HasBoonForLevel(player, pending.Level))
            {
                pendingQueue.RemoveAll(entry => entry.Level == pending.Level);
                SavePendingQueue(player, pendingQueue);
                player.SendMessage("You already chose a boon for that level.", ChatMessageType.System);
                return;
            }

            boon.Apply(player);
            AddChosen(player, pending.Level, boon.Id);
            pendingQueue.RemoveAll(entry => entry.Level == pending.Level);
            SavePendingQueue(player, pendingQueue);
            var remaining = pendingQueue.Count > 0 ? $" {pendingQueue.Count} pending boon choice(s) remain." : string.Empty;
            player.SendMessage($"[Crawler] You chose {boon.Name}.{remaining}", ChatMessageType.Advancement);
            player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
        }


        private static void QueueMissingTrials(Player player, int currentLevel, bool notify)
        {
            var interval = CrawlerTrialInterval;
            if (!IsActive(player) || interval <= 0 || currentLevel < interval)
                return;

            var trials = ParseTrials(player);
            var completed = ParseLevelSet(player.GetProperty(PropertyString.HardcoreCrawlerCompletedTrials));
            var added = 0;

            for (var level = interval; level <= currentLevel; level += interval)
            {
                if (completed.Contains(level) || trials.Any(trial => trial.Level == level))
                    continue;

                var def = RollTrial(player, level);
                if (def == null)
                    continue;

                trials.Add(new TrialState
                {
                    Level = level,
                    Kind = def.Kind,
                    Required = GetTrialRequirement(level, def.Kind),
                    Progress = 0,
                    RewardClaimed = false,
                });
                added++;
            }

            if (added <= 0)
                return;

            SaveTrials(player, trials);
            player.ChangesDetected = true;

            if (notify)
                player.SendMessage($"[Crawler] {added} milestone trial(s) opened. Use /crawler trial to view and /crawler trial claim when complete.", ChatMessageType.Advancement);
        }

        private static TrialDef RollTrial(Player player, int level)
        {
            var pool = Trials.Where(trial => trial.Eligible(player)).ToList();
            if (pool.Count == 0)
                return null;

            return pool[ThreadSafeRandom.Next(0, pool.Count - 1)];
        }

        private static int GetTrialRequirement(int level, TrialKind kind)
        {
            var interval = Math.Max(1, CrawlerTrialInterval);
            var tier = Math.Max(1, level / interval);
            switch (kind)
            {
                case TrialKind.HighRiskHunt:
                    return Math.Clamp(2 + tier, 3, 30);
                case TrialKind.MutatedHunt:
                    return Math.Clamp(1 + tier / 2, 2, 20);
                case TrialKind.FieldMedicine:
                    return Math.Clamp(3 + tier * 2, 5, 60);
                case TrialKind.Rations:
                    return Math.Clamp(4 + tier * 2, 6, 75);
                case TrialKind.SkillGrowth:
                    return Math.Clamp(4 + tier * 2, 6, 75);
                default:
                    return Math.Clamp(5 + tier * 3, 8, 100);
            }
        }

        public static void ShowTrial(Player player)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            QueueMissingTrials(player, player.Level ?? 1, notify: false);
            var trials = ParseTrials(player).OrderBy(trial => trial.Level).ToList();
            var completed = ParseLevelSet(player.GetProperty(PropertyString.HardcoreCrawlerCompletedTrials));

            var sb = new StringBuilder();
            sb.AppendLine("=== Hardcore Crawler Trials ===");
            var interval = CrawlerTrialInterval;
            sb.AppendLine(interval > 0 ? $"  Trial cadence: every {interval} Crawler levels" : "  Trial cadence: disabled");

            if (trials.Count == 0)
            {
                sb.AppendLine("  No active milestone trials yet.");
                if (interval > 0)
                    sb.AppendLine($"  Next trial opens at level {GetNextTrialLevel(player.Level ?? 1)}.");
            }
            else
            {
                foreach (var trial in trials)
                {
                    var def = FindTrial(trial.Kind);
                    var ready = trial.Progress >= trial.Required;
                    sb.AppendLine($"  Level {trial.Level}: {def?.Name ?? trial.Kind.ToString()} - {Math.Min(trial.Progress, trial.Required)}/{trial.Required}{(ready ? " READY" : string.Empty)}");
                    if (def != null)
                        sb.AppendLine($"    {def.Objective}");
                }
                sb.AppendLine("  Use /crawler trial claim to claim the oldest completed trial reward.");
            }

            if (completed.Count > 0)
                sb.AppendLine($"  Completed trial milestones: {string.Join(", ", completed.OrderBy(level => level))}");

            player.SendMessage(sb.ToString(), ChatMessageType.System);
        }

        public static void ClaimTrial(Player player)
        {
            if (!IsActive(player))
            {
                player.SendMessage("Hardcore Crawler is not active on this character.", ChatMessageType.System);
                return;
            }

            QueueMissingTrials(player, player.Level ?? 1, notify: false);
            var trials = ParseTrials(player).OrderBy(trial => trial.Level).ToList();
            var trial = trials.FirstOrDefault(entry => entry.Progress >= entry.Required && !entry.RewardClaimed);
            if (trial == null)
            {
                player.SendMessage("You do not have a completed Crawler trial reward to claim.", ChatMessageType.System);
                return;
            }

            var completed = ParseLevelSet(player.GetProperty(PropertyString.HardcoreCrawlerCompletedTrials));
            completed.Add(trial.Level);
            trials.RemoveAll(entry => entry.Level == trial.Level);
            SaveLevelSet(player, PropertyString.HardcoreCrawlerCompletedTrials, completed);
            SaveTrials(player, trials);
            GrantFanBox(player, trial);
            player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
            player.ChangesDetected = true;
        }

        public static void OnCreatureKilled(Player player, Creature creature, long xpEarned)
        {
            if (!IsActive(player) || creature == null || xpEarned <= 0)
                return;

            AdvanceTrials(player, trial =>
            {
                switch (trial.Kind)
                {
                    case TrialKind.Hunt:
                        return true;
                    case TrialKind.HighRiskHunt:
                        return (creature.Level ?? 1) >= (player.Level ?? 1) + 10;
                    case TrialKind.MutatedHunt:
                        return (creature.GetProperty(PropertyInt.MutatorCount) ?? 0) > 0;
                    default:
                        return false;
                }
            });
        }

        public static void OnHealingKitUsed(Player healer, Player target, uint actualHealAmount)
        {
            if (!IsActive(healer) || actualHealAmount == 0)
                return;

            AdvanceTrials(healer, trial => trial.Kind == TrialKind.FieldMedicine);
        }

        private static int GetNextTrialLevel(int currentLevel)
        {
            var interval = Math.Max(1, CrawlerTrialInterval);
            return ((Math.Max(1, currentLevel) / interval) + 1) * interval;
        }

        private static void AdvanceTrials(Player player, Func<TrialState, bool> matcher, int amount = 1)
        {
            QueueMissingTrials(player, player.Level ?? 1, notify: false);
            var trials = ParseTrials(player);
            var changed = false;

            foreach (var trial in trials.OrderBy(entry => entry.Level))
            {
                if (trial.Progress >= trial.Required || !matcher(trial))
                    continue;

                trial.Progress = Math.Min(trial.Required, trial.Progress + Math.Max(1, amount));
                changed = true;

                if (trial.Progress >= trial.Required)
                {
                    var def = FindTrial(trial.Kind);
                    player.SendMessage($"[Crawler] Trial complete: Level {trial.Level} {def?.Name ?? trial.Kind.ToString()}. Use /crawler trial claim for your fan box.", ChatMessageType.Advancement);
                }
                break;
            }

            if (!changed)
                return;

            SaveTrials(player, trials);
            player.ChangesDetected = true;
        }

        private static void GrantFanBox(Player player, TrialState trial)
        {
            var def = FindTrial(trial.Kind);
            var source = def?.Name ?? "Crawler Fan Box";
            player.SendMessage($"[Crawler] Fan Box unlocked: {source}. The unseen crowd sends supplies for surviving a terrible idea.", ChatMessageType.Advancement);

            switch (trial.Kind)
            {
                case TrialKind.HighRiskHunt:
                    GrantRolledWeapon(player, "Bad Idea Fan Box", GetPreferredWeaponFamily(player));
                    GrantFixedItems(player, "Bad Idea Fan Box", 273, 2, 2643, 2, 2597, 1);
                    break;
                case TrialKind.MutatedHunt:
                    GrantRolledArmor(player, "Mutation Insurance Fan Box");
                    GrantRolledWeapon(player, "Mutation Insurance Fan Box", GetPreferredWeaponFamily(player));
                    break;
                case TrialKind.FieldMedicine:
                    GrantFixedItems(player, "Back-Alley Surgeon Fan Box", 273, 4, 2643, 2);
                    AwardVitalRanks(player, 1, PropertyAttribute2nd.MaxHealth);
                    break;
                case TrialKind.Rations:
                    GrantFixedItems(player, "Eat Through It Fan Box", 273, 2, 2643, 3, 2597, 1);
                    AwardVitalRanks(player, 1, PropertyAttribute2nd.MaxStamina);
                    break;
                case TrialKind.SkillGrowth:
                    GrantRolledWeapon(player, "Road Lessons Fan Box", GetPreferredWeaponFamily(player));
                    AwardRelatedStatProgress(player, Skill.Run, 1);
                    break;
                default:
                    GrantRolledWeapon(player, "Crowd Wants Blood Fan Box", GetPreferredWeaponFamily(player));
                    GrantFixedItems(player, "Crowd Wants Blood Fan Box", 273, 1, 2643, 1);
                    break;
            }
        }

        private static TrialDef FindTrial(TrialKind kind)
        {
            return Trials.FirstOrDefault(trial => trial.Kind == kind);
        }

        private static List<TrialState> ParseTrials(Player player)
        {
            var result = new List<TrialState>();
            var raw = player.GetProperty(PropertyString.HardcoreCrawlerTrials) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split('|');
                if (parts.Length < 4 || !int.TryParse(parts[0], out var level) || level <= 0)
                    continue;
                if (!Enum.TryParse(parts[1], out TrialKind kind))
                    continue;
                if (!int.TryParse(parts[2], out var required) || required <= 0)
                    continue;
                if (!int.TryParse(parts[3], out var progress))
                    progress = 0;

                var claimed = parts.Length >= 5 && parts[4] == "1";
                result.Add(new TrialState
                {
                    Level = level,
                    Kind = kind,
                    Required = required,
                    Progress = Math.Clamp(progress, 0, required),
                    RewardClaimed = claimed,
                });
            }

            return result
                .GroupBy(trial => trial.Level)
                .Select(group => group.OrderByDescending(trial => trial.Progress).First())
                .OrderBy(trial => trial.Level)
                .ToList();
        }

        private static void SaveTrials(Player player, List<TrialState> trials)
        {
            var raw = string.Join(";", trials
                .Where(trial => trial.Level > 0 && trial.Required > 0 && !trial.RewardClaimed)
                .OrderBy(trial => trial.Level)
                .Select(trial => $"{trial.Level}|{trial.Kind}|{trial.Required}|{Math.Clamp(trial.Progress, 0, trial.Required)}|0"));

            if (raw.Length == 0)
                player.RemoveProperty(PropertyString.HardcoreCrawlerTrials);
            else
                player.SetProperty(PropertyString.HardcoreCrawlerTrials, raw);
        }
        private static void GrantGuideBook(Player player)
        {
            var book = WorldObjectFactory.CreateNewWorldObject(HardcodedWeenies.HardcoreCrawlerGuideBookWeenieClassId);
            if (book == null)
            {
                player.SendMessage("[Crawler] Failed to create the Hardcore Crawler guide book.", ChatMessageType.System);
                return;
            }

            book.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceHardcore);
            if (player.TryCreateInInventoryWithNetworking(book))
                player.SendMessage($"[Crawler] A guide book has been placed in your pack: {book.Name}.", ChatMessageType.Advancement);
            else
            {
                player.SendMessage($"[Crawler] Failed to place {book.Name} in your pack.", ChatMessageType.System);
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
                var boon = PickWeightedBoon(pool);
                choices.Add(boon);
                pool.Remove(boon);
            }
            return choices;
        }

        private static BoonDef PickWeightedBoon(List<BoonDef> pool)
        {
            var totalWeight = pool.Sum(b => GetRarityWeight(b.Rarity));
            var roll = ThreadSafeRandom.Next(1, totalWeight);
            var runningWeight = 0;

            foreach (var boon in pool)
            {
                runningWeight += GetRarityWeight(boon.Rarity);
                if (roll <= runningWeight)
                    return boon;
            }

            return pool[pool.Count - 1];
        }

        private static int GetRarityWeight(BoonRarity rarity)
        {
            switch (rarity)
            {
                case BoonRarity.Uncommon:
                    return 45;
                case BoonRarity.Rare:
                    return 18;
                case BoonRarity.Epic:
                    return 7;
                case BoonRarity.Legendary:
                    return 2;
                case BoonRarity.Mythical:
                    return 1;
                default:
                    return 100;
            }
        }
        private static void AwardMilestoneCache(Player player, int level)
        {
            var interval = Math.Max(0, DerpACEConfig.HardcoreCrawlerCacheMilestoneInterval);
            if (player == null || interval <= 0 || level < interval || level % interval != 0)
                return;

            var awarded = ParseLevelSet(player.GetProperty(PropertyString.HardcoreCrawlerMilestoneCaches));
            if (!awarded.Add(level))
                return;

            SaveLevelSet(player, PropertyString.HardcoreCrawlerMilestoneCaches, awarded);
            player.SendMessage($"[Crawler] Milestone reached: Level {level}. A Crawler Cache has been delivered.", ChatMessageType.Advancement);
            GrantMilestoneCache(player, level);
        }

        private static void GrantMilestoneCache(Player player, int level)
        {
            if (level % 25 == 0)
            {
                GrantRolledWeapon(player, "Mythic Crawler Cache", GetPreferredWeaponFamily(player));
                GrantRolledArmor(player, "Mythic Crawler Cache");
                GrantFixedItems(player, "Mythic Crawler Cache", 273, 2, 2643, 2, 2597, 2);
                return;
            }

            if (level % 10 == 0)
            {
                GrantRolledWeapon(player, "Crawler Armory Cache", GetPreferredWeaponFamily(player));
                GrantRolledArmor(player, "Crawler Armory Cache");
                return;
            }

            if (BestSkill(player, MagicGrowth) != null && ThreadSafeRandom.Next(0, 99) < 35)
                GrantRolledWeapon(player, "Crawler Arcane Cache", "caster");
            else if (IsUsable(player, Skill.MissileWeapons) && ThreadSafeRandom.Next(0, 99) < 35)
                GrantRolledWeapon(player, "Crawler Missile Cache", "missile");
            else if (BestSkill(player, MeleeGrowth) != null)
                GrantRolledWeapon(player, "Crawler Armory Cache", "melee");
            else
                GrantFixedItems(player, "Crawler Survival Cache", 273, 2, 2643, 1);
        }

        private static string GetPreferredWeaponFamily(Player player)
        {
            var melee = BestSkill(player, MeleeGrowth)?.Current ?? 0;
            var magic = BestSkill(player, MagicGrowth)?.Current ?? 0;
            var missile = player.GetCreatureSkill(Skill.MissileWeapons, false)?.Current ?? 0;

            if (magic >= melee && magic >= missile && magic > 0)
                return "caster";
            if (missile >= melee && missile > 0)
                return "missile";
            if (melee > 0)
                return "melee";
            return null;
        }

        public static bool HasBoon(Player player, string boonId)
        {
            return player != null
                && !string.IsNullOrWhiteSpace(boonId)
                && ParseChosen(player).Any(entry => string.Equals(entry.Id, boonId, StringComparison.OrdinalIgnoreCase));
        }

        public static void OnFoodConsumed(Player player, Food food, MotionCommand motionCommand)
        {
            if (!IsActive(player) || food == null || motionCommand != MotionCommand.Eat)
                return;

            AdvanceTrials(player, trial => trial.Kind == TrialKind.Rations);

            if (!HasBoon(player, "bottomless_engine"))
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            player.SetProperty(PropertyFloat.HardcoreCrawlerLastMealUnixTime, now);
            player.SetProperty(PropertyFloat.HardcoreCrawlerNextHungerPulse, now + 1200);
            player.ChangesDetected = true;
        }

        public static void Heartbeat(Player player, double currentUnixTime)
        {
            if (!IsActive(player) || !HasBoon(player, "bottomless_engine") || player.IsDead)
                return;

            var lastMeal = player.GetProperty(PropertyFloat.HardcoreCrawlerLastMealUnixTime) ?? 0;
            if (lastMeal <= 0)
            {
                player.SetProperty(PropertyFloat.HardcoreCrawlerLastMealUnixTime, currentUnixTime);
                player.SetProperty(PropertyFloat.HardcoreCrawlerNextHungerPulse, currentUnixTime + 1200);
                player.ChangesDetected = true;
                return;
            }

            if (currentUnixTime - lastMeal < 1200)
                return;

            var nextPulse = player.GetProperty(PropertyFloat.HardcoreCrawlerNextHungerPulse) ?? 0;
            if (nextPulse > currentUnixTime)
                return;

            var drain = Math.Max(1, (int)Math.Round(player.Stamina.MaxValue * 0.05));
            var drained = -player.UpdateVitalDelta(player.Stamina, -drain);
            player.SetProperty(PropertyFloat.HardcoreCrawlerNextHungerPulse, currentUnixTime + 60);
            player.ChangesDetected = true;

            if (drained > 0)
                player.SendMessage("[Crawler] Bottomless Engine growls. Eat something satiating or it keeps burning stamina.", ChatMessageType.Broadcast);
        }

        public static uint ModifyHealingKitAmount(Player healer, Player target, uint healAmount)
        {
            if (!IsActive(healer) || !HasBoon(healer, "field_surgeon_oath") || healAmount == 0)
                return healAmount;

            return (uint)Math.Max(1, Math.Round(healAmount * 1.35));
        }

        public static int ModifyIncomingSpellRestore(Player target, int boost)
        {
            if (!IsActive(target) || !HasBoon(target, "field_surgeon_oath") || boost <= 0)
                return boost;

            return Math.Max(1, (int)Math.Round(boost * 0.65));
        }

        private static void ApplyBrawnDebt(Player player)
        {
            AwardAttributeRanks(player, 5, PropertyAttribute.Strength, PropertyAttribute.Coordination);
            RemoveVitalRanks(player, 6, PropertyAttribute2nd.MaxMana);
            SendFlawBoonMessage(player, "Brawn Debt");
        }

        private static void ApplyGlassRitual(Player player)
        {
            AwardAttributeRanks(player, 5, PropertyAttribute.Focus, PropertyAttribute.Self);
            RemoveVitalRanks(player, 6, PropertyAttribute2nd.MaxHealth);
            SendFlawBoonMessage(player, "Glass Ritual");
        }

        private static void ApplyRunnersTax(Player player)
        {
            AwardAttributeRanks(player, 5, PropertyAttribute.Quickness);
            AwardSkills(player, Skill.Run, Skill.Jump);
            RemoveAttributeRanks(player, 4, PropertyAttribute.Strength);
            RemoveVitalRanks(player, 3, PropertyAttribute2nd.MaxHealth);
            SendFlawBoonMessage(player, "Runner's Tax");
        }

        private static void ApplyBloodForSparks(Player player)
        {
            AwardVitalRanks(player, 8, PropertyAttribute2nd.MaxMana);
            RemoveVitalRanks(player, 5, PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina);
            SendFlawBoonMessage(player, "Blood for Sparks");
        }

        private static void ApplyIronStomach(Player player)
        {
            AwardVitalRanks(player, 6, PropertyAttribute2nd.MaxHealth, PropertyAttribute2nd.MaxStamina);
            RemoveAttributeRanks(player, 4, PropertyAttribute.Focus, PropertyAttribute.Self);
            SendFlawBoonMessage(player, "Iron Stomach");
        }

        private static void ApplyBottomlessEngine(Player player)
        {
            AwardVitalRanks(player, 10, PropertyAttribute2nd.MaxStamina);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            player.SetProperty(PropertyFloat.HardcoreCrawlerLastMealUnixTime, now);
            player.SetProperty(PropertyFloat.HardcoreCrawlerNextHungerPulse, now + 1200);
            SendFlawBoonMessage(player, "Bottomless Engine");
        }

        private static void ApplyFieldSurgeonOath(Player player)
        {
            AwardSkills(player, Skill.Healing);
            AwardAttributeRanks(player, 3, PropertyAttribute.Focus, PropertyAttribute.Coordination);
            RemoveVitalRanks(player, 4, PropertyAttribute2nd.MaxMana);
            SendFlawBoonMessage(player, "Field Surgeon Oath");
        }

        private static void SendFlawBoonMessage(Player player, string boonName)
        {
            player.SendMessage($"[Crawler] {boonName} takes its price. Power gained, weakness accepted.", ChatMessageType.Advancement);
        }

        private static void RemoveAttributeRanks(Player player, int ranks, params PropertyAttribute[] attributes)
        {
            foreach (var attributeType in attributes)
            {
                if (!player.Attributes.TryGetValue(attributeType, out var attribute))
                    continue;

                var xpTable = DatManager.PortalDat.XpTable.AttributeXpList;
                var targetRank = Math.Max(0, (int)attribute.Ranks - ranks);
                attribute.ExperienceSpent = xpTable[targetRank];
                attribute.Ranks = (ushort)Player.CalcAttributeRank(attribute.ExperienceSpent);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, attribute));
            }

            player.SetMaxVitals();
            player.ChangesDetected = true;
        }

        private static void RemoveVitalRanks(Player player, int ranks, params PropertyAttribute2nd[] vitals)
        {
            foreach (var vitalType in vitals)
            {
                if (!player.Vitals.TryGetValue(vitalType, out var vital))
                    continue;

                var xpTable = DatManager.PortalDat.XpTable.VitalXpList;
                var targetRank = Math.Max(0, (int)vital.Ranks - ranks);
                vital.ExperienceSpent = xpTable[targetRank];
                vital.Ranks = (ushort)Player.CalcVitalRank(vital.ExperienceSpent);
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, vital));
            }

            player.SetMaxVitals();
            player.ChangesDetected = true;
        }

        private static HashSet<int> ParseLevelSet(string raw)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out var level) && level > 0)
                    result.Add(level);
            }
            return result;
        }

        private static void SaveLevelSet(Player player, PropertyString property, HashSet<int> levels)
        {
            var raw = string.Join(";", levels.Where(level => level > 0).OrderBy(level => level));
            if (raw.Length == 0)
                player.RemoveProperty(property);
            else
                player.SetProperty(property, raw);
        }
        private static void AddHardcoreLife(Player player, int cap)
        {
            var lives = Math.Max(0, player.GetProperty(PropertyInt.HardcoreLives) ?? 0);
            if (lives >= cap)
            {
                player.SendMessage($"[Crawler] Your borrowed pulse is already at its limit ({cap}).", ChatMessageType.System);
                return;
            }

            player.SetProperty(PropertyInt.HardcoreLives, lives + 1);
            player.SendMessage($"[Crawler] Something ugly decides you are not finished yet. Hardcore lives: {lives + 1}/{cap}.", ChatMessageType.Advancement);
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
                player.SendMessage($"[Crawler] {source} failed to produce an item.", ChatMessageType.System);
                return;
            }

            item.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceHardcore);
            if (!player.TryCreateInInventoryWithNetworking(item))
            {
                player.SendMessage($"[Crawler] {source} could not fit {item.Name} in your pack.", ChatMessageType.System);
                item.Destroy();
                return;
            }

            player.SendMessage($"[Crawler] {source} grants {item.Name}.", ChatMessageType.Advancement);
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
                AwardRelatedStatProgress(player, skill, 1);
            }
        }

        private static BoonDef FindBoon(string id)
        {
            return Boons.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static (int Level, List<string> Ids) ParsePending(Player player)
        {
            return ParsePendingQueue(player).OrderBy(entry => entry.Level).FirstOrDefault();
        }

        private static List<(int Level, List<string> Ids)> ParsePendingQueue(Player player)
        {
            var result = new List<(int Level, List<string> Ids)>();
            var raw = player.GetProperty(PropertyString.HardcoreCrawlerPendingChoices) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split('|');
                if (parts.Length != 2 || !int.TryParse(parts[0], out var level) || level <= 1)
                    continue;

                if (result.Any(entry => entry.Level == level) || HasBoonForLevel(player, level))
                    continue;

                var ids = parts[1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0 && FindBoon(x) != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ids.Count > 0)
                    result.Add((level, ids));
            }

            return result.OrderBy(entry => entry.Level).ToList();
        }

        private static void SavePendingQueue(Player player, List<(int Level, List<string> Ids)> pending)
        {
            var raw = string.Join(";", pending
                .Where(entry => entry.Level > 1 && !HasBoonForLevel(player, entry.Level) && entry.Ids != null && entry.Ids.Count > 0)
                .OrderBy(entry => entry.Level)
                .Select(entry => $"{entry.Level}|{string.Join(',', entry.Ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))}"));

            if (raw.Length == 0)
                player.RemoveProperty(PropertyString.HardcoreCrawlerPendingChoices);
            else
                player.SetProperty(PropertyString.HardcoreCrawlerPendingChoices, raw);
        }

        private static IEnumerable<(int Level, string Id)> ParseChosen(Player player)
        {
            var raw = player.GetProperty(PropertyString.HardcoreCrawlerBoons) ?? string.Empty;
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
            player.SetProperty(PropertyString.HardcoreCrawlerBoons, string.Join(";", entries.OrderBy(entry => entry.Level).Select(entry => $"{entry.Level}:{entry.Id}")));
        }
    }
}



