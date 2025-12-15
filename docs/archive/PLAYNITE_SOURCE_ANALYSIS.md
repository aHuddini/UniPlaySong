# Playnite Source Code Analysis & Refactoring Plan

**Date**: 2025-12-07  
**Purpose**: Analyze Playnite's native music implementation and identify simplification/refactoring opportunities

## Key Findings from Playnite's Source Code

### 1. Playnite's Native Music Initialization Pattern

Based on Playnite's `FullscreenApplication.cs` and `AudioEngine.cs`:

```csharp
// Simplified pattern Playnite uses:
1. InitializeAudio() is called once during app startup
2. LoadMusic() loads the file and returns IntPtr
3. BackgroundMusic static property stores the IntPtr
4. PlayMusic(BackgroundMusic) starts playback
5. SetMusicVolume() sets volume
```

**Key Insights:**
- Playnite loads music **once** during initialization
- `BackgroundMusic` is a **static property** (not instance)
- `Audio` is a **static AudioEngine** property
- `PlayMusic()` automatically handles `AudioClosed` by calling `OpenAudio()` if needed
- `GetIsMusicPlaying()` returns `false` if `AudioInitialized` is `false`

### 2. Playnite's Event Handling

Playnite uses **property change notifications** for audio control:
- `FullscreenAppViewModel` watches `BackgroundVolume` property changes
- When volume changes, it checks if music is playing and restarts if needed
- Uses simple boolean checks: `if (!audio.GetIsMusicPlaying()) { audio.PlayMusic(...); }`

**Our Current Approach:**
- We use complex retry logic with multiple verification attempts
- We check state multiple times with delays
- We have nested timer logic

**Simplification Opportunity:**
- Follow Playnite's pattern: **simple check, then play**
- Remove complex retry logic - if `PlayMusic()` fails, it's likely a deeper issue
- Trust Playnite's `PlayMusic()` to handle audio state

### 3. Reflection Complexity

**Current Issues:**
- We walk up inheritance chain to find `FullscreenApplication` type
- Multiple reflection attempts with different binding flags
- Complex property access patterns

**Playnite's Pattern:**
- Direct static property access: `FullscreenApplication.BackgroundMusic`
- Direct static method calls: `FullscreenApplication.Audio.PlayMusic(...)`

**Simplification Opportunity:**
- Cache the `Type` object after first discovery
- Cache property getters/setters
- Reduce reflection calls by storing references

## Refactoring Opportunities

### 1. Simplify `AllowNativeMusic()` Method

**Current Issues:**
- 200+ lines of code
- Multiple nested try-catch blocks
- Complex retry logic with delays
- Redundant state checks

**Proposed Simplification:**

```csharp
private void AllowNativeMusic()
{
    if (!IsFullscreen) return;
    
    // Simple settings check
    bool shouldRestore = _settings?.SuppressPlayniteBackgroundMusic == false ||
                         (_settings?.EnableDefaultMusic == true && 
                          _settings?.UseNativeMusicAsDefault == true);
    if (!shouldRestore) return;

    try
    {
        var audio = GetAudioEngine();
        var bgMusic = GetBackgroundMusic();
        
        if (audio == null || !audio.AudioInitialized)
        {
            _fileLogger?.Warn("AllowNativeMusic: Audio not initialized");
            return;
        }

        // Reload if needed (simple check)
        if (bgMusic == IntPtr.Zero)
        {
            bgMusic = ReloadBackgroundMusic(audio);
            if (bgMusic == IntPtr.Zero) return;
        }

        // Play if not playing (Playnite's pattern)
        if (!audio.GetIsMusicPlaying() && bgMusic != IntPtr.Zero)
        {
            var volume = GetBackgroundVolume();
            audio.PlayMusic(bgMusic);
            audio.SetMusicVolume(volume);
        }
    }
    catch (Exception ex)
    {
        _fileLogger?.Error(ex, "AllowNativeMusic: Error restoring native music");
    }
}
```

**Benefits:**
- Reduces from 200+ lines to ~40 lines
- Follows Playnite's simple pattern
- Removes complex retry logic
- Easier to debug and maintain

### 2. Cache Reflection Results

**Current Issue:**
- Every call to `AllowNativeMusic()` or `SuppressNativeMusic()` does full reflection lookup

**Proposed Solution:**

```csharp
private static Type _fullscreenApplicationType;
private static PropertyInfo _backgroundMusicProperty;
private static PropertyInfo _audioProperty;

private void CacheReflectionTypes()
{
    if (_fullscreenApplicationType != null) return;
    
    var mainModel = GetMainModel();
    var appType = mainModel?.App?.GetType();
    
    // Walk up inheritance chain once
    while (appType != null && appType.Name != "FullscreenApplication")
    {
        appType = appType.BaseType;
    }
    
    if (appType != null)
    {
        _fullscreenApplicationType = appType;
        _backgroundMusicProperty = appType.GetProperty("BackgroundMusic", 
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        _audioProperty = appType.GetProperty("Audio", 
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }
}
```

**Benefits:**
- Reflection happens once at startup
- Subsequent calls are fast property access
- Reduces overhead significantly

### 3. Simplify Startup Suppression

**Current Issue:**
- Nested timers with retry logic
- Complex delayed execution

**Proposed Simplification:**

```csharp
public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
{
    if (IsFullscreen && _settings?.SuppressPlayniteBackgroundMusic == true)
    {
        bool usingNativeAsDefault = _settings?.EnableDefaultMusic == true && 
                                   _settings?.UseNativeMusicAsDefault == true;
        
        if (!usingNativeAsDefault)
        {
            // Single delayed suppression (Playnite's audio initializes quickly)
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                System.Threading.Thread.Sleep(500); // Brief delay for audio init
                SuppressNativeMusic();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
```

**Benefits:**
- Removes nested timer complexity
- Uses WPF dispatcher (more appropriate for UI thread)
- Single attempt (if it fails, audio wasn't ready - that's okay)

### 4. Extract Helper Methods

**Current Issue:**
- `AllowNativeMusic()` does too many things

**Proposed Extraction:**

```csharp
private dynamic GetAudioEngine()
{
    CacheReflectionTypes();
    return _audioProperty?.GetValue(null);
}

private IntPtr GetBackgroundMusic()
{
    CacheReflectionTypes();
    return (IntPtr)(_backgroundMusicProperty?.GetValue(null) ?? IntPtr.Zero);
}

private IntPtr ReloadBackgroundMusic(dynamic audio)
{
    string filePath = FindBackgroundMusicFile();
    if (string.IsNullOrEmpty(filePath)) return IntPtr.Zero;
    
    IntPtr newMusic = audio.LoadMusic(filePath);
    if (newMusic != IntPtr.Zero)
    {
        // Free old music
        IntPtr oldMusic = GetBackgroundMusic();
        if (oldMusic != IntPtr.Zero)
        {
            try { SDL_mixer.Mix_FreeMusic(oldMusic); } catch { }
        }
        
        // Set new music
        _backgroundMusicProperty?.GetSetMethod(true)?.Invoke(null, new object[] { newMusic });
    }
    
    return newMusic;
}

private float GetBackgroundVolume()
{
    try
    {
        var fsSettings = _api.ApplicationSettings.Fullscreen;
        var settingsObj = fsSettings?.GetType()
            .GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(fsSettings);
        return (float)(settingsObj?.GetType().GetProperty("BackgroundVolume")?.GetValue(settingsObj) ?? 0.55f);
    }
    catch
    {
        return 0.55f; // Default volume
    }
}
```

**Benefits:**
- Each method has single responsibility
- Easier to test
- Easier to debug
- Reusable

## Critical Functions to NOT Touch

Based on user feedback, these functions work correctly and should not be modified:

1. **`MusicPlaybackService.PlayGameMusic()`** - Core playback logic
2. **`MusicPlaybackService.FadeOutAndStop()`** - Volume fade logic
3. **`MusicPlaybackCoordinator.HandleGameSelected()`** - Game selection coordination
4. **Event handlers in `UniPlaySong.cs`** - `OnGameSelected`, etc.

## Implementation Priority

### Phase 1: Safe Refactoring (Low Risk)
1. ✅ Extract helper methods from `AllowNativeMusic()`
2. ✅ Cache reflection results
3. ✅ Simplify startup suppression

### Phase 2: Pattern Alignment (Medium Risk)
1. ⚠️ Simplify `AllowNativeMusic()` to match Playnite's pattern
2. ⚠️ Remove complex retry logic
3. ⚠️ Test thoroughly with native music scenarios

### Phase 3: Code Cleanup (Low Risk)
1. ✅ Remove redundant logging
2. ✅ Consolidate duplicate code
3. ✅ Improve error messages

## Testing Strategy

After refactoring, test:
1. ✅ Native music suppression on startup
2. ✅ Native music restoration after login bypass
3. ✅ Native music as default when no game music
4. ✅ Switching between games with/without music
5. ✅ Volume control and fade transitions
6. ✅ No regression in existing music playback features

## References

- Playnite Repository: https://github.com/JosefNemec/Playnite
- FullscreenApplication.cs: https://github.com/JosefNemec/Playnite/blob/master/source/Playnite.FullscreenApp/FullscreenApplication.cs
- AudioEngine.cs: https://github.com/JosefNemec/Playnite/blob/master/source/Playnite/AudioEngine.cs
- FullscreenAppViewModel.cs: https://github.com/JosefNemec/Playnite/blob/master/source/Playnite.FullscreenApp/ViewModels/FullscreenAppViewModel.cs

