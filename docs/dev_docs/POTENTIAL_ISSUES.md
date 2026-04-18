# Potential Issues

Known edge cases and deferred fixes that may need attention in future versions.

## Broader Chiptune Support Beyond GME

**Status:** Future investigation (v1.5+)
**Driver:** GME's chip coverage is limited to home-console FM/PSG/APU chips. Neo Geo (YM2610), arcade (YM2151), PC-88 (YM2608), Saturn (SCSP), CPS-2 (QSound), and most other arcade boards are not emulated by GME — their VGM files play silent. The vast majority of files on VGMRips fall into this unsupported category.

Investigation was triggered by a user report of silent playback on a VGZ file from VGMRips (King of Fighters '99, Neo Geo). Confirmed via our test harness that GME sees the file as "Sega SMS/Genesis" with only 4 PSG voices, producing zero audio — GME has no YM2610 emulator, so the chip writes are discarded.

### Option A — Do nothing further (current state for v1.4.1)

Document the boundary. Users with arcade / Neo Geo music use external tools (e.g. VGMPlay standalone, foobar2000 with foo_input_vgm) to pre-render VGZ → WAV, then drop the WAVs into their game music folders. Zero additional integration work; zero legal risk; broadest format support (because they'd be playing finished WAVs). Limitation is UX — users have to convert.

### Option B — Integrate VGMPlay (C API, broad coverage, MAME-licensed cores)

[VGMPlay](https://github.com/vgmrips/vgmplay) by ValleyBell has a proper C API in `VGMPlay_Intf.h`:
- `VGMPlay_Init()`, `OpenVGMFile(path)`, `PlayVGM()`, `FillBuffer(buffer, samples)`, `SeekVGM()`, `CloseVGMFile()` — clean lifecycle + pull-based audio rendering that maps naturally to our NAudio `WaveStream + ISampleProvider` pattern (same integration shape as `GmeReader`).
- Supports every chip GME does, plus YM2610 (Neo Geo), YM2151 (arcade), YM2608 (PC-88), QSound (CPS-2), SegaPCM, Namco C140/C352, Konami K051649/K053260, and more.
- VGZ support built in (built against zlib like GME).

**Licensing:**
- VGMPlay ships chip cores from multiple origins. Most arcade-relevant cores (YM2610, YM2151, etc.) are MAME-derived.
- MAME license clause: *"Redistributions may not be sold, nor may they be used in a commercial product or activity."*
- In practice, the entire free chiptune ecosystem — VLC, foobar2000 chiptune plugins, ZXTune, Audacious, DeaDBeeF, VGMPlay itself, libvgm — has shipped MAME-derived cores in free software for 20+ years without objection from the MAME team. The license is targeted at commercial MAME ROM sales, not at free plugins that happen to reuse the chip emulation.
- **UPS's profile fits the "free software" pattern:** MIT-licensed, distributed free through Playnite's add-on database, no monetization, source on GitHub. Shipping MAME-derived cores is consistent with industry practice.
- **Attribution requirements we'd need to meet:** bundle `mame_license.txt`, preserve MAME copyright headers in vendored source, add attribution to README Credits ("Chip emulation cores from VGMPlay and MAME").

**Scope estimate: ~3-4 days:**
1. Build VGMPlay as x86 DLL with the C interface exposed (uses CMake + Visual Studio)
2. `VgmPlayNative.cs` — P/Invoke declarations (mirror of `GmeNative.cs`)
3. `VgmPlayReader.cs` — `WaveStream + ISampleProvider` wrapper (mirror of `GmeReader.cs`)
4. Chip-inspection logic to route VGM/VGZ: if file uses only GME-supported chips → route to existing GmeReader; otherwise → VgmPlayReader. Keeps hot path on the simpler / smaller GME engine for common cases.
5. Bundle VGMPlay DLL + license file alongside GME in `src/Audio/Native/RetroChiptune/`
6. Update `README.md` Credits and `LICENSE` third-party section

### Option C — Integrate libvgm (C++ API, active rewrite of VGMPlay)

[libvgm](https://github.com/ValleyBell/libvgm) is ValleyBell's ground-up rewrite of VGMPlay, organized into modular sub-libraries (libEmu, libAudio, player). Same chip coverage, cleaner architecture, more active development. **But:**
- C++ API only (`PlayerA` class). P/Invoke requires `extern "C"` functions, so we'd need to write a ~200-line C wrapper DLL around `PlayerA`.
- No pre-built binaries; same CMake build-from-source requirement.
- Same MAME-core licensing as VGMPlay (inherits the chip sources).
- Higher effort than Option B for marginal API elegance benefit.

Scope estimate: ~4-5 days (vs. ~3-4 for VGMPlay). Not compelling unless we're picking a long-term foundation rather than a quick addition.

### Option D — Integrate VGMPlay/libvgm WITHOUT MAME cores

Technically possible but delivers very little. VGMPlay's build system supports per-chip core selection via `EC_MAME`/`EC_GENS`/`EC_NUKED`/`EC_DBOPL`/`EC_OOTAKE`/`EC_NSFPLAY`/`EC_EMU` tags. We could build VGMPlay with MAME-derived chip source files excluded (e.g. drop `fm.c`, `2610intf.c`, `qsound_intf.c`, `c140.c`, etc., keep only `emu2413.c`, Nuked-OPM, etc.).

**Problem: the motivating chips have no non-MAME implementation available anywhere.**

| Chip | Used by | Non-MAME alternative? |
|------|---------|----------------------|
| YM2610 | Neo Geo | **Nuked-OPNB** (WIP, 5 commits, unverified accuracy) |
| YM2608 | PC-88/PC-98 | **None** |
| YM2151 | Arcade (Capcom CPS1, early Konami/SEGA) | **Nuked-OPM** (mature) |
| YMF262 (OPL3) | DOS Sound Blaster 16 | **Nuked-OPL3** or **DBOPL** (both mature) |
| QSound | Capcom CPS-2/CPS-3 | **None** |
| Namco C140/C352/C219 | Namco arcade | **None** |
| K051649/K053260/K054539 | Konami arcade | **None** |
| SegaPCM | Sega arcade (OutRun, Space Harrier) | **None** |
| SCSP | Sega Saturn | **None** |
| YMF271, YMF278B, ES5503, ES5506 | Various | **None** |

A MAME-stripped VGMPlay build would cover: what GME already covers + YM2151 arcade + OPL3 PC. **Not** Neo Geo, **not** PC-88, **not** CPS-2, **not** Namco or Konami arcade. Roughly a ~10-15% upgrade over GME-only coverage, and those are formats the user is less likely to ask about than the ones that would remain silent.

Scope estimate: ~4-5 days (same as Option B, no time saved). Plus potential stability concerns if VGMPlay's runtime assumes chip cores exist that we've stripped out — would need to test each expected code path.

**Verdict:** not a compelling middle ground. The MAME cores ARE the valuable part of VGMPlay/libvgm for our use case. Shipping the library without them is ~Option B with most of the value removed.

### Option E — Clean-room-only custom engine (NukeYKT family)

Cherry-pick individual LGPL v2.1 chip emulators from [nukeykt's repos](https://github.com/nukeykt):
- **[Nuked-OPN2](https://github.com/nukeykt/Nuked-OPN2)** (YM2612 / Genesis) — already what GME uses. ✓
- **[Nuked-OPL3](https://github.com/nukeykt/Nuked-OPL3)** (YMF262 / Sound Blaster 16) — mature.
- **[Nuked-OPM](https://github.com/nukeykt/Nuked-OPM)** (YM2151 / arcade — Capcom CPS1, early Konami/SEGA) — mature.
- **[Nuked-OPNB](https://github.com/nukeykt/Nuked-OPNB)** (YM2610 / Neo Geo) — WIP, ~5 commits, accuracy uncertain.

Scope estimate: ~5-7 days — build a custom GME fork that pulls in Nuked-OPM and (cautiously) Nuked-OPNB. Unlocks SOME arcade music and maybe Neo Geo. Doesn't approach VGMPlay's coverage (no PC-88, no CPS-2, no Namco, no Konami). Same realistic coverage gain as Option D with less library-level baggage but higher custom-code risk.

### Recommendation

**Option B (VGMPlay, with MAME cores).** Best coverage, cleanest C API, well-matched to existing `GmeReader` integration pattern, and MAME-licensed cores are practically safe to ship in a free MIT plugin based on 20+ years of ecosystem precedent (VLC, foobar2000 plugins, VGMPlay itself, libvgm, ZXTune, Audacious, DeaDBeeF — all free, all ship MAME-derived chip cores). MAME's license targets commercial MAME ROM sales, not free plugins reusing chip code. We'd bundle `mame_license.txt`, preserve copyright headers, credit MAME in README.

**Options D and E exist but are not recommended** — they deliver small coverage gains (OPL3, YM2151) for similar effort, without unlocking the motivating chips (Neo Geo, CPS-2, PC-88). Only worth pursuing if we specifically decide "no MAME-origin code at all" as a hard constraint.

**For v1.4.1 specifically: stay on Option A.** Ship the GME-only support we have, document the boundary clearly (done in `SUPPORTED_FILE_FORMATS.md`), and track Option B as a v1.5+ feature.

### Related

- User-facing docs on current GME boundary: [SUPPORTED_FILE_FORMATS.md](SUPPORTED_FILE_FORMATS.md)

---

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

---

## Celebration Trigger: Hardcoded English Completion-Status Names

**Status:** Low priority (silent feature break for non-English users / renamed statuses)
**Discovered:** v1.4.1 (while adding the `CelebrateBeaten` toggle)

### Problem

`UniPlaySong.OnItemUpdated` fires the completion celebration by comparing `newStatus.Name` against literal English strings:

```csharp
// src/UniPlaySong.cs, roughly line 413
newStatus.Name.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
(_settings.CelebrateBeaten && newStatus.Name.Equals("Beaten", StringComparison.OrdinalIgnoreCase))
```

This silently stops working in three scenarios:

1. **User renamed a built-in status.** Playnite lets users rename completion statuses (Settings → Completion Statuses). If a user renamed "Completed" to, say, "Platinum" or "100%", the trigger matches nothing and the fanfare never plays. No error, no log, no indication — the feature just appears to be broken.
2. **Non-English Playnite install.** Playnite's UI is localized but the `Name` field on the built-in statuses is stored as whatever label the user sees. A Spanish user with "Completado" / "Superado", a German user with "Abgeschlossen" / "Durchgespielt", a French user with "Terminé" / "Battu" — none of them would ever get the fanfare.
3. **User deleted the built-in status and created a new one with the same name.** Edge case, but the new status has a different GUID. Name-match still works here, so this isn't actually broken — included for completeness.

### Why we left it as-is for v1.4.1

The name match matches how the feature originally shipped (v1.2.x-ish), and changing the semantic to GUID-match would require schema migration (persist status GUIDs in settings, with a one-time name-based lookup at first load to backfill). For the `CelebrateBeaten` addition I mirrored the existing pattern for consistency — opting not to introduce a partial half-fix for only one of the two matches.

### Proper fix

Match by `CompletionStatusId` GUID, not `Name`. Playnite doesn't expose stable GUIDs for the built-in statuses as constants (they're user-editable, not fixed by the SDK) — but they ARE stable per-database. Approach:

1. Add `CelebrationCompletedStatusId` and `CelebrationBeatenStatusId` settings (`Guid?`).
2. On first-ever settings load (or when the IDs are null), scan `_api.Database.CompletionStatuses` and match by Name to populate the IDs. This is the one-time name→ID resolution.
3. Subsequent triggers compare `update.NewData.CompletionStatusId` to the stored GUIDs directly.
4. Add a settings-UI dropdown under each toggle: "Trigger status: [Completed ▼]" so users can override the detected match (or pick a custom status if they've created one like "Platinum").

Alternative (lighter-weight): use the same `CompletionStatusSelectionDialog` pattern used for Nostalgia Mode — replace the two bools with a single `Guid[] CelebrationStatusIds` setting and a multi-select list. Same correctness win, no hardcoded names anywhere, and lets users celebrate any status they want (e.g. "Abandoned? Sad trombone.").

### Scope

Half a day of work. Requires settings schema bump (old `EnableCompletionCelebration`/`CelebrateBeaten` bools → new `CelebrationStatusIds` array) with a one-shot migration path. UI needs a small multi-select or two dropdowns.

### When to do it

Queue for v1.5 when we next revisit the Playback / Gamification tab. Not urgent — the current behavior is correct for English users who haven't renamed the built-in statuses (the vast majority of the user base based on Playnite's localization adoption).
