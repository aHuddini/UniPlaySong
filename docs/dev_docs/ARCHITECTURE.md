# UniPlaySong - Architecture Documentation

## Overview

UniPlaySong is a Playnite extension that provides console-like game music preview functionality. The architecture attempts to follow a **service-oriented design pattern** with clear separation of concerns, dependency injection, and centralized state management. Most of the architecture was modeled off of PlayNiteSound as a foundation/

## Architecture Principles

1. **Service-Oriented Design**: Core functionality is organized into services with well-defined interfaces
2. **Dependency Injection**: Services receive dependencies through constructors for testability
3. **Centralized State Management**: `MusicPlaybackCoordinator` centralizes all playback decision logic
4. **Event-Driven Communication**: Services communicate via events and callbacks
5. **Interface-Based Design**: Services implement interfaces for flexibility and testability

## Project Structure

```
UniPlaySong/
├── Common/                    # Shared utilities and constants
│   ├── Constants.cs           # Centralized constants (volumes, paths, durations)
│   ├── FileLogger.cs          # File-based logging utility
│   ├── PlayniteThemeHelper.cs # Playnite theme integration helpers
│   ├── PrimarySongManager.cs  # Primary song selection logic
│   └── RelayCommand.cs        # MVVM command pattern implementation
│
├── Downloaders/               # Music download implementations
│   ├── IDownloader.cs         # Downloader interface
│   ├── IDownloadManager.cs    # Download manager interface
│   ├── DownloadManager.cs     # Central download coordinator
│   ├── KHInsiderDownloader.cs # KHInsider source implementation
│   ├── YouTubeDownloader.cs   # YouTube source implementation
│   └── YouTubeClient.cs       # YouTube API client
│
├── Models/                    # Data structures
│   ├── Album.cs               # Album/soundtrack model
│   ├── Song.cs                # Individual song model
│   ├── GameMusic.cs           # Game music association model
│   ├── Source.cs              # Download source enum
│   ├── DownloadItem.cs        # Download item model
│   ├── FailedDownload.cs      # Failed download tracking
│   ├── AudioState.cs          # Audio playback state enum
│   └── NormalizationSettings.cs # Audio normalization configuration
│
├── Monitors/                  # UI integration and monitoring
│   ├── WindowMonitor.cs       # Window state monitoring for theme support
│   ├── MediaElementsMonitor.cs # Video playback detection
│   └── GameContextBindingFactory.cs # Game context binding
│
├── Players/                   # Audio playback implementations
│   ├── IMusicPlayer.cs        # Music player interface
│   ├── MusicPlayer.cs         # WPF MediaPlayer implementation (fallback)
│   ├── SDL2MusicPlayer.cs     # SDL2 implementation (primary)
│   ├── MusicFader.cs          # Volume fade-in/fade-out controller
│   └── SDL/                   # SDL2 P/Invoke wrappers
│       ├── SDL.cs             # SDL2 core library bindings
│       └── SDL_mixer.cs       # SDL2_mixer audio library bindings
│
├── Services/                  # Core business logic services
│   ├── MusicPlaybackService.cs        # High-level playback orchestration
│   ├── IMusicPlaybackService.cs       # Playback service interface
│   ├── MusicPlaybackCoordinator.cs    # Central playback decision coordinator
│   ├── IMusicPlaybackCoordinator.cs   # Coordinator interface
│   ├── GameMusicFileService.cs         # File system operations for game music
│   ├── SettingsService.cs             # Settings management and persistence
│   ├── ErrorHandlerService.cs         # Centralized error handling
│   ├── DownloadDialogService.cs       # Download dialog orchestration
│   ├── SearchCacheService.cs          # Search result caching
│   ├── AudioNormalizationService.cs   # Audio normalization (EBU R128)
│   ├── INormalizationService.cs       # Normalization service interface
│   └── Controller/                    # Controller support services
│       ├── ControllerInputService.cs  # Xbox controller input handling
│       ├── ControllerOverlay.cs       # Controller UI overlay
│       ├── ControllerDetectionService.cs # Controller presence detection
│       └── VisualEnhancementService.cs # Visual feedback for controller
│
├── Menus/                     # Playnite menu integration
│   ├── GameMenuHandler.cs     # Game context menu handler
│   └── MainMenuHandler.cs     # Main menu handler
│
├── Views/                     # WPF UI views
│   ├── DownloadDialogView.xaml         # Download dialog UI
│   ├── SimpleControllerDialog.xaml    # Controller-optimized download dialog
│   ├── ControllerFilePickerDialog.xaml # Controller file picker
│   ├── ControllerDeleteSongsDialog.xaml # Controller delete dialog
│   └── NormalizationProgressDialog.xaml # Normalization progress UI
│
├── ViewModels/                # MVVM view models
│   └── DownloadDialogViewModel.cs     # Download dialog view model
│
└── UniPlaySong.cs             # Main plugin entry point
```

## Core Architecture Components

### 1. Plugin Entry Point (`UniPlaySong.cs`)

The main plugin class that:
- Initializes all services in dependency order
- Subscribes to Playnite events (`OnGameSelected`, `OnApplicationStarted`, etc.)
- Manages native music suppression
- Provides public API for other extensions
- Handles controller login bypass monitoring

**Key Responsibilities:**
- Service initialization and lifecycle management
- Event subscription and delegation to coordinator
- Native music suppression (SDL2_mixer integration)
- Controller input monitoring for login screens

### 2. Music Playback Coordinator (`MusicPlaybackCoordinator`)

**Central decision-making component** that:
- Determines when music should play (`ShouldPlayMusic()`)
- Manages skip logic (first selection, login skip)
- Coordinates between settings and playback service
- Handles view changes and video state changes

**State Management:**
- `_firstSelect`: Tracks if this is the first game selection
- `_loginSkipActive`: Tracks if login skip is active
- `_skipFirstSelectActive`: Tracks if skip window is active
- `_hasSeenFullscreen`: Tracks if fullscreen mode has been entered

**Key Methods:**
- `ShouldPlayMusic(Game game)`: Central gatekeeper for all playback decisions
- `HandleGameSelected(Game game, bool isFullscreen)`: Processes game selection
- `HandleLoginDismiss()`: Handles login screen dismissal
- `HandleVideoStateChange(bool isPlaying)`: Pauses/resumes on video playback

### 3. Music Playback Service (`MusicPlaybackService`)

**High-level playback orchestration** that:
- Manages song selection (primary songs, randomization)
- Handles fade-in/fade-out transitions
- Manages default music fallback
- Tracks playback state (current game, current song, pause state)
- Implements preview mode (restart songs after duration)

**Key Features:**
- **Primary Song Support**: Plays designated primary song first, then randomizes
- **Default Music Fallback**: Plays default music when games have no music files
- **Position Preservation**: Saves/resumes default music position when switching games
- **Preview Mode**: Restarts songs after configured duration (15-300 seconds)
- **Randomization**: Random song selection on game change or song end

**State Tracking:**
- `_currentGameId`: Currently selected game ID
- `_currentSongPath`: Currently playing song path
- `_previousSongPath`: Last played song (for randomization avoidance)
- `_isPlayingDefaultMusic`: Tracks if default music is playing
- `_defaultMusicPausedOnTime`: Saved position for default music resume

### 4. Settings Service (`SettingsService`)

**Centralized settings management** that:
- Loads/saves settings from Playnite's settings storage
- Provides automatic settings change notifications via events
- Manages settings versioning and migration
- Provides thread-safe settings access

**Event System:**
- `SettingsChanged`: Fired when settings are reloaded
- `SettingPropertyChanged`: Fired when individual properties change

**Benefits:**
- Single source of truth for settings
- Automatic propagation to all subscribers
- No manual `UpdateSettings()` calls needed

### 5. File Service (`GameMusicFileService`)

**File system operations** for:
- Locating game music directories
- Enumerating available songs for games
- Managing primary song files
- File existence and validation

**Directory Structure:**
```
%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\{GameId}\
```

### 6. Download Manager (`DownloadManager`)

**Download orchestration** that:
- Coordinates between download sources (KHInsider, YouTube)
- Manages download queue and retry logic
- Handles failed download tracking
- Provides search result caching

**Download Sources:**
- **KHInsider**: Direct album/song downloads from khinsider.com
- **YouTube**: YouTube playlist/track downloads via yt-dlp

### 7. Audio Normalization Service (`AudioNormalizationService`)

**EBU R128 audio normalization** that:
- Normalizes audio files to consistent loudness levels
- Uses FFmpeg for audio processing
- Preserves original files (optional)
- Provides progress reporting

**Normalization Parameters:**
- Target Loudness: -16.0 LUFS (EBU R128 standard)
- True Peak: -1.5 dBTP
- Loudness Range: 11.0 LU

## Design Patterns

### 1. Service Locator Pattern
Services are initialized in `UniPlaySong.cs` and passed to dependent services via constructor injection.

### 2. Coordinator Pattern
`MusicPlaybackCoordinator` centralizes all playback decision logic, preventing scattered conditionals throughout the codebase.

### 3. Strategy Pattern
Multiple music player implementations (`SDL2MusicPlayer`, `MusicPlayer`) implement `IMusicPlayer` interface, allowing runtime selection.

### 4. Observer Pattern
Settings changes are propagated via events (`SettingsChanged`, `SettingPropertyChanged`).

### 5. Factory Pattern
`GameContextBindingFactory` creates game context bindings for UI integration.

## Data Flow

### Game Selection Flow

```
1. User selects game in Playnite
   ↓
2. Playnite fires OnGameSelected event
   ↓
3. UniPlaySong.OnGameSelected() receives event
   ↓
4. Delegates to MusicPlaybackCoordinator.HandleGameSelected()
   ↓
5. Coordinator checks ShouldPlayMusic() (skip logic, settings, etc.)
   ↓
6. If should play, calls MusicPlaybackService.PlayGameMusic()
   ↓
7. Service selects song (primary, random, or default)
   ↓
8. Service loads song via IMusicPlayer (SDL2 or WPF)
   ↓
9. MusicFader handles fade-in transition
   ↓
10. Music plays
```

### Settings Change Flow

```
1. User changes setting in UI
   ↓
2. SettingsService saves to disk
   ↓
3. SettingsService fires SettingsChanged event
   ↓
4. All subscribers receive event (Coordinator, PlaybackService, etc.)
   ↓
5. Subscribers update their internal state
   ↓
6. Changes take effect immediately
```

### Download Flow

```
1. User requests download via menu
   ↓
2. DownloadDialogService shows dialog
   ↓
3. User selects source (KHInsider/YouTube) and searches
   ↓
4. DownloadManager coordinates download
   ↓
5. IDownloader implementation (KHInsiderDownloader/YouTubeDownloader) downloads
   ↓
6. File saved to game music directory
   ↓
7. PlaybackService automatically picks up new file
```

## Threading Model

- **UI Thread**: All WPF operations, event handlers, and service calls
- **Background Threads**: 
  - Download operations (async/await)
  - Audio normalization (Task.Run)
  - Controller input monitoring (Task.Run with polling)

**Thread Safety:**
- Settings access is thread-safe via `SettingsService`
- File operations use proper locking
- UI updates use `Dispatcher.BeginInvoke()` for cross-thread access

## Error Handling

**Centralized Error Handling** via `ErrorHandlerService`:
- Logs errors to both Playnite logger and file logger
- Provides user-friendly error messages
- Handles exceptions gracefully without crashing extension

**Error Recovery:**
- Download failures are tracked and can be retried
- Audio player failures fall back to alternative implementation
- File access errors are logged and skipped

## Extension Points

### Adding New Download Sources

1. Implement `IDownloader` interface
2. Register in `DownloadManager` constructor
3. Add source enum to `Models.Source`
4. Update UI to show new source option

### Adding New Music Players

1. Implement `IMusicPlayer` interface
2. Add initialization logic in `UniPlaySong.InitializeServices()`
3. Fallback chain: SDL2 → WPF MediaPlayer

### Adding New Settings

1. Add property to `UniPlaySongSettings.cs`
2. Update settings UI XAML
3. Subscribe to `SettingPropertyChanged` event in services that need it

## Performance Considerations

1. **Search Caching**: `SearchCacheService` caches search results to avoid repeated API calls
2. **Lazy Loading**: Services are initialized only when needed
3. **Preloading**: SDL2 player supports preloading next song during fade-out
4. **File Enumeration**: Cached file lists to avoid repeated directory scans

## Testing Considerations

The architecture supports testing through:
- **Interface-Based Design**: Services implement interfaces for mocking
- **Dependency Injection**: Dependencies passed via constructors
- **Functional Delegates**: Coordinator uses `Func<>` delegates for testability
- **Event-Driven**: State changes via events can be tested

## Future Architecture Improvement Ideas

1. **Dependency Injection Container**: Consider using a DI container (e.g., Autofac) for service management
2. **Configuration Management**: Separate configuration from settings for build-time configuration
3. **Plugin API**: Formalize public API for other extensions
4. **Unit Tests**: Add unit test project with service mocks

