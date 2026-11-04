using System;
using System.Collections;
using UnityEngine;

namespace CCFR.MusicStartup
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

        internal static MusicRecovery Instance;

        private float nextWatchdog;
        private bool audioConfigSubscribed;

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
                var game = CCFR.MusicStartup.PluginHolder.Cc;
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

                // No song was ever selected (e.g. main menu with no run yet, or after game over).
                if (controller.whichSongDecidedToPlay == -1)
                {
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
    }
}
