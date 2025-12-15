# Version 1.0.3.0 Summary

## Backup Created
- **Backup Location**: `backup_UniPSong_v1.0.3.0_2025-11-30_12-07-53`
- **Date**: 2025-11-30

## Changes Made

### Fader Optimizations
1. **Timer Interval**: Changed from 50ms to 16ms (60 FPS)
   - Smoother, more perceptible volume transitions
   - Common practice: 16-33ms for audio fades

2. **Fixed Frequency**: 62.5 steps/second (instead of dynamic calculation)
   - Consistent step calculation regardless of system timing
   - Formula: 1000ms / 16ms = 62.5 steps/second

3. **Minimum Step Size**: 0.5% of target volume
   - Ensures perceptible changes
   - Prevents steps that are too small to hear

4. **Reduced Logging**: Removed verbose per-tick logging
   - Only logs errors and warnings
   - Cleaner logs for debugging

### Fullscreen Fade Issue
- **Problem**: Fader doesn't appear to work in fullscreen mode
- **Status**: Volume changes are logged correctly, but audio output doesn't reflect fades
- **Documentation**: Created `FULLSCREEN_FADE_ISSUE.md` with investigation steps
- **Fixes Applied**:
  - Added fallback for when `Application.Current` or `Dispatcher` is null
  - Added diagnostic logging to detect dispatcher availability
  - Added try-catch in `TimerTick()` for error handling

## Known Issues
1. **Fullscreen Fade Not Audible**: Volume changes are applied (confirmed by logs), but audio output doesn't reflect fades in fullscreen mode
   - Possible causes: SDL2 volume control, Windows audio system, or volume step size
   - See `FULLSCREEN_FADE_ISSUE.md` for details

## Technical Details

### Fader Calculation
- **Step Size**: `fadeStep = musicVolume / (fadeDuration * 62.5)`
- **Minimum Step**: `minStep = musicVolume * 0.005` (0.5%)
- **Timer**: 16ms interval, 62.5 steps/second

### Example
For a 2.45s fade-in at 0.5 volume:
- Total steps: 2.45 * 62.5 = ~153 steps
- Step size: 0.5 / 153 = ~0.00327 (0.33% per step)
- With minimum step: 0.5 * 0.005 = 0.0025 (0.25%)

## Files Modified
- `UniPSong/Players/MusicFader.cs`: Optimized timer, frequency, and step calculation
- `UniPSong/docs/CHANGELOG.md`: Updated with v1.0.3.0 changes
- `UniPSong/docs/FULLSCREEN_FADE_ISSUE.md`: Created investigation document

## Next Steps
1. Investigate SDL2 volume control in fullscreen mode
2. Test with WPF MediaPlayer fallback to see if issue is SDL2-specific
3. Compare with PlayniteSound's fullscreen fade implementation
4. Consider alternative fade approaches for fullscreen mode

