# Isolating UniPlaySong's achievement sound — guide for PlayniteAchievements

**UniPlaySong 1.8.6+** · API version 1

You capture audio by process tree, and UniPlaySong plays its achievement sound from Playnite's
own process. When Playnite launched the game — an emulator — the game is inside that same tree,
so your Playnite-tree sidecar carries game audio along with the chime and can only be recovered
by cancellation. That is your `CancelGameReference` path, and it fails when the two correlate.

UniPlaySong can now play that sound from **its own process** instead. Point your capture at that
pid and the sidecar is chime-only in every case, emulators included — your `Clean` path, with no
cancellation step.

---

## 1. Turn it on

**UniPlaySong Settings → Gamification → Miscellaneous → Recording →
"Play achievement sounds from a separate process"**

Off by default. Takes effect immediately — no Playnite restart. When on, UniPlaySong starts
`UpsSound.exe` at startup and keeps it running for the session.

## 2. Get the pid

All calls are by reflection, so you need no reference to `UniPlaySong.dll`. They return JSON and
never throw across the boundary.

```csharp
private const string UpsId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

private string CallUps(string method)
{
    var ups = PlayniteApi.Addons.Plugins.FirstOrDefault(p => p.Id.ToString() == UpsId);
    return ups?.GetType().GetMethod(method, Type.EmptyTypes)?.Invoke(ups, null) as string;
}
```

| Method | Use |
|---|---|
| `GetSoundHostInfo()` | read the current state — the body of your `Func<int?>` |
| `EnsureSoundHostRunning()` | start it if a recording begins while it is down |
| `RestartSoundHost()` | recover a wedged host without restarting Playnite |

All three return the same shape:

```jsonc
{
  "apiVersion": 1,
  "ok": true,
  "enabled": true,        // the user's setting
  "running": true,        // process alive right now
  "processId": 24680,     // 0 when not running
  "executable": "UpsSound.exe",
  "reason": null          // when not running: "disabled" | "quarantined" | "failed" | "stopped"
}
```

**Read it per recording, not once.** The pid is normally stable for the session, but a host that
dies is restarted once and comes back with a new one. Your existing `Func<int?>` shape already
does the right thing.

## 3. Capture it

Your `AudioLoopbackRecorder` currently hardcodes Playnite's own pid for the chime sidecar:

```csharp
return new ProcessLoopbackCapture(
    System.Diagnostics.Process.GetCurrentProcess().Id, includeProcessTree: true);
```

Use the host's pid when it is available, and keep today's behaviour when it is not:

```csharp
var host = ReadSoundHostInfo();                 // your JSON parse of GetSoundHostInfo()
if (host.running && host.processId > 0)
{
    // Own process tree, so the game is never inside it — no cancellation reference needed.
    ChimeCaptureMode = PlayniteChimeCaptureMode.Clean;
    return new ProcessLoopbackCapture(host.processId, includeProcessTree: true);
}

// Unchanged: Playnite-tree sidecar, cancelled against the game reference.
```

`includeProcessTree: true` is correct — the host spawns nothing, so include and exclude are the
same set, and include is the safer default if that ever changes.

---

## What to expect

**`processId: 0` is not always a fault.** Check `reason` before reporting anything to the user:

| `reason` | Meaning | Your move |
|---|---|---|
| `disabled` | the user has not turned it on | use your existing sidecar path, say nothing |
| `quarantined` | `UpsSound.exe` missing — antivirus, or a partial install | fall back; worth telling the user |
| `failed` | it could not start, or stopped twice | fall back; UniPlaySong has already notified |
| `stopped` | enabled but not running yet | try `EnsureSoundHostRunning()` |

**The sound always plays.** If the host is unavailable for any reason, UniPlaySong plays the
sound itself, exactly as before. A user never loses an achievement sound to this feature — which
also means a silent failure on your side looks like "capture is degraded", never "audio is
broken".

**Scope.** Only achievement sounds move. Completion and abandoned jingles, the ControlUp sound,
and all music stay in Playnite's process.

**Lifetime.** The host dies with Playnite — job object plus a parent-pid watchdog — so it cannot
be left orphaned holding an audio device.

**Windows 10 build 19041+** for process loopback, which you already gate on.

## Sound file and timing

Unchanged, and still the companion to this: `ResolveAchievementSound(rarity)` returns the file
UniPlaySong would play, without playing it — so you can place your re-timed copy at Steam's true
unlock time.

## If something looks wrong

Ask the user for `UniPlaySong.log` with debug logging on (UniPlaySong Settings → Advanced).
Relevant lines:

```
Sound host started (pid 24680)
Achievement sound handed to the sound host (pid 24680): rare.mp3
Sound host did not acknowledge within 250ms - playing in process
Sound host unavailable (quarantined): ...
```

Anything routed to the host says so by pid, so a log answers "did it use the host" outright.
