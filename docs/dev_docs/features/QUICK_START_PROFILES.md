# Quick Start Profiles — design

A Quick Start page that configures UPS for a way of using it, in one click, instead of making a new
user visit six tabs to discover what the plugin can do. Landing page for the settings UI.

**Status:** BUILT, shipped in 1.7.1.

Lives in `Services/QuickStartProfiles.cs` (the catalogue), `Services/QuickStartService.cs` (apply /
undo / drift), the Quick Start tab in `UniPlaySongSettingsView.xaml`, and
`tests/Services/QuickStartServiceTests.cs`.

Tile names and values changed several times during the build, so **the catalogue is authoritative** —
this document explains the reasoning, not the current values. Where the two disagree, the code wins
and this file needs a correction.

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

**Every profile turns default music ON — including Jukebox.** An earlier draft had Jukebox switch it
off, reasoning that radio is already the continuous bed and two beds would layer. That was wrong:
`StartRadioPlayback` *returns without playing* when its pool is empty (logged as "RadioMode: pool
empty for source X"). A user with nothing downloaded yet, an empty library under `FullLibrary`, or
`CustomFolder` with no folder chosen would get silence and no indication why. The two do not double
up in practice — the radio owns playback whenever its pool has anything in it, so the fallback only
surfaces when the radio genuinely cannot play.

**And the fallback itself must be playable.** Four default-music sources need something the user has
to supply first — `CustomFile` (a path), `CustomFolder` (a path), `CustomRotation` (a game list),
`CompletionStatusPool` (a status list). Applying a profile while one of those is unconfigured would
enable a fallback that is itself empty: the same silent failure one layer down. So apply checks, and
falls back to `BundledPreset`, which ships with the plugin and is the one source guaranteed to
produce sound on a fresh install. A source the user *has* configured is never touched, and the
sources needing no setup (`RandomGame`, `ActiveThemeMusic`, `DeferToTrailerAudio`, `Spotify`) are
left alone.

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

### Keep the tile count small

An earlier draft had eight tiles per column by making a separate profile for every
trigger x qualifier pairing. That reproduced the settings UI's problem — too many near-identical
choices — in a page whose whole purpose is to remove it. A user cannot meaningfully choose between
"Hover Preview" and "Hover Preview, Installed Only" on a tile.

**Rule: a tile is a distinct way of listening, not a combination of checkboxes.** Variations that
differ by one setting are a *checkbox on the page*, not a tile of their own.

### Fullscreen profiles (couch / controller)

| Profile | What it does |
|---|---|
| **Hover Preview (PS3 style)** | Music follows the highlight as you browse. |
| **Select to Play** | Browsing stays on default music; a game's own music starts when you open it. |
| **Library Background (Default Music), Game Music In Details** | Bundled ambient track while browsing; the game's own music in the details view. |
| **Radio Mode (Random Game Music)** | One continuous mix instead of per-game music. Library or Spotify. |

### Desktop profiles (mouse / keyboard)

| Profile | What it does |
|---|---|
| **Hover Preview (PS3 style)** | Music follows your selection. |
| **Background Mode (Default Music)** | One bundled ambient track the whole time; game music OFF so nothing interrupts it. |
| **Radio Mode (Random Game Music)** | One continuous mix. Library or Spotify. |

Five tiles per mode. Desktop has no **Select to Play** because `PlayOnlyOnGameSelect` is Fullscreen-only —
because `PlayOnlyOnGameSelect` is Fullscreen-only —
a real asymmetry the page should show rather than fake.

### Reset to my settings

Alongside Undo. Undo steps back exactly one apply; this steps back to the settings the user had
**before the first profile of the session**, and clears the active profile so no tile reads as
current. Trying three tiles in a row still returns to where they started rather than to whichever
tile they tried second.

It restores across the *union* of every key any profile can write, not just the last profile's keys.
Without that, Background Mode's `EnableMusic = false` would survive a reset triggered from a
different tile.

Deliberately **not** a factory reset: it restores the user's own values, and touches only keys
profiles can write. Volume, tool paths and pause rules are as untouched here as during an apply.

### The page-level checkboxes

These apply on top of whichever tile is chosen, so they are checkboxes rather than more tiles:

- **Only play music for installed games** — the `MusicOnlyForInstalledGames` qualifier. Ignored by Jukebox: in the radio branch it makes radio yield to installed games with music, which would break the continuous mix.
- **Keep playing during games** — `RadioPlaysThroughGames`, only meaningful with the Jukebox tile
- **Use Spotify as the Jukebox source** — `RadioMusicSource`, likewise Jukebox-only
- **Add reverb** — `LiveEffectsEnabled` + `StylePreset.HuddiniRehearsal`, the "wide stereo, rich
  reverb, live rehearsal room" preset. UPS ships no "Concert"; Rehearsal is the closest to that
  intent. Unchecking owns only the master toggle, so a user's own preset survives rather than being
  reset. The UI states that this forces the NAudio backend rather than switching engines silently.

Everything else — completion-status filters, property filters, Preview Mode, volume, other effects —
stays on its own tab. Quick Start gets you a working setup; it is not a second settings screen.

### Dropped from the earlier draft

**Quiet / On-Demand** was "the same as Select to Play but with default music off", which is one
checkbox, not a way of listening. **Full Experience** is gone for the reason already recorded: it
silently forces the NAudio backend. **Spotify Fills the Gaps** is not its own tile — it is the
Spotify source choice on Hover/Select, since it means "per-game music as normal, Spotify in the
gaps", which is the fallback dimension rather than a listening mode.

**Preview Mode is not a profile and does not move.** It already exists as its own section on the
Playback tab ("Enable Preview Mode" — game music restarts after a set duration, and it explicitly
does not affect default music). It composes with every trigger, so making profiles for
clip-vs-full-track would double the tile count to express one checkbox the user can already find.
No profile owns it.

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

### Radio Mode (Random Game Music) — both modes

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

### Background Mode (Default Music) — Desktop

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

5. **Tab position: third**, after About and Setup — orient, set up tools, then pick how it plays.
6. **Preview Mode stays on the Playback tab.** It already exists there and no profile owns it.

## Still open

Nothing blocking. Naming is settled: the tiles carry the "(PS3 style)" shorthand in both modes,
because it is what makes the behaviour click for a reader — a hover-to-play console menu is a thing
people have used, where "Hover Preview" alone is abstract. The persisted ids (fs-hover, dt-hover)
deliberately do NOT contain it, so the label can change later without orphaning installs.

## What this does not solve

Sectioning the settings tabs tidied the UI but did not reduce how many tabs a new user must visit to
get a working setup. Profiles are the part that actually addresses that. They do not, however, make
the settings surface smaller — 246 persisted settings remain, and that is a separate problem.
