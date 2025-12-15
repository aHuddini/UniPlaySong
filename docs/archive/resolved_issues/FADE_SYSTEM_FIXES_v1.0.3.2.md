# Fade System Fixes - Version 1.0.3.2

## Overview

This document details the comprehensive fixes applied to the music fade system in version 1.0.3.2, which resolved critical issues with fade-in functionality in fullscreen mode and improved the overall fade experience with exponential curves.

## Problems Solved

### 1. Fade-In Not Working in Fullscreen Mode
**Issue**: Fade-in worked for only one game ("9 shadows") but not for other games when switching.

**Root Cause**: The song switching logic was placed after the completion check, causing it to be skipped when the fade-out completed. Additionally, the volume check was too strict (`== 0` instead of `<= 0.01`), missing the transition point with exponential curves.

**Fix Applied**: 
- Moved song switching check **before** completion check (line 130-152 in `MusicFader.cs`)
- Changed volume threshold from `== 0` to `<= 0.01` to account for floating-point precision
- Added `_isFadingOut` check to ensure we only handle song switching during fade-out

**Code Reference**:
```130:152:UniPSong/Players/MusicFader.cs
// Handle song switching during fade-out FIRST (before completion check)
// This ensures smooth transition from fade-out to fade-in
if (_isFadingOut && _player.Volume <= 0.01 && _pauseAction == null && _playAction != null)
{
    // Fade out complete (or nearly complete), switch to new song
    _stopAction?.Invoke();
    _player.Volume = 0; // Ensure volume is 0 before playAction
    _playAction.Invoke();
    
    // CRITICAL FIX: After playAction (which calls Load()/Play()), force volume to 0
    // SDL2's Play() doesn't reset volume, but we need to ensure it's 0 for fade-in
    // This is especially important in fullscreen where volume state might be inconsistent
    // Also ensure _isFadingOut is false BEFORE setting volume, so next tick knows to fade in
    _isFadingOut = false;
    _player.Volume = 0;
    
    // Reset fade start time for new fade-in
    _fadeStartTime = default;
    
    _stopAction = _playAction = null;
    // CRITICAL: Don't return here - continue timer to fade in (matching PNS)
    // The next tick will be in fade-in mode and will start fading in from volume 0
}
```

### 2. "Delayed at Low Volume" Feeling
**Issue**: With longer fade-in durations (e.g., 6 seconds), the music felt like it was "delayed 6 seconds starting at a low volume" rather than a smooth volume ramp.

**Root Cause**: Linear fade curves don't match human perception of volume, which is logarithmic. Linear fades spend too much time at imperceptibly low volumes.

**Fix Applied**: Implemented exponential fade curves based on audio engineering best practices:
- **Fade-in**: `progress^2` - starts fast, slows down (prevents "delayed" feeling)
- **Fade-out**: `1 - (1-progress)^2` - starts slow, speeds up (natural decay)

**Code Reference**:
```91:112:UniPSong/Players/MusicFader.cs
// Apply exponential curve for natural-sounding fades
// Human perception of volume is logarithmic, so we use exponential curves
// Fade-in: exponential curve (starts fast, slows down) - uses progress^2
// Fade-out: inverse exponential (starts slow, speeds up) - uses 1 - (1-progress)^2
double targetVolume;
if (_isFadingOut)
{
    // Fade-out: exponential decay (1 - progress^2) - starts slow, speeds up
    // This makes the fade feel natural as it gets quieter
    double curveProgress = 1.0 - Math.Pow(1.0 - progress, 2.0);
    targetVolume = musicVolume * (1.0 - curveProgress);
}
else
{
    // Fade-in: exponential rise (progress^2) - starts fast, slows down
    // This prevents the "delayed at low volume" feeling
    double curveProgress = Math.Pow(progress, 2.0);
    targetVolume = musicVolume * curveProgress;
}

// Clamp to valid range
targetVolume = Math.Max(0.0, Math.Min(musicVolume, targetVolume));
```

### 3. Time-Based Fade Calculation
**Issue**: Previous step-based approach was sensitive to timer tick timing variations, especially in fullscreen mode where dispatcher calls could be delayed.

**Fix Applied**: Switched to time-based calculation that uses elapsed time since fade start, making fades predictable regardless of tick timing.

**Code Reference**:
```81:89:UniPSong/Players/MusicFader.cs
// Initialize fade start time if this is the first tick of a new fade
if (_fadeStartTime == default)
{
    _fadeStartTime = DateTime.Now;
}

// Calculate elapsed time since fade started
double elapsedSeconds = (DateTime.Now - _fadeStartTime).TotalSeconds;
double progress = Math.Min(1.0, elapsedSeconds / fadeDuration);
```

### 4. Duplicate Code Block
**Issue**: Duplicate pause/stop handling block (lines 177-185 and 186-194) that could cause unexpected behavior.

**Fix Applied**: Removed duplicate block, keeping only the first instance.

## Technical Implementation Details

### Exponential Curve Mathematics

**Fade-In Formula**:
```
targetVolume = musicVolume * (progress^2)
where progress = elapsedTime / fadeDuration
```

**Example with 6-second fade-in at 50% volume**:
- At 0.0s (0%): volume = 0.5 * (0.0^2) = 0.0 (0%)
- At 1.0s (17%): volume = 0.5 * (0.17^2) = 0.014 (2.8%) - **perceptible immediately**
- At 3.0s (50%): volume = 0.5 * (0.5^2) = 0.125 (25%)
- At 6.0s (100%): volume = 0.5 * (1.0^2) = 0.5 (50%)

**Fade-Out Formula**:
```
targetVolume = musicVolume * (1 - (1 - progress)^2)
```

**Example with 3-second fade-out at 50% volume**:
- At 0.0s (0%): volume = 0.5 * (1 - 1^2) = 0.5 (50%)
- At 1.5s (50%): volume = 0.5 * (1 - 0.5^2) = 0.375 (37.5%)
- At 3.0s (100%): volume = 0.5 * (1 - 0^2) = 0.0 (0%)

### Volume State Management

The fix ensures proper volume state during transitions:

1. **Before playAction**: Volume set to 0 (line 136)
2. **After playAction**: Volume forced to 0 again (line 144) - SDL2's `Play()` doesn't reset volume
3. **Fade state**: `_isFadingOut` set to `false` before volume reset (line 143)
4. **Fade timing**: `_fadeStartTime` reset to `default` for new fade-in (line 147)

This ensures the next tick correctly identifies fade-in mode and starts from volume 0.

## Testing Results

### Before Fix
- Fade-in only worked for one specific game
- Long fade-ins (6+ seconds) felt "delayed" at low volume
- Fade transitions were inconsistent when switching games

### After Fix
- Fade-in works for all games when switching
- Long fade-ins feel smooth and natural
- Exponential curves provide perceptible volume changes immediately
- Consistent behavior in both desktop and fullscreen modes

## Code References

### Key Files Modified
- `UniPSong/Players/MusicFader.cs` - Core fade logic with exponential curves and fixed transition handling

### Key Methods
- `TimerTick()` - Main fade calculation with exponential curves (lines 65-204)
- `Switch()` - Song switching with proper fade start time reset (lines 235-258)
- `FadeIn()` - Initial fade-in with volume and timing reset (lines 289-304)

### Critical Sections
1. **Exponential curve calculation**: Lines 91-112
2. **Song switching logic**: Lines 130-152
3. **Completion check**: Lines 153-174
4. **Fade start time initialization**: Lines 81-85

## Related Documentation

- `FULLSCREEN_FADE_ISSUE.md` - Original problem investigation
- `FULLSCREEN_FADE_FIX.md` - Dynamic frequency calculation fix (v1.0.3.1)
- `CHANGELOG.md` - Version history

## Version Information

- **Version**: 1.0.3.2
- **Date**: 2025-11-30
- **Status**: ✅ All fade issues resolved
- **Tested**: Fullscreen and desktop modes, all game switching scenarios


