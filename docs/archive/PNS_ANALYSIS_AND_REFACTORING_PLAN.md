# PNS Analysis and Refactoring Plan

**Date**: 2025-12-06  
**Status**: 🔍 **ANALYSIS COMPLETE** - Ready for refactoring

---

## Why PNS Didn't Use BackgroundMusic Property

### Key Finding: PNS Only Suppresses, Never Plays Through BackgroundMusic

**Evidence from `PlayniteSounds.cs` (lines 302-320)**:
```csharp
private void SupressNativeFulscreenMusic()
{
    // PNS only uses BackgroundMusic to SUPPRESS Playnite's native music
    // They never play their own music through it
    dynamic backgroundMusicProperty = GetMainModel().App
        .GetType()
        .GetProperty("BackgroundMusic");
    
    IntPtr currentMusic = (IntPtr)backgroundMusicProperty.GetValue(null);
    if (currentMusic != new IntPtr(0))
    {
        // stop music
        SDL_mixer.Mix_HaltMusic();
        SDL_mixer.Mix_FreeMusic(currentMusic);
        backgroundMusicProperty.GetSetMethod(true).Invoke(null, new[] { new IntPtr(0) as object});
    }
}
```

**Why PNS didn't use BackgroundMusic for playback:**
1. **Control**: They need full control over playback (pause/resume, volume, fade)
2. **Position Tracking**: They manually track position (`_backgroundMusicPausedOnTime`) for resume
3. **Flexibility**: Their own player allows seeking, custom fade logic, etc.
4. **Reliability**: Playnite's BackgroundMusic might be reset or overridden by Playnite itself

---

## PNS's Single-Player Architecture

### Core Pattern: ONE Player, Multiple States

**PNS uses a single `_musicPlayer` for everything:**
- Game music
- Platform music  
- Filter music
- Background/backup music

**State Tracking (lines 2818-2820)**:
```csharp
private bool _isPlayingBackgroundMusic = false;
private string _lastBackgroundMusicFileName = null;
private TimeSpan _backgroundMusicPausedOnTime = default;
```

**Key Methods:**

1. **PauseBackgroundMusic()** (lines 966-973):
```csharp
private void PauseBackgroundMusic()
{
    if (_isPlayingBackgroundMusic)
    {
        _backgroundMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
        _isPlayingBackgroundMusic = false;
    }
}
```

2. **PlayBackgroundMusic()** (lines 974-1007):
```csharp
private void PlayBackgroundMusic(List<string> musicFiles)
{
    if (_isPlayingBackgroundMusic && !_musicEnded)
    {
        return; // Already playing
    }

    if (_isPlayingBackgroundMusic && _musicEnded)
    {
        _lastBackgroundMusicFileName = PlayMusicFromFiles(musicFiles);
        return;
    }

    _isPlayingBackgroundMusic = true;

    if (string.IsNullOrEmpty(_lastBackgroundMusicFileName) || _backgroundMusicPausedOnTime == default)
    {
        // First time or no saved position - start fresh
        _lastBackgroundMusicFileName = PlayMusicFromFiles(musicFiles);
    }
    else if (_lastBackgroundMusicFileName != _prevMusicFileName)
    {
        // Different file - switch with fade
        Try(() => _musicFader?.Switch(
            SubCloseMusic,
            () => PreloadMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime),
            () => SubPlayMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime)
        ));
    }
    else
    {
        // Same file - resume from saved position
        Try(() => _musicFader?.Switch(null, null, 
            () => SubPlayMusicFromPath(_lastBackgroundMusicFileName, _backgroundMusicPausedOnTime)));
    }
}
```

3. **SubPlayMusicFromPath()** (lines 1201-1214):
```csharp
private void SubPlayMusicFromPath(string filePath, TimeSpan startFrom = default)
{
    ReloadMusic = false;
    _prevMusicFileName = string.Empty;
    if (File.Exists(filePath))
    {
        _prevMusicFileName = filePath;
        _musicPlayer.Load(filePath);
        _musicPlayer.Play(startFrom); // Resume from saved position
        _musicEnded = false;
        SettingsModel.Settings.CurrentMusicName = Path.GetFileNameWithoutExtension(filePath);
    }
}
```

---

## UniPSong's Current Dual-Player Architecture

### Problems with Current Approach

**Current State (MusicPlaybackService.cs)**:
- TWO players: `_musicPlayer` (game) and `_defaultMusicPlayer` (default)
- Complex state: `_isPaused`, `_defaultMusicIsPaused`, `_currentSongPath`, `_defaultMusicPath`
- Complex switching logic (lines 202-340)
- Position tracking issues (SDL2_mixer doesn't support seeking)

**Issues:**
1. **State Synchronization**: Easy to get out of sync between two players
2. **Complex Logic**: 300+ lines of conditional logic for player switching
3. **Position Tracking**: SDL2_mixer can't seek, so position is lost
4. **Fade Management**: Two separate faders, complex coordination
5. **Edge Cases**: Many edge cases around player state

---

## Refactoring Plan: Single-Player Pattern (Like PNS)

### Goal: Simplify to ONE Player with State Tracking

**Benefits:**
1. ✅ Simpler state management (one player, one fader)
2. ✅ Easier to reason about (no dual-player coordination)
3. ✅ Matches PNS's proven pattern
4. ✅ Fewer edge cases

**Trade-offs:**
- ❌ SDL2_mixer doesn't support seeking, so position is lost (same as current)
- ❌ Need to reload default music when resuming (but this is acceptable)

### Implementation Strategy

#### Step 1: Remove Dual-Player System

**Remove:**
- `_defaultMusicPlayer`
- `_defaultMusicFader`
- `_defaultMusicIsPaused`
- `_defaultMusicPath`

**Add (like PNS):**
- `_isPlayingDefaultMusic` (bool flag)
- `_lastDefaultMusicPath` (string)
- `_defaultMusicPausedOnTime` (TimeSpan) - for future if we switch to WMP

#### Step 2: Simplify State Tracking

**Current:**
```csharp
public bool IsPlaying => (_musicPlayer?.IsActive ?? false) || (_defaultMusicPlayer?.IsActive ?? false);
public bool IsPaused => _isPaused || _defaultMusicIsPaused;
public bool IsLoaded => (_musicPlayer?.IsLoaded ?? false) || (_defaultMusicPlayer?.IsLoaded ?? false);
```

**Refactored:**
```csharp
public bool IsPlaying => _musicPlayer?.IsActive ?? false;
public bool IsPaused => _isPaused;
public bool IsLoaded => _musicPlayer?.IsLoaded ?? false;
```

#### Step 3: Simplify PlayGameMusic() Logic

**Current Flow (complex):**
1. Check if default music → use `_defaultMusicPlayer`
2. Check if game music → use `_musicPlayer`
3. Handle switching between players
4. Manage two separate faders

**Refactored Flow (simple, like PNS):**
1. Add default music to songs list if no game music (lines 139-145) ✅ Already done
2. Select song (could be game music or default music)
3. If switching from default music to game music → pause and save state
4. If switching to default music → check if paused, resume if same file
5. Use single player for everything

#### Step 4: Implement PNS-Style Pause/Resume

**PauseDefaultMusic()** (when switching to game music):
```csharp
private void PauseDefaultMusic(UniPlaySongSettings settings)
{
    if (_isPlayingDefaultMusic && 
        _currentSongPath == settings?.DefaultMusicPath &&
        _musicPlayer?.IsLoaded == true)
    {
        // Save state (for future if we add seeking support)
        // _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
        _isPlayingDefaultMusic = false;
        
        // Fade out and pause
        _fader.Pause();
        _isPaused = true;
    }
}
```

**ResumeDefaultMusic()** (when switching back to default music):
```csharp
private void ResumeDefaultMusic(string defaultMusicPath)
{
    if (!_isPlayingDefaultMusic && 
        _lastDefaultMusicPath == defaultMusicPath &&
        _isPaused)
    {
        // Same file - resume from paused state
        _fader.Resume();
        _isPaused = false;
        _isPlayingDefaultMusic = true;
    }
    else
    {
        // Different file or first time - load and play
        _lastDefaultMusicPath = defaultMusicPath;
        _isPlayingDefaultMusic = true;
        
        // Load and play with fade-in
        _musicPlayer.Load(defaultMusicPath);
        _musicPlayer.Volume = 0;
        _musicPlayer.Play();
        _fader.FadeIn();
    }
}
```

#### Step 5: Simplify PlayGameMusic() Method

**Key Changes:**
1. Remove all `_defaultMusicPlayer` checks
2. Remove dual-player switching logic
3. Use single `_musicPlayer` for everything
4. Track default music state with flags (like PNS)

**Simplified Logic:**
```csharp
// 1. Add default music to songs if needed (already done, lines 139-145)

// 2. Select song (could be game or default)
string songToPlay = SelectSongToPlay(game, songs, isNewGame);

// 3. Check if this is default music
bool isDefaultMusic = settings?.EnableDefaultMusic == true && 
                      !string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                      songToPlay == settings.DefaultMusicPath;

// 4. If switching FROM default music TO game music
bool wasDefaultMusic = _isPlayingDefaultMusic && 
                       _currentSongPath == settings?.DefaultMusicPath;
if (wasDefaultMusic && !isDefaultMusic)
{
    PauseDefaultMusic(settings);
}

// 5. If switching TO default music
if (isDefaultMusic)
{
    ResumeDefaultMusic(songToPlay);
    _currentSongPath = songToPlay;
    return;
}

// 6. Play game music (normal flow)
// ... existing game music logic ...
```

---

## Comparison: Before vs After

### Before (Dual-Player)
- **Lines of Code**: ~614 lines
- **State Variables**: 8+ (two players, two faders, multiple flags)
- **Complexity**: High (dual-player coordination)
- **Edge Cases**: Many (player state sync issues)

### After (Single-Player, PNS Pattern)
- **Lines of Code**: ~450 lines (estimated 25% reduction)
- **State Variables**: 5 (one player, one fader, simple flags)
- **Complexity**: Low (single player, clear state)
- **Edge Cases**: Fewer (no dual-player sync)

---

## Implementation Checklist

- [ ] Remove `_defaultMusicPlayer` and `_defaultMusicFader` fields
- [ ] Remove `_defaultMusicIsPaused` and `_defaultMusicPath` fields
- [ ] Add `_isPlayingDefaultMusic`, `_lastDefaultMusicPath`, `_defaultMusicPausedOnTime` fields
- [ ] Simplify `IsPlaying`, `IsPaused`, `IsLoaded` properties
- [ ] Remove `CreateDefaultMusicPlayer()` method
- [ ] Simplify `PlayGameMusic()` method (remove dual-player logic)
- [ ] Add `PauseDefaultMusic()` method (PNS pattern)
- [ ] Add `ResumeDefaultMusic()` method (PNS pattern)
- [ ] Simplify `Pause()`, `Resume()`, `Stop()`, `FadeOutAndStop()` methods
- [ ] Update `SetVolume()` to use single player
- [ ] Remove default music player MediaEnded handler
- [ ] Test: Game with music → Game with no music (default resumes)
- [ ] Test: Game with no music → Game with music (default pauses)
- [ ] Test: Multiple games with no music (default continues)
- [ ] Test: Fade-out smoothness
- [ ] Test: Fade-in smoothness

---

## Key Insights from PNS

1. **Single Player is Simpler**: One player, one fader, clear state
2. **State Flags Work**: `_isPlayingBackgroundMusic` is sufficient
3. **Position Tracking Optional**: PNS tracks position, but SDL2 can't seek anyway
4. **File Path Tracking**: `_lastBackgroundMusicFileName` tracks what was playing
5. **Resume Logic**: Check if same file, resume if paused; otherwise load fresh

---

## Next Steps

1. **Review this plan** with user
2. **Implement refactoring** step by step
3. **Test thoroughly** with all scenarios
4. **Document changes** in CHANGELOG

---

**Last Updated**: 2025-12-06  
**Status**: Ready for implementation


