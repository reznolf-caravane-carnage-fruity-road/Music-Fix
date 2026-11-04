using System;
using System.Collections;
using UnityEngine;

namespace CCFR.MusicFix
{
    internal static class Diag
    {
        internal static bool Enabled;
        internal static bool RecoveryDisabled;
        internal static bool ForcePlayDone;

        internal static string Describe(MusicController mc)
        {
            if (mc == null)
            {
                return "mc=null";
            }

            AudioSource src = mc.musicAudioSource_1;
            string clip = src != null && src.clip != null ? src.clip.name : "<null>";
            string srcState = src == null
                ? "src=null"
                : $"src(go='{src.gameObject.name}' active={src.gameObject.activeInHierarchy} enabled={src.enabled} "
                  + $"clip='{clip}' playing={src.isPlaying} time={src.time:0.000} vol={src.volume:0.000} loop={src.loop} mute={src.mute})";

            return $"song={mc.whichSongDecidedToPlay} playingFlag={mc.currentlySongIsPlaying} fadingOut={mc.fadingOutMusic} "
                 + $"maxVol={mc.current_max_volume:0.000} introChecker={(mc.musicIntroCheckerRef != null ? mc.musicIntroCheckerRef.enabled.ToString() : "null")} "
                 + srcState;
        }

        internal static string Global()
        {
            return $"focus={Application.isFocused} runInBg={Application.runInBackground} "
                 + $"listenerPause={AudioListener.pause} listenerVol={AudioListener.volume:0.00} "
                 + $"dt={Time.deltaTime:0.0000} unscaledDt={Time.unscaledDeltaTime:0.0000} "
                 + $"dsp={AudioSettings.dspTime:0.000}";
        }

        internal static float EnvFloat(string name, float fallback)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return float.TryParse(v, out float f) ? f : fallback;
        }

        internal static bool EnvBool(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Developer-only self test. Everything is inert unless one of the
    /// CCFR_MUSICDIAG_* environment variables is set.
    /// </summary>
    internal class Diagnostics : MonoBehaviour
    {
        private float started;

        private void Start()
        {
            started = Time.realtimeSinceStartup;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float seconds = Diag.EnvFloat("CCFR_MUSICDIAG_SECONDS", 25f);
            float forceAt = Diag.EnvFloat("CCFR_MUSICDIAG_FORCE_PLAY_AT", -1f);
            float resetAt = Diag.EnvFloat("CCFR_MUSICDIAG_RESET_AUDIO_AT", -1f);
            float fadeOutAt = Diag.EnvFloat("CCFR_MUSICDIAG_FADE_OUT_AT", -1f);
            float seekEndAt = Diag.EnvFloat("CCFR_MUSICDIAG_SEEK_END_AT", -1f);
            float shopTestAt = Diag.EnvFloat("CCFR_MUSICDIAG_SHOP_TEST_AT", -1f);
            bool resetDone = false;
            bool fadeOutDone = false;
            bool seekEndDone = false;
            bool shopTestDone = false;
            bool quit = Diag.EnvBool("CCFR_MUSICDIAG_QUIT");

            Plugin.Log.LogInfo($"[diag] started: seconds={seconds} forceAt={forceAt} resetAt={resetAt} "
                + $"fadeOutAt={fadeOutAt} seekEndAt={seekEndAt} shopTestAt={shopTestAt} quit={quit}");

            while (Time.realtimeSinceStartup - started < seconds)
            {
                float elapsed = Time.realtimeSinceStartup - started;
                var game = CCFR.MusicFix.PluginHolder.Cc;
                var controller = game != null ? game.musicControllerRef : null;
                if (controller != null)
                {
                    Plugin.Log.LogInfo($"[diag t={elapsed:0.0}] {Diag.Describe(controller)} | {Diag.Global()}");
                }
                else
                {
                    Plugin.Log.LogInfo($"[diag t={elapsed:0.0}] glov/musicController not ready | {Diag.Global()}");
                }

                if (!Diag.ForcePlayDone && forceAt >= 0f && elapsed >= forceAt)
                {
                    Diag.ForcePlayDone = true;
                    if (controller != null)
                    {
                        Plugin.Log.LogInfo($"[diag] forcing Nachat_New_Run at t={elapsed:0.0}");
                        try
                        {
                            controller.Nachat_New_Run(-1, false);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[diag] force failed: {ex}");
                        }
                    }
                }

                if (!fadeOutDone && fadeOutAt >= 0f && elapsed >= fadeOutAt)
                {
                    fadeOutDone = true;
                    if (controller != null)
                    {
                        Plugin.Log.LogInfo($"[diag] calling FadeOutMusicSong at t={elapsed:0.0}");
                        try
                        {
                            controller.FadeOutMusicSong();

                            // The real caller (Check_If_Saw_GameoverScreen_Many_Times)
                            // sets this private flag right after starting the fade.
                            var flag = typeof(MusicController).GetField(
                                "fadedOutMusicCuzSawGameoverManyTimes",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            flag?.SetValue(controller, true);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[diag] fade-out failed: {ex}");
                        }
                    }
                }

                if (!seekEndDone && seekEndAt >= 0f && elapsed >= seekEndAt)
                {
                    seekEndDone = true;
                    var source = controller != null ? controller.musicAudioSource_1 : null;
                    if (source != null && source.clip != null && source.clip.name.IndexOf("_loop", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Plugin.Log.LogInfo($"[diag] seeking near end of '{source.clip.name}' at t={elapsed:0.0}: "
                            + $"length={source.clip.length:0.000} loop={source.loop} time={source.time:0.000}");
                        source.time = Mathf.Max(0f, source.clip.length - 0.8f);
                    }
                }

                if (!shopTestDone && shopTestAt >= 0f && elapsed >= shopTestAt)
                {
                    shopTestDone = true;
                    if (controller != null && game != null && game.knopRef != null)
                    {
                        var source = controller.musicAudioSource_1;
                        if (source != null)
                        {
                            source.volume = 0.03f;
                        }

                        controller.current_max_volume = 0.05f;
                        Plugin.Log.LogInfo($"[diag] simulating shop close at t={elapsed:0.0} "
                            + $"(current_max_volume before={controller.current_max_volume:0.000})");
                        try
                        {
                            game.knopRef.Close_Shop();
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[diag] Close_Shop failed: {ex}");
                        }
                    }
                }

                if (!resetDone && resetAt >= 0f && elapsed >= resetAt)
                {
                    resetDone = true;
                    if (controller != null)
                    {
                        Plugin.Log.LogInfo($"[diag] resetting audio at t={elapsed:0.0}: {Diag.Describe(controller)}");
                        try
                        {
                            bool ok = AudioSettings.Reset(AudioSettings.GetConfiguration());
                            Plugin.Log.LogInfo($"[diag] AudioSettings.Reset returned {ok}");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[diag] audio reset failed: {ex}");
                        }
                    }
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            Plugin.Log.LogInfo("[diag] finished");
            if (quit)
            {
                Plugin.Log.LogInfo("[diag] quitting application");
                Application.Quit();
            }
        }
    }

    internal static class PluginHolder
    {
        internal static glov Cc => glov.cc;
    }
}
