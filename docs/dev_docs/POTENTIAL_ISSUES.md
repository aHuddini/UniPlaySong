# Potential Issues

Known edge cases and deferred fixes that may need attention in future versions.

## GME Output Gain Boost

**Status:** Active (monitoring)
**Discovered:** v1.4.x (GME integration)
**Restore point:** Commit `3a821cd` (before gain change)

### Problem

Retro chip-tune audio from GME (Genesis FM synth, NES pulse waves, etc.) has significantly lower perceived loudness than modern mastered MP3/OGG files. Raw chip output typically peaks at -6dB to -12dB compared to full scale.

### Current fix

A static 1.5x gain multiplier is applied during int16→float32 conversion in `GmeReader.cs`:
```csharp
private const float OutputGain = 1.5f;
buffer[offset + i] = _shortBuffer[i] / 32768f * OutputGain;
```

### Risks

- Some VGM files with hotter mixes could clip above 1.0f. NAudio's mixer will clamp, but it may sound distorted.
- The gain is applied before the Effects Chain, so Live Effects (reverb, limiter) process the boosted signal.
- Different retro formats (SPC vs VGM vs NSF) have different typical loudness levels — a single gain value may not suit all.

### Rollback

Revert `OutputGain` to `1.0f`, or remove the multiplier entirely. The restore point commit has no gain boost.

### Future alternatives

- Per-format gain constants (VGM=1.5, SPC=1.2, NSF=1.8, etc.)
- Auto-normalize: scan first N seconds for peak, compute gain to reach -1dB
- User-configurable "Retro Music Volume Boost" slider in settings

---

## Radio Mode Pause Check in PlayGameMusic

**Status:** Deferred (monitoring)
**Discovered:** v1.3.12
**Related Fixes:** ThemeOverlay preserved in ClearAllPauseSources (Fix 1, applied), OnApplicationStarted pause check (Fix 3, applied)

### Problem

The Radio Mode entry point inside `PlayGameMusic()` in `MusicPlaybackService.cs` does not check `_isPaused` before calling `StartRadioPlayback()`. If a game selection triggers `PlayGameMusic()` while pause sources are active, Radio Mode could start playback during a paused state.

### Current code (lines ~551-558)

```csharp
if (settings?.RadioModeEnabled == true && !forceReload)
{
    if (!_isInRadioMode || !IsPlaying)
        StartRadioPlayback(settings);
    else
        _fileLogger?.Debug($"RadioMode: ignoring game switch...");
    return;
}
```

### Why It's Not Fixed Yet

The two applied fixes cover the actual scenarios that trigger Radio Mode during login/welcome screens:

- **Fix 1** (ClearAllPauseSources preserves ThemeOverlay) — prevents `HandleGameSelected` → `Stop()` from wiping ThemeOverlay, keeping `_isPaused` true
- **Fix 3** (OnApplicationStarted checks `IsPaused`) — the direct `StartRadioPlayback()` call in `UniPlaySong.cs` line 636 now checks `_playbackService?.IsPaused != true` before starting

Together, Fix 1 + Fix 3 prevent Radio Mode from playing during login/welcome hub screens. The `PlayGameMusic()` path (Fix 2) was tested in isolation and didn't resolve the issue alone because the `OnApplicationStarted` direct call was the actual culprit.

### When This Could Become a Problem

- A code path calls `PlayGameMusic()` with `RadioModeEnabled` while pause sources are active but AFTER `OnApplicationStarted` has already fired (e.g., a settings change re-enables Radio Mode mid-session while a theme overlay is active)

### Fix If Needed

Add `_isPaused` check to the Radio Mode block in `PlayGameMusic()`:

```csharp
if (settings?.RadioModeEnabled == true && !forceReload)
{
    if (_isPaused)
        _fileLogger?.Debug($"RadioMode: not starting — paused ({string.Join(", ", _activePauseSources)})");
    else if (!_isInRadioMode || !IsPlaying)
        StartRadioPlayback(settings);
    else
        _fileLogger?.Debug($"RadioMode: ignoring game switch...");
    return;
}
```

---

## yt-dlp Download Command Inconsistencies

**Status:** Partially fixed in v1.3.12
**Discovered:** v1.3.12
**Comparison:** PlayniteSound (PNS) uses a simple command that works reliably; UPS adds extra arguments that may cause failures.

### Problem

UPS has two separate yt-dlp command builds in `YouTubeDownloader.cs` — one for cookie mode (line ~243) and one for no-cookie mode (line ~273). They have inconsistencies that may cause download failures:

1. **Post-processor args** (`--postprocessor-args "ffmpeg:-ar 48000 -ac 2"`) — Present in all modes. Forces FFmpeg to resample to 48kHz stereo after download. If FFmpeg encounters format issues during this step, the entire download fails even though audio was downloaded. PNS does not use post-processor args.

2. **Rate limiting** (`--sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5`) — Present in all modes. Adds 2-5 second delays between requests. May contribute to timeouts on slow connections. PNS does not use rate limiting.

3. **Extractor args** (`--extractor-args "youtube:player_client=android,ios,web"`) — Only in no-cookie mode. Forces specific YouTube client APIs. If YouTube blocks these clients, yt-dlp can't fall back to others. PNS lets yt-dlp choose automatically.

### PNS Command (works reliably)

```
-x --audio-format mp3 --audio-quality 0 --ffmpeg-location="{ffmpeg}" -o "{path}" {url}
```

### UPS No-Cookie Command

```
-x --audio-format mp3 --audio-quality 0 --extractor-args "youtube:player_client=android,ios,web" --sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5 --postprocessor-args "ffmpeg:-ar 48000 -ac 2" --no-playlist --ffmpeg-location="{ffmpeg}" -o "{path}" {url}
```

### UPS Cookie Command

```
{cookiesArg} -x --audio-format mp3 --sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5 --postprocessor-args "ffmpeg:-ar 48000 -ac 2" --ffmpeg-location="{ffmpeg}" -o "{path}" {url}
```

### What's Been Fixed (v1.4.0)

- **`--audio-quality 0`** added to cookie mode — was missing, causing 128kbps downloads instead of best quality
- **`--no-playlist`** added to cookie mode — was missing, could accidentally download entire playlists
- **`--extractor-args`** added to cookie mode — now consistent across all modes

### What's NOT Fixed (monitoring)
- **Post-processor args** — kept intentionally for SDL_mixer compatibility (48kHz stereo). Could be made optional if users report failures.
- **Rate limiting** — kept intentionally to reduce rate abuses by users. Could be made optional if users report timeout issues.

### Future Optimization: Audio-Only Stream for Previews

YouTube serves separate audio-only streams (m4a/AAC, webm/Opus) for nearly all videos. Currently yt-dlp downloads the best available stream (which could include video data) then extracts audio. Using `--format bestaudio[ext=m4a]/bestaudio` would skip video data entirely, reducing download size by 80-90% for typical music videos. Still needs FFmpeg conversion to MP3 for SDL_mixer compatibility. Available on 99%+ of YouTube videos (only very old pre-2012 videos might lack separate audio streams).

---

## DownloadManager.DownloadSong Duplicate Code Path

**Status:** Low priority (cleanup)
**Discovered:** v1.4.0

### Problem

`DownloadManager.DownloadSong()` in `DownloadManager.cs` (lines 922-1011) contains the same download logic duplicated in two branches:

- **Path A** (lines 930-968): Wrapped in `_errorHandler.Try()` — the primary path
- **Path B** (lines 970-1010): Manual `try/catch` — labeled as "fallback to original error handling"

Both paths do identical work: get downloader, set up temp path, create directory, call `downloader.DownloadSong()`, move temp file.

### Why It's Not a Bug

`_errorHandler` is a required constructor parameter (`_errorHandler = errorHandler ?? throw new ArgumentNullException`), so Path B is effectively dead code. It can never execute in normal operation.

### Why It Matters

If a download bug is fixed in Path A, the same fix must be applied to Path B. Easy to forget since Path B never runs. Increases maintenance surface for no benefit.

### Fix If Needed

Remove the `else` block (Path B) entirely since `_errorHandler` is guaranteed non-null.
