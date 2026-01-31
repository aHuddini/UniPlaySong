# UniPlaySong - Technical Reference

## Overview

This document provides detailed technical references for key variables, logic, and feature implementations in UniPlaySong. Use this as a reference when debugging, extending, or modifying the codebase.

## Table of Contents

1. [Music Playback State Variables](#music-playback-state-variables)
2. [Native Music Suppression](#native-music-suppression)
3. [Primary Song Selection](#primary-song-selection)
4. [Song Randomization Logic](#song-randomization-logic)
5. [Default Music Fallback](#default-music-fallback)
6. [Skip Logic and State Management](#skip-logic-and-state-management)
7. [Preview Mode](#preview-mode)
8. [Volume and Fade Control](#volume-and-fade-control)
9. [Controller Input Handling](#controller-input-handling)
10. [Audio Normalization](#audio-normalization)
11. [Download Manager](#download-manager)
12. [Theme Integration (UPS_MusicControl)](#theme-integration-ups_musiccontrol)

---

## Music Playback State Variables

### MusicPlaybackService State

**Location**: `Services/MusicPlaybackService.cs`

#### Core State Variables

```csharp
// Current playback state
private string _currentGameId;              // Currently selected game ID (Guid.ToString())
private string _currentSongPath;            // Full path to currently playing song
private string _previousSongPath;           // Last played song (for randomization avoidance)
private Game _currentGame;                   // Current game object (for randomization on song end)
private bool _isPaused;                     // Whether playback is paused

// Default music state (PNS-style position preservation)
private bool _isPlayingDefaultMusic;        // Whether default music is currently playing
private string _lastDefaultMusicPath;       // Path to last default music file
private TimeSpan _defaultMusicPausedOnTime; // Saved position when default music was paused

// Preview mode state
private DateTime _songStartTime;            // When current song started (for preview mode)
private UniPlaySongSettings _currentSettings; // Current settings reference
private System.Windows.Threading.DispatcherTimer _previewTimer; // Timer for preview mode

// Primary song tracking
private Dictionary<string, bool> _primarySongPlayed; // Tracks if primary song played per directory

// Fade control
private MusicFader _fader;                 // Volume fade controller
private double _targetVolume;               // Target volume (0.0-1.0)
private double _fadeInDuration;             // Fade-in duration in seconds
private double _fadeOutDuration;            // Fade-out duration in seconds
```

#### State Transitions

**Game Selection:**
```
_currentGameId = null → game.Id.ToString()
_currentSongPath = null → selected song path
_isPaused = false (when new song starts)
```

**Default Music:**
```
_isPlayingDefaultMusic = false → true (when default music starts)
_defaultMusicPausedOnTime = saved position (when switching to game music)
```

**Preview Mode:**
```
_songStartTime = DateTime.MinValue → DateTime.Now (when song starts)
_previewTimer.Start() (if preview mode enabled)
```

### MusicPlaybackCoordinator State

**Location**: `Services/MusicPlaybackCoordinator.cs`

```csharp
// Skip logic state
private bool _firstSelect;                  // First game selection flag
private bool _loginSkipActive;              // Login skip is active
private bool _skipFirstSelectActive;        // Skip first select window is active
private bool _hasSeenFullscreen;            // Track if fullscreen mode entered
private Game _currentGame;                  // Currently selected game
```

#### State Flow

**First Selection:**
```
_firstSelect = true (initial state)
→ HandleGameSelected() called
→ If SkipFirstSelectionAfterModeSwitch: _skipFirstSelectActive = true
→ If ThemeCompatibleSilentSkip: _loginSkipActive = true
→ _firstSelect = false (after processing)
```

**Login Dismiss:**
```
_loginSkipActive = true
→ HandleLoginDismiss() called
→ _loginSkipActive = false
→ _skipFirstSelectActive = false
→ _firstSelect = false
→ Music starts playing
```

---

## Native Music Suppression

### Implementation

**Location**: `UniPlaySong.cs` (lines 618-693)

### Key Variables

```csharp
// Native music suppression state
private System.Windows.Threading.DispatcherTimer _nativeMusicSuppressionTimer;
private bool _isNativeMusicSuppressionActive = false;
private bool _hasLoggedSuppression = false;
```

### Suppression Logic

**When to Suppress:**
```csharp
bool shouldSuppress = _settings?.SuppressPlayniteBackgroundMusic == true ||
                     (_settings?.EnableDefaultMusic == true && 
                      _settings?.UseNativeMusicAsDefault == true);
```

**Suppression Method:**
```csharp
private void SuppressNativeMusic()
{
    // 1. Get Playnite's BackgroundMusic property via reflection
    var mainModel = GetMainModel();
    var appType = mainModel.App.GetType();
    
    // 2. Walk up inheritance chain to find FullscreenApplication
    while (appType != null && appType.Name != "FullscreenApplication")
    {
        appType = appType.BaseType;
    }
    
    // 3. Get BackgroundMusic static property
    var backgroundMusicProperty = appType.GetProperty("BackgroundMusic", 
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    
    // 4. Get current music pointer
    IntPtr currentMusic = (IntPtr)(backgroundMusicProperty.GetValue(null) ?? IntPtr.Zero);
    
    // 5. Stop and free music
    if (currentMusic != IntPtr.Zero)
    {
        SDL_mixer.Mix_HaltMusic();              // Stop playback
        SDL_mixer.Mix_FreeMusic(currentMusic);   // Free resource
        backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
    }
    else
    {
        // Set to Zero to prevent loading
        backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
    }
}
```

### Suppression Timing

**Startup Suppression:**
- Triggered in `OnApplicationStarted()` if in fullscreen mode
- Runs for 15 seconds with 50ms polling interval
- Catches native music that starts at different times

**Event-Based Suppression:**
- Triggered when extension music starts (`OnMusicStarted` event)
- Immediate suppression when music begins

### SDL2_mixer Integration

**P/Invoke Declarations:**
```csharp
[DllImport("SDL2_mixer", CallingConvention = CallingConvention.Cdecl)]
public static extern int Mix_HaltMusic();

[DllImport("SDL2_mixer", CallingConvention = CallingConvention.Cdecl)]
public static extern void Mix_FreeMusic(IntPtr music);
```

**Usage:**
- Direct native calls to SDL2_mixer
- No managed wrapper needed for simple operations
- Thread-safe (SDL2 handles thread safety)

---

## Primary Song Selection

### Implementation

**Location**: `Common/PrimarySongManager.cs`, `Services/MusicPlaybackService.cs`

### Primary Song File Format

**Metadata File**: `.primarysong.json`
```json
{
    "PrimarySongFileName": "Main Theme.mp3",
    "SetDate": "2025-01-15T10:30:00"
}
```

**Location**: `{GameMusicDirectory}/.primarysong.json`

### Selection Logic

**In MusicPlaybackService.SelectSongToPlay():**
```csharp
// 1. Check if current song is still valid
if (!string.IsNullOrWhiteSpace(_currentSongPath) && songs.Contains(_currentSongPath))
{
    return _currentSongPath; // Continue playing current song
}

// 2. Check for primary song (only for game music, not default music)
var gameDirectory = _fileService.GetGameMusicDirectory(game);
if (!string.IsNullOrEmpty(gameDirectory))
{
    var primarySong = PrimarySongManager.GetPrimarySong(gameDirectory, null);
    
    // Primary song plays once on first selection
    if (isNewGame && primarySong != null && songs.Contains(primarySong))
    {
        var primaryPlayed = _primarySongPlayed.ContainsKey(gameDirectory) && 
                           _primarySongPlayed[gameDirectory];
        if (!primaryPlayed)
        {
            _primarySongPlayed[gameDirectory] = true; // Mark as played
            _previousSongPath = primarySong;          // Track for randomization
            return primarySong;
        }
    }
}

// 3. Fall back to first song or randomization
return songs.FirstOrDefault();
```

### Primary Song Tracking

**State Variable:**
```csharp
private Dictionary<string, bool> _primarySongPlayed;
// Key: Game music directory path
// Value: Whether primary song has been played this session
```

**Behavior:**
- Primary song plays **once per session** on first game selection
- After primary song plays, randomization/normal selection takes over
- State is **not persisted** - resets when extension restarts

---

## Song Randomization Logic

### Implementation

**Location**: `Services/MusicPlaybackService.cs` (lines 661-725, 839-904)

### Randomization Settings

```csharp
// Settings properties
public bool RandomizeOnEverySelect { get; set; }  // Randomize when selecting different game
public bool RandomizeOnMusicEnd { get; set; }    // Randomize when current song ends
```

### Selection Randomization

**When Selecting Game:**
```csharp
// In SelectSongToPlay()
if (songs.Count > 1 && _currentSettings != null)
{
    bool shouldRandomize = false;
    
    // Randomize on every select (when game changes)
    if (isNewGame && _currentSettings.RandomizeOnEverySelect)
    {
        shouldRandomize = true;
    }
    
    if (shouldRandomize)
    {
        // Select random song, avoiding immediate repeat
        var random = new Random();
        string selected;
        int attempts = 0;
        do
        {
            selected = songs[random.Next(songs.Count)];
            attempts++;
        }
        while (selected == _previousSongPath && songs.Count > 1 && attempts < 10);
        
        _previousSongPath = selected;
        return selected;
    }
}
```

### End-of-Song Randomization

**When Song Ends:**
```csharp
// In OnMediaEnded()
if (_currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null)
{
    // Only randomize for game music (not default music)
    bool isDefaultMusic = IsDefaultMusicPath(_currentSongPath, _currentSettings);
    if (!isDefaultMusic)
    {
        // Get available songs for current game
        var songs = _fileService.GetAvailableSongs(_currentGame);
        if (songs.Count > 1)
        {
            // Select random song (avoiding immediate repeat)
            var random = new Random();
            string nextSong;
            int attempts = 0;
            do
            {
                nextSong = songs[random.Next(songs.Count)];
                attempts++;
            }
            while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);
            
            // Play the new random song
            _previousSongPath = _currentSongPath;
            _currentSongPath = nextSong;
            LoadAndPlayFile(nextSong);
            return;
        }
    }
}
```

### Randomization Avoidance

**Previous Song Tracking:**
- `_previousSongPath`: Tracks last played song
- Used to avoid playing same song twice in a row
- Maximum 10 attempts to find different song (prevents infinite loops)

---

## Default Music Fallback

### Implementation

**Location**: `Services/MusicPlaybackService.cs` (lines 211-397)

### Default Music Types

**1. Custom Default Music:**
- Path: `UniPlaySongSettings.DefaultMusicPath`
- User-selected music file
- Plays when no game music found

**2. Native Playnite Music:**
- Path: Retrieved via `PlayniteThemeHelper.FindBackgroundMusicFile()`
- Playnite's default theme background music
- Enabled when `UseNativeMusicAsDefault = true`

### Fallback Logic

**In PlayGameMusic():**
```csharp
// Get game music files
var songs = _fileService.GetAvailableSongs(game);

// If no game music, add default music to songs list
if (songs.Count == 0 && settings?.EnableDefaultMusic == true)
{
    if (settings.UseNativeMusicAsDefault == true)
    {
        // Using native Playnite music
        var nativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(null);
        if (!string.IsNullOrWhiteSpace(nativeMusicPath) && File.Exists(nativeMusicPath))
        {
            songs.Add(nativeMusicPath);
        }
    }
    else
    {
        // Using custom default music file
        if (!string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
            File.Exists(settings.DefaultMusicPath))
        {
            songs.Add(settings.DefaultMusicPath);
        }
    }
}
```

### Position Preservation (PNS Pattern)

**Pausing Default Music:**
```csharp
private void PauseDefaultMusic(UniPlaySongSettings settings)
{
    if (_isPlayingDefaultMusic && 
        IsDefaultMusicPath(_currentSongPath, settings) &&
        _musicPlayer?.IsLoaded == true)
    {
        // CRITICAL: Save position BEFORE pausing
        _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
        _isPlayingDefaultMusic = false;
        _musicPlayer.Pause();
        _isPaused = true;
    }
}
```

**Resuming Default Music:**
```csharp
private void ResumeDefaultMusic(string defaultMusicPath, UniPlaySongSettings settings)
{
    // Check if we can resume (same file, paused, already loaded)
    if (!_isPlayingDefaultMusic && 
        string.Equals(_lastDefaultMusicPath, defaultMusicPath, StringComparison.OrdinalIgnoreCase) &&
        _isPaused &&
        _musicPlayer?.IsLoaded == true &&
        string.Equals(_musicPlayer.Source, defaultMusicPath, StringComparison.OrdinalIgnoreCase))
    {
        // Resume from paused state
        _fader.Resume();
        _isPaused = false;
        _isPlayingDefaultMusic = true;
    }
    else
    {
        // Load and play from saved position
        _musicPlayer.Load(defaultMusicPath);
        _musicPlayer.Volume = 0;
        _musicPlayer.Play(_defaultMusicPausedOnTime); // Resume from saved position
        _fader.FadeIn();
        _isPlayingDefaultMusic = true;
    }
}
```

### Default Music Detection

**Helper Method:**
```csharp
private bool IsDefaultMusicPath(string path, UniPlaySongSettings settings)
{
    if (string.IsNullOrWhiteSpace(path) || settings == null || !settings.EnableDefaultMusic)
    {
        return false;
    }
    
    if (settings.UseNativeMusicAsDefault == true)
    {
        // Check against native music path
        var nativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(null);
        return !string.IsNullOrWhiteSpace(nativeMusicPath) &&
               string.Equals(path, nativeMusicPath, StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        // Check against custom default music path
        return !string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
               string.Equals(path, settings.DefaultMusicPath, StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## Skip Logic and State Management

### Implementation

**Location**: `Services/MusicPlaybackCoordinator.cs`

### Skip Types

**1. SkipFirstSelectionAfterModeSwitch:**
- Skips first game selection after switching to fullscreen mode
- State: `_skipFirstSelectActive`
- Cleared: After first selection is processed

**2. ThemeCompatibleSilentSkip:**
- Waits for keyboard/controller input before playing music
- State: `_loginSkipActive`
- Cleared: When Enter/Space/Escape pressed or view changes

### Skip Logic Flow

**ShouldPlayMusic() Gatekeeper:**
```csharp
public bool ShouldPlayMusic(Game game)
{
    // Basic checks
    if (_settings == null || !_settings.EnableMusic) return false;
    if (_playbackService == null) return false;
    if (_settings.MusicVolume <= 0) return false;
    if (_settings.VideoIsPlaying) return false;
    if (game == null) return false;
    
    // Login skip check
    if (_loginSkipActive) return false;
    
    // First select skip check
    if ((_firstSelect || _skipFirstSelectActive) && 
        _settings.SkipFirstSelectionAfterModeSwitch)
    {
        return false;
    }
    
    // Mode-based checks
    var state = _settings.MusicState;
    if (_isFullscreen() && state != AudioState.Fullscreen && state != AudioState.Always)
        return false;
    if (_isDesktop() && state != AudioState.Desktop && state != AudioState.Always)
        return false;
    
    return true;
}
```

**HandleGameSelected() Processing:**
```csharp
public void HandleGameSelected(Game game, bool isFullscreen)
{
    // Reset skip state when entering fullscreen for first time
    if (isFullscreen && !_hasSeenFullscreen && 
        _settings?.SkipFirstSelectionAfterModeSwitch == true)
    {
        _firstSelect = true;
        _skipFirstSelectActive = false;
        _hasSeenFullscreen = true;
    }
    
    // SkipFirstSelectionAfterModeSwitch - takes precedence
    if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
    {
        _skipFirstSelectActive = true;
        _firstSelect = false;
        return; // Don't play music
    }
    
    // ThemeCompatibleSilentSkip - only if SkipFirstSelectionAfterModeSwitch is disabled
    if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && 
        isFullscreen && !_settings.SkipFirstSelectionAfterModeSwitch)
    {
        _loginSkipActive = true;
        return; // Don't play music yet
    }
    
    // Clear skip flags
    if (_loginSkipActive) _loginSkipActive = false;
    if (_skipFirstSelectActive) _skipFirstSelectActive = false;
    
    // Play music if should play
    if (ShouldPlayMusic(game))
    {
        _playbackService?.PlayGameMusic(game, _settings, false);
    }
}
```

### Login Dismiss Handling

**Keyboard Input:**
```csharp
// In UniPlaySong.cs
private void OnLoginDismissKeyPress(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Escape || e.Key == Key.Return)
    {
        _coordinator.HandleLoginDismiss();
    }
}
```

**Controller Input:**
```csharp
// In UniPlaySong.cs - XInput monitoring
private void CheckLoginBypassButtonPresses(XINPUT_GAMEPAD currentState, XINPUT_GAMEPAD lastState)
{
    ushort newlyPressed = (ushort)(currentState.wButtons & ~lastState.wButtons);
    
    if ((newlyPressed & XINPUT_GAMEPAD_A) != 0 || 
        (newlyPressed & XINPUT_GAMEPAD_START) != 0)
    {
        _coordinator.HandleLoginDismiss();
    }
}
```

**HandleLoginDismiss() Implementation:**
```csharp
public void HandleLoginDismiss()
{
    if (!_loginSkipActive) return;
    
    _loginSkipActive = false;
    _skipFirstSelectActive = false;
    _firstSelect = false;
    
    // Short delay then attempt to play
    var timer = new System.Timers.Timer(150) { AutoReset = false };
    timer.Elapsed += (s, args) =>
    {
        timer.Dispose();
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var game = _getSelectedGame();
            if (game != null && ShouldPlayMusic(game))
            {
                _playbackService?.PlayGameMusic(game, _settings, false);
            }
        });
    };
    timer.Start();
}
```

---

## Preview Mode

### Implementation

**Location**: `Services/MusicPlaybackService.cs` (lines 727-837)

### Preview Mode Settings

```csharp
public bool EnablePreviewMode { get; set; }      // Enable preview mode
public int PreviewDuration { get; set; }         // Duration in seconds (15-300)
```

### Preview Mode Logic

**Song Start Tracking:**
```csharp
private void MarkSongStart()
{
    _songStartTime = DateTime.Now;
    
    // Start preview timer if preview mode enabled for game music
    if (_currentSettings?.EnablePreviewMode == true &&
        !_isPlayingDefaultMusic &&
        !IsDefaultMusicPath(_currentSongPath, _currentSettings))
    {
        _previewTimer.Start();
    }
}
```

**Preview Timer Tick:**
```csharp
private void OnPreviewTimerTick(object sender, EventArgs e)
{
    if (ShouldRestartForPreview() && _musicPlayer?.IsActive == true)
    {
        _previewTimer.Stop();
        
        var songToRestart = _currentSongPath;
        
        // Use fader for smooth fade-out/fade-in transition
        _fader.Switch(
            stopAction: () => _musicPlayer.Stop(),
            preloadAction: () => _musicPlayer.PreLoad(songToRestart),
            playAction: () =>
            {
                _musicPlayer.Load(songToRestart);
                _musicPlayer.Play(TimeSpan.Zero); // Restart from beginning
                MarkSongStart(); // Reset timer for next cycle
            }
        );
    }
}
```

**Should Restart Check:**
```csharp
private bool ShouldRestartForPreview()
{
    // Preview mode only applies to game music (not default music)
    if (_isPlayingDefaultMusic || IsDefaultMusicPath(_currentSongPath, _currentSettings))
    {
        return false;
    }
    
    if (_currentSettings == null || !_currentSettings.EnablePreviewMode)
    {
        return false;
    }
    
    if (_songStartTime == DateTime.MinValue)
    {
        return false; // No start time recorded
    }
    
    var elapsed = DateTime.Now - _songStartTime;
    var previewDuration = TimeSpan.FromSeconds(_currentSettings.PreviewDuration);
    
    return elapsed >= previewDuration;
}
```

### Preview Mode Behavior

- **Applies Only To**: Game music (not default music)
- **Timer Interval**: 1 second (checks every second)
- **Restart Method**: Fade-out current playback, fade-in from beginning
- **Duration Range**: 15-300 seconds (configurable)

---

## Volume and Fade Control

### Implementation

**Location**: `Players/MusicFader.cs`, `Services/MusicPlaybackService.cs`

### Volume Management

**Volume Conversion:**
```csharp
// Settings: 0-100 (percentage)
// Player: 0.0-1.0 (decimal)
_targetVolume = settings.MusicVolume / Constants.VolumeDivisor; // 100.0
```

**SDL2 Volume:**
```csharp
// SDL2 uses 0-128 range
int mixerVolume = (int)(_volume * 128);
SDL2Mixer.Mix_VolumeMusic(mixerVolume);
```

### Fade Control

**Fade Durations:**
```csharp
private double _fadeInDuration;   // Fade-in duration (0.05-10.0 seconds)
private double _fadeOutDuration;  // Fade-out duration (0.05-10.0 seconds)
```

**Fade Methods:**
```csharp
// Fade in from 0 to target volume
_fader.FadeIn();

// Fade out from current volume to 0, then stop
_fader.FadeOutAndStop(() => { /* cleanup */ });

// Switch: Fade out current, fade in new
_fader.Switch(
    stopAction: () => { /* stop current */ },
    preloadAction: () => { /* preload new */ },
    playAction: () => { /* play new */ }
);

// Pause with fade out
_fader.Pause();

// Resume with fade in
_fader.Resume();
```

### Fade Implementation

**MusicFader Class:**
- Uses `DispatcherTimer` for smooth volume transitions
- Updates volume in small increments over fade duration
- Handles both SDL2 and WPF MediaPlayer implementations

---

## Controller Input Handling

### Implementation

**Location**: `Services/Controller/ControllerInputService.cs`, `UniPlaySong.cs`

### XInput Integration

**P/Invoke Declarations:**
```csharp
[DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
private static extern int XInputGetState_1_4(int dwUserIndex, ref XINPUT_STATE pState);

[DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
private static extern int XInputGetState_1_3(int dwUserIndex, ref XINPUT_STATE pState);

[DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
private static extern int XInputGetState_9_1_0(int dwUserIndex, ref XINPUT_STATE pState);
```

**Button Constants:**
```csharp
private const ushort XINPUT_GAMEPAD_A = 0x1000;
private const ushort XINPUT_GAMEPAD_START = 0x0010;
```

### Controller Monitoring

**Login Bypass Monitoring:**
```csharp
// Polls controller every 50ms
Task.Run(async () =>
{
    while (!_controllerLoginMonitoringCancellation.Token.IsCancellationRequested)
    {
        XINPUT_STATE state = new XINPUT_STATE();
        int result = XInputGetState(0, ref state); // Controller 0
        
        if (result == 0) // Success
        {
            // Check for button presses (only on state change)
            if (_hasLastLoginState && 
                state.dwPacketNumber != _lastControllerLoginState.dwPacketNumber)
            {
                CheckLoginBypassButtonPresses(state.Gamepad, _lastControllerLoginState.Gamepad);
            }
            
            _lastControllerLoginState = state;
            _hasLastLoginState = true;
        }
        
        await Task.Delay(50, _controllerLoginMonitoringCancellation.Token);
    }
});
```

**Button Press Detection:**
```csharp
private void CheckLoginBypassButtonPresses(XINPUT_GAMEPAD currentState, XINPUT_GAMEPAD lastState)
{
    // Get newly pressed buttons
    ushort newlyPressed = (ushort)(currentState.wButtons & ~lastState.wButtons);
    
    if (newlyPressed == 0) return; // No new button presses
    
    // Check for A button or Start button
    if ((newlyPressed & XINPUT_GAMEPAD_A) != 0 || 
        (newlyPressed & XINPUT_GAMEPAD_START) != 0)
    {
        // Trigger login dismiss on UI thread
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _coordinator.HandleLoginDismiss();
        }));
    }
}
```

---

## Audio Normalization

### Implementation

**Location**: `Services/AudioNormalizationService.cs`

### Normalization Parameters

**EBU R128 Standard:**
```csharp
public double NormalizationTargetLoudness { get; set; }  // -16.0 LUFS (default)
public double NormalizationTruePeak { get; set; }        // -1.5 dBTP (default)
public double NormalizationLoudnessRange { get; set; }    // 11.0 LU (default)
```

**FFmpeg Command:**
```bash
ffmpeg -i input.mp3 -af "loudnorm=I=-16:TP=-1.5:LRA=11" output.mp3
```

### Normalization Process

**Bulk Normalization:**
```csharp
public async Task<NormalizationResult> NormalizeBulkAsync(
    List<string> musicFiles,
    NormalizationSettings settings,
    IProgress<NormalizationProgress> progress,
    CancellationToken cancellationToken)
{
    // 1. Validate FFmpeg
    if (!ValidateFFmpegAvailable(settings.FFmpegPath))
    {
        throw new InvalidOperationException("FFmpeg not available");
    }
    
    // 2. Process each file
    foreach (var file in musicFiles)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        // 3. Check if already normalized
        if (settings.SkipAlreadyNormalized && 
            file.Contains(settings.NormalizationSuffix))
        {
            continue; // Skip
        }
        
        // 4. Preserve original (if enabled)
        if (!settings.DoNotPreserveOriginals)
        {
            // Move original to PreservedOriginals folder
        }
        
        // 5. Normalize file
        await NormalizeFileAsync(file, settings, progress);
    }
}
```

**File Normalization:**
```csharp
private async Task NormalizeFileAsync(
    string inputFile,
    NormalizationSettings settings,
    IProgress<NormalizationProgress> progress)
{
    // 1. Generate output filename
    string outputFile = GenerateNormalizedFileName(inputFile, settings.NormalizationSuffix);
    
    // 2. Build FFmpeg command
    string arguments = $"-i \"{inputFile}\" " +
                      $"-af \"loudnorm=I={settings.TargetLoudness}:TP={settings.TruePeak}:LRA={settings.LoudnessRange}\" " +
                      $"-c:a {settings.AudioCodec} " +
                      $"\"{outputFile}\"";
    
    // 3. Execute FFmpeg
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = settings.FFmpegPath,
        Arguments = arguments,
        // ...
    });
    
    await process.WaitForExitAsync();
    
    // 4. Replace original if DoNotPreserveOriginals is true
    if (settings.DoNotPreserveOriginals)
    {
        File.Delete(inputFile);
        File.Move(outputFile, inputFile);
    }
}
```

### Normalization File Management

**Preserved Originals:**
- Location: `{BasePath}/PreservedOriginals/{GameId}/{OriginalFileName}`
- Created when `DoNotPreserveOriginals = false`
- Can be restored via `RestoreFromBackupsAsync()`

**Normalized Files:**
- Suffix: `-normalized` (configurable)
- Example: `song.mp3` → `song-normalized.mp3`
- Original file preserved unless `DoNotPreserveOriginals = true`

---

## Download Manager

### Overview

The Download Manager handles bulk music downloads with Review Mode for correcting wrong album picks and Auto-Add Songs for expanding music libraries.

### Key Classes

**Location**: `Services/DownloadDialogService.cs`

#### Review Mode State

```csharp
// Review mode tracking
private bool _isReviewMode = false;              // Whether review mode is active
private List<GameDownloadResult> _downloadResults; // Results from batch download

// In BatchDownloadItem (Models/NormalizationSettings.cs)
public bool WasRedownloaded { get; set; }        // Orange highlight for corrected games
public bool HadSongsAdded { get; set; }          // Purple highlight for games with new songs
```

#### GameDownloadResult

**Location**: `Services/BatchDownloadService.cs`

```csharp
public class GameDownloadResult
{
    public Game Game { get; set; }
    public bool Success { get; set; }
    public string AlbumName { get; set; }
    public string AlbumId { get; set; }           // Preserved for hint-based re-downloads
    public string SourceName { get; set; }        // "KHInsider", "YouTube", "Zophar"
    public string SongPath { get; set; }
    public string ErrorMessage { get; set; }
    public bool WasSkipped { get; set; }
    public string SkipReason { get; set; }
}
```

### Auto-Add Songs Flow

```
HandleAutoAddMoreSongs():
├── Show song count picker dialog (1, 2, or 3 songs per game)
├── For each game in parallel (SemaphoreSlim limit 4):
│   ├── If AlbumId exists:
│   │   └── Create Album directly from stored ID (skip search)
│   ├── Else if AlbumName exists:
│   │   ├── Search using game name (NOT album name)
│   │   ├── Match album by name or fuzzy match
│   │   └── Use matched album for download
│   ├── Else (skipped game):
│   │   ├── Search for albums using game name
│   │   └── Use BestAlbumPick to select
│   ├── Get song list from album
│   ├── Filter out already-downloaded songs
│   ├── Pick N random songs
│   └── Download songs (play each as completed)
└── Update UI with purple highlighting for games that got songs
```

### Album ID Preservation

UPS Search Hints store album identifiers (YouTube playlist IDs, KHInsider slugs) that must be preserved for reliable re-downloads:

```csharp
// In BatchDownloadService.DownloadMusicForGameInternal():
result.AlbumName = bestAlbum.Name;
result.AlbumId = bestAlbum.Id;    // Preserve ID for Auto-Add Songs

// In HandleAutoAddMoreSongs():
if (!string.IsNullOrEmpty(albumId))
{
    // Use ID directly - no search needed
    matchingAlbum = new Album
    {
        Id = albumId,
        Name = albumName,
        Source = source
    };
}
```

### Parallel Processing

Downloads use `SemaphoreSlim` for controlled parallelism:

```csharp
private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(4, 4);

// Downloads start immediately as albums are found (no waiting for all searches)
var downloadTasks = games.Select(async game =>
{
    await _downloadSemaphore.WaitAsync(cancellationToken);
    try
    {
        return await DownloadMusicForGameInternal(game, settings, ...);
    }
    finally
    {
        _downloadSemaphore.Release();
    }
});
```

### Search Cache Optimization

**Location**: `Services/SearchCacheService.cs`

Cache stores only essential fields to reduce file size (~90% reduction):

```csharp
// Cached per album (v2.0 format):
public class CachedAlbum
{
    public string Id { get; set; }      // Album/playlist ID for downloading
    public string Name { get; set; }    // Album name for display
    public string Source { get; set; }  // "KHInsider", "YouTube"
    public string Year { get; set; }    // Release year
}
// Maximum 10 albums per source per game
```

Old v1.0 caches are automatically cleared on first access.

---

## Constants Reference

**Location**: `Common/Constants.cs`

### Volume Constants
```csharp
public const double DefaultTargetVolume = 0.5;        // 0.0-1.0
public const int DefaultMusicVolume = 50;            // 0-100
public const double VolumeDivisor = 100.0;            // Convert % to decimal
```

### Fade Constants
```csharp
public const double DefaultFadeInDuration = 0.5;     // seconds
public const double DefaultFadeOutDuration = 0.3;   // seconds
public const double MinFadeDuration = 0.05;          // seconds
public const double MaxFadeDuration = 10.0;          // seconds
```

### Preview Constants
```csharp
public const int DefaultPreviewDuration = 30;        // seconds
public const int MinPreviewDuration = 15;            // seconds
public const int MaxPreviewDuration = 300;           // seconds (5 minutes)
```

### Directory Constants
```csharp
public const string ExtraMetadataFolderName = "ExtraMetadata";
public const string ExtensionFolderName = "UniPlaySong";
public const string GamesFolderName = "Games";
public const string TempFolderName = "Temp";
public const string PreservedOriginalsFolderName = "PreservedOriginals";
```

---

## Theme Integration (UPS_MusicControl)

### Overview

UniPlaySong exposes a PluginControl called `UPS_MusicControl` that allows themes to pause/resume music through XAML bindings. This uses a multi-source pause system to prevent conflicts with other pause reasons.

### Architecture

**Location**: `Controls/MusicControl.xaml.cs`

**Control Registration** (in `UniPlaySong.cs`):
```csharp
AddCustomElementSupport(new AddCustomElementSupportArgs
{
    SourceName = "UPS",
    ElementList = new List<string> { "MusicControl" }
});
```

**Control Creation**:
```csharp
public override Control GetGameViewControl(GetGameViewControlArgs args)
{
    if (args.Name == "MusicControl")
    {
        return new Controls.MusicControl(_settings);
    }
    return null;
}
```

### State Variables

**MusicControl State**:
```csharp
private static UniPlaySongSettings _settings;           // Shared settings reference
private static readonly List<MusicControl> _musicControls;  // All active instances
```

**Settings Properties**:
```csharp
// UniPlaySongSettings.cs
public bool VideoIsPlaying { get; set; }      // Set by MediaElementsMonitor
public bool ThemeOverlayActive { get; set; }  // Set by MusicControl
```

### Data Flow

**Theme sets Tag="True"**:
```
1. TagProperty.OnTagChanged() fires
2. MusicControl.UpdateMute() aggregates all Tag values
3. _settings.ThemeOverlayActive = true
4. PropertyChanged event fires
5. UniPlaySong.OnSettingsChanged() receives event
6. MusicPlaybackCoordinator.HandleThemeOverlayChange(true)
7. MusicPlaybackService.AddPauseSource(PauseSource.ThemeOverlay)
8. MusicFader.Pause() - smooth fade-out
```

**Theme sets Tag="False"**:
```
1. TagProperty.OnTagChanged() fires
2. MusicControl.UpdateMute() aggregates all Tag values
3. _settings.ThemeOverlayActive = false
4. PropertyChanged event fires
5. UniPlaySong.OnSettingsChanged() receives event
6. MusicPlaybackCoordinator.HandleThemeOverlayChange(false)
7. MusicPlaybackService.RemovePauseSource(PauseSource.ThemeOverlay)
8. If no other pause sources: MusicFader.Resume() - smooth fade-in
9. If music not loaded: PlayGameMusic() starts fresh
```

### Multi-Source Pause System

**Location**: `Services/MusicPlaybackService.cs`

```csharp
// Pause source enum (Models/PauseSource.cs)
public enum PauseSource
{
    Video,                    // MediaElementsMonitor detected video
    Manual,                   // User manually paused via top panel button
    Settings,                 // Settings being changed
    ViewChange,               // Mode switching (desktop ↔ fullscreen)
    DefaultMusicPreservation, // Default music position saved during switch
    ThemeOverlay,             // Theme MusicControl Tag=True
    FocusLoss,                // Playnite window lost focus (PauseOnFocusLoss setting)
    Minimized,                // Playnite window minimized (PauseOnMinimize setting)
    SystemTray                // Playnite hidden in system tray (PauseWhenInSystemTray setting)
}

// Active sources tracking
private readonly HashSet<PauseSource> _activePauseSources;
private bool _isPaused => _activePauseSources.Count > 0;
```

**Source categories**:
- **Transient sources** (`Video`, `Settings`, `ViewChange`, `DefaultMusicPreservation`, `ThemeOverlay`) — cleared automatically by `ClearAllPauseSources()` during game music transitions.
- **Preserved sources** (`Manual`, `FocusLoss`, `Minimized`, `SystemTray`) — survive `ClearAllPauseSources()` and must be cleared by their own handlers (user press play, window events, etc.).

**Key methods**:

| Method | Behavior |
|--------|----------|
| `AddPauseSource(source)` | Adds source to set; calls `_fader.Pause()` if this is the first source (transitions from playing to paused). |
| `RemovePauseSource(source)` | Removes source from set; if all sources cleared and player is actively playing, calls `_fader.Resume()`. If player is loaded but not active (song loaded during manual pause), starts playback with `Play()` + `FadeIn()`. |
| `ClearAllPauseSources()` | Clears all **transient** sources. Preserves `Manual`, `FocusLoss`, `Minimized`, and `SystemTray`. Only calls `_fader.Resume()` if zero sources remain after clearing. |

**Manual pause and game switching**: When the user manually pauses and then switches games, `PlayGameMusic()` loads the new song (updating `CurrentSongPath` and firing `OnSongChanged` for Now Playing), but checks `_isPaused` after `ClearAllPauseSources()`. Since `Manual` is preserved, `_isPaused` remains true and `Play()`/`FadeIn()` are skipped. The song is loaded but silent. When the user presses play, `RemovePauseSource(Manual)` detects the loaded-but-not-active state and starts playback with fade-in.

### Integration Points

**UniPlaySong.cs Event Handlers**:
```csharp
private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
    {
        _coordinator.HandleVideoStateChange(_settings.VideoIsPlaying);
    }
    else if (e.PropertyName == nameof(UniPlaySongSettings.ThemeOverlayActive))
    {
        _coordinator.HandleThemeOverlayChange(_settings.ThemeOverlayActive);
    }
}
```

### Theme Usage

**Basic Example**:
```xml
<ContentControl x:Name="UPS_MusicControl"
    Tag="{Binding ElementName=MyOverlay, Path=IsVisible}" />
```

**DataTrigger Example**:
```xml
<ContentControl x:Name="UPS_MusicControl">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Tag" Value="False"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ElementName=IntroVideo, Path=Tag}" Value="Playing">
                    <Setter Property="Tag" Value="True"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Debugging Theme Integration

**Check MusicControl Creation**:
```
[GetGameViewControl] Creating MusicControl instance for theme
[MusicControl] Instance created
[MusicControl] Loaded (total instances: 1)
```

**Check Tag Changes**:
```
[MusicControl] Tag changed: False -> True
[MusicControl] Setting ThemeOverlayActive=true (was false)
HandleThemeOverlayChange: ThemeOverlayActive=true - adding ThemeOverlay pause source
Pause added: ThemeOverlay (total sources: 1) - fading out
```

**Check Resume**:
```
[MusicControl] Tag changed: True -> False
[MusicControl] Setting ThemeOverlayActive=false (was true)
HandleThemeOverlayChange: ThemeOverlayActive=false - removing ThemeOverlay pause source
Pause removed: ThemeOverlay - resuming playback (no pause sources remaining)
```

---

## Debugging Tips

### State Inspection

**Check Current State:**
```csharp
// In MusicPlaybackService
_fileLogger?.Info($"CurrentGameId: {_currentGameId}");
_fileLogger?.Info($"CurrentSongPath: {_currentSongPath}");
_fileLogger?.Info($"IsPlayingDefaultMusic: {_isPlayingDefaultMusic}");
_fileLogger?.Info($"IsPaused: {_isPaused}");
```

**Check Coordinator State:**
```csharp
// In MusicPlaybackCoordinator
_fileLogger?.Info($"FirstSelect: {_firstSelect}");
_fileLogger?.Info($"LoginSkipActive: {_loginSkipActive}");
_fileLogger?.Info($"SkipFirstSelectActive: {_skipFirstSelectActive}");
```

### Common Issues

**Music Not Playing:**
1. Check `ShouldPlayMusic()` return value
2. Check skip flags (`_loginSkipActive`, `_skipFirstSelectActive`)
3. Check `EnableMusic` setting
4. Check `MusicVolume > 0`
5. Check `VideoIsPlaying` (should be false)

**Default Music Not Resuming:**
1. Check `_isPlayingDefaultMusic` state
2. Check `_defaultMusicPausedOnTime` value
3. Check `_lastDefaultMusicPath` matches current path
4. Check `_isPaused` state

**Randomization Not Working:**
1. Check `RandomizeOnEverySelect` or `RandomizeOnMusicEnd` settings
2. Check `songs.Count > 1` (need multiple songs)
3. Check `_previousSongPath` tracking

