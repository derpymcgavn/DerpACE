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
        [JsonPropertyName("modern_mob_ai_enabled")]
        public bool ModernMobAiEnabled { get; set; } = true;
        [JsonPropertyName("modern_mob_ai_switch_threshold")]
        public float ModernMobAiSwitchThreshold { get; set; } = 1.20f;
        [JsonPropertyName("mob_movement_sync_interval_seconds")]
        public float MobMovementSyncIntervalSeconds { get; set; } = 0.20f;
        [JsonPropertyName("mob_outdoor_chase_range")]
        public float MobOutdoorChaseRange { get; set; } = 576.0f;
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
        [JsonPropertyName("admin_map_enabled")]
        public bool AdminMapEnabled { get; set; } = false;
        [JsonPropertyName("admin_map_host")]
        public string AdminMapHost { get; set; } = "127.0.0.1";
        [JsonPropertyName("admin_map_port")]
        public int AdminMapPort { get; set; } = 9110;
        [JsonPropertyName("admin_map_token")]
        public string AdminMapToken { get; set; } = "";
        [JsonPropertyName("admin_map_show_admins")]
        public bool AdminMapShowAdmins { get; set; } = false;
        [JsonPropertyName("admin_map_refresh_seconds")]
        public int AdminMapRefreshSeconds { get; set; } = 5;
        [JsonPropertyName("admin_map_image_path")]
        public string AdminMapImagePath { get; set; } = "Data/AdminMap/dereth-map.png";
        [JsonPropertyName("admin_map_icon_path")]
        public string AdminMapIconPath { get; set; } = "Data/AdminMap/icons";
        [JsonPropertyName("admin_map_bounds_left_pct")]
        public float AdminMapBoundsLeftPct { get; set; } = 0.45f;
        [JsonPropertyName("admin_map_bounds_top_pct")]
        public float AdminMapBoundsTopPct { get; set; } = 0.45f;
        [JsonPropertyName("admin_map_bounds_right_pct")]
        public float AdminMapBoundsRightPct { get; set; } = 99.55f;
        [JsonPropertyName("admin_map_bounds_bottom_pct")]
        public float AdminMapBoundsBottomPct { get; set; } = 99.55f;

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
        [JsonPropertyName("mob_enchanter_enabled")]
        public bool EnchanterMobEnabled { get; set; } = true;
        [JsonPropertyName("mob_shaman_enabled")]
        public bool ShamanMobEnabled { get; set; } = true;
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
        [JsonPropertyName("life_caster_enabled")]
        public bool LifeCasterEnabled { get; set; } = true;
        [JsonPropertyName("hierophant_enabled")]
        public bool HierophantEnabled { get; set; } = true;
        [JsonPropertyName("caster_shadow_clone_enabled")]
        public bool CasterShadowCloneEnabled { get; set; } = true;
        [JsonPropertyName("gravecaller_caster_enabled")]
        public bool GravecallerCasterEnabled { get; set; } = true;
        [JsonPropertyName("void_confusion_caster_enabled")]
        public bool VoidConfusionCasterEnabled { get; set; } = true;
        [JsonPropertyName("war_caster_special_enabled")]
        public bool WarCasterSpecialEnabled { get; set; } = true;
        [JsonPropertyName("thief_dagger_enabled")]
        public bool ThievesDaggerEnabled { get; set; } = true;
        [JsonPropertyName("sentinel_spear_enabled")]
        public bool SentinelSpearEnabled { get; set; } = true;
        [JsonPropertyName("unarmed_elem_enabled")]
        public bool UnarmedElemEnabled { get; set; } = true;
        [JsonPropertyName("pugilist_weapon_enabled")]
        public bool PugilistWeaponEnabled { get; set; } = true;
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
        [JsonPropertyName("lugian_hammer_throw_enabled")]
        public bool LugianHammerThrowEnabled { get; set; } = true;
        [JsonPropertyName("opportunist_weapon_enabled")]
        public bool OpportunistWeaponEnabled { get; set; } = true;
        [JsonPropertyName("executioner_weapon_enabled")]
        public bool ExecutionerWeaponEnabled { get; set; } = true;

        [JsonPropertyName("rally_banner_enabled")]
        public bool RallyBannerEnabled { get; set; } = true;
        [JsonPropertyName("rally_banner_item_wcid")]
        public uint RallyBannerItemWcid { get; set; } = 7000020;
        [JsonPropertyName("rally_banner_visual_wcid")]
        public uint RallyBannerVisualWcid { get; set; } = 7000021;
        [JsonPropertyName("rally_banner_duration_seconds")]
        public float RallyBannerDurationSeconds { get; set; } = 180.0f;
        [JsonPropertyName("rally_banner_pulse_seconds")]
        public float RallyBannerPulseSeconds { get; set; } = 8.0f;
        [JsonPropertyName("rally_banner_aura_seconds")]
        public float RallyBannerAuraSeconds { get; set; } = 12.0f;
        [JsonPropertyName("rally_banner_cooldown_seconds")]
        public float RallyBannerCooldownSeconds { get; set; } = 900.0f;
        [JsonPropertyName("rally_banner_radius")]
        public float RallyBannerRadius { get; set; } = 20.0f;
        [JsonPropertyName("rally_banner_damage_rating_bonus")]
        public int RallyBannerDamageRatingBonus { get; set; } = 5;
        [JsonPropertyName("rally_banner_damage_resist_rating_bonus")]
        public int RallyBannerDamageResistRatingBonus { get; set; } = 5;
        [JsonPropertyName("rally_banner_regen_multiplier")]
        public float RallyBannerRegenMultiplier { get; set; } = 5.0f;
        [JsonPropertyName("rally_banner_requires_leadership")]
        public bool RallyBannerRequiresLeadership { get; set; } = true;
        [JsonPropertyName("rally_banner_required_level")]
        public int RallyBannerRequiredLevel { get; set; } = 180;

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
        [JsonPropertyName("warrior_princess_call_drop_chance")]
        public float WarriorPrincessCallDropChance { get; set; } = 0.01f;
        [JsonPropertyName("warrior_princess_call_proc_min")]
        public float WarriorPrincessCallProcMin { get; set; } = 0.05f;
        [JsonPropertyName("warrior_princess_call_proc_max")]
        public float WarriorPrincessCallProcMax { get; set; } = 0.08f;
        [JsonPropertyName("flying_buffet_drop_chance")]
        public float FlyingBuffetDropChance { get; set; } = 0.01f;
        [JsonPropertyName("flying_buffet_proc_min")]
        public float FlyingBuffetProcMin { get; set; } = 0.03f;
        [JsonPropertyName("flying_buffet_proc_max")]
        public float FlyingBuffetProcMax { get; set; } = 0.05f;
        [JsonPropertyName("flying_buffet_first_bounce_damage_scale")]
        public float FlyingBuffetFirstBounceDamageScale { get; set; } = 0.60f;
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
        [JsonPropertyName("quickening_dagger_cooldown_seconds")]
        public float QuickeningDaggerCooldownSeconds { get; set; } = 10.0f;
        [JsonPropertyName("opportunist_melee_drop_chance")]
        public float OpportunistMeleeDropChance { get; set; } = 0.01f;
        [JsonPropertyName("opportunist_missile_drop_chance")]
        public float OpportunistMissileDropChance { get; set; } = 0.008f;
        [JsonPropertyName("opportunist_min_tier")]
        public int OpportunistMinTier { get; set; } = 6;
        [JsonPropertyName("opportunist_damage_bonus")]
        public float OpportunistDamageBonus { get; set; } = 0.25f;
        [JsonPropertyName("opportunist_window_seconds")]
        public float OpportunistWindowSeconds { get; set; } = 8.0f;
        [JsonPropertyName("executioner_melee_drop_chance")]
        public float ExecutionerMeleeDropChance { get; set; } = 0.0075f;
        [JsonPropertyName("executioner_missile_drop_chance")]
        public float ExecutionerMissileDropChance { get; set; } = 0.006f;
        [JsonPropertyName("executioner_min_tier")]
        public int ExecutionerMinTier { get; set; } = 7;
        [JsonPropertyName("executioner_damage_bonus")]
        public float ExecutionerDamageBonus { get; set; } = 0.20f;
        [JsonPropertyName("executioner_health_threshold")]
        public float ExecutionerHealthThreshold { get; set; } = 0.25f;


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
        [JsonPropertyName("stranger_min_account_age_days")]
        public int StrangerMinAccountAgeDays { get; set; } = 5;
        [JsonPropertyName("stranger_deal_cooldown_seconds")]
        public int StrangerDealCooldownSeconds { get; set; } = 86400;
        [JsonPropertyName("stranger_junk_prank_chance")]
        public double StrangerJunkPrankChance { get; set; } = 1.0;

        // Mob Modifiers
        [JsonPropertyName("mob_modifier_enabled")]
        public bool MobModifierEnabled { get; set; } = true;
        [JsonPropertyName("mob_modifier_min_level")]
        public int MobModifierMinLevel { get; set; } = 101;
        [JsonPropertyName("mob_modifier_min_tier")]
        public int MobModifierMinTier { get; set; } = 2;
        [JsonPropertyName("mob_modifier_defense_skill_cap")]
        public int MobModifierDefenseSkillCap { get; set; } = 600;
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
        [JsonPropertyName("mob_enchanter_chance")]
        public float EnchanterMobChance { get; set; } = 0.00035f;
        [JsonPropertyName("mob_shaman_chance")]
        public float ShamanMobChance { get; set; } = 0.00035f;
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
        [JsonPropertyName("mutated_mob_weapon_drop_base_chance")]
        public float MutatedMobWeaponDropBaseChance { get; set; } = 0.05f;
        [JsonPropertyName("mutated_mob_weapon_drop_tier_bonus")]
        public float MutatedMobWeaponDropTierBonus { get; set; } = 0.02f;
        [JsonPropertyName("mutated_mob_weapon_drop_mutator_bonus")]
        public float MutatedMobWeaponDropMutatorBonus { get; set; } = 0.08f;
        [JsonPropertyName("mutated_mob_weapon_drop_min_chance")]
        public float MutatedMobWeaponDropMinChance { get; set; } = 0.10f;
        [JsonPropertyName("mutated_mob_weapon_drop_max_chance")]
        public float MutatedMobWeaponDropMaxChance { get; set; } = 0.65f;
        [JsonPropertyName("mutated_mob_weapon_drop_attempts")]
        public int MutatedMobWeaponDropAttempts { get; set; } = 3;

        // Loot Modifier Balance Controls
        [JsonPropertyName("loot_modifier_global_drop_multiplier")]
        public float LootModifierGlobalDropMultiplier { get; set; } = 1.0f;
        [JsonPropertyName("loot_modifier_exclusive_per_item")]
        public bool LootModifierExclusivePerItem { get; set; } = true;
        [JsonPropertyName("loot_modifier_interchangeable")]
        public bool LootModifierInterchangeable { get; set; } = true;
        [JsonPropertyName("loot_modifier_interchangeable_min_tier")]
        public int LootModifierInterchangeableMinTier { get; set; } = 6;
        [JsonPropertyName("lugian_hammer_throw_drop_chance")]
        public float LugianHammerThrowDropChance { get; set; } = 0.04f;
        [JsonPropertyName("lugian_hammer_throw_min_tier")]
        public int LugianHammerThrowMinTier { get; set; } = 6;
        [JsonPropertyName("lugian_hammer_throw_proc_chance")]
        public float LugianHammerThrowProcChance { get; set; } = 0.08f;
        [JsonPropertyName("lugian_hammer_throw_damage_scale")]
        public float LugianHammerThrowDamageScale { get; set; } = 0.75f;
        [JsonPropertyName("lugian_hammer_throw_radius")]
        public float LugianHammerThrowRadius { get; set; } = 10.0f;
        [JsonPropertyName("lugian_hammer_throw_cooldown_seconds")]
        public float LugianHammerThrowCooldownSeconds { get; set; } = 4.0f;

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
        [JsonPropertyName("shield_thorns_roll_chance")]
        public float ShieldThornsRollChance { get; set; } = 0.10f;
        [JsonPropertyName("shield_bashing_roll_chance")]
        public float ShieldBashingRollChance { get; set; } = 0.10f;
        [JsonPropertyName("shield_reflection_roll_chance")]
        public float ShieldReflectionRollChance { get; set; } = 0.06f;
        [JsonPropertyName("shield_spell_mirror_roll_chance")]
        public float ShieldSpellMirrorRollChance { get; set; } = 0.04f;
        [JsonPropertyName("shield_tier8_triple_affix_chance")]
        public float ShieldTier8TripleAffixChance { get; set; } = 0.15f;
        [JsonPropertyName("shield_bashing_proc_chance")]
        public float ShieldBashingProcChance { get; set; } = 0.10f;
        [JsonPropertyName("shield_bashing_health_pct")]
        public float ShieldBashingHealthPct { get; set; } = 0.10f;
        [JsonPropertyName("shield_bashing_cooldown_seconds")]
        public float ShieldBashingCooldownSeconds { get; set; } = 8.0f;
        [JsonPropertyName("shield_bash_knockback_distance")]
        public float ShieldBashKnockbackDistance { get; set; } = 10.0f;
        [JsonPropertyName("shield_spell_mirror_cooldown_seconds")]
        public float ShieldSpellMirrorCooldownSeconds { get; set; } = 10.0f;
        [JsonPropertyName("battlemage_helm_min_tier")]
        public int BattlemageHelmMinTier { get; set; } = 5;
        [JsonPropertyName("battlemage_helm_chance_t5")]
        public float BattlemageHelmChanceT5 { get; set; } = 0.04f;
        [JsonPropertyName("battlemage_helm_chance_t7")]
        public float BattlemageHelmChanceT7 { get; set; } = 0.06f;
        [JsonPropertyName("battlemage_helm_chance_t8")]
        public float BattlemageHelmChanceT8 { get; set; } = 0.08f;
        [JsonPropertyName("armor_sort_min_tier")]
        public int ArmorSortMinTier { get; set; } = 4;
        [JsonPropertyName("armor_sort_chance_t4")]
        public float ArmorSortChanceT4 { get; set; } = 0.04f;
        [JsonPropertyName("armor_sort_chance_t6")]
        public float ArmorSortChanceT6 { get; set; } = 0.06f;
        [JsonPropertyName("armor_sort_chance_t7")]
        public float ArmorSortChanceT7 { get; set; } = 0.08f;
        [JsonPropertyName("armor_sort_chance_t8")]
        public float ArmorSortChanceT8 { get; set; } = 0.10f;
        [JsonPropertyName("culinarian_min_tier")]
        public int CulinarianMinTier { get; set; } = 4;
        [JsonPropertyName("culinarian_roll_chance")]
        public float CulinarianRollChance { get; set; } = 0.08f;
        [JsonPropertyName("alchemist_glove_min_tier")]
        public int AlchemistGloveMinTier { get; set; } = 4;
        [JsonPropertyName("alchemist_glove_roll_chance")]
        public float AlchemistGloveRollChance { get; set; } = 0.08f;
        [JsonPropertyName("culinarian_tier8_superior_bonus_chance")]
        public float CulinarianTier8SuperiorBonusChance { get; set; } = 0.10f;
        [JsonPropertyName("alchemical_instability_chance_t6")]
        public float AlchemicalInstabilityChanceT6 { get; set; } = 0.15f;
        [JsonPropertyName("alchemical_instability_chance_t8")]
        public float AlchemicalInstabilityChanceT8 { get; set; } = 0.25f;
        [JsonPropertyName("dance_boot_min_tier")]
        public int DanceBootMinTier { get; set; } = 4;
        [JsonPropertyName("dance_boot_roll_chance")]
        public float DanceBootRollChance { get; set; } = 0.06f;
        [JsonPropertyName("unarmed_armor_min_tier")]
        public int UnarmedArmorMinTier { get; set; } = 5;
        [JsonPropertyName("unarmed_armor_roll_chance")]
        public float UnarmedArmorRollChance { get; set; } = 0.15f;
        [JsonPropertyName("unarmed_armor_off_axis_defense_chance")]
        public float UnarmedArmorOffAxisDefenseChance { get; set; } = 0.10f;

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


        // Life Caster
        [JsonPropertyName("life_caster_drop_chance")]
        public float LifeCasterDropChance { get; set; } = 0.03f;
        [JsonPropertyName("life_caster_min_tier")]
        public int LifeCasterMinTier { get; set; } = 4;
        // Hierophant
        [JsonPropertyName("hierophant_drop_chance")]
        public float HierophantDropChance { get; set; } = 0.04f;
        [JsonPropertyName("hierophant_min_tier")]
        public int HierophantMinTier { get; set; } = 5;
        [JsonPropertyName("hierophant_heal_boost_min")]
        public float HierophantHealBoostMin { get; set; } = 0.01f;
        [JsonPropertyName("hierophant_heal_boost_max")]
        public float HierophantHealBoostMax { get; set; } = 0.10f;
        [JsonPropertyName("hierophant_hot_proc_chance")]
        public float HierophantHotProcChance { get; set; } = 0.15f;
        [JsonPropertyName("hierophant_hot_pct_min")]
        public float HierophantHotPctMin { get; set; } = 0.01f;
        [JsonPropertyName("hierophant_hot_pct_max")]
        public float HierophantHotPctMax { get; set; } = 0.15f;
        [JsonPropertyName("hierophant_hot_duration_seconds")]
        public float HierophantHotDurationSeconds { get; set; } = 12.0f;
        [JsonPropertyName("hierophant_hot_tick_interval")]
        public float HierophantHotTickInterval { get; set; } = 3.0f;
        [JsonPropertyName("hierophant_fellow_echo_pct")]
        public float HierophantFellowEchoPct { get; set; } = 0.12f;
        [JsonPropertyName("hierophant_fellow_echo_range")]
        public float HierophantFellowEchoRange { get; set; } = 30.0f;
        [JsonPropertyName("hierophant_aggro_bonus")]
        public float HierophantAggroBonus { get; set; } = 0.35f;
        [JsonPropertyName("hierophant_cooldown_seconds")]
        public float HierophantCooldownSeconds { get; set; } = 10.0f;
        // Caster Mutators
        [JsonPropertyName("caster_shadow_clone_drop_chance")]
        public float CasterShadowCloneDropChance { get; set; } = 0.03f;
        [JsonPropertyName("caster_shadow_clone_min_tier")]
        public int CasterShadowCloneMinTier { get; set; } = 6;
        [JsonPropertyName("caster_shadow_clone_proc_chance")]
        public float CasterShadowCloneProcChance { get; set; } = 0.04f;
        [JsonPropertyName("caster_shadow_clone_cooldown_seconds")]
        public float CasterShadowCloneCooldownSeconds { get; set; } = 120.0f;
        [JsonPropertyName("caster_shadow_clone_duration_seconds")]
        public float CasterShadowCloneDurationSeconds { get; set; } = 25.0f;
        [JsonPropertyName("caster_shadow_clone_damage_scale")]
        public float CasterShadowCloneDamageScale { get; set; } = 0.35f;
        [JsonPropertyName("gravecaller_drop_chance")]
        public float GravecallerDropChance { get; set; } = 0.02f;
        [JsonPropertyName("gravecaller_min_tier")]
        public int GravecallerMinTier { get; set; } = 6;
        [JsonPropertyName("gravecaller_cooldown_seconds")]
        public float GravecallerCooldownSeconds { get; set; } = 45.0f;
        [JsonPropertyName("gravecaller_duration_seconds")]
        public float GravecallerDurationSeconds { get; set; } = 20.0f;
        [JsonPropertyName("void_confusion_drop_chance")]
        public float VoidConfusionDropChance { get; set; } = 0.025f;
        [JsonPropertyName("void_confusion_min_tier")]
        public int VoidConfusionMinTier { get; set; } = 6;
        [JsonPropertyName("void_confusion_cooldown_seconds")]
        public float VoidConfusionCooldownSeconds { get; set; } = 45.0f;
        [JsonPropertyName("void_confusion_target_min")]
        public int VoidConfusionTargetMin { get; set; } = 1;
        [JsonPropertyName("void_confusion_target_max")]
        public int VoidConfusionTargetMax { get; set; } = 4;
        [JsonPropertyName("void_confusion_duration_min")]
        public int VoidConfusionDurationMin { get; set; } = 1;
        [JsonPropertyName("void_confusion_duration_max")]
        public int VoidConfusionDurationMax { get; set; } = 10;
        [JsonPropertyName("war_caster_special_drop_chance")]
        public float WarCasterSpecialDropChance { get; set; } = 0.025f;
        [JsonPropertyName("war_caster_special_min_tier")]
        public int WarCasterSpecialMinTier { get; set; } = 6;

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
        [JsonPropertyName("thief_dagger_seam_penalty")]
        public uint ThievesDaggerSeamPenalty { get; set; } = 5;
        [JsonPropertyName("thief_dagger_seam_duration")]
        public int ThievesDaggerSeamDuration { get; set; } = 4;

        // Sentinel Spear
        [JsonPropertyName("sentinel_spear_drop_chance")]
        public float SentinelSpearDropChance { get; set; } = 0.02f;
        [JsonPropertyName("sentinel_spear_min_tier")]
        public int SentinelSpearMinTier { get; set; } = 5;
        [JsonPropertyName("sentinel_spear_proc_chance")]
        public float SentinelSpearProcChance { get; set; } = 0.08f;
        [JsonPropertyName("sentinel_spear_power_threshold")]
        public float SentinelSpearPowerThreshold { get; set; } = 0.70f;
        [JsonPropertyName("sentinel_spear_max_stacks")]
        public int SentinelSpearMaxStacks { get; set; } = 3;
        [JsonPropertyName("sentinel_spear_drain_pct")]
        public float SentinelSpearDrainPct { get; set; } = 0.10f;
        [JsonPropertyName("sentinel_spear_return_mult")]
        public float SentinelSpearReturnMult { get; set; } = 0.25f;
        [JsonPropertyName("sentinel_spear_cooldown_seconds")]
        public int SentinelSpearCooldownSeconds { get; set; } = 12;
        [JsonPropertyName("sentinel_spear_poise_duration_seconds")]
        public int SentinelSpearPoiseDurationSeconds { get; set; } = 5;
        [JsonPropertyName("sentinel_spear_poise_damage_reduction")]
        public float SentinelSpearPoiseDamageReduction { get; set; } = 0.05f;
        [JsonPropertyName("sentinel_spear_aggro_bonus")]
        public float SentinelSpearAggroBonus { get; set; } = 0.25f;

        // Elemental Unarmed
        [JsonPropertyName("unarmed_elem_drop_chance")]
        public float UnarmedElemDropChance { get; set; } = 0.05f;
        [JsonPropertyName("unarmed_elem_proc_min")]
        public int UnarmedElemProcMin { get; set; } = 1;
        [JsonPropertyName("unarmed_elem_proc_max")]
        public int UnarmedElemProcMax { get; set; } = 5;

        // Pugilist Unarmed Weapons
        [JsonPropertyName("pugilist_weapon_drop_chance")]
        public float PugilistWeaponDropChance { get; set; } = 0.05f;
        [JsonPropertyName("pugilist_weapon_min_tier")]
        public int PugilistWeaponMinTier { get; set; } = 5;
        [JsonPropertyName("pugilist_proc_min")]
        public int PugilistProcMin { get; set; } = 6;
        [JsonPropertyName("pugilist_proc_max")]
        public int PugilistProcMax { get; set; } = 10;
        [JsonPropertyName("pugilist_flurry_damage_scale")]
        public float PugilistFlurryDamageScale { get; set; } = 0.35f;
        [JsonPropertyName("pugilist_rake_damage_scale")]
        public float PugilistRakeDamageScale { get; set; } = 0.45f;
        [JsonPropertyName("pugilist_rake_duration_seconds")]
        public float PugilistRakeDurationSeconds { get; set; } = 6.0f;
        [JsonPropertyName("pugilist_cooldown_seconds")]
        public float PugilistCooldownSeconds { get; set; } = 6.0f;

        // Fencer Blade
        [JsonPropertyName("fencer_blade_drop_chance")]
        public float FencerBladeDropChance { get; set; } = 0.02f;
        [JsonPropertyName("fencer_blade_min_tier")]
        public int FencerBladeMinTier { get; set; } = 5;
        [JsonPropertyName("fencer_pierce_min")]
        public int FencerPierceMin { get; set; } = 12;
        [JsonPropertyName("fencer_pierce_max")]
        public int FencerPierceMax { get; set; } = 20;
        [JsonPropertyName("fencer_pierce_proc_min")]
        public int FencerPierceProcMin { get; set; } = 8;
        [JsonPropertyName("fencer_pierce_proc_max")]
        public int FencerPierceProcMax { get; set; } = 12;
        [JsonPropertyName("fencer_deflect_min")]
        public int FencerDeflectMin { get; set; } = 4;
        [JsonPropertyName("fencer_deflect_max")]
        public int FencerDeflectMax { get; set; } = 8;

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
        [JsonPropertyName("resolute_kill_cooldown_seconds")]
        public float ResoluteKillCooldownSeconds { get; set; } = 10.0f;

        // Polebreaker Staff
        [JsonPropertyName("polebreaker_drop_chance")]
        public float PolebreakerDropChance { get; set; } = 0.02f;
        [JsonPropertyName("polebreaker_min_tier")]
        public int PolebreakerMinTier { get; set; } = 5;
        [JsonPropertyName("polebreaker_stack_min")]
        public int PolebreakerStackMin { get; set; } = 3;
        [JsonPropertyName("polebreaker_stack_max")]
        public int PolebreakerStackMax { get; set; } = 4;
        [JsonPropertyName("polebreaker_max_stack_min")]
        public int PolebreakerMaxStackMin { get; set; } = 4;
        [JsonPropertyName("polebreaker_max_stack_max")]
        public int PolebreakerMaxStackMax { get; set; } = 6;
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

        // Shadow Clone Weapon Affixes
        [JsonPropertyName("shadow_volley_drop_chance")]
        public float ShadowVolleyDropChance { get; set; } = 0.015f;
        [JsonPropertyName("second_shadow_drop_chance")]
        public float SecondShadowDropChance { get; set; } = 0.0125f;
        [JsonPropertyName("shadow_weapon_min_tier")]
        public int ShadowWeaponMinTier { get; set; } = 7;
        [JsonPropertyName("shadow_weapon_proc_chance")]
        public float ShadowWeaponProcChance { get; set; } = 0.03f;
        [JsonPropertyName("shadow_weapon_cooldown_seconds")]
        public float ShadowWeaponCooldownSeconds { get; set; } = 150.0f;
        [JsonPropertyName("shadow_volley_duration_seconds")]
        public float ShadowVolleyDurationSeconds { get; set; } = 18.0f;
        [JsonPropertyName("second_shadow_duration_seconds")]
        public float SecondShadowDurationSeconds { get; set; } = 16.0f;
        [JsonPropertyName("shadow_weapon_damage_scale")]
        public float ShadowWeaponDamageScale { get; set; } = 0.25f;

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
        public float IronmanHardcoreSecondsBetweenDeaths { get; set; } = 5.0f;
        [JsonPropertyName("ironman_xp_scalar")]
        public float IronmanXpScalar { get; set; } = 0.75f;
        [JsonPropertyName("nomad_xp_scalar")]
        public float NomadXpScalar { get; set; } = 0.75f;
        [JsonPropertyName("hardcore_xp_scalar")]
        public float HardcoreXpScalar { get; set; } = 1.0f;
        [JsonPropertyName("hardcore_rogue_enabled")]
        public bool HardcoreRogueEnabled { get; set; } = true;
        [JsonPropertyName("hardcore_rogue_max_opt_in_level")]
        public int HardcoreRogueMaxOptInLevel { get; set; } = 10;
        [JsonPropertyName("hardcore_rogue_boon_choices")]
        public int HardcoreRogueBoonChoices { get; set; } = 3;
        [JsonPropertyName("hardcore_rogue_proficiency_minutes")]
        public double HardcoreRogueProficiencyMinutes { get; set; } = 2.0;
        [JsonPropertyName("hardcore_rogue_proficiency_xp_multiplier")]
        public float HardcoreRogueProficiencyXpMultiplier { get; set; } = 2.0f;

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

