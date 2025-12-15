# Implementation Plan: Fix Login Screen Music Issue

**Goal**: Match PlayniteSound's simple flag-based pattern to permanently fix the login screen music issue.

**Status**: Ready for implementation  
**Estimated Time**: 30-45 minutes  
**Risk Level**: Low (removing complex code, adding simple code)

---

## Overview

This plan will:
1. Remove all complex view detection logic
2. Simplify `OnGameSelected` to match PlayniteSound's pattern
3. Add a central gatekeeper method (`ShouldPlayMusic`)
4. Clean up settings (remove unused settings)
5. Remove unnecessary tracking variables

---

## Step 1: Remove Complex Tracking Variables

### File: `UniPlaySong.cs`

**Remove**:
- `_lastSkippedGameId` field (line ~62)
- All references to `_lastSkippedGameId`

**Action**: Delete the field declaration and all code that uses it.

---

## Step 2: Simplify OnGameSelected Method

### File: `UniPlaySong.cs`

**Current**: Complex method with view detection, skipped game tracking, etc. (lines 184-299)

**Replace with**:

```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    try
    {
        var games = args?.NewValue;
        var game = games?.FirstOrDefault();
        var gameName = game?.Name ?? "No game";
        
        logger.Info($"OnGameSelected called - Game: {gameName}");
        _fileLogger?.Info($"OnGameSelected called - Game: {gameName}, FirstSelect: {_firstSelect}");
        
        // If no game selected, stop music
        if (games == null || games.Count == 0 || game == null)
        {
            _playbackService?.Stop();
            _firstSelect = false;
            return;
        }
        
        // Load settings
        var settings = LoadPluginSettings<UniPlaySongSettings>();
        
        // If music is disabled, stop music
        if (settings == null || !settings.EnableMusic)
        {
            _playbackService?.Stop();
            _firstSelect = false;
            return;
        }
        
        // Simple check: Skip first selection if setting is enabled (matches PlayniteSound)
        if (!(_firstSelect && settings.SkipFirstSelectionAfterModeSwitch))
        {
            logger.Info($"Playing music for selected game: {gameName}");
            _fileLogger?.Info($"Playing music for selected game: {gameName}");
            _playbackService?.PlayGameMusic(game, settings);
        }
        else
        {
            logger.Info($"Skipping first selection music for game: {gameName}");
            _fileLogger?.Info($"Skipping first selection music for game: {gameName}");
        }
        
        // CRITICAL: Clear flag immediately after check (like PlayniteSound)
        _firstSelect = false;
    }
    catch (Exception ex)
    {
        logger.Error(ex, $"Exception in OnGameSelected: {ex.Message}");
        _fileLogger?.Error($"Exception in OnGameSelected: {ex.Message}", ex);
    }
}
```

**Key Changes**:
- ✅ Removed all view detection logic
- ✅ Removed `_lastSkippedGameId` tracking
- ✅ Removed `isSkippedGameReselected` logic
- ✅ Removed `isLibraryView` checks
- ✅ Simple flag check matching PlayniteSound
- ✅ Flag cleared immediately after check

---

## Step 3: Remove OnMainModelPropertyChanged Handler

### File: `UniPlaySong.cs`

**Remove**:
- `OnMainModelPropertyChanged` method (lines ~338-420)
- Subscription to property changes in constructor (lines ~137-155)

**Action**: 
1. Delete the entire `OnMainModelPropertyChanged` method
2. Remove the subscription code in constructor:
   ```csharp
   // REMOVE THIS ENTIRE BLOCK:
   try
   {
       var mainModel = GetMainModel();
       if (mainModel != null)
       {
           var notifyPropertyChanged = mainModel as System.ComponentModel.INotifyPropertyChanged;
           if (notifyPropertyChanged != null)
           {
               notifyPropertyChanged.PropertyChanged += OnMainModelPropertyChanged;
               _fileLogger?.Info("Subscribed to MainModel property changes");
           }
       }
   }
   catch (Exception ex)
   {
       logger.Warn($"Could not subscribe to MainModel property changes: {ex.Message}");
       _fileLogger?.Error($"Could not subscribe to MainModel property changes: {ex.Message}", ex);
   }
   ```

---

## Step 4: Remove Helper Methods (No Longer Needed)

### File: `UniPlaySong.cs`

**Remove**:
- `GetActiveViewName()` method (lines ~422-435)
- `IsOnLoginScreen()` method (lines ~447-513)
- `GetActiveViewFullName()` method (lines ~515-528)

**Action**: Delete all three methods entirely.

**Note**: `GetMainModel()` is still needed for `SuppressNativeFullscreenMusic()`, so keep that method.

---

## Step 5: Add Central Gatekeeper Method (Optional but Recommended)

### File: `UniPlaySong.cs`

**Add** this method after `OnGameSelected`:

```csharp
/// <summary>
/// Central gatekeeper method that checks if music should play
/// Matches PlayniteSound's ShouldPlayAudio pattern
/// </summary>
private bool ShouldPlayMusic(UniPlaySongSettings settings)
{
    if (settings == null || !settings.EnableMusic)
        return false;
    
    if (_playbackService == null)
        return false;
    
    // Central check: Skip first selection if setting is enabled
    var skipFirstSelectMusic = _firstSelect && settings.SkipFirstSelectionAfterModeSwitch;
    
    return !skipFirstSelectMusic;
}
```

**Then update `OnGameSelected`** to use it (optional, but recommended for consistency):

```csharp
// Replace the check with:
if (ShouldPlayMusic(settings))
{
    logger.Info($"Playing music for selected game: {gameName}");
    _fileLogger?.Info($"Playing music for selected game: {gameName}");
    _playbackService?.PlayGameMusic(game, settings);
}
```

**Why**: This matches PlayniteSound's pattern and provides a single point of control for future enhancements.

---

## Step 6: Clean Up Settings (Remove Unused Settings)

### File: `UniPlaySongSettings.cs`

**Remove** these properties:
- `AutoPlayOnSelection` (lines ~54-62) - Redundant with `EnableMusic`
- `WaitForViewReadyAfterModeSwitch` (lines ~21-29) - Not working
- `ViewReadyDelayMs` (lines ~34-42) - Not working

**Keep**:
- `EnableMusic` - Master switch
- `SkipFirstSelectionAfterModeSwitch` - Core functionality (consider renaming to `SkipFirstSelectMusic` for consistency)
- `MusicVolume` - Volume control
- `YtDlpPath` - Download tool path
- `FFmpegPath` - Download tool path

**Action**: Delete the three properties and their backing fields.

---

## Step 7: Update Settings View Model

### File: `UniPlaySongSettingsViewModel.cs`

**Remove** references to deleted settings in `BrowseForYtDlpFile` and `BrowseForFFmpegFile` commands:

**Current**:
```csharp
var newSettings = new UniPlaySongSettings
{
    EnableMusic = Settings.EnableMusic,
    AutoPlayOnSelection = Settings.AutoPlayOnSelection,  // REMOVE
    MusicVolume = Settings.MusicVolume,
    YtDlpPath = filePath,
    FFmpegPath = Settings.FFmpegPath
};
```

**Replace with**:
```csharp
var newSettings = new UniPlaySongSettings
{
    EnableMusic = Settings.EnableMusic,
    SkipFirstSelectionAfterModeSwitch = Settings.SkipFirstSelectionAfterModeSwitch,
    MusicVolume = Settings.MusicVolume,
    YtDlpPath = filePath,
    FFmpegPath = Settings.FFmpegPath
};
```

**Do the same for `BrowseForFFmpegFile` command**.

---

## Step 8: Update Settings UI (XAML)

### File: `UniPlaySongSettingsView.xaml`

**Remove** UI elements for deleted settings:
- `AutoPlayOnSelection` checkbox
- `WaitForViewReadyAfterModeSwitch` checkbox
- `ViewReadyDelayMs` textbox/slider

**Action**: Delete the corresponding XAML elements.

**Keep**:
- `EnableMusic` checkbox
- `SkipFirstSelectionAfterModeSwitch` checkbox
- `MusicVolume` slider
- `YtDlpPath` textbox/browse button
- `FFmpegPath` textbox/browse button

---

## Step 9: Optional - Rename Setting for Consistency

### Consider renaming for consistency with PlayniteSound:

**Current**: `SkipFirstSelectionAfterModeSwitch`  
**Proposed**: `SkipFirstSelectMusic`

**Files to update**:
1. `UniPlaySongSettings.cs` - Property name
2. `UniPlaySongSettingsViewModel.cs` - All references
3. `UniPlaySong.cs` - All references
4. `UniPlaySongSettingsView.xaml` - Binding path

**Note**: This is optional but recommended for consistency with PlayniteSound's naming.

---

## Step 10: Testing Checklist

After implementation, test the following scenarios:

### ✅ Test 1: Desktop Mode
- [ ] Start Playnite in desktop mode
- [ ] Select a game → Music should play immediately
- [ ] Select another game → Music should switch
- [ ] Disable music in settings → Music should stop

### ✅ Test 2: Fullscreen Mode (No Login Screen)
- [ ] Switch to fullscreen mode (theme without login screen)
- [ ] Select a game → Music should play immediately
- [ ] Select another game → Music should switch

### ✅ Test 3: Fullscreen Mode (With Login Screen - ANIKI REMAKE)
- [ ] Switch to fullscreen mode with ANIKI REMAKE theme
- [ ] **CRITICAL**: Music should NOT play during login screen
- [ ] Pass login screen
- [ ] Select a game → Music should play immediately
- [ ] Select another game → Music should switch

### ✅ Test 4: Mode Switching
- [ ] Start in desktop mode, select game → Music plays
- [ ] Switch to fullscreen → Music should continue (if same game selected)
- [ ] Switch back to desktop → Music should continue

### ✅ Test 5: Settings
- [ ] Enable/disable `SkipFirstSelectionAfterModeSwitch` → Should work correctly
- [ ] Change volume → Should affect playback
- [ ] Browse for yt-dlp/ffmpeg paths → Should save correctly

### ✅ Test 6: Edge Cases
- [ ] Select game with no music → Should not error
- [ ] Select multiple games → Should handle gracefully
- [ ] Rapidly switch between games → Should not crash

---

## Implementation Order

1. **Step 1**: Remove `_lastSkippedGameId` variable
2. **Step 2**: Simplify `OnGameSelected` method
3. **Step 3**: Remove `OnMainModelPropertyChanged` handler
4. **Step 4**: Remove helper methods
5. **Step 5**: Add `ShouldPlayMusic` method (optional but recommended)
6. **Step 6**: Clean up settings class
7. **Step 7**: Update settings view model
8. **Step 8**: Update settings UI
9. **Step 9**: Optional rename
10. **Step 10**: Test thoroughly

---

## Code Comparison: Before vs After

### Before (Complex)
```csharp
// ~115 lines of complex logic
var activeViewName = GetActiveViewName();
var isLibraryView = false;
if (isFullscreen) { /* complex checks */ }
var isSkippedGameReselected = /* tracking logic */;
var shouldPlayMusic = !skipFirstSelect && isLibraryView;
if (!isLibraryView) { _lastSkippedGameId = gameId; }
// ... more complex logic ...
```

### After (Simple)
```csharp
// ~30 lines of simple logic
if (!(_firstSelect && settings.SkipFirstSelectionAfterModeSwitch))
{
    _playbackService?.PlayGameMusic(game, settings);
}
_firstSelect = false;
```

---

## Expected Results

### ✅ What Should Work
- Music plays immediately after passing login screen
- Music doesn't play during login screen transition
- Simple, maintainable code
- Consistent with PlayniteSound's proven pattern

### ❌ What We're Removing
- Complex view detection (unreliable)
- Skipped game tracking (unnecessary)
- Property change monitoring (unreliable)
- Multiple helper methods (not needed)

---

## Rollback Plan

If issues occur, rollback steps:
1. Revert `UniPlaySong.cs` to previous version
2. Revert `UniPlaySongSettings.cs` to previous version
3. Revert `UniPlaySongSettingsViewModel.cs` to previous version
4. Revert `UniPlaySongSettingsView.xaml` to previous version

**Note**: Keep a backup of current code before starting.

---

## Success Criteria

The fix is successful when:
- ✅ Music does NOT play during login screen transition
- ✅ Music DOES play immediately after passing login screen
- ✅ Works reliably with ANIKI REMAKE theme
- ✅ Works reliably with default theme
- ✅ Works reliably when switching between desktop and fullscreen modes
- ✅ Code is simpler and easier to maintain
- ✅ Matches PlayniteSound's proven pattern

---

## Post-Implementation

After successful implementation:
1. Update `CRITICAL_ISSUES.md` - Mark login screen issue as resolved
2. Update `IMPLEMENTATION_GUIDE.md` - Document the fix
3. Update `DEVELOPMENT_NOTES.md` - Note the simplified approach
4. Test with multiple themes to ensure compatibility

---

**Last Updated**: 2025-11-29  
**Status**: Ready for implementation  
**Priority**: High (fixes critical user experience issue)

