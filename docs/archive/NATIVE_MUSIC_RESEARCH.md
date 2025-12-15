# Native Background Music Research

**Date**: 2025-12-07  
**Status**: ✅ **SOLUTION IMPLEMENTED**

## Problem Statement

When `CompatibleFullscreenNativeBackground` is enabled, we need to:
1. Suppress Playnite's native background music during login/startup
2. Restore it after login bypass so it plays when no game music is available

**Current Issue**: Once we suppress native music (set `BackgroundMusic` to `IntPtr.Zero`), Playnite doesn't automatically reload it. We need to understand how Playnite loads it initially so we can reload it the same way.

## Playnite Source Code References

Based on GitHub Copilot analysis of [Playnite repository](https://github.com/JosefNemec/Playnite):

### Key Files to Study:

1. **`source/Playnite.FullscreenApp/FullscreenApplication.cs`**
   - Contains `BackgroundMusic` field/usage
   - Code that plays/pauses/resumes/disposes background music
   - References: `Audio.PlayMusic(BackgroundMusic)`, `Audio.PauseMusic()`, `Audio.SetMusicVolume(...)`, `Audio.DisposeMusic(BackgroundMusic)`
   - Link: https://github.com/JosefNemec/Playnite/blob/3297aeb9e0f74b8b33bb3432b1c120d2e31b6222/source/Playnite.FullscreenApp/FullscreenApplication.cs#L141-L270

2. **`source/Playnite.FullscreenApp/ViewModels/FullscreenAppViewModel.cs`**
   - Settings handlers for background music behavior
   - Controls: `BackgroundVolume`, `IsMusicMuted`
   - Calls: `audio.PlayMusic(FullscreenApplication.BackgroundMusic)`, `audio.StopMusic()`, `audio.SetMusicVolume(...)`, `audio.ResumeMusic()`
   - Link: https://github.com/JosefNemec/Playnite/blob/3297aeb9e0f74b8b33bb3432b1c120d2e31b6222/source/Playnite.FullscreenApp/ViewModels/FullscreenAppViewModel.cs#L549-L662

3. **`source/Playnite/license.txt`**
   - Attribution for default fullscreen theme background music (source: zapsplat)
   - Link: https://github.com/JosefNemec/Playnite/blob/3297aeb9e0f74b8b33bb3432b1c120d2e31b6222/source/Playnite/license.txt#L1-L80

## Research Questions

1. **How does Playnite load background music initially?**
   - Where is the music file path stored?
   - When is `BackgroundMusic` property set?
   - What triggers the initial load?

2. **How can we reload background music after suppression?**
   - Can we call `Audio.PlayMusic()` with the original `BackgroundMusic` IntPtr?
   - Do we need to reload the file and create a new IntPtr?
   - Is there a method to reload/reset background music?

3. **What is the Audio class interface?**
   - What methods does it expose?
   - How does `PlayMusic()` work?
   - Can we access the original music file path?

## Current Implementation

### Suppression (Working)
```csharp
private void SuppressNativeMusic()
{
    // Get BackgroundMusic property
    dynamic bgMusicProp = mainModel.App?.GetType().GetProperty("BackgroundMusic");
    IntPtr currentMusic = (IntPtr)bgMusicProp.GetValue(null);
    
    if (currentMusic != IntPtr.Zero)
    {
        SDL_mixer.Mix_HaltMusic();
        SDL_mixer.Mix_FreeMusic(currentMusic);
        bgMusicProp.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
    }
}
```

### Restoration (Not Working)
```csharp
private void AllowNativeMusic()
{
    // Currently just logs - can't force Playnite to reload
    // Need to find how Playnite loads it initially
}
```

## Next Steps

1. **Examine FullscreenApplication.cs** to understand:
   - How `BackgroundMusic` is initialized
   - Where the music file path comes from
   - How it's loaded into an IntPtr

2. **Examine FullscreenAppViewModel.cs** to understand:
   - How `PlayMusic()` is called
   - What triggers music playback
   - How settings affect music behavior

3. **Find Audio class implementation** to understand:
   - How `PlayMusic()` works
   - If we can reload music programmatically
   - What parameters it needs

4. **Test restoration approach**:
   - Try calling `Audio.PlayMusic()` with original IntPtr (if we save it)
   - Try reloading the music file and setting BackgroundMusic property
   - Try triggering Playnite's internal reload mechanism

## Potential Solutions

### Option 1: Save and Restore IntPtr
- Save the original `BackgroundMusic` IntPtr before suppressing
- Restore it by setting the property back to the saved IntPtr
- **Risk**: IntPtr might become invalid after `Mix_FreeMusic()`

### Option 2: Reload Music File
- Find where Playnite stores the background music file path
- Load it using SDL2_mixer: `SDL2Mixer.Mix_LoadMUS(filePath)`
- Set the new IntPtr to `BackgroundMusic` property
- Call `Audio.PlayMusic()` to start playback
- **Risk**: Need to find the file path and Audio class interface

### Option 3: Trigger Playnite's Reload
- Find what triggers Playnite to reload background music
- Simulate that trigger (e.g., view change, property change)
- **Risk**: May not exist or may be complex

## ✅ BREAKTHROUGH: Analysis of FullscreenApplication.cs

**Key Discoveries:**

1. **BackgroundMusic is static**: `public static IntPtr BackgroundMusic { get; private set; }`
   - Accessed via: `FullscreenApplication.BackgroundMusic`

2. **Audio is static AudioEngine**: `public static AudioEngine Audio { get; private set; }`
   - Accessed via: `FullscreenApplication.Audio`

3. **How Playnite loads background music** (from `InitializeAudio()` method):
   ```csharp
   var backgroundSoundPath = ThemeFile.GetFilePath(
       $@"audio\\background\.({AudioEngine.SupportedFileTypesRegex})", 
       matchByRegex: true
   );
   
   if (!backgroundSoundPath.IsNullOrEmpty())
   {
       BackgroundMusic = Audio.LoadMusic(backgroundSoundPath);
       Audio.SetMusicVolume(AppSettings.Fullscreen.BackgroundVolume);
       if (Current.AppSettings.Fullscreen.BackgroundVolume > 0)
       {
           Audio.PlayMusic(BackgroundMusic);
       }
   }
   ```

4. **Critical insight from `FullscreenApplication_PropertyChanged`:**
   - When app becomes active and `MuteInBackground` is true:
   - If `Audio.AudioClosed`, it calls `Audio.PlayMusic(BackgroundMusic)` again!
   - **This proves we can reload it!**

5. **Solution: Reload the music file**
   - Find file using `ThemeFile.GetFilePath()` (same pattern as Playnite)
   - Reload using `Audio.LoadMusic(filePath)`
   - Set `BackgroundMusic` property back
   - Call `Audio.PlayMusic(BackgroundMusic)`
   - Set volume: `Audio.SetMusicVolume(AppSettings.Fullscreen.BackgroundVolume)`

## Key Findings from PlayniteSound

PlayniteSound (PNS) **only suppresses, never restores** native background music:
- They call `SupressNativeFulscreenMusic()` which sets `BackgroundMusic` to `IntPtr.Zero`
- They **never** restore it - once suppressed, it stays suppressed
- This confirms that restoration is a challenge, but **we now know how to do it!**

## Critical Research Needed

To properly restore native music, we need to understand:

1. **How Playnite loads BackgroundMusic initially:**
   - Is `BackgroundMusic` a static field in `FullscreenApplication`?
   - Where does the music file path come from?
   - When is it loaded (constructor, OnLoaded, etc.)?

2. **Audio class interface:**
   - What is the full signature of `Audio.PlayMusic()`?
   - Does it take an IntPtr or a file path?
   - Can we call it directly to reload music?

3. **File path location:**
   - Where does Playnite store the background music file?
   - Is it in the theme directory?
   - Can we find it programmatically?

## Potential Solution: Save IntPtr Before Suppressing

**Key Insight**: Instead of freeing the music, we could:
1. Save the original `BackgroundMusic` IntPtr before suppressing
2. **Don't call `Mix_FreeMusic()`** - just halt it
3. Restore by setting `BackgroundMusic` back to saved IntPtr
4. Call `Audio.PlayMusic()` or `Audio.ResumeMusic()` to restart

**Risk**: If we don't free it, we might leak memory. But if Playnite manages it, this might work.

## Alternative: Find and Reload File

If we can find the music file path:
1. Search theme directories for `background.*` files (mp3, ogg, wav, flac)
2. Load it using `SDL2Mixer.Mix_LoadMUS(filePath)`
3. Set the new IntPtr to `BackgroundMusic` property
4. Call `Audio.PlayMusic()` to start playback

## ✅ Key Insights from AudioEngine.cs

**Critical findings from Playnite's AudioEngine implementation:**

1. **`AudioInitialized` property**: Must be `true` for `LoadMusic()` and `GetIsMusicPlaying()` to work
   - `LoadMusic()` returns `IntPtr.Zero` if `AudioInitialized` is `false`
   - `GetIsMusicPlaying()` returns `false` if `AudioInitialized` is `false`

2. **`AudioClosed` property**: Audio can be closed and reopened
   - `PlayMusic()` automatically calls `OpenAudio()` if `AudioClosed` is `true`
   - `ResumeMusic()` also automatically calls `OpenAudio()` if `AudioClosed` is `true`
   - We don't need to manually check `AudioClosed` before calling `PlayMusic()`

3. **`PlayMusic()` behavior**:
   - Returns early if `music == IntPtr.Zero`
   - Returns early if `AudioInitialized` is `false`
   - Automatically reopens audio if `AudioClosed` is `true`
   - Calls `Mix_PlayMusic(music, -1)` to play in a loop

4. **`LoadMusic()` behavior**:
   - Returns `IntPtr.Zero` if `AudioInitialized` is `false`
   - Returns `IntPtr.Zero` if file doesn't exist or can't be loaded
   - Calls `Mix_LoadMUS(path)` internally

**Implementation updates:**
- Check `AudioInitialized` before attempting to load music
- Log audio state for debugging
- `PlayMusic()` handles `AudioClosed` automatically, so we don't need to check it

## ✅ Implementation Solution

**Implemented in `UniPlaySong.cs` - `AllowNativeMusic()` method:**

1. **Access AudioEngine**: Get static `Audio` field from `FullscreenApplication` type
2. **Access BackgroundMusic**: Get static `BackgroundMusic` property
3. **Find Music File**: Search theme directory for `background.*` files (mp3, ogg, wav, flac)
4. **Reload Music**: Call `Audio.LoadMusic(filePath)` to create new IntPtr
5. **Restore Property**: Set `BackgroundMusic` property to new IntPtr
6. **Play Music**: Call `Audio.PlayMusic(BackgroundMusic)` and set volume

**Key Methods:**
- `AllowNativeMusic()`: Main restoration logic
- `FindBackgroundMusicFile()`: Searches theme audio directory
- `GetCurrentThemePath()`: Gets current fullscreen theme path from Playnite settings

**File Location (Simplified - v1.0.5)**:
- **Primary Location**: `%LOCALAPPDATA%\Playnite\Themes\Fullscreen\Default\audio\background.*`
  - Example: `C:\Users\{User}\AppData\Local\Playnite\Themes\Fullscreen\Default\audio\background.mp3`
- **Supported Extensions**: `.mp3`, `.ogg`, `.wav`, `.flac`
- **Note**: Playnite stores themes in `LocalApplicationData` (not `Roaming`), which was discovered during v1.0.5 development

## References

- Playnite Repository: https://github.com/JosefNemec/Playnite
- Code Search: https://github.com/JosefNemec/Playnite/search?q=background+music&type=code
- FullscreenApplication.cs: https://github.com/JosefNemec/Playnite/blob/master/source/Playnite.FullscreenApp/FullscreenApplication.cs
- FullscreenAppViewModel.cs: https://github.com/JosefNemec/Playnite/blob/master/source/Playnite.FullscreenApp/ViewModels/FullscreenAppViewModel.cs
- PlayniteSound (reference implementation): https://github.com/joyrider3774/PlayniteSound

