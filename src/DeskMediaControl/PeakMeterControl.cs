using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UniPlaySong.Audio;

namespace UniPlaySong.DeskMediaControl
{
    // Compact L/R peak meter for the Desktop top panel.
    // Two vertical bars using stacked solid-color rectangles (green/yellow/red) clipped by a single
    // ScaleTransform for GPU-composited animation. Dirty checking skips GPU writes when <0.5px change.
    // Data: VisualizationDataProvider.GetStereoLevels() (near-zero cost, already computed every audio frame).
    public class PeakMeterControl : Canvas
    {
        // Each channel has one Rectangle with a frozen solid brush, scaled from bottom
        private readonly Rectangle _barL, _barR;
        private readonly ScaleTransform _scaleL, _scaleR;

        // Peak hold indicators
        private readonly Rectangle _peakL, _peakR;
        private readonly TranslateTransform _peakTransL, _peakTransR;

        // Animation state
        private float _smoothedL, _smoothedR;
        private float _peakHoldL, _peakHoldR;
        private float _peakTimerL, _peakTimerR;
        private float _peakFallVelL, _peakFallVelR;

        // Dirty checking
        private float _lastScaleL, _lastScaleR;
        private float _lastPeakPosL, _lastPeakPosR;

        // Layout constants
        private const double BarWidth = 4;
        private const double BarGap = 1;
        private const double BarMaxHeight = 18;
        private const double ControlWidth = BarWidth * 2 + BarGap; // 9px
        private const double ControlHeight = 20;

        // Animation constants
        private const float AttackAlpha = 0.4f;
        private const float DecayAlpha = 0.05f;
        private const float MinScale = 0.02f;
        private const float DirtyThreshold = 0.01f;
        private const float PeakHoldTime = 0.5f;
        private const float PeakGravity = 6.0f;
        private const double PeakIndicatorHeight = 1.5;

        // dB range
        private const float DbFloor = -60f;
        private const float DbRange = 60f;

        // Frame timing
        private bool _isRendering;
        private bool _isActive;
        private TimeSpan _lastRenderTime;
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        private long _lastFrameTicks;

        // Fade out
        private float _fadeOpacity = 1f;

        // Color thresholds — bar color changes based on level
        private static readonly Brush GreenBrush;
        private static readonly Brush YellowBrush;
        private static readonly Brush RedBrush;
        private static readonly Brush PeakBrush;

        static PeakMeterControl()
        {
            GreenBrush = new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x44));
            GreenBrush.Freeze();
            YellowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            YellowBrush.Freeze();
            RedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            RedBrush.Freeze();
            PeakBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            PeakBrush.Freeze();
        }

        public PeakMeterControl()
        {
            Width = ControlWidth;
            Height = ControlHeight;
            ClipToBounds = true;

            double barTop = (ControlHeight - BarMaxHeight) / 2;

            // Left bar — starts green, color updated per-frame based on level
            _scaleL = new ScaleTransform(1, MinScale, 0, BarMaxHeight);
            _barL = new Rectangle
            {
                Width = BarWidth,
                Height = BarMaxHeight,
                Fill = GreenBrush,
                RenderTransform = _scaleL
            };
            SetLeft(_barL, 0);
            SetTop(_barL, barTop);
            Children.Add(_barL);

            // Right bar
            _scaleR = new ScaleTransform(1, MinScale, 0, BarMaxHeight);
            _barR = new Rectangle
            {
                Width = BarWidth,
                Height = BarMaxHeight,
                Fill = GreenBrush,
                RenderTransform = _scaleR
            };
            SetLeft(_barR, BarWidth + BarGap);
            SetTop(_barR, barTop);
            Children.Add(_barR);

            // Peak hold indicators
            _peakTransL = new TranslateTransform(0, 0);
            _peakL = new Rectangle
            {
                Width = BarWidth,
                Height = PeakIndicatorHeight,
                Fill = PeakBrush,
                RenderTransform = _peakTransL,
                Opacity = 0.9
            };
            SetLeft(_peakL, 0);
            SetTop(_peakL, barTop);
            Children.Add(_peakL);

            _peakTransR = new TranslateTransform(0, 0);
            _peakR = new Rectangle
            {
                Width = BarWidth,
                Height = PeakIndicatorHeight,
                Fill = PeakBrush,
                RenderTransform = _peakTransR,
                Opacity = 0.9
            };
            SetLeft(_peakR, BarWidth + BarGap);
            SetTop(_peakR, barTop);
            Children.Add(_peakR);
        }

        public void SetSettingsProvider(Func<UniPlaySongSettings> getSettings) { }

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
            // When deactivated, the render loop handles fade-out and StopRendering()
        }

        private void StartRendering()
        {
            if (_isRendering) return;
            _isRendering = true;
            _frameStopwatch.Restart();
            _lastFrameTicks = 0;
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRender;
        }

        private void StopRendering()
        {
            if (!_isRendering) return;
            _isRendering = false;
            CompositionTarget.Rendering -= OnRender;
        }

        private void OnRender(object sender, EventArgs e)
        {
            var args = (RenderingEventArgs)e;
            if (args.RenderingTime == _lastRenderTime)
                return;
            _lastRenderTime = args.RenderingTime;

            long nowTicks = _frameStopwatch.ElapsedTicks;
            float dt = (float)(nowTicks - _lastFrameTicks) / Stopwatch.Frequency;
            _lastFrameTicks = nowTicks;
            if (dt <= 0 || dt > 0.5f) dt = 0.016f;

            var provider = VisualizationDataProvider.Current;

            // Inactive: fade out then stop rendering entirely
            if (!_isActive || provider == null)
            {
                if (_fadeOpacity > 0)
                {
                    _fadeOpacity = Math.Max(0, _fadeOpacity - dt * 3f);
                    Opacity = _fadeOpacity;
                    if (_fadeOpacity <= 0)
                    {
                        Visibility = Visibility.Collapsed;
                        StopRendering();
                    }
                }
                else
                {
                    StopRendering();
                }
                return;
            }

            // Get stereo levels (volatile reads, near-zero cost)
            provider.GetStereoLevels(out float rawL, out float rawR, out _, out _);

            float normL = AmplitudeToNormalized(rawL);
            float normR = AmplitudeToNormalized(rawR);

            // Asymmetric smoothing
            _smoothedL = normL > _smoothedL
                ? _smoothedL + (normL - _smoothedL) * AttackAlpha
                : _smoothedL + (normL - _smoothedL) * DecayAlpha;
            _smoothedR = normR > _smoothedR
                ? _smoothedR + (normR - _smoothedR) * AttackAlpha
                : _smoothedR + (normR - _smoothedR) * DecayAlpha;

            float displayL = Math.Max(MinScale, _smoothedL);
            float displayR = Math.Max(MinScale, _smoothedR);

            // Peak hold + gravity
            UpdatePeakHold(ref _peakHoldL, ref _peakTimerL, ref _peakFallVelL, displayL, dt);
            UpdatePeakHold(ref _peakHoldR, ref _peakTimerR, ref _peakFallVelR, displayR, dt);

            // Update bar scales (dirty check)
            if (Math.Abs(displayL - _lastScaleL) > DirtyThreshold)
            {
                _scaleL.ScaleY = displayL;
                _lastScaleL = displayL;
                UpdateBarColor(_barL, displayL);
            }
            if (Math.Abs(displayR - _lastScaleR) > DirtyThreshold)
            {
                _scaleR.ScaleY = displayR;
                _lastScaleR = displayR;
                UpdateBarColor(_barR, displayR);
            }

            // Update peak indicators
            float peakPosL = (float)(BarMaxHeight * (1 - _peakHoldL));
            float peakPosR = (float)(BarMaxHeight * (1 - _peakHoldR));

            if (Math.Abs(peakPosL - _lastPeakPosL) > 0.5f)
            {
                _peakTransL.Y = peakPosL;
                _lastPeakPosL = peakPosL;
            }
            if (Math.Abs(peakPosR - _lastPeakPosR) > 0.5f)
            {
                _peakTransR.Y = peakPosR;
                _lastPeakPosR = peakPosR;
            }
        }

        // Color by level: green <60%, yellow 60-80%, red >80%
        private static void UpdateBarColor(Rectangle bar, float level)
        {
            Brush target;
            if (level > 0.8f) target = RedBrush;
            else if (level > 0.6f) target = YellowBrush;
            else target = GreenBrush;

            if (!ReferenceEquals(bar.Fill, target))
                bar.Fill = target;
        }

        private static float AmplitudeToNormalized(float amplitude)
        {
            if (amplitude <= 0.000001f) return 0f;
            float db = 20f * (float)Math.Log10(amplitude);
            if (db < DbFloor) return 0f;
            return (db - DbFloor) / DbRange;
        }

        private static void UpdatePeakHold(ref float peakLevel, ref float holdTimer, ref float fallVel, float current, float dt)
        {
            if (current >= peakLevel)
            {
                peakLevel = current;
                holdTimer = PeakHoldTime;
                fallVel = 0;
            }
            else if (holdTimer > 0)
            {
                holdTimer -= dt;
            }
            else
            {
                fallVel += PeakGravity * dt;
                peakLevel = Math.Max(0, peakLevel - fallVel * dt);
            }
        }
    }
}
