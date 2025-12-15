# Default Music Implementation - Technical Documentation

**Date**: 2025-12-07  
**Status**: ✅ Complete  
**Version**: 1.0.4

---

## Overview

Default music provides a fallback music experience when games have no music files. The implementation preserves playback position when switching between games, allowing seamless continuation of the default music track.

---

## Architecture

### Single-Player Approach

The implementation uses a **single-player architecture** (simplified from an earlier dual-player approach):

- **One `IMusicPlayer` instance** handles both game music and default music
- **State tracking flags** distinguish between game music and default music
- **Position preservation** saves/restores playback position for default music

### State Management

```csharp
// State tracking for default music
private bool _isPlayingDefaultMusic = false;
private string _lastDefaultMusicPath = null;
private TimeSpan _defaultMusicPausedOnTime = default;
```

**State Variables**:
- `_isPlayingDefaultMusic`: Tracks when default music is currently active
- `_lastDefaultMusicPath`: Stores the path of the last default music file
- `_defaultMusicPausedOnTime`: Saves the playback position when pausing

---

## Position Preservation

### How It Works

1. **When switching from default music to game music**:
   - Current position is saved in `_defaultMusicPausedOnTime`
   - Music fades out using `Switch()` pattern
   - Position is logged for debugging

2. **When switching back to default music**:
   - If same file and paused: Resume from saved position
   - If different file or first time: Load and play from saved position (or beginning)

3. **When switching between games with no music**:
   - Default music continues playing (no restart)
   - Position is preserved automatically

### Implementation Details

**Position-Based Playback**:
- Added `Play(TimeSpan startFrom)` method to `IMusicPlayer` interface
- Implemented in both `SDL2MusicPlayer` and `MusicPlayer` (WPF)
- SDL2 uses `Mix_SetMusicPosition()` for seeking
- WPF uses `Clock.Controller?.Seek()` for seeking

**Position Saving**:
```csharp
// Save position before pausing/closing
_defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
```

**Position Restoring**:
```csharp
// Play from saved position
_musicPlayer.Play(_defaultMusicPausedOnTime);
```

---

## Fade Transitions

### Unified Fade Pattern

All music switching (game→game, game→default, default→game) uses the same `Switch()` pattern:

```csharp
_fader.Switch(
    stopAction: () => { /* Close current player */ },
    preloadAction: () => { /* Preload new music */ },
    playAction: () => { /* Load and play new music */ }
);
```

**Benefits**:
- Consistent fade behavior across all transitions
- Smooth fade-out/fade-in for all music types
- Preloading during fade-out eliminates gaps

### Transition Types

1. **Game Music → Default Music**:
   - Fades out game music
   - Preloads default music during fade-out
   - Fades in default music from saved position

2. **Default Music → Game Music**:
   - Saves default music position
   - Fades out default music
   - Preloads game music during fade-out
   - Fades in game music

3. **Default Music → Default Music** (switching between games with no music):
   - Continues playing (no fade needed)
   - Position preserved automatically

---

## Code Flow

### PlayGameMusic() Method

1. **Check for available songs** (game music)
2. **If no game music and default music enabled**:
   - Add default music to songs list
3. **Select song to play** (could be game or default)
4. **If default music**:
   - Check if already playing → continue
   - Check if paused → resume
   - Otherwise → load and play from saved position
5. **If game music**:
   - Save default music position if switching from default
   - Use `Switch()` for fade-out/fade-in

### Key Methods

**PauseDefaultMusic()**:
- Saves current position
- Marks default music as not playing
- Used when switching to game music

**ResumeDefaultMusic()**:
- Checks if can resume (same file, paused, loaded)
- Resumes from saved position if possible
- Otherwise loads and plays from saved position

---

## Player Backend Support

### SDL2 Music Player

- Uses `Mix_GetMusicPosition()` to get current position
- Uses `Mix_SetMusicPosition()` to seek to position
- Position is in seconds (double)

### WPF Media Player

- Uses `Clock.CurrentTime` to get current position
- Uses `Clock.Controller?.Seek()` to seek to position
- Position is `TimeSpan`

---

## Logging

The implementation includes detailed logging for debugging:

- Position saving: `"Pausing default music at position X.XXs"`
- Position restoring: `"Resuming paused default music from position X.XXs"`
- State checks: Shows current song path, playing state, pause state
- Transition logging: `"Switching from game music to default music with fade-out"`

---

## Testing

### Test Scenarios

1. **Default music plays when no game music**:
   - Select game with no music → default music should start

2. **Position preservation**:
   - Let default music play for ~30 seconds
   - Switch to game with music → default music pauses
   - Switch back to game without music → default music resumes from ~30 seconds

3. **Smooth transitions**:
   - Game music → Default music: Should fade out/in smoothly
   - Default music → Game music: Should fade out/in smoothly

4. **Switching between games with no music**:
   - Default music should continue playing (no restart)

---

## Files Modified

- `UniPSong/Services/IMusicPlayer.cs` - Added `Play(TimeSpan startFrom)` method
- `UniPSong/Services/SDL2MusicPlayer.cs` - Implemented position-based playback
- `UniPSong/Services/MusicPlayer.cs` - Implemented position-based playback
- `UniPSong/Services/MusicPlaybackService.cs` - Position preservation logic

---

## Future Enhancements

Potential improvements (not currently implemented):
- Position preservation for game music (not just default music)
- Playlist support for default music
- Separate volume control for default music
- Platform-specific default music

---

**Last Updated**: 2025-12-07

