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

**Current HEAD:** `597e9c8` + Issue 7 fix (uncommitted)
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
