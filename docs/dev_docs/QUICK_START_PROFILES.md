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

Reading the settings, the behaviour splits along three independent dimensions — trigger, qualifier
and fallback:

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

**What fills the gaps?** Default music is its own dimension, not a `true`/`false` rider on the two
above. Every qualifier works by letting non-matching games *fall through* to default music, so a
profile that sets a qualifier is implicitly making a decision about default music whether it says so
or not. The knobs:

| Setting | Meaning |
|---|---|
| `EnableDefaultMusic` | whether the fallback exists at all — off means silence in the gaps |
| `DefaultMusicSourceOption` | where it comes from: bundled preset, custom file/folder, random game, custom rotation, completion-status pool, active theme audio, defer to trailer audio, or Spotify |
| `DefaultMusicContinueSameSong` | whether it resumes where it left off or restarts on each gap |
| `RandomizeDefaultMusicOnEnd` | picks a new track when one finishes |

This matters because the two states are audibly different things. Game music is *reactive* — it
changes as you move. Default music is *continuous* — it is the bed underneath. A profile that gets
the trigger right but leaves default music wrong still feels wrong: hover-to-play with
`DefaultMusicContinueSameSong = false` restarts the bed every time you land on a game with no music,
which is exactly the stutter the setting exists to prevent.

So the three dimensions are **trigger x qualifier x fallback**, and they are orthogonal — any trigger
combines with any qualifier combines with any fallback. That is why these are hard to discover today:
they are spread across three areas of the settings UI. A profile's job is to pick a coherent triple
and name it.

**Every profile must state its fallback explicitly.** Leaving `EnableDefaultMusic` unowned would mean
the same profile behaves differently for two users, which defeats the point of a profile.

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
| **Jukebox / Radio (UPS pool)** | radio plays through browsing and games | n/a |
| **Spotify Radio** | Spotify plays continuously; UPS conducts | n/a |
| **Spotify Fills the Gaps** | per-game music as normal; Spotify covers games with none | all games |
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
| **Jukebox / Radio (UPS pool)** | radio plays continuously | n/a |
| **Spotify Radio** | Spotify plays continuously; UPS conducts | n/a |
| **Spotify Fills the Gaps** | per-game music as normal; Spotify covers games with none | all games |

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

### Jukebox / Radio (UPS pool) — both modes

Radio replaces per-game music entirely: it plays continuously from a pool rather than reacting to
selection.

| Setting | Value |
|---|---|
| `RadioModeEnabled` | `true` |
| `RadioMusicSource` | `FullLibrary` (or `CustomFolder` / `CustomRotation` / `CompletionStatusPool`) |
| `RadioPlaysThroughGames` | `true` |
| `PlayOnlyOnGameSelect` | `false` |
| `EnableDefaultMusic` | `false` — radio is already the continuous bed |

### Spotify Radio — both modes

The same shape, with Spotify as the source instead of a UPS pool. UPS conducts; Spotify plays.

| Setting | Value | Why |
|---|---|---|
| `RadioModeEnabled` | `true` | radio is the mechanism |
| `RadioMusicSource` | `Spotify` | **these two together are what `SpotifyRadioMode` means** |
| `RadioPlaysThroughGames` | `true` | keep Spotify going during a game session |
| `EnableDefaultMusic` | `false` | Spotify is the bed |

**Do not set `SpotifyRadioMode` directly** — it is `[JsonIgnore]` and read-only, derived as
`RadioModeEnabled && RadioMusicSource == Spotify`. A profile sets the two real keys; the derived
property follows.

Two things a profile cannot do, and the tile must say so rather than fail silently:

- Spotify must actually be installed and running for this to produce sound. The profile configures
  intent, not availability.
- Live effects over Spotify need the process-loopback capture path, which has an OS floor
  (Windows 10 build 19041). Not something a profile should enable blindly.

### Spotify as *default music* rather than radio

Distinct from the above and worth its own tile only if it earns one. Here Spotify fills the **gaps**
— games with no music of their own — while UPS still plays per-game music normally.

| Setting | Value |
|---|---|
| `EnableDefaultMusic` | `true` |
| `DefaultMusicSourceOption` | `Spotify` |
| `SpotifySkipOnGap` | user's choice — advance Spotify on each gap, or resume the current track |
| `RadioModeEnabled` | `false` — otherwise radio overrides per-game music entirely |

This is the pairing that most needs a profile, because getting it wrong (radio on *and* Spotify
default music) produces two competing Spotify behaviours from settings on different tabs.

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
