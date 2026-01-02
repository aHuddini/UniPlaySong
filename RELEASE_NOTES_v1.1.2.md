# UniPlaySong v1.1.2 Release Notes

## 🎵 What's New

### Major New Features
- **🎯 Precise Trim (Waveform Editor)** - Visual waveform-based audio trimming with draggable start/end markers, real-time duration display, and preview functionality
  - **Desktop mode**: Mouse-draggable markers for precise trimming
  - **Fullscreen mode**: Full Xbox controller navigation (D-Pad for markers, LB/RB for symmetric adjust, A=Preview, B=Cancel, Start=Apply)
  - **Real-time preview**: Hear your trim selection before applying changes

- **🧹 Factory Reset & Cleanup Tools** - New "Cleanup" settings tab for maintenance operations
  - Storage information display showing disk usage
  - "Delete All Music" - Remove all music files with confirmation
  - "Reset Settings" - Restore default settings
  - "Factory Reset" - Complete reset with double-confirmation dialogs
  - Double-confirmation dialogs prevent accidental data loss

- **🤖 Auto-Download on Library Update** ([#17](https://github.com/aHuddini/UniPlaySong/issues/17)) - Automatically download music when new games are added
  - Intelligent album/song selection algorithm
  - Tries KHInsider first with YouTube fallback
  - Configurable in Settings → Downloads

- **📦 Download Music for All Games** - Bulk download functionality
  - Non-blocking progress dialog with cancellation support
  - Summary of successful/failed/skipped downloads
  - Available in Settings → Downloads

- **🔧 Auto-Normalize After Download** ([#20](https://github.com/aHuddini/UniPlaySong/issues/20)) - Option to automatically normalize newly downloaded music
  - Uses your configured normalization settings
  - Runs in background after successful downloads
  - Toggle in Settings → Audio Normalization

- **🛠️ Audio Repair Tool** - Fix problematic audio files that fail to play after download
  - Re-encodes files to 48kHz stereo format
  - Available for individual files or entire folders
  - Fixes codec compatibility issues

### Improvements
- **📱 Desktop Context Menu Reorganization** - Cleaner menu structure with "Audio Processing" and "Audio Editing" submenus
- **🏷️ Consistent Menu Labels** - Renamed for clarity: "Normalize Single Song", "Normalize Music Folder", "Repair Music Folder"
- **⚙️ Better Defaults** - "Pause when minimized" is now enabled by default for new installations ([#20](https://github.com/aHuddini/UniPlaySong/issues/20))

## 📥 Installation

### Option 1: Direct Download
1. Download `UniPlaySong-1_1_2-Jan01_2026.pext` from GitHub releases
2. Double-click the downloaded file to install

### Option 2: Playnite Add-on Database
Download or update directly from the Playnite add-on database, or browse Generic plugins in Playnite's Add-ons menu

## 🔧 Requirements

| Tool | Purpose | Download |
|------|---------|----------|
| **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** | Downloading music | [GitHub Releases](https://github.com/yt-dlp/yt-dlp/releases) |
| **[Deno](https://deno.com/)** | Required JS runtime for yt-dlp | [Deno.com](https://deno.com/) |
| **[FFmpeg](https://ffmpeg.org/download.html)** | Audio processing | [Official Website](https://ffmpeg.org/download.html) |

> **Note**: Place `deno.exe` in the same folder as `yt-dlp.exe` for automatic detection.

## 📋 Full Changelog

See the complete changelog at: https://github.com/aHuddini/UniPlaySong/blob/main/CHANGELOG.md

## 🎮 New Features Demo

### Waveform Trimming
The new waveform editor provides pixel-perfect audio trimming with visual feedback:

- **Visual Waveform**: See the audio amplitude over time
- **Draggable Markers**: Click and drag start/end points
- **Real-time Duration**: See exact trim duration as you adjust
- **Audio Preview**: Hear your selection before applying
- **Controller Support**: Full Xbox controller navigation in fullscreen mode

### Auto-Download Features
- **Smart Detection**: Automatically detects new games in your library
- **Intelligent Selection**: Chooses appropriate music based on game metadata
- **Fallback System**: KHInsider → YouTube automatic fallback
- **Bulk Operations**: Download music for your entire library at once

### Cleanup & Maintenance
- **Storage Overview**: See exactly how much space your music library uses
- **Safe Operations**: Double-confirmation dialogs prevent accidents
- **Flexible Options**: Reset settings, delete music, or complete factory reset

## 🐛 Issues & Feedback

Found a bug or have a feature request? Please [open an issue](https://github.com/aHuddini/UniPlaySong/issues) on GitHub!

---

*Enjoy the enhanced audio editing and management capabilities! 🎵✂️🎮*