# Icon Glow Iteration 4 — Multi-Band Audio Reactivity Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the single bass-only signal with three independent frequency bands (bass/mids/treble), each driving a different visual property, so the glow reacts musically across all game music genres.

**Architecture:** Split the FFT spectrum into three bands at each tick. Each band gets its own rolling window normalizer, common mode subtraction, and smoothing envelope. Bass drives outer glow opacity breathing (slow, heavy pulse). Mids drive the inner halo color shift toward white (synth/melody brightness). Treble drives the inner halo blur radius bloom (shimmer/sparkle). All three are independent — a track with no kick but active synths will still produce a lively glow via mids.

**Tech Stack:** C#, `src/IconGlow/IconGlowManager.cs`. No new dependencies. FFT data from `VisualizationDataProvider.GetSpectrumData()`. Bin count math assumes 44100Hz sample rate with NAudio's default FFT size.

---

## Context: Iteration 3 Algorithm (preserve for rollback)

Current single-band approach in `OnGlowTick` (~lines 299–381):
- `bassBins = 6` → bins 0–5 (~0–250Hz): kick + sub-bass only
- 80% bass energy + 20% RMS blend → `level`
- Rolling window peak normalizer (`_peakWindow[300]`)
- Common mode subtraction (rise 0.03, decay 0.35, subtract 65%)
- Three-stage smoothing: s1(0.75/0.25), s2(0.35/0.15), s3(0.25/0.22)
- Punch: rise 0.70, decay 0.45
- Blend: `_smooth3 * 0.8–0.9 + _punch * 0.1–0.2`
- `rawIntensity` → power curve `Math.Pow(x, 0.5)` → `_smoothedIntensity`
- Outer glow opacity: `0.45 + _smoothedIntensity * 0.55`
- Inner halo blur: `2 + _smoothedIntensity * 23`
- Inner halo color: base → 75% toward white via `_smoothedIntensity`

---

## Frequency Band Layout

At 44100Hz with a typical 1024-point FFT, each bin = ~43Hz:

| Band | Hz Range | Bin Range | Instruments |
|------|----------|-----------|-------------|
| Bass | 20–250 Hz | 0–5 | Kick drum, sub-bass, bass guitar |
| Mids | 250–4000 Hz | 6–92 | Synths, pads, piano, guitars, vocals, melodies |
| Treble | 4000–12000 Hz | 93–278 | Hi-hats, cymbals, synth shimmer, air |

---

## Task 1: Add Per-Band Fields

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs` (field declarations, ~lines 37–55)

**Step 1: Replace single-band fields with three-band fields**

Remove these existing fields:
```csharp
private const int PeakWindowSize = 300;
private double[] _peakWindow;
private int _peakWindowIdx;
private double _peakWindowMax;
private int _peakMaxAge;
private double _commonMode;
private double _smooth1;
private double _smooth2;
private double _smooth3;
private double _punch;
private float[] _spectrumBuf;
private float[] _prevSpectrum;
private double _fluxBaseline;
private int _onsetFrames;
```

Add these in their place:
```csharp
// Per-band state — bass (0-250Hz), mids (250-4000Hz), treble (4000-12000Hz)
private const int PeakWindowSize = 300;

private double[] _bassWindow;
private int _bassWindowIdx;
private double _bassWindowMax;
private int _bassMaxAge;
private double _bassCommon;
private double _bassSmooth1, _bassSmooth2, _bassSmooth3;
private double _bassPunch;

private double[] _midWindow;
private int _midWindowIdx;
private double _midWindowMax;
private int _midMaxAge;
private double _midCommon;
private double _midSmooth;   // single-stage — mids need less smoothing

private double[] _trebleWindow;
private int _trebleWindowIdx;
private double _trebleWindowMax;
private int _trebleMaxAge;
private double _trebleSmooth; // single-stage, fast decay

private float[] _spectrumBuf;
private float[] _prevSpectrum; // bass bins only, for onset detection
private double _fluxBaseline;
private int _onsetFrames;
```

**Step 2: Build to verify no compile errors**
```bash
cd "c:/Projects/UniPSound/UniPlaySong"
dotnet build -c Release 2>&1 | tail -5
```
Expected: errors about missing fields — that's fine, we haven't updated the rest yet.

---

## Task 2: Update Initialization in ApplyGlowInternal

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs` (~lines 240–255, init block after `_glowAngle = 0.0`)

**Step 1: Replace old init block**

Remove:
```csharp
_peakWindow = new double[PeakWindowSize];
_peakWindowIdx = 0;
_peakWindowMax = 0.001;
_peakMaxAge = 0;
_commonMode = 0.0;
_smooth1 = 0.0;
_smooth2 = 0.0;
_smooth3 = 0.0;
_punch = 0.0;
_prevSpectrum = null;
_fluxBaseline = 0.0;
_onsetFrames = 0;
```

Replace with:
```csharp
_bassWindow = new double[PeakWindowSize];
_bassWindowIdx = 0; _bassWindowMax = 0.001; _bassMaxAge = 0;
_bassCommon = 0; _bassSmooth1 = 0; _bassSmooth2 = 0; _bassSmooth3 = 0; _bassPunch = 0;

_midWindow = new double[PeakWindowSize];
_midWindowIdx = 0; _midWindowMax = 0.001; _midMaxAge = 0;
_midCommon = 0; _midSmooth = 0;

_trebleWindow = new double[PeakWindowSize];
_trebleWindowIdx = 0; _trebleWindowMax = 0.001; _trebleMaxAge = 0;
_trebleSmooth = 0;

_prevSpectrum = null;
_fluxBaseline = 0.0;
_onsetFrames = 0;
```

**Step 2: Build**
```bash
dotnet build -c Release 2>&1 | tail -5
```

---

## Task 3: Rewrite OnGlowTick — Band Extraction

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs` (the `if (vizProvider != null)` block, ~lines 299–381)

**Step 1: Replace the entire `if (vizProvider != null)` block**

Replace everything from `if (vizProvider != null)` down to (but not including) `else if (_settings.EnableIconGlowPulse)` with:

```csharp
if (vizProvider != null)
{
    int specSize = vizProvider.SpectrumSize;

    if (specSize > 0)
    {
        if (_spectrumBuf == null || _spectrumBuf.Length < specSize)
            _spectrumBuf = new float[specSize];
        vizProvider.GetSpectrumData(_spectrumBuf, 0, specSize);

        // Bin layout: ~43Hz/bin at 44100Hz / 1024-pt FFT
        // Bass:   bins 0–5    (~0–250Hz)   kick, sub, bass guitar
        // Mids:   bins 6–92   (~250–4000Hz) synths, pads, piano, melody
        // Treble: bins 93–278 (~4000–12000Hz) hi-hats, shimmer, air
        int bassEnd   = Math.Min(6, specSize);
        int midEnd    = Math.Min(93, specSize);
        int trebleEnd = Math.Min(279, specSize);

        double bassSum = 0;
        for (int i = 0; i < bassEnd; i++) bassSum += _spectrumBuf[i];
        double bassLevel = bassEnd > 0 ? bassSum / bassEnd : 0;

        double midSum = 0;
        for (int i = bassEnd; i < midEnd; i++) midSum += _spectrumBuf[i];
        double midLevel = (midEnd - bassEnd) > 0 ? midSum / (midEnd - bassEnd) : 0;

        double trebleSum = 0;
        for (int i = midEnd; i < trebleEnd; i++) trebleSum += _spectrumBuf[i];
        double trebleLevel = (trebleEnd - midEnd) > 0 ? trebleSum / (trebleEnd - midEnd) : 0;

        // Spectral flux onset detection (bass bins only — kicks/bass transients)
        if (_prevSpectrum == null) _prevSpectrum = new float[bassEnd];
        double flux = 0;
        for (int i = 0; i < bassEnd; i++)
        {
            double diff = _spectrumBuf[i] - _prevSpectrum[i];
            if (diff > 0) flux += diff;
            _prevSpectrum[i] = _spectrumBuf[i];
        }
        flux /= Math.Max(1, bassEnd);
        _fluxBaseline += (flux - _fluxBaseline) * (flux > _fluxBaseline ? 0.10 : 0.01);
        if (flux > _fluxBaseline * 1.8 && flux > 0.01)
            _onsetFrames = 3;

        // === BASS: rolling window normalize → common mode → 3-stage smooth + punch ===
        _bassWindow[_bassWindowIdx] = bassLevel;
        _bassWindowIdx = (_bassWindowIdx + 1) % PeakWindowSize;
        _bassMaxAge++;
        if (_bassMaxAge >= 30 || bassLevel > _bassWindowMax)
        {
            _bassWindowMax = 0.001;
            for (int i = 0; i < PeakWindowSize; i++)
                if (_bassWindow[i] > _bassWindowMax) _bassWindowMax = _bassWindow[i];
            _bassMaxAge = 0;
        }
        double bassNorm = bassLevel / _bassWindowMax;
        double bcmAlpha = bassNorm > _bassCommon ? 0.03 : 0.35;
        _bassCommon = bcmAlpha * bassNorm + (1 - bcmAlpha) * _bassCommon;
        double bassReactive = Math.Max(0, bassNorm - _bassCommon * 0.65);

        double s1Rise = _onsetFrames > 0 ? 0.95 : 0.75;
        if (_onsetFrames > 0) _onsetFrames--;
        _bassSmooth1 += (bassReactive - _bassSmooth1) * (bassReactive > _bassSmooth1 ? s1Rise : 0.25);
        _bassSmooth2 += (_bassSmooth1 - _bassSmooth2) * (_bassSmooth1 > _bassSmooth2 ? 0.35 : 0.15);
        _bassSmooth3 += (_bassSmooth2 - _bassSmooth3) * (_bassSmooth2 > _bassSmooth3 ? 0.25 : 0.22);
        _bassPunch += (bassReactive - _bassPunch) * (bassReactive > _bassPunch ? 0.70 : 0.45);

        double bassPunchWeight = Math.Min(0.2, Math.Max(0.1, bassReactive * 2.0));
        double bassIntensity = _bassSmooth3 * (1 - bassPunchWeight) + _bassPunch * bassPunchWeight;
        bassIntensity = Math.Min(1.0, Math.Pow(bassIntensity * _settings.IconGlowAudioSensitivity, 0.5));

        // === MIDS: rolling window normalize → common mode → single-stage smooth ===
        _midWindow[_midWindowIdx] = midLevel;
        _midWindowIdx = (_midWindowIdx + 1) % PeakWindowSize;
        _midMaxAge++;
        if (_midMaxAge >= 30 || midLevel > _midWindowMax)
        {
            _midWindowMax = 0.001;
            for (int i = 0; i < PeakWindowSize; i++)
                if (_midWindow[i] > _midWindowMax) _midWindowMax = _midWindow[i];
            _midMaxAge = 0;
        }
        double midNorm = midLevel / _midWindowMax;
        double mcmAlpha = midNorm > _midCommon ? 0.05 : 0.25;
        _midCommon = mcmAlpha * midNorm + (1 - mcmAlpha) * _midCommon;
        double midReactive = Math.Max(0, midNorm - _midCommon * 0.60);
        _midSmooth += (midReactive - _midSmooth) * (midReactive > _midSmooth ? 0.40 : 0.18);
        double midIntensity = Math.Min(1.0, Math.Pow(_midSmooth * _settings.IconGlowAudioSensitivity, 0.6));

        // === TREBLE: rolling window normalize → fast single-stage smooth ===
        _trebleWindow[_trebleWindowIdx] = trebleLevel;
        _trebleWindowIdx = (_trebleWindowIdx + 1) % PeakWindowSize;
        _trebleMaxAge++;
        if (_trebleMaxAge >= 30 || trebleLevel > _trebleWindowMax)
        {
            _trebleWindowMax = 0.001;
            for (int i = 0; i < PeakWindowSize; i++)
                if (_trebleWindow[i] > _trebleWindowMax) _trebleWindowMax = _trebleWindow[i];
            _trebleMaxAge = 0;
        }
        double trebleNorm = trebleLevel / _trebleWindowMax;
        _trebleSmooth += (trebleNorm - _trebleSmooth) * (trebleNorm > _trebleSmooth ? 0.55 : 0.30);
        double trebleIntensity = Math.Min(1.0, _trebleSmooth * _settings.IconGlowAudioSensitivity);

        // === MAP TO VISUAL PROPERTIES ===
        // Bass → outer glow opacity (breathing pulse)
        // Mids → inner halo color shift toward white (melody brightness)
        // Treble → inner halo blur radius (shimmer/sparkle)
        rawIntensity = bassIntensity; // outer glow driven by bass
        _midIntensity = midIntensity;
        _trebleIntensity = trebleIntensity;
    }
    else
    {
        // No spectrum — fall back to RMS/peak
        vizProvider.GetLevels(out float peak, out float rms);
        rawIntensity = rms * 0.7 + peak * 0.3;
        _midIntensity = rawIntensity;
        _trebleIntensity = rawIntensity * 0.5;
    }
}
```

**Step 2: Add `_midIntensity` and `_trebleIntensity` fields** (near `_smoothedIntensity`):
```csharp
private double _smoothedIntensity;
private double _midIntensity;
private double _trebleIntensity;
```

**Step 3: Build**
```bash
dotnet build -c Release 2>&1 | tail -5
```

---

## Task 4: Update Visual Output to Use Per-Band Signals

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs` (visual output block after `_smoothedIntensity = ...`)

**Step 1: Replace the visual output block**

Find and replace from `_smoothedIntensity = Math.Pow(rawIntensity, 0.5);` down through the inner halo block:

```csharp
// Power curve on bass signal for outer glow
_smoothedIntensity = rawIntensity; // already power-curved in band extraction above

// Outer glow: opacity driven by bass (breathing pulse), floor prevents shrink illusion
_currentGlowImage.Opacity = 0.45 + _smoothedIntensity * 0.55;

// Slow rotation
if (_glowRotate != null && _settings.EnableIconGlowSpin)
{
    _glowAngle = (_glowAngle + 360.0 / (_settings.IconGlowSpinSpeed * 60.0)) % 360.0;
    _glowRotate.Angle = _glowAngle;
}

// Inner halo: treble drives blur radius (shimmer), mids drive color shift (melody brightness)
if (_innerGlow != null)
{
    _innerGlow.BlurRadius = 2 + _trebleIntensity * 23;
    _innerGlow.Opacity = 0.3 + _smoothedIntensity * 0.7;

    // Color: base color at rest → 75% toward white as mids rise (synth/melody brightness)
    byte r = (byte)(_glowBaseColor.R + (255 - _glowBaseColor.R) * _midIntensity * 0.75);
    byte g = (byte)(_glowBaseColor.G + (255 - _glowBaseColor.G) * _midIntensity * 0.75);
    byte b = (byte)(_glowBaseColor.B + (255 - _glowBaseColor.B) * _midIntensity * 0.75);
    _innerGlow.Color = Color.FromRgb(r, g, b);
}
```

**Step 2: Build and package**
```bash
cd "c:/Projects/UniPSound/UniPlaySong"
dotnet clean -c Release
dotnet build -c Release 2>&1 | tail -5
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1 2>&1 | tail -3
```
Expected: 0 errors, package created.

**Step 3: Commit**
```bash
git add src/IconGlow/IconGlowManager.cs docs/plans/2026-03-11-icon-glow-iteration4.md
git commit -m "-Icon Glow iteration 4: multi-band reactivity (bass/mids/treble → opacity/color/blur)"
```

---

## Visual Property Mapping Summary

| Signal | Visual Property | Effect |
|--------|----------------|--------|
| Bass (0–250Hz) | Outer glow opacity | Heavy breathing pulse on kicks/bass |
| Mids (250–4000Hz) | Inner halo color → white | Synth/melody brightens the glow |
| Treble (4000–12000Hz) | Inner halo blur radius | Hi-hats/cymbals add shimmer |

## Rollback to Iteration 3

If iteration 4 is worse, restore the single-band fields, init block, and `OnGlowTick` from iteration 3 (documented at top of this file and in `docs/plans/2026-03-10-icon-glow-iteration2.md`).
