using System.Collections.Generic;

using log4net;

using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;

namespace ACE.Server.DerpAce
{
    /// <summary>
    /// DerpACE: Hardcoded weenie definitions injected into the world database cache at startup.
    /// This lets us ship custom items (e.g. the Aetherial Quiver, WCID 2000600) without requiring
    /// the server operator to import a SQL file. If the same WCID is already in the DB it will be
    /// overwritten by these definitions so the canonical version always wins.
    /// </summary>
    public static class HardcodedWeenies
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static void RegisterAll()
        {
            try
            {
                DatabaseManager.World.SetCachedWeenie(BuildAetherialQuiver());
                DatabaseManager.World.SetCachedWeenie(BuildHandCrossbowBolts());
                DatabaseManager.World.SetCachedWeenie(BuildSausageMcBuffin());

                log.Info("DerpACE: Hardcoded weenies registered.");
            }
            catch (System.Exception ex)
            {
                log.Error("DerpACE: Failed to register hardcoded weenies.", ex);
            }
        }

        /// <summary>
        /// Aetherial Quiver (WCID 2000600) — the universal infinite ammo.
        /// Runtime behavior lives in <see cref="WorldObjects.UniversalAmmunition"/>; this is just
        /// the weenie shell so the factory can instantiate it on demand.
        /// </summary>
        private static Weenie BuildAetherialQuiver()
        {
            var w = new Weenie
            {
                WeenieClassId = 2000600,
                ClassName     = "ace2000600-enigmaticammo",
                WeenieType    = WeenieType.Ammunition,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            // --- ints ---
            w.PropertiesInt[PropertyInt.ItemType]            = (int)ItemType.MissileWeapon;
            w.PropertiesInt[PropertyInt.PaletteTemplate]     = 20; // Silver
            w.PropertiesInt[PropertyInt.EncumbranceVal]      = 1;
            w.PropertiesInt[PropertyInt.Mass]                = 2;
            w.PropertiesInt[PropertyInt.ValidLocations]      = (int)EquipMask.MissileAmmo;
            w.PropertiesInt[PropertyInt.MaxStackSize]        = 3000;
            w.PropertiesInt[PropertyInt.StackSize]           = 1;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance]= 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]       = 2;
            w.PropertiesInt[PropertyInt.StackUnitValue]      = 100;
            w.PropertiesInt[PropertyInt.ItemUseable]         = (int)Usable.No;
            w.PropertiesInt[PropertyInt.UiEffects]           = (int)UiEffects.BoostStamina;
            w.PropertiesInt[PropertyInt.Value]               = 100;
            w.PropertiesInt[PropertyInt.Damage]              = 39;
            w.PropertiesInt[PropertyInt.DamageType]          = (int)DamageType.Base;
            // Advertise one exact launcher family so the client accepts the ammo header.
            // UniversalAmmunition stamps the exact launcher AmmoType at equip time.
            w.PropertiesInt[PropertyInt.AmmoType]            = (int)AmmoType.Arrow;
            w.PropertiesInt[PropertyInt.CombatUse]           = (int)CombatUse.Ammo;
            w.PropertiesInt[PropertyInt.PhysicsState]        = 132116;
            w.PropertiesInt[PropertyInt.HookPlacement]       = (int)Placement.Hook;
            w.PropertiesInt[PropertyInt.HookType]            = (int)HookType.Wall;
            w.PropertiesInt[PropertyInt.WieldRequirements]   = (int)WieldRequirement.Training;
            w.PropertiesInt[PropertyInt.WieldSkillType]      = (int)Skill.Fletching;
            w.PropertiesInt[PropertyInt.WieldDifficulty]     = 3;
            w.PropertiesInt[PropertyInt.WieldRequirements2]  = (int)WieldRequirement.RawSkill;
            w.PropertiesInt[PropertyInt.WieldSkillType2]     = (int)Skill.Fletching;
            w.PropertiesInt[PropertyInt.WieldDifficulty2]    = 375;
            w.PropertiesInt[PropertyInt.WieldRequirements3]  = (int)WieldRequirement.RawSkill;
            w.PropertiesInt[PropertyInt.WieldSkillType3]     = (int)Skill.MissileWeapons;
            w.PropertiesInt[PropertyInt.WieldDifficulty3]    = 300;

            // --- bools ---
            w.PropertiesBool[PropertyBool.Inelastic]  = true;
            w.PropertiesBool[PropertyBool.IsSellable] = false;

            // --- floats ---
            w.PropertiesFloat[PropertyFloat.WeaponLength]    = 0;
            w.PropertiesFloat[PropertyFloat.DamageVariance]  = 0.2;
            w.PropertiesFloat[PropertyFloat.MaximumVelocity] = 0;
            w.PropertiesFloat[PropertyFloat.WeaponDefense]   = 1;
            w.PropertiesFloat[PropertyFloat.WeaponOffense]   = 1;
            w.PropertiesFloat[PropertyFloat.DamageMod]       = 1;
            w.PropertiesFloat[PropertyFloat.Friction]        = 1;
            w.PropertiesFloat[PropertyFloat.Elasticity]      = 0;

            // --- strings ---
            w.PropertiesString[PropertyString.Name]     = "Aetherial Quiver";
            w.PropertiesString[PropertyString.Use]      = "Nock the quiver to any bow, crossbow, or atlatl. It shapes itself to match \u2014 and never empties.";
            w.PropertiesString[PropertyString.LongDesc] = "A self-replenishing bundle of crystallized aether, bound by Empyrean artifice. When drawn against a missile weapon, the aether condenses into a perfect imitation of the proper ammunition, drinking in the weapon's elemental attunement as it flies. The shaft dissolves on impact, leaving only the wound.";

            // --- DIDs ---
            w.PropertiesDID[PropertyDataId.Setup]              = 0x02001A87;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 0x20000014;
            w.PropertiesDID[PropertyDataId.PaletteBase]        = 0x04000BEF;
            w.PropertiesDID[PropertyDataId.ClothingBase]       = 0x10000352;
            w.PropertiesDID[PropertyDataId.Icon]               = 0x06006FC7;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 0x3400002B;

            return w;
        }

        /// <summary>
        /// Sausage McBuffin (WCID 2000500) — the N00B Buffer NPC.
        /// Buff casting behavior is hardcoded in <see cref="WorldObjects.SausageMcBuffin"/>;
        /// this is the creature weenie shell so it can be spawned and used in the world without SQL.
        /// </summary>
        /// <summary>
        /// Handcrossbow Bolts (WCID 2000601) - half-strength prismatic bolt ammo for Hand Crossbow mutators.
        /// </summary>
        private static Weenie BuildHandCrossbowBolts()
        {
            var w = new Weenie
            {
                WeenieClassId = 2000601,
                ClassName     = "ace2000601-handcrossbowbolts",
                WeenieType    = WeenieType.Ammunition,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.MissileWeapon;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = 20;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 1;
            w.PropertiesInt[PropertyInt.Mass]                 = 1;
            w.PropertiesInt[PropertyInt.ValidLocations]       = (int)EquipMask.MissileAmmo;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = 3000;
            w.PropertiesInt[PropertyInt.StackSize]            = 100;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 1;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 25;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.No;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.BoostStamina;
            w.PropertiesInt[PropertyInt.Value]                = 25;
            w.PropertiesInt[PropertyInt.Damage]               = 20;
            w.PropertiesInt[PropertyInt.DamageType]           = (int)DamageType.Base;
            w.PropertiesInt[PropertyInt.AmmoType]             = (int)AmmoType.Bolt;
            w.PropertiesInt[PropertyInt.CombatUse]            = (int)CombatUse.Ammo;
            w.PropertiesInt[PropertyInt.PhysicsState]         = 132116;
            w.PropertiesInt[PropertyInt.WieldRequirements]    = (int)WieldRequirement.Training;
            w.PropertiesInt[PropertyInt.WieldSkillType]       = (int)Skill.MissileWeapons;
            w.PropertiesInt[PropertyInt.WieldDifficulty]      = (int)SkillAdvancementClass.Specialized;
            w.PropertiesInt[PropertyInt.WieldRequirements2]   = (int)WieldRequirement.Training;
            w.PropertiesInt[PropertyInt.WieldSkillType2]      = (int)Skill.DualWield;
            w.PropertiesInt[PropertyInt.WieldDifficulty2]     = (int)SkillAdvancementClass.Specialized;

            w.PropertiesBool[PropertyBool.Inelastic] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale]     = 0.5;
            w.PropertiesFloat[PropertyFloat.WeaponLength]     = 0;
            w.PropertiesFloat[PropertyFloat.DamageVariance]   = 0.2;
            w.PropertiesFloat[PropertyFloat.MaximumVelocity]  = 0;
            w.PropertiesFloat[PropertyFloat.WeaponDefense]    = 1;
            w.PropertiesFloat[PropertyFloat.WeaponOffense]    = 1;
            w.PropertiesFloat[PropertyFloat.DamageMod]        = 1;
            w.PropertiesFloat[PropertyFloat.Friction]         = 1;
            w.PropertiesFloat[PropertyFloat.Elasticity]       = 0;

            w.PropertiesString[PropertyString.Name]     = "Handcrossbow Bolts";
            w.PropertiesString[PropertyString.Use]      = "Nock these tiny bolts to hand crossbows.";
            w.PropertiesString[PropertyString.LongDesc] = "Compact prismatic quarrels balanced for hand crossbows. They strike at roughly half deadly-prismatic force and require specialized Missile Weapons and specialized Dual Wield.";

            w.PropertiesDID[PropertyDataId.Setup]              = 0x02001A87;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 0x20000014;
            w.PropertiesDID[PropertyDataId.PaletteBase]        = 0x04000BEF;
            w.PropertiesDID[PropertyDataId.ClothingBase]       = 0x10000352;
            w.PropertiesDID[PropertyDataId.Icon]               = 0x06006FC7;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 0x3400002B;

            return w;
        }

        private static Weenie BuildSausageMcBuffin()
        {
            var w = new Weenie
            {
                WeenieClassId = 2000500,
                ClassName     = "sausageMcbuffin",
                WeenieType    = WeenieType.Creature,

                PropertiesInt         = new Dictionary<PropertyInt, int>(),
                PropertiesBool        = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat       = new Dictionary<PropertyFloat, double>(),
                PropertiesString      = new Dictionary<PropertyString, string>(),
                PropertiesDID         = new Dictionary<PropertyDataId, uint>(),
                PropertiesAttribute   = new Dictionary<PropertyAttribute, PropertiesAttribute>(),
                PropertiesAttribute2nd= new Dictionary<PropertyAttribute2nd, PropertiesAttribute2nd>(),
                PropertiesSkill       = new Dictionary<Skill, PropertiesSkill>(),
                PropertiesBodyPart    = new Dictionary<CombatBodyPart, PropertiesBodyPart>(),
            };

            // --- ints ---
            w.PropertiesInt[PropertyInt.ItemType]                            = (int)ItemType.Creature;
            w.PropertiesInt[PropertyInt.CreatureType]                        = 13;       // Golem
            w.PropertiesInt[PropertyInt.PaletteTemplate]                     = 61;       // White
            w.PropertiesInt[PropertyInt.ItemsCapacity]                       = -1;
            w.PropertiesInt[PropertyInt.ContainersCapacity]                  = -1;
            w.PropertiesInt[PropertyInt.Mass]                                = 120;
            w.PropertiesInt[PropertyInt.ItemUseable]                         = (int)Usable.Remote;
            w.PropertiesInt[PropertyInt.Level]                               = 710;
            w.PropertiesInt[PropertyInt.ArmorType]                           = 0;        // None
            w.PropertiesInt[PropertyInt.PhysicsState]                        = 6292504;
            w.PropertiesInt[PropertyInt.RadarBlipColor]                      = 8;        // Yellow
            w.PropertiesInt[PropertyInt.ShowableOnRadar]                     = 4;        // ShowAlways
            w.PropertiesInt[PropertyInt.PlayerKillerStatus]                  = 16;       // RubberGlue
            w.PropertiesInt[PropertyInt.XpOverride]                          = 757504;
            w.PropertiesInt[PropertyInt.AugmentationIncreasedSpellDuration]  = 10;

            // --- bools ---
            w.PropertiesBool[PropertyBool.Stuck]                         = true;
            w.PropertiesBool[PropertyBool.AllowGive]                     = true;
            w.PropertiesBool[PropertyBool.IgnoreCollisions]             = true;
            w.PropertiesBool[PropertyBool.ReportCollisions]            = true;
            w.PropertiesBool[PropertyBool.Ethereal]                     = false;
            w.PropertiesBool[PropertyBool.GravityStatus]                = true;
            w.PropertiesBool[PropertyBool.Attackable]                   = false;
            w.PropertiesBool[PropertyBool.ReportCollisionsAsEnvironment]= true;
            w.PropertiesBool[PropertyBool.AllowEdgeSlide]               = true;
            w.PropertiesBool[PropertyBool.AiImmobile]                   = true;

            // --- floats ---
            w.PropertiesFloat[PropertyFloat.HeartbeatInterval]   = 5;
            w.PropertiesFloat[PropertyFloat.HeartbeatTimestamp]  = 0;
            w.PropertiesFloat[PropertyFloat.HealthRate]          = 1.1;
            w.PropertiesFloat[PropertyFloat.StaminaRate]         = 0.5;
            w.PropertiesFloat[PropertyFloat.ManaRate]            = 2;
            w.PropertiesFloat[PropertyFloat.Shade]               = 0.5;
            w.PropertiesFloat[PropertyFloat.ArmorModVsSlash]     = 0.79;
            w.PropertiesFloat[PropertyFloat.ArmorModVsPierce]    = 0.79;
            w.PropertiesFloat[PropertyFloat.ArmorModVsBludgeon]  = 0.8;
            w.PropertiesFloat[PropertyFloat.ArmorModVsCold]      = 1;
            w.PropertiesFloat[PropertyFloat.ArmorModVsFire]      = 1;
            w.PropertiesFloat[PropertyFloat.ArmorModVsAcid]      = 1;
            w.PropertiesFloat[PropertyFloat.ArmorModVsElectric]  = 1;
            w.PropertiesFloat[PropertyFloat.DefaultScale]        = 0.75;
            w.PropertiesFloat[PropertyFloat.UseRadius]           = 3;
            w.PropertiesFloat[PropertyFloat.ResistSlash]         = 1;
            w.PropertiesFloat[PropertyFloat.ResistPierce]        = 1;
            w.PropertiesFloat[PropertyFloat.ResistBludgeon]      = 1;
            w.PropertiesFloat[PropertyFloat.ResistFire]          = 1;
            w.PropertiesFloat[PropertyFloat.ResistCold]          = 1;
            w.PropertiesFloat[PropertyFloat.ResistAcid]          = 1;
            w.PropertiesFloat[PropertyFloat.ResistElectric]      = 1;
            w.PropertiesFloat[PropertyFloat.ResistHealthBoost]   = 1;
            w.PropertiesFloat[PropertyFloat.ResistStaminaDrain]  = 1;
            w.PropertiesFloat[PropertyFloat.ResistStaminaBoost]  = 1;
            w.PropertiesFloat[PropertyFloat.ResistManaDrain]     = 1;
            w.PropertiesFloat[PropertyFloat.ResistManaBoost]     = 1;
            w.PropertiesFloat[PropertyFloat.ObviousRadarRange]   = 10;
            w.PropertiesFloat[PropertyFloat.ResistHealthDrain]   = 1;

            // --- strings ---
            w.PropertiesString[PropertyString.Name]     = "Sausage McBuffin";
            w.PropertiesString[PropertyString.Template] = "N00B Buffer";

            // --- DIDs ---
            w.PropertiesDID[PropertyDataId.Setup]              = 0x020016D2;
            w.PropertiesDID[PropertyDataId.MotionTable]        = 0x09000081;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 0x20000099;
            w.PropertiesDID[PropertyDataId.CombatTable]        = 0x30000008;
            w.PropertiesDID[PropertyDataId.PaletteBase]        = 0x04000F46;
            w.PropertiesDID[PropertyDataId.ClothingBase]       = 0x1000020E;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 0x3400005E;

            // --- attributes ---
            w.PropertiesAttribute[PropertyAttribute.Strength]     = new PropertiesAttribute { InitLevel = 980 };
            w.PropertiesAttribute[PropertyAttribute.Endurance]    = new PropertiesAttribute { InitLevel = 940 };
            w.PropertiesAttribute[PropertyAttribute.Quickness]    = new PropertiesAttribute { InitLevel = 850 };
            w.PropertiesAttribute[PropertyAttribute.Coordination] = new PropertiesAttribute { InitLevel = 930 };
            w.PropertiesAttribute[PropertyAttribute.Focus]        = new PropertiesAttribute { InitLevel = 850 };
            w.PropertiesAttribute[PropertyAttribute.Self]         = new PropertiesAttribute { InitLevel = 885 };

            // --- secondary attributes ---
            w.PropertiesAttribute2nd[PropertyAttribute2nd.MaxHealth]  = new PropertiesAttribute2nd { CurrentLevel = 470 };
            w.PropertiesAttribute2nd[PropertyAttribute2nd.MaxStamina] = new PropertiesAttribute2nd { CurrentLevel = 940 };
            w.PropertiesAttribute2nd[PropertyAttribute2nd.MaxMana]    = new PropertiesAttribute2nd { CurrentLevel = 885 };

            // --- skills (SAC 3 = Specialized) ---
            w.PropertiesSkill[Skill.ArcaneLore]          = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 200 };
            w.PropertiesSkill[Skill.ManaConversion]      = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 200 };
            w.PropertiesSkill[Skill.Jump]                = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 200 };
            w.PropertiesSkill[Skill.Run]                 = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 200 };
            w.PropertiesSkill[Skill.CreatureEnchantment] = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 900 };
            w.PropertiesSkill[Skill.ItemEnchantment]     = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 900 };
            w.PropertiesSkill[Skill.LifeMagic]           = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 900 };
            w.PropertiesSkill[Skill.WarMagic]            = new PropertiesSkill { SAC = SkillAdvancementClass.Specialized, InitLevel = 900 };

            // --- body parts (full coverage, identical armor profile) ---
            for (var i = 0; i <= 8; i++)
            {
                w.PropertiesBodyPart[(CombatBodyPart)i] = new PropertiesBodyPart
                {
                    DType           = DamageType.Bludgeon,
                    BaseArmor       = 200,
                    ArmorVsSlash    = 100,
                    ArmorVsPierce   = 100,
                    ArmorVsBludgeon = 100,
                    ArmorVsCold     = 100,
                    ArmorVsFire     = 100,
                    ArmorVsAcid     = 100,
                    ArmorVsElectric = 100,
                };
            }

            return w;
        }
    }
}
