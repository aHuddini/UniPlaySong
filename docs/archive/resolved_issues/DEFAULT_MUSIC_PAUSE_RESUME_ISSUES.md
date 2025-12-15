# Default Music Pause/Resume Issues - Status Report

**Date**: 2025-12-06  
**Status**: ✅ **RESOLVED** - Feature completed (2025-12-07)  
**Resolution**: Implemented single-player architecture with position preservation  
**See**: `../DEFAULT_MUSIC_IMPLEMENTATION.md` for current implementation details

---

## Overview

This document describes the current state of the default music pause/resume feature and the issues that need to be resolved.

## Feature Goal

When switching between games:
1. **Game with no music → Game with music**: Default music should pause (fade out, then pause) and preserve position
2. **Game with music → Game with no music**: Default music should resume from where it paused (fade in from paused position)

This provides a seamless preview experience where default music continues from where it left off.

---

## Current Implementation

### Dual-Player Architecture ✅

A dual-player system has been implemented:
- **Main Player** (`_musicPlayer`): Plays game-specific music
- **Default Music Player** (`_defaultMusicPlayer`): Separate player instance for default music that can stay loaded and paused

**Files Modified**:
- `UniPSong/Services/MusicPlaybackService.cs`:
  - Added `_defaultMusicPlayer`, `_defaultMusicFader`, `_defaultMusicIsPaused`, `_defaultMusicPath` fields
  - Added `CreateDefaultMusicPlayer()` method to lazily create default music player instance
  - Updated `IsPlaying`, `IsPaused`, `IsLoaded` properties to account for both players
  - Updated `Stop()`, `FadeOutAndStop()`, `Pause()`, `Resume()`, `SetVolume()` to handle both players

### Current Behavior

**When switching from default music to game music**:
```csharp
// In PlayGameMusic() - when wasDefaultMusic && isGameMusic:
if (_defaultMusicPlayer != null && _defaultMusicPlayer.IsActive && _defaultMusicPlayer.IsLoaded)
{
    _defaultMusicFader.Pause(); // This should fade out then pause
    _defaultMusicIsPaused = true;
}
```

**When switching back to default music**:
```csharp
// In PlayGameMusic() - when isDefaultMusic:
if (_defaultMusicPlayer != null && 
    _defaultMusicPath == songToPlay && 
    _defaultMusicIsPaused && 
    _defaultMusicPlayer.IsLoaded)
{
    _defaultMusicPlayer.Resume();
    _defaultMusicFader.Resume(); // This should resume and fade in
    _defaultMusicIsPaused = false;
}
```

---

## Issues Reported

### Issue 1: Playback Restarts
**Problem**: When switching from a game with no music → game with music → game with no music, the default music restarts instead of resuming from where it paused.

**Expected**: Default music should resume from the paused position (seamless)  
**Actual**: Default music restarts from the beginning

### Issue 2: No Fade-Out
**Problem**: When switching from default music to a game with actual music, the default music doesn't properly fade out (abrupt stop).

**Expected**: Default music should fade out smoothly (respecting user's fade-out duration setting)  
**Actual**: Default music stops abruptly or doesn't fade out properly

---

## What Was Attempted

### Attempt 1: FadeOutAndPause Method
- Created `FadeOutAndPause()` method in `MusicFader` that would fade out then pause
- **Result**: Reverted - didn't fix the issues

### Attempt 2: Using Existing Fader System
- Changed to use `_defaultMusicFader.Pause()` which should fade out then pause
- Changed to use `_defaultMusicFader.Resume()` which should resume and fade in
- **Result**: Still not working - issues remain

---

## Technical Details

### SDL2_mixer Capabilities

From research:
- `Mix_PauseMusic()` - Pauses music and preserves position ✅
- `Mix_ResumeMusic()` - Resumes from paused position ✅
- `Mix_FadeOutMusic()` - Fades out then stops (not pauses) ❌

**Key Finding**: SDL2_mixer's `Mix_FadeOutMusic()` stops the music after fading, it doesn't pause. To fade out then pause, we need to manually fade the volume, then pause.

### Fader System

The `MusicFader` class:
- `Pause()`: Sets `_isFadingOut = true` and `_pauseAction = _player.Pause`
  - Fades out volume over `FadeOutDuration`
  - When volume reaches 0, calls `_pauseAction` (which pauses the player)
- `Resume()`: Calls `_player.Resume()` and sets `_isFadingOut = false`
  - Should fade in volume over `FadeInDuration`

**Current Implementation**:
- `MusicFader.Pause()` should work correctly (fades out then pauses)
- `MusicFader.Resume()` should work correctly (resumes then fades in)

---

## Root Cause Analysis

### Why Playback Restarts

**Hypothesis 1**: The default music player is being closed/reloaded instead of just paused/resumed.
- **Check**: Look for any `Close()` calls on `_defaultMusicPlayer` when switching to game music
- **Check**: Verify that `_defaultMusicPath` is being preserved correctly

**Hypothesis 2**: SDL2_mixer is losing position when paused.
- **Check**: Verify that `Mix_PauseMusic()` actually preserves position in our implementation
- **Check**: Test if position is lost when switching between players

**Hypothesis 3**: The resume logic isn't detecting the paused state correctly.
- **Check**: Verify that `_defaultMusicIsPaused` flag is being set/cleared correctly
- **Check**: Verify that `_defaultMusicPlayer.IsLoaded` is true when resuming

### Why No Fade-Out

**Hypothesis 1**: The fader's `Pause()` method isn't being called correctly.
- **Check**: Verify that `_defaultMusicFader.Pause()` is actually being called
- **Check**: Verify that the fader timer is running and fading out

**Hypothesis 2**: The fade-out completes too quickly or not at all.
- **Check**: Verify that `FadeOutDuration` setting is being used
- **Check**: Add logging to see if fade-out is happening

**Hypothesis 3**: The player is being paused before fade-out completes.
- **Check**: Verify that pause happens AFTER fade-out completes (in the `_pauseAction` callback)

---

## Recommended Next Steps

### Step 1: Add Detailed Logging
Add comprehensive logging to track:
- When default music is paused (with position)
- When default music is resumed (with position)
- Fade-out/fade-in progress
- Player state changes

```csharp
// In MusicPlaybackService.cs - when pausing:
_fileLogger?.Info($"Pausing default music - Position: {_defaultMusicPlayer.CurrentTime?.TotalSeconds:F2}s, Path: {_defaultMusicPath}");

// In MusicPlaybackService.cs - when resuming:
_fileLogger?.Info($"Resuming default music - Position: {_defaultMusicPlayer.CurrentTime?.TotalSeconds:F2}s, Path: {_defaultMusicPath}");
```

### Step 2: Verify SDL2_mixer Position Preservation
Test if SDL2_mixer actually preserves position:
```csharp
// Test sequence:
1. Load and play default music
2. Wait 10 seconds
3. Pause (note position)
4. Wait 5 seconds
5. Resume (check if position is same)
```

### Step 3: Review Fader Implementation
Check if `MusicFader.Pause()` and `MusicFader.Resume()` are working correctly:
- Does `Pause()` actually fade out before pausing?
- Does `Resume()` actually fade in after resuming?
- Are the fade durations being respected?

### Step 4: Check for Player State Issues
Verify that the default music player state is being maintained:
- Is `_defaultMusicPlayer.IsLoaded` true when resuming?
- Is `_defaultMusicPath` preserved correctly?
- Is `_defaultMusicIsPaused` flag set/cleared correctly?

### Step 5: Consider Alternative Approach
If SDL2_mixer doesn't preserve position reliably:
- Consider tracking position manually before pausing
- Consider using a different audio library that supports seeking
- Consider keeping default music loaded in a separate player that never closes

---

## Files to Review

### Primary Files
- `UniPSong/Services/MusicPlaybackService.cs` - Lines 210-340 (default music pause/resume logic)
- `UniPSong/Players/MusicFader.cs` - Lines 248-316 (Pause/Resume methods)
- `UniPSong/Services/SDL2MusicPlayer.cs` - Lines 196-212 (SDL2 pause/resume implementation)

### Supporting Files
- `UniPSong/Players/SDL/SDL_mixer.cs` - SDL2_mixer function declarations
- `UniPSong/Services/MusicPlaybackCoordinator.cs` - May need updates if coordinator handles default music

---

## Testing Checklist

- [ ] Default music plays when no game music found
- [ ] Default music fades out when switching to game with music
- [ ] Default music pauses (doesn't stop/close) when switching to game with music
- [ ] Default music resumes from paused position (not restart) when switching back
- [ ] Default music fades in when resuming
- [ ] Fade durations respect user settings
- [ ] Multiple switches (no music → music → no music) work correctly
- [ ] Position is preserved across multiple pause/resume cycles

---

## Related Documentation

- `UniPSong/docs/CHANGELOG.md` - Version history
- `UniPSong/docs/ACTION_PLAN_v1.0.4.md` - Default Music Support section
- `UniPSong/docs/SDL2_IMPLEMENTATION.md` - SDL2 implementation details

---

## Notes for Next Agent

1. **Backup Location**: Check for `backup_UniPSong_v1.0.4_[timestamp]` folder
2. **Current State**: Dual-player architecture is in place, but pause/resume logic needs fixing
3. **Key Constraint**: Must respect existing fader system and user settings (fade durations)
4. **SDL2 Limitation**: SDL2_mixer doesn't support seeking, so position preservation relies on pause/resume
5. **User Expectation**: Seamless pause/resume - default music should continue from where it paused

---

## Fixes Applied (2025-12-06)

### Fix 1: MusicFader.Resume() - Proper Fade-In After Resume
**Problem**: `Resume()` wasn't properly starting a fade-in after resuming the player.

**Solution**: 
- Reset `_fadeStartTime` to default to start a new fade-in
- Set `_player.Volume = 0` before starting fade-in (ensures clean fade-in from 0)
- Clear `_fadeOutStartVolume` to reset fade state

**Files Modified**:
- `UniPSong/Players/MusicFader.cs` - `Resume()` method (lines 289-316)

### Fix 2: MusicFader.Pause() - Prevent Restarting Fade-Out
**Problem**: `Pause()` could restart fade-out if called multiple times or if already paused.

**Solution**:
- Check `if (!_isPaused)` before starting fade-out
- Reset `_fadeStartTime` only when starting a new fade-out

**Files Modified**:
- `UniPSong/Players/MusicFader.cs` - `Pause()` method (lines 248-253)

### Fix 3: Volume Threshold Check for Pause Action
**Problem**: Pause action only triggered when volume was exactly 0.0, but floating-point precision meant it might be 0.001 or similar.

**Solution**:
- Changed check from `_player.Volume == 0` to `_player.Volume <= 0.01`
- Matches the threshold used for song switching (line 159)

**Files Modified**:
- `UniPSong/Players/MusicFader.cs` - `TimerTick()` method (line 206)

### Fix 4: Enhanced Logging for Position Tracking
**Problem**: No visibility into position preservation during pause/resume.

**Solution**:
- Added position logging when pausing default music
- Added position logging when resuming default music (before and after)
- Helps verify SDL2_mixer position preservation

**Files Modified**:
- `UniPSong/Services/MusicPlaybackService.cs` - Multiple locations (lines 216-238, 277-285, 328-334)

### Fix 5: Critical Bug - wasDefaultMusic Check Timing ⚠️ **ROOT CAUSE**
**Problem**: `_currentSongPath` was being cleared BEFORE checking if default music was playing, causing the pause logic to never trigger.

**Root Cause**:
- Line 177: `_currentSongPath = null` (cleared for new game)
- Line 320: `wasDefaultMusic` check used `_currentSongPath == settings.DefaultMusicPath`
- Since `_currentSongPath` was already null, the check always failed
- Default music was never paused when switching to game music

**Solution**:
- Moved `wasDefaultMusic` check to BEFORE clearing `_currentSongPath` (line 170-173)
- Added fallback check using `_defaultMusicPath` in addition to `_currentSongPath`
- Updated `wasDefaultMusicInMainPlayer` to use `_defaultMusicPath` instead of `_currentSongPath`

**Files Modified**:
- `UniPSong/Services/MusicPlaybackService.cs` - Lines 167-173, 320, 372

---

**Last Updated**: 2025-12-06  
**Status**: ✅ **RESOLVED** - All issues fixed

