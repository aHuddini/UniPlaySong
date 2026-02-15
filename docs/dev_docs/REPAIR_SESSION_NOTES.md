# UniPlaySong Repair Session Notes

## Timeline

| Date | Action | Commit |
|------|--------|--------|
| Session 1 | Baseline — visualizer features stable | `8486955 newgradientvisualizer` |
| Session 1 | First fix attempt (Issues 1-3) — committed but later rolled back | `708a24e` (discarded) |
| Session 1 | Additional fixes attempted (Issues 4-9) on top of 708a24e — uncommitted, rolled back | (discarded) |
| Session 2 | Prior session fixes re-attempted (Issues 10, 11, 12 approaches A-G) — rolled back | `891f8c1`, `1869b09`, `1c08d2d` (discarded) |
| Session 3 | Full rollback to `8486955`. Applied Issues 1, 10, 11 as clean fixes | `597e9c8` on `repairSessionFeb` |
| Session 3 | Applied Issue 7 — stuck pause when disabling Pause-on-X settings | uncommitted |

**Current HEAD:** `5bdac74` + Pause on Play feature (uncommitted)
**Branch:** `repairSessionFeb` (based on `8486955`)

---

## Issue Categories

### Category A: Settings Infrastructure (stale references, serialization, handler wiring)
These are foundational bugs in how settings instances are managed. They cause cascading failures in all other systems.

- **Issue 1** — Stale settings reference in MediaElementsMonitor
- **Issue 10** — Runtime state persisted to JSON
- **Issue 11** — Double-fire property change handlers

### Category B: Pause-on-X Behavior (FocusLoss, Minimize, SystemTray)
Bugs in how the "Pause on..." settings interact with window state events. These cause stuck pauses or music not playing on startup.

- **Issue 6** — FocusLoss blocking startup
- **Issue 7** — Stuck pause when disabling settings while source is active

### Category C: Video/Music Overlap (MediaElementsMonitor, EML integration)
Timing issues where music and video audio overlap during game switches.

- **Issue 2** — ThemeOverlay watchdog (stuck overlay)
- **Issue 3** — Grace period (premature timer stop)
- **Issue 4** — VideoIsPlaying in ShouldPlayMusic causing STOP instead of PAUSE
- **Issue 5** — SkipFirstSelection applying to desktop mode
- **Issue 8** — ClearAllPauseSources doc comment (cosmetic)
- **Issue 9** — Preemptive video detection in MediaElement_Opened
- **Issue 12** — Music/video overlap approaches (A-G)

---

## Applied Fixes (in commit `597e9c8`)

### Issue 1: Stale Settings Reference in MediaElementsMonitor

**Category:** A — Settings Infrastructure

**Symptom:** After opening/closing the settings panel (any plugin, including EML), MediaElementsMonitor continued writing `VideoIsPlaying` to the OLD settings object. The new settings object (created by Playnite on settings reload) never received the updates, so the coordinator never knew videos were playing. Result: music and video audio played simultaneously.

**Root Cause:** Playnite creates a new `UniPlaySongSettings` instance when settings are saved. MediaElementsMonitor held a reference to the old one. Additionally, `Attach()` had no guards — every call re-registered `RegisterClassHandler` (permanent, no unregister API) and created a new `DispatcherTimer` (orphaning the old one).

**Fix applied:**
- Added `MediaElementsMonitor.UpdateSettings(UniPlaySongSettings)` method to update the internal reference
- Added `_classHandlerRegistered` bool guard to prevent duplicate `RegisterClassHandler` calls
- Added `if (timer == null)` guard to prevent orphaned timers on repeated `Attach()` calls
- Added unconditional `MediaElementsMonitor.UpdateSettings(e.NewSettings)` call in `OnSettingsServiceChanged` so the monitor always has the current settings instance after any settings save

**Files changed:**
- `Monitors/MediaElementsMonitor.cs` — `UpdateSettings()` method, `_classHandlerRegistered` guard, timer null guard
- `UniPlaySong.cs` — `UpdateSettings()` call in `OnSettingsServiceChanged`

---

### Issue 10: Runtime State Persisted Across Sessions

**Category:** A — Settings Infrastructure

**Symptom:** Music doesn't play on Playnite startup. `VideoIsPlaying` was saved as `true` in the JSON settings from a previous session where Playnite was closed while a video was playing.

**Root Cause:** `VideoIsPlaying` and `ThemeOverlayActive` are runtime-only state but were being serialized by Newtonsoft.Json along with all other settings properties.

**Fix applied:**
- Added `[JsonIgnore]` attribute to both `VideoIsPlaying` and `ThemeOverlayActive` properties in `UniPlaySongSettings.cs`
- Added `using Newtonsoft.Json;` import
- Both properties now always start as their backing field default (`false`) on each session

**Note:** An initial approach of resetting them in `OnApplicationStarted` was tried in Session 2 but reverted because it raced with fullscreen theme controls that set these values during intro videos. `[JsonIgnore]` is the correct approach — no race conditions.

**Files changed:**
- `UniPlaySongSettings.cs` — `[JsonIgnore]` on two properties, `using Newtonsoft.Json;`

---

### Issue 11: Double-Fire Property Change Handlers

**Category:** A — Settings Infrastructure

**Symptom:** Inconsistent pause state when switching games, especially after interacting with EML mute button. Music playback and button states become confused.

**Root Cause:** Two separate PropertyChanged handlers both listened for `VideoIsPlaying` and `ThemeOverlayActive` changes:
- `OnSettingsChanged` — direct `_settings.PropertyChanged` subscription
- `OnSettingsServicePropertyChanged` — via `SettingsService` relay of `PropertyChanged`

Both called `HandleVideoStateChange()` / `HandleThemeOverlayChange()`, causing double-fire on every state change. The second call could interact with deferred playback logic or fire while the first call's fade transition was still in progress.

**Fix applied:**
- Removed `VideoIsPlaying` and `ThemeOverlayActive` handling from `OnSettingsChanged` (method body is now empty with a comment)
- These are now handled exclusively through `OnSettingsServicePropertyChanged` (via SettingsService relay), which is more robust as it automatically re-subscribes when settings instances change

**Files changed:**
- `UniPlaySong.cs` — emptied `OnSettingsChanged` body

### Issue 7: Stuck Pause When Disabling Pause-on-X Settings

**Category:** B — Pause-on-X Behavior

**Symptom:** If a "Pause on..." setting (Minimize, FocusLoss, SystemTray) is disabled while its pause source is active, music remains paused permanently. For example: PauseOnMinimize is enabled → user minimizes Playnite (music pauses) → user disables PauseOnMinimize in settings → user restores the window → music stays paused because `RemovePauseSource(Minimized)` is never called.

**Root Cause:** All three event handlers guarded both the Add and Remove of their pause source behind the setting check. When the setting was disabled, the early-return skipped `RemovePauseSource`, leaving the source stuck in the `_activePauseSources` HashSet.

**Fix applied:**
- `OnWindowStateChanged`: Removed top-level `if (PauseOnMinimize != true) return` guard. `RemovePauseSource(Minimized)` on restore is now unconditional; only `AddPauseSource(Minimized)` on minimize is guarded by the setting.
- `OnWindowVisibilityChanged`: Removed top-level `if (PauseWhenInSystemTray != true) return` guard. `RemovePauseSource(SystemTray)` on show is now unconditional; only `AddPauseSource(SystemTray)` on hide is guarded by the setting.
- `OnApplicationActivate`: Removed `if (PauseOnFocusLoss == true)` guard. `RemovePauseSource(FocusLoss)` is now unconditional. (`OnApplicationDeactivate` was already correct — it only adds when enabled.)

**Principle:** Removing a pause source that doesn't exist is a no-op on a `HashSet`, so removals are always safe to call unconditionally. The setting should only prevent *adding* the pause source.

**Files changed:**
- `UniPlaySong.cs` — `OnWindowStateChanged`, `OnWindowVisibilityChanged`, `OnApplicationActivate`

---

## v1.2.6 Changes

### Pause on Play — Splash Screen Compatibility (#61)

**Category:** New Feature — Pause-on-X Behavior

**Request:** Users with the [Splash Screen](https://github.com/darklinkpower/PlayniteExtensionsCollection/wiki/Splash-Screen) plugin wanted music to pause immediately when clicking Play on a game, before the splash screen appears — not after the game process starts.

**Challenge:** `OnGameStarting` plugin hooks fire sequentially. Splash Screen blocks in its `OnGameStarting` via `ActivateGlobalProgress`, so if Playnite calls it before UniPlaySong, our handler doesn't fire until after the splash finishes.

**Solution:** Instead of `OnGameStarting`, subscribe to `_api.Database.Games.ItemUpdated` — a database-level event that fires when `Game.IsLaunching` changes, independent of plugin hook execution order.

**Implementation:**
- Added `PauseSource.GameStarting` enum value
- Added `PauseOnGameStart` setting (default `false`, opt-in)
- Added `OnGameItemUpdated` handler: pauses on `IsLaunching` transition, resumes on `IsRunning` → false transition
- Added `OnGameStopped` override as fallback for resume
- UI label: "Compatibility: Pause on Play (Splash Screen Mode)"

**Key learning:** `OnGameStarting` plugin hooks are unsuitable for time-critical reactions when other plugins (like Splash Screen) block in the same hook via `ActivateGlobalProgress`. `Database.Games.ItemUpdated` fires at the data layer, before/independently of plugin hooks.

**Files changed:**
- `Models/PauseSource.cs` — `GameStarting` enum value
- `UniPlaySongSettings.cs` — `PauseOnGameStart` backing field + property
- `UniPlaySongSettingsView.xaml` — CheckBox + description
- `UniPlaySong.cs` — `OnGameItemUpdated` handler, `ItemUpdated` subscription, `OnGameStopped` fallback, defaults reset

### Now Playing Display — Duration Removed

Duration/timestamp removed from the Now Playing display text. Was `Title - Artist | 3:45`, now `Title - Artist`. `FormatDuration` kept for future use.

### NowPlayingPanel GPU Optimization (#55)

Uses `Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), 60)` to cap WPF's global render rate. The configurable FPS setting (15/30/60) was attempted but all approaches failed — `DesiredFrameRate` does not affect `CompositionTarget.Rendering`, `OverrideMetadata` can only be called once per AppDomain, and Stopwatch-based frame-skip performed poorly. Reverted to fixed 60fps cap.

### Settings UI Reorganization

Restructured the add-on settings tabs for better discoverability and logical grouping.

**Tab changes:**
- Renamed "Default Music" → "Playback", "Audio Normalization" → "Audio Editing", "Search Cache" → "Search"
- Moved Volume, Fade In/Out, Preview Mode, Song Randomization from General tab into new Playback tab
- Reordered tabs: General, Playback, Live Effects, Audio Editing, Downloads, Search, Migration, Cleanup

**New features:**
- "Open Log Folder" button in General → Troubleshooting section (opens extension folder containing UniPlaySong.log)
- Removed "(Experimental)" label from "Spectrum Visualizer" (only the Advanced Customization expander retains the experimental label)
- Polished tab titles/subtitles across Playback, Audio Editing, Downloads, Search tabs

**Files changed:**
- `UniPlaySongSettingsView.xaml` — tab renames, reordering, content moves, new button, label/subtitle updates
- `UniPlaySongSettingsViewModel.cs` — `OpenLogFolderCommand`

### Top Panel Media Controls — Styling Fix

**Category:** Bug Fix

**Symptom:** Play/pause and skip buttons in the desktop top panel looked visually mismatched compared to native Playnite icons — wrong size, bold weight distorting the symbol font, and inconsistent spacing across themes (Default vs Harmony).

**Root Cause:** Icons used FontSize 18 with FontWeight.Bold and hardcoded negative margins. IcoFont is a symbol font where Bold distorts glyphs. Margins were theme-specific, so hardcoded values only worked for one theme.

**Fix applied:**
- FontSize set to 18pt, removed FontWeight.Bold, added VerticalAlignment/HorizontalAlignment Center
- Theme-adaptive margin trimming: on Loaded, walks the visual tree to find the TopPanelItem container and symmetrically reduces the facing margins (play's right, skip's left) to 20% of the theme's value — groups the buttons without overriding other theme styling (MinWidth, Padding, Background)

**Files changed:**
- `DeskMediaControl/TopPanelMediaControlViewModel.cs` — icon styling, `AdjustTopPanelItemMargin()` method

### Spectrum Visualizer — Color & Opacity Tuning

**Category:** Improvement

**Changes:**
- **Opacity curve**: Changed from linear to `Math.Sqrt(current)` for better dynamic range — quiet bars are more transparent, loud bars reach near-full brightness
- **Preset/style decoupling**: Removed `VizColorTheme` and `VizGradientEnabled` assignments from all viz presets. Presets now only control tuning parameters (gain, gravity, smoothing, etc.). Color theme and gradient toggle are independent user choices
- **Classic white brush**: Alpha set to 200 (softened); all color themes use alpha=255 (full opacity with darkened RGB values)
- **Color theme tuning**: All existing themes received darker bottom gradient stops for more depth/contrast from bar base to tip
- **6 new color themes**: Synthwave (purple→pink), Ember (ember→amber), Abyss (navy→aqua), Solar (rust→gold), Terminal (dark→bright green), Frost (steel blue→frost white)

**Files changed:**
- `DeskMediaControl/SpectrumVisualizerControl.cs` — opacity formula, ThemeColors array (tuned + 6 new entries), BarBrush alpha
- `UniPlaySongSettings.cs` — 6 new `VizColorTheme` enum values
- `UniPlaySongSettingsView.xaml` — 6 new ComboBoxItem entries in color theme dropdown
- `UniPlaySongSettingsView.xaml.cs` — removed color/gradient assignments from presets

---

## Not Yet Applied

### Category B: Pause-on-X Behavior

| Issue | Summary | Priority | Notes |
|-------|---------|----------|-------|
| 6 | FocusLoss blocking startup | HIGH | `CheckInitialWindowState` adds FocusLoss when `IsActive=false` during WPF init |

### Category C: Video/Music Overlap

| Issue | Summary | Priority | Notes |
|-------|---------|----------|-------|
| 2 | ThemeOverlay watchdog | MEDIUM | 60s safety timeout in MusicControl — defensive |
| 3 | Grace period | MEDIUM | 5-tick delay before timer stop — prevents VideoIsPlaying flicker |
| 4 | VideoIsPlaying in ShouldPlayMusic | HIGH | Causes full STOP instead of PAUSE — most impactful change |
| 5 | SkipFirstSelection fullscreen gate | LOW | Desktop mode skip — straightforward |
| 8 | ClearAllPauseSources doc comment | LOW | Comment-only, behavior already correct |
| 9 | Preemptive MediaElement_Opened | MEDIUM | Eliminates 100-200ms detection gap |

### Previously Attempted and Reverted (Issue 12)

These overlap-reduction approaches were tried in Sessions 1-2 and reverted. Documented for reference — do not re-apply without fresh analysis.

| Approach | What it did | Why it failed |
|----------|-------------|---------------|
| A | Removed mute check from `isPlaying` | Muted videos (user choice) caused UPS to pause |
| B | Sticky mute tracking (HashSet) | Unreliable, inconsistent behavior |
| C | PauseImmediate (instant volume cut) | Audible click/pop, no fade transition |
| D | EmlVideoMonitor (WPF visual tree) | Redundant — Aniki already has XAML triggers for this |
| E | Short video fade-out (0.12s override) | Applied then reverted in later session — contributed to glitchy transitions |
| F | Preemptive trailer file detection | Applied then reverted — `ClearAllPauseSources` cleared the preemptive pause |
| G | Preserve Video/ThemeOverlay in ClearAllPauseSources | Made things worse — music stops late, glitchy fade-in/out, instant resume |

**Key architectural insight from EML source analysis:**
- EML's `VideoPlayerControl` exposes `SettingsModel.Settings.IsVideoPlaying` via `INotifyPropertyChanged`
- Themes with EML triggers (like Aniki) bind this to `MusicControl.Tag` → `ThemeOverlayActive` → instant detection
- This is the same pattern PlayniteSound used — theme-based integration is primary, MediaElement scanning is fallback
- The 220ms gap is EML's debounce, unavoidable from the detection side

---

## Test Matrix

| Theme | Mode | What to Check |
|-------|------|---------------|
| Dune | Desktop | Music plays on start, music pauses when EML trailer plays, music resumes after trailer |
| Aniki | Fullscreen | ThemeOverlayActive respected, music doesn't play during intro overlay |
| Aniki | Fullscreen | No music/video overlap on game switch |
| Any | Desktop | First game selection plays music immediately |
| Any | Fullscreen | SkipFirstSelectionAfterModeSwitch works correctly |
| Any | Either | PauseOnFocusLoss: music plays on startup, pauses on alt-tab, resumes on return |
| Any | Either | Disable PauseOnFocusLoss while unfocused → music should resume |
| Any | Either | Disable PauseOnMinimize while minimized → restore window → music should resume |
| Any | Either | Disable PauseWhenInSystemTray while in tray → show window → music should resume |
| Any | Either | Manual pause survives game switch |
| Any | Either | Muting EML video → UPS music plays; unmuting → UPS pauses correctly |
| Any | Either | Restart Playnite → music plays (VideoIsPlaying not persisted) |
| Any | Either | Save any plugin settings → music continues uninterrupted |
| Any | Either | PauseOnGameStart enabled + Splash Screen: click Play → music fades before splash |
| Any | Either | PauseOnGameStart enabled: close game → music resumes |
| Any | Either | PauseOnGameStart disabled: click Play → music continues until game steals focus |
