# UniPlaySong Repair Session Notes

## Context

These fixes were attempted after the visualizer features were implemented (commit `8486955 newgradientvisualizer`). The user reported multiple playback issues when testing with different themes (Dune in Desktop mode, Aniki in Fullscreen mode).

**Baseline commit to rollback to:** `8486955 newgradientvisualizer`

**Commits in the fix timeline:**
```
8486955 newgradientvisualizer          <-- ROLLBACK TARGET (stable visualizer)
708a24e -fixattempt: mediaelements&themeoverlays involving videos #53  <-- committed fix attempt (Issues 1-3)
(uncommitted changes on top of 708a24e) <-- additional fixes from this session (Issues 4-9)
```

**Files changed in `708a24e` (vs `8486955`):**
- `Controls/MusicControl.xaml.cs` — watchdog timer added
- `Monitors/MediaElementsMonitor.cs` — grace period, RegisterClassHandler guard, timer reuse, UpdateSettings
- `UniPlaySong.cs` — MediaElementsMonitor.UpdateSettings call in OnSettingsChanged

**Files changed uncommitted (vs `708a24e`):**
- `Services/MusicPlaybackCoordinator.cs` — VideoIsPlaying removed from ShouldPlayMusic, fullscreen gate
- `UniPlaySong.cs` — FocusLoss startup removal, stuck pause prevention, settings disable cleanup
- `Services/MusicPlaybackService.cs` — comment-only update
- `Monitors/MediaElementsMonitor.cs` — preemptive VideoIsPlaying in MediaElement_Opened

---

## Issue 1: Settings Reload Breaking MediaElementsMonitor (Stale Settings Reference)

**Symptom:** After opening/closing the settings panel, MediaElementsMonitor continued writing `VideoIsPlaying` to the OLD settings object. The new settings object (created by Playnite on settings reload) never received the updates, so the coordinator never knew videos were playing. Result: music and video audio played simultaneously.

**Root Cause:** Playnite creates a new `UniPlaySongSettings` instance when settings are saved. MediaElementsMonitor held a reference to the old one.

**What the baseline (`8486955`) looked like:**
```csharp
// MediaElementsMonitor.Attach() — no UpdateSettings method existed
// Every call to Attach() re-registered RegisterClassHandler (permanent, no unregister API)
// Every call to Attach() created a new DispatcherTimer (orphaning the old one)
static public void Attach(IPlayniteAPI api, UniPlaySongSettings settings)
{
    playniteApi = api;
    MediaElementsMonitor.settings = settings;
    EventManager.RegisterClassHandler(typeof(MediaElement), MediaElement.MediaOpenedEvent, new RoutedEventHandler(MediaElement_Opened));
    timer = new DispatcherTimer();
    timer.Interval = TimeSpan.FromMilliseconds(100);
    timer.Tick += Timer_Tick;
}

// UniPlaySong.OnSettingsChanged() — no MediaElementsMonitor.UpdateSettings call
```

**Fix (committed in `708a24e`):**
- Added `MediaElementsMonitor.UpdateSettings(UniPlaySongSettings)` method
- Called from `OnSettingsChanged()` in `UniPlaySong.cs` to keep the reference fresh
- Added `_classHandlerRegistered` bool guard to prevent duplicate `RegisterClassHandler` calls
- Added timer reuse (`if (timer == null)`) to prevent orphaned timers

**Status:** Solid fix. Should be re-applied.

---

## Issue 2: MusicControl ThemeOverlay Watchdog (Stuck Overlay Prevention)

**Symptom:** If a theme sets `MusicControl.Tag = True` but never resets it to `False`, music stays paused forever.

**What the baseline (`8486955`) looked like:**
```csharp
// MusicControl.UpdateMute() — no watchdog, just set ThemeOverlayActive
if (_settings.ThemeOverlayActive != mute)
{
    Logger.Info($"[MusicControl] Setting ThemeOverlayActive={mute} (was {_settings.ThemeOverlayActive})");
    _settings.ThemeOverlayActive = mute;
}
// No safety timeout — stuck forever if theme doesn't reset Tag
```

**Fix (committed in `708a24e`):**
- Added 60-second safety watchdog `DispatcherTimer` in `MusicControl.xaml.cs`
- If `ThemeOverlayActive` stays true for 60s, auto-clears it
- Watchdog restarts each time `UpdateMute()` confirms `mute=true` (resets countdown)
- Added `using System.Windows.Threading;` import

**Status:** Safe defensive fix. Should be re-applied.

---

## Issue 3: MediaElementsMonitor Grace Period (Premature Timer Stop)

**Symptom:** When navigating between games with EML trailers, the timer would stop immediately when `mediaElementPositions.Count == 0`, then miss the next video that loads milliseconds later. This caused `VideoIsPlaying` to flicker false→true with a gap where music would briefly resume.

**What the baseline (`8486955`) looked like:**
```csharp
// Timer stopped immediately when no MediaElements — no grace period
if (mediaElementPositions.Count == 0)
{
    timer.Stop();
    if (settings.VideoIsPlaying)
    {
        Logger.Info($"[UniPlaySong] MediaElementsMonitor: All media elements removed, setting VideoIsPlaying to false");
    }
    settings.VideoIsPlaying = false;
}
```

**Fix (committed in `708a24e`):**
- Added `_emptyTickCount` counter and `EmptyTickGracePeriod = 5`
- Timer only stops after 5 consecutive ticks (~500ms at 100ms interval) with no MediaElements
- Resets to 0 whenever any MediaElement is present
- Also added comments to the position-recording block explaining why we don't flag as playing immediately

**Status:** Good fix. Should be re-applied. Timer interval stays at 100ms.

---

## Issue 4: `VideoIsPlaying` in `ShouldPlayMusic()` Causing Full Stop Instead of Pause

**Symptom:** Music doesn't play on Playnite restart (Desktop mode, Dune theme). When switching games, music is paused and requires manual Play button press. The music is being fully STOPPED rather than PAUSED.

**Root Cause:** `ShouldPlayMusic()` in `MusicPlaybackCoordinator.cs` checked `_settings.VideoIsPlaying` and returned `false`. When `ShouldPlayMusic()` returns false, `HandleGameSelected()` calls `_playbackService.Stop()` — a full stop that unloads the music. The pause source system (`PauseSource.Video`) was designed to handle video detection with a PAUSE (allowing resume), but the `ShouldPlayMusic()` check was short-circuiting that entirely.

**What the baseline (`8486955`) looked like:**
```csharp
// ShouldPlayMusic() — both VideoIsPlaying AND ThemeOverlayActive checked
if (_settings.VideoIsPlaying)
{
    _fileLogger?.Debug("ShouldPlayMusic: Returning false - video is playing");
    return false;
}

if (_settings.ThemeOverlayActive)
{
    _fileLogger?.Debug("ShouldPlayMusic: Returning false - theme overlay is active");
    return false;
}
```

**Fix (uncommitted):**
- Removed `VideoIsPlaying` check from `ShouldPlayMusic()`
- Video detection is now handled exclusively by `HandleVideoStateChange()` which uses `AddPauseSource(PauseSource.Video)` / `RemovePauseSource(PauseSource.Video)`
- This means: video playing = music PAUSED (can resume) vs. old behavior: video playing = music STOPPED (must restart from scratch)

**IMPORTANT NUANCE — ThemeOverlayActive must stay:**
- Initially both `VideoIsPlaying` AND `ThemeOverlayActive` were removed from `ShouldPlayMusic()`
- This broke the Aniki theme which relies on ThemeOverlayActive (via MusicControl Tag bindings) to prevent music during intro/overlay screens
- `ThemeOverlayActive` was restored in `ShouldPlayMusic()` because it represents an **intentional theme decision** ("don't start music"), not a transient detection state
- `VideoIsPlaying` stays removed because it's **transient** (EML video loading/unloading during game switches) and should only PAUSE, not STOP

**Key architectural distinction:**
```
VideoIsPlaying    → transient    → handled by PauseSource.Video    → PAUSE (resume when video ends)
ThemeOverlayActive → intentional → checked in ShouldPlayMusic()    → prevents music from STARTING
```

**Status:** Most impactful change. Needs careful re-testing with:
- Dune theme (Desktop) — no MusicControl, uses EML trailers
- Aniki theme (Fullscreen) — uses MusicControl Tag bindings for overlay control
- Theme with ThemeCompatibleSilentSkip enabled

---

## Issue 5: `SkipFirstSelectionAfterModeSwitch` Applying to Desktop Mode

**Symptom:** Music doesn't play on first game selection in Desktop mode.

**Root Cause:** `SkipFirstSelectionAfterModeSwitch` was checked in both `ShouldPlayMusic()` and `HandleGameSelected()` without gating on fullscreen. The setting is described as "Skip first game selection after switching to fullscreen mode" but was applying to both modes.

**What the baseline (`8486955`) looked like:**
```csharp
// ShouldPlayMusic() — no fullscreen check
if ((_firstSelect || _skipFirstSelectActive) && _settings.SkipFirstSelectionAfterModeSwitch)
{
    _fileLogger?.Debug($"ShouldPlayMusic: Returning false - first select skip enabled ...");
    return false;
}

// HandleGameSelected() — no fullscreen check
if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
{
    _fileLogger?.Debug($"HandleGameSelected: Skipping first selection ...");
    _skipFirstSelectActive = true;
    _firstSelect = false;
    return;
}
```

**Fix (uncommitted):**
- Added `&& _isFullscreen()` to the check in `ShouldPlayMusic()`
- Added `&& isFullscreen` to the check in `HandleGameSelected()`
- Desktop mode now always plays music on first game selection

**Status:** Straightforward fix. Should be re-applied.

---

## Issue 6: `PauseOnFocusLoss` Preventing Music From Ever Playing on Startup

**Symptom:** With `PauseOnFocusLoss` enabled, music never plays after Playnite starts. User has to press Play manually.

**Root Cause:** `CheckInitialWindowStateEarly()` and `CheckInitialWindowState()` both checked `window.IsActive` and added `PauseSource.FocusLoss` if false. During WPF application startup, `window.IsActive` is almost always `false` (the window hasn't received focus yet). This added a FocusLoss pause source before any music could start, and it never got cleared because the `OnApplicationActivate` handler only fires when the window *gains* focus (which from the OS perspective it already had).

**What the baseline (`8486955`) looked like:**
```csharp
// CheckInitialWindowStateEarly() — checked FocusLoss
if (_settings?.PauseOnFocusLoss == true && !window.IsActive)
    _playbackService?.AddPauseSource(Models.PauseSource.FocusLoss);

// CheckInitialWindowState() — same check
if (_settings?.PauseOnFocusLoss == true && !window.IsActive)
    _playbackService?.AddPauseSource(Models.PauseSource.FocusLoss);

// OnApplicationActivate() — only removed FocusLoss if setting was enabled
private void OnApplicationActivate(object sender, EventArgs e)
{
    if (_settings?.PauseOnFocusLoss == true)
        _playbackService?.RemovePauseSource(Models.PauseSource.FocusLoss);
}
```

**Fix (uncommitted):**
- Removed `FocusLoss` from both `CheckInitialWindowStateEarly()` and `CheckInitialWindowState()`
- FocusLoss is now managed exclusively by the `Activated`/`Deactivated` event handlers after startup
- Semantic clarification: `PauseOnFocusLoss` means "pause when user LEAVES Playnite", not "don't play if window doesn't have initial focus"

**Status:** Should be re-applied. The old behavior was fundamentally wrong — `IsActive` is unreliable during initialization.

---

## Issue 7: Stuck Pause When Disabling Settings While Source Is Active

**Symptom:** If user disables `PauseOnFocusLoss` while Playnite is unfocused, music stays paused forever. Same for `PauseOnMinimize` while minimized, `PauseWhenInSystemTray` while in tray.

**Root Cause:** The event handlers had early-returns: `if (_settings?.PauseOnFocusLoss != true) return;` — so when the setting was disabled, the restore event (Activated) would skip removing the pause source.

**What the baseline (`8486955`) looked like:**
```csharp
// OnWindowStateChanged() — early return prevented cleanup
private void OnWindowStateChanged(object sender, EventArgs e)
{
    if (_settings?.PauseOnMinimize != true) return;  // <-- skips remove if setting disabled
    var windowState = Application.Current?.MainWindow?.WindowState;
    switch (windowState)
    {
        case WindowState.Normal:
        case WindowState.Maximized:
            _playbackService?.RemovePauseSource(Models.PauseSource.Minimized);
            break;
        case WindowState.Minimized:
            _playbackService?.AddPauseSource(Models.PauseSource.Minimized);
            break;
    }
}

// OnWindowVisibilityChanged() — same pattern
private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    if (_settings?.PauseWhenInSystemTray != true) return;  // <-- skips remove if setting disabled
    var isVisible = (bool)e.NewValue;
    if (isVisible)
        _playbackService?.RemovePauseSource(Models.PauseSource.SystemTray);
    else
        _playbackService?.AddPauseSource(Models.PauseSource.SystemTray);
}

// OnApplicationActivate() — same pattern (shown in Issue 6 above)

// OnSettingsChanged() — no cleanup of pause sources when settings disabled
```

**Fix (uncommitted):**
- Event handlers now ALWAYS remove their pause source on the "restore" event, regardless of setting state
- Only ADD the pause source when the setting is enabled
- Additionally, `OnSettingsChanged()` now explicitly removes pause sources when their settings are disabled:
```csharp
if (e.NewSettings.PauseOnFocusLoss == false)
    _playbackService.RemovePauseSource(Models.PauseSource.FocusLoss);
if (e.NewSettings.PauseOnMinimize == false)
    _playbackService.RemovePauseSource(Models.PauseSource.Minimized);
if (e.NewSettings.PauseWhenInSystemTray == false)
    _playbackService.RemovePauseSource(Models.PauseSource.SystemTray);
```

**Status:** Should be re-applied. Prevents a whole class of "stuck pause" bugs.

---

## Issue 8: Manual Pause Preserved in `ClearAllPauseSources()`

**Symptom:** User presses Pause on media controls, then switches game — does music start playing again?

**What the baseline (`8486955`) looked like:**
```csharp
// ClearAllPauseSources() — Manual was ALREADY preserved in the logic at baseline!
// The code already had:
if (_activePauseSources.Contains(PauseSource.Manual))
    preservedSources.Add(PauseSource.Manual);
// But the doc comment only mentioned FocusLoss, Minimized, SystemTray
```

**Fix (uncommitted):**
- Comment-only update to the doc comment to mention Manual is also preserved
- **The actual logic was already correct at baseline.** No code change needed.

**Status:** Comment-only. Low priority. The behavior was already correct.

---

## Issue 9: Music/Video Audio Overlap (Preemptive Video Detection)

**Symptom:** When EML loads a trailer video on game switch, there's a brief overlap where both music and video audio play simultaneously before MediaElementsMonitor detects the video.

**Root Cause:** MediaElementsMonitor detects playing videos by comparing positions across timer ticks. At 100ms interval, it takes 1-2 ticks (100-200ms) before the position change is detected and `VideoIsPlaying` is set to true. During this window, music keeps playing.

**What the baseline (`8486955`) looked like:**
```csharp
// MediaElement_Opened() — no preemptive detection
static private void MediaElement_Opened(object sender, RoutedEventArgs e)
{
    if (sender is MediaElement mediaElement)
    {
        Logger.Info($"[UniPlaySong] MediaElementsMonitor: MediaElement opened - ...");
    }
    Timer_Tick(sender, e);
    timer.Start();
}
```

**Fix (uncommitted):**
- Added preemptive `settings.VideoIsPlaying = true` in `MediaElement_Opened` event handler
- Conditions: video has audio, is visible, has dimensions, not muted, volume > 0, VideoIsPlaying not already true
- If the video turns out to be paused (not actually playing), `Timer_Tick` will correct `VideoIsPlaying` back to `false` within one tick cycle when it sees position hasn't changed
- Inspired by PlayniteSound commits `c0447a2` and `9c06920`

**Timer interval:** Must stay at 100ms. Grace period is 5 ticks = ~500ms.

**Status:** Should be re-applied. Eliminates the audible overlap gap.

---

## Summary: Re-apply Order After Rollback to `8486955`

### Phase 1: MediaElementsMonitor Hardening (from `708a24e`)
These were committed and tested together:

1. **Stale settings reference** — Add `UpdateSettings()` method + call from `OnSettingsChanged()`
2. **RegisterClassHandler guard** — `_classHandlerRegistered` bool to prevent duplicates
3. **Timer reuse** — `if (timer == null)` to prevent orphaned timers
4. **Grace period** — `_emptyTickCount` / `EmptyTickGracePeriod = 5` before stopping timer
5. **ThemeOverlay watchdog** — 60s safety timeout in `MusicControl.xaml.cs`

### Phase 2: Core Playback Fixes (previously uncommitted)
Apply one at a time and test:

6. **[HIGH]** Remove `VideoIsPlaying` from `ShouldPlayMusic()` — keep `ThemeOverlayActive`
7. **[HIGH]** Gate `SkipFirstSelectionAfterModeSwitch` on fullscreen only
8. **[HIGH]** Remove FocusLoss from startup checks (`CheckInitialWindowStateEarly` + `CheckInitialWindowState`)

### Phase 3: Stuck-Pause Prevention (previously uncommitted)
9. **[MEDIUM]** Always-remove pause sources on restore events (OnWindowStateChanged, OnWindowVisibilityChanged, OnApplicationActivate)
10. **[MEDIUM]** Clear pause sources when settings disabled (in `OnSettingsChanged`)

### Phase 4: Overlap Elimination (previously uncommitted)
11. **[MEDIUM]** Preemptive `VideoIsPlaying=true` in `MediaElement_Opened`

### Phase 5: Comment-only
12. **[LOW]** Update `ClearAllPauseSources()` doc comment to mention Manual (already correct in code)

---

---

## Issue 10: `VideoIsPlaying` and `ThemeOverlayActive` Persisted Across Sessions

**Symptom:** Music doesn't play on Playnite startup. `VideoIsPlaying` was saved as `true` in the JSON settings from a previous session where Playnite was closed while a video was playing.

**Root Cause:** `VideoIsPlaying` and `ThemeOverlayActive` are runtime-only state but were being serialized by Newtonsoft.Json along with all other settings properties.

**Fix (applied):**
- Added `[JsonIgnore]` attribute to both `VideoIsPlaying` and `ThemeOverlayActive` properties in `UniPlaySongSettings.cs`
- Added `using Newtonsoft.Json;` import
- Both properties now always start as their backing field default (`false`) on each session
- An initial approach of resetting them in `OnApplicationStarted` was tried first but **reverted** because it raced with fullscreen theme controls that set these values during intro videos

**Status:** Applied. Using `[JsonIgnore]` is the correct approach — no race conditions.

---

## Issue 11: HandleVideoStateChange Double-Fire

**Symptom:** Inconsistent pause state when switching games, especially after interacting with EML mute button. Music playback and button states become confused.

**Root Cause:** Two separate PropertyChanged handlers were both listening for `VideoIsPlaying` and `ThemeOverlayActive` changes:
- `OnSettingsChanged` (direct `_settings.PropertyChanged` subscription)
- `OnSettingsServicePropertyChanged` (via `SettingsService` relay of `PropertyChanged`)

Both called `HandleVideoStateChange()` / `HandleThemeOverlayChange()`, causing double-fire on every state change. The second call could interact with deferred playback logic or fire while the first call's fade transition was still in progress.

**Fix (applied):**
- Removed `VideoIsPlaying` and `ThemeOverlayActive` handling from `OnSettingsChanged`
- These are now handled exclusively through `OnSettingsServicePropertyChanged` (via SettingsService relay), which is more robust as it automatically re-subscribes when settings instances change

**Status:** Applied. Eliminates a whole class of double-fire state inconsistencies.

---

## Issue 12: Music/Video Audio Overlap — Preemptive Trailer Detection

**Symptom:** When switching to a game with an EML trailer in fullscreen (Aniki theme), UPS music plays audibly for ~200-500ms before fading out as the video starts.

**Root Cause (multi-layered):**
1. EML has a 220ms debounce before loading video sources after game selection
2. UPS starts music in `HandleGameSelected` → `PlayGameMusic` immediately
3. By the time EML triggers (via theme Tag → ThemeOverlayActive, or MediaElement_Opened → VideoIsPlaying), music is already audible
4. The 0.3s fade-out duration compounds the overlap

**Investigation and approaches tried:**

### Approach A: Ignore mute state in MediaElementsMonitor (REVERTED)
- Removed `!mediaElement.IsMuted && mediaElement.Volume > 0` from `isPlaying` check
- **Problem:** Muted videos (user intentionally muted to hear music) caused UPS to pause

### Approach B: Sticky mute tracking (REVERTED)
- Tracked which MediaElements were seen playing unmuted in a HashSet
- Once in the set, muting wouldn't flip VideoIsPlaying false
- **Problem:** Unreliable, didn't work consistently

### Approach C: PauseImmediate — instant volume cut (REVERTED)
- Added `PauseImmediate()` to MusicFader that set volume to 0 and paused instantly
- Used for Video/ThemeOverlay pause sources
- **Problem:** Sounded awful — no fade transition at all, audible click/pop

### Approach D: EmlVideoMonitor — WPF visual tree integration (REVERTED)
- New `EmlVideoMonitor` class that scanned WPF visual tree for EML's `VideoPlayerControl`
- Subscribed to EML's `SettingsModel.Settings.IsVideoPlaying` via `INotifyPropertyChanged`
- Provided instant detection at the exact moment EML called `MediaPlay()`
- **Problem:** Redundant — Aniki theme already has XAML triggers that bind EML's `IsVideoPlaying` to UPS's `MusicControl.Tag`, achieving the same thing through `ThemeOverlayActive`. For themes without triggers, `MediaElementsMonitor` remains the fallback.

### Approach E: Short video fade-out duration (APPLIED)
- Added `VideoFadeOutDuration = 0.12` constant in `Constants.cs`
- `AddPauseSource` uses `_fadeOutOverride` for Video/ThemeOverlay sources
- Fader lambda reads `_fadeOutOverride ?? _fadeOutDuration` each tick
- Override cleared in `RemovePauseSource` when Video/ThemeOverlay removed
- Normal fade-out (0.3s) preserved for other sources (FocusLoss, Minimized, etc.)

### Approach F: Preemptive trailer file detection (APPLIED)
- In `OnGameSelected`, before calling `HandleGameSelected`, checks if the selected game has an EML trailer file on disk:
  - `{ConfigPath}/ExtraMetadata/games/{GameId}/VideoTrailer.mp4`
  - `{ConfigPath}/ExtraMetadata/games/{GameId}/VideoMicrotrailer.mp4`
- If trailer exists and `PauseOnTrailer` is enabled, preemptively calls `AddPauseSource(Video)`
- Music loads in `PlayGameMusic` but starts already paused — zero audible overlap
- Cleanup: `ClearAllPauseSources()` (called by both `PlayGameMusic` and `Stop()`) removes the preemptive Video source when switching to non-trailer games
- `File.Exists` is fast (filesystem metadata only)

**Key architectural insight from EML source analysis:**
- EML's `VideoPlayerControl` exposes `SettingsModel.Settings.IsVideoPlaying` (INotifyPropertyChanged)
- Themes with EML triggers (like Aniki) bind this to `MusicControl.Tag` → `ThemeOverlayActive` → instant detection
- This is the same pattern PlayniteSound used — theme-based integration is primary, MediaElement scanning is fallback
- The 220ms gap is EML's debounce, unavoidable from the detection side — must be addressed preemptively

**Status:** Approaches E and F applied together. Preemptive file detection eliminates the overlap entirely for games with local trailers. Short fade-out provides a faster transition for edge cases.

### Approach G: Preserve Video/ThemeOverlay in ClearAllPauseSources (REVERTED)
- Modified `ClearAllPauseSources()` to preserve Video and ThemeOverlay sources alongside FocusLoss, Minimized, SystemTray, and Manual
- Theory: Preemptive `AddPauseSource(Video)` in `OnGameSelected` was being immediately cleared by `ClearAllPauseSources()` in `PlayGameMusic`
- Also replaced `_fadeOutOverride` (nullable double) with `_videoFadeActive` (bool flag) for cleaner state management
- **Problem:** Did not fix the overlap. Symptoms persisted or worsened:
  - Music still stops ~1 second after trailer starts
  - When stopping trailer, music does quick fade-in/out glitch before playing normally
  - In fullscreen, fade-out before video is too long, and resume is instant (no fade-in)
- **Root cause analysis was incorrect:** The actual issue may be timing/ordering of events, not the pause source clearing

---

## Updated Summary: Applied Fixes (Current Session)

### From baseline `8486955`, the following are applied (some committed, some uncommitted):

| # | Issue | Fix | Status |
|---|-------|-----|--------|
| 1 | Stale settings reference | `UpdateSettings()` + guards | Committed (`891f8c1`) |
| 4 | VideoIsPlaying in ShouldPlayMusic | Removed, uses PauseSource.Video | Uncommitted |
| 7 | Stuck pause on setting disable | Always-remove on restore + cleanup in OnSettingsChanged | Uncommitted |
| 9 | Preemptive MediaElement_Opened | Set VideoIsPlaying=true on MediaOpened | Uncommitted |
| 10 | Settings persistence | `[JsonIgnore]` on VideoIsPlaying/ThemeOverlayActive | Uncommitted |
| 11 | Double-fire handlers | Removed duplicate from OnSettingsChanged | Uncommitted |
| 12E | Short video fade-out | `VideoFadeOutDuration=0.12`, `_fadeOutOverride` in AddPauseSource | Uncommitted |
| 12F | Preemptive trailer detection | `GameHasTrailer()` + preemptive AddPauseSource(Video) in OnGameSelected | Uncommitted |

### Not yet applied:
| # | Issue | Notes |
|---|-------|-------|
| 2 | ThemeOverlay watchdog | 60s safety timeout — safe defensive fix |
| 3 | Grace period | 5-tick delay before timer stop — prevents flicker |
| 5 | SkipFirstSelection fullscreen gate | Desktop mode always plays on first selection |
| 6 | FocusLoss startup removal | Prevents stuck pause on startup |
| 8 | ClearAllPauseSources comment | Comment-only, low priority |

---

## Test Matrix

After each phase, test with:

| Theme | Mode | What to Check |
|-------|------|---------------|
| Dune | Desktop | Music plays on start, music pauses when EML trailer plays, music resumes after trailer |
| Aniki | Fullscreen | ThemeOverlayActive respected, music doesn't play during intro overlay |
| Aniki | Fullscreen | No music/video overlap on game switch (preemptive detection) |
| Any | Desktop | First game selection plays music immediately |
| Any | Fullscreen | SkipFirstSelectionAfterModeSwitch works correctly |
| Any | Either | PauseOnFocusLoss: music plays on startup, pauses on alt-tab, resumes on return |
| Any | Either | Disable PauseOnFocusLoss while unfocused → music should resume |
| Any | Either | Manual pause survives game switch |
| Any | Either | Muting EML video → UPS music plays; unmuting → UPS pauses correctly |
| Any | Either | Restart Playnite → music plays (VideoIsPlaying not persisted) |
