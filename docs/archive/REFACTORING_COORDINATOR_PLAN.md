# Music Playback Coordinator Refactoring Plan

**Date**: 2025-12-01  
**Status**: Design Phase  
**Goal**: Centralize music playback logic, fix skip issues, improve maintainability

---

## Current Problems

### 1. Scattered Logic
- **State Management**: `_firstSelect`, `_loginSkipActive` scattered across `UniPlaySong.cs`
- **Multiple Entry Points**: `PlayMusicForGame()`, `PlayMusicBasedOnSelected()`, `ResumeMusic()`, `OnMainModelChanged()`, `OnLoginDismissKeyPress()`
- **Complex Dependencies**: Skip logic depends on timing of `_firstSelect` clearing
- **Hard to Test**: Logic is embedded in event handlers

### 2. Skip Logic Issues
- `_firstSelect` clearing timing is fragile
- Multiple code paths can bypass skip checks
- Controller input can override skip
- State gets out of sync

### 3. Code Complexity
- `OnGameSelected()`: 50+ lines with nested conditions
- `ShouldPlayMusic()`: Multiple responsibilities
- Hard to reason about execution flow
- Difficult to debug

---

## Proposed Solution: MusicPlaybackCoordinator

### Architecture

```
UniPlaySong.cs (Main Plugin)
    ↓
MusicPlaybackCoordinator (New Service)
    ├── State Management (_firstSelect, _loginSkipActive)
    ├── ShouldPlayDecision() - Single source of truth
    ├── HandleGameSelected() - Coordinates skip + playback
    ├── HandleLoginDismiss() - Handles login skip
    └── HandleViewChange() - Handles view transitions
    ↓
MusicPlaybackService (Existing)
    └── PlayGameMusic() - Actual playback
```

### Benefits

1. **Single Source of Truth**: All "should play" logic in one place
2. **Clear State Management**: State tracked in coordinator
3. **Easier Testing**: Can test coordinator in isolation
4. **Simpler Main Class**: `UniPlaySong.cs` delegates to coordinator
5. **Preserves Functionality**: All existing features maintained

---

## Coordinator Design

### Interface

```csharp
public interface IMusicPlaybackCoordinator
{
    // State queries
    bool ShouldPlayMusic(Game game);
    bool IsFirstSelect();
    bool IsLoginSkipActive();
    
    // Event handlers
    void HandleGameSelected(Game game, bool isFullscreen);
    void HandleLoginDismiss();
    void HandleViewChange();
    void HandleVideoStateChange(bool isPlaying);
    
    // State management
    void ResetFirstSelect();
    void SetLoginSkipActive(bool active);
}
```

### Implementation

```csharp
public class MusicPlaybackCoordinator : IMusicPlaybackCoordinator
{
    private readonly IMusicPlaybackService _playbackService;
    private readonly UniPlaySongSettings _settings;
    private readonly ILogger _logger;
    private readonly FileLogger _fileLogger;
    
    // State - centralized here
    private bool _firstSelect = true;
    private bool _loginSkipActive = false;
    private Game _currentGame;
    
    // Dependencies
    private readonly Func<bool> _isFullscreen;
    private readonly Func<bool> _isDesktop;
    private readonly Func<Game> _getSelectedGame;
    
    public MusicPlaybackCoordinator(
        IMusicPlaybackService playbackService,
        UniPlaySongSettings settings,
        ILogger logger,
        FileLogger fileLogger,
        Func<bool> isFullscreen,
        Func<bool> isDesktop,
        Func<Game> getSelectedGame)
    {
        _playbackService = playbackService;
        _settings = settings;
        _logger = logger;
        _fileLogger = fileLogger;
        _isFullscreen = isFullscreen;
        _isDesktop = isDesktop;
        _getSelectedGame = getSelectedGame;
    }
    
    /// <summary>
    /// Central gatekeeper - ALL music playback decisions go through here
    /// </summary>
    public bool ShouldPlayMusic(Game game)
    {
        // Basic checks
        if (_settings == null || !_settings.EnableMusic) return false;
        if (_playbackService == null) return false;
        if (_settings.MusicVolume <= 0) return false;
        if (_settings.VideoIsPlaying) return false;
        if (game == null) return false;
        
        // Login skip check
        if (_loginSkipActive)
        {
            _fileLogger?.Info("ShouldPlayMusic: Returning false - login skip active");
            return false;
        }
        
        // First select skip check - CENTRAL GATEKEEPER
        if (_firstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
        {
            _fileLogger?.Info("ShouldPlayMusic: Returning false - first select skip enabled");
            return false;
        }
        
        // Mode-based checks
        var state = _settings.MusicState;
        if (_isFullscreen() && state != AudioState.Fullscreen && state != AudioState.Always) return false;
        if (_isDesktop() && state != AudioState.Desktop && state != AudioState.Always) return false;
        
        return true;
    }
    
    /// <summary>
    /// Handles game selection - coordinates skip logic and playback
    /// </summary>
    public void HandleGameSelected(Game game, bool isFullscreen)
    {
        if (game == null || _settings?.EnableMusic != true)
        {
            // Stop when no game selected (MusicPlaybackService handles fade-out internally)
            _playbackService?.Stop();
            _firstSelect = false;
            _currentGame = null;
            return;
        }
        
        var wasFirstSelect = _firstSelect;
        _currentGame = game;
        
        // SkipFirstSelectionAfterModeSwitch - takes precedence
        if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
        {
            _fileLogger?.Info($"HandleGameSelected: Skipping first selection (Game: {game.Name})");
            _firstSelect = false; // Clear after handling skip
            return;
        }
        
        // ThemeCompatibleSilentSkip - only if SkipFirstSelectionAfterModeSwitch is disabled
        if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && isFullscreen && !_settings.SkipFirstSelectionAfterModeSwitch)
        {
            _fileLogger?.Info($"HandleGameSelected: Login skip active (Game: {game.Name})");
            _loginSkipActive = true;
            _firstSelect = false; // Clear after handling skip
            return;
        }
        
        // Clear login skip if active
        if (_loginSkipActive)
        {
            _loginSkipActive = false;
        }
        
        // Attempt to play music - ShouldPlayMusic() will check all conditions
        if (ShouldPlayMusic(game))
        {
            _fileLogger?.Info($"HandleGameSelected: Playing music for {game.Name}");
            _playbackService?.PlayGameMusic(game, _settings, false);
        }
        else
        {
            _fileLogger?.Info($"HandleGameSelected: Not playing - ShouldPlayMusic returned false");
        }
        
        // Clear _firstSelect after processing (PNS pattern)
        _firstSelect = false;
    }
    
    /// <summary>
    /// Handles login screen dismissal (controller/keyboard input)
    /// </summary>
    public void HandleLoginDismiss()
    {
        if (!_loginSkipActive) return;
        
        _fileLogger?.Info("HandleLoginDismiss: Clearing login skip");
        _loginSkipActive = false;
        
        // Short delay then attempt to play
        var timer = new System.Timers.Timer(150) { AutoReset = false };
        timer.Elapsed += (s, args) =>
        {
            timer.Dispose();
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var game = _getSelectedGame();
                if (game != null && ShouldPlayMusic(game))
                {
                    _playbackService?.PlayGameMusic(game, _settings, false);
                }
            });
        };
        timer.Start();
    }
    
    /// <summary>
    /// Handles view changes (user left login screen)
    /// </summary>
    public void HandleViewChange()
    {
        if (!_loginSkipActive || !_isFullscreen()) return;
        
        var game = _getSelectedGame();
        if (game != null && _settings?.EnableMusic == true)
        {
            _fileLogger?.Info("HandleViewChange: Clearing login skip and starting music");
            _loginSkipActive = false;
            
            if (ShouldPlayMusic(game))
            {
                _playbackService?.PlayGameMusic(game, _settings, false);
            }
        }
    }
    
    /// <summary>
    /// Handles video state changes (pause/resume)
    /// </summary>
    public void HandleVideoStateChange(bool isPlaying)
    {
        if (isPlaying)
        {
            if (_playbackService?.IsLoaded == true)
            {
                _playbackService.Pause();
            }
        }
        else
        {
            // Resume only if we should play
            var game = _getSelectedGame();
            if (game != null && ShouldPlayMusic(game))
            {
                if (_playbackService?.IsLoaded == true)
                {
                    _playbackService.Resume();
                }
                else
                {
                    // Start playing if not loaded
                    _playbackService?.PlayGameMusic(game, _settings, false);
                }
            }
        }
    }
    
    // State queries
    public bool IsFirstSelect() => _firstSelect;
    public bool IsLoginSkipActive() => _loginSkipActive;
    
    // State management
    public void ResetFirstSelect() => _firstSelect = false;
    public void SetLoginSkipActive(bool active) => _loginSkipActive = active;
}
```

---

## Migration Plan

### Phase 1: Create Coordinator (No Breaking Changes)

1. **Create `IMusicPlaybackCoordinator` interface**
   - File: `UniPSong/Services/IMusicPlaybackCoordinator.cs`

2. **Create `MusicPlaybackCoordinator` implementation**
   - File: `UniPSong/Services/MusicPlaybackCoordinator.cs`
   - Copy all logic from `UniPlaySong.cs`
   - Centralize state management

3. **Initialize coordinator in `UniPlaySong.cs`**
   - Create coordinator instance
   - Keep existing methods (for now)

### Phase 2: Migrate Logic (Gradual)

4. **Migrate `OnGameSelected()`**
   - Replace with: `_coordinator.HandleGameSelected(game, IsFullscreen)`
   - Remove skip logic from `UniPlaySong.cs`

5. **Migrate `ShouldPlayMusic()`**
   - Replace calls with: `_coordinator.ShouldPlayMusic(game)`
   - Remove method from `UniPlaySong.cs`

6. **Migrate `ResumeMusic()` / `PauseMusic()`**
   - Replace with: `_coordinator.HandleVideoStateChange()`
   - Remove methods from `UniPlaySong.cs`

7. **Migrate event handlers**
   - `OnLoginDismissKeyPress` → `_coordinator.HandleLoginDismiss()`
   - `OnMainModelChanged` → `_coordinator.HandleViewChange()`

### Phase 3: Cleanup

8. **Remove old methods**
   - Remove `PlayMusicForGame()`, `PlayMusicBasedOnSelected()`
   - Remove state variables: `_firstSelect`, `_loginSkipActive`

9. **Simplify `UniPlaySong.cs`**
   - Should be mostly event delegation
   - Much simpler and easier to understand

---

## Key Design Decisions

### 1. State Management
**Decision**: Coordinator owns all state (`_firstSelect`, `_loginSkipActive`)  
**Rationale**: Single source of truth, easier to reason about

### 2. ShouldPlayMusic() Location
**Decision**: In coordinator, not in main class  
**Rationale**: All conditions in one place, easier to test

### 3. First Select Clearing
**Decision**: Clear at end of `HandleGameSelected()` (PNS pattern)  
**Rationale**: Matches proven PNS behavior, predictable timing

### 4. Dependencies
**Decision**: Use Func delegates for `IsFullscreen`, `GetSelectedGame`  
**Rationale**: Testable, no tight coupling to `UniPlaySong`

### 5. Backward Compatibility
**Decision**: Preserve all existing functionality  
**Rationale**: No breaking changes, safe migration

---

## Benefits

### 1. Fixes Skip Issue
- **Central Gatekeeper**: `ShouldPlayMusic()` checks `_firstSelect` before it's cleared
- **Predictable Timing**: State cleared at end of handler, not scattered
- **All Paths Protected**: Every music trigger goes through coordinator

### 2. Improves Maintainability
- **Single Responsibility**: Coordinator handles all playback decisions
- **Clear Separation**: Main class delegates, coordinator coordinates
- **Easier Debugging**: All logic in one place

### 3. Better Testability
- **Isolated Testing**: Can test coordinator without Playnite
- **Mock Dependencies**: Easy to mock `IMusicPlaybackService`
- **State Verification**: Can check state after operations

### 4. Preserves Functionality
- **All Features**: Skip, login skip, video pause, etc.
- **Same Behavior**: Matches current functionality
- **No Breaking Changes**: Gradual migration

---

## Implementation Steps

### Step 1: Create Interface and Coordinator (Safe)
1. Create `IMusicPlaybackCoordinator.cs`
2. Create `MusicPlaybackCoordinator.cs` with all logic
3. Initialize in `UniPlaySong.cs` constructor
4. **Test**: Verify coordinator works (no changes to main class yet)

### Step 2: Migrate OnGameSelected (Low Risk)
1. Replace `OnGameSelected()` body with coordinator call
2. Keep old method commented for reference
3. **Test**: Verify skip still works

### Step 3: Migrate ShouldPlayMusic (Low Risk)
1. Replace all `ShouldPlayMusic()` calls with coordinator
2. Remove old method
3. **Test**: Verify all paths still work

### Step 4: Migrate Event Handlers (Low Risk)
1. Migrate `OnLoginDismissKeyPress`
2. Migrate `OnMainModelChanged`
3. Migrate `OnSettingsChanged`
4. **Test**: Verify all events work

### Step 5: Cleanup (Safe)
1. Remove old methods
2. Remove state variables
3. Simplify `UniPlaySong.cs`
4. **Test**: Full regression test

---

## Risk Assessment

### Low Risk
- Creating coordinator (doesn't change existing code)
- Migrating one method at a time
- Keeping old code commented during migration

### Medium Risk
- State management changes
- Timing of `_firstSelect` clearing

### Mitigation
- Gradual migration (one method at a time)
- Keep old code for reference
- Extensive testing after each step
- Can revert easily if issues

---

## Testing Strategy

### Unit Tests (Future)
- Test coordinator in isolation
- Mock dependencies
- Test all skip scenarios

### Manual Testing (Now)
1. **SkipFirstSelectionAfterModeSwitch**
   - Enable setting
   - Switch to fullscreen
   - First select → no music
   - Second select → music plays

2. **ThemeCompatibleSilentSkip**
   - Enable setting
   - Switch to fullscreen
   - First select → wait for input
   - Press Enter → music plays

3. **Video Pause/Resume**
   - Play music
   - Start video → music pauses
   - Stop video → music resumes

4. **All Entry Points**
   - Game selection
   - View changes
   - Controller input
   - Settings changes

---

## Code Comparison

### Before (Complex)
```csharp
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
    
    if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
    {
        _fileLogger?.Info($"Skipping first selection music (Game: {game.Name})");
        _firstSelect = false;
        return;
    }
    
    if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && IsFullscreen && !_settings.SkipFirstSelectionAfterModeSwitch)
    {
        _loginSkipActive = true;
        _fileLogger?.Info($"Login skip active - waiting for user input (Game: {game.Name})");
        _firstSelect = false;
        return;
    }
    
    if (_loginSkipActive)
    {
        _loginSkipActive = false;
    }
    
    _fileLogger?.Info($"OnGameSelected: Before PlayMusicForGame, _firstSelect={_firstSelect}");
    PlayMusicForGame(game);
    
    _fileLogger?.Info($"OnGameSelected: Clearing _firstSelect from {_firstSelect} to false");
    _firstSelect = false;
}
```

### After (Simple)
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var game = args?.NewValue?.FirstOrDefault();
    _coordinator.HandleGameSelected(game, IsFullscreen);
}
```

**Benefits**:
- 50+ lines → 3 lines
- All logic in coordinator
- Easy to understand
- Easy to test

---

## Next Steps

1. **Review this plan** - Ensure it meets requirements
2. **Create coordinator** - Implement interface and class
3. **Migrate gradually** - One method at a time
4. **Test thoroughly** - After each migration step
5. **Clean up** - Remove old code when confident

---

**Priority**: High - Fixes skip issue and improves maintainability  
**Risk**: Low - Gradual migration with rollback capability  
**Effort**: Medium - ~2-3 hours for full migration

