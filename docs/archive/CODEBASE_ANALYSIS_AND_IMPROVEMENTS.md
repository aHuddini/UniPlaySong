# Codebase Analysis & Improvement Opportunities

**Date**: 2025-11-30  
**Version**: 1.0.4  
**Status**: Analysis Complete

## Executive Summary

This document provides a comprehensive analysis of the UniPlaySong codebase, comparing it with PlayniteSound (PNS), identifying refactoring opportunities, code optimizations, and potential bugs. The analysis focuses on architectural improvements, missing features, and code quality enhancements.

---

## 1. Missing Features from PlayniteSound

### 1.1 Sound Manager / Preview Audio Pack (Deferred)

**PNS Feature**: Complete sound pack management system for interface sounds
- **Load Sounds**: Import ZIP archives containing sound packs
- **Save Sounds**: Export current sound files as ZIP archive
- **Remove Sounds**: Delete sound pack ZIP files
- **Import Sounds**: Import sound packs from external locations
- **Open Sound Manager Folder**: Direct access to sound pack directory

**Note from User**: PNS's Sound Manager is geared towards interface sounds, not music. If we implement something similar, it should be called "Preview Audio Pack" and would serve a different purpose (preview audio management rather than interface sounds).

**Status**: **DEFERRED** - Out of scope for music preview extension. Sound effects are not a priority.

---

### 1.2 Platform Music Support (Deferred - Need Clarification)

**PNS Feature**: Music can be organized by gaming platform (Xbox, PlayStation, PC, etc.)
- `MusicType.Platform`: One music per platform
- Platform-specific directories: `{MusicPath}/Platform/{PlatformName}/`
- When you select an Xbox game, plays music from `Platform/Xbox/` folder
- Can collect music from all games on that platform

**How It Works**:
- User filters library to "Xbox" games → plays Xbox platform music
- User filters library to "PlayStation" games → plays PlayStation platform music
- Music organized by platform name: `Platform/Xbox/`, `Platform/PlayStation/`, etc.

**Current State**: UniPSong only supports game-specific music

**User Feedback**: Need to see how this works in practice before implementing.

**Status**: **DEFERRED** - Low priority until use case is understood.

---

### 1.3 Filter Music Support (Deferred - Need Clarification)

**PNS Feature**: Music can be organized by Playnite filter presets
- `MusicType.Filter`: One music per filter preset
- Filter-specific directories: `{MusicPath}/Filter/{FilterId}/`
- When you apply a filter (e.g., "Favorites", "Recently Played"), plays music from that filter's folder
- Music changes when you switch filters

**How It Works**:
- User creates filter "Action Games" → plays music from `Filter/{ActionFilterId}/` folder
- User creates filter "RPG Games" → plays music from `Filter/{RPGFilterId}/` folder
- Music organized by filter preset ID

**Current State**: UniPSong only supports game-specific music

**User Feedback**: Need to see how this works in practice before implementing.

**Status**: **DEFERRED** - Low priority until use case is understood.

---

### 1.4 Default Music Support (HIGH PRIORITY - User Requested)

**PNS Feature**: Fallback default music when no game/platform/filter music found
- `MusicType.Default`: Single default music file
- Default music path: `{MusicPath}/_music_.mp3`
- Plays when no other music is available

**Current State**: UniPSong fades out when no music found

**User Requirements**:
- ✅ Separate settings section: "Default Music"
- ✅ Setting: `EnableDefaultMusic` (bool)
- ✅ Setting: `DefaultMusicPath` (string) - file picker
- ✅ Setting: `SuppressPlayniteBackgroundMusic` (bool) - suppress Playnite's native music
- ✅ Integrate with existing `SuppressNativeMusic()` method
- ✅ When no game music found AND `EnableDefaultMusic` is true, play default music

**Recommendation**: **HIGH PRIORITY** - Implement as separate settings section with file picker. Build on top of existing `SuppressNativeMusic()` functionality.

---

### 1.5 Audio State Management (Partially Implemented - Need Clarification)

**PNS Feature**: Granular control over when music plays
- `AudioState` enum: `Never`, `Desktop`, `Fullscreen`, `Always`
- Separate settings for music and sounds
- `PauseNotInLibrary` option (pause when not in Library view)
- `SkipFirstSelectMusic` option (skip music on first game selection)

**Current State**: 
- ✅ UniPSong has `MusicState` (AudioState enum) property
- ✅ Basic checks in `ShouldPlayMusic()` for desktop/fullscreen
- ❌ `SkipFirstSelectionAfterModeSwitch` setting exists but **DOESN'T WORK** (BUG)
- ⚠️ `PauseNotInLibrary` - Low priority per user

**User Feedback**: 
- Need clarification on how `AudioState` benefits current architecture
- `SkipFirstSelectMusic` needs to be fixed (CRITICAL BUG)

**Status**: 
- **CRITICAL**: Fix `SkipFirstSelectionAfterModeSwitch` bug
- **LOW**: `PauseNotInLibrary` - defer for now
- **CLARIFICATION NEEDED**: How does AudioState benefit our architecture beyond current implementation?

---

### 1.6 Sound Effects System (OUT OF SCOPE)

**PNS Feature**: Sound effects for UI events
- Application Started/Stopped sounds
- Game Selected/Started/Stopped sounds
- Game Installed/Uninstalled sounds
- Library Updated sound
- Separate sound player system (`PlayerEntry` dictionary)
- Desktop/Fullscreen sound variants (D_/F_ prefix)

**Current State**: UniPSong has no sound effects

**User Feedback**: **NOT A PRIORITY** - User specifically forked PNS because sound effects were out of scope. Extension focuses on music previews only.

**Status**: **OUT OF SCOPE** - Will not implement.

---

### 1.7 Music Type Selection (Deferred)

**PNS Feature**: Different music types for different views
- `MusicType` selection: Default, Platform, Game, Filter
- `DetailsMusicType`: Separate music type for game details view
- `ChoosenMusicType`: Computed property based on current view
- Dynamic music switching based on view context

**Current State**: UniPSong only supports game music

**User Feedback**: Interesting feature, but want to focus on other fundamental features first.

**Status**: **DEFERRED** - Focus on core music preview functionality first.

---

### 1.8 Localization Support (Skeleton Priority)

**PNS Feature**: Full localization support
- 38+ language files in `Localization/` directory
- Resource-based string management
- `Resource` class for localized strings
- `Localization.cs` for language switching

**Current State**: UniPSong has no localization

**User Feedback**: 
- Makes sense to add localization support
- User doesn't have experience with other languages
- Can't assume AI models can translate accurately
- Build skeleton feature now, implement translations later

**Recommendation**: Create localization skeleton/framework for future translations. Implement actual translations when ready.

---

## 2. Refactoring Opportunities

### 2.1 Service Separation (High Priority)

**Current Issue**: `UniPlaySong.cs` is doing too much
- Main plugin class handles: initialization, event handling, playback coordination, menu management
- ~600 lines of mixed responsibilities

**PNS Pattern**: More separation but still monolithic
- `PlayniteSounds.cs` is ~2700 lines (but has more features)

**Recommendation**:
```csharp
// Create dedicated services:
- GameSelectionService.cs      // Handles OnGameSelected logic
- ApplicationLifecycleService.cs // Handles OnApplicationStarted/Stopped
- MusicCoordinationService.cs   // Coordinates playback service with events
```

**Benefits**:
- Better testability
- Clearer separation of concerns
- Easier to maintain
- Follows Single Responsibility Principle

---

### 2.2 Settings Management (Medium Priority)

**Current Issue**: Settings passed around as parameters
- `UniPlaySongSettings` passed to multiple methods
- No centralized settings access
- Settings updates require manual propagation

**PNS Pattern**: `SettingsModel` property with reactive updates

**Recommendation**:
```csharp
// Create SettingsService:
public class SettingsService
{
    public UniPlaySongSettings Current { get; private set; }
    public event EventHandler<SettingsChangedEventArgs> SettingsChanged;
    
    public void UpdateSettings(UniPlaySongSettings newSettings) { ... }
}
```

**Benefits**:
- Centralized settings access
- Reactive updates via events
- Easier to add settings change handlers
- Better testability

---

### 2.3 Error Handling Consistency (High Priority)

**Current Issue**: Inconsistent error handling patterns
- Some methods use try-catch, others don't
- Some log errors, others silently fail
- No consistent error recovery strategy

**PNS Pattern**: `Try()` wrapper method for consistent error handling

**Recommendation**:
```csharp
// Create ErrorHandler service:
public static class ErrorHandler
{
    public static void Try(Action action, string context = null)
    {
        try { action(); }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error in {context ?? "operation"}");
            // Optionally show user-friendly message
        }
    }
    
    public static T Try<T>(Func<T> func, T defaultValue = default, string context = null)
    {
        try { return func(); }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error in {context ?? "operation"}");
            return defaultValue;
        }
    }
}
```

**Benefits**:
- Consistent error handling
- Centralized logging
- Better error recovery
- Easier debugging

---

### 2.4 Player Management (Medium Priority)

**Current Issue**: Single player instance, no player pooling
- One `IMusicPlayer` instance
- No player caching for sounds
- No player lifecycle management

**PNS Pattern**: `PlayerEntry` dictionary for sound caching

**Recommendation**:
```csharp
// Enhance player management:
public class PlayerManager
{
    private readonly Dictionary<string, IMusicPlayer> _playerCache;
    
    public IMusicPlayer GetOrCreatePlayer(string key, Func<IMusicPlayer> factory)
    {
        if (!_playerCache.TryGetValue(key, out var player))
        {
            player = factory();
            _playerCache[key] = player;
        }
        return player;
    }
    
    public void CleanupUnusedPlayers() { ... }
}
```

**Benefits**:
- Better resource management
- Player reuse for sounds
- Memory optimization
- Easier to add sound effects

---

### 2.5 File Path Management (Low Priority)

**Current Issue**: Path construction scattered throughout code
- `GameMusicFileService` has path logic
- Downloaders construct paths
- No centralized path management

**Recommendation**:
```csharp
// Create PathService:
public class PathService
{
    public string GetGameMusicPath(Game game) { ... }
    public string GetPlatformMusicPath(Platform platform) { ... }
    public string GetFilterMusicPath(FilterPreset filter) { ... }
    public string GetDefaultMusicPath() { ... }
    public string GetSoundManagerPath() { ... }
}
```

**Benefits**:
- Centralized path logic
- Easier to change path structure
- Better path validation
- Consistent path formatting

---

## 3. Code Optimizations

### 3.1 Async/Await Usage (High Priority)

**Current Issue**: Some blocking operations on UI thread
- `DownloadSelectedSongs()` uses `Task.Run` but could be better structured
- File I/O operations may block
- Network operations could be more async

**Recommendation**:
```csharp
// Convert to async/await:
public async Task DownloadSelectedSongsAsync()
{
    IsDownloading = true;
    try
    {
        await Task.Run(async () =>
        {
            foreach (var song in selected)
            {
                await DownloadSongAsync(song, filePath, cancellationToken);
                await UpdateProgressAsync(downloaded, total);
            }
        });
    }
    finally
    {
        IsDownloading = false;
    }
}
```

**Benefits**:
- Better async patterns
- Non-blocking UI
- Better cancellation support
- Improved performance

---

### 3.2 Caching Improvements (Medium Priority)

**Current Issue**: Limited caching
- Song list not cached between selections
- Album search results not cached
- File existence checks repeated

**Recommendation**:
```csharp
// Add caching layer:
public class CacheService
{
    private readonly MemoryCache _songCache;
    private readonly MemoryCache _albumCache;
    
    public List<Song> GetCachedSongs(Game game)
    {
        var key = $"songs_{game.Id}";
        return _songCache.GetOrCreate(key, () => LoadSongs(game));
    }
}
```

**Benefits**:
- Reduced file I/O
- Faster UI responses
- Better user experience
- Lower system resource usage

---

### 3.3 LINQ Optimization (Low Priority)

**Current Issue**: Some LINQ queries could be optimized
- Multiple enumerations of collections
- Inefficient filtering
- Missing `.ToList()` where needed

**Recommendation**:
```csharp
// Optimize LINQ:
// Before:
var songs = allSongs.Where(s => s.Name.Contains(search)).Where(s => s.IsValid);
foreach (var song in songs) { ... }
foreach (var song in songs) { ... } // Enumerates twice!

// After:
var songs = allSongs.Where(s => s.Name.Contains(search) && s.IsValid).ToList();
foreach (var song in songs) { ... }
foreach (var song in songs) { ... } // Uses cached list
```

**Benefits**:
- Reduced memory allocations
- Better performance
- Fewer enumerations
- More predictable behavior

---

### 3.4 String Operations (Low Priority)

**Current Issue**: String concatenation in loops
- Progress text building
- Log message construction
- Path combination

**Recommendation**:
```csharp
// Use StringBuilder for repeated concatenation:
var sb = new StringBuilder();
foreach (var item in items)
{
    sb.AppendLine(item.ToString());
}
var result = sb.ToString();

// Use string interpolation:
var message = $"Downloading: {song.Name} ({current}/{total})";
```

**Benefits**:
- Better memory usage
- Faster string operations
- Cleaner code
- Better performance

---

## 4. Potential Bugs

### 4.1 Race Condition in Download Progress (Medium Priority)

**Location**: `DownloadDialogViewModel.DownloadSelectedSongs()`

**Issue**: Progress updates may race with UI thread
```csharp
// Current code:
app.Dispatcher.Invoke(() => { ProgressValue = downloaded; });
// If multiple songs finish simultaneously, updates may be out of order
```

**Fix**:
```csharp
// Use Interlocked for thread-safe counter:
private int _downloadedCount = 0;
Interlocked.Increment(ref _downloadedCount);
app.Dispatcher.Invoke(() => { ProgressValue = _downloadedCount; });
```

---

### 4.2 Memory Leak in Preview Downloads (CRITICAL - User Confirmed)

**Location**: `DownloadDialogViewModel.PreviewSong()`

**Issue**: Temporary preview files may not be cleaned up
- Preview files created in temp directory (`%TEMP%\UniPlaySong\Preview\`)
- No cleanup on dialog close
- No cleanup on preview stop
- Files accumulate on user's hard drive

**User Feedback**: **CRITICAL** - Resolve ASAP to prevent wasting disk space.

**Fix**:
```csharp
// Track preview files and clean up:
private readonly List<string> _previewFiles = new List<string>();

// In PreviewSong():
_previewFiles.Add(tempPath);

// In StopPreview():
if (!string.IsNullOrEmpty(_currentlyPreviewing))
{
    try
    {
        if (File.Exists(_currentlyPreviewing))
            File.Delete(_currentlyPreviewing);
        _previewFiles.Remove(_currentlyPreviewing);
    }
    catch { /* Ignore */ }
}

// Add cleanup on dialog close
public void CleanupPreviewFiles()
{
    foreach (var file in _previewFiles.ToList())
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch { /* Ignore */ }
        _previewFiles.Remove(file);
    }
}
```

**Status**: **CRITICAL PRIORITY** - Fix immediately.

---

### 4.3 Null Reference in Fade-Out (Deferred - Too Risky)

**Location**: `MusicFader.TimerTick()`

**Issue**: `_player` may be null during fade-out
```csharp
// Current code checks _player.Volume but _player could be null
if (_player != null && _player.IsActive)
{
    var currentVolume = _player.Volume; // Could throw if _player becomes null
}
```

**User Feedback**: **DEFERRED** - User is afraid to touch this since they worked very hard to get the fader system working with all the issues.

**Status**: **DEFERRED** - Don't modify fader system unless absolutely necessary. Current implementation is working.

---

### 4.4 File Lock Issues (Need Clarification)

**Location**: Multiple locations (downloaders, file service)

**Issue**: Files may be locked when trying to delete/move
- No retry logic for file operations
- No file lock detection
- May fail silently

**User Feedback**: Need clarification on separation of concern for this.

**Recommendation**: Create `FileOperationsService` for all file operations with retry logic:
```csharp
// Services/FileOperationsService.cs
public class FileOperationsService
{
    public static bool DeleteFileWithRetry(string path, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                Thread.Sleep(100 * (i + 1)); // Exponential backoff
            }
        }
        return false;
    }
    
    public static bool MoveFileWithRetry(string source, string dest, int maxRetries = 3)
    {
        // Similar retry logic
    }
}
```

**Status**: **MEDIUM PRIORITY** - Create service for file operations with retry logic.

---

### 4.5 Cancellation Token Not Checked (Low Priority)

**Location**: `DownloadDialogViewModel.DownloadSelectedSongs()`

**Issue**: Cancellation token created but not exposed for cancellation
```csharp
// Current code:
var cancellationTokenSource = new System.Threading.CancellationTokenSource();
// But no way to cancel from UI
```

**Fix**:
```csharp
// Expose cancellation:
private CancellationTokenSource _downloadCancellation;

public void CancelDownload()
{
    _downloadCancellation?.Cancel();
}

// In download method:
_downloadCancellation = new CancellationTokenSource();
var token = _downloadCancellation.Token;
```

---

## 5. Code Quality Improvements

### 5.1 Magic Numbers and Constants (Medium Priority)

**Current Issue**: Hard-coded values throughout code
- Timeout values: `2000`, `100`, etc.
- File size limits
- Retry counts

**Recommendation**:
```csharp
// Create Constants class:
public static class Constants
{
    public const int DownloadTimeoutMs = 30000;
    public const int PreviewDurationSeconds = 30;
    public const int MaxRetryAttempts = 3;
    public const int FileLockRetryDelayMs = 100;
    public const double DefaultFadeInDuration = 0.5;
    public const double DefaultFadeOutDuration = 0.3;
}
```

---

### 5.2 Logging Consistency (Medium Priority - Playnite Quirks)

**Current Issue**: Inconsistent logging levels and formats
- Some use `Logger.Info`, others `Logger.Debug`
- Inconsistent message formats
- Some operations not logged

**User Feedback**: 
- Logging is inconsistent, but keep in mind Playnite has ways of hiding/displaying certain log types
- Critical issues have to be "downgraded" to WARN/INFO/DEBUG for logs to be properly written
- Need to factor this in when addressing logging

**Recommendation**:
```csharp
// Create logging helper that accounts for Playnite quirks:
public static class LogHelper
{
    // Use WARN/INFO for critical issues (Playnite may hide ERROR)
    public static void LogCritical(string operation, object context = null)
    {
        Logger.Warn($"[UniPlaySong] CRITICAL: {operation}" + 
                   (context != null ? $" - {context}" : ""));
    }
    
    public static void LogOperation(string operation, object context = null)
    {
        Logger.Info($"[UniPlaySong] {operation}" + 
                   (context != null ? $" - {context}" : ""));
    }
    
    // Use WARN for errors that need to be visible
    public static void LogError(string operation, Exception ex, object context = null)
    {
        Logger.Warn(ex, $"[UniPlaySong] Error in {operation}" + 
                        (context != null ? $" - {context}" : ""));
    }
}
```

**Status**: **MEDIUM PRIORITY** - Account for Playnite's log filtering quirks.

---

### 5.3 Validation and Error Messages (Low Priority)

**Current Issue**: Limited input validation
- Settings validation minimal
- File path validation could be better
- User error messages could be more helpful

**Recommendation**:
```csharp
// Add validation:
public class SettingsValidator
{
    public ValidationResult Validate(UniPlaySongSettings settings)
    {
        var errors = new List<string>();
        
        if (settings.MusicVolume < 0 || settings.MusicVolume > 100)
            errors.Add("Music volume must be between 0 and 100");
        
        if (settings.FadeInDuration < 0)
            errors.Add("Fade-in duration must be positive");
        
        return new ValidationResult(errors);
    }
}
```

---

## 6. Architecture Improvements

### 6.1 Dependency Injection (Need Clarification)

**Current Issue**: Manual dependency creation
- Services created in `UniPlaySong.cs` constructor
- Hard to test
- Hard to swap implementations

**User Feedback**: Need clarification on dependency injection/creation item.

**Options**:
1. **Lightweight DI Container** (e.g., SimpleInjector, Autofac) - Adds external dependency
2. **Manual DI** (current approach, but better organized) - No external dependencies
3. **Service Locator** (anti-pattern, not recommended)

**Recommendation**: 
- **Keep manual DI for now** (simpler, no external dependencies)
- Organize service creation better (factory pattern)
- Consider DI container only if we need complex lifecycle management

**Status**: **DEFERRED** - Keep current approach, organize better.

---

### 6.2 Event-Driven Architecture (Deferred - Direct Calls Preferred)

**Current Issue**: Direct method calls for coordination
- Tight coupling between components
- Hard to extend
- Hard to test

**User Feedback**: 
- Thought direct calls were beneficial in the long-term
- Open to changing mind if benefits are clarified
- Nervous about messing with fader and playback music since fullscreen music works reliably

**EventBus Pattern**:
```csharp
// Components publish events
_eventBus.Publish(new GameSelectedEvent(game));

// Components subscribe to events
_eventBus.Subscribe<GameSelectedEvent>(e => _playbackService.PlayGameMusic(e.Game));
```

**Pros of Direct Calls** (Current):
- ✅ Simple, explicit, easy to debug
- ✅ No indirection, clear call chain
- ✅ Better performance (no event overhead)
- ✅ Works reliably (user's concern)

**Pros of EventBus**:
- ✅ Loose coupling
- ✅ Easy to add new subscribers
- ✅ Better for complex event flows

**Recommendation**: **Keep direct calls**. EventBus adds complexity without clear benefit for our use case. Only consider if we need multiple components reacting to the same events.

**Status**: **DEFERRED** - Keep direct method calls for reliability.

---

## 7. Testing Opportunities

### 7.1 Unit Tests (High Priority)

**Missing**: No unit tests currently

**Recommendations**:
- Test `MusicFader` logic
- Test `GameMusicFileService` path logic
- Test downloaders (with mocks)
- Test settings validation

---

### 7.2 Integration Tests (Medium Priority)

**Missing**: No integration tests

**Recommendations**:
- Test full download flow
- Test playback service integration
- Test dialog navigation

---

## 8. Documentation Improvements

### 8.1 Code Documentation (Medium Priority)

**Current Issue**: Some methods lack XML documentation

**Recommendation**: Add XML docs to all public APIs:
```csharp
/// <summary>
/// Downloads selected songs and shows progress inline in the dialog.
/// </summary>
/// <remarks>
/// This method runs asynchronously and updates the progress bar
/// in real-time. The dialog remains open during downloads.
/// </remarks>
public void DownloadSelectedSongs() { ... }
```

---

### 8.2 Architecture Diagrams (Low Priority)

**Recommendation**: Create diagrams showing:
- Service dependencies
- Event flow
- Data flow
- Component relationships

---

## 9. Priority Recommendations (Updated Based on User Feedback)

### 🔴 CRITICAL (Fix Immediately)
1. ✅ **Memory Leak Fix** - Preview file cleanup (user confirmed critical)
2. ✅ **SkipFirstSelectMusic Bug** - Setting exists but doesn't work (user confirmed bug)

### 🟠 HIGH PRIORITY (Next Version)
3. ✅ **Default Music Support** - Fallback music with suppress option (user requested)
4. ✅ **Service Separation** - Better architecture (user approved)
5. ✅ **Error Handler Service** - Centralized error handling (user approved)
6. ✅ **PathService** - Centralized path management (user approved)
7. ✅ **Constants Class** - Extract magic numbers (user approved)

### 🟡 MEDIUM PRIORITY (Future Versions)
8. ⚠️ **Caching Improvements** - Song/album result caching (user approved)
9. ⚠️ **String Operations** - Use StringBuilder (user approved)
10. ⚠️ **Logging Consistency** - Standardized logging with Playnite quirks (user approved)
11. ⚠️ **Settings Service** - Centralized settings (user approved)
12. ⚠️ **Localization Skeleton** - Framework for future translations (user approved)

### 🟢 LOW PRIORITY (Backlog)
13. 💡 **Platform Music Support** - After understanding use case (user needs clarification)
14. 💡 **Filter Music Support** - After understanding use case (user needs clarification)
15. 💡 **LINQ Optimization** - Performance improvements (user open to it)
16. 💡 **Async/Await Improvements** - Better async patterns (user needs to understand more)
17. 💡 **Code Documentation** - XML docs (user approved, but reduce verbosity for publication)
18. 💡 **Architecture Diagrams** - Visual documentation (user approved)

### ⚪ DEFERRED / OUT OF SCOPE
- ❌ **Sound Effects System** - Out of scope (user confirmed)
- ❌ **Preview Audio Pack** - Different scope than PNS (user noted)
- ❌ **AudioState** - Need clarification on benefit (user needs clarification)
- ❌ **Music Type Selection** - Focus on fundamentals first (user preference)
- ❌ **Race Condition in Download Progress** - Deferred (user deferred)
- ❌ **Null Reference in Fade-Out** - Too risky (user concern)
- ❌ **File Lock Issues** - Need clarification (user needs clarification)
- ❌ **Dependency Injection** - Keep manual DI (user preference)
- ❌ **EventBus** - Direct calls preferred (user preference)
- ❌ **Unit Tests** - Manual testing preferred (user preference)

---

## 10. Conclusion (Updated Based on User Feedback)

The UniPlaySong codebase is well-structured but has opportunities for improvement. Based on user feedback, the priorities are:

### Immediate Focus (Critical)
1. **Bug Fixes**: Memory leak in preview files, SkipFirstSelectMusic not working
2. **Reliability**: Keep music previews reliable, smooth, and compatible with all themes

### Next Version Focus (High Priority)
3. **Default Music Support**: Fallback music with suppress option
4. **Code Quality**: Service separation, error handling, path management, constants
5. **Maintainability**: Better organization without breaking working features

### Future Considerations
6. **Platform/Filter Music**: Only if use cases are understood
7. **Localization**: Skeleton framework for future translations
8. **Optimizations**: Caching, string operations, LINQ improvements

### Out of Scope
- Sound Effects (user specifically forked to avoid this)
- Complex architectural patterns (keep it simple, reliable)
- Features that risk breaking working functionality

**Key Principle**: Keep scope tight - ensure music previews are reliable, smooth, and compatible with as many Playnite themes and fullscreen modes as possible. Improvements should go towards that goal.

---

## Appendix: Comparison Matrix

| Feature | PlayniteSound | UniPlaySong | Priority |
|--------|---------------|-------------|----------|
| Game Music | ✅ | ✅ | - |
| Sound Manager | ✅ | ❌ | High |
| Platform Music | ✅ | ❌ | Medium |
| Filter Music | ✅ | ❌ | Medium |
| Default Music | ✅ | ❌ | Low |
| Sound Effects | ✅ | ❌ | High |
| Audio State Management | ✅ | ❌ | High |
| Music Type Selection | ✅ | ❌ | Medium |
| YouTube Downloads | ✅ | ✅ | - |
| KHInsider Downloads | ✅ | ✅ | - |
| Inline Download Progress | ❌ | ✅ | - |
| Controller Support Analysis | ❌ | ✅ | - |
| Localization | ✅ | ❌ | Low |

---

## 11. Related Documents

- **[ACTION_PLAN_v1.0.4.md](ACTION_PLAN_v1.0.4.md)** - Detailed action plan with implementation steps, code examples, and clarifications

---

**Document Version**: 1.1  
**Last Updated**: 2025-11-30  
**Updated Based On**: User feedback and clarifications  
**Next Review**: After implementing critical items

