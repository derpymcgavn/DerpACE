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
using ACE.Server.DerpAce;
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

        // DerpACE: -nh ("no non-humans") race pool. The -nh toggle REMOVES the non-human /
        // monstrous heritages (Gearknight, Tumerok, Lugian) so only the humanoid-framed races
        // remain in the roll.
        private static readonly HeritageGroup[] IronmanRacePoolNoNonHuman =
        {
            HeritageGroup.Aluvian,
            HeritageGroup.Gharundim,
            HeritageGroup.Sho,
            HeritageGroup.Viamontian,
            HeritageGroup.Shadowbound, // Umbraen
            HeritageGroup.Penumbraen,
            HeritageGroup.Undead,
            HeritageGroup.Empyrean,
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
        };

        // Base skills that are pre-trained at level 1 and should not be rolled
        // These start auto-trained for all races: Arcane Lore, Jump, Loyalty, Magic Defense, Run, Salvaging
        private static readonly Skill[] BaseSkillsPreTrained =
        {
            Skill.ArcaneLore,
            Skill.Jump,
            Skill.Loyalty,
            Skill.MagicDefense,
            Skill.Run,
            Skill.Salvaging,
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

        // Starter items are now granted exclusively via GiveStarterGear, which uses
        // StarterGearFactory and the starterGear.json configuration to properly map
        // racial weapons (e.g., Aluvian Light → Training Dagger, Sho Light → Training Knuckles).

        // ---------- Public entry point ----------

        public static void InitializeIronman(Player player, bool noNonHuman = false, bool blind = false)
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
                RollHeritageAndAppearance(player, noNonHuman);
                RollAttributes(player);
                rolledPrimary = RollSkills(player, blind: blind);
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
                // GiveStarterGear uses StarterGearFactory which correctly maps racial weapons
                // and all other skill-based starter items from starterGear.json
                GiveStarterGear(player);
                GrantChallengeBook(player, HardcodedWeenies.IronmanGuideBookWeenieClassId, "Ironman");

                // Final pass after all conversion actions so every remaining item is correctly tagged.
                TagAllPossessions(player);
                AutoSpendBlindIronmanXp(player);

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
            ApplyIronmanBlindFlag(player, blind);
        }

        // ---------- Nomad public entry point ----------

        /// <summary>
        /// Nomad Ironman initialization. Identical staging to InitializeIronman with these
        /// differences:
        ///   * Attributes roll randomly between 10 and 100 per stat.
        ///   * Weapon skill is forced to Light Weapons (trained + specialized).
        ///   * Arcane Lore is specialized in addition to being pre-trained.
        ///   * Player is flagged IsIronmanNomad — wielding any weapon/caster is blocked.
        ///   * Natural body AL is 450 in clothes only; worn armor is half effective.
        /// Starter gear uses a Nomad-specific kit instead of generic Pathwarden armor/weapons.
        /// </summary>
        public static void InitializeIronmanNomad(Player player, bool noNonHuman = false, bool blind = false)
        {
            if (player == null) return;

            if (player.GetProperty(PropertyBool.IsIronman) == true)
            {
                player.SendMessage("You are already an Ironman.");
                return;
            }

            player.SendMotionAsCommands(MotionCommand.EnterPortal, MotionStance.NonCombat);

            var chain = new ActionChain();
            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 1/6: rerolling heritage, appearance, random attributes, and skills...");
                RollHeritageAndAppearance(player, noNonHuman);
                RollAttributesRandom(player);
                RollSkills(player, forcedWeaponSkill: Skill.LightWeapons, forcedWeaponIsMagic: false, specializeArcaneLore: true, blind: blind);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 2/6: wiping inventory...");
                WipeInventory(player);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 3/6: wiping known spells...");
                WipeKnownSpells(player);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 4/6: applying ironman skill milestones...");
                ApplyIronmanPlanForLevel(player, player.Level ?? 1, announceGrants: false);
            });

            chain.AddDelaySeconds(1.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 5/6: teaching starter spells...");
                foreach (var spellId in DefaultSpells)
                    player.LearnSpellWithNetworking((uint)spellId, false);
                player.SendMessage("You have been taught the basic spells available to all Ironmen.");
            });

            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.ExitPortal, MotionStance.NonCombat));

            chain.AddDelaySeconds(3.0);
            chain.AddAction(player, () =>
            {
                player.SendMessage("Nomad step 6/6: granting starter gear...");
                GiveNomadStarterGear(player);
                GiveNomadGauntletsAndShoes(player);
                GrantChallengeBook(player, HardcodedWeenies.NomadSurvivalTomeWeenieClassId, "Nomad");
                TagAllPossessions(player);
                AutoSpendBlindIronmanXp(player);

                player.SendMessage("As a Nomad, you cannot wield weapons or casters. Your damage will come from elemental gauntlets and shoes.");
                player.SendMessage(DerpACEConfig.IronmanWelcomeMessage);
                for (var i = 0; i < 6; i++)
                    player.PlayParticleEffect(PlayScript.SkillUpPurple, player.Guid);
            });

            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.Cheer, MotionStance.NonCombat));
            chain.AddAction(player, () => player.SendMotionAsCommands(MotionCommand.Wave, MotionStance.NonCombat));

            chain.AddDelaySeconds(2.0);
            chain.AddAction(player, () =>
            {
                const string relogMsg = "finalizing nomad ironman mode - Relog!";
                player.Session.Terminate(SessionTerminationReason.ForcedLogOffRequested, new GameMessageBootAccount($" - {relogMsg}"));
            });
            chain.EnqueueChain();

            ApplyHardcore(player);
            ApplyIronmanFlag(player);
            ApplyIronmanNomadFlag(player);
            ApplyIronmanBlindFlag(player, blind);
        }

        private static void ApplyIronmanNomadFlag(Player player)
        {
            player.SetProperty(PropertyBool.IsIronmanNomad, true);
            player.SetModeTitle("NOMAD");
        }

        private static void GrantChallengeBook(Player player, uint wcid, string pathName)
        {
            var book = WorldObjectFactory.CreateNewWorldObject(wcid);
            if (book == null)
            {
                player.SendMessage($"[{pathName}] Failed to create path guide book (wcid {wcid}).");
                return;
            }

            book.SetProperty(PropertyBool.IsIronmanItem, true);
            book.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceIronman);

            if (player.TryCreateInInventoryWithNetworking(book))
                player.SendMessage($"[{pathName}] A guide book has been placed in your pack: {book.Name}.");
            else
                player.SendMessage($"[{pathName}] Failed to place {book.Name} in your pack.");
        }

        // Weenie class ids used to create the nomad's elemental gauntlets and shoes.
        private const uint NomadGauntletWcid = 56;  // W_GAUNTLETSLEATHER_CLASS
        private const uint NomadBootWcid     = 115; // W_BOOTSLEATHER_CLASS

        // Damage type -> friendly name used in the inscription.
        // Includes physical (Slash/Pierce/Bludgeon) and elemental (Fire/Cold/Acid/Electric).
        private static readonly (DamageType Type, string Name)[] NomadElements =
        {
            (DamageType.Slash,    "Slashing"),
            (DamageType.Pierce,   "Piercing"),
            (DamageType.Bludgeon, "Bludgeoning"),
            (DamageType.Fire,     "Flame"),
            (DamageType.Cold,     "Frost"),
            (DamageType.Acid,     "Acid"),
            (DamageType.Electric, "Lightning"),
        };

        /// <summary>
        /// Maps a DamageType to its matching <see cref="UiEffects"/> outline flag so
        /// elemental/physical items glow with the correct color in the UI.
        /// </summary>
        internal static UiEffects GetElementalUiEffect(DamageType damageType) => damageType switch
        {
            DamageType.Fire     => UiEffects.Fire,
            DamageType.Cold     => UiEffects.Frost,
            DamageType.Acid     => UiEffects.Acid,
            DamageType.Electric => UiEffects.Lightning,
            DamageType.Slash    => UiEffects.Slashing,
            DamageType.Pierce   => UiEffects.Piercing,
            DamageType.Bludgeon => UiEffects.Bludgeoning,
            DamageType.Nether   => UiEffects.Nether,
            _                   => UiEffects.Undef,
        };

        /// <summary>
        /// Grants a nomad their starting elemental gauntlets and shoes. Each pair rolls a
        /// random element (Fire / Cold / Acid / Electric) and is inscribed by "M. Stranger"
        /// with the unarmed damage stats stamped onto the inscription so the player can read
        /// exactly what the item does.
        /// </summary>
        private static void GiveNomadGauntletsAndShoes(Player player)
        {
            // Starter pair shares a single element so nomads cleanly collect one new element
            // every other level (2, 4, 6, ...) and have all 7 by level 14.
            var element = NomadElements[ThreadSafeRandom.Next(0, NomadElements.Length - 1)];

            CreateAndGrantNomadUnarmedItem(player, NomadGauntletWcid, "Gauntlets",  element.Type, element.Name, baseDamage: 12, variance: 0.50f);
            CreateAndGrantNomadUnarmedItem(player, NomadBootWcid,     "Shoes",      element.Type, element.Name, baseDamage: 10, variance: 0.55f);
        }

        /// <summary>
        /// Grants a nomad a matched gauntlet/shoe pair of a random element they don't already
        /// own. Called on every even level (2,4,6,...,14) so a nomad collects all 7 elements
        /// by level 14. No-ops once every element has been granted.
        /// </summary>
        private static void GrantNextNomadElement(Player player)
        {
            var have = new HashSet<DamageType>();
            foreach (var wo in player.GetAllPossessions())
            {
                var name = wo.GetProperty(PropertyString.Name);
                var dt = wo.GetProperty(PropertyInt.UnarmedDamageType);
                if (dt != null && name != null && name.Contains("Nomad"))
                    have.Add((DamageType)dt.Value);
            }

            var remaining = NomadElements.Where(e => !have.Contains(e.Type)).ToList();
            if (remaining.Count == 0)
                return;

            var pick = remaining[ThreadSafeRandom.Next(0, remaining.Count - 1)];

            CreateAndGrantNomadUnarmedItem(player, NomadGauntletWcid, "Gauntlets", pick.Type, pick.Name, baseDamage: 12, variance: 0.50f);
            CreateAndGrantNomadUnarmedItem(player, NomadBootWcid,     "Shoes",     pick.Type, pick.Name, baseDamage: 10, variance: 0.55f);

            if (player.Session != null)
                player.SendMessage($"[Nomad] You have unlocked a new element: {pick.Name}!", ChatMessageType.Broadcast);
        }

        private static void CreateAndGrantNomadUnarmedItem(Player player, uint wcid, string slotLabel,
            DamageType damageType, string elementName, int baseDamage, float variance)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(wcid);
            if (wo == null)
            {
                player.SendMessage($"[Nomad] Failed to create starter {slotLabel} (wcid {wcid}).");
                return;
            }

            // Stamp unarmed damage properties read by Player.GetBaseDamageMod / GetDamageType.
            wo.SetProperty(PropertyInt.UnarmedBaseDamage, baseDamage);
            wo.SetProperty(PropertyInt.UnarmedDamageType, (int)damageType);
            wo.SetProperty(PropertyFloat.UnarmedDamageVariance, variance);
            wo.SetProperty(PropertyInt.Damage, baseDamage);
            wo.SetProperty(PropertyInt.WeaponSkill, (int)Skill.UnarmedCombat);
            wo.SetProperty(PropertyInt.WeaponTime, slotLabel == "Gauntlets" ? 35 : 45);
            wo.SetProperty(PropertyFloat.DamageVariance, variance);
            wo.SetProperty(PropertyFloat.DamageMod, 1.0);
            wo.SetProperty(PropertyFloat.WeaponOffense, ACE.Server.Managers.PropertyManager.GetDouble("unarmed_nomad_item_attack_mod").Item);
            wo.SetProperty(PropertyFloat.WeaponDefense, 1.08);

            // Apply the matching UiEffects outline so the item glows with its element in the UI.
            // Stamp W_DamageType too so any downstream code that keys off it (e.g. ArmorLevel display
            // or elemental visuals) sees the matched type.
            wo.W_DamageType = damageType;
            var ui = GetElementalUiEffect(damageType);
            if (ui != UiEffects.Undef)
                wo.UiEffects = ui;

            // Roll a custom nomad proc onto the item — handled in Player_Combat.DamageTarget.
            // 1 = Cleave Flurry, 2 = Healing Strike. Roughly even odds.
            var procRoll = ThreadSafeRandom.Next(1, 2);
            string procDescription;
            if (procRoll == 1)
            {
                // Cleave Flurry: chance to unleash 2-4 fast extra strikes on hit.
                var chance = (float)ThreadSafeRandom.Next(0.08f, 0.15f);
                // Magnitude encodes per-extra-strike damage scale (0.35 = 35% of the original hit).
                var magnitude = (float)ThreadSafeRandom.Next(0.30f, 0.45f);
                wo.SetProperty(PropertyInt.NomadProcType, 1);
                wo.SetProperty(PropertyFloat.NomadProcChance, chance);
                wo.SetProperty(PropertyFloat.NomadProcMagnitude, magnitude);
                procDescription = $"Cleave Flurry: {chance * 100:0.#}% chance on hit to unleash 2-4 fast extra strikes at {magnitude * 100:0.#}% damage each.";
            }
            else
            {
                // Healing Strike: chance to do bonus damage and heal the wielder for a chunk of it.
                var chance = (float)ThreadSafeRandom.Next(0.08f, 0.15f);
                // Magnitude encodes self-heal as a fraction of damage dealt (1.00 = +100%, with 1-10% extra).
                var magnitude = (float)ThreadSafeRandom.Next(1.01f, 1.10f);
                wo.SetProperty(PropertyInt.NomadProcType, 2);
                wo.SetProperty(PropertyFloat.NomadProcChance, chance);
                wo.SetProperty(PropertyFloat.NomadProcMagnitude, magnitude);
                procDescription = $"Healing Strike: {chance * 100:0.#}% chance on hit to heal {magnitude * 100:0.#}% of damage dealt back to the wielder.";
            }

            // Rename so the element is visible at a glance.
            wo.SetProperty(PropertyString.Name, $"{elementName} Nomad {slotLabel}");

            // Inscription by M. Stranger documenting the damage and proc.
            var inscription =
                $"Inscribed by M. Stranger:\n" +
                $"These {slotLabel.ToLowerInvariant()} channel {elementName.ToLowerInvariant()} when struck unarmed.\n" +
                $"Base Damage: {baseDamage}  Variance: {variance:0.00}  Attack Mod: {wo.WeaponOffense:0.000}  Element: {elementName} ({damageType})\n" +
                $"{procDescription}";

            wo.SetProperty(PropertyString.Inscription, inscription);
            wo.SetProperty(PropertyString.ScribeName, "M. Stranger");
            // Must be true — Inscribable=false hides the inscription text on the client.
            wo.SetProperty(PropertyBool.Inscribable, true);

            if (!player.TryCreateInInventoryWithNetworking(wo))
                player.SendMessage($"[Nomad] Could not place {slotLabel} in inventory.");
        }

        // ---------- Attribute reroll ----------

        public static void RollAttributes(Player player)
        {
            // Pick one primary attribute to set to 100; others go to 46 (matches mod)
            var primary = (PropertyAttribute)ThreadSafeRandom.Next(1, 6);
            foreach (PropertyAttribute attr in System.Enum.GetValues(typeof(PropertyAttribute)))
            {
                if (attr == PropertyAttribute.Undef) continue;

                var pAttr = player.Attributes[attr];
                pAttr.StartingValue = attr == primary ? 100u : 46u;

                if (player.Session != null)
                    player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, pAttr));
            }
        }

        /// <summary>
        /// Nomad-style attribute reroll: every attribute rolls randomly between 10 and 100.
        /// </summary>
        public static void RollAttributesRandom(Player player)
        {
            foreach (PropertyAttribute attr in System.Enum.GetValues(typeof(PropertyAttribute)))
            {
                if (attr == PropertyAttribute.Undef) continue;

                var pAttr = player.Attributes[attr];
                pAttr.StartingValue = (uint)ThreadSafeRandom.Next(10, 100);

                if (player.Session != null)
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

        // Skill credits earned per level (cumulative AFTER the initial 52 at level 1)
        // Key = level, Value = total new credits earned from levels 2-X
        private static readonly Dictionary<int, int> SkillCreditsPerLevel = new Dictionary<int, int>
        {
            { 2, 1 }, { 3, 2 }, { 4, 3 }, { 5, 4 }, { 6, 5 }, { 7, 6 }, { 8, 7 }, { 9, 8 },
            { 10, 9 }, { 12, 10 }, { 14, 11 }, { 16, 12 }, { 18, 13 }, { 20, 14 },
            { 23, 15 }, { 26, 16 }, { 29, 17 }, { 32, 18 }, { 35, 19 }, { 40, 20 },
            { 45, 21 }, { 50, 22 }, { 55, 23 }, { 60, 24 }, { 65, 25 }, { 70, 26 },
            { 75, 27 }, { 80, 28 }, { 85, 29 }, { 90, 30 }, { 95, 31 }, { 100, 32 },
            { 105, 33 }, { 110, 34 }, { 115, 35 }, { 120, 36 }, { 125, 37 }, { 130, 38 },
            { 140, 39 }, { 150, 40 }, { 160, 41 }, { 180, 42 }, { 200, 43 }, { 225, 44 },
            { 250, 45 }, { 275, 46 },
        };

        // Extra hardcore life milestones between levels 1-275.
        private static readonly int[] HardcoreLifeMilestones = { 75, 150, 225 };
        private const int IronmanMaxHardcoreLives = 3;

        /// <summary>
        /// Resets all skills, builds a level-milestone plan, and immediately applies any skills
        /// due at the character's current level.  Returns the rolled primary skill.
        /// </summary>
        public static Skill RollSkills(Player player, Skill forcedWeaponSkill = Skill.None, bool forcedWeaponIsMagic = false, bool specializeArcaneLore = false, bool blind = false)
        {
            var showDebug = player.Session != null && !blind;

            // Reset every skill to Untrained (refunds credits + xp for trained/spec'd skills)
            foreach (Skill skill in System.Enum.GetValues(typeof(Skill)))
            {
                if (skill == Skill.None) continue;

                var cs = player.GetCreatureSkill(skill);
                if (cs == null) continue;

                // Reset ALL skills to Untrained first (including base skills)
                if (cs.AdvancementClass >= SkillAdvancementClass.Trained)
                {
                    player.ResetSkill(skill, true);

                    // ResetSkill might only bring specialized skills down to trained
                    // Force to untrained
                    cs.AdvancementClass = SkillAdvancementClass.Untrained;
                    cs.InitLevel = 0;
                    cs.Ranks = 0;
                    cs.ExperienceSpent = 0;
                }
            }

            // Set starting skill credits to 52 for level 1 Ironman
            player.AvailableSkillCredits = 52;

            if (showDebug)
                player.SendMessage($"[Ironman Debug] Starting with {player.AvailableSkillCredits} skill credits", ChatMessageType.System);

            // Now train base skills at no cost (they're pre-trained at level 1)
            foreach (var baseSkill in BaseSkillsPreTrained)
            {
                var cs = player.GetCreatureSkill(baseSkill);
                if (cs != null && cs.AdvancementClass < SkillAdvancementClass.Trained)
                {
                    player.TrainSkill(baseSkill, 0); // 0 cost for base skills - DO NOT SPECIALIZE

                    if (showDebug)
                        player.SendMessage($"[Ironman Debug] Pre-trained {baseSkill} (0 credits, TRAINED only)", ChatMessageType.System);
                }
            }

            // Nomad: specialize Arcane Lore (already trained above at 0 cost) using credits.
            if (specializeArcaneLore)
            {
                if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)Skill.ArcaneLore, out var arcaneBase))
                {
                    var specCost = arcaneBase.UpgradeCostFromTrainedToSpecialized;
                    if ((player.AvailableSkillCredits ?? 0) >= specCost)
                    {
                        player.SpecializeSkill(Skill.ArcaneLore, specCost, false);
                        SendIronmanSkillUpdate(player, Skill.ArcaneLore);

                        if (showDebug)
                            player.SendMessage($"[Nomad Debug] Specialized Arcane Lore ({specCost} credits). Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                    }
                }
            }

            var plan = new Dictionary<Skill, int>();

            // Roll the weapon skill from website-equivalent list, then train+spec it with proper credit costs.
            var rolledWeapon = forcedWeaponSkill != Skill.None
                ? new IronmanWeaponOption(forcedWeaponSkill, 0, forcedWeaponIsMagic)
                : WeaponSkillPool[ThreadSafeRandom.Next(0, WeaponSkillPool.Length - 1)];

            // Get actual costs from DAT instead of hardcoded values
            if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)rolledWeapon.Skill, out var weaponSkillBase))
            {
                var trainCost = weaponSkillBase.TrainedCost;
                var specCost = weaponSkillBase.UpgradeCostFromTrainedToSpecialized;

                // Verify we have enough credits for train + spec
                var totalWeaponCost = trainCost + specCost;

                if (showDebug)
                    player.SendMessage($"[Ironman Debug] Attempting weapon {rolledWeapon.Skill}: train ({trainCost}) + spec ({specCost}) = {totalWeaponCost} total. Available: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);

                if ((player.AvailableSkillCredits ?? 0) >= totalWeaponCost)
                {
                    bool trainSuccess = player.TrainSkill(rolledWeapon.Skill, trainCost);
                    if (trainSuccess)
                    {
                        bool specSuccess = player.SpecializeSkill(rolledWeapon.Skill, specCost, false);

                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] Weapon {rolledWeapon.Skill}: train={trainSuccess}, spec={specSuccess}. Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                    }
                    else
                    {
                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] FAILED to train weapon {rolledWeapon.Skill}! Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                    }
                }
                else
                {
                    // Fallback: just train without spec if we can't afford both
                    if ((player.AvailableSkillCredits ?? 0) >= trainCost)
                    {
                        bool trainSuccess = player.TrainSkill(rolledWeapon.Skill, trainCost);

                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] Weapon {rolledWeapon.Skill}: trained only ({trainCost}), train={trainSuccess}. Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                    }
                    else
                    {
                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] Cannot afford weapon {rolledWeapon.Skill} train cost ({trainCost}). Available: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                    }
                }
            }

            plan[rolledWeapon.Skill] = 0;
            SendIronmanSkillUpdate(player, rolledWeapon.Skill);

            // Magic weapon specs (Life / Void / War) MUST auto-train Mana Conversion immediately,
            // BEFORE we shuffle the rest of the primary pool. Otherwise the shuffled iteration can
            // run out of credits before reaching MC, leaving a magic build with no mana sustain.
            if (rolledWeapon.IsMagic)
            {
                if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)Skill.ManaConversion, out var mcBase))
                {
                    var mcCs = player.GetCreatureSkill(Skill.ManaConversion);
                    if (mcCs == null || mcCs.AdvancementClass < SkillAdvancementClass.Trained)
                    {
                        var mcTrainCost = mcBase.TrainedCost;
                        if ((player.AvailableSkillCredits ?? 0) >= mcTrainCost)
                        {
                            if (player.TrainSkill(Skill.ManaConversion, mcTrainCost))
                            {
                                plan[Skill.ManaConversion] = 0;
                                SendIronmanSkillUpdate(player, Skill.ManaConversion);

                                if (showDebug)
                                    player.SendMessage($"[Ironman Debug] Auto-trained Mana Conversion for magic spec ({mcTrainCost} credits). Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                            }
                        }
                        else if (showDebug)
                        {
                            player.SendMessage($"[Ironman Debug] Cannot afford Mana Conversion ({mcTrainCost} credits, have {player.AvailableSkillCredits ?? 0}); magic spec will be mana-starved.", ChatMessageType.System);
                        }
                    }
                }
            }

            // Build mutable primary list from website-equivalent list.
            var primaryPool = new List<IronmanPrimarySkillOption>(PrimarySkillPool);

            // Magic primaries already auto-trained Mana Conversion above — drop it from the pool
            // so the shuffled loop doesn't try to spend credits on it again.
            if (rolledWeapon.IsMagic)
                primaryPool.RemoveAll(x => x.Skill == Skill.ManaConversion);

            // If weapon is Life Magic, remove Life Magic from the primary pool.
            if (rolledWeapon.Skill == Skill.LifeMagic)
                primaryPool.RemoveAll(x => x.Skill == Skill.LifeMagic);

            // Shuffle the primary pool to randomize training order
            Shuffle(primaryPool);

            // Keep training random skills from the pool until we run out of credits (or nearly run out)
            // This ensures the 52 starting credits are mostly/fully spent at level 1
            var trainedAtCreation = new List<Skill> { rolledWeapon.Skill };

            foreach (var primaryOption in primaryPool)
            {
                if (plan.ContainsKey(primaryOption.Skill))
                    continue;

                if (trainedAtCreation.Contains(primaryOption.Skill))
                    continue;

                var cs = player.GetCreatureSkill(primaryOption.Skill);
                if (cs?.AdvancementClass >= SkillAdvancementClass.Trained)
                    continue; // already trained

                // Check if we have enough credits to train this skill
                if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)primaryOption.Skill, out var skillBase))
                {
                    var trainCost = skillBase.TrainedCost;

                    if ((player.AvailableSkillCredits ?? 0) >= trainCost)
                    {
                        // Try to train the skill, checking if it actually succeeds
                        bool trainSuccess = player.TrainSkill(primaryOption.Skill, trainCost);

                        if (trainSuccess)
                        {
                            plan[primaryOption.Skill] = 0; // Mark as trained at creation (level 0)
                            trainedAtCreation.Add(primaryOption.Skill);
                            SendIronmanSkillUpdate(player, primaryOption.Skill);

                            if (showDebug)
                                player.SendMessage($"[Ironman Debug] Trained {primaryOption.Skill} ({trainCost} credits). Remaining: {player.AvailableSkillCredits ?? 0}", ChatMessageType.System);
                        }
                        else
                        {
                            if (showDebug)
                                player.SendMessage($"[Ironman Debug] FAILED to train {primaryOption.Skill} (tried {trainCost} credits, have {player.AvailableSkillCredits ?? 0})", ChatMessageType.System);
                        }
                    }
                    else
                    {
                        // Not enough credits left, this skill will be assigned to a milestone
                        // Don't break - keep checking other skills that might be cheaper
                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] Skipped {primaryOption.Skill} (need {trainCost}, have {player.AvailableSkillCredits ?? 0})", ChatMessageType.System);
                    }
                }
            }

            // Any skills not trained at creation get assigned to milestone levels
            // We need to track cumulative credits earned and ensure skills can actually be trained
            var untrainedSkills = new List<(Skill skill, int trainCost)>();

            foreach (var primaryOption in primaryPool)
            {
                if (plan.ContainsKey(primaryOption.Skill))
                    continue; // already trained

                // Get the train cost for this skill
                if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)primaryOption.Skill, out var skillBase))
                {
                    untrainedSkills.Add((primaryOption.Skill, skillBase.TrainedCost));
                }
            }

            // Sort untrained skills by cost (cheapest first) so we can fit more skills into early milestones
            untrainedSkills.Sort((a, b) => a.trainCost.CompareTo(b.trainCost));

            // Track cumulative credits spent across ALL milestones
            int totalCreditsAllocated = 0;
            int currentMilestoneIndex = 0;

            if (showDebug)
                player.SendMessage($"[Ironman Debug] Assigning {untrainedSkills.Count} untrained skills to milestones...", ChatMessageType.System);

            foreach (var (skill, trainCost) in untrainedSkills)
            {
                bool assigned = false;

                // Try to find a milestone where we'll have enough cumulative credits
                for (int i = currentMilestoneIndex; i < SkillMilestones.Length; i++)
                {
                    var milestoneLevel = SkillMilestones[i];

                    // Get total NEW credits earned by this level (beyond the initial 52)
                    if (!SkillCreditsPerLevel.TryGetValue(milestoneLevel, out var cumulativeCreditsAtLevel))
                    {
                        // If not in table, find the closest lower level
                        cumulativeCreditsAtLevel = 0;
                        foreach (var kvp in SkillCreditsPerLevel.OrderByDescending(x => x.Key))
                        {
                            if (kvp.Key <= milestoneLevel)
                            {
                                cumulativeCreditsAtLevel = kvp.Value;
                                break;
                            }
                        }
                    }

                    // Check if we have enough credits left after all previous milestone assignments
                    var remainingCredits = cumulativeCreditsAtLevel - totalCreditsAllocated;

                    if (remainingCredits >= trainCost)
                    {
                        // We can assign this skill to this milestone
                        plan[skill] = milestoneLevel;
                        totalCreditsAllocated += trainCost;
                        assigned = true;

                        if (showDebug)
                            player.SendMessage($"[Ironman Debug] Assigned {skill} to level {milestoneLevel} (cost {trainCost}, remaining {remainingCredits - trainCost}/{cumulativeCreditsAtLevel})", ChatMessageType.System);

                        break;
                    }
                }

                if (!assigned)
                {
                    // Could not find any milestone with enough credits
                    plan[skill] = -2; // never unlock

                    if (showDebug)
                        player.SendMessage($"[Ironman Debug] {skill} marked as UNOBTAINABLE (cost {trainCost}, max credits {SkillCreditsPerLevel[SkillMilestones[SkillMilestones.Length - 1]]})", ChatMessageType.System);
                }
            }

            // Serialize and store the plan — at-creation grants will be applied by the delayed chain.
            player.SetProperty(PropertyString.IronmanPlan, string.Join(";", plan.Select(kv => $"{kv.Key}:{kv.Value}")));

            return rolledWeapon.Skill;
        }

        private static void RollHeritageAndAppearance(Player player, bool noNonHuman = false)
        {
            var racePool = noNonHuman ? IronmanRacePoolNoNonHuman : IronmanRacePool;
            var raceRoll = racePool[ThreadSafeRandom.Next(0, racePool.Length - 1)];
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
            if (player.Session != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(skill)),
                    new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0));
            }
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

                // Get the actual train cost from DAT and check if player has enough credits
                if (DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill, out var skillBase))
                {
                    var trainCost = skillBase.TrainedCost;

                    // Check if player has enough credits
                    if ((player.AvailableSkillCredits ?? 0) < trainCost)
                    {
                        // Not enough credits yet - don't mark as done, let it try again next level
                        plan[skill] = milestone; // reset to original milestone
                        anyChanged = true; // still need to save the plan

                        if (announceGrants && milestone > 0)
                            messages.Add($"[Ironman] Not enough credits to unlock {skill.ToSentence()} (need {trainCost}, have {player.AvailableSkillCredits ?? 0})");

                        continue;
                    }

                    // We have enough credits, train the skill
                    player.TrainSkill(skill, trainCost);
                }
                else
                {
                    player.TrainSkill(skill, 0); // fallback to 0 if we can't find cost
                }

                // Push the updated skill and credit count to the client immediately
                // so the skill panel reflects the change without a relog.
                if (player.Session != null)
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessagePrivateUpdateSkill(player, player.GetCreatureSkill(skill)),
                        new GameMessagePrivateUpdatePropertyInt(player, PropertyInt.AvailableSkillCredits, player.AvailableSkillCredits ?? 0));
                }

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
            var level = player.Level ?? 1;
            ApplyIronmanPlanForLevel(player, level, announceGrants: true);
            ApplyIronmanLifeMilestones(player, level);

            // Nomads collect one new element every other level (2,4,6,...,14 => all 7 elements).
            if (player.GetProperty(PropertyBool.IsIronmanNomad) == true && level >= 2 && level <= 14 && level % 2 == 0)
                GrantNextNomadElement(player);

            AutoSpendBlindIronmanXp(player);
        }

        // ---------- Inventory wipe ----------

        private static readonly HashSet<uint> IronmanPreservedStarterItemWcids = new HashSet<uint>
        {
            5084,  // Calling Stone
            33613, // Pathwarden Token
            48886, // Mud Golem Essence
            30985, // Sho Letter From Home
            30986, // Gharu'ndim Letter From Home
            30987, // Viamontian Letter From Home
            30988, // Aluvian Letter From Home
            43018, // Gear Knight Letter From Home
            43019, // Umbraen/Penumbraen Letter From Home
            42979, // Core Plating Integrator
            43022, // Core Plating Deintegrator
            HardcodedWeenies.DerptideIntroBookWeenieClassId,
            HardcodedWeenies.IronmanGuideBookWeenieClassId,
            HardcodedWeenies.NomadSurvivalTomeWeenieClassId,
        };

        private static void WipeInventory(Player player)
        {
            // Snapshot then destroy every wielded + carried item.
            var owned = player.GetAllPossessions().ToList();
            foreach (var item in owned)
            {
                try
                {
                    if (ShouldPreserveIronmanConversionItem(item))
                        continue;

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

        private static bool ShouldPreserveIronmanConversionItem(WorldObject item)
        {
            if (item == null)
                return false;

            if (IronmanPreservedStarterItemWcids.Contains(item.WeenieClassId))
                return true;

            if (item.WeenieType == WeenieType.Book || (item.ItemType & ItemType.Writable) != 0)
                return true;

            if (!string.IsNullOrWhiteSpace(item.Quest))
                return true;

            return false;
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

        // ---------- Starter gear ----------

        private const uint NomadStarterRobeWcid = 40439;

        private static void GiveNomadStarterGear(Player player)
        {
            NomadRunePouch.EnsureFor(player, notify: false);

            var boneGrinder = WorldObjectFactory.CreateNewWorldObject(ScavengersHexdust.BoneGrinderWeenieClassId);
            if (boneGrinder != null)
            {
                boneGrinder.SetProperty(PropertyBool.IsIronmanItem, true);
                boneGrinder.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceIronman);
                if (!player.TryCreateInInventoryWithNetworking(boneGrinder))
                    player.SendMessage("[Nomad] Failed to place your Bone Grinder in your pack.");
            }

            var robe = WorldObjectFactory.CreateNewWorldObject(NomadStarterRobeWcid);
            if (robe != null)
            {
                robe.SetProperty(PropertyBool.IsIronmanItem, true);
                robe.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceIronman);
                robe.SetProperty(PropertyInt.PaletteTemplate, 39);
                robe.SetProperty(PropertyFloat.Shade, 0.8583308693778472);
                if (!player.TryCreateInInventoryWithNetworking(robe))
                    player.SendMessage("[Nomad] Failed to place your Pathwarden robe in your pack.");
            }
            else
                player.SendMessage($"[Nomad] Failed to create starter robe (wcid {NomadStarterRobeWcid}).");

            var starterSpells = NomadRune.GetStarterSpells(player);
            foreach (var spellId in starterSpells)
            {
                var rune = WorldObjectFactory.CreateNewWorldObject(NomadRune.NomadRuneWeenieClassId);
                if (rune == null)
                    continue;

                NomadRune.PrepareRune(rune, spellId);
                rune.SetProperty(PropertyInt.GearProvenance, Player.GearProvenanceIronman);

                if (!NomadRunePouch.TryStoreRune(player, rune, notify: false) && !player.TryCreateInInventoryWithNetworking(rune))
                    player.SendMessage($"[Nomad] Failed to place {rune.Name} in your pack.");
            }

            if (starterSpells.Count > 0)
                player.SendMessage($"[Nomad] {starterSpells.Count} level 1 rune stacks have been tucked away for the road.");
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

                // Grant universal skill-based gear (not heritage-specific)
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

                    // TryCreateInInventoryWithNetworking will auto-tag IsIronmanItem for Ironman players
                    if (player.TryCreateInInventoryWithNetworking(wo))
                        grantedWeenies.Add(item.WeenieId);

                    // Dual-wield bonus: give a second copy of melee weapons
                    if (isDualWield && wo.WeenieType == WeenieType.MeleeWeapon)
                    {
                        var dw2 = WorldObjectFactory.CreateNewWorldObject(item.WeenieId);
                        if (dw2 != null)
                        {
                            // TryCreateInInventoryWithNetworking will auto-tag IsIronmanItem
                            player.TryCreateInInventoryWithNetworking(dw2);
                        }
                    }
                }

                // Grant heritage-specific gear (e.g., racial starter weapons)
                var heritageLoot = skillGear.Heritage.FirstOrDefault(i => i.HeritageId == (ushort)player.HeritageGroup);
                if (heritageLoot != null)
                {
                    foreach (var item in heritageLoot.Gear)
                    {
                        if (grantedWeenies.Contains(item.WeenieId))
                        {
                            var existing = player.Inventory.Values.FirstOrDefault(i => i.WeenieClassId == item.WeenieId);
                            if (existing != null && (existing.MaxStackSize ?? 1) > 1)
                                existing.SetStackSize(existing.StackSize + item.StackSize);
                            continue;
                        }

                        var wo = WorldObjectFactory.CreateNewWorldObject(item.WeenieId);
                        if (wo == null) continue;

                        if (wo.StackSize.HasValue && wo.MaxStackSize.HasValue)
                            wo.SetStackSize(Math.Min(item.StackSize, wo.MaxStackSize.Value));

                        // TryCreateInInventoryWithNetworking will auto-tag IsIronmanItem for Ironman players
                        if (player.TryCreateInInventoryWithNetworking(wo))
                            grantedWeenies.Add(item.WeenieId);

                        if (isDualWield && wo.WeenieType == WeenieType.MeleeWeapon)
                        {
                            var dw2 = WorldObjectFactory.CreateNewWorldObject(item.WeenieId);
                            if (dw2 != null)
                            {
                                // TryCreateInInventoryWithNetworking will auto-tag IsIronmanItem
                                player.TryCreateInInventoryWithNetworking(dw2);
                            }
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

        private static void ApplyIronmanBlindFlag(Player player, bool blind)
        {
            if (!blind)
                return;

            player.SetProperty(PropertyBool.IsIronmanBlind, true);
            player.SendMessage("Blind progression is active. Future skill milestones are hidden, and XP will be spent automatically.", ChatMessageType.System);
        }

        public static void AutoSpendBlindIronmanXp(Player player)
        {
            if (player == null || player.GetProperty(PropertyBool.IsIronmanBlind) != true)
                return;

            var guard = 0;
            while ((player.AvailableExperience ?? 0) > 0 && guard++ < 5000)
            {
                var spent = false;

                spent |= TrySpendNextBlindSkillRank(player);
                spent |= TrySpendNextBlindVitalRank(player);
                spent |= TrySpendNextBlindAttributeRank(player);

                if (!spent)
                    break;
            }
        }

        private static bool TrySpendNextBlindSkillRank(Player player)
        {
            var available = player.AvailableExperience ?? 0;
            if (available <= 0)
                return false;

            var nextSkill = player.Skills.Values
                .Where(skill => skill.AdvancementClass >= SkillAdvancementClass.Trained && !skill.IsMaxRank)
                .Select(skill => new { Skill = skill, Cost = player.GetXpToNextRank(skill) })
                .Where(candidate => candidate.Cost.HasValue && candidate.Cost.Value > 0 && candidate.Cost.Value <= available)
                .OrderBy(candidate => candidate.Skill.Ranks)
                .ThenBy(candidate => candidate.Cost.Value)
                .ThenBy(candidate => candidate.Skill.Skill)
                .FirstOrDefault();

            return nextSkill != null && player.HandleActionRaiseSkill(nextSkill.Skill.Skill, nextSkill.Cost.Value);
        }

        private static bool TrySpendNextBlindVitalRank(Player player)
        {
            var available = player.AvailableExperience ?? 0;
            if (available <= 0)
                return false;

            var vitalOrder = new[]
            {
                PropertyAttribute2nd.MaxHealth,
                PropertyAttribute2nd.MaxStamina,
                PropertyAttribute2nd.MaxMana,
            };

            var vitalXpTable = DatManager.PortalDat.XpTable.VitalXpList;
            var nextVital = vitalOrder
                .Where(vital => player.Vitals.ContainsKey(vital))
                .Select(vital => player.Vitals[vital])
                .Where(vital => !vital.IsMaxRank)
                .Select(vital => new { Vital = vital, Cost = GetXpToNextBlindRank(vitalXpTable, vital.Ranks, vital.ExperienceSpent) })
                .Where(candidate => candidate.Cost.HasValue && candidate.Cost.Value > 0 && candidate.Cost.Value <= available)
                .OrderBy(candidate => candidate.Vital.Ranks)
                .ThenBy(candidate => candidate.Cost.Value)
                .ThenBy(candidate => Array.IndexOf(vitalOrder, candidate.Vital.Vital))
                .FirstOrDefault();

            return nextVital != null && player.HandleActionRaiseVital(nextVital.Vital.Vital, nextVital.Cost.Value);
        }

        private static bool TrySpendNextBlindAttributeRank(Player player)
        {
            var available = player.AvailableExperience ?? 0;
            if (available <= 0)
                return false;

            var attributeOrder = new[]
            {
                PropertyAttribute.Endurance,
                PropertyAttribute.Focus,
                PropertyAttribute.Self,
                PropertyAttribute.Coordination,
                PropertyAttribute.Strength,
                PropertyAttribute.Quickness,
            };

            var attributeXpTable = DatManager.PortalDat.XpTable.AttributeXpList;
            var nextAttribute = attributeOrder
                .Where(attribute => player.Attributes.ContainsKey(attribute))
                .Select(attribute => player.Attributes[attribute])
                .Where(attribute => !attribute.IsMaxRank)
                .Select(attribute => new { Attribute = attribute, Cost = GetXpToNextBlindRank(attributeXpTable, attribute.Ranks, attribute.ExperienceSpent) })
                .Where(candidate => candidate.Cost.HasValue && candidate.Cost.Value > 0 && candidate.Cost.Value <= available)
                .OrderBy(candidate => candidate.Attribute.Ranks)
                .ThenBy(candidate => candidate.Cost.Value)
                .ThenBy(candidate => Array.IndexOf(attributeOrder, candidate.Attribute.Attribute))
                .FirstOrDefault();

            return nextAttribute != null && player.HandleActionRaiseAttribute(nextAttribute.Attribute.Attribute, nextAttribute.Cost.Value);
        }

        private static uint? GetXpToNextBlindRank(IReadOnlyList<uint> rankXpTable, uint ranks, uint experienceSpent)
        {
            var nextRank = ranks + 1;
            if (nextRank >= rankXpTable.Count)
                return null;

            var nextRankXp = rankXpTable[(int)nextRank];
            if (nextRankXp <= experienceSpent)
                return null;

            return nextRankXp - experienceSpent;
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
