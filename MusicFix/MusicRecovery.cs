using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace CCFR.MusicFix
{
    /// <summary>
    /// Keeps the game's music alive across audio-device reconfigurations and focus changes.
    ///
    /// Caravan Carnage starts music with AudioSource.Play(), but nothing re-plays it when
    /// Unity reinitialises the audio device (which happens when the game is launched
    /// unfocused and gains focus later, or when the default output device changes).
    /// The AudioSource keeps its clip/volume but isPlaying turns false and its time
    /// resets to 0, while MusicController.whichSongDecidedToPlay and
    /// currentlySongIsPlaying stay set - so the game believes music is playing and never
    /// retries. The only code path that calls Play() again is toggling music off/on in
    /// the settings menu.
    /// </summary>
    internal class MusicRecovery : MonoBehaviour
    {
        private const float WatchdogInterval = 0.5f;
        private const float GameOverRecoveryCooldown = 8f;

        private static readonly FieldInfo FadedOutAfterGameOversField =
            typeof(MusicController).GetField("fadedOutMusicCuzSawGameoverManyTimes", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo WaitingForNewLevelField =
            typeof(MusicController).GetField("currentlyWaitingToPlay_NEW_LVL_MUSIC", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static MusicRecovery Instance;

        private float nextWatchdog;
        private bool audioConfigSubscribed;
        private float lastGameOverRecovery = -100f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Subscribe()
        {
            if (audioConfigSubscribed)
            {
                return;
            }

            try
            {
                AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
                audioConfigSubscribed = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[recovery] could not subscribe to audio configuration changes: {ex.Message}");
            }

            try
            {
                Application.focusChanged += OnFocusChanged;
            }
            catch (Exception)
            {
                // Older runtimes: OnApplicationFocus still covers this.
            }
        }

        private void Unsubscribe()
        {
            if (audioConfigSubscribed)
            {
                try
                {
                    AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
                }
                catch (Exception)
                {
                }

                audioConfigSubscribed = false;
            }

            try
            {
                Application.focusChanged -= OnFocusChanged;
            }
            catch (Exception)
            {
            }
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextWatchdog)
            {
                return;
            }

            nextWatchdog = now + WatchdogInterval;
            Check("watchdog");
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ScheduleRecovery("application-focus", 0.35f);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                ScheduleRecovery("application-resume", 0.35f);
            }
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            Plugin.Log.LogInfo($"[recovery] audio configuration changed (deviceWasChanged={deviceWasChanged})");
            ScheduleRecovery("audio-configuration-changed", 0.5f);
        }

        private void OnFocusChanged(bool focused)
        {
            if (focused)
            {
                ScheduleRecovery("focus-changed", 0.35f);
            }
        }

        internal void ScheduleRecovery(string reason, float delay)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(RecoverAfter(reason, delay));
        }

        internal static void Schedule(string reason, float delay)
        {
            Instance?.ScheduleRecovery(reason, delay);
        }

        private IEnumerator RecoverAfter(string reason, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Check(reason);

            // Some devices come back a moment later; verify once more.
            yield return new WaitForSecondsRealtime(0.75f);
            Check(reason + "+verify");
        }

        private static void Check(string reason)
        {
            try
            {
                if (Diag.RecoveryDisabled)
                {
                    return;
                }

                var game = CCFR.MusicFix.PluginHolder.Cc;
                if (game == null)
                {
                    return;
                }

                if (game.musicIs_On_1_Off_0 != 1)
                {
                    return;
                }

                var controller = game.musicControllerRef;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    return;
                }

                AudioSource source = controller.musicAudioSource_1;
                if (source == null || !source.gameObject.activeInHierarchy || !source.enabled)
                {
                    return;
                }

                // A fade-out is an intentional silence; it invalidates the song when done.
                if (controller.fadingOutMusic)
                {
                    return;
                }

                if (controller.whichSongDecidedToPlay == -1)
                {
                    // The game fades music out and clears the song after the player has
                    // seen the game-over screen several times. Since nothing starts a
                    // new song until the next run, the menu/shop stays silent forever.
                    // Start a fresh song with the menu fade once that fade has finished.
                    TryRecoverAfterGameOverFade(game, controller);
                    return;
                }

                if (source.clip == null)
                {
                    return;
                }

                if (source.isPlaying)
                {
                    return;
                }

                bool wasIntro = source.clip.name.IndexOf("_intro", StringComparison.OrdinalIgnoreCase) >= 0;

                source.Play();

                if (source.volume <= 0.001f && controller.current_max_volume > 0.001f)
                {
                    float volume = controller.current_max_volume * Mathf.Max(0.01f, game.music_additional_volume_change_from_settings);
                    source.volume = volume;
                }

                if (wasIntro && controller.musicIntroCheckerRef != null)
                {
                    controller.musicIntroCheckerRef.enabled = true;
                }

                controller.currentlySongIsPlaying = true;

                Plugin.Log.LogInfo($"[recovery] restarted music ({reason}); song={controller.whichSongDecidedToPlay} "
                    + $"clip='{source.clip.name}' volume={source.volume:0.000} loop={source.loop}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[recovery] music check failed ({reason}): {ex.Message}");
            }
        }

        private static bool ReadPrivateBool(FieldInfo field, object instance)
        {
            if (field == null || instance == null)
            {
                return false;
            }

            try
            {
                return (bool)field.GetValue(instance);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryRecoverAfterGameOverFade(glov game, MusicController controller)
        {
            if (!ReadPrivateBool(FadedOutAfterGameOversField, controller))
            {
                return;
            }

            // A level-unlock transition also clears the song temporarily and starts its
            // own music a moment later; don't race it.
            if (ReadPrivateBool(WaitingForNewLevelField, controller))
            {
                return;
            }

            if (controller.fadingOutMusic || controller.currentlySongIsPlaying)
            {
                return;
            }

            if (MenuMusic.FadeInActive || Instance == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup - Instance.lastGameOverRecovery < GameOverRecoveryCooldown)
            {
                return;
            }

            Instance.lastGameOverRecovery = Time.realtimeSinceStartup;
            Plugin.Log.LogInfo("[recovery] game-over music fade finished; starting a new song with fade-in");
            MenuMusic.StartWithFade(controller, "game-over-fade");
        }

        internal static void ScheduleShopRestore()
        {
            Instance?.ScheduleShopRestoreInstance();
        }

        private void ScheduleShopRestoreInstance()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(RestoreShopVolumeAfter(0.5f));
        }

        private IEnumerator RestoreShopVolumeAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            try
            {
                var game = CCFR.MusicFix.PluginHolder.Cc;
                if (game == null || game.musicIs_On_1_Off_0 != 1)
                {
                    yield break;
                }

                var controller = game.musicControllerRef;
                if (controller == null || controller.fadingOutMusic)
                {
                    yield break;
                }

                if (controller.whichSongDecidedToPlay == -1)
                {
                    yield break;
                }

                controller.Set_Music_To_Average_Volume();
                Plugin.Log.LogInfo("[recovery] shop closed - restoring music volume");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[recovery] shop volume restore failed: {ex.Message}");
            }
        }
    }
}
