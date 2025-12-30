# UniPlaySong v1.1.1 Release Notes

## 🎵 What's New

### Features
- **Individual Song Processing** ([#16](https://github.com/aHuddini/UniPlaySong/issues/16)) - Normalize or silence-trim individual songs from context menu
  - Desktop mode: Windows file picker dialog
  - Fullscreen mode: Controller-friendly file picker with D-pad navigation
- **Open Preserved Folder** ([#16](https://github.com/aHuddini/UniPlaySong/issues/16)) - New button in Normalization settings to open backup folder of original songs that were edited by users.

### Improvements
- **Settings Tab Cleanup** ([#16](https://github.com/aHuddini/UniPlaySong/issues/16)) - Removed redundant per-game buttons from Normalization tab (use context menu instead)
- **Clearer Menu Labels** - Renamed trim options to "Silence Trim" for clarity (e.g., "Silence Trim - Single Song", "Silence Trim - Audio Folder")
- **Fixed PreservedOriginals Path** - "Open Preserved Folder" now opens the correct backup location
- **Code Refactoring** - Extracted dialog handlers (~1,270 lines) and common utilities (~370 lines) into dedicated files (UniPlaySong.cs was 2000+ lines of code). Updated developer documentation. Further refactoring planned.

## 📥 Installation

### Option 1: Direct Download (Recommended)
1. Download `UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890_1_1_1.pext`
2. Double-click the file or drag it onto Playnite
3. Playnite will automatically install/update the extension

### Option 2: Manual Installation
1. Open Playnite → Add-ons → Extensions
2. Click "Add extension" and select the downloaded `.pext` file

## 🔧 Requirements

- **yt-dlp** - For downloading music ([download here](https://github.com/yt-dlp/yt-dlp/releases))
- **Deno** - Required JS runtime ([download here](https://deno.com/))
- **FFmpeg** - For audio processing ([download here](https://ffmpeg.org/download.html))

## 📋 Full Changelog

See the complete changelog at: https://github.com/aHuddini/UniPlaySong/blob/main/CHANGELOG.md

## 🐛 Issues & Feedback

Found a bug or have a feature request? Please [open an issue](https://github.com/aHuddini/UniPlaySong/issues) on GitHub!

---

*Enjoy your enhanced music management experience! 🎮🎵*