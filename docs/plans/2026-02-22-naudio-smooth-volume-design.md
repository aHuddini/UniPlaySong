# NAudio Smooth Volume Fix — Design Document

## Problem

Tremolo/stutter audio artifact when using NAudioMusicPlayer (Live Effects or Visualizer enabled). Occurs during fades (fade-in on song start, pause/resume).

### Root Cause

`MusicFader` uses a `System.Timers.Timer` at ~16ms intervals (~60 ticks/sec), setting `IMusicPlayer.Volume` each tick via `Dispatcher.Invoke`. In the NAudio pipeline, this flows through `VolumeSampleProvider`, which applies a flat buffer-wide multiply — every sample in the current buffer gets the same volume value.

This creates a staircase waveform: volume jumps discretely ~60 times per second. The reverb chain downstream (8 parallel comb filters with feedback loops in `EffectsChain`) amplifies the rate-of-change discontinuities at step boundaries into audible tremolo.

SDL2 is unaffected because `Mix_VolumeMusic()` is applied by SDL2 internally after its own mixing, with no reverb feedback loop to amplify discontinuities.

### Why It Only Manifests During Fades

During steady-state playback, `Volume` is constant — no discontinuities exist. During fades, volume changes ~60x/sec, creating step boundaries that the reverb's comb filters amplify.

## Chosen Approach: Option 3 — "Fader Steps for Both, NAudio Smooths"

### Principle

The fader (`MusicFader`) continues stepping volume ~60x/sec for both backends — zero changes to shared code. A new `SmoothVolumeSampleProvider` replaces NAudio's built-in `VolumeSampleProvider` in the NAudio pipeline only. It interpolates between the fader's discrete steps on the audio thread, producing ~44,100 smooth volume transitions per second instead of ~60 staircase jumps.

### Why This Approach

- **Zero risk to SDL2** — fader is completely unchanged
- **Minimal code surface** — one new file, three lines changed in `NAudioMusicPlayer.cs`
- **Addresses root cause** — eliminates the discontinuities that reverb amplifies
- **No architectural changes** — no persistent mixer, no fader rewrite

### Pipeline Change

```
Before:  AudioFileReader → EffectsChain → VizProvider → VolumeSampleProvider → WaveOutEvent
After:   AudioFileReader → EffectsChain → VizProvider → SmoothVolumeSampleProvider → WaveOutEvent
```

## SmoothVolumeSampleProvider Design

Drop-in replacement for `VolumeSampleProvider`. Same `ISampleProvider` interface, same `Volume` property signature.

### Volume Property

- **Setter** (called by fader on UI thread, ~60x/sec): stores target volume, calculates per-sample increment to ramp from current to target over ~16ms (one fader interval worth of samples = `sampleRate * channels * 0.016`)
- **Getter** (called by fader to check current volume): returns `_currentVolume` (audio-thread owned)

### Read() Method

- **Ramping state**: applies per-sample linear interpolation (`vol += increment` per sample), clamping at target. Once target reached, switches to steady state for remainder of buffer.
- **Steady state fast paths**: `vol == 0` → zero-fill buffer; `vol == 1` → pass through unmodified; other → constant multiply.

### Thread Safety

- `_targetVolume`: `volatile float`, written by UI thread (fader), read by audio thread
- `_currentVolume`: owned by audio thread (written in `Read()`, read by `Volume` getter)
- `_rampIncrement`, `_isRamping`: set atomically in the setter before `_isRamping = true`

### Ramp Duration

~16ms of samples at source sample rate × channels. At 44100Hz stereo: `44100 * 2 * 0.016 = 1411` samples per ramp. This matches the fader's tick interval, so each fader step is fully interpolated before the next arrives.

## Testing Results (2026-02-22)

The SmoothVolumeSampleProvider was implemented and tested. Initial results showed improvement — artifact was absent for the first few game switches. However, the artifact returned after several switches, indicating that per-sample volume smoothing alone does not fully resolve the issue. The approach was reverted.

### Implications

The root cause may involve more than just volume stepping discontinuities. Possible contributing factors:
- WaveOutEvent device recreation on each song load (per-song device architecture)
- Buffer underruns during the transition period
- EffectsChain state carrying over between volume ramp segments
- Interaction between the fader's `Dispatcher.Invoke` timing and the audio thread's buffer boundaries

### Fallback: Option 1 — Fader Rewrite

If a future attempt is needed, Option 1 would rewrite `MusicFader` to use `SetVolumeRamp()` (single call per fade phase) with a `DispatcherTimer` monitor for NAudio, while keeping timer stepping for SDL2. This addresses the timing jitter from `Dispatcher.Invoke` in addition to the stepping discontinuity.

## Files

| File | Change |
|------|--------|
| `Audio/SmoothVolumeSampleProvider.cs` | New file (reverted) |
| `Services/NAudioMusicPlayer.cs` | 3-line swap: field type + construction + using directive (reverted) |
| `Players/MusicFader.cs` | No change |
| `Services/MusicPlaybackService.cs` | No change |
| `Services/SDL2MusicPlayer.cs` | No change |
