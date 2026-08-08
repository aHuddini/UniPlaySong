# Performance & Maintainability Backlog

Work queue for the **`PerfOptimization`** branch. That branch exists to carry optimization,
refactoring, and test-coverage work that is not tied to a feature release; it pulls from `dev`
whenever `dev` gains features, so it never drifts far from shipping code.

```bash
git switch PerfOptimization
git fetch origin && git merge origin/dev    # merge, not rebase — the branch is pushed/shared
```

Every figure below was measured against the codebase, not estimated. Where a claim was verified by
experiment the method is stated, so a future session can re-check rather than re-derive.

**Last verified:** 2026-08-08, at `dev` = `72eaf8c` (v1.7.1).

---

## Completed in v1.7.1

Kept for context — these came out of the same survey and are already on `dev`.

- Decoder build moved off the UI thread, on both the fade-out and the no-fade start paths
  (a field log measured `Load: 192ms total (Reader=191)`).
- Fade timers dropped from `DispatcherPriority.Normal` (9) to `Render`/`Input`, so they stop
  preempting drawing and controller input during every fade.
- Orphaned `MusicPlaybackService` on backend swap — `Shutdown()` now stops its four timers.
- Logging gated behind Enable Debug Logging (`extension.log` 580 → 372 sites, errors only).
- Two lowest-coupling regions lifted out of `UniPlaySong.cs` (6,598 → 6,242 lines).

---

## 1. Core playback engine has no test coverage — HIGHEST VALUE

**Status:** open. **Risk:** low (no behaviour change). **Payoff:** high.

All test files cover v1.5.8+ work — Spotify, radio, active-media, sleep. **Nothing tests
`MusicPlaybackService`'s pause-source logic, `MusicFader`, or either audio backend.** The few that
reference them do so through mocks, for Spotify's benefit.

The correlation is the argument: the parts extracted into pure policy classes —
`RadioPlayThroughPolicy`, `RadioGameSelectPolicy`, `SpotifyRadioDecision`, and now
`SongPoolProvider` — are tested, and those are the parts that have not regressed. The untested
parts produced:

- the `IsPlaying`-means-audible trap (`IMusicPlayer.IsActive` includes paused-mid-playback)
- the `MusicFader` double-advance (song starts then instantly ends)
- `ClearAllPauseSources` preservation-set bugs
- the pausing regression that forced the 1.7.0 revert and incremental re-land

A live example from this work: during the v1.7.1 orphan fix, an added
`if (ReferenceEquals(...)) return;` guard in `SongMetadataService.ResubscribeToService` broke
`NowPlayingPublisherTests.Refresh_GameMusicNoEmbeddedArt_FallsBackToGameCover` — that method
doubles as the "refresh cached info" entry point. The suite caught it immediately. That is exactly
the class of mistake the playback engine currently has no net for.

**Approach.** Lift the pause-source decision into a pure evaluator with no `IMusicPlayer` and no
SDK dependency — `ShouldPlayMusic`, the `PreservedOnClear` semantics, add/remove behaviour — and
test it. No behaviour change, mechanical extraction, same pattern that already works here.

Invariants worth pinning down in tests first:

- `IMusicPlayer.IsActive` means "playing **or** paused-mid-playback" on every backend. Resume-vs-restart
  branching depends on it, and inferring "audibly playing" from it is what caused the v1.7.0 regression.
- `PreservedOnClear` currently holds FocusLoss, Minimized, SystemTray, Manual, ExternalAudio, Idle,
  Video, ThemeOverlay, Dashboard, SystemLock.
- `ShouldPlayMusic()` deliberately does **not** check `VideoIsPlaying`/`ThemeOverlayActive` — the
  pause sources own those, and `HandleVideoStateChange`/`HandleThemeOverlayChange` own their lifecycles.

---

## 2. `UniPlaySong.cs` is still 6,242 lines

**Status:** partially done. **Risk:** low for the listed region, rising sharply after.

Two regions were lifted in v1.7.1. Candidates were ranked by how many plugin fields each region
reaches into, not by line count — that ranking is what made "Audio Normalization looks small" a
trap (233 lines but 9 fields across 13 methods; it is menu glue, not a service).

| Region | Lines | Coupling | Verdict |
|---|---|---|---|
| Settings & Menus | 1,368 | high | leave |
| Playnite Events | 1,010 | very high — the real tangle | leave for last |
| Initialization | 989 | very high (composition root) | leave |
| **Cleanup Operations** | **553** | **self-contained** | **next candidate** |
| Audio Normalization | 233 | 9 fields / 13 methods — menu glue | not worth it |

**Next:** Cleanup Operations. Re-measure its field coupling before starting; the earlier survey
sampled it only shallowly.

---

## 3. Settings: 4,237 lines, 286 hand-written `OnPropertyChanged` properties

**Status:** open. **Risk:** medium — touches a serialized type.

The real cost is not verbosity, it is the three-places rule: changing a default means updating the
backing field, the per-tab Reset handler, and verifying the global reset. That is a standing
correctness hazard, already written down as a project convention because it has bitten before.

**Safe subset only:** make the **reset** paths attribute-driven (`[Default(...)]` + reflection),
leaving serialization completely untouched. Three places collapse to one.

**Do not** restructure the properties themselves. Playnite persists settings as JSON and — verified
from an on-disk file — **serializes enums as integers**. Any reordering silently reinterprets saved
values. This bit the FullReel sibling project: appending `P2160`/`P1440` to a `VideoQuality` enum
was safe, inserting them in visual order would have turned every saved `1080p` into `2160p`.

---

## 4. Remaining cold `Load()` on the UI thread

**Status:** open. **Risk:** medium.

`ResumeDefaultMusic` at `src/Services/MusicPlaybackService.cs:648` still calls `_musicPlayer.Load()`
synchronously on the UI thread. It was deliberately left out of the v1.7.1 off-thread work: it did
not appear as a hotspot in the field log, and it carries seek-position (`_defaultMusicPausedOnTime`)
and pause-source interplay that makes deferring it genuinely riskier than the paths that were moved.

Worth doing only if it shows up hot in a real log. The `_playbackGeneration` token added in v1.7.1
is the mechanism to reuse.

---

## 5. Controller/desktop dialog pairs have diverged — RECOMMEND HOLD

**Status:** open, deliberately deferred.

| Pair | Controller | Desktop |
|---|---|---|
| Amplify | 1,036 | 634 |
| WaveformTrim | 1,059 | 625 |

~1,500 duplicated lines, sharing only `UserControl` — no common base, drifting independently.

**Hold.** It is UI (higher regression risk, lower payoff than 1–4), and the controller-SDK migration
in `docs/archive/plans/2026-03-28-controller-sdk-migration.md` would change the target shape.
Consolidating now means doing it twice.

---

## 6. Unaudited: 182 empty `catch { }` blocks

**Status:** not investigated.

Many are legitimate cleanup guards. Nobody has read them, so there is no claim here either way —
listed so the number is not rediscovered and mistaken for a finding.

---

## Checked and found FINE — do not re-flag

Each of these looked alarming in a survey and turned out to be correct. Recorded so a future pass
does not spend time re-deriving them.

- **`async void`** — 11 non-event-handler instances. All are body-wrapped in `catch (Exception ex)`,
  so the usual "unhandled exception kills the process" hazard does not apply.
- **`.Wait()` / `.Result`** — the conspicuous `StartBatchDownloadAsync(...).Wait()` at
  `src/Menus/GameMenuHandler.cs:921` runs inside `ActivateGlobalProgress`, which Playnite executes
  on a background thread. Standard pattern, no UI deadlock.
- **`OnNeedsPlayerSwitch` subscribed at three sites** — each targets a freshly constructed service,
  so no handler pile-up on one instance.
- **`NAudioMusicPlayer` fade timing** — ramps per-sample on the audio thread via
  `SmoothVolumeSampleProvider`; it has no dispatcher timer and needed no priority change.

## Out of scope

- **`MediaElementsMonitor`'s timer (`src/Monitors/MediaElementsMonitor.cs:58`)** — the video
  detector. Owner instruction: do not touch it at all. It detects videos from another plugin, and
  reducing its 100ms interval makes UPS behave erratically.

---

## Method notes

Things that made the difference between a real finding and a plausible-sounding one:

- **Measure coupling, not size.** Region line counts ranked the extraction candidates wrongly.
- **Verify CLI flags and tool behaviour against the binary**, never from memory.
- **A grep is not a finding.** `docs/` "clutter" turned out to be gitignored and untracked; a
  broken-link sweep produced false positives by matching `docs/` inside `dev_docs/`; a `+=` count
  included string concatenation. Confirm the mechanism before reporting it.
- **Run the tests before believing a refactor is behaviour-preserving.**
