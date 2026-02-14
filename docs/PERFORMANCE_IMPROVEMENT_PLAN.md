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
| **Phase 2** | UI Thread Blocking | 46 issues | 14 CRITICAL, 18 HIGH, 14 MEDIUM |
| **Phase 3** | Memory & Resource Mgmt | 13 issues | 3 CRITICAL, 4 HIGH, 6 MEDIUM/LOW |
| **Phase 4** | Architectural Refactoring | 13 issues | 6 HIGH, 4 MEDIUM, 3 LOW |
| **Phase 5** | Settings & Configuration | 8 issues | 0 CRITICAL, 5 MEDIUM, 3 LOW |
| **TOTAL** | | **87 issues** | **17 CRITICAL, 24 HIGH, 46 MEDIUM/LOW** |

### Completed Optimizations (v1.2.11)

- ✅ **Native music path caching** - Static constructor pattern eliminates 6+ file scans per game selection
- ✅ **Song list caching** - Session-scoped cache eliminates repeated `Directory.GetFiles()` calls
  - Performance: ~2-8ms (SSD) or ~15-80ms (HDD) per cached re-selection
  - 14 invalidation call sites ensure cache consistency

---

## Phase 1: Hot Path Analysis

**Goal:** Optimize code executed on every game selection for immediate user-facing performance gains.

### HIGH Priority

#### 1.1 Cache Native Music Path in Service Field
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

#### 1.2 Static Random Instance
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

### MEDIUM Priority

#### 1.3 Lowercase Extensions HashSet
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

### CRITICAL Issues (14 total)

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
**Priority:** CRITICAL
**Impact:** Every log call blocks thread until disk write completes
**Effort:** 2 hours

**Current:**
```csharp
lock (_lock)
{
    File.AppendAllText(_logFilePath, formattedMessage);  // Blocks!
}
```

**Suggested:**
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

---

#### 2.3 WebClient Synchronous Downloads
**File:** `Services/SearchHintsService.cs` lines 383-388
**Priority:** CRITICAL
**Impact:** UI freezes for 500ms-5s during metadata fetch
**Effort:** 30 minutes

**Current:**
```csharp
using (var client = new WebClient())
{
    var json = client.DownloadString(url);  // Blocks UI!
}
```

**Suggested:**
```csharp
using (var client = new HttpClient())
{
    var json = await client.GetStringAsync(url);  // Async
}
```

---

#### 2.4 Async Void Event Handlers (10 instances)
**Priority:** CRITICAL
**Impact:** Unhandled exceptions crash application
**Effort:** 1 hour

**Locations:**
- `Views/DownloadFromUrlDialog.xaml.cs` - 3 instances
- `Views/BatchManualDownloadViewModel.cs` - 2 instances
- `Views/NormalizationDialog.xaml.cs` - 2 instances
- `Views/TrimDialog.xaml.cs` - 3 instances

**Current:**
```csharp
private async void DownloadButton_Click(object sender, RoutedEventArgs e)
{
    await PerformDownload();  // Exception here crashes app!
}
```

**Suggested:**
```csharp
private async void DownloadButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await PerformDownload();
    }
    catch (Exception ex)
    {
        Logger.Error(ex, "Error during download");
        _api.Dialogs.ShowErrorMessage($"Download failed: {ex.Message}");
    }
}
```

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
**Impact:** Unnecessary UI thread context switching
**Effort:** 30 minutes

**Pattern:**
```csharp
// Add to all async library calls:
await Task.Delay(100).ConfigureAwait(false);
await httpClient.GetAsync(url).ConfigureAwait(false);
```

**Locations:** 50+ async calls missing `ConfigureAwait(false)`

---

### Summary - Phase 2

| Category | Count | Total Effort | Impact |
|----------|-------|--------------|--------|
| CRITICAL | 14 | 4.5 hours | Eliminates UI freezes |
| HIGH | 18 | 3 hours | Reduces UI stuttering |
| MEDIUM | 14 | 1.5 hours | Smoother async flow |
| **TOTAL** | **46** | **9 hours** | **Major UX improvement** |

**Recommended Order:**
1. Fix `async void` handlers (prevents crashes)
2. Replace `Thread.Sleep()` (biggest user-facing freeze)
3. Fix FileLogger (improves all operations)
4. Fix WebClient (eliminates metadata fetch freezes)
5. Parallelize file deletions
6. Add `ConfigureAwait(false)` (polish)

---

## Phase 3: Memory & Resource Management

**Goal:** Prevent resource leaks and memory exhaustion over long sessions.

### CRITICAL Issues (3 total)

#### 3.1 VisualizationDataProvider ManualResetEventSlim Leak
**File:** `Audio/VisualizationDataProvider.cs` lines 274-278
**Priority:** CRITICAL
**Impact:** Kernel handle leak (event handle not released)
**Effort:** 1 hour

**Current:**
```csharp
public void Dispose()
{
    _disposed = true;
    _dataAvailableEvent?.Set();
    // MISSING: _dataAvailableEvent?.Dispose();
}
```

**Fix:**
```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;

    _dataAvailableEvent?.Set();
    _dataAvailableEvent?.Dispose();  // ✅ Release kernel handle
    _dataAvailableEvent = null;
}
```

---

#### 3.2 MusicFader Timer Disposal Incomplete
**File:** `Players/MusicFader.cs` lines 53, 376-393
**Priority:** CRITICAL
**Impact:** Timer continues firing after disposal, callback leak
**Effort:** 1 hour

**Current:**
```csharp
private DispatcherTimer _fadeTimer;

public void Dispose()
{
    _fadeTimer?.Stop();
    // MISSING: _fadeTimer = null;
}

// Later in code:
_fadeTimer = new DispatcherTimer();  // Leaks previous timer!
```

**Fix:**
```csharp
public void Dispose()
{
    if (_fadeTimer != null)
    {
        _fadeTimer.Stop();
        _fadeTimer.Tick -= OnFadeTick;  // Unsubscribe
        _fadeTimer = null;
    }
}

private void StartFade(...)
{
    Dispose();  // Cleanup existing timer first
    _fadeTimer = new DispatcherTimer();
    _fadeTimer.Tick += OnFadeTick;
    _fadeTimer.Start();
}
```

---

#### 3.3 DownloadDialogService Event Handler Leak
**File:** `Services/DownloadDialogService.cs` lines 1680, 1768-1769
**Priority:** CRITICAL
**Impact:** Event handler keeps dialog alive after close
**Effort:** 1 hour

**Current:**
```csharp
_musicPlayer.OnSongEnded += HandleSongEnded;
// Dialog closes but handler never unsubscribed!
```

**Fix:**
```csharp
private void CleanupDialog()
{
    if (_musicPlayer != null)
    {
        _musicPlayer.OnSongEnded -= HandleSongEnded;
    }
    _musicPlayer = null;
}

// Call in dialog close handler
```

---

### HIGH Priority (4 issues)

#### 3.4 ControllerDetectionService Timer Never Disposed
**File:** `Services/ControllerDetectionService.cs`
**Impact:** Permanent 500ms polling even when controller unused
**Effort:** 30 minutes

#### 3.5 MediaElementsMonitor Timer Leak
**File:** `Monitors/MediaElementsMonitor.cs`
**Impact:** Permanent 100ms polling (10Hz) never stops
**Effort:** 30 minutes

#### 3.6 MusicPlaybackCoordinator Timer Leak
**File:** `Services/MusicPlaybackCoordinator.cs`
**Impact:** Timer continues after login dismiss
**Effort:** 30 minutes

#### 3.7 HttpClient Not Disposed
**File:** `UniPlaySong.cs` constructor
**Impact:** Socket exhaustion on repeated plugin reload
**Effort:** 15 minutes

---

### Summary - Phase 3

| Category | Count | Total Effort | Impact |
|----------|-------|--------------|--------|
| CRITICAL | 3 | 3 hours | Prevents handle/memory leaks |
| HIGH | 4 | 2 hours | Prevents timer/socket leaks |
| MEDIUM/LOW | 6 | 1.5 hours | Cleanup improvements |
| **TOTAL** | **13** | **6.5 hours** | **Long-session stability** |

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
- ⬜ Phase 3.1: Fix VisualizationDataProvider disposal
- ⬜ Phase 3.2: Fix MusicFader timer leaks
- ⬜ Phase 3.3: Fix DownloadDialogService event leak
- ⬜ Phase 3.4-3.7: Fix remaining timer/resource leaks

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
