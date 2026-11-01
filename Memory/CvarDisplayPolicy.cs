namespace Lab.Memory;

internal static class CvarDisplayPolicy
{
    // 5.1 uses the same flags for menu preferences and actual cheats. These
    // exact names are verified against SettingsDataSource_* method references,
    // video-quality helpers and the 35-entry player-profile table in that EXE.
    // See docs/validation/menu-cvar-references-5.1.json for method/table RVAs.
    // Exact membership deliberately preserves unrelated renderer/AI debug cvars.
    private static readonly HashSet<string> Preferences = new(StringComparer.OrdinalIgnoreCase) {
        "aa_OPTIONS_AdhesionScalar", "aa_OPTIONS_FrictionScalar", "aa_OPTIONS_TargetSnapAllowed", "com_showConsumerPerfMetrics",
        "demonBounty_Enabled", "g_fov", "g_gameDifficulty", "g_hints_disable",
        "g_hitConfirmSoundPlayer", "g_hitConfirmSoundSummon", "g_reticleMode", "g_setting_blood_punch_info",
        "g_setting_boss_health", "g_setting_combatScoring", "g_setting_compass", "g_setting_dash_info",
        "g_setting_equipment_info", "g_setting_gk_highlight", "g_setting_hands_bob", "g_setting_health_info",
        "g_setting_hud_ability_indicator_blood_punch", "g_setting_hud_ability_indicator_chainsaw", "g_setting_hud_ability_indicator_flame_belch", "g_setting_hud_ability_indicator_hammer",
        "g_setting_hud_ability_indicator_type", "g_setting_hud_extra_lives", "g_setting_hud_mission_challenges", "g_setting_hud_powerup",
        "g_setting_hud_preset", "g_setting_hud_show", "g_setting_hudNotifications", "g_setting_interact_prompt",
        "g_setting_keycard", "g_setting_notification_ammo_pickup", "g_setting_notification_health_pickup", "g_setting_notification_level_up",
        "g_setting_notification_major", "g_setting_notification_menu", "g_setting_notification_minor", "g_setting_objectiveMarkers",
        "g_setting_photomode", "g_setting_rune_info", "g_setting_sentinel_armor", "g_setting_speaker",
        "g_setting_subtitles", "g_setting_tutorials", "g_setting_weapon_info", "g_settings_dashStyle",
        "g_weaponAutoEquipNew", "g_weaponAutoSwitchOnEmpty", "g_weaponDoomClassicPose", "gs_cascadeLods",
        "hud_globalAlpha", "hud_subtitles_showBacking", "hud_subtitles_showSpeaker", "hud_subtitles_textSize",
        "in_joystick", "is_defragMinHeadroom", "is_lightmapDirMinMip", "is_lightmapMinMip",
        "is_poolSize", "is_uiMinMip", "joy_smoothingAcceleration", "menu_commandBar_holdTime",
        "pm_localPlayerPainSoundsEnabled", "prop_skipShadows", "r_antialiasing", "r_blurRadialQuality",
        "r_blurRadialScale", "r_brightness", "r_chromaticAberration", "r_colorBlindBrightness",
        "r_colorBlindContrast", "r_colorBlindCorrectionStrength", "r_colorBlindIntensity", "r_colorBlindMode",
        "r_contrast", "r_decalDistanceFadeMultiplier", "r_decalFadeCulling", "r_decalFilteringQuality",
        "r_decalLifetimeMultiplier", "r_dof", "r_dofTAA", "r_environmentProbes",
        "r_filmGrainRatio", "r_fullscreen", "r_gamma", "r_hdrBloom",
        "r_hdrDisplay", "r_hdrDisplayBrightness", "r_hdrDisplayMaxStops", "r_hdrDisplayMidPoint",
        "r_hdrDisplayMinStops", "r_hdrDisplaySaturation", "r_hdrShutterSpeed", "r_lightDistanceFadeMultiplier",
        "r_lightScattering", "r_lightScatteringQuality", "r_lodForce", "r_lodScale",
        "r_materialAniso", "r_materialAnisoCover", "r_mode", "r_motionBlurQuality",
        "r_multiSamples", "r_particleFadeQualityMultiplier", "r_particlesLightAtlasQuality", "r_pickupItemsSaturation",
        "r_presentFromAsync", "r_rainQuality", "r_renderMode", "r_shadowAtlasHeight",
        "r_shadowAtlasTileSize", "r_shadowAtlasWidth", "r_shadowCacheHalfPrecision", "r_shadowNumAccurateSunSlices",
        "r_shadowParallelMoveMargin", "r_shadowRestrictGeomCachesToSun", "r_shadowsDistanceFadeMultiplier", "r_sharpening",
        "r_skipPlayerShadow", "r_SSDO", "r_SSDOQuality", "r_SSR",
        "r_SSRQuality", "r_swapInterval", "r_waterCaustics",
        "r_waterGridResolution", "r_waterHitsSim", "r_waterInterleaveUpdates", "r_waterLodViewDistance",
        "r_waterPostProcess", "r_waterReflections", "r_windowPosX", "r_windowPosY",
        "rs_forceResolution", "s_panningRule", "s_soundpack_classic_doomguy_sounds", "s_volume",
        "s_volumeAux", "s_volumeAuxEnable", "s_volumeAuxSFX", "s_volumeAuxUI",
        "s_volumeAuxVO", "s_volumeMusic", "s_volumeSFX", "s_volumeVO",
        "settings_hdr_contrast", "swf_anchorProfile", "swf_colorProfile", "ui_settings_audio_hit_confirm",
        "ui_settings_controls_internal_aa_scale", "view_skipShakes",
    };

    internal static bool IsPreference(string name) => Preferences.Contains(name);

    // Renderer code updates this value automatically; it is not a user cheat.
    internal static bool IsAutomaticState(string name) => name.Equals("r_umbraAccurateOcclusionThreshold", StringComparison.OrdinalIgnoreCase);

}
