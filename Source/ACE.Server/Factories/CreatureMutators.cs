using System;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// DerpACE: Vampiric mob mutator (lifesteals on hit).
    /// </summary>
    public class VampiricMutator : CreatureMutator
    {
        public override string Identifier => "vampiric";
        public override string Name => "Vampiric";
        public override string Description => "Lifesteals a percentage of damage dealt to players.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsVampiricMob;
        public override string NamePrefix => "Vampiric";

        public VampiricMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.VampiricMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.VampiricMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Roll lifesteal % from configured range
            var minPct = Math.Max(0, DerpACEConfig.VampiricLifestealMin);
            var maxPct = Math.Max(minPct, DerpACEConfig.VampiricLifestealMax);
            var pct = ThreadSafeRandom.Next(minPct, maxPct) / 100.0;
            creature.SetProperty(PropertyFloat.VampiricLifestealPct, pct);

            // Boost HP slightly (use biota properties for persistent stat changes)
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.15);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Visual tells: scale and color
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.5f;
            creature.PaletteTemplate = (int)PaletteTemplate.Red;
            creature.Shade = 1.0;
        }
    }

    /// <summary>
    /// DerpACE: Thief mob mutator (steals tradenotes on hit, drops chest on death).
    /// </summary>
    public class ThiefMutator : CreatureMutator
    {
        public override string Identifier => "thieving";
        public override string Name => "Thieving";
        public override string Description => "Steals tradenotes from players and drops a loot chest on death.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsThiefMob;
        public override string NamePrefix => "Thieving";

        public ThiefMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ThiefMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.ThiefMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Boost HP + speed
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.10);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Small speed boost
            var runSkill = creature.GetCreatureSkill(Skill.Run);
            if (runSkill != null && runSkill.Base > 0)
            {
                var runBoost = (uint)(runSkill.Base * 0.05);
                runSkill.Ranks += (ushort)runBoost;
            }
        }
    }

    /// <summary>
    /// DerpACE: Scout mob mutator (increases aggro range and perception).
    /// </summary>
    public class ScoutMutator : CreatureMutator
    {
        public override string Identifier => "scout";
        public override string Name => "Scout";
        public override string Description => "Increases aggro range, perception, and announces player presence.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsScoutMob;
        public override string NamePrefix => "Scout";

        public ScoutMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ScoutMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.ScoutMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Increase visual/aggro range
            if (creature.VisualAwarenessRange.HasValue)
                creature.VisualAwarenessRange *= 1.5f;
            else
                creature.VisualAwarenessRange = 30.0f;

            // Boost attributes via base values
            if (creature.Coordination != null)
            {
                creature.Coordination.StartingValue += 20;
            }
            if (creature.Quickness != null)
            {
                creature.Quickness.StartingValue += 20;
            }

            // Small speed boost
            var runSkill = creature.GetCreatureSkill(Skill.Run);
            if (runSkill != null && runSkill.Base > 0)
            {
                var runBoost = (uint)(runSkill.Base * 0.10);
                runSkill.Ranks += (ushort)runBoost;
            }

            // TODO: Add alert broadcast on player detection (requires AI hook)
        }
    }

    /// <summary>
    /// DerpACE: Simulacrum mob mutator - only applies to CreatureType.Simulacrum creatures.
    /// When applied, causes the simulacrum to copy a random nearby player instead of the first attacker.
    /// </summary>
    public class SimulacrumMutator : CreatureMutator
    {
        public override string Identifier => "simulacrum";
        public override string Name => "Simulacrum";
        public override string Description => "Copies a random nearby player when spawned (only applies to Simulacrum creature type).";
        public override PropertyBool? MutatorFlag => PropertyBool.IsSimulacrumMob;
        public override string NamePrefix => "Simulacrum";

        public SimulacrumMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.SimulacrumMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.SimulacrumMobEnabled;
        }

        public override bool CanApply(Creature creature, int tier)
        {
            if (!base.CanApply(creature, tier)) return false;

            // Only apply to creatures that are already CreatureType.Simulacrum
            return creature.CreatureType == ACE.Entity.Enum.CreatureType.Simulacrum;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Boost HP to compensate for being a player clone
            if (creature.Health.Current > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var hpBoost = (uint)(currentMax * 0.20);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // The PropertyBool.IsSimulacrumMob flag is set by ApplyInternal in the base class.
            // When the creature acquires its first target, TryCopyFromPlayerOrRandom() will check
            // this flag and pick a random nearby player instead of copying the attack target.
        }
    }

    /// <summary>
    /// DerpACE: Nocturnal mob mutator â€” boosts DamageRating + Overpower at spawn.
    /// In random spawn flow, only rolls at night; force-apply (admin) bypasses time-of-day.
    /// </summary>
    public class NocturnalMutator : CreatureMutator
    {
        public override string Identifier => "nocturnal";
        public override string Name => "Nocturnal";
        public override string Description => "Hunts after dark â€” boosted damage and overpower.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsNocturnalMob;
        public override string NamePrefix => "Nocturnal";

        public NocturnalMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.NocturnalMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.NocturnalMobEnabled;
        }

        public override bool CanApply(Creature creature, int tier)
        {
            if (!base.CanApply(creature, tier)) return false;

            // Random-spawn path: only at night. Force-apply uses ForceApply() and bypasses CanApply().
            // Use the same in-game day/night clock that spawners use (GeneratorTimeType.Night).
            return !ACE.Server.Entity.Timers.CurrentInGameTime.IsDay;
        }

        protected override void Apply(Creature creature, int tier)
        {
            creature.DamageRating = (creature.DamageRating ?? 0) + ThreadSafeRandom.Next(1, 50);
            creature.Overpower = (creature.Overpower ?? 0) + ThreadSafeRandom.Next(1, 5);
        }
    }

    /// <summary>
    /// DerpACE: Exploding mob mutator â€” detonates on death casting an elemental ring spell.
    /// Death-side AoE is handled in Creature_Death.cs by checking PropertyBool.IsExplodingMob.
    /// </summary>
    public class ExplodingMutator : CreatureMutator
    {
        public override string Identifier => "exploding";
        public override string Name => "Exploding";
        public override string Description => "Explodes on death, casting an elemental ring spell at nearby players.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsExplodingMob;
        public override string NamePrefix => "Exploding";

        public ExplodingMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ExplodingMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.ExplodingMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Roll a random elemental damage type
            var elements = new[] { DamageType.Fire, DamageType.Cold, DamageType.Acid, DamageType.Electric };
            var element = elements[ThreadSafeRandom.Next(0, elements.Length - 1)];
            creature.SetProperty(PropertyInt.ExplodingMobElement, (int)element);

            // Visual tell: color by element and scale-up
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.45f;
            switch (element)
            {
                case DamageType.Fire:
                    creature.PaletteTemplate = (int)PaletteTemplate.Red;
                    break;
                case DamageType.Cold:
                    creature.PaletteTemplate = (int)PaletteTemplate.Blue;
                    creature.Shade = 0.5;
                    break;
                case DamageType.Acid:
                    creature.PaletteTemplate = (int)PaletteTemplate.Green;
                    creature.Shade = 0.6;
                    break;
                case DamageType.Electric:
                    creature.PaletteTemplate = (int)PaletteTemplate.Yellow;
                    creature.Shade = 0.7;
                    break;
            }
        }
    }

    /// <summary>
    /// DerpACE: Healer mob mutator â€” casts Heal Other on wounded nearby allies,
    /// spends mana, and shows heal notification/animation on the target.
    /// Heartbeat logic is in Creature_Healer.cs.
    /// </summary>
    public class HealerMutator : CreatureMutator
    {
        public override string Identifier => "healer";
        public override string Name => "Healer";
        public override string Description => "Casts Heal Other on wounded allies nearby.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsHealerMob;
        public override string NamePrefix => "Healer";

        public HealerMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.HealerMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.HealerMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Boost mana pool so it has enough to cast repeatedly
            if (creature.Mana != null && creature.Mana.MaxValue > 0)
            {
                var currentMax = creature.Mana.MaxValue;
                var manaBoost = (uint)(currentMax * 0.5);
                creature.Mana.StartingValue += manaBoost;
                creature.Mana.Current = creature.Mana.MaxValue;
            }

            // Visual tell: green tint
            creature.PaletteTemplate = (int)PaletteTemplate.Green;
            creature.Shade = 0.75;
        }
    }

    /// <summary>
    /// DerpACE: Enchanter mob mutator - smaller support caster that wards nearby allies.
    /// </summary>
    public class EnchanterMutator : CreatureMutator
    {
        public override string Identifier => "enchanter";
        public override string Name => "Enchanter";
        public override string Description => "Rotates palette and periodically wards nearby allies with broad protection.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsEnchanterMob;
        public override string NamePrefix => "Enchanted";

        public EnchanterMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.HealerMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers;
        }

        protected override void Apply(Creature creature, int tier)
        {
            creature.ObjScale = Math.Max(0.25f, (creature.ObjScale ?? 1.0f) - 0.124f);
            creature.PaletteTemplate = (int)PaletteTemplate.Purple;
            creature.Shade = 0.6;

            if (creature.Mana != null && creature.Mana.MaxValue > 0)
            {
                var manaBoost = (uint)(creature.Mana.MaxValue * 0.75);
                creature.Mana.StartingValue += manaBoost;
                creature.Mana.Current = creature.Mana.MaxValue;
            }
        }
    }

    /// <summary>
    /// DerpACE: Shaman mob mutator - elemental melee mage with periodic ring attacks.
    /// </summary>
    public class ShamanMutator : CreatureMutator
    {
        public override string Identifier => "shaman";
        public override string Name => "Shaman";
        public override string Description => "Elemental melee mage that casts ring attacks between melee swings.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsShamanMob;
        public override string NamePrefix => "Shamanic";

        public ShamanMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.NecromancerMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers;
        }

        protected override void Apply(Creature creature, int tier)
        {
            var elements = new[] { DamageType.Fire, DamageType.Cold, DamageType.Acid, DamageType.Electric };
            var element = elements[ThreadSafeRandom.Next(0, elements.Length - 1)];
            creature.SetProperty(PropertyInt.ExplodingMobElement, (int)element);

            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.12f;
            switch (element)
            {
                case DamageType.Cold:
                    creature.PaletteTemplate = (int)PaletteTemplate.Blue;
                    creature.Shade = 0.55;
                    creature.Name = $"Frost {creature.Name}";
                    break;
                case DamageType.Acid:
                    creature.PaletteTemplate = (int)PaletteTemplate.Green;
                    creature.Shade = 0.65;
                    creature.Name = $"Acid {creature.Name}";
                    break;
                case DamageType.Electric:
                    creature.PaletteTemplate = (int)PaletteTemplate.Yellow;
                    creature.Shade = 0.75;
                    creature.Name = $"Storm {creature.Name}";
                    break;
                case DamageType.Fire:
                default:
                    creature.PaletteTemplate = (int)PaletteTemplate.Red;
                    creature.Shade = 0.8;
                    creature.Name = $"Flame {creature.Name}";
                    break;
            }

            if (creature.Mana != null && creature.Mana.MaxValue > 0)
            {
                var manaBoost = (uint)(creature.Mana.MaxValue * 0.35);
                creature.Mana.StartingValue += manaBoost;
                creature.Mana.Current = creature.Mana.MaxValue;
            }
        }
    }
    /// <summary>
    /// DerpACE: Tank mob mutator â€” high HP, physical damage reduction, bonus healing received,
    /// and boosted Light Weapons + Shield skills.
    /// </summary>
    public class TankMutator : CreatureMutator
    {
        public override string Identifier => "tank";
        public override string Name => "Tank";
        public override string Description => "High HP, physical damage reduction, bonus healing received, and skilled with light weapons & shields.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsTankMob;
        public override string NamePrefix => "Tank";

        public TankMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.TankMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.TankMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // 250% health boost
            if (creature.Health != null && creature.Health.MaxValue > 0)
            {
                var currentMax = creature.Health.MaxValue;
                var healthMult = Math.Max(1.0f, DerpACEConfig.TankMobHealthMultiplier);
                var hpBoost = (uint)((currentMax * healthMult) - currentMax);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Physical damage resistances (Slash/Pierce/Bludgeon)
            var physReduction = Math.Clamp(DerpACEConfig.TankMobPhysicalReduction, 0.0f, 1.0f);
            creature.SetProperty(PropertyFloat.ResistSlash, physReduction);
            creature.SetProperty(PropertyFloat.ResistPierce, physReduction);
            creature.SetProperty(PropertyFloat.ResistBludgeon, physReduction);

            // Boost Light Weapons and Shield skills
            var skillBonus = Math.Max(0, DerpACEConfig.TankMobSkillBonus);
            var lightWeapons = creature.GetCreatureSkill(Skill.LightWeapons);
            if (lightWeapons.AdvancementClass >= SkillAdvancementClass.Trained)
            {
                lightWeapons.Ranks += (ushort)Math.Min(skillBonus, ushort.MaxValue);
                lightWeapons.InitLevel += (uint)skillBonus;
                creature.Skills[Skill.LightWeapons] = lightWeapons;
            }

            var shield = creature.GetCreatureSkill(Skill.Shield);
            if (shield.AdvancementClass >= SkillAdvancementClass.Trained)
            {
                shield.Ranks += (ushort)Math.Min(skillBonus, ushort.MaxValue);
                shield.InitLevel += (uint)skillBonus;
                creature.Skills[Skill.Shield] = shield;
            }

            // Visual tell: blue tint and larger scale
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.3f;
            creature.PaletteTemplate = (int)PaletteTemplate.Blue;
            creature.Shade = 0.85;
        }
    }

    /// <summary>
    /// DerpACE: Reaper affix â€” death-aspected: bonus melee damage and life-drain on hit.
    /// On-hit lifedrain handled in Player_Combat.TryProcMobModifiers.
    /// </summary>
    public class ReaperMutator : CreatureMutator
    {
        public override string Identifier => "reaper";
        public override string Name => "Reaper";
        public override string Description => "Death-aspected: deals bonus damage and drains health on every hit.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsReaperMob;
        public override string NamePrefix => "Reaping";

        public ReaperMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.ReaperMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.ReaperMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Modest HP boost
            if (creature.Health != null && creature.Health.MaxValue > 0)
            {
                var hpBoost = (uint)(creature.Health.MaxValue * 0.20);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Damage rating bump so even non-modified attacks bite harder
            creature.DamageRating = (creature.DamageRating ?? 0) + 25;

            // Visual tell: gaunt dark-purple, slightly larger
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.25f;
            creature.PaletteTemplate = (int)PaletteTemplate.Purple;
            creature.Shade = 0.9;
        }
    }

    /// <summary>
    /// DerpACE: Necromancer affix â€” applies a nether damage-over-time on hit.
    /// DoT roll handled in Player_Combat.TryProcMobModifiers.
    /// </summary>
    public class NecromancerMutator : CreatureMutator
    {
        public override string Identifier => "necromancer";
        public override string Name => "Necromancer";
        public override string Description => "Curses victims with a lingering nether damage-over-time on hit.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsNecromancerMob;
        public override string NamePrefix => "Necrotic";

        public NecromancerMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.NecromancerMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.NecromancerMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Mana boost (cosmetically tied to its nether casting flavor)
            if (creature.Mana != null && creature.Mana.MaxValue > 0)
            {
                var manaBoost = (uint)(creature.Mana.MaxValue * 0.4);
                creature.Mana.StartingValue += manaBoost;
                creature.Mana.Current = creature.Mana.MaxValue;
            }

            // Improve nether resistance â€” a necromancer shrugs off the same stuff it casts
            creature.SetProperty(PropertyFloat.ResistNether, 0.5f);

            // Visual tell: dark/black tint, slightly larger
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.15f;
            creature.PaletteTemplate = (int)PaletteTemplate.Black;
            creature.Shade = 1.0;
        }
    }

    /// <summary>
    /// DerpACE: Warder affix â€” wards nearby creatures, blocking offensive spells cast against them.
    /// Spell-cast block is enforced in Player_Magic.CreatePlayerSpell.
    /// </summary>
    public class WarderMutator : CreatureMutator
    {
        public override string Identifier => "warder";
        public override string Name => "Warden";
        public override string Description => "Massive protector that absorbs damage for nearby allied creatures and disrupts hostile magic.";
        public override PropertyBool? MutatorFlag => PropertyBool.IsWarderMob;
        public override string NamePrefix => "Warden";

        public WarderMutator()
        {
            MinTier = DerpACEConfig.MobModifierMinTier;
            Chance = DerpACEConfig.WarderMobChance;
            Enabled = DerpACEConfig.EnableMobModifiers && DerpACEConfig.WarderMobEnabled;
        }

        protected override void Apply(Creature creature, int tier)
        {
            // Wardens are meant to be killed first: huge health pool, then defensive skills.
            var magicDef = creature.GetCreatureSkill(Skill.MagicDefense);
            if (magicDef != null && magicDef.AdvancementClass >= SkillAdvancementClass.Trained)
            {
                magicDef.Ranks += 100;
                magicDef.InitLevel += 100;
                creature.Skills[Skill.MagicDefense] = magicDef;
            }

            if (creature.Health != null && creature.Health.MaxValue > 0)
            {
                var hpBoost = (uint)(creature.Health.MaxValue * 4.0);
                creature.Health.StartingValue += hpBoost;
                creature.Health.Current = creature.Health.MaxValue;
            }

            // Mana boost so the visual feels supported
            if (creature.Mana != null && creature.Mana.MaxValue > 0)
            {
                var manaBoost = (uint)(creature.Mana.MaxValue * 0.5);
                creature.Mana.StartingValue += manaBoost;
                creature.Mana.Current = creature.Mana.MaxValue;
            }

            // Visual tell: bright blue and slightly larger â€” telegraph that they buff allies
            creature.ObjScale = (creature.ObjScale ?? 1.0f) + 0.45f;
            creature.PaletteTemplate = (int)PaletteTemplate.Blue;
            creature.Shade = 0.4;
        }
    }

}

