# Phased Implementation Plan: Fix Login Screen Music Issue

**Goal**: Match PlayniteSound's simple flag-based pattern to permanently fix the login screen music issue.

**Approach**: Break implementation into 4 phases, each testable independently.

---

## Phase 1: Remove Complex Tracking (Foundation)
**Goal**: Remove unnecessary tracking variables and simplify the codebase  
**Risk**: Low  
**Time**: 10 minutes  
**Test**: Code compiles, no runtime errors

### Changes
1. Remove `_lastSkippedGameId` field from `UniPlaySong.cs`
2. Remove all references to `_lastSkippedGameId` in `OnGameSelected`
3. Remove `isSkippedGameReselected` logic

### Files Modified
- `UniPlaySong.cs`

### Success Criteria
- ✅ Code compiles without errors
- ✅ No references to `_lastSkippedGameId` remain
- ✅ Extension loads without errors

---

## Phase 2: Simplify OnGameSelected (Core Fix)
**Goal**: Replace complex view detection with simple flag check  
**Risk**: Medium  
**Time**: 15 minutes  
**Test**: Music plays correctly in desktop mode

### Changes
1. Replace complex `OnGameSelected` method with simplified version
2. Remove view detection logic (`GetActiveViewName()`, `isLibraryView`)
3. Use simple flag check matching PlayniteSound
4. Clear flag immediately after check

### Files Modified
- `UniPlaySong.cs`

### Success Criteria
- ✅ Code compiles without errors
- ✅ Music plays in desktop mode when selecting games
- ✅ Music stops when no game selected
- ✅ Music respects `EnableMusic` setting

---

## Phase 3: Remove Unused Methods and Handlers (Cleanup)
**Goal**: Remove dead code and unnecessary event handlers  
**Risk**: Low  
**Time**: 10 minutes  
**Test**: Code compiles, no functionality broken

### Changes
1. Remove `OnMainModelPropertyChanged` method
2. Remove subscription to property changes in constructor
3. Remove `GetActiveViewName()` helper method
4. Remove `IsOnLoginScreen()` helper method
5. Remove `GetActiveViewFullName()` helper method

### Files Modified
- `UniPlaySong.cs`

### Success Criteria
- ✅ Code compiles without errors
- ✅ All removed methods are deleted
- ✅ Extension loads without errors
- ✅ Music still works (from Phase 2)

---

## Phase 4: Clean Up Settings (UI Cleanup)
**Goal**: Remove unused settings from code and UI  
**Risk**: Low  
**Time**: 15 minutes  
**Test**: Settings UI works, no broken bindings

### Changes
1. Remove `AutoPlayOnSelection` from `UniPlaySongSettings.cs`
2. Remove `WaitForViewReadyAfterModeSwitch` from `UniPlaySongSettings.cs`
3. Remove `ViewReadyDelayMs` from `UniPlaySongSettings.cs`
4. Update `UniPlaySongSettingsViewModel.cs` commands
5. Remove UI elements from `UniPlaySongSettingsView.xaml`

### Files Modified
- `UniPlaySongSettings.cs`
- `UniPlaySongSettingsViewModel.cs`
- `UniPlaySongSettingsView.xaml`

### Success Criteria
- ✅ Code compiles without errors
- ✅ Settings UI opens without errors
- ✅ Remaining settings work correctly
- ✅ No broken bindings in UI

---

## Optional Phase 5: Add Central Gatekeeper (Enhancement)
**Goal**: Add `ShouldPlayMusic()` method for consistency with PlayniteSound  
**Risk**: Very Low  
**Time**: 5 minutes  
**Test**: Music still works, code is cleaner

### Changes
1. Add `ShouldPlayMusic()` method to `UniPlaySong.cs`
2. Update `OnGameSelected` to use the new method

### Files Modified
- `UniPlaySong.cs`

### Success Criteria
- ✅ Code compiles without errors
- ✅ Music still works correctly
- ✅ Code is more maintainable

---

## Testing After Each Phase

### After Phase 1
- [ ] Extension compiles
- [ ] Extension loads in Playnite
- [ ] No errors in logs

### After Phase 2
- [ ] Music plays in desktop mode
- [ ] Music stops when no game selected
- [ ] Music respects `EnableMusic` setting
- [ ] Music switches when selecting different games

### After Phase 3
- [ ] Extension compiles
- [ ] Extension loads in Playnite
- [ ] Music still works (from Phase 2)
- [ ] No errors in logs

### After Phase 4
- [ ] Settings UI opens
- [ ] Remaining settings work
- [ ] No broken bindings
- [ ] Music still works

### After Phase 5 (Optional)
- [ ] Music still works
- [ ] Code is cleaner
- [ ] Matches PlayniteSound pattern

---

## Final Integration Testing

After all phases complete:

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

## Rollback Strategy

If any phase fails:

1. **Phase 1-3**: Revert `UniPlaySong.cs` to previous version
2. **Phase 4**: Revert all three files to previous versions
3. **Phase 5**: Revert `UniPlaySong.cs` to Phase 4 version

**Note**: Keep a backup of current code before starting Phase 1.

---

## Implementation Notes

### Phase Order Rationale
- **Phase 1**: Simplest change, removes unused code
- **Phase 2**: Core fix, most important change
- **Phase 3**: Cleanup, removes dead code
- **Phase 4**: UI cleanup, independent of core logic
- **Phase 5**: Optional enhancement, can be skipped

### Testing Strategy
- Test after each phase to catch issues early
- Focus on core functionality (music playback) after Phase 2
- Full integration testing after all phases complete

### Risk Mitigation
- Each phase is independent and testable
- Simple changes first, complex changes later
- Can rollback individual phases if needed

---

**Ready to begin Phase 1?** Let me know when you're ready and I'll implement each phase systematically.

