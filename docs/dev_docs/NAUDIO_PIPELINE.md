# NAudio Audio Pipeline — Technical Documentation

## Overview

UniPlaySong uses two audio backends. The NAudio pipeline is active when **Live Effects** or **Visualizer** is enabled in settings. Otherwise, SDL2MusicPlayer handles playback (simpler, no effects/viz support).

This document covers the NAudio pipeline's architecture, the persistent mixer design, volume ramping, and the visualization data provider.

## Pipeline Architecture

### Persistent Mixer (v1.3.3+)

A single `WaveOutEvent` + `MixingSampleProvider` lives for the lifetime of the player. Songs are swapped via `AddMixerInput()`/`RemoveMixerInput()` — no device create/destroy per song.

```
Persistent layer (created once on first Load, never stopped until Dispose):
    MixingSampleProvider (44100Hz, stereo, float — ReadFully=true, outputs silence when empty)
        → SmoothVolumeSampleProvider (per-sample curve ramp, fader controls this)
            → WaveOutEvent (Init + Play called once, runs forever)

Per-song chain (created on Load, removed on Close):
    AudioFileReader / OggFileReader (NVorbis for .ogg files)
        → EffectsChain (reverb, echo, EQ — configurable style presets)
            → VisualizationDataProvider (FFT + peak/RMS tap for spectrum visualizer)
                → SongEndDetectorSampleProvider (fires event on partial read)
                    → [MonoToStereoSampleProvider if source is mono]
                        → [WdlResamplingSampleProvider if sample rate != 44100]
                            → added to mixer via AddMixerInput()
```

### Why Persistent Mixer?

Before v1.3.3, each song switch created a new `WaveOutEvent` + `.Init()` (~57ms) and disposed the old one (~15ms). These Windows audio API calls blocked the UI thread for ~70ms per game switch, causing visible lag during navigation.

The persistent mixer eliminates this entirely — `Close()` + `Load()` + `Play()` now takes **0ms** because it only manipulates the `MixingSampleProvider`'s input list.

### Format Normalization

The mixer operates at a fixed format: **44100Hz, stereo, IEEE float** (`WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)`).

Every song chain is normalized to match before being added:
- **Mono sources** → `MonoToStereoSampleProvider` (duplicates channel)
- **Non-44100Hz sources** → `WdlResamplingSampleProvider` (high-quality resampling)

### Song End Detection

Since `WaveOutEvent` never stops, its `PlaybackStopped` event won't fire for natural song endings. `SongEndDetectorSampleProvider` wraps the visualization provider and detects EOF.

**Critical: checks `read < count`, NOT `read == 0`.**

`MixingSampleProvider` auto-removes inputs when `Read()` returns fewer samples than requested (a partial buffer on the last audio chunk). The mixer removes the input immediately on that partial read and never calls `Read()` again. If the detector waited for `read == 0`, `SongEnded` would never fire.

The `SongEnded` event fires on the **audio thread**. `MediaEnded` is marshaled to the UI thread via `Dispatcher.BeginInvoke` to avoid deadlocks with the mixer's internal lock.

### Error Recovery

If `WaveOutEvent` reports an error (hardware disconnect, driver crash), `OnPlaybackStopped` tears down the entire persistent layer. The next `Load()` call rebuilds it fresh via `EnsurePersistentLayer()`.

## Volume Ramping (SmoothVolumeSampleProvider)

### Problem Solved

The old `VolumeSampleProvider` (NAudio built-in) applied a flat buffer-wide multiply — every sample in a buffer got the same volume. When the fader stepped volume ~60x/sec, this created a staircase waveform. The reverb chain's comb filters amplified the rate-of-change discontinuities into audible tremolo.

### Solution: Per-Sample Curve Ramp

`SmoothVolumeSampleProvider` sits in the persistent layer (between mixer and WaveOutEvent). The fader calls `SetTargetWithRamp(target, duration)` **once** per fade phase. The audio thread applies the selected curve per-sample — 44,100 increments/second vs. 60 discrete steps.

### Configurable Fade Curves

Five curve types, independently selectable for fade-in and fade-out via Experimental settings:

| Curve | Fade-In Behavior | Fade-Out Behavior |
|-------|-------------------|-------------------|
| **Linear** | Constant rate | Constant rate |
| **Quadratic** | `progress²` — starts slow, accelerates | `(1-t)²` — starts fast, decelerates |
| **Cubic** | `progress³` — slower start | `(1-t)³` — faster initial drop |
| **S-Curve** | Smoothstep `3t²-2t³` — gentle start and end | Same — gentle start and end |
| **Logarithmic** | `log(1+9t)/log(10)` — fast rise, slow tail | `1 - log(1+9t)/log(10)` — fast drop, slow tail |

Default: Quadratic fade-in, Cubic fade-out. The curve is snapshotted when `SetTargetWithRamp()` is called, so changing the setting mid-fade doesn't cause discontinuities.

### Fader Integration

`MusicFader` (`Players/MusicFader.cs`) is a `DispatcherTimer` at 50ms that **monitors** ramp completion — it does NOT step volume.

**TimerTick flow:**
1. Fire preload action once early in fade-out (pre-creates `AudioFileReader` for next song)
2. If `!_rampStarted`: call `player.SetVolumeRamp(target, duration)` once, set `_rampStarted = true`
3. Poll `_player.Volume` (getter returns audio-thread-owned `_currentVolume`)
4. Detect completion: `currentVol <= 0.0001` (fade-out) or `currentVol >= target - 0.001` (fade-in)
5. Execute pending actions (stop, play, pause) and transition to next phase

**Stall detection:** If the player's audio thread stops (short song EOF during fade-out), the ramp freezes because `Read()` is no longer called. The fader detects this via `!_player.IsActive` and force-completes pending actions.

**Deferred execution:** Song-switch completion (Close + Load + Play) is deferred to a `Dispatcher.BeginInvoke(Background)` frame so the timer tick doesn't block the UI thread.

### Interrupted Switch Recovery

If a pause source arrives during a mid-fade song switch (e.g., `GameStarting` fires during fade-out), the pause overwrites `_pauseAction` in the fader. The `_playAction` (which loads the new song) becomes orphaned.

`MusicFader.HasPendingPlayAction` returns `true` when paused with an orphaned play action. `MusicPlaybackService.RemovePauseSource()` and `RemovePauseSourceImmediate()` check this property first and call `_fader.Resume()` to execute the pending load+play.

## Visualization Data Provider

### Architecture

`VisualizationDataProvider` (`Audio/VisualizationDataProvider.cs`) is an `ISampleProvider` tap in the per-song chain. It passes audio through unmodified while capturing data for the desktop spectrum visualizer.

### Data Flow

1. **Audio thread** (`Read()`): Writes mono-downmixed samples into a circular buffer. Computes per-update peak and RMS levels (stereo + combined). Signals the FFT thread.
2. **FFT thread** (background, `BelowNormal` priority): Wakes on signal or ~16ms timer. Reads from circular buffer, applies Hann window, runs `FastFourierTransform.FFT()`, normalizes to dB, applies per-bin asymmetric smoothing, publishes via double-buffered swap.
3. **UI thread** (`GetSpectrumData()`): Copies pre-calculated spectrum from front buffer. Just `Array.Copy` — near-zero cost.

### FFT Configuration

| FFT Size | Frequency Resolution | Time Resolution | Use Case |
|----------|---------------------|-----------------|----------|
| 512 | ~86 Hz/bin | ~11.6ms | Fast response, lower detail |
| 1024 (default) | ~43 Hz/bin | ~23ms | Balanced |
| 2048 | ~21.5 Hz/bin | ~46ms | High detail, slower response |

Configurable via `VizFftSize` setting. Set at construction, immutable thereafter.

### Per-Bin Smoothing

Temporal smoothing uses asymmetric rise/fall alphas that vary by frequency:
- **Bass bins** (low index): Slower smoothing → weighty feel
- **Treble bins** (high index): Faster smoothing → sparkly feel
- **Rise** (signal increasing): Higher alpha for responsive attack
- **Fall** (signal decreasing): Lower alpha for smooth decay

Alphas are scaled by FFT size — larger windows update less frequently, so need higher alphas for equivalent visual responsiveness. Settings: `VizFftRiseLow`, `VizFftRiseHigh`, `VizFftFallLow`, `VizFftFallHigh` (int, 0-100).

### Fullscreen Gate

`VisualizationDataProvider.Paused` skips FFT computation when the desktop visualizer is not visible (fullscreen mode). Audio passthrough is unaffected. `GlobalPaused` static property propagates to newly created providers (one per song).

### Static Instance

`VisualizationDataProvider.Current` provides a static accessor for the UI visualizer component. Updated on each `Load()`, cleared on `Close()`.

## Logical Pause (NAudio-Specific)

`WaveOutEvent` is never paused or stopped during normal operation. Pausing is "logical":

1. Fader ramps volume to 0 via `SetVolumeRamp(0, fadeOutDuration)`
2. `NAudioMusicPlayer.Pause()` saves `_pausedPosition` and sets `_logicallyPaused = true`
3. Audio thread continues running, outputting silence (volume = 0)
4. On resume: seeks back to `_pausedPosition`, fader ramps volume up

This avoids stale pre-rendered buffer blips that occur with `WaveOutEvent.Pause()`/`Play()`.

### Short Track EOF During Pause

If a short song reaches EOF while logically paused (volume = 0, still in mixer), the mixer auto-removes the input on the partial read. `OnSongEnded()` clears both `_isPlaying` and `_logicallyPaused` so `IsActive` returns `false`, enabling the fader's stall detection.

On `Resume()`, if the song is no longer in the mixer (`!_isInMixer`), it resets `SongEndDetector` and re-adds to the mixer from the saved position.

## SDL2 Backend Differences

| Aspect | NAudio | SDL2 |
|--------|--------|------|
| Volume ramp | Per-sample on audio thread | DispatcherTimer ~60 steps/sec |
| Effects chain | Full (reverb, echo, EQ) | None |
| Visualization | FFT + peak/RMS | None |
| Pause mechanism | Logical (volume=0, position saved) | `Mix_PauseMusic()` / `Mix_ResumeMusic()` |
| Song end detection | `SongEndDetectorSampleProvider` | `Mix_HookMusicFinished` callback |
| Device lifecycle | Persistent (created once) | Per-song (Mix_LoadMUS/Mix_FreeMusic) |
| Fade curves | 5 configurable types | Quadratic (hardcoded) |

## File Reference

| File | Role |
|------|------|
| `Services/NAudioMusicPlayer.cs` | Persistent mixer + per-song chain management |
| `Audio/SmoothVolumeSampleProvider.cs` | Per-sample curve ramp (5 curve types) |
| `Audio/VisualizationDataProvider.cs` | FFT + peak/RMS tap for spectrum visualizer |
| `Audio/EffectsChain.cs` | Reverb + echo + EQ pipeline (style presets) |
| `Players/MusicFader.cs` | Ramp monitor + action dispatcher |
| `Services/IMusicPlayer.cs` | Player interface (shared by NAudio + SDL2) |
| `Services/SDL2MusicPlayer.cs` | SDL2 backend (DispatcherTimer ramp) |
| `UniPlaySongSettings.cs` | `FadeCurveType` enum, `NaudioFadeInCurve`, `NaudioFadeOutCurve` |
| `Common/FadeCurveTypeConverter.cs` | Enum↔int converter for XAML binding |
