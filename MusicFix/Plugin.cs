using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CCFR.MusicFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reznolf.ccfr.musicfix";
        public const string PluginName = "Music Fix";
        public const string PluginVersion = "0.3.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> StartMusicInMenu;
        internal static ConfigEntry<float> MenuFadeInSeconds;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading");

            StartMusicInMenu = Config.Bind(
                "Menu Music",
                "StartMusicInMenu",
                true,
                "Start one song automatically when the main menu is ready.");

            MenuFadeInSeconds = Config.Bind(
                "Menu Music",
                "FadeInSeconds",
                2.5f,
                new ConfigDescription(
                    "How long the automatic menu song takes to fade in.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Diag.Enabled = Diag.EnvBool("CCFR_MUSICDIAG")
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_SECONDS") != null
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_FORCE_PLAY_AT") != null
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_RESET_AUDIO_AT") != null
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_FADE_OUT_AT") != null
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_SEEK_END_AT") != null
                || Environment.GetEnvironmentVariable("CCFR_MUSICDIAG_SHOP_TEST_AT") != null;

            Diag.RecoveryDisabled = Diag.EnvBool("CCFR_MUSICDIAG_NO_RECOVERY");

            // Keep the player ticking while unfocused. The game does this itself in
            // glov.LateStart, but doing it earlier closes the window where Unity can
            // pause/reset audio during a background launch.
            Application.runInBackground = true;

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
            Log.LogInfo("Patched: MusicController.Start_MUSIC, MusicController.Nachat_New_Run, MusicIntroChecker.PlayMainMusic, knop.Close_Shop");

            var host = new GameObject("CCFR_MusicFix");
            DontDestroyOnLoad(host);
            host.AddComponent<MusicRecovery>();
            host.AddComponent<MenuMusic>();

            if (Diag.Enabled)
            {
                Log.LogInfo("Developer diagnostics enabled");
                host.AddComponent<Diagnostics>();
            }
        }
    }
}
