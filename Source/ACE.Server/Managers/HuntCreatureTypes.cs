using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Common;
using ACE.Entity.Enum;

namespace ACE.Server.Managers
{
    /// <summary>
    /// DerpACE: Creature families valid for repeatable hunts and Slayer Gems.
    /// This intentionally excludes non-hunt or limited-use enum buckets such as Fae, Food,
    /// Statue, Target, Wall, Device, and similar content-scaffolding types.
    /// </summary>
    public static class HuntCreatureTypes
    {
        public static readonly (CreatureType type, string name, int minKills, int maxKills)[] GlobalQuestPool =
        {
            (CreatureType.Olthoi,             "Olthoi",             15, 25),
            (CreatureType.ParadoxOlthoi,      "Paradox Olthoi",     15, 25),
            (CreatureType.OlthoiLarvae,       "Olthoi Larvae",      20, 40),
            (CreatureType.Banderling,         "Banderling",         20, 40),
            (CreatureType.Drudge,             "Drudge",             25, 50),
            (CreatureType.Mosswart,           "Mosswart",           20, 40),
            (CreatureType.Lugian,             "Lugian",             15, 30),
            (CreatureType.GotrokLugian,       "Gotrok Lugian",      15, 30),
            (CreatureType.Tumerok,            "Tumerok",            20, 40),
            (CreatureType.AunTumerok,         "Aun Tumerok",        15, 30),
            (CreatureType.HeaTumerok,         "Hea Tumerok",        15, 30),
            (CreatureType.Mite,               "Mite",               25, 50),
            (CreatureType.Tusker,             "Tusker",             20, 40),
            (CreatureType.PhyntosWasp,        "Phyntos Wasp",       20, 40),
            (CreatureType.Rat,                "Rat",                30, 60),
            (CreatureType.Auroch,             "Auroch",             20, 40),
            (CreatureType.Golem,              "Golem",              15, 30),
            (CreatureType.Undead,             "Undead",             25, 50),
            (CreatureType.Gromnie,            "Gromnie",            20, 40),
            (CreatureType.Reedshark,          "Reedshark",          25, 50),
            (CreatureType.Armoredillo,        "Armoredillo",        20, 40),
            (CreatureType.Virindi,            "Virindi",            15, 30),
            (CreatureType.Wisp,               "Wisp",               20, 40),
            (CreatureType.Knathtead,          "Knath'taed",         20, 40),
            (CreatureType.Shadow,             "Shadow",             20, 35),
            (CreatureType.Mattekar,           "Mattekar",           20, 40),
            (CreatureType.Mumiyah,            "Mumiyah",            20, 40),
            (CreatureType.Rabbit,             "Rabbit",             25, 50),
            (CreatureType.Sclavus,            "Sclavus",            20, 40),
            (CreatureType.ShallowsShark,      "Shallows Shark",     20, 40),
            (CreatureType.Monouga,            "Monouga",            15, 30),
            (CreatureType.Zefir,              "Zefir",              15, 30),
            (CreatureType.Skeleton,           "Skeleton",           30, 50),
            (CreatureType.Human,              "Human",              20, 40),
            (CreatureType.Shreth,             "Shreth",             25, 50),
            (CreatureType.Chittick,           "Chittick",           20, 40),
            (CreatureType.Moarsman,           "Moarsman",           25, 50),
            (CreatureType.Slithis,            "Slithis",            20, 40),
            (CreatureType.Deru,               "Deru",               15, 30),
            (CreatureType.FireElemental,      "Fire Elemental",     15, 30),
            (CreatureType.LightningElemental, "Lightning Elemental", 15, 30),
            (CreatureType.AcidElemental,      "Acid Elemental",     15, 30),
            (CreatureType.FrostElemental,     "Frost Elemental",    15, 30),
            (CreatureType.Elemental,          "Elemental",          20, 40),
            (CreatureType.Rockslide,          "Rockslide",          20, 40),
            (CreatureType.Grievver,           "Grievver",           15, 30),
            (CreatureType.Niffis,             "Niffis",             20, 40),
            (CreatureType.Ursuin,             "Ursuin",             20, 40),
            (CreatureType.Crystal,            "Crystal",            20, 40),
            (CreatureType.HollowMinion,       "Hollow Minion",      20, 35),
            (CreatureType.Idol,               "Idol",               20, 40),
            (CreatureType.Empyrean,           "Empyrean",           15, 30),
            (CreatureType.Carenzi,            "Carenzi",            15, 30),
            (CreatureType.Siraluun,           "Siraluun",           15, 30),
            (CreatureType.Simulacrum,         "Simulacrum",         15, 30),
            (CreatureType.AlteredHuman,       "Altered Human",      20, 40),
            (CreatureType.Margul,             "Margul",             20, 40),
            (CreatureType.Chicken,            "Chicken",            25, 50),
            (CreatureType.BleachedRabbit,     "Bleached Rabbit",    25, 50),
            (CreatureType.NastyRabbit,        "Nasty Rabbit",       25, 50),
            (CreatureType.GrimacingRabbit,    "Grimacing Rabbit",   25, 50),
            (CreatureType.Burun,              "Burun",              20, 40),
            (CreatureType.Ghost,              "Ghost",              20, 40),
            (CreatureType.Fiun,               "Fiun",               15, 30),
            (CreatureType.Eater,              "Eater",              20, 40),
            (CreatureType.Ruschk,             "Ruschk",             20, 40),
            (CreatureType.Thrungus,           "Thrungus",           20, 40),
            (CreatureType.ViamontianKnight,   "Viamontian Knight",  15, 30),
            (CreatureType.Remoran,            "Remoran",            20, 40),
            (CreatureType.Swarm,              "Swarm",              30, 60),
            (CreatureType.Moar,               "Moar",               20, 40),
            (CreatureType.EnchantedArms,      "Enchanted Arms",     20, 40),
            (CreatureType.Sleech,             "Sleech",             20, 40),
            (CreatureType.Mukkir,             "Mukkir",             20, 40),
            (CreatureType.Merwart,            "Merwart",            20, 40),
            (CreatureType.Harvest,            "Harvest",            20, 40),
            (CreatureType.Energy,             "Energy",             20, 40),
            (CreatureType.Apparition,         "Apparition",         20, 40),
            (CreatureType.Touched,            "Touched",            20, 40),
            (CreatureType.BlightedMoarsman,   "Blighted Moarsman",  20, 40),
            (CreatureType.GearKnight,         "Gear Knight",        15, 30),
            (CreatureType.Gurog,              "Gurog",              20, 40),
            (CreatureType.Anekshay,           "A'nekshay",          15, 30),
        };

        public static readonly (uint wcid, string name)[] GlobalItemQuestPool =
        {
            (3669, "Drudge Charm"),
            (3670, "Copper Golem Heart"),
            (3671, "Granite Golem Heart"),
            (3672, "Iron Golem Heart"),
            (3673, "Wood Golem Heart"),
            (3674, "Ash Gromnie Tooth"),
            (3675, "Ivory Gromnie Tooth"),
            (3676, "Jade Gromnie Tooth"),
            (3677, "Swamp Gromnie Tooth"),
            (3678, "Olthoi Carapace"),
            (3679, "Olthoi Claw"),
            (4232, "Small Armoredillo Hide"),
            (4233, "Armoredillo Hide"),
            (4234, "Large Armoredillo Hide"),
            (4235, "Thin Gromnie Hide"),
            (4236, "Gromnie Hide"),
            (4237, "Thick Gromnie Hide"),
            (4238, "Small Reedshark Hide"),
            (4239, "Reedshark Hide"),
            (4240, "Small Mattekar Hide"),
            (4241, "Mattekar Hide"),
            (4258, "Slithis Eyestalk"),
            (4266, "Old Skeleton Bones"),
            (6055, "Cracked Crystal Shard"),
            (6056, "Small Crystal Shard"),
            (6057, "Tiny Crystal Shard"),
            (6058, "Shadow Shard"),
            (6059, "Shadow Sliver"),
            (6060, "Shadow Speck"),
        };

        public static IReadOnlyList<CreatureType> SlayerTypes { get; } = GlobalQuestPool
            .Select(entry => entry.type)
            .Distinct()
            .ToList();

        public static CreatureType RollSlayerType()
        {
            return SlayerTypes[ThreadSafeRandom.Next(0, SlayerTypes.Count - 1)];
        }
    }
}
