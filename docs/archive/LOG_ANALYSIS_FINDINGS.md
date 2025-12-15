# Log Analysis Findings: Root Cause Identified

**Date**: 2025-11-29  
**Analysis Tool**: Direct log file review  
**Log Files Analyzed**: extensions.log, UniPlaySong.log, PlayniteSound.log

---

## ✅ CRITICAL FINDING: OnGameSelected IS Being Called

### The Actual Problem

**OnGameSelected IS working correctly and skipping music on first select.** However, music plays immediately after because `ResumeMusic()` is called when `VideoIsPlaying` changes, and it doesn't respect the "first select" state.

### Evidence from Logs

#### UniPlaySong Log (16:03:56.669 - First OnGameSelected)

```
[2025-11-29 16:03:56.669] OnGameSelected called - Game: 9 Years of Shadows, FirstSelect: True
[2025-11-29 16:03:56.671] OnGameSelected - SkipMusic: True, FirstSelect: True, SkipFirstSelectionAfterModeSwitch: True
[2025-11-29 16:03:56.671] OnGameSelected - Skipping music (first select) ✅ CORRECT
[2025-11-29 16:03:56.672] OnGameSelected - Setting _firstSelect from True to false
[2025-11-29 16:03:56.672] OnGameSelected - _firstSelect is now False
```

**OnGameSelected correctly skips music!** ✅

#### But Then VideoIsPlaying Changes (16:03:57.586-57.616)

```
[2025-11-29 16:03:57.586] OnSettingsChanged: Property 'VideoIsPlaying' changed
[2025-11-29 16:03:57.592] OnSettingsChanged: VideoIsPlaying changed to False
[2025-11-29 16:03:57.594] OnSettingsChanged: Video ended - Calling ResumeMusic()
[2025-11-29 16:03:57.599] ShouldPlayAudio - FirstSelect: False, SkipFirstSelectMusicResult: False, Result: True
[2025-11-29 16:03:57.600] ResumeMusic() - Music player NOT loaded, calling PlayMusicBasedOnSelected()
[2025-11-29 16:03:57.605] PlayMusicBasedOnSelected() - Calling PlayGameMusic()
[2025-11-29 16:03:57.613] Loading and playing song: Apino Belial.mp3 ❌ MUSIC PLAYS!
```

**The problem:** `ResumeMusic()` is called when video ends, and because `_firstSelect` was already cleared to `False` in `OnGameSelected`, `ShouldPlayAudio()` returns `True`, so music starts playing.

---

## Root Cause Analysis

### The Sequence of Events

1. **Extension loads** → `_firstSelect = True`
2. **OnGameSelected fires** (login screen) → Skips music correctly, sets `_firstSelect = False` ✅
3. **VideoIsPlaying changes** (login video/trailer ends) → Calls `ResumeMusic()`
4. **ResumeMusic() checks ShouldPlayMusic()** → Returns `True` because `_firstSelect` is now `False`
5. **Music starts playing** ❌ **This is the bug!**

### Why This Happens

The issue is that `_firstSelect` is cleared **immediately** in `OnGameSelected`, but we're still on the login screen. When the login video ends, `ResumeMusic()` is called, and it doesn't know that we're still in the "first select" window.

**The problem:** `_firstSelect` is a boolean flag that gets cleared too early. We need to track whether we've actually reached the library view yet.

### Comparison with PlayniteSound

Looking at PlayniteSound's logs, it has the same behavior - `_firstSelect` is cleared immediately. However, **PlayniteSound has `SkipFirstSelectMusic: False`** in its settings, so it intentionally plays music even on first select.

From PlayniteSound.log:
```
[2025-11-29 16:01:23.635] OnGameSelected - SkipSound: True, SkipMusic: False
[2025-11-29 16:01:23.646] ShouldPlayAudio - FirstSelect: True, SkipFirstSelectMusic: False, Result: True
```

**Key Difference:** PlayniteSound's `SkipFirstSelectMusic` setting is **False**, so it plays music on first select intentionally. UniPlaySong's `SkipFirstSelectionAfterModeSwitch` is **True**, so it should skip, but the ResumeMusic() path bypasses this check.

---

## The Fix

### Solution 1: Don't Clear _firstSelect Until Library View is Reached

Instead of clearing `_firstSelect` immediately in `OnGameSelected`, we should only clear it when:
1. We've actually reached the library view (ActiveView = "Library"), OR
2. A second OnGameSelected fires (meaning user has interacted with library)

### Solution 2: Check _firstSelect in ResumeMusic() Before Playing

In `ResumeMusic()`, if `_firstSelect` is `True` and `SkipFirstSelectionAfterModeSwitch` is `True`, don't call `PlayMusicBasedOnSelected()`.

However, this won't work because `_firstSelect` is already `False` by the time `ResumeMusic()` is called.

### Solution 3: Track "Login Screen State" Separately

Add a new flag `_isLoginScreen` that tracks whether we're still on the login screen. This flag should only be cleared when:
- ActiveView changes to "Library", OR
- We've had at least one OnGameSelected after the library view is ready

### Recommended Solution: Solution 1 + Solution 3 Combined

1. **Don't clear `_firstSelect` in `OnGameSelected`** - Instead, only clear it when we detect we've reached the library view
2. **Add `_isLoginScreen` flag** - Track login screen state separately
3. **In `ResumeMusic()`** - Check both flags before playing music

---

## Implementation Plan

### Step 1: Add Login Screen Detection

```csharp
private bool _isLoginScreen = true; // Track if we're still on login screen

// In OnMainModelChanged, when ActiveView changes:
if (activeViewName == "Library" || activeViewName == "Grid" || activeViewName == "List")
{
    _isLoginScreen = false;
    _firstSelect = false; // Now safe to clear
}
```

### Step 2: Modify ResumeMusic()

```csharp
private void ResumeMusic()
{
    // ... existing code ...
    
    // Don't play music if we're still on login screen and should skip first select
    if (_isLoginScreen && settings.SkipFirstSelectionAfterModeSwitch)
    {
        _fileLogger?.Info($"ResumeMusic() - Skipping (still on login screen, SkipFirstSelectionAfterModeSwitch: True)");
        return;
    }
    
    // ... rest of ResumeMusic logic ...
}
```

### Step 3: Modify OnGameSelected

```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    // ... existing code ...
    
    // Don't clear _firstSelect here - wait until we reach library view
    // Only clear if we're NOT on login screen
    if (!_isLoginScreen)
    {
        _firstSelect = false;
    }
    
    // ... rest of OnGameSelected logic ...
}
```

---

## Key Insights

1. **OnGameSelected IS working** - It correctly skips music on first select
2. **The bug is in ResumeMusic()** - It doesn't respect the login screen state
3. **Timing issue** - `_firstSelect` is cleared too early, before we've actually left the login screen
4. **PlayniteSound works differently** - It has `SkipFirstSelectMusic: False`, so it intentionally plays music on first select

---

## Next Steps

1. Implement login screen detection in `OnMainModelChanged`
2. Modify `ResumeMusic()` to check login screen state
3. Modify `OnGameSelected` to only clear `_firstSelect` when not on login screen
4. Test with fullscreen login screen startup
5. Verify music doesn't play until library view is reached
