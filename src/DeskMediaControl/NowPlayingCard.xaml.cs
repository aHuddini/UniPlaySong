using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace UniPlaySong.DeskMediaControl
{
    // The card that pops in under the top panel's Now Playing title (hover or click). Its own window, owned by Playnite's
    // so it stays above it, never activated (clicks don't take focus from Playnite or a game). On Windows 11 22H2+ the
    // system draws the frosted glass (DWM acrylic backdrop, rounded corners); elsewhere a dark tint stands in. Knows
    // nothing about players: it shows NowPlayingCardModel and calls the host's actions.
    internal partial class NowPlayingCard : Window
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const double GapBelowPanel = 0; // flush with the edge the view model found

        private static readonly Brush GlassTint = Frozen(Color.FromArgb(0x01, 0x1C, 0x20, 0x2C));   // the blur tints; this keeps hit-testing
        private static readonly Brush SolidTint = Frozen(Color.FromArgb(0xF0, 0x16, 0x1A, 0x24));   // no acrylic
        private static readonly Brush CalmOnBrush = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));   // Calm Down on: blue
        private static readonly Brush CalmOffBrush = Frozen(Color.FromRgb(0x8A, 0x8F, 0x9A));  // off: grey

        private readonly Window _owner;
        private readonly NowPlayingCardHost _host;
        private readonly NowPlayingCardModel _model;
        private readonly DispatcherTimer _tick;

        public NowPlayingCard(Window owner, NowPlayingCardHost host)
        {
            ((IComponentConnector)this).InitializeComponent();
            // Owned through Windows, not WPF: WPF ownership puts the card in Playnite's OwnedWindows, which sets
            // HasChildWindow, and themes dim the whole main window while that's true (as for a dialog). The native owner
            // still keeps the card above Playnite and hides it with Playnite.
            _owner = owner;
            new WindowInteropHelper(this).Owner = new WindowInteropHelper(owner).Handle;
            _host = host;
            _model = new NowPlayingCardModel(host);
            _model.PropertyChanged += OnModelChanged;
            DataContext = _model;
            _host.Changed += OnHostChanged;
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            _tick.Tick += (s, e) => { _model.Tick(); UpdateBars(); };
            SourceInitialized += (s, e) => ApplyWindowStyle();
            LayoutUpdated += (s, e) => UpdateBars();
            TrackScroll.ScrollChanged += (s, e) => UpdateBars();
            SeekStrip.MouseEnter += (s, e) => { if (_model.CanSeek) SeekLine.Height = 5; };
            VolumeTrack.SizeChanged += (s, e) => ShowVolume(_shownVolume);
            BannerArea.SizeChanged += (s, e) => SizeBackdrop();
            BannerLook.Current = BannerLook.Pick(_host.CardShowsArt?.Invoke() == true, _model.HasArt);
            ApplyLook();
            FxButton.Tag = WaveIcon;
            SeekStrip.MouseLeave += (s, e) => SeekLine.Height = 3;
            CollapseButton.Tag = FindResource(NowPlayingCardModel.StartsCollapsed ? "IconChevronDown" : "IconChevronUp");
            // The mouse back on the card while it fades out: it stays
            MouseEnter += (s, e) => { if (_closing) { _closing = false; AnimateIn(); } };
        }

        // Opens (or moves) the card under the panel. panel: the Now Playing panel's bounds in screen pixels.
        public void ShowUnder(Rect panel)
        {
            _model.Refresh();
            bool opening = !IsVisible || _closing;
            _closing = false;
            if (!IsVisible) Show();

            var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = fromDevice.Transform(panel.TopLeft);
            var bottomRight = fromDevice.Transform(panel.BottomRight);
            double left = (topLeft.X + bottomRight.X) / 2 - ActualWidth / 2;
            // Keep it inside Playnite's window, so it never hangs off the screen edge beside the top panel
            if (_owner != null && _owner.WindowState != WindowState.Minimized)
            {
                double ownerLeft = _owner.WindowState == WindowState.Maximized ? fromDevice.Transform(OwnerTopLeftDevice()).X : _owner.Left;
                left = Math.Max(ownerLeft + 8, Math.Min(left, ownerLeft + _owner.ActualWidth - ActualWidth - 8));
            }
            Left = left;
            Top = bottomRight.Y + GapBelowPanel;
            _tick.Start();
            _model.SetShown(true);
            if (opening) AnimateIn();
        }

        // Window and data ready before it shows: the hover delay covers creating the window and building the list.
        public void Prepare()
        {
            new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible) _model.Refresh();
            UpdateLook(reloadArt: true); // the setting may have changed since the last open
        }

        // Where the card is on screen, in pixels (empty while hidden)
        public Rect ScreenBounds
        {
            get
            {
                if (!IsVisible) return Rect.Empty;
                var hwnd = new WindowInteropHelper(this).Handle;
                return GetWindowRect(hwnd, out var r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;
            }
        }

        // Pinned: the card stays open when the mouse leaves, until it's unpinned or the music stops
        public bool Pinned { get; private set; }

        // Stays open through the mouse leaving: pinned, or its right-click menu is up (the menu can reach past the card)
        public bool HoldOpen => Pinned || _menuOpen;
        public event Action Released; // no longer held: close once the mouse is away
        private bool _menuOpen;

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            SetPinned(!Pinned);
            if (!Pinned) Released?.Invoke();
        }

        private void SetPinned(bool on)
        {
            Pinned = on;
            PinButton.Foreground = on ? CalmOnBrush : CalmOffBrush;
            PinButton.ToolTip = on ? "Unpin" : "Pin: keep the card open";
        }

        public void HideCard()
        {
            if (Pinned) SetPinned(false);
            _tick.Stop();
            _model.SetShown(false);
            UpdateBars();
            if (!IsVisible || _closing) return;
            _closing = true;
            var fade = new DoubleAnimation(0, OutDuration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) =>
            {
                if (!_closing) return; // reopened while fading out
                _closing = false;
                Hide();
            };
            Frame.BeginAnimation(OpacityProperty, fade);
        }

        // Opening: fades in while settling down a few pixels. Runs only for these frames, then nothing.
        private static readonly Duration InDuration = new Duration(TimeSpan.FromMilliseconds(160));
        private static readonly Duration OutDuration = new Duration(TimeSpan.FromMilliseconds(110));
        private const double SettleDistance = 6;
        private bool _closing;

        // Shown and not on its way out
        public bool IsOpen => IsVisible && !_closing;

        private bool _animatingIn;

        private void AnimateIn()
        {
            _animatingIn = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, InDuration) { EasingFunction = ease });
            if (!(Frame.RenderTransform is TranslateTransform slide))
                Frame.RenderTransform = slide = new TranslateTransform();
            var settle = new DoubleAnimation(-SettleDistance, 0, InDuration) { EasingFunction = ease };
            settle.Completed += (s, e) => { _animatingIn = false; UpdateBars(); };
            slide.BeginAnimation(TranslateTransform.YProperty, settle);
        }

        private Point OwnerTopLeftDevice()
        {
            var handle = new WindowInteropHelper(_owner).Handle;
            return GetWindowRect(handle, out var r) ? new Point(r.Left, r.Top) : new Point(_owner.Left, _owner.Top);
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        // One song change raises several updates (the song, its title arriving, the queue rebuilding): refresh once
        // after the burst.
        private bool _refreshQueued;
        private void OnHostChanged()
        {
            if (!IsVisible || _refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _refreshQueued = false; if (IsVisible) _model.Refresh(); }), DispatcherPriority.Background);
        }

        private void OnModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NowPlayingCardModel.EqualizerOn))
            {
                EqButton.Foreground = _model.EqualizerOn ? CalmOnBrush : CalmOffBrush;
                EqButton.ToolTip = _model.EqualizerOn ? "Equalizer is on" : "Equalizer";
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.LiveEffectsOn))
            {
                FxButton.Foreground = _model.LiveEffectsOn ? CalmOnBrush : CalmOffBrush;
                FxButton.ToolTip = _model.LiveEffectsOn ? "Live Effects are on" : "Live Effects";
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.Volume))
            {
                if (!_dragVolume) ShowVolume(_model.Volume);
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.Progress))
            {
                if (!_seeking) ProgressScale.ScaleX = _model.Progress;
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.CanSeek))
            {
                SeekStrip.Cursor = _model.CanSeek ? System.Windows.Input.Cursors.Hand : null;
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.Collapsed))
            {
                CollapseButton.Tag = FindResource(_model.Collapsed ? "IconChevronDown" : "IconChevronUp");
                CollapseButton.ToolTip = _model.Collapsed ? "Show songs" : "Hide songs";
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.HasArt))
                UpdateLook(reloadArt: false); // the model prepares the new art right after
            else if (e.PropertyName == nameof(NowPlayingCardModel.Banner))
            {
                // New art fades in rather than popping
                if (IsVisible)
                {
                    BannerArt.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, BannerLook.Current.ArtShown, TintFade));
                    BannerArtSharp.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, BannerLook.Current.SharpShown, TintFade));
                }
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.TintVivid))
            {
                // Mica-style tint: vivid colour at the top left, the darker one at the bottom right; a short fade
                ApplyTint();
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.IsPlaying))
            {
                PlayPauseButton.Tag = FindResource(_model.IsPlaying ? "IconPause" : "IconPlay");
                PlayPauseButton.ToolTip = _model.IsPlaying ? "Pause" : "Play";
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.CalmDownOn))
            {
                CalmDownButton.Foreground = _model.CalmDownOn ? CalmOnBrush : CalmOffBrush;
                CalmDownButton.ToolTip = _model.CalmDownOn ? "Calm Down is on" : "Calm Down";
            }
        }

        // No activation, no taskbar or Alt+Tab entry; frosted glass and rounded corners where available. The glass is
        // the compositor's blur-behind (as the toasts use), not Windows 11's system backdrop: that one only draws while
        // a window is active, and this card never is.
        private void ApplyWindowStyle()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            bool glass = false;
            try
            {
                int on = 1, round = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                glass = _glass = Common.DialogHelper.EnableBlurBehind(hwnd, BannerLook.Current.Glass, acrylic: true);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            // The blur shows only where WPF's own surface is clear, not just the window's background
            if (glass && HwndSource.FromHwnd(hwnd)?.CompositionTarget is HwndTarget target)
                target.BackgroundColor = Colors.Transparent;
            Frame.Background = glass ? GlassTint : SolidTint;
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) { _host.PlayPause?.Invoke(); _model.Tick(); }
        private void Next_Click(object sender, RoutedEventArgs e) => _host.Next?.Invoke();
        private void Previous_Click(object sender, RoutedEventArgs e) => _host.Previous?.Invoke();
        private void CalmDown_Click(object sender, RoutedEventArgs e) { _host.ToggleCalmDown?.Invoke(); _model.Refresh(); }
        private void Eq_Click(object sender, RoutedEventArgs e) { _host.ToggleEqualizer?.Invoke(); _model.Refresh(); }
        private void Fx_Click(object sender, RoutedEventArgs e) { _host.ToggleLiveEffects?.Invoke(); _model.Refresh(); }

        // Live Effects' icon: a decaying wave. Drawn as a line, so it's widened into a filled shape for the icon buttons.
        private static readonly Geometry WaveIcon = MakeWaveIcon();
        private static Geometry MakeWaveIcon()
        {
            var line = Geometry.Parse("M1,8 C2,8 2.5,2 4,2 C5.5,2 5.5,14 7,14 C8.2,14 8.5,4.5 9.7,4.5 C10.8,4.5 11,11.5 12,11.5 C12.8,11.5 13.2,8 15,8");
            var wave = line.GetWidenedPathGeometry(new Pen(Brushes.Black, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
            wave.Freeze();
            return wave;
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e) => _host.OpenFolder?.Invoke();

        // Folding the list shrinks the card from the bottom, leaving the mouse below it: stay open a moment so the
        // hover-out doesn't close it under the click.
        // ---- The playing row's bouncing bars, drawn by the compositor over the row's still WPF bars ----
        private CompositionBars _bars;
        private bool _barsTried;
        private FrameworkElement _hiddenIcon; // the still bars currently covered by the compositor's

        private void UpdateBars()
        {
            var row = FindLiveRow();
            var icon = row == null || !IsVisible ? null : FindNowIcon(row);
            if (icon != null && icon.IsVisible && _bars == null && !_barsTried)
            {
                _barsTried = true;
                _bars = CompositionBars.TryCreate(this, ((SolidColorBrush)FindResource("Accent")).Color, null);
            }

            // Not while the card fades and slides in: the compositor's bars don't follow WPF's slide
            bool live = _bars != null && icon != null && icon.IsVisible && !_animatingIn;
            if (_hiddenIcon != null && _hiddenIcon != icon) { _hiddenIcon.Opacity = 1; _hiddenIcon = null; }
            if (!live)
            {
                _bars?.SetLive(false);
                if (_hiddenIcon != null) { _hiddenIcon.Opacity = 1; _hiddenIcon = null; }
                return;
            }

            try
            {
                var bounds = icon.TransformToAncestor(this).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                var clip = TrackScroll.TransformToAncestor(this).TransformBounds(new Rect(0, 0, TrackScroll.ActualWidth, TrackScroll.ActualHeight));
                _bars.SetColor(((SolidColorBrush)FindResource(row.IsPrimary ? "PrimaryTint" : "Accent")).Color);
                _bars.Place(bounds, clip, VisualTreeHelper.GetDpi(this).DpiScaleX);
                _bars.SetLive(true);
                icon.Opacity = 0;
                _hiddenIcon = icon;
            }
            catch (InvalidOperationException) { _bars.SetLive(false); }
        }

        private NowPlayingCardTrack FindLiveRow()
        {
            foreach (var row in _model.Tracks) if (row.IsLive) return row;
            return null;
        }

        // The row's still bars, from its template (the row is a Button whose template holds "NowIcon")
        private FrameworkElement FindNowIcon(NowPlayingCardTrack row)
        {
            if (!(TrackList.ItemContainerGenerator.ContainerFromItem(row) is ContentPresenter presenter)) return null;
            if (VisualTreeHelper.GetChildrenCount(presenter) == 0 || !(VisualTreeHelper.GetChild(presenter, 0) is Button button)) return null;
            return button.Template?.FindName("NowIcon", button) as FrameworkElement;
        }

        // ---- Volume: drag sets it live, release saves it (one settings write, not one per pixel); the wheel steps 5% ----
        private bool _dragVolume;
        private int _shownVolume;

        private void ShowVolume(int volume)
        {
            _shownVolume = Math.Max(0, Math.Min(100, volume));
            double travel = Math.Max(0, VolumeTrack.ActualHeight - VolumeThumb.Height);
            double y = travel * _shownVolume / 100.0;
            VolumeFill.Height = y + VolumeThumb.Height / 2;
            VolumeThumb.Margin = new Thickness(0, 0, 0, y);
            VolumeTrack.ToolTip = $"Volume {_shownVolume}%";
        }

        private int VolumeAt(System.Windows.Input.MouseEventArgs e)
        {
            double travel = VolumeTrack.ActualHeight - VolumeThumb.Height;
            if (travel <= 0) return _shownVolume;
            double fromBottom = VolumeTrack.ActualHeight - e.GetPosition(VolumeTrack).Y - VolumeThumb.Height / 2;
            return (int)Math.Round(Math.Max(0, Math.Min(1, fromBottom / travel)) * 100);
        }

        private void Volume_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragVolume = true;
            VolumeTrack.CaptureMouse();
            ShowVolume(VolumeAt(e));
            _host.PreviewVolume?.Invoke(_shownVolume);
            e.Handled = true;
        }

        private void Volume_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragVolume) return;
            ShowVolume(VolumeAt(e));
            _host.PreviewVolume?.Invoke(_shownVolume);
        }

        private void Volume_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_dragVolume) return;
            _dragVolume = false;
            VolumeTrack.ReleaseMouseCapture();
            _host.SaveVolume?.Invoke(_shownVolume);
        }

        private void Volume_LostCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragVolume) return;
            _dragVolume = false;
            _host.SaveVolume?.Invoke(_shownVolume); // keep what the user dragged to
        }

        private void Volume_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            ShowVolume(_shownVolume + (e.Delta > 0 ? 5 : -5));
            _host.SaveVolume?.Invoke(_shownVolume);
            e.Handled = true;
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            _model.ToggleCollapsed();
            KeepOpenUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        }

        public DateTime KeepOpenUntil { get; private set; }

        // ---- Seeking: click to jump, drag to scrub (the line follows the mouse; one seek on release) ----
        private bool _seeking;

        private double SeekFraction(System.Windows.Input.MouseEventArgs e) =>
            SeekLine.ActualWidth <= 0 ? 0 : Math.Max(0, Math.Min(1, e.GetPosition(SeekLine).X / SeekLine.ActualWidth));

        private void Seek_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_model.CanSeek) return;
            _seeking = true;
            SeekStrip.CaptureMouse();
            ProgressScale.ScaleX = SeekFraction(e);
            e.Handled = true;
        }

        private void Seek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_seeking) ProgressScale.ScaleX = SeekFraction(e);
        }

        private void Seek_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_seeking) return;
            double fraction = SeekFraction(e);
            _seeking = false;
            SeekStrip.ReleaseMouseCapture();
            _host.SeekTo?.Invoke(fraction * 100);
            _model.Tick();
        }

        private void Seek_LostCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_seeking) return;
            _seeking = false; // capture taken away mid-drag: no seek, the line goes back to the song
            ProgressScale.ScaleX = _model.Progress;
        }

        private void Track_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NowPlayingCardTrack track && track.Playable)
            {
                _model.MarkPicked(track); // the marker moves now, not after the old song has faded out
                _host.PlayTrack?.Invoke(track);
            }
        }

        // Right-click one of this game's songs: primary song, Recycle Bin. Radio's queue and Spotify's history aren't
        // this game's songs, so they get no menu.
        private void Track_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is NowPlayingCardTrack track) || track.Path == null
                || _model.Context != NowPlayingCardContext.GameSongs) return;
            e.Handled = true;
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("CardMenu"),
                PlacementTarget = (UIElement)sender,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            menu.Items.Add(CardMenuItem(track.IsPrimary ? "Clear primary song" : "Set as primary song", () =>
            {
                if (track.IsPrimary) _host.ClearPrimarySong?.Invoke();
                else _host.SetPrimarySong?.Invoke(track.Path);
                _model.Refresh();
            }));
            menu.Items.Add(CardMenuItem("Move to Recycle Bin", () => Recycle(track)));
            menu.Opened += (s, a) => _menuOpen = true;
            menu.Closed += (s, a) => { _menuOpen = false; Released?.Invoke(); };
            menu.IsOpen = true;
        }

        private MenuItem CardMenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = (Style)FindResource("CardMenuItem") };
            item.Click += (s, e) => action();
            return item;
        }

        private async void Recycle(NowPlayingCardTrack track)
        {
            bool moved = _host.RecycleSong != null && await _host.RecycleSong(track.Path);
            _model.Refresh();
            _model.ShowNote(moved ? "Moved to Recycle Bin: " + track.Title : "Couldn't move " + track.Title + ", it's in use");
        }

        private static readonly Duration TintFade = new Duration(TimeSpan.FromMilliseconds(900)); // a game change: slow enough to notice, eased

        // The art's colour, toned down so the banner reads as a dark tinted glass rather than a bright swatch
        private bool _glass;

        // The banner's own art-coloured shade (looks with BannerTintAlpha): vivid top left, dark bottom right
        private readonly GradientStop _bannerVivid = new GradientStop(Colors.Transparent, 0);
        private readonly GradientStop _bannerDark = new GradientStop(Colors.Transparent, 1);
        private LinearGradientBrush _bannerTint;

        // Scales a colour's brightness, keeping its hue: by a factor, or (value > 0) to that brightness, so every
        // game's tint lands equally dark however bright its art is
        private static Color Shade(Color c, byte alpha, double factor, double value, double saturation = 1)
        {
            if (value > 0)
            {
                int max = Math.Max(c.R, Math.Max(c.G, c.B));
                factor = max == 0 ? 0 : value * 255 / max;
            }
            double r = c.R * factor, g = c.G * factor, b = c.B * factor;
            if (saturation != 1)
            {
                double grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = grey + (r - grey) * saturation; g = grey + (g - grey) * saturation; b = grey + (b - grey) * saturation;
            }
            return Color.FromArgb(alpha, Channel(r), Channel(g), Channel(b));
        }

        private static byte Channel(double v) => (byte)Math.Max(0, Math.Min(255, v));

        // Fine monochrome noise, tiled; drawn pixel for pixel
        private static readonly ImageBrush GrainBrush = MakeGrain();
        private static ImageBrush MakeGrain()
        {
            const int n = 128;
            var pixels = new byte[n * n * 4];
            var random = new Random(7);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte v = (byte)random.Next(256);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, pixels, n * 4);
            var brush = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile, Stretch = Stretch.None,
                Viewport = new Rect(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }

        private void SizeBackdrop()
        {
            bool whole = BannerLook.Current.WholeCard;
            Backdrop.VerticalAlignment = whole ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            Backdrop.Height = whole ? double.NaN : BannerArea.ActualHeight;
            ListShade.Margin = new Thickness(0, BannerArea.ActualHeight, 0, 0);
        }

        private void ApplyTint()
        {
            var look = BannerLook.Current;
            FadeTo(TintStopVivid, Shade(_model.TintVivid, look.VividAlpha, look.TintShade, look.TintValue));
            FadeTo(TintStopDark, Shade(_model.TintDark, look.DarkAlpha, look.TintShade, look.TintDarkValue));
            if (look.BannerTintAlpha > 0)
            {
                FadeTo(_bannerVivid, Shade(_model.TintVivid, look.BannerTintAlpha, 1, look.BannerTintValue));
                FadeTo(_bannerDark, Shade(_model.TintDark, look.BannerTintAlpha, 1, look.BannerTintDarkValue, look.BannerTintDarkSaturation));
            }
        }

        // Everything but the art bitmaps, which the model rebuilds for the look
        private void ApplyLook()
        {
            var look = BannerLook.Current;
            BannerArt.BeginAnimation(Brush.OpacityProperty, null); // a running fade-in would hold the old value
            BannerArtSharp.BeginAnimation(Brush.OpacityProperty, null);
            BannerArt.Opacity = look.ArtShown;
            BannerArtSharp.Opacity = look.SharpShown;
            MaskMid.Color = Color.FromArgb(look.MaskMidAlpha, 0, 0, 0);
            MaskMid.Offset = look.MaskMidOffset;
            MaskEnd.Offset = look.MaskEndOffset;
            Veil.Fill = new SolidColorBrush(look.Veil);
            Frost.Fill = new SolidColorBrush(look.Frost);
            ListShade.Fill = new SolidColorBrush(look.ListShade);
            if (_bannerTint == null)
                _bannerTint = new LinearGradientBrush(new GradientStopCollection { _bannerVivid, _bannerDark }, new Point(0, 0), new Point(1, 1));
            BannerVeil.Background = look.BannerTintAlpha > 0 ? (Brush)_bannerTint : new SolidColorBrush(look.BannerShade);
            Grain.Fill = GrainBrush;
            Grain.Opacity = look.Grain;
            RimMid.Color = look.RimMid;
            RimEnd.Color = look.RimEnd;
            SizeBackdrop();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (_glass && hwnd != IntPtr.Zero) Common.DialogHelper.EnableBlurBehind(hwnd, look.Glass, acrylic: true);
            Vignette.Center = Vignette.GradientOrigin = new Point(0.5, look.VignetteCentreY);
            Vignette.RadiusX = look.VignetteRadiusX;
            Vignette.RadiusY = look.VignetteRadiusY;
            VignetteClear.Offset = look.VignetteClear;
            VignetteMid.Color = look.VignetteMid;
            VignetteEdge.Color = look.VignetteEdge;
            RimTopLeft.Color = look.RimTopLeft;
            BannerArea.ToolTip = BannerLook.DevIndex == null && !BannerLook.GlassBanner ? null
                : "Look " + look.Name + (BannerLook.GlassBanner ? ", glass banner" : "");
        }

        // Art, Glass, or the no-art look, from the setting and the game
        private void UpdateLook(bool reloadArt)
        {
            var look = BannerLook.Pick(_host.CardShowsArt?.Invoke() == true, _model.HasArt);
            if (look == BannerLook.Current) return;
            BannerLook.Current = look;
            ApplyLook();
            ApplyTint();
            if (reloadArt) _model.ReloadArt();
        }

        // Hidden, for the owner's look work (BannerLook.DevIndex): right-click cycles every look, Ctrl+right-click
        // toggles the glass banner
        private void Banner_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
                BannerLook.GlassBanner = !BannerLook.GlassBanner;
            else
                BannerLook.Next();
            BannerLook.Current = BannerLook.Pick(_host.CardShowsArt?.Invoke() == true, _model.HasArt);
            ApplyLook();
            ApplyTint();
            _model.ReloadArt();
            e.Handled = true;
        }

        private void FadeTo(GradientStop stop, Color to)
        {
            if (!IsVisible) { stop.BeginAnimation(GradientStop.ColorProperty, null); stop.Color = to; return; }
            stop.BeginAnimation(GradientStop.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(to, TintFade)
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            });
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
