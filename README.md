# CCFR Music Startup

A small BepInEx 5 plugin for **Caravan Carnage: Fruity Road** that fixes the
"no music after launch until you toggle Music off/on in Settings" bug.

- Plugin ID: `com.reznolf.ccfr.musicstartup`
- DLL: `CCFR.MusicStartup.dll`
- Target: .NET Standard 2.1 / BepInEx 5.4.23.6 / Unity 6000.3.8f1 (Mono)

## Requirements

- Caravan Carnage: Fruity Road (Steam app `3330940`)
- [BepInEx 5.4.23.6 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6)

## Download

Grab the latest release:
[Caravan-Carnage-Music-Startup releases](https://github.com/reznolf-caravane-carnage-fruity-road/Caravan-Carnage-Music-Startup/releases/latest)

The zip contains `BepInEx/plugins/CCFR.MusicStartup.dll`; extract it into the
game folder.

## Installation

1. Install BepInEx 5 x64 into the game folder and run the game once.
2. Copy `CCFR.MusicStartup.dll` into `<game>\BepInEx\plugins\`.
3. Start the game. Music fades in when the main menu appears.

---

## Symptom

After the game is launched (especially when it starts unfocused/in the
background), music is silent. Opening Settings and toggling **Music** off and
back on makes it start.

## Root cause (verified in-game)

The game's music is driven by `MusicController.musicAudioSource_1` (the
`audio_1` child of `MUSIC_CONTROLLER`). `MusicController.Start_MUSIC()` does:

```csharp
musicAudioSource_1.Play();
musicAudioSource_1.volume = 0f;
setVolumeMaxRoutine = StartCoroutine(SetToMaxVolume(musicAudioSource_1, 60f));
```

and never checks the source again.

When Unity reinitialises its audio device - which happens when a game that was
started unfocused gains focus, or when the default output device changes -
every `AudioSource` is stopped. The source keeps its clip, loop flag and volume,
but `isPlaying` becomes `false`, `time` resets to `0`, and it stays stopped.

`MusicController` cannot notice:

- `whichSongDecidedToPlay` is still the selected song.
- `currentlySongIsPlaying` is still `true`.
- `fadingOutMusic` is `false`.
- `musicAudioSource_1.clip` is still assigned and `volume` is still correct.

Nothing in the game handles `AudioSettings.OnAudioConfigurationChanged`, so no
code calls `Play()` again. The only path that does is `knop.Clicked_On_Off_Music`
→ `ResetMusic()` + `Nachat_New_Run()`, which is why the Settings toggle is the
only workaround.

### Reproduction used for testing

`AudioSettings.Reset()` was invoked while a song was playing (this is the same
engine path used for an audio-device reconfiguration):

Before the fix:

```
[diag] resetting audio: clip='music_3_loop' playing=True time=6.741 vol=0.600
[diag] AudioSettings.Reset returned True
[diag t=17.0] clip='music_3_loop' playing=False time=0.000 vol=0.600   <-- silent forever
```

After the fix:

```
[diag] resetting audio: clip='music_4_loop' playing=True time=6.741 vol=0.600
[recovery] audio configuration changed (deviceWasChanged=False)
[recovery] restarted music (watchdog); song=4 clip='music_4_loop' volume=0.600 loop=True
[diag t=17.1] clip='music_4_loop' playing=True time=0.341 vol=0.600   <-- recovered
```

Normal (no reset) runs produce no recovery log entries, i.e. the watchdog does
not interfere with regular playback.

## What the plugin does

### Menu music with fade-in

- When the save, the main menu (`knop.start_menu`) and the music controller are
  ready, and the player has finished the tutorial and has Music enabled, the
  plugin calls `MusicController.Nachat_New_Run(-1, false)` once - exactly what
  the Settings toggle does when switching Music back on.
- Instead of the game's near-instant `SetToMaxVolume` ramp, the source starts at
  volume 0 and the plugin ramps `current_max_volume` with a smoothstep over
  `FadeInSeconds` (default **2.5 s**). The game's own volume coroutine follows
  that ramp, so every later volume change (shop, game over, ...) still works.
- Tutorial music is never touched. If Music is off in the game settings, nothing
  is started.

Config file: `BepInEx\config\com.reznolf.ccfr.musicstartup.cfg`

| Key | Default | Meaning |
|---|---|---|
| `Menu Music / StartMusicInMenu` | `true` | Start one song when the main menu is ready |
| `Menu Music / FadeInSeconds` | `2.5` | Fade-in duration (0-10 s) |

### Recovery watchdog

- `MusicRecovery` runs a 0.5 s watchdog. If **all** of these are true:
  - music is enabled (`glov.musicIs_On_1_Off_0 == 1`),
  - the controller and `audio_1` are active/enabled,
  - a song is selected (`whichSongDecidedToPlay != -1`),
  - the source has a clip but `isPlaying == false`,
  - there is no intentional fade-out,

  it calls `Play()` again, restores `current_max_volume` if the source volume
  was zeroed, re-enables `MusicIntroChecker` when the restored clip is an
  `*_intro`, and sets `currentlySongIsPlaying = true`.

- `AudioSettings.OnAudioConfigurationChanged` and focus/resume events trigger an
  immediate (delayed by 0.5 s) recovery attempt, then a verification attempt.
- `MusicController.Start_MUSIC`, `MusicController.Nachat_New_Run` and
  `MusicIntroChecker.PlayMainMusic` are verified shortly after they run, so a
  device reset in the middle of loading a song is also recovered.
- `Application.runInBackground` is forced on at plugin load (the game already
  does this later in `glov.LateStart`, but earlier is safer).

The watchdog deliberately does **not** start music when no song is selected
(`whichSongDecidedToPlay == -1`), so menus and game-over states are unchanged.

## Build and deploy

```powershell
cd <this repository>

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\deploy.ps1
```

`scripts\deploy.ps1` defaults to
`D:\SteamLibrary\steamapps\steamapps\common\CCFR`; pass `-GameDir "C:\path\to\game"`
if your install is elsewhere.

Or directly:

```powershell
dotnet build MusicStartup\MusicStartup.csproj -c Release -p:GameDir="C:\path\to\game"
Copy-Item MusicStartup\bin\Release\CCFR.MusicStartup.dll `
    "<game>\BepInEx\plugins\" -Force
```

**Close the game first** - Windows locks the loaded DLL.

## Developer self-test hooks

Everything below is inert unless the environment variable is set.

| Variable | Effect |
|---|---|
| `CCFR_MUSICDIAG=1` | Enable verbose patching logs |
| `CCFR_MUSICDIAG_SECONDS=30` | How long the heartbeat runs (also enables diagnostics) |
| `CCFR_MUSICDIAG_QUIT=1` | Quit the game when the heartbeat finishes |
| `CCFR_MUSICDIAG_FORCE_PLAY_AT=8` | Call `Nachat_New_Run(-1, false)` at t=8 s |
| `CCFR_MUSICDIAG_RESET_AUDIO_AT=15` | Call `AudioSettings.Reset()` at t=15 s (simulates a device change) |

Example:

```powershell
$env:CCFR_MUSICDIAG_SECONDS="24"
$env:CCFR_MUSICDIAG_QUIT="1"
$env:CCFR_MUSICDIAG_FORCE_PLAY_AT="8"
$env:CCFR_MUSICDIAG_RESET_AUDIO_AT="15"
& "D:\SteamLibrary\steamapps\steamapps\common\CCFR\Caravan Carnage Fruity Road.exe"
```

Then check `D:\SteamLibrary\steamapps\steamapps\common\CCFR\BepInEx\LogOutput.log`
for `[recovery] restarted music`.

## Source map

| File | Responsibility |
|---|---|
| `MusicStartup/Plugin.cs` | BepInEx entry point, config, Harmony bootstrap, host object |
| `MusicStartup/MusicRecovery.cs` | Watchdog, audio-config/focus recovery |
| `MusicStartup/MenuMusic.cs` | Automatic main-menu song + smooth fade-in |
| `MusicStartup/Patches.cs` | Post-start verification patches |
| `MusicStartup/SelfTest.cs` | Env-gated diagnostics, forced play/audio-reset |

## Notes / caveats

- The shipped game has **no dedicated main-menu music track**; music starts when
  a run (or the tutorial) begins. The plugin starts one of the normal run songs
  in the menu (the same result as toggling Music off/on there) and fades it in.
- The automatic menu start happens **once per launch**. Returning to the menu
  after a run keeps the existing behaviour of the base game.
- The music system is global (`glov.cc.musicControllerRef`), so the fix is
  independent of scene changes.
- If the player has Music turned **off** in Settings, the plugin does nothing.
- This mod does not change the save file.

## Credits

- Game and in-game assets are © [Gonzossm](https://www.gonzossm.com/) /
  [Caravan Carnage: Fruity Road](https://store.steampowered.com/app/3330940/).
  This repository contains no game assets.
- Built with [BepInEx](https://github.com/BepInEx/BepInEx) and
  [HarmonyX](https://github.com/BepInEx/HarmonyX).

## License

MIT. See [LICENSE](LICENSE).
