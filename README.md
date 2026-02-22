# UniPlaySong Playnite Extension

![Version](https://img.shields.io/badge/version-1.3.2-blue) ![License](https://img.shields.io/badge/license-MIT-green)

<p align="center">
  <img src="docs/assets/GHdisplay.png" alt="UniPlaySong" width="150">
</p>

<p align="center">
  <a href="https://ko-fi.com/Z8Z11SG2IK">
    <img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="ko-fi">
  </a>
</p>

A Playnite extension that provides a console-like game music preview experience with controller-friendly song management. Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens.

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

Built with the help of Claude Code and Cursor IDE

---

## What's New - v1.3.2

- **Taskbar Thumbnail Media Controls** - Previous, Play/Pause, and Next buttons in the Windows taskbar hover preview. Play/Pause icon reflects playback state (Desktop mode only). Graduated from Experimental.
- **Global Media Key Control** (Experimental) - Control music with keyboard media keys (Play/Pause, Next, Previous, Stop).
- **Auto-Cleanup Empty Folders** - Automatically removes empty game music directories after song deletion.
- **M3U Playlist Export** - Export per-game, multi-game, or library-wide M3U playlists for external players (VLC, foobar2000, MPC).
- **Extended Default Music Sources** - Three new default music fallbacks: Custom Folder (Playlist), Random Game, and Custom Game Rotation — with optional "continue same song" across game switches.
- **PS2 Menu Ambience** - New bundled default music preset. Four ambient tracks now ship with the plugin.
- **Custom Cookies File** - New cookie source option for yt-dlp downloads. Use a Netscape-format cookies.txt from any browser, alongside the existing Firefox option.
- **Per-Tab Reset Buttons** - Each settings tab now has its own "Reset to Defaults" button.
- **Improved Default Settings** - Better out-of-box experience with media controls, visualizer, live effects (Rehearsal), auto-tagging, song randomization, and completion celebration all enabled by default.
- **Graduated from Experimental** - Taskbar Thumbnail Media Controls (→ General), Random Game Picker Music (→ Playback), Celebration Toast (→ Playback), External Audio Detection (→ Pauses), Idle/AFK Pause (→ Pauses), System Lock Pause (→ Pauses).
- **Settings Reorganization** - Dedicated "Pauses" tab, cleaner labels, collapsible Dynamic Color Tuning, and some streamlined General settings.
- **Style Preset Tuning** - All style presets capped at 1dB makeup gain to prevent clipping. Rehearsal preset updated with Reverb-first chain.
- **Bug Fix**: Settings no longer silently reset when using Browse/Select buttons in settings dialogs.
- **Bug Fix**: Play button now clears stale automatic pause sources (Idle, External Audio, System Lock).
- **Bug Fix**: System unlock now properly clears idle state and restores idle volume.

### Previous Versions
- **v1.3.1**: Install-Aware Auto-Download, Auto-Pause on External Audio, Auto-Pause on Idle/AFK, Random Game Picker Music, Stay Paused on Focus Restore, Ignore Brief Focus Loss, Enhanced Library Statistics, Settings UI Reorganization, Focus Loss Fade Fix
- **v1.3.0**: Completion Fanfare (11 retro jingle presets), Song Count Badge, Default Music Indicator, Song Progress Indicator, Download Complete Sound, Celebration Toast, Auto-Pause on System Lock, Enhanced Library Statistics (card grid), skip/crossfade fixes
- **v1.2.11**: Bundled default music presets, installed games only, hide now playing for default music, song list caching, parallel deletions, async UI
---

## 🎬 Demo
https://github.com/user-attachments/assets/d7a9964e-fa2e-4d66-8de7-9ff16b1010de

---

## 🎵 Features

- **Auto-Download** - Automatic music search and downloads for existing libraries and new games from YouTube, KHInsider, and Zophar's Domain
- **Playback Customization** - Fade effects, preview duration (15s-1min), random song selection
- **Live Reverb Effects** - Real-time reverb effects with 18 Audacity-derived presets and custom controls to enhance preview audio (pairs well with Fullscreen theme aesthetics)
- **Audio-Reactive Visualizer** - Real-time spectrum visualizer with 22 color themes including Dynamic game-art colors, tuning presets, and per-bar gradient rendering (Desktop mode)
- **Controller Support** - Full Xbox controller navigation for music management in fullscreen mode
- **Audio Editing** - Amplify/Trim tools, audio normalization, and batch operations
- **Theme Integration** - UPS_MusicControl for theme developers and compatibility with modern themes
- **Migration Support** - Seamless import/export from PlayniteSound with cleanup options
- **Desktop Controls** - Optional top panel media controls and Now Playing information
- **Tagging & Filters** - Tag games with music/no music for better music management

<img src="docs/assets/DEMOScreen1.png" alt="Demo Screenshot" width="600">

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

---

## 🎮 Usage & Settings

### Quick Start
1. Install yt-dlp, Deno, and FFmpeg (see Requirements above)
2. Right-click games (single or multiple) for music download and management options. Use "bulk download" in add-on settings for alternative bulk downloads
3. Use controller in fullscreen mode: **Menu → Extensions → UniPlaySong**

### Settings Tabs
- **General**: Music behavior, top panel media controls, taskbar thumbnail controls, now playing display, tags, troubleshooting
- **Playback**: Volume, fade effects, preview mode, song randomization, default music (6 sources including custom folder/random game/rotation pool), completion fanfare, random game picker music
- **Pauses**: Pause on play, system lock, focus loss, minimize, system tray, external audio, idle/AFK
- **Live Effects**: Real-time audio effects, reverb presets, spectrum visualizer
- **Audio Editing**: EBU R128 volume normalization and silence trimming
- **Downloads**: Tool paths, cookie source (none/Firefox/custom file), auto-download options, install-aware auto-download, bulk download
- **Search**: Search result caching and auto-search hints database
- **Migration**: Import/export from PlayniteSound
- **Cleanup**: Storage management, reset options, factory reset
- **Experimental**: Media key control, download notification sound, song progress bar, peak meter, library statistics with audio metrics

**Music Location**: `%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\`  
**Supported Formats**: MP3, WAV, OGG, FLAC, M4A, WMA

---

## 🛠️ Building from Source

```powershell
dotnet clean -c Release && dotnet restore && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\package_extension.ps1 -Configuration Release
```

See `docs/dev_docs/` for detailed build instructions.

---

## 🙏 Credits

**Inspired by [PlayniteSound](https://github.com/joyrider3774/PlayniteSound)** - Special thanks to the original developer for the foundation that made this project possible.

### Libraries & Dependencies
- **Playnite SDK** - Extension framework
- **SDL2 & SDL2_mixer** - Audio playback (zlib license)
- **MaterialDesignThemes** - WPF UI components (MIT)
- **HtmlAgilityPack, Newtonsoft.Json, NAudio, FuzzySharp** - Core functionality (MIT)
- **TagLibSharp** - Audio metadata (LGPL)
- **yt-dlp** - Media downloading (Unlicense)
- **FFmpeg** - Audio processing (LGPL/GPL)

### Third-Party Acknowledgments
See [LICENSE](LICENSE) file for component licenses and acknowledgments.

---

## 📄 License

MIT License - See [LICENSE](LICENSE) file

## 🔗 Links

- [GitHub Repository](https://github.com/aHuddini/UniPlaySong)
- [PlayniteSound](https://github.com/joyrider3774/PlayniteSound)
- [Playnite](https://playnite.link/)
