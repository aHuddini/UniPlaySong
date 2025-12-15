# UniPlaySong Architecture

## Overview

UniPlaySong provides a console-like game music preview experience for Playnite. When users browse their game library, music associated with each game plays automatically.

## Core Components

### UniPlaySong.cs (Main Plugin)
- Entry point and Playnite event handling
- `OnGameSelected` - Triggers music playback when game selection changes
- `OnLoginDismissKeyPress` - Handles login screen dismissal for theme compatibility
- `OnMainModelChanged` - Detects view changes for login screen handling
- `ShouldPlayMusic()` - Central gatekeeper for playback decisions

### MusicPlaybackService.cs
- High-level music management
- Coordinates between fader, player, and file service
- Handles game-to-song mapping and primary song logic
- Initial playback (no fade) vs. switching (with fade)

### MusicPlayer.cs / SDL2MusicPlayer.cs
- **SDL2MusicPlayer** (Primary): SDL2-based audio player for reliable volume control
  - Native library - no WPF threading issues
  - Works consistently in fullscreen mode
  - Synchronous volume changes
- **MusicPlayer** (Fallback): Low-level WPF MediaPlayer wrapper
  - Used when SDL2 unavailable
  - **Dual-player preloading system** (like PlayniteSound):
    - `_mediaPlayer` - Current playing media
    - `_preloadedMediaPlayer` - Next song ready to swap in
    - `PreLoad(path)` - Loads into second player during fade out
    - `Load(path)` - Swaps to preloaded player or loads fresh
  - Uses `MediaTimeline`/`Clock` for reliable playback control

### MusicFader.cs
- Smooth volume transitions with exponential fade curves
- Matches PlayniteSound's implementation with improvements:
  - Uses `Dispatcher.Invoke` (blocking) for reliable timing
  - Exponential curves: `progress²` for fade-in, `1-(1-progress)²` for fade-out
  - Time-based calculation for predictable fades
  - Action queue: preloadAction → stopAction → playAction
- Supports separate fade-in and fade-out durations
- Optimized timer: 16ms (60 FPS) for smoother transitions

### MediaElementsMonitor.cs
- Detects video playback in Playnite UI
- Sets `VideoIsPlaying` to pause/resume music
- Monitors `MediaElement` controls in theme

## Data Flow

### Normal Game Selection
```
OnGameSelected(game)
  → ShouldPlayMusic() check
  → PlayMusicForGame(game)
    → MusicPlaybackService.PlayGameMusic(game)
      → If already playing:
          → Fader.Switch(close, preload, play)
            → During fade out: PreLoad() creates second player
            → On complete: Load() swaps players, Play() starts
      → If not playing:
          → Load() + Play() immediately at full volume
```

### Login Screen Flow (Input-Based Skip)
```
App starts in Fullscreen
  → First OnGameSelected: _loginSkipActive = true, return
  → User sees login screen (no music)
  → User presses Enter/Space/Escape/Return (or controller button)
    → OnLoginDismissKeyPress
      → _loginSkipActive = false
      → 150ms delay
      → PlayMusicBasedOnSelected()
```

### Video Detected (Trailer Playing)
```
MediaElementsMonitor detects video
  → Settings.VideoIsPlaying = true
  → OnSettingsChanged fires
    → PauseMusic()
      → Fader.Pause() - fades out then pauses

Video ends
  → Settings.VideoIsPlaying = false
  → ResumeMusic()
    → Fader.Resume() - fades in
```

## Key Design Decisions

### Why Dual-Player Preloading?
Loading a media file takes 100-500ms. By creating a second MediaPlayer and loading the next song during fade out, the load time is hidden. When fade completes, we just swap pointers - instant!

### Why Invoke Instead of BeginInvoke?
`BeginInvoke` (non-blocking) can cause timing issues where multiple timer ticks queue up. `Invoke` (blocking) ensures each tick completes before the next, matching PlayniteSound's reliable behavior.

### Why Match PlayniteSound Exactly?
PlayniteSound's fader is battle-tested. We match it exactly, then extend with features like separate fade-in/out durations.

## File Structure

```
UniPSong/
├── UniPlaySong.cs              # Main plugin
├── UniPlaySongSettings.cs      # Settings model
├── UniPlaySongSettingsView.xaml # Settings UI
├── Services/
│   ├── IMusicPlaybackService.cs
│   ├── MusicPlaybackService.cs # High-level playback
│   ├── IMusicPlayer.cs
│   ├── MusicPlayer.cs          # WPF MediaPlayer (fallback)
│   ├── SDL2MusicPlayer.cs      # SDL2 player (primary)
│   ├── GameMusicFileService.cs # File management
│   └── PrimarySongManager.cs   # Primary song tracking
├── Players/
│   └── MusicFader.cs           # Fade transitions with exponential curves
├── Monitors/
│   ├── MediaElementsMonitor.cs # Video detection
│   └── WindowMonitor.cs        # Window tracking
├── Downloaders/                # YouTube download support
├── Menus/                      # Context menu handlers
└── Common/
    └── FileLogger.cs           # Debug logging
```

