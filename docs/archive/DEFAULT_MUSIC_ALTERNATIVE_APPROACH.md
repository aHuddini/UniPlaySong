# Default Music - Alternative Approach Analysis

**Date**: 2025-12-06  
**Status**: 🔍 **ANALYSIS** - Proposed alternative to dual-player system

---

## Current Approach (Dual-Player System)

**Implementation**:
- Separate `_defaultMusicPlayer` instance for default music
- Complex pause/resume logic with position tracking
- Manual fade-out/fade-in management
- Issues with position preservation and fade behavior

**Problems**:
- Music restarts instead of resuming
- Abrupt fade-out (not smooth)
- Complex state management between two players
- Position tracking issues

---

## Proposed Alternative: Leverage Playnite's BackgroundMusic

### Core Concept

Instead of creating a separate default music player, **replace Playnite's BackgroundMusic property** with our default music file. Then:

1. **When game has music**: Suppress/pause Playnite's BackgroundMusic (as we do now)
2. **When game has no music**: Let Playnite's BackgroundMusic play (which is our default music)
3. **Playnite handles pause/resume**: Built-in position preservation

### How It Works

**Current Suppression** (line 402-434 in `UniPlaySong.cs`):
```csharp
// We currently SET BackgroundMusic to IntPtr.Zero (suppress)
bgMusicProp.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
```

**Proposed Replacement**:
```csharp
// Instead of suppressing, LOAD our default music into BackgroundMusic
if (settings?.EnableDefaultMusic == true && File.Exists(settings.DefaultMusicPath))
{
    IntPtr defaultMusic = SDL2Mixer.Mix_LoadMUS(settings.DefaultMusicPath);
    bgMusicProp.GetSetMethod(true)?.Invoke(null, new object[] { defaultMusic });
    // Playnite will handle playing/pausing this automatically
}
```

### Benefits

1. **Simpler Architecture**: No dual-player system needed
2. **Automatic Position Preservation**: Playnite handles pause/resume
3. **No Manual Fade Management**: Playnite's background music system handles transitions
4. **Less State Tracking**: No need to track `_defaultMusicIsPaused`, `_defaultMusicPath`, etc.
5. **Leverages Existing System**: Uses Playnite's built-in background music infrastructure

### Implementation Strategy

#### Step 1: Replace Suppression with Replacement
```csharp
private void SetupDefaultMusic()
{
    if (!IsFullscreen) return;
    if (_settings?.SuppressPlayniteBackgroundMusic != true) return;
    if (!_settings?.EnableDefaultMusic == true) return;
    if (string.IsNullOrWhiteSpace(_settings.DefaultMusicPath)) return;
    if (!File.Exists(_settings.DefaultMusicPath)) return;

    try
    {
        var mainModel = GetMainModel();
        if (mainModel == null) return;

        dynamic bgMusicProp = mainModel.App?.GetType().GetProperty("BackgroundMusic");
        if (bgMusicProp == null) return;

        // Load our default music into Playnite's BackgroundMusic
        IntPtr defaultMusic = SDL2Mixer.Mix_LoadMUS(_settings.DefaultMusicPath);
        if (defaultMusic != IntPtr.Zero)
        {
            // Free existing background music if any
            IntPtr currentMusic = (IntPtr)bgMusicProp.GetValue(null);
            if (currentMusic != IntPtr.Zero)
            {
                SDL2Mixer.Mix_HaltMusic();
                SDL2Mixer.Mix_FreeMusic(currentMusic);
            }
            
            // Set our default music as Playnite's background music
            bgMusicProp.GetSetMethod(true)?.Invoke(null, new object[] { defaultMusic });
            _fileLogger?.Info($"Replaced Playnite background music with default music: {_settings.DefaultMusicPath}");
        }
    }
    catch (Exception ex)
    {
        _fileLogger?.Error($"Error setting up default music: {ex.Message}", ex);
    }
}
```

#### Step 2: Modify SuppressNativeMusic
```csharp
private void SuppressNativeMusic()
{
    if (!IsFullscreen) return;
    
    // If default music is enabled, we've already replaced BackgroundMusic
    // Just need to pause it when game music plays
    if (_settings?.EnableDefaultMusic == true && 
        !string.IsNullOrWhiteSpace(_settings.DefaultMusicPath))
    {
        // Pause Playnite's background music (which is our default music)
        SDL2Mixer.Mix_PauseMusic();
        _fileLogger?.Info("Paused Playnite background music (default music)");
        return;
    }
    
    // Otherwise, suppress as before
    if (_settings?.SuppressPlayniteBackgroundMusic != true) return;
    
    // ... existing suppression code ...
}
```

#### Step 3: Resume When No Game Music
```csharp
// In MusicPlaybackService.PlayGameMusic() - when songs.Count == 0:
if (songs.Count == 0 && settings?.EnableDefaultMusic == true)
{
    // Resume Playnite's background music (which is our default music)
    SDL2Mixer.Mix_ResumeMusic();
    _fileLogger?.Info("Resumed Playnite background music (default music)");
    return;
}
```

### Key Questions to Investigate

1. **Does Playnite automatically play BackgroundMusic?**
   - Need to verify if setting BackgroundMusic automatically starts playback
   - May need to trigger Playnite's internal playback mechanism

2. **Does Playnite preserve position on pause/resume?**
   - SDL2_mixer's `Mix_PauseMusic()`/`Mix_ResumeMusic()` should preserve position
   - But need to verify Playnite doesn't interfere

3. **Volume Control**
   - How does Playnite control BackgroundMusic volume?
   - May need to sync with our volume settings

4. **When to Replace BackgroundMusic**
   - On extension startup?
   - On fullscreen mode switch?
   - Only once, or every time?

5. **Cleanup**
   - When extension unloads, should we restore original BackgroundMusic?
   - Or just leave our default music loaded?

### Comparison with PNS Approach

**PNS (PlayniteSound)**:
- Uses `_isPlayingBackgroundMusic` flag
- Manually tracks `_lastBackgroundMusicFileName` and `_backgroundMusicPausedOnTime`
- Uses their own music player, not Playnite's BackgroundMusic
- Calls `PauseBackgroundMusic()` which saves position manually
- Calls `PlayBackgroundMusic()` which resumes from saved position

**Our Proposed Approach**:
- Leverage Playnite's BackgroundMusic property directly
- Let Playnite handle pause/resume automatically
- No manual position tracking needed
- Simpler state management

### Potential Issues

1. **Playnite May Override**: Playnite might reset BackgroundMusic on certain events
2. **Volume Control**: May need to sync volume with Playnite's background volume setting
3. **Initialization Timing**: Need to ensure BackgroundMusic is set before Playnite tries to play it
4. **Multiple Extensions**: If other extensions also manipulate BackgroundMusic, conflicts may occur

### Next Steps

1. **Research**: Investigate how Playnite uses BackgroundMusic property
   - When does it start playing?
   - How does it handle pause/resume?
   - What events trigger it?

2. **Prototype**: Create a minimal test to verify:
   - Can we load music into BackgroundMusic?
   - Does Playnite automatically play it?
   - Does pause/resume preserve position?

3. **Compare**: Review PNS code more carefully to see why they didn't use this approach
   - There may be technical limitations we're not aware of

---

## Recommendation

This approach is **conceptually simpler** and could eliminate the dual-player complexity. However, we need to:

1. Verify Playnite's BackgroundMusic behavior
2. Test if position preservation works automatically
3. Understand any limitations or edge cases

If this approach works, it would be a **significant simplification** of the default music feature.

---

**Last Updated**: 2025-12-06  
**Status**: Awaiting investigation and prototyping


