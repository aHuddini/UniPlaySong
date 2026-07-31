# Icon Glow Reactivity — Iteration 2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the adaptive gain control (AGC) with a rolling-window peak normalizer to get consistent, musical reactivity across all genres and volume levels.

**Architecture:** The current `_gain` field (exponential AGC that chases the signal) is replaced by a circular buffer of recent peak values (5-second window at 60fps = 300 samples). Normalization divides current level by the rolling window max, which is stable and never chases transients. Common mode subtraction and three-stage smoothing are preserved. Decay coefficients on `_smooth3` and `_punch` are increased for cleaner dark→bright→dark breathing.

**Tech Stack:** C#, `OnGlowTick` in `src/IconGlow/IconGlowManager.cs`. No new dependencies.

---

## Context: Iteration 1 Algorithm (for reference / rollback)

The current algorithm in `OnGlowTick` (lines ~280–388) is **Iteration 1**. Key fields:

```
_gain          — exponential AGC: rise 0.98, decay 0.002. Problem: chases peaks, flattens signal.
_commonMode    — baseline tracker: rise 0.03, decay 0.35. Subtract 65%.
_smooth1/2/3   — three-stage: (0.75/0.25), (0.35/0.15), (0.25/0.10). Decay 0.10 too slow.
_punch         — rise 0.90, decay 0.40.
_onsetFrames   — 3-frame boost when spectral flux > 1.8x baseline.
```

**Problems identified:**
- A) `_gain` rise of 0.98 means it tracks the signal almost as fast as it rises → `normalized = level/_gain` stays near 1.0, AGC eats all peaks
- B) Because normalized is always ~1.0, `_commonMode` subtracts most of the signal → inconsistent beat response
- C) `_smooth3` decay 0.10 at 60fps = ~6 seconds to fall to 37% → blurs rhythm, stays bright between beats

---

## Task 1: Replace AGC with Rolling Window Peak Normalizer

**Files:**
- Modify: `src/IconGlow/IconGlowManager.cs`

**What to do:**

### Step 1: Replace `_gain` field with rolling window fields

In the field declarations block (~lines 37–47), **remove** `_gain` and **add**:

```csharp
// Rolling window peak normalizer (replaces AGC)
// 300 frames @ 60fps = ~5 second window
private const int PeakWindowSize = 300;
private double[] _peakWindow;   // circular buffer of recent peak levels
private int _peakWindowIdx;     // write index into circular buffer
private double _peakWindowMax;  // cached max of the window (recomputed when needed)
private int _peakMaxAge;        // frames since last full recompute (recompute every 30 frames)
```

### Step 2: Replace AGC initialization in `ApplyGlowInternal`

Find (~line 224):
```csharp
_gain = 0.01;
```

Replace with:
```csharp
_peakWindow = new double[PeakWindowSize];
_peakWindowIdx = 0;
_peakWindowMax = 0.001;
_peakMaxAge = 0;
```

### Step 3: Replace AGC logic in `OnGlowTick`

Find the AGC block (~lines 326–330):
```csharp
// AGC
double gainAlpha = level > _gain ? 0.98 : 0.002;
_gain = gainAlpha * level + (1.0 - gainAlpha) * _gain;
_gain = Math.Max(_gain, 0.001);
double normalized = level / _gain;
```

Replace with:
```csharp
// Rolling window peak normalization
_peakWindow[_peakWindowIdx] = level;
_peakWindowIdx = (_peakWindowIdx + 1) % PeakWindowSize;

// Recompute max every 30 frames (every ~0.5s) or when current level exceeds cached max
_peakMaxAge++;
if (_peakMaxAge >= 30 || level > _peakWindowMax)
{
    _peakWindowMax = 0.001;
    for (int i = 0; i < PeakWindowSize; i++)
        if (_peakWindow[i] > _peakWindowMax)
            _peakWindowMax = _peakWindow[i];
    _peakMaxAge = 0;
}

double normalized = level / _peakWindowMax;
```

### Step 4: Fix decay coefficients — `_smooth3` and `_punch`

Find (~line 344):
```csharp
_smooth3 += (_smooth2 - _smooth3) * (_smooth2 > _smooth3 ? 0.25 : 0.10);
```
Change decay from `0.10` → `0.22`:
```csharp
_smooth3 += (_smooth2 - _smooth3) * (_smooth2 > _smooth3 ? 0.25 : 0.22);
```

Find (~line 347):
```csharp
_punch += (reactive - _punch) * (reactive > _punch ? 0.90 : 0.40);
```
Change decay from `0.40` → `0.55`:
```csharp
_punch += (reactive - _punch) * (reactive > _punch ? 0.90 : 0.55);
```

### Step 5: Build and verify

```bash
cd "c:/Projects/UniPSound/UniPlaySong"
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

Expected: 0 errors, package created.

### Step 6: Commit

```bash
git add src/IconGlow/IconGlowManager.cs
git commit -m "-Icon Glow iteration 2: rolling window peak normalizer, faster decay"
```

---

## What Changes and Why

| Problem | Iteration 1 | Iteration 2 |
|---------|-------------|-------------|
| AGC eats peaks (A) | `_gain` rises at 0.98 — chases signal | Rolling 5s window — stable reference, peaks stay as peaks |
| Beats inconsistent (B) | Normalized ~1.0 always → common mode eats signal | Normalized relative to recent history → beats reliably exceed baseline |
| Slow decay (C) | `_smooth3` decay 0.10 (~6s fall) | `_smooth3` decay 0.22 (~2.5s fall), `_punch` decay 0.55 |
| Silence → burst | `_gain` decays slowly → first beat after silence is flattened | Window zeros out during silence → first beat is full intensity |

## Rollback

If iteration 2 is worse, revert to iteration 1 by restoring:
- `_gain` field replacing `_peakWindow*` fields
- AGC init (`_gain = 0.01`)
- AGC logic block
- Original decay: `_smooth3` 0.10, `_punch` 0.40
