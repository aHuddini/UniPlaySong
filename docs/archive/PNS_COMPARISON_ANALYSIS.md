# PlayniteSound vs UniPlaySong - Comprehensive Comparison Analysis

**Date**: 2025-11-29  
**Purpose**: Identify all differences between PlayniteSound and UniPlaySong that could affect music playback behavior

---

## Key Differences Found

### 1. **SelectedGames Property**

**PlayniteSound:**
```csharp
private IEnumerable<Game> SelectedGames => PlayniteApi.MainView.SelectedGames;
```

**UniPlaySong (FIXED):**
```csharp
// Match PlayniteSound: use property for SelectedGames
private IEnumerable<Game> SelectedGames => playniteApi.MainView.SelectedGames;
```

**Why This Matters**:
- **Property evaluation**: The property is evaluated each time it's accessed, ensuring we always get the latest selected games from Playnite's MainView
- **Consistency**: Using a property ensures consistent access pattern throughout the codebase
- **Thread safety**: Properties can be safer in multi-threaded scenarios (though Playnite's API should handle this)
- **Debugging**: Easier to add breakpoints/logging in one place if needed
- **FIXED**: Now matches PlayniteSound exactly - all direct accesses replaced with property

---

### 2. **PlayMusicFromFirstSelected vs Direct PlayGameMusic**

**PlayniteSound:**
```csharp
PlayMusicFromFirstSelected() => PlayMusicFromFirst(SelectedGames)

PlayMusicFromFirst(IEnumerable<Game> games = null)
{
    var game = games.FirstOrDefault();
    // Complex logic for MusicType (Game/Platform/Filter/Default)
    // Handles backup music
    // Calls PlayMusicFromFiles(files)
}
```

**UniPlaySong:**
```csharp
// Directly calls:
_playbackService?.PlayGameMusic(game, settings);
```

**Impact**: PlayniteSound has a more complex path that handles MusicType selection, backup music, and file collection. We're using a simpler direct approach. This should be fine for our use case (we only support Game music type).

---

### 3. **ReplayMusic Implementation**

**PlayniteSound:**
```csharp
public void ReplayMusic()
{
    var singleGame = SingleGame();
    if (singleGame)
    {
        var shouldPlay = ShouldPlayMusicOrClose();  // Uses ShouldPlayMusicOrClose
        if (shouldPlay)
        {
            PlayMusicFromFirstSelected();
        }
    }
}
```

**UniPlaySong:**
```csharp
public void ReplayMusic()
{
    var singleGame = selectedGamesCount == 1;
    if (singleGame)
    {
        var shouldPlay = ShouldPlayMusic(settings);  // Uses ShouldPlayMusic (NOT ShouldPlayMusicOrClose)
        if (shouldPlay)
        {
            _playbackService?.PlayGameMusic(game, settings);
        }
    }
}
```

**CRITICAL DIFFERENCE**: 
- PlayniteSound uses `ShouldPlayMusicOrClose()` which will STOP music if it shouldn't play
- UniPlaySong uses `ShouldPlayMusic()` which only checks, doesn't stop

**Impact**: This could cause music to continue playing when it shouldn't, or not stop properly when conditions change.

---

### 4. **PlayMusicFromFiles Logic**

**PlayniteSound:**
```csharp
private string PlayMusicFromFiles(List<string> musicFiles)
{
    if (musicFiles == null || musicFiles.Count == 0)
    {
        CloseMusic();  // Stops music if no files
        return string.Empty;
    }
    
    // Complex logic for:
    // - Primary song selection
    // - Directory change detection
    // - Randomization
    // - Song continuation
}
```

**UniPlaySong:**
```csharp
// In MusicPlaybackService.PlayGameMusic():
if (songs.Count == 0)
{
    if (_currentGameId != gameId)
    {
        Stop();  // Only stops if switching games
    }
    return;
}
```

**CRITICAL DIFFERENCE**:
- PlayniteSound ALWAYS stops music if no files found
- UniPlaySong only stops if switching to a different game

**Impact**: If a game has no music files, PlayniteSound stops music immediately. We might keep playing previous game's music.

---

### 5. **PlayMusicFromFirst - No Files Handling**

**PlayniteSound:**
```csharp
private void PlayMusicFromFirst(IEnumerable<Game> games = null)
{
    // ... get files ...
    
    // If no files found and we're switching games, stop current music
    if (!files.Any() && game != null)
    {
        var currentGameDirectory = GetMusicDirectoryPath(game);
        // Only stop if this is a different game (different directory) than what's currently playing
        if (!string.IsNullOrEmpty(_prevMusicFileName) && 
            Path.GetDirectoryName(_prevMusicFileName) != currentGameDirectory)
        {
            CloseMusic();
            return;
        }
    }
    
    PlayMusicFromFiles(files);
}
```

**UniPlaySong:**
```csharp
// In PlayGameMusic():
if (songs.Count == 0)
{
    if (_currentGameId != gameId)
    {
        Stop();
    }
    return;  // Returns early, doesn't call PlayMusicFromFiles equivalent
}
```

**Impact**: Similar logic, but PlayniteSound's is more sophisticated - it checks directory paths, not just game IDs.

---

### 6. **MediaElementsMonitor Timer Management**

**PlayniteSound:**
```csharp
static private void MediaElement_Opened(object sender, RoutedEventArgs e)
{
    Timer_Tick(sender, e);
    timer.Start();  // Always starts, no check
}
```

**UniPlaySong (FIXED):**
```csharp
static private void MediaElement_Opened(object sender, RoutedEventArgs e)
{
    Timer_Tick(sender, e);
    timer.Start();  // Match PlayniteSound: always start (no check)
}
```

**Why This Matters**: 
- PlayniteSound always starts the timer to ensure it's running whenever a MediaElement is detected
- If the timer gets stopped (e.g., when all media elements are removed), we need to restart it immediately when a new one appears
- Checking `IsEnabled` could miss edge cases where the timer state is inconsistent
- **FIXED**: Now matches PlayniteSound exactly

---

### 7. **OnGameSelected - No Additional Logic**

**PlayniteSound:**
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var skipMusic = _firstSelect && Settings.SkipFirstSelectMusic;
    
    if (!skipMusic)
    {
        PlayMusicBasedOnSelected();  // That's it - no extra logic
    }
    
    _firstSelect = false;  // Always cleared
}
```

**UniPlaySong:**
```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var skipMusic = _firstSelect && settings.SkipFirstSelectionAfterModeSwitch;
    
    if (!skipMusic)
    {
        // EXTRA LOGIC: Clear silent skip if needed
        if (settings.ThemeCompatibleSilentSkip)
        {
            if (_isSilentSkipActive || (_playbackService?.IsLoaded == true && !_firstSelect))
            {
                _isSilentSkipActive = false;
                // Don't call Stop() - let gatekeepers handle it
            }
        }
        
        PlayMusicBasedOnSelected();
    }
    
    _firstSelect = false;  // Always cleared
}
```

**Impact**: Our extra silent skip logic might be interfering. We should ensure it doesn't prevent music from playing.

---

## Critical Issues Fixed

### ✅ Issue 1: ReplayMusic Uses Wrong Gatekeeper
**Problem**: `ReplayMusic()` uses `ShouldPlayMusic()` instead of `ShouldPlayMusicOrClose()`
**Fix**: Changed to use `ShouldPlayMusicOrClose()` to match PlayniteSound

### ✅ Issue 2: No Files Handling
**Problem**: When a game has no music files, we might not stop music properly
**Fix**: Always stop music when no files found (like PlayniteSound does)

### ✅ Issue 3: Silent Skip Tracking
**Problem**: Silent file was being tracked in `_currentSongPath`, interfering with real game music detection
**Fix**: `LoadAndPlayFile()` now does NOT set `_currentSongPath` or `_currentGameId` for silent files, ensuring proper detection when real music starts

### ✅ Issue 4: SelectedGames Property
**Problem**: Direct access to `playniteApi.MainView.SelectedGames` instead of using property
**Fix**: Added `SelectedGames` property matching PlayniteSound, replaced all direct accesses

### ✅ Issue 5: MediaElementsMonitor Timer
**Problem**: Timer check before starting could miss edge cases
**Fix**: Always start timer (no check) to match PlayniteSound exactly

---

## Recommendations

1. **Fix ReplayMusic**: Use `ShouldPlayMusicOrClose()` instead of `ShouldPlayMusic()`
2. **Fix No Files Handling**: Always stop music when no files found for a game
3. **Simplify Silent Skip**: Only clear the flag, don't try to stop music manually - let gatekeepers handle it
4. **Match PlayMusicFromFiles Logic**: Ensure we handle empty file lists the same way PlayniteSound does

