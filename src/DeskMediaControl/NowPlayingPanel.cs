using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    // "Now Playing" panel with scrolling text and fade edges for the Desktop top panel bar.
    //
    // A title too long for the panel scrolls back and forth. Where the system compositor is available the scroll runs
    // there (CompositionTicker): every refresh at the display's own rate, no per-frame work in Playnite (issue #55).
    // Otherwise it falls back to stepping a transform from CompositionTarget.Rendering. Either way nothing moves while
    // the music is paused, the panel is hidden, Playnite isn't the active window or the mouse is over the title.
    public class NowPlayingPanel : Grid
    {
        private readonly TextBlock _songText;
        private readonly Canvas _scrollCanvas;
        private readonly Rectangle _rightFade;
        private readonly TranslateTransform _textTransform;
        private readonly TranslateTransform _canvasTransform;
        private readonly Action<string> _log;

        private const double PanelMaxWidth = 80;
        private const double ScrollSpeed = 25; // pixels per second
        private const double PauseAtEnds = 1.5; // seconds to pause at each end
        internal const double FadeWidth = 12;
        private const double FadeInDuration = 0.4; // seconds for fade-in
        private const double SlideDistance = 12; // pixels to slide from right
        private const double ScrollEndMargin = 10; // the scroll turns back with the title's end this far inside the edge
        private static readonly Color EdgeColor = Color.FromArgb(200, 10, 14, 30);

        private string _currentText = string.Empty;
        private string _currentBaseText = string.Empty;
        private double _lastTextWidth = 0;
        private bool _isUpdating = false;

        // Scroll animation state
        private bool _isScrolling = false;
        private bool _isScrollPaused = false; // mouse over the title (legacy animation only)
        private double _scrollDistance = 0;
        private double _scrollPosition = 0;
        private int _scrollDirection = -1; // -1 = scrolling left, +1 = scrolling right
        private bool _isPausedAtEnd = false;

        // Fade-in animation state
        private bool _isFadingIn = false;
        private double _fadeElapsed;

        // High-precision timing via Stopwatch
        private readonly Stopwatch _frameStopwatch = new Stopwatch();
        private long _lastFrameTicks;
        private double _pauseElapsed;

        // Rendering subscription state
        private bool _isRenderingSubscribed = false;
        private bool _isAppFocused = true;
        private bool _isPlaying = true;
        private bool _scrollEnabled = true;
        private bool _legacyAnimation; // "Use legacy scroll animation": the WPF loop even where the compositor works

        // Compositor scroll: created on the first title that scrolls; null where unavailable (WPF fallback)
        private CompositionTicker _ticker;
        private bool _tickerTried;
        private bool _useTicker;
        private Rect _tickerPlacement = Rect.Empty;
        private Rect _tickerShown = Rect.Empty;
        private bool _tickerVisible;
        private bool _placeQueued;
        private DispatcherTimer _coverCheck; // rechecks what covers the title (see StartCoverCheck)

        // Embedded progress bar (BelowNowPlaying mode)
        private Rectangle _embeddedProgressTrack;
        private Rectangle _embeddedProgressFill;
        private bool _embeddedProgressEnabled = false;
        private Func<IMusicPlaybackService> _getPlaybackService;
        private TimeSpan _embeddedDuration = TimeSpan.Zero;
        private double _lastEmbeddedFillWidth = -1;
        private DispatcherTimer _embeddedProgressTimer;

        public NowPlayingPanel(Action<string> log = null)
        {
            _log = log;
            Background = Brushes.Transparent;
            ClipToBounds = true;
            Width = PanelMaxWidth;
            Margin = new Thickness(4, 0, 4, 0);

            // Canvas transform for fade-in slide
            _canvasTransform = new TranslateTransform(0, 0);

            _scrollCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                ClipToBounds = true,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _canvasTransform
            };

            // Text transform for scrolling
            _textTransform = new TranslateTransform(0, 0);

            _songText = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.None,
                RenderTransform = _textTransform
            };
            // A moving title keeps its shape in Ideal mode; Display mode, which some themes set, blurs it.
            TextOptions.SetTextFormattingMode(_songText, TextFormattingMode.Ideal);
            Canvas.SetTop(_songText, 0);
            Canvas.SetLeft(_songText, 0);
            _scrollCanvas.Children.Add(_songText);

            _rightFade = CreateFadeRectangle();
            _rightFade.Visibility = Visibility.Collapsed;

            Children.Add(_scrollCanvas);
            Children.Add(_rightFade);

            // Mouse over the title pauses the legacy animation; the compositor's keeps going
            MouseEnter += (s, e) => { _isScrollPaused = true; UpdateMotion(); };
            MouseLeave += (s, e) => { _isScrollPaused = false; UpdateMotion(); };
            IsVisibleChanged += (s, e) => UpdateMotion();
            LayoutUpdated += (s, e) => QueuePlaceTicker();

            // Suspend animation when app loses focus to save GPU
            if (Application.Current != null)
            {
                Application.Current.Activated += OnAppActivated;
                Application.Current.Deactivated += OnAppDeactivated;
            }

            Visibility = Visibility.Collapsed;
            _scrollCanvas.Opacity = 0;
        }

        private void OnAppActivated(object sender, EventArgs e)
        {
            _isAppFocused = true;
            UpdateMotion();
        }

        private void OnAppDeactivated(object sender, EventArgs e)
        {
            _isAppFocused = false;
            UpdateMotion();
        }

        // Music playing (UPS or Spotify): from the top panel's icon refresh.
        public void SetPlaying(bool playing)
        {
            if (playing == _isPlaying) return;
            _isPlaying = playing;
            UpdateMotion();
        }

        // "Scroll long song titles": off shows a long title shortened with an ellipsis instead.
        public void SetScrollEnabled(bool enabled)
        {
            if (enabled == _scrollEnabled) return;
            _scrollEnabled = enabled;
            if (!string.IsNullOrEmpty(_currentText)) ApplyLayout(_lastTextWidth);
        }

        // "Use legacy scroll animation": switches a scrolling title between the compositor and the WPF loop live.
        public void SetLegacyAnimation(bool legacy)
        {
            if (legacy == _legacyAnimation) return;
            _legacyAnimation = legacy;
            if (_isScrolling)
            {
                LeaveTicker();
                StartScroll(_lastTextWidth);
            }
        }

        private bool MayScroll => _isAppFocused && !_isScrollPaused && _isPlaying && IsVisible;
        private bool MayScrollTicker => _isAppFocused && _isPlaying && IsVisible;

        // The song shown changed (null when the panel empties): the top panel item's tooltip follows it.
        public event Action<SongInfo> SongShown;

        // Re-evaluates whether anything should move after a state change.
        private void UpdateMotion()
        {
            if (_useTicker)
            {
                _ticker.SetPaused(!MayScrollTicker);
                PlaceTicker();
                return;
            }
            if (NeedsAnimation()) EnsureRenderingSubscribed();
            else UnsubscribeRendering();
        }

        private Rectangle CreateFadeRectangle()
        {
            var rect = new Rectangle
            {
                Width = FadeWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsHitTestVisible = false
            };

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, EdgeColor.R, EdgeColor.G, EdgeColor.B), 0));
            gradient.GradientStops.Add(new GradientStop(EdgeColor, 1));

            rect.Fill = gradient;
            return rect;
        }

        private void EnsureRenderingSubscribed()
        {
            if (!_isRenderingSubscribed && _isAppFocused)
            {
                _frameStopwatch.Restart();
                _lastFrameTicks = 0;
                CompositionTarget.Rendering += OnRendering;
                _isRenderingSubscribed = true;
            }
        }

        private void UnsubscribeRendering()
        {
            if (_isRenderingSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRenderingSubscribed = false;
                _frameStopwatch.Stop();
            }
        }

        private bool NeedsAnimation()
        {
            if (!_isAppFocused) return false;
            if (_isFadingIn) return true;
            return _isScrolling && !_useTicker && MayScroll;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            // Delta time via Stopwatch — no frame skipping, process every render callback
            long nowTicks = _frameStopwatch.ElapsedTicks;
            double rawDt = (double)(nowTicks - _lastFrameTicks) / Stopwatch.Frequency;
            _lastFrameTicks = nowTicks;

            double dt = rawDt;
            if (dt > 0.1 || dt <= 0.0)
                dt = 0.016;

            // Handle fade-in animation
            if (_isFadingIn)
            {
                _fadeElapsed += dt;
                double progress = Math.Min(1.0, _fadeElapsed / FadeInDuration);

                _scrollCanvas.Opacity = progress;
                _canvasTransform.X = SlideDistance * (1.0 - progress);

                if (progress >= 1.0)
                {
                    _isFadingIn = false;
                    _scrollCanvas.Opacity = 1;
                    _canvasTransform.X = 0;
                }
            }

            // Handle scroll animation (WPF fallback only)
            if (_isScrolling && !_useTicker && MayScroll)
            {
                if (_isPausedAtEnd)
                {
                    _pauseElapsed += dt;
                    if (_pauseElapsed >= PauseAtEnds)
                    {
                        _isPausedAtEnd = false;
                        _scrollDirection = -_scrollDirection;
                    }
                }
                else
                {
                    _scrollPosition += _scrollDirection * ScrollSpeed * dt;

                    if (_scrollPosition <= -_scrollDistance)
                    {
                        _scrollPosition = -_scrollDistance;
                        _isPausedAtEnd = true;
                        _pauseElapsed = 0;
                    }
                    else if (_scrollPosition >= 0)
                    {
                        _scrollPosition = 0;
                        _isPausedAtEnd = true;
                        _pauseElapsed = 0;
                    }

                    _textTransform.X = _scrollPosition;
                }
            }

            // Piggyback: update embedded progress while render loop is already active
            UpdateEmbeddedProgress();

            // Unsubscribe when there's nothing left to animate
            if (!NeedsAnimation())
                UnsubscribeRendering();
        }

        public void UpdateSongInfo(SongInfo songInfo)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess())
            {
                UpdateSongInfoInternal(songInfo);
            }
            else
            {
                // BeginInvoke (async), never Invoke: callers include SMTC/WinRT callback threads that
                // may hold locks the UI thread wants — a sync Invoke there deadlocks (launch freeze).
                dispatcher.BeginInvoke(new Action(() => UpdateSongInfoInternal(songInfo)));
            }
        }

        private void UpdateSongInfoInternal(SongInfo songInfo)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                // Key "nothing playing" on the title, not FilePath: Spotify tracks legitimately have
                // no file but always carry a title, and a truly-stopped state has neither — so a
                // title check blanks correctly for stops while keeping Spotify's now-playing entry.
                if (songInfo == null || string.IsNullOrEmpty(songInfo.Title))
                {
                    if (!string.IsNullOrEmpty(_currentText))
                    {
                        _currentText = string.Empty;
                        _currentBaseText = string.Empty;
                        _songText.Text = string.Empty;
                        Visibility = Visibility.Collapsed;
                        StopScroll();
                        SongShown?.Invoke(null);
                    }
                    return;
                }

                string newText = songInfo.DisplayText;
                string newBaseText = $"{songInfo.Title}|{songInfo.Artist}";

                if (string.Equals(_currentText, newText, StringComparison.Ordinal))
                {
                    return;
                }

                bool sameBaseText = string.Equals(_currentBaseText, newBaseText, StringComparison.Ordinal);
                bool isNewSong = !sameBaseText && !string.IsNullOrEmpty(_currentBaseText);

                _currentText = newText;
                _currentBaseText = newBaseText;
                _songText.Text = newText;
                SongShown?.Invoke(songInfo);
                Visibility = Visibility.Visible;

                // Natural width: measured without the ellipsis limit the scroll toggle may have set
                _songText.MaxWidth = double.PositiveInfinity;
                _songText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = _songText.DesiredSize.Width;

                bool textGotLonger = textWidth > _lastTextWidth + 5;
                if (!sameBaseText || !_isScrolling || textGotLonger)
                {
                    _lastTextWidth = textWidth;
                    ApplyLayout(textWidth);

                    if (isNewSong || _scrollCanvas.Opacity < 1)
                    {
                        PlayFadeIn();
                    }
                }
                else
                {
                    // Same song, text changed (a [Default] tag, say): redraw it where it is
                    ApplyLayout(_lastTextWidth);
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        // Scroll a title wider than the panel, or show it whole / shortened with an ellipsis.
        private void ApplyLayout(double textWidth)
        {
            bool scroll = textWidth > PanelMaxWidth && _scrollEnabled;
            _songText.MaxWidth = scroll ? double.PositiveInfinity : PanelMaxWidth;
            _songText.TextTrimming = scroll ? TextTrimming.None : TextTrimming.CharacterEllipsis;

            if (scroll)
            {
                StartScroll(textWidth);
            }
            else
            {
                StopScroll();
                _rightFade.Visibility = Visibility.Collapsed;
            }
        }

        private void PlayFadeIn()
        {
            if (_useTicker)
            {
                _scrollCanvas.Opacity = 1;
                _ticker.FadeIn(SlideDistance, TimeSpan.FromSeconds(FadeInDuration), VisualTreeHelper.GetDpi(this).DpiScaleX);
                return;
            }
            _isFadingIn = true;
            _fadeElapsed = 0;
            _scrollCanvas.Opacity = 0;
            _canvasTransform.X = SlideDistance;
            EnsureRenderingSubscribed();
        }

        private void StartScroll(double textWidth)
        {
            _scrollDistance = textWidth - PanelMaxWidth + ScrollEndMargin;
            _scrollPosition = 0;
            _scrollDirection = -1;
            _isPausedAtEnd = true;
            _pauseElapsed = 0;
            _isScrolling = true;
            _textTransform.X = 0;

            if (EnsureTicker())
            {
                try
                {
                    double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    _ticker.SetText(_songText.Text,
                        new Typeface(_songText.FontFamily, _songText.FontStyle, _songText.FontWeight, _songText.FontStretch),
                        _songText.FontSize, _songText.Foreground, _scrollCanvas.Height, scale);
                    _ticker.StartScroll(_scrollDistance, ScrollSpeed, PauseAtEnds, scale);
                    _useTicker = true;
                    _songText.Opacity = 0; // the ticker draws the title; this copy keeps the layout and theme styling
                    _rightFade.Visibility = Visibility.Collapsed;
                    _tickerPlacement = Rect.Empty;
                    _tickerShown = Rect.Empty;
                    _tickerVisible = false;
                    StartCoverCheck();
                    UpdateMotion();
                    return;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"NowPlaying: compositor ticker failed, using WPF animation ({ex.Message})");
                    _ticker = null;
                    LeaveTicker();
                }
            }

            _rightFade.Visibility = Visibility.Visible;
            UpdateMotion();
        }

        private void StopScroll()
        {
            _isScrolling = false;
            _isPausedAtEnd = false;
            _scrollPosition = 0;
            _textTransform.X = 0;
            LeaveTicker();
            if (!NeedsAnimation())
                UnsubscribeRendering();
        }

        private void LeaveTicker()
        {
            if (!_useTicker) return;
            _useTicker = false;
            _coverCheck?.Stop();
            _ticker?.StopScroll();
            _ticker?.Hide();
            _tickerVisible = false;
            _songText.Opacity = 1;
        }

        private bool EnsureTicker()
        {
            if (_legacyAnimation) return false;
            if (_ticker != null) return true;
            if (_tickerTried) return false;
            var window = Window.GetWindow(this);
            if (window == null) return false; // not in Playnite's window yet: try again on the next title
            _tickerTried = true;
            _ticker = CompositionTicker.TryCreate(window, _log);
            return _ticker != null;
        }

        // Something can cover the title without a layout pass: Playnite's dim overlay behind a dialog, a theme's
        // pulsing button. While the compositor draws the title, recheck what covers it twice a second (hit tests only;
        // the compositor is only told about a change).
        private void StartCoverCheck()
        {
            if (_coverCheck == null)
            {
                _coverCheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
                _coverCheck.Tick += (s, e) => PlaceTicker();
            }
            _coverCheck.Start();
        }

        // LayoutUpdated fires for every layout pass in the window: work out the placement once, after the burst.
        private void QueuePlaceTicker()
        {
            if (!_useTicker || _placeQueued) return;
            _placeQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _placeQueued = false; PlaceTicker(); }), DispatcherPriority.Background);
        }

        // Keeps the compositor layer over exactly the part of the title WPF would show. The layer draws above all WPF
        // content, so it is trimmed to (1) every clip WPF applies on the way up, the theme's layout clipping included,
        // and (2) the run of the title strip that hit-tests to this panel: a button the theme draws over the right
        // end (Playnite's notification bell, say) ends the visible part there. Only talks to the compositor on change.
        private void PlaceTicker()
        {
            if (!_useTicker) return;
            var window = Window.GetWindow(this);
            if (window == null) return;
            bool visible = IsVisible;
            Rect place = _tickerPlacement, shown = _tickerShown;
            try
            {
                place = _scrollCanvas.TransformToAncestor(window)
                    .TransformBounds(new Rect(0, 0, _scrollCanvas.ActualWidth, _scrollCanvas.ActualHeight));
                shown = ShownPart(window, place);
            }
            catch (InvalidOperationException)
            {
                visible = false; // not connected to the window right now
            }
            if (place == _tickerPlacement && shown == _tickerShown && visible == _tickerVisible) return;
            _tickerPlacement = place;
            _tickerShown = shown;
            _tickerVisible = visible;
            if (place.IsEmpty) return;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _ticker.SetPlacement(place.TopLeft, shown.IsEmpty ? new Rect() : shown, scale, visible, EdgeColor);

            // Scroll far enough for the title's end to clear the visible part, not the panel's full width: a theme
            // that trims the panel or draws a button over its end would otherwise turn the scroll back early.
            double visibleWidth = shown.IsEmpty ? PanelMaxWidth : Math.Max(0, shown.Right - place.Left);
            double distance = Math.Max(0, _lastTextWidth - visibleWidth + ScrollEndMargin);
            if (Math.Abs(distance - _scrollDistance) > 0.5)
            {
                _scrollDistance = distance;
                _ticker.StartScroll(distance, ScrollSpeed, PauseAtEnds, scale);
            }
        }

        private Rect ShownPart(Window window, Rect place)
        {
            var shown = place;
            for (DependencyObject node = _scrollCanvas; node != null && node != window; node = VisualTreeHelper.GetParent(node))
            {
                if (!(node is Visual visual)) continue;
                var clip = VisualTreeHelper.GetClip(visual); // Clip, ClipToBounds and layout clipping alike
                if (clip == null) continue;
                shown.Intersect(visual.TransformToAncestor(window).TransformBounds(clip.Bounds));
                if (shown.IsEmpty) return shown;
            }

            // Walk the middle of the strip left to right; the first point owned by something else ends it.
            double y = shown.Top + shown.Height / 2;
            for (double x = shown.Left + 1; x < shown.Right; x += 2)
            {
                var hit = VisualTreeHelper.HitTest(window, new Point(x, y))?.VisualHit;
                if (hit != null && hit != this && !IsAncestorOf(hit))
                {
                    shown.Width = Math.Max(0, x - 1 - shown.Left);
                    break;
                }
            }
            return shown;
        }

        // A move to a monitor with another scale: redraw the title at the new resolution.
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            if (_useTicker)
            {
                LeaveTicker();
                StartScroll(_lastTextWidth);
            }
        }

        public void Clear()
        {
            UpdateSongInfo(SongInfo.Empty);
        }

        // Enables the embedded progress bar below the scrolling text.
        // Called once during initialization when ProgressBarPosition is BelowNowPlaying.
        // Uses a 1-second DispatcherTimer — decoupled from the 60fps scroll/fade render loop.
        public void EnableEmbeddedProgressBar(Func<IMusicPlaybackService> getPlaybackService)
        {
            _getPlaybackService = getPlaybackService;
            _embeddedProgressEnabled = true;

            // Add a second row for the progress bar
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) }); // text row
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });  // bar row

            Grid.SetRow(_scrollCanvas, 0);
            Grid.SetRow(_rightFade, 0);

            _embeddedProgressTrack = new Rectangle
            {
                Width = PanelMaxWidth,
                Height = 3,
                Fill = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                RadiusX = 1.5,
                RadiusY = 1.5
            };
            Grid.SetRow(_embeddedProgressTrack, 1);
            Children.Add(_embeddedProgressTrack);

            _embeddedProgressFill = new Rectangle
            {
                Width = 0,
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Left,
                Fill = new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)),
                RadiusX = 1.5,
                RadiusY = 1.5
            };
            Grid.SetRow(_embeddedProgressFill, 1);
            Children.Add(_embeddedProgressFill);

            _embeddedProgressTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _embeddedProgressTimer.Tick += (s, e) => UpdateEmbeddedProgress();
            _embeddedProgressTimer.Start();
        }

        // Updates embedded progress bar position (called from 1-second DispatcherTimer)
        private void UpdateEmbeddedProgress()
        {
            if (!_embeddedProgressEnabled || _getPlaybackService == null) return;

            var playbackService = _getPlaybackService.Invoke();
            var currentTime = playbackService?.CurrentTime;

            if (!currentTime.HasValue || _embeddedDuration.TotalSeconds <= 0)
            {
                if (_lastEmbeddedFillWidth != 0)
                {
                    _embeddedProgressFill.Width = 0;
                    _lastEmbeddedFillWidth = 0;
                }
                return;
            }

            double progress = Math.Min(1.0, Math.Max(0.0,
                currentTime.Value.TotalSeconds / _embeddedDuration.TotalSeconds));
            double fillWidth = Math.Round(progress * PanelMaxWidth, 1);

            if (Math.Abs(fillWidth - _lastEmbeddedFillWidth) >= 0.2)
            {
                _embeddedProgressFill.Width = fillWidth;
                _lastEmbeddedFillWidth = fillWidth;
            }
        }

        // Resets the embedded progress bar to zero (called on song change to avoid stale position)
        public void ResetEmbeddedProgress()
        {
            if (!_embeddedProgressEnabled || _embeddedProgressFill == null) return;
            _embeddedProgressFill.Width = 0;
            _lastEmbeddedFillWidth = 0;
            _embeddedDuration = TimeSpan.Zero; // Zero duration stops updates until SetEmbeddedDuration is called with new song
        }

        // Sets the song duration for embedded progress tracking (called on song change)
        public void SetEmbeddedDuration(TimeSpan duration)
        {
            _embeddedDuration = duration;
            _lastEmbeddedFillWidth = -1;
        }
    }
}
