using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Playnite.SDK;
using UniPlaySong.Audio;

namespace UniPlaySong.DeskMediaControl
{
    // Compact 12-bar spectrum visualizer for the Desktop top panel.
    // Uses WPF Rectangles with ScaleTransform for GPU-composited animation (no layout invalidation).
    // Animation: peak hold + gravity drop, per-band compression, dirty checking (<0.5px skips GPU writes).
    // 12 frequency bands from 40Hz-11.3kHz, auto-configured for FFT size. Layout: 3px bars, 1px gap, 18px height.
    public class SpectrumVisualizerControl : Canvas
    {
        // Visualization data
        private float[] _spectrumData;
        private float[] _barHeights; // current display height (0..1)
        private int _currentFftSize; // tracks which FFT size the bin ranges are configured for

        // Peak hold + gravity drop state
        private float[] _peakHoldTimer; // seconds remaining at peak before falling
        private float[] _fallVelocity;  // current fall speed (accelerates via gravity)

        // Bar elements
        private Rectangle[] _bars;
        private ScaleTransform[] _barScales;

        // Dirty checking — last committed pixel values
        private float[] _lastScaleY;
        private float[] _lastOpacity;

        // Band bin boundaries, resampled from the reference table for the current bar count
        private int[] _binStarts;
        private int[] _binEnds;

        // Per-bar tuning, resampled from the reference tables for the current bar count.
        // At the reference count these are copies of the tables themselves.
        private float[] _barGain;
        private float[] _bleedFraction;
        private float[] _barGravityScale;

        // Settings accessor (optional — uses defaults if null)
        private Func<UniPlaySongSettings> _getSettings;

        // Layout constants
        //
        // ReferenceBarCount is what every tuning table below is calibrated for - the band
        // boundaries, the per-bar gains, the bleed fractions and the gravity scales all have
        // exactly this many entries. Any other bar count resamples them (see BuildBars), and the
        // resampling is the identity at this count, so the default visualizer is unchanged.
        private const int ReferenceBarCount = 12;
        private const int MinBarCount = 1;
        private const double BarWidth = 3;
        private const double BarGap = 1;
        private const double BarMaxHeight = 18;
        private const double ControlHeight = 20;

        // How many bars this instance is currently drawing.
        private int _barCount = ReferenceBarCount;

        // Animation defaults
        private const float PeakHoldTime = 0.08f;   // seconds to hold at peak before falling (~5 frames)
        private const float Gravity = 8.0f;          // base fall acceleration (units/sec²) — snappy drop
        private const float DefaultOpacityMin = 0.3f;
        private const float MinBarScale = 0.056f;   // ~1px out of 18px
        private const float DirtyThreshold = 0.01f;  // ~0.18px out of 18px — tighter threshold for smoother animation


        // Frame timing (CompositionTarget.Rendering dedup)
        private bool _isRendering;
        private bool _isActive;
        private TimeSpan _lastRenderTime;
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        private long _lastFrameTicks;

        // Fade-out: smooth opacity transition when music stops
        private float _fadeOpacity = 1f;
        private const float FadeOutSpeed = 2.5f; // full fade in ~0.4 seconds

        // Noise floor — below this, bar is zeroed
        private const float NoiseFloor = 0.005f;

        // Frequency bleed — fraction of each bar's energy that bleeds into adjacent bars.
        // Higher values in bass/low-mid simulate how kick energy naturally spreads across bands.
        // Lower values in treble preserve detail and separation.
        private static readonly float[] BleedFraction =
        {
            0.35f, // Bar 0:  sub-bass — kick energy bleeds strongly up
            0.30f, // Bar 1:  bass — kick body bleeds both ways
            0.25f, // Bar 2:  low-mid — significant bleed
            0.20f, // Bar 3:  mid
            0.16f, // Bar 4:  upper-mid
            0.13f, // Bar 5:  vocal presence
            0.11f, // Bar 6:  upper vocal
            0.09f, // Bar 7:  low presence
            0.07f, // Bar 8:  presence
            0.06f, // Bar 9:  detail — less bleed preserves clarity
            0.05f, // Bar 10: shimmer
            0.04f, // Bar 11: treble — minimal bleed
        };

        // Scratch buffers for bleed computation (avoids allocation per frame)
        private float[] _bleedBuffer;
        private float[] _bleedOrig;

        // UI-side smoothing — second pass of asymmetric EMA (fast rise, smooth fall). This smooths the post-bleed
        // signal before the peak-hold animation, removing frame-to-frame jitter while preserving sharp beat attacks.
        // Rise/fall alphas are configurable via VizSmoothRise / VizSmoothFall settings.
        private float[] _smoothedTarget;

        // Per-bar direct gain — calibrated for -80dB FFT range with squared curve.
        // Bass has naturally very high FFT energy after squaring (~0.2-0.4 RMS),
        // so bass gains are kept well below 1.0 to prevent pegging.
        // Treble energy is spread thin across many bins (~0.01-0.05 RMS),
        // so treble gains are much higher to compensate.
        // Target: all bars averaging ~0.3-0.5 with peaks around 0.7.
        private static readonly float[] BarGain =
        {
            1.4f,  // Bar 0:  43-108 Hz    — strongest raw energy, moderate attenuation
            1.7f,  // Bar 1:  108-194 Hz
            2.2f,  // Bar 2:  194-301 Hz
            2.8f,  // Bar 3:  301-452 Hz
            3.6f,  // Bar 4:  452-732 Hz
            4.5f,  // Bar 5:  732-1.0k Hz
            5.5f,  // Bar 6:  1.0k-1.4k Hz
            7.0f,  // Bar 7:  1.4k-2.0k Hz
            9.0f,  // Bar 8:  2.0k-2.8k Hz
            12.0f, // Bar 9:  2.8k-4.2k Hz
            16.0f, // Bar 10: 4.2k-7.0k Hz
            20.0f, // Bar 11: 7.0k-11.3k Hz
        };

        // Per-bar gravity multiplier — bass drops fast (punchy), treble floats (shimmery)
        private static readonly float[] BarGravityScale =
        {
            1.4f,  // Bar 0:  sub-bass — snappy kick response
            1.3f,  // Bar 1:  bass
            1.2f,  // Bar 2:  low-mid
            1.1f,  // Bar 3:  mid
            1.0f,  // Bar 4:  upper-mid — baseline
            0.95f, // Bar 5:  vocal presence
            0.90f, // Bar 6:  upper vocal
            0.85f, // Bar 7:  low presence
            0.80f, // Bar 8:  presence
            0.75f, // Bar 9:  detail
            0.65f, // Bar 10: shimmer — floaty
            0.55f, // Bar 11: treble — slowest drop
        };

        // Bar color — Classic white brush (default, preserved as static frozen brush).
        // Alpha=200: softened peak brightness; per-bar Opacity handles dynamic range.
        private static readonly SolidColorBrush BarBrush =
            new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

        // Color theme definitions: (bottom gradient stop, top gradient stop)
        // Index matches VizColorTheme enum values. Index 0/14/21 = Dynamic V1/Vivid/Alt (unused placeholders — reads from settings).
        // Classic: alpha=200 (softened white). Color themes: alpha=255 (full opacity, colors already darkened).
        private static readonly (Color Bottom, Color Top)[] ThemeColors =
        {
            (Color.FromArgb(200, 255, 255, 255), Color.FromArgb(200, 255, 255, 255)),  // [0] Dynamic V1: placeholder (never read)
            (Color.FromArgb(200, 255, 255, 255), Color.FromArgb(200, 255, 255, 255)),  // [1] Classic: white
            (Color.FromArgb(255,   0, 140, 170), Color.FromArgb(255, 220,  40, 220)),  // [2] Neon: dark cyan→magenta
            (Color.FromArgb(255, 170,  90,  10), Color.FromArgb(255, 220,  30,  15)),  // [3] Fire: dark orange→red
            (Color.FromArgb(255,   0, 140, 150), Color.FromArgb(255,  45, 100, 220)),  // [4] Ocean: dark teal→blue
            (Color.FromArgb(255, 170, 140,  20), Color.FromArgb(255, 220,  65, 100)),  // [5] Sunset: dark yellow→warm pink
            (Color.FromArgb(255,  10, 140,  10), Color.FromArgb(255,  65, 220,  80)),  // [6] Matrix: dark green→bright green
            (Color.FromArgb(255,  90, 140, 180), Color.FromArgb(255,  60, 170, 220)),  // [7] Ice: dark blue→ice blue
            (Color.FromArgb(255,  50,   0, 120), Color.FromArgb(255, 255,  40, 180)),  // [8] Synthwave: deep purple→hot pink
            (Color.FromArgb(255, 140,  30,   0), Color.FromArgb(255, 255, 160,  20)),  // [9] Ember: dark ember→bright amber
            (Color.FromArgb(255,   0,  40, 120), Color.FromArgb(255,   0, 200, 220)),  // [10] Abyss: deep navy→aqua
            (Color.FromArgb(255, 180,  50,  20), Color.FromArgb(255, 255, 200,  40)),  // [11] Solar: warm rust→golden yellow
            (Color.FromArgb(255,  30, 160, 120), Color.FromArgb(255, 180, 140, 255)),  // [12] Vapor: mint green→lavender
            (Color.FromArgb(255,  40,  80, 140), Color.FromArgb(255, 180, 230, 255)),  // [13] Frost: steel blue→frost white
            (Color.FromArgb(200, 255, 255, 255), Color.FromArgb(200, 255, 255, 255)),  // [14] Dynamic V2: placeholder (never read)
            (Color.FromArgb(255,   0, 130, 120), Color.FromArgb(255, 160, 255,  50)),  // [15] Aurora: deep teal→lime green
            (Color.FromArgb(255, 200,  60,  80), Color.FromArgb(255, 255, 180, 100)),  // [16] Coral: deep coral→peach gold
            (Color.FromArgb(255,  70,  20, 200), Color.FromArgb(255, 255,  50,  80)),  // [17] Plasma: electric indigo→hot magenta-red
            (Color.FromArgb(255, 160, 210,   0), Color.FromArgb(255, 120,  30, 200)),  // [18] Toxic: acid yellow-green→deep purple
            (Color.FromArgb(255, 130,  10,  50), Color.FromArgb(255, 255, 120, 160)),  // [19] Cherry: dark cherry→rose pink
            (Color.FromArgb(255,  15,  10,  80), Color.FromArgb(255,   0, 220, 255)),  // [20] Midnight: dark indigo→bright cyan
            (Color.FromArgb(200, 255, 255, 255), Color.FromArgb(200, 255, 255, 255)),  // [21] Dynamic Alt: placeholder (never read)
        };

        // Cached theme state for dirty checking (avoid brush recreation every frame)
        private int _lastThemeIndex = -1;
        private bool _lastGradientEnabled = true;

        // Dynamic theme dirty checking — colors change per game, not per theme index
        private Color _lastDynamicBottom;
        private Color _lastDynamicTop;

        public SpectrumVisualizerControl()
        {
            // Width follows the bar count and is set by BuildBars.
            Height = ControlHeight;
            ClipToBounds = true;
            Margin = new Thickness(4, 0, 4, 0);
            Background = Brushes.Transparent;

            BarBrush.Freeze();

            BuildBars(ReferenceBarCount);

            Visibility = Visibility.Collapsed;
        }

        // (Re)creates the bar rectangles and every per-bar buffer for a given count, and resamples
        // the reference tuning tables onto it. Cheap - a handful of Rectangles - and only runs when
        // the count actually changes, so it can be driven straight from the setting.
        private void BuildBars(int count)
        {
            count = Math.Max(MinBarCount, Math.Min(ReferenceBarCount, count));
            _barCount = count;

            Children.Clear();

            _barHeights = new float[count];
            _peakHoldTimer = new float[count];
            _fallVelocity = new float[count];
            _lastScaleY = new float[count];
            _lastOpacity = new float[count];
            _bars = new Rectangle[count];
            _barScales = new ScaleTransform[count];
            _binStarts = new int[count];
            _binEnds = new int[count];
            _bleedBuffer = new float[count];
            _bleedOrig = new float[count];
            _smoothedTarget = new float[count];

            // Resample the per-bar tuning. Each bar samples the reference tables at its own LOW
            // frequency edge rather than its centre: RMS across a band is dominated by its lowest
            // frequencies, so a wide bar behaves like the bass it contains. Centre-sampling would
            // hand a 1-bar visualizer a treble gain of ~5 and peg it solid forever.
            _barGain = new float[count];
            _bleedFraction = new float[count];
            _barGravityScale = new float[count];
            for (int i = 0; i < count; i++)
            {
                double pos = i * (double)ReferenceBarCount / count;
                _barGain[i] = SampleTable(BarGain, pos);
                _bleedFraction[i] = SampleTable(BleedFraction, pos);
                _barGravityScale[i] = SampleTable(BarGravityScale, pos);
            }

            // Bin ranges depend on the count, so force a reconfigure at the size already in use.
            int fftSize = _currentFftSize > 0 ? _currentFftSize : 1024;
            _currentFftSize = 0;
            ConfigureBinRanges(fftSize);

            Width = (BarWidth + BarGap) * count - BarGap;

            // Vertical offset to center the bars in the control
            double yOffset = (ControlHeight - BarMaxHeight) / 2.0;

            for (int i = 0; i < count; i++)
            {
                // ScaleTransform with origin at bottom of the bar
                var scale = new ScaleTransform(1.0, MinBarScale, 0, BarMaxHeight);
                _barScales[i] = scale;

                var bar = new Rectangle
                {
                    Width = BarWidth,
                    Height = BarMaxHeight,
                    Fill = BarBrush,
                    RenderTransform = scale,
                    SnapsToDevicePixels = true
                };

                Canvas.SetLeft(bar, i * (BarWidth + BarGap));
                Canvas.SetTop(bar, yOffset);
                Children.Add(bar);
                _bars[i] = bar;
            }

            // Brushes are applied by the per-frame dirty check; force it to run for the new bars.
            _lastThemeIndex = -1;
        }

        // Linear interpolation into a reference table at a fractional index. Exact at whole
        // positions, so at ReferenceBarCount every bar reads its own original entry.
        private static float SampleTable(float[] table, double pos)
        {
            int lo = (int)Math.Floor(pos);
            if (lo >= table.Length - 1) return table[table.Length - 1];
            if (lo < 0) return table[0];
            return table[lo] + (float)((table[lo + 1] - table[lo]) * (pos - lo));
        }

        // Provide a settings accessor so the visualizer can read tuning parameters live
        public void SetSettingsProvider(Func<UniPlaySongSettings> getSettings)
        {
            _getSettings = getSettings;
        }

        // Checks if color theme changed and updates bar brushes (cheap int+bool dirty check per frame)
        private void UpdateBarBrushes(UniPlaySongSettings settings)
        {
            int themeIndex = settings?.VizColorTheme ?? 0;
            bool gradientEnabled = settings?.VizGradientEnabled ?? true;

            // Dynamic themes (V1 + V2): dirty-check on actual color values (they change per game, not per theme index)
            bool isDynamic = themeIndex == (int)VizColorTheme.Dynamic || themeIndex == (int)VizColorTheme.DynamicVivid || themeIndex == (int)VizColorTheme.DynamicAlt;
            if (isDynamic)
            {
                var dynBottom = settings.DynamicColorBottom;
                var dynTop = settings.DynamicColorTop;
                bool colorsChanged = dynBottom != _lastDynamicBottom || dynTop != _lastDynamicTop;

                if (themeIndex == _lastThemeIndex && gradientEnabled == _lastGradientEnabled && !colorsChanged)
                    return;

                _lastThemeIndex = themeIndex;
                _lastGradientEnabled = gradientEnabled;
                _lastDynamicBottom = dynBottom;
                _lastDynamicTop = dynTop;

                ApplyBrush(dynBottom, dynTop, gradientEnabled);
                return;
            }

            if (themeIndex == _lastThemeIndex && gradientEnabled == _lastGradientEnabled)
                return;

            _lastThemeIndex = themeIndex;
            _lastGradientEnabled = gradientEnabled;

            // Classic theme — always use the original static frozen brush
            if (themeIndex == (int)VizColorTheme.Classic)
            {
                for (int i = 0; i < _barCount; i++)
                    _bars[i].Fill = BarBrush;
                return;
            }

            // Clamp to valid theme range
            if (themeIndex < 0 || themeIndex >= ThemeColors.Length)
                themeIndex = 0;

            var (bottom, top) = ThemeColors[themeIndex];
            ApplyBrush(bottom, top, gradientEnabled);
        }

        private void ApplyBrush(Color bottom, Color top, bool gradientEnabled)
        {
            Brush brush;
            if (gradientEnabled)
            {
                var gradient = new LinearGradientBrush(bottom, top, new Point(0, 1), new Point(0, 0));
                gradient.Freeze();
                brush = gradient;
            }
            else
            {
                var solid = new SolidColorBrush(bottom);
                solid.Freeze();
                brush = solid;
            }

            for (int i = 0; i < _barCount; i++)
                _bars[i].Fill = brush;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (active)
            {
                _fadeOpacity = 1f;
                Opacity = 1.0;
                Visibility = Visibility.Visible;
                StartRendering();
            }
        }

        // Configure bin ranges for the given FFT size (auto-reconfigures if provider's FFT size changes)
        private void ConfigureBinRanges(int fftSize)
        {
            if (fftSize == _currentFftSize) return;
            _currentFftSize = fftSize;

            int spectrumSize = fftSize / 2;
            _spectrumData = new float[spectrumSize];

            // Bin resolution: sampleRate / fftSize (e.g., 44100/2048 = ~21.5Hz)
            // Convert frequency to bin index: freq / binResolution = freq * fftSize / 44100
            // Using 44100 as reference — other sample rates will shift slightly but 12 bars is forgiving.
            double scale = fftSize / 44100.0;

            // The reference band edges (Hz), same regardless of FFT size. Thirteen edges for the
            // twelve reference bars. Hand-tuned rather than evenly spaced - the low end is
            // deliberately compressed relative to a pure logarithmic split, so the bass bars stay
            // readable - which is why other counts interpolate THIS curve instead of recomputing
            // one. Interpolating in log-frequency space keeps the ratio between neighbouring bands
            // constant, which is what makes a band split look even to the ear.
            for (int i = 0; i < _barCount; i++)
            {
                double startHz = EdgeAt(i * (double)ReferenceBarCount / _barCount);
                double endHz = EdgeAt((i + 1) * (double)ReferenceBarCount / _barCount);

                _binStarts[i] = Math.Max(1, (int)(startHz * scale));
                _binEnds[i] = Math.Min(spectrumSize, (int)(endHz * scale) + 1);
                // Ensure at least 1 bin per bar
                if (_binEnds[i] <= _binStarts[i]) _binEnds[i] = _binStarts[i] + 1;
            }
        }

        // Band edges for the reference layout: BandEdges[i] is where reference bar i starts, and
        // the final entry closes the last band.
        private static readonly double[] BandEdges =
        { 40, 108, 194, 301, 452, 732, 1034, 1421, 2024, 2821, 4214, 7024, 11310 };

        // Interpolates the reference edge curve at a fractional position, in log-frequency space so
        // the spacing stays musically even. Exact at whole positions, so at ReferenceBarCount the
        // bands come out as the table's own numbers.
        private static double EdgeAt(double pos)
        {
            int lo = (int)Math.Floor(pos);
            if (lo >= BandEdges.Length - 1) return BandEdges[BandEdges.Length - 1];
            if (lo < 0) return BandEdges[0];

            double frac = pos - lo;
            if (frac <= 0) return BandEdges[lo];

            double a = Math.Log(BandEdges[lo]);
            double b = Math.Log(BandEdges[lo + 1]);
            return Math.Exp(a + (b - a) * frac);
        }

        private void StartRendering()
        {
            if (_isRendering) return;
            _isRendering = true;
            _frameStopwatch.Restart();
            _lastFrameTicks = 0;
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }

        private void StopRendering()
        {
            if (!_isRendering) return;
            _isRendering = false;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
        }

        private void OnCompositionTargetRendering(object sender, EventArgs e)
        {
            var args = (RenderingEventArgs)e;
            if (args.RenderingTime == _lastRenderTime)
                return;
            _lastRenderTime = args.RenderingTime;

            // High-resolution delta time via Stopwatch (sub-microsecond precision).
            // Eliminates the ~15ms DateTime.UtcNow granularity that required EMA smoothing.
            long nowTicks = _frameStopwatch.ElapsedTicks;
            float dt = (float)(nowTicks - _lastFrameTicks) / Stopwatch.Frequency;
            _lastFrameTicks = nowTicks;
            if (dt > 0.2f || dt <= 0f) dt = 0.016f;

            // Read tuning from settings (cached per frame, no allocation)
            var settings = _getSettings?.Invoke();

            // Rebuild if the user changed how many bars they want (cheap int compare per frame).
            // Live rather than restart-gated: the whole point of the setting is fitting the bars to
            // a theme's top panel, which is a look-and-adjust job.
            int wantBars = settings?.VizBarCount ?? ReferenceBarCount;
            if (wantBars != _barCount)
                BuildBars(wantBars);

            // Update bar brushes if color theme changed (cheap dirty check)
            UpdateBarBrushes(settings);

            float opacityMin = (settings?.VizOpacityMin ?? 30) / 100f;
            float gainMult = 1f + (settings?.VizBarGainBoost ?? 0) / 100f;
            float peakHold = (settings?.VizPeakHoldMs ?? 80) / 1000f;  // ms → seconds
            float gravity = (settings?.VizGravity ?? 120) / 10f;        // tenths → actual
            float biasPct = (settings?.VizBassGravityBias ?? 50) / 100f; // 0..1
            float bassGainMult = (settings?.VizBassGain ?? 100) / 100f;   // bass gain scale
            float trebleGainMult = (settings?.VizTrebleGain ?? 100) / 100f; // treble gain scale
            float bleedScale = (settings?.VizBleedAmount ?? 100) / 100f;  // bleed multiplier
            float compressionPct = (settings?.VizCompression ?? 50) / 100f; // 0..1
            float smoothRise = (settings?.VizSmoothRise ?? 85) / 100f;    // UI EMA rise alpha
            float smoothFall = (settings?.VizSmoothFall ?? 15) / 100f;    // UI EMA fall alpha

            var provider = VisualizationDataProvider.Current;
            bool hasData = provider != null && _isActive;

            if (hasData)
            {
                // Auto-reconfigure bin ranges if provider's FFT size changed
                if (provider.FftSize != _currentFftSize)
                    ConfigureBinRanges(provider.FftSize);

                provider.GetSpectrumData(_spectrumData, 0, _spectrumData.Length);
            }

            bool anyBarVisible = false;

            // === Pass 1: Compute raw bar targets ===
            for (int i = 0; i < _barCount; i++)
            {
                _bleedBuffer[i] = 0f;

                if (hasData)
                {
                    int end = Math.Min(_binEnds[i], _spectrumData.Length);
                    int binCount = end - _binStarts[i];

                    // RMS energy across all bins in the band
                    float sumSq = 0f;
                    for (int bin = _binStarts[i]; bin < end; bin++)
                    {
                        float v = _spectrumData[bin];
                        sumSq += v * v;
                    }
                    float rms = binCount > 0 ? (float)Math.Sqrt(sumSq / binCount) : 0f;

                    // Apply per-bar gain with bass/treble scaling
                    // Lower half bass, upper half treble - i * 2 < count is the reference split
                    // (bars 0-5 of 12) generalised to any count.
                    float regionMult = i * 2 < _barCount ? bassGainMult : trebleGainMult;
                    float value = rms * _barGain[i] * gainMult * regionMult;

                    // Noise floor gate
                    if (rms < NoiseFloor) value = 0f;

                    // Soft-knee compression: knee point varies with compressionPct.
                    // compressionPct=0: no compression (linear passthrough)
                    // compressionPct=0.5: knee at 0.5, moderate compression
                    // compressionPct=1.0: knee at 0.2, heavy compression
                    if (compressionPct > 0f)
                    {
                        float knee = 1f - compressionPct * 0.8f; // 1.0 → 0.2
                        if (value > knee)
                        {
                            float excess = value - knee;
                            float range = 1f - knee;
                            value = knee + range * excess / (excess + range);
                        }
                    }

                    // Clamp to 0..1
                    if (value > 1f) value = 1f;

                    _bleedBuffer[i] = value;
                }
            }

            // === Pass 2: Frequency bleed — donate energy to adjacent bars ===
            // Each bar shares a fraction of its energy with neighbors.
            // This makes kicks feel more "whole" across the low end and
            // creates natural frequency coupling throughout the spectrum.
            if (hasData && bleedScale > 0f)
            {
                // Copy pre-bleed values to avoid feedback within the same pass
                Array.Copy(_bleedBuffer, _bleedOrig, _barCount);

                for (int i = 0; i < _barCount; i++)
                {
                    float bleed = 0f;
                    // Receive bleed from left neighbor
                    if (i > 0)
                        bleed += _bleedOrig[i - 1] * _bleedFraction[i - 1];
                    // Receive bleed from right neighbor
                    if (i < _barCount - 1)
                        bleed += _bleedOrig[i + 1] * _bleedFraction[i + 1];

                    _bleedBuffer[i] = Math.Min(1f, _bleedBuffer[i] + bleed * bleedScale);
                }
            }

            // === Pass 3: UI-side asymmetric smoothing ===
            // Fast rise (beats punch through), slow fall (smooth trailing).
            // This runs at display rate (~60fps) so it smooths the ~43fps FFT output.
            for (int i = 0; i < _barCount; i++)
            {
                float raw = _bleedBuffer[i];
                float prev = _smoothedTarget[i];
                float alpha = raw >= prev ? smoothRise : smoothFall;
                _smoothedTarget[i] = prev + (raw - prev) * alpha;
            }

            // === Pass 4: Animation (peak hold + gravity drop) ===
            for (int i = 0; i < _barCount; i++)
            {
                float target = _smoothedTarget[i];

                // Peak hold + gravity drop animation
                // No EMA smoothing — FFT temporal smoothing handles jitter at the source,
                // and peak-hold + gravity provides all the visual smoothing needed on fall.
                float current = _barHeights[i];
                if (target > current)
                {
                    // Instant snap to new peak, reset hold timer and velocity
                    current = target;
                    _peakHoldTimer[i] = peakHold;
                    _fallVelocity[i] = 0f;
                }
                else if (_peakHoldTimer[i] > 0f)
                {
                    // Hold at peak for a few frames
                    _peakHoldTimer[i] -= dt;
                }
                else
                {
                    // Gravity-accelerated fall (per-bar: bass snappy, treble floaty)
                    // Lerp between uniform (1.0) and full contrast (BarGravityScale) based on bias setting
                    float barScale = 1f + (_barGravityScale[i] - 1f) * biasPct;
                    float barGravity = gravity * barScale;
                    _fallVelocity[i] += barGravity * dt;
                    current -= _fallVelocity[i] * dt;
                    if (current < target) current = target;
                    if (current < 0f) current = 0f;
                }
                _barHeights[i] = current;

                // Dirty checking — only update GPU properties if pixel change > 0.5px
                float scaleY = current > MinBarScale ? current : MinBarScale;
                // Sqrt curve for brightness modulation — brighter bars at lower amplitudes.
                float opacity = opacityMin + (1f - opacityMin) * (float)Math.Sqrt(current);

                float scaleDelta = scaleY - _lastScaleY[i];
                if (scaleDelta < 0) scaleDelta = -scaleDelta;
                float opacityDelta = opacity - _lastOpacity[i];
                if (opacityDelta < 0) opacityDelta = -opacityDelta;

                if (scaleDelta > DirtyThreshold || opacityDelta > DirtyThreshold)
                {
                    _barScales[i].ScaleY = scaleY;
                    _bars[i].Opacity = opacity;
                    _lastScaleY[i] = scaleY;
                    _lastOpacity[i] = opacity;
                }

                if (current > 0.005f)
                    anyBarVisible = true;
            }

            // Visualization rendering (no diagnostic logging - use performance profiler if needed)

            // Smooth fade-out when music stops: bars decay first, then control fades to transparent
            if (!_isActive)
            {
                if (!anyBarVisible)
                {
                    // Bars have fully decayed — now fade the whole control's opacity
                    _fadeOpacity -= FadeOutSpeed * dt;
                    if (_fadeOpacity <= 0f)
                    {
                        _fadeOpacity = 0f;
                        Opacity = 0;
                        Visibility = Visibility.Collapsed;
                        StopRendering();
                        return;
                    }
                    Opacity = _fadeOpacity;
                }
            }
        }
    }
}
