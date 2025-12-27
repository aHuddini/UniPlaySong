# UniPlaySong Playnite Extension

![Version](https://img.shields.io/badge/version-1.0.8-blue) ![License](https://img.shields.io/badge/license-MIT-green)

<p align="center">
  <img src="GHdisplay.png" alt="UniPlaySong">
</p>

A Playnite extension that provides a console-like game music preview experience with controller-friendly song management. Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens.

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

---

## 🆕 What's New - v1.0.8

### Bug Fixes
- Fixed double "MB MB" suffix in download dialogs ([#7](https://github.com/aHuddini/UniPlaySong/issues/7))
- Fixed Topmost windows blocking other apps in Desktop mode ([#8](https://github.com/aHuddini/UniPlaySong/issues/8))
- Fixed failed downloads persisting across batch runs ([#9](https://github.com/aHuddini/UniPlaySong/issues/9))
- Fixed music not playing immediately after download
- Fixed FFmpeg deadlock and normalization issues in non-English locales

### New Features
- **Reduced logging** ([#3](https://github.com/aHuddini/UniPlaySong/issues/3)) - Added "Enable Debug Logging" toggle
- **Rate limiting** ([#6](https://github.com/aHuddini/UniPlaySong/issues/6)) - Delays between downloads to avoid throttling
- **Firefox Cookies Support** - Improves YouTube download reliability
- **Deno JavaScript Runtime** - Required for yt-dlp 2025.11.12+

**Known Issue**: Play state setting doesn't work reliably. Use "Enable Music" toggle as workaround.

### Previous Version - v1.0.7
- **Silence Trimming**: Automatically remove leading silence from audio files

---

## 🎬 Demo
https://github.com/user-attachments/assets/d7a9964e-fa2e-4d66-8de7-9ff16b1010de

---

## 🎵 Features

- **Automatic Music Playback** - Music plays when selecting games
- **🎮 Full Controller Support** - Manage music entirely from fullscreen mode with Xbox controller
- **Custom Preview Time** - Play 15s, 30s, or full tracks
- **Fade Transitions** - Customizable fade-in/fade-out effects
- **Audio Normalization** - EBU R128 standard volume leveling
- **Silence Trimming** - Remove leading silence from tracks
- **Online Downloads** - Download from YouTube and KHInsider
- **Primary Songs** - Set default songs per game
- **Default/Fallback Music** - Play background music when games have no music
- **Theme Compatibility** - Works with login screen themes

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

1. Download the latest `.pext` file from [Releases](https://github.com/aHuddini/UniPlaySong/releases)
2. Open Playnite → **Add-ons → Extensions**
3. Click **"Install from file"** and select the `.pext` file

### Setup

1. Download yt-dlp, Deno, and FFmpeg
2. Place `deno.exe` in the same folder as `yt-dlp.exe`
3. In Playnite: **Settings → Add-ons → Extension Settings → UniPlaySong → Downloads Tab**
4. Set paths to `yt-dlp.exe` and `ffmpeg.exe`
5. Optional: Enable "Use cookies from browser (Firefox)" for better YouTube reliability

---

## 🎮 Usage

### Desktop Mode
Right-click a game → **UniPlaySong** → Download Music / Set Primary Song / Open Music Folder

### Fullscreen Mode
Press Menu button → **Extensions → UniPlaySong** → Access all features with controller

All music management features are available in fullscreen mode with full controller support.

---

## ⚙️ Settings Overview

Settings are accessible via **Add-ons → Extension Settings → UniPlaySong**:

- **General**: Enable/disable music, volume, fade durations, preview mode
- **Default Music**: Fallback music settings, native Playnite music integration
- **Audio Processing**: Normalization (EBU R128) and silence trimming settings
- **Downloads**: yt-dlp/FFmpeg paths, Firefox cookies option
- **Search Cache**: Cache search results to speed up downloads

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
