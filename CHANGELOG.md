# Changelog

All notable changes to UniPlaySong will be documented in this file.

## [Unreleased]

### Added
- **Audacity-Compatible Reverb Presets** - 18 factory presets matching Audacity's reverb effect
  - General Purpose: Acoustic, Ambience, Artificial, Clean, Modern
  - Vocals: Vocal I, Vocal II, Dance Vocal, Modern Vocal, Voice Tail
  - Room Sizes: Bathroom, Small Room Bright/Dark, Medium Room, Large Room, Church Hall, Cathedral, Big Cave
- **Reverberance Parameter** - New slider controlling reverb tail length separately from Room Size (key for more pronounced effects)
- **Tone Low/High Parameters** - New sliders for bass and treble content of reverb (Audacity-style post-reverb EQ)
- **Wet Gain Extended Range** - Now allows +10 dB (was 0 dB max) for more pronounced reverb effects
- **Effect Chain Ordering** - Dropdown to configure effect processing order (6 preset orderings)

### Changed
- Reverb algorithm now uses Reverberance (not Room Size) for feedback calculation, matching Audacity behavior
- Reverb presets updated to use exact Audacity factory values
- Technical documentation updated with complete algorithm details

## [1.1.4] - 2026-01-04

### Added
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
