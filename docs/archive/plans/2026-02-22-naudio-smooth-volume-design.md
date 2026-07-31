# NAudio Audio Artifact Fix — Design Document

## Problem

Tremolo/stutter audio artifact when using NAudioMusicPlayer (Live Effects or Visualizer enabled). Occurs during fades (fade-in on song start, fade-out on pause/game switch, pause/resume transitions).

### Root Cause

`MusicFader` used a `System.Timers.Timer` at ~16ms intervals (~60 ticks/sec), setting `IMusicPlayer.Volume` each tick via `Dispatcher.Invoke`. In the NAudio pipeline, this flowed through `VolumeSampleProvider`, which applies a flat buffer-wide multiply — every sample in the current buffer gets the same volume value.

This creates a staircase waveform: volume jumps discretely ~60 times per second. The reverb chain downstream (8 parallel comb filters with feedback loops in `EffectsChain`) amplifies the rate-of-change discontinuities at step boundaries into audible tremolo.

SDL2 is unaffected because `Mix_VolumeMusic()` is applied by SDL2 internally after its own mixing, with no reverb feedback loop to amplify discontinuities.

### Why It Only Manifests During Fades

During steady-state playback, `Volume` is constant — no discontinuities exist. During fades, volume changes ~60x/sec, creating step boundaries that the reverb's comb filters amplify.

## Approaches Attempted

### Option 3 (Failed): "Fader Steps for Both, NAudio Smooths"

Replaced `VolumeSampleProvider` with `SmoothVolumeSampleProvider` (per-sample linear ramp) but left the fader completely unchanged — it still called `Volume` setter ~60x/sec.

**Result:** Intermittent play/pause stuttering. The fader's rapid `Volume` setter calls (~60/sec) conflicted with the audio-thread ramp. Each setter call triggered a new ~16ms ramp, but the ramp calculation used `_currentVolume` which was being modified on the audio thread simultaneously. The setter overwrote the ramp target before the previous ramp completed, causing volume to bounce.

**Lesson:** The fader and the provider cannot independently control volume. Either the fader steps and the provider accepts passively (original design), or the fader delegates and the provider ramps autonomously. Hybrid doesn't work.

### Option 1 (Succeeded): "Fader Delegates, Backends Ramp"

Rewrote `MusicFader` to call `SetVolumeRamp(target, duration)` **once** at the start of each fade phase, then poll `_player.Volume` every 50ms to detect ramp completion and fire actions.

Each backend implements `SetVolumeRamp` optimally:
- **NAudio**: `SmoothVolumeSampleProvider.SetTargetWithRamp()` — per-sample exponential curve on the audio thread (44,100 increments/sec). Zero discrete steps for reverb to amplify.
- **SDL2/WPF**: Own `DispatcherTimer` at 16ms with exponential curve (`progress^2` fade-in, `1-(1-progress)^2` fade-out). Same behavior as the old fader.

## Architecture

### Before (broken)
```
Fader (System.Timers.Timer, 16ms, ~60 steps/sec)
  → Dispatcher.Invoke → _player.Volume = curvedValue (per tick)
    → VolumeSampleProvider: flat buffer multiply → STAIRCASE → reverb amplifies → TREMOLO

All backends receive same Volume stepping
```

### After (fixed)
```
Fader (DispatcherTimer, 50ms poll, NO volume stepping)
  → SetVolumeRamp(target, duration) called ONCE per phase
  → Polls _player.Volume to detect completion → fires stop/play/pause actions

NAudio backend:
  SmoothVolumeSampleProvider.SetTargetWithRamp()
    → per-sample exponential curve on audio thread → SMOOTH → reverb happy

SDL2/WPF backend:
  Own DispatcherTimer (16ms, ~60 steps/sec, exponential curve)
    → Same behavior as old fader, proven artifact-free
```

### Pipeline Change (NAudio only)
```
Before:  AudioFileReader → EffectsChain → VizProvider → VolumeSampleProvider → WaveOutEvent
After:   AudioFileReader → EffectsChain → VizProvider → SmoothVolumeSampleProvider → WaveOutEvent
```

## SmoothVolumeSampleProvider Design

Per-sample exponential-curve volume ramp. Curves match SDL2/WPF exactly:
- **Fade-in**: `progress^2` (starts fast, slows down)
- **Fade-out**: `1-(1-progress)^2` (starts slow, speeds up)

### SetTargetWithRamp(target, duration)
Called by fader once per phase. Calculates total ramp samples from `sampleRate * channels * duration`. Stores start volume, target, total samples, and resets position counter. The audio thread handles the entire ramp.

### Volume Property
- **Setter**: Instant volume set (no ramp). Used by fader for `Volume = 0` safety resets before play actions.
- **Getter**: Returns `_currentVolume` (audio-thread owned). The fader polls this at 50ms to detect ramp completion.

### Read() Method
- **Ramping**: Per-sample exponential curve using position-based progress. Calculates volume per sample from `progress = pos / totalSamples`, applies curve, multiplies buffer sample.
- **Steady state**: Fast paths for `vol == 0` (zero-fill), `vol == 1` (passthrough), other (constant multiply).

## MusicFader Design

`DispatcherTimer` at 50ms, `DispatcherPriority.Normal`. Does NOT step volume.

### TimerTick() Flow
1. Fire preload action once (early in fade-out, gives time to prepare next song)
2. If `!_rampStarted`: call `SetVolumeRamp(target, duration)` once, set `_rampStarted = true`
3. Poll `_player.Volume`:
   - **Song switch**: fade-out done (`<= 0.0001`) + has `_playAction` → stop old, play new, reset for fade-in
   - **Fade-in complete**: `currentVol >= musicVolume - 0.001` → set final volume, stop timer
   - **Pause/stop complete**: fade-out done + has `_pauseAction`/`_stopAction` → execute, stop timer

### Resume from Paused
Sets `_player.Volume = 0` **before** calling `_player.Resume()`, ensuring the fade-in ramp starts from silence. This prevents a blip at whatever stale volume the player was paused at.

### Phase Transitions
`SnapshotFadeParams()` captures `_snapVolume` and `_snapDuration` at phase start. `EnsureTimer()` resets `_rampStarted = false` and starts timer if not running.

## SDL2 Song-End Fix (Bonus)

`OnMusicFinishedInternal()` now marshals `MediaEnded` to the UI thread via `Dispatcher.BeginInvoke`. Previously it fired `MediaEnded` directly on the SDL2 audio callback thread. The handler then called `LoadAndPlayFile()` → `Mix_LoadMUS()` + `Mix_PlayMusic()` while SDL2 held the audio device lock, risking a crash.

## Files Changed

| File | Change |
|------|--------|
| `Audio/SmoothVolumeSampleProvider.cs` | New — per-sample exponential volume ramp for NAudio |
| `Services/IMusicPlayer.cs` | Added `SetVolumeRamp(double, double)` to interface |
| `Services/NAudioMusicPlayer.cs` | Swapped `VolumeSampleProvider` → `SmoothVolumeSampleProvider`, added `SetVolumeRamp` |
| `Services/SDL2MusicPlayer.cs` | Added `SetVolumeRamp` (DispatcherTimer + exponential curve), fixed `OnMusicFinishedInternal` threading |
| `Services/MusicPlayer.cs` | Added `SetVolumeRamp` (DispatcherTimer + exponential curve) |
| `Players/MusicFader.cs` | Rewritten — `DispatcherTimer` monitor, calls `SetVolumeRamp` once per phase |
| `Services/MusicPlaybackService.cs` | Updated 2 stale comments |

## Testing Results (2026-02-22)

- Option 3 (smooth provider only, fader unchanged): Failed — intermittent play/pause stuttering
- Option 1 (fader rewrite + smooth provider + backend ramps): **Succeeded**
  - No tremolo/stutter on game switching (20+ switches tested)
  - No artifact on pause/resume (alt-tab, media controls)
  - Exponential fade curves feel natural (matching SDL2/WPF behavior)
  - Resume from pause starts cleanly from silence (no blip)

---

## Addendum: Persistent Mixer + Configurable Curves (v1.3.3)

Two follow-up changes built on the smooth volume foundation:

### 1. Persistent Mixer Architecture

The per-song `WaveOutEvent` lifecycle (~70ms: Init=57ms + Dispose=15ms) was still blocking the UI thread during game switches. Replaced with a persistent `MixingSampleProvider` + single `WaveOutEvent` that runs for the player's lifetime. Songs are swapped via `AddMixerInput()`/`RemoveMixerInput()` — song switch dropped to **0ms**.

`SmoothVolumeSampleProvider` moved from the per-song chain to the persistent layer (between mixer output and WaveOutEvent). See [NAUDIO_PIPELINE.md](../dev_docs/NAUDIO_PIPELINE.md) for full architecture.

Key changes:
- `SongEndDetectorSampleProvider`: detects EOF via `read < count` (mixer auto-removes on partial read)
- Format normalization: `MonoToStereoSampleProvider` + `WdlResamplingSampleProvider` to match 44100Hz/2ch mixer format
- `OnSongEnded` dispatches `MediaEnded` to UI thread via `BeginInvoke` (audio thread → UI thread)
- Error recovery: device failures tear down and rebuild the persistent layer

### 2. Configurable Fade Curves

Replaced hardcoded quadratic/cubic curves with 5 selectable types: Linear, Quadratic, Cubic, S-Curve, Logarithmic. Independently configurable for fade-in and fade-out in Experimental settings.

`SmoothVolumeSampleProvider` constructor accepts `Func<FadeCurveType>` getters. Curve is snapshotted at ramp start via `_activeCurve = _isRampingDown ? _getFadeOutCurve() : _getFadeInCurve()`.

### 3. Interrupted Switch Recovery

`MusicFader.HasPendingPlayAction` property detects when a pause source arrived during a mid-fade song switch, orphaning the play action. `RemovePauseSource()` checks this first and calls `_fader.Resume()` to execute the orphaned load+play.
