# Spotify Control Integration

Developer reference for UniPlaySong's Spotify control feature (v1.5.7, `dev`). Control-only: UPS never captures, mixes, stores, or processes Spotify audio — it only sends transport commands to the running Spotify desktop app and reads now-playing metadata.

> **v1.6.5 — Live Effects & Visualizer on Spotify (Experimental, opt-in).** An opt-in path (`ApplyLiveEffectsToSpotify`, Live Effects tab, default off; visualizer couples to the existing Spectrum Visualizer setting) now *does* capture Spotify audio via Windows Process Loopback and run it through the shared NAudio effects/visualizer chain — see **Live Effects & Visualizer on Spotify** below. The control-only architecture documented here is unchanged and still the default; the effects path is layered on top of it.

## Why "control-only"

Spotify's DRM + ToS (§IV.2 no stream-ripping, Policy §III.7 no-mixing) forbid capturing or remixing the audio stream. So UPS treats Spotify as an **external audio source it conducts**, not an audio source it plays. Consequences:

- No visualizer, no fades, no SmoothVolume on Spotify audio (those operate on UPS's own NAudio pipeline only).
- The "music" during a Spotify gap is literally the Spotify app's own output; UPS just pauses/resumes/skips it.

## Architecture (event-mirror, two layers)

```
MusicPlaybackService  ──events──►  SpotifyControlService  ──commands──►  SpotifySmtcClient  ──►  Windows SMTC  ──►  Spotify app
 (engine, owns gap state)          (policy / "the conductor")             (mechanism, WinRT)
```

- **`SpotifySmtcClient`** (`src/Services/Spotify/SpotifySmtcClient.cs`) — mechanism. Wraps `Dubya.WindowsMediaController` 2.5.6 (MIT, net4.6.2) over the Windows `GlobalSystemMediaTransportControlsSession`. Implements `ISpotifyClient`. Every method is fail-safe (never throws; returns `false`/`Empty` on any failure). Identifies Spotify by a **case-insensitive substring `"spotify"` on the session Id** — works for both the Win32 build (`Spotify.exe`) and the Store build (`SpotifyAB.SpotifyMusic_…!Spotify`). Re-pulls the session every call (never caches a stale reference, because the library's close events are unreliable).

- **`SpotifyControlService`** (`src/Services/Spotify/SpotifyControlService.cs`) — policy. Observes `MusicPlaybackService`'s public surface (`OnPlaybackStateChanged`, `IsPaused`, `IsPlayingDefaultMusic`) plus `ISpotifyClient.AvailabilityChanged`, and a low-frequency `DispatcherTimer` (7 s) safety refresh (the SMTC change events are unreliable; a missed close event can otherwise strand `SpotifyActive=true`). `Recompute()` is `lock`-serialized because it's called from the UI thread (events), the threadpool (WinRT callbacks), and the timer.

- **`MusicPlaybackService`** — the engine. Knows nothing about SMTC. Owns the "default-music gap" state and exposes it via `IsPlayingDefaultMusic`.

`ISpotifyClient` surface: `IsAvailable`, `IsPlaying`, `TryPause`, `TryResume`, `TrySkipNext`, `TrySkipPrevious`, `TryTogglePlayPause`, `GetNowPlaying`, `event AvailabilityChanged`. Each transport command is gated on its SMTC `Controls.IsXEnabled` flag.

## Engagement modes

UPS conducts Spotify in two mutually-reinforcing situations, both computed by `ComputeActive`:

1. **Spotify radio mode** — `RadioModeEnabled && SpotifyRadioMode`. Spotify replaces UPS music entirely (radio precedence; evaluated first, unconditionally).
2. **Spotify default source** — `DefaultMusicSourceOption == DefaultMusicSource.Spotify && IsPlayingDefaultMusic`. For games with no UPS music, Spotify is conducted instead of playing a default track. Games that *have* music still play it (Spotify pauses).

`SpotifyActive` (`[JsonIgnore]` runtime setting) is the computed result; it's both consumed internally and surfaced for the now-playing UI.

## The default-music gap (how `DefaultMusicSource.Spotify` activates)

`SpotifyActive` depends on `IsPlayingDefaultMusic == true`, but Spotify has **no file** to resolve. So the gap is a pure flag set on the no-songs resolution path:

- `MusicPlaybackService.PlayGameMusic` → `case DefaultMusicSource.Spotify` (reached when `songs.Count == 0 && EnableDefaultMusic`): sets `_isPlayingDefaultMusic = true`, fades out any prior UPS track, and fires `OnPlaybackStateChanged` so the service recomputes. UPS itself plays nothing.
- `IsDefaultMusicPath()` returns `false` for `DefaultMusicSource.Spotify` (no local file is ever "the Spotify default path").
- Suppression is **song-count driven, not flag driven**: a game with songs always plays them; only the 0-song path marks the gap. (An earlier flag-based suppression guard caused a stale-read race and was removed.)

## The drive model: "active = play, inactive = pause"

`Recompute()` drives Spotify toward a desired state rather than mirroring UPS's pause stack:

- `wantSpotifyPlaying = active && _playback.IsPaused != true`.
- **Want playing** → ensure Spotify is playing (`TryResume` if not). Sets `_drivingSpotify = true` (UPS "takes the wheel").
- **Want paused** (while `_drivingSpotify`) → `TryPause()` **unconditionally** (not gated on `IsPlaying`). On `!active`, release the wheel (`_drivingSpotify = false`).

### Key invariants / why they're written that way

- **Pause is unconditional** (not gated on `IsPlaying`). A no-music game (gap) immediately followed by a game-with-music (e.g. a Fullscreen→Desktop mode switch, ~20 ms apart) could leave a just-issued `TryResume` not yet reflected in Spotify's SMTC status. The old `if (IsPlaying) TryPause()` gate then skipped the pause AND released the wheel; Spotify then started and nothing paused it → it played over the game. `TryPause()` self-guards via `IsPauseEnabled`, so calling it when Spotify isn't (yet) playing is a safe no-op; SMTC serializes resume+pause and the pause wins (issued last).
- **`_drivingSpotify` is the ownership flag.** It stays true the entire time Spotify is the active music — *including across consecutive no-music games* — and only releases when a game-with-music or lifecycle pause takes over. It prevents UPS from fighting a user's own manual Spotify before UPS has taken the wheel.
- **The asymmetric handoff** (driven from `MusicPlaybackService`, coupled to the fader):
  - *Entering the gap* (game-with-music → no-music): Spotify is **incoming** → the resume trigger is deferred into the fade-out completion callback, so Spotify comes in only after UPS audio has faded (no overlap).
  - *Leaving the gap* (no-music → game-with-music): Spotify is **outgoing** → the pause fires synchronously at takeover (instant), so Spotify clears before the game track fades in. This also avoids waiting for the 7 s timer.

## Skip-on-gap (`SpotifySkipOnGap`)

Playback-tab toggle (under the Spotify default-source option; behavior-only, no restart). When on, **entering the gap** advances Spotify to a new track instead of resuming the current one — a fresh song per no-music game.

- Trigger: the **entering edge** `enteringActive = active && !_isActive` (captured before `_isActive` is mutated), **and** `!_drivingSpotify`. The `!_drivingSpotify` guard means no-music → no-music does **not** re-skip (we never left the gap, so we were already driving Spotify); only a fresh takeover from non-Spotify music skips. It's also immune to a transient `SpotifyActive` flicker on game switches.
- Fallback: if `TrySkipNext` is unavailable (end of queue, no autoplay → returns `false`), fall back to `TryResume` so the gap is never silent.

> **Open verification:** the no-music → no-music "no re-skip" guard assumes the transition keeps `SpotifyActive` continuously true. If that transition flickers `active=false` (releasing `_drivingSpotify` mid-switch), a second skip could slip through. Confirm with a `[SPOTIFY-DIAG]`-style log if behavior regresses; the flicker-immune alternative is to track the last game id skipped for.

## Manual transport commands (Fullscreen/Desktop menus)

Spotify Skip Next / Previous / Play-Pause are exposed in **both** the game menu and the main (Extensions) menu via the shared `GetSpotifyMenuActions()` helper in `UniPlaySong.cs` (one source of truth). Gated on `_spotifyClient.IsAvailable` — entries vanish when Spotify isn't running.

- Main menu: `MenuSection = "@"` → top of the Fullscreen Extensions menu (labels are self-identifying, "Spotify: …").
- Game menu: same `menuSection` as the other UPS game-menu items → inside the single existing UniPlaySong submenu.

**Manual commands route through the SERVICE, not the client**, so the policy layer can manage the manual-pause hold (below).

### Manual-pause hold (`_manualPauseHold`)

Without this, pressing menu Play/Pause while Spotify is the active music is self-defeating: the pause fires an SMTC state-change event → `Recompute` sees "should be playing, isn't" → `TryResume` → instant re-play.

- `ToggleManualPlayPause()` reads `IsPlaying` *before* toggling: if it was playing (→ now pausing), set `_manualPauseHold = (wasPlaying && _isActive)`; if it was paused (→ now resuming), clear it.
- While `_manualPauseHold && active`, `Recompute` goes **fully hands-off** (issues no command) — it does NOT keep re-pausing (which would fight a resume-from-elsewhere). `_drivingSpotify` stays true (we still own the slot).
- The hold **auto-clears** on the `!active` transition, so the next time Spotify becomes the active music it plays fresh rather than inheriting a stale held-pause.
- Manual **Skip** clears the hold (skipping implies "play this").

## Settings (three-site rule)

`SpotifyRadioMode`, `SpotifySkipOnGap` — backing field default in `UniPlaySongSettings.cs`, property with `OnPropertyChanged()`, and the Playback-tab `Reset*Tab_Click` handler. Global reset is auto-covered by the JSON deep-clone of backing-field defaults.

- `SpotifyRadioMode` checkbox and the `DefaultMusicSource.Spotify` radio button both bind `SetRestartRequired` (Playnite's native restart prompt) — toggling them mid-session can race the live drive state; a restart cleanly resets all flags. `SpotifySkipOnGap` is behavior-only and needs no restart.

## Now-playing

`SpotifyControlService.GetNowPlaying()` returns the current track (title/artist) when active, `Empty` otherwise. `TopPanelMediaControlViewModel` subscribes to `NowPlayingChanged` and shows the Spotify track in the Top Panel now-playing area while Spotify is active, restoring the UPS song when it goes inactive. SMTC also exposes the album-art thumbnail and timeline position (see "Future" below).

## Tier boundary

This is **Tier 1 (SMTC control)** — no OAuth, no Spotify Web API, no per-user credentials, no audio access. A future Tier 2 (Web API for playlist selection) would be additive and is out of scope here.

## Future / available-but-unused SMTC surface

The `GlobalSystemMediaTransportControlsSession` we already hold also exposes (all gated on the corresponding `Controls.IsXEnabled`): `TryStopAsync`, `TryChangeShuffleActiveAsync`, `TryChangeAutoRepeatModeAsync`, `TryChangePlaybackPositionAsync` (seek — Spotify support is inconsistent), and **read** access to `MediaProperties.Thumbnail` (album art, an `IRandomAccessStreamReference` decodable to a WPF bitmap) and `GetTimelineProperties()` (position/duration, updated on SMTC's own coarse cadence). These enable a richer mini-player / album-art surface — Desktop only, since Playnite's Top Panel and Sidebar don't render in Fullscreen.

## Live Effects & Visualizer on Spotify (v1.6.5, Experimental)

Opt-in path that lets UPS's Live Effects (reverb/EQ) and the Spectrum Visualizer operate on Spotify audio, the same way they operate on UPS's own game music. This is the one place UPS *does* capture Spotify audio — gated behind `ApplyLiveEffectsToSpotify` (effects) and the existing Spectrum Visualizer setting (visualizer), both off by default. Requires **Windows 10 build 20348+** (Process Loopback Capture); below that the feature is unavailable and Spotify plays dry.

**Seam:** `SpotifyLoopback.dll` (bundled native shim; Process Loopback via `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`, `INCLUDE_TARGET_PROCESS_TREE` on Spotify's window PID) → `SpotifyLoopbackClient` (P/Invoke + ring buffer) → `SpotifyCaptureSampleProvider` (an `ISampleProvider`, normalized to 44100Hz stereo float) → `NAudioMusicPlayer.LoadExternalSource`, which runs it through the **same** `EffectsChain` / `VisualizationDataProvider` / persistent mixer as game music (no `SongEndDetector` — a live capture never ends). See `docs/dev_docs/NAUDIO_PIPELINE.md` → "External Source Path".

**Safety invariant (`SpotifyEffectsCoordinator`):** hearing Spotify's raw output *and* the effected version at once would double the audio, so for the EFFECTS case Spotify's dry output is **muted iff UPS is producing effected output for it**, and **auto-unmuted on any stop** (effects disabled, capture stops/dies, UPS closes). The dry-output mute reuses `SpotifyAudioSession` (`ISimpleAudioVolume.SetMute` — the same audio-session mute as the v1.6.4 theme-mute work). The VISUALIZER case mutes **nothing**: it reacts to Spotify (dry or effected) with Spotify still audible. Error/unsupported-OS/mute-failure paths fall back to dry Spotify with no effects/viz.

Full feasibility (proven end-to-end 2026-07-12) and design: `docs/dev_docs/SPOTIFY_LIVE_EFFECTS_FEASIBILITY.md`.

## Key files

- `src/Services/Spotify/ISpotifyClient.cs` — mechanism contract
- `src/Services/Spotify/SpotifySmtcClient.cs` — SMTC implementation
- `src/Services/Spotify/SpotifyControlService.cs` — policy / drive model / manual hold
- `src/Services/MusicPlaybackService.cs` — gap case (`DefaultMusicSource.Spotify`), asymmetric handoff
- `src/UniPlaySong.cs` — `GetSpotifyMenuActions`, game + main menu wiring
- `tests/Services/Spotify/SpotifyControlServiceTests.cs` — drive model, skip-on-gap, manual-pause-hold
- `tests/Services/Spotify/SpotifyDefaultSourceIntegrationTests.cs` — real-service gap activation
