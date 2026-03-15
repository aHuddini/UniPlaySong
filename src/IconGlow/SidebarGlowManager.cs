using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Audio;
using UniPlaySong.Services;

namespace UniPlaySong.IconGlow
{
    // Audio-reactive sidebar effects. Two modes:
    // Breathing: subtle tint + opacity pulse on the sidebar border.
    // DotTrail: colored dots on a Canvas overlay that bounce with audio energy.
    class SidebarGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IconColorExtractor _colorExtractor;
        private readonly SettingsService _settingsService;
        private readonly IPlayniteAPI _api;

        private UniPlaySongSettings _settings => _settingsService.Current;

        private Border _sidebarBorder;
        private Control _sidebarControl;
        private Brush _originalBackground;
        private double _originalOpacity;
        private bool _originalSaved;
        private Color _baseColor = Color.FromRgb(100, 149, 237);
        private Color _secondaryColor = Color.FromRgb(200, 150, 100);
        private Color _lastAppliedColor;
        private double _smoothedEnergy;
        private DateTime _pulseStartTime = DateTime.UtcNow;
        private bool _attached;
        private bool _hasLoggedVizState;
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private double _deltaTime; // seconds since last frame, capped

        // Glow bar state
        private Canvas _barCanvas;
        private Grid _wrapperGrid;
        private UIElement _originalChild;
        private const int BarCount = 10;
        private const int GlowLayers = 3; // layers per bar for bloom falloff
        private System.Windows.Shapes.Rectangle[,] _bars; // [barIndex, glowLayer]
        private double[] _barSmoothed; // smoothed energy per bar
        private double[] _barPeaks; // peak hold per bar
        // Pixel grid state
        private Canvas _pixelCanvas;
        private const int PixelCols = 4;
        private const int PixelRows = 20;
        private System.Windows.Shapes.Rectangle[,] _pixels; // [row, col]
        private SidebarGlowMode _activeMode;

        // Generic canvas for new modes (shared inject/remove)
        private Canvas _effectCanvas;
        private Grid _effectWrapperGrid;
        private UIElement _effectOriginalChild;

        // Rain drops state
        private const int RainCols = 4;
        private const int RainMaxDrops = 12;
        private Rectangle[] _rainDrops;
        private double[] _rainDropY;      // current Y position
        private double[] _rainDropSpeed;  // fall speed
        private double[] _rainDropFade;   // opacity (1.0→0.0)
        private int[] _rainDropCol;       // column index
        private Random _rng = new Random();

        // Waveform state
        private const int WavePoints = 30;
        private System.Windows.Shapes.Polyline _waveLine;
        private double _wavePhase;

        // Fire state
        private const int FireCols = 6;
        private const int FireRows = 15;
        private Rectangle[,] _fireCells;
        private double[,] _fireHeat; // heat map

        // Starfield state
        private const int MaxStars = 25;
        private System.Windows.Shapes.Ellipse[] _stars;
        private double[] _starX;
        private double[] _starY;
        private double[] _starSpeed;
        private double[] _starBrightness;
        private double[] _starSize;

        // Ripple state
        private const int MaxRipples = 5;
        private System.Windows.Shapes.Ellipse[] _ripples;
        private double[] _rippleRadius;
        private double[] _rippleFade;
        private double _rippleCooldown;

        // Aurora state
        private const int AuroraBands = 8;
        private Rectangle[] _auroraBars;
        private double[] _auroraOffset;

        // Heartbeat state
        private System.Windows.Shapes.Polyline _heartLine;
        private double[] _heartHistory;
        private const int HeartHistoryLen = 60;
        private int _heartIndex;
        private double _heartAccum; // accumulate time before recording a sample

        // Waterfall state
        private const int WaterfallRows = 25;
        private const int WaterfallCols = 5;
        private Rectangle[,] _waterfallCells;
        private double[,] _waterfallData; // scrolling color data
        private double _waterfallAccum; // accumulate time before scrolling

        // Nebula state (Kabuto point cloud adaptation)
        private const int NebulaParticles = 40;
        private System.Windows.Shapes.Ellipse[] _nebulaParticles;

        // DNA Helix state
        private const int HelixDots = 20;
        private System.Windows.Shapes.Ellipse[] _helixDotsA;
        private System.Windows.Shapes.Ellipse[] _helixDotsB;

        // DNA Helix Bloom state (glow halos behind each dot)
        private System.Windows.Shapes.Ellipse[] _helixGlowA;
        private System.Windows.Shapes.Ellipse[] _helixGlowB;

        // Pulse Waves state (Guyver-style glowing sine waves)
        private const int PulseWaveCount = 4;
        private const int PulseWavePoints = 40;
        private System.Windows.Shapes.Polyline[] _pulseLines;
        private System.Windows.Shapes.Polyline[] _pulseGlowLines;

        // Laser state (converging beams)
        private const int LaserBeams = 16;
        private System.Windows.Shapes.Line[] _laserLines;
        private System.Windows.Shapes.Line[] _laserGlowLines; // soft glow behind each beam

        // Voronoi state (glowing vertical line distorted by Voronoi noise)
        private const int VoronoiLinePoints = 50;
        private System.Windows.Shapes.Polyline _voronoiLine;
        private System.Windows.Shapes.Polyline _voronoiGlowLine;

        // Equalizer Grid state
        private const int EqGridCols = 4;
        private const int EqGridRows = 20;
        private Rectangle[,] _eqGridCells;

        // Snow state (multi-layer parallax snowfall)
        private const int SnowLayers = 5;
        private const int SnowPerLayer = 15;
        private System.Windows.Shapes.Ellipse[,] _snowFlakes; // [layer, index]
        private double[,] _snowX;
        private double[,] _snowY;
        private double[,] _snowDrift; // horizontal drift phase per flake

        // Neon Line state (smooth glowing vertical beam)
        private const int NeonLinePoints = 50;
        private System.Windows.Shapes.Polyline _neonLine;
        private System.Windows.Shapes.Polyline _neonGlowLine;

        // Matrix state
        private const int MatrixCols = 6;
        private const int MatrixMaxStreaks = 15;
        private Rectangle[] _matrixCells;
        private double[] _matrixY;
        private double[] _matrixSpeed;
        private int[] _matrixCol;
        private double[] _matrixLength; // trail length in pixels
        private double[] _matrixBrightness;

        public SidebarGlowManager(IconColorExtractor colorExtractor, SettingsService settingsService, IPlayniteAPI api)
        {
            _colorExtractor = colorExtractor;
            _settingsService = settingsService;
            _api = api;
        }

        public void Attach()
        {
            if (_attached) return;

            _sidebarBorder = FindSidebarBorder(out _sidebarControl);
            if (_sidebarBorder == null || _sidebarControl == null)
            {
                Logger.Warn("[SidebarGlow] Could not find sidebar elements");
                return;
            }

            if (!_originalSaved)
            {
                _originalBackground = _sidebarBorder.Background;
                _originalOpacity = _sidebarBorder.Opacity;
                _originalSaved = true;
            }

            _attached = true;
            _pulseStartTime = DateTime.UtcNow;
            _activeMode = _settings.SidebarGlowMode;

            if (_settings.EnableSidebarGlow)
            {
                ActivateMode(_activeMode);
                CompositionTarget.Rendering += OnRendering;
            }

            _settingsService.Current.PropertyChanged += OnSettingChanged;
            _settingsService.SettingsChanged += OnSettingsReplaced;
            Logger.Info($"[SidebarGlow] Attached, mode={_activeMode}");
        }

        public void Detach()
        {
            if (!_attached) return;

            CompositionTarget.Rendering -= OnRendering;
            _settingsService.Current.PropertyChanged -= OnSettingChanged;
            _settingsService.SettingsChanged -= OnSettingsReplaced;
            DeactivateMode();
            RestoreOriginal();

            _attached = false;
            Logger.Info("[SidebarGlow] Detached");
        }

        public void OnGameSelected(Game game)
        {
            if (!_attached) return;

            if (game == null)
            {
                _baseColor = Color.FromRgb(100, 149, 237);
                _secondaryColor = Color.FromRgb(200, 150, 100);
                return;
            }

            try
            {
                var iconPath = game.Icon != null ? _api.Database.GetFullFilePath(game.Icon) : null;
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(iconPath);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var (primary, secondary) = _colorExtractor.GetGlowColors(game.Id, bitmap);
                    _baseColor = primary;
                    _secondaryColor = secondary;
                }
                else
                {
                    _baseColor = Color.FromRgb(100, 149, 237);
                    _secondaryColor = Color.FromRgb(200, 150, 100);
                }
            }
            catch
            {
                _baseColor = Color.FromRgb(100, 149, 237);
                _secondaryColor = Color.FromRgb(200, 150, 100);
            }

            _smoothedEnergy = 0;
        }

        // Handles settings object replacement (save/load replaces the entire object)
        private void OnSettingsReplaced(object sender, SettingsChangedEventArgs e)
        {
            // Re-subscribe PropertyChanged on the new settings object
            if (e.OldSettings != null)
                e.OldSettings.PropertyChanged -= OnSettingChanged;
            if (e.NewSettings != null)
                e.NewSettings.PropertyChanged += OnSettingChanged;

            // Check if enable or mode changed
            bool wasEnabled = e.OldSettings?.EnableSidebarGlow ?? false;
            bool isEnabled = e.NewSettings?.EnableSidebarGlow ?? false;
            var newMode = e.NewSettings?.SidebarGlowMode ?? SidebarGlowMode.Breathing;

            Logger.Info($"[SidebarGlow] OnSettingsReplaced: wasEnabled={wasEnabled}, isEnabled={isEnabled}, oldMode={e.OldSettings?.SidebarGlowMode}, newMode={newMode}, activeMode={_activeMode}");

            if (!wasEnabled && isEnabled)
            {
                _activeMode = newMode;
                ActivateMode(_activeMode);
                CompositionTarget.Rendering += OnRendering;
                Logger.Info($"[SidebarGlow] Enabled, activated mode={_activeMode}");
            }
            else if (wasEnabled && !isEnabled)
            {
                CompositionTarget.Rendering -= OnRendering;
                DeactivateMode();
                RestoreOriginal();
                Logger.Info("[SidebarGlow] Disabled");
            }
            else if (isEnabled && newMode != _activeMode)
            {
                DeactivateMode();
                RestoreOriginal();
                _activeMode = newMode;
                ActivateMode(_activeMode);
                Logger.Info($"[SidebarGlow] Mode switched to {_activeMode}");
            }
            else
            {
                Logger.Info("[SidebarGlow] No sidebar glow change detected");
            }
        }

        private void OnSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.EnableSidebarGlow))
            {
                if (_settings.EnableSidebarGlow)
                {
                    _activeMode = _settings.SidebarGlowMode;
                    ActivateMode(_activeMode);
                    CompositionTarget.Rendering += OnRendering;
                }
                else
                {
                    CompositionTarget.Rendering -= OnRendering;
                    DeactivateMode();
                    RestoreOriginal();
                }
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.SidebarGlowMode) && _settings.EnableSidebarGlow)
            {
                DeactivateMode();
                RestoreOriginal();
                _activeMode = _settings.SidebarGlowMode;
                ActivateMode(_activeMode);
            }
        }

        private void ActivateMode(SidebarGlowMode mode)
        {
            if (mode == SidebarGlowMode.GlowBars || mode == SidebarGlowMode.PlasmaGrid || mode == SidebarGlowMode.PlasmaTinted)
                InjectDotCanvas();
            else if (mode == SidebarGlowMode.PixelGrid)
                InjectPixelCanvas();
            else if (mode == SidebarGlowMode.RainDrops)
                InjectRainDrops();
            else if (mode == SidebarGlowMode.Waveform)
                InjectWaveform();
            else if (mode == SidebarGlowMode.Fire)
                InjectFire();
            else if (mode == SidebarGlowMode.Starfield)
                InjectStarfield();
            else if (mode == SidebarGlowMode.Ripple)
                InjectRipple();
            else if (mode == SidebarGlowMode.Aurora)
                InjectAurora();
            else if (mode == SidebarGlowMode.Heartbeat)
                InjectHeartbeat();
            else if (mode == SidebarGlowMode.Waterfall)
                InjectWaterfall();
            else if (mode == SidebarGlowMode.Nebula)
                InjectNebula();
            else if (mode == SidebarGlowMode.DnaHelix)
                InjectDnaHelix();
            else if (mode == SidebarGlowMode.DnaHelixBloom)
                InjectDnaHelixBloom();
            else if (mode == SidebarGlowMode.Matrix)
                InjectMatrix();
            else if (mode == SidebarGlowMode.Laser)
                InjectLaser();
            else if (mode == SidebarGlowMode.PulseWaves)
                InjectPulseWaves();
            else if (mode == SidebarGlowMode.Voronoi)
                InjectVoronoi();
            else if (mode == SidebarGlowMode.EqualizerGrid)
                InjectEqualizerGrid();
            else if (mode == SidebarGlowMode.Snow)
                InjectSnow();
            else if (mode == SidebarGlowMode.NeonLine)
                InjectNeonLine();
        }

        private void DeactivateMode()
        {
            RemoveDotCanvas();
            RemovePixelCanvas();
            RemoveEffectCanvas();
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!_settings.EnableSidebarGlow || _sidebarBorder == null) return;

            // Delta time: normalize all animation speeds to be frame-rate independent
            var now = DateTime.UtcNow;
            _deltaTime = (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;
            // Cap to avoid jumps after tab-away or debugger pause (target ~60fps = 0.0167s)
            if (_deltaTime > 0.05) _deltaTime = 0.0167;
            if (_deltaTime <= 0) _deltaTime = 0.0167;

            double energy = ComputeEnergy();

            if (_activeMode == SidebarGlowMode.Breathing)
                RenderBreathing(energy);
            else if (_activeMode == SidebarGlowMode.GlowBars)
                RenderGlowBars(energy);
            else if (_activeMode == SidebarGlowMode.PlasmaGrid)
                RenderPlasmaGrid(energy);
            else if (_activeMode == SidebarGlowMode.PlasmaTinted)
                RenderPlasmaTinted(energy);
            else if (_activeMode == SidebarGlowMode.PixelGrid)
                RenderPixelGrid(energy);
            else if (_activeMode == SidebarGlowMode.RainDrops)
                RenderRainDrops(energy);
            else if (_activeMode == SidebarGlowMode.Waveform)
                RenderWaveform(energy);
            else if (_activeMode == SidebarGlowMode.Fire)
                RenderFire(energy);
            else if (_activeMode == SidebarGlowMode.Starfield)
                RenderStarfield(energy);
            else if (_activeMode == SidebarGlowMode.Ripple)
                RenderRipple(energy);
            else if (_activeMode == SidebarGlowMode.Aurora)
                RenderAurora(energy);
            else if (_activeMode == SidebarGlowMode.Heartbeat)
                RenderHeartbeat(energy);
            else if (_activeMode == SidebarGlowMode.Waterfall)
                RenderWaterfall(energy);
            else if (_activeMode == SidebarGlowMode.Nebula)
                RenderNebula(energy);
            else if (_activeMode == SidebarGlowMode.DnaHelix)
                RenderDnaHelix(energy);
            else if (_activeMode == SidebarGlowMode.DnaHelixBloom)
                RenderDnaHelixBloom(energy);
            else if (_activeMode == SidebarGlowMode.Matrix)
                RenderMatrix(energy);
            else if (_activeMode == SidebarGlowMode.Laser)
                RenderLaser(energy);
            else if (_activeMode == SidebarGlowMode.PulseWaves)
                RenderPulseWaves(energy);
            else if (_activeMode == SidebarGlowMode.Voronoi)
                RenderVoronoi(energy);
            else if (_activeMode == SidebarGlowMode.EqualizerGrid)
                RenderEqualizerGrid(energy);
            else if (_activeMode == SidebarGlowMode.Snow)
                RenderSnow(energy);
            else if (_activeMode == SidebarGlowMode.NeonLine)
                RenderNeonLine(energy);
        }

        // --- Breathing mode ---

        private void RenderBreathing(double energy)
        {
            var color = MapColor(_baseColor, energy);
            if (color == _lastAppliedColor) return;

            _sidebarBorder.Background = new SolidColorBrush(
                Color.FromArgb(35, color.R, color.G, color.B));
            _sidebarBorder.Opacity = 0.75 + energy * 0.25;
            _lastAppliedColor = color;
        }

        // --- Glow bar mode (vertical VU meter with bloom) ---

        private void InjectDotCanvas()
        {
            if (_barCanvas != null || _sidebarBorder == null) return;

            _originalChild = _sidebarBorder.Child as UIElement;
            if (_originalChild == null) return;

            _barCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            // Each bar has multiple glow layers: outermost = wide+faint, innermost = narrow+bright
            _bars = new Rectangle[BarCount, GlowLayers];
            _barSmoothed = new double[BarCount];
            _barPeaks = new double[BarCount];

            for (int i = 0; i < BarCount; i++)
            {
                for (int g = GlowLayers - 1; g >= 0; g--)
                {
                    _bars[i, g] = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Opacity = 0,
                        RadiusX = 1,
                        RadiusY = 1
                    };
                    _barCanvas.Children.Add(_bars[i, g]);
                }
                _barSmoothed[i] = 0;
                _barPeaks[i] = 0;
            }

            _sidebarBorder.Child = null;
            _wrapperGrid = new Grid();
            _wrapperGrid.Children.Add(_barCanvas);      // glow behind
            _wrapperGrid.Children.Add(_originalChild);   // icons on top
            _sidebarBorder.Child = _wrapperGrid;

            Logger.Info("[SidebarGlow] Glow bar canvas injected");
        }

        private void RemoveDotCanvas()
        {
            if (_barCanvas == null || _sidebarBorder == null || _originalChild == null) return;

            if (_wrapperGrid != null)
            {
                _wrapperGrid.Children.Clear();
                _sidebarBorder.Child = _originalChild;
            }

            _barCanvas = null;
            _wrapperGrid = null;
            _bars = null;
            _barSmoothed = null;
            _barPeaks = null;
            _originalChild = null;
            Logger.Info("[SidebarGlow] Glow bar canvas removed");
        }

        private void RenderGlowBars(double energy)
        {
            if (_barCanvas == null || _bars == null) return;

            double canvasHeight = _sidebarBorder.ActualHeight;
            double canvasWidth = _sidebarBorder.ActualWidth;
            if (canvasHeight <= 0 || canvasWidth <= 0) return;

            var color = MapColor(_baseColor, energy);

            // Bar layout: bars fill from bottom upward like a VU meter
            double gap = 2.0;
            double barHeight = (canvasHeight - gap * (BarCount + 1)) / BarCount;
            if (barHeight < 2) barHeight = 2;

            // Energy fills bars from bottom up; each bar has a threshold
            for (int i = 0; i < BarCount; i++)
            {
                // Bar 0 = bottom (lowest threshold), Bar N-1 = top (highest)
                double threshold = (double)i / BarCount;
                double barEnergy = Math.Max(0, (energy - threshold) / (1.0 - threshold));
                barEnergy = Math.Min(1.0, barEnergy);

                // Smooth: fast attack, slow decay
                double alpha = barEnergy > _barSmoothed[i] ? 0.35 : 0.06;
                _barSmoothed[i] += (barEnergy - _barSmoothed[i]) * alpha;

                // Peak hold with slow decay
                if (_barSmoothed[i] > _barPeaks[i])
                    _barPeaks[i] = _barSmoothed[i];
                else
                    _barPeaks[i] *= 0.995;

                double intensity = _barSmoothed[i];

                // Fade out toward top: bottom bars fully opaque, top bars transparent
                // heightFade: 1.0 at bottom, ~0.25 at top
                double heightFade = 1.0 - (double)i / BarCount * 0.75;

                // Y position: bottom-up (bar 0 at bottom)
                double y = canvasHeight - (i + 1) * (barHeight + gap);

                // Render glow layers — all full width, differentiated by opacity
                for (int g = 0; g < GlowLayers; g++)
                {
                    double layerFraction = (double)(g + 1) / GlowLayers;
                    double layerOpacity = intensity * layerFraction * 0.7 * heightFade;

                    var layerColor = color;
                    if (g == GlowLayers - 1)
                    {
                        layerColor = Color.FromRgb(
                            (byte)Math.Min(255, color.R + 30),
                            (byte)Math.Min(255, color.G + 20),
                            (byte)Math.Min(255, color.B + 10));
                    }

                    byte a = (byte)(255 * Math.Min(0.9, layerOpacity));
                    _bars[i, g].Fill = new SolidColorBrush(Color.FromArgb(a, layerColor.R, layerColor.G, layerColor.B));
                    _bars[i, g].Width = canvasWidth;
                    _bars[i, g].Height = barHeight + gap;
                    _bars[i, g].Opacity = 1.0;
                    Canvas.SetLeft(_bars[i, g], 0);
                    Canvas.SetTop(_bars[i, g], y);
                }
            }

            _lastAppliedColor = color;
        }

        // --- Plasma grid mode ---
        // Inspired by johanberonius "plasma grid": time-varying cos*sin color waves
        // modulated by audio energy. Each bar cell gets a unique evolving color.

        private void RenderPlasmaGrid(double energy)
        {
            if (_barCanvas == null || _bars == null) return;

            double canvasHeight = _sidebarBorder.ActualHeight;
            double canvasWidth = _sidebarBorder.ActualWidth;
            if (canvasHeight <= 0 || canvasWidth <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double gap = 2.0;
            double barHeight = (canvasHeight - gap * (BarCount + 1)) / BarCount;
            if (barHeight < 2) barHeight = 2;

            // Smooth energy for plasma intensity
            for (int i = 0; i < BarCount; i++)
            {
                double alpha = energy > _barSmoothed[i] ? 0.3 : 0.05;
                _barSmoothed[i] += (energy - _barSmoothed[i]) * alpha;
            }

            // Base color HSV for tinting the plasma toward the game icon color
            var (baseHue, baseSat, _) = RgbToHsv(_baseColor.R, _baseColor.G, _baseColor.B);

            for (int i = 0; i < BarCount; i++)
            {
                double v = (double)i / BarCount; // normalized position 0..1
                double snd = _barSmoothed[i];

                // Plasma color channels: slow time-varying cos*sin waves
                // Each channel uses different frequencies for organic color drift
                double r = snd * Math.Pow(Math.Cos(time * 1.92 + v * 2.9) * Math.Sin(-time * 1.63 + v * 2.9) * 0.5 + 0.5, 3.0);
                double g = snd * Math.Pow(Math.Cos(time * 1.74 - v * 2.41) * Math.Sin(time * 1.34 - v * 3.4) * 0.5 + 0.5, 3.0);
                double b = snd * Math.Pow(Math.Cos(time * 1.21 + v * 1.5) * Math.Sin(time * 1.53 + v * 1.41) * 0.5 + 0.5, 3.0);

                // Tint toward base color: blend plasma RGB with game icon color
                double tint = 0.4;
                double baseR = _baseColor.R / 255.0;
                double baseG = _baseColor.G / 255.0;
                double baseB = _baseColor.B / 255.0;
                r = r * (1 - tint) + baseR * snd * tint;
                g = g * (1 - tint) + baseG * snd * tint;
                b = b * (1 - tint) + baseB * snd * tint;

                // Fade out toward top
                double heightFade = 1.0 - (double)i / BarCount * 0.75;

                // Y position: bottom-up
                double y = canvasHeight - (i + 1) * (barHeight + gap);

                // Plasma fills full width — no glow layer narrowing
                byte cr = (byte)Math.Min(255, r * 255);
                byte cg = (byte)Math.Min(255, g * 255);
                byte cb = (byte)Math.Min(255, b * 255);
                byte ca = (byte)Math.Min(230, 255 * snd * 0.8 * heightFade);

                // Use only the innermost layer at full width; hide outer layers
                for (int gl = 0; gl < GlowLayers; gl++)
                {
                    if (gl == GlowLayers - 1)
                    {
                        _bars[i, gl].Fill = new SolidColorBrush(Color.FromArgb(ca, cr, cg, cb));
                        _bars[i, gl].Width = canvasWidth;
                        _bars[i, gl].Height = barHeight + gap; // fill the gap for seamless look
                        _bars[i, gl].Opacity = 1.0;
                        Canvas.SetLeft(_bars[i, gl], 0);
                        Canvas.SetTop(_bars[i, gl], y);
                    }
                    else
                    {
                        _bars[i, gl].Opacity = 0;
                    }
                }
            }
        }

        // --- Plasma Tinted mode ---
        // Like Plasma Grid but colors are locked to the game icon's primary and secondary
        // colors. Plasma waves modulate between the two extracted colors instead of
        // free-cycling RGB, keeping the effect visually matched to the game art.

        private void RenderPlasmaTinted(double energy)
        {
            if (_barCanvas == null || _bars == null) return;

            double canvasHeight = _sidebarBorder.ActualHeight;
            double canvasWidth = _sidebarBorder.ActualWidth;
            if (canvasHeight <= 0 || canvasWidth <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double gap = 2.0;
            double barHeight = (canvasHeight - gap * (BarCount + 1)) / BarCount;
            if (barHeight < 2) barHeight = 2;

            for (int i = 0; i < BarCount; i++)
            {
                double alpha = energy > _barSmoothed[i] ? 0.3 : 0.05;
                _barSmoothed[i] += (energy - _barSmoothed[i]) * alpha;
            }

            // Primary and secondary colors from game icon
            double pR = _baseColor.R / 255.0;
            double pG = _baseColor.G / 255.0;
            double pB = _baseColor.B / 255.0;
            double sR = _secondaryColor.R / 255.0;
            double sG = _secondaryColor.G / 255.0;
            double sB = _secondaryColor.B / 255.0;

            for (int i = 0; i < BarCount; i++)
            {
                double v = (double)i / BarCount;
                double snd = _barSmoothed[i];

                // Blend factor oscillates between primary and secondary using plasma waves
                double blend = Math.Pow(Math.Cos(time * 1.2 + v * 3.5) * 0.5 + 0.5, 1.5);
                // Second wave adds variation
                double blend2 = Math.Pow(Math.Sin(time * 0.8 - v * 2.1) * 0.5 + 0.5, 1.5);
                double mix = blend * 0.6 + blend2 * 0.4;

                // Lerp between primary and secondary
                double r = (pR * (1 - mix) + sR * mix) * snd;
                double g = (pG * (1 - mix) + sG * mix) * snd;
                double b = (pB * (1 - mix) + sB * mix) * snd;

                // Slight brightness variation per bar for depth
                double brightPulse = 0.8 + 0.2 * Math.Sin(time * 2.0 + i * 0.7);
                r *= brightPulse;
                g *= brightPulse;
                b *= brightPulse;

                // Fade toward bottom: top bars (high i) are brightest, bottom bars (low i) fade out
                double heightFade = 0.25 + (double)i / BarCount * 0.75;
                double y = canvasHeight - (i + 1) * (barHeight + gap);

                byte cr = (byte)Math.Min(255, r * 255);
                byte cg = (byte)Math.Min(255, g * 255);
                byte cb = (byte)Math.Min(255, b * 255);
                byte ca = (byte)Math.Min(180, 255 * snd * 0.6 * heightFade);

                for (int gl = 0; gl < GlowLayers; gl++)
                {
                    if (gl == GlowLayers - 1)
                    {
                        _bars[i, gl].Fill = new SolidColorBrush(Color.FromArgb(ca, cr, cg, cb));
                        _bars[i, gl].Width = canvasWidth;
                        _bars[i, gl].Height = barHeight + gap;
                        _bars[i, gl].Opacity = 1.0;
                        Canvas.SetLeft(_bars[i, gl], 0);
                        Canvas.SetTop(_bars[i, gl], y);
                    }
                    else
                    {
                        _bars[i, gl].Opacity = 0;
                    }
                }
            }
        }

        // --- Pixel grid mode ---

        private void InjectPixelCanvas()
        {
            if (_pixelCanvas != null || _sidebarBorder == null) return;

            _originalChild = _sidebarBorder.Child as UIElement;
            if (_originalChild == null) return;

            _pixelCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            _pixels = new Rectangle[PixelRows, PixelCols];
            _barSmoothed = new double[PixelRows]; // reuse for per-row smoothing

            for (int row = 0; row < PixelRows; row++)
            {
                for (int col = 0; col < PixelCols; col++)
                {
                    _pixels[row, col] = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Opacity = 0
                    };
                    _pixelCanvas.Children.Add(_pixels[row, col]);
                }
                _barSmoothed[row] = 0;
            }

            _sidebarBorder.Child = null;
            _wrapperGrid = new Grid();
            _wrapperGrid.Children.Add(_pixelCanvas);
            _wrapperGrid.Children.Add(_originalChild);
            _sidebarBorder.Child = _wrapperGrid;

            Logger.Info("[SidebarGlow] Pixel grid canvas injected");
        }

        private void RemovePixelCanvas()
        {
            if (_pixelCanvas == null || _sidebarBorder == null || _originalChild == null) return;

            if (_wrapperGrid != null)
            {
                _wrapperGrid.Children.Clear();
                _sidebarBorder.Child = _originalChild;
            }

            _pixelCanvas = null;
            _wrapperGrid = null;
            _pixels = null;
            _originalChild = null;
            Logger.Info("[SidebarGlow] Pixel grid canvas removed");
        }

        private void RenderPixelGrid(double energy)
        {
            if (_pixelCanvas == null || _pixels == null) return;

            double canvasHeight = _sidebarBorder.ActualHeight;
            double canvasWidth = _sidebarBorder.ActualWidth;
            if (canvasHeight <= 0 || canvasWidth <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double pixelGap = 2.0;
            double cellWidth = (canvasWidth - pixelGap * (PixelCols + 1)) / PixelCols;
            double cellHeight = (canvasHeight - pixelGap * (PixelRows + 1)) / PixelRows;
            if (cellWidth < 2) cellWidth = 2;
            if (cellHeight < 2) cellHeight = 2;

            // Smooth energy per row
            for (int row = 0; row < PixelRows; row++)
            {
                double alpha = energy > _barSmoothed[row] ? 0.3 : 0.05;
                _barSmoothed[row] += (energy - _barSmoothed[row]) * alpha;
            }

            for (int row = 0; row < PixelRows; row++)
            {
                double v = (double)row / PixelRows;
                double snd = _barSmoothed[row];
                double heightFade = 1.0 - v * 0.75;
                double y = canvasHeight - (row + 1) * (cellHeight + pixelGap);

                for (int col = 0; col < PixelCols; col++)
                {
                    double u = (double)col / PixelCols;

                    // Plasma color with both u and v coordinates
                    double r = snd * Math.Pow(Math.Cos(time * 1.92 + u * 3.5 + v * 2.9) * Math.Sin(-time * 1.63 + v * 2.9) * 0.5 + 0.5, 3.0);
                    double g = snd * Math.Pow(Math.Cos(time * 1.74 - u * 2.8 - v * 2.41) * Math.Sin(time * 1.34 - v * 3.4) * 0.5 + 0.5, 3.0);
                    double b = snd * Math.Pow(Math.Cos(time * 1.21 + u * 2.1 + v * 1.5) * Math.Sin(time * 1.53 + v * 1.41) * 0.5 + 0.5, 3.0);

                    // Tint toward base color
                    double tint = 0.4;
                    r = r * (1 - tint) + (_baseColor.R / 255.0) * snd * tint;
                    g = g * (1 - tint) + (_baseColor.G / 255.0) * snd * tint;
                    b = b * (1 - tint) + (_baseColor.B / 255.0) * snd * tint;

                    byte cr = (byte)Math.Min(255, r * 255);
                    byte cg = (byte)Math.Min(255, g * 255);
                    byte cb = (byte)Math.Min(255, b * 255);
                    byte ca = (byte)Math.Min(230, 255 * snd * 0.85 * heightFade);

                    double x = pixelGap + col * (cellWidth + pixelGap);

                    _pixels[row, col].Fill = new SolidColorBrush(Color.FromArgb(ca, cr, cg, cb));
                    _pixels[row, col].Width = cellWidth;
                    _pixels[row, col].Height = cellHeight;
                    _pixels[row, col].Opacity = 1.0;
                    Canvas.SetLeft(_pixels[row, col], x);
                    Canvas.SetTop(_pixels[row, col], y);
                }
            }
        }

        // --- Shared effect canvas inject/remove for new modes ---

        private Canvas InjectEffectCanvas()
        {
            if (_effectCanvas != null || _sidebarBorder == null) return _effectCanvas;

            _effectOriginalChild = _sidebarBorder.Child as UIElement;
            if (_effectOriginalChild == null) return null;

            _effectCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            _sidebarBorder.Child = null;
            _effectWrapperGrid = new Grid();
            _effectWrapperGrid.Children.Add(_effectCanvas);
            _effectWrapperGrid.Children.Add(_effectOriginalChild);
            _sidebarBorder.Child = _effectWrapperGrid;
            return _effectCanvas;
        }

        private void RemoveEffectCanvas()
        {
            if (_effectCanvas == null || _sidebarBorder == null || _effectOriginalChild == null) return;

            if (_effectWrapperGrid != null)
            {
                _effectWrapperGrid.Children.Clear();
                _sidebarBorder.Child = _effectOriginalChild;
            }

            _effectCanvas = null;
            _effectWrapperGrid = null;
            _effectOriginalChild = null;
            _rainDrops = null;
            _waveLine = null;
            _fireCells = null;
            _fireHeat = null;
            _stars = null;
            _ripples = null;
            _auroraBars = null;
            _heartLine = null;
            _heartHistory = null;
            _waterfallCells = null;
            _waterfallData = null;
            _nebulaParticles = null;
            _helixDotsA = null;
            _helixDotsB = null;
            _matrixCells = null;
            _matrixY = null;
            _matrixSpeed = null;
            _matrixCol = null;
            _matrixLength = null;
            _matrixBrightness = null;
        }

        // --- Rain Drops mode ---

        private void InjectRainDrops()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _rainDrops = new Rectangle[RainMaxDrops];
            _rainDropY = new double[RainMaxDrops];
            _rainDropSpeed = new double[RainMaxDrops];
            _rainDropFade = new double[RainMaxDrops];
            _rainDropCol = new int[RainMaxDrops];

            for (int i = 0; i < RainMaxDrops; i++)
            {
                _rainDrops[i] = new Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = 0,
                    RadiusX = 1,
                    RadiusY = 1
                };
                canvas.Children.Add(_rainDrops[i]);
                _rainDropY[i] = -1; // inactive
            }
            Logger.Info("[SidebarGlow] Rain drops injected");
        }

        private void RenderRainDrops(double energy)
        {
            if (_effectCanvas == null || _rainDrops == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            var color = MapColor(_baseColor, energy);
            double colWidth = w / RainCols;
            double dropHeight = h / 12.0;

            double dt = _deltaTime * 60.0; // normalize to 60fps baseline

            // Spawn new drops based on energy (scale spawn rate with dt)
            double spawnChance = energy * 0.25 * dt;
            for (int i = 0; i < RainMaxDrops; i++)
            {
                if (_rainDropY[i] < 0 && _rng.NextDouble() < spawnChance)
                {
                    _rainDropY[i] = 0;
                    _rainDropSpeed[i] = 80 + energy * 200 + _rng.NextDouble() * 100; // pixels/sec
                    _rainDropFade[i] = 0.7 + energy * 0.3;
                    _rainDropCol[i] = _rng.Next(RainCols);
                    break;
                }
            }

            for (int i = 0; i < RainMaxDrops; i++)
            {
                if (_rainDropY[i] < 0)
                {
                    _rainDrops[i].Opacity = 0;
                    continue;
                }

                // Move down (speed is pixels/sec, multiply by actual deltaTime)
                _rainDropY[i] += _rainDropSpeed[i] * _deltaTime;
                _rainDropFade[i] *= Math.Pow(0.985, dt);

                // Fade as it falls
                double progress = _rainDropY[i] / h;
                double opacity = _rainDropFade[i] * (1.0 - progress * 0.7);

                if (_rainDropY[i] > h || opacity < 0.02)
                {
                    _rainDropY[i] = -1; // deactivate
                    _rainDrops[i].Opacity = 0;
                    continue;
                }

                byte a = (byte)(255 * Math.Min(0.9, opacity));
                _rainDrops[i].Fill = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                _rainDrops[i].Width = colWidth - 2;
                _rainDrops[i].Height = dropHeight;
                _rainDrops[i].Opacity = 1.0;
                Canvas.SetLeft(_rainDrops[i], _rainDropCol[i] * colWidth + 1);
                Canvas.SetTop(_rainDrops[i], _rainDropY[i]);
            }
        }

        // --- Waveform mode ---

        private void InjectWaveform()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _waveLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(_waveLine);
            _wavePhase = 0;
            Logger.Info("[SidebarGlow] Waveform injected");
        }

        private void RenderWaveform(double energy)
        {
            if (_effectCanvas == null || _waveLine == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            var color = MapColor(_baseColor, energy);
            byte a = (byte)(80 + 175 * energy);
            _waveLine.Stroke = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));

            _wavePhase += (0.08 + energy * 0.15) * _deltaTime * 60.0;
            double amplitude = w * 0.3 * energy + w * 0.05;
            double centerX = w / 2.0;

            var points = new PointCollection();
            for (int i = 0; i < WavePoints; i++)
            {
                double t = (double)i / (WavePoints - 1);
                double y = t * h;
                double x = centerX + Math.Sin(_wavePhase + t * Math.PI * 3) * amplitude;
                // Add secondary harmonic for richness
                x += Math.Sin(_wavePhase * 0.7 + t * Math.PI * 5) * amplitude * 0.3;
                x = Math.Max(0, Math.Min(w, x));
                points.Add(new Point(x, y));
            }
            _waveLine.Points = points;
        }

        // --- Fire mode ---

        private void InjectFire()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _fireCells = new Rectangle[FireRows, FireCols];
            _fireHeat = new double[FireRows, FireCols];

            for (int row = 0; row < FireRows; row++)
            {
                for (int col = 0; col < FireCols; col++)
                {
                    _fireCells[row, col] = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Opacity = 0
                    };
                    canvas.Children.Add(_fireCells[row, col]);
                    _fireHeat[row, col] = 0;
                }
            }
            Logger.Info("[SidebarGlow] Fire injected");
        }

        private void RenderFire(double energy)
        {
            if (_effectCanvas == null || _fireCells == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double cellW = w / FireCols;
            double cellH = h / FireRows;

            // Set bottom row heat from audio energy
            for (int col = 0; col < FireCols; col++)
            {
                double flicker = 0.7 + _rng.NextDouble() * 0.3;
                _fireHeat[0, col] = energy * flicker;
            }

            // Propagate heat upward with cooling and spread (dt-scaled)
            double dt = _deltaTime * 60.0;
            for (int row = FireRows - 1; row >= 1; row--)
            {
                for (int col = 0; col < FireCols; col++)
                {
                    double below = _fireHeat[row - 1, col];
                    double left = col > 0 ? _fireHeat[row - 1, col - 1] : below;
                    double right = col < FireCols - 1 ? _fireHeat[row - 1, col + 1] : below;
                    double blend = Math.Min(1.0, dt); // clamp blend factor
                    double avg = (below * 0.6 + left * 0.2 + right * 0.2);
                    double cooling = (0.06 + _rng.NextDouble() * 0.03) * dt;
                    _fireHeat[row, col] = _fireHeat[row, col] * (1 - blend) + Math.Max(0, avg - cooling) * blend;
                }
            }

            for (int row = 0; row < FireRows; row++)
            {
                double y = h - (row + 1) * cellH;
                for (int col = 0; col < FireCols; col++)
                {
                    double heat = _fireHeat[row, col];
                    // Fire palette: black → red → orange → yellow → white
                    byte fr, fg, fb;
                    if (heat < 0.25)
                    {
                        double t = heat / 0.25;
                        fr = (byte)(t * 200);
                        fg = 0;
                        fb = 0;
                    }
                    else if (heat < 0.5)
                    {
                        double t = (heat - 0.25) / 0.25;
                        fr = (byte)(200 + t * 55);
                        fg = (byte)(t * 120);
                        fb = 0;
                    }
                    else if (heat < 0.75)
                    {
                        double t = (heat - 0.5) / 0.25;
                        fr = 255;
                        fg = (byte)(120 + t * 135);
                        fb = (byte)(t * 50);
                    }
                    else
                    {
                        double t = (heat - 0.75) / 0.25;
                        fr = 255;
                        fg = 255;
                        fb = (byte)(50 + t * 205);
                    }

                    byte fa = (byte)(heat * 220);
                    double x = col * cellW;

                    _fireCells[row, col].Fill = new SolidColorBrush(Color.FromArgb(fa, fr, fg, fb));
                    _fireCells[row, col].Width = cellW;
                    _fireCells[row, col].Height = cellH;
                    _fireCells[row, col].Opacity = 1.0;
                    Canvas.SetLeft(_fireCells[row, col], x);
                    Canvas.SetTop(_fireCells[row, col], y);
                }
            }
        }

        // --- Starfield mode ---

        private void InjectStarfield()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _stars = new System.Windows.Shapes.Ellipse[MaxStars];
            _starX = new double[MaxStars];
            _starY = new double[MaxStars];
            _starSpeed = new double[MaxStars];
            _starBrightness = new double[MaxStars];
            _starSize = new double[MaxStars];

            for (int i = 0; i < MaxStars; i++)
            {
                _stars[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_stars[i]);
                _starY[i] = -1; // inactive
            }
            Logger.Info("[SidebarGlow] Starfield injected");
        }

        private void RenderStarfield(double energy)
        {
            if (_effectCanvas == null || _stars == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            var color = MapColor(_baseColor, energy);

            double dt = _deltaTime * 60.0;

            // Spawn stars from bottom based on energy
            double spawnChance = (energy * 0.2 + 0.01) * dt;
            for (int i = 0; i < MaxStars; i++)
            {
                if (_starY[i] < 0 && _rng.NextDouble() < spawnChance)
                {
                    _starX[i] = _rng.NextDouble() * w;
                    _starY[i] = h;
                    _starSpeed[i] = 30 + energy * 120 + _rng.NextDouble() * 60; // pixels/sec
                    _starBrightness[i] = 0.4 + energy * 0.6;
                    _starSize[i] = 2 + _rng.NextDouble() * 3;
                    break;
                }
            }

            for (int i = 0; i < MaxStars; i++)
            {
                if (_starY[i] < 0)
                {
                    _stars[i].Opacity = 0;
                    continue;
                }

                _starY[i] -= _starSpeed[i] * _deltaTime; // rise upward (pixels/sec)
                _starX[i] += Math.Sin(_starY[i] * 0.05) * 0.3 * dt; // gentle drift
                _starBrightness[i] *= Math.Pow(0.995, dt);

                // Twinkle
                double twinkle = 0.8 + 0.2 * Math.Sin(_starY[i] * 0.3 + _starX[i]);
                double opacity = _starBrightness[i] * twinkle;

                if (_starY[i] < -5 || opacity < 0.02)
                {
                    _starY[i] = -1;
                    _stars[i].Opacity = 0;
                    continue;
                }

                byte a = (byte)(255 * Math.Min(0.95, opacity));
                // Brighter core: shift toward white
                byte sr = (byte)Math.Min(255, color.R + 60);
                byte sg = (byte)Math.Min(255, color.G + 40);
                byte sb = (byte)Math.Min(255, color.B + 30);
                _stars[i].Fill = new SolidColorBrush(Color.FromArgb(a, sr, sg, sb));
                _stars[i].Width = _starSize[i];
                _stars[i].Height = _starSize[i];
                _stars[i].Opacity = 1.0;
                Canvas.SetLeft(_stars[i], _starX[i]);
                Canvas.SetTop(_stars[i], _starY[i]);
            }
        }

        // --- Ripple mode ---

        private void InjectRipple()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _ripples = new System.Windows.Shapes.Ellipse[MaxRipples];
            _rippleRadius = new double[MaxRipples];
            _rippleFade = new double[MaxRipples];
            _rippleCooldown = 0;

            for (int i = 0; i < MaxRipples; i++)
            {
                _ripples[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0,
                    Fill = Brushes.Transparent,
                    StrokeThickness = 2
                };
                canvas.Children.Add(_ripples[i]);
                _rippleRadius[i] = -1; // inactive
            }
            Logger.Info("[SidebarGlow] Ripple injected");
        }

        private void RenderRipple(double energy)
        {
            if (_effectCanvas == null || _ripples == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            var color = MapColor(_baseColor, energy);
            double centerX = w / 2.0;
            double centerY = h / 2.0;
            double maxRadius = Math.Max(w, h) * 0.7;

            double dt = _deltaTime * 60.0;

            // Spawn new ripple on energy spikes
            _rippleCooldown -= _deltaTime;
            if (energy > 0.3 && _rippleCooldown <= 0)
            {
                for (int i = 0; i < MaxRipples; i++)
                {
                    if (_rippleRadius[i] < 0)
                    {
                        _rippleRadius[i] = 5;
                        _rippleFade[i] = 0.6 + energy * 0.4;
                        _rippleCooldown = 0.2; // prevent spam (seconds)
                        break;
                    }
                }
            }

            for (int i = 0; i < MaxRipples; i++)
            {
                if (_rippleRadius[i] < 0)
                {
                    _ripples[i].Opacity = 0;
                    continue;
                }

                _rippleRadius[i] += (80 + energy * 120) * _deltaTime; // pixels/sec
                _rippleFade[i] *= Math.Pow(0.97, dt);

                if (_rippleRadius[i] > maxRadius || _rippleFade[i] < 0.02)
                {
                    _rippleRadius[i] = -1;
                    _ripples[i].Opacity = 0;
                    continue;
                }

                double r = _rippleRadius[i];
                byte a = (byte)(255 * _rippleFade[i]);
                _ripples[i].Stroke = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                _ripples[i].Width = r * 2;
                _ripples[i].Height = r * 2;
                _ripples[i].Opacity = 1.0;
                Canvas.SetLeft(_ripples[i], centerX - r);
                Canvas.SetTop(_ripples[i], centerY - r);
            }
        }

        // --- Aurora mode ---

        private void InjectAurora()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _auroraBars = new Rectangle[AuroraBands];
            _auroraOffset = new double[AuroraBands];

            for (int i = 0; i < AuroraBands; i++)
            {
                _auroraBars[i] = new Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_auroraBars[i]);
                _auroraOffset[i] = _rng.NextDouble() * Math.PI * 2;
            }
            Logger.Info("[SidebarGlow] Aurora injected");
        }

        private void RenderAurora(double energy)
        {
            if (_effectCanvas == null || _auroraBars == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double bandHeight = h / AuroraBands;

            for (int i = 0; i < AuroraBands; i++)
            {
                double t = (double)i / AuroraBands;
                double wave = Math.Sin(time * 0.5 + _auroraOffset[i] + t * 3.0);
                double sway = Math.Sin(time * 0.3 + i * 1.2) * 0.3;

                // Aurora green-blue-purple palette blended with base color
                double hue = 120 + t * 180 + wave * 30; // green → cyan → purple
                if (hue > 360) hue -= 360;
                double sat = 0.6 + energy * 0.3;
                double val = 0.3 + energy * 0.5 + wave * 0.15;
                val = Math.Max(0, Math.Min(1, val));

                var (cr, cg, cb) = HsvToRgb(hue, sat, val);

                // Blend with base color
                double tint = 0.3;
                byte fr = (byte)Math.Min(255, cr * (1 - tint) + _baseColor.R * tint);
                byte fg = (byte)Math.Min(255, cg * (1 - tint) + _baseColor.G * tint);
                byte fb = (byte)Math.Min(255, cb * (1 - tint) + _baseColor.B * tint);

                double opacity = (0.3 + energy * 0.5 + sway * 0.1) * (0.7 + 0.3 * (1 - t));
                opacity = Math.Max(0, Math.Min(0.85, opacity));
                byte fa = (byte)(255 * opacity);

                double y = i * bandHeight + Math.Sin(time * 0.7 + i) * bandHeight * 0.2;

                _auroraBars[i].Fill = new SolidColorBrush(Color.FromArgb(fa, fr, fg, fb));
                _auroraBars[i].Width = w;
                _auroraBars[i].Height = bandHeight * 1.3; // overlap for smooth blending
                _auroraBars[i].Opacity = 1.0;
                Canvas.SetLeft(_auroraBars[i], 0);
                Canvas.SetTop(_auroraBars[i], y);
            }
        }

        // --- Heartbeat mode ---

        private void InjectHeartbeat()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _heartLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(_heartLine);

            _heartHistory = new double[HeartHistoryLen];
            _heartIndex = 0;
            _heartAccum = 0;
            Logger.Info("[SidebarGlow] Heartbeat injected");
        }

        private void RenderHeartbeat(double energy)
        {
            if (_effectCanvas == null || _heartLine == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            var color = MapColor(_baseColor, energy);
            byte a = (byte)(100 + 155 * energy);
            _heartLine.Stroke = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));

            // Record energy at ~20 samples/sec for smooth scrolling
            _heartAccum += _deltaTime;
            if (_heartAccum >= 0.05) // 20 samples/sec
            {
                _heartAccum -= 0.05;
                _heartHistory[_heartIndex % HeartHistoryLen] = energy;
                _heartIndex++;
            }

            double centerX = w / 2.0;
            double segmentHeight = h / HeartHistoryLen;

            var points = new PointCollection();
            for (int i = 0; i < HeartHistoryLen; i++)
            {
                // Read from oldest to newest (scrolling up)
                int idx = (_heartIndex + i) % HeartHistoryLen;
                double val = _heartHistory[idx];
                double y = h - i * segmentHeight;
                double deflection = val * w * 0.4;
                // EKG spike pattern: sharp spike then return
                double spike = Math.Pow(val, 2.0);
                double x = centerX + spike * w * 0.35 * (i % 2 == 0 ? 1 : -1);
                x = Math.Max(1, Math.Min(w - 1, x));
                points.Add(new Point(x, y));
            }
            _heartLine.Points = points;
        }

        // --- Waterfall mode ---

        private void InjectWaterfall()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _waterfallCells = new Rectangle[WaterfallRows, WaterfallCols];
            _waterfallData = new double[WaterfallRows, WaterfallCols];

            for (int row = 0; row < WaterfallRows; row++)
            {
                for (int col = 0; col < WaterfallCols; col++)
                {
                    _waterfallCells[row, col] = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Opacity = 0
                    };
                    canvas.Children.Add(_waterfallCells[row, col]);
                    _waterfallData[row, col] = 0;
                }
            }
            Logger.Info("[SidebarGlow] Waterfall injected");
        }

        private void RenderWaterfall(double energy)
        {
            if (_effectCanvas == null || _waterfallCells == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double cellW = w / WaterfallCols;
            double cellH = h / WaterfallRows;
            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;

            // Scroll at ~15 rows/sec for a relaxed waterfall
            _waterfallAccum += _deltaTime;
            if (_waterfallAccum >= 0.067)
            {
                _waterfallAccum -= 0.067;

                // Scroll data down: move each row to the next
                for (int row = WaterfallRows - 1; row >= 1; row--)
                    for (int col = 0; col < WaterfallCols; col++)
                        _waterfallData[row, col] = _waterfallData[row - 1, col];

                // Top row: generate new frequency-band data from energy
                for (int col = 0; col < WaterfallCols; col++)
                {
                    double freq = (double)col / WaterfallCols;
                    double bandEnergy = energy * (0.5 + 0.5 * Math.Sin(time * 2.0 + freq * 5.0));
                    bandEnergy += _rng.NextDouble() * energy * 0.2;
                    _waterfallData[0, col] = Math.Min(1.0, bandEnergy);
                }
            }

            // Render
            for (int row = 0; row < WaterfallRows; row++)
            {
                double y = row * cellH;
                for (int col = 0; col < WaterfallCols; col++)
                {
                    double val = _waterfallData[row, col];
                    // Spectrogram coloring: blue → cyan → green → yellow → red
                    double hue = 240 - val * 240; // 240 (blue) → 0 (red)
                    if (hue < 0) hue = 0;
                    var (cr, cg, cb) = HsvToRgb(hue, 0.85, 0.4 + val * 0.6);

                    // Blend with base color
                    double tint = 0.25;
                    byte fr = (byte)Math.Min(255, cr * (1 - tint) + _baseColor.R * tint);
                    byte fg = (byte)Math.Min(255, cg * (1 - tint) + _baseColor.G * tint);
                    byte fb = (byte)Math.Min(255, cb * (1 - tint) + _baseColor.B * tint);
                    byte fa = (byte)(val * 200);

                    double x = col * cellW;
                    _waterfallCells[row, col].Fill = new SolidColorBrush(Color.FromArgb(fa, fr, fg, fb));
                    _waterfallCells[row, col].Width = cellW;
                    _waterfallCells[row, col].Height = cellH;
                    _waterfallCells[row, col].Opacity = 1.0;
                    Canvas.SetLeft(_waterfallCells[row, col], x);
                    Canvas.SetTop(_waterfallCells[row, col], y);
                }
            }
        }

        // --- Nebula mode (simple Lissajous point cloud) ---

        private void InjectNebula()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _nebulaParticles = new System.Windows.Shapes.Ellipse[NebulaParticles];
            for (int i = 0; i < NebulaParticles; i++)
            {
                _nebulaParticles[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_nebulaParticles[i]);
            }
            Logger.Info("[SidebarGlow] Nebula injected");
        }

        private static double NebulaSin(double t, double i, double offset)
        {
            return (Math.Sin(t + i * 0.9553 + offset) +
                    Math.Sin(t * 1.311 + i * 1.0 + offset) +
                    Math.Sin(t * 1.4 + i * 1.53 + offset) +
                    Math.Sin(t * 1.84 + i * 0.76 + offset)) * 0.2;
        }

        private void RenderNebula(double energy)
        {
            if (_effectCanvas == null || _nebulaParticles == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds * 0.3;
            var color = MapColor(_baseColor, energy);
            double centerX = w / 2.0;
            double centerY = h / 2.0;

            for (int i = 0; i < NebulaParticles; i++)
            {
                double id = i + Math.Sin(i) * 100.0;

                double px = NebulaSin(time, id, 0);
                double py = NebulaSin(time, id, 2.1);
                double pz = NebulaSin(time, id, -2.1);

                double screenX = centerX + px * w * (0.8 + energy * 0.5);
                double screenY = centerY + py * h * (0.35 + energy * 0.2);
                double depth = 0.5 + pz * 0.4;

                double size = (2 + energy * 4) * depth;
                size = Math.Max(1.5, Math.Min(8, size));

                double cr = Math.Abs(px) * 2;
                double cg = Math.Abs(py) * 2;
                double cb = Math.Abs(pz) * 2;
                double tint = 0.5;
                byte fr = (byte)Math.Min(255, (cr * (1 - tint) + color.R / 255.0 * tint) * 255);
                byte fg = (byte)Math.Min(255, (cg * (1 - tint) + color.G / 255.0 * tint) * 255);
                byte fb = (byte)Math.Min(255, (cb * (1 - tint) + color.B / 255.0 * tint) * 255);

                double opacity = (0.3 + energy * 0.6) * depth;
                byte fa = (byte)(255 * Math.Min(0.9, opacity));

                screenX = Math.Max(0, Math.Min(w - size, screenX - size / 2));
                screenY = Math.Max(0, Math.Min(h - size, screenY - size / 2));

                _nebulaParticles[i].Fill = new SolidColorBrush(Color.FromArgb(fa, fr, fg, fb));
                _nebulaParticles[i].Width = size;
                _nebulaParticles[i].Height = size;
                _nebulaParticles[i].Opacity = 1.0;
                Canvas.SetLeft(_nebulaParticles[i], screenX);
                Canvas.SetTop(_nebulaParticles[i], screenY);
            }
        }

        // --- DNA Helix mode ---
        // Two interleaving sine helices spiraling vertically

        private void InjectDnaHelix()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _helixDotsA = new System.Windows.Shapes.Ellipse[HelixDots];
            _helixDotsB = new System.Windows.Shapes.Ellipse[HelixDots];

            for (int i = 0; i < HelixDots; i++)
            {
                _helixDotsA[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                _helixDotsB[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_helixDotsA[i]);
                canvas.Children.Add(_helixDotsB[i]);
            }
            Logger.Info("[SidebarGlow] DNA Helix injected");
        }

        private void RenderDnaHelix(double energy)
        {
            if (_effectCanvas == null || _helixDotsA == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            var color = MapColor(_baseColor, energy);
            double centerX = w / 2.0;
            double amplitude = w * 0.3 + energy * w * 0.15;

            for (int i = 0; i < HelixDots; i++)
            {
                double t = (double)i / HelixDots;
                double y = t * h;
                double phase = time * 1.5 + t * Math.PI * 4; // 2 full rotations along sidebar

                // Strand A
                double xA = centerX + Math.Sin(phase) * amplitude;
                // Strand B (180° offset)
                double xB = centerX + Math.Sin(phase + Math.PI) * amplitude;

                // Depth from cos (front/back of helix)
                double depthA = Math.Cos(phase) * 0.5 + 0.5; // 0 = back, 1 = front
                double depthB = Math.Cos(phase + Math.PI) * 0.5 + 0.5;

                double sizeA = 3 + depthA * 3 + energy * 2;
                double sizeB = 3 + depthB * 3 + energy * 2;

                double opA = (0.3 + energy * 0.5) * (0.4 + depthA * 0.6);
                double opB = (0.3 + energy * 0.5) * (0.4 + depthB * 0.6);

                // Color: strand A uses base color, strand B uses complementary
                byte aA = (byte)(255 * Math.Min(0.9, opA));
                byte aB = (byte)(255 * Math.Min(0.9, opB));

                // Complementary hue for strand B
                var (hue, sat, val) = RgbToHsv(color.R, color.G, color.B);
                double hue2 = (hue + 180) % 360;
                var (r2, g2, b2) = HsvToRgb(hue2, sat, val);

                xA = Math.Max(0, Math.Min(w - sizeA, xA - sizeA / 2));
                xB = Math.Max(0, Math.Min(w - sizeB, xB - sizeB / 2));

                _helixDotsA[i].Fill = new SolidColorBrush(Color.FromArgb(aA, color.R, color.G, color.B));
                _helixDotsA[i].Width = sizeA;
                _helixDotsA[i].Height = sizeA;
                _helixDotsA[i].Opacity = 1.0;
                Canvas.SetLeft(_helixDotsA[i], xA);
                Canvas.SetTop(_helixDotsA[i], y - sizeA / 2);

                _helixDotsB[i].Fill = new SolidColorBrush(Color.FromArgb(aB, r2, g2, b2));
                _helixDotsB[i].Width = sizeB;
                _helixDotsB[i].Height = sizeB;
                _helixDotsB[i].Opacity = 1.0;
                Canvas.SetLeft(_helixDotsB[i], xB);
                Canvas.SetTop(_helixDotsB[i], y - sizeB / 2);
            }
        }

        // --- DNA Helix Bloom mode ---
        // Same helix but with soft glow halos behind each dot for bloom effect

        private void InjectDnaHelixBloom()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            // Glow halos first (behind dots)
            _helixGlowA = new System.Windows.Shapes.Ellipse[HelixDots];
            _helixGlowB = new System.Windows.Shapes.Ellipse[HelixDots];
            _helixDotsA = new System.Windows.Shapes.Ellipse[HelixDots];
            _helixDotsB = new System.Windows.Shapes.Ellipse[HelixDots];

            for (int i = 0; i < HelixDots; i++)
            {
                _helixGlowA[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                _helixGlowB[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_helixGlowA[i]);
                canvas.Children.Add(_helixGlowB[i]);

                _helixDotsA[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                _helixDotsB[i] = new System.Windows.Shapes.Ellipse
                {
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                canvas.Children.Add(_helixDotsA[i]);
                canvas.Children.Add(_helixDotsB[i]);
            }
            Logger.Info("[SidebarGlow] DNA Helix Bloom injected");
        }

        private void RenderDnaHelixBloom(double energy)
        {
            if (_effectCanvas == null || _helixDotsA == null || _helixGlowA == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            var color = MapColor(_baseColor, energy);
            double centerX = w / 2.0;
            double amplitude = w * 0.3 + energy * w * 0.15;

            // Complementary hue for strand B
            var (hue, sat, val) = RgbToHsv(color.R, color.G, color.B);
            double hue2 = (hue + 180) % 360;
            var (r2, g2, b2) = HsvToRgb(hue2, sat, val);

            for (int i = 0; i < HelixDots; i++)
            {
                double t = (double)i / HelixDots;
                double y = t * h;
                double phase = time * 1.5 + t * Math.PI * 4;

                double xA = centerX + Math.Sin(phase) * amplitude;
                double xB = centerX + Math.Sin(phase + Math.PI) * amplitude;

                double depthA = Math.Cos(phase) * 0.5 + 0.5;
                double depthB = Math.Cos(phase + Math.PI) * 0.5 + 0.5;

                double sizeA = 3 + depthA * 3 + energy * 2;
                double sizeB = 3 + depthB * 3 + energy * 2;

                // Bloom halo: 3x the dot size, softer opacity
                double glowSizeA = sizeA * 3.0;
                double glowSizeB = sizeB * 3.0;
                double glowOpA = (0.15 + energy * 0.25) * (0.3 + depthA * 0.7);
                double glowOpB = (0.15 + energy * 0.25) * (0.3 + depthB * 0.7);

                double opA = (0.4 + energy * 0.5) * (0.4 + depthA * 0.6);
                double opB = (0.4 + energy * 0.5) * (0.4 + depthB * 0.6);

                byte aA = (byte)(255 * Math.Min(0.95, opA));
                byte aB = (byte)(255 * Math.Min(0.95, opB));
                byte gAA = (byte)(255 * Math.Min(0.6, glowOpA));
                byte gAB = (byte)(255 * Math.Min(0.6, glowOpB));

                double clampedXA = Math.Max(0, Math.Min(w - sizeA, xA - sizeA / 2));
                double clampedXB = Math.Max(0, Math.Min(w - sizeB, xB - sizeB / 2));
                double glowXA = Math.Max(-glowSizeA / 2, Math.Min(w - glowSizeA / 2, xA - glowSizeA / 2));
                double glowXB = Math.Max(-glowSizeB / 2, Math.Min(w - glowSizeB / 2, xB - glowSizeB / 2));

                // Glow halos (soft, large, behind dots)
                _helixGlowA[i].Fill = new RadialGradientBrush(
                    Color.FromArgb(gAA, color.R, color.G, color.B),
                    Color.FromArgb(0, color.R, color.G, color.B));
                _helixGlowA[i].Width = glowSizeA;
                _helixGlowA[i].Height = glowSizeA;
                _helixGlowA[i].Opacity = 1.0;
                Canvas.SetLeft(_helixGlowA[i], glowXA);
                Canvas.SetTop(_helixGlowA[i], y - glowSizeA / 2);

                _helixGlowB[i].Fill = new RadialGradientBrush(
                    Color.FromArgb(gAB, r2, g2, b2),
                    Color.FromArgb(0, r2, g2, b2));
                _helixGlowB[i].Width = glowSizeB;
                _helixGlowB[i].Height = glowSizeB;
                _helixGlowB[i].Opacity = 1.0;
                Canvas.SetLeft(_helixGlowB[i], glowXB);
                Canvas.SetTop(_helixGlowB[i], y - glowSizeB / 2);

                // Core dots (bright, sharp, on top)
                _helixDotsA[i].Fill = new SolidColorBrush(Color.FromArgb(aA, color.R, color.G, color.B));
                _helixDotsA[i].Width = sizeA;
                _helixDotsA[i].Height = sizeA;
                _helixDotsA[i].Opacity = 1.0;
                Canvas.SetLeft(_helixDotsA[i], clampedXA);
                Canvas.SetTop(_helixDotsA[i], y - sizeA / 2);

                _helixDotsB[i].Fill = new SolidColorBrush(Color.FromArgb(aB, r2, g2, b2));
                _helixDotsB[i].Width = sizeB;
                _helixDotsB[i].Height = sizeB;
                _helixDotsB[i].Opacity = 1.0;
                Canvas.SetLeft(_helixDotsB[i], clampedXB);
                Canvas.SetTop(_helixDotsB[i], y - sizeB / 2);
            }
        }

        // --- Matrix mode ---
        // Classic green character rain: thin vertical streaks falling at varied speeds

        private void InjectMatrix()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _matrixCells = new Rectangle[MatrixMaxStreaks];
            _matrixY = new double[MatrixMaxStreaks];
            _matrixSpeed = new double[MatrixMaxStreaks];
            _matrixCol = new int[MatrixMaxStreaks];
            _matrixLength = new double[MatrixMaxStreaks];
            _matrixBrightness = new double[MatrixMaxStreaks];

            for (int i = 0; i < MatrixMaxStreaks; i++)
            {
                _matrixCells[i] = new Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = 0,
                    RadiusX = 1,
                    RadiusY = 1
                };
                canvas.Children.Add(_matrixCells[i]);
                _matrixY[i] = -1; // inactive
            }
            Logger.Info("[SidebarGlow] Matrix injected");
        }

        private void RenderMatrix(double energy)
        {
            if (_effectCanvas == null || _matrixCells == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double dt = _deltaTime * 60.0;
            double colWidth = w / MatrixCols;
            double streakWidth = Math.Max(2, colWidth * 0.3); // thin streaks

            // Spawn new streaks
            double spawnChance = (energy * 0.3 + 0.05) * dt;
            for (int i = 0; i < MatrixMaxStreaks; i++)
            {
                if (_matrixY[i] < 0 && _rng.NextDouble() < spawnChance)
                {
                    _matrixCol[i] = _rng.Next(MatrixCols);
                    _matrixY[i] = -_rng.NextDouble() * h * 0.3; // start above viewport
                    _matrixSpeed[i] = 60 + energy * 180 + _rng.NextDouble() * 80; // pixels/sec
                    _matrixLength[i] = 30 + _rng.NextDouble() * h * 0.3; // streak length
                    _matrixBrightness[i] = 0.5 + energy * 0.5;
                    break;
                }
            }

            for (int i = 0; i < MatrixMaxStreaks; i++)
            {
                if (_matrixY[i] < -h)
                {
                    _matrixCells[i].Opacity = 0;
                    continue;
                }

                if (_matrixY[i] < 0 - _matrixLength[i] - 10)
                {
                    _matrixY[i] = -1 - h; // mark inactive
                    _matrixCells[i].Opacity = 0;
                    continue;
                }

                _matrixY[i] += _matrixSpeed[i] * _deltaTime;
                _matrixBrightness[i] *= Math.Pow(0.998, dt);

                if (_matrixY[i] > h + _matrixLength[i])
                {
                    _matrixY[i] = -1 - h; // deactivate
                    _matrixCells[i].Opacity = 0;
                    continue;
                }

                // Matrix green with brightness variation
                // Blend toward base color slightly
                double tint = 0.25;
                byte mr = (byte)Math.Min(255, 0 * (1 - tint) + _baseColor.R * tint);
                byte mg = (byte)Math.Min(255, 255 * _matrixBrightness[i] * (1 - tint) + _baseColor.G * tint);
                byte mb = (byte)Math.Min(255, 0 * (1 - tint) + _baseColor.B * tint * 0.3);
                byte ma = (byte)(200 * _matrixBrightness[i]);

                double x = _matrixCol[i] * colWidth + (colWidth - streakWidth) / 2;

                _matrixCells[i].Fill = new SolidColorBrush(Color.FromArgb(ma, mr, mg, mb));
                _matrixCells[i].Width = streakWidth;
                _matrixCells[i].Height = _matrixLength[i];
                _matrixCells[i].Opacity = 1.0;
                Canvas.SetLeft(_matrixCells[i], x);
                Canvas.SetTop(_matrixCells[i], _matrixY[i]);
            }
        }

        // --- Laser mode ---
        // Converging rainbow beams that sweep angles, inspired by ShaderToy WdscR4.
        // Each beam is a line from the canvas edge toward a focal point near center.

        private void InjectLaser()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _laserGlowLines = new System.Windows.Shapes.Line[LaserBeams];
            _laserLines = new System.Windows.Shapes.Line[LaserBeams];

            for (int i = 0; i < LaserBeams; i++)
            {
                // Glow line (wider, softer, behind)
                _laserGlowLines[i] = new System.Windows.Shapes.Line
                {
                    IsHitTestVisible = false,
                    StrokeThickness = 4,
                    Opacity = 0
                };
                canvas.Children.Add(_laserGlowLines[i]);

                // Core line (thin, bright, on top)
                _laserLines[i] = new System.Windows.Shapes.Line
                {
                    IsHitTestVisible = false,
                    StrokeThickness = 1.5,
                    Opacity = 0
                };
                canvas.Children.Add(_laserLines[i]);
            }
            Logger.Info("[SidebarGlow] Laser injected");
        }

        private void RenderLaser(double energy)
        {
            if (_effectCanvas == null || _laserLines == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double centerX = w * 0.5;
            double centerY = h * 0.5;

            // Focal point wobbles slightly with audio
            double focalX = centerX + Math.Sin(time * 0.7) * w * 0.05 * (1 + energy);
            double focalY = centerY + Math.Cos(time * 0.9) * h * 0.03 * (1 + energy);

            var (baseHue, baseSat, _) = RgbToHsv(_baseColor.R, _baseColor.G, _baseColor.B);

            for (int i = 0; i < LaserBeams; i++)
            {
                // Angle sweeps over time, each beam offset by golden ratio for even spread
                double r = 1.5 - Math.Cos(time + i * 0.5 * Math.PI) * 0.1;
                double angle = time * 0.2 + (i + 1) * 0.1 * Math.PI;

                // Direction from shader: tan/sin create varied sweep patterns
                double dirX = Math.Tan(angle) * r;
                double dirY = Math.Sin(angle) * r;

                // Normalize direction and extend to edge
                double len = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (len < 0.001) len = 0.001;
                dirX /= len;
                dirY /= len;

                // Extend beam from focal point outward to beyond canvas edge
                double reach = Math.Max(w, h) * (0.8 + energy * 0.4);
                double startX = focalX;
                double startY = focalY;
                double endX = focalX + dirX * reach;
                double endY = focalY + dirY * reach;

                // Rainbow color cycling per beam (from shader)
                double t2 = time * 0.5 + i * 0.1 * Math.PI * 2;
                double hue = (baseHue + (Math.Sin(t2) * 0.5 + 0.5) * 120) % 360;
                double sat = Math.Min(1.0, baseSat * 0.5 + 0.5);
                double val = 0.6 + energy * 0.4;
                var (cr, cg, cb) = HsvToRgb(hue, sat, val);

                double beamOpacity = (0.2 + energy * 0.6) * (0.5 + 0.5 * Math.Sin(time + i * 1.3));
                beamOpacity = Math.Max(0.05, Math.Min(0.9, beamOpacity));

                byte alpha = (byte)(255 * beamOpacity);
                byte glowAlpha = (byte)(255 * beamOpacity * 0.35);

                var coreColor = Color.FromArgb(alpha, cr, cg, cb);
                var glowColor = Color.FromArgb(glowAlpha, cr, cg, cb);

                // Glow line
                _laserGlowLines[i].X1 = startX;
                _laserGlowLines[i].Y1 = startY;
                _laserGlowLines[i].X2 = endX;
                _laserGlowLines[i].Y2 = endY;
                _laserGlowLines[i].Stroke = new SolidColorBrush(glowColor);
                _laserGlowLines[i].StrokeThickness = 4 + energy * 3;
                _laserGlowLines[i].Opacity = 1.0;

                // Core line
                _laserLines[i].X1 = startX;
                _laserLines[i].Y1 = startY;
                _laserLines[i].X2 = endX;
                _laserLines[i].Y2 = endY;
                _laserLines[i].Stroke = new SolidColorBrush(coreColor);
                _laserLines[i].StrokeThickness = 1.5 + energy * 1.0;
                _laserLines[i].Opacity = 1.0;
            }
        }

        // --- Pulse Waves mode ---
        // Guyver-style layered glowing sine waves. Each wave has its own color, frequency,
        // and speed. The inverse-distance glow from the shader is approximated with
        // a thick semi-transparent glow polyline behind each thin core polyline.

        // Wave parameters: (frequency multiplier, time speed, y-offset fraction 0..1)
        private static readonly double[][] PulseWaveParams = new double[][]
        {
            new[] { 3.0, 6.0, 0.35 },   // fast, mid-upper
            new[] { 3.0, 10.0, 0.50 },   // faster, center
            new[] { 5.0, 14.0, 0.65 },   // fastest, mid-lower
            new[] { 4.0, 8.0, 0.45 },    // medium, slightly above center
        };

        private void InjectPulseWaves()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _pulseGlowLines = new System.Windows.Shapes.Polyline[PulseWaveCount];
            _pulseLines = new System.Windows.Shapes.Polyline[PulseWaveCount];

            for (int i = 0; i < PulseWaveCount; i++)
            {
                _pulseGlowLines[i] = new System.Windows.Shapes.Polyline
                {
                    IsHitTestVisible = false,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = 0
                };
                canvas.Children.Add(_pulseGlowLines[i]);

                _pulseLines[i] = new System.Windows.Shapes.Polyline
                {
                    IsHitTestVisible = false,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = 0
                };
                canvas.Children.Add(_pulseLines[i]);
            }
            Logger.Info("[SidebarGlow] Pulse Waves injected");
        }

        private void RenderPulseWaves(double energy)
        {
            if (_effectCanvas == null || _pulseLines == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            var (baseHue, baseSat, _) = RgbToHsv(_baseColor.R, _baseColor.G, _baseColor.B);

            // Four wave colors inspired by the shader's SUN_1..SUN_4, shifted by base hue
            double[] waveHues = new[]
            {
                (baseHue + 0) % 360,     // base color
                (baseHue + 120) % 360,   // triadic 1
                (baseHue + 60) % 360,    // analogous
                (baseHue + 240) % 360,   // triadic 2
            };

            for (int wi = 0; wi < PulseWaveCount; wi++)
            {
                double freq = PulseWaveParams[wi][0];
                double speed = PulseWaveParams[wi][1];
                double yBase = PulseWaveParams[wi][2] * h;

                // Wave amplitude scales with energy
                double amp = h * (0.02 + energy * 0.06) / (wi + 1.5);

                var corePoints = new PointCollection(PulseWavePoints);
                var glowPoints = new PointCollection(PulseWavePoints);

                for (int p = 0; p < PulseWavePoints; p++)
                {
                    double t = (double)p / (PulseWavePoints - 1);
                    double x = t * w;

                    // Layered sines like the shader: multiple frequency components
                    double wave = Math.Sin(x / w * freq * Math.PI * 2 + time * speed);
                    // Add harmonics for richness
                    wave += Math.Sin(x / w * freq * 2 * Math.PI * 2 + time * speed * 0.7) * 0.3;

                    double y = yBase + wave * amp;
                    corePoints.Add(new Point(x, y));
                    glowPoints.Add(new Point(x, y));
                }

                // Color
                double sat = Math.Min(1.0, baseSat * 0.6 + 0.4);
                double val = 0.6 + energy * 0.4;
                var (cr, cg, cb) = HsvToRgb(waveHues[wi], sat, val);

                double opacity = 0.3 + energy * 0.5;
                byte alpha = (byte)(255 * Math.Min(0.9, opacity));
                byte glowAlpha = (byte)(255 * Math.Min(0.4, opacity * 0.4));

                // Glow line (wide, soft)
                _pulseGlowLines[wi].Points = glowPoints;
                _pulseGlowLines[wi].Stroke = new SolidColorBrush(Color.FromArgb(glowAlpha, cr, cg, cb));
                _pulseGlowLines[wi].StrokeThickness = 6 + energy * 5;
                _pulseGlowLines[wi].Opacity = 1.0;

                // Core line (thin, bright)
                _pulseLines[wi].Points = corePoints;
                _pulseLines[wi].Stroke = new SolidColorBrush(Color.FromArgb(alpha, cr, cg, cb));
                _pulseLines[wi].StrokeThickness = 1.5 + energy * 1.0;
                _pulseLines[wi].Opacity = 1.0;
            }
        }

        // --- Voronoi mode ---
        // A glowing vertical line that wobbles horizontally, distorted by Voronoi noise.
        // From the shader: t = 1.0/abs(((uv.x + sin(uv.y + k)) + offset) * 200.0)
        // The 1/abs() creates an inverse-distance glow around a bending vertical line.
        // Voronoi noise displaces the line position at each point for organic jitter.

        private void InjectVoronoi()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _voronoiGlowLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0
            };
            canvas.Children.Add(_voronoiGlowLine);

            _voronoiLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0
            };
            canvas.Children.Add(_voronoiLine);

            Logger.Info("[SidebarGlow] Voronoi injected");
        }

        // Simple Voronoi-like noise: hash a grid cell, return distance to nearest feature point
        private static double VoronoiNoise(double x, double y)
        {
            double gx = Math.Floor(x);
            double gy = Math.Floor(y);
            double fx = x - gx;
            double fy = y - gy;
            double minDist = 1.0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    double lx = dx;
                    double ly = dy;
                    // Hash: fract(sin(dot(lattice, vec2(13.85, 47.77))) * 46738.29)
                    double hx = (gx + dx) * 13.85 + (gy + dy) * 47.77;
                    double hy = (gx + dx) * 99.41 + (gy + dy) * 88.48;
                    double px = lx + Frac(Math.Sin(hx) * 46738.29) - fx;
                    double py = ly + Frac(Math.Sin(hy) * 46738.29) - fy;
                    double dist = Math.Sqrt(px * px + py * py);
                    if (dist < minDist) minDist = dist;
                }
            }
            return minDist;
        }

        private static double Frac(double v) { return v - Math.Floor(v); }

        private void RenderVoronoi(double energy)
        {
            if (_effectCanvas == null || _voronoiLine == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double k = 0.3 * time;
            var color = MapColor(_baseColor, energy);

            var points = new PointCollection(VoronoiLinePoints);
            var glowPts = new PointCollection(VoronoiLinePoints);

            for (int i = 0; i < VoronoiLinePoints; i++)
            {
                double t = (double)i / (VoronoiLinePoints - 1);
                double y = t * h;

                // uv.y mapped to -1..1
                double uvY = t * 2.0 - 1.0;
                // uv.x is the center of sidebar, normalized
                double uvX = 0.0;

                // Voronoi offset from shader: voronoi(uv * 10 + vec2(k))
                double vOffset = VoronoiNoise(uvY * 10.0 + k, uvX * 10.0 + k * 0.7);

                // Line position from shader: uv.x + sin(uv.y + k) + offset
                // We use this to compute horizontal displacement
                double lineX = Math.Sin(uvY * 3.0 + k) * 0.3 + vOffset * 0.4;

                // Audio energy amplifies the wobble
                double x = w * 0.5 + lineX * w * (0.5 + energy * 0.3);

                x = Math.Max(0, Math.Min(w, x));
                points.Add(new Point(x, y));
                glowPts.Add(new Point(x, y));
            }

            // Vertical color gradient from shader: vec3(10.0 * uv.y, 2.0, 1.0 * r)
            // We use the base color with energy-based brightness
            double opacity = 0.4 + energy * 0.5;
            byte alpha = (byte)(255 * Math.Min(0.9, opacity));
            byte glowAlpha = (byte)(255 * Math.Min(0.35, opacity * 0.35));

            _voronoiGlowLine.Points = glowPts;
            _voronoiGlowLine.Stroke = new SolidColorBrush(Color.FromArgb(glowAlpha, color.R, color.G, color.B));
            _voronoiGlowLine.StrokeThickness = 8 + energy * 8;
            _voronoiGlowLine.Opacity = 1.0;

            _voronoiLine.Points = points;
            _voronoiLine.Stroke = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            _voronoiLine.StrokeThickness = 2 + energy * 1.5;
            _voronoiLine.Opacity = 1.0;
        }

        // --- Equalizer Grid mode ---
        // Tiled grid of horizontal bars extending from the left edge. Each bar's width
        // is driven by layered sine waves — sin(time + sin(col + time + rowMod) + TWO_PI * i / n).
        // Creates a rippling wave pattern across the grid. Color is per-column with
        // red/blue channels driven by column index, matching the shader's color scheme.

        private void InjectEqualizerGrid()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _eqGridCells = new Rectangle[EqGridRows, EqGridCols];

            for (int r = 0; r < EqGridRows; r++)
            {
                for (int c = 0; c < EqGridCols; c++)
                {
                    _eqGridCells[r, c] = new Rectangle
                    {
                        IsHitTestVisible = false,
                        Opacity = 0
                    };
                    canvas.Children.Add(_eqGridCells[r, c]);
                }
            }
            Logger.Info("[SidebarGlow] Equalizer Grid injected");
        }

        private void RenderEqualizerGrid(double energy)
        {
            if (_effectCanvas == null || _eqGridCells == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double cellH = h / EqGridRows;
            double twoPi = Math.PI * 2;
            var (baseHue, baseSat, _) = RgbToHsv(_baseColor.R, _baseColor.G, _baseColor.B);

            for (int c = 0; c < EqGridCols; c++)
            {
                // Row modulation from shader: m = sin(row)
                double colTime = time * 1.1;

                for (int r = 0; r < EqGridRows; r++)
                {
                    double row = (double)r / EqGridRows * h;
                    double m = Math.Sin(row / h * 4.0);

                    // Shader formula: offset = (sin(time + sin(col + time + m) + TWO_PI * i / n) + 1) / 2
                    double innerSin = Math.Sin(c + colTime + m);
                    double phase = twoPi * r / EqGridRows;
                    double offset = (Math.Sin(colTime + innerSin + phase) + 1.0) / 2.0;

                    // Bar extends from left edge, width driven by offset + energy
                    double barWidth = w * offset * (0.4 + energy * 0.6);
                    double barHeight = cellH * 0.65;

                    // Color from shader: r = rect/(col+1), b = rect/(zoom/col), g = rect*sin(row+time)
                    // Map to hue: per-column hue shift, brightness from offset
                    double hue = (baseHue + c * 90.0 + time * 10) % 360;
                    double sat = Math.Min(1.0, baseSat * 0.4 + 0.5);
                    double val = offset * (0.4 + energy * 0.5);
                    var (cr, cg, cb) = HsvToRgb(hue, sat, val);

                    double opacity = offset * (0.4 + energy * 0.5);
                    byte alpha = (byte)(255 * Math.Min(0.85, opacity));

                    double y = r * cellH + (cellH - barHeight) / 2;

                    _eqGridCells[r, c].Fill = new SolidColorBrush(Color.FromArgb(alpha, cr, cg, cb));
                    _eqGridCells[r, c].Width = Math.Max(1, barWidth);
                    _eqGridCells[r, c].Height = barHeight;
                    _eqGridCells[r, c].Opacity = 1.0;
                    Canvas.SetLeft(_eqGridCells[r, c], 0); // extend from left edge
                    Canvas.SetTop(_eqGridCells[r, c], y);
                }
            }
        }

        // --- Snow mode ---
        // Multi-layer parallax snowfall. Each layer has different flake sizes
        // and fall speeds (closer = bigger/faster). Horizontal sine drift matches
        // the shader's uv.x += sin(uv.y + time * 0.5) / scale.

        private void InjectSnow()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _snowFlakes = new System.Windows.Shapes.Ellipse[SnowLayers, SnowPerLayer];
            _snowX = new double[SnowLayers, SnowPerLayer];
            _snowY = new double[SnowLayers, SnowPerLayer];
            _snowDrift = new double[SnowLayers, SnowPerLayer];

            for (int layer = 0; layer < SnowLayers; layer++)
            {
                for (int i = 0; i < SnowPerLayer; i++)
                {
                    _snowFlakes[layer, i] = new System.Windows.Shapes.Ellipse
                    {
                        IsHitTestVisible = false,
                        Opacity = 0
                    };
                    canvas.Children.Add(_snowFlakes[layer, i]);

                    // Randomize initial positions
                    _snowX[layer, i] = _rng.NextDouble();
                    _snowY[layer, i] = _rng.NextDouble() * 1.2 - 0.2; // start some above viewport
                    _snowDrift[layer, i] = _rng.NextDouble() * Math.PI * 2; // drift phase offset
                }
            }
            Logger.Info("[SidebarGlow] Snow injected");
        }

        private void RenderSnow(double energy)
        {
            if (_effectCanvas == null || _snowFlakes == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double dt60 = _deltaTime * 60.0;
            var color = MapColor(_baseColor, energy);

            for (int layer = 0; layer < SnowLayers; layer++)
            {
                // Closer layers (higher index) = bigger, faster, more opaque
                double depthFactor = (double)(layer + 1) / SnowLayers;
                double fallSpeed = 0.003 + depthFactor * 0.008; // per-frame normalized fall speed
                double flakeSize = 1.5 + depthFactor * 3.0 + energy * 1.5;
                double baseOpacity = 0.15 + depthFactor * 0.35 + energy * 0.2;
                // Shader drift: sin(uv.y + time * 0.5) / scale
                double driftAmplitude = 0.08 / (depthFactor + 0.3);

                for (int i = 0; i < SnowPerLayer; i++)
                {
                    // Fall downward
                    _snowY[layer, i] += fallSpeed * dt60 * (0.7 + energy * 0.6);

                    // Horizontal sine drift from shader
                    double drift = Math.Sin(_snowY[layer, i] * 4.0 + time * 0.5 + _snowDrift[layer, i]) * driftAmplitude;
                    double drawX = (_snowX[layer, i] + drift) * w;

                    // Wrap when fallen below
                    if (_snowY[layer, i] > 1.05)
                    {
                        _snowY[layer, i] = -0.05;
                        _snowX[layer, i] = _rng.NextDouble();
                        _snowDrift[layer, i] = _rng.NextDouble() * Math.PI * 2;
                    }

                    double drawY = _snowY[layer, i] * h;

                    // Color: shader uses (c*0.9, c*0.3, c) — pinkish-white tinted by base color
                    byte a = (byte)(255 * Math.Min(0.9, baseOpacity));
                    // Blend white with base color for tinted snow
                    byte sr = (byte)Math.Min(255, 200 + color.R * 0.2);
                    byte sg = (byte)Math.Min(255, 200 + color.G * 0.2);
                    byte sb = (byte)Math.Min(255, 200 + color.B * 0.2);

                    _snowFlakes[layer, i].Fill = new SolidColorBrush(Color.FromArgb(a, sr, sg, sb));
                    _snowFlakes[layer, i].Width = flakeSize;
                    _snowFlakes[layer, i].Height = flakeSize;
                    _snowFlakes[layer, i].Opacity = 1.0;
                    Canvas.SetLeft(_snowFlakes[layer, i], Math.Max(0, Math.Min(w - flakeSize, drawX - flakeSize / 2)));
                    Canvas.SetTop(_snowFlakes[layer, i], drawY - flakeSize / 2);
                }
            }
        }

        // --- Neon Line mode ---
        // A smooth glowing vertical beam that snakes horizontally.
        // From shader: uPos.x += sin(uPos.y + t) * 0.3; fTemp = abs(1.0 / uPos.x / 100.0)
        // The 1/abs() creates the characteristic neon glow around a wobbling line.

        private void InjectNeonLine()
        {
            var canvas = InjectEffectCanvas();
            if (canvas == null) return;

            _neonGlowLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0
            };
            canvas.Children.Add(_neonGlowLine);

            _neonLine = new System.Windows.Shapes.Polyline
            {
                IsHitTestVisible = false,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0
            };
            canvas.Children.Add(_neonLine);

            Logger.Info("[SidebarGlow] Neon Line injected");
        }

        private void RenderNeonLine(double energy)
        {
            if (_effectCanvas == null || _neonLine == null) return;

            double h = _sidebarBorder.ActualHeight;
            double w = _sidebarBorder.ActualWidth;
            if (h <= 0 || w <= 0) return;

            double time = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            double t = time * 0.9; // shader: time * 0.9
            var color = MapColor(_baseColor, energy);

            var points = new PointCollection(NeonLinePoints);
            var glowPts = new PointCollection(NeonLinePoints);

            for (int i = 0; i < NeonLinePoints; i++)
            {
                double frac = (double)i / (NeonLinePoints - 1);
                double y = frac * h;

                // Shader: uPos.x += sin(uPos.y + t) * 0.3
                // uPos.y is normalized 0..1, shifted by -0.3
                double uvY = frac - 0.3;
                double displacement = Math.Sin(uvY * 6.0 + t) * 0.3;

                // Map displacement to pixel x position (center of sidebar + offset)
                double x = w * (0.5 + displacement * (0.6 + energy * 0.3));
                x = Math.Max(0, Math.Min(w, x));

                points.Add(new Point(x, y));
                glowPts.Add(new Point(x, y));
            }

            // Shader color: (vertColor, vertColor, vertColor * 2.5) — bluish-white
            // We blend with base color
            double opacity = 0.5 + energy * 0.4;
            byte alpha = (byte)(255 * Math.Min(0.95, opacity));
            byte glowAlpha = (byte)(255 * Math.Min(0.3, opacity * 0.3));

            _neonGlowLine.Points = glowPts;
            _neonGlowLine.Stroke = new SolidColorBrush(Color.FromArgb(glowAlpha, color.R, color.G, color.B));
            _neonGlowLine.StrokeThickness = 12 + energy * 10;
            _neonGlowLine.Opacity = 1.0;

            _neonLine.Points = points;
            _neonLine.Stroke = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            _neonLine.StrokeThickness = 2.0 + energy * 1.5;
            _neonLine.Opacity = 1.0;
        }

        private void RestoreOriginal()
        {
            if (!_originalSaved || _sidebarBorder == null) return;
            _sidebarBorder.Background = _originalBackground;
            _sidebarBorder.Opacity = _originalOpacity;
        }

        private double ComputeEnergy()
        {
            try
            {
                var vizProvider = VisualizationDataProvider.Current;
                if (vizProvider != null)
                {
                    if (!_hasLoggedVizState)
                    {
                        Logger.Info("[SidebarGlow] VisualizationDataProvider available — audio-reactive mode");
                        _hasLoggedVizState = true;
                    }

                    vizProvider.GetLevels(out float peak, out float rms);
                    double raw = Math.Min(1.0, (rms * 0.6 + peak * 0.4) * 2.5);
                    double alpha = raw > _smoothedEnergy ? 0.4 : 0.12;
                    _smoothedEnergy += (raw - _smoothedEnergy) * alpha;
                    return _smoothedEnergy;
                }
            }
            catch { }

            if (!_hasLoggedVizState)
            {
                Logger.Info("[SidebarGlow] No VisualizationDataProvider — sine pulse fallback");
                _hasLoggedVizState = true;
            }

            double elapsed = (DateTime.UtcNow - _pulseStartTime).TotalSeconds;
            return 0.35 + 0.1 * Math.Sin(2.0 * Math.PI * elapsed / 4.0);
        }

        private static Color MapColor(Color baseColor, double energy)
        {
            var (hue, sat, _) = RgbToHsv(baseColor.R, baseColor.G, baseColor.B);

            double val = 0.5 + energy * 0.5;
            double newSat = Math.Min(1.0, sat * 0.8 + energy * 0.3);
            double newHue = hue - energy * 8.0;
            if (newHue < 0) newHue += 360;

            var (r, g, b) = HsvToRgb(newHue, newSat, val);
            return Color.FromRgb(r, g, b);
        }

        private static Border FindSidebarBorder(out Control sidebarCtrl)
        {
            sidebarCtrl = null;
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return null;

            var sidebar = FindByTypeName(mainWindow, "Sidebar");
            if (sidebar is Control ctrl)
            {
                sidebarCtrl = ctrl;
                return ctrl.Template?.FindName("BorderContentHolder", ctrl) as Border;
            }

            return null;
        }

        private static DependencyObject FindByTypeName(DependencyObject parent, string typeName)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child.GetType().Name == typeName)
                    return child;

                var result = FindByTypeName(child, typeName);
                if (result != null) return result;
            }
            return null;
        }

        // --- HSV utilities ---

        private static (double hue, double saturation, double value) RgbToHsv(byte r, byte g, byte b)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            double hue = 0;
            if (delta > 0)
            {
                if (max == rd) hue = 60 * (((gd - bd) / delta) % 6);
                else if (max == gd) hue = 60 * (((bd - rd) / delta) + 2);
                else hue = 60 * (((rd - gd) / delta) + 4);
            }
            if (hue < 0) hue += 360;

            double saturation = max > 0 ? delta / max : 0;
            return (hue, saturation, max);
        }

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r1, g1, b1;
            if (h < 60)       { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else              { r1 = c; g1 = 0; b1 = x; }

            return (
                (byte)Math.Min(255, (r1 + m) * 255),
                (byte)Math.Min(255, (g1 + m) * 255),
                (byte)Math.Min(255, (b1 + m) * 255));
        }
    }
}
