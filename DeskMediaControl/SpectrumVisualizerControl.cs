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
    /// <summary>
    /// Compact 12-bar spectrum visualizer for the Desktop top panel.
    /// Uses WPF Rectangle elements with ScaleTransform for GPU-composited animation.
    /// Only ScaleY changes per frame — no layout invalidation, no bitmap writes.
    /// CompositionTarget.Rendering drives updates synced to WPF's render pipeline (~60fps).
    ///
    /// === Animation Model (v5 — 12-bar peak hold + gravity drop) ===
    /// - Snap up instantly on new peak, hold briefly, then gravity-accelerated fall
    /// - Per-band soft compression with rolling peak reference
    /// - Sqrt scaling for poppier mid-range response
    /// - Dirty checking skips GPU property updates when pixel delta &lt; 0.5px
    ///
    /// === Frequency Binning ===
    /// 12 bands with frequency-based bin mapping (auto-configured for FFT size):
    ///   Bar 0:  40-108 Hz      Sub-bass / Kick
    ///   Bar 1:  108-194 Hz     Bass / Snare body
    ///   Bar 2:  194-301 Hz     Low mids
    ///   Bar 3:  301-452 Hz     Mids
    ///   Bar 4:  452-732 Hz     Upper mids / Vocals
    ///   Bar 5:  732-1.0k Hz    Vocal presence
    ///   Bar 6:  1.0k-1.4k Hz   Upper vocal
    ///   Bar 7:  1.4k-2.0k Hz   Low presence
    ///   Bar 8:  2.0k-2.8k Hz   Presence
    ///   Bar 9:  2.8k-4.2k Hz   Detail
    ///   Bar 10: 4.2k-7.0k Hz   Shimmer
    ///   Bar 11: 7.0k-11.3k Hz  High treble
    /// Bin indices computed from frequency boundaries: bin = freq * fftSize / 44100
    ///
    /// Layout: 12 bars, 3px wide, 1px gap, 18px max height
    /// </summary>
    public class SpectrumVisualizerControl : Canvas
    {
        // Visualization data
        private float[] _spectrumData;
        private readonly float[] _barHeights; // current display height (0..1)
        private int _currentFftSize; // tracks which FFT size the bin ranges are configured for

        // Peak hold + gravity drop state
        private readonly float[] _peakHoldTimer; // seconds remaining at peak before falling
        private readonly float[] _fallVelocity;  // current fall speed (accelerates via gravity)

        // Bar elements
        private readonly Rectangle[] _bars;
        private readonly ScaleTransform[] _barScales;

        // Dirty checking — last committed pixel values
        private readonly float[] _lastScaleY;
        private readonly float[] _lastOpacity;

        // ISO octave band bin boundaries — hardcoded for 44100Hz/2048-point FFT
        private readonly int[] _binStarts;
        private readonly int[] _binEnds;

        // Settings accessor (optional — uses defaults if null)
        private Func<UniPlaySongSettings> _getSettings;

        // Layout constants
        private const int BarCount = 12;
        private const double BarWidth = 3;
        private const double BarGap = 1;
        private const double BarMaxHeight = 18;
        private const double ControlWidth = (BarWidth + BarGap) * BarCount - BarGap;
        private const double ControlHeight = 20;

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

        // Diagnostic logging (gated by EnableDebugLogging, logs ~1/sec)
        private static readonly ILogger Logger = LogManager.GetLogger();
        private int _frameCounter;
        private const int DiagLogInterval = 60; // Log every 60 frames (~1 sec)

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
        private readonly float[] _bleedBuffer = new float[BarCount];
        private readonly float[] _bleedOrig = new float[BarCount];

        // UI-side smoothing — second pass of asymmetric EMA (fast rise, smooth fall).
        // This smooths the post-bleed signal before the peak-hold animation,
        // removing frame-to-frame jitter while preserving sharp beat attacks.
        // Rise/fall alphas are configurable via VizSmoothRise / VizSmoothFall settings.
        private readonly float[] _smoothedTarget = new float[BarCount];

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

        // Bar color — Classic white brush (default, preserved as static frozen brush)
        private static readonly SolidColorBrush BarBrush =
            new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

        // Color theme definitions: (bottom gradient stop, top gradient stop)
        // Index matches VizColorTheme enum values.
        // Alpha is 255 (fully opaque) — per-bar Opacity handles transparency.
        private static readonly (Color Bottom, Color Top)[] ThemeColors =
        {
            (Color.FromArgb(200, 255, 255, 255), Color.FromArgb(255, 255, 255, 255)),  // Classic: white (matches BarBrush)
            (Color.FromArgb(255,   0, 220, 255), Color.FromArgb(255, 255,   0, 255)),  // Neon: cyan→magenta
            (Color.FromArgb(255, 255, 120,   0), Color.FromArgb(255, 255,  20,  20)),  // Fire: orange→red
            (Color.FromArgb(255,   0, 200, 200), Color.FromArgb(255,  20,  60, 240)),  // Ocean: teal→blue
            (Color.FromArgb(255, 255, 200,   0), Color.FromArgb(255, 255,  50, 100)),  // Sunset: yellow→pink
            (Color.FromArgb(255,   0, 160,   0), Color.FromArgb(255,   0, 255,  60)),  // Matrix: dark→bright green
            (Color.FromArgb(255, 220, 240, 255), Color.FromArgb(255, 100, 180, 255)),  // Ice: pale white→light blue
        };

        // Cached theme state for dirty checking (avoid brush recreation every frame)
        private int _lastThemeIndex = -1;
        private bool _lastGradientEnabled = true;

        public SpectrumVisualizerControl()
        {
            Width = ControlWidth;
            Height = ControlHeight;
            ClipToBounds = true;
            Margin = new Thickness(4, 0, 4, 0);
            Background = Brushes.Transparent;

            BarBrush.Freeze();

            _barHeights = new float[BarCount];
            _peakHoldTimer = new float[BarCount];
            _fallVelocity = new float[BarCount];
            _lastScaleY = new float[BarCount];
            _lastOpacity = new float[BarCount];
            _bars = new Rectangle[BarCount];
            _barScales = new ScaleTransform[BarCount];
            _binStarts = new int[BarCount];
            _binEnds = new int[BarCount];

            // Initialize bin ranges for default FFT size (will auto-reconfigure if provider differs)
            ConfigureBinRanges(1024);

            // Vertical offset to center the bars in the control
            double yOffset = (ControlHeight - BarMaxHeight) / 2.0;

            for (int i = 0; i < BarCount; i++)
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

            Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Provide a settings accessor so the visualizer can read tuning parameters live.
        /// </summary>
        public void SetSettingsProvider(Func<UniPlaySongSettings> getSettings)
        {
            _getSettings = getSettings;
        }

        /// <summary>
        /// Checks if the color theme setting changed and updates all bar brushes if needed.
        /// Called once per frame — dirty check is a cheap int + bool compare.
        /// </summary>
        private void UpdateBarBrushes(UniPlaySongSettings settings)
        {
            int themeIndex = settings?.VizColorTheme ?? 0;
            bool gradientEnabled = settings?.VizGradientEnabled ?? true;

            if (themeIndex == _lastThemeIndex && gradientEnabled == _lastGradientEnabled)
                return;

            _lastThemeIndex = themeIndex;
            _lastGradientEnabled = gradientEnabled;

            // Classic theme (0) — always use the original static frozen brush
            if (themeIndex == 0)
            {
                for (int i = 0; i < BarCount; i++)
                    _bars[i].Fill = BarBrush;
                return;
            }

            // Clamp to valid theme range
            if (themeIndex < 0 || themeIndex >= ThemeColors.Length)
                themeIndex = 0;

            var (bottom, top) = ThemeColors[themeIndex];

            Brush brush;
            if (gradientEnabled)
            {
                // Gradient: bottom of bar → top of bar
                var gradient = new LinearGradientBrush(bottom, top, new Point(0, 1), new Point(0, 0));
                gradient.Freeze();
                brush = gradient;
            }
            else
            {
                // Solid color using the theme's bottom (primary) color
                var solid = new SolidColorBrush(bottom);
                solid.Freeze();
                brush = solid;
            }

            for (int i = 0; i < BarCount; i++)
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

        /// <summary>
        /// Configure bin ranges for the given FFT size. Called once at construction and
        /// auto-reconfigured if the provider's FFT size changes (e.g., after settings change + restart).
        /// Frequency boundaries target the same ranges regardless of FFT size.
        /// </summary>
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

            // Target frequency boundaries for 12 bars (Hz)
            // These are the same regardless of FFT size
            int[] freqStarts = { 40, 108, 194, 301, 452, 732, 1034, 1421, 2024, 2821, 4214, 7024 };
            int[] freqEnds   = { 108, 194, 301, 452, 732, 1034, 1421, 2024, 2821, 4214, 7024, 11310 };

            for (int i = 0; i < BarCount; i++)
            {
                _binStarts[i] = Math.Max(1, (int)(freqStarts[i] * scale));
                _binEnds[i] = Math.Min(spectrumSize, (int)(freqEnds[i] * scale) + 1);
                // Ensure at least 1 bin per bar
                if (_binEnds[i] <= _binStarts[i]) _binEnds[i] = _binStarts[i] + 1;
            }
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
            for (int i = 0; i < BarCount; i++)
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
                    // Bars 0-5 use bassGainMult, bars 6-11 use trebleGainMult
                    float regionMult = i < 6 ? bassGainMult : trebleGainMult;
                    float value = rms * BarGain[i] * gainMult * regionMult;

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
                Array.Copy(_bleedBuffer, _bleedOrig, BarCount);

                for (int i = 0; i < BarCount; i++)
                {
                    float bleed = 0f;
                    // Receive bleed from left neighbor
                    if (i > 0)
                        bleed += _bleedOrig[i - 1] * BleedFraction[i - 1];
                    // Receive bleed from right neighbor
                    if (i < BarCount - 1)
                        bleed += _bleedOrig[i + 1] * BleedFraction[i + 1];

                    _bleedBuffer[i] = Math.Min(1f, _bleedBuffer[i] + bleed * bleedScale);
                }
            }

            // === Pass 3: UI-side asymmetric smoothing ===
            // Fast rise (beats punch through), slow fall (smooth trailing).
            // This runs at display rate (~60fps) so it smooths the ~43fps FFT output.
            for (int i = 0; i < BarCount; i++)
            {
                float raw = _bleedBuffer[i];
                float prev = _smoothedTarget[i];
                float alpha = raw >= prev ? smoothRise : smoothFall;
                _smoothedTarget[i] = prev + (raw - prev) * alpha;
            }

            // === Pass 4: Animation (peak hold + gravity drop) ===
            for (int i = 0; i < BarCount; i++)
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
                    float barScale = 1f + (BarGravityScale[i] - 1f) * biasPct;
                    float barGravity = gravity * barScale;
                    _fallVelocity[i] += barGravity * dt;
                    current -= _fallVelocity[i] * dt;
                    if (current < target) current = target;
                    if (current < 0f) current = 0f;
                }
                _barHeights[i] = current;

                // Dirty checking — only update GPU properties if pixel change > 0.5px
                float scaleY = current > MinBarScale ? current : MinBarScale;
                float opacity = opacityMin + (1f - opacityMin) * current;

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

            // Diagnostic logging (~1/sec, gated by EnableDebugLogging)
            if (settings?.EnableDebugLogging == true && hasData)
            {
                _frameCounter++;
                if (_frameCounter >= DiagLogInterval)
                {
                    _frameCounter = 0;
                    Logger.Debug($"[Viz] dt={dt:F4} gravity={gravity:F2} peakHold={peakHold:F3} bias={biasPct:F2} gainMult={gainMult:F2}");
                    for (int i = 0; i < BarCount; i++)
                    {
                        int end = Math.Min(_binEnds[i], _spectrumData.Length);
                        int bc = end - _binStarts[i];
                        float ss = 0f;
                        for (int bin = _binStarts[i]; bin < end; bin++)
                        { float v = _spectrumData[bin]; ss += v * v; }
                        float rms = bc > 0 ? (float)Math.Sqrt(ss / bc) : 0f;
                        float scaled = rms * BarGain[i];
                        Logger.Debug($"[Viz] Bar{i}: bins={bc} rms={rms:F4} gain={BarGain[i]:F1} scaled={scaled:F4} h={_barHeights[i]:F3}");
                    }
                }
            }

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
