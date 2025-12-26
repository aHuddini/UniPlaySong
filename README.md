# UniPlaySong Playnite Extension

![Version](https://img.shields.io/badge/version-1.0.8-blue) ![License](https://img.shields.io/badge/license-MIT-green)

A Playnite extension that provides a console-like game music preview experience and controller-friendly song management, with custom options to fine-tune the experience (like fade-in and fade-out controls). Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens.

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

---

## 🆕 What's New - v1.0.8 (Upcoming Release)

- **🔥 Firefox Cookies Support**: New option to use Firefox browser cookies for YouTube downloads - greatly improves reliability for international users
- **⚡ Deno JavaScript Runtime**: yt-dlp now requires Deno (or Node.js/QuickJS) - place `deno.exe` in the same folder as `yt-dlp.exe`
- **🐛 Critical Bug Fixes**: Fixed FFmpeg process deadlock causing normalization/trimming to freeze at "analyzing music" with files more than 10 minutes long
- **🌍 Normalization Locale Fix**: Fixed normalization failing in non-English locales (German, French, etc.) due to decimal separator issues
- **🎵 Default Music Fix**: Fixed issue where default music wouldn't play when switching to games with no music after downloads
- **🔧 MP4 to MP3 Fix**: Fixed issue where simplified cookies command was downloading MP4 files instead of MP3 audio
- **🎮 Simplified Multi-Game Menu**: Streamlined context menu for multiple game selection - removed individual source options, simplified to "Download All"
- **⚠️ Long Audio Warnings**: Alerts when processing files >10 minutes to set user expectations
- **🔧 Automatic Filename Sanitization**: Special characters in filenames are automatically fixed for FFmpeg compatibility
- **✨ Customizable Trim Suffix**: Match your trimming suffix with normalization suffix (e.g., "-trimmed")
- **📊 Improved Progress Tracking**: Clearer distinction between succeeded/skipped/failed operations with accurate file counts
- **🏷️ Better UI Labels**: All trim features now clearly labeled as "Silence Trimming" to avoid confusion
-**⚠️** Right-click context menu UI clean-up.

**KNOWN BUGS**
-The add-on settings play state (never, desktop, fullscreen, both) doesn't work reliably depending on certain functions you use. Music will still play no matter what options you typically pick. Will fix later. Workaround: Uncheck "ENABLE MUSIC" if you wish to disable the music features.

## Previous Version - v1.0.7

- **✂️ Silence Trimming**: Automatically remove leading silence from your game music files
  - Clean up downloaded tracks that have unwanted silence at the start
  - Enhances fade effects for seamless music preview transitions
  - Configurable detection settings for fine-tuning
  - Works seamlessly with audio normalization

## v1.0.6

- **🔊 Audio Normalization**: Normalize all game music to consistent volume levels using FFmpeg
  - Uses standard FFmpeg only (FFmpeg-normalize not required)
  - EBU R128 standard support with customizable target loudness, true peak, and loudness range
  - Bulk normalization support for entire game libraries
  - Accessible from both desktop and fullscreen modes
- **🎮 Fullscreen Controller Support**: Complete Xbox controller support for music management in fullscreen mode
  - Download music, delete songs, normalize audio, and set primary songs - all from your couch!
  - Never leave fullscreen mode to manage your game music library
- **🔊 Default / Fallback Music Integration**: Use Playnite's built-in background music or a custom background track as your default music

---

## 🎬 Demo
https://github.com/user-attachments/assets/d7a9964e-fa2e-4d66-8de7-9ff16b1010de

---

## 🎵 Features

### 🎮 Fullscreen Mode with Xbox Controller Support
Complete controller-friendly interface for managing game music in fullscreen mode:
- Download individual tracks/albums directly from fullscreen menu using Xbox controller
- Controller-optimized dialogs for all music management tasks
- Preview tracks with controller buttons (X/Y buttons)
- Set primary songs or delete songs
- Normalize audio files directly from fullscreen menu
- **Never leave fullscreen mode** - complete music management from your couch!

<img src="DEMOScreen1.png" alt="Demo Screenshot" width="600">

### 🎼 Core Features
- **Automatic Music Playback**: Music plays when selecting games in your library
- **Custom Preview Time**: Customize songs to play for 15 seconds, 30 seconds, or the entire length of a file.
- **Configurable Song Randomization**: Randomize songs when selecting a game or when song loops end
- **Smooth, customizable Fade Transitions**: Edit fade-in and fade-out effects for song playback when switching games
- **Audio Normalization**: Normalize all game music to consistent volume levels (EBU R128 standard or with custom values). Uses standard FFmpeg only (FFmpeg-normalize not required).
- **Default Music Support**: Fallback music that seamlessly plays when games have no music, with natural position preservation
- **Theme Compatibility**: Works with fullscreen themes that have login screens
- **Online Audio Downloads**: Download and preview game music directly from online sources (either in Fullscreen or desktop mode)
- **Primary Songs**: Set "default" songs for each game that plays when you launch Playnite.

---

## 📋 Requirements

Before installing, ensure you have the following tools:

| Tool | Purpose | Download |
|------|---------|----------|
| **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** | Required for downloading music from YouTube/KHInsider | [GitHub Releases](https://github.com/yt-dlp/yt-dlp/releases) |
| **[Deno](https://deno.com/)** | **Required JavaScript runtime for yt-dlp** (recommended) | [Deno.com](https://deno.com/) or [GitHub Releases](https://github.com/denoland/deno/releases) |
| **[FFmpeg](https://ffmpeg.org/download.html)** | Required for audio normalization and processing | [Official Website](https://ffmpeg.org/download.html) |

> **⚠️ Important**: yt-dlp version 2025.11.12+ now **requires** an external JavaScript runtime (Deno, Node.js, or QuickJS) for YouTube downloads. **Deno is recommended**. Place `deno.exe` in the **same folder as yt-dlp.exe** for automatic detection.

---

## 📦 Installation

1. Download the latest `.pext` file from [Releases](https://github.com/yourusername/UniPlaySong/releases)
2. Open Playnite
3. Go to **Add-ons → Extensions**
4. Click **"Install from file"** and select the `.pext` file

### Initial Setup

After installation, configure the required tools for full functionality:

1. **Install yt-dlp, Deno, and FFmpeg**:
   - Download yt-dlp from [GitHub releases](https://github.com/yt-dlp/yt-dlp/releases)
   - **Download Deno** (recommended JavaScript runtime) from [deno.com](https://deno.com/) or [GitHub releases](https://github.com/denoland/deno/releases)
     - **Important**: Extract `deno.exe` and place it in the **same folder as yt-dlp.exe**
     - yt-dlp will automatically detect Deno when it's in the same directory
     - Alternative: Node.js (v20+) or QuickJS if Deno doesn't work
   - Download FFmpeg from the [official website](https://ffmpeg.org/download.html) or use a package manager
   - Extract/install all tools to locations of your choice

2. **Configure Paths in Extension Settings**:
   - On Desktop mode, go to Playnite Settings → **Add-ons → Extension Settings → Generic → UniPlaySong**
   - Navigate to the **Downloads** Tab
   - Set the **yt-dlp Path** to point to your `yt-dlp.exe` file
   - Set the **FFmpeg Path** to point to your `ffmpeg.exe` file
   - **Optional but Recommended**: Check **"Use cookies from browser (Firefox)"** to improve download reliability
     - Make sure Firefox is installed and you're logged into YouTube in Firefox
     - This helps bypass YouTube bot detection and greatly improves download success rates

> **⚠️ Note**: Without yt-dlp and FFmpeg configured, the extension will not be able to download music or normalize audio files. Without Deno (or another JS runtime), YouTube downloads will fail with bot detection errors.

---

## ⚙️ Settings

### General
| Setting | Description |
|---------|-------------|
| **Enable Music** | Turn music playback on/off |
| **Play Music State** | Never / Desktop / Fullscreen / Always |
| **Do not play music on startup** | Skip first game selection |
| **Theme Compatible Login Skip** | Wait for keyboard/controller input before playing music (for login/Welcome screens used in themes like ANIKI REMAKE) |
| **Music Volume** | 0-100% |
| **Fade-In Duration** | How long music takes to fade in | 0.05 - 10.0 seconds |
| **Fade-Out Duration** | How long music takes to fade out | 0.05 - 10.0 seconds |

#### Preview Music Options
| Setting | Description | Range |
|---------|-------------|-------|
| **Enable Preview Mode** | When enabled, game music tracks restart after the preview duration instead of playing continuously | On/Off |
| **Preview Duration** | How long each game music track plays before restarting (in seconds) | 15-300 seconds |

> 💡 **Preview Mode**: When enabled, songs play for the specified duration (e.g., 15, 30, or 60 seconds), then restarts, giving you a taste of each track without waiting for the full song. Pair it with custom fade effects. This only affects game-specific music and does not apply to the fallback default music.

#### Song Randomization
| Setting | Description |
|---------|-------------|
| **Randomize song upon selection** | Choose a random song each time you select a game (after primary song plays) |
| **Randomize song when current song ends** | Automatically play different songs when tracks finish |

### Default Music
| Setting | Description |
|---------|-------------|
| **Enable Default Music** | Play fallback music when games have no music files |
| **Default Music Path** | Select a music file to use as default/fallback music |
| **Suppress Native Background Music** | Suppress Playnite's native background music when extension music plays |
| **Use Native Playnite Music as Default** | Uses Playnite's default music background when selecting games with no custom preview music |

### Audio Normalization
| Setting | Description | Default |
|---------|-------------|---------|
| **Target Loudness** | Target loudness in LUFS (EBU R128 standard) | -16.0 |
| **True Peak** | True peak limit in dBTP | -1.5 |
| **Loudness Range** | Loudness range in LU | 11.0 |
| **Normalization Suffix** | Customizable suffix labels appended to normalized files | "-normalized" |
| **Skip Already Normalized** | Automatically skip files that are already normalized | ✓ |
| **Do Not Preserve Originals** | Space saver mode - replace originals directly (no backup) | - |
| **Restore Original Files** | Restore original files from preserved backups | - |
| **Delete Preserved Originals** | Free up disk space by deleting preserved files | - |

### Silence Trimming
| Setting | Description | Default |
|---------|-------------|---------|
| **Silence Threshold** | Audio level below which is considered silence (in dB) | -50.0 |
| **Minimum Duration** | Minimum silence duration to detect (in seconds) | 0.1 |
| **Silence Trim Suffix** | Customizable suffix appended to trimmed files | "-trimmed" |
| **Skip Already Trimmed** | Automatically skip files that are already trimmed | ✓ |

### Search Cache
| Setting | Description | Range |
|---------|-------------|-------|
| **Enable Search Cache** | Cache search results to speed up subsequent song searches | On/Off |
| **Cache Duration** | How long search results are cached before expiring (in days) | 1-30 days |

> 💡 **Search Cache**: When enabled, the extension caches album search results to avoid redundant API calls. If a game has no results on search source, the cache remembers this and skips directly to YouTube on subsequent searches.

### Music Downloads
| Setting | Description |
|---------|-------------|
| **yt-dlp Path** | Path to yt-dlp executable |
| **FFmpeg Path** | Path to ffmpeg executable (required for audio normalization) |
| **Use cookies from browser (Firefox)** | Enable to use Firefox cookies for YouTube downloads (recommended) |
| | - Improves download reliability and bypasses bot detection |
| | - Requires Firefox installed and logged into YouTube |
| | - Uses simplified yt-dlp command for better compatibility |

## 📁 Music Folder Structure

Music files are stored in:
```
%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\
```

**Supported formats**: MP3, WAV, OGG, FLAC, M4A, WMA

---

## 🎮 Usage

### Desktop Mode

| Action | Method |
|-------|--------|
| **Adding Music** | Right-click a game → **UniPlaySong → Download Music** |
| **Setting Primary Song** | Right-click a game → **UniPlaySong → Set Primary Song** |
| **Opening Music Folder** | Right-click a game → **UniPlaySong → Open Music Folder** |

### Fullscreen Mode with Xbox Controller 🎮

UniPlaySong provides complete controller support for fullscreen gaming. Essential music management tasks can be performed without leaving fullscreen mode.

#### How to Access UniPlaySong in Fullscreen Mode

1. Select a game in your library
2. Press the **Menu/Context button** on your Xbox controller (or right-click with mouse)
3. Navigate to **Extensions → UniPlaySong**
4. Select from the available options:
   - **Download Music (🎮 Mode)** - Browse and download tracks/albums
   - **Set Primary Song (🎮 Mode)** - Choose which song plays first
   - **Remove Primary Song (🎮 Mode)** - Clear the primary song
   - **Delete Songs (🎮 Mode)** - Remove unwanted music files
   - **Normalize Selected Music** - Normalize audio files for consistent volume

#### Controller Features

| Feature | Description |
|--------|-------------|
| **Download Music (🎮 Mode)** | Browse and download tracks/albums using your Xbox controller |
| **Set Primary Song (🎮 Mode)** | File picker optimized for controller navigation |
| **Remove Primary Song (🎮 Mode)** | Quick removal with controller |
| **Delete Songs (🎮 Mode)** | Safe deletion with confirmation dialog |
| **Normalize Selected Music** | Normalize audio files directly from fullscreen menu |

#### Controller Mappings

| Button | Action |
|-------|--------|
| **A Button** | Select/Confirm/Download |
| **B Button** | Back/Cancel |
| **X/Y Buttons** | Preview audio (automatically pauses game music) |
| **D-Pad** | Navigate lists |
| **LB/RB** | Page through results |
| **LT/RT** | Jump to top/bottom of lists |

> 🎨 Controller GUI dialogs use Material Design and are designed to be navigated with a TV/monitor in mind.

---

## 🔐 Theme Compatible Login Skip

For fullscreen themes with login screens (like ANIKI), enable **"Theme Compatible Login Skip"**:

1. Open **Settings → UniPlaySong**
2. Check **"Theme Compatible Login Skip"**
3. When you enter fullscreen, no music plays until you press **Enter/Space/Escape/Return** (or controller equivalent) to dismiss the login screen

---

## 🛠️ Building from Source

**Complete build and package workflow:**

```powershell
# Navigate to project directory
cd [drive]:\<folder>\UniPlaySong

# Clean, restore, and build
dotnet clean -c Release
dotnet restore
dotnet build -c Release

# Package the extension (with execution policy bypass)
powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
```

For detailed build instructions and developer documentation, see the `docs/dev_docs/` folder in the repository.

---

## 🙏 Credits

### Inspiration

**UniPlaySong is heavily inspired by the [PlayniteSound](https://github.com/joyrider3774/PlayniteSound) extension.**

I originally built this extension as I wanted to manage individual game select music purely from fullscreen (since I'm normally not at my desktop and stream my games remotely with a controller). This helps me greatly when dealing with failures with auto-searching and downloading music. I can correct individual games a bit more faster through fullscreen mode now.

Special thanks and credit to the original PlayniteSound developer for creating the foundation and architecture that inspired this project. I struggled getting this plugin off the ground even with AI-assisted tools. PlayniteSound was critical to helping me apply proven coding patterns and business logic that worked with the Playnite application. Their foundation is what allowed me to experiment further.

### Third-Party Libraries and Dependencies

This extension uses the following open-source libraries and frameworks:

| Library/Framework | License | Purpose |
|-------------------|---------|---------|
| **[Playnite SDK](https://github.com/JosefNemec/Playnite)** | MIT | Playnite extension API and SDK |
| **[SDL2](https://www.libsdl.org/)** | zlib | Audio playback and native music suppression |
| **[MaterialDesignThemes](https://github.com/MaterialDesignInXAMLToolkit/MaterialDesignInXAMLToolkit)** | MIT | Modern UI components for controller-optimized dialogs |
| **[MaterialDesignColors](https://github.com/MaterialDesignInXAMLToolkit/MaterialDesignInXAMLToolkit)** | MIT | Material Design color palette |
| **[HtmlAgilityPack](https://html-agility-pack.net/)** | MIT | HTML parsing for KHInsider downloads |
| **[Newtonsoft.Json](https://www.newtonsoft.com/json)** | MIT | JSON serialization |
| **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** | Unlicense | YouTube music downloads |
| **[FFmpeg](https://ffmpeg.org/)** | LGPL/GPL | Audio normalization and processing |

**License Notices:**
- All third-party libraries retain their original licenses
- SDL2 uses the zlib license (permissive, commercial-friendly)
- Material Design guidelines are licensed under CC BY 4.0
- Full license texts are available in the respective project repositories

### Development

This extension was built with the assistance of AI tools (Cursor IDE, Claude Code + Sonnet 4 and Opus 4.5 models as the primary agent drivers).

---

## 📄 License

MIT License - See [LICENSE](LICENSE) file

---

## 🔗 Links

- **GitHub Repository**: [UniPlaySong](https://github.com/aHuddini/UniPlaySong)
- **Playnite Sound Extension**: [PlayniteSound](https://github.com/joyrider3774/PlayniteSound)
- **Playnite**: [Official Website](https://playnite.link/)
