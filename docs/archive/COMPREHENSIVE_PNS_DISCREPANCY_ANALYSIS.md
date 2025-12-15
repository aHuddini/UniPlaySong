# Comprehensive PlayniteSound vs UniPlaySong Discrepancy Analysis

**Date**: 2025-11-29  
**Purpose**: Identify ALL discrepancies between PlayniteSound and UniPlaySong that could affect music playback

---

## 1. STATE VARIABLES & TRACKING

### PlayniteSound State Variables:
```csharp
private bool _gameRunning;
private bool _musicEnded;
private bool _firstSelect = true;
private bool _closeAudioFilesNextPlay;
private string _prevMusicFileName = string.Empty;  // CRITICAL: Tracks last played file
private readonly Dictionary<string, bool> _primarySongPlayed = new Dictionary<string, bool>();
private IMusicPlayer _musicPlayer;
private MusicFader _musicFader;  // CRITICAL: Handles fade transitions
private ISet<string> _pausers = new HashSet<string>();  // CRITICAL: Tracks pause sources
public bool ReloadMusic { get; set; }  // CRITICAL: Forces reload even if same file
```

### UniPlaySong State Variables:
```csharp
private bool _gameDetailsVisible = false;
private bool _isSilentSkipActive = false;  // EXTRA: Not in PlayniteSound
private bool _firstSelect = true;
// NO _gameRunning tracking
// NO _musicEnded tracking
// NO _closeAudioFilesNextPlay
private string _currentSongPath;  // In MusicPlaybackService, not main class
private string _currentGameId;  // In MusicPlaybackService, not main class
// NO _primarySongPlayed dictionary
// NO MusicFader - we use direct MediaPlayer
// NO _pausers set
// NO ReloadMusic flag
```

**CRITICAL DIFFERENCES**:
1. **Missing `_pausers`**: PlayniteSound uses this to track multiple pause sources. We don't have this.
2. **Missing `ReloadMusic`**: PlayniteSound uses this to force reload even if same file. We don't have this.
3. **Missing `_musicEnded`**: PlayniteSound tracks when music ends for randomization logic.
4. **Missing `_gameRunning`**: PlayniteSound tracks if a game is running.
5. **Missing `_prevMusicFileName` in main class**: We track it in MusicPlaybackService, but PlayniteSound tracks it at the main class level.
6. **Missing `MusicFader`**: PlayniteSound uses a fader for smooth transitions. We use direct MediaPlayer calls.

---

## 2. MUSIC PLAYER INITIALIZATION

### PlayniteSound:
```csharp
// In constructor:
_musicPlayer = MusicPlayer.Create(Settings);  // Creates SDL2 or WMP player
_musicPlayer.MediaEnded += MediaEnded;
_musicPlayer.MediaFailed += MediaFailed;
_musicFader = new MusicFader(_musicPlayer, Settings);  // CRITICAL: Wraps player
```

### UniPlaySong:
```csharp
// In constructor:
var musicPlayer = new MusicPlayer();  // Direct WPF MediaPlayer
_playbackService = new MusicPlaybackService(musicPlayer, _fileService, _fileLogger);
// NO MusicFader
// NO MediaEnded/MediaFailed handlers in main class (handled in service)
```

**CRITICAL DIFFERENCE**: PlayniteSound wraps the player in a `MusicFader` that handles fade transitions. We don't have this, which means:
- No fade-out when stopping
- No fade-in when starting
- Direct stop/start which might cause audio glitches
- No preloading support

---

## 3. MUSIC LOADING & PLAYING FLOW

### PlayniteSound Flow:
```
PlayMusicBasedOnSelected()
  -> PlayMusicFromFirstSelected()
    -> PlayMusicFromFirst(games)
      -> PlayMusicFromFiles(files)
        -> PlayMusicFromPath(filePath)
          -> MusicFader.Switch(SubCloseMusic, PreloadMusicFromPath, SubPlayMusicFromPath)
            -> SubPlayMusicFromPath(filePath)
              -> _prevMusicFileName = string.Empty  // CLEAR FIRST
              -> _musicPlayer.Load(filePath)
              -> _musicPlayer.Play(startFrom)
              -> _prevMusicFileName = filePath  // SET AFTER
```

### UniPlaySong Flow:
```
PlayMusicBasedOnSelected()
  -> _playbackService.PlayGameMusic(game, settings)
    -> SelectSongToPlay()
    -> Stop/Close if needed
    -> _currentSongPath = null  // CLEAR FIRST (we do this)
    -> _musicPlayer.Load(songToPlay)
    -> _musicPlayer.Play()
    -> _currentSongPath = songToPlay  // SET AFTER (we do this)
```

**KEY DIFFERENCES**:
1. **MusicFader wrapper**: PlayniteSound uses fader for transitions, we don't
2. **Preloading**: PlayniteSound has `PreloadMusicFromPath` step, we don't
3. **File tracking location**: PlayniteSound tracks `_prevMusicFileName` in main class, we track `_currentSongPath` in service

---

## 4. RESUME MUSIC LOGIC

### PlayniteSound:
```csharp
private void ResumeMusic()
{
    var shouldPlay = ShouldPlayMusic();
    var isLoaded = _musicPlayer?.IsLoaded == true;
    
    if (shouldPlay)
    {
        if (isLoaded)
        {
            _musicFader?.Resume();  // Uses fader
        }
        else
        {
            PlayMusicBasedOnSelected();  // Loads new music
        }
    }
}
```

### UniPlaySong:
```csharp
private void ResumeMusic()
{
    var shouldPlay = ShouldPlayMusic(settings);
    var isLoaded = _playbackService?.IsLoaded ?? false;
    
    // EXTRA: Silent skip handling (not in PlayniteSound)
    if (_isSilentSkipActive && isLoaded)
    {
        _playbackService.Resume();
        return;
    }
    
    if (_isSilentSkipActive && !isLoaded)
    {
        return;  // Don't start new music
    }
    
    if (shouldPlay)
    {
        if (isLoaded)
        {
            _playbackService.Resume();  // Direct resume, no fader
        }
        else
        {
            PlayMusicBasedOnSelected();
        }
    }
}
```

**CRITICAL DIFFERENCES**:
1. **MusicFader.Resume()**: PlayniteSound uses fader, we use direct resume
2. **Silent skip logic**: We have extra logic that PlayniteSound doesn't have
3. **ShouldPlayMusic parameter**: We pass settings, PlayniteSound uses property

---

## 5. PAUSE MUSIC LOGIC

### PlayniteSound:
```csharp
private void PauseMusic()
{
    var isLoaded = _musicPlayer?.IsLoaded == true;
    if (isLoaded)
    {
        _musicFader?.Pause();  // Uses fader
    }
}
```

### UniPlaySong:
```csharp
private void PauseMusic()
{
    var isLoaded = _playbackService?.IsLoaded ?? false;
    if (isLoaded)
    {
        _playbackService.Pause();  // Direct pause, no fader
    }
}
```

**DIFFERENCE**: PlayniteSound uses fader for pause, we use direct pause.

---

## 6. CLOSE MUSIC LOGIC

### PlayniteSound:
```csharp
private void CloseMusic()
{
    if (_musicPlayer?.IsLoaded == true)
    {
        _musicFader?.Switch(SubCloseMusic);  // Uses fader with fade-out
    }
}

private void SubCloseMusic()
{
    _musicPlayer.Close();
    SettingsModel.Settings.CurrentMusicName = string.Empty;
    _prevMusicFileName = string.Empty;  // CRITICAL: Clears tracking
}
```

### UniPlaySong:
```csharp
// In MusicPlaybackService:
public void Stop()
{
    _musicPlayer?.Stop();
    _musicPlayer?.Close();
    _currentSongPath = null;
    _currentGameId = null;
    _isPaused = false;
}
```

**CRITICAL DIFFERENCES**:
1. **Fader usage**: PlayniteSound fades out, we stop immediately
2. **Settings tracking**: PlayniteSound clears `CurrentMusicName` in settings, we don't
3. **File tracking**: Both clear, but PlayniteSound does it in `SubCloseMusic`

---

## 7. SHOULD PLAY MUSIC LOGIC

### PlayniteSound:
```csharp
private bool ShouldPlayMusic()
{
    return _musicPlayer != null
        && _pausers.Count == 0  // CRITICAL: Checks pausers
        && Settings.MusicVolume > 0
        && !Settings.VideoIsPlaying
        && !Settings.PreviewIsPlaying
        && !_gameRunning  // CRITICAL: Checks game running
        && ShouldPlayAudio(Settings.MusicState);
}
```

### UniPlaySong:
```csharp
private bool ShouldPlayMusic(UniPlaySongSettings settings)
{
    return _playbackService != null
        && 0 == 0  // We don't have pausers, so always 0
        && settings.MusicVolume > 0
        && !settings.VideoIsPlaying
        && false  // We don't track previewIsPlaying
        && false  // We don't track gameRunning
        && ShouldPlayAudio(settings);
}
```

**CRITICAL DIFFERENCES**:
1. **Pausers check**: PlayniteSound checks `_pausers.Count`, we hardcode 0
2. **Game running check**: PlayniteSound checks `_gameRunning`, we hardcode false
3. **Preview playing check**: PlayniteSound checks `PreviewIsPlaying`, we hardcode false

---

## 8. ON GAME SELECTED LOGIC

### PlayniteSound:
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var skipMusic = _firstSelect && Settings.SkipFirstSelectMusic;
    
    if (!skipMusic)
    {
        PlayMusicBasedOnSelected();
    }
    
    _firstSelect = false;  // ALWAYS cleared
}
```

### UniPlaySong:
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var skipMusic = _firstSelect && settings.SkipFirstSelectionAfterModeSwitch;
    
    // EXTRA: Silent skip clearing logic (not in PlayniteSound)
    if (settings.ThemeCompatibleSilentSkip)
    {
        if (_isSilentSkipActive || (_playbackService?.IsLoaded == true && !_firstSelect))
        {
            _isSilentSkipActive = false;
            _playbackService?.Stop();  // EXTRA: Explicit stop
        }
    }
    
    if (!skipMusic)
    {
        PlayMusicBasedOnSelected();
    }
    
    _firstSelect = false;  // ALWAYS cleared
}
```

**CRITICAL DIFFERENCES**:
1. **Silent skip logic**: We have extra logic that PlayniteSound doesn't have
2. **Explicit Stop()**: We call Stop() when clearing silent skip, PlayniteSound doesn't
3. **Setting name**: PlayniteSound uses `SkipFirstSelectMusic`, we use `SkipFirstSelectionAfterModeSwitch`

---

## 9. PLAY MUSIC FROM PATH LOGIC

### PlayniteSound:
```csharp
private void PlayMusicFromPath(string filePath)
{
    // Checks ReloadMusic flag
    if (!ReloadMusic && filePath == _prevMusicFileName)
    {
        // Just resume, don't reload
        _musicFader?.Switch(null, null, () => SubPlayMusicFromPath(filePath));
        return;
    }
    
    // Checks if directory changed or file is empty
    if (ReloadMusic || _prevMusicFileName.Equals(string.Empty) || filePath.Equals(string.Empty) ||
        (Path.GetDirectoryName(filePath) != Path.GetDirectoryName(_prevMusicFileName)))
    {
        if (File.Exists(filePath))
        {
            // Uses fader with close, preload, play sequence
            _musicFader?.Switch(
                SubCloseMusic,
                () => PreloadMusicFromPath(filePath),
                () => SubPlayMusicFromPath(filePath)
            );
        }
    }
}

private void SubPlayMusicFromPath(string filePath, TimeSpan startFrom = default)
{
    ReloadMusic = false;
    _prevMusicFileName = string.Empty;  // CLEAR FIRST
    if (File.Exists(filePath))
    {
        _prevMusicFileName = filePath;  // SET AFTER
        _musicPlayer.Load(filePath);
        _musicPlayer.Play(startFrom);
        _musicEnded = false;
        SettingsModel.Settings.CurrentMusicName = Path.GetFileNameWithoutExtension(filePath);
    }
}
```

### UniPlaySong:
```csharp
// In MusicPlaybackService.PlayGameMusic():
if (isNewGame || _currentSongPath != songToPlay || _currentSongPath == null)
{
    _musicPlayer?.Stop();
    _musicPlayer?.Close();
    _currentSongPath = null;  // CLEAR FIRST
    _isPaused = false;
}

_musicPlayer.Load(songToPlay);
_musicPlayer.Play();
_currentSongPath = songToPlay;  // SET AFTER
```

**CRITICAL DIFFERENCES**:
1. **ReloadMusic flag**: PlayniteSound checks this to force reload, we don't have it
2. **Directory comparison**: PlayniteSound compares directories, we compare full paths
3. **MusicFader**: PlayniteSound uses fader for transitions, we don't
4. **Preloading**: PlayniteSound preloads, we don't
5. **Settings tracking**: PlayniteSound sets `CurrentMusicName`, we don't
6. **MusicEnded tracking**: PlayniteSound clears `_musicEnded`, we don't track it

---

## 10. ON APPLICATION STARTED

### PlayniteSound:
```csharp
public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
{
    // One-time operations
    UpdateFromLegacyVersion();
    CopyAudioFiles();
    PlaySoundFileFromName(SoundFile.ApplicationStartedSound);
    // Registers event handlers
    // NO music loading here
}
```

### UniPlaySong:
```csharp
public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
{
    // EXTRA: Silent skip loading (not in PlayniteSound)
    if (settings.ThemeCompatibleSilentSkip && currentMode == ApplicationMode.Fullscreen)
    {
        _isSilentSkipActive = true;
        LoadSilentAudioFile();
    }
    // Suppresses native music
}
```

**DIFFERENCE**: We load silent file on startup, PlayniteSound doesn't.

---

## 11. ON SETTINGS CHANGED (VIDEO IS PLAYING)

### PlayniteSound:
```csharp
if (args.PropertyName == nameof(SettingsModel.Settings.VideoIsPlaying) ||
    args.PropertyName == nameof(SettingsModel.Settings.PreviewIsPlaying))
{
    if (videoPlaying || previewPlaying)
    {
        PauseMusic();
    }
    else
    {
        ResumeMusic();  // NO _firstSelect check
    }
}
```

### UniPlaySong:
```csharp
if (args.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
{
    if (videoPlaying)
    {
        PauseMusic();
    }
    else
    {
        ResumeMusic();  // NO _firstSelect check (matches PlayniteSound)
    }
}
```

**MATCHES**: Both call ResumeMusic without checking _firstSelect.

---

## 12. ON MAIN MODEL CHANGED

### PlayniteSound:
```csharp
public void OnMainModelChanged(object sender, PropertyChangedEventArgs args)
{
    // Logs property changes
    // NO automatic music triggering
    // Only handles GameDetailsVisible for UI updates
}
```

### UniPlaySong:
```csharp
public void OnMainModelChanged(object sender, PropertyChangedEventArgs args)
{
    // EXTRA: Silent skip clearing logic
    // EXTRA: ReplayMusic() calls when view changes
    // More complex logic than PlayniteSound
}
```

**DIFFERENCE**: We have more complex logic in OnMainModelChanged than PlayniteSound.

---

## 13. MEDIA ENDED HANDLING

### PlayniteSound:
```csharp
private void MediaEnded()
{
    _musicEnded = true;
    // Handles randomization and next song logic
    // Uses ReloadMusic flag
}
```

### UniPlaySong:
```csharp
// In MusicPlayer:
private void OnMediaEnded(object sender, EventArgs e)
{
    MediaEnded?.Invoke(sender, e);
}
// Handled in MusicPlaybackService, but we don't track _musicEnded
```

**DIFFERENCE**: PlayniteSound tracks `_musicEnded` for randomization logic, we don't.

---

## SUMMARY OF CRITICAL MISSING FEATURES

1. **MusicFader**: No fade transitions, direct stop/start
2. **ReloadMusic flag**: No way to force reload of same file
3. **Pausers set**: No multi-source pause tracking
4. **_musicEnded tracking**: No music end detection for randomization
5. **_gameRunning tracking**: No game running state
6. **Preloading**: No preload step before playing
7. **Settings.CurrentMusicName**: No UI tracking of current song
8. **PreviewIsPlaying**: No preview video tracking

---

## POTENTIAL ROOT CAUSE OF INITIAL GAME MUSIC NOT PLAYING

Based on the analysis, the most likely causes are:

1. **CRITICAL: WPF MediaPlayer.Open() is asynchronous** - We were calling `Play()` immediately after `Load()`, but the file might not be ready yet. **FIXED**: Now we wait for `MediaOpened` event before playing.
2. **No MusicFader**: Direct stop/start might cause timing issues
3. **Missing ReloadMusic flag**: Can't force reload when needed
4. **Silent skip interference**: Our extra logic might be preventing normal flow
5. **File tracking timing**: Clearing `_currentSongPath` before load might not be enough
6. **No preloading**: MediaPlayer might need time to prepare

---

## RECOMMENDED FIXES

1. **Add ReloadMusic flag** to force reload when needed
2. **Ensure proper file tracking** - clear before, set after (we do this, but verify timing)
3. **Simplify silent skip logic** - ensure it doesn't interfere with normal flow
4. **Add MediaPlayer event handlers** in main class to track state
5. **Consider adding a simple delay** after Close() before Load() to ensure MediaPlayer is ready

