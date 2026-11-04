using HarmonyLib;

namespace CCFR.MusicStartup
{
    internal static class Patches
    {
        /// <summary>
        /// Music is started here after the song clips finish loading. Verify the
        /// source really began playing; an audio device reconfiguration in the
        /// middle of this sequence would otherwise leave it silent forever.
        /// </summary>
        [HarmonyPatch(typeof(MusicController), "Start_MUSIC")]
        internal static class StartMusicPatch
        {
            private static void Postfix(MusicController __instance)
            {
                if (Diag.Enabled)
                {
                    Plugin.Log.LogInfo($"[patch Start_MUSIC] {Diag.Describe(__instance)} | {Diag.Global()}");
                }

                MenuMusic.OnMusicStarted(__instance);
                MusicRecovery.Schedule("music-started", 0.75f);
            }
        }

        /// <summary>
        /// Selecting a new song is asynchronous (Resources.LoadAsync). Re-check
        /// shortly after the call returns, once the coroutine has had a chance
        /// to assign the clip and call Play().
        /// </summary>
        [HarmonyPatch(typeof(MusicController), nameof(MusicController.Nachat_New_Run))]
        internal static class NewRunPatch
        {
            private static void Postfix(MusicController __instance)
            {
                if (Diag.Enabled)
                {
                    Plugin.Log.LogInfo($"[patch Nachat_New_Run] {Diag.Describe(__instance)} | {Diag.Global()}");
                }

                MusicRecovery.Schedule("new-run", 1.5f);
            }
        }

        /// <summary>
        /// The intro clip hands over to the looping clip here. Verify that the
        /// hand-over actually produced a playing source.
        /// </summary>
        [HarmonyPatch(typeof(MusicIntroChecker), "PlayMainMusic")]
        internal static class IntroToLoopPatch
        {
            private static void Postfix()
            {
                MusicRecovery.Schedule("intro-to-loop", 0.5f);
            }
        }
    }
}
