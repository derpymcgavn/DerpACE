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
        /// Points healed per proc based on number of Vampiric Jewelry pieces equipped (1-based index).
        /// 1 piece = 3 pts, 2 = 5, 3 = 6, 4 = 7, 5 = 8, 6 = 9, 7+ = 10 (hard cap).
        /// </summary>
        private static int GetVampiricProcPoints(int pieceCount)
        {
            switch (pieceCount)
            {
                case 1:  return 3;
                case 2:  return 5;
                case 3:  return 6;
                case 4:  return 7;
                case 5:  return 8;
                case 6:  return 9;
                default: return 10;  // 7+ pieces — hard cap
            }
        }

        /// <summary>
        /// Called from on-hit damage resolution. Counts all equipped Vampiric jewelry pieces,
        /// fires a single proc roll, and heals using the piece-count DR table.
        /// Each piece may target a different vital; all contribute to a single combined proc event.
        /// </summary>
        public uint TryProcVampiricJewelryOnHit()
        {
            if (!IsAlive)
                return 0;

            var procChance = DerpACEConfig.VampiricJewelryOnHitProcChance;
            if (procChance <= 0)
                return 0;

            // Collect equipped Vampiric jewelry
            var pieces = new List<WorldObject>();
            foreach (var item in EquippedObjects.Values)
            {
                if (item.GetProperty(PropertyBool.IsVampiricJewelry) == true)
                    pieces.Add(item);
            }

            if (pieces.Count == 0)
                return 0;

            // Single proc roll for the whole set
            if (ThreadSafeRandom.Next(0.0f, 1.0f) >= procChance)
                return 0;

            var totalPts = GetVampiricProcPoints(pieces.Count);
            var mult = DerpACEConfig.VampiricJewelryOnHitMultiplier;

            var totalHealthHealed = 0;
            var totalStaminaRestored = 0;
            var totalManaRestored = 0;

            // Distribute points evenly across pieces (each piece contributes its vital type)
            var ptsPerPiece = Math.Max(1, (int)Math.Round((double)totalPts / pieces.Count));

            foreach (var item in pieces)
            {
                var vitalIdx = item.GetProperty(PropertyInt.VampiricJewelryVital) ?? 0;
                if (vitalIdx < 0 || vitalIdx > 2)
                    vitalIdx = 0;

                var vital = GetVampiricVital(vitalIdx);
                if (vital == null || vital.Current >= vital.MaxValue)
                    continue;

                var amount = (int)Math.Round(ptsPerPiece * mult);
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
