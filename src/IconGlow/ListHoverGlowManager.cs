using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Services;

namespace UniPlaySong.IconGlow
{
    // Applies glow effects to game icons in Playnite's list/grid view:
    //   1. Hover: lightweight DropShadowEffect (no layout changes, no flicker)
    //   2. Selected: full SkiaSharp glow with Grid wrapper (or DropShadowEffect in subtle mode)
    public class ListHoverGlowManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SettingsService _settingsService;
        private readonly IconColorExtractor _colorExtractor;

        private UniPlaySongSettings _settings => _settingsService.Current;

        private Window _mainWindow;

        // Full glow: stronger SkiaSharp render for selected game
        private const double FullGlowSize = 5.0;
        private const double FullGlowIntensity = 1.8;
        private const double FullGlowOpacity = 0.85;
        private const double FullDropShadowBlurRadius = 10;
        private const double FullDropShadowOpacity = 0.8;

        // Subtle glow: softer SkiaSharp render for selected game
        private const double SubtleGlowSize = 4.0;
        private const double SubtleGlowIntensity = 1.6;
        private const double SubtleGlowOpacity = 0.75;
        private const double SubtleDropShadowBlurRadius = 8;
        private const double SubtleDropShadowOpacity = 0.7;

        // Hover DropShadowEffect parameters (no Grid wrapper, no flicker)
        private const double HoverDropShadowBlurRadius = 12;
        private const double HoverDropShadowOpacity = 0.85;

        // Fade durations
        private static readonly Duration FadeInDuration = new Duration(TimeSpan.FromMilliseconds(150));
        private static readonly Duration FadeOutDuration = new Duration(TimeSpan.FromMilliseconds(120));
        private static readonly Duration HoverFadeInDuration = new Duration(TimeSpan.FromMilliseconds(50));

        // Hover state — only DropShadowEffect, no Grid wrapper
        private Image _hoveredIcon;
        private Effect _hoverSavedEffect;

        // Selected state — Grid wrapper (full mode) or DropShadowEffect (subtle mode)
        private Image _selectedIcon;
        private Panel _selectedParent;
        private Grid _selectedWrapper;
        private int _selectedOriginalIndex;
        private Effect _selectedSavedEffect;
        private Thickness _selectedSavedMargin;
        private Guid _selectedGameId;
        private Game _lastSelectedGame;
        private System.Windows.Threading.DispatcherTimer _glowTimer;
        private int _listRenderVersion;

        public ListHoverGlowManager(SettingsService settingsService, IconColorExtractor colorExtractor)
        {
            _settingsService = settingsService;
            _colorExtractor = colorExtractor;
            _settingsService.Current.PropertyChanged += OnSettingChanged;
        }

        public void Attach()
        {
            _mainWindow = Application.Current?.MainWindow;
            if (_mainWindow == null) return;

            _mainWindow.PreviewMouseMove += OnPreviewMouseMove;
            _mainWindow.MouseLeave += OnMouseLeave;
        }

        public void Detach()
        {
            _settingsService.Current.PropertyChanged -= OnSettingChanged;
            _glowTimer?.Stop();
            _glowTimer = null;
            ClearHoverGlow();
            ClearSelectedGlow();
            _lastSelectedGame = null;
            if (_mainWindow != null)
            {
                _mainWindow.PreviewMouseMove -= OnPreviewMouseMove;
                _mainWindow.MouseLeave -= OnMouseLeave;
                _mainWindow = null;
            }
        }

        private void OnSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.SubtleListGlow) ||
                e.PropertyName == nameof(UniPlaySongSettings.EnableListIconGlow))
            {
                // Re-apply selected glow with new setting values
                if (_lastSelectedGame != null)
                    OnGameSelected(_lastSelectedGame);
            }
        }

        public void OnGameSelected(Game game)
        {
            _lastSelectedGame = game;
            if (_mainWindow == null) return;

            // Fade out old glow, then unwrap after fade completes (avoids flicker on previous icon)
            FadeOutAndClearSelectedGlow();

            // Delay lets Playnite finish updating the visual tree for the newly
            // selected game (icon load, template application, layout) before we
            // try to find and wrap the icon.  Without this, the icon element may
            // not be ready or the layout pass exposes a bare icon frame.
            _glowTimer?.Stop();
            _glowTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _glowTimer.Tick += (s, e) =>
            {
                _glowTimer.Stop();
                _glowTimer = null;
                ApplySelectedGlow(game);
            };
            _glowTimer.Start();
        }

        private void ApplySelectedGlow(Game game)
        {
            if (game == null || !_settings.EnableListIconGlow)
            {
                ClearSelectedGlow();
                return;
            }

            try
            {
                var gameId = game.Id;

                // Same game, wrapper still live — update glow in-place (no flicker)
                if (gameId == _selectedGameId && _selectedIcon != null && _selectedWrapper != null
                    && _selectedParent != null && _selectedParent.Children.Contains(_selectedWrapper))
                {
                    UpdateSelectedGlowInPlace();
                    return;
                }

                // --- Phase 1: gather info and render glow (old glow stays visible) ---
                var listItem = FindListBoxItemByGameId(gameId);
                if (listItem == null) { ClearSelectedGlow(); return; }

                if (IsInsideGameView(listItem)) { ClearSelectedGlow(); return; }

                var icon = TileFinder.FindChildByName<Image>(listItem, "PART_ImageIcon");
                if (icon == null || icon.ActualWidth <= 0 || icon.ActualHeight <= 0) { ClearSelectedGlow(); return; }

                bool subtle = _settings.SubtleListGlow;
                double glowSize = subtle ? SubtleGlowSize : FullGlowSize;
                double glowIntensity = subtle ? SubtleGlowIntensity : FullGlowIntensity;
                double glowOpacity = subtle ? SubtleGlowOpacity : FullGlowOpacity;
                double dsBlur = subtle ? SubtleDropShadowBlurRadius : FullDropShadowBlurRadius;
                double dsOpacity = subtle ? SubtleDropShadowOpacity : FullDropShadowOpacity;

                var parent = FindParentPanel(icon);
                if (parent == null) { ClearSelectedGlow(); return; }

                int iconIndex = parent.Children.IndexOf(icon);
                if (iconIndex < 0) { ClearSelectedGlow(); return; }

                // Snapshot icon source for background render
                var iconSource = icon.Source as BitmapSource;
                if (iconSource == null) { ClearSelectedGlow(); return; }
                if (!iconSource.IsFrozen) iconSource = (BitmapSource)iconSource.CloneCurrentValue();
                if (!iconSource.IsFrozen) iconSource.Freeze();

                double iconW = icon.ActualWidth, iconH = icon.ActualHeight;
                int thisVersion = ++_listRenderVersion;

                // Offload color extraction + SkiaSharp render to background thread
                Task.Run(() =>
                {
                    var color1 = GetGlowColorBg(gameId, iconSource);
                    var glowBitmap = RenderGlowBg(gameId, iconSource, iconW, iconH, glowSize, glowIntensity);

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (_listRenderVersion != thisVersion) return; // stale
                        ApplySelectedGlowResult(icon, parent, iconIndex, gameId,
                            glowBitmap, color1, iconW, iconH, glowSize, glowOpacity, dsBlur, dsOpacity);
                    }));
                });
            }
            catch
            {
                // Visual tree may not be ready
            }
        }

        private void ApplySelectedGlowResult(Image icon, Panel parent, int iconIndex, Guid gameId,
            BitmapSource glowBitmap, Color color1, double iconW, double iconH,
            double glowSize, double glowOpacity, double dsBlur, double dsOpacity)
        {
            try
            {
                if (glowBitmap == null) return;

                // Verify icon is still valid in the visual tree
                if (icon.ActualWidth <= 0) return;

                if (icon == _hoveredIcon)
                    ClearHoverGlow();

                var glowImage = GlowRenderer.CreateGlowImage(glowBitmap, iconW, iconH, glowSize);
                glowImage.Opacity = 0;
                glowImage.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, glowOpacity, FadeInDuration));

                var savedEffect = icon.Effect;
                var dropShadow = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    Color = color1,
                    BlurRadius = dsBlur,
                    Opacity = 0
                };
                icon.Effect = dropShadow;
                dropShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0, dsOpacity, FadeInDuration));

                var savedMargin = icon.Margin;
                var wrapper = new Grid
                {
                    HorizontalAlignment = icon.HorizontalAlignment,
                    VerticalAlignment = icon.VerticalAlignment,
                    Margin = savedMargin,
                    ClipToBounds = false
                };

                // Re-check iconIndex in case visual tree changed during async render
                int currentIndex = parent.Children.IndexOf(icon);
                if (currentIndex < 0) return;

                icon.Margin = new Thickness(0);
                parent.Children.RemoveAt(currentIndex);
                wrapper.Children.Add(glowImage);
                wrapper.Children.Add(icon);

                if (parent is DockPanel)
                    DockPanel.SetDock(wrapper, DockPanel.GetDock(icon));

                parent.Children.Insert(currentIndex, wrapper);

                _selectedIcon = icon;
                _selectedParent = parent;
                _selectedWrapper = wrapper;
                _selectedOriginalIndex = currentIndex;
                _selectedGameId = gameId;
                _selectedSavedEffect = savedEffect;
                _selectedSavedMargin = savedMargin;
            }
            catch { }
        }

        // Swaps the glow image inside the existing wrapper without tearing it down
        private void UpdateSelectedGlowInPlace()
        {
            try
            {
                bool subtle = _settings.SubtleListGlow;
                double glowSize = subtle ? SubtleGlowSize : FullGlowSize;
                double glowIntensity = subtle ? SubtleGlowIntensity : FullGlowIntensity;
                double glowOpacity = subtle ? SubtleGlowOpacity : FullGlowOpacity;
                double dsBlur = subtle ? SubtleDropShadowBlurRadius : FullDropShadowBlurRadius;
                double dsOpacity = subtle ? SubtleDropShadowOpacity : FullDropShadowOpacity;

                var color1 = GetGlowColor(_selectedGameId, _selectedIcon);
                var glowBitmap = GetOrRenderGlow(_selectedGameId, _selectedIcon, glowSize, glowIntensity);
                if (glowBitmap == null) return;

                var newGlowImage = GlowRenderer.CreateGlowImage(glowBitmap,
                    _selectedIcon.ActualWidth, _selectedIcon.ActualHeight, glowSize);
                newGlowImage.Opacity = glowOpacity;

                // Replace old glow image (index 0) with new one — icon stays at index 1
                if (_selectedWrapper.Children.Count > 0 && _selectedWrapper.Children[0] is Image)
                    _selectedWrapper.Children.RemoveAt(0);
                _selectedWrapper.Children.Insert(0, newGlowImage);

                _selectedIcon.Effect = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    Color = color1,
                    BlurRadius = dsBlur,
                    Opacity = dsOpacity
                };
            }
            catch { }
        }

        // Fades glow out on the previous game icon, then unwraps cleanly after animation
        private void FadeOutAndClearSelectedGlow()
        {
            if (_selectedIcon == null) return;

            try
            {
                bool wrapperIsLive = _selectedWrapper != null && _selectedParent != null
                                  && _selectedParent.Children.Contains(_selectedWrapper);
                if (!wrapperIsLive) { ClearSelectedGlow(); return; }

                // Capture references before nulling state
                var icon = _selectedIcon;
                var parent = _selectedParent;
                var wrapper = _selectedWrapper;
                var savedEffect = _selectedSavedEffect;
                var savedMargin = _selectedSavedMargin;
                var originalIndex = _selectedOriginalIndex;

                // Null out state so new glow can be applied independently
                _selectedIcon = null;
                _selectedParent = null;
                _selectedWrapper = null;
                _selectedSavedEffect = null;
                _selectedGameId = Guid.Empty;

                // Fade out the glow image
                var fadeOut = new DoubleAnimation(0, FadeOutDuration);
                if (wrapper.Children.Count > 0 && wrapper.Children[0] is Image glowImg)
                    glowImg.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                // Fade out the drop shadow
                if (icon.Effect is DropShadowEffect ds)
                    ds.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, FadeOutDuration));

                // After fade completes, remove glow image and effect but leave
                // the wrapper in place — unwrapping causes layout shifts that flicker.
                var cleanupTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = FadeOutDuration.TimeSpan + TimeSpan.FromMilliseconds(20)
                };
                cleanupTimer.Tick += (s, e) =>
                {
                    cleanupTimer.Stop();
                    try
                    {
                        // Remove glow image (index 0), keep icon (index 1 → becomes 0)
                        if (wrapper.Children.Count > 1 && wrapper.Children[0] is Image)
                            wrapper.Children.RemoveAt(0);
                        icon.Effect = savedEffect;
                    }
                    catch { }
                };
                cleanupTimer.Start();
            }
            catch { ClearSelectedGlow(); }
        }

        private void ClearSelectedGlow()
        {
            if (_selectedIcon == null) return;

            try
            {
                bool wrapperIsLive = _selectedWrapper != null && _selectedParent != null
                                  && _selectedParent.Children.Contains(_selectedWrapper);

                if (wrapperIsLive)
                {
                    _selectedIcon.Effect = _selectedSavedEffect;
                    _selectedIcon.Margin = _selectedSavedMargin;
                    _selectedWrapper.Children.Clear();
                    _selectedParent.Children.Remove(_selectedWrapper);
                    _selectedParent.Children.Insert(
                        Math.Min(_selectedOriginalIndex, _selectedParent.Children.Count),
                        _selectedIcon);
                }
                else
                {
                    try { _selectedIcon.Effect = _selectedSavedEffect; }
                    catch { }
                }
            }
            catch { }

            _selectedIcon = null;
            _selectedParent = null;
            _selectedWrapper = null;
            _selectedSavedEffect = null;
            _selectedGameId = Guid.Empty;
        }

        private ListBoxItem FindListBoxItemByGameId(Guid gameId)
        {
            if (_mainWindow == null || gameId == Guid.Empty) return null;

            var listBoxItems = new List<ListBoxItem>();
            FindAllOfType(_mainWindow, listBoxItems);

            foreach (var item in listBoxItems)
            {
                if (!item.IsVisible || IsInsideGameView(item)) continue;
                var itemGameId = GetGameId(item.DataContext);
                if (itemGameId == gameId)
                    return item;
            }
            return null;
        }

        private static void FindAllOfType<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            if (parent == null) return;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    results.Add(match);
                FindAllOfType(child, results);
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            ClearHoverGlow();
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_settings.EnableListIconGlow)
            {
                ClearHoverGlow();
                return;
            }

            try
            {
                var pos = e.GetPosition(_mainWindow);
                var hitResult = VisualTreeHelper.HitTest(_mainWindow, pos);
                if (hitResult?.VisualHit == null)
                {
                    ClearHoverGlow();
                    return;
                }

                var listBoxItem = TileFinder.FindAncestor<ListBoxItem>(hitResult.VisualHit);
                if (listBoxItem == null)
                {
                    ClearHoverGlow();
                    return;
                }

                if (IsInsideGameView(listBoxItem))
                {
                    ClearHoverGlow();
                    return;
                }

                var icon = TileFinder.FindChildByName<Image>(listBoxItem, "PART_ImageIcon");
                if (icon == null || icon.ActualWidth <= 0 || icon.ActualHeight <= 0)
                {
                    ClearHoverGlow();
                    return;
                }

                // Skip if this is the selected icon (already has glow)
                if (icon == _selectedIcon)
                {
                    ClearHoverGlow();
                    return;
                }

                // Same icon — nothing to do
                if (icon == _hoveredIcon) return;

                ClearHoverGlow();
                ApplyHoverGlow(icon, listBoxItem);
            }
            catch
            {
                ClearHoverGlow();
            }
        }

        // Hover uses only DropShadowEffect — no Grid wrapper, no layout changes, no flicker
        private void ApplyHoverGlow(Image icon, ListBoxItem listBoxItem)
        {
            try
            {
                var gameId = GetGameId(listBoxItem.DataContext);
                var color1 = GetGlowColor(gameId, icon);

                _hoverSavedEffect = icon.Effect;
                var dropShadow = new DropShadowEffect
                {
                    ShadowDepth = 0,
                    Color = color1,
                    BlurRadius = HoverDropShadowBlurRadius,
                    Opacity = 0
                };
                icon.Effect = dropShadow;
                dropShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0, HoverDropShadowOpacity, HoverFadeInDuration));
                _hoveredIcon = icon;
            }
            catch { }
        }

        private void ClearHoverGlow()
        {
            if (_hoveredIcon == null) return;

            try
            {
                _hoveredIcon.Effect = _hoverSavedEffect;
            }
            catch { }

            _hoveredIcon = null;
            _hoverSavedEffect = null;
        }

        // Gets the primary glow color for a game icon
        private Color GetGlowColor(Guid gameId, Image icon)
        {
            if (gameId != Guid.Empty && icon.Source != null)
            {
                var (primary, _) = _colorExtractor.GetGlowColors(gameId, icon.Source);
                return primary;
            }
            return Color.FromRgb(100, 149, 237);
        }

        // Thread-safe: gets glow color from a frozen BitmapSource
        private Color GetGlowColorBg(Guid gameId, BitmapSource iconSource)
        {
            if (gameId != Guid.Empty && iconSource != null)
            {
                var (primary, _) = _colorExtractor.GetGlowColors(gameId, iconSource);
                return primary;
            }
            return Color.FromRgb(100, 149, 237);
        }

        // Thread-safe: renders glow bitmap from a frozen BitmapSource
        private BitmapSource RenderGlowBg(Guid gameId, BitmapSource iconSource,
            double iconW, double iconH, double glowSize, double glowIntensity)
        {
            Color color1, color2;
            if (gameId != Guid.Empty && iconSource != null)
                (color1, color2) = _colorExtractor.GetGlowColors(gameId, iconSource);
            else
            {
                color1 = Color.FromRgb(100, 149, 237);
                color2 = Color.FromRgb(180, 100, 255);
            }

            return GlowRenderer.RenderGlow(iconSource, color1, color2,
                glowSize, iconW, iconH, glowIntensity);
        }

        // Returns a freshly rendered glow bitmap (cache cleared on setting change via Detach/Attach cycle)
        private BitmapSource GetOrRenderGlow(Guid gameId, Image icon, double glowSize, double glowIntensity)
        {
            Color color1, color2;
            if (gameId != Guid.Empty && icon.Source != null)
                (color1, color2) = _colorExtractor.GetGlowColors(gameId, icon.Source);
            else
            {
                color1 = Color.FromRgb(100, 149, 237);
                color2 = Color.FromRgb(180, 100, 255);
            }

            return GlowRenderer.RenderGlow(icon.Source, color1, color2,
                glowSize, icon.ActualWidth, icon.ActualHeight, glowIntensity);
        }

        private static Panel FindParentPanel(DependencyObject child)
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

        private static bool IsInsideGameView(DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.Name == "PART_ControlGameView")
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private static Guid GetGameId(object dataContext)
        {
            if (dataContext == null) return Guid.Empty;

            try
            {
                var type = dataContext.GetType();

                var idProp = type.GetProperty("Id");
                if (idProp != null && idProp.PropertyType == typeof(Guid))
                    return (Guid)idProp.GetValue(dataContext);

                var gameProp = type.GetProperty("Game");
                if (gameProp != null)
                {
                    var game = gameProp.GetValue(dataContext);
                    if (game != null)
                    {
                        var gameIdProp = game.GetType().GetProperty("Id");
                        if (gameIdProp != null)
                            return (Guid)gameIdProp.GetValue(game);
                    }
                }
            }
            catch { }

            return Guid.Empty;
        }
    }
}
