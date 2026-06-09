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

            ["ricochet"] = "ricochet",
            ["ricochetatlatl"] = "ricochet",
            ["dartflinger"] = "ricochet",

            ["archmagi"] = "archmagi",
            ["archmage"] = "archmagi",

            ["hierophant"] = "hierophant",
            ["life"] = "hierophant",
            ["martyr"] = "hierophant",
        };

        private static readonly Dictionary<string, TreasureWeaponType> WeaponMutatorTestTypes = new Dictionary<string, TreasureWeaponType>(StringComparer.OrdinalIgnoreCase)
        {
            ["thief"] = TreasureWeaponType.Dagger,
            ["fencer"] = TreasureWeaponType.SwordMS,
            ["ravager"] = TreasureWeaponType.Axe,
            ["warden"] = TreasureWeaponType.Mace,
            ["resolute"] = TreasureWeaponType.Sword,
            ["polebreaker"] = TreasureWeaponType.Staff,
            ["sentinel"] = TreasureWeaponType.Spear,
            ["stalker"] = TreasureWeaponType.Bow,
            ["breacher"] = TreasureWeaponType.Crossbow,
            ["ricochet"] = TreasureWeaponType.Atlatl,
            ["archmagi"] = TreasureWeaponType.Caster,
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
                "fencer/duelist",
                "ravager",
                "warden/concussive",
                "resolute/vampire",
                "polebreaker",
                "sentinel",
                "stalker",
                "breacher",
                "ricochet",
                "archmagi",
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
                "fencer"      => wo.GetProperty(PropertyBool.IsFencerBlade) == true,
                "ravager"     => wo.GetProperty(PropertyBool.IsRavagersAxe) == true,
                "warden"      => wo.GetProperty(PropertyBool.IsWardensMaul) == true,
                "resolute"    => wo.GetProperty(PropertyBool.IsResoluteBlade) == true,
                "polebreaker" => wo.GetProperty(PropertyBool.IsPolebreakerStaff) == true,
                "sentinel"    => wo.GetProperty(PropertyBool.IsSentinelSpear) == true,
                "stalker"     => wo.GetProperty(PropertyBool.IsStalkersBow) == true,
                "breacher"    => wo.GetProperty(PropertyBool.IsBreachersCrossbow) == true,
                "ricochet"    => wo.GetProperty(PropertyBool.IsRicochetAtlatl) == true,
                "archmagi"    => wo.GetProperty(PropertyBool.IsArchmagiCaster) == true,
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
