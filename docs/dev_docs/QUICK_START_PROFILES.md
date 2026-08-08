# Quick Start Profiles — design

A Quick Start page that configures UPS for a way of using it, in one click, instead of making a new
user visit six tabs to discover what the plugin can do. Landing page for the settings UI.

**Status:** design, not built. Decisions below are settled; the value tables are the part to argue with.

---

## What a profile is

A profile is **not** a new settings mechanism. It is a named set of values written into settings the
plugin already has, applied through the existing preset enums where one covers the ground
(`StylePreset`, `VizPreset`, `IconGlowPreset`).

The distinction that matters: these are **behavioural** profiles, not cosmetic ones. "PS3 Style" is
not a reverb choice — it is select-to-play, a short preview clip, snappy fades, and console-like
pause semantics. That spans tabs a user currently has to find one at a time.

## Settled decisions

**Scope — declared keys only.** Each profile owns an explicit key set and writes only those. Anything
outside it — volume, pause rules, Spotify setup, downloads, tool paths — is left exactly as the user
had it. Reset-then-apply was rejected: it would silently discard deliberate configuration from
someone who only wanted to try a profile.

**Persistence — remember the active profile.** New `ActiveQuickStartProfile` (string, empty = none).
This buys three things:

- the page can show "PS3 Style (modified)" once any owned key drifts
- a Re-apply action that means something
- a better future meaning for per-tab Reset: *back to my profile*, not factory defaults

**Applying must be reversible.** Snapshot the owned keys before writing, and offer Undo for the rest
of the settings session. A profile that cannot be backed out of is a trap.

## Desktop vs Fullscreen is the primary axis

This is the organising idea, and it is grounded in the code rather than taste:

- `PlayOnlyOnGameSelect` is **Fullscreen-only by design** — "game music plays only on explicit
  A-button select, not D-pad navigation." A console-style profile is therefore inherently a
  Fullscreen profile.
- `MusicState` already gates playback per mode (Never / Desktop / Fullscreen / Both).
- `AutoPlayOnFirstLaunchDesktop`, `FullscreenVolumeBoostPercent` and `ShowDesktopMediaControls` are
  each mode-specific already.

So the page should present two columns rather than one flat list. A user knows which they are, and
the profiles in each column differ in ways the other mode cannot express.

### Fullscreen profiles (couch / controller)

| Profile | Idea |
|---|---|
| **Console Preview (PS3 style)** | Music on explicit select, short preview clip, snappy fades. The classic console-menu feel. |
| **Console Continuous (PS5 style)** | Same select-to-play, but full tracks and longer crossfade — less clipped, more ambient. |
| **Jukebox / Radio** | Radio plays continuously through browsing *and* game sessions. No per-game switching. |
| **Quiet Fullscreen** | Music only when a game is opened, no default music while browsing. |

### Desktop profiles (mouse / keyboard)

| Profile | Idea |
|---|---|
| **Ambient Background** | Default music runs while you work; game music on selection; media controls visible. |
| **On-Demand** | Nothing auto-plays. Music starts when you press play. For users who find UPS too eager. |
| **Jukebox / Radio** | Same as its Fullscreen sibling — radio is mode-agnostic. |
| **Full Experience** | Everything on: per-game music, default music, visualizer, live effects. A demo of what UPS does. |

`Jukebox / Radio` deliberately appears in both columns with the same key set — radio genuinely does
not care about mode, and hiding it from one column would be arbitrary.

## Key sets — TO BE AGREED

Values below are a proposal, not a decision. Everything here already exists as a setting; the
question is only what each profile should set it to.

### Console Preview (PS3 style) — Fullscreen

| Setting | Value | Why |
|---|---|---|
| `MusicState` | `FullscreenOnly` | it is a Fullscreen profile |
| `PlayOnlyOnGameSelect` | `true` | the defining behaviour — select, don't browse-play |
| `EnablePreviewMode` | `true` | the 30-second-clip feel |
| `PreviewDuration` | 30s | |
| `FadeInDuration` / `FadeOutDuration` | short | snappy, console-menu transitions |
| `EnableDefaultMusic` | `true` | something plays while browsing |
| `RandomizeOnEverySelect` | `true` | a different track each time you open a game |
| `StopAfterSongEnds` | `false` | |
| `RadioModeEnabled` | `false` | radio would override per-game music |

### Console Continuous (PS5 style) — Fullscreen

As above, except `EnablePreviewMode = false` and longer fades. Full tracks rather than clips.

### Jukebox / Radio — both modes

| Setting | Value |
|---|---|
| `RadioModeEnabled` | `true` |
| `RadioPlaysThroughGames` | `true` |
| `PlayOnlyOnGameSelect` | `false` |
| `EnableDefaultMusic` | `false` |

### Quiet Fullscreen / On-Demand Desktop

| Setting | Value |
|---|---|
| `EnableDefaultMusic` | `false` |
| `RadioModeEnabled` | `false` |
| `AutoPlayOnFirstLaunchDesktop` | `false` (Desktop variant) |
| `PlayOnlyOnGameSelect` | `true` (Fullscreen variant) |

### Ambient Background — Desktop

| Setting | Value |
|---|---|
| `MusicState` | `DesktopOnly` or `Always` |
| `EnableDefaultMusic` | `true` |
| `DefaultMusicContinueSameSong` | `true` |
| `ShowDesktopMediaControls` | `true` |
| `EnablePreviewMode` | `false` |

### Full Experience — Desktop

Everything above plus `ShowSpectrumVisualizer` and live effects on. Note this forces the NAudio
backend, which is a real consequence worth stating in the UI rather than applying silently.

## Open questions

1. **Does a profile own `MusicVolume`?** Leaning no — volume is personal and mode-independent, and
   overwriting it is the most annoying thing a profile could do.
2. **Should Full Experience be a profile at all**, given it forces a backend switch? It might belong
   as a "try everything" button with an explicit warning rather than a peer of the others.
3. **Where does the page live** — a Quick Start tab, or a panel on About (the new first tab)?
4. **First-run behaviour.** Offer Quick Start automatically on first install? Powerful, but it must be
   dismissible and must never apply anything without a click.

## What this does not solve

Sectioning the settings tabs tidied the UI but did not reduce how many tabs a new user must visit to
get a working setup. Profiles are the part that actually addresses that. They do not, however, make
the settings surface smaller — 246 persisted settings remain, and that is a separate problem.
