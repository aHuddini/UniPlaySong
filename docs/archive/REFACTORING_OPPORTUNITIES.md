# Refactoring Opportunities - UniPlaySong v1.0.4+

**Date**: 2025-12-07  
**Status**: Default Music Position Preservation ✅ COMPLETE  
**Next Focus**: Code Quality & Architecture Improvements

---

## ✅ Recently Completed

### Default Music Position Preservation (v1.0.4)
- ✅ Single-player architecture (simplified from dual-player)
- ✅ Position preservation when switching between games
- ✅ Smooth fade-out/fade-in transitions
- ✅ Works with both SDL2 and WPF MediaPlayer backends

---

## 🟠 HIGH PRIORITY Refactoring Opportunities

### 1. Service Separation
**Goal**: Break down `UniPlaySong.cs` into focused services

**Current State**:
- `UniPlaySong.cs` is still ~500+ lines
- Mixes Playnite integration, business logic, and coordination

**Proposed Services**:
1. **`GameSelectionService.cs`** - Handles `OnGameSelected` logic
   - Move `OnGameSelected()` logic here
   - Handle first-select skip logic
   - Theme-compatible login skip logic

2. **`ApplicationLifecycleService.cs`** - Handles app lifecycle
   - `OnApplicationStarted()` / `OnApplicationStopped()`
   - Initialization logic
   - Cleanup logic

3. **`MusicCoordinationService.cs`** - Coordinates playback with events
   - View change handling
   - Video state change handling
   - Mode switch handling

**Benefits**:
- Better separation of concerns
- Easier to test individual components
- More maintainable codebase
- Matches modern service-oriented architecture

**Files to Create**:
- `UniPSong/Services/GameSelectionService.cs`
- `UniPSong/Services/ApplicationLifecycleService.cs`
- `UniPSong/Services/MusicCoordinationService.cs`

**Files to Modify**:
- `UniPSong/UniPlaySong.cs` - Delegate to services

---

### 2. Error Handler Service
**Goal**: Centralized error handling and logging

**Current State**:
- Error handling scattered throughout codebase
- Inconsistent logging patterns
- Some errors silently swallowed

**Proposed Service**:
```csharp
public class ErrorHandlerService
{
    public void HandleError(Exception ex, string context);
    public void LogInfo(string message);
    public void LogWarning(string message);
    public void LogError(string message, Exception ex = null);
}
```

**Benefits**:
- Consistent error handling
- Centralized logging configuration
- Better debugging capabilities
- User-friendly error messages

---

### 3. PathService
**Goal**: Centralized path management

**Current State**:
- Path logic scattered across multiple files
- Hardcoded paths in some places
- Inconsistent path handling

**Proposed Service**:
```csharp
public class PathService
{
    public string GetGameMusicDirectory(Game game);
    public string GetExtensionDirectory();
    public string GetTempDirectory();
    public string GetLogDirectory();
}
```

**Benefits**:
- Single source of truth for paths
- Easier to change path structure
- Better testability
- Consistent path handling

---

### 4. Constants Class
**Goal**: Extract magic numbers and strings

**Current State**:
- Magic numbers scattered throughout code (e.g., fade durations, volume ranges)
- Hardcoded strings for file extensions, directory names
- Inconsistent default values

**Proposed Class**:
```csharp
public static class Constants
{
    // Fade durations
    public const double DefaultFadeInDuration = 0.5;
    public const double DefaultFadeOutDuration = 0.3;
    public const double MinFadeDuration = 0.05;
    public const double MaxFadeDuration = 10.0;
    
    // Volume
    public const double MinVolume = 0.0;
    public const double MaxVolume = 1.0;
    
    // File extensions
    public static readonly string[] SupportedAudioExtensions = { ".mp3", ".wav", ".ogg", ".flac" };
    
    // Directory names
    public const string MusicDirectoryName = "Music";
    public const string TempDirectoryName = "UniPlaySong";
}
```

**Benefits**:
- Easier to maintain
- Self-documenting code
- Prevents typos
- Centralized configuration

---

## 🟡 MEDIUM PRIORITY Refactoring Opportunities

### 5. Caching Improvements
**Goal**: Cache song/album results to reduce API calls

**Current State**:
- Every search/download makes fresh API calls
- No caching of results
- Can be slow with large libraries

**Proposed**:
- Cache search results (with TTL)
- Cache album listings
- Invalidate cache on manual refresh

**Benefits**:
- Faster response times
- Reduced API load
- Better user experience

---

### 6. String Operations Optimization
**Goal**: Use StringBuilder where appropriate

**Current State**:
- String concatenation in loops
- Multiple string operations
- Potential performance issues

**Proposed**:
- Identify hot paths with string operations
- Replace with StringBuilder where beneficial
- Profile before optimizing

---

### 7. Logging Consistency
**Goal**: Standardized logging across codebase

**Current State**:
- Mix of `Logger.Info()`, `_fileLogger?.Info()`, `Console.WriteLine()`
- Inconsistent log levels
- Some areas lack logging

**Proposed**:
- Standardize on `FileLogger` for all logging
- Consistent log level usage
- Structured logging format
- Account for Playnite's logging quirks

---

### 8. Settings Service
**Goal**: Centralized settings management

**Current State**:
- Settings accessed directly from `UniPlaySongSettings`
- No validation layer
- Settings changes scattered

**Proposed**:
```csharp
public class SettingsService
{
    public UniPlaySongSettings Settings { get; }
    public void UpdateSettings(Action<UniPlaySongSettings> update);
    public void ValidateSettings();
    public event EventHandler SettingsChanged;
}
```

**Benefits**:
- Centralized validation
- Event-driven updates
- Better testability
- Consistent settings access

---

### 9. Localization Skeleton
**Goal**: Framework for future translations

**Current State**:
- All strings hardcoded in English
- No localization infrastructure

**Proposed**:
- Create resource files structure
- Extract user-facing strings
- Add localization helper class
- Prepare for future translations

**Benefits**:
- Ready for internationalization
- Easier to add languages later
- Better code organization

---

## 🟢 LOW PRIORITY / FUTURE CONSIDERATIONS

### 10. Platform Music Support
**Status**: Needs clarification on use case
- Similar to default music but per-platform
- Would require platform detection
- Need to understand user demand

### 11. Filter Music Support
**Status**: Needs clarification on use case
- Music per filter/collection
- Would require filter detection
- Need to understand user demand

### 12. LINQ Optimization
**Status**: Performance improvements
- Profile first to identify bottlenecks
- Optimize LINQ queries where beneficial
- Consider async alternatives

### 13. Async/Await Improvements
**Status**: Better async patterns
- Current async usage is minimal
- Could improve download operations
- Need to understand Playnite's async constraints

### 14. Code Documentation
**Status**: XML docs for public APIs
- Add XML documentation comments
- Focus on public interfaces
- Keep concise (user preference)

### 15. Architecture Diagrams
**Status**: Visual documentation
- Service interaction diagrams
- Data flow diagrams
- Component architecture

---

## ⚪ DEFERRED / OUT OF SCOPE

- **Sound Effects System** - Out of scope (different feature set)
- **Preview Audio Pack** - Different scope than PNS
- **AudioState** - Need clarification on benefit
- **Music Type Selection** - Focus on fundamentals first
- **Dependency Injection** - Need clarification (direct calls preferred)
- **EventBus** - Need clarification (direct calls preferred)
- **Unit Tests** - Manual testing preferred for now

---

## Recommended Next Steps

1. **Start with Service Separation** - Biggest architectural improvement
2. **Add Constants Class** - Quick win, improves maintainability
3. **Create PathService** - Centralizes path logic
4. **Implement Error Handler Service** - Improves debugging
5. **Standardize Logging** - Better observability

---

## Notes

- All refactoring should maintain backward compatibility
- Test thoroughly after each refactoring
- Follow PlayniteSound patterns where applicable
- Keep code simple and focused
- Document significant architectural changes

---

**Last Updated**: 2025-12-07  
**Default Music Feature**: ✅ Complete

