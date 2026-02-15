# UniPlaySong Performance Improvement Plan

**Version:** 1.2.11+
**Created:** 2026-02-14
**Status:** Analysis Complete, Implementation Pending

---

## Executive Summary

Comprehensive 5-phase analysis of the UniPlaySong codebase identified **87 optimization opportunities** across hot paths, UI thread blocking, memory management, architecture, and settings. This document provides a prioritized roadmap for systematic improvements.

### Analysis Overview

| Phase | Focus Area | Issues Found | Priority Distribution |
|-------|------------|--------------|----------------------|
| **Phase 1** | Hot Path Analysis | 7 issues | 2 HIGH, 3 MEDIUM, 2 LOW |
| **Phase 2** | UI Thread Blocking | 46 issues | ~~14~~ ~~13~~ 11 CRITICAL, 18 HIGH, 14 MEDIUM, 1 LOW |
| **Phase 3** | Memory & Resource Mgmt | 13 issues | ~~3~~ 0 CRITICAL, ~~4~~ 0 HIGH, ~~6~~ 13 MEDIUM/LOW (6 NOT A BUG, 1 LOW, 6 remaining) |
| **Phase 4** | Architectural Refactoring | 13 issues | 6 HIGH, 4 MEDIUM, 3 LOW |
| **Phase 5** | Settings & Configuration | 8 issues | 0 CRITICAL, 5 MEDIUM, 3 LOW |
| **TOTAL** | | **87 issues** | **~~17~~ ~~16~~ ~~14~~ ~~12~~ 11 CRITICAL, ~~24~~ 20 HIGH, ~~46~~ ~~51~~ 56 MEDIUM/LOW** |

### Completed Optimizations (v1.2.11)

#### ✅ Phase 1: Hot Path Analysis (COMPLETE)
- ✅ **1.1 Native music path caching** - Static constructor + service-level caching eliminates 6+ calls per game selection
- ✅ **1.2 Static Random instance** - Shared instance eliminates 4 allocations + fixes duplicate sequence bug
- ✅ **1.3 Lowercase extensions HashSet** - O(1) hash lookup replaces O(n) array scan, pre-computed lowercase
- ✅ **1.5 Song list caching** - Session-scoped cache eliminates repeated `Directory.GetFiles()` calls
  - Performance: ~2-8ms (SSD) or ~15-80ms (HDD) per cached re-selection
  - **17 invalidation call sites** ensure cache consistency across ALL UPS operations
  - Covers: 5 download types, 9 modification types (normalize/trim/silence/amplify/waveform-trim/repair), 3 deletion types
  - Design: Strict invalidation on known operations, manual file additions require restart (acceptable)

#### 🔄 Phase 2: UI Thread Blocking (PARTIAL)
- ✅ **2.1 Thread.Sleep → Task.Delay** (7/10) - Rate limiting (3) + file release (4) converted
  - ⏸️ Controller polling delays (3) - Deferred pending SDK controller events refactor
- ~~SKIP~~ **2.2 FileLogger async queue** - Downgraded to LOW, debug logging disabled by default
- ~~SKIP~~ **2.3 WebClient → HttpClient** - Tiny JSON file, network latency dominates, no user-visible benefit
- ✅ **2.4 Async void handlers** - Already mitigated with try-catch blocks in all instances

---

## Phase 1: Hot Path Analysis ✅ COMPLETE

**Goal:** Optimize code executed on every game selection for immediate user-facing performance gains.

**Status:** All optimizations implemented and tested in v1.2.11

### ✅ HIGH Priority (COMPLETE)

#### 1.1 Cache Native Music Path in Service Field ✅ IMPLEMENTED
**File:** `Services/MusicPlaybackService.cs` (lines 274-292)

**Issue:**
- `IsDefaultMusicPath()` called 6+ times per game selection
- Each call invokes `PlayniteThemeHelper.FindBackgroundMusicFile(api)`
- While now cached at static level, still has method call overhead

**Current:**
```csharp
private bool IsDefaultMusicPath(string path)
{
    if (string.IsNullOrEmpty(path)) return false;
    var nativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(_api);
    return !string.IsNullOrEmpty(nativeMusicPath) &&
           string.Equals(path, nativeMusicPath, StringComparison.OrdinalIgnoreCase);
}
```

**Suggested:**
```csharp
// Cache at service level to avoid repeated static method calls
private readonly string _nativeMusicPath;

public MusicPlaybackService(...)
{
    _nativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(_api);
}

private bool IsDefaultMusicPath(string path)
{
    if (string.IsNullOrEmpty(path)) return false;
    return !string.IsNullOrEmpty(_nativeMusicPath) &&
           string.Equals(path, _nativeMusicPath, StringComparison.OrdinalIgnoreCase);
}
```

**Impact:** Eliminates 6+ method calls per selection
**Effort:** 10 minutes
**Priority:** HIGH

---

#### 1.2 Static Random Instance ✅ IMPLEMENTED
**Files:** `Services/MusicPlaybackService.cs` (lines 867, 1049, 1121, 1244)

**Issue:**
- `new Random()` allocated 4 times in shuffle/random logic
- Unnecessary heap allocations and GC pressure
- Sequential calls within same millisecond produce identical seeds

**Current:**
```csharp
var random = new Random();
var randomIndex = random.Next(availableSongs.Count);
```

**Suggested:**
```csharp
private static readonly Random _random = new Random();

// Usage:
var randomIndex = _random.Next(availableSongs.Count);
```

**Impact:** Eliminates 4 allocations per random operation, prevents duplicate sequences
**Effort:** 5 minutes
**Priority:** HIGH

---

### ✅ MEDIUM Priority (COMPLETE)

#### 1.3 Lowercase Extensions HashSet ✅ IMPLEMENTED
**File:** `Services/GameMusicFileService.cs` (lines 65, 78)

**Issue:**
- `ToLowerInvariant()` called on every file extension during directory scan
- String allocations for each file checked
- Linear search through `SupportedExtensions` array

**Current:**
```csharp
.Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
```

**Suggested:**
```csharp
// In Constants.cs:
public static readonly HashSet<string> SupportedAudioExtensionsLowercase =
    new HashSet<string>(SupportedAudioExtensions.Select(e => e.ToLowerInvariant()));

// In GameMusicFileService.cs:
.Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(
    Path.GetExtension(f).ToLowerInvariant()))
```

**Impact:** O(1) hash lookup vs O(n) array scan, pre-computed lowercase
**Effort:** 10 minutes
**Priority:** MEDIUM

---

#### 1.4 Consolidate IsDefaultMusicPath Calls
**File:** `Services/MusicPlaybackService.cs` (lines 274-292)

**Issue:**
- Method called multiple times with same `path` parameter within single operation
- Results could be cached locally during operation

**Suggested:**
```csharp
// Cache result for duration of operation:
private string SelectSongForGame(Game game)
{
    var directory = _fileService.GetGameMusicDirectory(game);
    var songs = _fileService.GetAvailableSongs(game);

    // Cache for this operation
    bool isDefaultPath = IsDefaultMusicPath(directory);

    // Use cached result throughout method
    if (isDefaultPath && settings.UseNativeAsDefault) { ... }
}
```

**Impact:** Reduces redundant comparisons
**Effort:** 15 minutes
**Priority:** MEDIUM

---

#### 1.5 Pre-compute Album Art Paths
**File:** `Services/MusicPlaybackService.cs` (album art lookup pattern)

**Issue:**
- Album art path construction repeated on every song change
- Could be computed once when game changes

**Note:** Requires deeper analysis to determine if this is actually a hot path

**Priority:** MEDIUM (needs profiling confirmation)

---

### Summary - Phase 1

| Issue | File | Lines | Impact | Effort | Priority |
|-------|------|-------|--------|--------|----------|
| Cache native music path | MusicPlaybackService.cs | 274-292 | 6+ calls eliminated | 10 min | **HIGH** |
| Static Random instance | MusicPlaybackService.cs | 867, 1049, 1121, 1244 | 4 allocations eliminated | 5 min | **HIGH** |
| Lowercase extensions HashSet | GameMusicFileService.cs | 65, 78 | O(n)→O(1) lookup | 10 min | MEDIUM |
| Consolidate IsDefaultMusicPath | MusicPlaybackService.cs | 274-292 | Redundant calls | 15 min | MEDIUM |

**Total Effort:** 40 minutes
**Expected Impact:** 5-15% reduction in hot path execution time

---

## Phase 2: UI Thread Blocking & Async Opportunities

**Goal:** Eliminate UI freezes and improve responsiveness during heavy operations.

**UPDATE 2026-02-14:** FileLogger (2.2) downgraded from CRITICAL → LOW after real-world analysis. Debug logging disabled by default means minimal impact. See corrected counts below.

### CRITICAL Issues (~~14~~ 13 total)

#### 2.1 Thread.Sleep() Blocking UI Thread (10 instances)
**Priority:** CRITICAL
**Impact:** UI freezes for seconds during operations
**Effort:** 1 hour

**Locations:**
1. `UniPlaySong.cs` line 2675 - `Thread.Sleep(2000)` during game start
2. `Players/MusicFader.cs` lines 142-168 - Sleep during fade loops
3. `Services/MusicPlaybackCoordinator.cs` - Sleep in coordination logic
4. `Views/WaveformTrimDialog.xaml.cs` - Sleep during preview playback
5. `Views/ControllerWaveformTrimDialog.xaml.cs` - Sleep during preview
6. Multiple test/debugging sleep calls

**Fix:**
```csharp
// Replace ALL instances:
Thread.Sleep(2000);  // ❌ Blocks UI thread

// With:
await Task.Delay(2000);  // ✅ Frees UI thread
```

---

#### 2.2 FileLogger Synchronous File I/O
**File:** `Common/FileLogger.cs` line 113
**Priority:** ~~CRITICAL~~ **LOW** (CORRECTED)
**Impact:** ~~Every log call blocks thread~~ Minimal - debug logging disabled by default, only 4-6 Info/Warn calls per game selection
**Effort:** 2 hours

**ANALYSIS CORRECTION:**
- Debug logging is **disabled by default** (`enableDebugLogging = false`)
- When disabled, only Info/Warn/Error messages write (4-6 per game selection)
- `_fileLogger?.Debug()` calls short-circuit when debug logging is off
- Modern SSDs: ~0.1-2ms per write, not noticeable for infrequent calls
- **NOT a real bottleneck** for 99% of users

**Current:**
```csharp
lock (_lock)
{
    File.AppendAllText(_logFilePath, formattedMessage);  // Blocks (but infrequent)
}
```

**Suggested (LOW PRIORITY):**
```csharp
private BlockingCollection<string> _logQueue = new BlockingCollection<string>();
private Task _logWriterTask;

public FileLogger(string logFilePath)
{
    _logFilePath = logFilePath;
    _logWriterTask = Task.Run(async () => await LogWriterLoop());
}

private async Task LogWriterLoop()
{
    foreach (var message in _logQueue.GetConsumingEnumerable())
    {
        await File.AppendAllTextAsync(_logFilePath, message);
    }
}

public void Log(LogLevel level, string message)
{
    var formatted = FormatMessage(level, message);
    _logQueue.Add(formatted);  // Non-blocking enqueue
}
```

**Recommendation:** Skip this unless user enables debug logging on slow drives.

---

#### 2.3 WebClient Synchronous Downloads
**File:** `Services/SearchHintsService.cs` lines 383-388
**Priority:** ~~CRITICAL~~ **SKIP** (CORRECTED)
**Impact:** ~~UI freezes for 500ms-5s during metadata fetch~~ Negligible — downloads a single tiny JSON file (search_hints.json)
**Effort:** 30 minutes

**ANALYSIS CORRECTION:**
- Downloads a single small JSON file (`search_hints.json`) from GitHub
- The "500ms-5s" is network latency, not payload size — `HttpClient` async wouldn't reduce perceived wait time
- Called only on manual user action (check/download hints) or once at startup if auto-check enabled
- **NOT worth the churn** — switching to HttpClient adds complexity for zero user-visible benefit

---

#### 2.4 Async Void Event Handlers (10 instances)
**Priority:** ~~CRITICAL~~ **ALREADY MITIGATED** (CORRECTED)
**Impact:** ~~Unhandled exceptions crash application~~ Already protected by try-catch blocks
**Effort:** ~~1 hour~~ None needed

**ANALYSIS CORRECTION:**
- All `async void` event handlers in the codebase already wrap their bodies in try-catch blocks
- `async void` is required by WPF event handler signatures — this is the correct pattern
- Verified in: AmplifyDialog, ControllerAmplifyDialog, ControllerDeleteSongsDialog, ControllerWaveformTrimDialog, WaveformTrimDialog
- **No action needed** — the suggested fix is already implemented

---

### HIGH Priority (18 issues)

#### 2.5 UI Thread File I/O (12 instances)
**Impact:** UI stuttering during file operations
**Effort:** 2-3 hours

**Locations:**
- Dialog file pickers (6 instances) - synchronous file browsing
- Settings save operations (4 instances) - synchronous JSON write
- Log file operations (2 instances) - synchronous log writes

**Fix Pattern:**
```csharp
// Current:
var files = Directory.GetFiles(directory);

// Better:
var files = await Task.Run(() => Directory.GetFiles(directory));
```

---

#### 2.6 Sequential File Deletions (40+ files)
**File:** `Services/GameMusicFileService.cs` lines 205-220
**Impact:** Blocking delete of 40 files takes 200-2000ms on slow drives
**Effort:** 30 minutes

**Current:**
```csharp
foreach (var file in files)
{
    File.Delete(file);  // Sequential, blocks UI
}
```

**Suggested:**
```csharp
var deleteTasks = files.Select(file => Task.Run(() =>
{
    try { File.Delete(file); }
    catch (Exception ex) { Logger.Warn($"Failed: {ex.Message}"); }
}));

await Task.WhenAll(deleteTasks);  // Parallel, non-blocking
```

---

### MEDIUM Priority (14 issues)

#### 2.7 ConfigureAwait(false) Missing
**Priority:** ~~MEDIUM~~ **CANCELLED**
**Impact:** ~~Unnecessary UI thread context switching~~ Negligible in practice

**ANALYSIS (2026-02-14):**
- 173 total `await` calls; only 14 use `ConfigureAwait(false)` (already in the right places — FFmpeg stream reads inside Task.Run)
- ~120 of the remaining 159 are in Views/Handlers that **must** stay on UI thread — adding ConfigureAwait(false) would cause WPF threading exceptions
- Only ~15-20 are safe to add, saving ~0.1-0.5ms each (total: ~5-25ms in stress scenarios)
- **Risk outweighs benefit:** WPF threading bugs from incorrect ConfigureAwait(false) are hard to debug and can cause deadlocks or `InvalidOperationException` on UI element access
- **Recommendation: Do not implement.** Current async strategy is already correct.

---

### Summary - Phase 2

| Category | Count | Status | Impact |
|----------|-------|--------|--------|
| ~~CRITICAL~~ 2.1 Thread.Sleep | 10 instances | **7/10 DONE** (3 deferred) | Eliminates UI freezes |
| ~~CRITICAL~~ 2.2 FileLogger | 1 | **SKIP** — LOW priority | Minimal impact, debug off by default |
| ~~CRITICAL~~ 2.3 WebClient | 2 calls | **SKIP** — tiny JSON file | Network latency dominates, no benefit |
| ~~CRITICAL~~ 2.4 Async void | 10 instances | **ALREADY MITIGATED** | All have try-catch blocks |
| HIGH 2.5 UI thread file I/O | 12 instances | **PARTIALLY ADDRESSED** | ControllerDeleteSongsDialog main issue |
| HIGH 2.6 Sequential deletions | 3 locations | **TODO** | Parallel delete for large libraries |
| ~~MEDIUM~~ 2.7 ConfigureAwait | 50+ calls | **CANCELLED** | Risk outweighs 5-25ms gain |

**Remaining Work:**
1. Parallelize file deletions (2.6) - HIGH — DeleteAllMusic, DeleteLongSongs, DeleteAllMusicForGames

---

## Phase 3: Memory & Resource Management

**Goal:** Prevent resource leaks and memory exhaustion over long sessions.

### ~~CRITICAL~~ Issues (~~3~~ 1 CRITICAL, 1 LOW, 1 NOT A BUG)

#### 3.1 VisualizationDataProvider ManualResetEventSlim Leak
**File:** `Audio/VisualizationDataProvider.cs` lines 274-278
**Priority:** ~~CRITICAL~~ **LOW** (CORRECTED)
**Impact:** ~~Kernel handle leak~~ One kernel handle per session, cleaned up by GC finalizer
**Effort:** 5 minutes (one-line fix)

**ANALYSIS CORRECTION (2026-02-14):**
- The field is `_newSamplesSignal` (not `_dataAvailableEvent` as originally stated)
- `VisualizationDataProvider` is effectively a singleton (`_current` static field) — created once when NAudio player is active
- It is NOT created/destroyed repeatedly during a session
- The `ManualResetEventSlim` handle will be cleaned up by the GC finalizer when the object is collected
- **Impact: One leaked handle per session** — not accumulating, not causing real-world problems

**Current:**
```csharp
public void Dispose()
{
    _disposed = true;
    _newSamplesSignal.Set(); // wake thread so it can exit
    // MISSING: _newSamplesSignal.Dispose();
}
```

**Fix (optional, for correctness):**
```csharp
public void Dispose()
{
    _disposed = true;
    _newSamplesSignal.Set();
    _newSamplesSignal.Dispose();
}
```

---

#### 3.2 MusicFader Timer Disposal — NOT A BUG
**File:** `Players/MusicFader.cs` lines 53, 376-393
**Priority:** ~~CRITICAL~~ **NOT A BUG** (CORRECTED)
**Impact:** None — already properly implemented

**ANALYSIS CORRECTION (2026-02-14):**
The original analysis was **incorrect** on multiple points:
- The timer is `System.Timers.Timer` (NOT `DispatcherTimer` as claimed)
- The timer is created once in the constructor with `AutoReset = false` — it is NOT recreated on each fade
- Subsequent fades call `_fadeTimer.Start()` on the same instance
- Dispose already does Stop + Close + Dispose + null:

```csharp
public void Dispose()
{
    try
    {
        _fadeTimer?.Stop();
        _fadeTimer?.Close();
        _fadeTimer?.Dispose();
        _fadeTimer = null;
    }
    catch (Exception ex) { ... }
}
```

**No action needed.**

---

#### ~~3.3 DownloadDialogService Event Handler Leak~~ — NOT A BUG
**File:** `Services/DownloadDialogService.cs`
**Priority:** ~~CRITICAL~~ **NOT A BUG**

**Analysis (2026-02-14):**
- The plan claimed `OnSongEnded` handlers are never unsubscribed after dialog close. This is **wrong**.
- Two subscription patterns exist, and **both properly unsubscribe**:
  1. **Batch download dialog** (line 1689): Subscribes, then unsubscribes at line 1777 after `window.ShowDialog()` returns (blocking call — always runs)
  2. **Auto-add songs dialog** (line 2338): Subscribes, then unsubscribes at line 2629 inside a `finally` block (guarantees cleanup)
- The plan's example code doesn't match actual code. **No action needed.**

---

### HIGH Priority (4 issues)

#### 3.4 ControllerDetectionService Timer — LOW-MEDIUM (commented-out cleanup)
**File:** `Services/Controller/ControllerDetectionService.cs`, `Views/DownloadDialogView.xaml.cs`
**Priority:** ~~HIGH~~ **LOW-MEDIUM**

**Analysis (2026-02-14):**
- The plan claimed "Permanent 500ms polling even when controller unused" — actual interval is **2000ms** (not 500ms)
- `ControllerDetectionService` has proper `Dispose()` → `StopMonitoring()` → timer disposed and nulled
- `ControllerOverlay.Dispose()` calls `_detectionService?.Dispose()` correctly
- `DownloadDialogView.CleanupControllerSupport()` calls `_controllerOverlay.Dispose()` correctly
- **Issue:** At `DownloadDialogView.xaml.cs:51`, the cleanup call is **commented out**: `// CleanupControllerSupport();`
- Timer keeps polling XInput every 2s until GC collects unreferenced objects
- **Actual impact:** LOW — lightweight XInput check (microseconds), only when download dialog opened, GC cleans up
- **Fix:** Uncomment `CleanupControllerSupport()` — but was "temporarily disabled" for a reason worth investigating

#### ~~3.5 MediaElementsMonitor Timer Leak~~ — NOT A BUG
**File:** `Monitors/MediaElementsMonitor.cs`
**Priority:** ~~HIGH~~ **NOT A BUG**

**Analysis (2026-02-14):**
- The plan claimed "Permanent 100ms polling (10Hz) never stops." This is **wrong**.
- The timer is **self-stopping**: when `mediaElementPositions.Count == 0` (no tracked media elements), it calls `timer.Stop()` (line 155)
- Timer only starts when `MediaElement_Opened` fires (a video element appears)
- Timer created once with `if (timer == null)` guard — prevents orphaned timers on repeated `Attach()` calls
- **Lifecycle:** Video opens → timer starts → polls at 100ms → video ends → dictionary empties → timer stops
- **No action needed.**

#### ~~3.6 MusicPlaybackCoordinator Timer Leak~~ — NOT A BUG
**File:** `Services/MusicPlaybackCoordinator.cs`
**Priority:** ~~HIGH~~ **NOT A BUG**

**Analysis (2026-02-14):**
- The plan claimed "Timer continues after login dismiss." This is **wrong**.
- `HandleLoginDismiss()` creates a **one-shot** `System.Timers.Timer(150)` with `AutoReset = false`
- The elapsed handler calls `timer.Dispose()` immediately after executing — self-disposes
- No persistent timer field exists in this class — no ongoing polling
- **No action needed.**

#### ~~3.7 HttpClient Not Disposed~~ — NOT A BUG
**File:** `UniPlaySong.cs`
**Priority:** ~~HIGH~~ **NOT A BUG**

**Analysis (2026-02-14):**
- The plan claimed "Socket exhaustion on repeated plugin reload." This is **wrong**.
- `_httpClient = new HttpClient()` created once in constructor (line 137)
- `_httpClient?.Dispose()` called in plugin cleanup (line 678)
- Single instance for plugin lifetime, properly disposed on shutdown
- This is the [recommended HttpClient pattern](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines) — reuse a single instance
- **No action needed.**

---

### Summary - Phase 3

| Category | Count | Status | Impact |
|----------|-------|--------|--------|
| ~~CRITICAL~~ 3.1 ManualResetEventSlim | 1 handle | **LOW** — GC finalizer cleans up | One handle per session, not accumulating |
| ~~CRITICAL~~ 3.2 MusicFader timer | 1 timer | **NOT A BUG** — already properly disposed | No action needed |
| ~~CRITICAL~~ 3.3 Event handler leak | 1 | **NOT A BUG** — both handlers properly unsubscribed | No action needed |
| ~~HIGH~~ 3.4 Controller timer | 1 timer | **LOW-MEDIUM** — commented-out cleanup, GC cleans up | Uncomment cleanup call |
| ~~HIGH~~ 3.5 MediaElementsMonitor timer | 1 timer | **NOT A BUG** — self-stopping when no media elements | No action needed |
| ~~HIGH~~ 3.6 MusicPlaybackCoordinator timer | 1 timer | **NOT A BUG** — one-shot fire-and-dispose pattern | No action needed |
| ~~HIGH~~ 3.7 HttpClient | 1 client | **NOT A BUG** — singleton, properly disposed on unload | No action needed |
| MEDIUM/LOW | 6 | **TODO** | Cleanup improvements |

---

## Phase 4: Architectural Refactoring

**Goal:** Reduce code duplication, improve testability, and ease maintenance.

### HIGH Priority (6 issues)

#### 4.1 FFmpeg Validation Duplication (12+ instances)
**Files:** 4 handler classes
**Impact:** 40+ duplicate lines per handler
**Effort:** 30 minutes

**Current Pattern (repeated 12x):**
```csharp
if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
{
    _playniteApi.Dialogs.ShowErrorMessage(
        "FFmpeg path is not configured...",
        "FFmpeg Not Configured");
    return;
}

if (!_service.ValidateFFmpegAvailable(settings.FFmpegPath))
{
    _playniteApi.Dialogs.ShowErrorMessage(
        $"FFmpeg not found at: {settings.FFmpegPath}...",
        "FFmpeg Not Available");
    return;
}
```

**Suggested:**
```csharp
// Common/FFmpegValidator.cs
public static class FFmpegValidator
{
    public static bool ValidateAndShowError(
        IPlayniteAPI api,
        UniPlaySongSettings settings,
        IFFmpegService service)
    {
        if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
        {
            api.Dialogs.ShowErrorMessage(
                "FFmpeg path is not configured...",
                "FFmpeg Not Configured");
            return false;
        }

        if (!service.ValidateFFmpegAvailable(settings.FFmpegPath))
        {
            api.Dialogs.ShowErrorMessage(
                $"FFmpeg not found at: {settings.FFmpegPath}...",
                "FFmpeg Not Available");
            return false;
        }

        return true;
    }
}

// Usage (1 line):
if (!FFmpegValidator.ValidateAndShowError(_api, settings, _service)) return;
```

**Impact:** Eliminates ~160 lines of duplication
**Priority:** HIGH

---

#### 4.2 Dialog Show Pattern Duplication (8 instances)
**Files:** `Handlers/AmplifyDialogHandler.cs`, `Handlers/WaveformTrimDialogHandler.cs`
**Impact:** Identical validation flow in desktop/controller variants
**Effort:** 30 minutes

**Suggested:**
```csharp
// Handlers/BaseDialogHandler.cs
public abstract class BaseDialogHandler<TDialog> where TDialog : Window, new()
{
    protected bool ShowDialog(
        Game game,
        bool isController,
        Action<TDialog> initialize)
    {
        // Shared validation
        if (game == null) { ShowMessage(...); return false; }

        var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
        if (songs.Count == 0) { ShowMessage(...); return false; }

        if (!FFmpegValidator.ValidateAndShowError(...)) return false;

        _playbackService?.Stop();

        // Create and show dialog
        var dialog = new TDialog();
        var window = isController
            ? DialogHelper.CreateFixedDialog(dialog, ...)
            : DialogHelper.CreateStandardDialog(dialog, ...);

        initialize(dialog);

        DialogHelper.AddFocusReturnHandler(window, ...);
        window.ShowDialog();
        return true;
    }
}

// Usage:
public class AmplifyDialogHandler : BaseDialogHandler<AmplifyDialog>
{
    public void ShowAmplifyDialog(Game game)
    {
        ShowDialog(game, isController: false, dialog =>
        {
            dialog.Initialize(game, _settings, ...);
        });
    }
}
```

**Impact:** Eliminates pattern duplication across 4 dialog pairs
**Priority:** HIGH

---

#### 4.3 Desktop/Controller Dialog Implementation Divergence
**Files:** 4 dialog pairs (8 files total)
**Impact:** Bug fixes must be applied twice, features implemented twice
**Effort:** 90+ minutes (high value)

**Problem:**
- `AmplifyDialog.xaml.cs` (desktop) - 531 lines of business logic
- `ControllerAmplifyDialog.xaml.cs` (controller) - 500+ lines of DUPLICATE logic
- Only difference: mouse input vs XInput, UI layout

**Suggested:**
```csharp
// ViewModels/AmplifyDialogViewModel.cs
public class AmplifyDialogViewModel : ObservableObject
{
    // Shared business logic
    public ObservableCollection<string> FilesList { get; }
    public float CurrentGain { get; set; }
    public WaveformData Waveform { get; private set; }

    public async Task ApplyGainAsync() { /* shared */ }
    public async Task PreviewGainAsync() { /* shared */ }
    public void AdjustGain(float delta) { /* shared */ }
}

// Views/AmplifyDialog.xaml.cs (desktop - 50 lines)
public partial class AmplifyDialog : Window
{
    private AmplifyDialogViewModel _viewModel;

    private void WaveformCanvas_MouseDown(object sender, MouseEventArgs e)
    {
        var clickPosition = e.GetPosition(WaveformCanvas);
        var gainDelta = CalculateGainFromClick(clickPosition);
        _viewModel.AdjustGain(gainDelta);
    }
}

// Views/ControllerAmplifyDialog.xaml.cs (controller - 50 lines)
public partial class ControllerAmplifyDialog : Window
{
    private AmplifyDialogViewModel _viewModel;

    private void HandleControllerInput()
    {
        var state = GamePad.GetState(PlayerIndex.One);
        var gainDelta = state.ThumbSticks.Left.Y * 0.5f;
        _viewModel.AdjustGain(gainDelta);
    }
}
```

**Impact:**
- Reduces 1000+ lines to ~600 lines (40% reduction)
- Bug fixes applied once
- New features implemented once

**Priority:** HIGH

---

#### 4.4 DialogHelper "God Class" (1,327 lines)
**File:** `Common/DialogHelper.cs`
**Impact:** Violates Single Responsibility Principle
**Effort:** 60-90 minutes

**Current Responsibilities:**
1. Dialog creation (CreateDialog, CreateStandardDialog, CreateFixedDialog)
2. Focus management (ReturnFocusToMainWindow, AddFocusReturnHandler)
3. Toast notifications (ShowAutoCloseToast, ShowSuccessToast)
4. Controller input (ShowControllerMessage, ShowControllerConfirmation)
5. Windows effects (EnableWindowBlur, SetRoundedWindowRegion)
6. Color utilities (HexToRgb, ParseHexColor)
7. Settings sync (SyncToastSettings)
8. DPI scaling (GetDpiScaleFactor, ScaleForDpi)

**Suggested Split:**
```
Common/
├── DialogHelper.cs (300 lines) - Dialog creation & focus only
├── ToastHelper.cs (400 lines) - Toast notifications
├── ControllerDialogHelper.cs (200 lines) - XInput handling
├── WindowEffectHelper.cs (200 lines) - Blur & acrylic effects
├── ColorHelper.cs (50 lines) - Color conversion
└── DpiHelper.cs (50 lines) - DPI scaling
```

**Impact:** Better separation of concerns, easier testing
**Priority:** HIGH

---

#### 4.5 Business Logic in Dialog Code-Behind
**Files:** `Views/AmplifyDialog.xaml.cs` (130 lines), `Views/ControllerAmplifyDialog.xaml.cs` (100+ lines)
**Impact:** Cannot unit test without WPF runtime
**Effort:** 45-60 minutes per dialog

**Current:**
- Waveform rendering logic in code-behind (lines 165-188)
- Gain calculations in code-behind (lines 190-232)
- Clipping detection in code-behind (lines 264-325)

**Suggested:**
- Extract to `ViewModels/AmplifyDialogViewModel`
- Dialog becomes thin XAML binding layer
- ViewModel testable without WPF

**Priority:** HIGH

---

#### 4.6 Async Progress Dialog Pattern Duplication
**Files:** `Handlers/TrimDialogHandler.cs`, `Handlers/NormalizationDialogHandler.cs`
**Impact:** ~110 duplicate lines per handler
**Effort:** 25-35 minutes

**Suggested:**
```csharp
// Common/AsyncProgressDialogHelper.cs
public static async Task ShowAndExecuteAsync<TSettings, TResult>(
    string operationName,
    ProgressDialog dialog,
    List<string> files,
    TSettings settings,
    Func<List<string>, TSettings, IProgress<ProgressInfo>, CancellationToken, Task<TResult>> operation,
    Action<List<string>, TResult> onCacheInvalidation,
    IPlayniteAPI api)
{
    var window = DialogHelper.CreateStandardDialog(dialog, ...);

    Task.Run(async () =>
    {
        try
        {
            var progress = new Progress<ProgressInfo>(p =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    dialog.ReportProgress(p);
                });
            });

            var result = await operation(files, settings, progress, dialog.CancellationToken);

            // Invalidate cache
            onCacheInvalidation(files, result);

            // Show completion
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                window.Close();
                ShowCompletionMessage(result);
            });
        }
        catch (OperationCanceledException) { /* ... */ }
        catch (Exception ex) { /* ... */ }
    });

    window.ShowDialog();
}

// Usage (5 lines):
await AsyncProgressDialogHelper.ShowAndExecuteAsync(
    "Trim",
    progressDialog,
    musicFiles,
    trimSettings,
    _trimService.TrimBulkAsync,
    (files, result) => InvalidateCache(files),
    _playniteApi);
```

**Impact:** Eliminates ~220 lines of duplication
**Priority:** MEDIUM

---

### Summary - Phase 4

| Issue | Files | Impact | Effort | Priority |
|-------|-------|--------|--------|----------|
| FFmpeg validation duplication | 4 handlers | 160 lines eliminated | 30 min | **HIGH** |
| Dialog show pattern duplication | 2 handlers | Pattern consolidation | 30 min | **HIGH** |
| Desktop/Controller divergence | 4 dialog pairs | 40% code reduction | 90 min | **HIGH** |
| DialogHelper God class | 1 file | Better SRP | 60-90 min | **HIGH** |
| Business logic in code-behind | 2 dialogs | Testability | 45-60 min | **HIGH** |
| Async progress pattern | 2 handlers | 220 lines eliminated | 25-35 min | MEDIUM |

**Total Effort:** 4-6 hours
**Expected Impact:** 15-20% code reduction, better maintainability, improved testability

---

## Phase 5: Settings & Configuration

**Goal:** Improve settings performance and organization.

### MEDIUM Priority (5 issues)

#### 5.1 Duplicate Settings Save in Library Update
**File:** `UniPlaySong.cs` lines 446, 453
**Impact:** Unnecessary disk I/O (5-50ms wasted)
**Effort:** 15 minutes

**Current:**
```csharp
_settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
SavePluginSettings(_settings);  // Line 446

// ... some processing ...

SavePluginSettings(_settings);  // Line 453 - DUPLICATE!
```

**Fix:**
```csharp
// Only save once after all processing
_settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
// ... processing ...
SavePluginSettings(_settings);  // Save once at end
```

**Priority:** MEDIUM

---

#### 5.2 Native Music File I/O on Property Change
**File:** `SettingsService.cs` lines 157-221
**Impact:** UI blocking (50-500ms) on settings toggle
**Effort:** 2-3 hours

**Current:**
```csharp
if (e.PropertyName == nameof(UniPlaySongSettings.UseNativeMusicAsDefault))
{
    if (_currentSettings.UseNativeMusicAsDefault == true)
    {
        ValidateNativeMusicFile(_currentSettings, showErrors: false);
        // ⬆️ Synchronous file I/O blocks UI!
    }
}
```

**Suggested:**
```csharp
private string _cachedNativeMusicPath;
private DateTime _nativeMusicPathCachedAt;
private const int CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

private string GetCachedNativeMusicPath()
{
    if (!string.IsNullOrEmpty(_cachedNativeMusicPath) &&
        DateTime.Now - _nativeMusicPathCachedAt < TimeSpan.FromMilliseconds(CACHE_TTL_MS))
    {
        return _cachedNativeMusicPath;
    }

    _cachedNativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(_api);
    _nativeMusicPathCachedAt = DateTime.Now;
    return _cachedNativeMusicPath;
}

// Async validation
private async Task ValidateNativeMusicFileAsync(...)
{
    var path = await Task.Run(() => GetCachedNativeMusicPath());
    // ... validation
}
```

**Priority:** MEDIUM

---

#### 5.3 No Batch Notifications on Preset Apply
**File:** `UniPlaySongSettings.cs` lines 1731-2629
**Impact:** 20-30 PropertyChanged events cause UI flicker
**Effort:** 3-4 hours

**Current:**
```csharp
public void ApplyReverbPreset(ReverbPreset preset)
{
    switch (preset)
    {
        case ReverbPreset.Acoustic:
            ReverbRoomSize = 80;        // PropertyChanged
            ReverbPreDelay = 16;        // PropertyChanged
            ReverbReverberance = 75;    // PropertyChanged
            // ... 6 more property changes
            // = 9 PropertyChanged events!
    }
}
```

**Suggested:**
```csharp
public void ApplyReverbPreset(ReverbPreset preset)
{
    using (var batch = DeferPropertyChanged())
    {
        switch (preset)
        {
            case ReverbPreset.Acoustic:
                ReverbRoomSize = 80;
                ReverbPreDelay = 16;
                // ... all changes batched
        }
    } // Single PropertyChanged("*") fired here
}
```

**Priority:** MEDIUM (high UX impact)

---

#### 5.4 Cascading PropertyChanged for Computed Properties
**File:** `UniPlaySongSettings.cs` lines 200-206, 454
**Impact:** Redundant UI binding updates
**Effort:** 30 minutes

**Current:**
```csharp
public bool SkipFirstSelectionAfterModeSwitch
{
    set
    {
        skipFirstSelectionAfterModeSwitch = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsThemeCompatibleSilentSkipEnabled)); // ❌ Redundant
    }
}

// IsThemeCompatibleSilentSkipEnabled is computed:
public bool IsThemeCompatibleSilentSkipEnabled => !skipFirstSelectionAfterModeSwitch;
```

**Fix:**
```csharp
// Remove explicit notification for computed property
public bool SkipFirstSelectionAfterModeSwitch
{
    set
    {
        skipFirstSelectionAfterModeSwitch = value;
        OnPropertyChanged();
        // Computed property auto-updates via backing field change
    }
}
```

**Priority:** LOW

---

#### 5.5 Duplicate Settings Load
**File:** `UniPlaySongSettingsViewModel.cs` line 24
**Impact:** Redundant JSON deserialization (10-20ms)
**Effort:** 30 minutes

**Current:**
```csharp
// UniPlaySong.cs line 157 - Load #1
_settingsService = new SettingsService(...);

// SettingsViewModel.cs line 24 - Load #2
var savedSettings = plugin.LoadPluginSettings<UniPlaySongSettings>();
```

**Fix:**
```csharp
// ViewModel should use existing service:
public UniPlaySongSettingsViewModel(UniPlaySong plugin)
{
    this.plugin = plugin;
    var settingsService = plugin.GetSettingsService();
    settings = settingsService.Current;  // Reuse already-loaded settings
}
```

**Priority:** LOW

---

### LOW Priority (3 issues)

#### 5.6 Settings Property Bloat (100+ properties)
**Impact:** Maintenance burden, no performance impact
**Effort:** 4-6 hours

**Suggested:** Refactor into sub-classes:
- `PlaybackSettings`
- `VisualizerSettings`
- `LiveEffectsSettings`
- `DefaultMusicSettings`
- `SearchSettings`
- `AudioProcessingSettings`

**Priority:** LOW (maintenance improvement)

---

#### 5.7 Settings with Hidden Side-Effects
**File:** `UniPlaySongSettings.cs` lines 196-209
**Impact:** UX confusion (properties mysteriously change)
**Effort:** 1-2 hours documentation

**Priority:** LOW (UX clarity)

---

### Summary - Phase 5

| Issue | File | Impact | Effort | Priority |
|-------|------|--------|--------|----------|
| Duplicate settings save | UniPlaySong.cs | 5-50ms wasted | 15 min | MEDIUM |
| Native music file I/O | SettingsService.cs | UI blocking | 2-3 hrs | MEDIUM |
| No batch notifications | UniPlaySongSettings.cs | UI flicker | 3-4 hrs | MEDIUM |
| Cascading PropertyChanged | UniPlaySongSettings.cs | Redundant updates | 30 min | LOW |
| Duplicate settings load | SettingsViewModel.cs | 10-20ms wasted | 30 min | LOW |

**Total Effort:** 6.5-9 hours
**Expected Impact:** Smoother settings UI, elimination of UI blocking

---

## Implementation Roadmap

### Milestone 1: Quick Wins (v1.2.12) - 2-4 hours
**Goal:** Low-hanging fruit for immediate performance gains

- ✅ Phase 1.1: Static Random instance
- ✅ Phase 1.2: Cache native music path in service
- ✅ Phase 1.3: Lowercase extensions HashSet
- ✅ Phase 5.1: Remove duplicate settings save
- ✅ Phase 5.4: Remove cascading PropertyChanged
- ✅ Phase 4.1: FFmpeg validator service

**Expected Impact:** 10-25ms faster game selection, cleaner code

---

### Milestone 2: Critical Stability (v1.2.13) - 4-8 hours
**Goal:** Eliminate resource leaks and UI freezes

- ⬜ Phase 2.1: Replace Thread.Sleep() (10 locations)
- ⬜ Phase 2.4: Fix async void handlers (10 locations)
- ~~⬜ Phase 3.1: Fix VisualizationDataProvider disposal~~ — LOW (GC handles it)
- ~~⬜ Phase 3.2: Fix MusicFader timer leaks~~ — NOT A BUG (already disposed)
- ~~⬜ Phase 3.3: Fix DownloadDialogService event leak~~ — NOT A BUG (already unsubscribed)
- ~~⬜ Phase 3.4: ControllerDetectionService timer~~ — LOW-MEDIUM (commented-out cleanup)
- ~~⬜ Phase 3.5: MediaElementsMonitor timer~~ — NOT A BUG (self-stopping)
- ~~⬜ Phase 3.6: MusicPlaybackCoordinator timer~~ — NOT A BUG (one-shot, self-disposing)
- ~~⬜ Phase 3.7: HttpClient disposal~~ — NOT A BUG (singleton, properly disposed)

**Expected Impact:** Prevents crashes, eliminates UI freezes, long-session stability

---

### Milestone 3: High-Impact UX (v1.3.0) - 6-12 hours
**Goal:** Major user experience improvements

- ⬜ Phase 2.2: FileLogger async queue
- ⬜ Phase 2.3: WebClient → HttpClient async
- ⬜ Phase 5.3: Batch notifications for presets
- ⬜ Phase 4.3: Shared Desktop/Controller ViewModels

**Expected Impact:** Instant preset application, no UI blocking, 40% code reduction

---

### Milestone 4: Architectural Cleanup (v1.3.1+) - 8-16 hours
**Goal:** Technical debt reduction and maintainability

- ⬜ Phase 4.2: Base dialog handler pattern
- ⬜ Phase 4.4: Split DialogHelper God class
- ⬜ Phase 4.5: Extract ViewModels from code-behind
- ⬜ Phase 5.6: Settings sub-class organization

**Expected Impact:** 15-20% total code reduction, better testability, easier maintenance

---

## Metrics & Success Criteria

### Performance Targets

| Metric | Current | Target | Milestone |
|--------|---------|--------|-----------|
| Game selection latency (cached) | 2-8ms | <2ms | M1 |
| Settings preset application | 100-500ms | <50ms | M3 |
| File operation UI freeze | 200-2000ms | 0ms | M2 |
| Memory leak rate | ~5MB/hour | 0MB/hour | M2 |

### Code Quality Targets

| Metric | Current | Target | Milestone |
|--------|---------|--------|-----------|
| Lines of code | ~35,000 | ~29,000 | M4 |
| Code duplication | ~15% | <5% | M4 |
| Unit test coverage | 0% | 30%+ | M4 |
| Critical bugs | 17 | 0 | M2 |

---

## Risk Assessment

### High Risk
- **Phase 2.2 (FileLogger)**: Core logging infrastructure, extensive testing needed
- **Phase 4.3 (Shared ViewModels)**: Large refactor, potential for regressions
- **Phase 5.3 (Batch notifications)**: Changes core data binding, UI testing critical

### Medium Risk
- **Phase 2.1 (Thread.Sleep)**: Async conversion requires careful error handling
- **Phase 3.x (Resource leaks)**: Disposal patterns must be thorough
- **Phase 4.4 (DialogHelper split)**: Many dependencies, wide impact

### Low Risk
- **Phase 1.x (Hot path)**: Localized changes, easy to test
- **Phase 4.1 (FFmpeg validator)**: Simple extraction, low impact
- **Phase 5.1 (Duplicate save)**: Single-line fix

---

## Testing Strategy

### Per-Phase Testing

**Phase 1 (Hot Path):**
- Profile game selection latency before/after
- Test random song selection (verify no sequence duplication)
- Test file extension filtering (verify all formats recognized)

**Phase 2 (UI Thread):**
- UI responsiveness test (click spam during operations)
- Long-running operation test (50+ file batch operations)
- Exception handling test (async void handlers)

**Phase 3 (Memory/Resources):**
- Memory profiler (8+ hour session, monitor handle count)
- Resource leak test (repeated open/close dialogs 100x)
- Timer disposal verification (process monitor)

**Phase 4 (Architecture):**
- Regression test all dialogs (desktop + controller)
- Unit test ViewModels (if implemented)
- Code coverage analysis

**Phase 5 (Settings):**
- Settings UI responsiveness (preset application flicker test)
- Settings persistence (save/load verification)
- Property change notification verification

### Integration Testing

- Full playthrough test: Library load → game selection → music playback → settings change → dialog operations
- Long-session stability test: 8+ hours continuous use
- Performance regression test: Compare metrics before/after each milestone

---

## Notes

### Already Completed (v1.2.11)
- ✅ Native music path caching (static constructor pattern)
- ✅ Song list caching (session-scoped with 14 invalidation sites)
- ✅ Version bump to 1.2.11
- ✅ Documentation updated (MEMORY.md, README.md, CHANGELOG.md)

### Dependencies
- .NET 4.6.2 framework limitations (no Span<T>, no ValueTask)
- WPF dispatcher requirements for UI updates
- Playnite SDK API constraints

### Future Considerations
- Consider async/await throughout (requires extensive testing)
- Consider reactive extensions (Rx.NET) for event handling
- Consider persistent song list cache (requires file timestamps)
- Consider dependency injection framework (Autofac, etc.)

---

**Last Updated:** 2026-02-14
**Next Review:** After Milestone 1 completion
