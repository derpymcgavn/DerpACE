using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories
{
    /// <summary>
    /// Deterministic balance pass for DerpACE weapon/shield/caster mutators.
    /// This is intentionally analytical-lightweight so admins can run it in game without touching the database.
    /// </summary>
    public static class MutatorBalanceSimulator
    {
        private const double DefaultTargetLifeHits = 18.0;

        public static string Run(int attacks, double baseDamage, double attacksPerSecond, double hitRate, double evadeRate, double armorStopped)
        {
            attacks = Math.Clamp(attacks, 1_000, 2_000_000);
            baseDamage = Clamp(baseDamage, 1.0, 100_000.0);
            attacksPerSecond = Clamp(attacksPerSecond, 0.2, 6.0);
            hitRate = Clamp(hitRate, 0.05, 1.0);
            evadeRate = Clamp(evadeRate, 0.0, 0.95);
            if (hitRate + evadeRate > 1.0)
                evadeRate = 1.0 - hitRate;
            armorStopped = Clamp(armorStopped, 0.0, baseDamage * 5.0);

            var rows = new List<Row>();
            var seed = 0xD3AACE;

            AddDamageProc(rows, "Thief dagger", DerpACEConfig.ThievesDaggerProcChance, DerpACEConfig.ThievesDaggerProcBonus, 0.0, attacks, hitRate, attacksPerSecond, seed++, "also shadowsteps and applies seam penalty");
            AddSpeedProc(rows, "Quickening dagger", AvgPct(DerpACEConfig.QuickeningDaggerProcMin, DerpACEConfig.QuickeningDaggerProcMax), AvgPct(DerpACEConfig.QuickeningDaggerSpeedMin, DerpACEConfig.QuickeningDaggerSpeedMax), Avg(DerpACEConfig.QuickeningDaggerDurationMin, DerpACEConfig.QuickeningDaggerDurationMax), 10.0, attacks, hitRate, attacksPerSecond, seed++);
            AddArmorPierce(rows, "Fencer blade", AvgPct(DerpACEConfig.FencerPierceProcMin, DerpACEConfig.FencerPierceProcMax), AvgPct(DerpACEConfig.FencerPierceMin, DerpACEConfig.FencerPierceMax), 0.0, attacks, baseDamage, hitRate, armorStopped, attacksPerSecond, seed++, "riposte/parry utility is not included in DPS");
            AddDamageProc(rows, "Pugilist flurry", 0.08, 0.35, Player.PugilistCooldownSeconds, attacks, hitRate, attacksPerSecond, seed++, "hardcoded 6-10% roll, 35% extra hit");
            AddDamageProc(rows, "Pugilist rake", 0.08, 0.45, Player.PugilistCooldownSeconds, attacks, hitRate, attacksPerSecond, seed++, "hardcoded 6-10% roll, 45% trauma over time");
            AddDamageProc(rows, "Ravager axe", AvgPct(DerpACEConfig.RavagerProcMin, DerpACEConfig.RavagerProcMax), AvgPct(DerpACEConfig.RavagerBleedMin, DerpACEConfig.RavagerBleedMax), 0.0, attacks, hitRate, attacksPerSecond, seed++, "two-handed axes scale by configured two-hand multiplier");
            AddDamageProc(rows, "Ravager hammer cleave", DerpACEConfig.RavagerHammerCleaveChance, DerpACEConfig.RavagerHammerCleaveDamageScale, 0.0, attacks, hitRate, attacksPerSecond, seed++, "multi-target only, up to configured secondary target count", true);
            AddUtility(rows, "Warden maul", AvgPct(DerpACEConfig.WardenProcMin, DerpACEConfig.WardenProcMax), "defense debuff, cooldown-limited; direct damage neutral");
            AddUtility(rows, "Resolute blade", 1.0, "kill-fed sustain/defense posture; direct damage neutral");
            AddPolebreaker(rows, attacks, hitRate);
            AddUtility(rows, "Sentinel spear", DerpACEConfig.SentinelSpearProcChance, "stamina-to-poise tanking loop; direct damage neutral");
            AddDamageProc(rows, "Stonehand hammer", DerpACEConfig.LugianHammerThrowProcChance, DerpACEConfig.LugianHammerThrowDamageScale, DerpACEConfig.LugianHammerThrowCooldownSeconds, attacks, hitRate, attacksPerSecond, seed++, "secondary target only; fallback does not hit primary", true);
            AddOpportunist(rows, attacks, hitRate, evadeRate, DerpACEConfig.OpportunistDamageBonus, DerpACEConfig.OpportunistWindowSeconds, attacksPerSecond, seed++);
            AddExecutioner(rows, DerpACEConfig.ExecutionerDamageBonus, DerpACEConfig.ExecutionerHealthThreshold);

            AddStalker(rows, attacks, hitRate, baseDamage, seed++);
            AddArmorPierce(rows, "Breacher crossbow", AvgPct(DerpACEConfig.BreacherArmorIgnoreMin, DerpACEConfig.BreacherArmorIgnoreMax), 1.0, 0.0, attacks, baseDamage, hitRate, armorStopped, attacksPerSecond, seed++, "recovers full armor stopped on proc");
            AddUtility(rows, "Reaper atlatl", AvgPct(DerpACEConfig.ReaperProcMin, DerpACEConfig.ReaperProcMax), "kill-fed heal; direct damage neutral");
            AddDamageProc(rows, "Dartflinger atlatl", AvgPct(DerpACEConfig.RicochetProcMin, DerpACEConfig.RicochetProcMax), DerpACEConfig.RicochetDamageScale, Player.RicochetCooldownSeconds, attacks, hitRate, attacksPerSecond, seed++, "secondary target only", true);
            AddDamageProc(rows, "Shadow volley", 0.03, 0.25 * 18.0 / 150.0, 0.0, attacks, hitRate, attacksPerSecond, seed++, "hardcoded shadow uptime approximation; very low sustained value");
            AddDamageProc(rows, "Second shadow", 0.03, 0.25 * 16.0 / 150.0, 0.0, attacks, hitRate, attacksPerSecond, seed++, "hardcoded shadow uptime approximation; very low sustained value");

            AddDamageProc(rows, "Archmagi caster", DerpACEConfig.ArchmagiProcChance, DerpACEConfig.ArchmagiDualCastDamageModifier, 0.0, attacks, hitRate, attacksPerSecond, seed++, "chains to another target when possible, otherwise same target");
            AddUtility(rows, "Hierophant caster", DerpACEConfig.HierophantHotProcChance, "healing amp/HoT/fellow echo; not offensive DPS");
            AddUtility(rows, "Gravecaller caster", 0.02, "corpse-raised pet, cooldown-limited; encounter utility");
            AddUtility(rows, "Bedlam caster", 0.025, "temporary monster charm; damage depends on nearby mobs");
            AddDamageProc(rows, "Umbral mirror caster", 0.04, 0.35 * 25.0 / 120.0, 0.0, attacks, hitRate, attacksPerSecond, seed++, "hardcoded shadow uptime approximation");

            AddArmorPierce(rows, "Thorns shield", 1.0, 0.10, 1.0, attacks, baseDamage, 1.0, baseDamage, attacksPerSecond, seed++, "defensive reflected damage, assumes one incoming hit per second");
            AddDamageProc(rows, "Bashing shield", 0.10, 0.10, 8.0, attacks, 1.0, attacksPerSecond, seed++, "defensive proc, assumes one incoming hit per second");
            AddArmorPierce(rows, "Projectile reflect shield", 0.10, 1.0, 6.0, attacks, baseDamage, 1.0, baseDamage, attacksPerSecond, seed++, "defensive missile reflect, assumes one incoming missile per second");
            AddUtility(rows, "Spell mirror shield", 0.10, "reflects a hostile spell on cooldown; damage depends on incoming spell");

            rows.Sort((a, b) => b.DirectPct.CompareTo(a.DirectPct));

            var sb = new StringBuilder(4096);
            sb.AppendLine("DerpACE mutator hard sim");
            sb.AppendLine($"attacks={attacks:N0}, baseDamage={baseDamage:0.##}, aps={attacksPerSecond:0.##}, hit={Pct(hitRate)}, evade={Pct(evadeRate)}, armorStopped={armorStopped:0.##}");
            sb.AppendLine("Target: sustained single-target direct damage should usually stay under +12%; +12-20% is watch; above +20% needs a deliberate reason.");
            sb.AppendLine();
            sb.AppendLine("Mutator                         Direct   Splash   Verdict   Notes");
            sb.AppendLine("------------------------------  -------  -------  --------  ----------------------------------------");
            foreach (var row in rows)
                sb.AppendLine($"{Trim(row.Name, 30),-30}  {Pct(row.DirectPct),7}  {Pct(row.SplashPct),7}  {row.Verdict,-8}  {row.Notes}");

            sb.AppendLine();
            sb.AppendLine("Recommendations:");
            foreach (var row in rows)
            {
                if (row.DirectPct > 0.20)
                    sb.AppendLine($"- {row.Name}: high sustained direct bonus; lower proc/damage scale or add/raise cooldown.");
                else if (row.DirectPct > 0.12)
                    sb.AppendLine($"- {row.Name}: watch item; keep it conditional or rare.");
            }

            sb.AppendLine("- Pugilist and shadow-clone family still use some hardcoded proc/uptime values; move those into Loot Lab if you want live tuning.");
            sb.AppendLine("- Splash values assume valid nearby targets. For boss-only fights, read the Direct column first.");
            return sb.ToString();
        }

        private static void AddDamageProc(List<Row> rows, string name, double procChance, double damageScale, double cooldownSeconds, int attacks, double hitRate, double aps, int seed, string notes, bool splash = false)
        {
            var pct = SimProc(attacks, hitRate, procChance, damageScale, cooldownSeconds, aps, seed);
            rows.Add(new Row(name, splash ? 0.0 : pct, splash ? pct : 0.0, notes));
        }

        private static void AddArmorPierce(List<Row> rows, string name, double procChance, double piercePct, double cooldownSeconds, int attacks, double baseDamage, double hitRate, double armorStopped, double aps, int seed, string notes)
        {
            var scale = baseDamage <= 0.0 ? 0.0 : (armorStopped / baseDamage) * piercePct;
            var pct = SimProc(attacks, hitRate, procChance, scale, cooldownSeconds, aps, seed);
            rows.Add(new Row(name, pct, 0.0, notes));
        }

        private static void AddSpeedProc(List<Row> rows, string name, double procChance, double speedPct, double duration, double cooldownSeconds, int attacks, double hitRate, double aps, int seed)
        {
            var uptime = SimUptime(attacks, hitRate, procChance, duration, cooldownSeconds, aps, seed);
            var pct = uptime * speedPct;
            rows.Add(new Row(name, pct, 0.0, $"attack-speed uptime model, uptime {Pct(uptime)}"));
        }

        private static void AddOpportunist(List<Row> rows, int attacks, double hitRate, double evadeRate, double bonus, double windowSeconds, double aps, int seed)
        {
            var rng = new Random(seed);
            var windowAttacks = Math.Max(1, (int)Math.Round(windowSeconds * aps));
            var armedFor = 0;
            var extra = 0.0;
            var baseHits = 0.0;

            for (var i = 0; i < attacks; i++)
            {
                if (armedFor > 0)
                    armedFor--;

                var roll = rng.NextDouble();
                if (roll < hitRate)
                {
                    baseHits++;
                    if (armedFor > 0)
                    {
                        extra += bonus;
                        armedFor = 0;
                    }
                }
                else if (roll < hitRate + evadeRate)
                {
                    armedFor = windowAttacks;
                }
            }

            rows.Add(new Row("Opportunist", baseHits <= 0.0 ? 0.0 : extra / baseHits, 0.0, "only after the weapon is evaded"));
        }

        private static void AddExecutioner(List<Row> rows, double bonus, double threshold)
        {
            rows.Add(new Row("Executioner", Clamp(bonus, 0.0, 5.0) * Clamp(threshold, 0.0, 1.0), 0.0, "assumes linear target time under threshold"));
        }

        private static void AddPolebreaker(List<Row> rows, int attacks, double hitRate)
        {
            var stackPct = AvgPct(DerpACEConfig.PolebreakerStackMin, DerpACEConfig.PolebreakerStackMax);
            var maxStacks = Math.Max(1, (int)Math.Round(Avg(DerpACEConfig.PolebreakerMaxStackMin, DerpACEConfig.PolebreakerMaxStackMax)));
            var totalBonus = 0.0;
            var hits = 0.0;
            var stacks = 0;
            var rng = new Random(0x50424B);

            for (var i = 0; i < attacks; i++)
            {
                if (rng.NextDouble() >= hitRate)
                {
                    stacks = 0;
                    continue;
                }

                stacks = Math.Min(maxStacks, stacks + 1);
                hits++;
                totalBonus += stackPct * stacks;
            }

            rows.Add(new Row("Polebreaker staff", hits <= 0.0 ? 0.0 : totalBonus / hits, 0.0, "assumes full-power attacks on same target; misses reset stacks"));
        }

        private static void AddStalker(List<Row> rows, int attacks, double hitRate, double baseDamage, int seed)
        {
            var proc = AvgPct(DerpACEConfig.StalkerProcMin, DerpACEConfig.StalkerProcMax);
            var bonus = AvgPct(DerpACEConfig.StalkerBonusMin, DerpACEConfig.StalkerBonusMax);
            var opening = proc * bonus;
            var sustained = opening / DefaultTargetLifeHits;
            rows.Add(new Row("Stalker bow", sustained, 0.0, $"opening shot averages {Pct(opening)}; sustained assumes {DefaultTargetLifeHits:0} hits per target"));
        }

        private static void AddUtility(List<Row> rows, string name, double chance, string notes)
        {
            rows.Add(new Row(name, 0.0, 0.0, $"proc/roll {Pct(chance)}; {notes}"));
        }

        private static double SimProc(int attacks, double hitRate, double procChance, double damageScale, double cooldownSeconds, double aps, int seed)
        {
            var rng = new Random(seed);
            var cooldownAttacks = Math.Max(0, (int)Math.Round(cooldownSeconds * aps));
            var cooldown = 0;
            var hits = 0.0;
            var extra = 0.0;

            for (var i = 0; i < attacks; i++)
            {
                if (cooldown > 0)
                    cooldown--;

                if (rng.NextDouble() >= hitRate)
                    continue;

                hits++;
                if (cooldown <= 0 && rng.NextDouble() < procChance)
                {
                    extra += damageScale;
                    cooldown = cooldownAttacks;
                }
            }

            return hits <= 0.0 ? 0.0 : extra / hits;
        }

        private static double SimUptime(int attacks, double hitRate, double procChance, double durationSeconds, double cooldownSeconds, double aps, int seed)
        {
            var rng = new Random(seed);
            var durationAttacks = Math.Max(1, (int)Math.Round(durationSeconds * aps));
            var cooldownAttacks = Math.Max(durationAttacks, (int)Math.Round(cooldownSeconds * aps));
            var active = 0;
            var cooldown = 0;
            var activeTicks = 0;

            for (var i = 0; i < attacks; i++)
            {
                if (active > 0)
                {
                    active--;
                    activeTicks++;
                }

                if (cooldown > 0)
                    cooldown--;

                if (cooldown <= 0 && rng.NextDouble() < hitRate && rng.NextDouble() < procChance)
                {
                    active = durationAttacks;
                    cooldown = cooldownAttacks;
                }
            }

            return attacks <= 0 ? 0.0 : Clamp((double)activeTicks / attacks, 0.0, 1.0);
        }

        private static double Avg(double min, double max) => (min + max) / 2.0;
        private static double AvgPct(double min, double max) => Avg(min, max) / 100.0;
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        private static string Pct(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
        private static string Trim(string text, int length) => text.Length <= length ? text : text.Substring(0, length);

        private sealed class Row
        {
            public Row(string name, double directPct, double splashPct, string notes)
            {
                Name = name;
                DirectPct = directPct;
                SplashPct = splashPct;
                Notes = notes;
            }

            public string Name { get; }
            public double DirectPct { get; }
            public double SplashPct { get; }
            public string Notes { get; }
            public string Verdict => DirectPct > 0.20 ? "HIGH" : DirectPct > 0.12 ? "WATCH" : "OK";
        }
    }
}