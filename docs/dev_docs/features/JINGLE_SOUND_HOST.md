# Out-of-Process Jingle Sound Host

**Status:** stage 1 built (seam + null host); stages 2-3 not started
**Target:** 1.8.6 or later
**Scope:** achievement sounds only — see *Not in scope*
**Requested by:** the PlayniteAchievements developer (justin-delano)

---

## Why

PlayniteAchievements records achievement unlocks and re-times the chime in post: the
notification fires some seconds after Steam's true unlock timestamp, so the real-time
chime is removed from the recording and a fresh copy is placed at the true time.

It already has the machinery for this. `ProcessLoopbackCapture` uses the Windows
Application Loopback API (`ActivateAudioInterfaceAsync` + `PROCESS_LOOPBACK`) with both
`IncludeTargetProcessTree` and `ExcludeTargetProcessTree`, and `AudioLoopbackRecorder`
captures a **Playnite-tree sidecar** next to the main track purely to isolate the chime.
Its own `PlayniteChimeCaptureMode` names the problem exactly:

> `Clean` | `CancelGameReference` — *"A game launched beneath Playnite requires a
> simultaneous game-only reference to be cancelled from that sidecar; a separate process
> tree is already clean."*

`Clean` is what happens when the game runs in its own process tree. **`CancelGameReference`
is the degraded path**: emulators launch *under* Playnite, so the Playnite-tree sidecar
contains chime **plus** emulator audio, and the chime has to be recovered by cancellation.
That fails whenever game audio correlates with the chime — his reported ~90% ceiling.

UniPlaySong plays the chime from Playnite's PID. If it played from **its own process
tree**, his sidecar would come back `Clean` in the emulator case too, and the cancellation
step disappears.

**The requirement is a PID, not a device.** Device routing and OBS-style exclusion do not
help — he is not using OBS, and his capture is process-tree based.

## Why only the jingle path moves

UniPlaySong's music player cannot move out of process without a rewrite: Live Effects run
on the sample stream, `VisualizationDataProvider` taps FFT off it for the desktop
visualizer, Spotify Live Effects captures and replays through the same mixer, and the
fader does per-sample ramping with position preservation. Every one of those becomes an
IPC latency and sync problem.

The achievement-sound path is the opposite, and `JingleService.PlayExternalSound` already
documents why:

- no fader, no pause/resume — *"these fire over a running game where UPS music is already paused"*
- *"no viz-provider save/restore — no Live Effects / visualizer involvement by design"*
- *"own player + own MediaEnded, fully isolated"*

It is already a stateless fire-and-forget emission that happens to share Playnite's PID.
It needs only `Load` / `Volume` / `Play` / `MediaEnded` — the subset a helper can satisfy.

**Nothing existing is ported.** This is a third, additive path.

---

## Design

### Three emission paths in `JingleService`

| Path | Player | Used for |
|---|---|---|
| `_jinglePlayer` | NAudio or SDL2, effects optional | completion / abandoned jingle |
| `_externalPlayer` | SDL2 lightweight | achievement sounds — **unchanged** |
| `_helperPlayer` *(new)* | out-of-process `UpsSound.exe` | **achievement sounds only**, when capture mode is on |

### Injection point

`PlayExternalSound` is **shared**: achievement rarities, the achievement master sound, the
URI-triggered achievement event *and* `ControllerDetected` all route through it. Only the
achievement events belong on the helper — a controller-connected chirp has nothing to do
with unlock capture and must not move.

So the branch is **event-aware**, taken at the two achievement call sites rather than
inside `PlayExternalSound`:

```csharp
// rarity path
if (!string.IsNullOrEmpty(rarityPath))
{
    if (_soundHost?.TryPlay(rarityPath, VolumeFor(settings)) != true)
        PlayExternalSound(rarityPath, settings);
    return;
}

// master fallback path — same shape
```

`PlayExternalSound` itself is untouched, so `ControllerDetected` and every non-achievement
caller keep today's behaviour by construction rather than by a runtime check.

Delete the two `TryPlay` guards and UniPlaySong behaves exactly as it does now. That is the
whole blast radius in existing code.

### The helper: `UpsSound.exe`

Deliberately dumb — it must be too boring to develop bugs of its own.

- Own csproj, no reference to `UniPlaySong.dll`, ~100 lines
- NAudio (`WasapiOut` on the default endpoint) or raw WASAPI
- Reads newline-delimited commands from **stdin**, writes acks to **stdout**
- **Resident** for the session; holds its output device open and idles between sounds, so no
  chime pays a device-open cost and the PID never changes

**Protocol** (text, one command per line — no serializer, no versioning ceremony):

```
-> ready                          (helper, on startup, once)
<- play <volume 0.000-1.000> <absolute path>
-> ok <id>                        (accepted, playing)
-> done <id>                      (finished; drives MediaEnded)
-> err <id> <reason>              (failed — caller falls back in-process)
<- stop                           (halt current sound)
<- quit                           (exit cleanly)
```

Paths may contain spaces; volume comes first so the path is the unbounded tail.

### Packaging

`package_extension.ps1` copies loose files into the package (no embedded-resource
extraction is used today — see the SDL2 DLL block). `UpsSound.exe` ships **beside**
`UniPlaySong.dll` and needs no extraction step:

- add the helper csproj to the solution
- copy its build output in the packaging script next to the SDL2 DLL copy
- fail the package loudly if it is missing, the way the script already does for `UniPlaySong.dll`

### Lifecycle

- **Start: resident from startup — decided, not a preference.** Spawned at
  `OnApplicationStarted` when the setting is on, and kept alive for the session. Matches the
  existing prewarm pattern (`[Jingle] Prewarmed external player`).

  Not lazy-on-first-sound, for two reasons. The PID must exist **before** the first unlock,
  because the consumer attaches `ProcessLoopbackCapture` to it — a PID that appears at the
  moment of the first chime is a race he would have to poll around, and the first unlock is
  exactly the one he would lose. And a resident helper has its audio device already open, so
  no chime pays the ~70 ms cold start.

  Consequence to accept: the helper runs for the whole session even if no achievement ever
  fires. That is the point — a stable PID from launch is the feature.
- **Spawn:** `UseShellExecute = false`, `RedirectStandardInput/Output = true`,
  **`CreateNoWindow = true`** — without it a console flashes on every launch.
- **Death with parent:** assign to a Job Object with
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` so the helper dies even if Playnite crashes.
  A parent-PID watchdog inside the helper is the simpler fallback. Orphaned audio
  processes would be a serious bug.
- **Restart:** if it exits unexpectedly, restart **once**. On a second failure, fall back
  in-process for the rest of the session and warn.
- **Stop:** `quit` on `OnApplicationStopped`, alongside existing teardown.

### Failure rules — these matter more than the feature

1. **Never silent.** Helper missing, quarantined, crashed, or no `ok` within a short
   timeout (~250 ms) → play in-process immediately. A capture feature must never cost a
   normal user their achievement sound.
2. **Say so once.** On fallback, raise a one-time notification naming the reason and
   withdraw it when the helper works again — the pattern established for the zeroed
   Background Volume in 1.8.5. Silent degradation is the bug class that release existed
   to remove.
3. **Off by default, out of the way.** Advanced, described as a capture/streaming
   integration rather than an audio preference. A user must not arrive here by accident.
4. **Machine-specific.** `NeverReset`, and excluded from any settings export/profile — a
   helper toggle has no business travelling between machines.

### API for PlayniteAchievements

One addition beside the existing `ResolveAchievementSound` reflection surface:

```csharp
public int GetSoundHostProcessId()   // helper PID, or 0 when not running
```

Stable for the session because the helper is resident, so it can be read once at startup
rather than polled. Returns 0 when the setting is off or the helper failed to start — the
consumer should treat 0 as "capture mode unavailable, fall back to the sidecar path".

He points `ProcessLoopbackCapture` at it with `IncludeTargetProcessTree` and gets a clean
chime stem in every case, emulators included. Nothing else on his side changes — it is the
`Clean` path he already wrote.

---

## Risks

- **AV false positives.** An unsigned exe spawning from the extensions folder is exactly
  the shape SmartScreen and third-party AV flag, and this project already fights a Defender
  race during packaging. Mitigated by rule 1: quarantined helper means today's behaviour
  plus a notice, never silence.
- **A second binary to support.** Worst case is a fallback to the current code path.
- **Latency.** Resident helper + prewarm means the first chime does not pay device open.

## Build order

1. **Seam + fallback**, with a stub host that always returns false. Proves the branch is
   inert and that existing behaviour is untouched.
2. **Helper exe + IPC**, behind the setting. Decisions taken: bundle NAudio into the
   helper rather than share the packaged copy (independence is the point), and accept
   SmartScreen risk unsigned unless a free certificate turns up — the never-silent
   fallback is what makes that acceptable.
3. **PID API + packaging.**

Stage 1 is independently safe to ship; stages 2–3 can be dropped without unwinding
anything.

## Not in scope

**This is for achievement sounds and the PlayniteAchievements integration only.** It is not
a general "play UniPlaySong audio elsewhere" feature, and the scope should be defended:

- **Completion and abandoned jingles stay in process.** They are celebration audio for the
  user, not capture material, and they may carry Live Effects.
- **`ControllerDetected` stays in process**, despite sharing `PlayExternalSound`. That is
  why the branch sits at the achievement call sites rather than inside the shared method.
- **Music, Live Effects, the visualizer and Spotify capture stay in process.** Moving them
  is a rewrite, not a port — see "Why only the jingle path moves".
- **No output-device routing.** Solves an OBS-shaped problem this consumer does not have.

If a second consumer ever wants out-of-process audio, revisit the shape then — do not widen
this one on speculation.
