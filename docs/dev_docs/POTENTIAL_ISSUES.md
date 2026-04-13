# Potential Issues

Known edge cases and deferred fixes that may need attention in future versions.

## Radio Mode Pause Check in PlayGameMusic

**Status:** Deferred (monitoring)
**Discovered:** v1.3.12
**Related Fixes:** ThemeOverlay preserved in ClearAllPauseSources (Fix 1, applied), OnApplicationStarted pause check (Fix 3, applied)

### Problem

The Radio Mode entry point inside `PlayGameMusic()` in `MusicPlaybackService.cs` does not check `_isPaused` before calling `StartRadioPlayback()`. If a game selection triggers `PlayGameMusic()` while pause sources are active, Radio Mode could start playback during a paused state.

### Current code (lines ~551-558)

```csharp
if (settings?.RadioModeEnabled == true && !forceReload)
{
    if (!_isInRadioMode || !IsPlaying)
        StartRadioPlayback(settings);
    else
        _fileLogger?.Debug($"RadioMode: ignoring game switch...");
    return;
}
```

### Why It's Not Fixed Yet

The two applied fixes cover the actual scenarios that trigger Radio Mode during login/welcome screens:

- **Fix 1** (ClearAllPauseSources preserves ThemeOverlay) — prevents `HandleGameSelected` → `Stop()` from wiping ThemeOverlay, keeping `_isPaused` true
- **Fix 3** (OnApplicationStarted checks `IsPaused`) — the direct `StartRadioPlayback()` call in `UniPlaySong.cs` line 636 now checks `_playbackService?.IsPaused != true` before starting

Together, Fix 1 + Fix 3 prevent Radio Mode from playing during login/welcome hub screens. The `PlayGameMusic()` path (Fix 2) was tested in isolation and didn't resolve the issue alone because the `OnApplicationStarted` direct call was the actual culprit.

### When This Could Become a Problem

- A code path calls `PlayGameMusic()` with `RadioModeEnabled` while pause sources are active but AFTER `OnApplicationStarted` has already fired (e.g., a settings change re-enables Radio Mode mid-session while a theme overlay is active)

### Fix If Needed

Add `_isPaused` check to the Radio Mode block in `PlayGameMusic()`:

```csharp
if (settings?.RadioModeEnabled == true && !forceReload)
{
    if (_isPaused)
        _fileLogger?.Debug($"RadioMode: not starting — paused ({string.Join(", ", _activePauseSources)})");
    else if (!_isInRadioMode || !IsPlaying)
        StartRadioPlayback(settings);
    else
        _fileLogger?.Debug($"RadioMode: ignoring game switch...");
    return;
}
```
