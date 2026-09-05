# Out-of-process helpers as a pattern

**Status:** design note. Nothing to build yet — deliberately.

The achievement sound host (`JINGLE_SOUND_HOST.md`) put a piece of UniPlaySong in its own
process. It worked, and it raised a bigger question worth writing down: should more extension
work live in helper processes that Playnite's plugin drives?

The mechanism is proven. The judgement about *when* is the part that needs recording.

---

## What the jingle host proved

~250 lines, one afternoon, and the hard parts held:

- a helper spawns invisibly (`CreateNoWindow`, WinExe so no console flashes)
- it dies reliably with its parent — job object with `KILL_ON_JOB_CLOSE`, plus a parent-pid
  watchdog as the belt
- a newline text protocol over stdin/stdout is enough; no serializer, no versioning ceremony
- a failure on the far side reads as a plain decline, so the caller carries on

None of that needed a framework.

## Where it genuinely wins

**Escaping net4.6.2 / x86.** The strongest argument, and it is about the impossible rather than
the elegant. Playnite extensions are pinned to .NET Framework 4.6.2, which closes off most of the
modern ecosystem: no `System.Text.Json`, no modern HTTP stack, no ML or Whisper bindings, no
library that assumes .NET 8. A helper can be .NET 9, Rust, Go, Python. UniPlaySong already shells
out to yt-dlp and FFmpeg for exactly this reason; the pattern makes that deliberate rather than
a workaround.

**Crash isolation.** Native code that segfaults takes a helper down, not Playnite. UniPlaySong
has real exposure here — SDL2, GME and the Spotify loopback shim all run native inside Playnite's
process today.

**A structural guarantee about the UI thread.** A separate process *cannot* block Playnite's
dispatcher. Given how many bugs in this codebase have been cross-thread or deadlock-shaped, that
is worth more than it sounds.

**Process-tree isolation for capture** — the thing the jingle host was built for.

## Where it costs more than it looks

**Every call becomes fallible.** In process, `player.Play()` works or throws. Across a pipe it can
also never answer, answer late, answer after the caller gave up, or die mid-call.
`ProcessJingleSoundHost` is ~300 lines and most of them are that: ack timeout, restart once,
permanent-failure latch, fallback. Tractable there **only because an in-process fallback exists**.
A feature with no fallback has no such safety net, and its failures are user-visible.

**Latency and conversation.** Fine for "play this file". Wrong for per-sample volume ramps, FFT at
frame rate, or position queries — which is exactly why UniPlaySong's music, Live Effects and
visualizer stay in process. Moving those is not a port, it is a rewrite into client/server, and
every existing feature becomes a sync problem.

**Distribution.** Each helper is a binary in the `.pext` that antivirus may quarantine and
SmartScreen may flag. One is a manageable risk. Five, in a plugin people install casually, is a
support burden — unsigned binaries spawning from `%AppData%` is a genuinely suspicious shape.

**Debuggability.** Attaching to a child process, correlating two log streams, and reproducing a
race across a pipe are all meaningfully harder than reading a stack trace.

## The line

Suited to work that is **stateless, coarse-grained and failure-tolerant**.

| Good fit | Poor fit |
|---|---|
| "Play this file" | Per-sample volume ramping |
| "Transcode this" | Live visualizer data |
| "Fetch and parse this" | Anything a theme binds to |
| "Capture this audio" | Anything on a hot path |
| Anything needing a modern runtime | Anything with no fallback |

**The test: what happens when the helper is not there?** If there is no good answer, the work does
not belong in one. The jingle host has a clean one — play it in Playnite — and that is the whole
reason it is safe.

## Why there is no harness yet

The obvious next step is extracting the reusable part: spawn, job-object lifetime, line protocol,
ack timeout, restart-once, fallback hook. The next helper would then be ~20 lines of glue, and
every one would inherit the never-silent discipline rather than reinventing it badly.

**Not yet.** One consumer is not a pattern, and extracting a framework from a single use is how
abstractions come out the wrong shape. `ProcessJingleSoundHost` is honestly still *coupled* to its
caller — it names "achievement sounds" in its user-facing failure message, and `TryPlay` is named
for sound rather than work. That is correct for one consumer and wrong for a harness, and the
difference is only visible once a second case exists.

Extract when there are two real ones. Candidates, in rough order of plausibility:

- **Spotify loopback capture** — already a native shim, already isolation-shaped
- **A modern-runtime transcoder** — anything wanting current FFmpeg or .NET bindings
- **Metadata or search work** — network-bound, failure-tolerant, benefits from modern HTTP

## What exists today

| Piece | Reusable? |
|---|---|
| `UpsSound.exe` | Yes in principle — it knows nothing of UniPlaySong; its whole interface is `play <volume> <path>` |
| `IJingleSoundHost` | No — named and shaped for sound |
| `ProcessJingleSoundHost` | No — spawn/lifetime/fallback logic is general, but its messaging is coupled to achievements |

The split is sound. The harness is not written, on purpose.
