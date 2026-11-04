# Music Fix

A small BepInEx 5 plugin for **Caravan Carnage: Fruity Road**. It keeps music
playing when the base game would leave it silent, and fades in a song when the
main menu appears.

- Plugin ID: `com.reznolf.ccfr.musicfix`
- DLL: `CCFR.MusicFix.dll`
- Version: `0.3.0`
- Target: .NET Standard 2.1 / BepInEx 5.4.23.6 / Unity 6000.3.8f1 (Mono)

## Requirements

- Caravan Carnage: Fruity Road (Steam app `3330940`)
- [BepInEx 5.4.23.6 x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6)

## Download

Grab the latest release:
[Music-Fix releases](https://github.com/reznolf-caravane-carnage-fruity-road/Music-Fix/releases/latest)

The zip contains `BepInEx/plugins/CCFR.MusicFix.dll`; extract it into the game
folder.

## Installation

1. Install BepInEx 5 x64 into the game folder and run the game once.
2. Copy `CCFR.MusicFix.dll` into `<game>\BepInEx\plugins\`.
3. Start the game. Music fades in when the main menu appears.

---

## Symptoms

1. **No music after launch** (especially when the game starts unfocused/in the
   background). Toggling Music off/on in Settings makes it start.
2. **Music fades out and stays silent** after a long session, usually when
   entering the shop after several runs. Toggling Music off/on restarts it.
3. **Music stays very quiet after closing the shop** (`EnterShop()` lowers it to
   5% and the normal close path never restores it).

## Root causes (verified in-game)

### 1. Audio-device reconfiguration stops the source

`MusicController.Start_MUSIC()` plays the source once and never checks it again:

```csharp
musicAudioSource_1.Play();
musicAudioSource_1.volume = 0f;
setVolumeMaxRoutine = StartCoroutine(SetToMaxVolume(musicAudioSource_1, 60f));
```

When Unity reinitialises its audio device (starting unfocused and gaining focus
later, or changing the default output device), every `AudioSource` stops. The
clip, loop flag and volume survive, but `isPlaying` becomes `false` and `time`
resets to `0`.

The controller still reports `whichSongDecidedToPlay != -1`,
`currentlySongIsPlaying = true`, `fadingOutMusic = false`, so the game believes
music is playing. Nothing handles `AudioSettings.OnAudioConfigurationChanged`.
The only path that calls `Play()` again is `knop.Clicked_On_Off_Music` →
`ResetMusic()` + `Nachat_New_Run()` - the Settings toggle.

### 2. The "change music after 4 game-overs" fade leaves nothing playing

`DoBill` → `Check_If_Saw_GameoverScreen_Many_Times` is intentional game logic:

```csharp
FadeOutMusicSong();                              // 15 s fade to zero
fadedOutMusicCuzSawGameoverManyTimes = true;
```

`VolumeWaiter` then clears the song:

```csharp
musicAudioSource_1.volume = 0f;
whichSongDecidedToPlay = -1;
currentlySongIsPlaying = false;
```

Nothing starts a new song until the **next run** begins, so the menu/shop stays
silent forever. The fade often finishes while the player is in the shop, which
makes it look like the shop caused it.

**Looping is not the cause.** A loop clip was tested by seeking to
`length - 0.8 s`: `music_1_loop` (114.426 s) wrapped from `time=114.117` back to
`time=0.203` and kept playing with `loop=True`. A source that stopped at the end
of a clip with a song still selected would also be restarted by the recovery
watchdog.

### 3. Shop ducking is never undone

`Shop.Open_Shop_Init()` calls `MusicController.EnterShop()`, which sets
`current_max_volume = 0.05f` and slowly fades the music down. `knop.Close_Shop()`
only hides the shop UI - it never calls `Set_Music_To_Average_Volume()`, so the
music stays at 5% after leaving the shop.

## What the plugin does

### Menu music with fade-in

- When the save, the main menu and the music controller are ready, the player
  has finished the tutorial, and Music is enabled, the plugin calls
  `MusicController.Nachat_New_Run(-1, false)` once - exactly what the Settings
  toggle does.
- The source starts at volume 0 and `current_max_volume` is ramped with a
  smoothstep over `FadeInSeconds` (default **2.5 s**). The game's own volume
  coroutine follows that ramp, so later volume changes still work.

### Recovery watchdog

- Checks every 0.5 s. If music is enabled, a song is selected, a clip is
  assigned, `isPlaying == false`, and there is no intentional fade-out, it calls
  `Play()` again, restores the volume target if needed, re-enables
  `MusicIntroChecker` for `*_intro` clips, and sets `currentlySongIsPlaying`.
- Reacts to `AudioSettings.OnAudioConfigurationChanged`, application focus and
  resume events.
- Verifies `Start_MUSIC`, `Nachat_New_Run` and `PlayMainMusic` shortly after they
  run, covering a device reset during song loading.

### Game-over fade recovery

When the game's own game-over fade finishes (`fadedOutMusicCuzSawGameoverManyTimes`
is set, the song is cleared, and the fade is no longer running), the plugin
starts a **new random song** with the same fade-in instead of leaving silence.
This preserves the game's "change music" intent. It does not race the separate
level-unlock transition.

### Shop volume restore

A postfix on `knop.Close_Shop()` restores the normal music target with
`Set_Music_To_Average_Volume()` after the shop closes.

### Other

- `Application.runInBackground` is forced on at plugin load (the game does this
  later in `glov.LateStart`, but earlier is safer).
- If Music is off in Settings, the plugin does nothing.

## Configuration

`BepInEx\config\com.reznolf.ccfr.musicfix.cfg`

| Key | Default | Meaning |
|---|---|---|
| `Menu Music / StartMusicInMenu` | `true` | Start one song when the main menu is ready |
| `Menu Music / FadeInSeconds` | `2.5` | Fade-in duration (0-10 s) |

## Build and deploy

```powershell
cd <this repository>

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\deploy.ps1
```

`scripts\deploy.ps1` defaults to
`D:\SteamLibrary\steamapps\steamapps\common\CCFR`; pass
`-GameDir "C:\path\to\game"` if your install is elsewhere.

Or directly:

```powershell
dotnet build MusicFix\MusicFix.csproj -c Release -p:GameDir="C:\path\to\game"
Copy-Item MusicFix\bin\Release\CCFR.MusicFix.dll `
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
| `CCFR_MUSICDIAG_FADE_OUT_AT=8` | Call `FadeOutMusicSong()` at t=8 s (simulates the game-over fade) |
| `CCFR_MUSICDIAG_SEEK_END_AT=9` | Seek the current loop clip near its end (loop test) |
| `CCFR_MUSICDIAG_SHOP_TEST_AT=9` | Simulate the shop-close volume restore |
| `CCFR_MUSICDIAG_NO_RECOVERY=1` | Disable the recovery watchdog (for isolated tests) |

Example:

```powershell
$env:CCFR_MUSICDIAG_SECONDS="34"
$env:CCFR_MUSICDIAG_QUIT="1"
$env:CCFR_MUSICDIAG_FADE_OUT_AT="8"
& "D:\SteamLibrary\steamapps\steamapps\common\CCFR\Caravan Carnage Fruity Road.exe"
```

Check `BepInEx\LogOutput.log` for:

- `[recovery] restarted music`
- `[recovery] game-over music fade finished`
- `[recovery] shop closed - restoring music volume`
- `[menu] fade-in complete`

## Source map

| File | Responsibility |
|---|---|
| `MusicFix/Plugin.cs` | BepInEx entry point, config, Harmony bootstrap, host object |
| `MusicFix/MusicRecovery.cs` | Watchdog, audio-config/focus recovery, game-over fade recovery, shop restore |
| `MusicFix/MenuMusic.cs` | Automatic main-menu song + smooth fade-in |
| `MusicFix/Patches.cs` | Post-start verification patches, `knop.Close_Shop` postfix |
| `MusicFix/SelfTest.cs` | Env-gated diagnostics and test hooks |

## Notes / caveats

- The shipped game has **no dedicated main-menu music track**; the plugin starts
  one of the normal run songs there (the same result as toggling Music off/on)
  and fades it in.
- The automatic menu start happens **once per launch**. Returning to the menu
  after a run keeps the existing behaviour of the base game.
- The music system is global (`glov.cc.musicControllerRef`), so the fix is
  independent of scene changes.
- The plugin does not change the save file.

## Credits

- Game and in-game assets are © [Gonzossm](https://www.gonzossm.com/) /
  [Caravan Carnage: Fruity Road](https://store.steampowered.com/app/3330940/).
  This repository contains no game assets.
- Built with [BepInEx](https://github.com/BepInEx/BepInEx) and
  [HarmonyX](https://github.com/BepInEx/HarmonyX).

## License

MIT. See [LICENSE](LICENSE).
