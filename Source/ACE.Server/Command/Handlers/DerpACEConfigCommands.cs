using System.Globalization;
using System.Text;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACEConfigCommands
    {
        // @lootconfig list|set <key> <value>
        [CommandHandler("lootconfig", AccessLevel.Developer, CommandHandlerFlag.None, 1,
            "View or modify DerpACE loot item variables.",
            "list                    — print all current values\n" +
            "set <key> <value>       — change a value at runtime\n" +
            "\nKeys:\n" +
            "  defender.drop         DefenderShieldDropChance (float 0-1)\n" +
            "  defender.tier         DefenderShieldMinTier (int)\n" +
            "  defender.aggro        DefenderAggroBonus (float)\n" +
            "  archmagi.drop         ArchmagiDropChance (float 0-1)\n" +
            "  archmagi.tier         ArchmagiMinTier (int)\n" +
            "  archmagi.proc         ArchmagiProcChance (float 0-1)\n" +
            "  thief.drop            ThievesDaggerDropChance (float 0-1)\n" +
            "  thief.tier            ThievesDaggerMinTier (int)\n" +
            "  thief.proc            ThievesDaggerProcChance (float 0-1)\n" +
            "  thief.bonus           ThievesDaggerProcBonus (float 0-1)\n" +
            "  thief.aggro           ThievesDaggerAggroPenalty (float)\n" +
            "  sentinel.drop         SentinelSpearDropChance (float 0-1)\n" +
            "  sentinel.tier         SentinelSpearMinTier (int)\n" +
            "  sentinel.proc         SentinelSpearProcChance (float 0-1)\n" +
            "  sentinel.drain        SentinelSpearDrainPct (float 0-1)\n" +
            "  sentinel.return       SentinelSpearReturnMult (float)")]
        public static void HandleLootConfig(Session session, params string[] parameters)
        {
            var sub = parameters[0].ToLower();

            if (sub == "list")
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== DerpACE Loot Config ===");
                sb.AppendLine($"  defender.drop   = {DerpACEConfig.DefenderShieldDropChance:P0}  ({DerpACEConfig.DefenderShieldDropChance})");
                sb.AppendLine($"  defender.tier   = {DerpACEConfig.DefenderShieldMinTier}");
                sb.AppendLine($"  defender.aggro  = {DerpACEConfig.DefenderAggroBonus}");
                sb.AppendLine($"  archmagi.drop   = {DerpACEConfig.ArchmagiDropChance:P0}  ({DerpACEConfig.ArchmagiDropChance})");
                sb.AppendLine($"  archmagi.tier   = {DerpACEConfig.ArchmagiMinTier}");
                sb.AppendLine($"  archmagi.proc   = {DerpACEConfig.ArchmagiProcChance:P0}  ({DerpACEConfig.ArchmagiProcChance})");
                sb.AppendLine($"  thief.drop      = {DerpACEConfig.ThievesDaggerDropChance:P0}  ({DerpACEConfig.ThievesDaggerDropChance})");
                sb.AppendLine($"  thief.tier      = {DerpACEConfig.ThievesDaggerMinTier}");
                sb.AppendLine($"  thief.proc      = {DerpACEConfig.ThievesDaggerProcChance:P0}  ({DerpACEConfig.ThievesDaggerProcChance})");
                sb.AppendLine($"  thief.bonus     = {DerpACEConfig.ThievesDaggerProcBonus:P0}  ({DerpACEConfig.ThievesDaggerProcBonus})");
                sb.AppendLine($"  thief.aggro     = {DerpACEConfig.ThievesDaggerAggroPenalty}");
                sb.AppendLine($"  sentinel.drop   = {DerpACEConfig.SentinelSpearDropChance:P0}  ({DerpACEConfig.SentinelSpearDropChance})");
                sb.AppendLine($"  sentinel.tier   = {DerpACEConfig.SentinelSpearMinTier}");
                sb.AppendLine($"  sentinel.proc   = {DerpACEConfig.SentinelSpearProcChance:P0}  ({DerpACEConfig.SentinelSpearProcChance})");
                sb.AppendLine($"  sentinel.drain  = {DerpACEConfig.SentinelSpearDrainPct:P0}  ({DerpACEConfig.SentinelSpearDrainPct})");
                sb.AppendLine($"  sentinel.return = {DerpACEConfig.SentinelSpearReturnMult}");
                CommandHandlerHelper.WriteOutputInfo(session, sb.ToString().TrimEnd(), ChatMessageType.Broadcast);
                return;
            }

            if (sub == "set")
            {
                if (parameters.Length < 3)
                {
                    CommandHandlerHelper.WriteOutputInfo(session, "Usage: @lootconfig set <key> <value>", ChatMessageType.Broadcast);
                    return;
                }

                var key = parameters[1].ToLower();
                var raw = parameters[2];

                bool TryFloat(out float result) => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                bool TryInt(out int result) => int.TryParse(raw, out result);

                switch (key)
                {
                    case "defender.drop":
                        if (!TryFloat(out var dd)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DefenderShieldDropChance = dd;
                        break;
                    case "defender.tier":
                        if (!TryInt(out var dt)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.DefenderShieldMinTier = dt;
                        break;
                    case "defender.aggro":
                        if (!TryFloat(out var da)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DefenderAggroBonus = da;
                        break;

                    case "archmagi.drop":
                        if (!TryFloat(out var ad)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ArchmagiDropChance = ad;
                        break;
                    case "archmagi.tier":
                        if (!TryInt(out var at)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ArchmagiMinTier = at;
                        break;
                    case "archmagi.proc":
                        if (!TryFloat(out var ap)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ArchmagiProcChance = ap;
                        break;

                    case "thief.drop":
                        if (!TryFloat(out var tdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThievesDaggerDropChance = tdr;
                        break;
                    case "thief.tier":
                        if (!TryInt(out var tt)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ThievesDaggerMinTier = tt;
                        break;
                    case "thief.proc":
                        if (!TryFloat(out var tp)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThievesDaggerProcChance = tp;
                        break;
                    case "thief.bonus":
                        if (!TryFloat(out var tb)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThievesDaggerProcBonus = tb;
                        break;
                    case "thief.aggro":
                        if (!TryFloat(out var ta)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThievesDaggerAggroPenalty = ta;
                        break;

                    case "sentinel.drop":
                        if (!TryFloat(out var sdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearDropChance = sdr;
                        break;
                    case "sentinel.tier":
                        if (!TryInt(out var st)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.SentinelSpearMinTier = st;
                        break;
                    case "sentinel.proc":
                        if (!TryFloat(out var sp)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearProcChance = sp;
                        break;
                    case "sentinel.drain":
                        if (!TryFloat(out var sdn)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearDrainPct = sdn;
                        break;
                    case "sentinel.return":
                        if (!TryFloat(out var sr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearReturnMult = sr;
                        break;

                    default:
                        CommandHandlerHelper.WriteOutputInfo(session,
                            $"Unknown key '{key}'. Use @lootconfig list to see all keys.",
                            ChatMessageType.Broadcast);
                        return;
                }

                CommandHandlerHelper.WriteOutputInfo(session,
                    $"[LootConfig] {key} = {raw}",
                    ChatMessageType.Broadcast);
                return;
            }

            CommandHandlerHelper.WriteOutputInfo(session,
                "Usage: @lootconfig list  |  @lootconfig set <key> <value>",
                ChatMessageType.Broadcast);
        }

        private static void BadValue(Session session, string key, string type)
        {
            CommandHandlerHelper.WriteOutputInfo(session,
                $"Invalid value for '{key}' — expected a {type}.",
                ChatMessageType.Broadcast);
        }
    }
}
