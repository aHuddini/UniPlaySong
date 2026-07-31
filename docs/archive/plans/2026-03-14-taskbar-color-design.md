# Music-Reactive Taskbar Color — Design Spec

## Goal

Tint the Windows taskbar with colors extracted from the selected game's icon, with ambient brightness and temperature shifting based on audio energy. Desktop mode only.

## Architecture

A new `TaskbarColorManager` service polls audio energy at ~4Hz (250ms) and applies a computed color to the Windows taskbar via platform-specific strategies. Color source is the shared `IconColorExtractor` cache (no duplicate pixel scanning). The feature is independent from Icon Glow — either or both can be enabled.

## Components

### TaskbarColorManager

**Location:** `src/IconGlow/TaskbarColorManager.cs`

Orchestrates the feature. Owns a `DispatcherTimer` at 250ms interval.

**Responsibilities:**
- Receive game selection events (`OnGameSelected(Playnite.SDK.Models.Game game)`) — handles `null` (no game selected) by falling through to no-music mode
- Extract base color from `IconColorExtractor` (shared instance)
- Poll `VisualizationDataProvider.Current` for audio energy each tick (wrapped in try-catch for `ObjectDisposedException` in case the provider is disposed between null check and `GetLevels` call)
- Compute target color via HSV mapping (using existing `RgbToHsv`/`HsvToRgb` utilities from `IconColorExtractor`)
- Delegate application to `ITaskbarColorStrategy`
- Handle "no music" modes (Disable / Static / Pulse)
- Save and restore original taskbar state

**Icon source acquisition:** Uses `_api.Database.GetFullFilePath(game.Icon)` to load a `BitmapImage` from disk, then passes it to `IconColorExtractor.GetGlowColors(gameId, imageSource)`. This avoids visual tree dependency — no `TileFinder` needed. If the icon path is null or the file doesn't exist, falls back to the default cornflower blue color. The loaded `BitmapImage` uses `BitmapCacheOption.OnLoad` and is frozen for thread safety, then discarded after extraction (the color result is cached by game ID).

**Lifecycle:**
- Created in `UniPlaySong.cs` alongside other desktop services
- `Attach()` on desktop startup when `EnableTaskbarColor` is true
- `Detach()` on shutdown — restores original taskbar, stops timer, nulls references. Called **before** other cleanup in `OnApplicationStopped` to ensure taskbar is restored even if later cleanup throws.
- `AppDomain.CurrentDomain.ProcessExit` as secondary safety net (unreliable on crashes — see Crash Recovery below)

**Crash recovery:** On first `Apply()`, the original taskbar state is persisted to a JSON file in the plugin data directory (`taskbar_original_state.json`). On `Attach()`, if this file exists and the feature was not cleanly detached (detected via a `taskbar_active` flag file), restore from the persisted state before proceeding. This handles `TerminateProcess`, crashes, and power loss scenarios where `ProcessExit` doesn't fire.

### ITaskbarColorStrategy

**Location:** `src/IconGlow/ITaskbarColorStrategy.cs`

```csharp
interface ITaskbarColorStrategy : IDisposable
{
    bool IsSupported { get; }
    void Apply(Color color);
    void Restore();
}
```

### Win10TaskbarColorStrategy

**Location:** `src/IconGlow/Win10TaskbarColorStrategy.cs`

- `FindWindow("Shell_TrayWnd", null)` to get primary taskbar HWND (cached, re-queried on failure)
- `SetWindowCompositionAttribute` with `ACCENT_ENABLE_GRADIENT` (numeric value `1`, undocumented Windows API — must be added to the `AccentState` enum)
- P/Invoke: `SetWindowCompositionAttribute` struct/enum exists in `DialogHelper.cs` but is `private`. The strategy class declares its own P/Invoke imports (`FindWindow`, `SetWindowCompositionAttribute`) with the necessary structs rather than modifying `DialogHelper`'s visibility.
- `Restore()`: re-applies original accent state saved on first `Apply()`

**Known limitation:** `FindWindow("Shell_TrayWnd", null)` returns only the primary monitor's taskbar. Secondary monitor taskbars (`Shell_SecondaryTrayWnd`) are not colored. Documented as a known limitation for v1.

### Win11TaskbarColorStrategy

**Location:** `src/IconGlow/Win11TaskbarColorStrategy.cs`

- Reads and saves original registry values on first `Apply()`:
  - `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\ColorPrevalence`
  - `HKCU\SOFTWARE\Microsoft\Windows\DWM\ColorizationColor`
- Writes computed ABGR color to registry, ensures `ColorPrevalence = 1`
- After writing, broadcasts `SendMessage(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet")` to force the shell to pick up the change. This is the documented mechanism for accent color updates.
- **Rate limiting:** To avoid excessive shell re-renders, the strategy skips `Apply()` if the new color is within a perceptual threshold (deltaE < 3) of the last applied color. At 4Hz with EMA smoothing, most ticks during steady audio will be skipped.
- `Restore()`: writes back original values + broadcasts setting change
- `RegistryKey` opened and closed per write (no held handles)

**Caveat:** Modifies system-wide accent color (title bars, Start menu, taskbar). Documented in UI warning text.

### OS Detection

`Environment.OSVersion.Version.Build`:
- `< 22000` → Win10 strategy
- `>= 22000` → Win11 strategy
- Strategy returns `IsSupported = false` if calls fail → feature silently disabled

## Color Pipeline

### Base Color Source

`IconColorExtractor.GetGlowColors(gameId, iconSource).primary` — same `ConcurrentDictionary` cache shared with IconGlow and ListHoverGlowManager. No duplicate extraction.

### Audio Energy

Simplified single-value energy (no 3-band FFT needed at 4Hz):

1. `VisualizationDataProvider.Current.GetLevels(out peak, out rms)` — wrapped in try-catch
2. `energy = rms * 0.7 + peak * 0.3`
3. Asymmetric EMA: `smoothed += (energy - smoothed) * (energy > smoothed ? 0.3 : 0.08)`

No punch signal, no onset detection, no spectrum bins — overkill for 250ms updates.

### Color Mapping (Hybrid Brightness + Subtle Temperature)

Uses HSV (consistent with `IconColorExtractor`'s existing `RgbToHsv`/`HsvToRgb` utilities):

- **Value (brightness):** `floor + smoothedEnergy * range` (e.g., 0.15 → 0.55)
- **Saturation:** `baseSat + smoothedEnergy * 0.3` (more vivid at peaks, clamped to 1.0)
- **Hue:** `baseHue - smoothedEnergy * 15°` (subtle warm shift toward orange at high energy)

Convert back to RGB → ABGR for system call.

### "When No Music" Modes

| Mode | Behavior |
|------|----------|
| **Disable** (default) | Restore original taskbar, stop timer |
| **Static** | Hold game's base color at medium brightness, no animation |
| **Pulse** | Sine-wave on brightness (~3s period), same pattern as IconGlow pulse fallback |

## Settings

### Properties (UniPlaySongSettings.cs)

```csharp
private bool enableTaskbarColor = false;
public bool EnableTaskbarColor
{
    get => enableTaskbarColor;
    set { enableTaskbarColor = value; OnPropertyChanged(); }
}

private TaskbarColorNoMusicMode taskbarColorNoMusicMode = TaskbarColorNoMusicMode.Disable;
public TaskbarColorNoMusicMode TaskbarColorNoMusicMode
{
    get => taskbarColorNoMusicMode;
    set { taskbarColorNoMusicMode = value; OnPropertyChanged(); }
}
```

### Enum (UniPlaySongSettings.cs — alongside other feature enums)

```csharp
public enum TaskbarColorNoMusicMode
{
    Disable,
    Static,
    Pulse
}
```

### UI (UniPlaySongSettingsView.xaml — Experimental tab)

Own section after List Icon Glow, before any other experimental features:

```
─── Taskbar Color ───────────────────────────
☑ Enable Music-Reactive Taskbar Color (Desktop only)
  ⚠ On Windows 11, this modifies the system accent color
    which also affects title bars and Start menu.

  When no music is playing:  [Disable ▾]
```

### Reset Handler

`ResetExperimentalTab_Click`: `s.EnableTaskbarColor = false; s.TaskbarColorNoMusicMode = TaskbarColorNoMusicMode.Disable;`

## Memory & Resource Management

- **No bitmaps or buffers** — pure color math (HSV ↔ RGB), just doubles and a Color struct
- **Zero GC pressure per tick** — no allocations in the hot path
- **Shared color cache** — reuses `IconColorExtractor` from `IconGlowManager`
- **Icon loading** — `BitmapImage` loaded once per game selection, frozen, passed to extractor, then eligible for GC. Cached result (two `Color` structs) lives in the `ConcurrentDictionary`.
- **Timer lifecycle** — created only when needed, stopped when feature disabled or no-music Disable mode
- **Registry handles** — opened and closed per write, no held handles
- **Win32 HWND** — `FindWindow` result cached, re-queried only on failure (Explorer restart)
- **Deterministic cleanup** in `Detach()`: unsubscribe events, stop timer, restore taskbar, null references, delete `taskbar_active` flag file
- **ProcessExit safety net** — `AppDomain.CurrentDomain.ProcessExit` unregistered in `Detach()`
- **Crash recovery file** — `taskbar_original_state.json` persisted on first apply, cleaned up on detach

## Integration Points

### UniPlaySong.cs

- Field: `private TaskbarColorManager _taskbarColorManager;`
- Create in `OnApplicationStarted()` after `_iconGlowManager` (needs its `ColorExtractor`)
- `OnGameSelected`: `_taskbarColorManager?.OnGameSelected(game);`
- Cleanup in `OnApplicationStopped`: `_taskbarColorManager?.Detach(); _taskbarColorManager = null;` — placed **before** other cleanup (before `_iconGlowManager.Destroy()`)

### Interaction with Other Features

- **IconGlow enabled + Taskbar Color enabled:** Both share `IconColorExtractor` cache. No conflict.
- **IconGlow disabled + Taskbar Color enabled:** `IconColorExtractor` still instantiated (it's a standalone object on `IconGlowManager`). Works fine. If `IconGlowManager` is not created for some reason, `TaskbarColorManager` creates its own `IconColorExtractor` instance.
- **NAudio not active (SDL2 backend):** `VisualizationDataProvider.Current` is null → falls through to no-music mode. No audio reactivity without NAudio.
- **Fullscreen mode:** Feature is desktop-only. Not attached in fullscreen.

## Known Limitations

- **Multi-monitor (Win10):** Only the primary taskbar is colored. Secondary monitor taskbars (`Shell_SecondaryTrayWnd`) are not affected.
- **Win11 system-wide accent:** Registry approach modifies the accent color globally, not just the taskbar. Documented in the UI warning.
- **SDL2 backend:** No audio reactivity — falls through to no-music mode since `VisualizationDataProvider` only exists in NAudio pipeline.

## File Summary

| File | Action |
|------|--------|
| `src/IconGlow/TaskbarColorManager.cs` | Create — main orchestrator |
| `src/IconGlow/ITaskbarColorStrategy.cs` | Create — platform strategy interface |
| `src/IconGlow/Win10TaskbarColorStrategy.cs` | Create — SetWindowCompositionAttribute (own P/Invoke) |
| `src/IconGlow/Win11TaskbarColorStrategy.cs` | Create — Registry accent color |
| `src/UniPlaySongSettings.cs` | Modify — add 2 properties + enum |
| `src/UniPlaySongSettingsView.xaml` | Modify — add Experimental section |
| `src/UniPlaySongSettingsView.xaml.cs` | Modify — add to reset handler |
| `src/UniPlaySong.cs` | Modify — wire up lifecycle |

## Future Iteration

- **Overlay approach (Phase 2):** Transparent click-through WPF window over taskbar for Win11 without registry side effects. Uses `WS_EX_TRANSPARENT | WS_EX_LAYERED` + `SetWindowPos`. More code but no system-wide accent changes.
- **Multi-monitor support:** Enumerate `Shell_SecondaryTrayWnd` windows for Win10 strategy.
- **Settings expansion:** Intensity slider, update speed, color mapping mode selector if users want more control.
