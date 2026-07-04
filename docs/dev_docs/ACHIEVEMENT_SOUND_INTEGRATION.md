# Achievement Sound Integration (URI) — Technical Reference

**Status:** shipped v1.5.10. **Audience:** UniPlaySong maintainers and the Playnite Achievements dev (and any other plugin/theme that wants to reuse it).

**Designed for the Playnite Achievements (PA) plugin.**
This feature exists specifically because the PA developer proposed a cross-plugin collaboration: **PA
detects trophy unlocks and shows the visual notification; UniPlaySong plays the sound and lets the
user customize it** — console-style. The URI's `playniteachievements` path segment is named after PA
for exactly this reason. PA is the primary and reference consumer.

The mechanism is generic (any plugin or theme can fire the same URI), but it was built for the PA
integration; keep PA as the driving use case when changing anything here.

UniPlaySong does **not** detect achievement unlocks itself — PA tells it via a Playnite URI, and
UniPlaySong plays the user's configured sound. The two sides are fully decoupled: no shared
assembly, no compile-time dependency, safe if either is absent.

## The URI contract

```
playnite://uniplaysong/playniteachievements/{tier}
```

`{tier}` is one of (case-insensitive; the segment names follow the Playnite Achievements command labels):

| Tier segment            | Rarity      | Badge     |
|-------------------------|-------------|-----------|
| `commonachievement`     | Common      | bronze    |
| `uncommonachievement`   | Uncommon    | silver    |
| `rareachievement`       | Rare        | gold      |
| `ultrarareachievement`  | Ultra-Rare  | platinum  |
| `capstoneachievement`   | Capstone    | perfect   |

`capstoneachievement` = platinum trophy / 100% completion.

- **Namespaced** under `playniteachievements` so other source plugins can have their own path later
  (`uniplaysong/<source>/...`).
- **Tier is carried by the path** — no query args, no name parsing on the caller side.
- **Fire-and-forget** — no return value; UniPlaySong plays on receipt.
- **Forward-compatible** — an unrecognized or missing tier plays the master/default sound; never errors.
- **Per-rarity sounds:** each tier resolves to a sound via the user's selected **Achievement Sound
  Pack** (Theme / PA Starter / Custom), falling back to a single master/default sound. So the caller
  always fires the tier; whether it sounds distinct is the user's choice in UniPlaySong's settings.

### Caller side (e.g. Playnite Achievements)

```csharp
// On unlock — pick the tier segment, fire the URI. No reference to UniPlaySong.
// tier = "commonachievement" | "uncommonachievement" | "rareachievement"
//        | "ultrarareachievement" | "capstoneachievement"
string tier = /* map trophy rarity to one of the above */;
try { System.Diagnostics.Process.Start($"playnite://uniplaysong/playniteachievements/{tier}"); }
catch { /* UPS not installed / URI unhandled — ignore */ }
```

Note: `Process.Start` on a URI spawns a shell + resolves the protocol (some fixed OS latency). A
plugin already running inside Playnite has no faster in-process path today; a future typed
shared-interface channel (below) would remove even that.

## UniPlaySong side — how it's wired

```
URI  ->  UriHandler.RegisterSource("uniplaysong", ...)  [src/UniPlaySong.cs]
     ->  ExternalControlService.HandleCommand         [switch on args[0]]
     ->  case "playniteachievements" -> HandlePlayniteAchievement(args)
     ->  JingleService.PlayForEvent(JingleEvent.Achievement, settings)
     ->  PlayExternalSound(path, settings)            [dedicated lightweight path]
```

Key files:
- `src/Services/ExternalControlService.cs` — URI dispatch (`playniteachievements` case, `HandlePlayniteAchievement`). `args[1]` is the rarity (parsed; currently informational).
- `src/Services/JingleService.cs` — `JingleEvent.Achievement`, `GetConfigForEvent`, and the **separate** `PlayExternalSound` / `_externalPlayer` / `_createLightweightPlayer` path.
- `src/UniPlaySong.cs` — registers the URI source; constructs `JingleService` with the lightweight (SDL2) factory.
- `src/UniPlaySongSettings.cs` — `EnableAchievementSound`, `AchievementSoundType`, `SelectedAchievementJingle`, `AchievementSoundPath`, `AchievementSoundPack` (enum), and five `{Rarity}AchievementSoundPath` (off by default; persisted user settings, NOT `[JsonIgnore]` runtime state).
- `src/Services/BundledJingleService.cs` — `GetPAStarterPackPath(rarity)`; `src/Common/PlayniteThemeHelper.cs` — `FindThemeAchievementSound(rarity)` / `CountThemeAchievementSounds()`.
- Settings UI: `src/UniPlaySongSettingsView.xaml(.cs)` (Gamification section), `src/UniPlaySongSettingsViewModel.cs` (`PreviewAchievementSoundCommand`, `BrowseAchievementSoundCommand`).

## Why a dedicated lightweight player (the performance decision)

The achievement sound plays on its **own SDL2 player**, deliberately separate from the regular
jingle path (completion/abandoned), for two reasons:

1. **Latency.** With Live Effects on, the regular jingle path uses the NAudio player, which rebuilds
   its persistent mixer/device on **every** fire — measured at ~130ms (`EnsurePersistentLayer`).
   For a short notification "ding" that's pure, pointless lag. The lightweight SDL2 path skips it:
   SDL2's device is opened once (shared), so a fire is near-instant.
2. **Semantics.** A trophy sound fired over a running game shouldn't run through reverb/visualizer or
   the pause/duck machinery meant for the user's music. `PlayExternalSound` has **no**
   `PauseForJingle`/`ResumeFromJingle` and **no** viz save/restore — it just plays and disposes.

**The regular jingle path (completion/abandoned) is untouched** — separate factory, separate player
field (`_jinglePlayer`), separate `Play()`. Do not merge the two; the isolation is the point.

### Device / issue-#81 safety (important)

SDL2's audio device is **process-wide** — one shared device (`static _isSDLAudioInitialized`,
global `Mix_OpenAudio`/`Mix_CloseAudio`), **not** one per player. The external player is its own C#
object with its own music handle but **rides that shared device**. It is a **secondary** holder
(`enableIdleTeardown: false`), which means:

- Disposing it frees only its music (`Mix_FreeMusic`), **never** `Mix_CloseAudio` — it cannot kill
  the main player's audio.
- It must **not** close the shared device itself; only the main (teardown-enabled) player may.
- It **is** registered with `AudioDeviceRegistry` so the issue-#81 idle/lock/suspend release
  (`SleepCoordinator` -> `ReleaseAllDevices`) sees its open device via `IsAnyDeviceOpen`; the actual
  close is performed by the main player on the shared device.

Net: an achievement sound that opened the shared device is torn down after
`IdleAudioDeviceTeardownMinutes` (Experimental) just like any other playback, and it never blocks
Windows sleep. See `docs/dev_docs/` issue-#81 notes and `src/Services/SleepCoordinator.cs`.

## Behavior guarantees

- No-op if `EnableAchievementSound` is off.
- No-op (no crash) if UPS isn't installed — the URI just routes nowhere on the caller's side.
- Plays over a running game (UPS music is already paused; no ducking needed).
- Volume follows `MusicVolume` for now.

## Per-rarity sound resolution (sound-pack model)

`HandlePlayniteAchievement` maps each tier segment to a `JingleEvent`
(`AchievementCommon/Uncommon/Rare/UltraRare/Capstone`), plus `Achievement` for the master/default.

The global gate is `EnableAchievementSound`. When on, a rarity event resolves to a **file path**
directly (not a `JingleSoundConfig`) via `JingleService.ResolveAchievementRarityPath`, driven by the
`AchievementSoundPack` setting:

```
Theme         -> PlayniteThemeHelper.FindThemeAchievementSound(rarity)  // audio/Achievements/{rarity}.{wav,mp3,ogg,flac}
                 ?? PA Starter Pack file for {rarity}
PAStarterPack -> Jingles/Achievements/PAStarterPack/{rarity}.mp3        // bundled Pixabay set (default)
Custom        -> {Rarity}AchievementSoundPath (if set + exists)
                 ?? PA Starter Pack file for {rarity}
```

If the pack yields nothing, the event falls back to the **master default sound**
(`AchievementSoundType` / `SelectedAchievementJingle` / `AchievementSoundPath`, or a system beep). The
master event itself still goes through `GetConfigForEvent` → `MasterAchievementConfig`. All achievement
events play on the lightweight external-sound path (`PlayExternalSound`).

Settings: `EnableAchievementSound` (gate + master), master `AchievementSoundType` /
`SelectedAchievementJingle` / `AchievementSoundPath`, `AchievementSoundPack` (enum, default
`PAStarterPack`), and five custom paths `{Rarity}AchievementSoundPath` (used only in Custom mode).
Browsing a custom file for any rarity in the settings UI auto-switches the pack to `Custom`.

Rarity badge icons (`Images/Achievements/badge-{bronze,silver,gold,platinum,perfect}.png`, resolved
via `BundledImageService`) label each per-rarity file row. The PA Starter Pack sounds are royalty-free
Pixabay SFX; badge art is derived from the Playnite Achievements plugin badges (MIT) — see `NOTICES.txt`.

## Extending

- **Dedicated achievement volume.** Change the one line in `PlayExternalSound` that reads
  `settings.MusicVolume` to a new `AchievementVolume` setting (default = current behavior). Non-breaking.
- **Trophy name / metadata.** The path can carry an extra segment
  (`.../playniteachievements/{tier}/{urlEncodedName}`) that today's code ignores — parse it when needed.
- **Typed channel (optional, later).** If richer data + a return value is ever wanted, add a shared
  contract interface discovered via `PlayniteApi.Addons.Plugins.OfType<IUpsAchievementSink>()`,
  **alongside** — not replacing — the URI. The URI stays the zero-dependency default.

## History

- v1.5.10 — achievement unlock sound, dedicated lightweight SDL2 player. Proposed by the Playnite
  Achievements dev as PA<->UPS cross-support (PA owns the visual notification, UPS owns the audio +
  user config). Shipped with five per-rarity tiers (common/uncommon/rare/ultrarare/capstone) driven by
  a selectable **Achievement Sound Pack** (Theme / PA Starter / Custom) over a master/default fallback,
  plus the bundled PA Starter Pack (Pixabay SFX) and PA-derived rarity badge icons.
