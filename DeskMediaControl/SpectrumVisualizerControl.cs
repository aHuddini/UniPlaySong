using System;
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
    /// 12 bands for 44100Hz / 1024-point FFT (~43Hz per bin):
    ///   Bar 0:  40-88 Hz    (bins 1-2)     Sub-bass / Kick
    ///   Bar 1:  88-177 Hz   (bins 3-4)     Bass / Snare body
    ///   Bar 2:  177-280 Hz  (bins 5-6)     Low mids
    ///   Bar 3:  280-430 Hz  (bins 7-10)    Mids
    ///   Bar 4:  430-710 Hz  (bins 11-16)   Upper mids / Vocals
    ///   Bar 5:  710-1.0k Hz (bins 17-23)   Vocal presence
    ///   Bar 6:  1.0k-1.4k Hz (bins 24-32)  Upper vocal
    ///   Bar 7:  1.4k-2.0k Hz (bins 33-46)  Low presence
    ///   Bar 8:  2.0k-2.8k Hz (bins 47-65)  Presence
    ///   Bar 9:  2.8k-4.2k Hz (bins 66-98)  Detail
    ///   Bar 10: 4.2k-7.0k Hz (bins 99-163) Shimmer
    ///   Bar 11: 7.0k-11.3k Hz (bins 164-263) High treble
    ///
    /// Layout: 12 bars, 3px wide, 1px gap, 18px max height
    /// </summary>
    public class SpectrumVisualizerControl : Canvas
    {
        // Visualization data
        private readonly float[] _spectrumData;
        private readonly float[] _barHeights; // current display height (0..1)

        // Peak hold + gravity drop state
        private readonly float[] _peakHoldTimer; // seconds remaining at peak before falling
        private readonly float[] _fallVelocity;  // current fall speed (accelerates via gravity)

            // Per-bar auto-sensitivity: each bar tracks its own rolling max and normalizes against it.
        private readonly float[] _barPeak;          // rolling max RMS per bar (slow decay)
        private const float PeakDecayRate = 0.5f;   // per-second decay multiplier
        private const float PeakFloor = 0.01f;       // minimum peak to prevent division by near-zero

        // Bar elements
        private readonly Rectangle[] _bars;
        private readonly ScaleTransform[] _barScales;

        // Dirty checking — last committed pixel values
        private readonly float[] _lastScaleY;
        private readonly float[] _lastOpacity;

        // ISO octave band bin boundaries — hardcoded for 44100Hz/1024-point FFT
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
        private const float DirtyThreshold = 0.028f; // ~0.5px out of 18px — skip GPU update below this


        // Frame timing (CompositionTarget.Rendering dedup)
        private bool _isRendering;
        private bool _isActive;
        private TimeSpan _lastRenderTime;
        private DateTime _lastFrameTime;
        private float _smoothedDt = 0.016f; // EMA-smoothed delta time to reduce jitter

        // Fade-out: smooth opacity transition when music stops
        private float _fadeOpacity = 1f;
        private const float FadeOutSpeed = 2.5f; // full fade in ~0.4 seconds

        // Diagnostic logging (gated by EnableDebugLogging, logs ~1/sec)
        private static readonly ILogger Logger = LogManager.GetLogger();
        private int _frameCounter;
        private const int DiagLogInterval = 60; // Log every 60 frames (~1 sec)

        // Noise floor — below this, bar is zeroed
        private const float NoiseFloor = 0.005f;

        // Per-bar direct gain — calibrated for -80dB FFT range with squared curve.
        // After squaring, RMS drops significantly: bass ~0.16-0.36, treble ~0.01-0.09.
        // Gains compensate to target ~0.3-0.7 scaled output.
        private static readonly float[] BarGain =
        {
            2.5f,  // Bar 0:  40-88 Hz     (2 bins)
            2.8f,  // Bar 1:  88-177 Hz    (2 bins)
            3.0f,  // Bar 2:  177-280 Hz   (2 bins)
            3.2f,  // Bar 3:  280-430 Hz   (4 bins)
            3.5f,  // Bar 4:  430-710 Hz   (6 bins)
            4.0f,  // Bar 5:  710-1.0k Hz  (7 bins)
            4.5f,  // Bar 6:  1.0k-1.4k Hz (9 bins)
            5.5f,  // Bar 7:  1.4k-2.0k Hz (14 bins)
            7.0f,  // Bar 8:  2.0k-2.8k Hz (19 bins)
            9.0f,  // Bar 9:  2.8k-4.2k Hz (33 bins)
            12.0f, // Bar 10: 4.2k-7.0k Hz (65 bins)
            16.0f, // Bar 11: 7.0k-11.3k Hz (100 bins)
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

        // Bar color
        private static readonly SolidColorBrush BarBrush =
            new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

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
            _barPeak = new float[BarCount];
            _lastScaleY = new float[BarCount];
            _lastOpacity = new float[BarCount];
            _spectrumData = new float[512];
            _bars = new Rectangle[BarCount];
            _barScales = new ScaleTransform[BarCount];
            _binStarts = new int[BarCount];
            _binEnds = new int[BarCount];

            // 12-band bin ranges for 44100Hz / 1024-point FFT (bin resolution ~43Hz)
            // Finer mid-range resolution for better beat reactivity
            _binStarts[0]  = 1;   _binEnds[0]  = 3;    // 40-88 Hz     sub-bass
            _binStarts[1]  = 3;   _binEnds[1]  = 5;    // 88-177 Hz    bass
            _binStarts[2]  = 5;   _binEnds[2]  = 7;    // 177-280 Hz   low-mid
            _binStarts[3]  = 7;   _binEnds[3]  = 11;   // 280-430 Hz   mid
            _binStarts[4]  = 11;  _binEnds[4]  = 17;   // 430-710 Hz   upper-mid
            _binStarts[5]  = 17;  _binEnds[5]  = 24;   // 710-1.0k Hz  vocal presence
            _binStarts[6]  = 24;  _binEnds[6]  = 33;   // 1.0k-1.4k Hz upper vocal
            _binStarts[7]  = 33;  _binEnds[7]  = 47;   // 1.4k-2.0k Hz low presence
            _binStarts[8]  = 47;  _binEnds[8]  = 66;   // 2.0k-2.8k Hz presence
            _binStarts[9]  = 66;  _binEnds[9]  = 99;   // 2.8k-4.2k Hz detail
            _binStarts[10] = 99;  _binEnds[10] = 164;  // 4.2k-7.0k Hz shimmer
            _binStarts[11] = 164; _binEnds[11] = 264;  // 7.0k-11.3k Hz treble

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
                _barPeak[i] = PeakFloor;
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

        private void StartRendering()
        {
            if (_isRendering) return;
            _isRendering = true;
            _lastFrameTime = DateTime.UtcNow;
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

            var now = DateTime.UtcNow;
            float rawDt = (float)(now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;
            if (rawDt > 0.2f) rawDt = 0.016f;
            // EMA-smoothed dt reduces WPF frame timing jitter (7ms-18ms → steady ~12ms)
            _smoothedDt = _smoothedDt * 0.8f + rawDt * 0.2f;
            float dt = _smoothedDt;

            // Read tuning from settings (cached per frame, no allocation)
            var settings = _getSettings?.Invoke();
            float opacityMin = (settings?.VizOpacityMin ?? 30) / 100f;
            float gainMult = 1f + (settings?.VizBarGainBoost ?? 0) / 100f;
            float peakHold = (settings?.VizPeakHoldMs ?? 80) / 1000f;  // ms → seconds
            float gravity = (settings?.VizGravity ?? 100) / 10f;        // tenths → actual
            float biasPct = (settings?.VizBassGravityBias ?? 50) / 100f; // 0..1

            var provider = VisualizationDataProvider.Current;
            bool hasData = provider != null && _isActive;

            if (hasData)
                provider.GetSpectrumData(_spectrumData, 0, _spectrumData.Length);

            bool anyBarVisible = false;

            for (int i = 0; i < BarCount; i++)
            {
                float target = 0f;

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

                    // Apply per-bar gain to compensate for treble energy spread
                    float value = rms * BarGain[i] * gainMult;

                    // Noise floor gate
                    if (rms < NoiseFloor) value = 0f;

                    // Clamp to 0..1
                    if (value > 1f) value = 1f;

                    target = value;
                }

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
