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

        public const uint NomadSurvivalTomeWeenieClassId = 2000611;
        public const uint DerptideIntroBookWeenieClassId = 2000612;
        public const uint IronmanGuideBookWeenieClassId = 2000613;
        public const uint IronmanPathwardenChestWeenieClassId = 3238931;
        public const uint NomadPathwardenChestWeenieClassId = 2000615;
        public const uint IronmanSupplyKeyWeenieClassId = 3238934;
        public const uint NomadSupplyKeyWeenieClassId = 2000616;
        public const uint DrunkenEventBeerWeenieClassId = 2000617;
        public const uint HorriblyForgedDerpCoinWeenieClassId = 2000618;

        private const uint MortarAndPestleWeenieClassId = 4751;
        private const uint PowderedMalachiteWeenieClassId = 8321;
        private const uint TomeWeenieClassId = 9092;
        private const uint ArcanePedestalWeenieClassId = 11930;
        private const uint BrownSackWeenieClassId = 166;
        private const uint SmallBeerWeenieClassId = 2469;
        private const uint MajorShiveringStoneWeenieClassId = 6123;

        public static void RegisterAll()
        {
            try
            {
                DatabaseManager.World.SetCachedWeenie(BuildAetherialQuiver());
                DatabaseManager.World.SetCachedWeenie(BuildHandCrossbowBolts());
                DatabaseManager.World.SetCachedWeenie(BuildDormantSlayerGem());
                DatabaseManager.World.SetCachedWeenie(BuildChargedSlayerGem());
                DatabaseManager.World.SetCachedWeenie(BuildSpellFocus());
                DatabaseManager.World.SetCachedWeenie(BuildNomadRune());
                DatabaseManager.World.SetCachedWeenie(BuildNomadRunePouch());
                DatabaseManager.World.SetCachedWeenie(BuildNomadRuneMergeTool());
                DatabaseManager.World.SetCachedWeenie(BuildNomadRitualRune());
                DatabaseManager.World.SetCachedWeenie(BuildBoneGrinder());
                DatabaseManager.World.SetCachedWeenie(BuildScavengersMortar());
                DatabaseManager.World.SetCachedWeenie(BuildScavengersHexdust());
                DatabaseManager.World.SetCachedWeenie(BuildNomadSurvivalTome());
                DatabaseManager.World.SetCachedWeenie(BuildDerptideIntroBook());
                DatabaseManager.World.SetCachedWeenie(BuildIronmanGuideBook());
                DatabaseManager.World.SetCachedWeenie(BuildIronmanPathwardenChest());
                DatabaseManager.World.SetCachedWeenie(BuildIronmanSupplyKey());
                DatabaseManager.World.SetCachedWeenie(BuildNomadSupplyKey());
                DatabaseManager.World.SetCachedWeenie(BuildNomadPathwardenChest());
                DatabaseManager.World.SetCachedWeenie(BuildDrunkenEventBeer());
                DatabaseManager.World.SetCachedWeenie(BuildHorriblyForgedDerpCoin());
                DatabaseManager.World.SetCachedWeenie(BuildFlutterStone());
                DatabaseManager.World.SetCachedWeenie(BuildSausageMcBuffin());

                log.Info("DerpACE: Hardcoded weenies registered.");
            }
            catch (System.Exception ex)
            {
                log.Error("DerpACE: Failed to register hardcoded weenies.", ex);
            }
        }

        private static void CopySetupAndIcon(Weenie target, uint sourceWeenieClassId)
        {
            var source = DatabaseManager.World.GetCachedWeenie(sourceWeenieClassId);

            if (source?.PropertiesDID == null)
            {
                log.Warn($"DerpACE: Could not copy setup/icon from source WCID {sourceWeenieClassId}; using hardcoded fallback visuals for WCID {target.WeenieClassId}.");
                return;
            }

            if (source.PropertiesDID.TryGetValue(PropertyDataId.Setup, out var setup))
                target.PropertiesDID[PropertyDataId.Setup] = setup;

            if (source.PropertiesDID.TryGetValue(PropertyDataId.Icon, out var icon))
                target.PropertiesDID[PropertyDataId.Icon] = icon;
        }

        /// <summary>
        /// Aetherial Quiver (WCID 2000600) - the universal infinite ammo.
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
        /// Sausage McBuffin (WCID 2000500) - the N00B Buffer NPC.
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

        private static Weenie BuildDormantSlayerGem()
        {
            var w = BuildSlayerGemShell(WorldObjects.SlayerGem.DormantWeenieClassId, "ace2000602-slayergem", "Dormant Slayer Gem");

            w.PropertiesInt[PropertyInt.ItemUseable] = (int)Usable.No;
            w.PropertiesInt[PropertyInt.TargetType] = (int)ItemType.None;
            w.PropertiesBool[PropertyBool.IsChargedSlayerGem] = false;
            w.PropertiesString[PropertyString.LongDesc] = "A dormant slayer gem. When it drops, it attunes to a random creature type and gathers power from matching kills while carried in your inventory.";

            return w;
        }

        private static Weenie BuildChargedSlayerGem()
        {
            var w = BuildSlayerGemShell(WorldObjects.SlayerGem.ChargedWeenieClassId, "ace2000603-chargedslayergem", "Charged Slayer Gem");

            w.PropertiesInt[PropertyInt.ItemUseable] = (int)Usable.SourceContainedTargetContained;
            w.PropertiesInt[PropertyInt.TargetType] = (int)ItemType.WeaponOrCaster;
            w.PropertiesBool[PropertyBool.IsChargedSlayerGem] = true;
            w.PropertiesString[PropertyString.LongDesc] = "A charged slayer gem. Use it on a fully tinkered weapon, wand, or missile weapon to add its creature slayer. The ritual may destroy the target item.";

            return w;
        }

        private static Weenie BuildSpellFocus()
        {
            var w = new Weenie
            {
                WeenieClassId = 2000604,
                ClassName     = "ace2000604-spellfocus",
                WeenieType    = WeenieType.Generic,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Armor;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 20;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 100;
            w.PropertiesInt[PropertyInt.Mass]            = 100;
            w.PropertiesInt[PropertyInt.Value]           = 15000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.SourceContainedTargetContained;
            w.PropertiesInt[PropertyInt.TargetType]      = (int)(ItemType.WeaponOrCaster | ItemType.MagicWieldable);
            w.PropertiesInt[PropertyInt.ValidLocations]  = (int)EquipMask.Shield;
            w.PropertiesInt[PropertyInt.CombatUse]       = (int)CombatUse.Shield;
            w.PropertiesInt[PropertyInt.ArmorLevel]      = 10;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.Magical;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;
            w.PropertiesInt[PropertyInt.Placement]       = (int)Placement.Resting;

            w.PropertiesBool[PropertyBool.IsSpellFocus] = true;
            w.PropertiesBool[PropertyBool.IsSellable]   = false;
            w.PropertiesBool[PropertyBool.Inelastic]    = true;
            w.PropertiesBool[PropertyBool.DestroyOnSell]= true;

            w.PropertiesFloat[PropertyFloat.ArmorModVsSlash]    = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsPierce]   = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsBludgeon] = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsCold]     = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsFire]     = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsAcid]     = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsElectric] = 1.0;
            w.PropertiesFloat[PropertyFloat.ArmorModVsNether]   = 1.0;
            w.PropertiesFloat[PropertyFloat.DefaultScale]       = 0.6;
            w.PropertiesFloat[PropertyFloat.HeartbeatInterval]  = 5.0;
            w.PropertiesFloat[PropertyFloat.ManaRate]           = -0.033;
            w.PropertiesFloat[PropertyFloat.Shade]              = 1.0;
            w.PropertiesFloat[PropertyFloat.WeaponDefense]      = 1.12;
            w.PropertiesFloat[PropertyFloat.Translucency]       = 1.0;

            w.PropertiesString[PropertyString.Name]     = "Unattuned Spell Focus";
            w.PropertiesString[PropertyString.Use]      = "Use an elemental Atlan stone on the focus to attune it. Use armor upgrade kits to improve its magical armor. Use it on a weapon or caster to copy that item's appearance.";
            w.PropertiesString[PropertyString.LongDesc] = "A shield-slot spell focus for specialized War, Life, or Void mages. It may be used with a caster or caster staff, or with a Battlemage Helm and an eligible Light Weapon. Battlemage Helms also support one-handed caster-and-focus setups. Attune it with a Major elemental Atlan Stone or Black Fire Atlan Stone. Armor Upgrade Kits improve only its magical armor; base armor remains 10.";

            w.PropertiesDID[PropertyDataId.Setup]              = 33558442;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 536870932;
            w.PropertiesDID[PropertyDataId.Icon]               = 100674848;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 872415275;
            w.PropertiesDID[PropertyDataId.UseUserAnimation]   = 1073742049;
            w.PropertiesDID[PropertyDataId.MutateFilter]       = 234881046;

            return w;
        }

        private static Weenie BuildSlayerGemShell(uint wcid, string className, string name)
        {
            var w = new Weenie
            {
                WeenieClassId = wcid,
                ClassName     = className,
                WeenieType    = WeenieType.Gem,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
                PropertiesInt64  = new Dictionary<PropertyInt64, long>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Gem;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 32;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 50;
            w.PropertiesInt[PropertyInt.Mass]            = 50;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.Value]           = 100000;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.Magical;
            w.PropertiesInt[PropertyInt.ItemMaxLevel]    = WorldObjects.SlayerGem.SlayerMaxLevel;
            w.PropertiesInt[PropertyInt.ItemXpStyle]     = (int)ItemXpStyle.Fixed;

            w.PropertiesInt64[PropertyInt64.ItemBaseXp]  = WorldObjects.SlayerGem.BaseXp;
            w.PropertiesInt64[PropertyInt64.ItemTotalXp] = 0;

            w.PropertiesBool[PropertyBool.IsSlayerGem] = true;
            w.PropertiesBool[PropertyBool.IsSellable]  = false;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.5;

            w.PropertiesString[PropertyString.Name] = name;
            w.PropertiesString[PropertyString.Use]  = "Use this gem on a fully tinkered weapon, wand, or missile weapon.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;

            return w;
        }

        private static Weenie BuildNomadRune()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.NomadRune.NomadRuneWeenieClassId,
                ClassName     = "ace2000605-nomadrune",
                WeenieType    = WeenieType.Gem,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Gem;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 10;
            w.PropertiesInt[PropertyInt.Mass]            = 10;
            w.PropertiesInt[PropertyInt.Value]           = 5000;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = WorldObjects.NomadRune.NomadRuneUses;
            w.PropertiesInt[PropertyInt.StackSize]       = WorldObjects.NomadRune.NomadRuneUses;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]   = 1;
            w.PropertiesInt[PropertyInt.StackUnitValue]  = 500;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.Contained;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.Magical;

            w.PropertiesBool[PropertyBool.IsSellable]   = false;
            w.PropertiesBool[PropertyBool.Inelastic]    = true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.45;

            w.PropertiesString[PropertyString.Name]     = "Nomad Rune";
            w.PropertiesString[PropertyString.Use]      = "Use this rune to release its stored self-buff.";
            w.PropertiesString[PropertyString.LongDesc] = "A scavenged spell-rune carried by Ironman Nomads. It holds one creature or life self-buff, then crumbles when used.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;

            return w;
        }

        private static Weenie BuildNomadRunePouch()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.NomadRunePouch.NomadRunePouchWeenieClassId,
                ClassName     = "ace2000606-nomadrunepouch",
                WeenieType    = WeenieType.Container,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]           = (int)ItemType.Container;
            w.PropertiesInt[PropertyInt.PaletteTemplate]    = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]     = 20;
            w.PropertiesInt[PropertyInt.Mass]               = 20;
            w.PropertiesInt[PropertyInt.Value]              = 0;
            w.PropertiesInt[PropertyInt.ItemsCapacity]      = 100;
            w.PropertiesInt[PropertyInt.ContainersCapacity] = 0;
            w.PropertiesInt[PropertyInt.ItemUseable]        = (int)Usable.Contained;
            w.PropertiesInt[PropertyInt.UiEffects]          = (int)UiEffects.Magical;
            w.PropertiesInt[PropertyInt.MaxStackSize]       = 1;
            w.PropertiesInt[PropertyInt.StackSize]          = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]       = 1044;
            w.PropertiesInt[PropertyInt.Placement]          = (int)Placement.Resting;

            w.PropertiesBool[PropertyBool.IsSellable]    = false;
            w.PropertiesBool[PropertyBool.Inelastic]     = true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;
            w.PropertiesBool[PropertyBool.DestroyOnSell] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.6;

            w.PropertiesString[PropertyString.Name]     = "Nomad Rune Pouch";
            w.PropertiesString[PropertyString.Use]      = "Open this pouch to store Nomad Runes.";
            w.PropertiesString[PropertyString.LongDesc] = "A weathered pouch that only accepts Nomad Runes. Ironman Nomads use it to keep scavenged spell-runes sorted for later rituals.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, BrownSackWeenieClassId);

            return w;
        }

        private static Weenie BuildNomadRuneMergeTool()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.NomadRuneMergeTool.NomadRuneMergeToolWeenieClassId,
                ClassName     = "ace2000607-nomadrunemergingtool",
                WeenieType    = WeenieType.Generic,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Misc;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 50;
            w.PropertiesInt[PropertyInt.Mass]            = 50;
            w.PropertiesInt[PropertyInt.Value]           = 25000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.SourceContainedTargetContained;
            w.PropertiesInt[PropertyInt.TargetType]      = (int)ItemType.Gem;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)(UiEffects.Magical | UiEffects.Nether);
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;

            w.PropertiesBool[PropertyBool.IsSellable] = false;
            w.PropertiesBool[PropertyBool.Inelastic]  = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.6;

            w.PropertiesString[PropertyString.Name]     = "Nomad Rune Loom";
            w.PropertiesString[PropertyString.Use]      = "Use this on a full 10-use Nomad Rune to weave it into a 5-use ritual rune for that spell school.";
            w.PropertiesString[PropertyString.LongDesc] = "A small ritual frame for binding scattered Nomad Rune power into a school-wide buff rune. Use it on a full 10-use Nomad Rune.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, ArcanePedestalWeenieClassId);

            return w;
        }

        private static Weenie BuildNomadRitualRune()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.NomadRitualRune.NomadRitualRuneWeenieClassId,
                ClassName     = "ace2000608-nomadritualrune",
                WeenieType    = WeenieType.Gem,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Gem;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 25;
            w.PropertiesInt[PropertyInt.Mass]                 = 25;
            w.PropertiesInt[PropertyInt.Value]                = 25000;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = WorldObjects.NomadRitualRune.RitualRuneUses;
            w.PropertiesInt[PropertyInt.StackSize]            = WorldObjects.NomadRitualRune.RitualRuneUses;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 5;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 5;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 5000;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.Contained;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.Magical;

            w.PropertiesBool[PropertyBool.IsSellable]    = false;
            w.PropertiesBool[PropertyBool.Inelastic]     = true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.45;

            w.PropertiesString[PropertyString.Name]     = "Nomad Ritual Rune";
            w.PropertiesString[PropertyString.Use]      = "Use this rune to release a package of Nomad school buffs.";
            w.PropertiesString[PropertyString.LongDesc] = "A bound Nomad rune that casts a full school package of buffs and loses one charge.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;

            return w;
        }

        private static Weenie BuildBoneGrinder()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.ScavengersHexdust.BoneGrinderWeenieClassId,
                ClassName     = "ace2000619-bonegrinder",
                WeenieType    = WeenieType.Generic,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Misc;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 75;
            w.PropertiesInt[PropertyInt.Mass]            = 75;
            w.PropertiesInt[PropertyInt.Value]           = 5000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.SourceContainedTargetRemote;
            w.PropertiesInt[PropertyInt.TargetType]      = (int)ItemType.Container;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.BoostStamina;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;

            w.PropertiesBool[PropertyBool.IsSellable]    = false;
            w.PropertiesBool[PropertyBool.Inelastic]     = true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.6;

            w.PropertiesString[PropertyString.Name]     = "Bone Grinder";
            w.PropertiesString[PropertyString.Use]      = "Use this on a fresh monster corpse to begin or continue the Scavenger's Mortar trial.";
            w.PropertiesString[PropertyString.LongDesc] = "A rough mortar and pestle kept for the first ugly lesson of Nomad fieldcraft. Grind fifteen named bones with it, and the road may trade up.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, MortarAndPestleWeenieClassId);

            return w;
        }
        private static Weenie BuildScavengersMortar()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.ScavengersHexdust.ScavengersMortarWeenieClassId,
                ClassName     = "ace2000609-scavengersmortar",
                WeenieType    = WeenieType.Generic,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Misc;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 75;
            w.PropertiesInt[PropertyInt.Mass]            = 75;
            w.PropertiesInt[PropertyInt.Value]           = 15000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.SourceContainedTargetRemote;
            w.PropertiesInt[PropertyInt.TargetType]      = (int)ItemType.Container;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.BoostStamina;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;

            w.PropertiesBool[PropertyBool.IsSellable] = false;
            w.PropertiesBool[PropertyBool.Inelastic]  = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.6;

            w.PropertiesString[PropertyString.Name]     = "Scavenger's Mortar";
            w.PropertiesString[PropertyString.Use]      = "Use this on a monster corpse to grind Scavenger's Hexdust. Requires trained Assess Creature and an Ironman Nomad's fieldcraft.";
            w.PropertiesString[PropertyString.LongDesc] = "A chipped cup, a blunt stone, and a lifetime of ugly lessons. Nomads use it to grind scraps from fresh kills into hexdust.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, MortarAndPestleWeenieClassId);

            return w;
        }

        private static Weenie BuildScavengersHexdust()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.ScavengersHexdust.ScavengersHexdustWeenieClassId,
                ClassName     = "ace2000610-scavengershexdust",
                WeenieType    = WeenieType.Stackable,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Misc;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 1;
            w.PropertiesInt[PropertyInt.Mass]                 = 1;
            w.PropertiesInt[PropertyInt.Value]                = 100;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = 100;
            w.PropertiesInt[PropertyInt.StackSize]            = 1;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 1;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 100;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.SourceContainedTargetRemote;
            w.PropertiesInt[PropertyInt.TargetType]           = (int)ItemType.Creature;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)(UiEffects.Poisoned | UiEffects.Acid);
            w.PropertiesInt[PropertyInt.PhysicsState]         = 1044;

            w.PropertiesBool[PropertyBool.IsSellable]    = false;
            w.PropertiesBool[PropertyBool.Inelastic]     = true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale] = 0.45;

            w.PropertiesString[PropertyString.Name]     = "Scavenger's Hexdust";
            w.PropertiesString[PropertyString.Use]      = "Throw this at a creature to expose weak armor. Requires trained Assess Creature and Ironman Nomad fieldcraft.";
            w.PropertiesString[PropertyString.LongDesc] = "Ground bone, ash, grit, and bad intentions. A Nomad can read a creature's body well enough to make the dust bite like Imperil.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, PowderedMalachiteWeenieClassId);

            return w;
        }

        private static Weenie BuildNomadSurvivalTome()
        {
            var w = new Weenie
            {
                WeenieClassId = NomadSurvivalTomeWeenieClassId,
                ClassName     = "ace2000611-nomadsurvivaltome",
                WeenieType    = WeenieType.Book,

                PropertiesInt          = new Dictionary<PropertyInt, int>(),
                PropertiesBool         = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat        = new Dictionary<PropertyFloat, double>(),
                PropertiesString       = new Dictionary<PropertyString, string>(),
                PropertiesDID          = new Dictionary<PropertyDataId, uint>(),
                PropertiesBook         = new PropertiesBook { MaxNumPages = 11, MaxNumCharsPerPage = 1800 },
                PropertiesBookPageData = new List<PropertiesBookPageData>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Writable;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 50;
            w.PropertiesInt[PropertyInt.Mass]            = 50;
            w.PropertiesInt[PropertyInt.Value]           = 1000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.ContainedViewedRemote;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;
            w.PropertiesInt[PropertyInt.UiEffects]       = (int)UiEffects.Frost;

            w.PropertiesBool[PropertyBool.Inscribable] = false;
            w.PropertiesBool[PropertyBool.IsSellable]  = false;
            w.PropertiesBool[PropertyBool.Inelastic]   = true;
            w.PropertiesBool[PropertyBool.IgnoreAuthor]= true;

            w.PropertiesString[PropertyString.Name]       = "The Road That Keeps You";
            w.PropertiesString[PropertyString.ShortDesc]  = "A battered survival manual for Ironman Nomads.";
            w.PropertiesString[PropertyString.LongDesc]   = "A stained field book full of hard advice for those who live by empty hands, sharp eyes, and whatever the road provides.";
            w.PropertiesString[PropertyString.Inscription]= "If you have nothing, you still have your feet. If you have your feet, you are not beaten.";
            w.PropertiesString[PropertyString.ScribeName] = "Old Marra of the Ditchfire";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, TomeWeenieClassId);

            AddBookPage(w, "I. Before Hunger Gets a Vote\n\nYou are a Nomad now. This does not mean you are poor. It means you stopped pretending the world owes you a pack mule, a wand, and a warm bed.\n\nA Nomad wins by preparation. Keep your burden low. Keep your exits known. Keep one shield if you must, but do not let armor teach your skin to be lazy.\n\nYour empty hands are not empty. They carry stance, rhythm, and panic that has been trained until it looks like courage.");
            AddBookPage(w, "II. On Fists, Feet, and Staying Unburied\n\nYour gauntlets and shoes are your weapons when no proper weapon is in your hand. Low power favors the hands. High power calls the feet. Learn that boundary until you can feel it without looking.\n\nElemental gauntlets and shoes are not jewelry. Treat them like blades. Tinker them. Read their attack and defense. If the enemy evades you all day, better damage will not save you. Better contact will.\n\nIf you wear armor, you trade away the old road blessing. Plain clothes, shields, and Nomad unarmed gear are the exceptions. Everything else has a cost.");
            AddBookPage(w, "III. Runes Found in Ugly Places\n\nA Nomad does not memorize a spellbook. A Nomad steals moments of power from the dead.\n\nCreature, Life, and Item runes appear only for schools your life has actually taught you. Each rune carries several releases. Use them before the fight when you can, not while bleeding and negotiating with gravity.\n\nYour Rune Pouch is a filing box for miracles. Keep it clean. Same-spell runes may be merged. Different runes are different stories and do not belong in the same sentence.");
            AddBookPage(w, "IV. The Loom and the Long Buff\n\nWhen you hold a full rune, the Nomad Rune Loom can weave it into a school ritual rune. You give up a full stack of one spell and receive fewer, stronger conveniences: a ritual that releases the whole school package at that tier.\n\nCreature rituals wake the body. Life rituals keep the hide and breath together. Item rituals whisper through the gear you are already using.\n\nDo not weave every rune the moment you find it. Single runes are flexible. Ritual runes are for travel, boss doors, bad omens, and moments when your patience has been eaten by wolves.");
            AddBookPage(w, "V. Scavenger's Hexdust\n\nA mage calls it Imperil. A Nomad calls it knowing where the shell is soft.\n\nUse a Scavenger's Mortar on a monster corpse. Assess Creature is the hand that guides the grinding stone. Teeth, ash, old blood, grit: most of it is useless, but sometimes the dust remembers how the creature came apart.\n\nThrow Hexdust at a living monster to make its armor betray it. Better Assess Creature makes the bite stronger. This is not Life Magic. This is fieldcraft with a mean streak.");
            AddBookPage(w, "VI. The Rule of Three Pockets\n\nOne pocket is for escape: recall, stamina, anything that buys distance.\n\nOne pocket is for the next fight: runes, hexdust, food, and whatever keeps your hands moving.\n\nOne pocket is for shame: trophies you swear will matter later, strange rocks, lucky bones, and the spoon you will not explain.\n\nWhen all three pockets are full, go home. The road kills most often when it convinces you to take one more room.");
            AddBookPage(w, "VII. How to Lose Correctly\n\nYou will miss. You will be evaded. You will meet things that make your best plan look like a wet napkin.\n\nWhen that happens, stop proving bravery to nobody. Back up. Dust the target. Change element. Use a shield. Break line of sight. Drag one enemy instead of three. A living coward learns more than a heroic corpse.\n\nThe Nomad's pride is not in never needing help. It is in needing very little, very well.");
            AddBookPage(w, "VIII. Last Advice by Firelight\n\nBuff before danger. Carry Hexdust. Keep spare shoes. Respect evades. Never trust a room with too much floor.\n\nIf a stranger offers you a perfect weapon, ask what it eats. If a chest looks generous, ask who taught it manners. If the road gets quiet, sit down and listen until it confesses.\n\nAnd when you have nothing left, check again. Most people overlook the thing that saves them because it is dirty, small, and already in their hand.");

            AddBookPage(w, @"IX. The Words Beneath the Dust

The old ditch-walkers say these words came from no kingdom. They were collected from road shrines, grave songs, and the last breaths of people too stubborn to die politely. The cant is not a full language. It is a set of instructions taught to bone, dust, and fear.

Vosh du kael - Armor, remember the grave.
Mordo vek nesh - Bone reveals the weakness.
Zuthra kai mal - Dust, drink their strength.
Drego suth var - Let the hidden wound open.
Kesh voh duma - Their protection becomes ash.
Nethra zal ven - Road, take what shields them.
Kael mor nethra - The grave walks beside you.");
            AddBookPage(w, @"X. The Sevenfold Ritual

Each grade of Hexdust carries one phrase. Do not shout all seven. Powder remembers the lesson ground into it, and a mismatched word only tells the road you were not listening.

Hex I: Vosh du kael.
Hex II: Mordo vek nesh.
Hex III: Zuthra kai mal.
Hex IV: Drego suth var.
Hex V: Kesh voh duma.
Hex VI: Nethra zal ven.
Hex VII: Kael mor nethra.

The seventh is spoken softly. It does not threaten the target. It informs the grave that company is coming.");
            AddBookPage(w, @"XI. Sayings of the Open Road

The cant also lives outside ritual. Nomads use it when plain speech feels too expensive.

Nethra mor ven. - The road keeps those who move.
Kael ven; mor drego. - The grave waits; keep walking.
Zuthra vek. - The dust knows.
Vosh nesh. - Every armor has a weakness.
Kesh duma. - Let it become ash.
Du nethra. - To the road. A farewell between Nomads.
Mor kael, mor ven. - Walk beside death, but keep walking.
Nethra zal duma. - Let the road take the ashes.
Vek nesh, drego var. - Know the weakness; open the way.

Old Marra writes that a Nomad who says 'Zuthra vek' has either noticed something important or wants everyone to believe they have. Both uses are traditional.");

            return w;
        }

        private static Weenie BuildIronmanGuideBook()
        {
            var w = new Weenie
            {
                WeenieClassId = IronmanGuideBookWeenieClassId,
                ClassName     = "ace2000613-ironmanguide",
                WeenieType    = WeenieType.Book,

                PropertiesInt          = new Dictionary<PropertyInt, int>(),
                PropertiesBool         = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat        = new Dictionary<PropertyFloat, double>(),
                PropertiesString       = new Dictionary<PropertyString, string>(),
                PropertiesDID          = new Dictionary<PropertyDataId, uint>(),
                PropertiesBook         = new PropertiesBook { MaxNumPages = 8, MaxNumCharsPerPage = 1800 },
                PropertiesBookPageData = new List<PropertiesBookPageData>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Writable;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 50;
            w.PropertiesInt[PropertyInt.Mass]            = 50;
            w.PropertiesInt[PropertyInt.Value]           = 1000;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.ContainedViewedRemote;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;

            w.PropertiesBool[PropertyBool.Inscribable] = false;
            w.PropertiesBool[PropertyBool.IsSellable]  = false;
            w.PropertiesBool[PropertyBool.Inelastic]   = true;
            w.PropertiesBool[PropertyBool.IgnoreAuthor]= true;
            w.PropertiesBool[PropertyBool.IsIronmanItem] = true;

            w.PropertiesString[PropertyString.Name]        = "The Road Less Traveled";
            w.PropertiesString[PropertyString.ShortDesc]   = "A field guide for Ironman characters.";
            w.PropertiesString[PropertyString.LongDesc]    = "A plain-spoken guide for those who have taken the Ironman road: self-found gear, isolated aid, limited lives, and measured ambition.";
            w.PropertiesString[PropertyString.Inscription] = "The road is less traveled because it charges interest.";
            w.PropertiesString[PropertyString.ScribeName]  = "The Derptide Guides";
            w.PropertiesString[PropertyString.ScribeAccount] = "prewritten";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, TomeWeenieClassId);
            w.PropertiesDID[PropertyDataId.IconOverlay] = 0x06002AC4;

            AddIronmanGuidePage(w, "I. The First Promise\n\nYou have chosen Ironman. The server heard you. The road heard you. Your pack, spellbook, and old certainties have been taken apart and replaced with a new bargain.\n\nYou are self-found now. What you wear should come from your own challenge family: your kills, your quests, your vendors, your fellow Ironmen.\n\nNormal players may wear anything. You may not. That is not punishment. That is the shape of the game you asked to play.");
            AddIronmanGuidePage(w, "II. Lives and Panic\n\nIronman carries Hardcore blood. Lives matter. Death is not just a repair bill with dramatic music.\n\nSome life milestones may return a life as you climb, but do not spend lives as if the road owes you more. It does not.\n\nWhen danger turns strange, leave. Recall. Break line of sight. Drink too early instead of too late. Cowardice is what fools call survival before they understand it.");
            AddIronmanGuidePage(w, "III. Gear Provenance\n\nChallenge gear is marked quietly. Ironman-family gear belongs to Ironman-family characters. Hardcore gear belongs to Hardcore. Normal characters can wear anything, but restricted characters cannot freely borrow from the wider world.\n\nThis applies across looting, trading, mail, and wielding. If an item refuses you, it may have been touched by the wrong economy.\n\nThe cleanest rule is simple: if your path did not earn it, do not build around it.");
            AddIronmanGuidePage(w, "IV. Magic Aid\n\nThe vow rejects outside help.\n\nHelpful enchantments, heals, transfers, friendly negative dispels, item buffs, and support echoes are isolated across challenge economies. Your own buffs and your own gear still work. Other Ironman-family aid can help Ironman-family characters.\n\nDo not stand next to a normal buff line and wonder why the universe is being rude. It is not rude. It is consistent.");
            AddIronmanGuidePage(w, "V. Fellows, Trade, and Mail\n\nIronman-family characters may fellow and trade with other Ironman-family characters. They should not cross the wall with normal characters.\n\nMail follows the same spirit. It can carry messages, MMDs, and items, but challenge attachments are checked when shipped and claimed.\n\nUse /mail help if you need the command shapes. Use /ironman char to check your own progression. Use /ironman top if you need inspiration, rivalry, or a list of future ghosts.");
            AddIronmanGuidePage(w, "VI. Blind Ironman\n\nBlind Ironman hides the road ahead and spends experience into your plan as it opens. It is not weaker. It is simply less willing to hand you the map.\n\nYou will see what you have become, not every step you have not earned yet.\n\nIf you chose blind, resist the urge to turn every surprise into a spreadsheet. Some doors should stay closed until you are standing in front of them.");
            AddIronmanGuidePage(w, "VII. Loot, Quests, and Strange Stones\n\nSlayer Gems, global hunts, custom armor forces, spell focuses, and odd mutators all still exist for you, but the source matters. Keep your finds clean.\n\nGlobal quests can be checked with /gquest. Leaderboards are useful, but they are also bait. Chase them if you like; just remember the board does not pay death's bill.\n\nIf an item has strange text, read it twice before selling it once.");
            AddIronmanGuidePage(w, "VIII. Last Counsel\n\nIronman is not about proving you need nobody. It is about learning what you can do when the easy doors are shut.\n\nKeep backups. Carry recalls. Respect hollow damage. Do not let one good drop convince you that you are immortal.\n\nThe road less traveled is not empty. It is full of people who walked too loudly. Step softer.");

            return w;
        }

        private static Weenie BuildDerptideIntroBook()
        {
            var w = new Weenie
            {
                WeenieClassId = DerptideIntroBookWeenieClassId,
                ClassName     = "ace2000612-derptideintro",
                WeenieType    = WeenieType.Book,

                PropertiesInt          = new Dictionary<PropertyInt, int>(),
                PropertiesBool         = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat        = new Dictionary<PropertyFloat, double>(),
                PropertiesString       = new Dictionary<PropertyString, string>(),
                PropertiesDID          = new Dictionary<PropertyDataId, uint>(),
                PropertiesBook         = new PropertiesBook { MaxNumPages = 12, MaxNumCharsPerPage = 1800 },
                PropertiesBookPageData = new List<PropertiesBookPageData>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Writable;
            w.PropertiesInt[PropertyInt.PaletteTemplate] = 28;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 25;
            w.PropertiesInt[PropertyInt.Mass]            = 25;
            w.PropertiesInt[PropertyInt.Value]           = 0;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.ContainedViewedRemote;
            w.PropertiesInt[PropertyInt.MaxStackSize]    = 1;
            w.PropertiesInt[PropertyInt.StackSize]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;

            w.PropertiesBool[PropertyBool.Inscribable] = false;
            w.PropertiesBool[PropertyBool.IsSellable]  = false;
            w.PropertiesBool[PropertyBool.Inelastic]   = true;
            w.PropertiesBool[PropertyBool.IgnoreAuthor]= true;

            w.PropertiesString[PropertyString.Name]        = "Derptide Intro";
            w.PropertiesString[PropertyString.ShortDesc]   = "A welcome book for new Derptide adventurers.";
            w.PropertiesString[PropertyString.LongDesc]    = "A practical welcome guide covering Derptide's challenge paths, mail, bank, leaderboards, and custom systems.";
            w.PropertiesString[PropertyString.Inscription] = "Welcome to Derptide. Read this before you sell the weird thing that was trying to save your life.";
            w.PropertiesString[PropertyString.ScribeName]  = "The Derptide Guides";
            w.PropertiesString[PropertyString.ScribeAccount] = "prewritten";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, TomeWeenieClassId);

            AddDerptideIntroPage(w, "I. Welcome to Derptide\n\nWelcome, fresh face. Derptide is Asheron's Call with sharper edges, stranger toys, and a few local customs worth knowing before the local wildlife turns you into a teaching aid.\n\nThis book covers the basics: challenge paths, mail, bank, leaderboards, custom loot, and a few survival habits.\n\nMost player commands use /command. You can also try /acehelp or /acecommands if you forget the shape of something.\n\nKeep this book until you are comfortable. It weighs little, sells for nothing, and knows more than the first monster that will try to eat you.");
            AddDerptideIntroPage(w, "II. First Steps\n\nYou begin with normal starter gear based on your trained skills. If something seems missing, check your packs carefully.\n\nUseful early habits:\n\nUse /pop or /population to see who is online.\nUse /tp <player> to request a player teleport, then /tp accept, /tp decline, or /tp cancel.\nUse /cast-style npc if you prefer the NPC cast animation for compatible casting setups, or /cast-style status to check your setting.\nUse /gquest to see the current global kill quest.\n\nIf you are lost, ask in general chat. If you are embarrassed, ask anyway. The dirt has already seen worse.");
            AddDerptideIntroPage(w, "III. Hardcore\n\nHardcore is a challenge path for players who want danger without the full Ironman isolation.\n\nUse /hardcore on, then /hardcore confirm to commit. Read the warnings in chat before confirming.\n\nHardcore characters belong to a restricted challenge economy. Gear found by Hardcore characters is marked for Hardcore use. Normal players may wear anything, but restricted characters are expected to keep their own economy clean.\n\nHardcore is meant to make death and gearing matter. Do not opt in on a character you are not willing to risk.");
            AddDerptideIntroPage(w, "IV. Ironman Paths\n\nIronman is permanent and serious. Use /ironman on, then /ironman confirm to commit.\n\nIronman wipes your inventory and spellbook, rerolls your build, marks the character hardcore, blocks outside fellowships/allegiances, isolates helpful magic, and restricts gear to the Ironman family economy.\n\nOptions:\n\n/ironman on - standard Ironman\n/ironman nomad - Nomad Ironman\nadd -blind - hidden milestone progression and automatic XP spending\nadd -nh - restrict the heritage reroll pool\n\nIronman-family characters can trade/fellow with Ironman variants, but not with normal players.");
            AddDerptideIntroPage(w, "V. Nomad Ironman\n\nNomads give up normal weapons and casters for a scavenger's life: empty hands, gauntlets, shoes, shields, fieldcraft, and whatever the road coughs up.\n\nNomads can find runes from kills for schools they actually know. Runes have limited uses and can be stored or merged into broader ritual runes with the right tool.\n\nNomads can also make Scavenger's Hexdust from monster corpses with a Scavenger's Mortar. Hexdust is the Nomad answer to Imperil: dirty, practical, and rude to armor.\n\nRead The Road That Keeps You if you find one. It is the long version.");
            AddDerptideIntroPage(w, "VI. Mail\n\n/mail help shows the full summary.\n\nCommon mail commands:\n\n/mail list - view inbox\n/mail read <id> - read a message\n/mail send <name> <subject> | <body> - send text mail\n/mail pay <name> <mmds> [note] - send MMDs\n/mail ship <name> <wcid|item name> [stack] - ship an item\n/mail cod <name> <wcid|item name> <stack> <mmds> - send COD\n/mail take <id> - claim attachments\n/mail decline <id> - return COD\n/mail delete <id> - delete a read message\n\nMail respects challenge restrictions. If a package cannot be claimed by your path, it will not quietly poison your economy.");
            AddDerptideIntroPage(w, "VII. Bank and Cash\n\n/bank list shows bankable items and what you hold.\n/bank store <name|id> <amount|*> deposits items.\n/bank take <name|id> <amount|*> withdraws items.\n\nCash commands handle currency:\n\n/cash list - show pyreal and trade-note balances\n/cash give - deposit currency stacks from inventory\n/cash take <amount> - withdraw pyreals\n\n/ddt toggles direct-deposit opt-out for your character.\n\nThe mail system can pull from banked MMDs when paying or shipping if your inventory is short, so your bank is not just a dusty box with opinions.");
            AddDerptideIntroPage(w, "VIII. Leaderboards and Hunts\n\nChallenge and kill leaderboards are part of Derptide's public bragging machinery.\n\nUseful commands:\n\n/ironman top - Ironman leaderboard\n/ironman topkillers - creatures killing Ironmen\n/hardcoretop - Hardcore leaderboard\n/topkillers - general creature killer list\n/hardcoretopkillers - Hardcore deaths by creature\n/gquest - global kill quest status\n\nGlobal quests use curated creature targets. Your personal progress and the server's shared progress are shown with /gquest.");
            AddDerptideIntroPage(w, "IX. Custom Loot to Notice\n\nSlayer Gems: rare gems attune to a creature type, gain progress from matching kills while carried, and eventually charge. A charged gem can add a slayer to a fully tinkered weapon, wand, or missile weapon, but the ritual can destroy the item.\n\nElemental armor mutators: some armor can roll elemental force bonuses. These stack with diminishing returns and show their element in appraisal.\n\nBattlemage pieces and spell focuses support odd hybrid play. If an item has strange green text, read it. It may be offering you a build instead of just stats.");
            AddDerptideIntroPage(w, "X. Spell Focuses and Mage Gear\n\nSpell focuses are offhand magical tools for specialized mages. Attune them with major Atlan stones and upgrade them with stronger stones or kits. Their base armor stays low while magical protection improves, so hollow damage still matters.\n\nWith a focus equipped first, some mage weapons can be wielded one-handed. Focus-and-staff casting can use NPC-style cast animation and charge.\n\nThe idea is simple: mages get a shield-like identity without becoming plate tanks. Tailor the look if you want flair. Survive the consequences if you want glory.");
            AddDerptideIntroPage(w, "XI. Social Rules for Challenge Characters\n\nNormal players may wear any gear.\n\nRestricted challenge characters are different. Hardcore and Ironman-family characters keep separate gear provenance. Gear looted, traded, mailed, or equipped across the wrong challenge boundary can be rejected.\n\nHelpful magic is isolated too: external buffs, heals, transfers, and friendly negative dispels should only work from matching challenge economies. Self-buffs and your own items are fine.\n\nWhen in doubt, keep challenge gear with the path that earned it.");
            AddDerptideIntroPage(w, "XII. Last Advice\n\nDo not vendor unfamiliar custom items until you know what they do.\n\nUse /acehelp <command> when a command refuses to cooperate.\n\nRead item appraisals. Derptide hides many answers in green text, long descriptions, and odd requirements.\n\nIf you choose Ironman, read the prompt twice. If you choose Nomad, respect evades. If you choose Hardcore, keep your exits warm.\n\nAnd if the world seems unfair, it probably is. That is why it drops loot.");

            return w;
        }

        private static void AddDerptideIntroPage(Weenie book, string text)
        {
            AddBookPage(book, text, "The Derptide Guides");
        }

        private static void AddIronmanGuidePage(Weenie book, string text)
        {
            AddBookPage(book, text, "The Derptide Guides");
        }

        private static void AddBookPage(Weenie book, string text)
        {
            AddBookPage(book, text, "Old Marra of the Ditchfire");
        }

        private static void AddBookPage(Weenie book, string text, string authorName)
        {
            book.PropertiesBookPageData.Add(new PropertiesBookPageData
            {
                AuthorId = 0xFFFFFFFF,
                AuthorName = authorName,
                AuthorAccount = "",
                IgnoreAuthor = true,
                PageText = text
            });
        }

        private static Weenie BuildIronmanPathwardenChest()
        {
            var w = new Weenie
            {
                WeenieClassId = IronmanPathwardenChestWeenieClassId,
                ClassName     = "ace3238931-ironmanchest",
                WeenieType    = WeenieType.Chest,

                PropertiesInt       = new Dictionary<PropertyInt, int>(),
                PropertiesBool      = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat     = new Dictionary<PropertyFloat, double>(),
                PropertiesString    = new Dictionary<PropertyString, string>(),
                PropertiesDID       = new Dictionary<PropertyDataId, uint>(),
                PropertiesGenerator = new List<PropertiesGenerator>(),
            };

            w.PropertiesBool[PropertyBool.Stuck]              = true;
            w.PropertiesBool[PropertyBool.Open]               = false;
            w.PropertiesBool[PropertyBool.Locked]             = true;
            w.PropertiesBool[PropertyBool.IgnoreCollisions]   = true;
            w.PropertiesBool[PropertyBool.ReportCollisions]   = true;
            w.PropertiesBool[PropertyBool.GravityStatus]      = true;
            w.PropertiesBool[PropertyBool.Ethereal]           = true;
            w.PropertiesBool[PropertyBool.Attackable]         = true;
            w.PropertiesBool[PropertyBool.ChestRegenOnClose]  = true;

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Container;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 14750;
            w.PropertiesInt[PropertyInt.ItemsCapacity]        = 120;
            w.PropertiesInt[PropertyInt.ContainersCapacity]   = 8;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.ViewedRemote;
            w.PropertiesInt[PropertyInt.Value]                = 2500;
            w.PropertiesInt[PropertyInt.ResistLockpick]       = 9999;
            w.PropertiesInt[PropertyInt.MaxGeneratedObjects]  = 11;
            w.PropertiesInt[PropertyInt.InitGeneratedObjects] = 11;
            w.PropertiesInt[PropertyInt.PhysicsState]         = (int)(PhysicsState.Static | PhysicsState.Ethereal | PhysicsState.ReportCollisions | PhysicsState.IgnoreCollisions | PhysicsState.Gravity);
            w.PropertiesInt[PropertyInt.GeneratorType]        = (int)GeneratorType.Relative;

            w.PropertiesDID[PropertyDataId.Setup]              = 33554556;
            w.PropertiesDID[PropertyDataId.MotionTable]        = 150994948;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 536870945;
            w.PropertiesDID[PropertyDataId.Icon]               = 100667424;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 872415275;

            w.PropertiesFloat[PropertyFloat.ResetInterval]     = 1;
            w.PropertiesFloat[PropertyFloat.DefaultScale]      = 1;
            w.PropertiesFloat[PropertyFloat.GeneratorRadius]   = 1;
            w.PropertiesFloat[PropertyFloat.UseRadius]         = 1;

            w.PropertiesString[PropertyString.Name]            = "Ironman Pathwarden Gear Chest";
            w.PropertiesString[PropertyString.LockCode]        = "ironmanchestkey";
            w.PropertiesString[PropertyString.Use]             = "Use this item to open it and see its contents.";

            AddChestGenerator(w, 41513, 0);
            AddChestGenerator(w, 4616, 1);
            AddChestGenerator(w, 3238927, 2);
            AddChestGenerator(w, 3238929, 3);
            AddChestGenerator(w, 3238925, 4);
            AddChestGenerator(w, 3238930, 5);
            AddChestGenerator(w, 3238928, 6);
            AddChestGenerator(w, 3238926, 7);

            return w;
        }

        private static Weenie BuildIronmanSupplyKey()
        {
            var w = new Weenie
            {
                WeenieClassId = IronmanSupplyKeyWeenieClassId,
                ClassName     = "ace3238934-ironmansupplykey",
                WeenieType    = WeenieType.Key,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesBool[PropertyBool.Inscribable]   = true;
            w.PropertiesBool[PropertyBool.DestroyOnSell] = true;

            w.PropertiesInt[PropertyInt.ItemType]        = (int)ItemType.Key;
            w.PropertiesInt[PropertyInt.EncumbranceVal]  = 10;
            w.PropertiesInt[PropertyInt.Mass]            = 20;
            w.PropertiesInt[PropertyInt.ItemUseable]     = (int)Usable.SourceContainedTargetRemote;
            w.PropertiesInt[PropertyInt.Value]           = 0;
            w.PropertiesInt[PropertyInt.Bonded]          = (int)BondedStatus.Bonded;
            w.PropertiesInt[PropertyInt.MaxStructure]    = 1;
            w.PropertiesInt[PropertyInt.Structure]       = 1;
            w.PropertiesInt[PropertyInt.PhysicsState]    = 1044;
            w.PropertiesInt[PropertyInt.TargetType]      = (int)(ItemType.Container | ItemType.Lockable);
            w.PropertiesInt[PropertyInt.Attuned]         = (int)AttunedStatus.Attuned;

            w.PropertiesDID[PropertyDataId.Setup]              = 33554784;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 536870932;
            w.PropertiesDID[PropertyDataId.Icon]               = 100668438;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 872415275;

            w.PropertiesString[PropertyString.Name]            = "Ironman Gear Key";
            w.PropertiesString[PropertyString.KeyCode]         = "ironmanchestkey";
            w.PropertiesString[PropertyString.Use]             = "Use this item on a locked chest to unlock it.";
            w.PropertiesString[PropertyString.LongDesc]        = "This key unlocks the Ironman Pathwarden gear chest.";

            return w;
        }

        private static Weenie BuildNomadSupplyKey()
        {
            var w = BuildIronmanSupplyKey();
            w.WeenieClassId = NomadSupplyKeyWeenieClassId;
            w.ClassName = "ace2000616-nomadsupplykey";
            w.PropertiesString[PropertyString.Name] = "Nomad Gear Key";
            w.PropertiesString[PropertyString.KeyCode] = "nomadchestkey";
            w.PropertiesString[PropertyString.LongDesc] = "This key unlocks the Nomad Pathwarden chest.";
            return w;
        }

        private static void AddChestGenerator(Weenie chest, uint wcid, int slot)
        {
            chest.PropertiesGenerator.Add(new PropertiesGenerator
            {
                Probability = -1,
                WeenieClassId = wcid,
                Delay = 0,
                InitCreate = 1,
                MaxCreate = 1,
                WhenCreate = RegenerationType.PickUp,
                WhereCreate = RegenLocationType.Contain,
                StackSize = -1,
                PaletteId = 0,
                Shade = 0,
                ObjCellId = 0,
                OriginX = 0,
                OriginY = 0,
                OriginZ = 0,
                AnglesW = 1,
                AnglesX = 0,
                AnglesY = 0,
                AnglesZ = 0,
            });
        }

        private static Weenie BuildNomadPathwardenChest()
        {
            var w = new Weenie
            {
                WeenieClassId = NomadPathwardenChestWeenieClassId,
                ClassName     = "ace2000615-nomadpathwardenchest",
                WeenieType    = WeenieType.Chest,

                PropertiesInt       = new Dictionary<PropertyInt, int>(),
                PropertiesBool      = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat     = new Dictionary<PropertyFloat, double>(),
                PropertiesString    = new Dictionary<PropertyString, string>(),
                PropertiesDID       = new Dictionary<PropertyDataId, uint>(),
                PropertiesGenerator = new List<PropertiesGenerator>(),
            };

            w.PropertiesBool[PropertyBool.Stuck]              = true;
            w.PropertiesBool[PropertyBool.Open]               = false;
            w.PropertiesBool[PropertyBool.Locked]             = true;
            w.PropertiesBool[PropertyBool.DefaultLocked]      = true;
            w.PropertiesBool[PropertyBool.IgnoreCollisions]   = true;
            w.PropertiesBool[PropertyBool.ReportCollisions]   = true;
            w.PropertiesBool[PropertyBool.GravityStatus]      = true;
            w.PropertiesBool[PropertyBool.Ethereal]           = true;
            w.PropertiesBool[PropertyBool.Attackable]         = true;
            w.PropertiesBool[PropertyBool.ChestRegenOnClose]  = true;

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Container;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 14750;
            w.PropertiesInt[PropertyInt.ItemsCapacity]        = 120;
            w.PropertiesInt[PropertyInt.ContainersCapacity]   = 8;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.ViewedRemote;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.BoostStamina;
            w.PropertiesInt[PropertyInt.Value]                = 2500;
            w.PropertiesInt[PropertyInt.ResistLockpick]       = 9999;
            w.PropertiesInt[PropertyInt.MaxGeneratedObjects]  = 3;
            w.PropertiesInt[PropertyInt.InitGeneratedObjects] = 3;
            w.PropertiesInt[PropertyInt.PhysicsState]         = (int)(PhysicsState.Static | PhysicsState.Ethereal | PhysicsState.ReportCollisions | PhysicsState.IgnoreCollisions | PhysicsState.Gravity);
            w.PropertiesInt[PropertyInt.GeneratorType]        = (int)GeneratorType.Relative;

            w.PropertiesDID[PropertyDataId.Setup]              = 33554556;
            w.PropertiesDID[PropertyDataId.MotionTable]        = 150994948;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 536870945;
            w.PropertiesDID[PropertyDataId.Icon]               = 100667424;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 872415275;

            w.PropertiesFloat[PropertyFloat.ResetInterval]     = 1;
            w.PropertiesFloat[PropertyFloat.DefaultScale]      = 1;
            w.PropertiesFloat[PropertyFloat.GeneratorRadius]   = 1;
            w.PropertiesFloat[PropertyFloat.UseRadius]         = 1;

            w.PropertiesString[PropertyString.Name]            = "Nomad Pathwarden Gear Chest";
            w.PropertiesString[PropertyString.LockCode]        = "nomadchestkey";
            w.PropertiesString[PropertyString.ShortDesc]       = "A Pathwarden gear chest for Nomad Ironmen.";
            w.PropertiesString[PropertyString.Use]             = "Use this item to open it and see its contents.";
            w.PropertiesString[PropertyString.LongDesc]        = "A locked Pathwarden gear chest holding the first tools of the Nomad road.";

            AddChestGenerator(w, 40439, 0);
            AddChestGenerator(w, WorldObjects.NomadRunePouch.NomadRunePouchWeenieClassId, 1);
            AddChestGenerator(w, WorldObjects.ScavengersHexdust.BoneGrinderWeenieClassId, 2);

            return w;
        }
        private static Weenie BuildDrunkenEventBeer()
        {
            var w = new Weenie
            {
                WeenieClassId = DrunkenEventBeerWeenieClassId,
                ClassName     = "ace2000617-ulgrimsdubiousbeer",
                WeenieType    = WeenieType.Food,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Food;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = 27;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 50;
            w.PropertiesInt[PropertyInt.Mass]                 = 50;
            w.PropertiesInt[PropertyInt.Value]                = 0;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = 1;
            w.PropertiesInt[PropertyInt.StackSize]            = 1;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 50;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 50;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 0;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.Contained;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.BoostMana;
            w.PropertiesInt[PropertyInt.PhysicsState]         = 1044;

            w.PropertiesBool[PropertyBool.IsSellable] = false;
            w.PropertiesBool[PropertyBool.Inelastic]  = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale]     = 0.7;
            w.PropertiesFloat[PropertyFloat.CooldownDuration] = WorldObjects.FlutterStone.DefaultCooldownSeconds;

            w.PropertiesString[PropertyString.Name]     = "Ulgrim's Dubious Beer";
            w.PropertiesString[PropertyString.Use]      = "Bring this to Ulgrim the Unpleasant during the drunken mob hunt.";
            w.PropertiesString[PropertyString.LongDesc] = "An event beer pried from a drunken creature. It smells like poor choices and temporary global importance.";

            w.PropertiesDID[PropertyDataId.Setup] = 0x0200018B;
            w.PropertiesDID[PropertyDataId.Icon]  = 0x06001036;
            CopySetupAndIcon(w, SmallBeerWeenieClassId);

            return w;
        }

        private static Weenie BuildHorriblyForgedDerpCoin()
        {
            var w = new Weenie
            {
                WeenieClassId = HorriblyForgedDerpCoinWeenieClassId,
                ClassName     = "ace2000618-horriblyforgedderpcoin",
                WeenieType    = WeenieType.Stackable,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Misc;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = (int)PaletteTemplate.Yellow;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 1;
            w.PropertiesInt[PropertyInt.Mass]                 = 1;
            w.PropertiesInt[PropertyInt.Value]                = 0;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = 1000;
            w.PropertiesInt[PropertyInt.StackSize]            = 1;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 1;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 0;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.No;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.Lightning;
            w.PropertiesInt[PropertyInt.PhysicsState]         = 1044;

            w.PropertiesBool[PropertyBool.IsSellable] = false;

            w.PropertiesString[PropertyString.Name]     = "Horribly Forged Derp Coin";
            w.PropertiesString[PropertyString.ShortDesc] = "A corrupted counterfeit Derp Coin.";
            w.PropertiesString[PropertyString.LongDesc] = "A lumpy, bright-edged imitation Derp Coin recovered from corrupted tier 8 creatures. It is too ugly to spend, but useful proof for correcting the corruption.";

            w.PropertiesDID[PropertyDataId.Setup]              = 0x02000172;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 0x20000014;
            w.PropertiesDID[PropertyDataId.Icon]               = 0x0600754B;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 0x3400002B;
            w.PropertiesDID[PropertyDataId.IconOverlay]        = 0x06002AC6;

            return w;
        }

        private static Weenie BuildFlutterStone()
        {
            var w = new Weenie
            {
                WeenieClassId = WorldObjects.FlutterStone.FlutterStoneWeenieClassId,
                ClassName     = "ace2000620-flutterstone",
                WeenieType    = WeenieType.Gem,

                PropertiesInt    = new Dictionary<PropertyInt, int>(),
                PropertiesBool   = new Dictionary<PropertyBool, bool>(),
                PropertiesFloat  = new Dictionary<PropertyFloat, double>(),
                PropertiesString = new Dictionary<PropertyString, string>(),
                PropertiesDID    = new Dictionary<PropertyDataId, uint>(),
            };

            w.PropertiesInt[PropertyInt.ItemType]             = (int)ItemType.Gem;
            w.PropertiesInt[PropertyInt.PaletteTemplate]      = (int)PaletteTemplate.Blue;
            w.PropertiesInt[PropertyInt.EncumbranceVal]       = 1;
            w.PropertiesInt[PropertyInt.Mass]                 = 1;
            w.PropertiesInt[PropertyInt.Value]                = 5000;
            w.PropertiesInt[PropertyInt.MaxStackSize]         = 1000;
            w.PropertiesInt[PropertyInt.StackSize]            = 1;
            w.PropertiesInt[PropertyInt.StackUnitEncumbrance] = 1;
            w.PropertiesInt[PropertyInt.StackUnitMass]        = 1;
            w.PropertiesInt[PropertyInt.StackUnitValue]       = 5000;
            w.PropertiesInt[PropertyInt.ItemUseable]          = (int)Usable.Contained;
            w.PropertiesInt[PropertyInt.ItemDifficulty]       = 250;
            w.PropertiesInt[PropertyInt.UiEffects]            = (int)UiEffects.Magical;
            w.PropertiesInt[PropertyInt.PhysicsState]         = 1044;
            w.PropertiesInt[PropertyInt.SharedCooldown]       = WorldObjects.FlutterStone.FlutterStoneCooldownId;

            w.PropertiesBool[PropertyBool.Inelastic] = true;

            w.PropertiesFloat[PropertyFloat.DefaultScale]     = 0.7;
            w.PropertiesFloat[PropertyFloat.CooldownDuration] = WorldObjects.FlutterStone.DefaultCooldownSeconds;

            w.PropertiesString[PropertyString.Name]      = "Flutter Stone";
            w.PropertiesString[PropertyString.ShortDesc] = "An arcane stone that carries the bearer a short distance forward.";
            w.PropertiesString[PropertyString.Use]       = "Use this stone to flutter twenty feet forward. Requires Arcane Lore 250.";
            w.PropertiesString[PropertyString.LongDesc]  = "A softly humming stone that turns a mage's intent into a sudden forward flutter. It does not open a portal or teleport the bearer; it urges the body into one brief impossible stride.";

            w.PropertiesDID[PropertyDataId.Setup]              = 0x0200018B;
            w.PropertiesDID[PropertyDataId.SoundTable]         = 0x20000014;
            w.PropertiesDID[PropertyDataId.Icon]               = 0x06001036;
            w.PropertiesDID[PropertyDataId.PhysicsEffectTable] = 0x3400002B;
            CopySetupAndIcon(w, MajorShiveringStoneWeenieClassId);
            w.PropertiesDID[PropertyDataId.Icon]               = 0x06002157;
            w.PropertiesDID[PropertyDataId.IconUnderlay]       = 0x06002191;

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
