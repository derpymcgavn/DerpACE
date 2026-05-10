using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ACE.Common;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE Ironman mode (port of aquafir/ACE.BaseMod/Samples/Ironman, hardcoded).
    ///
    /// Activated by the player typing /ironman on then /ironman confirm. The toggle is
    /// IRREVERSIBLE — once committed, the character carries IsIronman = true for life.
    ///
    /// At commit time the character is rerolled (attributes, skills, inventory, spells)
    /// and given a starter loadout based on the rolled primary skill, then flagged as
    /// hardcore (final-death-deletes). Subsequent gameplay restrictions:
    ///   * Cannot recruit / be recruited into a fellowship containing non-Ironmen
    ///   * Cannot pledge to / be pledged to by a non-Ironman
    ///   * Cannot be enchanted by a non-Ironman caster
    ///   * Cannot wield items not flagged IsIronmanItem
    ///   * Items that enter their inventory are auto-tagged IsIronmanItem
    ///
    /// Note: appearance/heritage rerolling from the source mod is intentionally NOT
    /// ported — that path mutates Biota directly and is fragile across DerpACE forks.
    /// </summary>
    public static class IronmanFactory
    {
        // ---------- Hardcoded settings (mirrors aquafir's Settings.cs) ----------

        private static readonly Skill[] PrimarySkillPool =
        {
            Skill.TwoHandedCombat,
            Skill.MissileWeapons,
            Skill.WarMagic,
            Skill.VoidMagic,
            Skill.LightWeapons,
            Skill.HeavyWeapons,
            Skill.FinesseWeapons,
        };

        private static readonly HashSet<Skill> AugmentSpecializations = new HashSet<Skill>
        {
            Skill.Salvaging,
            Skill.ArmorTinkering,
            Skill.ItemTinkering,
            Skill.MagicItemTinkering,
            Skill.WeaponTinkering,
        };

        private static readonly Skill[] SecondarySkillPool =
        {
            Skill.Alchemy,
            Skill.ArmorTinkering,
            Skill.AssessCreature,
            Skill.AssessPerson,
            Skill.Cooking,
            Skill.CreatureEnchantment,
            Skill.Deception,
            Skill.DirtyFighting,
            Skill.DualWield,
            Skill.Fletching,
            Skill.Healing,
            Skill.ItemEnchantment,
            Skill.ItemTinkering,
            Skill.Leadership,
            Skill.LifeMagic,
            Skill.Lockpick,
            Skill.MagicItemTinkering,
            Skill.ManaConversion,
            Skill.MeleeDefense,
            Skill.MissileDefense,
            Skill.Recklessness,
            Skill.Shield,
            Skill.SneakAttack,
            Skill.Summoning,
            Skill.WeaponTinkering,
        };

        private static readonly SpellId[] DefaultSpells =
        {
            // Creature
            SpellId.FocusSelf1,
            // Life
            SpellId.ArmorOther1,
            SpellId.ArmorSelf1,
            SpellId.HealOther1,
            SpellId.HealSelf1,
            SpellId.ImperilOther1,
            // Item
            SpellId.BloodDrinkerSelf1,
            SpellId.SwiftKillerSelf1,
            SpellId.BludgeonBane1,
            SpellId.Impenetrability1,
            // War
            SpellId.FlameBolt1,
            SpellId.FrostBolt1,
            SpellId.ShockWave1,
            SpellId.ForceBolt1,
        };

        // wcid + optional " amount" stack-size (matches aquafir's "20631 100" syntax)
        private static readonly Dictionary<Skill, string[]> SkillStarterItems = new Dictionary<Skill, string[]>
        {
            [Skill.WarMagic]       = new[] { "12748", "20631 100", "691 10" },
            [Skill.VoidMagic]      = new[] { "12748", "20631 100", "691 10" },
            [Skill.LightWeapons]   = new[] { "30857" },               // wood training short sword
            [Skill.HeavyWeapons]   = new[] { "30857" },
            [Skill.FinesseWeapons] = new[] { "30857" },
            [Skill.TwoHandedCombat] = new[] { "30857" },
            [Skill.MissileWeapons] = new[] { "300", "302 50" },        // training bow + arrows
        };

        // ---------- Public entry point ----------

        public static void InitializeIronman(Player player)
        {
            if (player == null) return;

            if (player.GetProperty(PropertyBool.IsIronman) == true)
            {
                player.SendMessage("You are already an Ironman.");
                return;
            }

            // Reroll attributes and skills first; RollSkills returns the rolled primary skill
            RollAttributes(player);
            var rolledPrimary = RollSkills(player);

            // Wipe inventory (everything in side packs + main pack + equipped)
            WipeInventory(player);

            // Wipe known spells, then learn the default low-level set on a short delay
            // so the spellbook update lands after the inventory wipe networking settles.
            // We also apply at-creation / level-0 skill grants here so the client is fully
            // settled after the attribute/skill reset storm before we push new skill updates.
            WipeKnownSpells(player);

            var chain = new ActionChain();
            chain.AddDelaySeconds(2.0);
            chain.AddAction(player, () =>
            {
                // Apply at-creation skills and any milestones already met (real-time, no relog).
                ApplyIronmanPlanForLevel(player, player.Level ?? 1, announceGrants: false);

                foreach (var spellId in DefaultSpells)
                    player.LearnSpellWithNetworking((uint)spellId, false);

                player.SendMessage("You have been taught the basic spells available to all Ironmen.");
            });
            chain.EnqueueChain();

            // Grant Ironman-specific starter items, then normal creation gear based on trained skills
            GiveStarterItems(player, rolledPrimary);
            GiveStarterGear(player);

            // Hardcore + flag + visual
            ApplyHardcore(player);
            ApplyIronmanFlag(player);

            // Tag every possession we now have so wield/use checks pass
            TagAllPossessions(player);

            // Welcome message + flair
            player.SendMessage(DerpACEConfig.IronmanWelcomeMessage);
            for (var i = 0; i < 6; i++)
                player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
        }

        // ---------- Attribute reroll ----------

        private static void RollAttributes(Player player)
        {
            // Pick one primary attribute to set to 100; others go to 46 (matches mod)
            var primary = (PropertyAttribute)ThreadSafeRandom.Next(1, 6);
            foreach (PropertyAttribute attr in System.Enum.GetValues(typeof(PropertyAttribute)))
            {
                if (attr == PropertyAttribute.Undef) continue;

                var pAttr = player.Attributes[attr];
                pAttr.StartingValue = attr == primary ? 100u : 46u;

                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, pAttr));
            }
        }

        // ---------- Skill reroll ----------

        // Milestone levels at which the Ironman plan grants new skills
        private static readonly int[] SkillMilestones = { 5, 12, 20, 32, 50, 70, 100, 130, 150, 175, 200, 225, 250, 275 };

        /// <summary>
        /// Resets all skills, builds a level-milestone plan, and immediately applies any skills
        /// due at the character's current level.  Returns the rolled primary skill.
        /// </summary>
        private static Skill RollSkills(Player player)
        {
            // Reset every skill (refunds credits + xp for trained/spec'd skills)
            foreach (Skill skill in System.Enum.GetValues(typeof(Skill)))
            {
                if (skill == Skill.None) continue;
                player.ResetSkill(skill, true);
            }

            var plan = new Dictionary<Skill, int>();

            // Pick + freely train + specialize primary weapon/magic skill (0 credit cost)
            var primary = PrimarySkillPool[ThreadSafeRandom.Next(0, PrimarySkillPool.Length - 1)];
            player.TrainSkill(primary, 0);
            player.SpecializeSkill(primary, 0, false);
            plan[primary] = 0; // 0 = already applied
            // Push primary skill update to client immediately; always show 0 credits for Ironman
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(primary)),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, 0));

            // Secondary: ManaConversion for magic primaries, else random non-aug skill
            var pool = new List<Skill>(SecondarySkillPool);
            pool.Remove(primary);

            bool isMagicPrimary = primary == Skill.WarMagic || primary == Skill.VoidMagic || primary == Skill.LifeMagic;
            var secondary = isMagicPrimary
                ? Skill.ManaConversion
                : pool.Where(x => !AugmentSpecializations.Contains(x))
                      .OrderBy(_ => ThreadSafeRandom.Next(0, int.MaxValue - 1))
                      .FirstOrDefault();

            if (secondary != Skill.None)
            {
                player.TrainSkill(secondary, 0);
                if (!isMagicPrimary)
                    player.SpecializeSkill(secondary, 0, false);
                plan[secondary] = 0;
                pool.Remove(secondary);
                // Push secondary skill update to client immediately; always show 0 credits for Ironman
                player.Session.Network.EnqueueSend(
                    new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(secondary)),
                    new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, 0));
            }

            // Fisher-Yates shuffle the remaining pool
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = ThreadSafeRandom.Next(0, i);
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }

            // Assign: 2-4 extra at-creation skills (-1), one per milestone, rest not-obtainable (-2)
            int atCreationCount = ThreadSafeRandom.Next(2, 4);
            int idx = 0;
            for (int i = 0; i < atCreationCount && idx < pool.Count; i++, idx++)
                plan[pool[idx]] = -1;

            for (int m = 0; m < SkillMilestones.Length && idx < pool.Count; m++, idx++)
                plan[pool[idx]] = SkillMilestones[m];

            for (; idx < pool.Count; idx++)
                plan[pool[idx]] = -2;

            // Serialize and store the plan — at-creation grants will be applied by the delayed chain.
            player.SetProperty(PropertyString.IronmanPlan, string.Join(";", plan.Select(kv => $"{kv.Key}:{kv.Value}")));

            return primary;
        }

        /// <summary>
        /// Reads the stored Ironman plan and trains any skills whose milestone level is
        /// at or below <paramref name="currentLevel"/>.  Marks applied entries as 0.
        /// </summary>
        private static void ApplyIronmanPlanForLevel(Player player, int currentLevel, bool announceGrants = true)
        {
            var planStr = player.GetProperty(PropertyString.IronmanPlan);
            if (string.IsNullOrEmpty(planStr)) return;

            var plan = new Dictionary<Skill, int>();
            foreach (var entry in planStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length == 2 &&
                    System.Enum.TryParse<Skill>(parts[0], out var sk) &&
                    int.TryParse(parts[1], out var lvl))
                {
                    plan[sk] = lvl;
                }
            }

            bool anyChanged = false;
            var messages = new List<string>();

            foreach (var skill in plan.Keys.ToList())
            {
                int milestone = plan[skill];
                if (milestone == 0 || milestone == -2) continue;         // already done / never
                if (milestone > 0 && milestone > currentLevel) continue; // not yet

                // milestone == -1 (at creation) or milestone > 0 && <= currentLevel: grant it
                plan[skill] = 0;
                anyChanged = true;

                var cs = player.GetCreatureSkill(skill);
                if (cs == null || cs.AdvancementClass >= SkillAdvancementClass.Trained)
                    continue; // already trained somehow, just mark done

                player.TrainSkill(skill, 0); // free grant — no credit cost

                // Push the updated skill and credit count to the client immediately
                // so the skill panel reflects the change without a relog.
                // Always show 0 credits for Ironman — skills are granted by the plan, not purchased.
                player.Session.Network.EnqueueSend(
                    new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(skill)),
                    new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, 0));

                if (announceGrants && milestone > 0)
                    messages.Add($"[Ironman] Your plan unlocks: {skill.ToSentence()} (Level {milestone} unlock)");
            }

            if (anyChanged)
            {
                player.SetProperty(PropertyString.IronmanPlan, string.Join(";", plan.Select(kv => $"{kv.Key}:{kv.Value}")));
                foreach (var msg in messages)
                    player.SendMessage(msg, ChatMessageType.Advancement);
            }
        }

        /// <summary>
        /// Called from Player_Xp.CheckForLevelup on every level-up.
        /// Trains any plan skills whose milestone is now met.
        /// </summary>
        public static void CheckIronmanLevelGrants(Player player)
        {
            if (player.GetProperty(PropertyBool.IsIronman) != true) return;
            ApplyIronmanPlanForLevel(player, player.Level ?? 1, announceGrants: true);
        }

        // ---------- Inventory wipe ----------

        private static void WipeInventory(Player player)
        {
            // Snapshot then destroy every wielded + carried item.
            var owned = player.GetAllPossessions().ToList();
            foreach (var item in owned)
            {
                try
                {
                    if (item.WielderId != null)
                    {
                        // Equipped — dequip first via direct unwield path
                        player.HandleActionPutItemInContainer(item.Guid.Full, player.Guid.Full);
                    }

                    if (player.TryRemoveFromInventoryWithNetworking(item.Guid, out var removed, Player.RemoveFromInventoryAction.SpendItem))
                        removed?.Destroy();
                }
                catch
                {
                    // Swallow individual item failures so a single broken item doesn't abort the whole wipe.
                }
            }
        }

        // ---------- Spell wipe ----------

        private static void WipeKnownSpells(Player player)
        {
            var known = player.Biota.GetKnownSpellsIds(player.BiotaDatabaseLock).ToList();
            foreach (var spellId in known)
            {
                try { player.HandleActionMagicRemoveSpellId((uint)spellId); }
                catch { /* ignore */ }
            }
        }

        // ---------- Starter items ----------

        private static void GiveStarterItems(Player player, Skill primary)
        {
            if (!SkillStarterItems.TryGetValue(primary, out var items)) return;

            foreach (var entry in items)
            {
                var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (!uint.TryParse(parts[0], out var wcid)) continue;
                int amount = 1;
                if (parts.Length > 1) int.TryParse(parts[1], out amount);

                try
                {
                    var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
                    if (wo == null) continue;
                    if (amount > 1 && wo.MaxStackSize.HasValue && wo.MaxStackSize > 1)
                        wo.SetStackSize(amount);

                    // Pre-tag so it survives the IsIronman-flag check at the end of init
                    wo.SetProperty(PropertyBool.IsIronmanItem, true);
                    player.TryCreateInInventoryWithNetworking(wo);
                }
                catch
                {
                    // Skip bad wcids silently
                }
            }
        }

        /// <summary>
        /// Grants the standard new-character starter gear based on the player's current trained/specialized skills,
        /// mirroring what PlayerFactory does during normal character creation.
        /// Items go through TryCreateInInventoryWithNetworking so the existing hook auto-tags them IsIronmanItem.
        /// </summary>
        private static void GiveStarterGear(Player player)
        {
            var starterGearConfig = StarterGearFactory.GetStarterGearConfiguration();
            if (starterGearConfig == null) return;

            var isDualWield = player.Skills.TryGetValue(Skill.DualWield, out var dwSkill)
                && dwSkill.AdvancementClass > SkillAdvancementClass.Untrained;

            var grantedWeenies = new HashSet<uint>();

            foreach (var skillGear in starterGearConfig.Skills)
            {
                if (!player.Skills.TryGetValue((Skill)skillGear.SkillId, out var charSkill)) continue;
                if (charSkill.AdvancementClass < SkillAdvancementClass.Trained) continue;

                foreach (var item in skillGear.Gear)
                {
                    if (grantedWeenies.Contains(item.WeenieId))
                    {
                        // Stack onto existing item if stackable
                        var existing = player.Inventory.Values.FirstOrDefault(i => i.WeenieClassId == item.WeenieId);
                        if (existing != null && (existing.MaxStackSize ?? 1) > 1)
                            existing.SetStackSize(existing.StackSize + item.StackSize);
                        continue;
                    }

                    var wo = WorldObjectFactory.CreateNewWorldObject(item.WeenieId);
                    if (wo == null) continue;

                    if (wo.StackSize.HasValue && wo.MaxStackSize.HasValue)
                        wo.SetStackSize(Math.Min(item.StackSize, wo.MaxStackSize.Value));

                    // Pre-tag so wield check passes immediately (TryCreateInInventoryWithNetworking also tags)
                    wo.SetProperty(PropertyBool.IsIronmanItem, true);

                    if (player.TryCreateInInventoryWithNetworking(wo))
                        grantedWeenies.Add(item.WeenieId);

                    // Dual-wield bonus: give a second copy of melee weapons
                    if (isDualWield && wo.WeenieType == WeenieType.MeleeWeapon)
                    {
                        var dw2 = WorldObjectFactory.CreateNewWorldObject(item.WeenieId);
                        if (dw2 != null)
                        {
                            dw2.SetProperty(PropertyBool.IsIronmanItem, true);
                            player.TryCreateInInventoryWithNetworking(dw2);
                        }
                    }
                }
            }
        }

        // ---------- Hardcore + flag ----------

        private static void ApplyHardcore(Player player)
        {
            player.SetProperty(PropertyInt.HardcoreLives, DerpACEConfig.IronmanHardcoreStartingLives);
            player.SetProperty(PropertyBool.IsHardcore, true);
            player.SetModeTitle("HARDCORE");
            player.SendMessage($"You begin with {DerpACEConfig.IronmanHardcoreStartingLives} hardcore life/lives. Final death is permanent.");
        }

        private static void ApplyIronmanFlag(Player player)
        {
            player.SetProperty(PropertyBool.IsIronman, true);
            player.RadarColor = RadarColor.Sentinel;
            player.SetModeTitle("IRONMAN");
            player.QuestManager.Stamp("IronmanChallenge");

            // Append " - IM" suffix to the character name if not already present
            const string imSuffix = " - IM";
            if (!player.Name.EndsWith(imSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var newName = player.Name + imSuffix;
                player.Character.Name = newName;
                player.CharacterChangesDetected = true;
                player.Name = newName;
                player.SavePlayerToDatabase();
            }
        }

        // ---------- Item tagging ----------

        public static void TagAllPossessions(Player player)
        {
            foreach (var item in player.GetAllPossessions())
            {
                if (item.GetProperty(PropertyBool.IsIronmanItem) != true)
                    item.SetProperty(PropertyBool.IsIronmanItem, true);
            }
        }
    }
}
