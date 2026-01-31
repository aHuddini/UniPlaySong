# Changelog

All notable changes to UniPlaySong will be documented in this file.

## [1.2.4] - 2026-01-30

### Added
- **Auto-Delete Music on Game Removal** - Music files are now automatically deleted when their associated games are removed from Playnite ([#59](https://github.com/aHuddini/UniPlaySong/issues/59))
  - Subscribes to Playnite's `Games.ItemCollectionChanged` event for real-time cleanup
  - Stops playback if the currently playing game is removed
  - Enabled by default; toggle in Settings → Cleanup tab
- **Clean Up Orphaned Music** - New button in Settings → Cleanup to scan for and remove music folders belonging to games no longer in your library
  - Shows count of orphaned folders before confirming deletion
  - Useful for cleaning up music left behind from games removed before this feature existed

### Changed
- **Playnite SDK** updated to version 6.15.0

### Fixed
- **Fullscreen Background Music Volume** - Playnite's fullscreen "Background Music Volume" slider now controls UniPlaySong's volume ([#62](https://github.com/aHuddini/UniPlaySong/issues/62))
  - Effective volume = plugin volume × Playnite background volume, applied in real-time
  - Setting Playnite's volume to 0 fully mutes music
  - Native Playnite music is always suppressed in fullscreen to prevent SDL2_mixer conflicts
  - Desktop mode is unaffected
- **Audio Stuttering During Video Playback** - Fixed music repeatedly pausing and resuming during trailer/ScreenshotVisualizer video playback ([#58](https://github.com/aHuddini/UniPlaySong/issues/58), [#60](https://github.com/aHuddini/UniPlaySong/pull/60)) - Credit: @rovri
  - Increased MediaElementsMonitor timer interval from 10ms to 100ms to prevent race conditions with video framerates

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
