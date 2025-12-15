# UniPlaySong

A Playnite extension that provides a console-like game music preview experience with custom options to fine-tune the experience (like fade-in and fade-out controls). Music plays when browsing your game library, creating an immersive experience similar to PlayStation and Xbox game selection screens. 

Designed for both Desktop and Fullscreen mode, with compatibility to modern themes (like ANIKI REMAKE).

This extension was built with the assistance of AI tools (Cursor IDE, Claude Code + Sonnet 4 and Opus 4.5 models as the primary agent drivers).

## Features

- **Fullscreen Mode with Xbox Controller Support** 🎮: Complete controller-friendly interface for managing game music in fullscreen mode
  - Download individual tracks/albums directly from fullscreen menu using Xbox controller
  - Controller-optimized dialogs for all music management tasks
  - Preview tracks with controller buttons (X/Y buttons)
  - Set primary songs, remove songs, delete songs - all with controller navigation
  - Normalize audio files directly from fullscreen menu
  - Never leave fullscreen mode - complete music management from your couch!
- **Automatic Music Playback**: Music plays when selecting games in your library
- **Song Randomization**: Randomize songs when switching games or when songs end (configurable)
- **Smooth Transitions**: Fades between songs when switching games
- **Audio Normalization**: Normalize all game music to consistent volume levels (EBU R128 standard)
- **Default Music Support**: Fallback music that plays when games have no music, with position preservation
- **Theme Compatibility**: Works with fullscreen themes that have login screens (waits for keyboard/controller input)
- **Online Music Downloads**: Download game music directly from YouTube or KHInsider/YouTube (either in Fullscreen or desktop mode)
- **Primary Songs**: Set default songs for each game
- **Customizable**: Adjust volume, fade durations, and behavior

## Requirements

Before installing, ensure you have the following tools:

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** - Required for downloading music from YouTube/KHInsider
- **[FFmpeg](https://ffmpeg.org/download.html)** - Required for all other audio features like normalization

## Installation

1. Download the latest `.pext` file from Releases
2. Open Playnite
3. Go to Add-ons → Extensions
4. Click "Install from file" and select the `.pext` file

### Initial Setup

After installation, configure the required tools for full functionality:

1. **Install yt-dlp and FFmpeg**:
   - Download yt-dlp from [GitHub releases](https://github.com/yt-dlp/yt-dlp/releases)
   - Download FFmpeg from the [official website](https://ffmpeg.org/download.html) or use a package manager
   - Extract/install both tools to a location of your choice

2. **Configure Paths in Extension Settings**:
   - Open Playnite Settings → Add-ons → Extensions → UniPlaySong → Configure
   - Navigate to the **YouTube Downloads** section
   - Set the **yt-dlp Path** to point to your `yt-dlp.exe` file
   - Set the **FFmpeg Path** to point to your `ffmpeg.exe` file

**Note**: 
- Without these configured, the extension will not be able to work properly.

## Settings

### General
- **Enable Music**: Turn music playback on/off
- **Play Music State**: Never / Desktop / Fullscreen / Always
- **Music Volume**: 0-100%

### Theme Compatibility
- **Do not play music on startup**: Skip first game selection
- **Theme Compatible Login Skip**: Wait for keyboard/controller input before playing music (for login screens)

### Song Randomization
- **Randomize song upon selection**: Choose a random song each time you select a game (after primary song plays)
- **Randomize song when current song ends**: Automatically play different songs when tracks finish
- Smart randomization avoids playing the same song twice in a row
- Only applies to game music (not default music)

### Default Music
- **Enable Default Music**: Play fallback music when games have no music files
- **Default Music Path**: Select a music file to use as default/fallback music
- **Position Preservation**: Default music resumes from saved position when switching between games

### Fade Settings
- **Fade-In Duration**: How long music takes to fade in (0.05 - 10.0 seconds)
- **Fade-Out Duration**: How long music takes to fade out (0.05 - 10.0 seconds)

### Native Music Suppression
- **Suppress Native Background Music**: Suppress Playnite's native background music when extension music plays
- Automatically suppresses when using "Use Native Playnite Music as Default" option
- Optimized for consistent suppression during fullscreen startup

### Audio Normalization
- **Target Loudness**: Target loudness in LUFS (default: -16.0, EBU R128 standard)
- **True Peak**: True peak limit in dBTP (default: -1.5)
- **Loudness Range**: Loudness range in LU (default: 11.0)
- **Normalization Suffix**: Suffix appended to normalized files (default: "-normalized")
- **Skip Already Normalized**: Automatically skip files that are already normalized
- **Do Not Preserve Originals**: Space saver mode - replace originals directly (no backup)
- **Restore Original Files**: Restore original files from preserved backups
- **Delete Preserved Originals**: Free up disk space by deleting preserved files

### Music Downloads
- **yt-dlp Path**: Path to yt-dlp executable
- **FFmpeg Path**: Path to ffmpeg executable (required for audio normalization)

## Music Folder Structure

Music files are stored in:
```
%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\
```

Supported formats: MP3, WAV, OGG, FLAC, M4A, WMA

## Usage

### Desktop Mode
- **Adding Music**: Right-click a game → UniPlaySong → Download Music
- **Setting Primary Song**: Right-click a game → UniPlaySong → Set Primary Song
- **Opening Music Folder**: Right-click a game → UniPlaySong → Open Music Folder

### Fullscreen Mode with Xbox Controller 🎮
UniPlaySong provides complete controller support for fullscreen gaming. All music management tasks can be performed without leaving fullscreen mode.

**How to Access in Fullscreen Mode:**
1. Select a game in your library
2. Press the **Menu/Context button** on your Xbox controller (or right-click with mouse)
3. Navigate to **Extensions** → **UniPlaySong**
4. Select from the available options:
   - **Download Music (🎮 Mode)** - Browse and download tracks/albums
   - **Set Primary Song (🎮 Mode)** - Choose which song plays first
   - **Remove Primary Song (🎮 Mode)** - Clear the primary song
   - **Delete Songs (🎮 Mode)** - Remove unwanted music files
   - **Normalize Selected Music** - Normalize audio files for consistent volume

**Features:**
- **Download Music (🎮 Mode)**: Browse and download tracks/albums using your Xbox controller
  - Navigate sources (KHInsider/YouTube) with D-pad
  - Preview tracks with X/Y buttons (automatically pauses game music)
  - Download with A button
  - Navigate back with B button
- **Set Primary Song (🎮 Mode)**: File picker optimized for controller navigation
- **Remove Primary Song (🎮 Mode)**: Quick removal with controller
- **Delete Songs (🎮 Mode)**: Safe deletion with confirmation dialog
- **Normalize Selected Music**: Normalize audio files directly from fullscreen menu

**Controller Mappings**:
- **A Button**: Select/Confirm/Download
- **B Button**: Back/Cancel
- **X/Y Buttons**: Preview audio (automatically pauses game music)
- **D-Pad**: Navigate lists
- **LB/RB**: Page through results
- **LT/RT**: Jump to top/bottom of lists

All dialogs use Material Design and are optimized for TV/monitor viewing distances.

## Theme Compatible Login Skip

For fullscreen themes with login screens (like ANIKI), enable "Theme Compatible Login Skip":

1. Open Settings → UniPlaySong
2. Check "Theme Compatible Login Skip"
3. When you enter fullscreen, no music plays until you press Enter/Space/Escape/Return (or controller equivalent) to dismiss the login screen



## Building from Source

```powershell
cd UniPSong
dotnet build -c Release
.\package_extension.ps1
```

The package script automatically handles version management. For detailed build instructions and developer documentation, see the `docs/` folder in the repository.

## Credits

**UniPlaySong is 100% inspired by the [PlayniteSound](https://github.com/Lacro59/PlayniteSound) extension.**

I originally built this extension as I wanted to manage individual game select music purely from fullscreen (since I'm normally not at my desktop and stream my games remotely with a controller). 

Special thanks and credit to the original PlayniteSound developer for creating the foundation and architecture that inspired this project. I struggled getting this plugin off the ground even with AI-assisted tools. PlayniteSound was critical to helping me apply proven coding patterns and business logic that worked with the Playnite application. Their foundations is what allowed me to experiment further.

Built for the Playnite community with gratitude to the PlayniteSound project.

## License

MIT License - See LICENSE file
