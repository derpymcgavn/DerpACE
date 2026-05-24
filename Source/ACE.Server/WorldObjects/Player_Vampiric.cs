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
        /// Passive vital-steal tick — disabled. Vampiric jewelry now uses on-hit proc only.
        /// </summary>
        private void TickVampiricJewelry(double currentUnixTime) { }

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
