# Recommendations to Fix Initial Game Music Not Playing

## Current Status
- ✅ Fixed: WPF MediaPlayer async loading (MediaOpened event handling)
- ❌ Still broken: Initial selected game's music doesn't play after login
- ✅ Working: Music plays when switching to second game

## Recommendations from Discrepancy Analysis

### 1. **Add ReloadMusic Flag** (HIGH PRIORITY)
**Why**: PlayniteSound uses `ReloadMusic` to force reload even if the same file is selected. This ensures music always plays when needed.

**Current Issue**: When we clear silent skip and try to play the first game, if `_currentSongPath` is already set (from silent file or previous state), we might skip loading.

**Fix**: Add `ReloadMusic` boolean flag and check it in `PlayGameMusic()` to force reload.

### 2. **Simplify Silent Skip Logic** (HIGH PRIORITY)
**Why**: Our silent skip logic might be interfering with normal music playback flow.

**Current Issue**: When we clear `_isSilentSkipActive` and call `Stop()`, then immediately call `PlayMusicBasedOnSelected()`, there might be a race condition or state conflict.

**Fix**: 
- Ensure `Stop()` fully completes before loading new music
- Consider removing explicit `Stop()` call and let `PlayGameMusic()` handle it
- Verify silent skip clearing doesn't prevent `ShouldPlayMusicOrClose()` from returning true

### 3. **Add Delay After Close() Before Load()** (MEDIUM PRIORITY)
**Why**: WPF MediaPlayer might need time to fully close before loading a new file.

**Current Issue**: We call `Close()` then immediately `Load()`, but MediaPlayer might not be ready.

**Fix**: Add a small delay (10-50ms) or use Dispatcher to ensure Close() completes before Load().

### 4. **Match PlayniteSound's MediaTimeline/Clock Approach** (HIGH PRIORITY - CRITICAL DIFFERENCE)
**Why**: PlayniteSound's WMPMusicPlayer uses `MediaTimeline` and `Clock` instead of direct `MediaPlayer.Open()`. This might handle async loading better.

**Current Issue**: We use `MediaPlayer.Open()` which is async, but PlayniteSound uses `MediaTimeline.CreateClock()` which might be more reliable.

**Fix**: Consider switching to MediaTimeline/Clock approach like PlayniteSound's WMPMusicPlayer.

### 5. **Add MediaPlayer Event Handlers in Main Class** (LOW PRIORITY)
**Why**: Better state tracking and debugging.

**Fix**: Subscribe to MediaOpened/MediaEnded/MediaFailed in main class to track state.

---

## Most Likely Root Cause

Based on the analysis, the most likely issue is **#4 - MediaTimeline/Clock approach**. 

PlayniteSound's WMPMusicPlayer uses:
```csharp
_timeLine.Source = new Uri(filePath);
_mediaPlayer.Clock = _timeLine.CreateClock();
```

This is different from our approach:
```csharp
_mediaPlayer.Open(uri);
```

The MediaTimeline/Clock approach might handle the async loading more reliably, especially when transitioning from silent file to real music.

