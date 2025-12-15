# Failure Analysis: Login Screen Music Playback Issue

## Problem Statement
Music plays during fullscreen startup/login screen when it should only play after the user reaches the library/grid view. The goal is to achieve a "console-like game preview experience" where music only plays after passing the login screen.

## Current Status
**UNRESOLVED** - After multiple refactoring attempts, the issue persists:
1. Music still plays immediately when fullscreen mode starts
2. Music doesn't trigger when switching to a second game

## Root Cause Analysis

### Primary Issue
The core problem is that `OnGameSelected` fires during fullscreen startup (before login screen is passed), and the protection mechanisms (`_firstSelect` flag, `VideoIsPlaying` check) are not working correctly.

### Key Observations from PlayniteSound
1. **PlayniteSound uses `_firstSelect` flag** - Set to `true` initially, cleared in `OnGameSelected`
2. **PlayniteSound checks `SkipFirstSelectMusic` setting** - If enabled, skips music on first select
3. **PlayniteSound uses `VideoIsPlaying`** - Set by `MediaElementsMonitor` when videos are detected
4. **PlayniteSound's `PauseMusic()` checks `IsLoaded`** - Not `IsPlaying`
5. **PlayniteSound's `ResumeMusic()` checks `IsLoaded`** - Resumes if loaded, otherwise calls `PlayMusicBasedOnSelected()`

## Attempted Solutions and Failures

### Attempt 1: Initial `_firstSelect` Flag Implementation
**What was done:**
- Added `_firstSelect` flag initialized to `true`
- Checked in `OnGameSelected` to skip music if `_firstSelect && SkipFirstSelectionAfterModeSwitch`
- Cleared `_firstSelect` after check

**Why it failed:**
- `_firstSelect` was cleared too early (immediately after check in `OnGameSelected`)
- When `VideoIsPlaying` changed from `true` to `false`, `ResumeMusic()` was called, but `_firstSelect` was already `false`
- Music played prematurely

### Attempt 2: Delayed `_firstSelect` Clearing
**What was done:**
- Attempted to delay clearing `_firstSelect` until after videos ended
- Added `_hasSeenVideoPlay` flag to track video state

**Why it failed:**
- Added custom logic not present in PlayniteSound
- User explicitly requested to match PlayniteSound exactly
- Removed per user feedback

### Attempt 3: `VideoIsPlaying` Initialization
**What was done:**
- Initialized `VideoIsPlaying = true` in fullscreen mode during constructor
- Expected `MediaElementsMonitor` to set it to `false` when videos end

**Why it failed:**
- `MediaElementsMonitor` timer only starts when `MediaElement_Opened` fires
- If no media elements are detected initially, `VideoIsPlaying` stays `true` or gets set to `false` incorrectly
- Music still played because `OnGameSelected` fired before videos were detected

### Attempt 4: Immediate Timer Start in Fullscreen
**What was done:**
- Started `MediaElementsMonitor` timer immediately in fullscreen mode
- Expected to detect videos before `OnGameSelected` fires

**Why it failed:**
- This was custom logic not in PlayniteSound
- Removed per user feedback to match PlayniteSound exactly

### Attempt 5: `PauseMusic()` Fix (IsLoaded vs IsPlaying)
**What was done:**
- Changed `PauseMusic()` to check `IsLoaded` instead of `IsPlaying`
- Matched PlayniteSound's exact pattern

**Why it failed:**
- This was a correct fix, but the underlying issue (music playing during startup) was not addressed
- The real problem is that `ShouldPlayMusic()` returns `true` when it shouldn't

## Critical Code Paths

### OnGameSelected Flow (Current)
```
OnGameSelected fires
  → skipMusic = _firstSelect && SkipFirstSelectionAfterModeSwitch
  → if (!skipMusic) PlayMusicBasedOnSelected()
  → _firstSelect = false (ALWAYS cleared)
  → PlayMusicBasedOnSelected() calls ShouldPlayMusicOrClose()
  → ShouldPlayMusic() checks:
     - !videoIsPlaying (from settings)
     - ShouldPlayAudio() which checks _firstSelect (but it's already false!)
```

### The Problem
1. `OnGameSelected` fires during startup/login
2. `skipMusic` might be `false` if `SkipFirstSelectionAfterModeSwitch` is disabled
3. `PlayMusicBasedOnSelected()` is called
4. `_firstSelect` is cleared BEFORE `ShouldPlayMusic()` checks it
5. `ShouldPlayAudio()` checks `_firstSelect`, but it's already `false`
6. Music plays

### PlayniteSound's Flow (Reference)
```
OnGameSelected fires
  → skipMusic = _firstSelect && SkipFirstSelectMusic
  → if (!skipMusic) PlayMusicBasedOnSelected()
  → _firstSelect = false (ALWAYS cleared)
  → PlayMusicBasedOnSelected() calls ShouldPlayMusicOrClose()
  → ShouldPlayMusic() checks:
     - !videoIsPlaying (from settings)
     - ShouldPlayAudio() which checks _firstSelect (but it's already false!)
```

**Wait - PlayniteSound has the same pattern!** So why does it work?

## Potential Issues with Analysis

### Issue 1: Incomplete File Review
**Problem:** May have missed critical differences in:
- How `MediaElementsMonitor` detects videos
- When `VideoIsPlaying` is set to `true` vs `false`
- The exact timing of `OnGameSelected` vs video detection
- How PlayniteSound handles the initial fullscreen startup

**Evidence:**
- User feedback: "You have failed to review files properly to make misinformed decisions"
- Multiple attempts failed despite matching PlayniteSound's structure
- Logs were not thoroughly analyzed to understand the exact sequence of events

### Issue 2: Assumptions About PlayniteSound's Behavior
**Problem:** Assumed PlayniteSound's code would work the same way in UniPlaySong without verifying:
- Exact timing of events
- Initial state of `VideoIsPlaying`
- When `MediaElementsMonitor` actually detects videos
- Whether `OnGameSelected` fires at different times in different scenarios

**Evidence:**
- Attempted to initialize `VideoIsPlaying = true` without verifying if PlayniteSound does this
- Added custom timer start logic without checking if PlayniteSound needs it
- Didn't verify if PlayniteSound has any startup-specific logic

### Issue 3: Not Analyzing Logs Thoroughly
**Problem:** Didn't systematically compare UniPlaySong logs with PlayniteSound logs to identify:
- Exact sequence of events
- When `OnGameSelected` fires relative to video detection
- When `VideoIsPlaying` changes
- When `_firstSelect` is checked vs cleared

**Evidence:**
- User provided logs but analysis was superficial
- Didn't create a detailed event timeline
- Didn't identify the exact moment when music starts playing incorrectly

## Files Modified

### Core Files
1. **UniPSong/UniPlaySong.cs**
   - Added `_firstSelect` flag (line ~68)
   - Modified `OnGameSelected` to check `_firstSelect` (lines 245-302)
   - Added `ShouldPlayMusic()`, `ShouldPlayAudio()`, `ShouldPlayMusicOrClose()` methods
   - Added `PauseMusic()`, `ResumeMusic()`, `PlayMusicBasedOnSelected()` methods
   - Added `OnSettingsChanged` handler for `VideoIsPlaying` changes
   - Added `MediaElementsMonitor` attachment

2. **UniPSong/UniPlaySongSettings.cs**
   - Added `MusicState` property (AudioState enum)
   - Added `VideoIsPlaying` property
   - Added `PauseOnTrailer` property

3. **UniPSong/Monitors/MediaElementsMonitor.cs**
   - Created to match PlayniteSound's MediaElementsMonitor
   - Monitors MediaElement instances to detect video playback
   - Updates `VideoIsPlaying` in settings

4. **UniPSong/Models/AudioState.cs**
   - Created enum for music state (Never, Desktop, Fullscreen, Always)

5. **UniPSong/Services/MusicPlaybackService.cs**
   - Added `IsPaused` property
   - Added `IsLoaded` property
   - Modified `Pause()` to check `IsLoaded`
   - Modified `Resume()` to check `IsPaused`

6. **UniPSong/Services/IMusicPlaybackService.cs**
   - Added `IsPaused` property
   - Added `IsLoaded` property

## What Needs to Be Done

### Immediate Actions
1. **Thoroughly analyze PlayniteSound's logs vs UniPlaySong logs**
   - Create a detailed event timeline
   - Identify exact sequence: OnApplicationStarted → OnGameSelected → MediaElementsMonitor → VideoIsPlaying changes
   - Find the exact moment when music starts playing incorrectly

2. **Review PlayniteSound's initialization sequence**
   - Check if `VideoIsPlaying` is initialized to any value
   - Check if `MediaElementsMonitor` has any startup-specific logic
   - Check if there's any delay or timing mechanism

3. **Verify the exact timing of events**
   - When does `OnGameSelected` fire relative to fullscreen startup?
   - When does `MediaElementsMonitor` detect videos?
   - When does `VideoIsPlaying` get set to `true`?

4. **Check if PlayniteSound has any hidden logic**
   - Review all event handlers
   - Check if there's any mode switch detection
   - Check if there's any view detection logic

### Potential Solutions to Investigate

1. **Delay `_firstSelect` clearing until after video detection**
   - Don't clear `_firstSelect` in `OnGameSelected` if `VideoIsPlaying` is `true`
   - Clear it when `VideoIsPlaying` changes from `true` to `false`

2. **Initialize `VideoIsPlaying = true` in fullscreen, but handle it differently**
   - Set it to `true` initially
   - Only set it to `false` when videos are actually detected and end
   - Don't set it to `false` if no videos are found initially

3. **Check if `OnGameSelected` should be ignored entirely during startup**
   - Add a flag to track if we're in startup phase
   - Only process `OnGameSelected` after startup is complete
   - But this might not match PlayniteSound

4. **Review PlayniteSound's `ShouldPlayAudio` more carefully**
   - Maybe there's a condition we're missing
   - Maybe the `_firstSelect` check works differently than we think

## Lessons Learned

1. **Don't assume code structure matching means behavior matching**
   - Even if the code looks the same, timing and initialization matter
   - Need to verify exact behavior, not just code structure

2. **Thoroughly analyze logs before making changes**
   - Create detailed event timelines
   - Compare with reference implementation logs
   - Identify exact failure points

3. **Verify all assumptions**
   - Don't assume PlayniteSound does something without checking
   - Don't add custom logic without verifying it's not in PlayniteSound
   - Question every assumption

4. **Review files completely**
   - Don't skip sections
   - Read entire methods, not just snippets
   - Check for hidden logic or edge cases

## Notes for Next Agent

1. **Start with log analysis**
   - Get fresh logs from both UniPlaySong and PlayniteSound
   - Create a detailed event timeline
   - Identify the exact moment of failure

2. **Review PlayniteSound's entire initialization sequence**
   - Read the constructor completely
   - Check all event subscriptions
   - Verify all initial state

3. **Test incrementally**
   - Make one change at a time
   - Test after each change
   - Revert if it doesn't work

4. **Question everything**
   - Don't assume anything
   - Verify every assumption
   - Check PlayniteSound's code for every decision

5. **Consider that PlayniteSound might have hidden logic**
   - Check for reflection-based code
   - Check for dynamic code
   - Check for theme-specific logic

## Current Code State

The code currently matches PlayniteSound's structure, but the behavior is incorrect. The issue is likely in:
- Timing of events (when things fire relative to each other)
- Initial state (what values things start with)
- Event sequence (the order things happen in)

The next agent should focus on understanding the EXACT sequence of events in PlayniteSound and replicating it precisely.

