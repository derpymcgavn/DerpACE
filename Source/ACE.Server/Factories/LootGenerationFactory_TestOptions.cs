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

            ["pugilist"] = "pugilist",
            ["combo"] = "pugilist",
            ["fist"] = "pugilist",
            ["fists"] = "pugilist",
            ["flurry"] = "pugilist",
            ["rake"] = "pugilist",

            ["ravager"] = "ravager",
            ["ravageraxe"] = "ravager",
            ["bonebreak"] = "ravager",

            ["warden"] = "warden",
            ["maul"] = "warden",
            ["concussive"] = "warden",
            ["concussion"] = "warden",

            ["lugianhammer"] = "lugianhammer",
            ["stonehand"] = "lugianhammer",
            ["hammerthrow"] = "lugianhammer",
            ["thrownhammer"] = "lugianhammer",

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
            ["platter"] = "platter",
            ["flyingbuffet"] = "platter",
            ["buffet"] = "platter",
            ["servingplatter"] = "platter",
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

            ["gravecaller"] = "gravecaller",
            ["necromancer"] = "gravecaller",
            ["archmagi"] = "archmagi",
            ["archmage"] = "archmagi",

            ["shadowclone"] = "shadowclone",
            ["shadowcaster"] = "shadowclone",
            ["voidshadow"] = "shadowclone",
            ["umbral"] = "shadowclone",
            ["mirror"] = "shadowclone",
            ["shadowshot"] = "shadowshot",
            ["shadowvolley"] = "shadowshot",
            ["secondshadow"] = "secondshadow",
            ["shadowblade"] = "secondshadow",

            ["hierophant"] = "hierophant",
            ["life"] = "hierophant",
            ["martyr"] = "hierophant",

            ["skybreaker"] = "skybreaker",
            ["meteor"] = "skybreaker",
            ["meteorsquall"] = "skybreaker",
            ["squall"] = "skybreaker",

            ["stormcaller"] = "stormcaller",
            ["chainlightning"] = "stormcaller",
            ["chain"] = "stormcaller",

            ["orbitweaver"] = "orbitweaver",
            ["spiralstar"] = "orbitweaver",
            ["spiral"] = "orbitweaver",
            ["orbit"] = "orbitweaver",

            ["opportunist"] = "opportunist",
            ["opportunity"] = "opportunist",
            ["executioner"] = "executioner",
            ["execute"] = "executioner",

            ["confusion"] = "confusion",
            ["voidconfusion"] = "confusion",
            ["bedlam"] = "confusion",
            ["maddening"] = "confusion",
        };

        private static readonly Dictionary<string, TreasureWeaponType> WeaponMutatorTestTypes = new Dictionary<string, TreasureWeaponType>(StringComparer.OrdinalIgnoreCase)
        {
            ["thief"] = TreasureWeaponType.Dagger,
            ["quickening"] = TreasureWeaponType.Dagger,
            ["fencer"] = TreasureWeaponType.SwordMS,
            ["pugilist"] = TreasureWeaponType.Unarmed,
            ["ravager"] = TreasureWeaponType.Axe,
            ["warden"] = TreasureWeaponType.Mace,
            ["lugianhammer"] = TreasureWeaponType.Mace,
            ["resolute"] = TreasureWeaponType.Sword,
            ["polebreaker"] = TreasureWeaponType.Staff,
            ["sentinel"] = TreasureWeaponType.Spear,
            ["stalker"] = TreasureWeaponType.Bow,
            ["breacher"] = TreasureWeaponType.Crossbow,
            ["dinnerware"] = TreasureWeaponType.ThrownDinnerware,
            ["discus"] = TreasureWeaponType.Discus,
            ["platter"] = TreasureWeaponType.Platter,
            ["dartflinger"] = TreasureWeaponType.Atlatl,
            ["reaper"] = TreasureWeaponType.Atlatl,
            ["gravecaller"] = TreasureWeaponType.Caster,
            ["archmagi"] = TreasureWeaponType.Caster,
            ["shadowclone"] = TreasureWeaponType.Caster,
            ["shadowshot"] = TreasureWeaponType.Bow,
            ["secondshadow"] = TreasureWeaponType.Sword,
            ["hierophant"] = TreasureWeaponType.Caster,
            ["skybreaker"] = TreasureWeaponType.Caster,
            ["stormcaller"] = TreasureWeaponType.Caster,
            ["orbitweaver"] = TreasureWeaponType.Caster,
            ["opportunist"] = TreasureWeaponType.Sword,
            ["executioner"] = TreasureWeaponType.Axe,
            ["confusion"] = TreasureWeaponType.Caster,
        };

        private static readonly Dictionary<string, string> ShieldMutatorAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["defender"] = "defender",
            ["defenders"] = "defender",
            ["defendersshield"] = "defender",
            ["challenge"] = "defender",

            ["thorns"] = "thorns",
            ["thorn"] = "thorns",
            ["thornshield"] = "thorns",
            ["thornbound"] = "thorns",

            ["bashing"] = "bashing",
            ["bash"] = "bashing",
            ["bashshield"] = "bashing",
            ["shieldbash"] = "bashing",
            ["breaker"] = "bashing",

            ["reflection"] = "reflection",
            ["reflect"] = "reflection",
            ["projectilereflect"] = "reflection",
            ["returnshot"] = "reflection",

            ["spellmirror"] = "spellmirror",
            ["mirror"] = "spellmirror",
            ["wardmirror"] = "spellmirror",
            ["spellreflect"] = "spellmirror",
        };

        private static readonly Dictionary<string, string> ArmorMutatorAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["culinarian"] = "culinarian",
            ["cooking"] = "culinarian",
            ["cookinggloves"] = "culinarian",
            ["chef"] = "culinarian",
            ["wellfed"] = "culinarian",

            ["alchemist"] = "alchemist",
            ["alchemy"] = "alchemist",
            ["alchemygloves"] = "alchemist",
            ["potion"] = "alchemist",
            ["phial"] = "alchemist",
            ["splash"] = "alchemist",
            ["instability"] = "alchemicalinstability",
            ["alchemicalinstability"] = "alchemicalinstability",
            ["unstable"] = "alchemicalinstability",

            ["unarmed"] = "unarmed",
            ["unarmeddamage"] = "unarmed",
            ["brawler"] = "unarmed",
            ["punch"] = "unarmed",
            ["kick"] = "unarmed",

            ["healingdance"] = "healingdance",
            ["healing"] = "healingdance",
            ["danceheal"] = "healingdance",
            ["healthdance"] = "healingdance",

            ["rejuvenatingdance"] = "rejuvenatingdance",
            ["rejuvenating"] = "rejuvenatingdance",
            ["rejuvdance"] = "rejuvenatingdance",
            ["staminadance"] = "rejuvenatingdance",

            ["replenishingdance"] = "replenishingdance",
            ["replenishing"] = "replenishingdance",
            ["replendance"] = "replenishingdance",
            ["manadance"] = "replenishingdance",

            ["armorsort"] = "armorsort",
            ["sort"] = "armorsort",
            ["damagearmor"] = "armorsort",
            ["spellarmor"] = "armorsort",

            ["battlemage"] = "battlemage",
            ["battlemagehelm"] = "battlemage",
            ["warblade"] = "battlemage",
            ["spellbladehelm"] = "battlemage",
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
                "pugilist/combo",
                "ravager",
                "warden/concussive",
                "lugianhammer/hammerthrow",
                "resolute/vampire",
                "polebreaker",
                "sentinel",
                "stalker",
                "breacher",
                "dinnerware/banquet",
                "discus/warriorprincess",
                "platter/flyingbuffet",
                "dartflinger/ricochet",
                "reaper",
                "gravecaller/necromancer",
                "archmagi",
                "shadowclone/voidshadow",
                "shadowshot/shadowvolley",
                "secondshadow/shadowblade",
                "hierophant/life",
                "skybreaker/meteor",
                "stormcaller/chainlightning",
                "orbitweaver/spiralstar",
                "confusion/bedlam",
                "opportunist",
                "executioner"
            });
        }

        public static bool TryResolveShieldMutator(string name, out string canonicalName)
        {
            canonicalName = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return ShieldMutatorAliases.TryGetValue(NormalizeWeaponMutatorName(name), out canonicalName);
        }

        public static string GetShieldMutatorNames()
        {
            return string.Join(", ", new[]
            {
                "defender",
                "thorns/thornbound",
                "bashing/shieldbash",
                "reflection/returnshot",
                "spellmirror/spellreflect"
            });
        }

        public static bool TryResolveArmorMutator(string name, out string canonicalName)
        {
            canonicalName = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return ArmorMutatorAliases.TryGetValue(NormalizeWeaponMutatorName(name), out canonicalName);
        }

        public static string GetArmorMutatorNames()
        {
            return string.Join(", ", new[]
            {
                "culinarian/cooking",
                "alchemist/alchemy",
                "alchemicalinstability",
                "unarmed/brawler",
                "healingdance",
                "rejuvenatingdance",
                "replenishingdance",
                "armorsort/sort",
                "battlemage/battlemagehelm"
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
                "pugilist"    => wo.GetProperty(PropertyBool.IsPugilistUnarmedWeapon) == true,
                "ravager"     => wo.GetProperty(PropertyBool.IsRavagersAxe) == true,
                "warden"      => wo.GetProperty(PropertyBool.IsWardensMaul) == true,
                "lugianhammer"=> wo.GetProperty(PropertyBool.IsLugianHammerThrowWeapon) == true,
                "resolute"    => wo.GetProperty(PropertyBool.IsResoluteBlade) == true,
                "polebreaker" => wo.GetProperty(PropertyBool.IsPolebreakerStaff) == true,
                "sentinel"    => wo.GetProperty(PropertyBool.IsSentinelSpear) == true,
                "stalker"     => wo.GetProperty(PropertyBool.IsStalkersBow) == true,
                "breacher"    => wo.GetProperty(PropertyBool.IsBreachersCrossbow) == true,
                "dinnerware"  => wo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true,
                "discus"      => wo.WeenieClassId == (uint)WeenieClassName.discus
                                 && wo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true,
                "platter"     => wo.WeenieClassId == (uint)WeenieClassName.platter
                                 && wo.GetProperty(PropertyBool.IsDinnerwareWeapon) == true,
                "dartflinger" => wo.GetProperty(PropertyBool.IsRicochetAtlatl) == true
                                 || wo.GetProperty(PropertyBool.IsDartflingerAtlatl) == true,
                "ricochet"    => wo.GetProperty(PropertyBool.IsRicochetAtlatl) == true
                                 || wo.GetProperty(PropertyBool.IsDartflingerAtlatl) == true,
                "reaper"      => wo.GetProperty(PropertyBool.IsReapersAtlatl) == true,
                "gravecaller" => wo.GetProperty(PropertyBool.IsGravecallerCaster) == true,
                "archmagi"    => wo.GetProperty(PropertyBool.IsArchmagiCaster) == true,
                "shadowclone" => wo.GetProperty(PropertyBool.IsShadowCloneCaster) == true
                                 || wo.GetProperty(PropertyBool.IsShadowCloneWeapon) == true,
                "shadowshot"  => wo.GetProperty(PropertyBool.IsShadowVolleyWeapon) == true
                                 || wo.GetProperty(PropertyBool.IsShadowCloneWeapon) == true
                                    && wo.GetProperty(PropertyBool.IsSecondShadowWeapon) != true,
                "secondshadow"=> wo.GetProperty(PropertyBool.IsSecondShadowWeapon) == true
                                 || wo.GetProperty(PropertyBool.IsShadowCloneWeapon) == true,
                "opportunist" => wo.GetProperty(PropertyBool.IsOpportunistWeapon) == true,
                "executioner" => wo.GetProperty(PropertyBool.IsExecutionerWeapon) == true,
                "hierophant"  => wo.GetProperty(PropertyBool.IsHierophantCaster) == true,
                "skybreaker"  => wo.GetProperty(PropertyBool.IsSkybreakerCaster) == true,
                "stormcaller" => wo.GetProperty(PropertyBool.IsStormcallerCaster) == true,
                "orbitweaver" => wo.GetProperty(PropertyBool.IsOrbitweaverCaster) == true,
                "confusion"   => wo.GetProperty(PropertyBool.IsConfusionCaster) == true,
                _             => false,
            };
        }

        public static bool HasShieldMutator(WorldObject wo, string mutatorName)
        {
            if (wo == null || !TryResolveShieldMutator(mutatorName, out var canonicalName))
                return false;

            return canonicalName switch
            {
                "defender" => wo.GetProperty(PropertyBool.IsDefendersShield) == true,
                "thorns"   => wo.GetProperty(PropertyBool.IsThornsShield) == true,
                "bashing"  => wo.GetProperty(PropertyBool.IsBashingShield) == true,
                "reflection" => wo.GetProperty(PropertyBool.IsProjectileReflectShield) == true,
                "spellmirror" => wo.GetProperty(PropertyBool.IsSpellMirrorShield) == true,
                _          => false,
            };
        }

        public static bool HasArmorMutator(WorldObject wo, string mutatorName)
        {
            if (wo == null || !TryResolveArmorMutator(mutatorName, out var canonicalName))
                return false;

            return canonicalName switch
            {
                "culinarian"        => wo.GetProperty(PropertyBool.IsCookingGloves) == true,
                "alchemist"         => wo.GetProperty(PropertyBool.IsAlchemistGloves) == true,
                "alchemicalinstability" => wo.GetProperty(PropertyBool.IsAlchemicalInstabilityGloves) == true,
                "unarmed"           => (wo.UnarmedBaseDamage ?? 0) > 0,
                "healingdance"      => wo.GetProperty(PropertyBool.IsHealingDanceBoots) == true,
                "rejuvenatingdance" => wo.GetProperty(PropertyBool.IsRejuvenatingDanceBoots) == true,
                "replenishingdance" => wo.GetProperty(PropertyBool.IsReplenishingDanceBoots) == true,
                "armorsort"         => (wo.ArmorSortDamageBonus ?? 0) > 0,
                "battlemage"        => wo.GetProperty(PropertyBool.IsBattlemageHelm) == true,
                _            => false,
            };
        }

        private static string NormalizeWeaponMutatorName(string name)
        {
            return name.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        }
    }
}
