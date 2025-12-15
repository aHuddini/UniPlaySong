# UniPlaySong Development Notes

## Critical Development Principles

### 1. PlayniteSound is the Reference Implementation

**IMPORTANT**: The PlayniteSound project (`src/PlayniteSound/`) is the **SUPERIOR** source code and should be the **PRIMARY REFERENCE** for all development decisions.

**Before implementing any feature, logic, or solution:**
1. **ALWAYS** check how PlayniteSound handles it first
2. If PlayniteSound doesn't add unnecessary logic, **NEITHER SHOULD WE**
3. If PlayniteSound uses a simple approach, use the same simple approach
4. If PlayniteSound doesn't check for something, don't add that check unless absolutely necessary

**Key Principle**: Keep the codebase simple and focused. PlayniteSound has been battle-tested and refined over years. Follow its patterns.

### 2. Code Review Approach

**For Future Agents**: When debugging or implementing features, review the codebase like a **senior software developer and engineer** with great success in fixing bugs:

1. **Start with PlayniteSound**: Understand how it solves the problem
2. **Compare implementations**: Look at what we're doing vs. what PlayniteSound does
3. **Identify differences**: Any deviation from PlayniteSound should have a clear, justified reason
4. **Simplify first**: Before adding complexity, see if PlayniteSound's simpler approach works
5. **Test incrementally**: Make small changes and test, don't rewrite large sections at once

### 3. Known Issues and Failures

#### Login Screen vs. Library View Music Selection Issue

**Problem**: Music plays during the login screen transition in fullscreen mode (especially with ANIKI REMAKE theme), when it should only play after the user reaches the library/grid view.

**Attempted Solutions (All Failed)**:

1. **Time-based delays**: Added delays after mode switch - didn't work reliably
2. **View name detection**: Tried to detect login screens by view name - unreliable because `GetMainModel()` returns "unknown" in fullscreen initially
3. **Skip first selection setting**: Added setting to skip first selection - confusing and doesn't solve root cause
4. **ActiveView property change monitoring**: Subscribed to `OnMainModelPropertyChanged` - view changes happen but timing is unpredictable
5. **Library view detection**: Check for "Grid", "Details", "List" views - but `GetMainModel()` fails during login screen, returning "unknown"

**Current State**:
- Code checks for library views (Grid/Details/List in fullscreen, Library in desktop)
- If view is "unknown" or not a library view, music is skipped
- When view changes to library view, attempts to play previously skipped game
- **Still not working reliably** - music sometimes plays during login screen

**Root Cause Analysis Needed**:
- Why does `GetMainModel()` return null/"unknown" during login screen?
- Is `OnGameSelected` being called before the view is ready?
- How does PlayniteSound actually prevent this issue?
- Is there a different event or property we should be monitoring?

**Next Steps for Future Agents**:
1. **Thoroughly review PlayniteSound's `OnGameSelected` implementation** - line by line
2. **Review PlayniteSound's `ShouldPlayAudio` method** - understand all conditions
3. **Check if PlayniteSound has any special handling for fullscreen mode transitions**
4. **Review PlayniteSound's `OnMainModelChanged` implementation** - see how it handles view changes
5. **Check PlayniteSound's `_firstSelect` flag usage** - understand when it's set/reset
6. **Look for any timing mechanisms** - delays, waits, or synchronization in PlayniteSound
7. **Review ANIKI REMAKE theme source code** - understand when views actually change
8. **Consider if we need to wait for a different event** - maybe not `OnGameSelected` but something else

**Key Questions to Answer**:
- Does PlayniteSound actually prevent music during login screen, or does it just happen to work due to timing?
- Is there a Playnite API event that fires when the library view is ready?
- Should we be checking something other than `ActiveView`?
- Is the issue that `OnGameSelected` fires too early, or that we're not detecting the view correctly?

### 4. Architecture Principles

**Separation of Concerns**:
- Menu items: `Menus/GameMenuHandler.cs` and `Menus/MainMenuHandler.cs`
- Music playback: `Services/MusicPlaybackService.cs`
- File management: `Services/GameMusicFileService.cs`
- Download management: `Downloaders/DownloadManager.cs`
- UI dialogs: `Services/DownloadDialogService.cs`

**Keep It Simple**:
- Don't add features PlayniteSound doesn't have unless explicitly requested
- Don't add settings that aren't necessary
- Don't add complex state management if simple flags work

### 5. Settings Philosophy

**Current Settings**:
- `EnableMusic`: Master switch - if off, no music plays
- `MusicVolume`: Volume control (0-100)
- `YtDlpPath`: Path to yt-dlp executable
- `FFmpegPath`: Path to ffmpeg executable

**Removed Settings** (were causing confusion):
- `AutoPlayOnSelection`: Redundant - if music is enabled, it should play
- `SkipFirstSelectionAfterModeSwitch`: Confusing and didn't solve the problem
- `WaitForViewReadyAfterModeSwitch`: Complex timing logic that didn't work
- `ViewReadyDelayMs`: Time-based delays that were unreliable

**Principle**: One setting = one clear purpose. If a setting is confusing or redundant, remove it.

### 6. Logging and Debugging

**Log Files**:
- Extension log: `UniPlaySong.log` in extension installation directory
- Playnite log: `playnite.log` in Playnite AppData directory
- Extensions log: `extensions.log` in Playnite AppData directory

**When Debugging**:
1. Check all three log files
2. Compare with PlayniteSound's logs (if available)
3. Look for patterns in timing - when events fire relative to each other
4. Check view names - what are the actual view names during transitions?

### 7. Testing Checklist

Before considering a feature complete:
1. Test in desktop mode
2. Test in fullscreen mode
3. Test with ANIKI REMAKE theme (has login screen)
4. Test with default theme
5. Test mode switching (desktop ↔ fullscreen)
6. Test with music enabled/disabled
7. Test with games that have music and games without music
8. Test primary song behavior
9. Test download functionality
10. Check logs for errors or unexpected behavior

### 8. Code Quality Standards

**Follow PlayniteSound Patterns**:
- Use same naming conventions
- Use same error handling patterns
- Use same logging patterns
- Use same service organization

**Don't Reinvent the Wheel**:
- If PlayniteSound has a solution, use it
- If PlayniteSound doesn't do something, question if we need it
- If PlayniteSound uses a simple approach, don't overcomplicate it

## Recent Changes (2025-11-29)

### Removed Redundant Settings
- Removed "Auto-play music when selecting games" checkbox
- Simplified to single "Enable Music" master switch
- Updated all logic to only check `EnableMusic`

### Library View Detection
- Added check for library views before playing music
- Fullscreen: Checks for "Grid", "Details", "List"
- Desktop: Checks for "Library"
- Skips music if view is "unknown" or not a library view

### View Change Monitoring
- Enhanced `OnMainModelPropertyChanged` to detect when view changes to library view
- Attempts to play previously skipped game when library view is reached
- **Still not working reliably** - needs further investigation

### Removed Fullscreen Settings from UI
- Removed confusing fullscreen-specific settings
- Simplified settings interface
- Focus on core functionality

## Future Work

### High Priority
1. **Fix login screen music issue** - This is the #1 priority
2. Review PlayniteSound's implementation thoroughly
3. Understand why `GetMainModel()` fails during login screen
4. Find reliable way to detect when library view is ready

### Medium Priority
1. Improve error handling
2. Add more comprehensive logging
3. Test with more themes
4. Optimize performance

### Low Priority
1. Add more download sources
2. Improve UI/UX
3. Add more customization options (only if PlayniteSound has them)

