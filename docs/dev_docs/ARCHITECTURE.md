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
│   ├── DialogHelper.cs        # Centralized dialog window creation
│   ├── FileLogger.cs          # File-based logging utility
│   ├── PlayniteThemeHelper.cs # Playnite theme integration helpers
│   ├── PrimarySongManager.cs  # Primary song selection logic
│   ├── RelayCommand.cs        # MVVM command pattern implementation
│   └── XInputWrapper.cs       # Xbox controller input (single source of truth)
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
│   ├── NormalizationSettings.cs # Audio normalization configuration
│   └── WaveformTrim/          # Precise trim models
│       ├── TrimWindow.cs      # Trim selection model (start/end times)
│       └── WaveformData.cs    # Waveform samples for display
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
│   ├── AudioTrimService.cs            # Silence trimming service
│   ├── ITrimService.cs                # Trim service interface
│   ├── WaveformTrimService.cs         # Precise waveform-based trimming (NAudio + FFmpeg)
│   ├── IWaveformTrimService.cs        # Waveform trim service interface
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
├── Handlers/                  # Dialog and operation handlers
│   ├── ControllerDialogHandler.cs      # Controller-friendly dialog operations
│   ├── NormalizationDialogHandler.cs   # Audio normalization dialog operations
│   ├── TrimDialogHandler.cs            # Silence trimming dialog operations
│   └── WaveformTrimDialogHandler.cs    # Precise waveform trim dialog operations
│
├── Views/                     # WPF UI views
│   ├── DownloadDialogView.xaml           # Download dialog UI
│   ├── SimpleControllerDialog.xaml       # Controller-optimized download dialog
│   ├── ControllerFilePickerDialog.xaml   # Controller file picker
│   ├── ControllerDeleteSongsDialog.xaml  # Controller delete dialog
│   ├── NormalizationProgressDialog.xaml  # Normalization progress UI
│   ├── WaveformTrimDialog.xaml           # Desktop waveform trim dialog
│   └── ControllerWaveformTrimDialog.xaml # Controller waveform trim dialog
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
- Delegates dialog operations to specialized handlers

**Key Responsibilities:**
- Service initialization and lifecycle management
- Event subscription and delegation to coordinator
- Native music suppression (SDL2_mixer integration)
- Controller input monitoring for login screens
- Menu registration and action delegation

**Refactored Structure (v1.1.0+):**
The main plugin file has been refactored for maintainability. Large code blocks have been extracted to dedicated handler classes in the `Handlers/` folder:
- `ControllerDialogHandler`: Controller-friendly dialog operations (set/clear primary song, delete songs, download)
- `NormalizationDialogHandler`: Audio normalization progress dialogs and operations
- `TrimDialogHandler`: Audio trimming progress dialogs and operations

Public methods in `UniPlaySong.cs` now delegate to these handlers, keeping the main file focused on initialization, event handling, and coordination.

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
- **Parallel bulk processing** for faster operations (v1.1.3+)

**Normalization Parameters:**
- Target Loudness: -16.0 LUFS (EBU R128 standard)
- True Peak: -1.5 dBTP
- Loudness Range: 11.0 LU

**Parallel Processing (v1.1.3+):**
- Bulk normalization processes up to 3 files simultaneously
- Bulk silence trimming also processes up to 3 files simultaneously
- Uses `Math.Min(Environment.ProcessorCount, 3)` for safe parallelism cap
- Thread-safe counters with `Interlocked.Increment`
- Thread-safe failed files collection with `ConcurrentBag<string>`
- Playback stopped once at start, not per-file

### 8. Waveform Trim Service (`WaveformTrimService`)

**Precise audio trimming with visual waveform editing** that:
- Generates waveform data for visual display using NAudio
- Applies precise trim points using FFmpeg
- Preserves original files in `PreservedOriginals/` folder
- Supports both desktop (mouse) and controller (D-pad) interfaces

**Key Features:**
- **Waveform Generation**: Uses NAudio `AudioFileReader` to extract ~1000 samples for smooth display
- **Precise Trimming**: FFmpeg handles format-aware trimming with codec-specific encoding
- **Two Interface Modes**:
  - Desktop: Canvas-based waveform with draggable markers
  - Controller: Two-step flow (file selection → waveform adjustment via D-pad)

**Fullscreen/Controller Support:**
The Precise Trim feature is **fully usable in Playnite's Fullscreen Mode** with complete Xbox controller support. Users can trim audio files from the couch without needing a keyboard or mouse. The controller dialog provides:
- Large, controller-friendly UI optimized for TV viewing
- Visual feedback with button prompts and sound cues
- Continuous D-pad input with smooth, consistent marker movement

**Controller Scheme (Waveform Editor Step):**
| Input | Action |
|-------|--------|
| D-Pad Left | Move start marker earlier (0.5s steps) |
| D-Pad Right | Move start marker later (0.5s steps) |
| D-Pad Up | Move end marker later / extend (0.5s steps) |
| D-Pad Down | Move end marker earlier / shorten (0.5s steps) |
| Left Bumper (LB) | Contract window (move both edges inward) |
| Right Bumper (RB) | Expand window (move both edges outward) |
| A Button | Preview the kept portion |
| B Button | Go back to file selection |
| X Button | Apply trim and save |
| Y Button | Reset to full duration |

**Continuous D-Pad Navigation:**
When holding D-pad directions, the markers move continuously with:
- Initial delay: 200ms before continuous movement begins
- Repeat interval: 50ms for smooth, precise control
- Fixed 0.5 second increments for predictable, clean adjustments

**Settings:**
- `PreciseTrimSuffix`: Suffix for trimmed files (default: `-ptrimmed`)

### 9. Dialog Handlers (`Handlers/`)

**Extracted dialog operation handlers** that encapsulate complex UI operations:

#### ControllerDialogHandler
Handles controller-friendly (fullscreen mode) dialog operations:
- `ShowSetPrimarySong(Game)`: File picker for setting primary song
- `ClearPrimarySong(Game)`: Clears primary song with notification
- `ShowDeleteSongs(Game)`: Multi-select dialog for deleting songs
- `ShowDownloadDialog(Game)`: Controller-optimized download dialog
- `ShowNormalizeIndividualSong(Game)`: File picker for normalizing single song
- `ShowTrimIndividualSong(Game)`: File picker for silence-trimming single song

#### NormalizationDialogHandler
Handles audio normalization operations:
- `NormalizeAllMusicFiles()`: Bulk normalize entire library
- `NormalizeSelectedGames(List<Game>)`: Normalize selected games
- `NormalizeSingleFile(Game, string)`: Normalize individual file
- `DeletePreservedOriginals()`: Delete backup files
- `RestoreNormalizedFiles()`: Restore from backups

#### TrimDialogHandler
Handles silence trimming operations:
- `TrimAllMusicFiles()`: Bulk trim silence from library
- `TrimSelectedGames(List<Game>)`: Trim selected games
- `TrimSingleFile(Game, string)`: Trim individual file

#### WaveformTrimDialogHandler
Handles precise waveform-based trim operations:
- `ShowPreciseTrimDialog(Game)`: Desktop waveform editor with mouse interaction
- `ShowControllerPreciseTrimDialog(Game)`: Controller-friendly two-step dialog

### 10. Cleanup Operations (`UniPlaySong.cs`)

**Plugin data management** operations accessible via Settings → Cleanup tab:

**Methods:**
- `GetStorageInfo()`: Returns storage statistics (game count, file count, bytes, preserved files count/size)
- `DeleteAllMusic()`: Removes all game music folders and preserved originals, preserves settings
- `ResetSettingsToDefaults()`: Resets all settings to defaults while preserving tool paths (FFmpeg, yt-dlp)
- `FactoryReset()`: Complete reset - deletes all music, clears search cache, resets settings

**Storage Locations Cleaned:**
- `%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Games\*` - All game music folders
- `%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\PreservedOriginals\` - Backup files
- `%APPDATA%\Playnite\ExtraMetadata\UniPlaySong\Temp\` - Temporary files (factory reset only)
- Search cache cleared (factory reset only)

**Safety Features:**
- Double-confirmation dialogs for destructive operations
- Music playback is stopped before any deletion
- Tool paths (FFmpeg, yt-dlp) preserved during settings reset (system-specific)
- Detailed logging of all operations

**Handler Pattern Benefits:**
- Reduces main plugin file size for better maintainability
- Encapsulates related operations together
- Uses `Func<UniPlaySongSettings>` for settings access (avoids stale references)
- Shares common helper methods (focus management, playback stopping)

### 9. Dialog Helper (`Common/DialogHelper.cs`)

**Centralized dialog window creation** that provides:
- Consistent window styling across all dialogs
- Common default settings (size, position, taskbar visibility)
- Focus management when dialogs close

**Key Methods:**
- `CreateStandardDialog()`: Resizable dialog with maximize button
- `CreateFixedDialog()`: Non-resizable dialog (for progress dialogs, etc.)
- `CreateFullscreenDialog()`: Dialog with Topmost and dark background for fullscreen mode
- `CreateDialog()`: Full customization via `DialogOptions`
- `AddFocusReturnHandler()`: Adds handler to return focus to main window on close
- `ReturnFocusToMainWindow()`: Static helper for focus management

**Usage Example:**
```csharp
var dialog = new Views.MyDialog();
var window = DialogHelper.CreateStandardDialog(
    _playniteApi,
    "Dialog Title",
    dialog,
    width: 600,
    height: 500);

DialogHelper.AddFocusReturnHandler(window, _playniteApi, "my dialog close");
window.ShowDialog();
```

**Benefits:**
- Single point of customization for all dialog appearances
- Eliminates duplicate window creation code (~8-12 lines per dialog)
- Consistent focus behavior in fullscreen mode
- Easy theming adjustments in the future

**Taskbar Visibility (v1.1.3+):**
- Default `ShowInTaskbar = true` for Desktop mode - dialogs appear in Windows taskbar
- Fullscreen mode: `ShowInTaskbar = false` (topmost handles visibility)
- Users can easily find and click back to dialogs when switching applications

### 10. XInput Wrapper (`Common/XInputWrapper.cs`)

**Single source of truth for Xbox controller input** that provides:
- P/Invoke declarations for XInput DLLs (1.4, 1.3, 9.1.0 fallback chain)
- `XINPUT_STATE` and `XINPUT_GAMEPAD` structs
- Button constants (`XINPUT_GAMEPAD_A`, `XINPUT_GAMEPAD_B`, etc.)

**Key Methods:**
- `XInputGetState(int, ref XINPUT_STATE)`: Get controller state with automatic DLL fallback

**Usage:**
All controller dialogs (`ControllerFilePickerDialog`, `ControllerDeleteSongsDialog`, `SimpleControllerDialog`) use `XInputWrapper` instead of duplicating P/Invoke code.

```csharp
XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
int result = XInputWrapper.XInputGetState(0, ref state);
if (result == 0) // Success
{
    if ((state.Gamepad.wButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
    {
        // A button pressed
    }
}
```

**D-Pad Debouncing:**
Controller dialogs implement D-pad debouncing to prevent double-input when both WPF (which processes D-pad as arrow keys) and XInput process the same input:

```csharp
private DateTime _lastDpadNavigationTime = DateTime.MinValue;
private const int DpadDebounceMs = 150; // Minimum ms between D-pad navigations

private bool TryDpadNavigation()
{
    var now = DateTime.Now;
    if ((now - _lastDpadNavigationTime).TotalMilliseconds < DpadDebounceMs)
        return false; // Too soon, ignore this input
    _lastDpadNavigationTime = now;
    return true;
}
```

### 11. Game Menu System (`UniPlaySong.cs:GetGameMenuItems`)

**Mode-specific menu organization** that adapts to Playnite's display mode:

#### Fullscreen Mode Structure
Organized with subfolders for controller navigation:
- **🎮 Download Music** - Controller-optimized download (parent level)
- **Primary Song** subfolder - 🎮 Set/Clear Primary Song
- **Audio Processing** subfolder:
  - 🎮 Normalize Individual Song, 🎮 Silence Trim - Single Song
  - Normalize Music Directory, Silence Trim - Audio Folder
  - 🎮 Precise Trim (waveform-based visual trimming)
- **Manage Music** subfolder - 🎮 Delete Songs, Open Music Folder
- **🖥️ PC Mode** subfolder - Desktop dialogs for keyboard/mouse users

#### Desktop Mode Structure
Flat layout with PC options at parent level:
- Download Music, Download From URL
- Set/Clear Primary Song
- Normalize Individual Song, Silence Trim - Single Song
- Normalize Music Directory, Silence Trim - Audio Folder
- Precise Trim (waveform-based visual trimming)
- Open Music Folder
- **🎮 Controller Mode** subfolder - Controller-friendly versions (including 🎮 Precise Trim)

#### Multi-Game Selection
When multiple games are selected (Desktop mode only):
- **Download All** - Bulk download for all selected games
- **Normalize All** - Bulk normalize selected games
- **Trim All** - Bulk trim selected games

**Visual Indicators:**
- 🎮 emoji prefix = Controller-optimized dialog (large buttons, D-pad navigation)
- 🖥️ emoji prefix = Desktop dialog (standard Windows UI)
- No emoji = Works with both modes

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

### 6. Static Helper Pattern
`DialogHelper` and `XInputWrapper` provide centralized, reusable functionality as static classes. This eliminates code duplication and provides single points of maintenance for cross-cutting concerns (dialog creation, controller input).

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
2. Source Selection Dialog (KHInsider/YouTube)
   ↓ (Cancel exits, Back N/A)
3. Album Selection Dialog
   ↓ (Cancel exits, Back → Source Selection)
4. Song Selection Dialog
   ↓ (Cancel exits, Back → Album Selection)
5. DownloadManager coordinates download
   ↓
6. IDownloader implementation (KHInsiderDownloader/YouTubeDownloader) downloads
   ↓
7. File saved to game music directory
   ↓
8. PlaybackService automatically picks up new file
```

**Back Button Navigation:**
- Desktop mode dialogs support Back button to navigate through the flow
- Uses `Album.BackSignal` sentinel to distinguish Back from Cancel
- `GameMenuHandler` uses nested loops to enable re-selection at each level

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

## Settings Tab Structure

The settings UI (`UniPlaySongSettingsView.xaml`) is organized into the following tabs:

### General Tab
- Enable Music toggle
- Music State (Never/Desktop/Fullscreen/Always)
- Volume slider
- Randomization options
- Skip first selection behavior
- Theme compatibility options
- Pause on trailer/focus loss/minimize/system tray settings

### Default Music Tab
- Enable default music
- Default music path
- Use Playnite native music as default
- Suppress Playnite background music

### Audio Normalization Tab
- Target loudness (LUFS)
- True peak limit (dBTP)
- Loudness range (LU)
- Audio codec selection
- Suffix settings (normalization, trim)
- Preserve originals toggle
- Bulk operations (Normalize All, Restore, Delete Preserved)

### Downloads Tab
- yt-dlp and FFmpeg path configuration
- Firefox cookies support toggle
- Search cache settings
- Auto-normalize after download
- Auto-download on library update
- Bulk download button

### Migration Tab
- PlayniteSound status display
- UniPlaySong status display
- Import from PlayniteSound
- Export to PlayniteSound
- Directory location info

### Cleanup Tab (v1.1.2+)
- Storage usage statistics
- Delete All Music button
- Reset Settings button
- Factory Reset button

### Debug Tab
- Enable debug logging toggle

## Future Architecture Improvement Ideas

1. **Dependency Injection Container**: Consider using a DI container (e.g., Autofac) for service management
2. **Configuration Management**: Separate configuration from settings for build-time configuration
3. **Plugin API**: Formalize public API for other extensions
4. **Unit Tests**: Add unit test project with service mocks

