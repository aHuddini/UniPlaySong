# Fade-In Not Working in Fullscreen - Analysis & Recommendations

## Problem
Fade-in works in desktop mode but not in fullscreen mode when switching games normally. Fade-out works fine in both modes.

## Key Findings

### 1. **Volume Setter Complexity**
**PNS Implementation:**
```csharp
public double Volume
{
    get => _mediaPlayer?.Volume ?? 0;
    set
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = value;  // Simple, direct, synchronous
        }
    }
}
```

**Our Previous Implementation:**
- Used `_pendingVolume` field
- Used `Dispatcher.BeginInvoke` (non-blocking) when not on UI thread
- Added complexity that could cause timing issues

**Fix Applied:**
- Simplified to match PNS exactly - direct synchronous volume set
- Removed `_pendingVolume` entirely
- MediaPlayer is free-threaded, so dispatcher complexity was unnecessary

### 2. **Load() Volume Manipulation**
**PNS Implementation:**
```csharp
public void Load(string filePath)
{
    if (_preloadedMediaPlayer != null && _preloadedFile == filePath)
    {
        SwapMediaPlayers();
    }
    else
    {
        _timeLine.Source = new Uri(filePath);
        _mediaPlayer.Clock = _timeLine.CreateClock();  // No volume manipulation
    }
    DisposePreloaded();
}
```

**Our Previous Implementation:**
- Checked volume before/after Clock creation
- Tried to force volume to 0 if needed
- Added complexity that might interfere with fader

**Fix Applied:**
- Simplified to match PNS exactly - no volume manipulation in Load()
- Let the fader control volume completely

### 3. **Fader Logic**
**PNS Implementation:**
```csharp
else if (player.Volume == 0 && pauseAction == null && playAction != null)
{
    stopAction?.Invoke();
    player.Volume = 0;  // Set to 0 before playAction
    playAction.Invoke();
    stopAction = playAction = null;
    isFadingOut = false;  // Switch to fade-in mode
    // No return - continues timer to fade in
}
```

**Our Implementation:**
- Matches PNS exactly now
- Sets volume to 0 before playAction
- Continues timer to fade in (no return)

### 4. **Potential Issue: Clock Creation Resets Volume**
When `Load()` creates a new `Clock`, WPF's `MediaPlayer` might reset its volume to a default (0.5) or previous value. This could explain why:
- Logs show `"Load: Volume was 0.500 before Clock"` 
- After Clock creation, volume is 0.500 instead of 0

**PNS doesn't handle this either** - they rely on the fader to set volume to 0 in the next tick after `playAction` is called.

**However**, if the fader checks `_player.Volume == 0` to determine fade-out completion, but after `Load()` the volume is 0.500, the fader might be in an inconsistent state:
- `_isFadingOut = false` (fade-in mode)
- But `_player.Volume = 0.500` (not 0)
- Next tick should set it to 0, then fade in

## Alternative Audio Players

### 1. **SDL2 (PlayniteSound's Primary Choice)**
**Pros:**
- Native library, no WPF threading issues
- Works reliably in fullscreen
- Better performance
- Cross-platform (if needed)

**Cons:**
- Requires native DLL (`SDL2.dll`, `SDL2_mixer.dll`)
- More complex setup
- Need to handle P/Invoke

**Implementation:**
- PNS has `SDL2MusicPlayer.cs` as reference
- Uses `SDL2Mixer` for audio playback
- Volume control is direct and synchronous

### 2. **NAudio**
**Pros:**
- Pure .NET library (no native dependencies)
- More control over audio
- Better for advanced features

**Cons:**
- Larger dependency
- More complex API
- Might be overkill for simple playback

### 3. **CSCore**
**Pros:**
- Pure .NET
- Good performance
- Active development

**Cons:**
- Less popular
- Might have compatibility issues

### 4. **WPF MediaPlayer (Current)**
**Pros:**
- Built into WPF (no dependencies)
- Simple API
- Works in desktop mode

**Cons:**
- **Volume control issues in fullscreen** (current problem)
- Clock creation might reset volume
- Threading complexity

## Recommendations

### Short-term (Current Fix)
1. ✅ Simplified Volume setter to match PNS exactly
2. ✅ Removed `_pendingVolume` complexity
3. ✅ Simplified `Load()` to match PNS exactly
4. ✅ Simplified `SwapMediaPlayers()` to match PNS exactly

**Test this version** - the simplified implementation should work better.

### If Issue Persists
1. **Add explicit volume check after Load() in fader:**
   - After `playAction.Invoke()` (which calls `Load()`), check if volume is still 0
   - If not, force it to 0 before continuing fade-in

2. **Consider SDL2 migration:**
   - PNS uses SDL2 as primary player (WMP is fallback)
   - SDL2 doesn't have WPF threading issues
   - Would require adding native DLLs to extension package

3. **Debug logging:**
   - Add more detailed logging in fader after `playAction` is called
   - Log volume before/after `Load()` and Clock creation
   - Track `_isFadingOut` state throughout fade process

## Next Steps
1. Test the simplified version (already built)
2. If fade-in still doesn't work, add explicit volume check after `playAction`
3. If still broken, consider SDL2 migration (more work but more reliable)

