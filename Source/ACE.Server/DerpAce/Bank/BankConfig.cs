using System.Collections.Generic;

namespace ACE.Server.DerpAce.Bank
{
    /// <summary>
    /// Runtime-static configuration for the DerpACE Bank system.
    /// Values are loaded from DerpAce.json by DerpAceConfigManager.Apply().
    /// </summary>
    public static class BankConfig
    {
        // ── Section toggle ────────────────────────────────────────────────────
        public static bool EnableBank { get; set; } = true;

        // ── Behaviour flags ───────────────────────────────────────────────────
        /// <summary>When selling to a vendor, deposit pyreal payout directly into the bank instead of inventory.</summary>
        public static bool DirectDeposit { get; set; } = true;

        /// <summary>When buying from a vendor, draw from banked pyreals first (then inventory).</summary>
        public static bool VendorsUseBank { get; set; } = true;

        /// <summary>Cap pyreals lost on death (ignored when &lt; 0).</summary>
        public static int MaxCoinsDropped { get; set; } = 1_000_000;

        /// <summary>When depositing more than the max, clamp to max instead of rejecting.</summary>
        public static bool ExcessSetToMax { get; set; } = true;

        // ── PropertyInt64 slot for the cash (pyreal) balance ─────────────────
        /// <summary>The PropertyInt64 ID used to store the player's banked pyreal balance.</summary>
        public static int CashProperty { get; set; } = 39999;

        // ── Bankable item definitions ─────────────────────────────────────────
        public static List<BankItem> Items { get; set; } = new List<BankItem>
        {
            new BankItem { Name = "MMD",                    Id = 20630,   Prop = 40000 },
            new BankItem { Name = "Infused Amber Shard",    Id = 52968,   Prop = 40001 },
            new BankItem { Name = "Small Olthoi Venom Sac", Id = 36376,   Prop = 40002 },
            new BankItem { Name = "A'nekshay Token",        Id = 44240,   Prop = 40003 },
            new BankItem { Name = "Ornate Gear Marker",     Id = 43142,   Prop = 40004 },
            new BankItem { Name = "Colosseum Coin",         Id = 36518,   Prop = 40005 },
            new BankItem { Name = "Ancient Mhoire Coin",    Id = 35383,   Prop = 40006 },
            new BankItem { Name = "Promissory Note",        Id = 43901,   Prop = 40007 },
            new BankItem { Name = "Derp Coin",              Id = 7000011, Prop = 40008 },
            new BankItem { Name = "Cakru Idol",             Id = 238651,  Prop = 40009 },
            new BankItem { Name = "Frozen Tome",            Id = 2238932, Prop = 40010 },
            new BankItem { Name = "Tumerok Signet Ring",    Id = 3238020, Prop = 40011 },
        };

        // ── Currency definitions (items treated as pyreal equivalents) ────────
        public static List<BankCurrency> Currencies { get; set; } = new List<BankCurrency>
        {
            new BankCurrency { Name = "Pyreal",      Id = 273,   Value = 1       },
            new BankCurrency { Name = "I",           Id = 2621,  Value = 100     },
            new BankCurrency { Name = "V",           Id = 2622,  Value = 500     },
            new BankCurrency { Name = "X",           Id = 2623,  Value = 1000    },
            new BankCurrency { Name = "L",           Id = 2624,  Value = 5000    },
            new BankCurrency { Name = "C",           Id = 2625,  Value = 10000   },
            new BankCurrency { Name = "D",           Id = 2626,  Value = 50000   },
            new BankCurrency { Name = "M",           Id = 2627,  Value = 100000  },
            new BankCurrency { Name = "CL",          Id = 7374,  Value = 15000   },
            new BankCurrency { Name = "CC",          Id = 7375,  Value = 20000   },
            new BankCurrency { Name = "CCL",         Id = 7376,  Value = 25000   },
            new BankCurrency { Name = "DCCL",        Id = 7377,  Value = 75000   },
            new BankCurrency { Name = "MD",          Id = 20628, Value = 150000  },
            new BankCurrency { Name = "MM",          Id = 20629, Value = 200000  },
            new BankCurrency { Name = "MMD",         Id = 20630, Value = 250000  },
            new BankCurrency { Name = "Low-Stakes",  Id = 44715, Value = 100000  },
            new BankCurrency { Name = "Mid-Stakes",  Id = 44716, Value = 200000  },
            new BankCurrency { Name = "High-Stakes", Id = 44717, Value = 500000  },
        };
    }

    public class BankItem
    {
        public string Name { get; set; }
        /// <summary>WCID of the item in-game.</summary>
        public uint Id { get; set; }
        /// <summary>PropertyInt64 slot used to store the banked count.</summary>
        public int Prop { get; set; }
    }

    public class BankCurrency
    {
        public string Name { get; set; }
        /// <summary>WCID of the currency stack.</summary>
        public uint Id { get; set; }
        /// <summary>Pyreal value of one unit of this currency.</summary>
        public long Value { get; set; }
    }
}
