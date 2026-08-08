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

## Profiles are differentiated by PLAYBACK BEHAVIOUR, not by console branding

The names are a shorthand for a *way music behaves*, and that is what distinguishes one profile from
the next. "PS3 style" means hover-to-play — move the highlight, music follows. That is a different
profile from select-to-play, where music only starts once you drill into a game's details view.
Branding is the label; the trigger is the substance.

Reading the settings, the behaviour splits along two independent dimensions:

**When does music trigger?**

| Setting | Behaviour |
|---|---|
| (none set) | **Hover** — music follows the highlight as you browse. The PS3 feel. |
| `PlayOnlyOnGameSelect` | **Select** — music starts only on explicit A-button select / details view. Default music plays while browsing. Fullscreen only. |
| `EnablePreviewMode` + `PreviewDuration` | **Clip** — plays a fixed-length snippet rather than the whole track. Composes with either of the above. |

**Which games qualify?**

| Setting | Filter |
|---|---|
| `MusicOnlyForInstalledGames` | installed games only; the rest fall through to default music |
| `NostalgiaMode` + `NostalgiaStatusIds` | only games with chosen completion statuses |
| `GamePropFilterEnabled` + platform/genre/source ids | only games matching those properties |
| `FilterModeEnabled` | only while a named Playnite filter preset is active |

These are orthogonal — any trigger combines with any qualifier — which is exactly why the settings
UI makes them hard to discover. A profile's job is to pick a sensible pairing and name it.

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

| Profile | Trigger | Qualifier |
|---|---|---|
| **Hover Preview (PS3 style)** | Hover — music follows the highlight | all games |
| **Select to Play** | Select — only on details view | all games |
| **Hover Preview, Installed Only** | Hover | installed games only |
| **Select to Play, Installed Only** | Select | installed games only |
| **Jukebox / Radio** | radio plays through browsing and games | n/a |
| **Quiet** | Select, no default music while browsing | all games |

The first four are the grid that matters: two triggers × two qualifiers. That pairing is the thing
users currently have to discover by finding two unrelated checkboxes on two different tabs.

### Desktop profiles (mouse / keyboard)

| Profile | Trigger | Qualifier |
|---|---|---|
| **Hover Preview** | Hover — music follows selection | all games |
| **Hover Preview, Installed Only** | Hover | installed games only |
| **Ambient Background** | default music runs; game music on selection | all games |
| **On-Demand** | nothing auto-plays; press play to start | all games |
| **Jukebox / Radio** | radio plays continuously | n/a |

Desktop has no **Select to Play** variants: `PlayOnlyOnGameSelect` is Fullscreen-only, so the trigger
does not exist there. That asymmetry is real and the page should not pretend otherwise.

`Jukebox / Radio` deliberately appears in both columns with the same key set — radio genuinely does
not care about mode, and hiding it from one column would be arbitrary.

**Clip vs full track** is deliberately *not* a separate profile in either column. It composes with
every trigger, so it belongs as a toggle on the Quick Start page ("play a short preview clip")
rather than doubling the profile count.

## Key sets — TO BE AGREED

Values below are a proposal, not a decision. Everything here already exists as a setting; the
question is only what each profile should set it to.

Note that **no profile sets `MusicVolume`** — decided, see Resolved questions.

### Hover Preview (PS3 style) — Fullscreen

| Setting | Value | Why |
|---|---|---|
| `PlayOnlyOnGameSelect` | `false` | the defining behaviour — music follows the highlight |
| `MusicOnlyForInstalledGames` | `false` | every game plays |
| `EnableDefaultMusic` | `true` | something plays where a game has no music |
| `RandomizeOnEverySelect` | `true` | a different track each time you land on a game |
| `FadeInDuration` / `FadeOutDuration` | short | snappy, console-menu transitions |
| `StopAfterSongEnds` | `false` | |
| `RadioModeEnabled` | `false` | radio would override per-game music |

### Select to Play — Fullscreen

Identical, except `PlayOnlyOnGameSelect = true`. Browsing plays default music; a game's own music
starts when you open it.

### …, Installed Only — both triggers

Either of the two above plus `MusicOnlyForInstalledGames = true`. Uninstalled games fall through to
default music.

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

## Resolved questions

1. **`MusicVolume` is never owned by a profile.** Volume is personal and mode-independent; a profile
   overwriting it would be the most irritating thing it could do.
2. **Full Experience is not a peer profile.** It forces the NAudio backend (visualizer and live
   effects both require it), which is too large a side effect to apply from a one-click tile
   alongside behavioural profiles. If it ships at all it is a separate "try everything" action with
   an explicit warning about the backend switch.
3. **Its own Quick Start tab**, not a panel on About.
4. **No first-run prompt.** Quick Start is discoverable, never automatic.

## Still open

- **Tab position.** Quick Start presumably belongs at or near the front, which competes with About
  and Setup for the first slot. Current order starts About, Setup, General.
- **Whether the clip toggle lives on the page** or is left to the Playback tab. Listed as a page-level
  toggle above, but it is the one piece of the design not yet argued through.

## What this does not solve

Sectioning the settings tabs tidied the UI but did not reduce how many tabs a new user must visit to
get a working setup. Profiles are the part that actually addresses that. They do not, however, make
the settings surface smaller — 246 persisted settings remain, and that is a separate problem.
