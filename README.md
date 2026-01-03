# UniPlaySong Playnite Extension

![Version](https://img.shields.io/badge/version-1.1.3-blue) ![License](https://img.shields.io/badge/license-MIT-green)

<p align="center">
  <img src="GHdisplay.png" alt="UniPlaySong" width="150">
</p>

A Playnite extension that provides a console-like game music preview experience with controller-friendly song management. Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens.

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

---

## What's New - v1.1.3

### Pause When in System Tray 
- When enabled, music now pauses when Playnite is hidden in the system tray
- Prevents music from playing on startup when Playnite launches minimized
- Configurable in General settings (enabled by default)

### Performance Boost: Parallel Processing
- Bulk Audio normalization now processes up to 3 files simultaneously (instead of 1 file at a time)
- Silence trimming also uses parallel processing for bulk operations
- Significantly faster bulk operations

### UI/UX Improvements
- Dialog windows now appear in your Windows taskbar (Desktop Mode)
- Back button in download dialogs now properly navigates through the full flow (Song → Album → Source)
- Required FFmpeg filepath now set in Audio Normalization tab only
- Fixed auto-normalize progress display after downloads

### Previous Versions
- **v1.1.2**: Precise Trim waveform editor, Cleanup tools, Auto-download, Bulk operations, Audio repair

---

## 🎬 Demo
https://github.com/user-attachments/assets/d7a9964e-fa2e-4d66-8de7-9ff16b1010de

---

## 🎵 Features

- **Automatic Music Playback** - Music plays when selecting games
- **Precise Audio Trimming** - Visual waveform editor with controller support
- **Full Controller Support** - Manage music entirely from fullscreen mode with Xbox controller
- **Custom Preview Time** - Play 15s, 30s, or full tracks
- **Fade Transitions** - Customizable fade-in/fade-out effects
- **Audio Normalization** - EBU R128 standard volume leveling
- **Silence Trimming** - Remove leading silence from tracks
- **Online Downloads** - Download from YouTube and KHInsider
- **Smart Auto-Download** - Automatically download music for new games
- **Bulk Operations** - Download music for all games at once
- **Audio Repair Tools** - Fix problematic audio files
- **Cleanup & Maintenance** - Factory reset and storage management tools
- **Primary Songs** - Set default songs per game
- **Default/Fallback Music** - Play background music when games have no music
- **Theme Compatibility** - Works with login screen themes
- **PlayniteSound Migration** - Import/export music between UniPlaySong and PlayniteSound

<img src="DEMOScreen1.png" alt="Demo Screenshot" width="600">

---

## 📋 Requirements

| Tool | Purpose | Download |
|------|---------|----------|
| **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** | Downloading music | [GitHub Releases](https://github.com/yt-dlp/yt-dlp/releases) |
| **[Deno](https://deno.com/)** | Required JS runtime for yt-dlp | [Deno.com](https://deno.com/) |
| **[FFmpeg](https://ffmpeg.org/download.html)** | Audio processing | [Official Website](https://ffmpeg.org/download.html) |

> **Note**: Place `deno.exe` in the same folder as `yt-dlp.exe` for automatic detection.

---

## 📦 Installation

### Option 1: Direct Download
1. Download the latest `.pext` file from [Releases](https://github.com/aHuddini/UniPlaySong/releases)
2. Double-click the downloaded file to install

### Option 2: Playnite Add-on Database
Download or update directly from the Playnite add-on database, or browse Generic plugins in Playnite's Add-ons menu

### Setup

1. Download yt-dlp, Deno, and FFmpeg
2. Place `deno.exe` in the same folder as `yt-dlp.exe`
3. In Playnite: **Settings → Add-ons → Extension Settings → UniPlaySong → Downloads Tab**
4. Set paths to `yt-dlp.exe` and `ffmpeg.exe`
5. Optional: Enable "Use cookies from browser (Firefox)" for better YouTube reliability

---

## 🎮 Usage

### Desktop Mode
Right-click a game → **UniPlaySong**:
- **Download Music** - Search and download from KHInsider/YouTube
- **Download From URL** - Paste a specific YouTube URL
- **Download for All Games** - Bulk download music for entire library
- **Audio Processing** → Normalize/Trim individual songs or folders
- **Audio Editing** → Precise trim with waveform editor, repair audio files
- **Set/Clear Primary Song** - Choose which song plays first
- **Open Music Folder** - Access game's music directory

### Fullscreen Mode
Press Menu button → **Extensions → UniPlaySong** → Access all features with controller

All music management features are available in fullscreen mode with full controller support.

---

## ⚙️ Settings Overview

Settings are accessible via **Add-ons → Extension Settings → UniPlaySong**:

- **General**: Enable/disable music, volume, fade durations, preview mode
- **Default Music**: Fallback music settings, native Playnite music integration
- **Audio Processing**: Normalization (EBU R128) and silence trimming settings
- **Downloads**: yt-dlp/FFmpeg paths, Firefox cookies option, auto-download settings
- **Search Cache**: Cache search results to speed up downloads
- **Migration**: Import/export music between PlayniteSound and UniPlaySong
- **Cleanup**: Storage info, delete all music, reset settings, factory reset

Music files are stored in: `%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\`

**Supported formats**: MP3, WAV, OGG, FLAC, M4A, WMA

---

## 🛠️ Building from Source

```powershell
dotnet clean -c Release && dotnet restore && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
```

See `docs/dev_docs/` for detailed build instructions.

---

## 🙏 Credits

**Inspired by [PlayniteSound](https://github.com/joyrider3774/PlayniteSound)** - Special thanks to the original developer for the foundation that made this project possible.

### Libraries
Playnite SDK, SDL2, MaterialDesignThemes, HtmlAgilityPack, Newtonsoft.Json, yt-dlp, FFmpeg

---

## 📄 License

MIT License - See [LICENSE](LICENSE) file

## 🔗 Links

- [GitHub Repository](https://github.com/aHuddini/UniPlaySong)
- [PlayniteSound](https://github.com/joyrider3774/PlayniteSound)
- [Playnite](https://playnite.link/)
