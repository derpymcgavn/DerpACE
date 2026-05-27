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
                    total += (long)(stack.StackSize ?? 1) * cur.Value;
                    player.TryConsumeFromInventoryWithNetworking(stack);
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

        public static void SpendWithBank(this Player player, long cost)
        {
            if (cost <= 0) return;
            var banked = player.GetCash();
            if (banked >= cost) { player.IncCash(-cost); return; }
            if (banked > 0)    { player.IncCash(-banked); cost -= banked; }
            var stacks = player.GetInventoryItemsOfWCID(273).OrderBy(c => c.StackSize ?? 0).ToList();
            foreach (var stack in stacks)
            {
                if (cost <= 0) break;
                var qty = stack.StackSize ?? 1;
                if (qty <= cost) { cost -= qty; player.TryConsumeFromInventoryWithNetworking(stack); }
                else             { stack.SetStackSize((int)(qty - cost)); cost = 0; }
            }
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