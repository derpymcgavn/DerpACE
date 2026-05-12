using System;
using System.Collections.Generic;

using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects
{
    partial class Player
    {
        /// <summary>
        /// Last unix time (seconds) the Vampiric jewelry passive regen ticked for this player.
        /// </summary>
        private double _lastVampiricJewelryTick;

        /// <summary>
        /// Per-heartbeat passive vital-steal from equipped Vampiric jewelry (rings/necklaces/bracelets).
        /// Buckets pieces by their rolled vital flavor (Health/Stamina/Mana), then applies the
        /// diminishing-returns curve based on equipped piece count within each flavor.
        /// </summary>
        private void TickVampiricJewelry(double currentUnixTime)
        {
            if (!IsAlive)
                return;

            var interval = DerpACEConfig.VampiricJewelryRegenIntervalSeconds;
            if (interval <= 0)
                return;

            if (_lastVampiricJewelryTick != 0 && (currentUnixTime - _lastVampiricJewelryTick) < interval)
                return;

            _lastVampiricJewelryTick = currentUnixTime;

            // [0]=Health, [1]=Stamina, [2]=Mana
            var pointsByVital = new int[3];
            var countByVital = new int[3];

            foreach (var item in EquippedObjects.Values)
            {
                if (item.GetProperty(PropertyBool.IsVampiricJewelry) != true)
                    continue;

                var pts = item.GetProperty(PropertyInt.VampiricJewelryPoints) ?? 0;
                if (pts <= 0)
                    continue;

                var vital = item.GetProperty(PropertyInt.VampiricJewelryVital) ?? 0;
                if (vital < 0 || vital > 2)
                    vital = 0;

                pointsByVital[vital] += pts;
                countByVital[vital]++;
            }

            var dr = DerpACEConfig.VampiricJewelryDiminishingReturns;
            for (var v = 0; v < 3; v++)
            {
                if (pointsByVital[v] <= 0)
                    continue;

                var vital = GetVampiricVital(v);
                if (vital == null || vital.Current >= vital.MaxValue)
                    continue;

                float drMult;
                if (dr == null || dr.Length == 0)
                    drMult = 1.0f;
                else if (countByVital[v] >= dr.Length)
                    drMult = dr[dr.Length - 1];
                else
                    drMult = dr[countByVital[v]];

                var amount = (int)Math.Round(pointsByVital[v] * drMult);
                if (amount < 1)
                    amount = 1;

                var applied = UpdateVitalDelta(vital, amount);
                if (v == 0 && applied > 0)
                    DamageHistory.OnHeal((uint)applied);
            }
        }

        /// <summary>
        /// Called from on-hit damage resolution. For each equipped Vampiric jewelry piece, rolls a small chance
        /// to immediately restore the wielder's matching vital for points * multiplier. Returns total amount
        /// restored for the Health flavor only (used for the on-hit chat message and visual).
        /// </summary>
        public uint TryProcVampiricJewelryOnHit()
        {
            if (!IsAlive)
                return 0;

            var procChance = DerpACEConfig.VampiricJewelryOnHitProcChance;
            if (procChance <= 0)
                return 0;

            var mult = DerpACEConfig.VampiricJewelryOnHitMultiplier;
            var totalHealthHealed = 0;
            var totalStaminaRestored = 0;
            var totalManaRestored = 0;

            foreach (var item in EquippedObjects.Values)
            {
                if (item.GetProperty(PropertyBool.IsVampiricJewelry) != true)
                    continue;

                var pts = item.GetProperty(PropertyInt.VampiricJewelryPoints) ?? 0;
                if (pts <= 0)
                    continue;

                if (ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                    continue;

                var vitalIdx = item.GetProperty(PropertyInt.VampiricJewelryVital) ?? 0;
                if (vitalIdx < 0 || vitalIdx > 2)
                    vitalIdx = 0;

                var vital = GetVampiricVital(vitalIdx);
                if (vital == null || vital.Current >= vital.MaxValue)
                    continue;

                var amount = (int)Math.Round(pts * mult);
                if (amount < 1)
                    amount = 1;

                var applied = UpdateVitalDelta(vital, amount);
                if (applied <= 0)
                    continue;

                switch (vitalIdx)
                {
                    case 1:
                        totalStaminaRestored += applied;
                        break;
                    case 2:
                        totalManaRestored += applied;
                        break;
                    default:
                        totalHealthHealed += applied;
                        DamageHistory.OnHeal((uint)applied);
                        break;
                }
            }

            if (totalStaminaRestored > 0)
            {
                ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpYellow);
                // Fellowship channel renders bright yellow client-side, matching the stamina visual.
                Session?.Network.EnqueueSend(new GameMessageSystemChat($"+{totalStaminaRestored} stamina drained [Vampiric Jewelry]", ChatMessageType.Fellowship));
            }
            if (totalManaRestored > 0)
            {
                ApplyVisualEffects(ACE.Entity.Enum.PlayScript.HealthUpBlue);
                // Magic channel renders blue client-side, matching the mana visual.
                Session?.Network.EnqueueSend(new GameMessageSystemChat($"+{totalManaRestored} mana siphoned [Vampiric Jewelry]", ChatMessageType.Magic));
            }

            return (uint)totalHealthHealed;
        }

        private ACE.Server.WorldObjects.Entity.CreatureVital GetVampiricVital(int vitalIdx)
        {
            switch (vitalIdx)
            {
                case 1: return Stamina;
                case 2: return Mana;
                default: return Health;
            }
        }
    }
}
