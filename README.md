# UniPlaySong Playnite Extension

![Version](https://img.shields.io/badge/version-1.0.6-blue) ![License](https://img.shields.io/badge/license-MIT-green)

A Playnite extension that provides a console-like game music preview experience with custom options to fine-tune the experience (like fade-in and fade-out controls). Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens.

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

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

![Demo Screenshot](DEMOScreen1.png)

### 🎼 Core Features
- **Automatic Music Playback**: Music plays when selecting games in your library
- **Custom Preview Time**: Customize songs to play for 15 seconds, 30 seconds, or the entire length of a file.
- **Configurable Song Randomization**: Randomize songs when selecting a game or when song loops end
- **Smooth, customizable Fade Transitions**: Edit fade-in and fade-out effects for song playback when switching games
- **Audio Normalization**: Normalize all game music to consistent volume levels (EBU R128 standard or with custom values). All using standard FFPMEG encoder.
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
| **[FFmpeg](https://ffmpeg.org/download.html)** | Required for audio normalization and processing | [Official Website](https://ffmpeg.org/download.html) |

---

## 📦 Installation

1. Download the latest `.pext` file from [Releases](https://github.com/yourusername/UniPlaySong/releases)
2. Open Playnite
3. Go to **Add-ons → Extensions**
4. Click **"Install from file"** and select the `.pext` file

### Initial Setup

After installation, configure the required tools for full functionality:

1. **Install yt-dlp and FFmpeg**:
   - Download yt-dlp from [GitHub releases](https://github.com/yt-dlp/yt-dlp/releases)
   - Download FFmpeg from the [official website](https://ffmpeg.org/download.html) or use a package manager
   - Extract/install both tools to a location of your choice

2. **Configure Paths in Extension Settings**:
   - On Desktop mode, go to Playnite Settings → **Add-ons → Extension Settings → Generic → UniPlaySong**
   - Navigate to the **Downloads** Tab
   - Set the **yt-dlp Path** to point to your `yt-dlp.exe` file
   - Set the **FFmpeg Path** to point to your `ffmpeg.exe` file

> **⚠️ Note**: Without these configured, the extension will not be able to download music or normalize audio files.

---

## ⚙️ Settings

### General
| Setting | Description |
|---------|-------------|
| **Enable Music** | Turn music playback on/off |
| **Play Music State** | Never / Desktop / Fullscreen / Always |
| **Music Volume** | 0-100% |

### Theme Compatibility
| Setting | Description |
|---------|-------------|
| **Do not play music on startup** | Skip first game selection |
| **Theme Compatible Login Skip** | Wait for keyboard/controller input before playing music (for login/Welcome screens used in themes like ANIKI REMAKE) |

### Song Randomization
| Setting | Description |
|---------|-------------|
| **Randomize song upon selection** | Choose a random song each time you select a game (after primary song plays) |
| **Randomize song when current song ends** | Automatically play different songs when tracks finish |

> 💡 **Smart Randomization**: Avoids playing the same song twice in a row. Only applies to game music (not default music).

### Preview Music Options
| Setting | Description | Range |
|---------|-------------|-------|
| **Enable Preview Mode** | When enabled, game music tracks restart after the preview duration instead of playing continuously | On/Off |
| **Preview Duration** | How long each game music track plays before restarting (in seconds) | 15-300 seconds |

> 💡 **Preview Mode**: When enabled, songs play for the specified duration (e.g., 15, 30, or 60 seconds) then restart, giving you a taste of each track without waiting for the full song. This only affects game-specific music and does not apply to the fallback default music.

### Default Music
| Setting | Description |
|---------|-------------|
| **Enable Default Music** | Play fallback music when games have no music files |
| **Default Music Path** | Select a music file to use as default/fallback music |
| **Position Preservation** | Default music resumes from saved position when switching between games |

### Fade Settings
| Setting | Description | Range |
|---------|-------------|-------|
| **Fade-In Duration** | How long music takes to fade in | 0.05 - 10.0 seconds |
| **Fade-Out Duration** | How long music takes to fade out | 0.05 - 10.0 seconds |

### Native Music Suppression
| Setting | Description |
|---------|-------------|
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

### Music Downloads
| Setting | Description |
|---------|-------------|
| **yt-dlp Path** | Path to yt-dlp executable |
| **FFmpeg Path** | Path to ffmpeg executable (required for audio normalization) |

### Search Cache
| Setting | Description | Range |
|---------|-------------|-------|
| **Enable Search Cache** | Cache search results to speed up subsequent song searches | On/Off |
| **Cache Duration** | How long search results are cached before expiring (in days) | 1-30 days |

> 💡 **Search Cache**: When enabled, the extension caches album search results to avoid redundant API calls. If a game has no results on on search source, the cache remembers this and skips directly to YouTube on subsequent searches.

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
cd C:\Projects\UniPSound\UniPlaySong

# Clean, restore, and build
dotnet clean -c Release
dotnet restore
dotnet build -c Release

# Package the extension (with execution policy bypass)
powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
```

**One-liner version:**

```powershell
cd C:\Projects\UniPSound\UniPlaySong; dotnet clean -c Release; dotnet restore; dotnet build -c Release; powershell -ExecutionPolicy Bypass -File .\package_extension.ps1 -Configuration Release
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
