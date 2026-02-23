# NAudio Audio Artifact Fix — Implementation Plan

> **Status:** Implemented and verified (2026-02-22). Option 1 succeeded.

**Goal:** Eliminate tremolo/stutter artifact in NAudio mode by replacing the fader's per-tick volume stepping with a single `SetVolumeRamp()` call per fade phase, delegating per-sample interpolation to the audio thread.

**Architecture:** New `SetVolumeRamp` method on `IMusicPlayer`. NAudio uses per-sample exponential ramp (`SmoothVolumeSampleProvider`). SDL2/WPF use their own DispatcherTimer with exponential curves. MusicFader rewritten to call `SetVolumeRamp` once and poll for completion.

---

## Task 1: Create SmoothVolumeSampleProvider

**Files:**
- Create: `Audio/SmoothVolumeSampleProvider.cs`

Per-sample exponential-curve volume ramp for NAudio pipeline. `SetTargetWithRamp(target, duration)` called once by fader. Position-based progress, `progress^2` fade-in, `1-(1-progress)^2` fade-out. `Volume` setter for instant sets (no ramp).

## Task 2: Add SetVolumeRamp to IMusicPlayer

**Files:**
- Modify: `Services/IMusicPlayer.cs`

Added `void SetVolumeRamp(double targetVolume, double durationSeconds)`.

## Task 3: Wire into NAudioMusicPlayer

**Files:**
- Modify: `Services/NAudioMusicPlayer.cs`

**Changes:**
1. Removed `using NAudio.Wave.SampleProviders;`
2. Changed field: `VolumeSampleProvider` → `SmoothVolumeSampleProvider`
3. Changed construction: `new VolumeSampleProvider(...)` → `new SmoothVolumeSampleProvider(...)`
4. Added `SetVolumeRamp` delegating to `_volumeProvider.SetTargetWithRamp()`

## Task 4: Implement SetVolumeRamp in SDL2MusicPlayer

**Files:**
- Modify: `Services/SDL2MusicPlayer.cs`

Own DispatcherTimer at 16ms (~60 steps/sec) with exponential curve matching old fader behavior. Also fixed `OnMusicFinishedInternal` to marshal `MediaEnded` to UI thread via `Dispatcher.BeginInvoke`.

## Task 5: Implement SetVolumeRamp in MusicPlayer (WPF)

**Files:**
- Modify: `Services/MusicPlayer.cs`

Same DispatcherTimer + exponential curve pattern as SDL2.

## Task 6: Rewrite MusicFader

**Files:**
- Modify: `Players/MusicFader.cs`

Replaced `System.Timers.Timer` (16ms, Dispatcher.Invoke, per-tick volume stepping) with `DispatcherTimer` (50ms, polling only). Calls `SetVolumeRamp` once per fade phase. Polls `_player.Volume` to detect completion and fire actions.

Key fix: Resume from paused sets `_player.Volume = 0` before `_player.Resume()` to prevent blip.

## Task 7: Update MusicPlaybackService comments

**Files:**
- Modify: `Services/MusicPlaybackService.cs`

Updated 2 stale comments referencing `VolumeSampleProvider at 1.0`.

## Test Results

**Option 3 (smooth provider only):** Failed — fader's ~60/sec Volume setter calls conflicted with audio-thread ramp.

**Option 1 (full implementation):** Succeeded — no artifact on game switching, pause/resume, or alt-tab.
