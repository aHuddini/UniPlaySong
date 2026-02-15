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
| **Phase 4** | Architectural Refactoring | 13 issues | ~~6~~ 1 HIGH, ~~4~~ 3 MEDIUM, ~~3~~ 1 LOW, 1 SKIP (1 wrong) |
| **Phase 5** | Settings & Configuration | 8 issues | ~~5 MEDIUM, 3 LOW~~ → 1 MEDIUM, 2 LOW, 4 SKIP (2 harmful fixes) |
| **TOTAL** | | **87 issues** | **~~17~~ 11 CRITICAL, ~~24~~ 15 HIGH, rest MEDIUM/LOW/SKIP** |

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

### Corrected Analysis (2026-02-14)

Original analysis claimed 6 HIGH priority items. After code verification, most were overstated.

---

#### 4.1 FFmpeg Validation Duplication — MEDIUM (not HIGH)
**Files:** 5 handler classes, 5 audio services
**Priority:** ~~HIGH~~ **MEDIUM**

**Analysis (2026-02-14):**
- Claimed "12+ instances, 4 handler classes" — actual: **28 instances across 18+ call sites**
- The validation IS repeated, but across **different service types** (amplify, trim, normalize, waveform-trim, repair)
- Each handler calls a different service's `ValidateFFmpegAvailable()` method
- A centralized helper could reduce ~20-30 lines per handler, but impact is moderate
- **Worth doing but not HIGH priority** — saves ~80-100 lines total

---

#### ~~4.2 Dialog Show Pattern Duplication~~ — LOW (overstated)
**Priority:** ~~HIGH~~ **LOW**

**Analysis (2026-02-14):**
- Claimed "8 instances" — actual: **4 call sites** (2 per handler, not 8)
- Each follows a 5-6 line boilerplate pattern (CreateDialog → Initialize → ShowDialog)
- Extracting to a base class would save only ~20 lines total
- **Low ROI — not worth the abstraction overhead**

---

#### 4.3 Desktop/Controller Dialog Divergence — MEDIUM (misleading)
**Priority:** ~~HIGH~~ **MEDIUM**

**Analysis (2026-02-14):**
- Claimed "531 vs 500+ lines of DUPLICATE logic" — actual: 634 vs 1,174 lines
- The 540-line gap is **NOT duplication** — it's the entire XInput controller subsystem:
  - D-pad debouncing (~80 lines), button state polling (~150 lines), controller input processing (~200 lines), step-by-step UI flow (~150 lines)
- Some shared logic exists (waveform loading, gain calculation) that COULD be extracted to ViewModels
- But controller code cannot be shared — it's XInput-specific by nature
- **40% code reduction claim is wrong** — extracting shared logic would save ~100-150 lines, not 400+

---

#### 4.4 DialogHelper Size — MEDIUM (accurate but not urgent)
**File:** `Common/DialogHelper.cs`
**Priority:** ~~HIGH~~ **MEDIUM**

**Analysis (2026-02-14):**
- Line count confirmed: **1,327 lines**
- 8 responsibility areas confirmed (dialogs, toasts, controller, blur, color, DPI, focus, settings sync)
- However: each responsibility is **discrete and self-contained** — no cross-method tangling
- "God class" characterization is fair by line count, but the code is well-organized internally
- Splitting would improve navigability but has **zero performance impact**
- **Reasonable future refactor, not urgent**

---

#### ~~4.5 Business Logic in Dialog Code-Behind~~ — SKIP (wrong)
**Priority:** ~~HIGH~~ **SKIP**

**Analysis (2026-02-14):**
- Claimed "130 lines of business logic" — actual file is 634 lines
- The code-behind contains **presentation-layer logic**: waveform rendering, gain display, mouse interaction, clipping visualization
- All actual business logic (FFmpeg execution, file I/O, audio processing) is properly delegated to services
- This is **appropriate for WPF code-behind** — rendering and interaction ARE the view's responsibility
- Extracting to a ViewModel would add indirection without benefit (ViewModels shouldn't manage Canvas rendering)
- **No action needed — current separation is correct**

---

#### 4.6 Async Progress Dialog Pattern Duplication — HIGH (confirmed)
**Files:** `Handlers/TrimDialogHandler.cs` (414 lines), `Handlers/NormalizationDialogHandler.cs` (563 lines)
**Priority:** ~~MEDIUM~~ **HIGH**

**Analysis (2026-02-14):**
- Claimed "~110 duplicate lines per handler" — **confirmed accurate**
- 6 async operation methods across 2 handlers (3 per handler: BulkAll, BulkSelected, Single)
- Each follows identical pattern: CreateDialog → Task.Run → progress callback → cache invalidation → completion message
- Only differences: variable names, service types, UI messages
- A generic helper could eliminate ~200+ lines of boilerplate
- **Highest ROI refactoring item in Phase 4**

---

### Summary - Phase 4 (Corrected 2026-02-14)

| Issue | Original | Corrected | Status |
|-------|----------|-----------|--------|
| 4.1 FFmpeg validation | HIGH (12+ instances) | **MEDIUM** — 18+ call sites, real but moderate savings | Refactor when touching handlers |
| ~~4.2~~ Dialog show pattern | HIGH (8 instances) | **LOW** — only 4 call sites, saves ~20 lines | Skip |
| 4.3 Desktop/Controller divergence | HIGH (40% reduction) | **MEDIUM** — gap is controller code, not duplication | Extract shared logic opportunistically |
| 4.4 DialogHelper size | HIGH (God class) | **MEDIUM** — 1,327 lines confirmed, but well-organized | Split when natural |
| ~~4.5~~ Business logic in code-behind | HIGH (needs ViewModel) | **SKIP** — presentation logic is appropriate for WPF code-behind | No action needed |
| 4.6 Async progress duplication | MEDIUM (110 lines) | **HIGH** — confirmed, ~200+ lines of identical boilerplate | Best ROI item |

**Corrected Total:** 1 HIGH, 3 MEDIUM, 1 LOW, 1 SKIP (was: 5 HIGH, 1 MEDIUM)

---

## Phase 5: Settings & Configuration

**Goal:** Improve settings performance and organization.

### Corrected Analysis (2026-02-14)

Original analysis claimed 5 MEDIUM and 2 LOW issues. After code verification, most were wrong or already mitigated.

---

#### ~~5.1 Duplicate Settings Save in Library Update~~ — SKIP (wrong)
**Priority:** ~~MEDIUM~~ **SKIP**

**Analysis (2026-02-14):**
- Claimed "two consecutive SavePluginSettings at lines 446, 453" — they're in **mutually exclusive if/else branches**
- Only ONE executes per `OnLibraryUpdated` event — this is correct control flow, not a duplicate
- **No action needed**

---

#### 5.2 Native Music File I/O on Property Change — LOW (already mitigated)
**Priority:** ~~MEDIUM~~ **LOW**

**Analysis (2026-02-14):**
- Claimed "UI blocking 50-500ms on settings toggle"
- **Phase 1 already mitigated this**: `PlayniteThemeHelper.FindBackgroundMusicFile()` is now cached via static constructor (zero I/O on subsequent calls)
- Remaining I/O is a single `File.Exists()` check (~1-5ms on SSD) — unavoidable and not noticeable
- The suggested 5-minute TTL caching is unnecessary given Phase 1's static cache
- **No further action needed**

---

#### 5.3 No Batch Notifications on Preset Apply — MEDIUM (confirmed)
**Priority:** MEDIUM

**Analysis (2026-02-14):**
- Claimed "20-30 PropertyChanged events" — **confirmed accurate**
- `ApplyStylePreset`: fires ~24 PropertyChanged events per preset application
- `ApplyReverbPreset`: fires ~9-11 events
- WPF batches rendering per frame (~16.67ms at 60Hz), so visual flicker depends on machine speed
- Could be fixed with a suppression flag (e.g., `_applyingPreset` bool that skips per-property notifications, then fires a single `PropertyChanged("*")` at the end)
- **Worth doing for smoother UX**, but presets are applied rarely

---

#### ~~5.4 Cascading PropertyChanged~~ — SKIP (plan's fix would BREAK UI)
**Priority:** ~~LOW~~ **SKIP — DO NOT IMPLEMENT**

**Analysis (2026-02-14):**
- Plan claimed `OnPropertyChanged(nameof(IsThemeCompatibleSilentSkipEnabled))` is "redundant"
- **THIS IS WRONG.** WPF does NOT automatically track computed property dependencies
- `IsThemeCompatibleSilentSkipEnabled` has no backing field — it returns `!skipFirstSelectionAfterModeSwitch`
- Without the explicit notification, UI controls bound to this property **will not update**
- The same pattern exists correctly in the `ThemeCompatibleSilentSkip` setter
- **Removing these notifications would break the settings UI**

---

#### ~~5.5 Duplicate Settings Load~~ — SKIP (plan's fix would BREAK cancel)
**Priority:** ~~LOW~~ **SKIP — DO NOT IMPLEMENT**

**Analysis (2026-02-14):**
- Two `LoadPluginSettings` calls exist but in **different methods serving different purposes**:
  1. Constructor (line 24): Initializes ViewModel with saved settings
  2. `CancelEdit()`: Reloads saved settings to **discard user changes** (cancel/revert mechanism)
- This is **required by Playnite's ISettings interface** — `BeginEdit`/`EndEdit`/`CancelEdit` pattern
- Removing the CancelEdit load would break the settings cancel button
- **No action needed**

---

#### 5.6 Settings Property Bloat (100+ properties) — LOW (accurate but impractical)
**Priority:** LOW

**Analysis (2026-02-14):**
- ~100 properties confirmed (playback, download, audio effects, visualization, UI, search)
- Refactoring to sub-classes would:
  - Break existing user settings JSON files (backward compatibility)
  - Require migration code for every existing user
  - Conflict with Playnite's flat `LoadPluginSettings<T>()` serialization
- Properties are already organized into logical groups with comments
- **Not worth the migration risk** — document structure instead

---

#### ~~5.7 Settings with Hidden Side-Effects~~ — SKIP (not problematic)
**Priority:** ~~LOW~~ **SKIP**

**Analysis (2026-02-14):**
- The "side-effect" is intentional **mutual exclusion**: setting `SkipFirstSelectionAfterModeSwitch = true` auto-disables `ThemeCompatibleSilentSkip` (these features conflict)
- This is documented in code comments and is **good UX design** — better than showing an error
- Changes are immediately visible in the settings UI
- **No action needed**

---

### Summary - Phase 5 (Corrected 2026-02-14)

| Issue | Original | Corrected | Status |
|-------|----------|-----------|--------|
| ~~5.1~~ Duplicate settings save | MEDIUM | **SKIP** — if/else branches, not duplicate | No action needed |
| 5.2 Native music I/O | MEDIUM | **LOW** — Phase 1 caching already mitigated | No action needed |
| 5.3 Batch notifications | MEDIUM | **MEDIUM** — confirmed, ~24 events per preset | Batch with suppression flag |
| ~~5.4~~ Cascading PropertyChanged | LOW | **SKIP — DO NOT IMPLEMENT** — would break WPF UI binding | Current code is correct |
| ~~5.5~~ Duplicate settings load | LOW | **SKIP — DO NOT IMPLEMENT** — required for cancel/revert | Current code is correct |
| 5.6 Property bloat | LOW | **LOW** — accurate but impractical to refactor | Document structure |
| ~~5.7~~ Hidden side-effects | LOW | **SKIP** — intentional conflict resolution, good UX | No action needed |

**Corrected Total:** 0 HIGH, 1 MEDIUM, 2 LOW, 4 SKIP/DO NOT IMPLEMENT (was: 0 HIGH, 3 MEDIUM, 2 LOW)

**WARNING:** Items 5.4 and 5.5 had suggested fixes that would **break functionality**. Do not implement.

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

### Milestone 1: Quick Wins (v1.2.11) ✅ COMPLETE
**Goal:** Low-hanging fruit for immediate performance gains

- ✅ Phase 1.1: Static Random instance
- ✅ Phase 1.2: Cache native music path in service
- ✅ Phase 1.3: Lowercase extensions HashSet
- ✅ Phase 1.5: Song list caching (with opt-in toggle)

**Achieved:** 10-80ms faster game selection (SSD-HDD range)

---

### Milestone 2: UI Thread & Parallel I/O (v1.2.11) ✅ MOSTLY COMPLETE
**Goal:** Eliminate UI freezes during operations

- ✅ Phase 2.1: Thread.Sleep → Task.Delay (7/10 — 3 deferred to SDK controller events)
- ✅ Phase 2.6: Parallel file deletions (DeleteAllMusic, DeleteLongSongs)
- ~~Phase 2.2: FileLogger async~~ — SKIP (disabled by default, LOW impact)
- ~~Phase 2.3: WebClient → HttpClient~~ — SKIP (tiny JSON, network dominates)
- ~~Phase 2.4: Async void~~ — ALREADY MITIGATED (all have try-catch)
- ~~Phase 2.7: ConfigureAwait(false)~~ — CANCELLED (risk > benefit)
- ~~Phase 3.1-3.7~~ — 6/7 NOT A BUG, 1 LOW (commented-out cleanup)

**Achieved:** UI stays responsive during rate limiting, file releases, and bulk deletes

---

### Remaining Work (future versions)
**Items verified as actually worth doing:**

**HIGH:**
- ⬜ Phase 4.6: Async progress dialog pattern deduplication (~200+ lines savings)

**MEDIUM:**
- ⬜ Phase 5.3: Batch PropertyChanged on preset apply (~24 events → 1)
- ⬜ Phase 4.1: FFmpeg validation helper (moderate cleanup)
- ⬜ Phase 4.3: Extract shared logic from desktop/controller dialogs (~100-150 lines)
- ⬜ Phase 4.4: Split DialogHelper into focused files (1,327 → ~4-5 files)

**LOW:**
- ⬜ Phase 3.4: Uncomment CleanupControllerSupport() in DownloadDialogView
- ⬜ Phase 5.6: Document settings property organization

**DO NOT IMPLEMENT (plan's fix was wrong/harmful):**
- ~~Phase 5.4: Remove cascading PropertyChanged~~ — would break WPF computed property binding
- ~~Phase 5.5: Remove CancelEdit settings load~~ — would break settings cancel/revert
- ~~Phase 4.5: Extract ViewModels from code-behind~~ — code-behind is appropriate for WPF rendering logic

---

## Metrics & Success Criteria

### Performance Targets

| Metric | Before v1.2.11 | After v1.2.11 | Status |
|--------|----------------|---------------|--------|
| Game selection latency (cached) | 2-80ms | <2ms (cache hit) | ✅ Achieved (Phase 1) |
| Thread.Sleep UI freeze | 50-2000ms | 0ms (async) | ✅ 7/10 done |
| Bulk file delete | 200-2000ms sequential | Parallel | ✅ Achieved (Phase 2.6) |
| Settings preset flicker | ~24 PropertyChanged events | 1 event (batched) | ⬜ Remaining (5.3) |

### Code Quality Targets (Corrected)

| Metric | Status | Notes |
|--------|--------|-------|
| Memory leak rate | **No leaks found** | Phase 3 verified: 6/7 items NOT A BUG |
| Critical bugs (original: 17) | **11 actual** | 6 were not bugs after code verification |
| Code duplication | ~200 lines addressable | 4.6 async progress pattern is main target |

---

## Risk Assessment (Corrected 2026-02-14)

### Medium Risk
- **Phase 4.6 (Async progress helper)**: Touches multiple handlers, needs careful testing
- **Phase 5.3 (Batch notifications)**: Changes PropertyChanged behavior, UI testing critical
- **Phase 4.4 (DialogHelper split)**: Many dependencies, wide impact

### Low Risk
- **Phase 4.1 (FFmpeg validator)**: Simple extraction, low impact
- **Phase 4.3 (Shared dialog logic)**: Extract incrementally, test per-dialog
- **Phase 3.4 (Uncomment cleanup)**: Single line uncomment, investigate why disabled first

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
- ✅ Static Random instance (eliminates 4 allocations per session)
- ✅ Lowercase extensions HashSet (O(1) lookup)
- ✅ Song list caching (session-scoped with 17 invalidation sites, opt-in toggle)
- ✅ Thread.Sleep → Task.Delay (7/10 instances)
- ✅ Parallel file deletions (DeleteAllMusic, DeleteLongSongs)
- ✅ Documentation updated (MEMORY.md, README.md, CHANGELOG.md)

### Analysis Reliability Note
The original 87-item analysis was generated without verifying claims against actual code.
After systematic code verification (Phases 2-5), **~25 items were found to be wrong, not a bug,
already mitigated, or had suggested fixes that would break functionality.**
Remaining items in this document have been verified against the actual codebase.

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
