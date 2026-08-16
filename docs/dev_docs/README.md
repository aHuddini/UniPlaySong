# UniPlaySong — Developer Documentation

Reference documentation for working on UniPlaySong. Everything here describes UPS internals;
theme developers integrating *with* UPS want [THEME_INTEGRATION_GUIDE.md](THEME_INTEGRATION_GUIDE.md).

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
| [NAUDIO_PIPELINE.md](NAUDIO_PIPELINE.md) | The persistent-mixer architecture, per-sample volume ramping, the five fade curves, song-end detection, format normalization, the logical-pause mechanism, and how the NAudio backend compares to SDL2. |
| [SUPPORTED_FILE_FORMATS.md](SUPPORTED_FILE_FORMATS.md) | Standard formats plus retro chiptune via GME, and the backend auto-switch that GME files trigger. |
| [GME_BUILD.md](GME_BUILD.md) | Building the Game Music Emu native library. |

## Spotify

| Doc | Covers |
|---|---|
| [SPOTIFY_INTEGRATION.md](SPOTIFY_INTEGRATION.md) | Control and radio: the event-mirror architecture (`SpotifyControlService` ↔ SMTC), the drive model and pause ownership, default-music gap handling, and Radio Mode. |
| [SPOTIFY_LIVE_EFFECTS_FEASIBILITY.md](SPOTIFY_LIVE_EFFECTS_FEASIBILITY.md) | Why live effects over Spotify need WASAPI process-loopback capture, and the OS floor that gates it (Windows 10 build 19041). |

## Feature-specific

| Doc | Covers |
|---|---|
| [THEME_INTEGRATION_GUIDE.md](THEME_INTEGRATION_GUIDE.md) | The theme-developer contract: `UPS_MusicControl`, `{PluginSettings}` bindings, theme-shipped audio, external control URIs, unified media controls, now-playing data. Kept current — the canonical theme reference. |
| [ACHIEVEMENT_SOUND_INTEGRATION.md](ACHIEVEMENT_SOUND_INTEGRATION.md) | The achievement/trophy unlock sound contract and rarity URIs. |
| [DYNAMIC_COLOR_ALGORITHM.md](DYNAMIC_COLOR_ALGORITHM.md) | Album-art colour extraction, brightness/saturation tuning, caching. |
| [POTENTIAL_ISSUES.md](POTENTIAL_ISSUES.md) | Known edge cases and deliberately deferred fixes. Written around v1.4.x — verify against current code before acting on it. |
| [PERF_OPTIMIZATION_PLAN.md](PERF_OPTIMIZATION_PLAN.md) | Backlog for the `PerfOptimization` branch — ranked optimization, refactor and test-coverage work, with the measurements behind each item and a list of things already checked and found fine. |
| [SETTINGS_DESIGN.md](SETTINGS_DESIGN.md) | You're changing how the settings window looks or adding a page. The palette and type scale with the reasoning behind them, the toggle switch's anatomy and its three rules, section behaviour, the per-group reset architecture, and the WPF constraints (no letter-spacing, no inset shadow, one child per Border) that shaped all of it. Read the "Changing the style" section before touching `SettingsResources.xaml`. |
| [QUICK_START_PROFILES.md](QUICK_START_PROFILES.md) | Design for one-click Quick Start profiles, split by Desktop vs Fullscreen. Settled decisions, proposed key sets per profile, and the open questions. Not built yet. |

## Other locations

- `docs/EXTERNAL_CONTROL.md` — the localhost control API (StreamDeck, AutoHotkey, Touch Portal).
- `docs/archive/` — shipped design/plan documents and historical session notes, kept for provenance. Not maintained.
- `docs/dev_docs/roadmaps/` and `docs/dev_docs/spikes/` — forward-looking ideas and experiments.
- `CHANGELOG.md` (root) — developer-facing version history, the most reliable record of *why* something changed.

## Conventions

- **Code comments** are single-line `//`. XML doc comments only for public APIs that genuinely need param/returns; drop them when the signature is self-documenting.
- **Two loggers**: `Logger.*` goes to Playnite's `extension.log`; `_fileLogger?.*` goes to `UniPlaySong.log` and is gated behind the Enable Debug Logging setting. Prefer the file logger for anything high-frequency.
- **Changing a setting's default** means editing the backing field, and nothing else. Reset copies from a pristine `UniPlaySongSettings`, so the initialiser is the single source of truth. **Adding** a setting means filing it in `SettingsGroups.Map` (or `NeverReset`); `SettingsResetCoverageTests` fails by name if you forget. See [SETTINGS_DESIGN.md](SETTINGS_DESIGN.md).
- **Update the doc alongside the code.** A doc that describes last release's behaviour is worse than no doc; this index exists because several didn't get that treatment.

---

**Last updated**: 2026-08-16 · **Covers**: v1.7.4
