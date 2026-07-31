# Trailer Audio Extraction — Design Spec

**Date:** 2026-06-17
**Status:** Approved (design); pending implementation plan
**Target version:** v1.5.5 (Experimental)
**Author:** UniPlaySong dev

---

## Summary

When a game has no UniPlaySong (UPS) music, UPS can already fall back to a
configurable default-music source. This feature gives the `DeferToTrailerAudio`
source real behavior: instead of staying silent so another plugin's trailer can
play, **UPS extracts the audio track from the game's ExtraMetadataLoader (EML)
video trailer and plays it itself** as the default music.

This bypasses EML entirely. UPS does not — and cannot reliably — mute or control
another plugin's `MediaElement`. Instead it reads the trailer `.mp4` off disk,
demuxes its audio with FFmpeg, caches the result, and feeds that audio file into
its normal playback pipeline. The user hears the trailer's music/audio, looped,
with all the usual UPS controls (volume, fade, skip).

The feature is **experimental** and **off by default** (it is one selectable
value of the existing `DefaultMusicSource` enum on the Experimental tab).

---

## Background & current state

There are two unrelated "trailer" mechanisms in UPS today; this spec touches only
the second:

- **`PauseOnTrailer`** (General tab) — the only thing that pauses UPS music around
  a playing trailer. It gates `MediaElementsMonitor.Attach()`. **Out of scope.**
- **`DefaultMusicSource.DeferToTrailerAudio`** (Experimental tab) — a default-music
  source. As of v1.5.4 it is an intentional **no-op**: when a no-music game is
  selected, UPS adds nothing to `songs` and stays silent, with a diagnostics-only
  log line reporting whether an EML trailer exists. **This is what we are
  changing.**

EML (`darklinkpower/PlayniteExtensionsCollection`) stores trailers at:

```
<Config>\ExtraMetadata\Games\{GameId}\VideoTrailer.mp4        (full trailer)
<Config>\ExtraMetadata\Games\{GameId}\VideoMicrotrailer.mp4   (micro trailer)
```

This feature uses **`VideoTrailer.mp4` only**. Games with only a micro-trailer, or
no trailer, are treated as having no trailer (→ silence).

FFmpeg is already a UPS dependency (path in `settings.FFmpegPath`, configured on the
Downloads tab, validated by `FFmpegHelper.IsAvailable`). `.m4a` and `.aac` are
already in `Constants.SupportedAudioExtensionsLowercase`, so extracted trailer
audio plays through the existing pipeline with no format work.

---

## Goals / Non-goals

**Goals**
- For no-music games with a full EML trailer, play the trailer's audio as default music.
- Extract on demand, cache forever, keyed per game.
- Fail safe: every error path resolves to silence, never a crash or a playback-time popup.
- Keep `MusicPlaybackService` thin; isolate all I/O and process logic in a new service.

**Non-goals**
- Muting or controlling EML's `MediaElement` (architecturally infeasible; explicitly rejected).
- Touching `PauseOnTrailer` / `MediaElementsMonitor` / `PauseSource.Video` behavior.
- Micro-trailer support.
- Automatic cache invalidation when a trailer file changes (cache is forever; manual clear only).
- An automated test suite (UPS has none; verification is build-gate + one manual scenario).

---

## Architecture & data flow

The feature lives entirely on the UPS side. EML is never touched. UPS reads
`VideoTrailer.mp4` off disk and demuxes its audio with FFmpeg, exactly like any
other default-music source resolves a file path and adds it to `songs`.

```
PlayGameMusic(game)  [MusicPlaybackService]
  └─ songs.Count == 0 && settings.EnableDefaultMusic
      └─ case DefaultMusicSource.DeferToTrailerAudio:
          └─ path = _trailerAudioService.GetOrExtractAudio(game)
               ├─ cache hit?  → return  <cacheDir>\{GameId}.m4a   (instant)
               └─ cache miss:
                    ├─ ResolveFullTrailer(game) → <emlGamesPath>\{GameId}\VideoTrailer.mp4
                    │     └─ not found        → return null   (→ silence)
                    ├─ FFmpegHelper.IsAvailable(FFmpegPath)?
                    │     └─ no               → return null   (→ silence)
                    └─ Extract(mp4, outPath)  (FFmpeg -vn -c:a copy; transcode fallback)
                         ├─ success           → return cached path
                         └─ failure           → return null   (→ silence)
          └─ if (path != null) songs.Add(path);
```

Once the cached `.m4a` is added to `songs`, the existing pipeline handles
looping, fade, volume, and skip — it is just another audio file to the player.

`-c:a copy` copies the AAC audio stream out of the MP4 container without
re-encoding: near-instant, lossless, output `.m4a`. If `-c:a copy` produces an
unplayable file or fails (rare non-AAC trailer audio), the service retries once
transcoding to `.mp3`.

---

## Components

### 1. `TrailerAudioService` (new)

**Files:** `src/Services/TrailerAudioService.cs`, `src/Services/ITrailerAudioService.cs`

Single responsibility: *given a game, return the playable path to its extracted
trailer audio, extracting and caching on first call.*

**Public surface:**

```csharp
// Returns a playable path to the game's extracted trailer audio, extracting +
// caching on first call. Returns null if there is no full trailer, FFmpeg is
// unavailable, or extraction fails (caller stays silent).
string GetOrExtractAudio(Game game);
```

**Dependencies (constructor-injected, mirroring GameMusicFileService):**
- `UniPlaySongSettings _settings` — source of `FFmpegPath`.
- `string _emlGamesPath` — the `<Config>\ExtraMetadata\Games` root, resolved from
  the SDK `ConfigurationPath` by the caller, exactly as `GameMusicFileService`
  already does. No second source of truth for this path.
- `string _pluginDataPath` — plugin user-data root, for the cache directory.
- `FileLogger _fileLogger` — gated debug logging.

**Internal members (top to bottom):**
1. `GetCacheDir()` → `<pluginDataPath>\TrailerAudioCache\`, created on first use.
2. `GetCachedPath(Game)` → `<cacheDir>\{game.Id}.m4a`. Game `Id` is a GUID — safe
   filename, stable across renames, unique per game.
3. `ResolveFullTrailer(Game)` → `<emlGamesPath>\{game.Id}\VideoTrailer.mp4` if it
   exists, else `null`. **Full trailer only** — narrower than the existing
   `HasTrailerVideo()` (which OR's both files). `HasTrailerVideo()` is not modified.
4. `GetOrExtractAudio(Game)` — orchestration per the data-flow diagram above.
5. `Extract(string mp4, string outPath)` — the FFmpeg invocation, modeled on
   `AudioConversionService`'s `ProcessStartInfo` pattern. Synchronous with a
   timeout. Writes to a uniquely-named `.tmp` then `File.Move`s it into place, so
   a killed/crashed FFmpeg can never leave a half-written file that a later call
   reads as a valid cache hit.

**Extraction command (fast path):**
```
ffmpeg -y -i "<VideoTrailer.mp4>" -vn -c:a copy "<temp>.m4a"
```
**Transcode fallback (only if copy fails / output unplayable):**
```
ffmpeg -y -i "<VideoTrailer.mp4>" -vn "<temp>.mp3"
```

### 2. `MusicPlaybackService` (modify)

**File:** `src/Services/MusicPlaybackService.cs`

- **`case DefaultMusicSource.DeferToTrailerAudio:`** (currently the no-op at
  ~line 854) becomes ~3 lines: call `_trailerAudioService.GetOrExtractAudio(game)`,
  null-check, `songs.Add(path)` on success. Mirrors the `ActiveThemeMusic` case
  at ~line 839.
- **`IsDefaultMusicPath(path, settings)`** (~line 484): the `DeferToTrailerAudio`
  case currently returns `false` with the comment "No UPS file is ever loaded."
  That is no longer true — a cached `.m4a` *is* loaded. This case must now match
  the cached trailer-audio path for the current game, exactly like the
  `ActiveThemeMusic` case (~line 479). **Missing this breaks position-preservation
  and looping** (the same class of bug that `CompletionStatusPool` hit — see
  project memory). The cached-path comparison reuses
  `_trailerAudioService.GetCachedPath`-equivalent logic exposed for the match, or
  compares against the last loaded path; implementation plan decides the cleanest
  of the two (favor matching the deterministic cached path).
- Inject `ITrailerAudioService` into the constructor and wire it at the
  composition point where `GameMusicFileService` is constructed (same place
  `_emlGamesPath` is already resolved).

### 3. Settings UI (modify)

**Files:** `UniPlaySongSettings.cs` (label/help strings if string-based),
`UniPlaySongSettingsView.xaml`, `UniPlaySongSettingsView.xaml.cs`,
`UniPlaySongSettingsViewModel.cs`

- **Relabel** the Experimental-tab `DeferToTrailerAudio` option:
  - **Label:** "Stream audio from the game's EML trailer (no-music games only)"
  - **Help text:** "For games with no UniPlaySong music, extracts the audio from
    the game's EML video trailer and plays it as the default music. The first
    play for each game may take a moment while the audio is extracted (cached
    afterward). Requires FFmpeg (set in the Downloads tab) and a full video
    trailer from the ExtraMetadataLoader extension. Games with only a
    micro-trailer, or no trailer, stay silent."
- **Gate when FFmpeg is missing:** the option is disabled/greyed with an inline
  note "Requires FFmpeg — set its path in the Downloads tab" whenever
  `FFmpegHelper.IsAvailable(settings.FFmpegPath)` is false. Enablement is
  evaluated when the settings window opens (existing `BeginEdit()` init-on-open
  pattern). A same-session FFmpeg-path fix requires reopening settings to
  un-grey — an accepted constraint that avoids cross-tab live binding. Wire
  `IsEnabled` following the existing conditionally-enabled-setting pattern in the
  view; do not invent a new one.

### 4. Cleanup tab (modify)

**Files:** `UniPlaySongSettingsView.xaml`, `UniPlaySongSettingsView.xaml.cs`

- New **"Clear trailer-audio cache"** button. Deletes the contents of
  `<pluginDataPath>\TrailerAudioCache\` and reports count / space freed using the
  same notification pattern as the existing Cleanup-tab clear handlers. Match an
  existing handler; do not invent new UI patterns.
- **Factory reset** needs no change — the cache dir lives under the plugin data
  path, so existing factory-reset logic that wipes plugin data removes it.

---

## Error handling, edge cases, concurrency

Every path resolves to either *play the cached file* or *stay silent* — never a
crash, never a playback-time popup.

| Situation | Behavior |
|---|---|
| No `VideoTrailer.mp4` (micro-only or none) | return null → silence |
| FFmpeg path unset/invalid at play time | return null → silence |
| FFmpeg runs but errors (bad stream, exit ≠ 0) | delete temp, return null → silence |
| `-c:a copy` produces unplayable output | transcode-to-`.mp3` fallback |
| FFmpeg hangs | killed at timeout, temp deleted, return null |
| Cache hit | return cached path immediately |
| Cached file exists but is 0 bytes / corrupt | treated as miss → re-extract |
| Game has no `Id` / null game | return null → silence |
| EML root path unresolvable | return null → silence |
| Game **with** UPS music | trailer path never reached (gated by `songs.Count == 0`) |

**Concurrency.** Default-music selection fires on game-select and can repeat
rapidly during scrolling. Two selections of the *same* game could race into
`Extract`. The temp-then-`File.Move` pattern makes this safe: each call writes a
uniquely-named temp (e.g. `{Id}.{n}.tmp`) then atomically moves onto
`{Id}.m4a`. Last writer wins, identical content, no partial reads. No lock — the
filesystem move is the synchronization point.

**Interaction with the pause/video subsystem.** None. A playing trailer-audio
file is just default music in the normal pipeline. `PauseOnTrailer` and
`PauseSource.Video` are a separate, orthogonal feature and are unchanged. If a
user has both on, existing pause-on-trailer logic still governs whether UPS
pauses; this feature does not entangle with it.

---

## Logging policy

Log only the actionable and the rare. Stay silent on the expected and the
repeated, to avoid flooding `UniPlaySong.log` during rapid game-scroll selection.
All logging is behind `_fileLogger?.` (gated by `EnableDebugLogging`).

| Situation | Log? | Rationale |
|---|---|---|
| No `VideoTrailer.mp4` | no | Expected for most games; fires on every scroll |
| Cache hit | no | Hot path, every replay |
| EML root unresolvable | once per session (guard flag) | Real config issue, don't repeat |
| FFmpeg unavailable at play time | once per session (guard flag) | Actionable, don't spam |
| Extraction started | no | Implied by the success line |
| Extraction succeeded | yes (one line: game + ms) | Rare (once per game ever); confirms it works / how slow |
| Extraction failed (exit ≠ 0, hang, unplayable) | yes (one line: game + reason/exit code) | Rare and actionable |
| Cached file corrupt → re-extract | yes (one line) | Rare; explains an unexpected re-extract |

Two `bool` guard fields (`_loggedNoFfmpeg`, `_loggedNoEmlRoot`) keep the two
config-error cases to one line per session. During normal browsing the feature
writes nothing to the log.

---

## Testing & verification

UPS has no automated test harness. Verification is the mandatory build gate plus
one manual acceptance scenario (this is an experimental feature; the bar is "core
scenario works").

**Build gate (every change, in order, verified output before any "done" claim):**
```
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

**Manual acceptance test:**

| Setup | Action | Expected |
|---|---|---|
| Option ON, FFmpeg set, game with no UPS music + a full EML trailer | Select the game | Brief pause on first select, then trailer audio plays and loops as default music. Re-selecting plays instantly from cache. |

**Code self-verification before handing to tester (cheap, prevents known footguns):**
- Confirm `.m4a` / `.aac` are in `Constants.SupportedAudioExtensionsLowercase`.
- Confirm `DeferToTrailerAudio` is handled in `IsDefaultMusicPath()` so the cached
  file is treated as default music (the `CompletionStatusPool` lesson).
- Confirm the `case` is reached only inside the `songs.Count == 0 &&
  EnableDefaultMusic` block.

---

## File touch list

| File | Change |
|---|---|
| `src/Services/ITrailerAudioService.cs` | **Create** — interface |
| `src/Services/TrailerAudioService.cs` | **Create** — extraction + caching |
| `src/Services/MusicPlaybackService.cs` | Modify — `DeferToTrailerAudio` case (~854), `IsDefaultMusicPath` case (~484), inject service |
| `src/UniPlaySong.cs` (or composition root) | Modify — construct + inject `TrailerAudioService` where `_emlGamesPath` is resolved |
| `UniPlaySongSettingsView.xaml` | Modify — relabel option, gate `IsEnabled`, add Cleanup-tab clear button |
| `UniPlaySongSettingsView.xaml.cs` | Modify — clear-cache handler, open-time enablement eval |
| `UniPlaySongSettingsViewModel.cs` | Modify — display values for label/help/enablement if VM-bound |
| `UniPlaySongSettings.cs` | Modify — label/help strings if string-based (else XAML only) |

`HasTrailerVideo()` in `GameMusicFileService.cs` is intentionally **not** modified.
```

