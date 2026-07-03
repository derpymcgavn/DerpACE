using System;
using System.Collections.Generic;
using System.Linq;

using ACE.Entity.Enum;

namespace ACE.Server.WorldObjects
{
    public class ArmorSortDamageBreakdown
    {
        public DamageType DamageType { get; set; }
        public List<int> RawBonuses { get; } = new List<int>();
        public int TotalBonus { get; set; }
        public bool Applies => TotalBonus > 0;

        public string RawBonusText => RawBonuses.Count == 0 ? "none" : string.Join(", ", RawBonuses.Select(b => $"+{b}"));
    }

    partial class Player
    {
        public int GetArmorSortDamageBonus(DamageType damageType)
        {
            return GetArmorSortDamageBreakdown(damageType).TotalBonus;
        }

        public ArmorSortDamageBreakdown GetArmorSortDamageBreakdown(DamageType damageType)
        {
            var breakdown = new ArmorSortDamageBreakdown { DamageType = damageType };

            if (damageType == DamageType.Undef || damageType == DamageType.Base)
                return breakdown;

            breakdown.RawBonuses.AddRange(EquippedObjects.Values
                .Where(i => IsActiveArmorSortPiece(i, damageType))
                .Select(i => Math.Min(3, Math.Max(1, i.ArmorSortDamageBonus.Value)))
                .OrderByDescending(bonus => bonus));

            breakdown.TotalBonus = ApplyArmorSortDiminishingReturns(breakdown.RawBonuses);
            return breakdown;
        }

        private static bool IsActiveArmorSortPiece(WorldObject item, DamageType damageType)
        {
            return item != null
                && item.Wielder != null
                && item.CurrentWieldedLocation != null
                && item.ArmorSortDamageType == damageType
                && (item.ArmorSortDamageBonus ?? 0) > 0;
        }

        private static int ApplyArmorSortDiminishingReturns(List<int> bonuses)
        {
            var total = 0.0;
            var weight = 1.0;

            foreach (var bonus in bonuses)
            {
                total += bonus * weight;
                weight *= 0.5;
            }

            return (int)Math.Round(total, MidpointRounding.AwayFromZero);
        }
    }
}
