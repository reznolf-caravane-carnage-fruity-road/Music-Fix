using System.Collections;
using UnityEngine;

namespace CCFR.MusicFix
{
    /// <summary>
    /// Starts one song when the main menu becomes usable and fades it in.
    /// This mirrors what the Music on/off button does, but without the volume
    /// jump: the game's own SetToMaxVolume ramp is driven from a ramped
    /// current_max_volume instead of its normal 60x target.
    /// </summary>
    internal class MenuMusic : MonoBehaviour
    {
        internal static MenuMusic Instance;

        internal static bool FadeInActive;
        internal static MusicController FadeController;

        private static bool bootAttempted;

        private AudioSource fadeSource;
        private Coroutine fadeRoutine;
        private float fadeTargetCurrentMax;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(BootRoutine());
        }

        private static bool ReadyToStart()
        {
            var game = CCFR.MusicFix.PluginHolder.Cc;
            if (game == null || !game.loaded_save)
            {
                return false;
            }

            if (game.musicControllerRef == null || game.knopRef == null || game.urovRef == null)
            {
                return false;
            }

            // The tutorial has its own music cue; never fight it.
            if (!game.hasFinishedBrutalTutorial)
            {
                return false;
            }

            if (game.urovRef.gameIsBeingPlayed)
            {
                return false;
            }

            if (game.knopRef.start_menu == null || !game.knopRef.start_menu.gameObject.activeInHierarchy)
            {
                return false;
            }

            return game.musicControllerRef.gameObject.activeInHierarchy;
        }

        private IEnumerator BootRoutine()
        {
            // Wait for the save, the main menu and the music controller to settle.
            while (!ReadyToStart())
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (bootAttempted)
            {
                yield break;
            }

            bootAttempted = true;

            var game = CCFR.MusicFix.PluginHolder.Cc;
            if (game == null)
            {
                yield break;
            }

            if (!Plugin.StartMusicInMenu.Value)
            {
                Plugin.Log.LogInfo("[menu] automatic menu music is disabled in the config");
                yield break;
            }

            if (game.musicIs_On_1_Off_0 != 1)
            {
                Plugin.Log.LogInfo("[menu] music is disabled in the game settings; not starting it");
                yield break;
            }

            var controller = game.musicControllerRef;
            if (controller == null)
            {
                yield break;
            }

            if (controller.whichSongDecidedToPlay != -1
                || (controller.musicAudioSource_1 != null && controller.musicAudioSource_1.isPlaying))
            {
                // The game already has music (tutorial, resumed run, ...).
                yield break;
            }

            StartWithFade(controller, "main-menu");
        }

        /// <summary>
        /// Start a song on a controller with no current song and fade it in.
        /// Also used by the recovery watcher after the game's game-over music fade.
        /// </summary>
        internal static bool StartWithFade(MusicController controller, string reason)
        {
            if (Instance == null || controller == null || FadeInActive)
            {
                return false;
            }

            if (!controller.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (controller.whichSongDecidedToPlay != -1)
            {
                return false;
            }

            if (controller.musicAudioSource_1 == null)
            {
                return false;
            }

            FadeInActive = true;
            FadeController = controller;
            Plugin.Log.LogInfo($"[menu] starting music with fade-in ({reason})");

            controller.Nachat_New_Run(-1, false);
            Instance.StartCoroutine(Instance.WaitForFadeStart(reason));
            return true;
        }

        private IEnumerator WaitForFadeStart(string reason)
        {
            // Start_MUSIC runs asynchronously after the song clips load. If that
            // never happens, release the fade flag again.
            float deadline = Time.realtimeSinceStartup + 12f;
            while (FadeInActive && fadeSource == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (fadeSource == null)
            {
                FadeInActive = false;
                FadeController = null;
                Plugin.Log.LogWarning($"[menu] music did not start in time ({reason}); fade-in cancelled");
            }
        }

        internal static bool IsFading(MusicController controller)
        {
            return FadeInActive && (FadeController == null || FadeController == controller);
        }

        /// <summary>
        /// Called right after MusicController.Start_MUSIC() has played the intro
        /// clip and set the target volume. Replace the instant ramp with a fade.
        /// </summary>
        internal static void OnMusicStarted(MusicController controller)
        {
            if (!FadeInActive || Instance == null || controller == null)
            {
                return;
            }

            if (FadeController != null && FadeController != controller)
            {
                return;
            }

            Instance.BeginFade(controller);
        }

        private void BeginFade(MusicController controller)
        {
            var source = controller.musicAudioSource_1;
            if (source == null || source.clip == null)
            {
                FadeInActive = false;
                FadeController = null;
                return;
            }

            fadeTargetCurrentMax = controller.current_max_volume;
            if (fadeTargetCurrentMax <= 0.0001f)
            {
                FadeInActive = false;
                FadeController = null;
                return;
            }

            fadeSource = source;
            source.volume = 0f;
            controller.current_max_volume = 0f;

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }

            fadeRoutine = StartCoroutine(FadeRoutine(controller, source, fadeTargetCurrentMax));
        }

        private IEnumerator FadeRoutine(MusicController controller, AudioSource source, float targetCurrentMax)
        {
            var game = CCFR.MusicFix.PluginHolder.Cc;
            float additional = game != null ? game.music_additional_volume_change_from_settings : 1f;
            float duration = Mathf.Max(0f, Plugin.MenuFadeInSeconds.Value);
            float finalVolume = targetCurrentMax * additional;

            Plugin.Log.LogInfo($"[menu] fade-in: {duration:0.0}s to volume {finalVolume:0.000}");

            if (duration > 0.01f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float smooth = t * t * (3f - 2f * t);

                    if (controller != null)
                    {
                        controller.current_max_volume = targetCurrentMax * smooth;
                    }

                    if (source != null)
                    {
                        source.volume = finalVolume * smooth;
                    }

                    yield return null;
                }
            }

            if (controller != null)
            {
                controller.current_max_volume = targetCurrentMax;
            }

            if (source != null)
            {
                source.volume = finalVolume;
            }

            Plugin.Log.LogInfo("[menu] fade-in complete");

            fadeSource = null;
            fadeRoutine = null;
            FadeInActive = false;
            FadeController = null;
        }
    }
}
