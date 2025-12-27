# UniPlaySong - Changelog

## Version 1.0.8 (Latest)

### Bug Fixes
- **Fixed double "MB MB" suffix in download dialogs** ([#7](https://github.com/aHuddini/UniPlaySong/issues/7))
- **Fixed Topmost windows blocking other apps in Desktop mode** ([#8](https://github.com/aHuddini/UniPlaySong/issues/8)) - Dialogs now only use Topmost in Fullscreen mode
- **Fixed failed downloads persisting across batch runs** ([#9](https://github.com/aHuddini/UniPlaySong/issues/9)) - Previous failures no longer carry over to new batch downloads
- **Fixed music not playing immediately after download** - Downloads now trigger automatic music refresh
- **Fixed preview threading issue** - Resolved `InvalidOperationException` when stopping previews
- **Fixed FFmpeg process deadlock** - Audio normalization/trimming no longer freezes at "analyzing music"
- **Fixed normalization in non-English locales** - Decimal separator issues (German, French, etc.) resolved
- **Fixed default music not playing** after downloading music for another game
- **Fixed MP4 files being renamed to MP3** - Simplified cookies command now properly extracts audio

### Features
- **Reduced logging verbosity** ([#3](https://github.com/aHuddini/UniPlaySong/issues/3)) - Added "Enable Debug Logging" toggle; verbose logs now only appear when enabled
- **Rate limiting for downloads** ([#6](https://github.com/aHuddini/UniPlaySong/issues/6)) - Added delays between batch downloads and preview requests to avoid server throttling
- **Firefox Cookies Support** - New option for YouTube downloads to improve reliability
- **JavaScript Runtime Support (Deno)** - yt-dlp 2025.11.12+ requires Deno/Node.js/QuickJS for YouTube
- **Customizable Trim Suffix** - Match your trimming suffix with normalization suffix
- **Long Audio File Warning** - Warns when processing files >10 minutes
- **Automatic Filename Sanitization** - Special characters fixed for FFmpeg compatibility

### UI Improvements
- **Simplified Multi-Game Context Menu** - Removed individual source options, uses "Download All" with automatic KHInsider→YouTube fallback
- **Better UI Labels** - All trim features clearly labeled as "Silence Trimming"
- **Improved Progress Tracking** - Clear distinction between succeeded/skipped/failed operations

## Version 1.0.7

### Features
- **Silence Trimming** - Automatically remove leading silence from audio files using FFmpeg
  - Configurable silence detection threshold and duration
  - Smart file naming that preserves operation order
  - Works seamlessly with audio normalization

## Version 1.0.6

### Features
- **Audio Normalization** - Normalize game music to consistent volume levels using FFmpeg
  - EBU R128 standard with customizable settings
  - Bulk normalization for entire game libraries
- **Fullscreen Controller Support** - Complete Xbox controller support for music management
- **Native Music Integration** - Use Playnite's built-in background music as default

### Improvements
- Enhanced native music suppression reliability
- Better theme compatibility for fullscreen modes

## Version 1.0.5

### Features
- **Native Music Control** - Option to use Playnite's native music as default or suppress it

## Version 1.0.4

### Features
- **Primary Song System** - Set a "primary" song that plays first when selecting a game
- **Universal Fullscreen Support** - Works with any Playnite theme
- **Improved File Management** - Better file dialogs and error handling

### Bug Fixes
- Fixed music not stopping when switching to games without music
- Fixed music not playing when switching back to games with music
- Fixed file locking issues when selecting music files
