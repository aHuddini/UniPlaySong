# Error Handler Service - Design Document

**Date**: 2025-12-07  
**Status**: Proposed (Not Yet Implemented)  
**Priority**: Medium (Low Risk, Medium Value)  
**Estimated Time**: 2-3 hours

---

## Overview

The Error Handler Service is a centralized wrapper for consistent error handling and logging across the UniPlaySong codebase. It provides a unified way to handle exceptions, log errors, and optionally display user-friendly error messages.

---

## Current State Analysis

### Current Error Handling Patterns

**Pattern 1: Basic Try-Catch with Logging** (Most Common)
```csharp
// From DownloadManager.cs (lines 54-58)
try
{
    // ... operation ...
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error getting albums for '{gameName}' from {source}: {ex.Message}");
    return Enumerable.Empty<Album>();
}
```

**Pattern 2: Try-Catch with User Notification**
```csharp
// From GameMenuHandler.cs (lines 99-103)
try
{
    // ... operation ...
}
catch (Exception ex)
{
    _logger.Error(ex, $"Error in DownloadMusicForGame: {ex.Message}");
    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
}
```

**Pattern 3: Try-Catch with Dual Logging**
```csharp
// From MusicPlaybackService.cs (lines 405-409)
try
{
    // ... operation ...
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error playing music for game '{game?.Name}'");
    _fileLogger?.Error($"Error: {ex.Message}", ex);
}
```

**Pattern 4: Silent Failure (Return Default)**
```csharp
// Some methods return empty collections or null on error
catch (Exception ex)
{
    Logger.Error(ex, "Error message");
    return null; // or Enumerable.Empty<T>()
}
```

### Current Issues

1. **Inconsistent Error Messages**
   - Some errors show user dialogs, others don't
   - Error messages vary in format and detail
   - Some errors are logged but not visible to users

2. **Scattered Error Handling**
   - Error handling logic duplicated across 85+ catch blocks
   - No centralized error recovery strategy
   - Inconsistent logging patterns

3. **Missing Context**
   - Some errors lack context about what operation failed
   - No standardized error categorization
   - Difficult to track error patterns

4. **User Experience**
   - Some critical errors shown to users, others silently logged
   - No consistent error message format
   - Technical error messages shown directly to users

---

## Proposed Solution: Error Handler Service

### Design Philosophy

**Wrapper Pattern** (Not Replacement):
- Wrap existing error handling, don't replace it initially
- Add incrementally, starting with non-critical areas
- Maintain backward compatibility
- Low risk - can be added gradually

### Service Interface

```csharp
namespace UniPlaySong.Services
{
    /// <summary>
    /// Centralized error handling and logging service
    /// </summary>
    public class ErrorHandlerService
    {
        private readonly ILogger _logger;
        private readonly FileLogger _fileLogger;
        private readonly IPlayniteAPI _playniteApi;

        public ErrorHandlerService(ILogger logger, FileLogger fileLogger, IPlayniteAPI playniteApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileLogger = fileLogger;
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        }

        /// <summary>
        /// Executes an action with error handling
        /// </summary>
        public void Try(Action action, string context = null, bool showUserMessage = false)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                HandleError(ex, context, showUserMessage);
            }
        }

        /// <summary>
        /// Executes a function with error handling, returns default value on error
        /// </summary>
        public T Try<T>(Func<T> func, T defaultValue = default, string context = null, bool showUserMessage = false)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                HandleError(ex, context, showUserMessage);
                return defaultValue;
            }
        }

        /// <summary>
        /// Handles an exception with logging and optional user notification
        /// </summary>
        public void HandleError(Exception ex, string context = null, bool showUserMessage = false, string userFriendlyMessage = null)
        {
            // Log to Playnite logger
            var logMessage = string.IsNullOrEmpty(context) 
                ? $"Error: {ex.Message}" 
                : $"Error in {context}: {ex.Message}";
            
            _logger.Error(ex, logMessage);

            // Log to file logger (detailed)
            _fileLogger?.Error($"Error Details - Context: {context ?? "Unknown"}, Message: {ex.Message}, StackTrace: {ex.StackTrace}", ex);

            // Show user-friendly message if requested
            if (showUserMessage)
            {
                var message = userFriendlyMessage ?? GetUserFriendlyMessage(ex, context);
                _playniteApi.Dialogs.ShowErrorMessage(message, "UniPlaySong");
            }
        }

        /// <summary>
        /// Converts technical exceptions to user-friendly messages
        /// </summary>
        private string GetUserFriendlyMessage(Exception ex, string context)
        {
            // Handle specific exception types
            if (ex is FileNotFoundException)
                return $"File not found. Please check that the file exists and try again.";
            
            if (ex is UnauthorizedAccessException)
                return $"Access denied. Please check file permissions and try again.";
            
            if (ex is IOException)
                return $"File operation failed. The file may be in use by another program.";
            
            if (ex is TimeoutException)
                return $"Operation timed out. Please try again.";
            
            if (ex is ArgumentException || ex is ArgumentNullException)
                return $"Invalid input. Please check your settings and try again.";

            // Generic message with context
            if (!string.IsNullOrEmpty(context))
                return $"An error occurred while {context.ToLower()}. Please check the logs for details.";
            
            return "An unexpected error occurred. Please check the logs for details.";
        }

        /// <summary>
        /// Logs an informational message
        /// </summary>
        public void LogInfo(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Info(message);
            _fileLogger?.Info(message);
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        public void LogWarning(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Warn(message);
            _fileLogger?.Warn(message);
        }
    }
}
```

---

## Usage Examples

### Example 1: Simple Operation (No Return Value)

**Before:**
```csharp
public void Cleanup()
{
    try
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
        Directory.CreateDirectory(_tempPath);
    }
    catch (Exception ex)
    {
        Logger.Warn($"Error cleaning up temp directory: {ex.Message}");
    }
}
```

**After:**
```csharp
public void Cleanup()
{
    _errorHandler.Try(
        () =>
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }
            Directory.CreateDirectory(_tempPath);
        },
        context: "cleaning up temp directory"
    );
}
```

### Example 2: Operation with Return Value

**Before:**
```csharp
public IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false)
{
    try
    {
        var downloader = GetDownloaderForSource(source);
        if (downloader == null)
        {
            Logger.Warn($"No downloader available for source: {source}");
            return Enumerable.Empty<Album>();
        }

        return downloader.GetAlbumsForGame(gameName, cancellationToken, auto);
    }
    catch (Exception ex)
    {
        Logger.Error(ex, $"Error getting albums for '{gameName}' from {source}: {ex.Message}");
        return Enumerable.Empty<Album>();
    }
}
```

**After:**
```csharp
public IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false)
{
    return _errorHandler.Try(
        () =>
        {
            var downloader = GetDownloaderForSource(source);
            if (downloader == null)
            {
                Logger.Warn($"No downloader available for source: {source}");
                return Enumerable.Empty<Album>();
            }

            return downloader.GetAlbumsForGame(gameName, cancellationToken, auto);
        },
        defaultValue: Enumerable.Empty<Album>(),
        context: $"getting albums for '{gameName}' from {source}"
    );
}
```

### Example 3: Operation with User Notification

**Before:**
```csharp
public void DownloadMusicForGame(Game game)
{
    try
    {
        // ... download logic ...
    }
    catch (Exception ex)
    {
        _logger.Error(ex, $"Error in DownloadMusicForGame: {ex.Message}");
        _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
    }
}
```

**After:**
```csharp
public void DownloadMusicForGame(Game game)
{
    _errorHandler.Try(
        () =>
        {
            // ... download logic ...
        },
        context: $"downloading music for '{game.Name}'",
        showUserMessage: true
    );
}
```

### Example 4: Dual Logging (Playback Service)

**Before:**
```csharp
try
{
    // ... playback logic ...
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error playing music for game '{game?.Name}'");
    _fileLogger?.Error($"Error: {ex.Message}", ex);
}
```

**After:**
```csharp
_errorHandler.Try(
    () =>
    {
        // ... playback logic ...
    },
    context: $"playing music for '{game?.Name}'"
);
// ErrorHandlerService automatically handles both loggers
```

---

## Benefits

### 1. Consistency
- **Unified Error Format**: All errors logged with consistent format
- **Standardized Messages**: User-friendly messages follow same pattern
- **Centralized Logic**: Error handling logic in one place

### 2. Maintainability
- **Single Point of Change**: Update error handling logic in one place
- **Easier Debugging**: Consistent error format makes debugging easier
- **Less Code Duplication**: Eliminates 85+ duplicate catch blocks

### 3. User Experience
- **User-Friendly Messages**: Technical errors converted to readable messages
- **Consistent Notifications**: Users see errors in consistent format
- **Better Context**: Errors include context about what operation failed

### 4. Developer Experience
- **Simpler Code**: Less boilerplate try-catch code
- **Clear Intent**: `Try()` method clearly indicates error handling
- **Better Logging**: Automatic dual logging (Playnite + FileLogger)

---

## Implementation Strategy

### Phase 1: Create Service (Low Risk)
1. Create `ErrorHandlerService.cs`
2. Add to dependency injection in `UniPlaySong.cs`
3. **Don't use it yet** - just create it

### Phase 2: Non-Critical Areas (Low Risk)
1. Start with `DownloadManager.cs` (download operations)
2. Then `GameMusicFileService.cs` (file operations)
3. Then `DownloadDialogService.cs` (UI operations)
4. Test thoroughly after each file

### Phase 3: Critical Areas (Medium Risk)
1. **DO NOT** touch `MusicPlaybackService.cs` initially
2. **DO NOT** touch `MusicPlaybackCoordinator.cs` initially
3. Only after Phase 2 is proven stable

### Phase 4: Optional Enhancements
1. Add error categorization (Critical, Warning, Info)
2. Add error counting/statistics
3. Add error recovery strategies
4. Add retry logic for transient errors

---

## Risk Assessment

### Risk Level: ⚠️ **LOW**

**Why Low Risk:**
- Wrapper pattern (doesn't change existing logic)
- Can be added incrementally
- Easy to revert if issues arise
- Doesn't affect core playback logic

**Mitigation:**
- Start with non-critical areas
- Test thoroughly after each change
- Keep existing try-catch blocks initially (add wrapper alongside)
- Don't touch playback services until proven stable

---

## Files to Modify

### Create:
- `UniPSong/Services/ErrorHandlerService.cs`

### Modify (Incremental):
1. **Phase 1** (Low Risk):
   - `UniPSong/Downloaders/DownloadManager.cs`
   - `UniPSong/Services/GameMusicFileService.cs`
   - `UniPSong/Downloaders/YouTubeDownloader.cs`
   - `UniPSong/Downloaders/KHInsiderDownloader.cs`

2. **Phase 2** (Medium Risk):
   - `UniPSong/Services/DownloadDialogService.cs`
   - `UniPSong/ViewModels/DownloadDialogViewModel.cs`
   - `UniPSong/Menus/GameMenuHandler.cs`

3. **Phase 3** (Higher Risk - Defer):
   - `UniPSong/Services/MusicPlaybackService.cs` ⚠️
   - `UniPSong/Services/MusicPlaybackCoordinator.cs` ⚠️

---

## Testing Strategy

### Unit Testing
- Test `Try()` with various exception types
- Test user-friendly message generation
- Test dual logging (Playnite + FileLogger)
- Test default value returns

### Integration Testing
- Test error handling in download operations
- Test error handling in file operations
- Test user notification display
- Verify logs appear in both places

### Regression Testing
- Ensure no functionality broken
- Verify error messages still appear
- Check that logs still work
- Test error scenarios

---

## Comparison with PlayniteSound

PlayniteSound uses a similar pattern:
```csharp
static public void Try(Action action) 
{ 
    try { action(); } 
    catch (Exception ex) { HandleException(ex); } 
}
```

Our implementation adds:
- Return value support (`Try<T>`)
- Context parameter for better logging
- User-friendly message conversion
- Dual logging (Playnite + FileLogger)
- Optional user notification flag

---

## Future Enhancements

### Error Categorization
```csharp
public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public void HandleError(Exception ex, ErrorSeverity severity, ...)
```

### Error Statistics
```csharp
public class ErrorStatistics
{
    public int TotalErrors { get; }
    public Dictionary<string, int> ErrorsByContext { get; }
    public List<Exception> RecentErrors { get; }
}
```

### Retry Logic
```csharp
public T TryWithRetry<T>(Func<T> func, int maxRetries = 3, ...)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try { return func(); }
        catch (Exception ex) when (IsTransientError(ex) && i < maxRetries - 1)
        {
            // Retry
        }
    }
}
```

---

## Decision Points

### Should We Implement This?

**Pros:**
- ✅ Improves code consistency
- ✅ Better user experience
- ✅ Easier maintenance
- ✅ Low risk (wrapper pattern)

**Cons:**
- ⚠️ Adds another service to maintain
- ⚠️ Requires refactoring existing code
- ⚠️ May be overkill for current codebase size

**Recommendation:**
- **YES** - But implement incrementally
- Start with non-critical areas
- Measure value before expanding
- Can stop at any phase if not providing value

---

## Next Steps

1. **Review this document** - Ensure design meets needs
2. **Create ErrorHandlerService** - Implement basic version
3. **Test with one file** - Try with `DownloadManager.cs`
4. **Evaluate value** - Is it helping?
5. **Expand or stop** - Based on evaluation

---

**Last Updated**: 2025-12-07  
**Status**: Design Complete - Ready for Implementation Review

