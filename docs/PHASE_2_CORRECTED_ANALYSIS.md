# Phase 2: Corrected Priority Analysis

**Date:** 2026-02-14
**Last Updated:** 2026-02-14
**Status:** In Progress (v1.2.11)

---

## Summary of Corrections

The original Phase 2 analysis overestimated severity for several items. After examining actual code and usage patterns, multiple items have been corrected.

### Corrected Priority Counts

| Item | Original Priority | Corrected Priority | Reason |
|------|-------------------|-------------------|--------|
| 2.1 Thread.Sleep | CRITICAL | CRITICAL | Correct — 7/10 done, 3 deferred |
| 2.2 FileLogger | CRITICAL | **LOW (SKIP)** | Debug logging disabled by default, ~3-4 writes per game selection |
| 2.3 WebClient | CRITICAL | **SKIP** | Tiny JSON file — network latency dominates, HttpClient won't help |
| 2.4 Async void | CRITICAL | **ALREADY MITIGATED** | All handlers already have try-catch blocks |
| 2.5 UI thread file I/O | HIGH | HIGH | Correct |
| 2.6 Sequential deletions | HIGH | HIGH | Correct |
| 2.7 ConfigureAwait(false) | MEDIUM | MEDIUM | Correct |

---

## Completed Items

### 2.1 Thread.Sleep() — 7/10 DONE
**Real Impact:** UI freezes for 50ms-2000ms per call

**Completed (7):**
- Rate limiting delays (3) — BatchDownloadService, GameMenuHandler, UniPlaySong
- File release delays (4) — NormalizationDialogHandler, TrimDialogHandler, ControllerDeleteSongsDialog

**Deferred (3):** Controller polling delays — will be refactored when SDK controller events are available

---

## Items Reclassified as SKIP / Already Done

### 2.2 FileLogger — SKIP (LOW)
- Debug logging is **disabled by default** (`enableDebugLogging = false`)
- When disabled, only 4-6 Info/Warn calls per game selection actually write to disk
- `_fileLogger?.Debug()` calls short-circuit when debug logging is off
- Modern SSDs: ~0.1-2ms per write, not noticeable for infrequent calls
- **NOT a real bottleneck** for 99% of users

### 2.3 WebClient — SKIP
- Downloads a single small JSON file (`search_hints.json`) from GitHub
- The "500ms-5s" delay is network latency, not payload processing
- `HttpClient.GetStringAsync()` would not reduce perceived wait time
- Called only on manual action or once at startup (auto-check)
- **Not worth the code churn** for zero user-visible improvement

### 2.4 Async Void Handlers — ALREADY MITIGATED
- All `async void` event handlers already wrap their bodies in try-catch blocks
- `async void` is required by WPF event handler signatures — this is correct
- Verified across: AmplifyDialog, ControllerAmplifyDialog, ControllerDeleteSongsDialog, ControllerWaveformTrimDialog, WaveformTrimDialog
- **No action needed**

---

## Remaining Work

### 2.5 UI Thread File I/O — HIGH (12 instances)
**Impact:** UI stuttering during file operations

**Locations:**
- Dialog file pickers (6 instances) — synchronous file browsing
- Settings save operations (4 instances) — synchronous JSON write
- Log file operations (2 instances) — synchronous log writes

**Fix Pattern:**
```csharp
// Current:
var files = Directory.GetFiles(directory);

// Better:
var files = await Task.Run(() => Directory.GetFiles(directory));
```

---

### 2.6 Sequential File Deletions — HIGH
**File:** `Services/GameMusicFileService.cs`
**Impact:** Blocking delete of 40+ files takes 200-2000ms on slow drives

**Fix Pattern:**
```csharp
// Current: sequential blocking deletes
foreach (var file in files) { File.Delete(file); }

// Better: parallel non-blocking
await Task.Run(() => Parallel.ForEach(files, file => { File.Delete(file); }));
```

---

### 2.7 ConfigureAwait(false) — CANCELLED
**Priority:** ~~MEDIUM~~ **CANCELLED**

**Analysis (2026-02-14):**
- 173 total `await` calls; 14 already use ConfigureAwait(false) in the correct places (FFmpeg stream reads)
- ~120 of remaining 159 are in Views/Handlers — adding ConfigureAwait(false) would cause WPF `InvalidOperationException` on UI element access
- Only ~15-20 safe candidates exist, saving ~0.1-0.5ms each (total: 5-25ms in stress scenarios)
- **Risk outweighs benefit.** WPF threading bugs from incorrect ConfigureAwait(false) are hard to debug and can cause deadlocks
- **Do not implement.**

---

## Recommended Implementation Order

1. **2.6 — Parallel file deletions** (HIGH) — Parallelize bulk delete across DeleteAllMusic, DeleteLongSongs, DeleteAllMusicForGames

---

## Testing Strategy

### 2.6 Parallel File Deletion
- Delete 40+ files — verify speed improvement
- Measure before/after timing
- Verify proper error handling for locked files
- Test bulk delete across multiple games (nested loop scenario)
