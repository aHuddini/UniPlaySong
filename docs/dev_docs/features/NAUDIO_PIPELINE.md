# NAudio Audio Pipeline — Technical Documentation

## Overview

UniPlaySong uses two audio backends. The NAudio pipeline is active when **Live Effects** or **Visualizer** is enabled in settings. Otherwise, SDL2MusicPlayer handles playback (simpler, no effects/viz support).

This document covers the NAudio pipeline's architecture, the persistent mixer design, volume ramping, and the visualization data provider.

## Pipeline Architecture

### Persistent Mixer (v1.3.3+)

A single `WaveOutEvent` + `MixingSampleProvider` lives for the lifetime of the player. Songs are swapped via `AddMixerInput()`/`RemoveMixerInput()` — no device create/destroy per song.

```
Persistent layer (created once on first Load, never stopped until Dispose):
    _mixer: MixingSampleProvider (44100Hz, stereo, float — ReadFully=true, outputs silence when empty)
        → CalmDownProcessor
            → SmoothVolumeSampleProvider (per-sample curve ramp, fader controls this)
                → _outputMixer: MixingSampleProvider (+ external source, post-master)
                    → Equalizer (graphic EQ; passthrough when off or flat)
                        → OutputClock (counts frames handed to the device)
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

### External Source Path (`LoadExternalSource`) — v1.6.5+

`NAudioMusicPlayer.LoadExternalSource(ISampleProvider)` runs an **external** live provider (Spotify capture) through the same `EffectsChain` / `VisualizationDataProvider` / `CalmDownProcessor` classes as game music, so effects/visualizer/Calm Down apply identically. This is how Spotify gets them (v1.6.5): Windows Process Loopback capture → `SpotifyLoopbackClient` → `SpotifyCaptureSampleProvider` (the `ISampleProvider`) → `LoadExternalSource`.

Two structural differences from the per-song chain (both learned the hard way during bring-up):

1. **It plays on a second, post-master output mixer — NOT the game-music mixer.** `EnsurePersistentLayer` builds `_outputMixer` that combines the master-faded game chain (`_mixer → CalmDown → _volumeProvider`) *and* the external source, then feeds the device. The external input is added to `_outputMixer` **after** the master fader. This is deliberate: game-music pause sources (fullscreen theme overlays especially) ride `_volumeProvider` to 0, and radio mode never runs the game fader to bring it back — so an external source sharing the master would be silenced permanently. Post-master, it's immune. Its only knobs are its own per-input `SmoothVolumeSampleProvider` (`_externalVolume`, faded with the user's configured curves/durations) and a theme-overlay/video gate (`ExternalGateProvider` → `TargetExternalVolume`). It carries its **own** `CalmDownProcessor` because the game-music one is pre-master, which it bypasses.

2. **No `SongEndDetectorSampleProvider`.** A live capture stream never EOFs, so there's no loop/advance to fire; `ExternalGateProvider` zero-fills to always return `count` (a partial read would make `MixingSampleProvider` auto-remove the input). Teardown is explicit via `StopExternalSource` (ramps the per-input volume to 0, then removes off-thread).

The mute-and-replace physics: the loopback tap is **post** session-volume, so the coordinator ducks Spotify's session to 2⁻¹⁰ (not a hard mute — that would silence the capture) and `SpotifyCaptureSampleProvider` restores level with a power-of-2 makeup gain. Capture is 32-bit float.

Idle audio-device teardown (issue #81) must NOT fire while effected Spotify is audible: `SleepCoordinator.isAudible` counts `SpotifyLiveEffectsHost.IsEffecting`, and lock/suspend shut the host down (unmuting Spotify) and rebuild on resume.

See `docs/dev_docs/features/SPOTIFY_INTEGRATION.md` for the capture path and `SpotifyEffectsCoordinator`'s dry-output mute invariant, and `TECHNICAL_REFERENCE.md` for the capture approaches ruled out.

## Equalizer — v1.8.10

`Audio/Equalizer.cs` is the last stage before `OutputClock`, so it shapes game music, crossfades and an
external (effected Spotify) source alike. Settings: Live Effects → Equalizer (EQ), shown again under Quick Start
(one `EqualizerPage` control in both places, bound to the same settings).

- **Layouts.** 15 bands (2/3-octave, 25 Hz–16 kHz, the default) or 10 (Winamp's, 60 Hz–16 kHz), chosen by
  `EqualizerBandCount`. Each layout keeps its own curve (`EqualizerBand*`, `Equalizer15Band*`); a curve's layout is
  told by its length. ±12 dB per band plus `EqualizerPreampDb`.
- **Filters.** Audio EQ Cookbook peaking filters in the digital bandwidth form, each reaching halfway (in octaves)
  to its neighbours; the outer bands extend three octaves below and one above. NAudio's `PeakingEQ` takes an analog
  Q and comes out too narrow near the top bands.
- **Solved gains.** Neighbouring filters overlap and add up, so `SolveGains` adjusts all gains together (Gaussian
  elimination over the per-dB interaction matrix, up to six rounds, ±36 dB guard) until the response meets every
  slider at its centre. Every preset plays within 0.1 dB of its sliders in both layouts.
- **Presets** are written for 10 bands; the 15-band versions are `CarryCurve` of them — what the 10-band curve plays
  at the 15 centres. Switching layouts in the page carries a custom curve the same way.
- **Backend.** `EqualizerEnabled` forces NAudio like Calm Down, at all three decision points: `CreateMusicPlayer`,
  the settings diff, and the live property-changed path (the dialog edits the live object, so ticking it while SDL2
  plays switches at once).
- Off, or every band and the preamp at 0, audio passes through untouched. When active, output goes through
  `EffectsChain.SoftKneeLimiter`.
- The page's vertical faders are its own template (`EqSlider`): Playnite's theme rotates a vertical slider and sizes
  the track from `Height`, which collapses it.

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

### Bar Count and the Tuning Tables — v1.8.7+

`SpectrumVisualizerControl` draws `VizBarCount` bars (1–12, default 12); the control's width follows
(3px per bar on a 1px gap, so 47px at 12 and 23px at 6). Added for Desktop themes whose top panel is
styled with short or circular buttons the full-width visualizer will not sit beside.

The catch is that **four tables in that control are hand-calibrated for exactly twelve bars**: the
band edges (`BandEdges`), `BarGain`, `BleedFraction` and `BarGravityScale`. `BuildBars(count)`
resamples all four, and the mapping (`i * ReferenceBarCount / count`) is the **identity at 12**, so
the default visualizer is unchanged rather than merely close.

Two things about that resampling are deliberate and easy to get wrong:

- **Band edges interpolate in log-frequency space, resampling the existing curve rather than
  recomputing one.** The reference edges are *not* evenly log-spaced — the low end is deliberately
  compressed, by up to 47% against a pure log split — so generating fresh logarithmic bands would
  visibly change the default. Taking the first N entries instead would leave a 4-bar visualizer
  showing bass only, with the top of the spectrum invisible.
- **The per-bar tables sample at each bar's LOW frequency edge, not its centre.** RMS across a band
  is dominated by its lowest frequencies, so a wide bar behaves like the bass inside it.
  Centre-sampling would hand a one-bar visualizer a treble gain near 5.0 and peg it at full height
  permanently. The bass/treble gain split follows the count (`i * 2 < _barCount`) rather than the
  old fixed index 6.

The count is applied **live** by a per-frame comparison in the render loop — the same dirty-check
pattern the colour theme uses — so no restart is needed while fitting the bars to a theme.

### Per-Bin Smoothing

Temporal smoothing uses asymmetric rise/fall alphas that vary by frequency:
- **Bass bins** (low index): Slower smoothing → weighty feel
- **Treble bins** (high index): Faster smoothing → sparkly feel
- **Rise** (signal increasing): Higher alpha for responsive attack
- **Fall** (signal decreasing): Lower alpha for smooth decay

Alphas are scaled by FFT size — larger windows update less frequently, so need higher alphas for equivalent visual responsiveness. Settings: `VizFftRiseLow`, `VizFftRiseHigh`, `VizFftFallLow`, `VizFftFallHigh` (int, 0-100).

### Alternative Sync (Experimental) — v1.8.10

`WaveOutEvent` pulls 150 ms blocks and keeps two queued, so a tap sees audio ~350 ms before it is heard (measured
with WASAPI loopback). By default the tap analyses the newest 1024 samples of each block. With
`AlternativeAudioReactiveVisualizerSync` on (Advanced → Experimental), the taps on `NAudioMusicPlayer` (primary,
crossfade secondary, external) use `OutputClock`:

- `OutputClock` counts frames handed to the device; with `WaveOutEvent.GetPosition()` (32-bit, wraps after ~3.4 h:
  taken modulo 2^32) that gives what is queued. One per player, re-attached on each persistent-layer rebuild.
- Each `Read` records the device frame its block ends on; the window ends at the frame being heard, less a
  measured 40 ms (`DeviceLatencyFrames`) the position count can't see.
- Updates run every ~15 ms and average the power of quarter-overlapping Hann windows covering everything played
  since the last update, so a short hit counts the same wherever it lands.
- Peak/RMS come from the same 150 ms window. Smoothing is time-based: rise per 23 ms hop, fall per ~49 ms.
- Taps without a clock (the Spotify viz-only pump, which analyses audio already played) keep the default behaviour.

Taps read settings through a live getter (`Func<UniPlaySongSettings>`), since a settings save replaces the object.

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

### Limitation: Instant Pause Must Set Volume to 0

Because `_logicallyPaused` is only a flag — it does not stop the audio chain from outputting samples — any code path that bypasses the fader must explicitly set `_musicPlayer.Volume = 0` before calling `Pause()`. Without this, audio continues playing at the last volume level despite the "paused" state.

Paths that handle this correctly:
- **Fader-based pause** (manual, FocusLoss, etc.) — fader ramps to 0 before logical pause
- **Jingle pause** (`PauseForJingle`) — sets `Volume = 0` explicitly
- **Dashboard pause** — sets `Volume = 0` explicitly
- **Instant pause** (`AddPauseSourceImmediate`) — sets `Volume = 0` explicitly (fixed in v1.3.10)

**Rule:** Never call `_musicPlayer.Pause()` on NAudio without ensuring volume is 0 first.

### Short Track EOF During Pause

If a short song reaches EOF while logically paused (volume = 0, still in mixer), the mixer auto-removes the input on the partial read. `OnSongEnded()` clears both `_isPlaying` and `_logicallyPaused` so `IsActive` returns `false`, enabling the fader's stall detection.

On `Resume()`, if the song is no longer in the mixer (`!_isInMixer`), it resets `SongEndDetector` and re-adds to the mixer from the saved position.

## GME Retro Chiptune Reader (v1.4.0+)

`GmeReader` (P/Invoke wrapper around `libgme`) is a drop-in NAudio `WaveStream + ISampleProvider` that slots into `CreateAudioReader()` for `.vgm` / `.vgz` / `.spc` / `.nsf` etc. Unlike `AudioFileReader`, GME has two traits that force special handling:

1. **`libgme` is not thread-safe on a single `emu` handle.** The audio thread's `gme_play()` and any other thread's `gme_seek()` / `gme_tell()` cannot run concurrently. `GmeReader._gmeLock` serializes every native call (`gme_play`, `gme_seek`, `gme_tell`, `gme_delete`). Without this lock, concurrent calls silently corrupt the emulator's internal state and produce silent output.

2. **`gme_seek(target_ms)` is O(target_ms).** GME rewinds to track start and replays emulation forward to the target position at real CPU speed — no "already there" shortcut exists in the C API. Seeking 2 minutes into a track takes several seconds of wall-clock time. Running `gme_seek` synchronously on the UI thread blocks Playnite's entire UI, sometimes triggering Windows' "not responding" dialog.

### Pause-Detach Design

For `GmeReader`, `Pause()` calls `RemoveSongFromMixer()` so the audio thread stops calling `gme_play()`. The emu's position freezes at `_pausedPosition` for the duration of the pause. This differs from the `AudioFileReader` path, where the mixer input stays attached during logical pause (muted at volume 0) — cheap there because advancing a buffer pointer while silent is free, but unacceptable for GME because every `Read()` drives real emulation work that would invalidate the saved position.

### Resume Paths

`IMusicPlayer.Resume(Action onReady = null)` takes an optional callback invoked when the player is actually producing audio (v1.4.1). For `AudioFileReader`-backed songs, Resume seeks synchronously (buffer-pointer update) and invokes `onReady` before returning. For `GmeReader`-backed songs there are three paths:

1. **Coalesce** — if a seek to the same target is already in flight (rare; only triggered by the slow path below), append `onReady` to the existing callback list and return. Prevents duplicate fade-in triggers.

2. **Fast path (common case)** — read the emu's current position. If it's within 100ms of `_pausedPosition`, the Pause-detach froze the emu at the right spot and no seek is needed. Re-add the mixer input, invoke `onReady` synchronously. Near-zero cost; feels instant to the user.

3. **Slow path (genuine seeks)** — the emu isn't where we want it (e.g. a hypothetical user-initiated scrub or seek-to-position). Remove the mixer input, run `gme_seek` on a thread-pool task, dispatch re-add + `onReady` via `Application.Current.Dispatcher.BeginInvoke` when complete. If Pause arrives mid-seek, the seek's completion discards the stale callbacks (the next Resume will kick a fresh cycle) and leaves the player in the paused state.

`MusicFader.Resume` triggers its `EnsureTimer()` fade-in ramp from inside `onReady`, not directly after `_player.Resume()`. For the fast path (synchronous callback) this is identical to the old behavior; for the slow path the fade-in begins only when audio is actually flowing again, preventing silent-ramp-then-snap-in. `ResumeFromJingle` uses the same pattern.

`Pause()` reads `_seekInFlightTarget` before touching `_audioFile.CurrentTime` — during a slow-path seek, that read would block the UI thread on `_gmeLock`. If a seek is in flight, Pause uses the known target as `_pausedPosition` instead. Safe because Pause only needs a position that the next Resume can agree on.

Not all fader-bypass paths use `onReady` (e.g. `ResumeImmediate`): those set volume to target directly and accept a brief silent gap for GME — intentional for instant-resume flows where fade-in is undesired.

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
| `Audio/OutputClock.cs` | Frames submitted vs played, for the experimental visualizer sync |
| `Audio/Equalizer.cs` | Graphic EQ stage (15 or 10 bands, solved gains, presets) |
| `Controls/Settings/EqualizerPage.xaml` | EQ page and its fader template (shown in Live Effects and Quick Start) |
| `Audio/EffectsChain.cs` | Reverb + echo + EQ pipeline (style presets) |
| `Players/MusicFader.cs` | Ramp monitor + action dispatcher |
| `Services/IMusicPlayer.cs` | Player interface (shared by NAudio + SDL2) |
| `Services/SDL2MusicPlayer.cs` | SDL2 backend (DispatcherTimer ramp) |
| `UniPlaySongSettings.cs` | `FadeCurveType` enum, `NaudioFadeInCurve`, `NaudioFadeOutCurve` |
| `Common/FadeCurveTypeConverter.cs` | Enum↔int converter for XAML binding |
