# Docs Reorganization — To Do

**Status:** not started (deliberately)
**Created:** 2026-09-02

Five commits on `feat/settings-two-tier-nav` did this work between v1.7.x and
v1.8.0 and were never merged. The branch was deleted on 2026-09-02 rather than
merged: the code moved on considerably since, and rebasing a docs reshuffle onto
a changed tree risks more than redoing it deliberately.

**Redo the intent, not the diffs.** Every path below should be re-checked against
the current tree before acting — several of these files have moved, grown or been
superseded since.

---

## 1. Split `docs/dev_docs/` into subfolders

Flat `dev_docs/` mixes feature deep-dives with cross-cutting reference. Group the
per-feature docs under `features/`:

- `NAUDIO_PIPELINE.md`
- `SPOTIFY_INTEGRATION.md`
- `QUICK_START_PROFILES.md`
- `ACHIEVEMENT_SOUND_INTEGRATION.md`
- `CHIPTUNE_GME_DLL_BUILD.md` (was `GME_BUILD.md`)
- `VISUALIZER_DYNAMIC_COLOR_ALGORITHM.md`

Leave the cross-cutting ones at the top level: `ARCHITECTURE.md`,
`TECHNICAL_REFERENCE.md`, `DEPENDENCIES.md`, `BUILD_INSTRUCTIONS.md`,
`SUPPORTED_FILE_FORMATS.md`, `README.md`.

A `plugin/` folder was tried and reverted in the same branch — one document did
not justify a folder. Don't repeat that.

## 2. Move the outward-facing doc out of `dev_docs/`

`THEME_INTEGRATION_GUIDE.md` → `docs/Theme Support/`. It is written for people
outside the project, and `dev_docs/README.md` opens with "everything here
describes UPS internals". Note the `%20` needed in links for the space in the
folder name.

## 3. Archive superseded planning docs

`docs/archive/` for backlogs and plans that are no longer reference:

- `POTENTIAL_ISSUES.md` — already carried a "written around v1.4.x, verify before
  acting" caveat
- `PERF_OPTIMIZATION_PLAN.md`
- `docs/dev_docs/spikes/spotloop_spike.cpp` → `docs/archive/spikes/` — the
  throwaway C++/WinRT console that proved process-loopback capture. Never in any
  csproj, superseded by `native/SpotifyLoopback/SpotifyLoopbackCapture.cpp`.
  **Archive, don't delete:** the Spotify Live Effects feasibility doc cites it as
  the evidence behind its "PROVEN" claim.

In `dev_docs/README.md`, archived docs come out of the feature table (an archive
is not feature reference) and get described under "Other locations".

## 4. Gitignore proposals

`git rm --cached` `docs/dev_docs/proposals/`, keep the files on disk, and ignore
`**/proposals/` — the glob rather than the one path, so the convention holds for
any proposals folder anywhere. Planning docs stay local by default; nothing
half-decided gets published. Record the rule in `dev_docs/README.md` so it is
discoverable and not merely enforced by gitignore.

## 5. Fix every inbound link the moves break

Known to have referenced moved files at the time: `dev_docs/README.md`,
`TECHNICAL_REFERENCE.md`, `SUPPORTED_FILE_FORMATS.md`, `ARCHITECTURE.md`,
`DEPENDENCIES.md`, `docs/EXTERNAL_CONTROL.md`, `NOTICES.txt`, root `README.md`,
`docs/archive/POTENTIAL_ISSUES.md`, the Spotify feasibility doc, and
`docs/plans/2026-02-22-naudio-smooth-volume-design.md`.

Re-derive that list rather than trusting it, then verify every relative `.md`
link still resolves before committing. Use `git mv` throughout so history
follows the files.

---

## Not carried over

**Mike Aniki's two-tier settings proposal** (`16e5931`) was the design input for
the settings rework. That work **shipped in v1.8.0**, so the proposal is now
history rather than a plan, and it is not being reinstated. If a record of the
original proposal is wanted, it should be written as a short retrospective note
rather than restoring a stale `.reference` XAML file.

One finding from it worth keeping, since it still describes the codebase: the
settings view hardcodes 78 colors and uses `DynamicResource` zero times. That is
house style, not a regression — but it is the reason the settings UI does not
follow the Playnite theme, and it is a separate open question.
