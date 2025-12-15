# Fullscreen Fade Fix - Dynamic Frequency Calculation

## Problem Summary
The music fader was not perceptible in fullscreen mode, despite volume changes being logged correctly. The fade transitions were not audible to users.

## Root Cause
UniPlaySong's fader was using a **fixed frequency** calculation (62.5 Hz) based on the assumption that timer ticks would always occur at exactly 16ms intervals. However:

1. **In fullscreen mode**, the WPF dispatcher may be slower due to:
   - Theme rendering overhead (especially with complex themes like ANIKIREMAKE)
   - Fullscreen window management
   - Additional UI processing

2. **Actual tick intervals** might be 50ms+ instead of the expected 16ms

3. **Fixed frequency calculation** with slower ticks resulted in:
   - Fade steps that were too small (calculated for 16ms but applied at 50ms+)
   - Fade completing too quickly or not being perceptible
   - Volume changes happening but not audibly noticeable

## Solution: Dynamic Frequency Calculation

Changed to match PlayniteSound's proven approach - **dynamic frequency calculation** that adapts to actual system timing.

### Implementation

1. **Added tick timing tracking**:
   ```csharp
   private DateTime _lastTickCall = default;
   ```

2. **Calculate frequency from actual intervals**:
   ```csharp
   double fadeFrequency = 20; // Default: 20 steps per second (50ms intervals)
   
   if (_lastTickCall != default)
   {
       double lastInterval = (DateTime.Now - _lastTickCall).TotalMilliseconds;
       if (lastInterval != 0)
       {
           fadeFrequency = 1000 / lastInterval; // Calculate actual frequency
       }
   }
   ```

3. **Reset tracking when starting new fades**:
   ```csharp
   private void EnsureTimer()
   {
       _lastTickCall = default; // Reset for new fade
       // ...
   }
   ```

4. **Update tracking after each tick**:
   ```csharp
   _lastTickCall = DateTime.Now; // Track actual tick time
   ```

### Benefits

- ✅ **Adapts to actual system timing** - Works correctly whether dispatcher is fast or slow
- ✅ **Perceptible fades in fullscreen** - Step size matches actual tick rate
- ✅ **Consistent behavior** - Desktop and fullscreen modes work the same way
- ✅ **Matches PlayniteSound** - Uses the same proven approach

## Technical Details

### Before (Fixed Frequency)
```csharp
// Assumed 16ms intervals
double fadeFrequency = 62.5; // 1000ms / 16ms
double fadeStep = musicVolume / (fadeDuration * fadeFrequency);
```

**Problem**: If actual interval is 50ms, steps are calculated for 16ms but applied at 50ms, making them too small.

### After (Dynamic Frequency)
```csharp
// Measures actual intervals
double lastInterval = (DateTime.Now - _lastTickCall).TotalMilliseconds;
double fadeFrequency = 1000 / lastInterval; // Adapts to real timing
double fadeStep = musicVolume / (fadeDuration * fadeFrequency);
```

**Solution**: Step size matches actual tick rate, ensuring perceptible fades.

## Testing

### Expected Behavior
- Fade-in should be audible when switching to a game with music
- Fade-out should be audible when switching away from a game
- Fades should work consistently in both desktop and fullscreen modes
- Volume transitions should be smooth and perceptible

### Test Scenarios
1. **Desktop Mode**: Switch between games - verify audible fades
2. **Fullscreen Mode**: Switch between games - verify audible fades
3. **Complex Theme**: Test with ANIKIREMAKE or other fullscreen themes
4. **Rapid Switching**: Switch games quickly - verify smooth transitions

## Files Modified

- `UniPSong/Players/MusicFader.cs`
  - Added `_lastTickCall` field for timing tracking
  - Changed from fixed to dynamic frequency calculation
  - Updated `EnsureTimer()` to reset tracking
  - Updated `TimerTick()` to track and use actual intervals

## Related Documentation

- `FULLSCREEN_FADE_ISSUE.md` - Original issue investigation
- `PNS_COMPARISON_ANALYSIS.md` - Comparison with PlayniteSound
- `ARCHITECTURE.md` - Overall system architecture

## Version

Fixed in: **v1.0.3.1** (or next version)


