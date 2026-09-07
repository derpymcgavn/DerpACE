using System;
using System.Collections.Generic;
using System.Text;

using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    public static partial class LootGenerationFactory
    {
        private static readonly string[] MutatorAuditWeaponMutators =
        {
            "thief",
            "quickening",
            "fencer",
            "pugilist",
            "ravager",
            "warden",
            "lugianhammer",
            "resolute",
            "polebreaker",
            "sentinel",
            "stalker",
            "breacher",
            "dinnerware",
            "discus",
            "platter",
            "dartflinger",
            "reaper",
            "gravecaller",
            "archmagi",
            "shadowclone",
            "shadowshot",
            "secondshadow",
            "hierophant",
            "skybreaker",
            "stormcaller",
            "orbitweaver",
            "confusion",
            "opportunist",
            "executioner",
        };

        private static readonly string[] MutatorAuditShieldMutators =
        {
            "defender",
            "thorns",
            "bashing",
            "reflection",
            "spellmirror",
        };

        private static readonly string[] MutatorAuditArmorMutators =
        {
            "culinarian",
            "alchemist",
            "alchemicalinstability",
            "unarmed",
            "healingdance",
            "rejuvenatingdance",
            "replenishingdance",
            "armorsort",
            "battlemage",
        };

        public static string BuildMutatorAuditReport(int tier = 8, int maxAttempts = 100)
        {
            tier = Math.Clamp(tier, 1, 100);
            maxAttempts = Math.Clamp(maxAttempts, 1, 1000);

            var profile = new TreasureDeath
            {
                Tier = tier,
                LootQualityMod = 0,
            };

            var sb = new StringBuilder();
            sb.AppendLine("DerpACE Mutator Audit");
            sb.AppendLine("---------------------");
            sb.AppendLine($"Tier: {tier}");
            sb.AppendLine($"Armor retries per mutator: {maxAttempts}");
            sb.AppendLine();

            sb.AppendLine("Weapon / caster mutators");
            foreach (var mutator in MutatorAuditWeaponMutators)
            {
                var item = CreateWeapon(profile, true, mutator, tier);
                var passed = HasWeaponMutator(item, mutator);
                AppendMutatorAuditLine(sb, mutator, passed, item);
            }

            sb.AppendLine();
            sb.AppendLine("Shield mutators");
            foreach (var mutator in MutatorAuditShieldMutators)
            {
                var item = TryGenerateArmorMutatorSample(profile, tier, mutator, true, maxAttempts, HasShieldMutator);
                var passed = item != null && HasShieldMutator(item, mutator);
                AppendMutatorAuditLine(sb, mutator, passed, item);
            }

            sb.AppendLine();
            sb.AppendLine("Armor / clothing mutators");
            foreach (var mutator in MutatorAuditArmorMutators)
            {
                var item = TryGenerateArmorMutatorSample(profile, tier, mutator, true, maxAttempts, HasArmorMutator)
                    ?? TryGenerateArmorMutatorSample(profile, tier, mutator, false, maxAttempts, HasArmorMutator);
                var passed = item != null && HasArmorMutator(item, mutator);
                AppendMutatorAuditLine(sb, mutator, passed, item);
            }

            return sb.ToString();
        }

        private static WorldObject TryGenerateArmorMutatorSample(TreasureDeath profile, int tier, string mutator, bool isArmor, int maxAttempts, Func<WorldObject, string, bool> predicate)
        {
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var item = CreateArmor(profile, true, isArmor, tier, mutator);
                if (predicate(item, mutator))
                    return item;
            }

            return null;
        }

        private static void AppendMutatorAuditLine(StringBuilder sb, string mutator, bool passed, WorldObject item)
        {
            var state = passed ? "PASS" : "FAIL";
            if (item == null)
            {
                sb.AppendLine($"{state,-4} {mutator,-24} no compatible sample generated");
                return;
            }

            var bits = new List<string>
            {
                $"0x{item.WeenieClassId:X8}",
                item.Name ?? "(unnamed)",
            };

            if (item.IconOverlayId.HasValue)
                bits.Add($"overlay=0x{item.IconOverlayId.Value:X8}");
            if (item.IconUnderlayId.HasValue)
                bits.Add($"underlay=0x{item.IconUnderlayId.Value:X8}");
            if (item.CooldownId.HasValue)
                bits.Add($"cooldown={item.CooldownId.Value}/{item.CooldownDuration:0.#}s");

            AppendFloat(bits, item, PropertyFloat.ProcSpellRate, "procspell");
            AppendFloat(bits, item, PropertyFloat.RicochetProcChance, "ricochet");
            AppendFloat(bits, item, PropertyFloat.DinnerwareSpinProcChance, "dinnerware");
            AppendFloat(bits, item, PropertyFloat.ShadowCloneProcChance, "shadow");
            AppendFloat(bits, item, PropertyFloat.LugianHammerThrowProcChance, "stonehand");
            AppendFloat(bits, item, PropertyFloat.PugilistProcChance, "pugilist");
            AppendFloat(bits, item, PropertyFloat.ShieldBashingProcChance, "bash");
            AppendFloat(bits, item, PropertyFloat.ShieldThornsReflectPct, "thorns");
            AppendFloat(bits, item, PropertyFloat.AlchemistSplashProcChance, "splash");
            AppendFloat(bits, item, PropertyFloat.AlchemicalInstabilityProcChance, "instability");

            sb.AppendLine($"{state,-4} {mutator,-24} {string.Join(" | ", bits)}");
        }

        private static void AppendFloat(List<string> bits, WorldObject item, PropertyFloat property, string label)
        {
            var value = item.GetProperty(property);
            if (value.HasValue)
                bits.Add($"{label}={value.Value:0.###}");
        }
    }
}