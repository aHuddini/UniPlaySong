# UniPlaySong v1.0.8 Release Notes

## Bug Fixes

- **Fixed double "MB MB" suffix in download dialogs** ([#7](https://github.com/aHuddini/UniPlaySong/issues/7))
- **Fixed Topmost windows blocking other apps in Desktop mode** ([#8](https://github.com/aHuddini/UniPlaySong/issues/8)) - Dialogs now only use Topmost in Fullscreen mode
- **Fixed failed downloads persisting across batch runs** ([#9](https://github.com/aHuddini/UniPlaySong/issues/9)) - Previous failures no longer carry over to new batch downloads
- **Fixed music not playing immediately after download** - Downloads now trigger automatic music refresh
- **Fixed preview threading issue** - Resolved `InvalidOperationException` when stopping previews
- **Fixed FFmpeg process deadlock** - Audio normalization/trimming no longer freezes at "analyzing music"
- **Fixed normalization in non-English locales** - Decimal separator issues (German, French, etc.) resolved
- **Fixed default music not playing** after downloading music for another game
- **Fixed MP4 files being renamed to MP3** - Simplified cookies command now properly extracts audio

## New Features

- **Reduced logging verbosity** ([#3](https://github.com/aHuddini/UniPlaySong/issues/3)) - Added "Enable Debug Logging" toggle in settings; verbose logs now only appear when enabled
- **Rate limiting for downloads** ([#6](https://github.com/aHuddini/UniPlaySong/issues/6)) - Added delays between batch downloads and preview requests to avoid server throttling
- **Firefox Cookies Support** - New option for YouTube downloads to improve reliability
- **JavaScript Runtime Support (Deno)** - yt-dlp 2025.11.12+ requires Deno/Node.js/QuickJS for YouTube
- **Customizable Trim Suffix** - Match your trimming suffix with normalization suffix
- **Long Audio File Warning** - Warns when processing files >10 minutes
- **Automatic Filename Sanitization** - Special characters fixed for FFmpeg compatibility

## UI Improvements

- Simplified multi-game context menu - uses "Download All" with automatic KHInsider→YouTube fallback
- Better UI labels - all trim features clearly labeled as "Silence Trimming"
- Improved progress tracking - clear distinction between succeeded/skipped/failed operations

---

## Installation

1. Download `UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890_1_0_8.pext` from the release assets
2. Open Playnite
3. Go to **Add-ons → Extensions**
4. Click **"Install from file"** and select the `.pext` file

## Requirements

- **yt-dlp** - For downloading music from YouTube/KHInsider
- **Deno** (recommended) - Required JavaScript runtime for yt-dlp 2025.11.12+
- **FFmpeg** - For audio normalization and processing

> **Note**: Place `deno.exe` in the same folder as `yt-dlp.exe` for automatic detection.
