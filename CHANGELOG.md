# Changelog

All notable changes to UniPlaySong will be documented in this file.

> **Release Availability Notice:** Due to the GitHub account suspension, release downloads prior to v1.3.3 are no longer available. Full changelog history is preserved below for reference.

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
- **License Attribution Updated for GME + zlib** — `LICENSE` now credits Game Music Emu (LGPL v2.1+) and zlib (zlib license) as new bundled native libraries. Added an explicit note that GME is used via **dynamic linking** (P/Invoke to a separately-shipped `gme.dll`), the standard LGPL-compliant integration pattern, and clarified that our build uses the Nuked OPN2 YM2612 core (LGPL-safe) rather than the MAME core (which would make the library GPL v2+).
- **License Doc Scope Cleanup** — `LICENSE`, `README.md` Credits, and `docs/dev_docs/DEPENDENCIES.md` now distinguish between **bundled libraries** (what we redistribute inside the `.pext`) and **external tools** (yt-dlp, FFmpeg, Deno — installed by the user, not redistributed). Removed yt-dlp and FFmpeg from the LICENSE third-party section and the DEPENDENCIES License Compatibility list since they are not shipped; they remain in the README Credits under a new "External Tools (installed by user)" subsection.
- **New Dev Doc** — Added `docs/dev_docs/SUPPORTED_FILE_FORMATS.md` summarizing all supported audio formats (standard + GME retro chiptune) with verification status per format.

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
- **Global Media Key Control** (Experimental) - Control music playback using keyboard media keys (Play/Pause, Next Track, Previous Track, Stop)
  - Uses Win32 `RegisterHotKey` with a hidden `HwndSource` message window for zero per-keystroke overhead
  - Works globally even when Playnite is not focused
  - Play/Pause toggles manual pause, Next skips to a random different song, Previous restarts current song, Stop acts as Play/Pause
  - Graceful degradation: if another app has claimed a media key, that key is skipped (logged in debug log)
  - Toggle in Settings → Experimental (disabled by default, requires Playnite restart)
- **Taskbar Thumbnail Media Controls** (Experimental) - Previous/Play-Pause/Next buttons in Windows taskbar preview pane
  - Uses WPF's built-in `TaskbarItemInfo` (wraps `ITaskbarList3` COM) — no external dependencies
  - Play/Pause icon dynamically updates to reflect current playback state
  - Vector-rendered 32x32 icons generated at runtime (no external image files)
  - Desktop mode only; guarded against overwriting Playnite's own `TaskbarItemInfo` if set
  - Toggle in Settings → Experimental (disabled by default, requires Playnite restart)
- **Auto-Cleanup Empty Folders** - Automatically removes empty game music directories after song deletion
  - Triggers after all deletion paths: Delete All Music, Delete Long Songs, and controller single-song deletion
  - Checks for remaining audio files before removal (cleans up `.primarysong.json` and other non-audio leftovers)
  - Matches existing cleanup pattern used by game removal and orphan cleanup
- **M3U Playlist Export** - Export music as M3U playlists for external players (VLC, foobar2000, MPC)
  - Per-game export: right-click game → "Export M3U Playlist"
  - Multi-game export: select multiple games → "Export M3U Playlist (N games)"
  - Library-wide export: main menu → "Export Music Library Playlist (UPS)" with progress dialog
  - Extended M3U format with `#EXTINF` duration/title entries and absolute file paths
- **Extended Default Music Sources** - Three new default music source options in Settings → Playback → Default Music:
  - **Custom Folder**: Point to any directory of audio files — UPS randomly picks from it when no game music is available
  - **Random Game**: Randomly selects a song from any game in your music library
  - **Custom Game Rotation**: Select specific games whose music serves as the default rotation pool, with a searchable multi-select game picker dialog
  - **Continue Same Song**: Optional toggle to keep the same default song playing across game switches instead of re-rolling on each selection
  - All three sources integrate with existing skip behavior (skip picks a new random song from the pool)

### Changed
- **Settings Reorganization** - General settings reorganization to improve navigation experience. Pause scenarios moved to dedicated "Pauses" tab, "Audio Editing" tab renamed to "Editing", fullscreen-only options labeled accordingly.
- **Graduated from Experimental** - Taskbar Thumbnail Media Controls (→ General), Random Game Picker Music (→ Playback, now uses instant song switching for snappy browsing), Celebration Toast (→ Playback), External Audio Detection (→ Pauses), Idle/AFK Pause (→ Pauses), System Lock Pause (→ Pauses).
- **Per-Tab Reset Buttons** - Each settings tab now has a "Reset to Defaults" button in the tab header. Tool paths (FFmpeg) are preserved on reset.
- **Improved Default Settings** - New installs now ship with a better out-of-box experience: media controls, now playing display, auto-tagging, song randomization, bundled default music preset, live effects with Rehearsal style, spectrum visualizer, external audio pausing, and completion celebration all enabled by default.
- **Settings UI Polish** - Horizontal checkbox grouping for compact layout, italic tab subtitles in sage/seafoam accent, collapsible Dynamic Color Tuning section, streamlined button descriptions.
- **Bundled Default Music** - Renamed bundled preset files for cleaner filenames. Added PS2 Menu Ambience as a fourth bundled preset option.
- **Style Preset Tuning** - Rehearsal preset updated to Reverb-first effect chain with -1dB makeup gain. All style presets capped at 1dB maximum makeup gain to prevent audio clipping.
- **Global Settings Reset Rewritten** - Cleanup menu's "Reset Settings to Defaults" now uses JSON deep clone instead of incomplete manual property copying, ensuring all 180+ properties are properly reset.

### Fixed
- **Play Button Clears Stale Pause Sources** - Play button now clears automatic pause sources (Idle, ExternalAudio, SystemLock) when resuming. Previously, stale sources could remain active after long idle or lock/unlock cycles, causing music to stay paused even after clicking play.
- **System Unlock Clears Idle State** - Unlocking Windows now properly clears the idle pause source and restores idle volume. Fixes music not resuming after lock/unlock when idle detection or idle volume lowering was active before locking.
- **Random Picker music not stopping on close (SDL2 mode)** - Deferred playback via `BeginInvoke` could race with the picker's close handler, causing the last previewed song to keep playing after the dialog was dismissed. Fixed with an `IsActive` guard on the deferred action.
- **Default music skipped for uninstalled games after live effects toggle** - Player recreation used `forceReload: true` which bypassed the `MusicOnlyForInstalledGames` filter, causing game-specific music to play instead of falling through to default music. Changed to `forceReload: false`.
- **Settings silently resetting on dialog interactions** - `CreateSettingsWithUpdate` only cloned ~30 of 180 properties. Clicking Browse/Select buttons in settings would silently reset all un-mapped properties (live effects, visualizer, normalization, etc.) to defaults. Replaced with JSON deep clone that preserves all properties automatically.

## [1.3.1] - 2026-02-18

### Added
- **Auto-Pause on External Audio** (Experimental) - Automatically pauses music when another application produces audio
  - Detects any app playing audio through Windows via NAudio CoreAudioApi session enumeration
  - Configurable debounce slider (0–10 seconds, default 0 "Instant") — controls how long external audio must persist before pausing. Default is instant (reacts on first detection poll)
  - Instant pause toggle — bypasses fade transitions for immediate pause/resume (off by default, faded transitions used)
  - Adaptive polling: 500ms in instant mode, 1000ms in normal mode. Lower peak threshold (0.005) in instant mode for higher sensitivity
  - App exclusion list — comma-separated process names to ignore (OBS excluded by default)
  - Known limitation: Screen recorders/streaming apps that capture desktop audio may cause false detections
  - New `AddPauseSourceImmediate`/`RemovePauseSourceImmediate` methods in MusicPlaybackService for fade-free pause/resume with proper source tracking
- **Auto-Pause on Idle / AFK** (Experimental) - Pauses music after no keyboard/mouse input for a configurable duration
  - Win32 `GetLastInputInfo()` P/Invoke with 10-second polling timer
  - Configurable timeout slider (1–60 minutes, default 15 min)
  - Music resumes automatically when any keyboard or mouse input is detected
  - Known limitation: Does not detect gamepad input
  - Preserved across game switches (user is still AFK regardless of which game is selected)
- **Stay Paused on Focus Restore** ([#69](https://github.com/aHuddini/UniPlaySong/issues/69)) - Option to keep music paused after Playnite regains focus
  - When enabled, music stays paused after alt-tabbing back — press play to resume manually
  - Uses atomic `ConvertPauseSource(FocusLoss → Manual)` to avoid audible resume blip
  - Play and skip buttons work naturally (they clear Manual pause source)
  - Sub-option under "Pause on focus loss" in General → Pause Scenarios
- **Random Game Picker Music** (Experimental) - Plays music for each game shown in Playnite's random game picker dialog
  - Hooks into the picker's ViewModel via `INotifyPropertyChanged` to detect game changes as you click "Pick Another"
  - Restores previous game's music when the dialog is cancelled or closed with Escape
  - Commits naturally when using "Play" or "Navigate to Game" (normal `OnGameSelected` flow takes over)
  - Toggle in Settings → Experimental (disabled by default)
- **Ignore Brief Focus Loss (Alt-Tab)** - Detects the alt-tab overlay and only pauses if you actually switch apps
  - Uses Win32 `GetForegroundWindow()` + `GetClassName()` to identify the task switcher window (`ForegroundStaging` on Windows 11)
  - Polls until the overlay resolves: aborted alt-tabs are ignored, completed switches pause normally
  - Skips P/Invoke work entirely when music is already paused (game running, idle, etc.)
  - Sub-option under "Pause on focus loss" in General → Pause Scenarios

### Improved
- **Enhanced Library Statistics** (Experimental) - Expanded the stats panel with background audio-level metrics powered by TagLib#
  - New audio metrics row: Average Song Length, Total Playtime, Songs With ID3 Tags (auto-populated via background scan)
  - Song Bitrate Distribution card showing exact counts per standard bitrate (320/256/192/160/128/96/64/32 kbps) with non-standard VBR values grouped as "Other"
  - Reducible Track Size card with teal accent showing count of songs above 128 kbps and estimated space recoverable if downsampled
  - Reorganized card layout: Total Songs → Total Size → Games with Music → Avg Songs/Game → Avg Song Length → Total Playtime → ID3 Tags → Top Games → Format Distribution → Bitrate Distribution → Reducible Track Size
  - Improved card labels for clarity (e.g., "Games in Library with Music", "Total Size of Songs", "Average #Songs / Game")
  - "Scanning...[Please Wait]" placeholder shown during background audio scan
- **Settings UI Reorganization** - General tab now has clearly separated sections
  - New "Pause Scenarios" section groups all pause-related options with a header and description
  - New "Top Panel Display" section groups Now Playing and Media Controls options

### Fixed
- **Focus Loss Fade Artifact** - Fixed echo/doppler audio artifact when music pauses and resumes during brief focus loss
  - Root cause: reversing a mid-fade-out (e.g., quick alt-tab back) used the old fade-out start time for the fade-in calculation, causing a sudden volume jump
  - Fix: calculates the equivalent fade-in progress from the current player volume and backdates the start time so the fade-in continues smoothly from where the fade-out left off

## [1.3.0] - 2026-02-16

### Added
- **Completion Fanfare** - Play a celebration jingle when marking a game as "Completed"
  - Ships with 11 retro jingle presets (Sonic, Streets of Rage, Mortal Kombat, and more)
  - Three sound source options: System beep, Jingle preset, or Custom audio file
  - Jingle presets selectable via dropdown with preview button
  - Enabled by default with Streets of Rage - Level Clear preset
  - Toggle in Settings → Playback → Gamification
- **Jingle Live Effects** - Celebration jingles play through a dedicated NAudio player, so live effects (reverb, filters, stereo width, etc.) are applied when Live Effects mode is enabled
  - Main music pauses during the jingle and resumes automatically when it finishes
  - Toggleable via "Apply live effects to jingles" in Gamification settings
- **Song Count Badge in Menu** - Right-click context menu header now shows song count for single-game selection (e.g., "UniPlaySong (3 songs)")
  - Info line at the top of the submenu displays song count and total folder size (e.g., `[ 3 songs | 12.4 MB ]`)
  - Multi-game selection retains the plain "UniPlaySong" header
- **Default Music Indicator** - Optional `[Default]` prefix in the Now Playing ticker when default/fallback music is playing
  - Shows `[Default]` alone for non-bundled default music, `[Default] Song Title - Artist` for bundled presets
  - Toggle in Settings → General (disabled by default, requires "Show Now Playing" to be enabled)
- **Experimental Settings Tab** - New settings tab for features under active development
- **Celebration Toast Notification** (Experimental) - Visual gold-glow toast popup when a game is marked completed, complementing the fanfare sound
  - Smooth pulsing glow animation with gold accent theme
  - Auto-dismisses after 6 seconds, click to dismiss early
  - Toggle in Settings → Experimental
- **Auto-Pause on System Lock** (Experimental) - Music pauses when you lock your PC (Win+L) and resumes on unlock
  - Uses `SystemEvents.SessionSwitch` with `PauseSource.SystemLock` in the multi-source pause system
  - Toggle in Settings → Experimental (disabled by default)
- **Song Progress Indicator** (Experimental) - Thin progress bar in the Desktop top panel showing playback position
  - Four configurable positions: After skip button, After visualizer, After now playing, or embedded below now playing text
  - Uses a lightweight 1-second timer instead of per-frame rendering
  - Toggle and position selector in Settings → Experimental (requires Playnite restart for position changes)
- **Enhanced Library Statistics** (Experimental) - Upgraded the library stats panel in Experimental settings from a single line to a structured card grid layout
  - Primary metrics: games with music (with percentage), total songs, total storage, average songs per game
  - Format distribution breakdown showing all audio formats
  - Top 5 games ranked by song count

### Fixed
- **Play/Pause Icon Flicker on Song Transition** - Play button briefly showed "paused" state when songs changed (randomize on end, skip to next). Root cause: `OnSongChanged` fired before the new song started playing, so `IsPlaying` was false during the event
- **Skip While Paused** - Pressing skip while music was paused left the play button in paused state even though the new song was playing. `PauseSource.Manual` was preserved by `ClearAllPauseSources()` by design; skip now explicitly removes it
- **Skip Delay When Paused** - Skipping while paused triggered a full fade-out on silence, causing a noticeable delay before the next song loaded. Now loads immediately when already paused
- **Now Playing Text Delay on Skip** - Song title, visualizer, and progress bar didn't update until after the crossfade completed. Now fires `OnSongChanged` immediately on skip (matching game-selection behavior) so UI updates are instant
- **Skip Crossfade Improved** - Skip now uses `Switch()` (with preloading) instead of `FadeOutAndStop()` for smoother transitions when playing

### Performance
- **Song Progress Bar** - Uses `DispatcherTimer` at 1-second intervals instead of `CompositionTarget.Rendering` (60fps). A 50px bar on a 3-minute song moves ~0.28px/sec, making per-frame updates wasteful
- **NowPlayingPanel Render Loop** - Embedded progress bar no longer keeps the 60fps render loop alive when text scrolling/fading has finished. Uses its own 1-second timer, piggybacking on the render loop only when it's already running for animations

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
- **Stop After Song Ends** - New toggle to play songs once without looping ([#67](https://github.com/aHuddini/UniPlaySong/issues/67))
  - When enabled, music stops after the current song finishes instead of looping or advancing
  - Works in both regular and preview mode

### Fixed
- **Fullscreen Performance** - Eliminated native music suppression polling timer that caused UI lag with themes like ANIKI Remake. Suppression now fires once at startup, matching the PlayniteSounds approach.

### Backend
- **Visualizer FFT** - Spectrum FFT processing is now paused in fullscreen mode via `VisualizationDataProvider.GlobalPaused` (desktop visualizer is not visible in fullscreen; groundwork for future fullscreen visualizer support)

### Removed
- Removed one-time migration code for suppress native music setting (v1.2.8 migration — all users migrated)

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
- **Pause on Play (Splash Screen Compatibility)** - New setting to pause music immediately when clicking Play on a game ([#61](https://github.com/aHuddini/UniPlaySong/issues/61))
  - Music fades out before the splash screen appears and resumes when the game closes
  - Works independently of plugin load order by using database-level game state detection
  - Enable in settings: "Compatibility: Pause on Play (Splash Screen Mode)"

### Fixed
- **Top Panel Media Controls** - Fixed button styling to match native Playnite icons and adapt to different desktop themes
  - Corrected font size and removed bold weight that distorted IcoFont symbols
  - Theme-adaptive margin trimming groups play/pause and skip buttons closer together across all themes

### Improved
- **Settings Reorganization** - Restructured add-on settings for better discoverability
  - New "Playback" tab with volume, fade effects, preview mode, and song randomization
  - Renamed tabs: "Audio Normalization" → "Audio Editing", "Search Cache" → "Search"
  - Added "Open Log Folder" button in Troubleshooting section
  - Spectrum Visualizer no longer labeled as "Experimental"
- **Now Playing Display** - Cleaner display showing only song title and artist (removed duration/timestamp)
- **Now Playing Performance** - Reduced GPU usage on high refresh rate monitors by capping the ticker animation at 60fps ([#55](https://github.com/aHuddini/UniPlaySong/issues/55))
- **Spectrum Visualizer** - Major overhaul with new defaults and 22 color themes
  - Now enabled by default with Punchy preset and Dynamic (Game Art) colors
  - 12 new static color themes: Synthwave, Ember, Abyss, Solar, Vapor, Frost, Aurora, Coral, Plasma, Toxic, Cherry, Midnight
  - 3 Dynamic themes that sample colors from game artwork:
    - **Dynamic (Game Art V1)**: Natural tones with user-configurable brightness/saturation sliders
    - **Dynamic (Alt Algo)**: Advanced extraction with center-weighted sampling and color diversity bonus
    - **Dynamic (Vibrant Vibes)**: Vivid mode with aggressive color separation for creative gradients
  - Decoupled color themes from visualizer presets — presets only control tuning parameters
  - Improved opacity curve (sqrt) for better dynamic range
  - Replaced Terminal (duplicate of Matrix) with Vapor (mint → lavender)

## [1.2.5] - 2026-02-07

### Added
- **Audio-Reactive Spectrum Visualizer (Desktop Mode)** - New real-time frequency spectrum visualizer that reacts to your game music
  - 6 gradient color themes: Classic (white), Neon (cyan→magenta), Sunset (orange→pink), Ocean (blue→teal), Fire (red→yellow), Ice (white→blue)
  - Best viewed in Harmony and Vanilla desktop themes
  - Toggle between smooth gradient bars and solid color rendering
  - 5 tuning presets (Default, Dynamic, Smooth, Energetic, Minimal) plus Custom mode
  - Advanced tuning controls: FFT size, smoothing, rise/fall speeds, frequency-dependent behavior
  - Creates vibrant visual feedback that responds to music energy and frequency in real-time
- **Style Presets** - 15+ one-click audio effect combinations for different listening moods
  - Huddini Styles: Rehearsal, Bright Room, Retro Radio, Lo-Bit, Slowed Dream, Cave Lake, Honey Room (signature creative effects)
  - Clean presets: Clean Boost, Warm FM Radio, Bright Airy, Concert Live (no slow effect)
  - Character presets: Telephone, Muffled Next Room, Lo-Fi Chill, Slowed Reverb (unique audio textures)
  - Combines reverb, filters, stereo width, bitcrushing, and slow effects into cohesive sonic profiles

### Improved
- **Logging Cleanup** - Removed 234 debug logs (89% reduction) that cluttered extension.log during normal operation
- **Desktop Media Controls** - Tighter button spacing for more compact top panel layout (24px closer)
- **Code Quality** - Consolidated duplicate code and eliminated magic strings for improved maintainability

### Fixed
- **Settings Integration with other Plugins** - Fixed music and video audio playing simultaneously after opening/closing settings
  - MediaElementsMonitor now properly updates settings reference when settings are saved
  - Runtime state properties (`VideoIsPlaying`, `ThemeOverlayActive`) no longer persisted to disk, preventing startup playback issues
  - Eliminated double-fire property change handlers that caused inconsistent pause states
- **Pause-on-X Settings Behavior** - Fixed stuck pause when disabling "Pause on Minimize/Focus Loss/System Tray" settings while their pause source is active

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
- **Bulk Delete Music (Multi-Select)** - Delete music for multiple games at once
  - Select 2+ games → Right-click → UniPlaySong → "Delete Music (All)"
  - Confirmation dialog shows game count before proceeding
  - Automatically stops playback if currently playing game is in selection
  - Shows summary of deleted files when complete

### Improved
- **Safer Playnite Restart Handling** - Settings requiring restart now use Playnite's built-in mechanism

### Fixed
- **Window State Pause Settings** - Fixed issues with pause settings at startup ([#51](https://github.com/aHuddini/UniPlaySong/issues/51), [#27](https://github.com/aHuddini/UniPlaySong/issues/27))
  - Music no longer plays briefly when Playnite launches minimized or in system tray
  - Music now correctly auto-plays when window is restored after starting minimized

## [1.2.2] - 2026-01-17

### Performance
- **Search Cache** - 90% smaller cache files with automatic migration from old format

## [1.2.1] - 2026-01-17

### Added
- **Now Playing Display** - Shows current song title, artist, and duration in Desktop top panel
  - Scrolling text animation for long titles
  - Click to open the music folder in Explorer
- **New Add-on Settings Options** (Settings → General)
  - **Show Now Playing (Desktop mode)** - Toggle the Now Playing song info display
  - **Show Media Controls (Desktop mode)** - Toggle the Play/Pause and Skip buttons
  - Both options are disabled by default; changing requires Playnite restart

## [1.2.0] - 2026-01-15

### Added
- **Desktop Top Panel Media Controls** - Play/Pause and Skip buttons in Playnite's top panel bar ([#5](https://github.com/aHuddini/UniPlaySong/issues/5))
  - **Play/Pause button** - Always visible, toggles music playback with standard media player conventions
  - **Skip/Next button** - Skip to a random different song from the current game's music folder
    - Greyed out (30% opacity) when only one song is available
    - Full opacity and functional when 2+ songs are available
  - Uses IcoFont icons for visual consistency with Playnite's native UI

## [1.1.9] - 2026-01-13

### Added
- **Theme Integration (UPS_MusicControl)** - PluginControl support for theme developers ([#43](https://github.com/aHuddini/UniPlaySong/issues/43))
  - Allows themes to pause/resume music via XAML Tag bindings
  - Control name: `UPS_MusicControl` (follows PlayniteSound pattern for compatibility)
  - Multi-source pause system prevents conflicts with other pause reasons (focus loss, video detection, etc.)
  - Smooth fade-out when pausing, fade-in when resuming
  - New `PauseSource.ThemeOverlay` and `PauseSource.Video` for independent pause tracking
  - Designed to provide future support to themes like ANIKI REMAKE
  - See [Theme Integration Guide](docs/THEME_INTEGRATION_GUIDE.md) for usage examples

### Credits
- **Special thanks to Mike Aniki** for his guidance and extensive testing help with getting this plugin to work with ANIKI REMAKE in mind!

## [1.1.8] - 2026-01-11

### Added
- **Toast Notifications** - New lightweight notification system for controller-mode operations
  - Custom Windows API blur effects
  - Color-coded accent bars: green for success, red for errors, blue for info
  - Non-intrusive positioning with smooth fade animations
  - Specifically designed for controller-mode features

### Changed
- **Controller-Mode Dialog System** - Replaced problematic confirmation dialogs with toast notifications in controller mode

### Fixed
- **Settings Persistence** - Fixed critical bug where settings changes were not being saved
- **Controller Mode Dialog Windows** - Fixed dialog handling in Fullscreen/Controller mode
- **NAudio Default Music Crash** - Fixed crash when live effects are enabled with default music playback

## [1.1.7] - 2026-01-10

### Fixed
- **UI Performance** - Fixed progress dialog freezing with large game counts

## [1.1.6] - 2026-01-10

### Added
- **Search Hints Database** - New settings section for managing the bundled search hints database
  - Hints are stored in AutoSearchDatabase folder in extension data
  - Open Database Folder button for easy access

### Changed
- Renamed "User Hint" to "UPS Hint" throughout the UI for better branding
- Added "GOG Cut" to game name suffix stripping

## [1.1.5] - 2026-01-09

### Added
- **Search Hints Backend** - Intelligent game name resolution system
  - Dual-source system: bundled curated hints + user-editable custom hints
  - Provides direct album/playlist links for problematic games that cause search issues
  - Supports fuzzy matching, base name matching, and exact lookups
- **Delete Long Songs** cleanup tool - Scans music library and deletes songs longer than 10 minutes
  - Helps remove accidentally added full albums, podcasts, or corrupted files
  - Shows preview of files to be deleted with duration and total size before confirmation
- **Import from PlayniteSound & Delete** migration option - Clean migration that imports music then removes PlayniteSound originals

## [1.1.4] - 2026-01-07

### Added
- **Live Effects: Audacity-Compatible Reverb** - Professional-grade reverb using libSoX/Freeverb algorithm
  - 18 Audacity factory presets: Acoustic, Ambience, Artificial, Clean, Modern, Vocal I/II, Dance Vocal, Modern Vocal, Voice Tail, Bathroom, Small Room Bright/Dark, Medium Room, Large Room, Church Hall, Cathedral, Big Cave
  - 7 UniPlaySong environment presets: Living Room, Home Theater, Late Night TV, Lounge/Cafe, Jazz Club, Night Club, Concert Hall
  - New parameters: Reverberance (tail length), Tone Low/High (post-reverb EQ), Stereo Width
  - Extended Wet Gain range to +10 dB for more pronounced effects
- **Live Effects: Effect Chain Ordering** - Configurable processing order with 6 preset orderings
- **Live Effects: Advanced Reverb Tuning** - Expert-mode controls for algorithm parameters
- **Amplify Audio** feature with waveform-based gain adjustment editor
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
