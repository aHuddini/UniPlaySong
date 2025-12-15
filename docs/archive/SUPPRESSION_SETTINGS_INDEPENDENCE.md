# Suppression Settings Independence

## Settings Overview

The native background music suppression feature uses **independent settings** that do not conflict with other add-on options:

### Suppression Settings (Independent)

1. **`SuppressPlayniteBackgroundMusic`** (Checkbox: "Suppress Native Background")
   - When enabled: Suppresses Playnite's native background music when our music plays
   - When disabled: Allows native music to play alongside or when our music stops
   - **Independent of**: Default Music, Game Music, Volume settings

2. **`CompatibleFullscreenNativeBackground`** (Checkbox: "Compatible Fullscreen Native Background Theme Check")
   - When enabled: Suppresses native music only when our music plays, then restores it when our music stops
   - When disabled: Uses regular suppression behavior
   - **Independent of**: Default Music, Game Music, Volume settings

### Other Settings (Do NOT affect suppression)

- **`EnableDefaultMusic`**: Controls whether to play default music when no game music is found
  - **Does NOT affect suppression** - suppression works the same whether default music is enabled or disabled
  - If default music is disabled and suppression is disabled, native music should play

- **`DefaultMusicPath`**: Path to default music file
  - **Does NOT affect suppression**

- **Volume/Fade settings**: Music volume and fade durations
  - **Do NOT affect suppression**

## How Suppression Works

### When Our Music Starts (`OnMusicStarted` event):

```csharp
if (settings?.SuppressPlayniteBackgroundMusic == true || 
    settings?.CompatibleFullscreenNativeBackground == true)
{
    SuppressNativeMusic(); // Suppresses native music
}
```

**Logic:**
- If `SuppressPlayniteBackgroundMusic = true` → Always suppress
- If `CompatibleFullscreenNativeBackground = true` → Suppress when our music plays
- If both are `false` → Never suppress

### When Our Music Stops (`OnMusicStopped` event):

```csharp
if (_settings?.SuppressPlayniteBackgroundMusic == true)
{
    return; // Don't restore if suppression is enabled
}
// Otherwise, restore native music
AllowNativeMusic();
```

**Logic:**
- If `SuppressPlayniteBackgroundMusic = false` → Restore native music
- If `SuppressPlayniteBackgroundMusic = true` → Don't restore (user wants it suppressed)

## Example Scenarios

### Scenario 1: Suppression Disabled, Default Music Disabled
- `SuppressPlayniteBackgroundMusic = false`
- `EnableDefaultMusic = false`
- **Result**: Native music should play when no game music is available

### Scenario 2: Suppression Enabled, Default Music Disabled
- `SuppressPlayniteBackgroundMusic = true`
- `EnableDefaultMusic = false`
- **Result**: Native music is suppressed, no music plays when no game music is available

### Scenario 3: Compatible Mode Enabled, Default Music Disabled
- `CompatibleFullscreenNativeBackground = true`
- `SuppressPlayniteBackgroundMusic = false`
- `EnableDefaultMusic = false`
- **Result**: Native music plays until our music starts, then suppressed. When our music stops, native music should restore.

## Current Issue

If native music is not restoring after login bypass, the issue is likely in the `AllowNativeMusic()` restoration logic, not a settings conflict.

**To verify settings are being respected:**
1. Check log for `SuppressNativeMusic: Suppression disabled by user setting` (should appear if suppression is disabled)
2. Check log for `AllowNativeMusic: ...` messages to see restoration attempts
3. Verify `_settings?.SuppressPlayniteBackgroundMusic` value in logs

