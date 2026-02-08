# Dynamic Color Extraction Algorithm

Internal reference for the Dynamic visualizer color theme. Documents the quantized histogram algorithm used by `GameColorExtractor` and its version history.

## Current Algorithm (v7)

**File:** `Services/GameColorExtractor.cs`

### Pipeline

1. **Load & downsample** image to 100px wide via `BitmapImage.DecodePixelWidth` (WPF native, handles JPG/PNG/WebP)
2. **Convert** to BGRA32 pixel format for consistent byte-level access
3. **Quantize** each pixel to 4-bit per channel (16 levels R/G/B = 4096 color buckets)
4. **Filter** pixels before bucketing:
   - Skip transparent (alpha < 128)
   - Skip near-black (R+G+B < 30)
   - Skip near-white (R+G+B > 720)
   - Skip achromatic (max channel - min channel < 10)
5. **Center-weighted sampling**: each pixel's contribution is weighted by proximity to image center. Weight = `1.0 - 0.7 * (dist / maxDist)`, so center pixels count ~3.3x more than corner pixels. Game art subjects are typically centered; dark borders and UI chrome at edges are de-emphasized.
6. **Neighbor bucket merging**: after histogram, small buckets (< 10% of max) are absorbed into their largest direct neighbor (±1 per R/G/B channel). Fixes color splitting at quantization boundaries (e.g., R=127 vs R=128 landing in different buckets).
7. **Rank** buckets by weighted count, take top 10 clusters
8. **Select bottom color** (gradient base): first cluster whose average max channel >= 65 (IntensityThreshold). Falls back to most common cluster if none qualify.
9. **Select top color** (gradient tip): highest-scoring cluster passing intensity gate, scored by:
   ```
   score = saturation * 0.4 + (brightness / 255) * 0.35 + frequencyWeight * 0.25
   ```
   where `frequencyWeight = clusterCount / topClusterCount`
   Plus a **diversity bonus** (+0.15) for clusters with Euclidean RGB distance >= 40 from the bottom color. Ensures gradient has visual contrast.
10. **Post-process** both colors:
    - `EnsureMinBrightness`: scales all channels proportionally so max channel reaches floor (preserves hue)
    - `EnsureMinSaturation`: pushes non-dominant channels down so HSV saturation reaches floor

### User-Configurable Parameters

| Parameter | Setting Property | Default | Range | Effect |
|-----------|-----------------|---------|-------|--------|
| Min Brightness (Base) | `DynMinBrightnessBottom` | 100 | 30-220 | Brightness floor for gradient base |
| Min Brightness (Tip) | `DynMinBrightnessTop` | 140 | 30-220 | Brightness floor for gradient tip |
| Min Saturation (Base) | `DynMinSatBottom` | 30% | 0-80% | Saturation floor for gradient base |
| Min Saturation (Tip) | `DynMinSatTop` | 35% | 0-80% | Saturation floor for gradient tip |

### Fixed Constants

| Constant | Value | Rationale |
|----------|-------|-----------|
| DecodePixelWidth | 100 | ~10k pixels for sampling; fast enough for background thread |
| Quantization | 4-bit/channel | 4096 buckets — groups similar colors without losing distinction |
| IntensityThreshold | 65 | Skip dark clusters (dark grays, dark browns) that won't produce visible bars |
| TopN clusters | 10 | Enough variety without picking noise |
| Near-black cutoff | sum < 30 | Excludes near-black pixels from bucketing |
| Near-white cutoff | sum > 720 | Excludes near-white pixels from bucketing |
| Achromatic cutoff | range < 10 | Excludes pure grays but allows muted tones |
| Center weight range | 1.0 → 0.3 | Center pixels 3.3x more influential than corners |
| Merge threshold | 10% of max | Small buckets absorbed into largest neighbor |
| MinColorDistance | 40 | Euclidean RGB distance for diversity bonus |
| Diversity bonus | +0.15 | Score bonus for top candidates distant from bottom |

---

## Caching

**File:** `Services/DynamicColorCache.cs`

- Persisted to `dynamic_colors.json` in extension data folder
- Key: `Game.Id` (GUID string)
- Cache hit requires match on: algorithm version, image path hash, AND all 4 tuning parameters
- Changing any slider value causes cache misses, triggering lazy re-extraction per game
- Atomic write pattern (temp file + move) matches `SearchCacheService`

---

## Version History

### v1 (initial)
- 64px downsample, 4096 buckets
- Gray filter: max-min < 25 (aggressive)
- Bottom color: most common cluster, darkened by flat 30%
- Top color: best saturation+brightness score
- Post-process: EnsureMinSaturation only (40%)
- **Issue:** Washed out/dark colors on some games. Flat darkening crushed already-dark images. Aggressive gray filter excluded muted game art.

### v2
- Relaxed gray filter: max-min < 10
- Adaptive darkening instead of flat 30%
- Added EnsureMinBrightness (bottom=60, top=90)
- Frequency weight added to top color scoring
- **Issue:** "Loses colors overall" per user testing. Adaptive darkening still too aggressive for dark artwork.

### v3 (reverted)
- Vibrancy-based scoring for BOTH colors (brightness*saturation over frequency)
- **Reverted immediately:** Picked unrepresentative accent colors instead of dominant image tones. User explicitly rejected.

### v4
- Reverted to v2 structure with targeted fixes
- Intensity threshold gate: max channel >= 50 for both color selections
- No darkening on bottom color (removed entirely)
- Stronger min brightness floors: bottom=70, top=100
- Min saturation: bottom=0.25, top=0.30
- **Feedback:** Still not aggressive enough for naturally darker game images.

### v5
- Raised IntensityThreshold: 50 -> 65
- Raised EnsureMinBrightness: bottom 70->100, top 100->140
- Raised EnsureMinSaturation: bottom 0.25->0.30, top 0.30->0.35
- Added user-configurable parameters (4 sliders in settings)
- Cache now stores tuning params for per-parameter invalidation
- VizColorTheme enum reordered: Dynamic=0 (default), Classic=1, etc.
- Migration logic for existing users' saved theme index

### v6
- Increased downsample resolution: 64px -> 100px (~2.4x more pixels for sampling)
- Better color representation from game artwork, especially for images with small colorful details
- All other parameters unchanged from v5

### v7 (current)
- **Center-weighted sampling**: pixels near image center weighted 3.3x more than corners. Game art subjects are centered; dark borders and UI chrome at edges were skewing results.
- **Neighbor bucket merging**: small buckets (< 10% of max) absorbed into largest adjacent neighbor (±1 per channel). Fixes color splitting at 4-bit quantization boundaries (e.g., a dominant color straddling R=127/R=128 was split into two weak buckets).
- **Top color diversity bonus**: +0.15 score bonus for clusters with Euclidean RGB distance >= 40 from bottom color. Prevents same-color gradients when one dominant color overwhelms both selections.
- Histogram arrays changed from `int`/`long` to `float`/`double` to support fractional center weights.
- **Issue addressed:** "Odd images" where edge noise or quantization boundary effects produced questionable color sampling.
