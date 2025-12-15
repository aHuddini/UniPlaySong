# Action Plan - UniPlaySong v1.0.4+

**Date**: 2025-11-30  
**Based on**: Codebase Analysis & User Feedback  
**Focus**: Reliability, Music Previews, Universal Compatibility

---

## Priority Tiers

### 🔴 **CRITICAL** (Fix Immediately)
1. ✅ **Memory Leak: Preview File Cleanup** - Temp files not deleted (COMPLETED)
2. ✅ **SkipFirstSelectMusic Not Working** - Setting exists but doesn't function (COMPLETED - v1.0.4)

### 🟠 **HIGH** (Next Version)
3. **Default Music Support** - Fallback music with suppress option
4. **Service Separation** - Break down monolithic code
5. **Error Handler Service** - Centralized error handling
6. **PathService** - Centralized path management
7. **Constants Class** - Extract magic numbers

### 🟡 **MEDIUM** (Future Versions)
8. **Caching Improvements** - Song/album result caching
9. **String Operations** - Use StringBuilder where needed
10. **Logging Consistency** - Standardized logging (accounting for Playnite quirks)
11. **Settings Service** - Centralized settings management
12. **Localization Skeleton** - Framework for future translations

### 🟢 **LOW** (Backlog)
13. **Platform Music Support** - After understanding use case
14. **Filter Music Support** - After understanding use case
15. **LINQ Optimization** - Performance improvements
16. **Async/Await Improvements** - Better async patterns
17. **Code Documentation** - XML docs for public APIs
18. **Architecture Diagrams** - Visual documentation

### ⚪ **DEFERRED** (No Action)
- Sound Effects System (out of scope)
- Preview Audio Pack (different scope than PNS)
- AudioState (need clarification on benefit)
- Music Type Selection (focus on fundamentals first)
- Race Condition in Download Progress (deferred)
- Null Reference in Fade-Out (too risky)
- File Lock Issues (need clarification)
- Dependency Injection (need clarification)
- EventBus (need clarification - direct calls preferred)
- Unit Tests (manual testing preferred for now)

---

## Detailed Action Items

### 🔴 CRITICAL PRIORITY

#### 1. Memory Leak: Preview File Cleanup
**Issue**: Temporary preview files accumulate in `%TEMP%\UniPlaySong\Preview\`

**Location**: `DownloadDialogViewModel.cs` - `PreviewSong()`, `GetTempPathForPreview()`

**Solution**:
```csharp
// Track preview files
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

**Files to Modify**:
- `UniPSong/ViewModels/DownloadDialogViewModel.cs`
- `UniPSong/Services/DownloadDialogService.cs` (call cleanup on dialog close)

---

#### 2. SkipFirstSelectMusic Not Working
**Issue**: Setting `SkipFirstSelectionAfterModeSwitch` exists but doesn't prevent music on first selection

**Location**: `UniPlaySong.cs` - `OnGameSelected()`, `ShouldPlayMusic()`

**Current Problem**: 
- `_firstSelect` is cleared immediately (line 180)
- `ResumeMusic()` path doesn't check `_firstSelect`
- Complex login skip logic interferes

**Solution**: Implement double-check pattern like PNS:
```csharp
// In ShouldPlayMusic() - add check:
private bool ShouldPlayMusic()
{
    if (_settings == null || !_settings.EnableMusic) return false;
    if (_playbackService == null) return false;
    if (_settings.MusicVolume <= 0) return false;
    if (_settings.VideoIsPlaying) return false;
    if (_loginSkipActive) return false;
    
    // ADD THIS: Check first select skip
    if (_firstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
    {
        _fileLogger?.Info("Skipping music - first select and SkipFirstSelectionAfterModeSwitch enabled");
        return false;
    }

    // Check mode-based settings
    var state = _settings.MusicState;
    if (IsFullscreen && state != AudioState.Fullscreen && state != AudioState.Always) return false;
    if (IsDesktop && state != AudioState.Desktop && state != AudioState.Always) return false;

    return true;
}

// In OnGameSelected() - simplify:
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var game = args?.NewValue?.FirstOrDefault();
    if (game == null || _settings?.EnableMusic != true)
    {
        PlayMusicForGame(null);
        _firstSelect = false;
        return;
    }

    var wasFirstSelect = _firstSelect;
    _firstSelect = false; // Clear immediately (PNS pattern)

    // Theme-compatible login skip (keep this)
    if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && IsFullscreen)
    {
        _loginSkipActive = true;
        return;
    }

    // Normal skip - ShouldPlayMusic() will handle the check
    if (ShouldPlayMusic())
    {
        PlayMusicForGame(game);
    }
}

// In ResumeMusic() - ensure it goes through ShouldPlayMusic():
private void ResumeMusic()
{
    if (ShouldPlayMusic())
    {
        PlayMusicBasedOnSelected();
    }
}
```

**Files to Modify**:
- `UniPSong/UniPlaySong.cs` - `OnGameSelected()`, `ShouldPlayMusic()`, `ResumeMusic()`

**Testing**: 
- Enable `SkipFirstSelectionAfterModeSwitch`
- Switch to fullscreen mode
- First game selection should NOT play music
- Second selection should play music

---

### 🟠 HIGH PRIORITY

#### 3. Default Music Support ✅ COMPLETE
**Feature**: Fallback default music when no game music found

**Status**: 
- ✅ Basic default music playback implemented
- ✅ Single-player architecture (simplified from dual-player)
- ✅ Position preservation working
- ✅ Smooth fade transitions
- See `DEFAULT_MUSIC_IMPLEMENTATION.md` for technical details

**Requirements** (User Decisions):
- ✅ Separate settings section: "Default Music"
- ✅ Setting: `EnableDefaultMusic` (bool)
- ✅ Setting: `DefaultMusicPath` (string) - file picker
- ✅ Setting: `SuppressPlayniteBackgroundMusic` (bool) - suppress Playnite's native music
- ✅ When no game music found AND `EnableDefaultMusic` is true, play default music
- ✅ Integrate with existing `SuppressNativeMusic()` method
- ✅ **Single file support** (playlist support deferred to future)
- ✅ **Same volume as game music** (no separate volume control)
- ✅ **Same fade duration** (uses existing game music fader system)

**Implementation**:
```csharp
// In UniPlaySongSettings.cs:
public bool EnableDefaultMusic { get; set; } = false;
public string DefaultMusicPath { get; set; } = string.Empty;
public bool SuppressPlayniteBackgroundMusic { get; set; } = true;

// In MusicPlaybackService.cs - PlayGameMusic():
if (songs.Count == 0)
{
    // Check for default music
    if (settings?.EnableDefaultMusic == true && 
        !string.IsNullOrEmpty(settings.DefaultMusicPath) &&
        File.Exists(settings.DefaultMusicPath))
    {
        _fileLogger?.Info($"No game music found, playing default music: {settings.DefaultMusicPath}");
        // Play default music instead of fading out
        PlayDefaultMusic(settings.DefaultMusicPath);
        return;
    }
    
    FadeOutAndStop();
    return;
}

// In UniPlaySong.cs - SuppressNativeMusic():
private void SuppressNativeMusic()
{
    if (!IsFullscreen) return;
    if (_settings?.SuppressPlayniteBackgroundMusic != true) return; // NEW CHECK
    
    // ... existing suppression code ...
}
```

**Files to Create/Modify**:
- `UniPSong/UniPlaySongSettings.cs` - Add properties
- `UniPSong/UniPlaySongSettingsView.xaml` - Add UI section
- `UniPSong/Services/MusicPlaybackService.cs` - Add default music logic
- `UniPSong/UniPlaySong.cs` - Update `SuppressNativeMusic()`

---

#### 4. Service Separation
**Goal**: Break down `UniPlaySong.cs` (600+ lines) into focused services

**Services to Create**:
1. `GameSelectionService.cs` - Handles `OnGameSelected` logic
2. `ApplicationLifecycleService.cs` - Handles `OnApplicationStarted/Stopped`
3. `MusicCoordinationService.cs` - Coordinates playback with events

**Pattern** (inspired by PNS but adapted):
```csharp
// GameSelectionService.cs
public class GameSelectionService
{
    private readonly IMusicPlaybackService _playbackService;
    private readonly UniPlaySongSettings _settings;
    private bool _firstSelect = true;
    private bool _loginSkipActive = false;
    
    public void HandleGameSelected(Game game, bool isFullscreen)
    {
        // Move OnGameSelected logic here
    }
    
    public bool ShouldPlayMusic()
    {
        // Move ShouldPlayMusic logic here
    }
}

// In UniPlaySong.cs:
private GameSelectionService _gameSelectionService;

public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var game = args?.NewValue?.FirstOrDefault();
    _gameSelectionService.HandleGameSelected(game, IsFullscreen);
}
```

**Files to Create**:
- `UniPSong/Services/GameSelectionService.cs`
- `UniPSong/Services/ApplicationLifecycleService.cs`
- `UniPSong/Services/MusicCoordinationService.cs`

**Files to Modify**:
- `UniPSong/UniPlaySong.cs` - Delegate to services

---

#### 5. Error Handler Service
**Goal**: Centralized error handling to reduce try-catch proliferation

**Implementation**:
```csharp
// Common/ErrorHandler.cs
public static class ErrorHandler
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    
    public static void Try(Action action, string context = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            var message = context != null 
                ? $"Error in {context}" 
                : "Error in operation";
            Logger.Error(ex, $"[UniPlaySong] {message}");
        }
    }
    
    public static T Try<T>(Func<T> func, T defaultValue = default, string context = null)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            var message = context != null 
                ? $"Error in {context}" 
                : "Error in operation";
            Logger.Error(ex, $"[UniPlaySong] {message}");
            return defaultValue;
        }
    }
    
    public static async Task TryAsync(Func<Task> action, string context = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var message = context != null 
                ? $"Error in {context}" 
                : "Error in operation";
            Logger.Error(ex, $"[UniPlaySong] {message}");
        }
    }
}

// Usage:
ErrorHandler.Try(() => 
{
    // risky operation
}, "downloading song");
```

**Files to Create**:
- `UniPSong/Common/ErrorHandler.cs`

**Files to Refactor**:
- Replace try-catch blocks with `ErrorHandler.Try()` where appropriate
- Keep try-catch for operations that need specific error handling

---

#### 6. PathService
**Goal**: Centralize all path construction logic

**Implementation**:
```csharp
// Services/PathService.cs
public class PathService
{
    private readonly string _extensionDataPath;
    
    public PathService(IPlayniteAPI api)
    {
        _extensionDataPath = api.Paths.ExtensionsDataPath;
    }
    
    public string GetGameMusicPath(Game game)
    {
        return Path.Combine(
            _extensionDataPath,
            "UniPlaySong",
            "Music",
            game.Id.ToString());
    }
    
    public string GetPreviewTempPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "UniPlaySong",
            "Preview");
    }
    
    public string GetDownloadPath(Game game)
    {
        return GetGameMusicPath(game);
    }
}
```

**Files to Create**:
- `UniPSong/Services/PathService.cs`

**Files to Modify**:
- `UniPSong/Services/GameMusicFileService.cs` - Use PathService
- `UniPSong/ViewModels/DownloadDialogViewModel.cs` - Use PathService
- `UniPSong/Downloaders/*.cs` - Use PathService

---

#### 7. Constants Class
**Goal**: Extract magic numbers and strings

**Implementation**:
```csharp
// Common/Constants.cs
public static class Constants
{
    // Timeouts
    public const int DownloadTimeoutMs = 30000;
    public const int PreviewDurationSeconds = 30;
    public const int FileLockRetryDelayMs = 100;
    public const int MaxRetryAttempts = 3;
    
    // Fade durations
    public const double DefaultFadeInDuration = 0.5;
    public const double DefaultFadeOutDuration = 0.3;
    public const double MinFadeDuration = 0.05;
    public const double MaxFadeDuration = 10.0;
    
    // Volume
    public const double PreviewVolume = 0.7;
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    
    // Paths
    public const string PreviewTempFolder = "UniPlaySong\\Preview";
    public const string MusicFolder = "Music";
    
    // File extensions
    public static readonly string[] SupportedAudioExtensions = 
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg" };
}
```

**Files to Create**:
- `UniPSong/Common/Constants.cs`

**Files to Modify**:
- Replace magic numbers/strings throughout codebase

---

### 🟡 MEDIUM PRIORITY

#### 8. Caching Improvements
**Implementation**: Add caching for song lists and album search results

#### 9. String Operations
**Implementation**: Use `StringBuilder` for repeated concatenation

#### 10. Logging Consistency
**Implementation**: Create `LogHelper` class, account for Playnite's log level quirks

#### 11. Settings Service
**Implementation**: Centralized settings access with change events

#### 12. Localization Skeleton
**Implementation**: Create localization structure for future translations

---

## Clarifications Needed

### Platform Music Support
**Question**: What does "platform" mean in practice?

**Answer from PNS Code**:
- Platform = Gaming Platform (Xbox, PlayStation, PC, etc.)
- When `MusicType.Platform` is selected:
  - Music is organized by platform: `{MusicPath}/Platform/{PlatformName}/`
  - When you select an Xbox game, it plays music from `Platform/Xbox/` folder
  - If that game has no music, it can fall back to platform music
  - You can also collect music from all games on that platform

**Use Case**: 
- User has Xbox games and PlayStation games
- User wants different music for Xbox vs PlayStation
- User puts Xbox music in `Platform/Xbox/` folder
- User puts PlayStation music in `Platform/PlayStation/` folder

**Recommendation**: Defer until we understand if users want this feature.

---

### Filter Music Support
**Question**: How does filter music work in practice?

**Answer from PNS Code**:
- Filter = Playnite Filter Preset (custom filters you create)
- When `MusicType.Filter` is selected:
  - Music is organized by filter: `{MusicPath}/Filter/{FilterId}/`
  - When you apply a filter (e.g., "Favorites", "Recently Played"), it plays music from that filter's folder
  - Music changes when you switch filters

**Use Case**:
- User creates filter "Action Games"
- User creates filter "RPG Games"
- User wants different music for each filter
- User puts action game music in `Filter/{ActionFilterId}/` folder
- User puts RPG music in `Filter/{RPGFilterId}/` folder

**Recommendation**: Defer until we understand if users want this feature.

---

### AudioState Benefit
**Question**: How does AudioState benefit our current architecture?

**Current State**: We have `MusicState` (AudioState enum) but it's not fully utilized.

**PNS Benefit**:
- `AudioState.Never` - Music never plays
- `AudioState.Desktop` - Music only in desktop mode
- `AudioState.Fullscreen` - Music only in fullscreen mode
- `AudioState.Always` - Music in both modes

**Our Current Implementation**:
- We check `MusicState` in `ShouldPlayMusic()` but it's basic
- We could enhance it to match PNS's granular control

**Recommendation**: Keep current implementation, enhance if users request more granular control.

---

### Player Management
**Question**: What are the downsides of having one `IMusicPlayer` instance?

**Current State**: Single `SDL2MusicPlayer` instance for all music playback.

**Potential Issues**:
- Can't play multiple sounds simultaneously (not needed for music previews)
- Can't preload next song while current is playing (we do have preload support)
- Memory: One instance is more efficient than multiple

**PNS Approach**: Uses `PlayerEntry` dictionary for sound effects (multiple sounds), but single player for music.

**Recommendation**: Current approach is fine for music-only extension. No changes needed unless we add sound effects.

---

### File Lock Issues
**Question**: Can we do separation of concern on file lock issues?

**Current Issue**: Files may be locked when trying to delete/move (no retry logic).

**Separation of Concern**:
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
                Thread.Sleep(100 * (i + 1));
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

**Recommendation**: Create `FileOperationsService` for all file operations with retry logic.

---

### Dependency Injection
**Question**: Clarify dependency injection/creation item.

**Current State**: Manual dependency creation in `UniPlaySong.cs` constructor.

**DI Options**:
1. **Lightweight DI Container** (e.g., SimpleInjector, Autofac)
2. **Manual DI** (current approach, but better organized)
3. **Service Locator** (anti-pattern, not recommended)

**Recommendation**: 
- Keep manual DI for now (simpler, no external dependencies)
- Organize service creation better (factory pattern)
- Consider DI container only if we need complex lifecycle management

---

### EventBus
**Question**: Clarify EventBus - I thought direct calls were beneficial.

**Current State**: Direct method calls between components.

**EventBus Pattern**:
```csharp
// Components publish events
_eventBus.Publish(new GameSelectedEvent(game));

// Components subscribe to events
_eventBus.Subscribe<GameSelectedEvent>(e => _playbackService.PlayGameMusic(e.Game));
```

**Pros of Direct Calls** (Current):
- Simple, explicit, easy to debug
- No indirection, clear call chain
- Better performance (no event overhead)

**Pros of EventBus**:
- Loose coupling
- Easy to add new subscribers
- Better for complex event flows

**Recommendation**: **Keep direct calls**. EventBus adds complexity without clear benefit for our use case. Only consider if we need multiple components reacting to the same events.

---

## Testing Strategy

### Manual Testing (Preferred)
- Test in Playnite desktop mode
- Test in Playnite fullscreen mode
- Test with different themes
- Test with ANIKI theme (login screen)
- Test music transitions
- Test download flows

### Automated Testing (Future)
- Unit tests for `MusicFader` logic
- Unit tests for `PathService`
- Unit tests for `ErrorHandler`
- Integration tests for download flow (with mocks)

---

## Success Criteria

### Critical Items
- ✅ Preview files are cleaned up (no disk space waste)
- ✅ SkipFirstSelectMusic setting works correctly
- ✅ No music plays on first selection when setting enabled

### High Priority Items
- ✅ Default music plays when no game music found
- ✅ Playnite background music can be suppressed via setting
- ✅ Code is more maintainable (services separated)
- ✅ Error handling is consistent
- ✅ Path logic is centralized

---

## Timeline

### Immediate (This Week)
- Memory leak fix
- SkipFirstSelectMusic fix

### Next Version (v1.0.5)
- Default Music Support
- Service Separation (partial)
- Error Handler Service
- PathService
- Constants Class

### Future Versions
- Remaining medium/low priority items
- Platform/Filter music (if requested)
- Localization (when ready)

---

**Document Version**: 1.0  
**Last Updated**: 2025-11-30  
**Next Review**: After critical items completed

