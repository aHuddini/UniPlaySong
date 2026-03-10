using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.IconGlow
{
    // Multi-layer neon glow: SkiaSharp pre-rendered outer glow image behind the icon,
    // plus a DropShadowEffect on the icon itself for a tight bright inner halo.
    // Animation drives opacity of the glow image and blur radius of the inner halo.
    public class IconGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly UniPlaySongSettings _settings;
        private readonly IconColorExtractor _colorExtractor = new IconColorExtractor();
        private readonly FileLogger _fileLogger;

        private DispatcherTimer _glowTimer;
        private Image _currentIcon;
        private Image _currentGlowImage;
        private ScaleTransform _glowScale;
        private DropShadowEffect _innerGlow;
        private Effect _savedEffect;
        private Grid _currentWrapperGrid;
        private Panel _currentParentPanel;
        private int _originalIconIndex;
        private DateTime _pulseStartTime;
        private double _smoothedIntensity;

        // Audio-reactive: bass FFT energy + three-stage smoothing + onset detection
        // Rolling window peak normalizer — ~5 second window at 60fps
        private const int PeakWindowSize = 300;
        private double[] _peakWindow;   // circular buffer of recent peak levels
        private int _peakWindowIdx;     // write index into circular buffer
        private double _peakWindowMax;  // cached max of the window (recomputed every 30 frames)
        private int _peakMaxAge;        // frames since last full recompute
        private double _commonMode;     // baseline tracker for common mode subtraction
        private double _smooth1;        // stage 1: fast envelope (catches beats)
        private double _smooth2;        // stage 2: medium breathing (integrates over phrases)
        private double _smooth3;        // stage 3: slow polish (anti-jitter)
        private double _punch;          // fast-reacting signal for beat flashes
        private float[] _spectrumBuf;   // reusable buffer for FFT spectrum data
        private float[] _prevSpectrum;  // previous frame's bass bins for spectral flux
        private double _fluxBaseline;   // adaptive threshold for onset detection
        private int _onsetFrames;       // frames remaining in onset boost

        public IconGlowManager(UniPlaySongSettings settings, FileLogger fileLogger = null)
        {
            _settings = settings;
            _fileLogger = fileLogger;
            _colorExtractor.SetFileLogger(fileLogger);
        }

        public void OnGameSelected(Game game)
        {
            _fileLogger?.Debug($"[IconGlow] OnGameSelected: {game?.Name ?? "null"}, EnableIconGlow={_settings.EnableIconGlow}");

            if (!_settings.EnableIconGlow)
            {
                RemoveGlow();
                return;
            }

            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyGlow(game)));
        }

        public void RemoveGlow()
        {
            StopTimer();

            // Restore icon's original Effect
            if (_currentIcon != null)
            {
                try { _currentIcon.Effect = _savedEffect; }
                catch { /* icon may have been removed from tree */ }
            }

            // Unwrap icon from Grid back to parent
            if (_currentWrapperGrid != null && _currentParentPanel != null && _currentIcon != null)
            {
                try
                {
                    _currentWrapperGrid.Children.Clear();
                    _currentParentPanel.Children.Remove(_currentWrapperGrid);
                    _currentParentPanel.Children.Insert(_originalIconIndex, _currentIcon);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[IconGlow] Error removing glow wrapper");
                }
            }

            _currentGlowImage = null;
            _glowScale = null;
            _innerGlow = null;
            _savedEffect = null;
            _currentWrapperGrid = null;
            _currentIcon = null;
            _currentParentPanel = null;
        }

        public void Destroy()
        {
            RemoveGlow();
            _colorExtractor.ClearCache();
        }

        private void ApplyGlow(Game game)
        {
            try
            {
                ApplyGlowInternal(game);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[IconGlow] ApplyGlow failed");
                _fileLogger?.Debug($"[IconGlow] ApplyGlow exception: {ex.Message}");
                RemoveGlow();
            }
        }

        private void ApplyGlowInternal(Game game)
        {
            RemoveGlow();

            if (game == null) return;

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null)
            {
                _fileLogger?.Debug("[IconGlow] MainWindow is null");
                return;
            }

            _fileLogger?.Debug($"[IconGlow] ApplyGlow called for: {game.Name}");

            var icon = TileFinder.FindSelectedGameIcon(mainWindow, _fileLogger);
            if (icon == null)
            {
                _fileLogger?.Debug("[IconGlow] PART_ImageIcon not found in visual tree");
                return;
            }

            var parent = FindParentPanel(icon);
            if (parent == null)
            {
                _fileLogger?.Debug("[IconGlow] No parent Panel found for icon");
                return;
            }

            int iconIndex = parent.Children.IndexOf(icon);
            if (iconIndex < 0)
            {
                _fileLogger?.Debug("[IconGlow] Icon not a direct child of parent panel");
                return;
            }

            var (color1, color2) = _colorExtractor.GetGlowColors(game.Id, icon.Source);

            // === Layer 1: SkiaSharp outer glow (pre-rendered, behind icon) ===
            var glowBitmap = GlowRenderer.RenderGlow(icon.Source, color1, color2,
                _settings.IconGlowSize, icon.ActualWidth, icon.ActualHeight, _settings.IconGlowIntensity);
            if (glowBitmap == null)
            {
                _fileLogger?.Debug("[IconGlow] GlowRenderer returned null");
                return;
            }
            var glowImage = GlowRenderer.CreateGlowImage(glowBitmap, icon.ActualWidth, icon.ActualHeight, _settings.IconGlowSize);

            // ScaleTransform on glow image — peaks make glow physically expand
            var scale = new ScaleTransform(1.0, 1.0);
            glowImage.RenderTransform = scale;
            glowImage.RenderTransformOrigin = new Point(0.5, 0.5);

            // === Layer 2: DropShadowEffect inner halo (on the icon itself) ===
            _savedEffect = icon.Effect;
            var innerGlow = new DropShadowEffect
            {
                ShadowDepth = 0,
                Color = color1,
                BlurRadius = 8,
                Opacity = 0.9
            };
            icon.Effect = innerGlow;

            // Wrap icon in Grid with glow image behind
            var savedMargin = icon.Margin;
            var wrapper = new Grid
            {
                HorizontalAlignment = icon.HorizontalAlignment,
                VerticalAlignment = icon.VerticalAlignment,
                Margin = new Thickness(savedMargin.Left, savedMargin.Top,
                    Math.Max(savedMargin.Right, 10), savedMargin.Bottom),
                ClipToBounds = false
            };

            icon.Margin = new Thickness(0);

            parent.Children.RemoveAt(iconIndex);
            wrapper.Children.Add(glowImage);
            wrapper.Children.Add(icon);

            if (parent is DockPanel)
            {
                var dock = DockPanel.GetDock(icon);
                DockPanel.SetDock(wrapper, dock);
            }

            parent.Children.Insert(iconIndex, wrapper);

            _currentGlowImage = glowImage;
            _glowScale = scale;
            _innerGlow = innerGlow;
            _currentWrapperGrid = wrapper;
            _currentIcon = icon;
            _currentParentPanel = parent;
            _originalIconIndex = iconIndex;
            _pulseStartTime = DateTime.UtcNow;
            _smoothedIntensity = 0.0;
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

            _fileLogger?.Debug($"[IconGlow] Applied multi-layer glow to {game.Name} (colors: #{color1.R:X2}{color1.G:X2}{color1.B:X2} → #{color2.R:X2}{color2.G:X2}{color2.B:X2}, icon: {icon.ActualWidth}x{icon.ActualHeight})");

            StartTimer();
        }

        private Panel FindParentPanel(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is Panel panel)
                    return panel;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void StartTimer()
        {
            if (_glowTimer != null) return;

            _glowTimer = new DispatcherTimer(DispatcherPriority.Render, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _glowTimer.Tick += OnGlowTick;
            _glowTimer.Start();
        }

        private void StopTimer()
        {
            if (_glowTimer != null)
            {
                _glowTimer.Stop();
                _glowTimer.Tick -= OnGlowTick;
                _glowTimer = null;
            }
        }

        private void OnGlowTick(object sender, EventArgs e)
        {
            if (_currentGlowImage == null) return;

            double rawIntensity;
            var vizProvider = VisualizationDataProvider.Current;

            if (vizProvider != null)
            {
                double level;
                int specSize = vizProvider.SpectrumSize;
                int bassBins = Math.Min(6, specSize);

                if (specSize > 0)
                {
                    if (_spectrumBuf == null || _spectrumBuf.Length < specSize)
                        _spectrumBuf = new float[specSize];
                    vizProvider.GetSpectrumData(_spectrumBuf, 0, specSize);

                    // Bass energy (~0-250Hz)
                    double bassSum = 0;
                    for (int i = 0; i < bassBins; i++)
                        bassSum += _spectrumBuf[i];
                    level = bassSum / bassBins;

                    // Blend in RMS for liveliness (80/20)
                    vizProvider.GetLevels(out _, out float rms);
                    level = level * 0.8 + rms * 0.2;

                    // Spectral flux onset detection (bass bins only)
                    if (_prevSpectrum == null)
                        _prevSpectrum = new float[bassBins];

                    double flux = 0;
                    for (int i = 0; i < bassBins; i++)
                    {
                        double diff = _spectrumBuf[i] - _prevSpectrum[i];
                        if (diff > 0) flux += diff;
                        _prevSpectrum[i] = _spectrumBuf[i];
                    }
                    flux /= bassBins;

                    // Adaptive onset threshold
                    _fluxBaseline += (flux - _fluxBaseline) * (flux > _fluxBaseline ? 0.10 : 0.01);
                    if (flux > _fluxBaseline * 1.8 && flux > 0.01)
                        _onsetFrames = 3; // boost for 3 frames (~50ms)
                }
                else
                {
                    vizProvider.GetLevels(out float peak, out float rms);
                    level = rms * 0.7 + peak * 0.3;
                }

                // Rolling window peak normalization (~5s window)
                _peakWindow[_peakWindowIdx] = level;
                _peakWindowIdx = (_peakWindowIdx + 1) % PeakWindowSize;

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

                // Common mode subtraction
                double cmAlpha = normalized > _commonMode ? 0.03 : 0.35;
                _commonMode = cmAlpha * normalized + (1.0 - cmAlpha) * _commonMode;
                double reactive = Math.Max(0, normalized - _commonMode * 0.65);

                // Onset boost: temporarily increase stage 1 rise for beat snap
                double s1Rise = _onsetFrames > 0 ? 0.95 : 0.75;
                if (_onsetFrames > 0) _onsetFrames--;

                // Three-stage smoothing for breathing base
                _smooth1 += (reactive - _smooth1) * (reactive > _smooth1 ? s1Rise : 0.25);
                _smooth2 += (_smooth1 - _smooth2) * (_smooth1 > _smooth2 ? 0.35 : 0.15);
                _smooth3 += (_smooth2 - _smooth3) * (_smooth2 > _smooth3 ? 0.25 : 0.22);

                // Fast punch signal for beat flashes (lightly smoothed)
                _punch += (reactive - _punch) * (reactive > _punch ? 0.90 : 0.55);

                // Blend: smooth breathing base + punchy transients (dynamic weight)
                double punchWeight = Math.Min(0.4, Math.Max(0.1, reactive * 2.0));
                rawIntensity = _smooth3 * (1.0 - punchWeight) + _punch * punchWeight;
                rawIntensity = Math.Min(1.0, rawIntensity * _settings.IconGlowAudioSensitivity);
            }
            else if (_settings.EnableIconGlowPulse)
            {
                double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
                rawIntensity = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * elapsed / _settings.IconGlowPulseSpeed);
            }
            else
            {
                // Static glow
                _currentGlowImage.Opacity = 1.0;
                if (_innerGlow != null) _innerGlow.Opacity = 0.9;
                StopTimer();
                return;
            }

            _smoothedIntensity = rawIntensity;

            // Outer glow: opacity pulses 0.1–1.0
            _currentGlowImage.Opacity = 0.1 + _smoothedIntensity * 0.9;

            // Scale: use breathing base only (less punch to avoid jitter)
            if (_glowScale != null)
            {
                double baseOnly = Math.Min(1.0, _smooth3 * _settings.IconGlowAudioSensitivity);
                double s = 0.95 + baseOnly * 0.10;
                _glowScale.ScaleX = s;
                _glowScale.ScaleY = s;
            }

            // Inner halo: blur radius and opacity use blended signal for punch
            if (_innerGlow != null)
            {
                _innerGlow.BlurRadius = 2 + _smoothedIntensity * 18;
                _innerGlow.Opacity = 0.1 + _smoothedIntensity * 0.9;
            }
        }
    }
}
