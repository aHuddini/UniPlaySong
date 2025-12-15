# Logging Standard - UniPlaySong

**Date**: 2025-12-07  
**Status**: ✅ **STANDARDIZED** - All files reviewed and documented  
**Last Change**: MediaElementsMonitor.cs updated to use uppercase `Logger`

---

## Standard Logging Patterns

### Pattern 1: Static Readonly Logger (Standard for Services)
**Use for**: Services, downloaders, players, static utility classes, monitors

```csharp
private static readonly ILogger Logger = LogManager.GetLogger();

// Usage:
Logger.Info("Message");
Logger.Debug("Detailed message");
Logger.Warn("Warning message");
Logger.Error(ex, "Error message");
```

**Files Using This Pattern** (11 files):
- ✅ `DownloadManager.cs` - Download management
- ✅ `MusicPlaybackService.cs` - Core playback (also uses FileLogger)
- ✅ `MusicFader.cs` - Volume transitions
- ✅ `GameMusicFileService.cs` - File operations
- ✅ `SDL2MusicPlayer.cs` - SDL2 audio player
- ✅ `MusicPlayer.cs` - WPF audio player
- ✅ `YouTubeDownloader.cs` - YouTube downloads
- ✅ `KHInsiderDownloader.cs` - KHInsider downloads
- ✅ `YouTubeClient.cs` - YouTube API client
- ✅ `WindowMonitor.cs` - Window monitoring
- ✅ `MediaElementsMonitor.cs` - Video detection (updated 2025-12-07)

### Pattern 2: Instance Logger (Dependency Injection)
**Use for**: Classes that receive logger via constructor

```csharp
private readonly ILogger _logger;

public MyClass(ILogger logger)
{
    _logger = logger;
}

// Usage:
_logger.Info("Message");
_logger.Error(ex, "Error message");
```

**Files Using This Pattern** (2 files):
- ✅ `MusicPlaybackCoordinator.cs` - Playback coordination (also uses FileLogger)
- ✅ `GameMenuHandler.cs` - Menu actions

**Rationale**: These classes receive the logger via constructor, allowing for dependency injection and testability.

### Pattern 3: Dual Logging (Critical Services)
**Use for**: Core playback services that need both Playnite logs and detailed file logs

```csharp
private static readonly ILogger Logger = LogManager.GetLogger();
private readonly FileLogger _fileLogger;

// Usage:
Logger.Info("Important event");  // Playnite log (visible in Playnite's log)
_fileLogger?.Info("Detailed debug info");  // File log (detailed debugging)
```

**Files Using This Pattern** (3 files):
- ✅ `MusicPlaybackService.cs` - Core playback service
- ✅ `MusicPlaybackCoordinator.cs` - Playback coordination
- ✅ `UniPlaySong.cs` - Main plugin

**When to Use Both**:
- `Logger` (Playnite): Important events, errors, initialization
- `FileLogger`: Detailed state changes, debug info, position tracking

### Pattern 4: Main Plugin Logger (Special Case)
**Use for**: Main plugin class only

```csharp
private static readonly ILogger logger = LogManager.GetLogger();  // lowercase for main class
private readonly FileLogger _fileLogger;

// Usage:
logger.Info("Plugin-level event");
_fileLogger?.Info("Detailed debug info");
```

**Files Using This Pattern** (1 file):
- ✅ `UniPlaySong.cs` - Main plugin entry point

**Rationale**: Main plugin class uses lowercase `logger` as a convention. This is acceptable and consistent within the file.

---

## Log Level Guidelines

### Debug
**Use for**: Detailed technical information, state changes, file operations, internal flow
```csharp
Logger.Debug($"Loading file: {filePath}");
Logger.Debug($"State changed: {oldState} -> {newState}");
Logger.Debug($"Selected album '{album.Name}' with score {score}");
```

**Current Usage**: Used appropriately in downloaders and selection logic.

### Info
**Use for**: Important events, user-visible actions, successful operations, initialization
```csharp
Logger.Info($"Playing music for '{game.Name}'");
Logger.Info("Settings reloaded");
Logger.Info("MusicPlaybackCoordinator initialized");
```

**Current Usage**: Primary log level for most events. Used consistently.

### Warn
**Use for**: Recoverable issues, fallbacks, missing optional resources, cleanup failures
```csharp
Logger.Warn($"File not found: {filePath}, using default");
Logger.Warn("No downloader available for source");
Logger.Warn($"Error cleaning up temp directory: {ex.Message}");
```

**Current Usage**: Used appropriately for non-critical failures.

### Error
**Use for**: Exceptions, failures, unrecoverable errors, initialization failures
```csharp
Logger.Error(ex, $"Error downloading song: {ex.Message}");
Logger.Error(ex, "Failed to initialize service");
Logger.Error(ex, $"Error playing music for game '{game?.Name}'");
```

**Current Usage**: Used consistently for exceptions and failures.

---

## File-by-File Review

### ✅ Core Services (All Standardized)

**MusicPlaybackService.cs** - Pattern 1 + 3
- Static `Logger` for Playnite logs (5 uses)
- Instance `_fileLogger` for detailed debug logs (30+ uses)
- ✅ Appropriate dual-logging pattern

**MusicPlaybackCoordinator.cs** - Pattern 2 + 3
- Instance `_logger` (received via constructor)
- Instance `_fileLogger` (received via constructor)
- ✅ Appropriate for dependency injection pattern

**MusicFader.cs** - Pattern 1
- Static `Logger` only
- ✅ Appropriate for utility class

**GameMusicFileService.cs** - Pattern 1
- Static `Logger` only (1 use)
- ✅ Appropriate for service class

### ✅ Downloaders (All Standardized)

**DownloadManager.cs** - Pattern 1
- Static `Logger` (10 uses: Warn, Error, Debug)
- ✅ Appropriate log levels

**YouTubeDownloader.cs** - Pattern 1
- Static `Logger` (26 uses)
- ✅ Standard pattern

**KHInsiderDownloader.cs** - Pattern 1
- Static `Logger` (27 uses)
- ✅ Standard pattern

**YouTubeClient.cs** - Pattern 1
- Static `Logger` (18 uses)
- ✅ Standard pattern

### ✅ Players (All Standardized)

**SDL2MusicPlayer.cs** - Pattern 1
- Static `Logger` (4 uses: Info, Error)
- ✅ Standard pattern

**MusicPlayer.cs** - Pattern 1
- Static `Logger` (11 uses: Debug, Error)
- ✅ Standard pattern

### ✅ Monitors (All Standardized)

**MediaElementsMonitor.cs** - Pattern 1
- Static `Logger` (6 uses: Info)
- ✅ **Updated 2025-12-07**: Changed from lowercase `logger` to uppercase `Logger`

**WindowMonitor.cs** - Pattern 1
- Static `Logger` (3 uses: Info, Debug, Warn)
- ✅ Standard pattern

### ✅ Menus (All Standardized)

**GameMenuHandler.cs** - Pattern 2
- Instance `_logger` (received via constructor, 19 uses)
- ✅ Appropriate for dependency injection

### ✅ Main Plugin (Special Case)

**UniPlaySong.cs** - Pattern 4
- Static lowercase `logger` (8 uses)
- Instance `_fileLogger` (multiple uses)
- ✅ Acceptable for main plugin class

### ✅ ViewModels/Services (Standardized)

**DownloadDialogService.cs** - Pattern 1 ✅
- Uses static readonly `Logger` field
- **Status**: Converted to Pattern 1 (2025-12-07)

**DownloadDialogViewModel.cs** - Pattern 1 ✅
- Uses static readonly `Logger` field
- **Status**: Converted to Pattern 1 (2025-12-07)

---

## Standardization Status

### ✅ Completed (2025-12-07)
- ✅ `MediaElementsMonitor.cs` - Updated to uppercase `Logger` (Pattern 1)
- ✅ `DownloadDialogService.cs` - Converted from inline logger to Pattern 1
- ✅ `DownloadDialogViewModel.cs` - Converted from inline logger to Pattern 1

### ✅ Already Standard (No Changes Needed)
- ✅ All downloaders (Pattern 1) - 3 files
- ✅ All players (Pattern 1) - 2 files
- ✅ All monitors (Pattern 1) - 2 files
- ✅ Core services (Pattern 1, 2, or 3) - 4 files
- ✅ Main plugin (Pattern 4) - 1 file
- ✅ Menu handlers (Pattern 2) - 1 file
- ✅ Dialog services (Pattern 1) - 2 files

**Total Standardized**: 16 files ✅

### ✅ All Files Standardized
All files in the codebase now follow the standard logging patterns. No further changes needed.

---

## Best Practices

1. **Use Static Logger** for services, downloaders, players, utility classes
2. **Use Instance Logger** when logger is injected via constructor
3. **Use Dual Logging** for critical services that need detailed debugging
4. **Log Levels**:
   - Debug: Technical details, state changes
   - Info: Important events, user actions
   - Warn: Recoverable issues, fallbacks
   - Error: Exceptions, failures

5. **FileLogger Usage**:
   - Use `_fileLogger?.Info()` for detailed debug information
   - Use `Logger.Info()` for important events visible in Playnite logs
   - Always use null-conditional operator (`?.`) with FileLogger

---

**Last Updated**: 2025-12-07  
**Status**: Standardized (1 file updated, all others already compliant)

