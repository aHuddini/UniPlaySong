# Changelog

All notable changes to UniPlaySong will be documented in this file.

## [1.3.2] - TBD

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

## [1.3.1] - 2026-02-18

### Added
- **Install-Aware Auto-Download** - Automatically downloads music when a game transitions from uninstalled to installed, if it doesn't already have music. Plays the downloaded music immediately if the game is still selected. Enabled by default in Settings → Downloads
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
- **Download Complete Notification** (Experimental) - Plays a notification sound when music downloads finish
  - Briefly pauses current music so the notification is audible, then resumes automatically
  - Toggle in Settings → Experimental
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
- **Song List Caching** - Directory scans cached in-memory with smart invalidation after file operations (downloads, edits, deletes). Opt-in toggle in General Settings → Performance
- **Native Music Path Caching** - Cached native music file path at startup instead of scanning per game selection
- **Parallel File Deletions** - Bulk delete operations (Delete All Music, Delete Long Songs) now run in parallel
- **Async UI Operations** - Converted blocking delays to async across batch downloads and audio processing dialogs

### Fixed
- **Shuffle Duplicate Sequences** - Fixed identical "random" songs when rapidly switching games in shuffle mode
- **Song Cache Invalidation** - Downloaded/repaired music now appears immediately without needing to re-select the game

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
  - Centralized default file extension (`.mp3`) to single constant
  - Replaced manual file sanitization loops with unified utility method
  - Reduced code duplication by ~40 lines across 9 files

### Fixed
- **Settings Integration with other Plugins** - Fixed music and video audio playing simultaneously after opening/closing settings
  - MediaElementsMonitor now properly updates settings reference when settings are saved
  - Runtime state properties (`VideoIsPlaying`, `ThemeOverlayActive`) no longer persisted to disk, preventing startup playback issues
  - Eliminated double-fire property change handlers that caused inconsistent pause states
- **Pause-on-X Settings Behavior** - Fixed stuck pause when disabling "Pause on Minimize/Focus Loss/System Tray" settings while their pause source is active
  - Pause source removal now unconditional on window state changes
  - Music properly resumes when re-enabling window after disabling pause setting

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
  - Uses `IsRestartRequired` Playnite pattern instead of manual process restart

### Fixed
- **Window State Pause Settings** - Fixed issues with pause settings at startup ([#51](https://github.com/aHuddini/UniPlaySong/issues/51), [#27](https://github.com/aHuddini/UniPlaySong/issues/27))
  - Music no longer plays briefly when Playnite launches minimized or in system tray
  - Music now correctly auto-plays when window is restored after starting minimized
  - "Pause on Focus Loss", "Pause on Minimize", and "Pause when in System Tray" now work reliably at startup when used along Desktop Pause/Play media controls

## [1.2.2] - 2026-01-17

### Added
- **Download Manager with Review Mode** - Correct wrong album picks after auto-download
  - Click "Review Downloads" to enter Review Mode, click games to re-download with different albums
  - Games you've corrected show orange highlighting for easy tracking
- **Auto-Add More Songs** - One-click bulk song expansion in Review Mode
  - Adds 1-3 random songs per game from matched albums (works for successful AND skipped games)
  - Newly downloaded songs play automatically as they complete
- **Batch Manual Download Dialog** - Unified dialog for retrying failed games
  - Cover thumbnails, custom search box, preview button, green checkmarks for progress
- **Parallel Download Processing** - Up to 4 concurrent downloads, starts immediately as albums are found
- **Music Playback Controls** - Pause/Resume button in download dialog footer
- **SoundCloud Support** - New hints-only source for search hints database
  - Add `soundcloudUrl` field to search_hints.json entries for direct SoundCloud downloads
  - Supports both single tracks and playlists/sets
  - Uses yt-dlp for reliable downloads with rate limiting
- **Auto-Check for Hints Updates** - Optional startup check for search hints database updates
  - Compares your local hints database with the latest GitHub version
  - Notifies when new entries are available.
  - Toggle in Settings → Search Cache → "Check for updates on startup"
- **Bundled Hints Fallback** - When downloaded hints lack direct links, automatically falls back to bundled hints during download operations.

### Fixed
- **Search Hints Priority** - Reordered hint sources: KHInsider (best quality) → SoundCloud → YouTube (last resort)

### Performance
- **Search Cache** - 90% smaller cache files with automatic migration from old format

## [1.2.1] - 2026-01-17

### Improved
- **YouTube Search Reliability** - Significantly improved auto-download success rate
  - Updated internal Youtube parser client version to 2025 iteration
  - Added dual-format playlist parser supporting both modern `lockupViewModel` and legacy `playlistRenderer` response formats
  - Enhanced property fallbacks for title, channel, thumbnail, and video count extraction
  - Addressed unicode bug in search results #48
- **Dedicated Download Logging** - Cleaner logging separation
  - New `downloader.log` file in extension folder for all download-related operations
  - Automatic log rotation when exceeding 5MB (keeps one `.old` backup)
  - Session markers for batch download operations
  - Significantly reduced logging verbosity while maintaining important status messages

### Added
- **Now Playing Display** - Shows current song title, artist, and duration in Desktop top panel
  - Scrolling text animation for long titles
  - Click to open the music folder in Explorer
- **New Add-on Settings Options** (Settings → General)
  - **Show Now Playing (Desktop mode)** - Toggle the Now Playing song info display
  - **Show Media Controls (Desktop mode)** - Toggle the Play/Pause and Skip buttons
  - Both options are disabled by default; changing requires Playnite restart
- **New Search Hints: CONTRIBUTIONS WELCOME!** - Added YouTube playlist ID for Necromunda: Hired Gun (official YouTube Music album). 

## [1.2.0] - 2026-01-15

### Added
- **Desktop Top Panel Media Controls** - Play/Pause and Skip buttons in Playnite's top panel bar ([#5](https://github.com/aHuddini/UniPlaySong/issues/5))
  - **Play/Pause button** - Always visible, toggles music playback with standard media player conventions
    - Shows pause icon (⏸) when music is playing (click to pause)
    - Shows play icon (▶) when music is paused/stopped (click to play)
  - **Skip/Next button** - Skip to a random different song from the current game's music folder
    - Greyed out (30% opacity) when only one song is available
    - Full opacity and functional when 2+ songs are available
    - Automatically updates after downloading new music or auto-normalization
  - Uses IcoFont icons for visual consistency with Playnite's native UI
  - Survives Live Effects toggle (properly resubscribes to recreated playback service)

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
- **Special thanks to Mike Aniki** for his guidance and extensive testing help with getting this plugin to work with ANIKI REMAKE in mind! His theme can support UPS in the future.

## [1.1.8] - 2026-01-11

### Added
- **Toast Notifications** - New lightweight notification system for controller-mode operations
  - Custom Windows API blur effects (direct SetWindowCompositionAttribute calls, not WPF Blur Effect)
  - Color-coded accent bars: green for success, red for errors, blue for info
  - Non-intrusive positioning with smooth fade animations
  - Customizable appearance settings in new "Toast Notifications" settings tab
  - Specifically designed for controller-mode features to replace buggy confirmation dialogs
- **Toast Notification Customization (DEBUG ONLY, NOT IN RELEASE)** - Full control over notification appearance
  - Background opacity slider (0-100%)
  - Blur intensity control (0-25)
  - Corner radius adjustment (0-20px)
  - Custom accent color with RGB sliders and brightness adjustment
  - Border color customization with RGB sliders
  - Border thickness control (0-5px)
  - Live preview of all color settings
- **Custom Windows Blur System** - Direct Windows API blur infrastructure (Windows 10+ compatible)
  - `SetWindowCompositionAttribute` Windows API integration
  - `AccentPolicy` structure for blur/transparency control
  - Support for both Basic blur (Windows 10+) and Acrylic blur (Windows 10 1803+) modes
  - Extensible for future UI elements requiring native Windows blur effects

### Changed
- **Controller-Mode Dialog System** - Replaced problematic confirmation dialogs with toast notifications in controller mode
  - Removes disruptive modal dialogs that interrupted controller navigation workflow
  - Confirmation feedback now appears as subtle toast notifications
  - Improves user experience in fullscreen/controller mode operations

### Fixed
- **Settings Persistence** - Fixed critical bug where settings changes were not being saved
  - Settings like "Pause music when Playnite loses focus" and "Automatically download music for new games" now persist correctly
  - Root cause: ViewModel instance mismatch between GetSettings and GetSettingsView methods
  - Implemented PlayniteSound pattern: cached ViewModel initialization in constructor
  - Playnite now correctly sets DataContext without manual override
- **Controller Mode Dialog Windows** - Fixed dialog handling in Fullscreen/Controller mode
  - Dialogs now properly respect controller navigation context
  - Improved focus management when dialogs open and close
  - Fixed issues with dialogs becoming unresponsive or hidden
- **NAudio Default Music Crash** - Fixed crash when live effects are enabled with default music playback
  - Resolved Playnite crashes during looped default music playback with custom music
  - Improved stability when mixing live effects with default/fallback music sources

## [1.1.7] - 2026-01-10

### Fixed
- **Batch Download UI Freeze** - Progress dialog no longer freezes with large game counts
  - Replaced expensive LINQ-based progress counting with incremental counters (O(1) vs O(n))
  - Added throttled UI updates (max 10 updates/second) to prevent dispatcher flooding
  - Significantly improves responsiveness when downloading 100+ games
- **Download Music Playback** - Fixed shuffle behavior and queue management during downloads
  - Corrected random music playback interruptions during bulk operations
  - Improved music queue handling for better user experience

## [1.1.6] - 2026-01-10

### Added
- **UPS Hints in Manual Search** - Search hints now appear at the top of manual album search results
  - Gold/orange color-coded highlighting makes hint albums easy to identify
  - Hints from search_hints.json displayed with "★ UPS Hint" label
  - Available in both Desktop and Fullscreen/Controller modes
- **Auto-Download Uses Search Hints First** - Search hints are now prioritized in auto-download
  - `BestAlbumPick` now checks for hint albums before fuzzy matching
  - Games with configured YouTube playlists or KHInsider albums in search_hints.json will use those directly
  - Significantly improves auto-download success rate for problematic game names
- **Auto-Download Playback Resume** - Music playback now resumes during/after batch downloads
  - Playback starts immediately when the currently-selected game's music downloads successfully
  - If current game isn't in the batch, playback resumes after batch completes
- **Manual Search Summary** - Summary popup now appears after completing manual retry for failed downloads
  - Shows total games attempted, successful downloads, and skipped/cancelled games
  - Lists game names for easy reference
- **Auto-Search Hint Database** - New settings section for managing the search hints database
  - Download latest search_hints.json from GitHub to get updated hints
  - Hints are stored in AutoSearchDatabase folder in extension data
  - Shows download status with entry count and last update time
  - Open Database Folder button for easy access to downloaded hints
  - Revert to Bundled option to delete downloaded hints and use bundled version

### Changed
- Renamed "User Hint" to "UPS Hint" throughout the UI for better branding
- Added "GOG Cut" to game name suffix stripping (e.g., "Daggerfall Unity - GOG Cut" → "Daggerfall Unity")

### Fixed
- Search hints not being used during auto-download despite being configured
  - Hint albums now have `Type = "Hint"` flag for proper identification
  - `BestAlbumPick` and `BestAlbumPickBroader` now return hint albums immediately without fuzzy matching
- Removed "Continue with remaining games?" popup during manual retry after auto-download failures
  - Canceling manual search now silently continues to the next game
- Manual download no longer interrupts preview playback
  - Downloaded music won't auto-start if a preview is currently playing

## [1.1.5] - 2026-01-09

### Added
- **Zophar Integration** - New download source for video game music
  - Added Zophar.net as second-priority download source between KHInsider and YouTube
  - Specializes in retro gaming soundtracks and emulated format rips
  - Increases auto-download success rates for games not available on KHInsider
  - Full caching and search hint support for problematic game names
- **Search Hints Backend** - Intelligent game name resolution system
  - Dual-source system: bundled curated hints + user-editable custom hints
  - Provides direct album/playlist links for problematic games that cause search issues
  - Supports fuzzy matching, base name matching, and exact lookups
- **Revamped Auto-Download Operation** - Improved bulk download performance and UX
  - New dedicated GUI dialog for auto-download operations with real-time progress
  - Parallel task execution for search and download operations
  - Improved error handling and retry logic for failed downloads
  - Better visual feedback during multi-game bulk operations
- **Delete Long Songs** cleanup tool - Scans music library and deletes songs longer than 10 minutes
  - Helps remove accidentally downloaded full albums, podcasts, or corrupted files
  - Prevents bulk operations (normalization, trimming) from getting stuck on excessively long files
  - Shows preview of files to be deleted with duration and total size before confirmation
  - Uses progress dialog to prevent UI freeze during scan
- **Import from PlayniteSound & Delete** migration option - Clean migration that imports music then removes PlayniteSound originals
  - Two-step process: imports all music, then deletes original PlayniteSound files
  - Includes double-confirmation to prevent accidental data loss
  - Cleans up empty "Music Files" folders and game directories after deletion

### Fixed
- Back button in batch download album selection now properly returns to source selection ([#36](https://github.com/aHuddini/UniPlaySong/issues/36))
  - Previously displayed "BACK_SIGNAL" text instead of allowing source change
  - Now shows source selection dialog (KHInsider/YouTube) when pressing Back

## [1.1.4] - 2026-01-07

### Added
- **Live Effects: Audacity-Compatible Reverb** - Professional-grade reverb using libSoX/Freeverb algorithm
  - 18 Audacity factory presets: Acoustic, Ambience, Artificial, Clean, Modern, Vocal I/II, Dance Vocal, Modern Vocal, Voice Tail, Bathroom, Small Room Bright/Dark, Medium Room, Large Room, Church Hall, Cathedral, Big Cave
  - 7 UniPlaySong environment presets: Living Room, Home Theater, Late Night TV, Lounge/Cafe, Jazz Club, Night Club, Concert Hall
  - New parameters: Reverberance (tail length), Tone Low/High (post-reverb EQ), Stereo Width
  - Extended Wet Gain range to +10 dB for more pronounced effects
- **Live Effects: Effect Chain Ordering** - Configurable processing order with 6 preset orderings
- **Live Effects: Advanced Reverb Tuning** - Expert-mode controls for algorithm parameters
  - Wet Gain Multiplier (0.01-0.25) - Controls overall reverb intensity
  - HF Damping Min/Max - Controls brightness/darkness range of damping slider
  - Includes safety warnings about hearing damage risks
- **Amplify Audio** feature with waveform-based gain adjustment editor
  - Visual waveform display with real-time gain preview
  - Clipping indicator shows when gain would exceed 0dBFS
  - Headroom display shows maximum safe gain
  - Supports gain range of -12dB to +12dB in 0.5dB steps
  - Controller-friendly version for Fullscreen mode (D-Pad Up/Down for fine adjustment, LB/RB for coarse)
  - Original files preserved in PreservedOriginals folder
- Repair Music Folder option in Fullscreen mode context menu
- Repair Audio File option with controller-friendly file picker in Fullscreen mode
- Music status tags: Games are now tagged with "[UPS] Has Music" or "[UPS] No Music" for easy filtering in Playnite ([#18](https://github.com/aHuddini/UniPlaySong/issues/18)). Auto-tags on library update with manual "Scan & Tag All Games" button in settings

### Changed
- Live Effects reverb algorithm now uses Reverberance (not Room Size) for feedback calculation, matching Audacity behavior
- Reverb wet gain multiplier changed to 0.03 for balanced reverb intensity (adjustable via Advanced Tuning)
- Reverb tuning constants extracted and documented in EffectsChain.cs for easy modification

### Fixed
- Room size slider changes now apply in real-time during playback (previously required song restart)
- Stop() no longer incorrectly triggers MediaEnded event in NAudio player
- Music now properly restarts when toggling Live Effects on/off (instead of just stopping)

## [1.1.3] - 2026-01-02

### Added
- Pause music when in system tray: New option to pause music when Playnite is hidden in the system tray ([#27](https://github.com/aHuddini/UniPlaySong/issues/27))
- Parallel bulk normalization: Audio normalization now processes up to 3 files in parallel for significantly faster bulk operations ([#25](https://github.com/aHuddini/UniPlaySong/issues/25))
- Parallel bulk silence trimming: Silence trim operations also now process up to 3 files in parallel
- Dialog windows now appear in Windows taskbar (Desktop mode): Easy to find dialogs when switching between applications ([#28](https://github.com/aHuddini/UniPlaySong/issues/28))

### Changed
- Simplified bulk normalization progress dialog by removing success/fail counters ([#26](https://github.com/aHuddini/UniPlaySong/issues/26))
- FFmpeg path setting consolidated to Audio Normalization tab only (removed duplicate from Downloads tab) ([#30](https://github.com/aHuddini/UniPlaySong/issues/30))

### Fixed
- Normalization progress showing "0/0" after downloads ([#29](https://github.com/aHuddini/UniPlaySong/issues/29))
- Dialog windows getting "lost" when switching to other programs in Desktop mode ([#28](https://github.com/aHuddini/UniPlaySong/issues/28))
- Back button in Desktop download dialogs now properly navigates through the full flow (Song → Album → Source) instead of closing the dialog ([#31](https://github.com/aHuddini/UniPlaySong/issues/31))

## [1.1.2] - 2026-01-01

### Added
- Precise Trim (Waveform Editor): Visual waveform-based audio trimming with draggable start/end markers, real-time duration display, and preview functionality. Desktop mode uses mouse-draggable markers; Fullscreen mode supports full Xbox controller navigation (D-Pad for markers, LB/RB for symmetric adjust, A=Preview, B=Cancel, Start=Apply)
- Factory Reset & Cleanup Tools: New settings tab (Settings → Cleanup) with storage info display, Delete All Music, Reset Settings, and Factory Reset options. Double-confirmation dialogs prevent accidental data loss
- Auto-Download on Library Update: Automatically download music when new games are added ([#17](https://github.com/aHuddini/UniPlaySong/issues/17)). Uses intelligent album/song selection, tries KHInsider first with YouTube fallback
- Download Music for All Games: Bulk download button with non-blocking progress dialog, cancellation support, and summary of results
- Auto-Normalize After Download: Option to automatically normalize downloaded music using configured settings ([#20](https://github.com/aHuddini/UniPlaySong/issues/20))
- Audio Repair Tool: Fix problematic audio files that fail to play by re-encoding to 48kHz stereo format

### Changed
- "Pause when minimized" is now enabled by default for new installations ([#20](https://github.com/aHuddini/UniPlaySong/issues/20))
- Desktop context menu reorganized with "Audio Processing" and "Audio Editing" submenus for better organization
- Renamed menu items for consistency: "Normalize Single Song", "Normalize Music Folder", "Repair Music Folder"

### Fixed
- Music not playing after adding files to game (required restart before)
- Desktop download preview audio overlap with game music
- YouTube downloads failing to play due to encoding issues (unusual sample rates/channels)
- YouTube JSON parsing "Path returned multiple tokens" error
- Bulk download using stale cache results
- Improved bulk download success rate with simplified game name search (strips edition suffixes like "Definitive Edition" when full name search fails)
- KHInsider albums not found for games with colons in names (e.g., "Hitman: Absolution" now correctly finds "Hitman Absolution" album)

## [1.1.1] - 2025-12-15

### Added
- Individual Song Processing: Normalize or silence-trim individual songs from context menu ([#16](https://github.com/aHuddini/UniPlaySong/issues/16))
- Open Preserved Folder button in Normalization settings ([#16](https://github.com/aHuddini/UniPlaySong/issues/16))

### Changed
- Removed redundant per-game buttons from Normalization tab (use context menu instead)
- Renamed trim options to "Silence Trim" for clarity
- Extracted dialog handlers and common utilities into dedicated files for maintainability

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
- Download From URL: Download music from specific YouTube URLs with preview ([#10](https://github.com/aHuddini/UniPlaySong/issues/10))
- PlayniteSound Migration: Bidirectional import/export between PlayniteSound and UniPlaySong
- New Migration tab in settings

### Changed
- Reorganized context menu with logical groupings
- Enhanced rate limiting for yt-dlp commands

## [1.0.8] - 2025-11-01

### Added
- Debug Logging toggle to reduce log verbosity ([#3](https://github.com/aHuddini/UniPlaySong/issues/3))
- Rate limiting for downloads ([#6](https://github.com/aHuddini/UniPlaySong/issues/6))
- Firefox Cookies Support for YouTube downloads
- JavaScript Runtime Support (Deno) for yt-dlp 2025.11.12+
- Customizable Trim Suffix
- Long Audio File Warning (>10 minutes)
- Automatic Filename Sanitization

### Fixed
- Double "MB MB" suffix in download dialogs ([#7](https://github.com/aHuddini/UniPlaySong/issues/7))
- Topmost windows blocking other apps in Desktop mode ([#8](https://github.com/aHuddini/UniPlaySong/issues/8))
- Failed downloads persisting across batch runs ([#9](https://github.com/aHuddini/UniPlaySong/issues/9))
- Music not playing immediately after download
- Preview threading InvalidOperationException
- FFmpeg process deadlock during normalization/trimming
- Normalization in non-English locales (decimal separator issues)
- Default music not playing after downloading for another game
- MP4 files being renamed to MP3

### Changed
- Simplified Multi-Game Context Menu with automatic KHInsider→YouTube fallback
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
