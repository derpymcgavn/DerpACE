using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.DerpAce;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

namespace ACE.Server.Command.Handlers
{
    public static class DerpACEConfigCommands
    {
        // @lootconfig list|set <key> <value>
        [CommandHandler("lootconfig", AccessLevel.Developer, CommandHandlerFlag.None, 1,
            "View or modify DerpACE loot item variables.",
            "list                    - print all current values\n" +
            "set <key> <value>       - change a value at runtime\n" +
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
            "  thief.seampenalty     ThievesDaggerSeamPenalty (int)\n" +
            "  thief.seamduration    ThievesDaggerSeamDuration (int seconds)\n" +
            "  sentinel.drop         SentinelSpearDropChance (float 0-1)\n" +
            "  sentinel.tier         SentinelSpearMinTier (int)\n" +
            "  sentinel.proc         SentinelSpearProcChance (legacy float 0-1)\n" +
            "  sentinel.power        SentinelSpearPowerThreshold (float 0-1)\n" +
            "  sentinel.stacks       SentinelSpearMaxStacks (int)\n" +
            "  sentinel.drain        SentinelSpearDrainPct (float 0-1)\n" +
            "  sentinel.return       SentinelSpearReturnMult (float)\n" +
            "  sentinel.cooldown     SentinelSpearCooldownSeconds (int seconds)\n" +
            "  sentinel.poisedur     SentinelSpearPoiseDurationSeconds (int seconds)\n" +
            "  sentinel.poisedr      SentinelSpearPoiseDamageReduction (float 0-0.5)\n" +
            "  sentinel.aggro        SentinelSpearAggroBonus (float)\n" +
            "  unarmed.drop          UnarmedElemDropChance (float 0-1)\n" +
            "  unarmed.procmin       UnarmedElemProcMin (int %)\n" +
            "  unarmed.procmax       UnarmedElemProcMax (int %)\n" +
            "  fencer.drop           FencerBladeDropChance (float 0-1)\n" +
            "  fencer.tier           FencerBladeMinTier (int)\n" +
            "  fencer.piercemin      FencerPierceMin (int %)\n" +
            "  fencer.piercemax      FencerPierceMax (int %)\n" +
            "  fencer.procmin        FencerPierceProcMin (int %)\n" +
            "  fencer.procmax        FencerPierceProcMax (int %)\n" +
            "  fencer.deflectmin     FencerDeflectMin (int %)\n" +
            "  fencer.deflectmax     FencerDeflectMax (int %)\n" +
            "  ravager.drop          RavagerAxeDropChance (float 0-1)\n" +
            "  ravager.tier          RavagerAxeMinTier (int)\n" +
            "  ravager.procmin       RavagerProcMin (int %)\n" +
            "  ravager.procmax       RavagerProcMax (int %)\n" +
            "  ravager.bleedmin      RavagerBleedMin (int %)\n" +
            "  ravager.bleedmax      RavagerBleedMax (int %)\n" +
            "  ravager.twohandmult   RavagerTwoHandMult (float)\n" +
            "  ravager.ticks         RavagerBleedTicks (int)\n" +
            "  ravager.interval      RavagerBleedInterval (float seconds)\n" +
            "  ravager.cleavechance  RavagerHammerCleaveChance (float 0-1)\n" +
            "  ravager.cleavetargets RavagerHammerCleaveMaxTargets (int, includes primary)\n" +
            "  ravager.cleavescale   RavagerHammerCleaveDamageScale (float 0-1)\n" +
            "  ravager.cleaveradius  RavagerHammerCleaveRadius (float meters)\n" +
            "  warden.drop           WardenMaulDropChance (float 0-1)\n" +
            "  warden.tier           WardenMaulMinTier (int)\n" +
            "  warden.procmin        WardenProcMin (int %)\n" +
            "  warden.procmax        WardenProcMax (int %)\n" +
            "  warden.penaltymin     WardenPenaltyMin (int defense skill)\n" +
            "  warden.penaltymax     WardenPenaltyMax (int defense skill)\n" +
            "  warden.durationmin    WardenDurationMin (int seconds)\n" +
            "  warden.durationmax    WardenDurationMax (int seconds)\n" +
            "  warden.twohandmult    WardenTwoHandMult (float)\n" +
            "  resolute.drop         ResoluteBladeDropChance (float 0-1)\n" +
            "  resolute.tier         ResoluteBladeMinTier (int)\n" +
            "  resolute.procmin      ResoluteProcMin (int %)\n" +
            "  resolute.procmax      ResoluteProcMax (int %)\n" +
            "  resolute.healmin      ResoluteHealMin (int %)\n" +
            "  resolute.healmax      ResoluteHealMax (int %)\n" +
            "  resolute.killburst    ResoluteKillBurstPct (float 0-1)\n" +
            "  resolute.twohandmult  ResoluteTwoHandMult (float)\n" +
            "  polebreaker.drop      PolebreakerDropChance (float 0-1)\n" +
            "  polebreaker.tier      PolebreakerMinTier (int)\n" +
            "  polebreaker.stackmin  PolebreakerStackMin (int %)\n" +
            "  polebreaker.stackmax  PolebreakerStackMax (int %)\n" +
            "  polebreaker.maxstackmin  PolebreakerMaxStackMin (int)\n" +
            "  polebreaker.maxstackmax  PolebreakerMaxStackMax (int)\n" +
            "  stalker.drop          StalkerBowDropChance (float 0-1)\n" +
            "  stalker.tier          StalkerBowMinTier (int)\n" +
            "  stalker.procmin       StalkerProcMin (int %)\n" +
            "  stalker.procmax       StalkerProcMax (int %)\n" +
            "  stalker.bonusmin      StalkerBonusMin (int %)\n" +
            "  stalker.bonusmax      StalkerBonusMax (int %)\n" +
            "  breacher.drop         BreacherCrossbowDropChance (float 0-1)\n" +
            "  breacher.tier         BreacherCrossbowMinTier (int)\n" +
            "  breacher.ignorechancemin  BreacherArmorIgnoreMin (int %)\n" +
            "  breacher.ignorechancemax  BreacherArmorIgnoreMax (int %)\n" +
            "  reaper.drop           ReaperAtlatlDropChance (float 0-1)\n" +
            "  reaper.tier           ReaperAtlatlMinTier (int)\n" +
            "  reaper.procmin        ReaperProcMin (int %)\n" +
            "  reaper.procmax        ReaperProcMax (int %)\n" +
            "  reaper.healmin        ReaperHealMin (int % MaxHealth)\n" +
            "  reaper.healmax        ReaperHealMax (int % MaxHealth)\n" +
            "  ricochet.drop         RicochetAtlatlDropChance (float 0-1)\n" +
            "  ricochet.tier         RicochetAtlatlMinTier (int)\n" +
            "  ricochet.procmin      RicochetProcMin (int %)\n" +
            "  ricochet.procmax      RicochetProcMax (int %)\n" +
            "  ricochet.scale        RicochetDamageScale (float 0-1)\n" +
            "  ricochet.radius       RicochetRadius (float meters)\n" +
            "  lugianhammer.drop      LugianHammerThrowDropChance (float 0-1)\n" +
            "  lugianhammer.tier      LugianHammerThrowMinTier (int)\n" +
            "  lugianhammer.proc      LugianHammerThrowProcChance (float 0-1)\n" +
            "  lugianhammer.scale     LugianHammerThrowDamageScale (float 0-1)\n" +
            "  lugianhammer.radius    LugianHammerThrowRadius (float yards)\n" +
            "  lugianhammer.cooldown  LugianHammerThrowCooldownSeconds (float)\n" +
            "  opportunist.melee     OpportunistMeleeDropChance (float 0-1)\n" +
            "  opportunist.missile   OpportunistMissileDropChance (float 0-1)\n" +
            "  opportunist.tier      OpportunistMinTier (int)\n" +
            "  opportunist.bonus     OpportunistDamageBonus (float, 0.25 = +25%)\n" +
            "  opportunist.window    OpportunistWindowSeconds (float)\n" +
            "  executioner.melee     ExecutionerMeleeDropChance (float 0-1)\n" +
            "  executioner.missile   ExecutionerMissileDropChance (float 0-1)\n" +
            "  executioner.tier      ExecutionerMinTier (int)\n" +
            "  executioner.bonus     ExecutionerDamageBonus (float, 0.20 = +20%)\n" +
            "  executioner.threshold ExecutionerHealthThreshold (float, 0.25 = 25%)\n" +
            "  dinnerware.drop       DinnerwareWeaponDropChance (float 0-1)\n" +
            "  dinnerware.tier       DinnerwareWeaponMinTier (int)\n" +
            "  dinnerware.spin       DinnerwareSpinDropChance (float 0-1)\n" +
            "  dinnerware.spintier   DinnerwareSpinMinTier (int)\n" +
            "  dinnerware.scale      DinnerwareSpinDamageScale (float)\n" +
            "  dinnerware.radius     DinnerwareSpinRadius (float meters)\n" +
            "  quickening.drop       QuickeningDaggerDropChance (float 0-1)\n" +
            "  quickening.tier       QuickeningDaggerMinTier (int)\n" +
            "  quickening.procmin    QuickeningDaggerProcMin (int %)\n" +
            "  quickening.procmax    QuickeningDaggerProcMax (int %)\n" +
            "  quickening.speedmin   QuickeningDaggerSpeedMin (int %)\n" +
            "  quickening.speedmax   QuickeningDaggerSpeedMax (int %)\n" +
            "  quickening.durmin     QuickeningDaggerDurationMin (int seconds)\n" +
            "  quickening.durmax     QuickeningDaggerDurationMax (int seconds)\n" +
            "  lootmod.mult          LootModifierGlobalDropMultiplier (float 0-2)\n" +
            "  lootmod.exclusive     LootModifierExclusivePerItem (bool)\n" +
            "  lootmod.interchange   LootModifierInterchangeable (bool)\n" +
            "  lootmod.interchangetier LootModifierInterchangeableMinTier (int)\n" +
            "  armor.banenormal      ArmorBaneChanceNormal (float 0-1, per-bane chance on normal armor)\n" +
            "  armor.banecovenant    ArmorBaneChanceCovenant (float 0-1, per-bane chance on Covenant armor)\n" +
            "  armor.enchbonus       ArmorEnchantmentChanceBonus (float 0-1, flat bonus to critter/life chance)\n" +
            "  armor.enchmax         ArmorMaxEnchantments (int, max critter/life spells per armor piece)\n" +
            "  armor.enchmult        ArmorExtraEnchantmentChanceMult (float 0-1, per-extra-spell chance mult)\n" +
            "  blast.mintier         WeaponBlastProcMinTier (int, min tier for elemental blast proc)\n" +
            "  blast.chancemin       WeaponBlastProcChanceMin (float 0-1, roll chance at min tier)\n" +
            "  blast.chancemax       WeaponBlastProcChanceMax (float 0-1, roll chance at T8)\n" +
            "  blast.ratemin         WeaponBlastProcRateMin (float, per-hit fire rate min)\n" +
            "  blast.ratemax         WeaponBlastProcRateMax (float, per-hit fire rate max)\n" +
            "  mobmod.enabled        MobModifierEnabled (bool, master switch)\n" +
            "  mobmod.level          MobModifierMinLevel (int)\n" +
            "  mobmod.tier           MobModifierMinTier (int)\n" +
            "  mobmod.defcap         MobModifierDefenseSkillCap (int, 0=disabled)\n" +
            "  vampiric.chance       VampiricMobChance (float 0-1)\n" +
            "  vampiric.lifestealmin VampiricLifestealMin (int %)\n" +
            "  vampiric.lifestealmax VampiricLifestealMax (int %)\n" +
            "  thiefmob.chance       ThiefMobChance (float 0-1)\n" +
            "  scoutmob.chance       ScoutMobChance (float 0-1)\n" +
            "  thiefmob.proc         ThiefStealProc (float 0-1)\n" +
            "  thiefmob.chestchance  ThiefChestDropChance (float 0-1)\n" +
            "  thiefmob.chestwcid    ThiefChestWcid (uint)\n" +
            "  thiefmob.chestdespawn ThiefChestDespawnSeconds (float, 0=never)\n" +
            "  simulacrum.chance     SimulacrumMobChance (float 0-1)\n" +
            "  healermob.chance      HealerMobChance (float 0-1)\n" +
            "  enchantermob.chance   EnchanterMobChance (float 0-1)\n" +
            "  shamanmob.chance      ShamanMobChance (float 0-1)\n" +
            "  healermob.range       HealerMobRange (float meters)\n" +
            "  healermob.threshold   HealerMobHealThreshold (float 0-1)\n" +
            "  healermob.cooldown    HealerMobCooldownSeconds (float)\n" +
            "  ironman.enabled       IronmanEnabled (bool, master switch)\n" +
            "  ironman.xp            IronmanXpScalar (float, default 0.75)\n" +
            "  nomad.xp              NomadXpScalar (float, default 0.75)\n" +
            "  hardcore.xp           HardcoreXpScalar (float, default 1.0)\n" +
            "  vendor.loot           VendorRandomLootEnabled (bool, master switch)\n" +
            "  vendor.lootmin        VendorRandomLootMinItems (int, min items per category)\n" +
            "  vendor.lootmax        VendorRandomLootMaxItems (int, max items per category)")]
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
                sb.AppendLine($"  sneak.bonus     = {DerpACEConfig.SneakAttackBonusPct:P0}  ({DerpACEConfig.SneakAttackBonusPct})");
                sb.AppendLine($"  thief.drop      = {DerpACEConfig.ThievesDaggerDropChance:P0}  ({DerpACEConfig.ThievesDaggerDropChance})");
                sb.AppendLine($"  thief.tier      = {DerpACEConfig.ThievesDaggerMinTier}");
                sb.AppendLine($"  thief.proc      = {DerpACEConfig.ThievesDaggerProcChance:P0}  ({DerpACEConfig.ThievesDaggerProcChance})");
                sb.AppendLine($"  thief.bonus     = {DerpACEConfig.ThievesDaggerProcBonus:P0}  ({DerpACEConfig.ThievesDaggerProcBonus})");
                sb.AppendLine($"  thief.aggro     = {DerpACEConfig.ThievesDaggerAggroPenalty}");
                sb.AppendLine($"  thief.seampenalty = {DerpACEConfig.ThievesDaggerSeamPenalty}");
                sb.AppendLine($"  thief.seamduration = {DerpACEConfig.ThievesDaggerSeamDuration}s");
                sb.AppendLine($"  sentinel.drop   = {DerpACEConfig.SentinelSpearDropChance:P0}  ({DerpACEConfig.SentinelSpearDropChance})");
                sb.AppendLine($"  sentinel.tier   = {DerpACEConfig.SentinelSpearMinTier}");
                sb.AppendLine($"  sentinel.proc   = {DerpACEConfig.SentinelSpearProcChance:P0}  ({DerpACEConfig.SentinelSpearProcChance}) legacy");
                sb.AppendLine($"  sentinel.power  = {DerpACEConfig.SentinelSpearPowerThreshold:P0}  ({DerpACEConfig.SentinelSpearPowerThreshold})");
                sb.AppendLine($"  sentinel.stacks = {DerpACEConfig.SentinelSpearMaxStacks}");
                sb.AppendLine($"  sentinel.drain  = {DerpACEConfig.SentinelSpearDrainPct:P0}  ({DerpACEConfig.SentinelSpearDrainPct})");
                sb.AppendLine($"  sentinel.return = {DerpACEConfig.SentinelSpearReturnMult}");
                sb.AppendLine($"  sentinel.cooldown = {DerpACEConfig.SentinelSpearCooldownSeconds}s");
                sb.AppendLine($"  sentinel.poisedur = {DerpACEConfig.SentinelSpearPoiseDurationSeconds}s");
                sb.AppendLine($"  sentinel.poisedr = {DerpACEConfig.SentinelSpearPoiseDamageReduction:P0}  ({DerpACEConfig.SentinelSpearPoiseDamageReduction})");
                sb.AppendLine($"  sentinel.aggro  = {DerpACEConfig.SentinelSpearAggroBonus}");
                sb.AppendLine($"  unarmed.drop    = {DerpACEConfig.UnarmedElemDropChance:P0}  ({DerpACEConfig.UnarmedElemDropChance})");
                sb.AppendLine($"  unarmed.procmin = {DerpACEConfig.UnarmedElemProcMin}%");
                sb.AppendLine($"  unarmed.procmax = {DerpACEConfig.UnarmedElemProcMax}%");
                sb.AppendLine($"  fencer.drop     = {DerpACEConfig.FencerBladeDropChance:P0}  ({DerpACEConfig.FencerBladeDropChance})");
                sb.AppendLine($"  fencer.tier     = {DerpACEConfig.FencerBladeMinTier}");
                sb.AppendLine($"  fencer.piercemin = {DerpACEConfig.FencerPierceMin}%");
                sb.AppendLine($"  fencer.piercemax = {DerpACEConfig.FencerPierceMax}%");
                sb.AppendLine($"  fencer.procmin  = {DerpACEConfig.FencerPierceProcMin}%");
                sb.AppendLine($"  fencer.procmax  = {DerpACEConfig.FencerPierceProcMax}%");
                sb.AppendLine($"  fencer.deflectmin = {DerpACEConfig.FencerDeflectMin}%");
                sb.AppendLine($"  fencer.deflectmax = {DerpACEConfig.FencerDeflectMax}%");
                sb.AppendLine($"  ravager.drop        = {DerpACEConfig.RavagerAxeDropChance:P0}  ({DerpACEConfig.RavagerAxeDropChance})");
                sb.AppendLine($"  ravager.tier        = {DerpACEConfig.RavagerAxeMinTier}");
                sb.AppendLine($"  ravager.procmin     = {DerpACEConfig.RavagerProcMin}%");
                sb.AppendLine($"  ravager.procmax     = {DerpACEConfig.RavagerProcMax}%");
                sb.AppendLine($"  ravager.bleedmin    = {DerpACEConfig.RavagerBleedMin}%");
                sb.AppendLine($"  ravager.bleedmax    = {DerpACEConfig.RavagerBleedMax}%");
                sb.AppendLine($"  ravager.twohandmult = {DerpACEConfig.RavagerTwoHandMult}");
                sb.AppendLine($"  ravager.ticks       = {DerpACEConfig.RavagerBleedTicks}");
                sb.AppendLine($"  ravager.interval    = {DerpACEConfig.RavagerBleedInterval}s");
                sb.AppendLine($"  ravager.cleavechance  = {DerpACEConfig.RavagerHammerCleaveChance:P0}  ({DerpACEConfig.RavagerHammerCleaveChance})");
                sb.AppendLine($"  ravager.cleavetargets = {DerpACEConfig.RavagerHammerCleaveMaxTargets}");
                sb.AppendLine($"  ravager.cleavescale   = {DerpACEConfig.RavagerHammerCleaveDamageScale:P0}  ({DerpACEConfig.RavagerHammerCleaveDamageScale})");
                sb.AppendLine($"  ravager.cleaveradius  = {DerpACEConfig.RavagerHammerCleaveRadius}m");
                sb.AppendLine($"  warden.drop         = {DerpACEConfig.WardenMaulDropChance:P0}  ({DerpACEConfig.WardenMaulDropChance})");
                sb.AppendLine($"  warden.tier         = {DerpACEConfig.WardenMaulMinTier}");
                sb.AppendLine($"  warden.procmin      = {DerpACEConfig.WardenProcMin}%");
                sb.AppendLine($"  warden.procmax      = {DerpACEConfig.WardenProcMax}%");
                sb.AppendLine($"  warden.penaltymin   = {DerpACEConfig.WardenPenaltyMin}");
                sb.AppendLine($"  warden.penaltymax   = {DerpACEConfig.WardenPenaltyMax}");
                sb.AppendLine($"  warden.durationmin  = {DerpACEConfig.WardenDurationMin}s");
                sb.AppendLine($"  warden.durationmax  = {DerpACEConfig.WardenDurationMax}s");
                sb.AppendLine($"  warden.twohandmult  = {DerpACEConfig.WardenTwoHandMult}");
                sb.AppendLine($"  resolute.drop        = {DerpACEConfig.ResoluteBladeDropChance:P0}  ({DerpACEConfig.ResoluteBladeDropChance})");
                sb.AppendLine($"  resolute.tier        = {DerpACEConfig.ResoluteBladeMinTier}");
                sb.AppendLine($"  resolute.procmin     = {DerpACEConfig.ResoluteProcMin}%");
                sb.AppendLine($"  resolute.procmax     = {DerpACEConfig.ResoluteProcMax}%");
                sb.AppendLine($"  resolute.healmin     = {DerpACEConfig.ResoluteHealMin}%");
                sb.AppendLine($"  resolute.healmax     = {DerpACEConfig.ResoluteHealMax}%");
                sb.AppendLine($"  resolute.killburst   = {DerpACEConfig.ResoluteKillBurstPct:P0}");
                sb.AppendLine($"  resolute.twohandmult = {DerpACEConfig.ResoluteTwoHandMult}");
                sb.AppendLine($"  polebreaker.drop        = {DerpACEConfig.PolebreakerDropChance:P0}  ({DerpACEConfig.PolebreakerDropChance})");
                sb.AppendLine($"  polebreaker.tier        = {DerpACEConfig.PolebreakerMinTier}");
                sb.AppendLine($"  polebreaker.stackmin    = {DerpACEConfig.PolebreakerStackMin}%");
                sb.AppendLine($"  polebreaker.stackmax    = {DerpACEConfig.PolebreakerStackMax}%");
                sb.AppendLine($"  polebreaker.maxstackmin = {DerpACEConfig.PolebreakerMaxStackMin}");
                sb.AppendLine($"  polebreaker.maxstackmax = {DerpACEConfig.PolebreakerMaxStackMax}");
                sb.AppendLine($"  stalker.drop         = {DerpACEConfig.StalkerBowDropChance:P0}  ({DerpACEConfig.StalkerBowDropChance})");
                sb.AppendLine($"  stalker.tier         = {DerpACEConfig.StalkerBowMinTier}");
                sb.AppendLine($"  stalker.procmin      = {DerpACEConfig.StalkerProcMin}%");
                sb.AppendLine($"  stalker.procmax      = {DerpACEConfig.StalkerProcMax}%");
                sb.AppendLine($"  stalker.bonusmin     = {DerpACEConfig.StalkerBonusMin}%");
                sb.AppendLine($"  stalker.bonusmax     = {DerpACEConfig.StalkerBonusMax}%");
                sb.AppendLine($"  breacher.drop        = {DerpACEConfig.BreacherCrossbowDropChance:P0}  ({DerpACEConfig.BreacherCrossbowDropChance})");
                sb.AppendLine($"  breacher.tier        = {DerpACEConfig.BreacherCrossbowMinTier}");
                sb.AppendLine($"  breacher.ignorechancemin = {DerpACEConfig.BreacherArmorIgnoreMin}%");
                sb.AppendLine($"  breacher.ignorechancemax = {DerpACEConfig.BreacherArmorIgnoreMax}%");
                sb.AppendLine($"  reaper.drop          = {DerpACEConfig.ReaperAtlatlDropChance:P0}  ({DerpACEConfig.ReaperAtlatlDropChance})");
                sb.AppendLine($"  reaper.tier          = {DerpACEConfig.ReaperAtlatlMinTier}");
                sb.AppendLine($"  reaper.procmin       = {DerpACEConfig.ReaperProcMin}%");
                sb.AppendLine($"  reaper.procmax       = {DerpACEConfig.ReaperProcMax}%");
                sb.AppendLine($"  reaper.healmin       = {DerpACEConfig.ReaperHealMin}%");
                sb.AppendLine($"  reaper.healmax       = {DerpACEConfig.ReaperHealMax}%");
                sb.AppendLine($"  ricochet.drop        = {DerpACEConfig.RicochetAtlatlDropChance:P0}  ({DerpACEConfig.RicochetAtlatlDropChance})");
                sb.AppendLine($"  ricochet.tier        = {DerpACEConfig.RicochetAtlatlMinTier}");
                sb.AppendLine($"  ricochet.procmin     = {DerpACEConfig.RicochetProcMin}%");
                sb.AppendLine($"  ricochet.procmax     = {DerpACEConfig.RicochetProcMax}%");
                sb.AppendLine($"  ricochet.scale       = {DerpACEConfig.RicochetDamageScale:P0}  ({DerpACEConfig.RicochetDamageScale})");
                sb.AppendLine($"  ricochet.radius      = {DerpACEConfig.RicochetRadius}m");
                sb.AppendLine($"  lugianhammer.drop   = {DerpACEConfig.LugianHammerThrowDropChance:P0}  ({DerpACEConfig.LugianHammerThrowDropChance})");
                sb.AppendLine($"  lugianhammer.tier   = {DerpACEConfig.LugianHammerThrowMinTier}");
                sb.AppendLine($"  lugianhammer.proc   = {DerpACEConfig.LugianHammerThrowProcChance:P0}  ({DerpACEConfig.LugianHammerThrowProcChance})");
                sb.AppendLine($"  lugianhammer.scale  = {DerpACEConfig.LugianHammerThrowDamageScale:P0}  ({DerpACEConfig.LugianHammerThrowDamageScale})");
                sb.AppendLine($"  lugianhammer.radius = {DerpACEConfig.LugianHammerThrowRadius}y");
                sb.AppendLine($"  lugianhammer.cooldown = {DerpACEConfig.LugianHammerThrowCooldownSeconds}s");
                sb.AppendLine($"  opportunist.melee    = {DerpACEConfig.OpportunistMeleeDropChance:P1}  ({DerpACEConfig.OpportunistMeleeDropChance})");
                sb.AppendLine($"  opportunist.missile  = {DerpACEConfig.OpportunistMissileDropChance:P1}  ({DerpACEConfig.OpportunistMissileDropChance})");
                sb.AppendLine($"  opportunist.tier     = {DerpACEConfig.OpportunistMinTier}");
                sb.AppendLine($"  opportunist.bonus    = {DerpACEConfig.OpportunistDamageBonus:P0}  ({DerpACEConfig.OpportunistDamageBonus})");
                sb.AppendLine($"  opportunist.window   = {DerpACEConfig.OpportunistWindowSeconds}s");
                sb.AppendLine($"  executioner.melee    = {DerpACEConfig.ExecutionerMeleeDropChance:P1}  ({DerpACEConfig.ExecutionerMeleeDropChance})");
                sb.AppendLine($"  executioner.missile  = {DerpACEConfig.ExecutionerMissileDropChance:P1}  ({DerpACEConfig.ExecutionerMissileDropChance})");
                sb.AppendLine($"  executioner.tier     = {DerpACEConfig.ExecutionerMinTier}");
                sb.AppendLine($"  executioner.bonus    = {DerpACEConfig.ExecutionerDamageBonus:P0}  ({DerpACEConfig.ExecutionerDamageBonus})");
                sb.AppendLine($"  executioner.threshold= {DerpACEConfig.ExecutionerHealthThreshold:P0}  ({DerpACEConfig.ExecutionerHealthThreshold})");
                sb.AppendLine($"  dinnerware.drop      = {DerpACEConfig.DinnerwareWeaponDropChance:P0}  ({DerpACEConfig.DinnerwareWeaponDropChance})");
                sb.AppendLine($"  dinnerware.tier      = {DerpACEConfig.DinnerwareWeaponMinTier}");
                sb.AppendLine($"  dinnerware.spin      = {DerpACEConfig.DinnerwareSpinDropChance:P0}  ({DerpACEConfig.DinnerwareSpinDropChance})");
                sb.AppendLine($"  dinnerware.spintier  = {DerpACEConfig.DinnerwareSpinMinTier}");
                sb.AppendLine($"  dinnerware.scale     = {DerpACEConfig.DinnerwareSpinDamageScale:P0}  ({DerpACEConfig.DinnerwareSpinDamageScale})");
                sb.AppendLine($"  dinnerware.radius    = {DerpACEConfig.DinnerwareSpinRadius}m");
                sb.AppendLine($"  quickening.drop      = {DerpACEConfig.QuickeningDaggerDropChance:P0}  ({DerpACEConfig.QuickeningDaggerDropChance})");
                sb.AppendLine($"  quickening.tier      = {DerpACEConfig.QuickeningDaggerMinTier}");
                sb.AppendLine($"  quickening.procmin   = {DerpACEConfig.QuickeningDaggerProcMin}%");
                sb.AppendLine($"  quickening.procmax   = {DerpACEConfig.QuickeningDaggerProcMax}%");
                sb.AppendLine($"  quickening.speedmin  = {DerpACEConfig.QuickeningDaggerSpeedMin}%");
                sb.AppendLine($"  quickening.speedmax  = {DerpACEConfig.QuickeningDaggerSpeedMax}%");
                sb.AppendLine($"  quickening.durmin    = {DerpACEConfig.QuickeningDaggerDurationMin}s");
                sb.AppendLine($"  quickening.durmax    = {DerpACEConfig.QuickeningDaggerDurationMax}s");
                sb.AppendLine($"  lootmod.mult         = {DerpACEConfig.LootModifierGlobalDropMultiplier}");
                sb.AppendLine($"  lootmod.exclusive    = {DerpACEConfig.LootModifierExclusivePerItem}");
                sb.AppendLine($"  lootmod.interchange  = {DerpACEConfig.LootModifierInterchangeable}");
                sb.AppendLine($"  lootmod.interchangetier = {DerpACEConfig.LootModifierInterchangeableMinTier}");
                sb.AppendLine($"  armor.banenormal     = {DerpACEConfig.ArmorBaneChanceNormal:P0}  ({DerpACEConfig.ArmorBaneChanceNormal})");
                sb.AppendLine($"  armor.banecovenant   = {DerpACEConfig.ArmorBaneChanceCovenant:P0}  ({DerpACEConfig.ArmorBaneChanceCovenant})");
                sb.AppendLine($"  armor.enchbonus      = {DerpACEConfig.ArmorEnchantmentChanceBonus:P0}  ({DerpACEConfig.ArmorEnchantmentChanceBonus}) flat critter/life chance bonus");
                sb.AppendLine($"  armor.enchmax        = {DerpACEConfig.ArmorMaxEnchantments} max critter/life spells per armor piece");
                sb.AppendLine($"  armor.enchmult       = {DerpACEConfig.ArmorExtraEnchantmentChanceMult:P0}  ({DerpACEConfig.ArmorExtraEnchantmentChanceMult}) extra-spell chance multiplier");
                sb.AppendLine($"  blast.mintier        = {DerpACEConfig.WeaponBlastProcMinTier} min tier for weapon blast proc");
                sb.AppendLine($"  blast.chancemin      = {DerpACEConfig.WeaponBlastProcChanceMin:P1}  ({DerpACEConfig.WeaponBlastProcChanceMin}) blast proc roll chance at min tier");
                sb.AppendLine($"  blast.chancemax      = {DerpACEConfig.WeaponBlastProcChanceMax:P1}  ({DerpACEConfig.WeaponBlastProcChanceMax}) blast proc roll chance at T8");
                sb.AppendLine($"  blast.ratemin        = {DerpACEConfig.WeaponBlastProcRateMin:G4} per-hit blast fire rate min");
                sb.AppendLine($"  blast.ratemax        = {DerpACEConfig.WeaponBlastProcRateMax:G4} per-hit blast fire rate max");
                sb.AppendLine($"  mobmod.enabled       = {DerpACEConfig.MobModifierEnabled}");
                sb.AppendLine($"  mobmod.level         = {DerpACEConfig.MobModifierMinLevel}");
                sb.AppendLine($"  mobmod.tier          = {DerpACEConfig.MobModifierMinTier}");
                sb.AppendLine($"  mobmod.defcap        = {DerpACEConfig.MobModifierDefenseSkillCap} effective defense cap (0=disabled)");
                sb.AppendLine($"  vampiric.chance      = {DerpACEConfig.VampiricMobChance:P1}  ({DerpACEConfig.VampiricMobChance})");
                sb.AppendLine($"  vampiric.lifestealmin= {DerpACEConfig.VampiricLifestealMin}%");
                sb.AppendLine($"  vampiric.lifestealmax= {DerpACEConfig.VampiricLifestealMax}%");
                sb.AppendLine($"  thiefmob.chance      = {DerpACEConfig.ThiefMobChance:P1}  ({DerpACEConfig.ThiefMobChance})");
                sb.AppendLine($"  scoutmob.chance      = {DerpACEConfig.ScoutMobChance:P1}  ({DerpACEConfig.ScoutMobChance})");
                sb.AppendLine($"  thiefmob.proc        = {DerpACEConfig.ThiefStealProc:P0}  ({DerpACEConfig.ThiefStealProc})");
                sb.AppendLine($"  thiefmob.chestchance = {DerpACEConfig.ThiefChestDropChance:P0}  ({DerpACEConfig.ThiefChestDropChance})");
                sb.AppendLine($"  thiefmob.chestwcid   = {DerpACEConfig.ThiefChestWcid}");
                sb.AppendLine($"  thiefmob.chestdespawn= {DerpACEConfig.ThiefChestDespawnSeconds}s");
                sb.AppendLine($"  simulacrum.chance    = {DerpACEConfig.SimulacrumMobChance:P1}  ({DerpACEConfig.SimulacrumMobChance})");
                sb.AppendLine($"  healermob.chance     = {DerpACEConfig.HealerMobChance:P1}  ({DerpACEConfig.HealerMobChance})");
                sb.AppendLine($"  enchantermob.chance = {DerpACEConfig.EnchanterMobChance:P1}  ({DerpACEConfig.EnchanterMobChance})");
                sb.AppendLine($"  shamanmob.chance     = {DerpACEConfig.ShamanMobChance:P1}  ({DerpACEConfig.ShamanMobChance})");
                sb.AppendLine($"  healermob.range      = {DerpACEConfig.HealerMobRange}m");
                sb.AppendLine($"  healermob.threshold  = {DerpACEConfig.HealerMobHealThreshold:P0}  ({DerpACEConfig.HealerMobHealThreshold})");
                sb.AppendLine($"  healermob.cooldown   = {DerpACEConfig.HealerMobCooldownSeconds}s");
                sb.AppendLine($"  tankmob.chance       = {DerpACEConfig.TankMobChance:P1}  ({DerpACEConfig.TankMobChance})");
                sb.AppendLine($"  tankmob.hpmult       = {DerpACEConfig.TankMobHealthMultiplier}x");
                sb.AppendLine($"  tankmob.physreduction= {DerpACEConfig.TankMobPhysicalReduction:P0}  ({DerpACEConfig.TankMobPhysicalReduction})");
                sb.AppendLine($"  tankmob.healbonus    = {DerpACEConfig.TankMobHealBonus}x");
                sb.AppendLine($"  tankmob.skillbonus   = +{DerpACEConfig.TankMobSkillBonus}");
                sb.AppendLine($"  ironman.enabled      = {DerpACEConfig.IronmanEnabled}");
                sb.AppendLine($"  ironman.xp           = {DerpACEConfig.IronmanXpScalar:P0}  ({DerpACEConfig.IronmanXpScalar})");
                sb.AppendLine($"  nomad.xp             = {DerpACEConfig.NomadXpScalar:P0}  ({DerpACEConfig.NomadXpScalar})");
                sb.AppendLine($"  hardcore.xp          = {DerpACEConfig.HardcoreXpScalar:P0}  ({DerpACEConfig.HardcoreXpScalar})");
                sb.AppendLine($"  vendor.loot          = {DerpACEConfig.VendorRandomLootEnabled}");
                sb.AppendLine($"  vendor.lootmin       = {DerpACEConfig.VendorRandomLootMinItems}");
                sb.AppendLine($"  vendor.lootmax       = {DerpACEConfig.VendorRandomLootMaxItems}");
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
                    case "thief.seampenalty":
                        if (!TryInt(out var tspen)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ThievesDaggerSeamPenalty = (uint)Math.Max(0, tspen);
                        break;
                    case "thief.seamduration":
                        if (!TryInt(out var tsdur)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ThievesDaggerSeamDuration = Math.Max(1, tsdur);
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
                    case "sentinel.power":
                        if (!TryFloat(out var spow)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.SentinelSpearPowerThreshold = Math.Clamp(spow, 0f, 1f);
                        break;
                    case "sentinel.stacks":
                        if (!TryInt(out var sstacks)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.SentinelSpearMaxStacks = Math.Max(1, sstacks);
                        break;
                    case "sentinel.drain":
                        if (!TryFloat(out var sdn)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearDrainPct = Math.Clamp(sdn, 0f, 1f);
                        break;
                    case "sentinel.return":
                        if (!TryFloat(out var sr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearReturnMult = Math.Clamp(sr, 0f, 2f);
                        break;
                    case "sentinel.cooldown":
                        if (!TryInt(out var scool)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.SentinelSpearCooldownSeconds = Math.Max(1, scool);
                        break;
                    case "sentinel.poisedur":
                        if (!TryInt(out var spdur)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.SentinelSpearPoiseDurationSeconds = Math.Max(1, spdur);
                        break;
                    case "sentinel.poisedr":
                        if (!TryFloat(out var spdr)) { BadValue(session, key, "float 0-0.5"); return; }
                        DerpACEConfig.SentinelSpearPoiseDamageReduction = Math.Clamp(spdr, 0f, 0.5f);
                        break;
                    case "sentinel.aggro":
                        if (!TryFloat(out var saggro)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SentinelSpearAggroBonus = saggro;
                        break;

                    case "unarmed.drop":
                        if (!TryFloat(out var ud)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.UnarmedElemDropChance = ud;
                        break;
                    case "unarmed.procmin":
                        if (!TryInt(out var upmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.UnarmedElemProcMin = upmin;
                        break;
                    case "unarmed.procmax":
                        if (!TryInt(out var upmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.UnarmedElemProcMax = upmax;
                        break;

                    case "fencer.drop":
                        if (!TryFloat(out var fdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.FencerBladeDropChance = fdr;
                        break;
                    case "fencer.tier":
                        if (!TryInt(out var ft)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerBladeMinTier = ft;
                        break;
                    case "fencer.piercemin":
                        if (!TryInt(out var fpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerPierceMin = fpmin;
                        break;
                    case "fencer.piercemax":
                        if (!TryInt(out var fpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerPierceMax = fpmax;
                        break;
                    case "fencer.procmin":
                        if (!TryInt(out var fppmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerPierceProcMin = fppmin;
                        break;
                    case "fencer.procmax":
                        if (!TryInt(out var fppmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerPierceProcMax = fppmax;
                        break;
                    case "fencer.deflectmin":
                        if (!TryInt(out var fdmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerDeflectMin = fdmin;
                        break;
                    case "fencer.deflectmax":
                        if (!TryInt(out var fdmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.FencerDeflectMax = fdmax;
                        break;
                    case "ravager.drop":
                        if (!TryFloat(out var rdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerAxeDropChance = rdrop;
                        break;
                    case "ravager.tier":
                        if (!TryInt(out var rtier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerAxeMinTier = rtier;
                        break;
                    case "ravager.procmin":
                        if (!TryInt(out var rpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerProcMin = rpmin;
                        break;
                    case "ravager.procmax":
                        if (!TryInt(out var rpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerProcMax = rpmax;
                        break;
                    case "ravager.bleedmin":
                        if (!TryInt(out var rbmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerBleedMin = rbmin;
                        break;
                    case "ravager.bleedmax":
                        if (!TryInt(out var rbmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerBleedMax = rbmax;
                        break;
                    case "ravager.twohandmult":
                        if (!TryFloat(out var rthm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerTwoHandMult = rthm;
                        break;
                    case "ravager.ticks":
                        if (!TryInt(out var rticks)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerBleedTicks = rticks;
                        break;
                    case "ravager.interval":
                        if (!TryFloat(out var rint)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerBleedInterval = rint;
                        break;
                    case "ravager.cleavechance":
                        if (!TryFloat(out var rcleavechance)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerHammerCleaveChance = rcleavechance;
                        break;
                    case "ravager.cleavetargets":
                        if (!TryInt(out var rcleavetargets)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RavagerHammerCleaveMaxTargets = rcleavetargets;
                        break;
                    case "ravager.cleavescale":
                        if (!TryFloat(out var rcleavescale)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerHammerCleaveDamageScale = rcleavescale;
                        break;
                    case "ravager.cleaveradius":
                        if (!TryFloat(out var rcleaveradius)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RavagerHammerCleaveRadius = rcleaveradius;
                        break;
                    case "warden.drop":
                        if (!TryFloat(out var wdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.WardenMaulDropChance = wdrop;
                        break;
                    case "warden.tier":
                        if (!TryInt(out var wtier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenMaulMinTier = wtier;
                        break;
                    case "warden.procmin":
                        if (!TryInt(out var wpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenProcMin = wpmin;
                        break;
                    case "warden.procmax":
                        if (!TryInt(out var wpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenProcMax = wpmax;
                        break;
                    case "warden.penaltymin":
                        if (!TryInt(out var wpenmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenPenaltyMin = wpenmin;
                        break;
                    case "warden.penaltymax":
                        if (!TryInt(out var wpenmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenPenaltyMax = wpenmax;
                        break;
                    case "warden.durationmin":
                        if (!TryInt(out var wdmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenDurationMin = wdmin;
                        break;
                    case "warden.durationmax":
                        if (!TryInt(out var wdmaxw)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WardenDurationMax = wdmaxw;
                        break;
                    case "warden.twohandmult":
                        if (!TryFloat(out var wthm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.WardenTwoHandMult = wthm;
                        break;
                    case "resolute.drop":
                        if (!TryFloat(out var resdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ResoluteBladeDropChance = resdrop;
                        break;
                    case "resolute.tier":
                        if (!TryInt(out var restier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ResoluteBladeMinTier = restier;
                        break;
                    case "resolute.procmin":
                        if (!TryInt(out var respmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ResoluteProcMin = respmin;
                        break;
                    case "resolute.procmax":
                        if (!TryInt(out var respmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ResoluteProcMax = respmax;
                        break;
                    case "resolute.healmin":
                        if (!TryInt(out var reshmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ResoluteHealMin = reshmin;
                        break;
                    case "resolute.healmax":
                        if (!TryInt(out var reshmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ResoluteHealMax = reshmax;
                        break;
                    case "resolute.killburst":
                        if (!TryFloat(out var reskb)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ResoluteKillBurstPct = reskb;
                        break;
                    case "resolute.twohandmult":
                        if (!TryFloat(out var resthm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ResoluteTwoHandMult = resthm;
                        break;

                    case "polebreaker.drop":
                        if (!TryFloat(out var pbdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.PolebreakerDropChance = pbdr;
                        break;
                    case "polebreaker.tier":
                        if (!TryInt(out var pbtr)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.PolebreakerMinTier = pbtr;
                        break;
                    case "polebreaker.stackmin":
                        if (!TryInt(out var pbsmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.PolebreakerStackMin = pbsmin;
                        break;
                    case "polebreaker.stackmax":
                        if (!TryInt(out var pbsmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.PolebreakerStackMax = pbsmax;
                        break;
                    case "polebreaker.maxstackmin":
                        if (!TryInt(out var pbmsmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.PolebreakerMaxStackMin = pbmsmin;
                        break;
                    case "polebreaker.maxstackmax":
                        if (!TryInt(out var pbmsmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.PolebreakerMaxStackMax = pbmsmax;
                        break;

                    case "stalker.drop":
                        if (!TryFloat(out var sbdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.StalkerBowDropChance = sbdr;
                        break;
                    case "stalker.tier":
                        if (!TryInt(out var sbtr)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.StalkerBowMinTier = sbtr;
                        break;
                    case "stalker.procmin":
                        if (!TryInt(out var sbpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.StalkerProcMin = sbpmin;
                        break;
                    case "stalker.procmax":
                        if (!TryInt(out var sbpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.StalkerProcMax = sbpmax;
                        break;
                    case "stalker.bonusmin":
                        if (!TryInt(out var sbbmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.StalkerBonusMin = sbbmin;
                        break;
                    case "stalker.bonusmax":
                        if (!TryInt(out var sbbmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.StalkerBonusMax = sbbmax;
                        break;

                    case "breacher.drop":
                        if (!TryFloat(out var bcdr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.BreacherCrossbowDropChance = bcdr;
                        break;
                    case "breacher.tier":
                        if (!TryInt(out var bctr)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.BreacherCrossbowMinTier = bctr;
                        break;
                    case "breacher.ignorechancemin":
                        if (!TryInt(out var bcmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.BreacherArmorIgnoreMin = bcmin;
                        break;
                    case "breacher.ignorechancemax":
                        if (!TryInt(out var bcmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.BreacherArmorIgnoreMax = bcmax;
                        break;

                    case "reaper.drop":
                        if (!TryFloat(out var radr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ReaperAtlatlDropChance = radr;
                        break;
                    case "reaper.tier":
                        if (!TryInt(out var ratr)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ReaperAtlatlMinTier = ratr;
                        break;
                    case "reaper.procmin":
                        if (!TryInt(out var rapmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ReaperProcMin = rapmin;
                        break;
                    case "reaper.procmax":
                        if (!TryInt(out var rapmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ReaperProcMax = rapmax;
                        break;
                    case "reaper.healmin":
                        if (!TryInt(out var rahmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ReaperHealMin = rahmin;
                        break;
                    case "reaper.healmax":
                        if (!TryInt(out var rahmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ReaperHealMax = rahmax;
                        break;

                    case "ricochet.drop":
                        if (!TryFloat(out var ricdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RicochetAtlatlDropChance = ricdrop;
                        break;
                    case "ricochet.tier":
                        if (!TryInt(out var rictier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RicochetAtlatlMinTier = rictier;
                        break;
                    case "ricochet.procmin":
                        if (!TryInt(out var ricpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RicochetProcMin = ricpmin;
                        break;
                    case "ricochet.procmax":
                        if (!TryInt(out var ricpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.RicochetProcMax = ricpmax;
                        break;
                    case "ricochet.scale":
                        if (!TryFloat(out var ricscale)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RicochetDamageScale = Math.Clamp(ricscale, 0f, 1f);
                        break;
                    case "ricochet.radius":
                        if (!TryFloat(out var ricradius)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.RicochetRadius = Math.Max(1f, ricradius);
                        break;

                    case "lugianhammer.drop":
                        if (!TryFloat(out var lhdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LugianHammerThrowDropChance = Math.Clamp(lhdrop, 0f, 1f);
                        break;
                    case "lugianhammer.tier":
                        if (!TryInt(out var lhtier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.LugianHammerThrowMinTier = Math.Max(1, lhtier);
                        break;
                    case "lugianhammer.proc":
                        if (!TryFloat(out var lhproc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LugianHammerThrowProcChance = Math.Clamp(lhproc, 0f, 1f);
                        break;
                    case "lugianhammer.scale":
                        if (!TryFloat(out var lhscale)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LugianHammerThrowDamageScale = Math.Clamp(lhscale, 0.05f, 1f);
                        break;
                    case "lugianhammer.radius":
                        if (!TryFloat(out var lhradius)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LugianHammerThrowRadius = Math.Max(1f, lhradius);
                        break;
                    case "lugianhammer.cooldown":
                        if (!TryFloat(out var lhcooldown)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LugianHammerThrowCooldownSeconds = Math.Max(1f, lhcooldown);
                        break;

                    case "opportunist.melee":
                        if (!TryFloat(out var opm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.OpportunistMeleeDropChance = Math.Clamp(opm, 0f, 1f);
                        break;
                    case "opportunist.missile":
                        if (!TryFloat(out var opmi)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.OpportunistMissileDropChance = Math.Clamp(opmi, 0f, 1f);
                        break;
                    case "opportunist.tier":
                        if (!TryInt(out var opt)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.OpportunistMinTier = Math.Max(1, opt);
                        break;
                    case "opportunist.bonus":
                        if (!TryFloat(out var opb)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.OpportunistDamageBonus = Math.Clamp(opb, 0f, 5f);
                        break;
                    case "opportunist.window":
                        if (!TryFloat(out var opw)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.OpportunistWindowSeconds = Math.Max(1f, opw);
                        break;
                    case "executioner.melee":
                        if (!TryFloat(out var exm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ExecutionerMeleeDropChance = Math.Clamp(exm, 0f, 1f);
                        break;
                    case "executioner.missile":
                        if (!TryFloat(out var exmi)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ExecutionerMissileDropChance = Math.Clamp(exmi, 0f, 1f);
                        break;
                    case "executioner.tier":
                        if (!TryInt(out var ext)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.ExecutionerMinTier = Math.Max(1, ext);
                        break;
                    case "executioner.bonus":
                        if (!TryFloat(out var exb)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ExecutionerDamageBonus = Math.Clamp(exb, 0f, 5f);
                        break;
                    case "executioner.threshold":
                        if (!TryFloat(out var exth)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ExecutionerHealthThreshold = Math.Clamp(exth, 0.01f, 1f);
                        break;

                    case "dinnerware.drop":
                        if (!TryFloat(out var dwdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DinnerwareWeaponDropChance = dwdrop;
                        break;
                    case "dinnerware.tier":
                        if (!TryInt(out var dwtier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.DinnerwareWeaponMinTier = dwtier;
                        break;
                    case "dinnerware.spin":
                        if (!TryFloat(out var dwspin)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DinnerwareSpinDropChance = Math.Clamp(dwspin, 0f, 1f);
                        break;
                    case "dinnerware.spintier":
                        if (!TryInt(out var dwspintier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.DinnerwareSpinMinTier = dwspintier;
                        break;
                    case "dinnerware.scale":
                        if (!TryFloat(out var dwscale)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DinnerwareSpinDamageScale = Math.Max(0f, dwscale);
                        break;
                    case "dinnerware.radius":
                        if (!TryFloat(out var dwradius)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.DinnerwareSpinRadius = Math.Max(1f, dwradius);
                        break;

                    case "quickening.drop":
                        if (!TryFloat(out var qdrop)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.QuickeningDaggerDropChance = qdrop;
                        break;
                    case "quickening.tier":
                        if (!TryInt(out var qtier)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerMinTier = qtier;
                        break;
                    case "quickening.procmin":
                        if (!TryInt(out var qpmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerProcMin = qpmin;
                        break;
                    case "quickening.procmax":
                        if (!TryInt(out var qpmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerProcMax = qpmax;
                        break;
                    case "quickening.speedmin":
                        if (!TryInt(out var qsmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerSpeedMin = qsmin;
                        break;
                    case "quickening.speedmax":
                        if (!TryInt(out var qsmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerSpeedMax = qsmax;
                        break;
                    case "quickening.durmin":
                        if (!TryInt(out var qdmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerDurationMin = Math.Max(1, qdmin);
                        break;
                    case "quickening.durmax":
                        if (!TryInt(out var qdmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.QuickeningDaggerDurationMax = Math.Max(1, qdmax);
                        break;

                    case "lootmod.mult":
                        if (!TryFloat(out var lmm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.LootModifierGlobalDropMultiplier = lmm;
                        break;
                    case "lootmod.exclusive":
                        if (!bool.TryParse(raw, out var lme)) { BadValue(session, key, "bool"); return; }
                        DerpACEConfig.LootModifierExclusivePerItem = lme;
                        break;
                    case "lootmod.interchange":
                        if (!bool.TryParse(raw, out var lmi)) { BadValue(session, key, "bool"); return; }
                        DerpACEConfig.LootModifierInterchangeable = lmi;
                        break;
                    case "lootmod.interchangetier":
                        if (!TryInt(out var lmit)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.LootModifierInterchangeableMinTier = lmit;
                        break;

                    case "armor.banenormal":
                        if (!TryFloat(out var abn)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ArmorBaneChanceNormal = abn;
                        break;
                    case "armor.banecovenant":
                        if (!TryFloat(out var abc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ArmorBaneChanceCovenant = abc;
                        break;
                    case "armor.enchbonus":
                        if (!TryFloat(out var aeb)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.ArmorEnchantmentChanceBonus = Math.Clamp(aeb, 0f, 1f);
                        break;
                    case "armor.enchmax":
                        if (!TryInt(out var aem)) { BadValue(session, key, "int >= 1"); return; }
                        DerpACEConfig.ArmorMaxEnchantments = Math.Max(1, aem);
                        break;
                    case "armor.enchmult":
                        if (!TryFloat(out var aeml)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.ArmorExtraEnchantmentChanceMult = Math.Clamp(aeml, 0f, 1f);
                        break;

                    case "blast.mintier":
                        if (!TryInt(out var bmt)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.WeaponBlastProcMinTier = Math.Max(1, bmt);
                        break;
                    case "blast.chancemin":
                        if (!TryFloat(out var bcmin2)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.WeaponBlastProcChanceMin = Math.Clamp(bcmin2, 0f, 1f);
                        break;
                    case "blast.chancemax":
                        if (!TryFloat(out var bcmax2)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.WeaponBlastProcChanceMax = Math.Clamp(bcmax2, 0f, 1f);
                        break;
                    case "blast.ratemin":
                        if (!TryFloat(out var brmin)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.WeaponBlastProcRateMin = Math.Clamp(brmin, 0f, 1f);
                        break;
                    case "blast.ratemax":
                        if (!TryFloat(out var brmax)) { BadValue(session, key, "float 0-1"); return; }
                        DerpACEConfig.WeaponBlastProcRateMax = Math.Clamp(brmax, 0f, 1f);
                        break;

                    case "mobmod.enabled":
                        if (!bool.TryParse(raw, out var mme)) { BadValue(session, key, "bool"); return; }
                        DerpACEConfig.MobModifierEnabled = mme;
                        break;
                    case "mobmod.level":
                        if (!TryInt(out var mml)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.MobModifierMinLevel = Math.Max(1, mml);
                        break;
                    case "mobmod.tier":
                        if (!TryInt(out var mmt)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.MobModifierMinTier = mmt;
                        break;
                    case "mobmod.defcap":
                        if (!TryInt(out var mmdc)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.MobModifierDefenseSkillCap = Math.Max(0, mmdc);
                        break;
                    case "vampiric.chance":
                        if (!TryFloat(out var vmc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.VampiricMobChance = vmc;
                        break;
                    case "vampiric.lifestealmin":
                        if (!TryInt(out var vlsmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.VampiricLifestealMin = vlsmin;
                        break;
                    case "vampiric.lifestealmax":
                        if (!TryInt(out var vlsmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.VampiricLifestealMax = vlsmax;
                        break;
                    case "thiefmob.chance":
                        if (!TryFloat(out var tmc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThiefMobChance = tmc;
                        break;
                    case "scoutmob.chance":
                        if (!TryFloat(out var smobc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ScoutMobChance = smobc;
                        break;
                    case "thiefmob.proc":
                        if (!TryFloat(out var tsp)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThiefStealProc = tsp;
                        break;
                    case "thiefmob.chestchance":
                        if (!TryFloat(out var tcc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThiefChestDropChance = tcc;
                        break;
                    case "thiefmob.chestwcid":
                        if (!uint.TryParse(raw, out var tcw)) { BadValue(session, key, "uint"); return; }
                        DerpACEConfig.ThiefChestWcid = tcw;
                        break;
                    case "thiefmob.chestdespawn":
                        if (!TryFloat(out var tcd)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ThiefChestDespawnSeconds = tcd;
                        break;
                    case "simulacrum.chance":
                        if (!TryFloat(out var smc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.SimulacrumMobChance = smc;
                        break;
                    case "healermob.chance":
                        if (!TryFloat(out var hmc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.HealerMobChance = hmc;
                        break;
                    case "enchantermob.chance":
                        if (!TryFloat(out var emc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.EnchanterMobChance = Math.Clamp(emc, 0f, 1f);
                        break;
                    case "shamanmob.chance":
                        if (!TryFloat(out var shmc)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.ShamanMobChance = Math.Clamp(shmc, 0f, 1f);
                        break;
                    case "healermob.range":
                        if (!TryFloat(out var hmr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.HealerMobRange = hmr;
                        break;
                    case "healermob.threshold":
                        if (!TryFloat(out var hmt)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.HealerMobHealThreshold = hmt;
                        break;
                    case "healermob.cooldown":
                        if (!TryFloat(out var hmcd)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.HealerMobCooldownSeconds = hmcd;
                        break;
                    case "tankmob.chance":
                        if (!TryFloat(out var tankmobchance)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.TankMobChance = tankmobchance;
                        break;
                    case "tankmob.hpmult":
                        if (!TryFloat(out var tmhm)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.TankMobHealthMultiplier = tmhm;
                        break;
                    case "tankmob.physreduction":
                        if (!TryFloat(out var tmpr)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.TankMobPhysicalReduction = tmpr;
                        break;
                    case "tankmob.healbonus":
                        if (!TryFloat(out var tmhb)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.TankMobHealBonus = tmhb;
                        break;
                    case "tankmob.skillbonus":
                        if (!TryInt(out var tmsb)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.TankMobSkillBonus = tmsb;
                        break;
                    case "ironman.enabled":
                        if (!bool.TryParse(raw, out var ime)) { BadValue(session, key, "bool"); return; }
                        DerpACEConfig.IronmanEnabled = ime;
                        break;

                    case "ironman.xp":
                        if (!TryFloat(out var imxp)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.IronmanXpScalar = Math.Max(0.0f, imxp);
                        break;

                    case "nomad.xp":
                        if (!TryFloat(out var nomx)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.NomadXpScalar = Math.Max(0.0f, nomx);
                        break;

                    case "hardcore.xp":
                        if (!TryFloat(out var hcxp)) { BadValue(session, key, "float"); return; }
                        DerpACEConfig.HardcoreXpScalar = Math.Max(0.0f, hcxp);
                        break;

                    case "vendor.loot":
                        if (!bool.TryParse(raw, out var vle)) { BadValue(session, key, "bool"); return; }
                        DerpACEConfig.VendorRandomLootEnabled = vle;
                        break;

                    case "vendor.lootmin":
                        if (!int.TryParse(raw, out var vlmin)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.VendorRandomLootMinItems = Math.Max(0, vlmin);
                        break;

                    case "vendor.lootmax":
                        if (!int.TryParse(raw, out var vlmax)) { BadValue(session, key, "int"); return; }
                        DerpACEConfig.VendorRandomLootMaxItems = Math.Max(1, vlmax);
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
                $"Invalid value for '{key}' - expected a {type}.",
                ChatMessageType.Broadcast);
        }

        // ── @vendortier ────────────────────────────────────────────────────────
        // Usage:
        //   @vendortier              - show tier for the vendor you are targeting
        //   @vendortier <1-8>        - set explicit tier override on targeted vendor
        //   @vendortier clear        - remove explicit tier override (revert to auto)
        // ─────────────────────────────────────────────────────────────────────
        [CommandHandler("vendortier", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "View or set the random-loot tier on the vendor you are currently targeting.",
            "<1-8>    - force a specific tier\n" +
            "clear    - remove the override (vendor will auto-detect from town location)\n" +
            "(no arg) - report the current tier and auto-detected town")]
        public static void HandleVendorTier(Session session, params string[] parameters)
        {
            var player = session.Player;
            if (player == null) return;

            // Resolve the targeted vendor.
            var target = player.CurrentAppraisalTarget.HasValue
                ? player.CurrentLandblock?.GetObject(player.CurrentAppraisalTarget.Value) as Vendor
                : null;

            // Also accept the last opened vendor.
            if (target == null && player.LastOpenedContainerId != default)
                target = player.CurrentLandblock?.GetObject(player.LastOpenedContainerId) as Vendor;

            if (target == null)
            {
                CommandHandlerHelper.WriteOutputInfo(session,
                    "No vendor targeted. Approach and appraise a vendor first.",
                    ChatMessageType.Broadcast);
                return;
            }

            var lbX = (int)target.Location.LandblockX;
            var lbY = (int)target.Location.LandblockY;
            var autoTier = VendorTownTier.GetTierForVendor(target);
            var townName = VendorTownTier.GetTownName(lbX, lbY) ?? $"unknown (LB {lbX:X2},{lbY:X2})";
            var explicitTier = target.GetProperty(PropertyInt.VendorLootTier);

            if (parameters.Length == 0)
            {
                var tierLine = explicitTier.HasValue && explicitTier.Value > 0
                    ? $"Explicit override: T{explicitTier.Value}  |  Auto-detected: T{autoTier} ({townName})"
                    : $"No override set  |  Auto-detected: T{autoTier} ({townName})";

                CommandHandlerHelper.WriteOutputInfo(session,
                    $"[VendorTier] {target.Name} ({target.WeenieClassId})\n  {tierLine}",
                    ChatMessageType.Broadcast);
                return;
            }

            var arg = parameters[0].Trim().ToLower();

            if (arg == "clear")
            {
                target.RemoveProperty(PropertyInt.VendorLootTier);
                // Reset inventory so it re-stocks on next approach.
                target.DefaultItemsForSale.Clear();
                target.SetProperty(ACE.Entity.Enum.Properties.PropertyBool.Open, false); // force reload flag
                CommandHandlerHelper.WriteOutputInfo(session,
                    $"[VendorTier] Explicit tier cleared from {target.Name}. Will auto-detect as T{autoTier} ({townName}) on next approach.",
                    ChatMessageType.Broadcast);
                return;
            }

            if (!int.TryParse(arg, out var newTier) || newTier < 1 || newTier > 8)
            {
                CommandHandlerHelper.WriteOutputInfo(session,
                    "Usage: @vendortier <1-8>  |  @vendortier clear",
                    ChatMessageType.Broadcast);
                return;
            }

            target.SetProperty(PropertyInt.VendorLootTier, newTier);
            // Clear cached inventory so it regenerates on next approach.
            target.DefaultItemsForSale.Clear();
            var field = typeof(Vendor).GetField("inventoryloaded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(target, false);

            CommandHandlerHelper.WriteOutputInfo(session,
                $"[VendorTier] {target.Name} ({target.WeenieClassId}) set to T{newTier}. Re-approach the vendor to reload stock.",
                ChatMessageType.Broadcast);
        }

        // ── @derpconfig ──────────────────────────────────────────────────────

        [CommandHandler("derpconfig", AccessLevel.Developer, CommandHandlerFlag.None, 0,
            "Manage the DerpAce.json runtime config file.",
            "reload   - reload DerpAce.json from disk and apply all values\n" +
            "show     - print the current in-memory config values\n" +
            "save     - write current in-memory values back to DerpAce.json")]
        public static void HandleDerpConfig(Session session, params string[] parameters)
        {
            var cmd = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "show";

            void Reply(string msg)
            {
                if (session != null)
                    session.Network.EnqueueSend(new Network.GameMessages.Messages.GameMessageSystemChat(msg, ChatMessageType.Broadcast));
                else
                    Console.WriteLine(msg);
            }

            switch (cmd)
            {
                case "reload":
                {
                    var result = DerpAceConfigManager.Reload();
                    Reply(result);
                    break;
                }
                case "save":
                {
                    DerpAceConfigManager.Save();
                    Reply("DerpAce config saved to disk.");
                    break;
                }
                case "show":
                default:
                {
                    var c = DerpAceConfigManager.Config;
                    var sb = new StringBuilder();
                    sb.AppendLine("── DerpAce Config ──────────────────────────────");
                    sb.AppendLine($"  [Section Toggles]");
                    sb.AppendLine($"    enable_teleport           = {c.EnableTeleport}");
                    sb.AppendLine($"    enable_mysterious_stranger= {c.EnableMysteriousStranger}");
                    sb.AppendLine($"    enable_mob_modifiers      = {c.EnableMobModifiers}");
                    sb.AppendLine($"    enable_derpcoin           = {c.EnableDerpcoin}");
                    sb.AppendLine($"    enable_custom_weapons     = {c.EnableCustomWeapons}");
                    sb.AppendLine($"    enable_armor_enchants     = {c.EnableArmorEnchants}");
                    sb.AppendLine($"    enable_vampiric_jewelry   = {c.EnableVampiricJewelry}");
                    sb.AppendLine($"    enable_prepatch_variants  = {c.EnablePrePatchVariants}");
                    sb.AppendLine($"  [Mutator Toggles]");
                    sb.AppendLine($"    nocturnal={c.NocturnalMobEnabled}  exploding={c.ExplodingMobEnabled}  vampiric={c.VampiricMobEnabled}  thief={c.ThiefMobEnabled}");
                    sb.AppendLine($"    scout={c.ScoutMobEnabled}  simulacrum={c.SimulacrumMobEnabled}  healer={c.HealerMobEnabled}  tank={c.TankMobEnabled}");
                    sb.AppendLine($"    reaper={c.ReaperMobEnabled}  necromancer={c.NecromancerMobEnabled}  warder={c.WarderMobEnabled}  enchanter={c.EnchanterMobEnabled}  shaman={c.ShamanMobEnabled}");
                    sb.AppendLine($"  [Weapon / LootGen Toggles]");
                    sb.AppendLine($"    defender={c.DefenderShieldEnabled}  archmagi={c.ArchmagiEnabled}  hierophant={c.HierophantEnabled}");
                    sb.AppendLine($"    thievesdagger={c.ThievesDaggerEnabled}  sentinel={c.SentinelSpearEnabled}  unarmedelem={c.UnarmedElemEnabled}  pugilist={c.PugilistWeaponEnabled}");
                    sb.AppendLine($"    fencer={c.FencerBladeEnabled}  ravager={c.RavagerAxeEnabled}  warden={c.WardenMaulEnabled}");
                    sb.AppendLine($"    resolute={c.ResoluteBladeEnabled}  polebreaker={c.PolebreakerStaffEnabled}  stalker={c.StalkerBowEnabled}");
                    sb.AppendLine($"    breacher={c.BreacherCrossbowEnabled}  reaperatlatl={c.ReaperAtlatlEnabled}  dartflinger={c.RicochetAtlatlEnabled}");
                    sb.AppendLine($"    dinnerware={c.DinnerwareWeaponEnabled}  elemblast={c.WeaponElemBlastEnabled}  stonehand={c.LugianHammerThrowEnabled}");
                    sb.AppendLine($"    opportunist={c.OpportunistWeaponEnabled}  executioner={c.ExecutionerWeaponEnabled}");
                    sb.AppendLine($"    dinnerware_drop={c.DinnerwareWeaponDropChance:0.###}  dinnerware_mintier={c.DinnerwareWeaponMinTier}");
                    sb.AppendLine($"    dinnerware_spin={c.DinnerwareSpinDropChance:0.###}  dinnerware_spin_min={c.DinnerwareSpinMinTier}  spin_scale={c.DinnerwareSpinDamageScale:0.###}  spin_radius={c.DinnerwareSpinRadius:0.###}");
                    sb.AppendLine($"  [TP]");
                    sb.AppendLine($"    tp_cost_per_meter         = {c.TpCostPerMeter}");
                    sb.AppendLine($"    tp_min_cost               = {c.TpMinCost}");
                    sb.AppendLine($"    tp_request_ttl_seconds    = {c.TpRequestTtlSeconds}");
                    sb.AppendLine($"  [Mysterious Stranger]");
                    sb.AppendLine($"    stranger_min_vitae_percent            = {c.StrangerMinVitaePercent}");
                    sb.AppendLine($"    stranger_max_vitae_percent            = {c.StrangerMaxVitaePercent}");
                    sb.AppendLine($"    stranger_min_chest_opens              = {c.StrangerMinChestOpens}");
                    sb.AppendLine($"    stranger_max_chest_opens              = {c.StrangerMaxChestOpens}");
                    sb.AppendLine($"    stranger_chest_despawn_seconds        = {c.StrangerChestDespawnSeconds}");
                    sb.AppendLine($"    stranger_chest_despawn_warning_seconds= {c.StrangerChestDespawnWarningSeconds}");
                    sb.AppendLine($"    stranger_chest_despawn_grace_seconds  = {c.StrangerChestDespawnGraceSeconds}");
                    sb.AppendLine($"    stranger_chest_arc_distance           = {c.StrangerChestArcDistance}");
                    sb.AppendLine($"    stranger_chest_arc_sweep_degrees      = {c.StrangerChestArcSweepDegrees}");
                    sb.AppendLine($"    stranger_dramatic_spawn_delay         = {c.StrangerDramaticSpawnDelay}");
                    sb.AppendLine($"    stranger_obfuscated_burden_min        = {c.StrangerObfuscatedBurdenMin}");
                    sb.AppendLine($"    stranger_obfuscated_burden_max        = {c.StrangerObfuscatedBurdenMax}");
                    sb.AppendLine($"    stranger_min_account_age_days         = {c.StrangerMinAccountAgeDays}");
                    sb.AppendLine($"    stranger_deal_cooldown_seconds        = {c.StrangerDealCooldownSeconds}");
                    sb.AppendLine($"    stranger_junk_prank_chance            = {c.StrangerJunkPrankChance}");
                    sb.Append("────────────────────────────────────────────────");
                    Reply(sb.ToString());
                    break;
                }
            }
        }
    }
}
