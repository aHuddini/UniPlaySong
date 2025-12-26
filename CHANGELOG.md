# UniPlaySong - Changelog

## Version 1.0.8 (Latest)

### Major Features
- **🔥 Firefox Cookies Support for YouTube Downloads**: New option to use Firefox browser cookies for YouTube downloads
  - Checkbox option in Downloads settings: "Use cookies from browser (Firefox)"
  - When enabled, uses simplified yt-dlp command: `--cookies-from-browser firefox -x --audio-format mp3`
  - Greatly improves download reliability and bypasses YouTube bot detection
  - Works seamlessly - just enable the option and ensure Firefox is installed and logged into YouTube
  - This is a major feature that significantly improves download success rates, especially in regions with strict bot detection

- **⚡ JavaScript Runtime Support (Deno)**: yt-dlp now requires an external JavaScript runtime for YouTube downloads
  - **Deno is now recommended** by yt-dlp (version 2025.11.12+)
  - Installation: Download Deno from https://deno.com/ or https://github.com/denoland/deno/releases
  - **Important**: Place the Deno executable (`deno.exe`) in the **same folder as yt-dlp.exe**
  - yt-dlp will automatically detect and use Deno when it's in the same directory
  - Alternative runtimes: Node.js (v20+) or QuickJS (if Deno doesn't work)
  - Extension provides helpful error messages if JS runtime is missing
  - Enhanced logging detects and reports when JavaScript runtime is active

### Bug Fixes
- **FIXED: Audio Normalization Failing in Non-English Locales**: Fixed critical issue where normalization would fail with FFmpeg parsing errors in locales using comma as decimal separator (e.g., German, French)
  - Issue: Numeric values were formatted using system locale, causing `-1.5` to become `-1,5` which FFmpeg couldn't parse
  - Error: `Error parsing filterchain 'loudnorm=I=-16,0:TP=-1,5:LRA=11,0'` - FFmpeg expects dot (`.`) as decimal separator
  - Solution: All numeric formatting for FFmpeg commands now uses `InvariantCulture` to ensure decimal separator is always `.`
  - Affects: `AudioNormalizationService.cs` - both analysis phase (first pass) and normalization phase (second pass)
  - This fix ensures normalization works correctly regardless of user's locale settings

- **FIXED: Default Music Not Playing After Downloads**: Fixed issue where default music wouldn't play when switching to games with no music after downloading music for another game
  - Issue: `EnableMusic` check was blocking default music playback even though default music is a fallback
  - Solution: Default music now plays even when `EnableMusic` is false (as intended)
  - Default music is properly detected and allowed to play as a fallback mechanism

- **FIXED: MP4 Files Being Renamed to MP3**: Fixed issue where simplified cookies command was downloading MP4 video files instead of MP3 audio
  - Issue: Simplified command without `-x --audio-format mp3` was downloading video files
  - Solution: Updated simplified command to include audio extraction: `--cookies-from-browser firefox -x --audio-format mp3 --ffmpeg-location="..." -o "..." <url>`
  - Now properly extracts audio to MP3 format that SDL2_mixer can play

### Critical Bug Fixes
- **FIXED: FFmpeg Process Deadlock**: Resolved critical deadlock in audio normalization and trimming that caused progress to freeze at "analyzing music"
  - Issue: `ReadToEnd()` was called before `WaitForExit()`, causing FFmpeg output buffers to fill and block indefinitely
  - Solution: Changed to asynchronous stream reading pattern to prevent buffer blocking
  - Affects: AudioNormalizationService.cs (analysis and normalization phases) and AudioTrimService.cs (detection and trim phases)
  - This fix resolves the primary cause of normalization appearing to "hang" or get stuck

### Features
- **Customizable Trim Suffix**: Users can now customize the suffix for trimmed files (default: "-trimmed")
  - New setting in add-on settings UI alongside normalization suffix
  - Suffix is applied consistently across trim and normalization operations
  - Preserves chronological operation order (e.g., "song-trimmed-normalized.mp3" or "song-normalized-trimmed.mp3")

- **Long Audio File Warning**: Warns users when normalizing long audio files (>10 minutes)
  - Detects audio duration using FFmpeg before starting normalization
  - Displays warning with duration in minutes (e.g., "Warning: Long file (46 min) - this may take a while...")
  - Helps users understand why processing may take several minutes
  - 2-second pause to ensure warning is visible

- **Automatic Filename Sanitization**: Files with problematic characters are automatically renamed for FFmpeg compatibility
  - Detects and replaces special characters that cause FFmpeg command-line issues
  - Problematic characters include: `[ ] ( ) { } ' " ` & | ; < > ! $ # %`
  - Characters are replaced with underscores and consecutive underscores are collapsed
  - Example: `[ENG SUB] Game (2024).mp3` → `_ENG_SUB__Game__2024_.mp3`
  - Shows "Sanitizing filename..." status in progress dialog

### UI Improvements
- **Simplified Context Menu for Multiple Game Selection**: Streamlined right-click context menu when multiple games are selected
  - Removed individual "KHInsider" and "YouTube" source options from multi-game menu
  - Now shows only: "Download Music (🎮 Mode)" and "Download All (X games)"
  - "Download All" uses `Source.All` which automatically tries KHInsider first, then falls back to YouTube
  - Cleaner, more intuitive interface for batch downloads

### Improvements
- **Simplified Suffix Logic**: Completely redesigned suffix handling for reliability and predictability
  - Removed complex suffix detection and reordering logic
  - Now uses simple append-only approach for consistent behavior
  - Suffix detection now uses case-insensitive search for better compatibility
  - Eliminates edge cases where suffixes weren't applied correctly

- **Improved Skip Tracking for Silence Trimming**: Files without leading silence are now properly tracked as skipped
  - Result messages now show: "Complete: X succeeded, Y skipped, Z failed"
  - Skipped files no longer counted as "succeeded" which was misleading
  - Progress messages show running count of skipped files
  - Clearer distinction between successful trim, skipped file, and failed operation
  - Completion dialogs now accurately reflect what happened with clear file renaming status:
    - Skipped files labeled: "not renamed - no leading silence or already trimmed"
    - Failed files labeled: "not renamed - processing error"
    - Success note: "Only successfully trimmed files have the suffix appended"
    - Shows list of skipped and failed files in detailed view
    - "Changes will take effect when game is re-selected" only shown if files were actually trimmed

- **Clearer UI Labels for Silence Trimming**: Updated all trim-related labels to clarify this is "silence trimming"
  - Settings UI now shows "Silence Trim Suffix" instead of "Trim Suffix"
  - Button labels changed to "Trim Silence - All Files" and "Trim Silence - Selected Games"
  - Context menu shows "Trim Leading Silence" to be explicit about what's being trimmed
  - Helps users understand this only removes leading silence, not general audio trimming

## Version 1.0.7

### Features
- **Silence Trimming**: Automatically remove leading silence from audio files using FFmpeg
  - Configurable silence detection threshold and duration
  - Enhances fade effects for seamless music preview transitions
  - Smart file naming that preserves operation order (normalize → trim or trim → normalize)
  - Option to skip already-trimmed files
  - Minimum silence duration threshold to avoid trimming very short silences
  - Works seamlessly with audio normalization - files can be both normalized and trimmed

## Version 1.0.6

### Features
- **Audio Normalization**: Normalize all game music to consistent volume levels using FFmpeg
  - Uses standard FFmpeg only (FFmpeg-normalize not required)
  - EBU R128 standard support with customizable target loudness, true peak, and loudness range
  - Bulk normalization support for entire game libraries
  - Option to preserve original files or replace them
  - Accessible from both desktop and fullscreen modes
- **Fullscreen Controller Support**: Complete Xbox controller support for managing music in fullscreen mode
  - Download music directly from fullscreen using controller
  - Delete songs with controller-friendly dialogs
  - Normalize audio files from fullscreen menu
  - Set primary songs without leaving fullscreen
- **Native Music Integration**: Use Playnite's native background music as default music option
- **Improved Settings**: Better organization and clearer options

### Improvements
- Enhanced native music suppression reliability
- Better theme compatibility for fullscreen modes
- Improved error handling and stability

## Version 1.0.5

### Features
- **Native Music Control**: Option to use Playnite's native music as default or suppress it
- **Settings Improvements**: New "Use Native Playnite Music as Default" option

### Improvements
- Optimized native music handling
- Better compatibility with themes that have login screens
- Improved music playback reliability

## Version 1.0.4

### Features
- **Primary Song System**: Set a "primary" song that plays first when selecting a game
- **Universal Fullscreen Support**: Works with any Playnite theme without modifications
- **Improved File Management**: Better file dialogs and error handling

### Bug Fixes
- Fixed music not stopping when switching to games without music
- Fixed music not playing when switching back to games with music
- Fixed file locking issues when selecting music files

