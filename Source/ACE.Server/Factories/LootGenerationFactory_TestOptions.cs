using System;
using System.Collections.Generic;

using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Enum;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        private static readonly Dictionary<string, string> WeaponMutatorAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["thief"] = "thief",
            ["thieves"] = "thief",
            ["shadow"] = "thief",

            ["quickening"] = "quickening",
            ["quick"] = "quickening",
            ["haste"] = "quickening",
            ["swift"] = "quickening",

            ["fencer"] = "fencer",
            ["duelist"] = "fencer",
            ["duelistblade"] = "fencer",

            ["ravager"] = "ravager",
            ["ravageraxe"] = "ravager",
            ["bonebreak"] = "ravager",

            ["warden"] = "warden",
            ["maul"] = "warden",
            ["concussive"] = "warden",
            ["concussion"] = "warden",

            ["resolute"] = "resolute",
            ["resolve"] = "resolute",
            ["vampire"] = "resolute",

            ["polebreaker"] = "polebreaker",
            ["pole"] = "polebreaker",

            ["sentinel"] = "sentinel",
            ["sentinelspear"] = "sentinel",

            ["stalker"] = "stalker",
            ["stalkerbow"] = "stalker",

            ["breacher"] = "breacher",
            ["breachercrossbow"] = "breacher",

            ["dinnerware"] = "dinnerware",
            ["discus"] = "discus",
            ["warriorprincess"] = "discus",
            ["warriorprincesscall"] = "discus",
            ["throwware"] = "dinnerware",
            ["banquet"] = "dinnerware",
            ["porcelain"] = "dinnerware",

            ["dartflinger"] = "dartflinger",
            ["dartflingers"] = "dartflinger",
            ["ricochet"] = "dartflinger",
            ["ricochetatlatl"] = "dartflinger",

            ["reaper"] = "reaper",
            ["reapers"] = "reaper",
            ["reaperatlatl"] = "reaper",
            ["reapersatlatl"] = "reaper",

            ["archmagi"] = "archmagi",
            ["archmage"] = "archmagi",

            ["shadowclone"] = "shadowclone",
            ["shadowcaster"] = "shadowclone",
            ["voidshadow"] = "shadowclone",
            ["umbral"] = "shadowclone",
            ["mirror"] = "shadowclone",

            ["hierophant"] = "hierophant",
            ["life"] = "hierophant",
            ["martyr"] = "hierophant",
        };

        private static readonly Dictionary<string, TreasureWeaponType> WeaponMutatorTestTypes = new Dictionary<string, TreasureWeaponType>(StringComparer.OrdinalIgnoreCase)
        {
            ["thief"] = TreasureWeaponType.Dagger,
            ["quickening"] = TreasureWeaponType.Dagger,
            ["fencer"] = TreasureWeaponType.SwordMS,
            ["ravager"] = TreasureWeaponType.Axe,
            ["warden"] = TreasureWeaponType.Mace,
            ["resolute"] = TreasureWeaponType.Sword,
            ["polebreaker"] = TreasureWeaponType.Staff,
            ["sentinel"] = TreasureWeaponType.Spear,
            ["stalker"] = TreasureWeaponType.Bow,
            ["breacher"] = TreasureWeaponType.Crossbow,
            ["dinnerware"] = TreasureWeaponType.ThrownDinnerware,
            ["discus"] = TreasureWeaponType.Discus,
            ["dartflinger"] = TreasureWeaponType.Atlatl,
            ["reaper"] = TreasureWeaponType.Atlatl,
            ["archmagi"] = TreasureWeaponType.Caster,
            ["shadowclone"] = TreasureWeaponType.Caster,
            ["hierophant"] = TreasureWeaponType.Caster,
        };

        public static bool TryResolveWeaponMutator(string name, out string canonicalName)
        {
            canonicalName = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return WeaponMutatorAliases.TryGetValue(NormalizeWeaponMutatorName(name), out canonicalName);
        }

        public static string GetWeaponMutatorNames()
        {
            return string.Join(", ", new[]
            {
                "thief",
                "quickening/haste",
                "fencer/duelist",
                "ravager",
                "warden/concussive",
                "resolute/vampire",
                "polebreaker",
                "sentinel",
                "stalker",
                "breacher",
                "dinnerware/banquet",
                "discus/warriorprincess",
                "dartflinger/ricochet",
                "reaper",
                "archmagi",
                "shadowclone/voidshadow",
                "hierophant/life"
            });
        }

        public static bool TryGetWeaponMutatorTestType(string canonicalName, out TreasureWeaponType weaponType)
        {
            return WeaponMutatorTestTypes.TryGetValue(canonicalName ?? string.Empty, out weaponType);
        }

        public static bool HasWeaponMutator(WorldObject wo, string mutatorName)
        {
            if (wo == null || !TryResolveWeaponMutator(mutatorName, out var canonicalName))
                return false;

            return canonicalName switch
            {
                "thief"       => wo.GetProperty(PropertyBool.IsThievesDagger) == true,
                "quickening"  => wo.GetProperty(PropertyBool.IsQuickeningDagger) == true,
                "fencer"      => wo.GetProperty(PropertyBool.IsFencerBlade) == true,
                "ravager"     => wo.GetProperty(PropertyBool.IsRavagersAxe) == true,
                "warden"      => wo.GetProperty(PropertyBool.IsWardensMaul) == true,
                "resolute"    => wo.GetProperty(PropertyBool.IsResoluteBlade) == true,
                "polebreaker" => wo.GetProperty(PropertyBool.IsPolebreakerStaff) == true,
                "sentinel"    => wo.GetProperty(PropertyBool.IsSentinelSpear) == true,
                "stalker"     => wo.GetProperty(PropertyBool.IsStalkersBow) == true,
                "breacher"    => wo.GetProperty(PropertyBool.IsBreachersCrossbow) == true,
                "dinnerware"  => wo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true,
                "discus"      => wo.WeenieClassId == (uint)WeenieClassName.discus
                                 && wo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true,
                "dartflinger" => wo.GetProperty(PropertyBool.IsRicochetAtlatl) == true
                                 || wo.GetProperty(PropertyBool.IsDartflingerAtlatl) == true,
                "ricochet"    => wo.GetProperty(PropertyBool.IsRicochetAtlatl) == true
                                 || wo.GetProperty(PropertyBool.IsDartflingerAtlatl) == true,
                "reaper"      => wo.GetProperty(PropertyBool.IsReapersAtlatl) == true,
                "archmagi"    => wo.GetProperty(PropertyBool.IsArchmagiCaster) == true,
                "shadowclone" => wo.GetProperty(PropertyBool.IsShadowCloneCaster) == true,
                "hierophant"  => wo.GetProperty(PropertyBool.IsHierophantCaster) == true,
                _             => false,
            };
        }

        private static string NormalizeWeaponMutatorName(string name)
        {
            return name.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        }
    }
}
