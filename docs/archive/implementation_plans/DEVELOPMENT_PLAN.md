# UniPlaySong - Development Plan

## Architecture Overview

The codebase is organized into clear layers with separation of concerns:

```
UniPlaySong/
├── Models/              # Data structures (Album, Song, GameMusic, etc.)
├── Downloaders/         # Music download modules (KHInsider, YouTube)
├── Services/            # Core business logic (MusicPlayback, FileManager)
├── Common/              # Shared utilities and helpers
├── Monitors/            # UI integration (fullscreen/desktop compatibility)
└── Controls/            # WPF controls (if needed)
```

## Phase 1: Foundation - Models & Core Structure

**Goal**: Establish data models and basic infrastructure

### 1.1 Models Layer
**Location**: `Models/`

Create data structures:
- `Song.cs` - Represents a single music track
- `Album.cs` - Represents a collection of songs (soundtrack)
- `GameMusic.cs` - Represents music associated with a game
- `Source.cs` - Enum for download sources (KHInsider, YouTube)
- `DownloadItem.cs` - Base class for downloadable items

**Why First**: All other modules depend on these models. Foundation must be solid.

### 1.2 Common Utilities
**Location**: `Common/`

Create helpers:
- `PathHelper.cs` - Game music directory paths
- `FileHelper.cs` - File operations
- `LoggerHelper.cs` - Logging utilities

**Why First**: Needed by all other modules for basic operations.

---

## Phase 2: Download Module

**Goal**: Separate, testable download functionality

### 2.1 Download Interfaces
**Location**: `Downloaders/`

- `IDownloader.cs` - Interface for download sources
  ```csharp
  - GetAlbumsForGame(gameName)
  - GetSongsFromAlbum(album)
  - DownloadSong(song, path)
  ```

- `IDownloadManager.cs` - Interface for managing downloads
  ```csharp
  - GetAlbumsForGame(gameName, source)
  - BestAlbumPick(albums, game)
  - DownloadSong(song, path)
  ```

**Why Separate**: Allows easy testing, swapping implementations, adding new sources.

### 2.2 Download Implementations
**Location**: `Downloaders/`

- `KHInsiderDownloader.cs` - Implements IDownloader for KHInsider
- `YouTubeDownloader.cs` - Implements IDownloader for YouTube
- `DownloadManager.cs` - Implements IDownloadManager, orchestrates downloaders

**Why After Interfaces**: Implementations depend on interfaces and models.

### 2.3 Download UI (Optional for Phase 2)
**Location**: `Views/` or `Controls/`

- Simple download dialog/UI for selecting and downloading music

---

## Phase 3: Music Playback Service

**Goal**: Core music playback functionality

### 3.1 Playback Interfaces
**Location**: `Services/`

- `IMusicPlayer.cs` - Interface for music playback
  ```csharp
  - Play(filePath)
  - Stop()
  - Pause()
  - SetVolume(volume)
  ```

- `IMusicPlaybackService.cs` - High-level playback service
  ```csharp
  - PlayGameMusic(game)
  - StopCurrent()
  - GetAvailableSongs(game)
  ```

**Why Separate**: Allows different playback implementations (WMP, SDL, etc.)

### 3.2 Playback Implementation
**Location**: `Services/`

- `MusicPlayer.cs` - Basic music player implementation
- `MusicPlaybackService.cs` - Service that manages playback for games
- `PrimarySongManager.cs` - Manages which song plays for each game (learned from PlayniteSound)

**Why After Download**: Playback needs music files, which come from downloads.

---

## Phase 4: File Management Service

**Goal**: Organize and manage game music files

### 4.1 File Service
**Location**: `Services/`

- `GameMusicFileService.cs` - Manages game music directories
  ```csharp
  - GetGameMusicDirectory(game)
  - GetAvailableSongs(game)
  - GetPrimarySong(game)
  - SetPrimarySong(game, song)
  ```

**Why After Models**: Needs to understand game/music relationships.

---

## Phase 5: Universal UI Integration

**Goal**: Make it work in desktop and fullscreen modes

### 5.1 Game Context Binding
**Location**: `Monitors/`

- `GameContextBindingFactory.cs` - Universal binding for game context (from PlayniteSound)
  - Works with any theme
  - Supports multiple data context paths
  - Priority-based fallback system

### 5.2 Window Monitoring
**Location**: `Monitors/`

- `WindowMonitor.cs` - Attaches music controls to Playnite windows
  - Desktop mode support
  - Fullscreen mode support
  - Theme-independent

**Why Last**: UI integration depends on all services being ready.

---

## Phase 6: Main Plugin Integration

**Goal**: Wire everything together

### 6.1 Plugin Orchestration
**Location**: Root

- Update `UniPlaySong.cs` to:
  - Initialize services
  - Handle game selection events
  - Coordinate download, playback, and file management

### 6.2 Settings Integration
**Location**: Root

- Update settings to control:
  - Download sources
  - Playback behavior
  - Volume controls

---

## Implementation Order Summary

1. **Models** → Foundation for everything
2. **Common** → Utilities needed by all
3. **Downloaders** → Get music files
4. **Services** → Play music and manage files
5. **Monitors** → UI integration
6. **Plugin** → Wire it all together

## Key Design Principles

### Separation of Concerns
- **Downloaders**: Only handle downloading, no playback logic
- **Services**: Only handle business logic, no UI
- **Monitors**: Only handle UI integration, no business logic
- **Models**: Only data structures, no behavior

### Dependency Direction
```
Plugin → Services → Downloaders → Models
        ↓
      Monitors → Services → Models
```

### Testability
- Interfaces allow mocking
- Services can be tested independently
- Downloaders can be tested without Playnite

### Extensibility
- New download sources: Implement `IDownloader`
- New playback methods: Implement `IMusicPlayer`
- New themes: `GameContextBindingFactory` handles it

## File Structure (Final)

```
UniPSong/
├── Models/
│   ├── Song.cs
│   ├── Album.cs
│   ├── GameMusic.cs
│   ├── Source.cs
│   └── DownloadItem.cs
├── Downloaders/
│   ├── IDownloader.cs
│   ├── IDownloadManager.cs
│   ├── KHInsiderDownloader.cs
│   ├── YouTubeDownloader.cs
│   └── DownloadManager.cs
├── Services/
│   ├── IMusicPlayer.cs
│   ├── IMusicPlaybackService.cs
│   ├── MusicPlayer.cs
│   ├── MusicPlaybackService.cs
│   ├── GameMusicFileService.cs
│   └── PrimarySongManager.cs
├── Common/
│   ├── PathHelper.cs
│   ├── FileHelper.cs
│   └── LoggerHelper.cs
├── Monitors/
│   ├── GameContextBindingFactory.cs
│   └── WindowMonitor.cs
└── [Root plugin files]
```

## Next Steps

Start with **Phase 1: Models** - Create the data structures that everything else will build upon.

