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
        private const uint GlassGradient = 0x992C201C; // AABBGGRR: #1C202C at 60%, the blur's tint
        private const double GapBelowPanel = 0; // flush with the edge the view model found

        private static readonly Brush GlassTint = Frozen(Color.FromArgb(0x01, 0x1C, 0x20, 0x2C));   // the blur tints; this keeps hit-testing
        private static readonly Brush SolidTint = Frozen(Color.FromArgb(0xF0, 0x16, 0x1A, 0x24));   // no acrylic
        private static readonly Brush CalmOnBrush = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));

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

        public void HideCard()
        {
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
            if (e.PropertyName == nameof(NowPlayingCardModel.Progress))
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
            else if (e.PropertyName == nameof(NowPlayingCardModel.Banner))
            {
                // New art fades in rather than popping
                if (IsVisible)
                    BannerArt.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, BannerArtOpacity, TintFade));
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.TintVivid))
            {
                // Mica-style tint: vivid colour at the top left, the darker one at the bottom right; a short fade
                FadeTo(TintStopVivid, Darken(_model.TintVivid, 0x8C));
                FadeTo(TintStopDark, Darken(_model.TintDark, 0xB3));
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.IsPlaying))
            {
                PlayPauseButton.Tag = FindResource(_model.IsPlaying ? "IconPause" : "IconPlay");
                PlayPauseButton.ToolTip = _model.IsPlaying ? "Pause" : "Play";
            }
            else if (e.PropertyName == nameof(NowPlayingCardModel.CalmDownOn))
            {
                CalmDownButton.Foreground = _model.CalmDownOn ? CalmOnBrush : (Brush)Brushes.White;
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
                glass = Common.DialogHelper.EnableBlurBehind(hwnd, GlassGradient, acrylic: true);
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

        private static readonly Duration TintFade = new Duration(TimeSpan.FromMilliseconds(600));
        private const double BannerArtOpacity = 0.2; // the art shows through the tint, faintly

        // The art's colour, toned down so the banner reads as a dark tinted glass rather than a bright swatch
        private const double TintShade = 0.65;
        private static Color Darken(Color c, byte alpha) =>
            Color.FromArgb(alpha, (byte)(c.R * TintShade), (byte)(c.G * TintShade), (byte)(c.B * TintShade));

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
