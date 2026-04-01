# Bulk Audio Format Conversion — Design

**Date:** 2026-03-30
**Version:** v1.3.10
**Status:** Approved

## Goal

Allow users to bulk convert all music files in their library to OGG or MP3 format at selectable bitrate quality (128/192/256 kbps), with optional backup of originals and parallel processing.

## User Flow

1. Settings > Editing > Bulk Actions
2. Select target format (OGG / MP3) from dropdown
3. Select quality (128kbps / 192kbps / 256kbps) from dropdown
4. Optional checkbox: "Keep original files as backup" (default: off)
5. Click "Convert All Music Files"
6. Confirmation: "This will convert X files to OGG 192kbps. Continue?"
7. Progress dialog (reuses NormalizationProgressDialog)
8. Completion report:
   ```
   Conversion Complete!

   Converted: 412 | Failed: 3 | Total: 415
   Original size: 4.2 GB
   New size: 1.8 GB
   Space saved: 2.4 GB (57%)
   ```
9. Failed files listed (up to 5 shown, rest summarized)

## FFmpeg Conversion Logic

### Format → FFmpeg Arguments

| Target | Codec | FFmpeg args |
|--------|-------|-------------|
| MP3 128kbps | libmp3lame | `-c:a libmp3lame -b:a 128k` |
| MP3 192kbps | libmp3lame | `-c:a libmp3lame -b:a 192k` |
| MP3 256kbps | libmp3lame | `-c:a libmp3lame -b:a 256k` |
| OGG 128kbps | libvorbis | `-c:a libvorbis -b:a 128k` |
| OGG 192kbps | libvorbis | `-c:a libvorbis -b:a 192k` |
| OGG 256kbps | libvorbis | `-c:a libvorbis -b:a 256k` |

### Per-File Conversion Flow

1. Source: `game_folder/song.wav`
2. FFmpeg outputs to temp file: `game_folder/song.converting.ogg`
3. On success:
   - If backup enabled: rename `song.wav` → `song-preconvert.wav`
   - If backup disabled: delete `song.wav`
   - Rename `song.converting.ogg` → `song.ogg`
4. On failure:
   - Delete temp file `song.converting.ogg` (if exists)
   - Original untouched
   - Add to failed files list, continue

The `.converting` temp file ensures originals are never touched until FFmpeg confirms success.

## Architecture

```
Button Click (Settings UI)
    │
    ▼
ConversionDialogHandler
    │  - Validates FFmpeg path
    │  - Collects all music files from all game folders
    │  - Shows confirmation with file count
    │  - Stops playback
    │  - Opens progress dialog
    │
    ▼
AudioConversionService
    │  - Parallel.ForEach (max 3 workers)
    │  - Per-file FFmpeg conversion
    │  - Temp file → rename pattern
    │  - Tracks file sizes for space report
    │  - Reports progress via IProgress<>
    │
    ▼
NormalizationProgressDialog (reused)
    - Shows current file, progress bar, status log
    - Cancel button
```

## Files

**New:**
- `src/Services/AudioConversionService.cs` — FFmpeg conversion logic, parallelization, size tracking
- `src/Handlers/ConversionDialogHandler.cs` — UI flow, validation, progress dialog management

**Modified:**
- `src/UniPlaySongSettings.cs` — new properties
- `src/UniPlaySongSettingsView.xaml` — format/quality dropdowns, backup checkbox, convert button
- `src/UniPlaySongSettingsViewModel.cs` — ConvertAllMusicCommand
- `src/UniPlaySongSettingsView.xaml.cs` — reset handler update

## Settings

| Property | Type | Default | Values |
|----------|------|---------|--------|
| `ConversionTargetFormat` | string | `"ogg"` | `"ogg"`, `"mp3"` |
| `ConversionBitrate` | string | `"192"` | `"128"`, `"192"`, `"256"` |
| `ConversionKeepOriginals` | bool | `false` | checkbox |

## Design Decisions

- **Reuse NormalizationProgressDialog** — same progress UI pattern, no new dialog needed
- **Parallelization capped at 3 workers** — matches existing normalization pattern, prevents system overload
- **Temp file pattern** — `.converting` suffix protects originals during FFmpeg execution
- **All files re-encoded** — no skip for files already in target format (user may want to change bitrate)
- **Backup off by default** — users may have thousands of files, doubling storage is undesirable
- **Backup suffix: `-preconvert`** — distinguishes from normalization's `-normalized` suffix
- **Continue on error** — per-file try/catch, never stops bulk operation for a single failure
- **Space reporting** — tracks cumulative original/new file sizes for completion summary
