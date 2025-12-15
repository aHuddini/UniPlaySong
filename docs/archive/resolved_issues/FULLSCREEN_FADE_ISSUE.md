# Fullscreen Fade Issue - RESOLVED

**Status**: ✅ **FIXED in v1.0.3.2**

All fade issues have been resolved. See `FADE_SYSTEM_FIXES_v1.0.3.2.md` for complete documentation of the fixes.

---

# Fullscreen Fade Issue (Historical)

## Problem
The music fader does not appear to work in fullscreen mode, despite working correctly in desktop mode. Volume changes are being applied (as confirmed by logs), but the audio output does not reflect the fade transitions.

## Current Implementation
- **Timer Interval**: 16ms (60 FPS)
- **Frequency**: 62.5 steps/second (fixed)
- **Dispatcher**: `Application.Current?.Dispatcher?.Invoke(() => TimerTick())`
- **Volume Control**: SDL2_mixer `Mix_VolumeMusic()` (synchronous)

## Logs Analysis
Logs confirm that:
1. SDL2 is correctly applying volume changes (`mixer: X/128, actual: X/128` match)
2. Fade-in/fade-out volume calculations are correct
3. Timer is firing and updating volume

## Possible Causes

### 1. Dispatcher Issues in Fullscreen
- `Application.Current` might be null or different in fullscreen mode
- Fullscreen themes may use a different WPF application context
- The dispatcher might not be on the UI thread in fullscreen

### 2. SDL2 Volume Control in Fullscreen
- SDL2 might be using a different audio device in fullscreen
- Windows audio system might be interfering with volume changes
- Fullscreen mode might have different audio routing

### 3. Timing Issues
- Fullscreen mode might have different thread priorities
- Dispatcher.Invoke might be blocking differently in fullscreen
- Timer might be firing but volume changes not being applied fast enough

## Investigation Steps

1. **Check Application.Current in fullscreen**
   - Add logging to verify `Application.Current` is available
   - Check if dispatcher is on correct thread

2. **Verify SDL2 volume changes**
   - Add logging to confirm `Mix_VolumeMusic()` is being called
   - Check if volume changes are actually applied to the audio device

3. **Compare with PlayniteSound**
   - Check how PNS handles fullscreen fades
   - Verify if PNS has the same issue or uses different approach

4. **Test with WPF MediaPlayer fallback**
   - Try using WPF MediaPlayer instead of SDL2 in fullscreen
   - See if the issue is SDL2-specific

## Next Steps
- ✅ Add diagnostic logging for fullscreen mode (Application.Current availability)
- ✅ Add fallback for when dispatcher is unavailable (direct call for SDL2)
- Investigate SDL2 volume control in fullscreen mode
- Test if WPF MediaPlayer has the same issue
- Compare with PlayniteSound's fullscreen fade implementation

## Changes Made (v1.0.3.0)
- Added fallback: If `Application.Current` or `Dispatcher` is null, call `TimerTick()` directly (SDL2 is thread-safe)
- Added diagnostic logging to detect dispatcher availability issues
- Added try-catch in `TimerTick()` to catch any exceptions

## Root Cause Identified

The issue was **fixed** by implementing dynamic frequency calculation (matching PlayniteSound's approach).

### The Problem
UniPlaySong was using a **fixed frequency** (62.5 Hz) based on the assumption that timer ticks would always occur at exactly 16ms intervals. However, in fullscreen mode:
- The WPF dispatcher may be slower due to theme rendering overhead
- Actual tick intervals might be 50ms+ instead of 16ms
- Using fixed frequency with slower ticks resulted in fade steps that were too small
- The fade completed too quickly or wasn't perceptible because step size was calculated incorrectly

### The Solution
Changed to **dynamic frequency calculation** (matching PlayniteSound exactly):
- Track actual time between ticks using `_lastTickCall`
- Calculate frequency from real timing: `fadeFrequency = 1000 / lastInterval`
- Adapts to actual system timing, whether desktop or fullscreen
- Ensures fade steps are correctly sized for the actual tick rate

### Changes Made
1. Added `_lastTickCall` field to track actual tick timing
2. Changed from fixed 62.5 Hz to dynamic frequency calculation
3. Reset `_lastTickCall` in `EnsureTimer()` when starting new fades
4. Update `_lastTickCall` after each tick to measure next interval

### Result
The fader now adapts to actual system timing, ensuring perceptible fades in both desktop and fullscreen modes, regardless of dispatcher performance.

## Previous Investigation (Resolved)

The fader appeared to work correctly (volume changes were logged), but audio output in fullscreen mode did not reflect the fade transitions. This was caused by incorrect fade step calculation due to fixed frequency assumption.
