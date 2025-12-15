# Error Handler Service - Implementation Plan

**Date**: 2025-12-07  
**Status**: ⚠️ **PARTIALLY IMPLEMENTED** - Service exists, needs full integration  
**Priority**: Medium (Low Risk, Medium Value)  
**Estimated Time**: 3-4 hours to complete integration

---

## Current State

### ✅ Already Complete

1. **ErrorHandlerService.cs** - ✅ **CREATED**
   - Service exists and is fully implemented
   - All methods working: `Try()`, `Try<T>()`, `HandleError()`, `LogInfo()`, `LogWarning()`
   - User-friendly message conversion implemented

2. **UniPlaySong.cs** - ✅ **INTEGRATED**
   - Service created in `InitializeServices()` (line 341)
   - Passed to services that need it

3. **Partial Integration** - ⚠️ **IN PROGRESS**
   - `DownloadManager.cs` - Uses ErrorHandlerService with fallback
   - `GameMusicFileService.cs` - Uses ErrorHandlerService with fallback
   - `GameMenuHandler.cs` - Uses ErrorHandlerService with fallback

### ⚠️ Still Needs Integration

**Files with try-catch blocks that could use ErrorHandlerService** (27 files found):

**High Priority** (Non-Critical, Easy Wins):
1. `DownloadDialogService.cs` - 12 try-catch blocks
2. `DownloadDialogViewModel.cs` - 7 try-catch blocks
3. `YouTubeDownloader.cs` - 6 try-catch blocks
4. `KHInsiderDownloader.cs` - 8 try-catch blocks
5. `YouTubeClient.cs` - 8 try-catch blocks
6. `UniPlaySongSettingsViewModel.cs` - 4 try-catch blocks

**Medium Priority** (Some Risk):
7. `PrimarySongManager.cs` - 3 try-catch blocks
8. `WindowMonitor.cs` - 1 try-catch block
9. `FileLogger.cs` - 2 try-catch blocks

**Low Priority** (Critical Services - Defer):
10. `MusicPlaybackService.cs` - 4 try-catch blocks ⚠️
11. `MusicPlaybackCoordinator.cs` - 0 try-catch blocks ✅
12. `MusicFader.cs` - 2 try-catch blocks ⚠️
13. `MusicPlayer.cs` - 9 try-catch blocks ⚠️
14. `SDL2MusicPlayer.cs` - 2 try-catch blocks ⚠️

**Skip** (Already handled or special cases):
- `UniPlaySong.cs` - Initialization errors (keep as-is)
- `SettingsService.cs` - Already has good error handling
- `MediaElementsMonitor.cs` - No try-catch blocks

---

## Implementation Strategy

### Phase 1: Complete Non-Critical Areas (Low Risk) ⭐ **START HERE**

**Goal**: Replace all try-catch blocks in downloaders and UI services

**Files to Update**:
1. `DownloadDialogService.cs` (12 blocks)
2. `DownloadDialogViewModel.cs` (7 blocks)
3. `YouTubeDownloader.cs` (6 blocks)
4. `KHInsiderDownloader.cs` (8 blocks)
5. `YouTubeClient.cs` (8 blocks)
6. `UniPlaySongSettingsViewModel.cs` (4 blocks)

**Approach**:
- Add `ErrorHandlerService` parameter to constructors
- Replace try-catch blocks with `_errorHandler.Try()` or `_errorHandler.Try<T>()`
- Remove fallback code (ErrorHandlerService is always available now)
- Test each file after updating

**Estimated Time**: 2-3 hours

---

### Phase 2: Utility Classes (Low Risk)

**Goal**: Update utility classes that don't affect core playback

**Files to Update**:
1. `PrimarySongManager.cs` (3 blocks)
2. `WindowMonitor.cs` (1 block)
3. `FileLogger.cs` (2 blocks) - May need special handling (it's the logger itself)

**Approach**:
- For `PrimarySongManager`: Add ErrorHandlerService parameter (optional)
- For `WindowMonitor`: Static class, may need to pass ErrorHandlerService
- For `FileLogger`: Keep existing error handling (it's the logger - can't log errors through itself)

**Estimated Time**: 1 hour

---

### Phase 3: Critical Services (Medium Risk) ⚠️ **DEFER**

**Goal**: Update playback services (only after Phase 1 & 2 proven stable)

**Files to Update** (if needed):
1. `MusicPlaybackService.cs` (4 blocks) ⚠️
2. `MusicFader.cs` (2 blocks) ⚠️
3. `MusicPlayer.cs` (9 blocks) ⚠️
4. `SDL2MusicPlayer.cs` (2 blocks) ⚠️

**Approach**:
- **VERY CAREFUL** - These affect core playback
- Test thoroughly after each change
- Keep existing error handling as fallback initially
- Only proceed if Phase 1 & 2 show clear benefits

**Estimated Time**: 2-3 hours (if proceeding)

---

## Detailed File Analysis

### DownloadDialogService.cs

**Current**: 12 try-catch blocks
**Pattern**: Mix of logging and user notifications
**Action**: Replace with `_errorHandler.Try()` and `_errorHandler.Try<T>()`
**User Messages**: Some operations should show user messages (download failures)

**Example Conversion**:
```csharp
// BEFORE:
try
{
    viewModel = new DownloadDialogViewModel(...);
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error creating DownloadDialogViewModel: {ex.Message}");
    _playniteApi.Dialogs.ShowErrorMessage(...);
    return null;
}

// AFTER:
return _errorHandler.Try(
    () => new DownloadDialogViewModel(...),
    defaultValue: null,
    context: "creating download dialog",
    showUserMessage: true
);
```

---

### DownloadDialogViewModel.cs

**Current**: 7 try-catch blocks
**Pattern**: Mostly logging, some user notifications
**Action**: Add ErrorHandlerService to constructor, replace try-catch blocks

**Challenge**: ViewModel may need ErrorHandlerService passed from parent
**Solution**: Pass from `DownloadDialogService` when creating ViewModel

---

### YouTubeDownloader.cs / KHInsiderDownloader.cs / YouTubeClient.cs

**Current**: 6-8 try-catch blocks each
**Pattern**: Mostly logging, return empty collections on error
**Action**: Replace with `_errorHandler.Try<T>()` returning empty collections

**Note**: These already receive ErrorHandlerService via DownloadManager
**Action**: Update to use ErrorHandlerService directly instead of fallback

---

### UniPlaySongSettingsViewModel.cs

**Current**: 4 try-catch blocks
**Pattern**: User notifications for file selection errors
**Action**: Add ErrorHandlerService to constructor, replace try-catch blocks

**Challenge**: ViewModel created by Playnite, not by our code
**Solution**: Get ErrorHandlerService from plugin via public property

---

## Implementation Checklist

### Phase 1: Non-Critical Areas

- [ ] **DownloadDialogService.cs**
  - [ ] Add `ErrorHandlerService` field
  - [ ] Update constructor to receive ErrorHandlerService
  - [ ] Replace 12 try-catch blocks with `_errorHandler.Try()`
  - [ ] Test download dialogs work correctly
  - [ ] Test error messages appear correctly

- [ ] **DownloadDialogViewModel.cs**
  - [ ] Add `ErrorHandlerService` field
  - [ ] Update constructor to receive ErrorHandlerService
  - [ ] Replace 7 try-catch blocks
  - [ ] Update DownloadDialogService to pass ErrorHandlerService
  - [ ] Test view model operations

- [ ] **YouTubeDownloader.cs**
  - [ ] Remove fallback code (ErrorHandlerService always available)
  - [ ] Replace 6 try-catch blocks with `_errorHandler.Try<T>()`
  - [ ] Test YouTube downloads

- [ ] **KHInsiderDownloader.cs**
  - [ ] Remove fallback code
  - [ ] Replace 8 try-catch blocks
  - [ ] Test KHInsider downloads

- [ ] **YouTubeClient.cs**
  - [ ] Remove fallback code
  - [ ] Replace 8 try-catch blocks
  - [ ] Test YouTube API calls

- [ ] **UniPlaySongSettingsViewModel.cs**
  - [ ] Add method to get ErrorHandlerService from plugin
  - [ ] Replace 4 try-catch blocks
  - [ ] Test settings file selection

### Phase 2: Utility Classes

- [ ] **PrimarySongManager.cs**
  - [ ] Add optional ErrorHandlerService parameter
  - [ ] Replace 3 try-catch blocks
  - [ ] Update callers to pass ErrorHandlerService

- [ ] **WindowMonitor.cs**
  - [ ] Evaluate if ErrorHandlerService needed (static class)
  - [ ] Update if beneficial

- [ ] **FileLogger.cs**
  - [ ] **SKIP** - It's the logger itself, can't use ErrorHandlerService

### Phase 3: Critical Services (Defer)

- [ ] **MusicPlaybackService.cs** ⚠️
  - [ ] Evaluate after Phase 1 & 2 complete
  - [ ] Only proceed if clear benefits shown

- [ ] **MusicFader.cs** ⚠️
  - [ ] Evaluate after Phase 1 & 2 complete

- [ ] **MusicPlayer.cs** ⚠️
  - [ ] Evaluate after Phase 1 & 2 complete

- [ ] **SDL2MusicPlayer.cs** ⚠️
  - [ ] Evaluate after Phase 1 & 2 complete

---

## Code Patterns to Replace

### Pattern 1: Basic Try-Catch with Logging

**Before**:
```csharp
try
{
    // operation
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error message: {ex.Message}");
    return defaultValue;
}
```

**After**:
```csharp
return _errorHandler.Try(
    () => { /* operation */ },
    defaultValue: defaultValue,
    context: "operation description"
);
```

---

### Pattern 2: Try-Catch with User Notification

**Before**:
```csharp
try
{
    // operation
}
catch (Exception ex)
{
    _logger.Error(ex, $"Error: {ex.Message}");
    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
}
```

**After**:
```csharp
_errorHandler.Try(
    () => { /* operation */ },
    context: "operation description",
    showUserMessage: true
);
```

---

### Pattern 3: Try-Catch with Dual Logging

**Before**:
```csharp
try
{
    // operation
}
catch (Exception ex)
{
    Logger.Error(ex, $"Error: {ex.Message}");
    _fileLogger?.Error($"Error: {ex.Message}", ex);
}
```

**After**:
```csharp
_errorHandler.Try(
    () => { /* operation */ },
    context: "operation description"
);
// ErrorHandlerService automatically handles both loggers
```

---

### Pattern 4: Nested Try-Catch

**Before**:
```csharp
try
{
    try
    {
        // inner operation
    }
    catch (Exception innerEx)
    {
        Logger.Warn(innerEx, "Inner error");
    }
    // outer operation
}
catch (Exception ex)
{
    Logger.Error(ex, "Outer error");
}
```

**After**:
```csharp
_errorHandler.Try(
    () =>
    {
        _errorHandler.Try(
            () => { /* inner operation */ },
            context: "inner operation"
        );
        // outer operation
    },
    context: "outer operation"
);
```

---

## Testing Strategy

### After Each File Update

1. **Compilation Test**
   - [ ] Code compiles without errors
   - [ ] No linter warnings introduced

2. **Functionality Test**
   - [ ] Feature still works correctly
   - [ ] Error scenarios handled properly
   - [ ] User messages appear when expected

3. **Logging Test**
   - [ ] Errors logged to Playnite logger
   - [ ] Errors logged to FileLogger
   - [ ] Context information included in logs

### Integration Test (After Phase 1)

1. **Download Operations**
   - [ ] Test successful downloads
   - [ ] Test download failures (network errors, file errors)
   - [ ] Verify error messages appear
   - [ ] Verify logs contain context

2. **UI Operations**
   - [ ] Test dialog operations
   - [ ] Test file selection
   - [ ] Test settings operations
   - [ ] Verify user-friendly error messages

3. **Regression Test**
   - [ ] All existing functionality works
   - [ ] No new errors introduced
   - [ ] Performance unchanged

---

## Benefits After Implementation

### Immediate Benefits (Phase 1)

1. **Consistency**: All download/UI errors handled uniformly
2. **User Experience**: Consistent, user-friendly error messages
3. **Maintainability**: Single place to update error handling logic
4. **Debugging**: Better context in error logs

### Long-Term Benefits (All Phases)

1. **Code Reduction**: Eliminate 85+ duplicate catch blocks
2. **Error Tracking**: Centralized error handling makes tracking easier
3. **Future Enhancements**: Easy to add error statistics, retry logic, etc.

---

## Risk Mitigation

### Low Risk Approach

1. **Incremental**: One file at a time
2. **Test After Each**: Don't batch changes
3. **Keep Fallbacks**: Initially keep old code as comments
4. **Skip Critical**: Don't touch playback services until proven

### Rollback Plan

- Each file change is independent
- Can revert individual files if issues arise
- Old try-catch code can be restored quickly
- Git commits per file for easy rollback

---

## Success Criteria

### Phase 1 Complete When:

- [ ] All downloader services use ErrorHandlerService
- [ ] All UI services use ErrorHandlerService
- [ ] No fallback code remains in updated files
- [ ] All tests pass
- [ ] Error messages are consistent and user-friendly

### Phase 2 Complete When:

- [ ] Utility classes updated (where applicable)
- [ ] No regressions introduced
- [ ] Code quality improved

### Phase 3 Decision Point:

- Evaluate if ErrorHandlerService provides value
- Measure code reduction and consistency improvement
- Decide if critical services should be updated
- **Only proceed if clear benefits shown**

---

## Next Steps

1. **Review this plan** - Ensure approach is acceptable
2. **Start Phase 1** - Begin with `DownloadDialogService.cs`
3. **Test incrementally** - One file at a time
4. **Evaluate after Phase 1** - Is it providing value?
5. **Continue or stop** - Based on evaluation

---

## Notes

- **ErrorHandlerService is already created** - Just needs full integration
- **Partial integration exists** - Some services already use it
- **Low risk** - Wrapper pattern, incremental changes
- **Can stop at any phase** - No commitment to complete all phases

---

**Last Updated**: 2025-12-07  
**Status**: Ready for Implementation - Phase 1  
**Estimated Time**: 3-4 hours for Phase 1 & 2

