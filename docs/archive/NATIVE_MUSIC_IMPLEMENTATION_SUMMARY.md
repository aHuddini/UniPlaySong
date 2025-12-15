# Native Music Implementation Summary - v1.0.5

**Date**: 2025-12-08  
**Status**: ✅ **COMPLETE - Working as Expected**

## Overview

The "Use Native Music as Default" feature allows the extension to use Playnite's default background music file as the extension's default music. This was implemented using a simple file-based approach.

## Key Discovery

**Playnite's "native" music is just a file path** - there's nothing built-in or special about it. Playnite loads the background music file from:
```
AppData\Local\Playnite\Themes\Fullscreen\Default\audio\background.*
```

The file is typically sourced from zapsplat.com and is just a regular audio file (mp3, ogg, wav, or flac).

## Implementation

### Simple File-Based Approach

1. **File Detection** (`PlayniteThemeHelper.FindBackgroundMusicFile()`):
   - Directly checks: `LocalApplicationData\Playnite\Themes\Fullscreen\Default\audio\background.{mp3|ogg|wav|flac}`
   - No reflection needed
   - No complex path resolution
   - Just 42 lines of code

2. **Settings Integration**:
   - When "Use Native Music as Default" is enabled:
     - Auto-detects the native music file path
     - Sets it as `DefaultMusicPath`
     - Enables `EnableDefaultMusic` automatically
     - Disables `SuppressPlayniteBackgroundMusic` (mutually exclusive)

3. **Suppression**:
   - Uses PlayniteSound's proven pattern:
     - Reflection to get `BackgroundMusic` property
     - `Mix_HaltMusic()` to stop playback
     - `Mix_FreeMusic()` to free resource
     - Set `BackgroundMusic` to `IntPtr.Zero` to prevent reload
   - Suppresses on startup and login screen when feature is enabled

4. **Playback**:
   - The native music file is treated like any other default music file
   - Uses the same playback system, faders, and position preservation
   - Works seamlessly with all existing features

## File Locations

- **Native Music File**: `%LOCALAPPDATA%\Playnite\Themes\Fullscreen\Default\audio\background.*`
- **Supported Extensions**: `.mp3`, `.ogg`, `.wav`, `.flac`

## Settings

- **`UseNativeMusicAsDefault`**: Checkbox to enable/disable the feature
- **Mutually Exclusive**: Cannot be enabled with `SuppressPlayniteBackgroundMusic`
- **Auto-Configuration**: Automatically sets `DefaultMusicPath` when enabled

## What Works

✅ Native music file detection  
✅ Auto-configuration when checkbox is enabled  
✅ Suppression on startup and login screen  
✅ Using native music as default music  
✅ Settings persistence  
✅ Seamless integration with existing playback system  

## Code Files

- **`PlayniteThemeHelper.cs`**: Simple 42-line helper for file detection
- **`UniPlaySongSettingsViewModel.cs`**: Settings UI and auto-configuration logic
- **`SettingsService.cs`**: Path validation and persistence
- **`MusicPlaybackService.cs`**: Playback logic (treats native music like any default music)
- **`UniPlaySong.cs`**: Suppression logic using PlayniteSound's pattern

## Lessons Learned

1. **Simplicity Wins**: The initial complex reflection-based approach was over-engineered. A simple file path check works perfectly.

2. **Playnite's Structure**: Playnite stores themes in `LocalApplicationData` (not `Roaming`), which was discovered during development.

3. **No Special SDK Support Needed**: Playnite's "native" music is just a file - we can use it like any other audio file.

4. **File-Based is Better**: Instead of trying to control Playnite's music playback, we just:
   - Find the file it uses
   - Play it ourselves
   - Suppress Playnite's separate playback

## Future Improvements

- Consider adding support for custom themes (not just "Default")
- Could add fallback to check installation directory if Local AppData doesn't have the file
- Could cache the file path to avoid repeated file system checks

