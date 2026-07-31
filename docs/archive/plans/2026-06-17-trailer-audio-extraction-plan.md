# Trailer Audio Extraction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For no-music games with a full EML video trailer, extract the trailer's audio with FFmpeg, cache it, and play it as UPS default music.

**Architecture:** A new `TrailerAudioService` owns trailer resolution, FFmpeg demux, and per-game caching. `MusicPlaybackService.PlayGameMusic`'s existing `DeferToTrailerAudio` case (currently a no-op) calls the service and adds the cached `.m4a` to `songs`. `IsDefaultMusicPath` is updated so the cached file is recognized as default music. The Experimental-tab setting is relabeled and gated behind FFmpeg availability; a Cleanup-tab button clears the cache.

**Tech Stack:** C# / .NET 4.6.2 / WPF (Playnite SDK). FFmpeg (user-installed, path in settings). No automated test framework — verification is the mandatory build/package gate plus a one-time manual acceptance scenario.

**Spec:** [docs/plans/2026-06-17-trailer-audio-extraction-design.md](2026-06-17-trailer-audio-extraction-design.md)

---

## Testing note (read first)

UPS has **no unit-test project**. The "write a failing test" TDD step is replaced by:
- **Build gate** after every task: `dotnet clean -c Release && dotnet build -c Release` must succeed.
- A **throwaway console probe** for pure logic (path building, FFmpeg arg strings) where it's cheap — written to `c:\tmp\`, run with `dotnet script` or compiled ad hoc, then deleted. Where a probe is impractical (WPF/Playnite-coupled code), the verification is a compile + a code-reading checklist stated in the step.
- One **manual acceptance scenario** at the very end (Task 8), run in Playnite by the maintainer.

Never claim a task "done" without the build succeeding in that task's verification step.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Services/ITrailerAudioService.cs` | **Create.** Interface: one method `GetOrExtractAudio(Game)` + `GetCachedPath(Game)` + `ClearCache()`. |
| `src/Services/TrailerAudioService.cs` | **Create.** Trailer resolution, FFmpeg demux, per-game caching, gated logging. |
| `src/Services/MusicPlaybackService.cs` | **Modify.** Accept `ITrailerAudioService`; wire the `DeferToTrailerAudio` play case and `IsDefaultMusicPath` case. |
| `src/UniPlaySong.cs` | **Modify.** Construct `TrailerAudioService` at the composition root; pass to all 3 `MusicPlaybackService` constructions; Cleanup-tab clear handler. |
| `src/UniPlaySongSettingsView.xaml` | **Modify.** Relabel option, gate `IsEnabled`, add "Clear trailer-audio cache" button. |
| `src/UniPlaySongSettingsView.xaml.cs` | **Modify.** Clear-cache click handler, open-time FFmpeg enablement evaluation. |
| `src/UniPlaySongSettingsViewModel.cs` | **Modify (if VM-bound).** Expose `IsTrailerAudioAvailable` for the `IsEnabled` binding. |

`GameMusicFileService.HasTrailerVideo()` is **not** modified.

---

## Task 1: Create the `ITrailerAudioService` interface

**Files:**
- Create: `src/Services/ITrailerAudioService.cs`

- [ ] **Step 1: Write the interface**

Create `src/Services/ITrailerAudioService.cs`:

```csharp
using Playnite.SDK.Models;

namespace UniPlaySong.Services
{
    // Extracts and caches the audio track of a game's EML video trailer so UPS can
    // play it as default music for games that have no UPS music of their own.
    public interface ITrailerAudioService
    {
        // Returns a playable path to the game's extracted trailer audio, extracting
        // and caching on first call. Returns null if there is no full trailer, FFmpeg
        // is unavailable, or extraction fails (caller stays silent).
        string GetOrExtractAudio(Game game);

        // Deterministic cache path for this game's extracted audio (no I/O, no extraction).
        // Used by IsDefaultMusicPath to recognize the cached file as default music.
        string GetCachedPath(Game game);

        // Deletes all cached trailer audio. Returns (filesDeleted, bytesFreed).
        (int filesDeleted, long bytesFreed) ClearCache();
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build -c Release`
Expected: Build succeeds (interface only, no consumers yet).

- [ ] **Step 3: Commit**

```bash
git add src/Services/ITrailerAudioService.cs
git commit -m "feat(trailer-audio): add ITrailerAudioService interface"
```

---

## Task 2: Implement `TrailerAudioService` — paths & cache, no FFmpeg yet

**Files:**
- Create: `src/Services/TrailerAudioService.cs`

This task builds everything *except* the FFmpeg call: constructor, cache dir, cached path, full-trailer resolution, `ClearCache`. `GetOrExtractAudio` returns the cached path if it already exists, else null (extraction lands in Task 3). This is verifiable by a console probe because it's pure path/file logic.

- [ ] **Step 1: Write the service skeleton**

Create `src/Services/TrailerAudioService.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Extracts the audio track from a game's EML VideoTrailer.mp4 and caches it per game
    // so UPS can play it as default music. Full trailers only (no micro-trailer fallback).
    // Every failure path returns null so the caller stays silent — never throws to the caller.
    public class TrailerAudioService : ITrailerAudioService
    {
        private const string CacheFolderName = "TrailerAudioCache";

        private readonly UniPlaySongSettings _settings;
        private readonly string _emlGamesPath;     // <Config>\ExtraMetadata\Games
        private readonly string _pluginDataPath;   // <Config>\ExtraMetadata\UniPlaySong
        private readonly FileLogger _fileLogger;

        // Once-per-session log guards so config errors don't spam the log on every game select.
        private bool _loggedNoFfmpeg;
        private bool _loggedNoEmlRoot;

        public TrailerAudioService(UniPlaySongSettings settings, string emlGamesPath, string pluginDataPath, FileLogger fileLogger = null)
        {
            _settings = settings;
            _emlGamesPath = emlGamesPath;
            _pluginDataPath = pluginDataPath;
            _fileLogger = fileLogger;
        }

        public string GetCachedPath(Game game)
        {
            if (game == null || string.IsNullOrEmpty(_pluginDataPath))
            {
                return null;
            }
            return Path.Combine(GetCacheDir(), game.Id.ToString() + ".m4a");
        }

        private string GetCacheDir()
        {
            var dir = Path.Combine(_pluginDataPath, CacheFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // <emlGamesPath>\{GameId}\VideoTrailer.mp4 if it exists, else null. Full trailer ONLY
        // (deliberately narrower than GameMusicFileService.HasTrailerVideo, which OR's micro).
        private string ResolveFullTrailer(Game game)
        {
            if (game == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(_emlGamesPath))
            {
                if (!_loggedNoEmlRoot)
                {
                    _fileLogger?.Debug("TrailerAudio: EML games root unresolved; trailer audio unavailable.");
                    _loggedNoEmlRoot = true;
                }
                return null;
            }

            var path = Path.Combine(_emlGamesPath, game.Id.ToString(), Constants.VideoTrailerFileName);
            return File.Exists(path) ? path : null;
        }

        // Implemented fully in Task 3.
        public string GetOrExtractAudio(Game game)
        {
            var cached = GetCachedPath(game);
            if (cached != null && File.Exists(cached) && new FileInfo(cached).Length > 0)
            {
                return cached;
            }
            return null;
        }

        public (int filesDeleted, long bytesFreed) ClearCache()
        {
            int files = 0;
            long bytes = 0;
            try
            {
                var dir = Path.Combine(_pluginDataPath ?? string.Empty, CacheFolderName);
                if (!Directory.Exists(dir))
                {
                    return (0, 0);
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    try
                    {
                        bytes += new FileInfo(f).Length;
                        File.Delete(f);
                        files++;
                    }
                    catch { /* skip locked file; report what we cleared */ }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"TrailerAudio: ClearCache failed: {ex.Message}");
            }
            return (files, bytes);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build -c Release`
Expected: Build succeeds.

- [ ] **Step 3: Probe the pure path logic**

Create `c:\tmp\probe_trailer_paths.csx` (run with `dotnet script` if available; otherwise read-verify the four assertions below against the code):

```csharp
using System.IO;
// Simulate GetCachedPath + ResolveFullTrailer logic against a temp tree.
var pluginData = Path.Combine(Path.GetTempPath(), "ups_probe_data");
var emlGames   = Path.Combine(Path.GetTempPath(), "ups_probe_eml");
var gameId     = Guid.NewGuid().ToString();
Directory.CreateDirectory(Path.Combine(emlGames, gameId));

// 1. Cached path is <pluginData>\TrailerAudioCache\{id}.m4a
var cached = Path.Combine(pluginData, "TrailerAudioCache", gameId + ".m4a");
Console.WriteLine("cached=" + cached);

// 2. Full trailer resolves only when VideoTrailer.mp4 exists
var trailer = Path.Combine(emlGames, gameId, "VideoTrailer.mp4");
Console.WriteLine("trailer-missing => " + (File.Exists(trailer) ? "FOUND" : "null (correct)"));
File.WriteAllText(trailer, "x");
Console.WriteLine("trailer-present => " + (File.Exists(trailer) ? "FOUND (correct)" : "null"));

// 3. Micro-only must NOT resolve
var micro = Path.Combine(emlGames, gameId, "VideoMicrotrailer.mp4");
File.Delete(trailer); File.WriteAllText(micro, "x");
Console.WriteLine("micro-only => " + (File.Exists(trailer) ? "FOUND (BUG)" : "null (correct)"));

Directory.Delete(pluginData, true); Directory.Delete(emlGames, true);
```

Run: `dotnet script c:\tmp\probe_trailer_paths.csx`
Expected output includes: `trailer-missing => null (correct)`, `trailer-present => FOUND (correct)`, `micro-only => null (correct)`.
(If `dotnet script` is unavailable, instead read `ResolveFullTrailer` and confirm it uses `Constants.VideoTrailerFileName` only and never references `VideoMicrotrailerFileName`.)

- [ ] **Step 4: Delete the probe and commit**

```bash
rm -f c:/tmp/probe_trailer_paths.csx
git add src/Services/TrailerAudioService.cs
git commit -m "feat(trailer-audio): TrailerAudioService paths, cache resolve, ClearCache"
```

---

## Task 3: Implement FFmpeg extraction (copy + transcode fallback)

**Files:**
- Modify: `src/Services/TrailerAudioService.cs`

Add the FFmpeg demux, modeled exactly on `AudioConversionService.ConvertFileInternal` ([src/Services/AudioConversionService.cs:198-254](../../src/Services/AudioConversionService.cs#L198)): `ProcessStartInfo` with both streams redirected, `CreateNoWindow`, `UseShellExecute=false`, UTF8 stderr; `WaitForExit(timeout)` with `Kill()` on timeout; exit-code + nonzero-output checks; temp-delete on failure. Write to a unique temp then `File.Move` into place (atomic, concurrency-safe).

- [ ] **Step 1: Add `using` directives and the extraction methods**

At the top of `src/Services/TrailerAudioService.cs`, ensure these usings are present (add any missing):

```csharp
using System.Diagnostics;
using System.Text;
using System.Threading;
```

Replace the Task-2 placeholder `GetOrExtractAudio` body with the full orchestration, and add the `Extract` helper. Full method block:

```csharp
        public string GetOrExtractAudio(Game game)
        {
            var cached = GetCachedPath(game);
            if (cached == null)
            {
                return null; // null game / no plugin data path
            }
            if (File.Exists(cached) && new FileInfo(cached).Length > 0)
            {
                return cached; // hot path — no logging
            }
            // Corrupt/empty cache file: treat as miss and re-extract.
            if (File.Exists(cached))
            {
                _fileLogger?.Debug($"TrailerAudio: cached file for {game.Name} was empty; re-extracting.");
                try { File.Delete(cached); } catch { }
            }

            var trailer = ResolveFullTrailer(game);
            if (trailer == null)
            {
                return null; // no full trailer — expected for most games, not logged
            }

            var ffmpeg = _settings?.FFmpegPath;
            if (!FFmpegHelper.IsAvailable(ffmpeg))
            {
                if (!_loggedNoFfmpeg)
                {
                    _fileLogger?.Info("TrailerAudio: FFmpeg not available — set its path in the Downloads tab. Trailer audio disabled.");
                    _loggedNoFfmpeg = true;
                }
                return null;
            }

            var sw = Stopwatch.StartNew();
            // Fast path: copy the AAC stream into .m4a (lossless, near-instant).
            if (Extract(ffmpeg, trailer, cached, transcode: false))
            {
                _fileLogger?.Info($"TrailerAudio: extracted audio for {game.Name} in {sw.ElapsedMilliseconds} ms.");
                return cached;
            }
            // Fallback: transcode to .mp3 (rare non-AAC trailer audio).
            var mp3Cached = Path.ChangeExtension(cached, ".mp3");
            if (Extract(ffmpeg, trailer, mp3Cached, transcode: true))
            {
                _fileLogger?.Info($"TrailerAudio: extracted audio (transcoded) for {game.Name} in {sw.ElapsedMilliseconds} ms.");
                return mp3Cached;
            }

            _fileLogger?.Error($"TrailerAudio: extraction failed for {game.Name}; staying silent.");
            return null;
        }

        // Demux/transcode the trailer audio into outPath via a unique temp + atomic move.
        // transcode=false => -c:a copy into .m4a; transcode=true => re-encode into .mp3.
        // Returns true only if a non-empty output file landed at outPath.
        private bool Extract(string ffmpeg, string trailerMp4, string outPath, bool transcode)
        {
            // Unique temp so concurrent same-game extractions never read each other's partial file.
            var temp = outPath + "." + Guid.NewGuid().ToString("N") + ".tmp" + Path.GetExtension(outPath);
            var args = transcode
                ? $"-y -i \"{trailerMp4}\" -vn \"{temp}\""
                : $"-y -i \"{trailerMp4}\" -vn -c:a copy \"{temp}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    string stderr = null;
                    var readTask = System.Threading.Tasks.Task.Run(async () =>
                    {
                        var errTask = process.StandardError.ReadToEndAsync();
                        var _ = process.StandardOutput.ReadToEndAsync();
                        if (!process.WaitForExit(60000)) // 1 minute — trailers are short
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg trailer extraction timed out.");
                        }
                        stderr = await errTask.ConfigureAwait(false);
                    });
                    readTask.GetAwaiter().GetResult();

                    if (process.ExitCode != 0)
                    {
                        _fileLogger?.Error($"TrailerAudio: FFmpeg exit {process.ExitCode}: {stderr}");
                        TryDelete(temp);
                        return false;
                    }
                }

                if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
                {
                    TryDelete(temp);
                    return false;
                }

                // Atomic publish. Last writer wins on concurrent same-game extraction.
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                File.Move(temp, outPath);
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"TrailerAudio: extraction error: {ex.Message}");
                TryDelete(temp);
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build -c Release`
Expected: Build succeeds.

- [ ] **Step 3: Probe FFmpeg arg construction**

Read-verify (no probe binary needed — this is a string assertion): confirm the two `args` strings exactly match:
- copy: `-y -i "<trailer>" -vn -c:a copy "<temp>"`
- transcode: `-y -i "<trailer>" -vn "<temp>"`

Both use `-vn` (drop video). `-c:a copy` (copy audio codec) is fast path; transcode omits `-c:a copy` so FFmpeg picks the `.mp3` encoder from the `.mp3` extension. These mirror `AudioConversionService`'s quoting style (`\"{path}\"`).

- [ ] **Step 4: Commit**

```bash
git add src/Services/TrailerAudioService.cs
git commit -m "feat(trailer-audio): FFmpeg demux with copy + transcode fallback"
```

---

## Task 4: Wire `TrailerAudioService` into the composition root

**Files:**
- Modify: `src/UniPlaySong.cs:2448-2467` (and the other two `MusicPlaybackService` constructions at `:2736` and `:2821`)

The plugin data path is `basePath` (`<Config>\ExtraMetadata\UniPlaySong`, already computed at line 2449). `emlGamesPath` is at line 2457. Both are in scope at the first construction site.

- [ ] **Step 1: Add a field for the service**

Find the field declarations near the other service fields (`_fileService`, `_playbackService`). Add:

```csharp
        private Services.ITrailerAudioService _trailerAudioService;
```

- [ ] **Step 2: Construct the service at the composition root**

In `src/UniPlaySong.cs`, immediately after the `_fileService = new GameMusicFileService(...)` line (currently line 2462), add:

```csharp
            // Trailer-audio extraction service: demuxes EML VideoTrailer.mp4 audio for
            // no-music games when DefaultMusicSource.DeferToTrailerAudio is selected.
            // basePath is <Config>\ExtraMetadata\UniPlaySong — the cache lives under it.
            _trailerAudioService = new Services.TrailerAudioService(_settings, emlGamesPath, basePath, _fileLogger);
```

- [ ] **Step 3: Pass the service to all three MusicPlaybackService constructions**

Change the constructor call at line 2467 from:

```csharp
            _playbackService = new MusicPlaybackService(_currentMusicPlayer, _fileService, _fileLogger, _errorHandler);
```

to:

```csharp
            _playbackService = new MusicPlaybackService(_currentMusicPlayer, _fileService, _fileLogger, _errorHandler, _trailerAudioService);
```

Apply the **identical** change at the other two sites (originally lines 2736 and 2821 — search for `new MusicPlaybackService(` to find all three; there are exactly three).

> Note: `_trailerAudioService` is constructed once in the first init path. If sites 2736/2821 run in a code path where `_trailerAudioService` could still be null, pass it anyway — `MusicPlaybackService` (Task 5) treats a null service as "feature unavailable" and stays silent. Do **not** reconstruct it at those sites.

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build -c Release`
Expected: FAIL — `MusicPlaybackService` does not yet accept a 5th argument. This confirms all three call sites were updated (the error names the constructor). Proceed to Task 5 to add the parameter; do not revert.

- [ ] **Step 5: Commit**

```bash
git add src/UniPlaySong.cs
git commit -m "feat(trailer-audio): construct TrailerAudioService at composition root"
```

---

## Task 5: Wire the play path and `IsDefaultMusicPath` in `MusicPlaybackService`

**Files:**
- Modify: `src/Services/MusicPlaybackService.cs` (constructor ~line 24/field, play case ~854, `IsDefaultMusicPath` ~484)

- [ ] **Step 1: Add the field and constructor parameter**

Add a field next to `_fileService` (currently line 24):

```csharp
        private readonly ITrailerAudioService _trailerAudioService;
```

Find the constructor (search for `public MusicPlaybackService(`). Add `ITrailerAudioService trailerAudioService = null` as the last parameter and assign it:

```csharp
            _trailerAudioService = trailerAudioService;
```

(Defaulting to `null` keeps the constructor backward-compatible and means a missing service = feature silently unavailable.)

- [ ] **Step 2: Verify the constructor change compiles**

Run: `dotnet build -c Release`
Expected: Build succeeds (Task 4's call sites now match the 5-arg constructor).

- [ ] **Step 3: Replace the `DeferToTrailerAudio` play case**

In `PlayGameMusic` (the `switch` inside `if (songs.Count == 0 && settings?.EnableDefaultMusic == true)`), replace the entire current `case DefaultMusicSource.DeferToTrailerAudio:` block (the v1.5.4 no-op + `HasTrailerVideo` diagnostics, currently lines ~854-868) with:

```csharp
                        case DefaultMusicSource.DeferToTrailerAudio:
                            // Extract and play the game's EML trailer audio as default music.
                            // Service returns null (→ silence) if no full trailer, FFmpeg
                            // unavailable, or extraction fails. Only reached for no-music games.
                            var trailerAudioPath = _trailerAudioService?.GetOrExtractAudio(game);
                            if (!string.IsNullOrWhiteSpace(trailerAudioPath) && File.Exists(trailerAudioPath))
                            {
                                _lastDefaultMusicPath = trailerAudioPath;
                                songs.Add(trailerAudioPath);
                            }
                            break;
```

(Setting `_lastDefaultMusicPath` lets `IsDefaultMusicPath`'s pool-style match also recognize the file, matching how the other pool sources track their last path.)

- [ ] **Step 4: Update `IsDefaultMusicPath` for `DeferToTrailerAudio`**

In `IsDefaultMusicPath` (currently line 460), replace the existing `DeferToTrailerAudio` case (currently lines ~484-486, which returns `false` with the now-obsolete comment "No UPS file is ever loaded"):

```csharp
                case DefaultMusicSource.DeferToTrailerAudio:
                    // The extracted trailer-audio file IS loaded as default music. Match it so
                    // position-preservation, looping, and "continue same song" treat it correctly.
                    var trailerCached = _trailerAudioService?.GetCachedPath(_currentGame);
                    return (!string.IsNullOrWhiteSpace(trailerCached) &&
                            string.Equals(path, trailerCached, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(_lastDefaultMusicPath) &&
                            string.Equals(path, _lastDefaultMusicPath, StringComparison.OrdinalIgnoreCase));
```

> The transcode-fallback path is `.mp3`, not the `.m4a` that `GetCachedPath` returns — so the `_lastDefaultMusicPath` arm of the OR is what catches the fallback case. Both arms are required.

- [ ] **Step 5: Confirm `_currentGame` exists**

`IsDefaultMusicPath` references `_currentGame`. Search `src/Services/MusicPlaybackService.cs` for `_currentGame`. If a field named `_currentGame` (type `Game`) exists and is set in `PlayGameMusic`, use it. If the field has a different name (e.g. `_lastGame`), use that exact name instead in Step 4. Do not introduce a new field — reuse the existing current-game reference.

- [ ] **Step 6: Verify it compiles**

Run: `dotnet build -c Release`
Expected: Build succeeds.

- [ ] **Step 7: Package to confirm the full artifact builds**

Run: `powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
Expected: Packaging succeeds, `.pext` produced.

- [ ] **Step 8: Commit**

```bash
git add src/Services/MusicPlaybackService.cs
git commit -m "feat(trailer-audio): play extracted trailer audio + recognize as default music"
```

---

## Task 6: Relabel the setting and gate it behind FFmpeg availability

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml` (Experimental tab option + IsEnabled binding)
- Modify: `src/UniPlaySongSettingsViewModel.cs` (expose availability flag)
- Modify: `src/UniPlaySongSettingsView.xaml.cs` (evaluate availability on open)

- [ ] **Step 1: Find the existing option markup**

Search `src/UniPlaySongSettingsView.xaml` for the `DeferToTrailerAudio` option (it will be a `RadioButton` or `ComboBoxItem` with the v1.5.4 label "Stay silent for games with no music", on the Experimental tab). Note its exact control type and surrounding layout — match it.

- [ ] **Step 2: Relabel**

Change the option's displayed text to:

```
Stream audio from the game's EML trailer (no-music games only)
```

And the associated help/description text to:

```
For games with no UniPlaySong music, extracts the audio from the game's EML video trailer and plays it as the default music. The first play for each game may take a moment while the audio is extracted (cached afterward). Requires FFmpeg (set in the Downloads tab) and a full video trailer from the ExtraMetadataLoader extension. Games with only a micro-trailer, or no trailer, stay silent.
```

- [ ] **Step 3: Add the availability flag to the ViewModel**

In `src/UniPlaySongSettingsViewModel.cs`, add a display-only property (evaluated at open via the existing `BeginEdit()` init-on-open pattern — search the file for `BeginEdit` to find where init-on-open values are set):

```csharp
        private bool _isTrailerAudioAvailable;
        public bool IsTrailerAudioAvailable
        {
            get => _isTrailerAudioAvailable;
            set { _isTrailerAudioAvailable = value; OnPropertyChanged(); }
        }
```

In the `BeginEdit()` body (or wherever open-time display values are computed), set it:

```csharp
            IsTrailerAudioAvailable = Common.FFmpegHelper.IsAvailable(Settings?.FFmpegPath);
```

(Use the same accessor for the settings instance that neighboring init-on-open lines use — match the existing `Settings.`/`settings.` reference style in that method.)

- [ ] **Step 4: Bind `IsEnabled` and add the inline note**

On the relabeled option control, add:

```xml
IsEnabled="{Binding IsTrailerAudioAvailable}"
```

Immediately below the option, add a note shown only when FFmpeg is missing (match the existing pattern used by other conditional notes in this view; this is the canonical form):

```xml
<TextBlock Text="Requires FFmpeg — set its path in the Downloads tab"
           Foreground="{DynamicResource GlyphBrush}"
           FontSize="11" Margin="24,0,0,4"
           Visibility="{Binding IsTrailerAudioAvailable, Converter={StaticResource InverseBoolToVisibilityConverter}}"/>
```

> If `InverseBoolToVisibilityConverter` is not already a registered resource in this view, search the XAML for an existing inverse-bool-to-visibility converter and use that key instead. If none exists, bind `Visibility` to a new VM property `FFmpegMissingNoteVisibility` returning `Visibility.Visible`/`Collapsed` computed alongside `IsTrailerAudioAvailable` in Step 3 — do not add a converter just for this.

- [ ] **Step 5: Verify it compiles and the XAML loads**

Run: `dotnet build -c Release`
Expected: Build succeeds (XAML parse errors surface here).

- [ ] **Step 6: Commit**

```bash
git add src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsViewModel.cs src/UniPlaySongSettingsView.xaml.cs
git commit -m "feat(trailer-audio): relabel setting, gate behind FFmpeg availability"
```

---

## Task 7: Add the "Clear trailer-audio cache" button to the Cleanup tab

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml` (Cleanup tab button)
- Modify: `src/UniPlaySongSettingsView.xaml.cs` (click handler)

The handler needs access to the cache, which lives under the plugin data path. The cleanest path is to call `ITrailerAudioService.ClearCache()`. The settings view does not hold the service instance, so route through the plugin: expose a static/instance accessor, OR compute the path directly in the handler. To stay simple and avoid threading the service into the view, the handler computes the cache directory the same way the service does and deletes its contents.

- [ ] **Step 1: Find an existing Cleanup-tab clear button**

Search `src/UniPlaySongSettingsView.xaml` for the Cleanup tab and an existing clear/reset button (e.g. a "Clear cache" or "Clear ... " `Button` with a `Click` handler). Note its style and the notification pattern its handler uses (search the matching handler in `src/UniPlaySongSettingsView.xaml.cs`).

- [ ] **Step 2: Add the button**

In the Cleanup tab, next to the other clear buttons, add (match the neighboring button's `Style`/`Margin`):

```xml
<Button Content="Clear trailer-audio cache"
        Click="ClearTrailerAudioCache_Click"
        Margin="0,4,0,0"/>
```

- [ ] **Step 3: Add the click handler**

In `src/UniPlaySongSettingsView.xaml.cs`, add the handler. It computes the cache dir from the SDK `ConfigurationPath` (the same anchor the service uses) so it needs no service reference:

```csharp
        private void ClearTrailerAudioCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var api = Playnite.SDK.API.Instance;
                var cacheDir = System.IO.Path.Combine(
                    api.Paths.ConfigurationPath,
                    Common.Constants.ExtraMetadataFolderName,
                    Common.Constants.ExtensionFolderName,
                    "TrailerAudioCache");

                int files = 0;
                long bytes = 0;
                if (System.IO.Directory.Exists(cacheDir))
                {
                    foreach (var f in System.IO.Directory.GetFiles(cacheDir))
                    {
                        try
                        {
                            bytes += new System.IO.FileInfo(f).Length;
                            System.IO.File.Delete(f);
                            files++;
                        }
                        catch { /* skip locked file */ }
                    }
                }

                var mb = bytes / 1024.0 / 1024.0;
                api.Dialogs.ShowMessage($"Cleared {files} cached trailer-audio file(s), freed {mb:0.0} MB.");
            }
            catch (Exception ex)
            {
                Playnite.SDK.API.Instance.Dialogs.ShowErrorMessage(
                    $"Failed to clear trailer-audio cache: {ex.Message}", "UniPlaySong");
            }
        }
```

> The `"TrailerAudioCache"` literal must match `TrailerAudioService.CacheFolderName`. If you prefer a single source of truth, promote `CacheFolderName` to a `public const` on `Constants` (`Constants.TrailerAudioCacheFolderName`) and use it in both `TrailerAudioService` and this handler. Recommended — do that promotion now to avoid the duplicated literal.

- [ ] **Step 4: If promoting the constant — do it**

Add to `src/Common/Constants.cs` (in the folder-names region, near `GamesFolderName`):

```csharp
        public const string TrailerAudioCacheFolderName = "TrailerAudioCache";
```

Replace the literal `"TrailerAudioCache"` in `TrailerAudioService.cs` (`CacheFolderName` const) and in the handler above with `Constants.TrailerAudioCacheFolderName`. In `TrailerAudioService.cs`, delete the private `CacheFolderName` const and use `Constants.TrailerAudioCacheFolderName` everywhere it appeared (`GetCacheDir`, `ClearCache`).

- [ ] **Step 5: Verify it compiles and packages**

Run: `dotnet clean -c Release && dotnet build -c Release && powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1`
Expected: All three succeed; `.pext` produced.

- [ ] **Step 6: Commit**

```bash
git add src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml.cs src/Common/Constants.cs src/Services/TrailerAudioService.cs
git commit -m "feat(trailer-audio): add Clear trailer-audio cache button to Cleanup tab"
```

---

## Task 8: Final verification — build gate + manual acceptance

**Files:** none (verification only)

- [ ] **Step 1: Full clean build + package**

Run:
```bash
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```
Expected: All succeed with no errors; `.pext` produced under the package output.

- [ ] **Step 2: Code self-verification checklist (read, confirm)**

- [ ] `.m4a` and `.aac` are present in `Constants.SupportedAudioExtensionsLowercase` (grep `Constants.cs`). If absent, the player will reject the extracted file — STOP and add them.
- [ ] `DeferToTrailerAudio` is handled in BOTH the play `switch` (Task 5 Step 3) AND `IsDefaultMusicPath` (Task 5 Step 4).
- [ ] The play case is inside the `if (songs.Count == 0 && settings?.EnableDefaultMusic == true)` block (so games with music never reach it).
- [ ] All three `new MusicPlaybackService(...)` sites pass `_trailerAudioService`.
- [ ] `HasTrailerVideo()` in `GameMusicFileService.cs` is unchanged.

- [ ] **Step 3: Manual acceptance scenario (maintainer runs in Playnite)**

Install the `.pext`. Then:

| Setup | Action | Expected |
|---|---|---|
| Settings → Experimental: default music source = "Stream audio from the game's EML trailer". FFmpeg path set in Downloads tab. Pick a game with **no UPS music** but **with a full EML trailer** (`VideoTrailer.mp4` present under `<Config>\ExtraMetadata\Games\{GameId}`). | Select the game. | First select: brief pause, then the trailer's audio plays and loops as default music. A `{GameId}.m4a` file appears in `<Config>\ExtraMetadata\UniPlaySong\TrailerAudioCache\`. Re-select the game: audio plays instantly (cache hit). |

- [ ] **Step 4: Report outcome**

State plainly whether the build/package succeeded and whether the manual scenario played audio. If the manual scenario hasn't been run by the maintainer yet, say so — do not claim the feature works end-to-end without that confirmation.

---

## Self-Review (completed during planning)

**Spec coverage:**
- Architecture/data flow → Tasks 2,3,5. ✔
- `TrailerAudioService` (service, deps, members, extract cmd, transcode fallback) → Tasks 1,2,3. ✔
- `MusicPlaybackService` play case + `IsDefaultMusicPath` + injection → Tasks 4,5. ✔
- Settings relabel + FFmpeg gating (open-time eval) → Task 6. ✔
- Cleanup-tab clear button + factory-reset-covered cache location → Task 7. ✔
- Error/edge matrix (null game, no trailer, FFmpeg missing, hang, corrupt cache, concurrency via temp+move) → Tasks 2,3. ✔
- Logging policy (hot path silent, once-per-session guards, success/failure lines) → Tasks 2,3. ✔
- Testing (build gate + single manual scenario) → Task 8. ✔
- `HasTrailerVideo()` untouched → asserted in Tasks 2 & 8. ✔

**Placeholder scan:** No TBD/TODO. Conditional branches (converter-exists, `_currentGame` field name, constant promotion) give an explicit default action, not a deferral. ✔

**Type consistency:** `GetOrExtractAudio(Game)→string`, `GetCachedPath(Game)→string`, `ClearCache()→(int,long)` consistent across interface (Task 1), impl (Tasks 2,3), and consumers (Tasks 5,7). `ITrailerAudioService` parameter name `trailerAudioService` and field `_trailerAudioService` consistent across Tasks 4,5. `CacheFolderName`/`Constants.TrailerAudioCacheFolderName` reconciled in Task 7 Step 4. ✔
