# UniPlaySong Developer Guide

## Overview

This guide provides technical documentation for developers working on or building the UniPlaySong extension.

## Table of Contents

1. [Architecture](#architecture)
2. [Build and Packaging](#build-and-packaging)
3. [Key Components](#key-components)
4. [Technical Implementation](#technical-implementation)
5. [Troubleshooting](#troubleshooting)

---

## Architecture

### Core Components

**UniPlaySong.cs** - Main plugin entry point
- Handles Playnite events (`OnGameSelected`, `OnApplicationStarted`, etc.)
- Delegates all playback decisions to `MusicPlaybackCoordinator`
- Manages settings, UI integration, and service initialization
- Initializes SDL2MusicPlayer (primary) or falls back to MusicPlayer (WPF)

**MusicPlaybackCoordinator** - Centralized playback coordination
- Single source of truth for "should play" decisions
- Handles skip logic (first select, login screen, mode switching)
- Manages state: `_firstSelect`, `_loginSkipActive`, `_skipFirstSelectActive`
- Coordinates between Playnite events and playback service
- Implements `ShouldPlayMusic()` gatekeeper method

**MusicPlaybackService** - High-level music management
- Coordinates between fader, player, and file service
- Handles game-to-song mapping and primary song logic
- Manages default music with position preservation (single-player architecture)
- Uses `Fader.Switch()` for all music transitions (game→game, game→default, default→game)
- State tracking: `_isPlayingDefaultMusic`, `_lastDefaultMusicPath`, `_defaultMusicPausedOnTime`

**MusicPlayer / SDL2MusicPlayer** - Audio playback
- **SDL2MusicPlayer** (Primary): SDL2-based for reliable volume control
  - Native P/Invoke wrappers in `Players/SDL/`
  - Synchronous volume changes, no WPF threading issues
- **MusicPlayer** (Fallback): WPF MediaPlayer wrapper with preloading
  - Dual-player preloading system for seamless switching
- Both support `Play(TimeSpan startFrom)` for position-based playback

**MusicFader** - Volume transitions (`Players/MusicFader.cs`)
- Exponential fade curves: `progress²` (fade-in), `1-(1-progress)²` (fade-out)
- Uses `Switch()` pattern for all music transitions
- Supports separate fade-in and fade-out durations
- Time-based calculation with dynamic frequency tracking
- Uses `Dispatcher.Invoke` (blocking) for reliable timing

### Data Flow

```
OnGameSelected(game)
  → MusicPlaybackCoordinator.HandleGameSelected(game, isFullscreen)
    → Checks skip conditions (first select, login skip, mode switch)
    → If should play: MusicPlaybackService.PlayGameMusic(game, settings)
      → Selects song (game music or default music)
      → Checks if default music already playing (preserves position)
      → Uses Fader.Switch() for smooth transitions
        → Preload during fade-out → Stop current → Fade in new
```

**Skip Logic Flow**:
```
First Game Selection
  → SkipFirstSelectionAfterModeSwitch: Skip first selection after mode switch
  → ThemeCompatibleSilentSkip: Wait for keyboard/controller input
    → OnLoginDismissKeyPress → Coordinator.HandleLoginDismiss()
      → Resume music playback
```

---

## Build and Packaging

### Prerequisites

- .NET Framework 4.6.2 Developer Pack
- Visual Studio 2019+ (or .NET SDK)
- NuGet package source configured

### Build

```powershell
cd UniPSong
dotnet build -c Release
```

### Package

```powershell
.\package_extension.ps1
```

The packaging script:
- Reads version from `version.txt`
- Updates `AssemblyInfo.cs` and `extension.yaml`
- Copies all required DLLs (including SDL2 native DLLs)
- Creates `.pext` file for installation

See `BUILD_INSTRUCTIONS.md` for detailed steps.

---

## Key Components

### Music Playback System

**Single-Player Architecture**:
- One `IMusicPlayer` instance handles both game music and default music
- State tracking flags: `_isPlayingDefaultMusic`, `_lastDefaultMusicPath`, `_defaultMusicPausedOnTime`
- Position preservation: Saves position when switching from default to game music, restores when switching back
- When switching between games with no music: Default music continues playing (no restart)

**Fade System**:
- All transitions use `Fader.Switch()` pattern for consistency
- Exponential curves: `progress²` (fade-in), `1-(1-progress)²` (fade-out)
- Time-based calculation with dynamic frequency tracking
- Preload action executes during fade-out for seamless transitions

**Default Music**:
- Fallback music when games have no music files
- Position preserved when switching between games (`_defaultMusicPausedOnTime`)
- Smooth fade transitions when switching to/from game music
- Continues playing when switching between games with no music (no restart)
- See `archive/DEFAULT_MUSIC_IMPLEMENTATION.md` for detailed technical docs

### SDL2 Audio Player

**Why SDL2?**:
- Reliable volume control (no WPF threading issues)
- Works consistently in fullscreen mode
- Synchronous volume changes
- Native library - no WPF dependency

**Implementation**:
- Native P/Invoke wrappers in `Players/SDL/` (`SDL.cs`, `SDL_mixer.cs`)
- Preloading support for seamless switching
- Position tracking: `Mix_GetMusicPosition()` and `Mix_SetMusicPosition()`
- Falls back to WPF MediaPlayer if SDL2 initialization fails
- See `archive/SDL2_IMPLEMENTATION.md` for migration details

---

## Technical Implementation

### Settings System

Settings use `ObservableObject` from Playnite SDK:
```csharp
public class UniPlaySongSettings : ObservableObject
{
    private bool enableMusic = true;
    public bool EnableMusic
    {
        get => enableMusic;
        set { enableMusic = value; OnPropertyChanged(); }
    }
}
```

### File Management

**Music Files**:
```
%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\
```

**Primary Song Metadata**:
```
{GameDirectory}\.primarysong.json
```

**File Service**:
- `GameMusicFileService` handles file discovery and primary song management
- Supports multiple audio formats: MP3, WAV, OGG, FLAC, M4A, WMA
- Primary song selection determines which song plays first for each game

### Error Handling

- Uses Playnite's `ILogger` for standard logging
- `FileLogger` for detailed debug logs
- Errors logged but don't crash the extension

---

## Troubleshooting

### Build Issues

**"Reference assemblies for .NETFramework,Version=v4.6.2 were not found"**
- Install .NET Framework 4.6.2 Developer Pack

**"Unable to find package PlayniteSDK"**
- Configure NuGet package source (nuget.org)

### Runtime Issues

**Music doesn't play**
- Check Playnite logs: `%AppData%\Playnite\playnite.log`
- Check extension logs: `%AppData%\Playnite\Extensions\{ExtensionId}\UniPlaySong.log`
- Verify music files exist in game directory
- Check settings: Enable Music, Play Music State

**Fade-out not working**
- Verify SDL2 DLLs are included in package
- Check fade duration settings (should be > 0.05 seconds)
- Review logs for fade-related errors

**Default music restarts**
- Should preserve position - check logs for position save/restore
- Verify `_defaultMusicPausedOnTime` is being saved correctly
- Check if default music is already playing (should continue, not restart)
- Verify `_isPlayingDefaultMusic` flag is set correctly

**Skip logic not working**
- Check coordinator logs for skip state
- Verify `SkipFirstSelectionAfterModeSwitch` or `ThemeCompatibleSilentSkip` settings
- Check if `_firstSelect` flag is being cleared too early

---

## Development Principles

1. **PlayniteSound is the Reference**: Check `src/PlayniteSound/` first
2. **Keep It Simple**: Match proven patterns, don't add unnecessary complexity
3. **Test Incrementally**: Small, focused changes
4. **Document Decisions**: Technical details in docs, not CHANGELOG

---

## File Structure

```
UniPSong/
├── UniPlaySong.cs              # Main plugin entry point
├── UniPlaySongSettings.cs      # Settings model (ObservableObject)
├── UniPlaySongSettingsView.xaml # Settings UI
├── Services/
│   ├── IMusicPlaybackCoordinator.cs
│   ├── MusicPlaybackCoordinator.cs  # Centralized playback coordination
│   ├── IMusicPlaybackService.cs
│   ├── MusicPlaybackService.cs      # High-level music management
│   ├── IMusicPlayer.cs
│   ├── MusicPlayer.cs               # WPF MediaPlayer (fallback)
│   ├── SDL2MusicPlayer.cs           # SDL2 player (primary)
│   ├── GameMusicFileService.cs      # File management
│   └── DownloadDialogService.cs     # Download UI
├── Players/
│   ├── MusicFader.cs                # Volume transitions
│   └── SDL/
│       ├── SDL.cs                   # SDL2 P/Invoke
│       └── SDL_mixer.cs              # SDL2_mixer P/Invoke
├── Downloaders/                     # Music download implementations
├── Menus/                           # Context menu handlers
├── Monitors/                        # UI integration (video detection)
└── Common/
    ├── FileLogger.cs                # Debug logging
    └── PrimarySongManager.cs        # Primary song metadata
```

## Additional Resources

- **Architecture Details**: See `archive/` for historical analysis
- **Feature Implementation**: See `archive/DEFAULT_MUSIC_IMPLEMENTATION.md`
- **SDL2 Details**: See `archive/SDL2_IMPLEMENTATION.md`
- **Refactoring Plans**: See `REFACTORING_OPPORTUNITIES.md`

---

**Last Updated**: 2025-12-07

