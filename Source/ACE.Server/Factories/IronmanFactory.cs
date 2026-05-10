using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ACE.Common;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameEvent.Events;
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

        private readonly struct IronmanCharacterSizeOption
        {
            public IronmanCharacterSizeOption(string label, float scaleMultiplier)
            {
                Label = label;
                ScaleMultiplier = scaleMultiplier;
            }

            public string Label { get; }
            public float ScaleMultiplier { get; }
        }

        // Mirrors the webpage race list.
        private static readonly HeritageGroup[] IronmanRacePool =
        {
            HeritageGroup.Aluvian,
            HeritageGroup.Gharundim,
            HeritageGroup.Sho,
            HeritageGroup.Viamontian,
            HeritageGroup.Shadowbound, // Umbraen
            HeritageGroup.Penumbraen,  // Panumoraen
            HeritageGroup.Gearknight,
            HeritageGroup.Undead,
            HeritageGroup.Empyrean,
            HeritageGroup.Tumerok,     // Aun Tumerok
            HeritageGroup.Lugian,
        };

        // Mirrors the webpage character size list.
        private static readonly IronmanCharacterSizeOption[] IronmanCharacterSizes =
        {
            new IronmanCharacterSizeOption("Extended Growth", 1.08f),
            new IronmanCharacterSizeOption("Growth", 1.04f),
            new IronmanCharacterSizeOption("Average", 1.00f),
            new IronmanCharacterSizeOption("Degrowth", 0.96f),
            new IronmanCharacterSizeOption("Extended Degrowth", 0.92f),
        };

        private readonly struct IronmanWeaponOption
        {
            public IronmanWeaponOption(Skill skill, int trainCost, bool isMagic)
            {
                Skill = skill;
                TrainCost = trainCost;
                IsMagic = isMagic;
            }

            public Skill Skill { get; }
            public int TrainCost { get; }
            public bool IsMagic { get; }
        }

        private readonly struct IronmanPrimarySkillOption
        {
            public IronmanPrimarySkillOption(Skill skill, int trainCost, int specCost)
            {
                Skill = skill;
                TrainCost = trainCost;
                SpecCost = specCost;
            }

            public Skill Skill { get; }
            public int TrainCost { get; }
            public int SpecCost { get; }
        }

        // Mirrors website weapon array ordering and train-cost values.
        private static readonly IronmanWeaponOption[] WeaponSkillPool =
        {
            new IronmanWeaponOption(Skill.FinesseWeapons, 8, false),
            new IronmanWeaponOption(Skill.LightWeapons, 8, false),
            new IronmanWeaponOption(Skill.HeavyWeapons, 12, false),
            new IronmanWeaponOption(Skill.TwoHandedCombat, 16, false),
            new IronmanWeaponOption(Skill.MissileWeapons, 12, false),
            new IronmanWeaponOption(Skill.VoidMagic, 28, true),
            new IronmanWeaponOption(Skill.WarMagic, 28, true),
            new IronmanWeaponOption(Skill.LifeMagic, 20, true),
        };

        // Mirrors website primary array ordering and train/spec costs.
        private static readonly IronmanPrimarySkillOption[] PrimarySkillPool =
        {
            new IronmanPrimarySkillOption(Skill.ArmorTinkering, 4, 0),
            new IronmanPrimarySkillOption(Skill.AssessCreature, 4, 2),
            new IronmanPrimarySkillOption(Skill.AssessPerson, 2, 2),
            new IronmanPrimarySkillOption(Skill.Deception, 4, 2),
            new IronmanPrimarySkillOption(Skill.DualWield, 2, 2),
            new IronmanPrimarySkillOption(Skill.ItemTinkering, 2, 0),
            new IronmanPrimarySkillOption(Skill.Leadership, 4, 2),
            new IronmanPrimarySkillOption(Skill.MagicItemTinkering, 4, 0),
            new IronmanPrimarySkillOption(Skill.MeleeDefense, 10, 10),
            new IronmanPrimarySkillOption(Skill.MissileDefense, 6, 4),
            new IronmanPrimarySkillOption(Skill.Shield, 2, 2),
            new IronmanPrimarySkillOption(Skill.WeaponTinkering, 4, 0),
            new IronmanPrimarySkillOption(Skill.Alchemy, 6, 6),
            new IronmanPrimarySkillOption(Skill.Cooking, 4, 4),
            new IronmanPrimarySkillOption(Skill.CreatureEnchantment, 8, 8),
            new IronmanPrimarySkillOption(Skill.DirtyFighting, 2, 2),
            new IronmanPrimarySkillOption(Skill.Fletching, 4, 4),
            new IronmanPrimarySkillOption(Skill.Healing, 6, 4),
            new IronmanPrimarySkillOption(Skill.ItemEnchantment, 8, 8),
            new IronmanPrimarySkillOption(Skill.LifeMagic, 12, 8),
            new IronmanPrimarySkillOption(Skill.Lockpick, 6, 4),
            new IronmanPrimarySkillOption(Skill.ManaConversion, 6, 6),
            new IronmanPrimarySkillOption(Skill.SneakAttack, 4, 2),
            new IronmanPrimarySkillOption(Skill.Summoning, 8, 4),

            // Website "pretrained" entries
            new IronmanPrimarySkillOption(Skill.ArcaneLore, 0, 2),
            new IronmanPrimarySkillOption(Skill.Jump, 0, 4),
            new IronmanPrimarySkillOption(Skill.Loyalty, 0, 2),
            new IronmanPrimarySkillOption(Skill.MagicDefense, 0, 12),
            new IronmanPrimarySkillOption(Skill.Run, 0, 4),
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
            [Skill.LightWeapons]   = new[] { "12739" },               // Training Dirk
            [Skill.HeavyWeapons]   = new[] { "12740" },               // Training Battle Axe
            [Skill.FinesseWeapons] = new[] { "12739" },               // Training Dirk
            [Skill.TwoHandedCombat] = new[] { "41512" },              // Training Spadone
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

            var rolledPrimary = Skill.None;

            // Cinematic flow: play EnterPortal emote first, then perform each verbose step with 1-second spacing.
            player.SendMotionAsCommands(MotionCommand.EnterPortal, MotionStance.NonCombat);

            var chain = new ActionChain();
            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 1/6: rerolling heritage, appearance, attributes, and skills...");
                RollHeritageAndAppearance(player);
                RollAttributes(player);
                rolledPrimary = RollSkills(player);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 2/6: wiping inventory...");
                WipeInventory(player);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 3/6: wiping known spells...");
                WipeKnownSpells(player);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 4/6: applying ironman skill milestones...");

                // Apply at-creation skills and any milestones already met (real-time, no relog).
                ApplyIronmanPlanForLevel(player, player.Level ?? 1, announceGrants: false);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 5/6: teaching starter spells...");

                foreach (var spellId in DefaultSpells)
                    player.LearnSpellWithNetworking((uint)spellId, false);

                player.SendMessage("You have been taught the basic spells available to all Ironmen.");
            });

            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.ExitPortal, MotionStance.NonCombat));

            chain.AddDelaySeconds(3.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Ironman step 6/6: granting starter gear...");
                GiveStarterItems(player, rolledPrimary);
                GiveStarterGear(player);

                // Final pass after all conversion actions so every remaining item is correctly tagged.
                TagAllPossessions(player);

                player.SendMessage(DerpACEConfig.IronmanWelcomeMessage);
                for (var i = 0; i < 6; i++)
                    player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
            });

            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.Cheer, MotionStance.NonCombat));
            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.Wave, MotionStance.NonCombat));

            chain.AddDelaySeconds(2.0);
            chain.AddAction(player, () =>
            {
                const string relogMsg = "finalizing ironman mode - Relog!";
                player.Session.Terminate(SessionTerminationReason.ForcedLogOffRequested, new GameMessageBootAccount($" - {relogMsg}"));
            });
            chain.EnqueueChain();

            // Hardcore + flag + visual
            ApplyHardcore(player);
            ApplyIronmanFlag(player);
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

        // Milestone levels mirror the website's level array.
        private static readonly int[] SkillMilestones =
        {
            2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 23, 26, 29, 32, 35,
            40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 105, 110, 115,
            120, 125, 130, 140, 150, 160, 180, 200, 225, 250, 275,
        };

        // Extra hardcore life milestones between levels 1-275.
        private static readonly int[] HardcoreLifeMilestones = { 75, 150, 225 };
        private const int IronmanMaxHardcoreLives = 3;

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

            // Roll the weapon skill from website-equivalent list, then freely train+spec it.
            var rolledWeapon = WeaponSkillPool[ThreadSafeRandom.Next(0, WeaponSkillPool.Length - 1)];
            player.TrainSkill(rolledWeapon.Skill, 0);
            player.SpecializeSkill(rolledWeapon.Skill, 0, false);
            plan[rolledWeapon.Skill] = 0;
            SendIronmanSkillUpdate(player, rolledWeapon.Skill);

            // Build mutable primary list from website-equivalent list.
            var primaryPool = new List<IronmanPrimarySkillOption>(PrimarySkillPool);

            // Magic primaries auto-train Mana Conversion (handled by prepending below).
            if (rolledWeapon.IsMagic)
                primaryPool.RemoveAll(x => x.Skill == Skill.ManaConversion);

            // If weapon is Life Magic, remove Life Magic from the primary pool.
            if (rolledWeapon.Skill == Skill.LifeMagic)
                primaryPool.RemoveAll(x => x.Skill == Skill.LifeMagic);

            // Shuffle until first option has a non-zero specialization cost,
            // mirroring the website's while(primary[0][2] == 0) guard.
            do
            {
                Shuffle(primaryPool);
            }
            while (primaryPool.Count > 0 && primaryPool[0].SpecCost == 0);

            // For magic weapon builds, force Mana Conversion to the front (trained only).
            if (rolledWeapon.IsMagic)
                primaryPool.Insert(0, new IronmanPrimarySkillOption(Skill.ManaConversion, 6, 6));

            // Apply first primary option immediately:
            // - magic builds: trained only (website treats Mana Conversion as auto-trained)
            // - non-magic builds: train + spec first rolled primary option
            if (primaryPool.Count > 0)
            {
                var firstPrimary = primaryPool[0];
                if (!plan.ContainsKey(firstPrimary.Skill))
                {
                    if (player.GetCreatureSkill(firstPrimary.Skill)?.AdvancementClass < SkillAdvancementClass.Trained)
                        player.TrainSkill(firstPrimary.Skill, 0);

                    if (!rolledWeapon.IsMagic && firstPrimary.SpecCost > 0)
                        player.SpecializeSkill(firstPrimary.Skill, 0, false);

                    plan[firstPrimary.Skill] = 0;
                    SendIronmanSkillUpdate(player, firstPrimary.Skill);
                }
            }

            // Remaining primary options unlock by milestone levels.
            int milestoneIndex = 0;
            for (int i = 1; i < primaryPool.Count; i++)
            {
                var skill = primaryPool[i].Skill;
                if (plan.ContainsKey(skill))
                    continue;

                if (milestoneIndex < SkillMilestones.Length)
                    plan[skill] = SkillMilestones[milestoneIndex++];
                else
                    plan[skill] = -2;
            }

            // Serialize and store the plan — at-creation grants will be applied by the delayed chain.
            player.SetProperty(PropertyString.IronmanPlan, string.Join(";", plan.Select(kv => $"{kv.Key}:{kv.Value}")));

            return rolledWeapon.Skill;
        }

        private static void RollHeritageAndAppearance(Player player)
        {
            var raceRoll = IronmanRacePool[ThreadSafeRandom.Next(0, IronmanRacePool.Length - 1)];
            var charSize = IronmanCharacterSizes[ThreadSafeRandom.Next(0, IronmanCharacterSizes.Length - 1)];
            var appearanceRoll = ThreadSafeRandom.Next(1, 15);

            var heritageGroup = DatManager.PortalDat.CharGen.HeritageGroups[(uint)raceRoll];
            if (heritageGroup == null || heritageGroup.Genders == null || heritageGroup.Genders.Count == 0)
                return;

            var genderKeys = heritageGroup.Genders.Keys.ToList();
            var genderKey = genderKeys[ThreadSafeRandom.Next(0, genderKeys.Count - 1)];
            var sex = heritageGroup.Genders[(int)genderKey];

            // Build appearance indices safely from DAT lists.
            var hairStyleIndex = PickAppearanceIndex(sex.HairStyleList?.Count ?? 0, appearanceRoll);
            var hairColorIndex = PickAppearanceIndex(sex.HairColorList?.Count ?? 0, appearanceRoll);
            var eyeIndex = PickAppearanceIndex(sex.EyeStripList?.Count ?? 0, appearanceRoll);
            var eyeColorIndex = PickAppearanceIndex(sex.EyeColorList?.Count ?? 0, appearanceRoll);
            var noseIndex = PickAppearanceIndex(sex.NoseStripList?.Count ?? 0, appearanceRoll);
            var mouthIndex = PickAppearanceIndex(sex.MouthStripList?.Count ?? 0, appearanceRoll);

            var skinHue = (float)ThreadSafeRandom.Next(0.0f, 1.0f);
            var hairHue = (float)ThreadSafeRandom.Next(0.0f, 1.0f);

            player.SetProperty(PropertyInt.HeritageGroup, (int)raceRoll);
            player.SetProperty(PropertyString.HeritageGroup, heritageGroup.Name);
            player.SetProperty(PropertyInt.Gender, (int)genderKey);
            player.SetProperty(PropertyString.Sex, (int)genderKey == 1 ? "Male" : "Female");

            player.SetProperty(PropertyDataId.MotionTable, sex.MotionTable);
            player.SetProperty(PropertyDataId.SoundTable, sex.SoundTable);
            player.SetProperty(PropertyDataId.PhysicsEffectTable, sex.PhysicsTable);
            player.SetProperty(PropertyDataId.Setup, sex.SetupID);
            player.SetProperty(PropertyDataId.PaletteBase, sex.BasePalette);
            player.SetProperty(PropertyDataId.CombatTable, sex.CombatTable);

            var baseScale = sex.Scale / 100.0f;
            player.SetProperty(PropertyFloat.DefaultScale, baseScale * charSize.ScaleMultiplier);

            var hairstyle = sex.HairStyleList[hairStyleIndex];

            if (hairstyle.ObjDesc.AnimPartChanges.Count > 1)
                player.SetProperty(PropertyInt.Hairstyle, hairStyleIndex);
            else
                player.RemoveProperty(PropertyInt.Hairstyle);

            if (hairstyle.AlternateSetup > 0)
                player.SetProperty(PropertyDataId.Setup, hairstyle.AlternateSetup);

            player.SetProperty(PropertyDataId.EyesTexture, sex.GetEyeTexture((uint)eyeIndex, hairstyle.Bald));
            player.SetProperty(PropertyDataId.DefaultEyesTexture, sex.GetDefaultEyeTexture((uint)eyeIndex, hairstyle.Bald));
            player.SetProperty(PropertyDataId.NoseTexture, sex.GetNoseTexture((uint)noseIndex));
            player.SetProperty(PropertyDataId.DefaultNoseTexture, sex.GetDefaultNoseTexture((uint)noseIndex));
            player.SetProperty(PropertyDataId.MouthTexture, sex.GetMouthTexture((uint)mouthIndex));
            player.SetProperty(PropertyDataId.DefaultMouthTexture, sex.GetDefaultMouthTexture((uint)mouthIndex));

            player.CharacterDatabaseLock.EnterWriteLock();
            try
            {
                player.Character.HairTexture = sex.GetHairTexture((uint)hairStyleIndex) ?? 0;
                player.Character.DefaultHairTexture = sex.GetDefaultHairTexture((uint)hairStyleIndex) ?? 0;
                player.CharacterChangesDetected = true;
            }
            finally
            {
                player.CharacterDatabaseLock.ExitWriteLock();
            }

            var headObject = sex.GetHeadObject((uint)hairStyleIndex);
            if (headObject != null)
                player.SetProperty(PropertyDataId.HeadObject, (uint)headObject);
            else
                player.RemoveProperty(PropertyDataId.HeadObject);

            var skinPalSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(sex.SkinPalSet);
            if (skinPalSet != null)
            {
                player.SetProperty(PropertyDataId.SkinPalette, skinPalSet.GetPaletteID(skinHue));
                player.SetProperty(PropertyFloat.Shade, skinHue);
            }

            if (sex.HairColorList.Count > hairColorIndex)
            {
                var hairPalSet = DatManager.PortalDat.ReadFromDat<PaletteSet>(sex.HairColorList[hairColorIndex]);
                if (hairPalSet != null)
                    player.SetProperty(PropertyDataId.HairPalette, hairPalSet.GetPaletteID(hairHue));
            }

            if (sex.EyeColorList.Count > eyeColorIndex)
                player.SetProperty(PropertyDataId.EyesPalette, sex.EyeColorList[eyeColorIndex]);

            GetMasteriesByHeritage(raceRoll, out WeaponType meleeMastery, out WeaponType rangedMastery);
            player.SetProperty(PropertyInt.MeleeMastery, (int)meleeMastery);
            player.SetProperty(PropertyInt.RangedMastery, (int)rangedMastery);

            player.SendMessage($"[Ironman] Rolled heritage: {heritageGroup.Name} | Size: {charSize.Label}", ChatMessageType.System);
        }

        private static int PickAppearanceIndex(int count, int appearanceRoll)
        {
            if (count <= 0)
                return 0;

            var offset = ThreadSafeRandom.Next(0, count - 1);
            return (appearanceRoll + offset) % count;
        }

        private static void GetMasteriesByHeritage(HeritageGroup heritageGroup, out WeaponType meleeMastery, out WeaponType rangedMastery)
        {
            switch (heritageGroup)
            {
                case HeritageGroup.Aluvian:
                    meleeMastery = WeaponType.Dagger;
                    rangedMastery = WeaponType.Bow;
                    break;
                case HeritageGroup.Gharundim:
                    meleeMastery = WeaponType.Staff;
                    rangedMastery = WeaponType.Magic;
                    break;
                case HeritageGroup.Sho:
                    meleeMastery = WeaponType.Unarmed;
                    rangedMastery = WeaponType.Bow;
                    break;
                case HeritageGroup.Viamontian:
                    meleeMastery = WeaponType.Sword;
                    rangedMastery = WeaponType.Crossbow;
                    break;
                case HeritageGroup.Penumbraen:
                case HeritageGroup.Shadowbound:
                    meleeMastery = WeaponType.Unarmed;
                    rangedMastery = WeaponType.Crossbow;
                    break;
                case HeritageGroup.Gearknight:
                    meleeMastery = WeaponType.Mace;
                    rangedMastery = WeaponType.Crossbow;
                    break;
                case HeritageGroup.Tumerok:
                    meleeMastery = WeaponType.Spear;
                    rangedMastery = WeaponType.Thrown;
                    break;
                case HeritageGroup.Undead:
                case HeritageGroup.Lugian:
                    meleeMastery = WeaponType.Axe;
                    rangedMastery = WeaponType.Thrown;
                    break;
                case HeritageGroup.Empyrean:
                    meleeMastery = WeaponType.Sword;
                    rangedMastery = WeaponType.Magic;
                    break;
                default:
                    meleeMastery = WeaponType.Undef;
                    rangedMastery = WeaponType.Undef;
                    break;
            }
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = ThreadSafeRandom.Next(0, i);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private static void SendIronmanSkillUpdate(Player player, Skill skill)
        {
            player.Session.Network.EnqueueSend(
                new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(skill)),
                new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, 0));
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
            ApplyIronmanLifeMilestones(player, player.Level ?? 1);
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
            player.RemoveProperty(PropertyString.IronmanLifeMilestones);
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

        private static HashSet<int> GetClaimedLifeMilestones(Player player)
        {
            var raw = player.GetProperty(PropertyString.IronmanLifeMilestones) ?? string.Empty;
            var claimed = new HashSet<int>();

            foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out var level))
                    claimed.Add(level);
            }

            return claimed;
        }

        private static void SaveClaimedLifeMilestones(Player player, HashSet<int> claimed)
        {
            if (claimed == null || claimed.Count == 0)
            {
                player.RemoveProperty(PropertyString.IronmanLifeMilestones);
                return;
            }

            player.SetProperty(PropertyString.IronmanLifeMilestones, string.Join(";", claimed.OrderBy(x => x)));
        }

        private static void ApplyIronmanLifeMilestones(Player player, int currentLevel)
        {
            if (player.GetProperty(PropertyBool.IsIronman) != true)
                return;

            var claimed = GetClaimedLifeMilestones(player);
            var changed = false;
            var lives = player.GetProperty(PropertyInt.HardcoreLives) ?? DerpACEConfig.IronmanHardcoreStartingLives;

            foreach (var milestone in HardcoreLifeMilestones)
            {
                if (currentLevel < milestone || claimed.Contains(milestone))
                    continue;

                claimed.Add(milestone);
                changed = true;

                var previousLives = lives;
                lives = Math.Min(IronmanMaxHardcoreLives, lives + 1);

                if (lives > previousLives)
                    player.SendMessage($"[Ironman] Milestone reached (Level {milestone}): +1 hardcore life ({lives}/{IronmanMaxHardcoreLives}).", ChatMessageType.Advancement);
                else
                    player.SendMessage($"[Ironman] Milestone reached (Level {milestone}), but lives are already capped at {IronmanMaxHardcoreLives}.", ChatMessageType.Advancement);
            }

            if (!changed)
                return;

            player.SetProperty(PropertyInt.HardcoreLives, lives);
            SaveClaimedLifeMilestones(player, claimed);
        }

        public static IReadOnlyList<int> GetHardcoreLifeMilestones() => HardcoreLifeMilestones;

        public static HashSet<int> GetClaimedHardcoreLifeMilestones(Player player) => GetClaimedLifeMilestones(player);
    }
}
