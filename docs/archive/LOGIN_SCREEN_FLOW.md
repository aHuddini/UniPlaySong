# Login Screen Music Flow: How It Works

## The Problem

When switching to fullscreen mode (especially with themes like ANIKI REMAKE that have login screens), music should:
1. **NOT play** during the login screen transition
2. **DO play** immediately after passing the login screen and reaching the library view

## PlayniteSound's Solution: Simple Flag with Immediate Clear

### The Flow

```
Timeline:
┌─────────────────────────────────────────────────────────────┐
│ 1. User switches to fullscreen mode                         │
│    → Login screen appears                                    │
│    → First OnGameSelected fires (game auto-selected)        │
│    → _firstSelect = true                                     │
│    → Music SKIPPED (because _firstSelect = true)            │
│    → _firstSelect = false (IMMEDIATELY cleared)              │
├─────────────────────────────────────────────────────────────┤
│ 2. User passes login screen                                  │
│    → Library view appears                                    │
│    → User selects game (or same game still selected)        │
│    → OnGameSelected fires AGAIN                              │
│    → _firstSelect = false (already cleared)                  │
│    → Music PLAYS (because _firstSelect = false)             │
└─────────────────────────────────────────────────────────────┘
```

### Key Code Pattern

```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var game = args?.NewValue?.FirstOrDefault();
    
    // First check: Skip if first select AND setting enabled
    if (!(_firstSelect && Settings.SkipFirstSelectMusic))
    {
        PlayMusicBasedOnSelected();  // This goes through ShouldPlayAudio() too
    }
    
    // CRITICAL: Clear flag IMMEDIATELY after check
    _firstSelect = false;  // ← This ensures next selection will play
}
```

### Why This Works

1. **Flag is cleared immediately**: After the first `OnGameSelected` (during login), `_firstSelect` is set to `false` right away
2. **Next selection plays**: When `OnGameSelected` fires again (after login), `_firstSelect` is already `false`, so music plays
3. **No view detection needed**: The system relies on the natural flow of `OnGameSelected` events, not view detection

### What Triggers the Second OnGameSelected?

The second `OnGameSelected` can be triggered by:

1. **User manually selects a game** after passing login screen
2. **Theme re-selects the game** when library view loads (many themes do this)
3. **Selection changes** when transitioning from login to library view
4. **Game details view opens** (some themes trigger selection events)

**Important**: Even if the same game is still selected, many themes will fire `OnGameSelected` again when the library view becomes active, ensuring music plays.

## UniPlaySong's Current Problem

UniPlaySong tries to detect login screens using view names, which fails because:
- `GetMainModel()` returns `null` during login screen
- View name is "unknown" during transitions
- Timing is unpredictable

### Current (Broken) Flow

```
1. OnGameSelected fires during login
   → activeViewName = "unknown" (GetMainModel() returns null)
   → isLibraryView = false
   → shouldPlayMusic = false
   → Music skipped
   → _firstSelect = false
   → _lastSkippedGameId = gameId (tracks skipped game)

2. User passes login screen
   → OnMainModelPropertyChanged fires (ActiveView changed)
   → Tries to detect if same game is selected
   → Complex logic to play skipped game
   → May or may not work depending on timing
```

**Problem**: This relies on view detection and property change events, which are unreliable.

## Recommended Fix: Match PlayniteSound's Pattern

### Simplified Flow

```
1. OnGameSelected fires during login
   → _firstSelect = true
   → Music SKIPPED (simple check)
   → _firstSelect = false (IMMEDIATELY cleared)

2. User passes login screen
   → OnGameSelected fires again (naturally)
   → _firstSelect = false (already cleared)
   → Music PLAYS (simple check passes)
```

### Simplified Code

```csharp
public override void OnGameSelected(OnGameSelectedEventArgs args)
{
    var games = args?.NewValue;
    var game = games?.FirstOrDefault();
    
    if (game == null || games == null || games.Count == 0)
    {
        _playbackService?.Stop();
        _firstSelect = false;
        return;
    }
    
    var settings = LoadPluginSettings<UniPlaySongSettings>();
    if (settings == null || !settings.EnableMusic)
    {
        _playbackService?.Stop();
        _firstSelect = false;
        return;
    }
    
    // Simple check: Skip first selection if setting enabled
    if (!(_firstSelect && settings.SkipFirstSelectionAfterModeSwitch))
    {
        _playbackService?.PlayGameMusic(game, settings);
    }
    
    // CRITICAL: Clear flag immediately so next selection plays
    _firstSelect = false;
}
```

### Remove All This

- ❌ View detection logic (`GetActiveViewName()`, `isLibraryView`)
- ❌ `_lastSkippedGameId` tracking
- ❌ `OnMainModelPropertyChanged` handler for view changes
- ❌ `IsOnLoginScreen()` method
- ❌ Complex conditional logic

### Keep Only This

- ✅ Simple `_firstSelect` flag
- ✅ `SkipFirstSelectionAfterModeSwitch` setting
- ✅ Immediate flag clear after check

## Why This Ensures Music Plays After Login

1. **Flag is cleared immediately**: After the first selection (during login), the flag is `false` for the next selection
2. **Natural event flow**: `OnGameSelected` typically fires again when the library view becomes active
3. **No timing dependencies**: Doesn't rely on view detection or property change events
4. **Simple and reliable**: Works regardless of theme, timing, or view state

## Edge Cases

### What if OnGameSelected doesn't fire again?

If `OnGameSelected` doesn't fire again after passing the login screen (rare), the user can:
- Manually select a game (triggers `OnGameSelected`)
- Navigate to a different game (triggers `OnGameSelected`)
- The theme will likely trigger selection when library view loads

**In practice**: This is extremely rare because most themes trigger selection events when views change.

### What if the same game is still selected?

Even if the same game is still selected, many themes will:
- Re-trigger `OnGameSelected` when the library view becomes active
- Fire selection events when transitioning from login to library
- Allow the user to manually select the game

**The flag being cleared ensures music plays on the next selection, regardless of which game is selected.**

## Conclusion

The key to ensuring music plays immediately after passing the login screen is:

1. **Clear the flag immediately** after the first check (not later)
2. **Rely on natural event flow** (`OnGameSelected` will fire again)
3. **Don't try to detect views** (unreliable during transitions)
4. **Keep it simple** (simple flag check, no complex logic)

PlayniteSound's approach works because it's simple and relies on the natural flow of Playnite events, not complex view detection.

