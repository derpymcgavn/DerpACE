using System.Text.Json.Serialization;

namespace ACE.Server.DerpAce
{
    /// <summary>
    /// All DerpACE-specific tunables, serialised to/from DerpAce.json.
    /// Reload at runtime with: @derpconfig reload
    /// </summary>
    public class DerpAceConfiguration
    {
        // Teleport (/tp)
        [JsonPropertyName("tp_cost_per_meter")]
        public double TpCostPerMeter { get; set; } = 2.0;
        [JsonPropertyName("tp_min_cost")]
        public int TpMinCost { get; set; } = 50;
        [JsonPropertyName("tp_request_ttl_seconds")]
        public double TpRequestTtlSeconds { get; set; } = 30.0;
        // ── Section Master Toggles ───────────────────────────────────────────
        // Set any of these to false to completely disable that system on reload.
        [JsonPropertyName("enable_teleport")]
        public bool EnableTeleport { get; set; } = true;
        [JsonPropertyName("enable_mysterious_stranger")]
        public bool EnableMysteriousStranger { get; set; } = true;
        [JsonPropertyName("enable_mob_modifiers")]
        public bool EnableMobModifiers { get; set; } = true;
        [JsonPropertyName("enable_derpcoin")]
        public bool EnableDerpcoin { get; set; } = true;
        [JsonPropertyName("enable_custom_weapons")]
        public bool EnableCustomWeapons { get; set; } = true;
        [JsonPropertyName("enable_armor_enchants")]
        public bool EnableArmorEnchants { get; set; } = true;
        [JsonPropertyName("enable_vampiric_jewelry")]
        public bool EnableVampiricJewelry { get; set; } = true;
        [JsonPropertyName("enable_prepatch_variants")]
        public bool EnablePrePatchVariants { get; set; } = true;

        // ── Per-Mutator Toggles ───────────────────────────────────────────────
        [JsonPropertyName("mob_nocturnal_enabled")]
        public bool NocturnalMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_exploding_enabled")]
        public bool ExplodingMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_vampiric_enabled")]
        public bool VampiricMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_thief_enabled")]
        public bool ThiefMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_scout_enabled")]
        public bool ScoutMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_simulacrum_enabled")]
        public bool SimulacrumMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_healer_enabled")]
        public bool HealerMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_tank_enabled")]
        public bool TankMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_reaper_enabled")]
        public bool ReaperMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_necromancer_enabled")]
        public bool NecromancerMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_warder_enabled")]
        public bool WarderMobEnabled { get; set; } = true;

        // ── Per-Custom-Weapon Toggles ─────────────────────────────────────────
        [JsonPropertyName("defender_shield_enabled")]
        public bool DefenderShieldEnabled { get; set; } = true;
        [JsonPropertyName("archmagi_enabled")]
        public bool ArchmagiEnabled { get; set; } = true;
        [JsonPropertyName("hierophant_enabled")]
        public bool HierophantEnabled { get; set; } = true;
        [JsonPropertyName("thief_dagger_enabled")]
        public bool ThievesDaggerEnabled { get; set; } = true;
        [JsonPropertyName("sentinel_spear_enabled")]
        public bool SentinelSpearEnabled { get; set; } = true;
        [JsonPropertyName("unarmed_elem_enabled")]
        public bool UnarmedElemEnabled { get; set; } = true;
        [JsonPropertyName("fencer_blade_enabled")]
        public bool FencerBladeEnabled { get; set; } = true;
        [JsonPropertyName("ravager_axe_enabled")]
        public bool RavagerAxeEnabled { get; set; } = true;
        [JsonPropertyName("warden_maul_enabled")]
        public bool WardenMaulEnabled { get; set; } = true;
        [JsonPropertyName("resolute_blade_enabled")]
        public bool ResoluteBladeEnabled { get; set; } = true;
        [JsonPropertyName("polebreaker_staff_enabled")]
        public bool PolebreakerStaffEnabled { get; set; } = true;
        [JsonPropertyName("stalker_bow_enabled")]
        public bool StalkerBowEnabled { get; set; } = true;
        [JsonPropertyName("breacher_crossbow_enabled")]
        public bool BreacherCrossbowEnabled { get; set; } = true;
        [JsonPropertyName("reaper_atlatl_enabled")]
        public bool ReaperAtlatlEnabled { get; set; } = true;
        [JsonPropertyName("ricochet_atlatl_enabled")]
        public bool RicochetAtlatlEnabled { get; set; } = true;
        [JsonPropertyName("dinnerware_weapon_enabled")]
        public bool DinnerwareWeaponEnabled { get; set; } = true;
        [JsonPropertyName("quickening_dagger_enabled")]
        public bool QuickeningDaggerEnabled { get; set; } = true;
        [JsonPropertyName("weapon_elem_blast_enabled")]
        public bool WeaponElemBlastEnabled { get; set; } = true;

        [JsonPropertyName("dinnerware_weapon_drop_chance")]
        public float DinnerwareWeaponDropChance { get; set; } = 0.02f;
        [JsonPropertyName("dinnerware_weapon_min_tier")]
        public int DinnerwareWeaponMinTier { get; set; } = 4;
        [JsonPropertyName("dinnerware_spin_drop_chance")]
        public float DinnerwareSpinDropChance { get; set; } = 0.08f;
        [JsonPropertyName("dinnerware_spin_min_tier")]
        public int DinnerwareSpinMinTier { get; set; } = 3;
        [JsonPropertyName("dinnerware_spin_damage_scale")]
        public float DinnerwareSpinDamageScale { get; set; } = 0.20f;
        [JsonPropertyName("dinnerware_spin_radius")]
        public float DinnerwareSpinRadius { get; set; } = 5.0f;
        [JsonPropertyName("quickening_dagger_drop_chance")]
        public float QuickeningDaggerDropChance { get; set; } = 0.02f;
        [JsonPropertyName("quickening_dagger_min_tier")]
        public int QuickeningDaggerMinTier { get; set; } = 5;
        [JsonPropertyName("quickening_dagger_proc_min")]
        public int QuickeningDaggerProcMin { get; set; } = 8;
        [JsonPropertyName("quickening_dagger_proc_max")]
        public int QuickeningDaggerProcMax { get; set; } = 14;
        [JsonPropertyName("quickening_dagger_speed_min")]
        public int QuickeningDaggerSpeedMin { get; set; } = 12;
        [JsonPropertyName("quickening_dagger_speed_max")]
        public int QuickeningDaggerSpeedMax { get; set; } = 24;
        [JsonPropertyName("quickening_dagger_duration_min")]
        public int QuickeningDaggerDurationMin { get; set; } = 4;
        [JsonPropertyName("quickening_dagger_duration_max")]
        public int QuickeningDaggerDurationMax { get; set; } = 7;


        // Mysterious Stranger
        [JsonPropertyName("stranger_min_vitae_percent")]
        public int StrangerMinVitaePercent { get; set; } = 1;
        [JsonPropertyName("stranger_max_vitae_percent")]
        public int StrangerMaxVitaePercent { get; set; } = 40;
        [JsonPropertyName("stranger_min_chest_opens")]
        public int StrangerMinChestOpens { get; set; } = 0;
        [JsonPropertyName("stranger_max_chest_opens")]
        public int StrangerMaxChestOpens { get; set; } = 4;
        [JsonPropertyName("stranger_chest_despawn_seconds")]
        public float StrangerChestDespawnSeconds { get; set; } = 120.0f;
        [JsonPropertyName("stranger_chest_despawn_warning_seconds")]
        public float StrangerChestDespawnWarningSeconds { get; set; } = 10.0f;
        [JsonPropertyName("stranger_chest_despawn_grace_seconds")]
        public float StrangerChestDespawnGraceSeconds { get; set; } = 5.0f;
        [JsonPropertyName("stranger_chest_arc_distance")]
        public float StrangerChestArcDistance { get; set; } = 4.0f;
        [JsonPropertyName("stranger_chest_arc_sweep_degrees")]
        public float StrangerChestArcSweepDegrees { get; set; } = 360.0f;
        [JsonPropertyName("stranger_dramatic_spawn_delay")]
        public float StrangerDramaticSpawnDelay { get; set; } = 0.9f;
        [JsonPropertyName("stranger_obfuscated_burden_min")]
        public int StrangerObfuscatedBurdenMin { get; set; } = 50;
        [JsonPropertyName("stranger_obfuscated_burden_max")]
        public int StrangerObfuscatedBurdenMax { get; set; } = 950;
        [JsonPropertyName("stranger_deal_cooldown_seconds")]
        public int StrangerDealCooldownSeconds { get; set; } = 86400;
        [JsonPropertyName("stranger_junk_prank_chance")]
        public double StrangerJunkPrankChance { get; set; } = 1.0;

        // Mob Modifiers
        [JsonPropertyName("mob_modifier_enabled")]
        public bool MobModifierEnabled { get; set; } = true;
        [JsonPropertyName("mob_modifier_min_tier")]
        public int MobModifierMinTier { get; set; } = 2;
        [JsonPropertyName("mob_nocturnal_chance")]
        public float NocturnalMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_exploding_chance")]
        public float ExplodingMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_exploding_radius")]
        public float ExplodingMobRadius { get; set; } = 6.0f;
        [JsonPropertyName("mob_exploding_damage_scale")]
        public float ExplodingMobDamageScale { get; set; } = 0.25f;
        [JsonPropertyName("mob_vampiric_chance")]
        public float VampiricMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_vampiric_lifesteal_min")]
        public int VampiricLifestealMin { get; set; } = 10;
        [JsonPropertyName("mob_vampiric_lifesteal_max")]
        public int VampiricLifestealMax { get; set; } = 25;
        [JsonPropertyName("mob_thief_chance")]
        public float ThiefMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_thief_steal_proc")]
        public float ThiefStealProc { get; set; } = 0.10f;
        [JsonPropertyName("mob_thief_chest_drop_chance")]
        public float ThiefChestDropChance { get; set; } = 0.10f;
        [JsonPropertyName("mob_thief_chest_wcid")]
        public uint ThiefChestWcid { get; set; } = 80524;
        [JsonPropertyName("mob_thief_chest_despawn_seconds")]
        public float ThiefChestDespawnSeconds { get; set; } = 30.0f;
        [JsonPropertyName("mob_scout_chance")]
        public float ScoutMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_simulacrum_chance")]
        public float SimulacrumMobChance { get; set; } = 0.0f;
        [JsonPropertyName("mob_healer_chance")]
        public float HealerMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_healer_range")]
        public float HealerMobRange { get; set; } = 25.0f;
        [JsonPropertyName("mob_healer_threshold")]
        public float HealerMobHealThreshold { get; set; } = 0.75f;
        [JsonPropertyName("mob_healer_cooldown_seconds")]
        public float HealerMobCooldownSeconds { get; set; } = 8.0f;
        [JsonPropertyName("mob_tank_chance")]
        public float TankMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_tank_health_multiplier")]
        public float TankMobHealthMultiplier { get; set; } = 2.5f;
        [JsonPropertyName("mob_tank_physical_reduction")]
        public float TankMobPhysicalReduction { get; set; } = 0.3f;
        [JsonPropertyName("mob_tank_heal_bonus")]
        public float TankMobHealBonus { get; set; } = 1.2f;
        [JsonPropertyName("mob_tank_skill_bonus")]
        public int TankMobSkillBonus { get; set; } = 200;
        [JsonPropertyName("mob_reaper_chance")]
        public float ReaperMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_reaper_damage_bonus")]
        public float ReaperDamageBonus { get; set; } = 1.35f;
        [JsonPropertyName("mob_reaper_lifedrain_pct")]
        public float ReaperLifedrainPct { get; set; } = 0.25f;
        [JsonPropertyName("mob_necromancer_chance")]
        public float NecromancerMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_necromancer_dot_chance")]
        public float NecromancerDotChance { get; set; } = 0.30f;
        [JsonPropertyName("mob_necromancer_dot_total")]
        public float NecromancerDotTotal { get; set; } = 60.0f;
        [JsonPropertyName("mob_warder_chance")]
        public float WarderMobChance { get; set; } = 0.0005f;
        [JsonPropertyName("mob_warder_range")]
        public float WarderRange { get; set; } = 8.0f;

        // Derpcoin Reward System
        [JsonPropertyName("derpcoin_wcid")]
        public uint DerpcoinWcid { get; set; } = 7000011;
        [JsonPropertyName("derpcoin_base_chance")]
        public float DerpcoinBaseChance { get; set; } = 0.001f;
        [JsonPropertyName("derpcoin_max_chance")]
        public float DerpcoinMaxChance { get; set; } = 0.06f;
        [JsonPropertyName("derpcoin_stack_multiplier")]
        public float DerpcoinStackMultiplier { get; set; } = 1.5f;

        // Loot Modifier Balance Controls
        [JsonPropertyName("loot_modifier_global_drop_multiplier")]
        public float LootModifierGlobalDropMultiplier { get; set; } = 1.0f;
        [JsonPropertyName("loot_modifier_exclusive_per_item")]
        public bool LootModifierExclusivePerItem { get; set; } = true;
        [JsonPropertyName("loot_modifier_interchangeable")]
        public bool LootModifierInterchangeable { get; set; } = true;
        [JsonPropertyName("loot_modifier_interchangeable_min_tier")]
        public int LootModifierInterchangeableMinTier { get; set; } = 6;

        // Armor Enchantments
        [JsonPropertyName("armor_bane_chance_normal")]
        public float ArmorBaneChanceNormal { get; set; } = 0.20f;
        [JsonPropertyName("armor_bane_chance_covenant")]
        public float ArmorBaneChanceCovenant { get; set; } = 0.60f;
        [JsonPropertyName("armor_enchantment_chance_bonus")]
        public float ArmorEnchantmentChanceBonus { get; set; } = 0.25f;
        [JsonPropertyName("armor_max_enchantments")]
        public int ArmorMaxEnchantments { get; set; } = 2;
        [JsonPropertyName("armor_extra_enchantment_chance_mult")]
        public float ArmorExtraEnchantmentChanceMult { get; set; } = 0.50f;

        // Defender Shield
        [JsonPropertyName("defender_drop_chance")]
        public float DefenderShieldDropChance { get; set; } = 0.05f;
        [JsonPropertyName("defender_min_tier")]
        public int DefenderShieldMinTier { get; set; } = 2;
        [JsonPropertyName("defender_aggro_bonus")]
        public float DefenderAggroBonus { get; set; } = 0.5f;

        // Archmagi Caster
        [JsonPropertyName("archmagi_drop_chance")]
        public float ArchmagiDropChance { get; set; } = 0.05f;
        [JsonPropertyName("archmagi_min_tier")]
        public int ArchmagiMinTier { get; set; } = 2;
        [JsonPropertyName("archmagi_proc_chance")]
        public float ArchmagiProcChance { get; set; } = 0.06f;
        [JsonPropertyName("archmagi_aggro_penalty")]
        public float ArchmagiAggroPenalty { get; set; } = 0.2f;
        [JsonPropertyName("archmagi_dual_cast_chance")]
        public float ArchmagiDualCastChance { get; set; } = 0.04f;
        [JsonPropertyName("archmagi_dual_cast_radius")]
        public float ArchmagiDualCastRadius { get; set; } = 10.0f;
        [JsonPropertyName("archmagi_dual_cast_damage_modifier")]
        public float ArchmagiDualCastDamageModifier { get; set; } = 0.75f;

        // Hierophant
        [JsonPropertyName("hierophant_drop_chance")]
        public float HierophantDropChance { get; set; } = 0.10f;
        [JsonPropertyName("hierophant_min_tier")]
        public int HierophantMinTier { get; set; } = 2;
        [JsonPropertyName("hierophant_heal_boost_min")]
        public float HierophantHealBoostMin { get; set; } = 0.01f;
        [JsonPropertyName("hierophant_heal_boost_max")]
        public float HierophantHealBoostMax { get; set; } = 0.10f;
        [JsonPropertyName("hierophant_hot_proc_chance")]
        public float HierophantHotProcChance { get; set; } = 0.15f;
        [JsonPropertyName("hierophant_hot_pct_min")]
        public float HierophantHotPctMin { get; set; } = 0.01f;
        [JsonPropertyName("hierophant_hot_pct_max")]
        public float HierophantHotPctMax { get; set; } = 0.25f;
        [JsonPropertyName("hierophant_hot_duration_seconds")]
        public float HierophantHotDurationSeconds { get; set; } = 12.0f;
        [JsonPropertyName("hierophant_hot_tick_interval")]
        public float HierophantHotTickInterval { get; set; } = 3.0f;
        [JsonPropertyName("hierophant_fellow_echo_pct")]
        public float HierophantFellowEchoPct { get; set; } = 0.20f;
        [JsonPropertyName("hierophant_fellow_echo_range")]
        public float HierophantFellowEchoRange { get; set; } = 30.0f;
        [JsonPropertyName("hierophant_aggro_bonus")]
        public float HierophantAggroBonus { get; set; } = 0.35f;

        // Thief Dagger
        [JsonPropertyName("sneak_attack_bonus_pct")]
        public float SneakAttackBonusPct { get; set; } = 0.15f;
        [JsonPropertyName("thief_dagger_drop_chance")]
        public float ThievesDaggerDropChance { get; set; } = 0.02f;
        [JsonPropertyName("thief_dagger_min_tier")]
        public int ThievesDaggerMinTier { get; set; } = 5;
        [JsonPropertyName("thief_dagger_proc_chance")]
        public float ThievesDaggerProcChance { get; set; } = 0.10f;
        [JsonPropertyName("thief_dagger_proc_bonus")]
        public float ThievesDaggerProcBonus { get; set; } = 0.10f;
        [JsonPropertyName("thief_dagger_aggro_penalty")]
        public float ThievesDaggerAggroPenalty { get; set; } = 0.3f;

        // Sentinel Spear
        [JsonPropertyName("sentinel_spear_drop_chance")]
        public float SentinelSpearDropChance { get; set; } = 0.02f;
        [JsonPropertyName("sentinel_spear_min_tier")]
        public int SentinelSpearMinTier { get; set; } = 5;
        [JsonPropertyName("sentinel_spear_proc_chance")]
        public float SentinelSpearProcChance { get; set; } = 0.08f;
        [JsonPropertyName("sentinel_spear_drain_pct")]
        public float SentinelSpearDrainPct { get; set; } = 0.08f;
        [JsonPropertyName("sentinel_spear_return_mult")]
        public float SentinelSpearReturnMult { get; set; } = 1.0f;
        [JsonPropertyName("sentinel_spear_aggro_bonus")]
        public float SentinelSpearAggroBonus { get; set; } = 0.25f;

        // Elemental Unarmed
        [JsonPropertyName("unarmed_elem_drop_chance")]
        public float UnarmedElemDropChance { get; set; } = 0.05f;
        [JsonPropertyName("unarmed_elem_proc_min")]
        public int UnarmedElemProcMin { get; set; } = 1;
        [JsonPropertyName("unarmed_elem_proc_max")]
        public int UnarmedElemProcMax { get; set; } = 5;

        // Fencer Blade
        [JsonPropertyName("fencer_blade_drop_chance")]
        public float FencerBladeDropChance { get; set; } = 0.02f;
        [JsonPropertyName("fencer_blade_min_tier")]
        public int FencerBladeMinTier { get; set; } = 5;
        [JsonPropertyName("fencer_pierce_min")]
        public int FencerPierceMin { get; set; } = 2;
        [JsonPropertyName("fencer_pierce_max")]
        public int FencerPierceMax { get; set; } = 8;
        [JsonPropertyName("fencer_pierce_proc_min")]
        public int FencerPierceProcMin { get; set; } = 2;
        [JsonPropertyName("fencer_pierce_proc_max")]
        public int FencerPierceProcMax { get; set; } = 6;
        [JsonPropertyName("fencer_deflect_min")]
        public int FencerDeflectMin { get; set; } = 1;
        [JsonPropertyName("fencer_deflect_max")]
        public int FencerDeflectMax { get; set; } = 2;

        // Ravager Axe
        [JsonPropertyName("ravager_axe_drop_chance")]
        public float RavagerAxeDropChance { get; set; } = 0.02f;
        [JsonPropertyName("ravager_axe_min_tier")]
        public int RavagerAxeMinTier { get; set; } = 5;
        [JsonPropertyName("ravager_proc_min")]
        public int RavagerProcMin { get; set; } = 2;
        [JsonPropertyName("ravager_proc_max")]
        public int RavagerProcMax { get; set; } = 4;
        [JsonPropertyName("ravager_bleed_min")]
        public int RavagerBleedMin { get; set; } = 20;
        [JsonPropertyName("ravager_bleed_max")]
        public int RavagerBleedMax { get; set; } = 40;
        [JsonPropertyName("ravager_two_hand_mult")]
        public float RavagerTwoHandMult { get; set; } = 1.35f;
        [JsonPropertyName("ravager_bleed_ticks")]
        public int RavagerBleedTicks { get; set; } = 3;
        [JsonPropertyName("ravager_bleed_interval")]
        public float RavagerBleedInterval { get; set; } = 2.5f;
        [JsonPropertyName("ravager_hammer_cleave_chance")]
        public float RavagerHammerCleaveChance { get; set; } = 0.15f;
        [JsonPropertyName("ravager_hammer_cleave_max_targets")]
        public int RavagerHammerCleaveMaxTargets { get; set; } = 5;
        [JsonPropertyName("ravager_hammer_cleave_damage_scale")]
        public float RavagerHammerCleaveDamageScale { get; set; } = 0.50f;
        [JsonPropertyName("ravager_hammer_cleave_radius")]
        public float RavagerHammerCleaveRadius { get; set; } = 10.0f;
        [JsonPropertyName("ravager_aggro_bonus")]
        public float RavagerAxeAggroBonus { get; set; } = 0.3f;

        // Warden Maul
        [JsonPropertyName("warden_maul_drop_chance")]
        public float WardenMaulDropChance { get; set; } = 0.02f;
        [JsonPropertyName("warden_maul_min_tier")]
        public int WardenMaulMinTier { get; set; } = 5;
        [JsonPropertyName("warden_proc_min")]
        public int WardenProcMin { get; set; } = 3;
        [JsonPropertyName("warden_proc_max")]
        public int WardenProcMax { get; set; } = 6;
        [JsonPropertyName("warden_penalty_min")]
        public int WardenPenaltyMin { get; set; } = 8;
        [JsonPropertyName("warden_penalty_max")]
        public int WardenPenaltyMax { get; set; } = 20;
        [JsonPropertyName("warden_duration_min")]
        public int WardenDurationMin { get; set; } = 4;
        [JsonPropertyName("warden_duration_max")]
        public int WardenDurationMax { get; set; } = 8;
        [JsonPropertyName("warden_two_hand_mult")]
        public float WardenTwoHandMult { get; set; } = 1.3f;
        [JsonPropertyName("warden_aggro_bonus")]
        public float WardenMaulAggroBonus { get; set; } = 0.3f;

        // Resolute Blade
        [JsonPropertyName("resolute_blade_drop_chance")]
        public float ResoluteBladeDropChance { get; set; } = 0.02f;
        [JsonPropertyName("resolute_blade_min_tier")]
        public int ResoluteBladeMinTier { get; set; } = 5;
        [JsonPropertyName("resolute_proc_min")]
        public int ResoluteProcMin { get; set; } = 15;
        [JsonPropertyName("resolute_proc_max")]
        public int ResoluteProcMax { get; set; } = 30;
        [JsonPropertyName("resolute_heal_min")]
        public int ResoluteHealMin { get; set; } = 2;
        [JsonPropertyName("resolute_heal_max")]
        public int ResoluteHealMax { get; set; } = 4;
        [JsonPropertyName("resolute_kill_burst_pct")]
        public float ResoluteKillBurstPct { get; set; } = 0.06f;
        [JsonPropertyName("resolute_two_hand_mult")]
        public float ResoluteTwoHandMult { get; set; } = 1.25f;

        // Polebreaker Staff
        [JsonPropertyName("polebreaker_drop_chance")]
        public float PolebreakerDropChance { get; set; } = 0.02f;
        [JsonPropertyName("polebreaker_min_tier")]
        public int PolebreakerMinTier { get; set; } = 5;
        [JsonPropertyName("polebreaker_stack_min")]
        public int PolebreakerStackMin { get; set; } = 1;
        [JsonPropertyName("polebreaker_stack_max")]
        public int PolebreakerStackMax { get; set; } = 2;
        [JsonPropertyName("polebreaker_max_stack_min")]
        public int PolebreakerMaxStackMin { get; set; } = 3;
        [JsonPropertyName("polebreaker_max_stack_max")]
        public int PolebreakerMaxStackMax { get; set; } = 5;
        [JsonPropertyName("polebreaker_aggro_bonus")]
        public float PolebreakerStaffAggroBonus { get; set; } = 0.2f;

        // Stalker Bow
        [JsonPropertyName("stalker_bow_drop_chance")]
        public float StalkerBowDropChance { get; set; } = 0.02f;
        [JsonPropertyName("stalker_bow_min_tier")]
        public int StalkerBowMinTier { get; set; } = 5;
        [JsonPropertyName("stalker_proc_min")]
        public int StalkerProcMin { get; set; } = 20;
        [JsonPropertyName("stalker_proc_max")]
        public int StalkerProcMax { get; set; } = 35;
        [JsonPropertyName("stalker_bonus_min")]
        public int StalkerBonusMin { get; set; } = 15;
        [JsonPropertyName("stalker_bonus_max")]
        public int StalkerBonusMax { get; set; } = 30;
        [JsonPropertyName("stalker_aggro_penalty")]
        public float StalkerBowAggroPenalty { get; set; } = 0.2f;

        // Breacher Crossbow
        [JsonPropertyName("breacher_crossbow_drop_chance")]
        public float BreacherCrossbowDropChance { get; set; } = 0.02f;
        [JsonPropertyName("breacher_crossbow_min_tier")]
        public int BreacherCrossbowMinTier { get; set; } = 5;
        [JsonPropertyName("breacher_armor_ignore_min")]
        public int BreacherArmorIgnoreMin { get; set; } = 4;
        [JsonPropertyName("breacher_armor_ignore_max")]
        public int BreacherArmorIgnoreMax { get; set; } = 10;

        // Reaper Atlatl
        [JsonPropertyName("reaper_atlatl_drop_chance")]
        public float ReaperAtlatlDropChance { get; set; } = 0.02f;
        [JsonPropertyName("reaper_atlatl_min_tier")]
        public int ReaperAtlatlMinTier { get; set; } = 5;
        [JsonPropertyName("reaper_atlatl_proc_min")]
        public int ReaperProcMin { get; set; } = 20;
        [JsonPropertyName("reaper_atlatl_proc_max")]
        public int ReaperProcMax { get; set; } = 40;
        [JsonPropertyName("reaper_atlatl_heal_min")]
        public int ReaperHealMin { get; set; } = 4;
        [JsonPropertyName("reaper_atlatl_heal_max")]
        public int ReaperHealMax { get; set; } = 10;

        // Ricochet Atlatl / Dartflinger
        [JsonPropertyName("ricochet_atlatl_drop_chance")]
        public float RicochetAtlatlDropChance { get; set; } = 0.02f;
        [JsonPropertyName("ricochet_atlatl_min_tier")]
        public int RicochetAtlatlMinTier { get; set; } = 5;
        [JsonPropertyName("ricochet_proc_min")]
        public int RicochetProcMin { get; set; } = 15;
        [JsonPropertyName("ricochet_proc_max")]
        public int RicochetProcMax { get; set; } = 30;
        [JsonPropertyName("ricochet_damage_scale")]
        public float RicochetDamageScale { get; set; } = 0.50f;
        [JsonPropertyName("ricochet_radius")]
        public float RicochetRadius { get; set; } = 10.0f;

        // Weapon Elemental Blast-on-Strike
        [JsonPropertyName("weapon_blast_proc_min_tier")]
        public int WeaponBlastProcMinTier { get; set; } = 5;
        [JsonPropertyName("weapon_blast_proc_chance_min")]
        public float WeaponBlastProcChanceMin { get; set; } = 0.01f;
        [JsonPropertyName("weapon_blast_proc_chance_max")]
        public float WeaponBlastProcChanceMax { get; set; } = 0.04f;
        [JsonPropertyName("weapon_blast_proc_rate_min")]
        public float WeaponBlastProcRateMin { get; set; } = 0.01f;
        [JsonPropertyName("weapon_blast_proc_rate_max")]
        public float WeaponBlastProcRateMax { get; set; } = 0.04f;

        // Vampiric Jewelry
        [JsonPropertyName("vampiric_jewelry_drop_chance")]
        public float VampiricJewelryDropChance { get; set; } = 0.04f;
        [JsonPropertyName("vampiric_jewelry_min_tier")]
        public int VampiricJewelryMinTier { get; set; } = 4;
        [JsonPropertyName("vampiric_jewelry_points_min")]
        public int VampiricJewelryPointsMin { get; set; } = 1;
        [JsonPropertyName("vampiric_jewelry_points_max")]
        public int VampiricJewelryPointsMax { get; set; } = 3;
        [JsonPropertyName("vampiric_jewelry_regen_interval_seconds")]
        public float VampiricJewelryRegenIntervalSeconds { get; set; } = 5.0f;
        [JsonPropertyName("vampiric_jewelry_on_hit_proc_chance")]
        public float VampiricJewelryOnHitProcChance { get; set; } = 0.04f;
        [JsonPropertyName("vampiric_jewelry_on_hit_multiplier")]
        public float VampiricJewelryOnHitMultiplier { get; set; } = 2.0f;

        // Pre-Patch Variants
        [JsonPropertyName("prepatch_8489_chance")]
        public float PrePatch8489Chance { get; set; } = 0.10f;
        [JsonPropertyName("prepatch_8489_setup_id")]
        public uint PrePatch8489SetupId { get; set; } = 33555248u;
        [JsonPropertyName("prepatch_8489_clothing_base")]
        public uint PrePatch8489ClothingBase { get; set; } = 268435629u;
        [JsonPropertyName("prepatch_8489_palette_base")]
        public uint PrePatch8489PaletteBase { get; set; } = 67108990u;

        // Vendor Random Loot
        [JsonPropertyName("vendor_random_loot_enabled")]
        public bool VendorRandomLootEnabled { get; set; } = true;
        [JsonPropertyName("vendor_random_loot_min_items")]
        public int VendorRandomLootMinItems { get; set; } = 1;
        [JsonPropertyName("vendor_random_loot_max_items")]
        public int VendorRandomLootMaxItems { get; set; } = 10;
        [JsonPropertyName("vendor_restock_min_minutes")]
        public int VendorRestockMinMinutes { get; set; } = 15;
        [JsonPropertyName("vendor_restock_max_minutes")]
        public int VendorRestockMaxMinutes { get; set; } = 45;

        // Ironman Mode
        [JsonPropertyName("ironman_enabled")]
        public bool IronmanEnabled { get; set; } = true;
        [JsonPropertyName("ironman_welcome_message")]
        public string IronmanWelcomeMessage { get; set; } = "You have committed to the Ironman path. There is no turning back.";
        [JsonPropertyName("ironman_credits_to_plan_for")]
        public int IronmanCreditsToPlanFor { get; set; } = 50;
        [JsonPropertyName("ironman_hardcore_starting_lives")]
        public int IronmanHardcoreStartingLives { get; set; } = 1;
        [JsonPropertyName("ironman_hardcore_seconds_between_deaths")]
        public float IronmanHardcoreSecondsBetweenDeaths { get; set; } = 604800.0f;

        // ── Bank ─────────────────────────────────────────────────────────────
        [JsonPropertyName("enable_bank")]
        public bool EnableBank { get; set; } = true;
        [JsonPropertyName("bank_direct_deposit")]
        public bool BankDirectDeposit { get; set; } = true;
        [JsonPropertyName("bank_vendors_use_bank")]
        public bool BankVendorsUseBank { get; set; } = true;
        [JsonPropertyName("bank_max_coins_dropped")]
        public int BankMaxCoinsDropped { get; set; } = 1_000_000;
        [JsonPropertyName("bank_excess_set_to_max")]
        public bool BankExcessSetToMax { get; set; } = true;
        [JsonPropertyName("bank_cash_property")]
        public int BankCashProperty { get; set; } = 39999;
    }
}

