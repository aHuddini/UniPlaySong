# Music Type System Analysis

## Discovery: PlayniteSound's Music Type System

### Key Finding
PlayniteSound uses a sophisticated music type system that dynamically determines which music to play based on:
1. **MusicType** setting (Game, Platform, Filter, Default)
2. **GameDetailsVisible** state (whether viewing game details or library list)
3. **DetailsMusicType** setting (for fullscreen game details view)

### How `ChoosenMusicType` Works

```csharp
public MusicType ChoosenMusicType
{
    get => !GameDetailsVisible ? MusicType : DetailsMusicType == MusicType.Same ? MusicType : DetailsMusicType;
}
```

**Logic:**
- When NOT in game details view: Uses `MusicType` (Game, Platform, Filter, Default)
- When IN game details view: Uses `DetailsMusicType` (can be "Same as in Game List" or different)

### Critical Flow: `OnMainModelChanged` → `ReplayMusic()`

```csharp
if (args.PropertyName == "GameDetailsVisible")
{
    SettingsModel.Settings.GameDetailsVisible = GetMainModel().GameDetailsVisible;
    if ( SettingsModel.Settings.DetailsMusicType != MusicType.Same
      && SettingsModel.Settings.DetailsMusicType != SettingsModel.Settings.MusicType)
    {
        ReplayMusic();  // ← This might be the key!
    }
}
```

**What this means:**
- When `GameDetailsVisible` changes, PlayniteSound checks if the music type would change
- If it would change, it calls `ReplayMusic()`
- `ReplayMusic()` goes through `ShouldPlayMusicOrClose()` → `ShouldPlayMusic()` → `ShouldPlayAudio()`
- `ShouldPlayAudio()` checks `_firstSelect` flag

### Why This Might Fix Our Issue

**Hypothesis:** When the library view becomes active after login:
1. `GameDetailsVisible` might change from `true` to `false` (or vice versa)
2. This triggers `OnMainModelChanged` with `GameDetailsVisible` property change
3. Even if `DetailsMusicType == Same`, there might be other state changes
4. OR: The view transition itself might trigger `ReplayMusic()` through a different path

### Current UniPlaySong Implementation

**What we have:**
- Only supports "Game" music type (music from game directory)
- No `GameDetailsVisible` tracking
- No `ReplayMusic()` method
- Fallback in `OnMainModelChanged` for `ActiveView` changes

**What we're missing:**
1. `GameDetailsVisible` property tracking
2. `OnMainModelChanged` handler for `GameDetailsVisible` changes
3. `ReplayMusic()` method that goes through gatekeeper
4. Music type system (Game, Platform, Filter, Default)

### Implementation Strategy

**Phase 1: Minimal Fix (Match PlayniteSound's Pattern)**
1. Track `GameDetailsVisible` in settings
2. Add `OnMainModelChanged` handler for `GameDetailsVisible` changes
3. Implement `ReplayMusic()` method that:
   - Checks `ShouldPlayMusic()` (which checks `_firstSelect`)
   - Calls `PlayGameMusic()` if music should play
4. This might trigger playback after login when view changes

**Phase 2: Full Music Type System (Future Enhancement)**
1. Add `MusicType` enum (Game, Platform, Filter, Default)
2. Add `DetailsMusicType` setting
3. Implement `ChoosenMusicType` computed property
4. Update `PlayMusicFromFirst()` to use `ChoosenMusicType`
5. Add UI for music type selection

### Next Steps

1. **Immediate:** Implement Phase 1 to match PlayniteSound's `GameDetailsVisible` tracking and `ReplayMusic()` pattern
2. **Test:** See if this fixes the login screen issue
3. **Future:** Consider implementing full music type system if needed

---

**Last Updated:** 2025-11-29  
**Status:** Analysis complete, ready for implementation

