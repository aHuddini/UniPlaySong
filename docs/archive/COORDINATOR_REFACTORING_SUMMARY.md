# Music Playback Coordinator - Refactoring Summary

**Date**: 2025-12-01  
**Status**: Ready for Implementation  
**Priority**: High - Fixes skip issue and improves maintainability

---

## Executive Summary

The current music playback logic in `UniPlaySong.cs` has become too complex, leading to fragile skip behavior. This refactoring introduces a `MusicPlaybackCoordinator` service that centralizes all playback decisions, state management, and skip logic.

**Key Benefits**:
- ✅ Fixes SkipFirstSelectMusic issue
- ✅ Centralizes all "should play" logic
- ✅ Improves maintainability and testability
- ✅ Preserves all existing functionality
- ✅ Gradual migration (low risk)

---

## Current Problems

1. **Scattered State**: `_firstSelect`, `_loginSkipActive` managed in multiple places
2. **Complex Timing**: Skip logic depends on fragile timing of state clearing
3. **Multiple Entry Points**: 5+ methods can trigger music playback
4. **Hard to Debug**: Logic spread across 200+ lines
5. **Skip Not Working**: Current implementation has timing issues

---

## Solution: MusicPlaybackCoordinator

### Architecture

```
UniPlaySong.cs (Main Plugin - Simplified)
    ↓ delegates to
MusicPlaybackCoordinator (New Service)
    ├── State: _firstSelect, _loginSkipActive
    ├── ShouldPlayMusic() - Single source of truth
    ├── HandleGameSelected() - Coordinates skip + playback
    ├── HandleLoginDismiss() - Controller/keyboard input
    ├── HandleViewChange() - View transitions
    └── HandleVideoStateChange() - Pause/resume
    ↓ uses
MusicPlaybackService (Existing - No Changes)
    └── PlayGameMusic() - Actual playback
```

### Key Design Principles

1. **Single Source of Truth**: All "should play" decisions in `ShouldPlayMusic()`
2. **Centralized State**: All state (`_firstSelect`, `_loginSkipActive`) in coordinator
3. **Clear Timing**: State cleared at end of handlers (PNS pattern)
4. **All Paths Protected**: Every music trigger goes through coordinator
5. **Preserve Functionality**: All existing features maintained

---

## Implementation Plan

### Phase 1: Create Coordinator (Safe - No Breaking Changes)
1. Create `IMusicPlaybackCoordinator` interface
2. Create `MusicPlaybackCoordinator` implementation
3. Initialize in `UniPlaySong.cs` constructor
4. **Test**: Verify coordinator works (existing code unchanged)

### Phase 2: Migrate OnGameSelected (Low Risk)
1. Replace `OnGameSelected()` body with `_coordinator.HandleGameSelected()`
2. Keep old code commented for reference
3. **Test**: Verify skip works correctly

### Phase 3: Migrate ShouldPlayMusic (Low Risk)
1. Replace all `ShouldPlayMusic()` calls with coordinator
2. Remove old method from `UniPlaySong.cs`
3. **Test**: Verify all paths still work

### Phase 4: Migrate Event Handlers (Low Risk)
1. Migrate `OnLoginDismissKeyPress` → `HandleLoginDismiss()`
2. Migrate `OnMainModelChanged` → `HandleViewChange()`
3. Migrate `OnSettingsChanged` → `HandleVideoStateChange()`
4. **Test**: Verify all events work

### Phase 5: Cleanup (Safe)
1. Remove old methods (`PlayMusicForGame`, `PlayMusicBasedOnSelected`)
2. Remove state variables (`_firstSelect`, `_loginSkipActive`)
3. Simplify `UniPlaySong.cs` (should be ~50% smaller)
4. **Test**: Full regression test

---

## Code Simplification

### Before: OnGameSelected() - 50+ lines
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
    
    // ... 30+ more lines of complex logic ...
}
```

### After: OnGameSelected() - 3 lines
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var game = args?.NewValue?.FirstOrDefault();
    _coordinator.HandleGameSelected(game, IsFullscreen);
}
```

**Result**: 50+ lines → 3 lines, all logic centralized

---

## Skip Logic Fix

### Current Problem
- `_firstSelect` cleared too early or too late
- Timing-dependent behavior
- Multiple code paths can bypass checks

### Coordinator Solution
- `_firstSelect` managed in one place
- Cleared at end of `HandleGameSelected()` (predictable)
- `ShouldPlayMusic()` checks it before clearing (execution order)
- All paths go through coordinator (no bypass)

### How It Works
1. First select: `_firstSelect = true` → skip check → clear at end
2. `ShouldPlayMusic()` checks `_firstSelect` (still true during check)
3. Returns false → no music plays
4. `_firstSelect` cleared at end → next selection works

---

## Risk Assessment

### Low Risk ✅
- Creating coordinator (doesn't change existing code)
- Gradual migration (one method at a time)
- Can revert easily if issues

### Mitigation
- Keep old code commented during migration
- Test after each step
- Can rollback to previous version

---

## Testing Checklist

### SkipFirstSelectionAfterModeSwitch
- [ ] Enable setting
- [ ] Switch to fullscreen
- [ ] First select → no music
- [ ] Second select → music plays
- [ ] Controller input → respects skip

### ThemeCompatibleSilentSkip
- [ ] Enable setting
- [ ] Switch to fullscreen
- [ ] First select → wait for input
- [ ] Press Enter → music plays

### Video Pause/Resume
- [ ] Play music
- [ ] Start video → music pauses
- [ ] Stop video → music resumes

### All Entry Points
- [ ] Game selection
- [ ] View changes
- [ ] Controller input
- [ ] Settings changes

---

## Files to Create

1. **`UniPSong/Services/IMusicPlaybackCoordinator.cs`** - Interface
2. **`UniPSong/Services/MusicPlaybackCoordinator.cs`** - Implementation

## Files to Modify

1. **`UniPSong/UniPlaySong.cs`** - Delegate to coordinator, remove old logic
2. **`UniPSong/Services/IMusicPlaybackService.cs`** - May need to add `FadeOutAndStop()` if needed

---

## Next Steps

1. **Review refactoring plan** - `REFACTORING_COORDINATOR_PLAN.md`
2. **Approve approach** - Confirm coordinator design
3. **Implement Phase 1** - Create coordinator (safe)
4. **Test Phase 1** - Verify coordinator works
5. **Gradual migration** - One phase at a time

---

**Estimated Time**: 2-3 hours for full implementation  
**Risk Level**: Low (gradual migration)  
**Priority**: High (fixes critical skip issue)

