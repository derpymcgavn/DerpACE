using System;
using System.Linq;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.DerpAce.Bank
{
    public static class BankExtensions
    {
        private const PropertyBool DirectDepositDisabled = (PropertyBool)49999;

        public static long GetCash(this Player player)
            => player.GetProperty((PropertyInt64)BankConfig.CashProperty) ?? 0L;

        public static void IncCash(this Player player, long delta)
        {
            var prop = (PropertyInt64)BankConfig.CashProperty;
            var cur  = player.GetProperty(prop) ?? 0L;
            player.SetProperty(prop, Math.Max(0L, cur + delta));
        }

        public static long GetBanked(this Player player, int propId)
            => player.GetProperty((PropertyInt64)propId) ?? 0L;

        public static void IncBanked(this Player player, int propId, long delta)
        {
            var prop = (PropertyInt64)propId;
            var cur  = player.GetProperty(prop) ?? 0L;
            player.SetProperty(prop, Math.Max(0L, cur + delta));
        }

        public static long DepositAllCurrency(this Player player)
        {
            long total = 0;
            foreach (var cur in BankConfig.Currencies)
            {
                var stacks = player.GetInventoryItemsOfWCID(cur.Id).ToList();
                foreach (var stack in stacks)
                {
                    var stackValue = (long)(stack.StackSize ?? 1) * cur.Value;
                    if (player.TryConsumeFromInventoryWithNetworking(stack))
                        total += stackValue;
                }
            }
            if (total > 0) player.IncCash(total);
            return total;
        }

        public static bool UseDirectDeposit(this Player player)
        {
            if (!BankConfig.EnableBank || !BankConfig.DirectDeposit) return false;
            return !(player.GetProperty(DirectDepositDisabled) ?? false);
        }

        public static bool SpendWithBank(this Player player, long cost)
        {
            if (cost <= 0) return true;

            var banked = player.GetCash();
            var physical = player.GetInventoryItemsOfWCID(273).Sum(c => (long)(c.StackSize ?? 1));
            if (banked + physical < cost)
                return false;

            var bankedSpent = Math.Min(banked, cost);
            if (bankedSpent > 0)
            {
                player.IncCash(-bankedSpent);
                cost -= bankedSpent;
            }

            var stacks = player.GetInventoryItemsOfWCID(273).OrderBy(c => c.StackSize ?? 0).ToList();
            foreach (var stack in stacks)
            {
                if (cost <= 0) break;

                var qty = stack.StackSize ?? 1;
                var spend = (int)Math.Min(qty, cost);
                if (!player.TryConsumeFromInventoryWithNetworking(stack, spend))
                {
                    player.IncCash(bankedSpent);
                    return false;
                }

                cost -= spend;
            }

            return cost == 0;
        }

        public static void ToggleDirectDeposit(this Player player)
        {
            var cur = player.GetProperty(DirectDepositDisabled) ?? false;
            player.SetProperty(DirectDepositDisabled, !cur);
        }

        public static bool IsDirectDepositDisabled(this Player player)
            => player.GetProperty(DirectDepositDisabled) ?? false;

        public static bool TryFindBankItem(string nameOrId, out BankItem result)
        {
            result = null;
            if (uint.TryParse(nameOrId, out var wcid))
                result = BankConfig.Items.FirstOrDefault(i => i.Id == wcid);
            else
                result = BankConfig.Items.FirstOrDefault(i => i.Name.Equals(nameOrId, System.StringComparison.OrdinalIgnoreCase));
            return result != null;
        }

        public static bool TryFindCurrency(string nameOrId, out BankCurrency result)
        {
            result = null;
            if (uint.TryParse(nameOrId, out var wcid))
                result = BankConfig.Currencies.FirstOrDefault(c => c.Id == wcid);
            else
                result = BankConfig.Currencies.FirstOrDefault(c => c.Name.Equals(nameOrId, System.StringComparison.OrdinalIgnoreCase));
            return result != null;
        }
    }
}
