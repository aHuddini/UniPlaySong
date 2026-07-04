# Achievement Sound Integration (URI) — Technical Reference

**Status:** shipped v1.5.10. **Audience:** UniPlaySong maintainers and external plugin devs (e.g. Playnite Achievements).

UniPlaySong can play a console-style "trophy unlocked" sound on demand. It does **not** detect
achievement unlocks itself — an external plugin (or theme) tells it via a Playnite URI, and
UniPlaySong plays the user's configured sound. This keeps the two sides fully decoupled: no shared
assembly, no compile-time dependency, safe if either is absent.

## The URI contract

```
playnite://uniplaysong/playniteachievements/{rarity}
```

`{rarity}` ∈ `common | uncommon | rare | ultrarare | capstone` (case-insensitive).

- **Namespaced** under `playniteachievements` so other source plugins can have their own path later
  (`uniplaysong/<source>/...`).
- **Rarity is carried by the path** — no query args, no name parsing.
- **Fire-and-forget** — no return value; UniPlaySong plays on receipt.
- **Forward-compatible** — an unrecognized or missing tier still plays the configured sound; never errors.
- **Current behavior:** all five tiers play the **same** user-configured sound. Per-rarity override
  sounds are a planned, non-breaking follow-up (see "Extending" below).

### Caller side (e.g. Playnite Achievements)

```csharp
// On unlock — pick the tier segment, fire the URI. No reference to UniPlaySong.
string tier = /* "common" | "uncommon" | "rare" | "ultrarare" | "capstone" */;
try { System.Diagnostics.Process.Start($"playnite://uniplaysong/playniteachievements/{tier}"); }
catch { /* UPS not installed / URI unhandled — ignore */ }
```

Note: `Process.Start` on a URI spawns a shell + resolves the protocol (some fixed OS latency). A
plugin already running inside Playnite has no faster in-process path today; a future typed
shared-interface channel (below) would remove even that.

## UniPlaySong side — how it's wired

```
URI  →  UriHandler.RegisterSource("uniplaysong", …)  [src/UniPlaySong.cs]
     →  ExternalControlService.HandleCommand         [switch on args[0]]
     →  case "playniteachievements" → HandlePlayniteAchievement(args)
     →  JingleService.PlayForEvent(JingleEvent.Achievement, settings)
     →  PlayExternalSound(path, settings)            [dedicated lightweight path]
```

Key files:
- `src/Services/ExternalControlService.cs` — URI dispatch (`playniteachievements` case, `HandlePlayniteAchievement`). `args[1]` is the rarity (parsed; currently informational).
- `src/Services/JingleService.cs` — `JingleEvent.Achievement`, `GetConfigForEvent`, and the **separate** `PlayExternalSound` / `_externalPlayer` / `_createLightweightPlayer` path.
- `src/UniPlaySong.cs` — registers the URI source; constructs `JingleService` with the lightweight (SDL2) factory.
- `src/UniPlaySongSettings.cs` — `EnableAchievementSound`, `AchievementSoundType`, `SelectedAchievementJingle`, `AchievementSoundPath` (all off/default; `[not] JsonIgnore` — these are persisted user settings).
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
  (`SleepCoordinator` → `ReleaseAllDevices`) sees its open device via `IsAnyDeviceOpen`; the actual
  close is performed by the main player on the shared device.

Net: an achievement sound that opened the shared device is torn down after
`IdleAudioDeviceTeardownMinutes` (Experimental) just like any other playback, and it never blocks
Windows sleep. See `docs/dev_docs/` issue-#81 notes and `src/Services/SleepCoordinator.cs`.

## Behavior guarantees

- No-op if `EnableAchievementSound` is off.
- No-op (no crash) if UPS isn't installed — the URI just routes nowhere on the caller's side.
- Plays over a running game (UPS music is already paused; no ducking needed).
- Volume follows `MusicVolume` for now.

## Extending

- **Per-rarity override sounds.** `HandlePlayniteAchievement` already parses `args[1]` (the tier).
  To vary the sound: add per-rarity settings, branch on the tier in `GetConfigForEvent` (or a new
  resolver), keep the master sound as the default when an override is blank. Non-breaking — the URI
  contract is unchanged.
- **Dedicated achievement volume.** Change the one line in `PlayExternalSound` that reads
  `settings.MusicVolume` to a new `AchievementVolume` setting (default = current behavior). Non-breaking.
- **Trophy name / metadata.** The path can carry an extra segment
  (`.../playniteachievements/{rarity}/{urlEncodedName}`) that v1 ignores — parse it when needed.
- **Typed channel (optional, later).** If richer data + a return value is ever wanted, add a shared
  contract interface discovered via `PlayniteApi.Addons.Plugins.OfType<IUpsAchievementSink>()`,
  **alongside** — not replacing — the URI. The URI stays the zero-dependency default.

## History

- v1.5.10 — initial release: single `playniteachievements/{rarity}` URI, one sound for all tiers,
  dedicated lightweight SDL2 player. Proposed by the Playnite Achievements dev as PA↔UPS
  cross-support (PA owns the visual notification, UPS owns the audio + user config).
