# UniPlaySong — Developer Documentation

Reference documentation for working on UniPlaySong. Everything here describes UPS internals;
theme developers integrating *with* UPS want [THEME_INTEGRATION_GUIDE.md](../Theme%20Support/THEME_INTEGRATION_GUIDE.md).

## Start here

| Doc | Read it when |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | You need the shape of the system — directory map, service organization, data flow, threading model, extension points. |
| [TECHNICAL_REFERENCE.md](TECHNICAL_REFERENCE.md) | You're chasing a specific behaviour — playback state variables, song selection, default-music fallback, skip logic, preview mode, fades, constants. The debugging tips at the end are the fastest way in. |
| [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) | You're building or packaging. `version.txt` is the version single source of truth. |
| [DEPENDENCIES.md](DEPENDENCIES.md) | You hit a missing DLL or external tool (SDL2, NAudio, yt-dlp, FFmpeg, the native Spotify loopback shim). |

## Audio pipeline

| Doc | Covers |
|---|---|
| [NAUDIO_PIPELINE.md](features/NAUDIO_PIPELINE.md) | The persistent-mixer architecture, per-sample volume ramping, the five fade curves, song-end detection, format normalization, the logical-pause mechanism, and how the NAudio backend compares to SDL2. |
| [SUPPORTED_FILE_FORMATS.md](SUPPORTED_FILE_FORMATS.md) | Standard formats plus retro chiptune via GME, and the backend auto-switch that GME files trigger. |
| [CHIPTUNE_GME_DLL_BUILD.md](features/CHIPTUNE_GME_DLL_BUILD.md) | Building the Game Music Emu native library. |

## Spotify

| Doc | Covers |
|---|---|
| [SPOTIFY_INTEGRATION.md](features/SPOTIFY_INTEGRATION.md) | Control and radio: the event-mirror architecture (`SpotifyControlService` ↔ SMTC), the drive model and pause ownership, default-music gap handling, and Radio Mode. |
| [SPOTIFY_LIVE_EFFECTS_FEASIBILITY.md](../archive/SPOTIFY_LIVE_EFFECTS_FEASIBILITY.md) | Why live effects over Spotify need WASAPI process-loopback capture, and the OS floor that gates it (Windows 10 build 19041). |

## Feature-specific

`docs/dev_docs/features/` — one doc per feature area. The audio-pipeline and Spotify docs above live here too.

| Doc | Covers |
|---|---|
| [VISUALIZER_DYNAMIC_COLOR_ALGORITHM.md](features/VISUALIZER_DYNAMIC_COLOR_ALGORITHM.md) | Album-art colour extraction, brightness/saturation tuning, caching. |
| [QUICK_START_PROFILES.md](features/QUICK_START_PROFILES.md) | Design for one-click Quick Start profiles, split by Desktop vs Fullscreen. Settled decisions, proposed key sets per profile, and the open questions. |

## Integrating with other plugins

`docs/dev_docs/plugin/` — the contracts other plugins call into.

| Doc | Covers |
|---|---|
| [ACHIEVEMENT_SOUND_INTEGRATION.md](plugin/ACHIEVEMENT_SOUND_INTEGRATION.md) | The achievement/trophy unlock sound contract and rarity URIs, as used by Playnite Achievements. |

Theme developers want [THEME_INTEGRATION_GUIDE.md](../Theme%20Support/THEME_INTEGRATION_GUIDE.md) — the `UPS_MusicControl` contract, `{PluginSettings}` bindings, theme-shipped audio, external control URIs, unified media controls and now-playing data. Kept current; the canonical theme reference.

## Other locations

- `docs/EXTERNAL_CONTROL.md` — the localhost control API (StreamDeck, AutoHotkey, Touch Portal).
- `docs/Theme Support/` — everything aimed at theme developers, starting with [THEME_INTEGRATION_GUIDE.md](../Theme%20Support/THEME_INTEGRATION_GUIDE.md).
- `docs/archive/` — shipped design/plan documents, planning backlogs, proven spikes and historical session notes, kept for provenance. Not maintained. Includes `POTENTIAL_ISSUES.md` (edge cases and deferred fixes, written around v1.4.x), `PERF_OPTIMIZATION_PLAN.md` (the `PerfOptimization` backlog), and `spikes/` (throwaway experiments whose questions are settled — the code that shipped from them lives under `native/` or `src/`).
- `docs/dev_docs/roadmaps/` — forward-looking ideas not yet committed to.
- `docs/dev_docs/proposals/` — planning and design documents for work not yet committed to. **Gitignored**: proposals stay local so nothing half-decided is published. Put new planning docs here; move one out only when it becomes shipped reference.
- `CHANGELOG.md` (root) — developer-facing version history, the most reliable record of *why* something changed.

## Conventions

- **Code comments** are single-line `//`. XML doc comments only for public APIs that genuinely need param/returns; drop them when the signature is self-documenting.
- **Two loggers**: `Logger.*` goes to Playnite's `extension.log`; `_fileLogger?.*` goes to `UniPlaySong.log` and is gated behind the Enable Debug Logging setting. Prefer the file logger for anything high-frequency.
- **Changing a setting's default** means updating the backing field, the per-tab Reset handler, and verifying the global reset — all three.
- **Update the doc alongside the code.** A doc that describes last release's behaviour is worse than no doc; this index exists because several didn't get that treatment.

---

**Last updated**: 2026-07-31 · **Covers**: v1.7.1
