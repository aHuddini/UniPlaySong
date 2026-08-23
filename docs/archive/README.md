# Archive

Historical documents kept for provenance. **Nothing here is maintained** — it describes the state
of the project at the time it was written, not current behaviour. Treat it as a record of *why*
a decision was made, and verify anything actionable against the code.

For current documentation see [docs/dev_docs/](../dev_docs/).

## Contents

- **`plans/`** — design and implementation plans for features that have since shipped (Feb–Jun 2026):
  icon glow, taskbar colour, external control, localization, bulk audio conversion, the controller
  SDK migration, play-on-select, NAudio smooth volume, and others. Most are design/plan pairs.
- **`REPAIR_SESSION_NOTES.md`** — the February 2026 repair sessions, including a rollback to a known
  good baseline. References several commits that were discarded, so its SHAs will not resolve.
- **`spikes/spotloop_spike.cpp`** — the July 2026 proof that Windows Process Loopback Capture could
  pull Spotify's isolated PCM with no virtual cable, driver or admin. Superseded by the shipping
  shim at `native/SpotifyLoopback/SpotifyLoopbackCapture.cpp`; kept because it is the smallest
  readable statement of the capture dance. The feasibility write-up it belongs to is still current
  and stays in `dev_docs/`.

Newer working plans are local-only (gitignored under `docs/plans/` and `docs/superpowers/`); these
were tracked before that rule existed and are retained rather than deleted.
