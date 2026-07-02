# Changelog

All notable changes to UniPlaySong will be documented in this file.

> **Release Availability Notice:** Due to the GitHub account suspension, release downloads prior to v1.3.3 are no longer available. Full changelog history is preserved below for reference.

## [1.5.8] - 2026-06-30

Issue #81 sleep/audio-device revamp (fix attempt — pending tester confirmation): UPS's open audio render stream no longer blocks Windows from sleeping/suspending. Rebuilt around a central device registry that releases *every* audio-device holder on lock/suspend/idle, with a resume path that comes back audible at the saved position. Supersedes the earlier main-player-only attempt (branch `fix/issue-81-idle-teardown`, `8fa106f`).

### Added

- **Radio Mode "Custom Folder" now has its own folder picker.** Previously the Radio Mode Custom Folder source silently reused the Default Music folder with no way to choose a folder for radio. It now has a dedicated `RadioCustomFolderPath` setting + Browse button; when left empty it falls back to the Default Music folder (so existing setups are unaffected). `src/UniPlaySongSettings.cs`, `src/UniPlaySong.cs`, `src/UniPlaySongSettingsView.xaml`, `src/UniPlaySongSettingsViewModel.cs`.

- **`AudioDeviceRegistry` + `IAudioDeviceHolder` contract.** A thread-safe registry owns the set of audio-device holders and a single `ReleaseAllDevices(reason)` operation (snapshot-under-lock so release runs off the UI thread; fail-safe so one throwing holder doesn't abort the rest). Holders implement `ReleaseAudioDevice()` / `IsAudioDeviceOpen` / `AudioDeviceLabel`. The four holders — main player, dashboard player (both `NAudioMusicPlayer`), `SDL2MusicPlayer`, and the transient jingle player — register on creation and unregister on dispose, structurally preventing the "forgot a holder" gap that sank the prior attempt. `src/Services/AudioDeviceRegistry.cs`, `src/Services/IAudioDeviceHolder.cs`.

- **`SleepCoordinator` owning the triggers.** Centralizes issue-#81 release: one 1-minute idle timer (a *loaded-but-paused* song now **counts toward** idle — the core bug in the old per-backend timers, which treated paused as activity) plus immediate release on lock/suspend routed from the `SystemEvents` handlers. `IdleTick(DateTime)` is a pure, unit-tested state machine (`0` minutes disables idle release). `src/Services/SleepCoordinator.cs`.

### Fixed

- **Spotify Radio Mode rebuilt — critical freeze, both-playing, and timing fixes (consolidated across the 1.5.8 cycle).**
  - *Freezes eliminated:* all blocking SMTC calls run on a dedicated worker thread (`SpotifySmtcWorker`), SMTC/WinRT startup moved off the plugin-constructor/UI thread (a cold WinRT broker could stall launch for seconds), and a lock-vs-dispatcher deadlock fixed — the radio path raised `NowPlayingChanged` inside `_recomputeLock` while subscribers did a synchronous `Dispatcher.Invoke` to a UI thread that was itself entering `Recompute` (deterministic regression test added; all now-playing UI marshals are async `BeginInvoke` now, removing the deadlock class).
  - *Unification:* Spotify is a `RadioMusicSource` and the standalone toggle is gone (`SpotifyRadioMode` is a derived read-only property), so a theme's Radio Mode button plays whichever source the user picked; disengaging (radio off or source switched away) pauses Spotify instead of leaving it playing.
  - *Both-playing fixed at every entry point:* theme toggle, settings-dialog save, and game switch all suppress UPS music when Spotify is the source (previously only game switch did); source changes while radio is on re-enter cleanly (pool→Spotify suppresses, Spotify→pool starts the pool and pauses Spotify).
  - *Timing:* the engage-Resume is gated on `OnApplicationStarted` (no more play/pause blip during Fullscreen startup, and no pre-UI early start in Desktop); availability poll 7s→2s with an immediate announce when SMTC startup completes. Radio control is a minimal two-flag state machine: pauses/resumes around game launch/video/lock, respects a pause made in the Spotify app until the user resumes. Now-playing updates are event-driven (subscribed to the OS `OnAnyMediaPropertyChanged` track-change signal — the mini-player previously went stale) and fetched off-thread.
  - Key files: `src/Services/Spotify/` (all), `src/Services/MusicPlaybackService.cs`, `src/UniPlaySong.cs`, `src/UniPlaySongSettings.cs`, `src/DeskMediaControl/NowPlayingPanel.cs`, `src/Controls/NowPlayingMiniPlayerModel.cs`.

- **Settings no longer reset after a .pext update.** Root cause: a transient settings-load failure on the update-restart put in-memory DEFAULTS in place, and `OnLibraryUpdated`'s unconditional timestamp save persisted them over the user's config ~6s after startup — seen as Radio Mode turning itself off, and historically as full settings wipes. `SettingsService.SettingsLoadedFromDisk` now tracks whether the config genuinely loaded (with one retry on failure; a missing file still counts as a real first run); all automatic saves (library-update timestamps, startup migrations) are gated on it, so defaults can never silently overwrite the file. User-initiated saves are unchanged. `src/Services/SettingsService.cs`, `src/UniPlaySong.cs`.

- **UPS's open audio stream blocked Windows sleep/suspend** (issue #81, fix attempt). Root cause: Windows will not suspend while any audio render stream is open, and UPS held several (main + dashboard `WaveOutEvent`, process-wide SDL `Mix_OpenAudio`, jingle player) between songs. Now `PowerModeChanged.Suspend` and `SessionSwitch.SessionLock` both call `ReleaseAllDevices`, closing every holder so Windows sees no audio session; devices reopen lazily on the next `Load`/`Play`. The prior attempt released only the main player and relied on `PowerModeChanged.Suspend` for the auto-idle case — which can't fire, because Windows won't *initiate* suspend while audio blocks it (the idle timer is the load-bearing trigger for that path). `src/UniPlaySong.cs`.

- **Resume came back silent after lock/suspend.** Root cause: the device teardown disposed the NAudio persistent mixer + output (nulling the volume provider) while the song reader and its mixer input survived, and `_isInMixer` wasn't reset — so on wake the fade-in ramp ran against a null/dead volume provider and stayed stuck at 0. Fix (modeled on SoundKeeper's "a sleep-reason fully stops the stream; resume fully restarts it, gated by a flag"): `ReleaseAudioDevice()` now saves the play position and marks the player paused-at-position before teardown, and `Resume()` self-heals — it rebuilds the persistent layer and re-adds the surviving input *before* seeking, then ramps against the live provider. Both wake paths are unified through the `PauseSource.SystemLock` symmetry: suspend adds the source (pause + save position) and resume/unlock removes it, driving the same self-healing resume-at-position (no track restart). `src/Services/NAudioMusicPlayer.cs`, `src/UniPlaySong.cs`.

- **SDL2 resume after device release.** SDL frees the decoded music (`Mix_FreeMusic`) on teardown, so it can't simply rebuild a mixer like NAudio. `ReleaseAudioDevice()` now stashes the track path + position (keeping the player logically "loaded" so the playback service still routes resume to it), and `Play()`/`Resume()` transparently reload from the stash and seek back when the music handle is gone. `src/Services/SDL2MusicPlayer.cs`.

### Removed

- **`PowerStateHelper` and its two call sites.** It called `SetThreadExecutionState(ES_CONTINUOUS)` alone — an opt-*out* that cleared UPS's own keep-awake hints, a red herring that neither blocked nor fixed sleep (the open stream is the blocker). UPS now never touches `SetThreadExecutionState`, the truest "never interfere with Windows power behavior" stance. The two per-backend idle timers + `OnIdleTeardownTick` were also removed, superseded by the centralized `SleepCoordinator`. `src/Common/PowerStateHelper.cs` (deleted).

### Changed

- **`IdleAudioDeviceTeardownMinutes` help text** clarified: it now governs the centralized idle timer; lock/suspend release fire immediately regardless of the idle setting (`0` disables *idle* teardown only). The setting remains on the Experimental tab.

## [1.5.7] - 2026-06-26

Spotify control integration (event-mirror architecture): UPS conducts the Spotify desktop app's transport via Windows SMTC while playing none of its own audio. Two opt-in engagement modes. Control-only — no audio capture, no OAuth, no Web API.

### Added

- **Spotify desktop-app control via SMTC.** New `SpotifySmtcClient` (the mechanism, implementing `ISpotifyClient`) wraps `Dubya.WindowsMediaController` 2.5.6 (Windows.Media.Control) to detect the Spotify SMTC session, issue play/pause (`TryResume`/`TryPause`), report availability/playing state, and read track metadata (title, artist) for a "Now Playing" display. Every call is fail-safe (never throws). No audio capture — transport and metadata only.

- **Event-mirror policy layer.** New `SpotifyControlService` (the policy/"conductor") observes `MusicPlaybackService`'s `OnPlaybackStateChanged`, `IsPaused`, and `IsPlayingDefaultMusic` and mirrors UPS's audible state onto the `ISpotifyClient`: it computes a `SpotifyActive` flag and, while active, pauses/resumes Spotify to match UPS. It enforces "only resume what UPS paused" ownership so a user's own Spotify pause is never auto-resumed. A low-frequency (7s) periodic refresh backs up the unreliable SMTC change events, and all recomputes are serialized under a lock (the event fires on the UI thread, SMTC `AvailabilityChanged` on the threadpool, plus the timer).

- **Two engagement modes.** (1) *Radio Mode source* (`SpotifyRadioMode`, a sub-toggle under Radio Mode): when Radio Mode is on, UPS conducts Spotify instead of playing its own radio pool. (2) *Default-music source* (`DefaultMusicSource.Spotify`): for a game with no UPS music, UPS enters a "default-music gap" (sets `IsPlayingDefaultMusic`) and hands off to Spotify instead of the bundled ambient preset. `SpotifyControlService` reacts to the gap and sets `SpotifyActive=true`. Both are opt-in under Settings → Playback.

- **Output suppression via a `SpotifyActive` guard (not a coordinator gate).** When `SpotifyActive` is true, `MusicPlaybackService.PlayGameMusic` stops its own player and returns, so UPS stays silent while Spotify is the audible source. There is no `MusicPlaybackCoordinator` involvement and no deferral gate.

- **Bundled dependency.** `WindowsMediaController.dll` (`Dubya.WindowsMediaController` 2.5.6, NuGet) added to the extension package. No user-installed prerequisite beyond the Spotify desktop app itself.

- **Live now-playing exposure for themes.** New `NowPlayingPublisher` coordinator observes the active music source (UPS song or Spotify) and publishes the current track onto `UniPlaySongSettings` as `[JsonIgnore]` runtime properties — `NowPlayingTitle`, `NowPlayingArtist`, `NowPlayingAlbumArtPath`, and (Spotify only) `NowPlayingAlbum`, `NowPlayingGenre`, `NowPlayingDuration` (preformatted `m:ss`). Themes bind any of these via `{PluginSettings Plugin=UniPlaySong, Path=…}` with live updates. Runs in both Desktop and Fullscreen. A live preview card in the General settings tab mirrors the same data.

- **Spotify track metadata.** `SpotifyNowPlaying` extended with Album, Genre (comma-joined), and total Duration; `SpotifySmtcClient.GetNowPlaying()` reads `MediaProperties.AlbumTitle`/`Genres` and derives length from `GetTimelineProperties()` (`EndTime - StartTime`), each separately fail-safe. (Year is not exposed by SMTC, so it's omitted.)

- **Album-art file + game-cover fallback.** New `NowPlayingArtWriter` extracts embedded ID3 art (TagLib#) or Spotify's SMTC thumbnail to a file and returns its path. It writes a **unique filename per track** (`nowplaying_art_{n}.png`, old file best-effort deleted) so WPF's URI-keyed `BitmapImage` cache can't serve a stale image across track changes. For game music with no embedded art, the publisher falls back to the game's cover image (resolved at the composition root, keeping the publisher decoupled from the Playnite API).

- **Now-playing mini-player theme elements.** Two display-only custom elements — `UPS_NowPlayingMiniPlayer` (horizontal bar: art + title + artist + Spotify album·genre·duration) and `UPS_NowPlayingMiniPlayerCompact` (one line). Both share a single `NowPlayingMiniPlayerModel` (a `DataContext`, not a control base — a XAML-rooted custom control base isn't visible to WPF's markup-compile pass) and are registered via `AddCustomElementSupport`. No mode gate — they render in Desktop and Fullscreen. Documented in `docs/dev_docs/THEME_INTEGRATION_GUIDE.md`.

### Fixed

- **`DefaultMusicSource.Spotify` never engaged.** The default-source mode was wired into the settings/policy layer but not into the engine: `MusicPlaybackService.IsDefaultMusicPath` and the `PlayGameMusic` default-music resolution switch had no `Spotify` case, so a no-music game never set `IsPlayingDefaultMusic` and `SpotifyControlService` never activated. Both switches now handle `DefaultMusicSource.Spotify` — `IsDefaultMusicPath` returns false (no local file) and the resolution switch marks the default-music gap and fires `OnPlaybackStateChanged` to trigger the policy recompute. `src/Services/MusicPlaybackService.cs`.

- **Now-playing tickers ignored Spotify.** The Top Panel ticker (`NowPlayingPanel`) and the Settings "Current Song" line showed "(No song playing)" while Spotify was the active music. Root cause: a Spotify track is modeled as a `SongInfo` with an empty `FilePath`, and `SongInfo.IsEmpty` is defined on `FilePath` — so the panel's empty-check discarded it. The panel now keys "nothing playing" on the title instead, and both tickers fall back to the published Spotify title/artist/duration during the gap.

- **Auto-tagging ignored its own off switch; "Remove All Tags" didn't stick.** All UPS tag mutations route through `GameMusicTagService` (intentionally unconditional — the manual "Scan & Tag" button must work regardless of the toggle), so gating is each caller's responsibility. `OnLibraryUpdated` gated on `AutoTagOnLibraryUpdate`, but the **download-complete callback** (`DownloadDialogService.OnFilesDownloaded`) called `UpdateGameMusicTag(game)` unconditionally — so downloading music tagged the game `[UPS] Has Music` even with the feature off, and a manual "Remove All Tags" was undone by the next download. Fixes: (1) the download path now checks `_settingsService.Current.AutoTagOnLibraryUpdate` (mirrors the `OnLibraryUpdated` gate), so "off" stops *all* automatic tagging; (2) `ExecuteRemoveAllMusicTags` now, when auto-tag is on, turns it off (persisted) before removing — otherwise the next library-update re-scan re-creates and re-applies the tags, making the button appear broken. Default for `AutoTagOnLibraryUpdate` is unchanged (on). `src/Services/DownloadDialogService.cs`, `src/UniPlaySongSettingsViewModel.cs`.

## [1.5.6] - 2026-06-22

Theme-integration fix: `UPS_MusicControl_PauseGamePlayDefault` now reliably plays the user's default music when its `Tag=True` (e.g. at a theme's Welcome Hub), even across login/logout.

### Fixed

- **`UPS_MusicControl_PauseGamePlayDefault` ignored `Tag=True`, playing the selected game's music instead of default music** (reported by a theme dev with a Welcome-Hub reproducer). Two WPF-timing root causes, both in the control's load lifecycle:
  1. The control commonly isn't in the visual tree at launch — it lives in a login-gated, collapsed host (`PART_ViewHost` behind a login screen). By the time it loads post-login, Playnite has already force-selected the first game and played its music. The override's settings `PropertyChanged` is edge-triggered, so when the flag was already at its target value (e.g. a reused control instance), the change notification was swallowed and the stale game music stuck.
  2. The control's `Tag` is bound to its ancestor via `Tag="{Binding RelativeSource={RelativeSource FindAncestor...}, Path=Tag}"`, and that binding only settles *during/after* the `Loaded` pass. Because WPF reuses the control instance across logout→login (the constructor doesn't re-run), reading `Tag` synchronously in `OnLoaded` saw the stale pre-logout value — so logging out on a game then back in at the Welcome Hub kept game music instead of switching to default.
  - **Fix:** `OnLoaded` now defers its work to `DispatcherPriority.Loaded` (the established codebase pattern) so the `FindAncestor` Tag binding settles first, then recomputes the override from the fresh Tag and re-applies the resulting state in both directions (game→default and default→game). To avoid an audible double-fade, `UpdateOverride()` reports whether it actually changed the flag; the explicit re-assert (`IMusicPlaybackCoordinator.HandleForceDefaultMusicOverrideLoaded()`) runs only when the flag was unchanged (the swallowed-edge case), since a real change already replays via the `PropertyChanged` path. New coordinator method + a `GetCoordinator()` accessor on the plugin. `src/Controls/MusicControlPauseGamePlayDefault.xaml.cs`, `src/Services/MusicPlaybackCoordinator.cs`, `src/Services/IMusicPlaybackCoordinator.cs`, `src/UniPlaySong.cs`.

- **`ForceDefaultMusicOverride` leaked across theme/mode reloads, suppressing all game music in themes that don't use the feature.** The flag is `[JsonIgnore]` runtime-only state (documented to "always start false"), set true by `MusicControlPauseGamePlayDefault` (a Fullscreen Welcome-Hub control). The control held its settings reference and its instance registry in **static** fields, so a true flag from one theme (e.g. PS5-Experience) carried over in-memory into a theme with no such control (e.g. Aniki ReMake) and into Desktop mode — clearing each game's song list and forcing the (often empty) theme default, so game selection appeared to do nothing. Reproduced identically on v1.5.3–v1.5.5, confirming the leak has existed since the feature's introduction. **Fix (state-ownership refactor):** the override flag is now a *derived* value — `UpdateOverride()` is the sole writer and computes `active = any currently-loaded control has Tag=True`. The control registers in the static instance list only in `OnLoaded` (not the constructor) and deregisters in `OnUnloaded`, so a torn-down theme's control cannot linger. `OnApplicationStarted` rebuilds the registry from scratch each launch (replacing a blunt `ForceDefaultMusicOverride = false` reset) via a new `MusicControlPauseGamePlayDefault.ResetRegistry()` — and since Desktop↔Fullscreen and theme switches are separate process launches, every session starts clean. The leak is now impossible by construction. `src/Controls/MusicControlPauseGamePlayDefault.xaml.cs`, `src/UniPlaySong.cs`.

- **`ForceDefaultMusicOverride` was honored in Desktop mode, where the feature does not apply.** The consumption point in `PlayGameMusic` cleared game songs whenever the flag was true, regardless of mode. The override is a Fullscreen Welcome-Hub concept; it is now gated to `ApplicationMode.Fullscreen` (resolved the same way as the adjacent `PlayOnlyOnGameSelect` block). In Desktop the override is ignored and game music plays. Documented as a permanent invariant, independent of the leak fix above. `src/Services/MusicPlaybackService.cs`.

- **Active-theme audio path could resolve to a previous theme's file after a theme/mode change.** `PlayniteThemeHelper` cached the resolved `UPS_BackgroundAudio.*` path in a static field with no key, so it could serve a stale theme's path. The cache is now keyed to the active theme ID (`GetActiveFullscreenThemeId()`): a cache hit requires the current theme ID to match the one the path was resolved for, otherwise it re-scans. The resolved path is always a function of the current theme; replaces an interim per-call `File.Exists` re-validation. `src/Common/PlayniteThemeHelper.cs`.

### Changed

- **Removed the first-install auto-detect that silently switched the default-music source to `ActiveThemeMusic`.** `MigrateBundledPresetSettings()` previously flipped `DefaultMusicSourceOption` to `ActiveThemeMusic` on first run when the active theme shipped a `UPS_BackgroundAudio.*` (gated by the one-time `ActiveThemeAutodetectRun` flag). This changed a user-facing setting without consent and could yield silence when the theme's file was empty/missing. The auto-switch block and the `ActiveThemeAutodetectRun` property/backing field are removed; new installs keep the `BundledPreset` default, and `ActiveThemeMusic` is a manual opt-in. Existing settings JSON containing the old key deserializes harmlessly (`PopulateObject` ignores unknown members). The user-initiated "copy `background.*` → `UPS_BackgroundAudio.*`" helper is unchanged. `src/UniPlaySong.cs`, `src/UniPlaySongSettings.cs`.

### Added

- **Two Experimental theme-developer opt-out toggles (`PauseOnThemeOverlay`, `PauseOnThemeVideo`), both default ON.** For theme developers who need to disable UPS's automatic pausing on theme overlays (the `UPS_MusicControl` Tag → `ThemeOverlayActive` path) or on theme `<MediaElement>` videos. With both on, behavior is unchanged for normal users; turning a toggle off self-heals correctly (the overlay gate's clear path is unconditional; the video gate clears `VideoIsPlaying`, releasing any active Video pause via the existing resume chain). Surfaced under a "Theme Developer Options" header in the Experimental tab with an orange theme-dev-only warning; both reset to true in `ResetExperimentalTab_Click`. `src/UniPlaySongSettings.cs`, `src/UniPlaySongSettingsView.xaml`, `src/UniPlaySongSettingsView.xaml.cs`, `src/Services/MusicPlaybackCoordinator.cs`, `src/Monitors/MediaElementsMonitor.cs`.

## [1.5.5] - 2026-06-17

Trailer-audio extraction: the Experimental "Defer to Trailer Audio" source now extracts the audio track from a no-music game's EML video trailer and plays it as default music, instead of merely staying silent.

### Added

- **Trailer-audio extraction and playback** (experimental, off by default). For a game with no UniPlaySong songs, the `DefaultMusicSource.DeferToTrailerAudio` source now demuxes the audio from the game's ExtraMetadataLoader `VideoTrailer.mp4` with FFmpeg and plays it as default music, rather than the prior v1.5.4 no-op that just suppressed UPS's own music. New `TrailerAudioService` (`ITrailerAudioService`) owns trailer resolution (full `VideoTrailer.mp4` only — micro-trailers are treated as no-trailer), FFmpeg demux, and per-game caching under `<Config>\ExtraMetadata\UniPlaySong\TrailerAudioCache\{GameId}.m4a`. Extraction tries a lossless AAC stream copy (`-vn -c:a copy` into `.m4a`) first and falls back to an `.mp3` transcode for non-AAC trailer audio. The extracted file is recognized as default music by `IsDefaultMusicPath` (via `_lastDefaultMusicPath`, covering both the `.m4a` copy and the `.mp3` transcode-fallback paths) so position-preservation and looping work. Cached forever; first play of each game blocks briefly while FFmpeg runs (synchronous `WaitForExit`, an accepted trade-off for the experimental feature — the help text warns it "might be slow"), then later plays are instant cache hits. Service constructed at the composition root and injected into all three `MusicPlaybackService` construction sites; a null service degrades silently to no trailer audio. `.m4a`/`.aac` confirmed in `Constants.SupportedAudioExtensionsLowercase` so the extracted file plays through the existing pipeline. `src/Services/TrailerAudioService.cs`, `src/Services/ITrailerAudioService.cs`, `src/Services/MusicPlaybackService.cs`, `src/UniPlaySong.cs`, `src/Common/Constants.cs`.

- **"Clear trailer-audio cache" button** in **Settings → Cleanup**. MVVM `ClearTrailerAudioCacheCommand` → `plugin.ClearTrailerAudioCache()` → `ITrailerAudioService.ClearCache()` (reuses the service's deletion logic; no duplicated file-walk in the view). Reports files cleared and bytes freed, or a "nothing to clear" message. `src/UniPlaySongSettingsView.xaml`, `src/UniPlaySongSettingsViewModel.cs`, `src/UniPlaySong.cs`.

### Changed

- **Trailer option relabeled and gated behind FFmpeg.** The Experimental-tab radio is now "Stream audio from the game's EML trailer (no-music games only)" with help text describing extraction, the first-play delay, and the FFmpeg/full-trailer requirements. `IsEnabled` binds to a computed `IsTrailerAudioEnabled` (FFmpeg configured **and** `EnableDefaultMusic` on — consistent with the sibling `DefaultMusicSource` radios), with a "Requires FFmpeg" note shown when it's not configured. The settings-open gate uses a cheap `File.Exists` check (matching the `FfmpegStatus` label) rather than spawning `ffmpeg -version`, so opening Settings never stalls; the runtime path independently re-checks availability and degrades to silence if a present-but-broken FFmpeg fails to extract. `src/UniPlaySongSettingsView.xaml`, `src/UniPlaySongSettingsViewModel.cs`.

### Known Issues

- **System still won't auto-suspend/sleep while Playnite is open** (issue #81, still open — a fix is being attempted). The persistent audio device (v1.3.3 architecture) keeps a Windows audio session active, which blocks sleep (UPS shows under `powercfg /requests` as `[DRIVER]`). The v1.5.3 release triggers — idle-teardown timer (`IdleAudioDeviceTeardownMinutes`), release-on-lock (`OnSessionSwitch`), release-on-suspend (`OnPowerModeChanged`) — did **not** resolve it in the field. Per the reporter's latest testing (2026-06-17), the PC never sleeps in any case (foreground+playing, Win+L locked, or idle/not-foreground), and there is no UPS log evidence that the lock/suspend callbacks fire (his own extension logs `OnPowerModeChanged`; UPS logs nothing for the same events). Suggests the release path isn't actually triggering as designed — under investigation. Workaround: fully exit Playnite when stepping away.

## [1.5.4] - 2026-06-16

Portable-install support: the active-theme music fix that didn't resolve on portable Playnite, plus a settings-tab reorganization for two still-maturing options.

### Fixed

- **Active Theme Music silent on portable Playnite installs** (issue #76, confirmed fixed by the reporter and a theme dev on a portable install). `PlayniteThemeHelper` resolved the theme folder and `fullscreenConfig.json` from hardcoded `%AppData%` / `%LocalAppData%`, but portable installs keep their data folder next to the executable on any drive — so the lookup found nothing and the "Use active theme's UPS audio file" source played silence. New `GetPlayniteDataRoots()` yields `_api.Paths.ConfigurationPath` first (the SDK-provided data root, correct for both portable and installed), then the `%AppData%` / `%LocalAppData%` Playnite folders as fallbacks so the built-in Default theme (historically under `%LocalAppData%`) still resolves for installed users. Both `ResolveActiveThemeDirectory()` and the `fullscreenConfig.json` read route through it; Paths access is null/exception guarded and a `HashSet` dedupes when `ConfigurationPath` equals `%AppData%\Playnite`. `src/Common/PlayniteThemeHelper.cs`.

- **FileLogger fallback log paths hardcoded to `%AppData%\Playnite`.** The two fallback log locations (the extension-folder probe and the final fallback) assumed the installed-mode data root, so on portable installs they pointed at the wrong drive. The `FileLogger` constructor now takes the SDK `ConfigurationPath` (threaded from `UniPlaySong.cs`) and derives both fallbacks from it. The primary log path — the DLL's own directory — was already portable-correct, so only the fallbacks change; `%AppData%` remains the last resort when no SDK path is supplied (e.g. a test harness constructing `FileLogger` directly). `src/Common/FileLogger.cs`, `src/UniPlaySong.cs`.

### Changed

- **"Defer to trailer audio" and "Release Audio When Idle" moved to the Experimental tab.** Both are still being ironed out, so the "Defer to Trailer Audio" default-music option (was Playback) and the "Release Audio When Idle" idle-teardown timer (issue #81, was General → Performance) now live under **Settings → Experimental**. The `IdleAudioDeviceTeardownMinutes` reset moved from `ResetGeneralTab_Click` to `ResetExperimentalTab_Click` to match the control's new home. `src/UniPlaySongSettingsView.xaml`, `src/UniPlaySongSettingsView.xaml.cs`.

- **Trailer option relabeled "Stay silent for games with no music."** The old "Defer to trailer audio when no UPS music is present" label overpromised — UPS can't reach into another plugin to play or unmute its trailer; it only suppresses *its own* music on no-music games so the trailer can be heard uncontested. Help text rewritten to say so plainly. Added `GameMusicFileService.HasTrailerVideo()` (checks for EML's `VideoTrailer.mp4` / `VideoMicrotrailer.mp4`) so the debug log records whether a trailer actually exists for a no-music game — diagnostics only, the silence outcome is unchanged. The EML games path is derived from the SDK `ConfigurationPath` (passed in as `emlGamesPath`) so it resolves on portable installs. `src/Services/GameMusicFileService.cs`, `src/Common/Constants.cs`, `src/Services/MusicPlaybackService.cs`.

## [1.5.3] - 2026-06-14

Two theme-integration features, a new default-music source, and a focus-tracking bug fix.

### Added

- **New theme element `UPS_MusicControl_PauseGamePlayDefault`.** Sibling to the existing `UPS_MusicControl`. Where `UPS_MusicControl` pauses everything when `Tag=True`, this new variant swaps the current game's music out for the user's default music (Bundled Ambient, Random Game, Custom Folder — whatever they picked) and restores game music when `Tag=False`. Theme devs can use it to keep background music playing while the user interacts with a custom panel (tag editor, settings sidebar, property pane) without silence or game-track stutter. Multiple instances stack via OR. The equivalent `{PluginSettings}` binding to `ForceDefaultMusicOverride` works too. See `docs/dev_docs/THEME_INTEGRATION_GUIDE.md` for the new section between Examples 5 and 6.

- **First-install auto-detection of UPS audio in the active fullscreen theme.** On first run, UPS checks whether the active fullscreen theme ships a `UPS_BackgroundAudio.{mp3,ogg,wav,flac}` file. If yes, the default-music source is auto-switched from Bundled Ambient to Active Theme Music so the theme's audio plays out of the box. Conservative: only when the theme dev explicitly added the UPS file (never silently copies from `background.*`), only when the user is still on the factory-default source (existing user choices are respected on upgrade), and the check runs once — never re-fires when the user switches themes later. Only the active theme is scanned; UPS doesn't enumerate other installed themes.

- **New default-music source: "Defer to trailer audio when no UPS music is present"** (issue #77). When picked, UPS stays silent on games with no UPS songs so the game's trailer audio (via ExtraMetadataLoader or any other plugin that plays a trailer) can be heard uncontested. If no trailer is configured or the relevant plugin isn't installed, the user gets silence — UPS doesn't request the trailer to play, it just doesn't compete with it. Minimum-viable, no-coupling-with-EML implementation: UPS doesn't reach into another plugin's visual tree to force a MediaElement to play, which keeps it resilient to EML's internal changes.

### Fixed

- **Music no longer resumes when Playnite's Keyboard Launcher opens** (issue #79). Symptom: with Playnite minimized in the background and another app in the foreground, pressing the Keyboard Launcher's global hotkey would cause UPS music to resume even though Playnite's main window wasn't actually back in the foreground. Root cause: `Application.Activated` (the WPF event UPS listened to) fires when ANY window owned by the app gets focus — including the Keyboard Launcher overlay, which is a sibling window to the main window. UPS now checks the foreground window handle against the main-window handle before clearing the FocusLoss pause source. This is the same window-handle check that the focus-loss verify timer already does on the symmetric path; the bug was the asymmetry. Reported by @darklinkpower with a video reproducer.

- **Windows can now auto-suspend / sleep while Playnite is open** (issue #81, reported by @darklinkpower). Since v1.3.3 UPS has kept its audio device open for the lifetime of the plugin — a deliberate trade-off that removes a ~70ms freeze and audible click on every game-switch song change — but Windows reads that open device as an active audio session and won't enter sleep (visible as Playnite under `[DRIVER]` in `powercfg /requests`). Two complementary fixes ship in v1.5.3:
  - **Idle teardown (default 5 minutes, configurable).** After the configured minutes with no music playing, UPS closes the audio device so Windows can sleep on schedule; the next track transparently reopens it. Set via **Settings → General → Performance → "Release Audio When Idle"** (range 0–60 minutes; 0 keeps the device open at all times, the pre-v1.5.3 behavior). Implemented for both the SDL2 and NAudio backends.
  - **Explicit power-state opt-out.** UPS now calls `SetThreadExecutionState(ES_CONTINUOUS)` so it never contributes an explicit "keep awake" hint of its own. Always on, no configuration, no downside.

## [1.5.2] - 2026-06-06

Point release covering two distinct issues: a rework of the Active Theme Music option after a reported file-handle-contention bug class, and a fix for the External Audio detector treating a launched game's own audio as an external source — which combined with `KeepPausedAfterExternalAudio` could leave music stuck in a paused state after game exit.

### Fixed

- **Music stuck paused after exiting a windowed game with `KeepPausedAfterExternalAudio` on.** Reported with log evidence by a user playing windowed (not fullscreen) games. Root cause: the External Audio detector ([src/UniPlaySong.cs:2022-2040](src/UniPlaySong.cs#L2022-L2040)) doesn't know anything about game state — it just polls system audio sessions for non-UPS audio output and triggers `PauseSource.ExternalAudio` when it sees any. When a user launched a game while the music was playing, the game's own audio output was correctly identified as "external" (UPS can't tell the difference between Discord and a game), and `ExternalAudio` was stacked on top of `GameStarting`. With windowed games this hit a particularly bad loop: game audio oscillates (brief silence between sound effects → detector flips `_externalAudioDetected = false` → silence ends → flips back to `true` again), and each oscillation triggered the `KeepPausedAfterExternalAudio` sticky-pause early-return. Result: at game-stop time, `RemovePauseSource(GameStarting)` ran but `ExternalAudio` was still in the set, so music never resumed until the user clicked play manually. v1.5.2 adds a single-line gate at the External Audio "detected" branch: if `GameStarting` is currently active, skip adding `ExternalAudio` (the audio is almost certainly the game itself, not a real external source). Gate uses a new `IMusicPlaybackService.HasPauseSource()` peek method ([src/Services/IMusicPlaybackService.cs:26](src/Services/IMusicPlaybackService.cs#L26) + one-line implementation in `MusicPlaybackService.cs`). Doesn't change behavior when no game is running — Discord, Spotify, Zoom calls, etc. still pause UPS the way they always did. Doesn't change behavior when `KeepPausedAfterExternalAudio` is off.

### Changed

- **Active Theme Music source reworked: strict `UPS_BackgroundAudio.{mp3,ogg,wav,flac}` filename, no fallback to `background.*`.** The pre-v1.5.2 implementation scanned the active fullscreen theme's `audio/` folder for `background.{mp3,ogg,wav,flac}` — the *same* file Playnite's built-in SDL player opens in fullscreen mode. Two players, one file handle → file-handle contention, partial reads, looping artifacts, silence, or in the reported reproducer a 4× / second EOF-loop on Aniki ReMake's intentionally-short 0.16s stub. v1.5.2 changes the convention: UPS now ONLY reads `UPS_BackgroundAudio.{mp3,ogg,wav,flac}` from the theme's `audio/` folder. No fallback to `background.*`. Theme developers must add the UPS-named file for the option to work. The radio button label changed to "Use active theme's UPS audio file (advanced — theme support required)". See `docs/dev_docs/THEME_INTEGRATION_GUIDE.md` for the updated theme-dev guidance. Code: `src/Common/PlayniteThemeHelper.cs` reworked end-to-end (`FindActiveThemeUpsAudioFile`, `GetActiveThemeStatus`, `CreateUpsAudioFromBackground`, `InvalidateCache`); call sites updated in `src/Services/MusicPlaybackService.cs:474-482` and `:811-826`.

- **New: four-state Settings UI panel for Active Theme UPS Audio.** The radio button now drives a visible status panel beneath it that reflects what UPS finds in the active theme: **Ready** (`UPS_BackgroundAudio.*` found — ✓ Detected), **CanBeCreated** (no UPS file but `background.*` exists — shows a one-click button to copy `background.{ext}` → `UPS_BackgroundAudio.{ext}` in the same folder, preserving extension), **Unsupported** (neither file present — ✗ "ask theme dev to add UPS_BackgroundAudio.*"), or **NotApplicable** (Desktop-only / theme lookup failed — ℹ informational). Status is recomputed on settings-tab open and after a successful copy. New ViewModel surface: `ActiveThemeStatus`, four `IsActiveTheme*` booleans, `ActiveThemeCopyResultMessage`, `CreateUpsAudioFromBackgroundCommand`. New converter: `src/Common/NullToVisibilityConverter.cs`.

- **"Compatibility: Pause on Play (Splash Screen Mode)" checkbox renamed to "Pause on Game Launch".** Same setting, same on/off semantic (pre-1.5.2 behavior preserved). The old label made it sound like a niche Splash Screen-specific compatibility flag, which led some users to disable it without realizing they were also disabling the "stop UPS music when a game launches" behavior entirely — there is no other code path that pauses UPS at game launch. Description rewritten to make the consequence explicit, plus a red warning line: *"Keep this ON for normal UPS behavior. Disabling causes UPS music to keep playing in the background while your game is running. Only disable if you're using UPS in Radio Mode to play random library music over your game while the game's own audio is muted."* The Radio Mode use case is the only legitimate reason to turn this off.

- **Legacy `NativeTheme` default music source paths fully retired.** v1.5.0 deprecated this source (audio overlap with Playnite's own player) and added a silent on-load migration to `BundledPreset`. v1.5.2 takes the remaining cleanup step: the legacy `case DefaultMusicSource.NativeTheme:` handlers in `MusicPlaybackService.cs` no longer probe `background.{ext}` — they're now safe no-ops in case hand-edited settings bypass the migration. The `_nativeMusicPath` field is removed. `SettingsService.ValidateNativeMusicFile` collapsed from ~75 lines to a 6-line silent legacy-flag cleanup. The migration on `UniPlaySong.cs:1744-1748` still runs every startup so nobody actually hits the no-op paths.

### Notes for Theme Developers

If your theme historically shipped `background.mp3` for the v1.5.1-and-earlier Active Theme Music feature, the simplest migration is to ship the same audio twice — once as `background.mp3` (for Playnite's built-in player) and once as `UPS_BackgroundAudio.mp3` (for v1.5.2+ UPS users). The two can be identical bytes, or different tracks entirely; your call. Or rely on your users clicking the new copy-helper button in Settings → Playback. See `docs/dev_docs/THEME_INTEGRATION_GUIDE.md` for the full rewrite.

## [1.5.1] - 2026-05-25

Point release focused on a reported game-stop pause-source bug, defensive cleanup of long-lived event subscriptions, and a codebase-wide documentation/doc-comment pass per the project style guide.

### Fixed

- **Music silent after game exit until selection change.** Reported by users running `PauseOnFocusLoss` (often alongside `PauseOnMinimize` / `PauseWhenInSystemTray`). When a game exited via Steam BPM, a fullscreen-borderless launcher, a sound-driver hitch, or any path where focus didn't cleanly return to Playnite, the `FocusLoss` (or `Minimized` / `SystemTray`) pause source could survive past game stop because `OnApplicationActivate` never fired to remove it. Music stayed paused until the user changed selection (which doesn't clear window-state sources via `ClearAllPauseSources` — they're preserved — but does eventually nudge the set clean by other means) or restarted Playnite. `OnGameStopped` in [src/UniPlaySong.cs:487](src/UniPlaySong.cs#L487) now defers a re-poll of window state to the next UI tick via `Dispatcher.BeginInvoke(Background)` and drops any window-state pause source whose corresponding condition (`window.IsActive` / `window.IsVisible` / `WindowState != Minimized`) currently says playback should be allowed. Idempotent and safe: removing a pause source that isn't in the set is a no-op, and we never override genuine user state (if Playnite IS still minimized at game-stop time, `Minimized` stays). Doesn't touch the focus-tracking subsystem itself — the activate/deactivate/verify-timer race the user described remains, but the bug it produces no longer survives the next game stop.

- **Event-handler leak in `MusicControl` (top panel control).** `OnUnloaded` removed the control from the static list but never unsubscribed `_settings.PropertyChanged -= OnSettingsChanged`. Each WPF tree rebuild (theme switch, fullscreen↔desktop mode swap, sidebar collapse/expand) leaked one settings handler. Over a long session this stacked to dozens of duplicate `PropertyChanged` fires per setting change. Fix at [src/Controls/MusicControl.xaml.cs:52-58](src/Controls/MusicControl.xaml.cs#L52-L58).

- **Stacked `Ended` handlers in `GmePreviewPlayer`.** If `Play()` was called twice on the same instance without a `Stop()` in between (rare but possible during rapid preview-button mashing), the `_sampleProvider.Ended` subscription would stack and fire `TrackEnded` multiple times per track end. Defensive `-=` before `+=` at [src/Audio/GmePreviewPlayer.cs:83-86](src/Audio/GmePreviewPlayer.cs#L83-L86).

### Performance

- **`DownloadManager.FindBestAlbumMatch` single-pass source bucketing.** Three sequential `albumsList.Where(a => a.Source == ...).ToList()` passes replaced with a single `foreach` switch into three pre-allocated lists. Saves ~5-15ms per album search on libraries with ~100+ candidate albums; reduces transient `List<Album>` allocations. [src/Downloaders/DownloadManager.cs:467-481](src/Downloaders/DownloadManager.cs#L467-L481).

- **`ExtractSeriesNumbers` regex compile removed.** Replaced `Regex.IsMatch(k, @"^\d{1,2}$") && int.Parse(k) <= 20` with a single `int.TryParse + range check + length check`. Saves ~200µs per call on the hot search path. [src/Downloaders/DownloadManager.cs:574-578](src/Downloaders/DownloadManager.cs#L574-L578).

### Changed

- **Documentation comments simplified across the codebase per `CLAUDE.md` style.** XML `/// <summary>` blocks removed where the method/property/enum-value name was self-documenting; converted to inline `//` lines where the comment carried real information; preserved on public APIs that legitimately need `<param>` / `<returns>` / `<exception>` tags. Touched files: `src/Models/Source.cs`, `src/Models/NormalizationSettings.cs`, `src/Services/GameMusicFileService.cs`, `src/Services/MusicPlayer.cs`, `src/Services/DownloadDialogService.cs`, `src/Services/BatchDownloadService.cs`, `src/Handlers/AmplifyDialogHandler.cs`, `src/Handlers/TrimDialogHandler.cs`, `src/UniPlaySongSettings.cs` (EffectChainPreset enum). No behavior changes.

## [1.5.0] - 2026-05-18

> Detailed release notes (full feature breakdown, module structure, upgrade notes) live in `docs/release_notes/v1.5.0.md`.

Quality-of-life and integration release. Four user-visible features land in this version: a per-game music dashboard with stylized backdrop and color theming, a smooth one-click "calm down" mode for ambient music, a randomize-on-startup option for bundled tracks, and a portable Settings Backup tab for moving your UPS config between machines.

### Added

- **Music Info Card** (right-click a game → Music Info Card) — a per-game stats dialog summarizing the game's music library at a glance: file count, total duration, on-disk size, longest/shortest track, average bitrate, and a sortable format breakdown. Includes a scrollable alphabetical song list with format chip, title, file size, and duration columns. Playlist-aware: HES files with M3U sidecars expand into per-track entries (each M3U track is its own row with a distinct chip), and playlist files contribute to the longest/shortest comparison at the per-track level. Available in both Desktop (right-click menu) and Fullscreen (controller-navigable, B/Back to close) flavors. Dialog opens immediately; metadata reads happen async via `Task.Run` so large folders don't block the UI. Cancels in-flight reads cleanly when the dialog closes. **Visual polish**: the game's icon shows beside the title with a soft `DropShadowEffect` glow tinted to the icon's dominant color (extracted by the same `IconColorExtractor` used in the existing IconGlow feature). A top accent strip, format-chip colors, and a faint card-wide tint layer all inherit the same per-game color so each card feels visually tied to its game. The dialog uses a raw transparent WPF Window (same chrome-less pattern as the toast notifications) with the game's `BackgroundImage` heavily blurred at radius 45 underneath a dark veil for atmosphere without sacrificing text readability. Custom titlebar with drag-to-move + close glyph; ESC also closes. Self-contained module under `src/Features/MusicInfoCard/` — Models, Services (`IMusicStatsProvider` + `MusicStatsService`), Views (Desktop + Fullscreen `UserControl`s), and the public `MusicInfoCardHandler` entry point. Two outside-of-module touchpoints in the rest of the plugin: the handler construction in `UniPlaySong.cs` and the right-click menu wiring.

- **Calm Down Mode** (Fullscreen Extensions menu → Calm Down Mode) — applies a low-pass filter (1500 Hz cutoff) and 0.5× volume attenuation to the live audio output, fading in/out over 1.5 seconds with a smooth S-curve. Useful for late-night browsing when the user wants ambient music gently dialed back without stopping it. Implementation lives on the persistent NAudio mixer chain (between `_mixer` and `_volumeProvider`), so it survives song switches, crossfades, and game-music/default-music handoffs. New `CalmDownProcessor : ISampleProvider` polls `CalmDownModeEnabled` every audio `Read()` call — toggling the setting arms an internal S-curve ramp, no song restart or coordinator plumbing required. Independent of `LiveEffectsEnabled` (sits downstream of the per-song `EffectsChain`). Forces the NAudio backend when toggled on; `CreateMusicPlayer` + the diff-event handler in `OnSettingsServiceChanged` handle the SDL2→NAudio promotion automatically. Four tuning knobs (`CalmDownLowPassCutoffHz=1500f`, `CalmDownVolumeMultiplier=0.5f`, `CalmDownFadeLengthMultiplier=2.0f`, `CalmDownTransitionDurationSeconds=1.5f`) live as settings backing fields for power-user tweaking via the settings JSON.

- **Calm Down Mode as a `{PluginSettings}` theme binding.** Theme authors (Aniki ReMake etc.) can drop a `CheckBoxEx` into their quick-options panel bound to `Path=CalmDownModeEnabled, Mode=TwoWay`. New per-property handler in `OnSettingsServicePropertyChanged` bridges theme writes to the backend-swap decision so SDL2→NAudio promotion fires correctly from both lanes (desktop dialog and `{PluginSettings}` markup). `THEME_INTEGRATION_GUIDE.md` updated with the binding table entry, the example block, and the Aniki paste-in.

- **Randomize bundled track every startup** (Settings → Playback → Bundled Ambient) — when enabled, UPS picks a random bundled preset once at Playnite startup and uses it for the entire session. Game-switches keep the same ambient track for consistency; each new Playnite session rolls a fresh pick so users get variety across sessions without the chaos of per-game-switch re-randomization. The manual preset picker is greyed out while randomization is on so it's clear the manual selection is being ignored. New `RandomizeBundledTrackOnStartup` setting (default false). New `BundledPresetService.GetEffectivePresetFilename(settings)` accessor centralizes the manual-vs-random decision for all consumers.

- **Settings Backup tab** (Settings → Backup) — export your UPS configuration to a portable file and re-import on another machine or after a Playnite reinstall. Two formats:
  - **JSON Export / Import** — portable, re-importable backup. Excludes machine-specific values (tool paths for yt-dlp / FFmpeg, custom default music file/folder paths, custom cookies file, Custom Rotation game IDs) so imports don't overwrite your local-only configuration. Includes a `_meta` header with the source UPS version; imports from a different version prompt a confirm dialog. Same `JsonConvert.SerializeObject` + `PopulateObject` round-trip used by the existing global-reset path.
  - **Markdown Snapshot** — one-way human-readable snapshot for sharing in GitHub issues, Discord support channels, or personal notes. Includes derived stats (total games tracked, games with music, total music storage), tool-path validation status (✓ Found / ✗ Not Found), and a "Diff from defaults" table built via reflection. User-specific path prefixes sanitized to environment placeholders (`%AppData%`, `%UserProfile%`, etc.) so snapshots can be pasted into public discussions without leaking the user's Windows username. Sensitive fields like API keys would be redacted as `*****` (mechanism in place, list grows as features land).
  - Both exports use Playnite SDK file dialogs (`Dialogs.SaveFile`, `Dialogs.SelectFile`) for consistent UI theming.
  - New `src/Services/SettingsBackupService.cs` (static class, ~400 lines) + new Backup tab in `UniPlaySongSettingsView.xaml` + click handlers in the code-behind. Two new public methods on the plugin (`ApplyImportedSettings`, `GetSnapshotStats`).

### Fixed

- **Default music no longer wiped by transient null-selection events** — Fullscreen filter-preset switches, Aniki ReMake tab changes, and Solaris filter buttons all briefly empty Playnite's `SelectedGames`. UPS's `HandleGameSelected(null)` path used to unconditionally call `PlayGameMusic(null)` which fired `FadeOutAndStop` and wiped `_isPlayingDefaultMusic` / `_lastDefaultMusicPath` — destroying in-flight default music that should have been session-persistent. The same null-call also triggered an audible "plays for a second, stops, tries to play again" loop when Playnite re-emitted the selection a beat later. Two-layer guard added: (1) coordinator at `MusicPlaybackCoordinator.cs:158-185` checks `IsPlayingDefaultMusic` + `EnableDefaultMusic==true` and treats null-selection as a no-op when default music should persist; (2) defense-in-depth at `MusicPlaybackService.cs:553-571` so any future caller bypassing the coordinator also gets the guard. Fixes "music stops on custom filter preset," "Aniki tab switch restarts music," "Recent Games preset stops default music" reports.

- **Default music now respects active pause sources during fresh load** — when `EnableMusic=off + EnableDefaultMusic=on` and the user navigated from a theme overlay (welcome hub / login screen) directly to a game card, UPS would load default music and immediately `Play()` + `FadeIn()` even though `ThemeOverlay` was still an active pause source. Result: default music played audibly over the welcome hub. The `ResumeDefaultMusic` path at `MusicPlaybackService.cs:547-571` now checks `_activePauseSources` (excluding the about-to-be-removed `DefaultMusicPreservation` source) before kicking off playback. When other sources are active, the file is loaded but Play/FadeIn is skipped — the existing `RemovePauseSource` resume path at line 280-289 (`IsLoaded && !IsActive` → start with fade-in) picks the music up cleanly once the overlay dismisses. Harmonizes with the symmetric path at line 1055 which already had this gate.

- **Randomize bundled track every startup** — three reliability fixes: (1) toggling the flag now actually triggers a default-music reload (previously missing from the change detector); (2) toggling OFF → ON clears the cached session pick so a fresh random preset rolls (previously reused the stale pick); (3) anti-repeat — the previous session's pick is excluded from the candidate pool when the bundled-preset pool has more than one preset, so consecutive Playnite restarts never hear the same random preset twice in a row. `Random` is now seeded from `Guid.NewGuid().GetHashCode()` to avoid `Environment.TickCount` collision on fast restarts. New `LastRandomizedBundledPreset` setting + `BundledPresetService.SetPersistSettingsCallback` so the anti-repeat memory survives Playnite restart.

### Changed

- **"Use Playnite native theme music" default source deprecated; replaced by bundled "Shades of Orange" preset.** The native-theme source caused audible overlap because UPS and Playnite's built-in audio player both tried to play the same `background.mp3` file simultaneously (especially noticeable on the welcome hub / login screen). v1.5.0 ships "Shades of Orange" by Dave Miles (the same track Playnite's default Fullscreen theme uses, sourced from Zapsplat under their Standard License with attribution) as a bundled preset, mirroring the pattern Crowfeather/Playnite already uses publicly. Users who previously had `DefaultMusicSourceOption == NativeTheme` are silently migrated to `BundledPreset` on settings load (their `SelectedBundledPreset` is left untouched; if blank, falls through to the existing validation block). The `NativeTheme` enum value is marked `[Obsolete]` but the ordinal is preserved so existing serialized settings still deserialize cleanly. The "Use Playnite native theme music" radio button is removed from the Settings UI. The legacy switch cases in `MusicPlaybackService` are kept as safety nets (the migration runs on every startup, so they should never fire in practice). Full attribution: see `NOTICES.txt`. In-plugin credit surfaced via Settings → About → Bundled Music Credits.

### Known Issues

- **Toast notification blur effect broken on Windows 11 (recent update)** — Windows 11 systems no longer see toast notifications with the proper acrylic/blur backdrop. Toasts render with a flat colored tint instead of the frosted-glass effect. Root cause is Microsoft's progressive deprecation of the `SetWindowCompositionAttribute` Win32 API across recent Win11 builds (23H2/24H2/25H2) — the API call still returns success at the OS level but the OS no longer honors it visually. **Windows 10 users are unaffected** and continue to see the proper acrylic blur. A future v1.5.x patch will reimplement the toast pipeline against the newer `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` API to restore real Mica/Acrylic on Win11.

## [1.4.6] - 2026-05-09

> Detailed release notes (test scenarios, full bug context) live in `docs/release_notes/v1.4.6-beta1.md`. Theme integration deep-dive in `docs/dev_docs/THEME_INTEGRATION_GUIDE.md` and `docs/dev_docs/TECHNICAL_REFERENCE.md`.

### Added
- **Theme integration via `{PluginSettings}` markup.** Themes can bind UPS settings (`EnableMusic`, `EnableDefaultMusic`, `RadioModeEnabled`, `PlayOnlyOnGameSelect`) directly from XAML — no custom UPS element required, no theme crash if UPS isn't installed. Validated end-to-end against the [Aniki ReMake](https://github.com/Mike-Aniki/Aniki-ReMake) theme. Replaces an earlier reverted approach that added bindable properties to `UPS_MusicControl` (crashed themes with load-order errors). The legacy `UPS_MusicControl` element remains supported for pause/resume bindings.
- **`Enable Game Music` and `Enable Default Music` toggles** in the Fullscreen Extensions menu (Menu → Extensions → UniPlaySong) — paired so users on themes without custom audio bindings can flip both layers from a controller. Toggle only Game Music off to leave ambient running; toggle both off for full silence.
- **Two Bundled Ambient tracks from Mike Aniki** (Hub OST, Login OST) — composer of the Aniki ReMake theme. Included with explicit permission from Mike Aniki for distribution inside UniPlaySong. Pick from Settings → Playback → Default Music Source → Bundled Ambient. Full attribution and permission record in `NOTICES.txt`.
- **HES (TurboGrafx-16 / PC Engine) chiptune support.** Drop a `.hes` + sibling `.m3u` into a game's music folder; UPS auto-advances through every M3U-listed track. Right-click → Chiptunes → "Split HES Tracks" splits a multi-track HES into per-track files for skip/shuffle. New files: `HesM3uParser.cs`, `HesHeaderPatcher.cs`, `HesSplitHandler.cs`.
- **`IMusicPlaybackService.IsInRadioMode`** API — surfaces existing radio-state for consumers (settings handlers, future dashboard / peak-meter UI).

### Fixed
- **Theme `{PluginSettings}` quick-options framework hardened end-to-end against Aniki ReMake.** Audio toggles bound from a theme now react reliably mid-playback, including when no game card is focused, when an overlay clears, or across game switches. Multiple latent settings-routing edge cases addressed across the playback coordinator, playback service, and per-property settings handlers. Architectural rule codified in `TECHNICAL_REFERENCE.md`: settings and game-selection handlers always route through `PlayGameMusic`, never call `Stop()` directly.

### Documentation / Compliance
- **LGPL §6 compliance for bundled GME** — added `NOTICES.txt`, `docs/dev_docs/GME_BUILD.md` (pinned commit + reproducible build), and committed the GME source tarball at `lib/source/gme-source-1815b97.tar.gz`. Bundled `gme.dll` is unmodified upstream at commit `1815b97`; UPS does not maintain a fork.

## [1.4.5] - 2026-04-23

> Detailed release notes (full context, benchmark numbers, before/after logs) live in `docs/release_notes/v1.4.5-beta1.md`.

### Added
- **yt-dlp version display in Settings → Downloads** — passive readout next to the path field (e.g. `✓ Found · v2025.12.08`), cached by `(path, mtime)` so in-place yt-dlp updates re-probe correctly.
- **Fullscreen search-variant buttons** — OST / Soundtrack / Music / Theme buttons in `SimpleControllerDialog`'s album-selection step, controller-navigable via D-Pad Left/Right.

### Performance
- **YouTube downloads ~30-50% faster on average.** Bundle of changes: `--sleep-requests` reduced 1.0s → 0.3s on full downloads, `--sleep-interval 1-3` removed entirely (yt-dlp source verification confirmed no automatic fallback), single-pass MP3 encoding promoted to default (was experimental), `--no-progress` + `--no-mtime` always-on, `--concurrent-fragments` bumped 3 → 4, and pre-flight `.writetest` probe removed. Savings stack to ~5s on a typical full song.
- **Previews ~3s faster.** Dropped `--sleep-requests` and `--prefer-free-formats` on the preview path; bundled into the always-on improvements above.
- **Cookie mode produces audio-only streams (~2x faster downloads).** With Firefox/Chrome cookies + Deno installed, yt-dlp negotiates to fmt 251 (opus audio-only DASH) instead of fmt 18 (mp4 video+audio fallback). Setting tooltip updated to mention the speedup and account-activity tradeoff.
- **YouTube search top 20 instead of top 100.** `YouTubeClient.Search` no longer pages through continuation tokens; quality unchanged because `BestAlbumPick` favors top-ranked results.
- **FFmpeg version-check cached per session.** Was running per-download; now once per binary mtime. Saves ~30-50ms/song after the first.
- **Dropped per-song log noise from extensions.log.** ~70% reduction in YouTube-download log volume per song. New format suffix `[fmt N]` on the success line for diagnostic visibility.
- **Removed dead `DownloaderLogger` infrastructure.** ~200 lines deleted across DownloadManager / YouTubeDownloader / UniPlaySong / Constants. The `downloader.log` file was never being created on user systems.
- **Removed experimental "Faster YouTube previews" setting.** Shipped briefly; removed after confirming cookie mode + Deno gives users the same architectural win automatically. Net deletion of plumbing across 7 files.

### Fixed
- **Duplicate `PerformSearch` calls (5x network requests per album selection)** — re-entry guard added in `DownloadDialogViewModel.PerformSearch` (`if (IsSearching) return`). Cause unclear; symptom-fixed via idempotency.
- **Cookie mode + forced `player_client` override broke downloads for users without Deno.** yt-dlp auto-skips android/ios when cookies are present, collapsing to web-only which needs nsig. Fix: drop the `player_client` override entirely on cookie path; yt-dlp picks its own cookie-compatible defaults (web_safari etc.).
- **yt-dlp version label stale after in-place update.** Cache key extended to `(path, mtime)` so replacing `yt-dlp.exe` and re-Browsing the same path triggers a fresh `--version` probe.
- **Rename-fallback's pattern-match pass deleted the file the explicit-extension pass just moved into place.** Surfaced as `FileNotFoundException` whenever yt-dlp produced an extension other than `.mp3`. Guard added so the second pass only runs if the first didn't already produce `path`.
- **"Open Music Folder" spawned a new explorer.exe per click and leaked process handles.** New `Common/ShellHelper.OpenFolderInExplorer` centralizes the 8 call sites; uses `ShellExecuteEx` and disposes the `Process` handle immediately.
- **Back button closed the download dialog mid-download.** BACK now disabled while `IsDownloading`; re-enables on completion.
- **No clean exit after download.** New FINISH button appears once a download succeeds; closes with `DialogResult = true`.

## [1.4.4] - In development

### Added
- **SNES `.spc` advertised as a supported format** — already worked end-to-end via `GmeReader`/libgme; About tab and README now list it.

### Fixed
- **Desktop YouTube preview failed on machines with aggressive `%TEMP%` scanning** — Desktop preview path was writing yt-dlp `.part` files to `%TEMP%`, where Defender quarantined them mid-download. Switched to the same `%AppData%\Roaming\Playnite\ExtraMetadata\UniPlaySong\temp` path Fullscreen already used.
- **yt-dlp preview errors silently suppressed** — preview path was passing `--no-warnings --quiet --no-progress`, hiding bot-detection / missing-JS-runtime / DLL-load errors. Flags removed so stderr surfaces for the error-classification block downstream.
- **Download error dialog pointed to the wrong log** — now explicitly directs users to `%AppData%\Playnite\extensions.log` (where `YouTubeDownloader` actually logs) and lists the three usual failure causes.

## [1.4.3] - 2026-04-19

### Added
- **NSF Track Manager (Desktop)** — right-click game → `Chiptunes → NSF Management`. Splits a multi-track `.nsf` into individual mini-NSFs, with optional preservation to `PreservedOriginals/`. Includes a per-track preview player.
- **NSF Track Manager: Edit Loops tab** — set a per-`.nsf` loop-length override (5–600s) when GME's 150s fallback loops short BGM tracks too many times. Persisted as `nsf-loops.json` in the game's music folder.
- **True Crossfade Mode** — opt-in DJ-style overlap on auto-advance transitions (Radio / pool-based default / RandomizeOnMusicEnd). 1–10s window, default 9s. Forces NAudio backend (SDL2's single music channel can't overlap).
- **Automatic Playback on First Launch (Desktop) setting** — when off, music waits for the user's first manual Play. Once unlocked, stays unlocked for the session (resets on Playnite restart).
- **About tab: `.nsf` listed under supported chiptune formats.**

### Fixed
- **NSF mini-files all played track 0** — `GmeReader` now honors the `starting_song` byte at offset 7 of the NSF header. `NsfHeaderPatcher` preserves the original `total_songs` byte (was being overwritten to 1, breaking GME's track index).
- **Short NSF tracks looped for 2:30 instead of advancing** — `GmeReader.Read()` now checks `gme_track_ended()` and short-circuits EOF when the chip emulator signals end. 2-second minimum guards against false-EOF during chip init.
- **Dialog showed "2:30" for every NSF track** — GME returns 150000ms as its own default for unknown lengths; `NsfTrackRow.DurationDisplay` now shows "—" for that sentinel.
- **Auto-play lock re-engaged after toggling Live Effects / Visualizer / True Crossfade** — `RecreateMusicPlayerForLiveEffects` now snapshots the old service's `UserHasManuallyStartedThisSession` flag onto the new service.
- **Song-end fade didn't re-arm after auto-advance** — `OnMediaEnded`'s auto-advance branches now call `MarkSongStart()` after `LoadAndPlayFile` so the fade timer is rescheduled.

### Changed
- **`NsfPreview` pause source defensively cleared on game switch** — guards against the dialog leaking the source on host-window force-close.
- **Play Music State dropdown** renamed `Desktop`/`Fullscreen` → `Desktop Only`/`Fullscreen Only` for clarity. Description now guides users to disable Desktop auto-playback by selecting "Fullscreen Only".
- **YouTube download diagnostic for PyInstaller DLL load failures** — `YouTubeDownloader` now recognizes `[PYI-*:ERROR] Failed to load Python DLL` and logs the fix: delete the `_internal` folder and re-download the SINGLE-FILE `yt-dlp.exe` (not the zip distribution).

## [1.4.2] - 2026-04-18

### Added
- **Fullscreen Volume Boost setting** — opt-in 0–20% nudge to compensate for Playnite's `BackgroundVolume` slider stacking multiplicatively with `MusicVolume` in Fullscreen. Desktop unaffected.
- **Fullscreen Quick Settings Menu** — Menu → Extensions → UniPlaySong surfaces 5 toggles (Live Effects, Radio Mode, Preview Mode, Play Only on Game Select, Music Only for Installed Games) and 2 sub-menus (Reverb Preset, Default Music Source) as controller-navigable items in Fullscreen.
- **Stay Paused After External Audio (Desktop) setting** — when enabled, UPS won't auto-resume after silence is detected; user must manually resume.

### Fixed
- **~1s of game music played during Fullscreen exit** — late theme-state events fired after `OnApplicationStopped` were routed through `HandleThemeOverlayChange` and started a fresh fade-in. Added `_isShuttingDown` flag; teardown-time events now early-return.

### Changed
- **Persistent default-music track on game switch is now the default** for fresh installs (`DefaultMusicContinueSameSong = true`). Existing configs preserved.
- **New `RandomizeDefaultMusicOnEnd` setting** — when disabled, the current default track loops at EOF instead of rolling a new pool pick. Orthogonal to game-music's auto-advance.
- **Fullscreen Quick Settings Menu** items prefixed with `[UPS]` for clarity, and settings now persist to disk via `SavePluginSettings(clone)` (was in-memory only).

## [1.4.1] - 2026-04-18

### Fixed
- **Play Only on Game Select didn't randomize on first song** — `PlayGameMusic` was updating `_currentGameId` during the List-view prep call, so the follow-up Details-view call saw `isNewGame == false` and skipped randomization. Prep call now skips the gameId update; repeat Details-view entries also force-randomize via a new `forceRandomize` flag.
- **Single-track looping went silent with FadeOutBeforeSongEnd enabled** — fade-out ramp continued running on the audio thread after the loop restart, parking volume at 0. Looping branch now cancels the stale fade and applies a fresh fade-in.
- **GME Pause/Resume: UI freezes, silent music, and stuck audio after jingles** — `libgme` isn't thread-safe; the muted-keep-pulling-samples pause approach forced `gme_seek` on resume, blocking the UI for 6–8s. Three-layer fix: `_gmeLock` serializes native calls, `Pause()` detaches the mixer input for `GmeReader` (freezing the emu in place), `Resume()` has a fast-path skipping the seek when within 100ms. `IMusicPlayer.Resume` gained an `onReady` callback so the fader defers `FadeIn` until audio is actually flowing.
- **Song-end fade leaves silent gap before next song** — fade-out ramp duration defaulted to the 0.3s game-switch fade rather than the 3s pre-EOF window, so volume hit 0 with 2.7s of silence remaining. `MusicFader.FadeOut` now accepts a ramp-length override; `ScheduleSongEndFade` passes its own duration.

### Added
- **Fanfare also celebrates "Beaten" status** — new `CelebrateBeaten` toggle (off by default) lets the completion fanfare fire on "Beaten" in addition to "Completed."
- **Abandoned status jingle + toast** — parallel pipeline to the celebration one, with 10 bundled abandoned-category jingles (Mortal Kombat fatalities, Streets of Rage Game Over, etc.), a muted-palette toast (Tombstone / DuskBlue / Rust / etc.), and a customizable message template.

### Changed
- **Play Only on Game Select uses `OnFullscreenViewChanged` event** instead of a 200ms polling DispatcherTimer. Net −14 lines.
- **Jingle playback extracted to `JingleService`** — `PlayCelebrationSound` and `PlayAbandonedSound` collapsed into a single event-driven dispatcher. `UniPlaySong.cs` shrinks ~100 lines.

### Documentation
- **About Tab: Supported Formats section** added.
- **GME chip-support boundary** documented in `SUPPORTED_FILE_FORMATS.md` (most VGMRips packs decode to silence; broader coverage deferred).

## [1.4.0] - 2026-04-16

### Added
- **Retro Game Music Support (GME)** — UniPlaySong now plays `.vgm` chiptune files (Sega Genesis / Mega Drive) via [Game Music Emu](https://github.com/libgme/game-music-emu). New files: `src/Audio/GmeNative.cs` (P/Invoke layer, 11 `gme_*` functions + helpers), `src/Audio/GmeReader.cs` (WaveStream + ISampleProvider, int16→float32 with 1.5x gain boost). Extension registered in `Constants.cs` and dispatched in `NAudioMusicPlayer.CreateAudioReader()`. Format-aware backend switch: `MusicPlaybackService.LoadAndPlayFileFrom()` raises `OnNeedsPlayerSwitch` when a `.vgm` is encountered on SDL2; `UniPlaySong.HandlePlayerSwitchForFormat()` recreates the player as NAudio using the same pattern as `RecreateMusicPlayerForLiveEffects()`. Native DLLs (`gme.dll` 221 KB LGPL, `z.dll` 77 KB zlib) built as x86 and bundled in `lib/`; `scripts/package_extension.ps1` copies both into the `.pext`. Verified end-to-end on `.vgm` with metadata parsing, YM2612+PSG output, and track transitions.
- **Faster YouTube Previews** — Preview downloads now use `--download-sections "*0:00-0:40"` to download only the first 40 seconds at the protocol level, instead of downloading the full track and trimming with FFmpeg. Reduces bandwidth and wait times.
- **Browser Cookie Support** — Added Chrome, Edge, Brave, and Opera as cookie source options alongside Firefox. All use yt-dlp's `--cookies-from-browser` with the corresponding browser name. `CookieMode` enum expanded; `YouTubeDownloader` uses a switch for browser-to-argument mapping.

### Fixed
- **GME Silent Next-Track** — Removed GME's internal fade (`gme_set_fade`) which overlapped with NAudio's `ScheduleSongEndFade` fader and could leave the next song stuck at volume 0. `GmeReader.Read()` now signals EOF via position tracking (`gme_tell >= play_length`) and lets NAudio's fader own all fade transitions.
- **GME x86 Build** — Rebuilt both `gme.dll` and `z.dll` as 32-bit (Win32) after initial x64 build caused `BadImageFormatException` in Playnite (which runs as a 32-bit process).
- **Source Downloads Consistency** — Cookie mode was missing `--audio-quality 0` (defaulting to 128kbps), `--no-playlist`, and `--extractor-args`. All download modes (no cookies, browser cookies, custom file) now use identical yt-dlp arguments.

### Documentation & Licensing
- **License Attribution: GME + zlib** — `LICENSE` updated with Game Music Emu (LGPL v2.1+, dynamic linking via P/Invoke) and zlib attributions for the new bundled native libraries.
- **Bundled vs External Tools** — `LICENSE`, `README.md` Credits, and `DEPENDENCIES.md` now distinguish redistributed libraries from user-installed tools (yt-dlp, FFmpeg, Deno).
- **New Dev Doc** — Added `docs/dev_docs/SUPPORTED_FILE_FORMATS.md` summarizing supported audio formats with verification status per format.

## [1.3.12] - 2026-04-13

### Fixed
- **Normalization Codec Mismatch** — Normalization always used `libmp3lame` regardless of input format, encoding MP3 data into `.ogg`/`.flac`/`.wav` files. Added `ResolveCodecArgs()` to auto-detect the correct codec from the input file extension. Default changed from `libmp3lame` to `auto`. Added high-quality VBR settings (`-q:a 0` for MP3, `-q:a 6` for OGG) instead of FFmpeg's 128kbps CBR default. Settings UI changed from free-text to dropdown (Auto, MP3, OGG Vorbis, FLAC, WAV).
- **Silence Trim OGG Support** — `AudioTrimService` was missing OGG codec mapping (fell through to `-c:a copy`). Added `-c:a libvorbis -q:a 6` for `.ogg` files, matching the pattern used by all other audio services.
- **External Audio Pause Detection** — "Pause on External Audio" silently failed due to an `InvalidCastException` when creating NAudio's `MMDeviceEnumerator` on a ThreadPool (MTA) thread. Replaced with `AudioSessionDetector` — a direct WASAPI COM interop implementation that bypasses NAudio's COM wrapper entirely.
- **Radio Mode Login/Welcome Screen Bypass** — Radio Mode ignored theme overlay pauses and played immediately on fullscreen startup. Two fixes: (1) `ClearAllPauseSources()` now preserves `ThemeOverlay` so `HandleGameSelected` → `Stop()` doesn't wipe the theme's pause request. (2) The direct `StartRadioPlayback()` call in `OnApplicationStarted` now checks `IsPaused` before starting, respecting active theme overlays.
- **Radio Mode + Installed Games** — Radio Mode and `MusicOnlyForInstalledGames` now work together properly. Installed games play their own music, uninstalled games resume radio from the full pool. `StartRadioPlayback()` now uses `_fader.Switch()` when music is already playing for smooth transitions.

- **Fullscreen Download Dialog Back Navigation (fix attempt)** — Pressing B to go back from song selection to album selection no longer re-searches the network. Uses cached album results instead, preventing misleading "Invalid album selection" error messages. Minor visual/display fix — download functionality is unaffected.

### Changed
- **Theme Compatible Login Skip** — Updated description to clarify this option is only needed for fullscreen themes that don't natively support UPS via `UPS_MusicControl`. Themes with built-in integration (like ANIKI REMAKE) handle login/welcome screen pausing automatically.
- **Tool Path Validation** — yt-dlp and FFmpeg path settings now show validation status ("✓ Found" / "✗ Not found") on settings open. Descriptions updated to clarify paths must point to the actual `.exe` files. yt-dlp description notes that `deno.exe` should be placed in the same folder for YouTube support.

## [1.3.11] - 2026-04-11

### Added
- **Active Theme Music Source** — New default music option: "Use active fullscreen theme's background music (If Available)." Detects the currently active fullscreen theme and plays its bundled `audio/background.mp3` through UPS's own audio pipeline with fade-in, volume control, and proper suppression. Works with themes that use Playnite's standard `background.mp3` convention (e.g. Solaris, Playnite Default). Themes that manage audio through other means (e.g. ANIKI REMAKE) are not supported. Theme ID resolved via reflection on `FullscreenSettingsAPI` with fallback to `fullscreenConfig.json` parsing. Handles both user-installed themes and the built-in default theme.
- **Add Music File** — New menu option to browse and add local audio files to any game. Desktop mode: standard `OpenFileDialog` filtered to `.mp3/.wav/.ogg/.flac`. Fullscreen mode: Material Design controller-friendly file browser (`ControllerAddMusicDialog`) with predefined folder locations (Downloads, Music, Desktop, Documents, current game folder), subfolder navigation, file size display, and full D-pad/shoulder/trigger support. File is copied (not moved) to the game's music directory with duplicate name handling.

### Fixed
- **Skip First Selection Double-Skip** — "Do not play music on startup (Fullscreen)" previously required two game selections instead of one. The skip state was being reset redundantly in `OnApplicationStarted` after already being consumed by the initial auto-select, causing the user's first manual selection to be skipped as well.

### Changed
- **Playnite SDK** — Updated from 6.15.0 to 6.16.0 (Playnite 10.52).

## [1.3.10] - 2026-03-29

### Added
- **External Control via URI Handler** — New `ExternalControlService` enables external tools (Stream Deck, AutoHotkey, PowerShell, desktop shortcuts) to control playback via Playnite's `playnite://uniplaysong/` URI protocol. Supported commands: `play`, `pause`, `playpausetoggle`, `skip`, `restart`, `stop`, `volume/{0-100}`. Invalid commands show Playnite notifications; valid commands execute silently.
- **Bulk Audio Format Conversion** — New `AudioConversionService` + `ConversionDialogHandler` for converting all music files to OGG or MP3 at selectable bitrate (128/192/256 kbps). Parallel processing (up to 3 workers). Temp file pattern (`.converting`) protects originals during FFmpeg execution. Optional backup with `-preconvert` suffix. Completion report shows converted/failed counts and space saved. Located in Settings > Editing > Bulk Actions > Format Conversion.

### Fixed
- **NAudio Restart Song** — `NAudioMusicPlayer.Play(TimeSpan.Zero)` silently skipped seeking to position zero because the guard `startFrom > TimeSpan.Zero` excluded `TimeSpan.Zero` (which equals `default(TimeSpan)`). Songs appeared to restart but continued from the current position. Now always sets `_audioFile.CurrentTime` regardless of the `startFrom` value. Affects any code path calling `RestartCurrentSong()` with Live Effects or Visualizer enabled.
- **NAudio Instant Pause Not Silencing** — `AddPauseSourceImmediate()` called `_musicPlayer.Pause()` without setting volume to 0 first. NAudio's persistent mixer architecture means "logical pause" only sets a flag — the audio chain continues outputting at the last volume level. Now sets `Volume = 0` before logical pause, matching the pattern used by jingle and dashboard pauses. Affects External Audio instant pause when Live Effects or Visualizer is enabled.

### Changed
- **Fullscreen Performance** — `OnGameSelected` visual effects (dynamic color extraction, icon glow, list hover glow, sidebar glow) now gated behind Desktop mode only. Fullscreen game selection is purely coordinator + playback logic — no unnecessary visual tree walks or SkiaSharp rendering.
- **External Audio Poll Timer** — Timer no longer starts unconditionally at plugin load. Only starts when `PauseOnExternalAudio` is enabled; dynamically starts/stops when the setting changes. Eliminates ~1 ThreadPool wake per second for users with the setting disabled (default).
- **Memory Leak Fix** — `Games.ItemUpdated` event now properly unsubscribed in `OnApplicationStopped()`.

## [1.3.9] - 2026-03-28

### Changed
- **Controller Input: SDK Migration** — All 5 controller dialogs (File Picker, Delete Songs, Download, Amplify, Waveform Trim) migrated from XInput polling to Playnite SDK 6.15 event-driven controller input. Supports Xbox, PlayStation, Switch Pro, and generic controllers via SDL2. Zero CPU when idle — no more 30Hz polling loops.
- **Controller Infrastructure** — New `IControllerInputReceiver` interface and stack-based `ControllerEventRouter` for centralized event routing. Nested modal dialogs properly push/pop on the receiver stack.
- **DialogHelper Modals** — Confirmation and message dialogs now use SDK events instead of XInput polling. Yes/No buttons are focus-navigable via D-pad (Playnite's built-in D-pad→keyboard translation).
- **Login Bypass** — Controller login dismiss replaced XInput polling with SDK `OnControllerButtonStateChanged` override.
- **D-Pad Navigation Speed** — Debounce reduced from 300ms to 150ms across all controller dialogs for snappier scrolling.
- **XInputWrapper Removed** — `XInputWrapper.cs` deleted. `ControllerDetectionService` (presence detection) retained separately.

### Fixed
- **Amplify Dialog: Music Not Resuming** — After amplifying a song and closing the dialog, the newly amplified song now continues playing instead of stopping.
- **Delete Dialog: B Button Leak** — Pressing B to close the delete dialog no longer triggers an unwanted delete confirmation popup.
- **OGG Audio Editing** — Amplify and Trim audio features now fully support `.ogg` format. Waveform generators use `OggFileReader` for loading, gain adjustment, and trimming OGG Vorbis files.
- **Confirmation Dialog Buttons** — Yes/No buttons now have rounded corners matching the highlight style.
- **Modal Dialog Controller Restore** — After a confirmation modal closes, the parent dialog's controller input is properly restored via the receiver stack.

### Added
- **Play Only on Game Select [Fullscreen Mode]** — New setting in Playback tab. Default/ambient music plays while browsing the game grid. Game-specific music only plays when you enter a game's detail view (A button). Automatically reverts to default music when returning to the grid. Uses Playnite SDK's `ActiveFullscreenView` (List vs Details) with a 200ms view-change monitor. Desktop mode unaffected. (#75)
- `IControllerInputReceiver` — interface for dialogs receiving SDK controller events
- `ControllerEventRouter` — stack-based router with registration cooldown and `DispatcherPriority.Input` dispatch for modal compatibility
- `OnControllerButtonStateChanged` + `OnDesktopControllerButtonStateChanged` — SDK overrides in UniPlaySong.cs for both Fullscreen and Desktop modes
- Continuous D-pad repeat via `DispatcherTimer` in Amplify (gain adjustment) and Trim (marker movement) dialogs

## [1.3.8] - 2026-03-24

### Fixed
- **OGG Vorbis Playback** — OGG files placed in game music folders were silently ignored. Two issues: `.ogg` was missing from the supported extensions list (dropped during a prior cleanup), and NAudio's `AudioFileReader` falls back to Windows Media Foundation for unknown formats, which fails with `COMException 0xC00D36C4` on systems without an OGG codec. Files are now recognized and play correctly on both SDL2 and NAudio backends.
- **Open Log Folder** — The "Open Log Folder" button in Settings crashed with `DirectoryNotFoundException` on non-standard Playnite installs (e.g. portable installs in `AppData\Local`). The path was hardcoded to `AppData\Roaming\Playnite\Extensions`. Now uses the actual DLL location, which works for any Playnite install type.
- **Music Blocked on Startup** — Transient runtime flags (`ThemeOverlayActive`, `VideoIsPlaying`) could get stuck as `true` if set by a theme during startup, permanently blocking automatic music playback. Now reset to `false` on every launch.
- **External Audio Detection Thread Safety** — Shared state flags between the external audio polling timer (ThreadPool) and the UI thread are now marked `volatile` to prevent stale reads during mode switches or rapid state transitions.

### Added
- **Native OGG Decoding** — New `OggFileReader` wrapper around NVorbis provides codec-independent OGG Vorbis decoding for the NAudio pipeline. No Windows codec or third-party codec pack required. SDL2 already had built-in OGG support via stb_vorbis.
- **NVorbis dependency** — Pure managed OGG Vorbis decoder (79 KB). No native binaries.

### Changed
- **Wallpaper Engine Default Exclusion** — `wallpaper64`, `wallpaper32`, and `webwallpaper32` are now excluded from external audio detection by default. Wallpaper Engine's persistent audio output caused constant pause/resume cycling. Existing users get the new exclusions automatically via settings migration.

## [1.3.7] - 2026-03-15

### Fixed
- **Live Effects Toggle Not Working** — Turning off Live Effects while the Spectrum Visualizer or Peak Meter was enabled had no effect — reverb and other audio effects continued playing. Root cause: the EffectsChain only checked individual effect toggles (ReverbEnabled, etc.) but never checked the master `LiveEffectsEnabled` switch. When NAudio stayed active for the visualizer, effects kept processing. Fix: EffectsChain now bypasses all processing when `LiveEffectsEnabled` is false, regardless of individual effect states.

## [1.3.6] - 2026-03-09

### Added
- **Music Library Dashboard** (Experimental, Desktop only) — Full-page music library browser accessible from the Playnite sidebar. Harmonoid-inspired tabbed interface (Games, Tracks, Artists, Genres, Stats) with persistent now-playing bar. Game card grid with icons, song counts, and durations. Game detail view with track listing and Play All. Search filtering. Independent NAudioMusicPlayer decoupled from main playback via `PauseSource.Dashboard`. Radio mode for library-wide shuffle. Expanded now-playing overlay with large cover art and transport controls. Audio-reactive game card glow. Player lazily created and fully disposed when dashboard closes. Enable in Settings → Experimental.
- **Fade Out Before Song End** — When Radio Mode or Randomize on Song End is active, a configurable fade-out (1–5s, default 3s) starts before the song finishes naturally, creating a smooth transition instead of an abrupt cut. Works with both SDL2 and NAudio backends. Setting: Playback tab.
- **Icon Glow** (Experimental, Desktop only) — Adds a multi-layer neon glow effect around the selected game's icon. The glow color is automatically extracted from the game icon using HSV-based color analysis. Two rendering layers: a SkiaSharp pre-rendered outer glow image behind the icon, plus a WPF DropShadowEffect inner halo on the icon itself. When Live Effects/Visualizer is active (NAudio backend), the glow reacts to music in real-time using bass FFT energy, adaptive gain control, common mode subtraction, three-stage cascaded smoothing, spectral flux onset detection, and a fast punch signal for beat responsiveness. When NAudio is not active, falls back to a gentle sine-wave pulse animation (configurable speed) or static glow. Settings: Experimental tab — Glow Intensity, Glow Size, Pulse Speed, Audio Sensitivity, with a dedicated Reset button.
- **List Icon Glow** (Experimental, Desktop only) — Applies glow effects to game icons in Playnite's list/grid view. The selected game gets a full SkiaSharp-rendered glow wrapped behind the icon, while hovered games get a lightweight DropShadowEffect. Glow colors are extracted from each game's icon art. Includes a "Subtle" mode option for softer glow values. Fade-in (150ms) and fade-out (120ms) animations prevent visual flicker during game selection changes. Independent toggle from the main Icon Glow reactor. Settings: Experimental tab.
- **Sidebar Glow Presets** (Experimental, Desktop only) — 22 audio-reactive animation presets for the sidebar area behind game icons. All presets use `CompositionTarget.Rendering` (~60fps) with delta-time normalization for frame-rate independence. Audio energy sourced from `VisualizationDataProvider.GetLevels()` (NAudio) with EMA smoothing (fast attack/slow decay), sine-wave fallback when NAudio is unavailable. Shared canvas injection pattern (`InjectEffectCanvas`/`RemoveEffectCanvas`) wraps the original sidebar child in a Grid with a Canvas overlay. Game icon colors extracted via `IconColorExtractor` tint all effects. Presets:
  - **Original 4:** Breathing (opacity pulse), Glow Bars (VU meter fill), Plasma Grid (color wash), Pixel Grid (pixelated cells)
  - **Iteration 2 (8):** Rain Drops (digital rain columns), Waveform (oscillating sine), Fire (bottom-up flame cells), Starfield (rising sparks), Ripple (concentric rings), Aurora (flowing horizontal bands), Heartbeat (EKG scrolling line with time accumulator), Waterfall (spectrogram color bands with time accumulator)
  - **Shader-inspired (6):** Nebula (Lissajous point cloud), DNA Helix (double helix with 3D depth from cos), DNA Helix Bloom (helix + RadialGradientBrush halos), Matrix (green character rain streaks), Laser (16 converging rainbow beams from ShaderToy WdscR4), Pulse Waves (4 layered glowing sine waves from Guyver shader)
  - **Shader-inspired iteration 2 (4):** Voronoi (glowing vertical polyline distorted by Voronoi noise hash), Equalizer Grid (4×20 bar grid with nested sine-driven widths), Snow (5-layer parallax snowfall with sine drift), Neon Line (smooth vertical beam with 1/abs glow)
  - Settings: Experimental tab → Sidebar Glow → Mode dropdown.

### Improved
- **Pause on External Audio** — Fixed potential reliability issues with external audio detection. Audio device COM object now properly disposed each poll tick (prevents stale session data and COM reference leaks). Individual audio sessions are now error-isolated — a bad session (crashed process, driver issue) no longer aborts the entire scan. Added diagnostic logging for COM and per-session errors to aid troubleshooting.

## [1.3.5] - 2026-03-08

### Improved
- **Open Music Folder** - When "Open Music Folder" is selected for a game with no existing music folder, a Yes/No dialog now asks if you want to create the folder and open it in Explorer. Previously showed a static info popup with the raw folder path. Selecting Yes creates the folder and opens it immediately; No cancels silently.
- **Create Music Folders for All Games** - New bulk action in Settings → Editing. Creates a music folder for every game in the library that doesn't have one yet. Reports how many folders were created on completion.
- **Game Folder Breadcrumbs** - Each game music folder now contains a `[Game Name].txt` file identifying the game by name and ID. Created automatically on folder creation, on "Open Music Folder" for existing folders, and retroactively for all existing folders via the bulk action. Makes raw folder browsing readable without needing to cross-reference GUIDs.
- **Game Index File** - Running "Create Music Folders for All Games" generates a `_game-index.txt` in the parent `Games/` directory listing all games with music folders and their IDs, sorted alphabetically.
- **Open Game Index** - New button in Settings → Editing → File Management opens `_game-index.txt` directly in the default text editor. Shows a helpful prompt if the index hasn't been generated yet. The "Original File Management" section has been renamed to "File Management".

### Added
- **Localization Infrastructure** - Foundation for community translation support. Implements WPF `ResourceDictionary` locale loading at startup (`LoadLocalization()`): detects system locale, tries to load a matching `Localization/{locale}.xaml` file compiled into the assembly, falls back to `en_US.xaml`. `ResourceProvider.GetString(key)` helper provides safe fallback to the key name if a string is missing. English reference file (`src/Localization/en_US.xaml`) defines the canonical key contract with ~30 seed strings. String extraction from C# dialogs and XAML labels is deferred until a translator is available. See `docs/plans/2026-03-08-localization-plan.md` for the full extraction plan.

## [1.3.4] - 2026-03-07

### Notice
GitHub suspended this account in February 2026 without notice or explanation. The account has since been restored. Out of an abundance of caution, and following discussion with the Playnite developer, certain features have been removed to minimize potential liabilities. A Gitea mirror has been established at [gitea.com/aHuddini/UniPlaySong](https://gitea.com/aHuddini/UniPlaySong) as a permanent backup.

### Removed
- **Download Sources** - Download sources have been removed to avoid potential DMCA issues.
- **Bulk Download** - "Download Music for All Games" button removed from Settings → Downloads.
- **Search Hints Online Updates** - The search hints database no longer checks for or downloads online updates. A bundled database is included with the extension, or you can load a custom database file via Settings → Search.

### Added
- **Game Property Filter** - Play game-specific music only for games matching certain platforms, genres, or sources. Configure via Settings → Playback → Game Property Filter. Uses OR logic across all selected criteria — a game matches if it belongs to any selected platform, genre, or source. Games that don't match fall through to default music.
- **Filter Mode** - Play game-specific music only when a Playnite filter is active. When enabled, switching to unfiltered ("All") view falls through to default music; applying any filter (platform, completion status, genre, custom preset, etc.) restores game-specific playback. Covers both criteria-based filters and Playnite's built-in quick-filter presets (Recently Played, Most Played). Configure via Settings → Playback.
- **Radio Mode** - Continuous background music from a fixed pool, ignoring game selection entirely. Four sources: Full Library (shuffles every song in your library), Custom Folder, Custom Game Rotation, and Completion Status Pool. Overrides both game-specific and default music while active. Skip and Now Playing work as expected. Configure via Settings → Playback → Play Methods.

### Changed
- **External Audio Pause Default** - "Pause on external audio" is now disabled by default. Previously enabled by default, which surprised new users with music stopping when launching games that played audio. Can be re-enabled in Settings → Pauses.

## [1.3.3] - 2026-02-22

### Fixed (Critical — NAudio Audio Pipeline)
- **Audio Artifact Eliminated** - Resolved a longstanding tremolo/stutter/doppler audio artifact that occurred when Live Effects or Visualizer was enabled. This artifact was unknowingly present since Live Effects were introduced in v1.1.4, manifesting as audible flutter during game switching and pause/resume. Root cause: the fader applied ~60 discrete volume steps/second via a `System.Timers.Timer`, and the EffectsChain's reverb feedback loops amplified the rate-of-change discontinuities at each step boundary into audible tremolo.
- **Per-Sample Volume Ramping** - Replaced timer-based discrete volume stepping with `SmoothVolumeSampleProvider`, which applies volume changes per-sample on the audio thread (44,100+ increments/second). Zero discontinuities through reverb, zero timer jitter, zero rate-of-change artifacts.
- **Logical Pause (NAudio)** - `WaveOutEvent` now stays running during pause (outputting silence at volume 0) instead of using `Pause()`/`Play()`. The old approach caused stale pre-rendered buffer blips on resume because NAudio pre-renders audio buffers. Position is saved on pause and restored on resume so the song doesn't drift while paused.
- **Fader Stall Recovery** - Short audio clips (sound effects, jingles) that reach EOF during a fade-out ramp no longer permanently freeze the fader and break all playback controls. The fader now detects when the audio thread has stopped processing samples and force-completes pending switch/pause/stop actions.
- **Pause Respected on Song End** - Music no longer silently auto-advances to the next song while paused. With logical pause, `WaveOutEvent` reaches EOF at volume 0 and fires `MediaEnded`; this is now properly ignored when paused.
- **MusicFader Rewrite** - The fader timer no longer steps volume directly. It monitors the audio-thread ramp for completion and dispatches phase transitions (stop/play for song switches, pause actions, etc.). This eliminates all timer-driven volume artifacts.
- **Short Track EOF During Pause** - Short songs that reached EOF while logically paused (volume 0, still in mixer) would stall the fader permanently. The mixer auto-removes inputs on EOF, but `IsActive` still returned true because `_logicallyPaused` wasn't cleared. Fixed: `OnSongEnded` clears logical pause state; `Resume()` re-adds to mixer if song was auto-removed.
- **Interrupted Song Switch** - If a pause source (e.g., game starting) arrived during a mid-fade song switch, the pause overwrote the fader's pending action, orphaning the new song load. Fixed: `MusicFader.HasPendingPlayAction` detects orphaned loads; `RemovePauseSource` executes them on resume.

### Fixed
- **Default Music Pool Sources — Media Controls** - Skip, Now Playing display, and Song Progress bar now work correctly when Custom Folder (Playlist), Random Game, or Custom Game Rotation is selected as the default music source. Previously these controls were non-functional or showed stale data with pool-based sources.

### Added
- **Persistent Mixer Architecture** - NAudio backend now uses a single `WaveOutEvent` + `MixingSampleProvider` that lives for the lifetime of the player. Songs are swapped via `AddMixerInput()`/`RemoveMixerInput()` instead of creating and destroying a `WaveOutEvent` per song. Eliminates the ~70ms UI-thread freeze from Windows audio API calls on every game switch.
- **Configurable Fade Curves** - Five fade curve types, independently selectable for fade-in and fade-out via Experimental settings: Linear, Quadratic, Cubic, S-Curve, and Logarithmic. Default: Quadratic fade-in, Cubic fade-out.

### Changed
- **Fade Duration Slider** - Refined range from 0.05–10s to 0.10–5s with finer 0.05s snap-to-tick granularity. Wider slider (200px), cleaner labels, and an informational note about how Live Effects influence fade perception.
- **Fade Duration Constants** - `MinFadeDuration` 0.05→0.10, `MaxFadeDuration` 10.0→5.0.

### Performance
- **Zero-Cost Song Switching (NAudio)** - Close + Load + Play now takes **0ms** with the persistent mixer (was ~70ms per game switch). Song preloading during fade-out further eliminates AudioFileReader construction time.
- **Deferred Song Switch Execution** - Song switch completion (Close + Load + Play) is deferred to a `Dispatcher.BeginInvoke(Background)` frame so the fader timer tick doesn't block the UI thread.

## [1.3.2] - 2026-02-20

### Added
- **Global Media Key Control** (Experimental) — Win32 `RegisterHotKey` for global Play/Pause / Next / Previous / Stop. Works when Playnite isn't focused.
- **Taskbar Thumbnail Media Controls** (Experimental) — Previous/Play-Pause/Next buttons in the Windows taskbar preview pane via WPF `TaskbarItemInfo`. Desktop only.
- **Auto-Cleanup Empty Folders** — empty game music directories removed after deletion (also cleans up `.primarysong.json` leftovers).
- **M3U Playlist Export** — per-game / multi-game / library-wide export to extended M3U with `#EXTINF` entries and absolute paths.
- **Extended Default Music Sources** — Custom Folder, Random Game, Custom Game Rotation. New "Continue Same Song" toggle keeps the same default track across game switches.

### Changed
- **Settings tabs reorganized** — pauses moved to dedicated "Pauses" tab, "Audio Editing" → "Editing".
- **Several features graduated from Experimental** — Taskbar Thumbnail Media Controls, Random Game Picker Music, Celebration Toast, External Audio Detection, Idle/AFK Pause, System Lock Pause.
- **Per-Tab Reset Buttons** — each settings tab has a "Reset to Defaults" button. Tool paths preserved on reset.
- **Better out-of-box defaults** — fresh installs now ship with media controls, now playing display, auto-tagging, song randomization, default music preset, live effects (Rehearsal), spectrum visualizer, external-audio pausing, and completion celebration enabled.
- **Bundled Default Music** — preset files renamed; PS2 Menu Ambience added as a fourth option.
- **Style preset tuning** — Rehearsal updated to Reverb-first chain with -1dB makeup gain; all presets capped at 1dB max to prevent clipping.
- **Global Settings Reset rewritten** to use JSON deep clone instead of manual property copying (was missing properties).

### Fixed
- **Play button clears stale pause sources** (Idle, ExternalAudio, SystemLock) on resume. Stale sources previously could persist after long idle or lock/unlock cycles.
- **System Unlock clears idle state** — fixes music not resuming after lock/unlock when idle detection was active.
- **Random Picker music kept playing on close (SDL2 mode)** — deferred playback raced with the close handler. Added `IsActive` guard.
- **Default music skipped for uninstalled games after Live Effects toggle** — player recreation used `forceReload: true` which bypassed the `MusicOnlyForInstalledGames` filter. Changed to `forceReload: false`.
- **Settings silently reset on dialog interactions** — `CreateSettingsWithUpdate` only cloned ~30 of 180 properties; Browse/Select buttons silently zeroed everything else. Now uses JSON deep clone.

## [1.3.1] - 2026-02-18

### Added
- **Auto-Pause on External Audio** (Experimental) — pauses when another app produces audio (NAudio CoreAudioApi session enumeration). Configurable debounce, instant-pause toggle, and exclusion list (OBS excluded by default).
- **Auto-Pause on Idle / AFK** (Experimental) — pauses after no keyboard/mouse input for a configurable timeout (1–60 min, default 15). Win32 `GetLastInputInfo` polling. Doesn't detect gamepad input.
- **Stay Paused on Focus Restore** ([#69](https://github.com/aHuddini/UniPlaySong/issues/69)) — when enabled, music stays paused after alt-tabbing back; press Play to resume. Atomic `ConvertPauseSource(FocusLoss → Manual)` avoids resume blip.
- **Random Game Picker Music** (Experimental) — plays music for each game shown in Playnite's random picker dialog. Restores previous game's music on cancel.
- **Ignore Brief Focus Loss (Alt-Tab)** — only pauses if you actually switch apps. Detects the task-switcher window (`ForegroundStaging` on Win11) and waits for the overlay to resolve.

### Improved
- **Enhanced Library Statistics** (Experimental) — added Average Song Length, Total Playtime, ID3 Tag count, Bitrate Distribution, and Reducible Track Size cards (TagLib# background scan).
- **Settings UI** — General tab gets dedicated "Pause Scenarios" and "Top Panel Display" sections.

### Fixed
- **Focus-loss fade artifact** — reversing a mid-fade-out (quick alt-tab back) caused a volume jump because the fade-in calculation used the old fade-out start time. Fix: backdate the start time so the fade-in continues smoothly from current volume.

## [1.3.0] - 2026-02-16

### Added
- **Completion Fanfare** — celebration jingle on "Completed" status. 11 retro presets, system-beep / preset / custom-file source options. Enabled by default with Streets of Rage Level Clear.
- **Jingle Live Effects** — jingles play through NAudio when Live Effects mode is on, so reverb/filters apply. Main music pauses during the jingle and auto-resumes.
- **Song Count Badge in Menu** — single-game right-click header shows `(N songs)`; submenu has `[ N songs | size ]` info line.
- **Default Music Indicator** — optional `[Default]` prefix in Now Playing when default/fallback music is playing.
- **Experimental Settings Tab.**
- **Celebration Toast Notification** (Experimental) — gold-glow popup on game completion. Auto-dismisses after 6s.
- **Auto-Pause on System Lock** (Experimental) — pauses on Win+L (SessionSwitch event), resumes on unlock.
- **Song Progress Indicator** (Experimental) — thin progress bar in the Desktop top panel. Four configurable positions; 1s timer (not per-frame).
- **Enhanced Library Statistics** (Experimental) — card-grid layout with games-with-music, total songs/storage, format distribution, top-5 games.

### Fixed
- **Play/Pause icon flicker on song transition** — `OnSongChanged` fired before the new song started playing.
- **Skip while paused left play button stuck in paused state** — `PauseSource.Manual` was preserved by `ClearAllPauseSources()`; skip now explicitly removes it.
- **Skip delay when paused** — was running a full fade-out on silence; now loads immediately when already paused.
- **Now Playing text delay on skip** — `OnSongChanged` now fires immediately on skip so title/visualizer/progress update instantly.
- **Skip crossfade smoother** — uses `Switch()` (with preloading) instead of `FadeOutAndStop()` when playing.

### Performance
- **Song progress bar uses 1-second `DispatcherTimer`** instead of `CompositionTarget.Rendering` 60fps loop.
- **NowPlayingPanel** embedded progress bar no longer keeps the 60fps render loop alive when not scrolling/fading.

## [1.2.11] - 2026-02-15

### Added
- **Bundled Default Music Presets** - Three ambient tracks now ship with the plugin, selectable via dropdown in Settings → Playback. New installs default to a bundled preset instead of requiring a custom file path. Existing users are migrated automatically based on their current default music settings
- **Installed Games Only** - New playback setting to only play game-specific music for installed games. Uninstalled games fall back to default music (or silence if default music is off). Disabled by default — enable in Settings → Playback. Reactively re-evaluates music when a game's install state changes in Playnite
- **Hide Now Playing for Default Music** - New sub-option under "Show Now Playing" that collapses the Now Playing panel when no game-specific music is playing (default music active). Settings → General

### Performance
- **Song List Caching** - Directory scans cached in-memory with smart invalidation after file operations. Opt-in toggle in General Settings → Performance
- **Native Music Path Caching** - Cached native music file path at startup instead of scanning per game selection
- **Parallel File Deletions** - Bulk delete operations (Delete All Music, Delete Long Songs) now run in parallel

### Fixed
- **Shuffle Duplicate Sequences** - Fixed identical "random" songs when rapidly switching games in shuffle mode
- **Song Cache Invalidation** - Music now appears immediately after file changes without needing to re-select the game

## [1.2.10] - 2026-02-13

### Fixed (Critical)
- **Fullscreen Performance** - Fixed remaining lag on themes like ANIKI Remake. Desktop-only visualizer components (spectrum visualizer, now playing panel, song metadata service) were being initialized in fullscreen mode where they're never displayed. These are now properly skipped in fullscreen.
- **Native Music Suppression** - Reduced suppression to a single call at startup (matching PlayniteSounds pattern), removing redundant suppress calls from `OnApplicationStarted` and `RecreateMusicPlayerForLiveEffects`.

## [1.2.9] - 2026-02-13

### Added
- **Stop After Song Ends** ([#67](https://github.com/aHuddini/UniPlaySong/issues/67)) — toggle to play songs once without looping or advancing.

### Fixed
- **Fullscreen lag with themes like ANIKI Remake** — eliminated the native-music-suppression polling timer; suppression now fires once at startup.

### Backend
- **Visualizer FFT paused in fullscreen** via `VisualizationDataProvider.GlobalPaused`.

### Removed
- One-time migration code for suppress-native-music setting (v1.2.8 migration; all users migrated).

## [1.2.8] - 2026-02-09

### Fixed
- **Native Music Conflict** - Playnite's vanilla background music could play simultaneously with UniPlaySong when "Use Native Music as Default" was enabled, causing audio overlap with themes like ANIKI Remake
  - "Suppress Playnite Native Background Music" is now independent of Default Music settings
  - Moved to General Settings under "Enable Music" with a clear warning when disabled
  - Existing users are migrated to have suppression enabled

### Removed
- Removed one-time migration code for visualizer defaults and color theme reorder (v1.2.6 migrations — all users migrated)

## [1.2.7] - 2026-02-08

### Fixed (Critical)
- **Spectrum Visualizer Not Responding** - Visualizer bars were completely static for all users without Live Effects enabled. The SDL2 player (default when Live Effects are off) had no visualization data tap, so the visualizer received no audio data ([#66](https://github.com/aHuddini/UniPlaySong/issues/66))
  - NAudio pipeline is now used whenever the visualizer is enabled, regardless of Live Effects setting
  - Visualizer toggle no longer greyed out when Live Effects are off
  - Toggling the visualizer on/off no longer requires a Playnite restart — takes effect immediately

## [1.2.6] - 2026-02-08

### Added
- **Pause on Play (Splash Screen Compatibility)** ([#61](https://github.com/aHuddini/UniPlaySong/issues/61)) — pauses music when clicking Play before the splash screen appears; resumes when the game closes. Database-level game-state detection so it's plugin-load-order independent.

### Fixed
- **Top panel media-control button styling** — corrected font size, removed bold weight that distorted IcoFont symbols, theme-adaptive margins.

### Improved
- **Settings reorganized** — new "Playback" tab; "Audio Normalization" → "Audio Editing", "Search Cache" → "Search". "Open Log Folder" button added under Troubleshooting. Spectrum Visualizer no longer labeled Experimental.
- **Now Playing display** simplified to song title + artist (removed duration/timestamp).
- **Now Playing performance** ([#55](https://github.com/aHuddini/UniPlaySong/issues/55)) — ticker animation capped at 60fps, reducing GPU usage on high-refresh monitors.
- **Spectrum Visualizer overhaul** — enabled by default with Punchy preset + Dynamic (Game Art) colors. 12 new static themes (Synthwave / Ember / Abyss / Solar / Vapor / Frost / Aurora / Coral / Plasma / Toxic / Cherry / Midnight) and 3 Dynamic themes that sample colors from game artwork (Game Art V1, Alt Algo with center-weighted sampling, Vibrant Vibes). Color themes decoupled from presets. Replaced duplicate Terminal theme with Vapor.

## [1.2.5] - 2026-02-07

### Added
- **Audio-Reactive Spectrum Visualizer (Desktop)** — real-time FFT bars with 6 color themes (Classic, Neon, Sunset, Ocean, Fire, Ice), 5 tuning presets, and per-band controls. Best in Harmony / Vanilla themes.
- **Style Presets** — 15+ one-click effect combinations: Huddini styles (Rehearsal, Bright Room, Retro Radio, Lo-Bit, Slowed Dream, Cave Lake, Honey Room), clean presets (Clean Boost, Warm FM Radio, Bright Airy, Concert Live), and character presets (Telephone, Muffled Next Room, Lo-Fi Chill, Slowed Reverb).

### Improved
- **Logging cleanup** — 234 debug logs removed from `extension.log` (89% reduction) during normal operation.
- **Desktop media controls** — tighter button spacing (~24px closer).

### Fixed
- **Music + video audio playing simultaneously after opening/closing settings** — `MediaElementsMonitor` now updates the settings reference; runtime state (`VideoIsPlaying`, `ThemeOverlayActive`) no longer persisted; double-fire property handlers eliminated.
- **Stuck pause** when disabling "Pause on Minimize / Focus Loss / System Tray" while their pause source was active.

## [1.2.4] - 2026-01-30

### Added
- **Auto-Delete Music on Game Removal** - Music files are now automatically cleaned up when games are removed from Playnite ([#59](https://github.com/aHuddini/UniPlaySong/issues/59))
- **Clean Up Orphaned Music** - New cleanup tool removes music folders for games no longer in your library

### Changed
- **Playnite SDK** updated to version 6.15.0

### Fixed
- **Fullscreen Background Music Volume** - Playnite's volume slider now controls UniPlaySong playback in real-time ([#62](https://github.com/aHuddini/UniPlaySong/issues/62))
- **Now Playing GPU Usage** - Fixed animation causing permanent GPU usage when app loses focus ([#55](https://github.com/aHuddini/UniPlaySong/issues/55))
- **Audio Stuttering During Video Playback** - Fixed music repeatedly pausing during trailer/video playback ([#58](https://github.com/aHuddini/UniPlaySong/issues/58), [#60](https://github.com/aHuddini/UniPlaySong/pull/60)) - Credit: @rovri
- **Media Controls After Live Effects Toggle** - Fixed play/pause and skip buttons becoming unresponsive after toggling live effects ([#56](https://github.com/aHuddini/UniPlaySong/issues/56))
- **Manual Pause Not Respected on Game Switch** - Pressing pause then switching games now properly stays paused

## [1.2.3] - 2026-01-18

### Added
- **Bulk Delete Music (Multi-Select)** — select 2+ games → right-click → UniPlaySong → "Delete Music (All)". Auto-stops playback if a currently-playing game is in the selection.

### Improved
- **Safer Playnite restart handling** — settings requiring restart now use Playnite's built-in mechanism.

### Fixed
- **Window-state pause settings at startup** ([#51](https://github.com/aHuddini/UniPlaySong/issues/51), [#27](https://github.com/aHuddini/UniPlaySong/issues/27)) — music no longer plays briefly when Playnite launches minimized / in tray; correctly auto-plays when restored.

## [1.2.2] - 2026-01-17

### Performance
- **Search Cache** - 90% smaller cache files with automatic migration from old format

## [1.2.1] - 2026-01-17

### Added
- **Now Playing Display (Desktop)** — current song title/artist/duration in the top panel with scrolling text for long titles. Click to open the music folder.
- **Show Now Playing / Show Media Controls toggles** in Settings → General (both off by default; requires Playnite restart).

## [1.2.0] - 2026-01-15

### Added
- **Desktop Top Panel Media Controls** ([#5](https://github.com/aHuddini/UniPlaySong/issues/5)) — Play/Pause + Skip buttons in Playnite's top panel. Skip greys out at 30% opacity when only one song is available.

## [1.1.9] - 2026-01-13

### Added
- **Theme Integration (UPS_MusicControl)** ([#43](https://github.com/aHuddini/UniPlaySong/issues/43)) — `PluginControl` for theme developers to pause/resume music via XAML Tag bindings. New `PauseSource.ThemeOverlay` and `PauseSource.Video` for independent pause tracking. See [Theme Integration Guide](docs/THEME_INTEGRATION_GUIDE.md). Designed for ANIKI REMAKE compatibility.

### Credits
- Thanks to **Mike Aniki** for guidance and ANIKI REMAKE testing.

## [1.1.8] - 2026-01-11

### Added
- **Toast Notifications** for controller-mode operations — custom Windows API blur, color-coded accent bars (green/red/blue), smooth fade animations.

### Changed
- **Controller-Mode dialogs** replaced with toast notifications.

### Fixed
- **Settings persistence** — critical bug where settings changes weren't saved.
- **Controller-mode dialog handling** in Fullscreen.
- **NAudio default music crash** with live effects enabled.

## [1.1.7] - 2026-01-10

### Fixed
- **UI Performance** - Fixed progress dialog freezing with large game counts

## [1.1.6] - 2026-01-10

### Added
- **Search Hints Database** management section in Settings; hints stored in `AutoSearchDatabase` folder in extension data.

### Changed
- Renamed "User Hint" → "UPS Hint" throughout the UI.
- Added "GOG Cut" to game-name suffix stripping.

## [1.1.5] - 2026-01-09

### Added
- **Search Hints Backend** — dual-source (bundled curated + user-editable) game-name resolution. Provides direct album/playlist links for problematic games. Supports fuzzy / base-name / exact lookups.
- **Delete Long Songs** cleanup tool — finds and removes songs longer than 10 minutes (catches accidentally added full albums or podcasts).
- **Import from PlayniteSound & Delete** — clean migration that imports music then removes PlayniteSound originals.

## [1.1.4] - 2026-01-07

### Added
- **Live Effects: Audacity-Compatible Reverb** — libSoX/Freeverb algorithm. Ships 18 Audacity presets (Acoustic, Ambience, Vocal I/II, Bathroom, Cathedral, Big Cave, etc.) + 7 UniPlaySong environment presets (Living Room, Home Theater, Jazz Club, Night Club, Concert Hall, etc.). New parameters: Reverberance, Tone Low/High, Stereo Width. Wet Gain range extended to +10 dB.
- **Live Effects: Effect Chain Ordering** — 6 preset orderings.
- **Live Effects: Advanced Reverb Tuning** — expert-mode algorithm controls.
- **Amplify Audio** — waveform-based gain adjustment editor.
- Repair Music Folder option in Fullscreen mode context menu
- Music status tags: Games are now tagged with "[UPS] Has Music" or "[UPS] No Music" for easy filtering in Playnite ([#18](https://github.com/aHuddini/UniPlaySong/issues/18))

### Changed
- Live Effects reverb algorithm now uses Reverberance (not Room Size) for feedback calculation, matching Audacity behavior

### Fixed
- Room size slider changes now apply in real-time during playback
- Stop() no longer incorrectly triggers MediaEnded event in NAudio player
- Music now properly restarts when toggling Live Effects on/off

## [1.1.3] - 2026-01-02

### Added
- Pause music when in system tray ([#27](https://github.com/aHuddini/UniPlaySong/issues/27))
- Parallel bulk normalization: Audio normalization now processes up to 3 files in parallel ([#25](https://github.com/aHuddini/UniPlaySong/issues/25))
- Parallel bulk silence trimming
- Dialog windows now appear in Windows taskbar (Desktop mode) ([#28](https://github.com/aHuddini/UniPlaySong/issues/28))

### Changed
- Simplified bulk normalization progress dialog ([#26](https://github.com/aHuddini/UniPlaySong/issues/26))
- FFmpeg path setting consolidated to Audio Editing tab only ([#30](https://github.com/aHuddini/UniPlaySong/issues/30))

### Fixed
- Dialog windows getting "lost" when switching to other programs in Desktop mode ([#28](https://github.com/aHuddini/UniPlaySong/issues/28))

## [1.1.2] - 2026-01-01

### Added
- Precise Trim (Waveform Editor): Visual waveform-based audio trimming with draggable start/end markers
- Factory Reset & Cleanup Tools: New settings tab (Settings → Cleanup)
- Audio Repair Tool: Fix problematic audio files by re-encoding to 48kHz stereo format

### Changed
- "Pause when minimized" is now enabled by default for new installations ([#20](https://github.com/aHuddini/UniPlaySong/issues/20))
- Desktop context menu reorganized with "Audio Processing" and "Audio Editing" submenus

### Fixed
- Music not playing after adding files to game (required restart before)

## [1.1.1] - 2025-12-15

### Added
- Individual Song Processing: Normalize or silence-trim individual songs from context menu ([#16](https://github.com/aHuddini/UniPlaySong/issues/16))
- Open Preserved Folder button in Normalization settings ([#16](https://github.com/aHuddini/UniPlaySong/issues/16))

### Changed
- Removed redundant per-game buttons from Normalization tab (use context menu instead)
- Renamed trim options to "Silence Trim" for clarity

### Fixed
- PreservedOriginals path now opens correct backup location

## [1.1.0] - 2025-12-01

### Added
- Pause music on focus loss option ([#4](https://github.com/aHuddini/UniPlaySong/issues/4))
- Pause music on minimize option ([#4](https://github.com/aHuddini/UniPlaySong/issues/4))

### Fixed
- Music Play State settings (Never/Desktop/Fullscreen/Always) now work reliably ([#4](https://github.com/aHuddini/UniPlaySong/issues/4))
- "Set Primary Song (PC Mode)" file picker now opens in game's music directory

### Changed
- Debug logging now respects the toggle setting
- Initialization errors surface properly instead of silently failing

## [1.0.9] - 2025-11-15

### Added
- PlayniteSound Migration: Bidirectional import/export between PlayniteSound and UniPlaySong
- New Migration tab in settings

### Changed
- Reorganized context menu with logical groupings

## [1.0.8] - 2025-11-01

### Added
- Debug Logging toggle to reduce log verbosity ([#3](https://github.com/aHuddini/UniPlaySong/issues/3))
- Customizable Trim Suffix
- Long Audio File Warning (>10 minutes)
- Automatic Filename Sanitization

### Fixed
- Topmost windows blocking other apps in Desktop mode ([#8](https://github.com/aHuddini/UniPlaySong/issues/8))
- Preview threading InvalidOperationException
- FFmpeg process deadlock during normalization/trimming
- Normalization in non-English locales (decimal separator issues)
- MP4 files being renamed to MP3

### Changed
- All trim features labeled as "Silence Trimming"
- Improved progress tracking with succeeded/skipped/failed distinction

## [1.0.7] - 2025-10-15

### Added
- Silence Trimming: Remove leading silence from audio files using FFmpeg with configurable threshold and duration

## [1.0.6] - 2025-10-01

### Added
- Audio Normalization using EBU R128 standard with customizable settings
- Fullscreen Controller Support for music management
- Native Music Integration with Playnite's built-in background music

### Changed
- Enhanced native music suppression reliability
- Better theme compatibility for fullscreen modes

## [1.0.5] - 2025-09-15

### Added
- Native Music Control option to use or suppress Playnite's native music

## [1.0.4] - 2025-09-01

### Added
- Primary Song System to set which song plays first
- Universal Fullscreen Support for any Playnite theme
- Improved file dialogs and error handling

### Fixed
- Music not stopping when switching to games without music
- Music not playing when switching back to games with music
- File locking issues when selecting music files
