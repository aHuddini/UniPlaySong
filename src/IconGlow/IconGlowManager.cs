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
using UniPlaySong.Services;

namespace UniPlaySong.IconGlow
{
    // Applies a multi-layer glow effect to the selected game's icon:
    //   - Outer glow: SkiaSharp pre-rendered blurred image behind the icon
    //   - Inner halo: WPF DropShadowEffect on the icon itself
    // Animation at ~60fps drives opacity, rotation, and color cycling.
    // Intensity source: NAudio spectrum (3-band FFT) > NAudio RMS/peak > sine pulse > static.
    public class IconGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SettingsService _settingsService;
        private readonly IconColorExtractor _colorExtractor = new IconColorExtractor();
        private readonly FileLogger _fileLogger;

        // Always reads the current settings object (survives Playnite JSON reload)
        private UniPlaySongSettings _settings => _settingsService.Current;

        // Visual tree state
        private DispatcherTimer _glowTimer;
        private Image _currentIcon;
        private Image _currentGlowImage;
        private ScaleTransform _glowScale;
        private RotateTransform _glowRotate;
        private double _glowAngle;
        private DropShadowEffect _innerGlow;
        private Color _glowBaseColor;
        private Effect _savedEffect;
        private Grid _currentWrapperGrid;
        private Panel _currentParentPanel;
        private int _originalIconIndex;

        // Animation state
        private DateTime _pulseStartTime;
        private double _smoothedIntensity;
        private double _midIntensity;
        private double _trebleIntensity;

        // Crossfade state for smooth transitions (game switch + cycling glow re-render)
        private Image _fadingOutGlowImage;   // old glow image being faded out
        private double _fadeProgress;         // 0 → 1 over crossfade duration
        private const double CrossfadeDuration = 0.1; // seconds (game switch)
        private const double CyclingCrossfadeDuration = 1.2; // seconds (cycling re-render, slow blend)
        private double _activeCrossfadeDuration = CrossfadeDuration;
        private bool _isPresetSwitch; // true when ApplyGlow triggered by preset/slider change

        // Color-cycling outer glow re-render (Neon, SharpWarm, SharpCool)
        private DateTime _lastOuterGlowRerender;
        private const double OuterGlowRerenderInterval = 1.5; // seconds between re-renders
        private Game _activeGame;

        // 3-band audio analysis state
        private const int PeakWindowSize = 300;
        private double[] _bassWindow, _midWindow, _trebleWindow;
        private int _bassWindowIdx, _midWindowIdx, _trebleWindowIdx;
        private double _bassWindowMax, _midWindowMax, _trebleWindowMax;
        private int _bassMaxAge, _midMaxAge, _trebleMaxAge;
        private double _bassCommon, _midCommon;
        private double _bassSmooth1, _bassSmooth2, _bassSmooth3, _bassPunch;
        private double _midSmooth, _trebleSmooth;
        private float[] _spectrumBuf;
        private float[] _prevSpectrum;
        private double _fluxBaseline;
        private int _onsetFrames;

        // Detect settings changes that bypass property setters (Playnite JSON deserialization)
        private IconGlowPreset _lastAppliedPreset;
        private double _lastAppliedSize;
        private double _lastAppliedIntensity;

        public IconGlowManager(SettingsService settingsService, FileLogger fileLogger = null)
        {
            _settingsService = settingsService;
            _fileLogger = fileLogger;
            _colorExtractor.SetFileLogger(fileLogger);
            _settingsService.SettingPropertyChanged += OnSettingsChanged;
            _settingsService.SettingsChanged += OnSettingsObjectReplaced;
        }

        public void OnGameSelected(Game game)
        {
            _fileLogger?.Debug($"[IconGlow] OnGameSelected: {game?.Name ?? "null"}, EnableIconGlow={_settings.EnableIconGlow}");

            if (!_settings.EnableIconGlow)
            {
                _activeGame = null;
                RemoveGlow();
                return;
            }

            _activeGame = game;

            // Freeze the current glow in place while the new glow renders.
            // This prevents a visible "pop" where the old glow disappears before the new one is ready.
            StopTimer();

            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyGlow(game)));
        }

        // Fires when the entire settings object is replaced (Playnite save/reload)
        private void OnSettingsObjectReplaced(object sender, SettingsChangedEventArgs e)
        {
            if (_activeGame == null) return;

            _fileLogger?.Debug($"[IconGlow] Settings object replaced (source: {e.Source}), re-applying glow");
            var game = _activeGame;
            _isPresetSwitch = true;
            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyGlow(game)));
        }

        // Fires when an individual property changes on the current settings object
        private void OnSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            bool isPresetChange = e.PropertyName == nameof(UniPlaySongSettings.IconGlowPreset);
            bool isSliderChange = (e.PropertyName == nameof(UniPlaySongSettings.IconGlowSize) ||
                                   e.PropertyName == nameof(UniPlaySongSettings.IconGlowIntensity))
                                  && _settings.IconGlowPreset == IconGlowPreset.Custom;

            if (!isPresetChange && !isSliderChange) return;
            if (_activeGame == null) return;

            var game = _activeGame;
            _fileLogger?.Debug($"[IconGlow] OnSettingsChanged: {e.PropertyName}, re-applying glow for {game.Name}");
            _isPresetSwitch = true;
            Application.Current?.Dispatcher?.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyGlow(game)));
        }

        public void RemoveGlow()
        {
            StopTimer();

            // Only unwrap if our wrapper is still in the live visual tree.
            // If Playnite rebuilt the tree (game switch), the wrapper is orphaned — just drop references.
            bool wrapperIsLive = _currentWrapperGrid != null && _currentParentPanel != null
                              && _currentParentPanel.Children.Contains(_currentWrapperGrid);

            if (wrapperIsLive && _currentIcon != null)
            {
                try
                {
                    _currentIcon.Effect = _savedEffect;
                    _currentIcon.Margin = _currentWrapperGrid.Margin;
                    _currentWrapperGrid.Children.Clear();
                    _currentParentPanel.Children.Remove(_currentWrapperGrid);
                    _currentParentPanel.Children.Insert(
                        Math.Min(_originalIconIndex, _currentParentPanel.Children.Count),
                        _currentIcon);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[IconGlow] Error removing glow wrapper");
                }
            }
            else if (_currentIcon != null)
            {
                // Wrapper is orphaned — just restore the icon's effect if it's still accessible
                try { _currentIcon.Effect = _savedEffect; }
                catch { /* icon may have been removed from tree */ }
            }

            _currentGlowImage = null;
            _fadingOutGlowImage = null;
            _glowScale = null;
            _glowRotate = null;
            _innerGlow = null;
            _savedEffect = null;
            _currentWrapperGrid = null;
            _currentIcon = null;
            _currentParentPanel = null;
        }

        public void Destroy()
        {
            _settingsService.SettingPropertyChanged -= OnSettingsChanged;
            _settingsService.SettingsChanged -= OnSettingsObjectReplaced;
            RemoveGlow();
            _colorExtractor.ClearCache();
        }

        // === GLOW APPLICATION ===

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
            if (game == null)
            {
                RemoveGlow();
                return;
            }

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null)
            {
                _fileLogger?.Debug("[IconGlow] MainWindow is null");
                return;
            }

            _fileLogger?.Debug($"[IconGlow] ApplyGlow called for: {game.Name}");

            // Try to reuse existing wrapper for smooth game-switch transitions
            bool reuseWrapper = false;
            Image icon;
            Panel parent;
            int iconIndex;

            if (_currentIcon != null && _currentWrapperGrid != null && _currentParentPanel != null
                && _currentWrapperGrid.Children.Contains(_currentIcon)
                && _currentIcon.ActualWidth > 0 && _currentIcon.ActualHeight > 0)
            {
                icon = _currentIcon;
                parent = _currentParentPanel;
                iconIndex = _currentParentPanel.Children.IndexOf(_currentWrapperGrid);
                reuseWrapper = iconIndex >= 0;
                _fileLogger?.Debug("[IconGlow] Reusing existing wrapper (smooth transition)");
            }
            else
            {
                RemoveGlow();
                icon = TileFinder.FindSelectedGameIcon(mainWindow, _fileLogger);
                parent = icon != null ? FindParentPanel(icon) : null;
                iconIndex = parent?.Children.IndexOf(icon) ?? -1;
            }

            if (icon == null || parent == null || (!reuseWrapper && iconIndex < 0))
            {
                _fileLogger?.Debug("[IconGlow] Could not locate icon in visual tree");
                return;
            }

            var (extractedColor1, extractedColor2) = _colorExtractor.GetGlowColors(game.Id, icon.Source);

            // For color-cycling presets, use preset colors for the outer glow instead of icon colors
            Color color1, color2;
            if (HasCyclingOuterGlow(_settings.IconGlowPreset))
            {
                GetOuterGlowColors(out color1, out color2);
            }
            else
            {
                color1 = extractedColor1;
                color2 = extractedColor2;
            }

            // Render outer glow bitmap
            var glowBitmap = GlowRenderer.RenderGlow(icon.Source, color1, color2,
                _settings.IconGlowSize, icon.ActualWidth, icon.ActualHeight, _settings.IconGlowIntensity);
            if (glowBitmap == null)
            {
                _fileLogger?.Debug("[IconGlow] GlowRenderer returned null");
                return;
            }
            var glowImage = GlowRenderer.CreateGlowImage(glowBitmap, icon.ActualWidth, icon.ActualHeight, _settings.IconGlowSize);

            // Transform: scale (audio peaks) + rotate (slow spin)
            var scale = new ScaleTransform(1.0, 1.0);
            var rotate = new RotateTransform(_glowAngle); // preserve angle for smooth transition
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scale);
            transformGroup.Children.Add(rotate);
            glowImage.RenderTransform = transformGroup;
            glowImage.RenderTransformOrigin = new Point(0.5, 0.5);

            // Inner halo effect
            var innerGlow = new DropShadowEffect
            {
                ShadowDepth = 0,
                Color = color1,
                BlurRadius = 8,
                Opacity = 0.9
            };

            if (reuseWrapper)
            {
                StopTimer();
                icon.Effect = innerGlow;

                if (_isPresetSwitch)
                {
                    // Preset/settings change: instant swap (no crossfade needed, visual change is intentional)
                    if (_currentGlowImage != null)
                        _currentWrapperGrid.Children.Remove(_currentGlowImage);
                    if (_fadingOutGlowImage != null)
                    {
                        _currentWrapperGrid.Children.Remove(_fadingOutGlowImage);
                        _fadingOutGlowImage = null;
                    }
                    _currentWrapperGrid.Children.Insert(0, glowImage);
                    _fadeProgress = 1.0; // fully visible immediately
                    _isPresetSwitch = false;
                }
                else
                {
                    // Game switch: crossfade old glow out, new glow in
                    if (_currentGlowImage != null)
                        _fadingOutGlowImage = _currentGlowImage;
                    _currentWrapperGrid.Children.Insert(0, glowImage);
                    glowImage.Opacity = 0;
                    _fadeProgress = 0;
                    _activeCrossfadeDuration = CrossfadeDuration;
                }
            }
            else
            {
                // First-time setup: wrap icon in Grid with glow behind it
                _savedEffect = icon.Effect;
                icon.Effect = innerGlow;

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
                    DockPanel.SetDock(wrapper, DockPanel.GetDock(icon));

                parent.Children.Insert(iconIndex, wrapper);
                _currentWrapperGrid = wrapper;
                _currentParentPanel = parent;
                _originalIconIndex = iconIndex;
            }

            _currentGlowImage = glowImage;
            _glowScale = scale;
            _glowRotate = rotate;
            _innerGlow = innerGlow;
            _glowBaseColor = extractedColor1; // always store extracted color for non-cycling presets
            _currentIcon = icon;

            // Reset rotation when spin is disabled (e.g. Sharp presets)
            if (!_settings.EnableIconGlowSpin)
                _glowAngle = 0;

            _lastOuterGlowRerender = DateTime.UtcNow;
            _pulseStartTime = DateTime.UtcNow;
            _smoothedIntensity = 0.0;

            ResetAudioState();

            _lastAppliedPreset = _settings.IconGlowPreset;
            _lastAppliedSize = _settings.IconGlowSize;
            _lastAppliedIntensity = _settings.IconGlowIntensity;

            _fileLogger?.Debug($"[IconGlow] Applied glow to {game.Name} (#{color1.R:X2}{color1.G:X2}{color1.B:X2} → #{color2.R:X2}{color2.G:X2}{color2.B:X2}, {icon.ActualWidth}x{icon.ActualHeight})");
            StartTimer();
        }

        private void ResetAudioState()
        {
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

        // === TIMER ===

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

        // === TICK LOOP ===

        private void OnGlowTick(object sender, EventArgs e)
        {
            if (_currentGlowImage == null) return;

            // Detect settings changes that bypassed property setters (Playnite JSON reload)
            if (HasSettingsChanged())
            {
                _fileLogger?.Debug($"[IconGlow] Detected settings change via poll: preset={_settings.IconGlowPreset} (was {_lastAppliedPreset})");
                ApplyGlow(_activeGame);
                return;
            }

            // Drive crossfade if active
            UpdateCrossfade();

            // Periodically re-render outer glow for color-cycling presets
            UpdateCyclingOuterGlow();

            double rawIntensity = ComputeRawIntensity();
            if (double.IsNaN(rawIntensity)) return; // static glow handled inline

            _smoothedIntensity = rawIntensity;

            GetOpacityRange(out double glowFloor, out double glowRange);
            double targetOpacity = glowFloor + _smoothedIntensity * glowRange;

            // During crossfade, scale new glow opacity by fade progress
            double fadeIn = _fadingOutGlowImage != null ? _fadeProgress : 1.0;
            _currentGlowImage.Opacity = targetOpacity * fadeIn;

            UpdateScale();
            UpdateRotation();
            UpdateInnerHalo(glowFloor, glowRange);
        }

        private void UpdateCrossfade()
        {
            if (_fadingOutGlowImage == null) return;

            _fadeProgress += 16.0 / (_activeCrossfadeDuration * 1000.0); // ~16ms per tick

            if (_fadeProgress >= 1.0)
            {
                // Crossfade complete — remove old glow image from wrapper
                _fadeProgress = 1.0;
                if (_currentWrapperGrid != null)
                    _currentWrapperGrid.Children.Remove(_fadingOutGlowImage);
                _fadingOutGlowImage = null;
            }
            else
            {
                // Fade out old glow
                _fadingOutGlowImage.Opacity = 1.0 - _fadeProgress;
            }
        }

        // Re-renders the outer glow image periodically for color-cycling presets.
        // Uses crossfade to smoothly blend between old and new color renders.
        private void UpdateCyclingOuterGlow()
        {
            if (_currentGlowImage == null || _currentIcon == null || _currentWrapperGrid == null) return;
            if (!HasCyclingOuterGlow(_settings.IconGlowPreset)) return;
            // Don't start a new re-render while a crossfade is already in progress
            if (_fadingOutGlowImage != null) return;
            if ((DateTime.UtcNow - _lastOuterGlowRerender).TotalSeconds < OuterGlowRerenderInterval) return;

            _lastOuterGlowRerender = DateTime.UtcNow;

            GetOuterGlowColors(out var c1, out var c2);
            var glowBitmap = GlowRenderer.RenderGlow(_currentIcon.Source, c1, c2,
                _settings.IconGlowSize, _currentIcon.ActualWidth, _currentIcon.ActualHeight, _settings.IconGlowIntensity);
            if (glowBitmap == null) return;

            var newGlowImage = GlowRenderer.CreateGlowImage(glowBitmap, _currentIcon.ActualWidth, _currentIcon.ActualHeight, _settings.IconGlowSize);

            // Share the same transform objects so scale/rotate updates affect both during crossfade
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_glowScale);
            transformGroup.Children.Add(_glowRotate);
            newGlowImage.RenderTransform = transformGroup;
            newGlowImage.RenderTransformOrigin = new Point(0.5, 0.5);
            newGlowImage.Opacity = 0; // starts invisible, fades in

            // Crossfade: old glow fades out, new glow fades in (slow blend over 1.2s)
            _fadingOutGlowImage = _currentGlowImage;
            _fadeProgress = 0;
            _activeCrossfadeDuration = CyclingCrossfadeDuration;
            _currentWrapperGrid.Children.Insert(0, newGlowImage);
            _currentGlowImage = newGlowImage;
        }

        private static bool HasCyclingOuterGlow(IconGlowPreset preset)
        {
            return preset == IconGlowPreset.Neon
                || preset == IconGlowPreset.SharpWarm
                || preset == IconGlowPreset.SharpCool;
        }

        // Gets the current outer glow color pair for cycling presets
        private void GetOuterGlowColors(out Color c1, out Color c2)
        {
            double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;

            switch (_settings.IconGlowPreset)
            {
                case IconGlowPreset.Neon:
                    c1 = CycleColors(NeonColors, 12.0);
                    // Offset second color by 1/4 of the cycle for gradient
                    double neonT2 = ((elapsed + 3.0) % 12.0) / 12.0 * 4.0;
                    int ni = (int)neonT2 % 4; int nn = (ni + 1) % 4;
                    double nf = neonT2 - Math.Floor(neonT2);
                    c2 = Color.FromRgb(
                        (byte)(NeonColors[ni, 0] * (1 - nf) + NeonColors[nn, 0] * nf),
                        (byte)(NeonColors[ni, 1] * (1 - nf) + NeonColors[nn, 1] * nf),
                        (byte)(NeonColors[ni, 2] * (1 - nf) + NeonColors[nn, 2] * nf));
                    break;
                case IconGlowPreset.SharpWarm:
                    c1 = CycleColors(WarmColors, 8.0);
                    double wT2 = ((elapsed + 2.67) % 8.0) / 8.0 * 3.0;
                    int wi = (int)wT2 % 3; int wn = (wi + 1) % 3;
                    double wf = wT2 - Math.Floor(wT2);
                    c2 = Color.FromRgb(
                        (byte)(WarmColors[wi, 0] * (1 - wf) + WarmColors[wn, 0] * wf),
                        (byte)(WarmColors[wi, 1] * (1 - wf) + WarmColors[wn, 1] * wf),
                        (byte)(WarmColors[wi, 2] * (1 - wf) + WarmColors[wn, 2] * wf));
                    break;
                case IconGlowPreset.SharpCool:
                    c1 = CycleColors(CoolColors, 8.0);
                    double cT2 = ((elapsed + 2.67) % 8.0) / 8.0 * 3.0;
                    int ci = (int)cT2 % 3; int cn = (ci + 1) % 3;
                    double cf = cT2 - Math.Floor(cT2);
                    c2 = Color.FromRgb(
                        (byte)(CoolColors[ci, 0] * (1 - cf) + CoolColors[cn, 0] * cf),
                        (byte)(CoolColors[ci, 1] * (1 - cf) + CoolColors[cn, 1] * cf),
                        (byte)(CoolColors[ci, 2] * (1 - cf) + CoolColors[cn, 2] * cf));
                    break;
                default:
                    c1 = Colors.White;
                    c2 = Colors.White;
                    break;
            }
        }

        private bool HasSettingsChanged()
        {
            if (_activeGame == null) return false;
            if (_settings.IconGlowPreset != _lastAppliedPreset) return true;
            return _settings.IconGlowPreset == IconGlowPreset.Custom &&
                   (_settings.IconGlowSize != _lastAppliedSize || _settings.IconGlowIntensity != _lastAppliedIntensity);
        }

        // === INTENSITY SOURCE SELECTION ===
        // Returns NaN to signal "static glow, already handled" (caller should return).

        private double ComputeRawIntensity()
        {
            var vizProvider = VisualizationDataProvider.Current;

            if (vizProvider != null)
                return ComputeAudioIntensity(vizProvider);

            if (_settings.EnableIconGlowPulse)
                return ComputePulseIntensity();

            // Static glow — only keep timer alive for spin
            _currentGlowImage.Opacity = 1.0;
            if (_innerGlow != null) _innerGlow.Opacity = 0.9;

            if (_settings.EnableIconGlowSpin && _glowRotate != null)
            {
                _glowAngle = (_glowAngle + 360.0 / (_settings.IconGlowSpinSpeed * 60.0)) % 360.0;
                _glowRotate.Angle = _glowAngle;
            }
            else
            {
                StopTimer();
            }
            return double.NaN;
        }

        // === AUDIO-REACTIVE INTENSITY (3-band FFT) ===

        private double ComputeAudioIntensity(VisualizationDataProvider vizProvider)
        {
            int specSize = vizProvider.SpectrumSize;
            if (specSize <= 0)
            {
                // No spectrum — fall back to RMS/peak
                vizProvider.GetLevels(out float peak, out float rms);
                double fallback = rms * 0.7 + peak * 0.3;
                _midIntensity = fallback;
                _trebleIntensity = fallback * 0.5;
                return fallback;
            }

            if (_spectrumBuf == null || _spectrumBuf.Length < specSize)
                _spectrumBuf = new float[specSize];
            vizProvider.GetSpectrumData(_spectrumBuf, 0, specSize);

            // Bin layout: ~43Hz/bin at 44100Hz / 1024-pt FFT
            int bassEnd   = Math.Min(6, specSize);     // 0–250Hz
            int midEnd    = Math.Min(93, specSize);     // 250–4000Hz
            int trebleEnd = Math.Min(279, specSize);    // 4000–12000Hz

            double bassLevel   = AverageBins(0, bassEnd);
            double midLevel    = AverageBins(bassEnd, midEnd);
            double trebleLevel = AverageBins(midEnd, trebleEnd);

            UpdateOnsetDetection(bassEnd);

            // Bass: rolling window → common mode → 3-stage smooth + punch
            double bassNorm = UpdateBand(bassLevel, _bassWindow, ref _bassWindowIdx, ref _bassWindowMax, ref _bassMaxAge);
            double bcmAlpha = bassNorm > _bassCommon ? 0.03 : 0.35;
            _bassCommon = bcmAlpha * bassNorm + (1 - bcmAlpha) * _bassCommon;

            // Strip more baseline for punchy presets so quiet moments go dark
            double cmStrip = 0.65;
            switch (_settings.IconGlowPreset)
            {
                case IconGlowPreset.Reactive:  cmStrip = 0.90; break;
                case IconGlowPreset.BassPunch: cmStrip = 0.85; break;
                case IconGlowPreset.Pulse:     cmStrip = 0.80; break;
                case IconGlowPreset.Neon:      cmStrip = 0.72; break;
            }
            double bassReactive = Math.Max(0, bassNorm - _bassCommon * cmStrip);

            // Preset-specific smoothing
            GetBassCoefficients(out double s3Decay, out double punchRise, out double punchDecay, out bool bassOnly);

            double s1Rise = _onsetFrames > 0 ? 0.95 : 0.75;
            if (_onsetFrames > 0) _onsetFrames--;
            _bassSmooth1 += (bassReactive - _bassSmooth1) * (bassReactive > _bassSmooth1 ? s1Rise : 0.25);
            _bassSmooth2 += (_bassSmooth1 - _bassSmooth2) * (_bassSmooth1 > _bassSmooth2 ? 0.35 : 0.15);
            _bassSmooth3 += (_bassSmooth2 - _bassSmooth3) * (_bassSmooth2 > _bassSmooth3 ? 0.25 : s3Decay);
            _bassPunch += (bassReactive - _bassPunch) * (bassReactive > _bassPunch ? punchRise : punchDecay);

            double bassIntensity;
            var preset = _settings.IconGlowPreset;

            if (preset == IconGlowPreset.Reactive || preset == IconGlowPreset.BassPunch)
            {
                // Full bypass — drive directly from punch for sharpest transients
                bassIntensity = Math.Min(1.0, Math.Pow(
                    _bassPunch * _settings.IconGlowAudioSensitivity,
                    preset == IconGlowPreset.Reactive ? 0.35 : 0.38));
            }
            else if (preset == IconGlowPreset.Pulse)
            {
                // Skip 1 smooth stage — blend smooth2 (two-stage) with punch
                double bassPunchWeight = Math.Min(0.45, Math.Max(0.15, bassReactive * 2.5));
                bassIntensity = _bassSmooth2 * (1 - bassPunchWeight) + _bassPunch * bassPunchWeight;
                bassIntensity = Math.Min(1.0, Math.Pow(bassIntensity * _settings.IconGlowAudioSensitivity, 0.42));
            }
            else if (preset == IconGlowPreset.Neon)
            {
                // Neon: blend punch with smooth for visible but not aggressive response
                double bassPunchWeight = Math.Min(0.35, Math.Max(0.15, bassReactive * 2.0));
                bassIntensity = _bassSmooth2 * (1 - bassPunchWeight) + _bassPunch * bassPunchWeight;
                bassIntensity = Math.Min(1.0, Math.Pow(bassIntensity * _settings.IconGlowAudioSensitivity, 0.45));
            }
            else if (preset == IconGlowPreset.Mellow)
            {
                // Mellow: heavy smoothing, gentle sway — use smooth3 only, no punch
                bassIntensity = Math.Min(1.0, Math.Pow(_bassSmooth3 * _settings.IconGlowAudioSensitivity, 0.60));
            }
            else
            {
                // All other presets: standard 3-stage smooth with light punch
                double bassPunchWeight = Math.Min(0.20, Math.Max(0.1, bassReactive * 2.0));
                bassIntensity = _bassSmooth3 * (1 - bassPunchWeight) + _bassPunch * bassPunchWeight;
                bassIntensity = Math.Min(1.0, Math.Pow(bassIntensity * _settings.IconGlowAudioSensitivity, 0.50));
            }

            if (bassOnly)
            {
                _midIntensity = 0;
                _trebleIntensity = 0;
            }
            else
            {
                // Mids: rolling window → common mode → single-stage smooth
                double midNorm = UpdateBand(midLevel, _midWindow, ref _midWindowIdx, ref _midWindowMax, ref _midMaxAge);
                double mcmAlpha = midNorm > _midCommon ? 0.05 : 0.25;
                _midCommon = mcmAlpha * midNorm + (1 - mcmAlpha) * _midCommon;
                double midReactive = Math.Max(0, midNorm - _midCommon * 0.60);
                _midSmooth += (midReactive - _midSmooth) * (midReactive > _midSmooth ? 0.40 : 0.18);
                _midIntensity = Math.Min(1.0, Math.Pow(_midSmooth * _settings.IconGlowAudioSensitivity, 0.6));

                // Treble: rolling window → fast single-stage smooth
                double trebleNorm = UpdateBand(trebleLevel, _trebleWindow, ref _trebleWindowIdx, ref _trebleWindowMax, ref _trebleMaxAge);
                _trebleSmooth += (trebleNorm - _trebleSmooth) * (trebleNorm > _trebleSmooth ? 0.20 : 0.30);
                _trebleIntensity = Math.Min(1.0, _trebleSmooth * _settings.IconGlowAudioSensitivity);
            }

            // Reactive blends all 3 bands — any frequency spike drives the glow
            if (_settings.IconGlowPreset == IconGlowPreset.Reactive)
            {
                double combined = bassIntensity * 0.50 + _midIntensity * 0.30 + _trebleIntensity * 0.20;
                return Math.Min(1.0, combined * 1.4); // boost so peaks still hit 1.0
            }

            return bassIntensity;
        }

        private double AverageBins(int from, int to)
        {
            if (to <= from) return 0;
            double sum = 0;
            for (int i = from; i < to; i++) sum += _spectrumBuf[i];
            return sum / (to - from);
        }

        // Rolling window peak normalization for one frequency band.
        // Returns the normalized value (0–1).
        private double UpdateBand(double level, double[] window, ref int idx, ref double max, ref int maxAge)
        {
            window[idx] = level;
            idx = (idx + 1) % PeakWindowSize;
            maxAge++;
            if (maxAge >= 30 || level > max)
            {
                max = 0.001;
                for (int i = 0; i < PeakWindowSize; i++)
                    if (window[i] > max) max = window[i];
                maxAge = 0;
            }
            return level / max;
        }

        private void UpdateOnsetDetection(int bassEnd)
        {
            if (_prevSpectrum == null || _prevSpectrum.Length < bassEnd)
                _prevSpectrum = new float[bassEnd];

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
        }

        private void GetBassCoefficients(out double s3Decay, out double punchRise, out double punchDecay, out bool bassOnly)
        {
            bassOnly = false;
            switch (_settings.IconGlowPreset)
            {
                case IconGlowPreset.Reactive:
                    s3Decay = 0.45; punchRise = 0.96; punchDecay = 0.70;
                    break;
                case IconGlowPreset.Pulse:
                    s3Decay = 0.35; punchRise = 0.92; punchDecay = 0.60;
                    break;
                case IconGlowPreset.BassPunch:
                    s3Decay = 0.40; punchRise = 0.95; punchDecay = 0.65;
                    bassOnly = true;
                    break;
                case IconGlowPreset.Neon:
                    s3Decay = 0.25; punchRise = 0.80; punchDecay = 0.50;
                    break;
                case IconGlowPreset.Mellow:
                    s3Decay = 0.08; punchRise = 0.40; punchDecay = 0.20;
                    break;
                default:
                    s3Decay = 0.22; punchRise = 0.70; punchDecay = 0.45;
                    break;
            }
        }

        // === SINE PULSE INTENSITY (fallback when no audio) ===

        private double ComputePulseIntensity()
        {
            double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double sine = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * elapsed / _settings.IconGlowPulseSpeed);

            switch (_settings.IconGlowPreset)
            {
                case IconGlowPreset.Ambient:
                    return 0.25 + sine * 0.75; // gentle, never fully dark
                case IconGlowPreset.Subtle:
                    return 0.70 + sine * 0.30; // barely-there movement
                case IconGlowPreset.SharpStatic:
                    return 0.80 + sine * 0.20; // nearly still, minimal breathing
                case IconGlowPreset.SharpBright:
                    return 0.35 + sine * 0.65; // visible pulse, stays bright
                case IconGlowPreset.SharpVivid:
                    return Math.Pow(sine, 0.7); // dramatic full-range, slight bias toward bright
                case IconGlowPreset.SharpWarm:
                case IconGlowPreset.SharpCool:
                    return 0.45 + sine * 0.55; // moderate breathing to show off color cycling
                case IconGlowPreset.SharpPulse:
                {
                    // Heartbeat shape: sharp attack, slow decay
                    double phase = (elapsed % _settings.IconGlowPulseSpeed) / _settings.IconGlowPulseSpeed;
                    double beat = phase < 0.15 ? Math.Pow(phase / 0.15, 0.5) // fast rise
                                : phase < 0.30 ? 1.0 - (phase - 0.15) / 0.15 * 0.6 // quick drop to 0.4
                                : phase < 0.45 ? 0.4 + (phase - 0.30) / 0.15 * 0.35 // second smaller bump
                                : 0.75 * Math.Pow(1.0 - (phase - 0.45) / 0.55, 1.5); // slow tail off
                    return beat;
                }
                case IconGlowPreset.Mellow:
                    return 0.40 + sine * 0.60; // warm, always visible, gentle sway
                default:
                    return sine;
            }
        }

        // === VISUAL OUTPUT ===

        private void GetOpacityRange(out double floor, out double range)
        {
            switch (_settings.IconGlowPreset)
            {
                // Audio-reactive: wide dynamic range, quiet = dim, loud = bright
                case IconGlowPreset.Reactive:
                    floor = 0.05; range = 0.95; break;
                case IconGlowPreset.BassPunch:
                    floor = 0.08; range = 0.92; break;
                case IconGlowPreset.Pulse:
                    floor = 0.38; range = 0.57; break;
                case IconGlowPreset.Neon:
                    floor = 0.25; range = 0.75; break;
                case IconGlowPreset.Mellow:
                    floor = 0.45; range = 0.40; break; // always visible, gentle sway

                // Visual-only: each has distinct feel
                case IconGlowPreset.Ambient:
                    floor = 0.35; range = 0.50; break; // visible but gentle breathing
                case IconGlowPreset.Subtle:
                    floor = 0.50; range = 0.25; break; // barely-there, minimal change
                case IconGlowPreset.SharpStatic:
                    floor = 0.65; range = 0.15; break; // nearly constant, edge highlight
                case IconGlowPreset.SharpBright:
                    floor = 0.35; range = 0.55; break; // visible pulse
                case IconGlowPreset.SharpVivid:
                    floor = 0.10; range = 0.90; break; // dramatic full-range flash
                case IconGlowPreset.SharpWarm:
                case IconGlowPreset.SharpCool:
                    floor = 0.40; range = 0.45; break; // moderate, shows off color cycling
                case IconGlowPreset.SharpPulse:
                    floor = 0.08; range = 0.92; break; // heartbeat: near-dark between beats
                default:
                    floor = 0.50; range = 0.50; break;
            }
        }

        private void UpdateScale()
        {
            if (_glowScale == null) return;

            var preset = _settings.IconGlowPreset;
            double maxExpand;
            switch (preset)
            {
                case IconGlowPreset.Reactive:  maxExpand = 0.12; break; // up to 12% larger on beats
                case IconGlowPreset.BassPunch: maxExpand = 0.10; break;
                case IconGlowPreset.Pulse:     maxExpand = 0.03; break;
                case IconGlowPreset.Neon:      maxExpand = 0.06; break;
                default:                       maxExpand = 0.0;  break;
            }

            double scale = 1.0 + _smoothedIntensity * maxExpand;
            _glowScale.ScaleX = scale;
            _glowScale.ScaleY = scale;
        }

        private void UpdateRotation()
        {
            if (_glowRotate == null || !_settings.EnableIconGlowSpin) return;

            double baseSpeed = 360.0 / (_settings.IconGlowSpinSpeed * 60.0); // degrees per frame

            // Audio-reactive presets: spin accelerates on beats (opt-in)
            if (_settings.EnableIconGlowSpinAcceleration)
            {
                var preset = _settings.IconGlowPreset;
                if (preset == IconGlowPreset.Reactive || preset == IconGlowPreset.Pulse
                    || preset == IconGlowPreset.BassPunch || preset == IconGlowPreset.Neon)
                {
                    double boost = 1.0 + _smoothedIntensity * 2.0;
                    baseSpeed *= boost;
                }
            }

            _glowAngle = (_glowAngle + baseSpeed) % 360.0;
            _glowRotate.Angle = _glowAngle;
        }

        private void UpdateInnerHalo(double glowFloor, double glowRange)
        {
            if (_innerGlow == null) return;

            double opacity = glowFloor + _smoothedIntensity * glowRange;
            _innerGlow.Opacity = opacity;
            var preset = _settings.IconGlowPreset;

            if (IsSharpPreset(preset))
            {
                UpdateSharpHalo(preset, opacity);
            }
            else if (preset == IconGlowPreset.Neon)
            {
                _innerGlow.BlurRadius = 8;
                _innerGlow.Color = CycleColors(NeonColors, 12.0);
            }
            else
            {
                double colorShift = GetColorShift(preset);
                double baseBlur = preset == IconGlowPreset.Mellow ? 10.0 : 8.0;

                // Hot presets bloom outward at peaks (blur expands with intensity)
                bool isHot = preset == IconGlowPreset.Pulse || preset == IconGlowPreset.Reactive
                          || preset == IconGlowPreset.BassPunch;
                double bloom = isHot ? _smoothedIntensity * 6.0 : 0; // up to +6px blur at peak
                _innerGlow.BlurRadius = baseBlur + bloom;
                _innerGlow.Color = ShiftToWhite(_glowBaseColor, _smoothedIntensity * colorShift);
            }
        }

        private static bool IsSharpPreset(IconGlowPreset p)
        {
            return p == IconGlowPreset.SharpStatic || p == IconGlowPreset.SharpBright
                || p == IconGlowPreset.SharpVivid  || p == IconGlowPreset.SharpWarm
                || p == IconGlowPreset.SharpCool   || p == IconGlowPreset.SharpPulse;
        }

        private void UpdateSharpHalo(IconGlowPreset preset, double opacity)
        {
            // Per-variant blur radius
            _innerGlow.BlurRadius = preset == IconGlowPreset.SharpBright ? 5
                                  : preset == IconGlowPreset.SharpStatic ? 3
                                  : 4;

            if (preset == IconGlowPreset.SharpWarm)
            {
                _innerGlow.Color = CycleColors(WarmColors, 8.0);
            }
            else if (preset == IconGlowPreset.SharpCool)
            {
                _innerGlow.Color = CycleColors(CoolColors, 8.0);
            }
            else
            {
                double shift = preset == IconGlowPreset.SharpVivid  ? 0.85
                             : preset == IconGlowPreset.SharpBright ? 0.65
                             : preset == IconGlowPreset.SharpPulse  ? 0.70
                             : 0.25; // SharpStatic
                _innerGlow.Color = ShiftToWhite(_glowBaseColor, _smoothedIntensity * shift);
            }
        }

        private static double GetColorShift(IconGlowPreset preset)
        {
            switch (preset)
            {
                case IconGlowPreset.Pulse:     return 0.80; // moderate white shift at peak
                case IconGlowPreset.Reactive:   return 0.95;
                case IconGlowPreset.BassPunch:  return 0.90;
                case IconGlowPreset.Mellow:     return 0.40;
                case IconGlowPreset.Subtle:     return 0.30;
                default:                        return 0.85;
            }
        }

        // === COLOR UTILITIES ===

        // Linearly interpolates base color toward white by the given amount (0–1).
        private static Color ShiftToWhite(Color baseColor, double amount)
        {
            byte r = (byte)(baseColor.R + (255 - baseColor.R) * amount);
            byte g = (byte)(baseColor.G + (255 - baseColor.G) * amount);
            byte b = (byte)(baseColor.B + (255 - baseColor.B) * amount);
            return Color.FromRgb(r, g, b);
        }

        // Smoothly cycles through a color palette over the given period (seconds).
        private Color CycleColors(byte[,] palette, double periodSeconds)
        {
            int count = palette.GetLength(0);
            double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double t = (elapsed % periodSeconds) / periodSeconds * count;
            int idx = (int)t % count;
            int next = (idx + 1) % count;
            double frac = t - Math.Floor(t);

            byte r = (byte)(palette[idx, 0] * (1 - frac) + palette[next, 0] * frac);
            byte g = (byte)(palette[idx, 1] * (1 - frac) + palette[next, 1] * frac);
            byte b = (byte)(palette[idx, 2] * (1 - frac) + palette[next, 2] * frac);
            return Color.FromRgb(r, g, b);
        }

        // Color palettes for cycling presets
        private static readonly byte[,] NeonColors = {
            { 0,   255, 255 }, // cyan
            { 255, 0,   220 }, // magenta
            { 0,   255, 80  }, // lime
            { 80,  0,   255 }  // electric blue
        };

        private static readonly byte[,] WarmColors = {
            { 255, 215, 0   }, // gold
            { 255, 140, 0   }, // amber
            { 255, 69,  0   }  // orange-red
        };

        private static readonly byte[,] CoolColors = {
            { 173, 216, 230 }, // ice blue
            { 0,   128, 128 }, // teal
            { 0,   255, 255 }  // cyan
        };
    }
}
