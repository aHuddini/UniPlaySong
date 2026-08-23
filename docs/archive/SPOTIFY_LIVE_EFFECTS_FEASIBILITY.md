# Spotify Live Effects — Feasibility Research

**Question:** Can UniPlaySong (a no-admin, no-installer, managed .NET Framework 4.6.2 Playnite plugin) apply its live audio effects (reverb/EQ via NAudio) to **Spotify's** audio — obtaining an isolated, clean PCM stream of Spotify's output — **without** requiring the user to install a third-party virtual audio cable?

**Verdict:** **YES — PROVEN, then SHIPPED in v1.6.5.** Windows Process Loopback Capture pulls Spotify's isolated, clean PCM with no virtual cable, no driver, no admin. A feasibility spike (2026-07-12) captured 4s of a playing Spotify track in isolation — non-silent, no DRM block, verified by ear. The feature shipped in v1.6.5 (Live Effects + Calm Down + Visualizer on Spotify) using a small bundled C++/WinRT shim. The OS floor was later corrected down to Windows 10 version 2004 / build 19041 in v1.6.9 (see Constraints).

> **This doc is the feasibility research (the proven path).** For the SHIPPED architecture — the post-master output mixer, the −60 dB duck (not mute) because the tap is post-session-volume, the per-input fader/gate, and the issue-#81 idle-teardown guard — see the "External Source Path" section of [`NAUDIO_PIPELINE.md`](../dev_docs/NAUDIO_PIPELINE.md).

## Spike results (2026-07-12) — PROVEN

A minimal C++/WinRT console (`scratchpad/spotloop_spike.cpp`, built with VS 2022 + Windows SDK 26100) targeting Spotify's process tree via `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` (INCLUDE_TARGET_PROCESS_TREE):

- **Paused Spotify:** clocked 4.00s of frames, peak=0.0 (stream live, no audio rendered — expected).
- **Playing Spotify:** `frames=175959 (3.99s @ 44100Hz), peak=0.384, RMS=0.057` → **NON-SILENT, no DRM block.** WAV written + confirmed by ear: clean, isolated Spotify audio, nothing else on the system.
- **Process tree:** Spotify's window PID is the parent; the ~6 other Spotify processes are its children, so INCLUDE_TARGET_PROCESS_TREE on the window PID covers whichever child renders audio. (Confirmed via Win32_Process ParentProcessId.)
- **Toolchain confirmed present:** VS 2022 Professional (MSVC 14.44) + Windows SDK 10.0.26100 with `audioclientactivationparams.h`.

The single-variable proof: same code/API/PID, peak 0.0 (paused) → 0.384 (playing). The loopback taps the render stream exactly as documented.

Cross-ref: [SPOTIFY_INTEGRATION.md](../dev_docs/SPOTIFY_INTEGRATION.md) (SMTC transport control), [NAUDIO_PIPELINE.md](../dev_docs/NAUDIO_PIPELINE.md) (the effect chain we'd feed).

---

## The two Windows media-control planes (why mute worked but effects are harder)

| Plane | What it gives cross-app | What UPS uses it for |
|---|---|---|
| **SMTC** (`GlobalSystemMediaTransportControlsSession`) | Transport only — play/pause/skip/seek. **No volume/mute.** | Spotify play/pause/next/previous |
| **WASAPI audio session** (`ISimpleAudioVolume`) | Per-app **control knobs** — volume, mute, peak meter. **No sample access.** | Spotify mute (v1.6.4, `SpotifyAudioSession.cs`) |
| **WASAPI Process Loopback** (`ActivateAudioInterfaceAsync`) | Per-app **sample capture** — the isolated PCM of one process tree. | (proposed) Spotify live effects |

Mute worked because it's a *control knob* (WASAPI session). Effects need the *samples* — a different, newer API.

## The answer: Process Loopback Capture

`ActivateAudioInterfaceAsync` with `AUDIOCLIENT_ACTIVATION_PARAMS` / `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` captures the render audio of **one process tree in isolation** — no driver, no virtual device.

- Isolates one app — "only audio from the specified process, and its children, will be captured. Audio rendered by other processes will not be captured." (MS ApplicationLoopback sample)
- Handles Spotify's multi-process model — `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` captures the target PID **and its child processes**. Spotify spawns ~7 processes; target the tree, capture them all. (We already observed the 7-process reality when building the mute feature — exactly one owns the audio session, but the loopback tree covers the whole family.)
- Also supports EXCLUDE mode (capture everything *except* a process tree).
- Pipeline: isolated Spotify PCM → NAudio effect chain → `WasapiOut` to the real device.

### Verified sources (adversarial verification, primary Microsoft docs)
- ApplicationLoopback sample: https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
- `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`: https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params
- `PROCESS_LOOPBACK_MODE`: https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode
- Real managed-ish impl reference: https://github.com/masonasons/AudioCapture

## Constraints (the honest caveats)

1. **Minimum OS: Windows 10 version 2004 / build 19041** — RESOLVED in v1.6.9. Microsoft documents the floor as 20348, and this doc originally chose 20348 "to be safe", but that excluded every consumer Windows 10 machine: retail Win10 tops out at **19045** (22H2), so 20348 effectively meant "Windows 11 only". The community claim was correct — the API ships working from 2004, as OBS `win-capture-audio` and `masonasons/AudioCapture` both rely on. The gate was lowered to 19041 and **tester-confirmed on Win10 22H2 (19045)**. Safe because capture start fails soft: a machine that genuinely can't do it stays dry instead of doubling audio.

2. **Managed .NET cannot call it directly.** NAudio's `WasapiLoopbackCapture` is **system-mix-only** and does NOT support process loopback (NAudio issue #878, still open: https://github.com/naudio/NAudio/issues/878). The API needs `ActivateAudioInterfaceAsync` + the activation-params struct + an async COM completion handler — impractical from .NET Framework 4.6.2 P/Invoke.
   → **Requires a small bundled native C++/WinRT shim DLL.** This is a normal DLL UPS ships in its extension folder — NOT a driver, no install, no admin. Microsoft's ApplicationLoopback sample is the template.

3. **DRM.** WASAPI loopback returns silence for DRM-protected streams (per MS loopback-recording docs). Spotify's standard desktop playback is not loopback-DRM-blocked (unlike, say, Netflix video) — but this MUST be verified on a real machine before committing.

4. **Latency.** capture → buffer → process → playback adds tens of ms. Fine for ambient reverb/EQ; not for tight sync.

5. **We become Spotify's effect host.** UPS captures Spotify's stream, effects it, plays the processed version. Spotify's *original* output would still play unless muted — so the feature must **mute Spotify's real session** (we already have `SpotifyAudioSession` for that!) and play only the processed stream. If UPS stops capturing, we must unmute Spotify or it goes silent.

## Ruled out (all verified, don't revisit)

- **Register our own virtual audio device at runtime** — impossible. Virtual endpoints are created by **SysAudio (kernel-mode)**; `KSCATEGORY_AUDIO_DEVICE` is "reserved exclusively for SysAudio." SysVAD requires test-signing + trusted cert + reboot. Dead for a no-admin plugin.
- **Classic WASAPI render loopback** — full system mix only, can't isolate one app.
- **Bundle VB-Cable / VoiceMeeter** — license *permits* redistribution (donationware terms) but still a **driver install** + worse UX than the native API. Only a fallback if the native path fails.
- **Equalizer APO** — system/device-wide effects, requires install + reboot, can't cleanly scope to one app.

## Proposed feature shape (if pursued)

Experimental, opt-in, clearly gated:

1. **Detect capability** — Windows build >= 19041 AND the shim loads. Below that: feature hidden/disabled with a one-line hint.
2. **Native shim** (`UniPlaySong.SpotifyLoopback.dll`, C++/WinRT) — exposes a minimal managed-callable surface: `StartCapture(pid) → callback(byte[] pcm, WAVEFORMATEX)` / `StopCapture()`. Wraps `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` (INCLUDE_TARGET_PROCESS_TREE, Spotify's main PID).
3. **Managed side** — feed the shim's PCM callback into the existing NAudio effect chain (see NAUDIO_PIPELINE.md), output via `WasapiOut`. Mute Spotify's real session via the existing `SpotifyAudioSession` so only the processed stream is audible.
4. **Lifecycle** — on stop/crash/disable, unmute Spotify (never leave it silent).

## Feasibility spike — DONE (proven, see "Spike results" above)

The spike is complete and successful, and the production shim it seeded now ships as
`native/SpotifyLoopback/SpotifyLoopbackCapture.cpp`. The original spike is archived at
[`docs/archive/spikes/spotloop_spike.cpp`](../archive/spikes/spotloop_spike.cpp) — the exact code that
captured isolated Spotify PCM. Read it for provenance; read the native project for current behaviour.

## Remaining work to ship the feature (all plumbing — the hard part is proven)

1. **Turn the spike into a shim DLL** (`UniPlaySong.SpotifyLoopback.dll`, C++/WinRT) — swap the WAV-writing capture loop for a callback surface: `StartCapture(pid) → callback(byte[] pcm, WAVEFORMATEX)` / `StopCapture()`. Build it as a proper VS C++ project (the spike was a one-file `cl` build).
2. **Managed bridge** — P/Invoke the shim, marshal the PCM callback into a `WaveProvider`/`ISampleProvider` that feeds the existing NAudio effect chain ([NAUDIO_PIPELINE.md](../dev_docs/NAUDIO_PIPELINE.md)).
3. **Output + real-Spotify mute** — play the processed stream via `WasapiOut`; mute Spotify's own session with the existing [`SpotifyAudioSession`](../../src/Common/SpotifyAudioSession.cs) so only the effected version is audible. On stop/crash/disable, **unmute** (never leave Spotify silent).
4. **Capability gate** — detect Windows build >= 19041; hide/disable the feature + hint below that.
5. **Buffer sizing** — the shipped shim requests a **5s** WASAPI capture buffer (the spike used 200ms). That is CAPACITY, not latency: packets are still delivered as they arrive. 200ms proved far too small in production — the capture loop hands PCM to a *managed* callback, so any managed stall (a gen2 GC pause, a heavy Fullscreen theme view load) blocks it, WASAPI overflows, and the surplus is discarded at the source — audible as static. 5s matches OBS `win-capture-audio`; the managed ring is ~1s to accept the catch-up burst when the callback unblocks.
6. **Packaging** — ship the native DLL in the extension folder (a normal DLL, no install/admin), plus its VC++ redist consideration.

Effort is bounded and de-risked: capture is proven, DSP already exists, mute already exists. The new surface is the shim + its managed marshalling.

---

*Research: deep-research workflow, 20 sources, 23 adversarially-verified claims (Microsoft Learn primary + real implementations), 2026-07-12. Synthesis by hand (workflow synth step hit a session limit; verified claims stand).*
