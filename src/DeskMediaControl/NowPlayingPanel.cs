using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    // "Now Playing" panel with scrolling text and fade edges for the Desktop top panel bar.
    // Uses Timeline.DesiredFrameRate=60 to cap WPF's render rate globally, then processes
    // every CompositionTarget.Rendering callback for smooth, consistent animation. #55
    public class NowPlayingPanel : Grid
    {
        private readonly TextBlock _songText;
        private readonly Canvas _scrollCanvas;
        private readonly Rectangle _rightFade;
        private readonly TranslateTransform _textTransform;
        private readonly TranslateTransform _canvasTransform;

        private const double PanelMaxWidth = 80;
        private const double ScrollSpeed = 25; // pixels per second
        private const double PauseAtEnds = 1.5; // seconds to pause at each end
        private const double FadeWidth = 12;
        private const double FadeInDuration = 0.4; // seconds for fade-in
        private const double SlideDistance = 12; // pixels to slide from right

        private string _currentText = string.Empty;
        private string _currentBaseText = string.Empty;
        private double _lastTextWidth = 0;
        private bool _isUpdating = false;

        // Scroll animation state
        private bool _isScrolling = false;
        private bool _isScrollPaused = false;
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

        // Embedded progress bar (BelowNowPlaying mode)
        private Rectangle _embeddedProgressTrack;
        private Rectangle _embeddedProgressFill;
        private bool _embeddedProgressEnabled = false;
        private Func<IMusicPlaybackService> _getPlaybackService;
        private TimeSpan _embeddedDuration = TimeSpan.Zero;
        private double _lastEmbeddedFillWidth = -1;
        private DispatcherTimer _embeddedProgressTimer;

        public NowPlayingPanel()
        {
            Background = Brushes.Transparent;
            ClipToBounds = true;
            Width = PanelMaxWidth;
            Margin = new Thickness(4, 0, 4, 0);

            // Cap WPF's global render rate at 60fps — reduces CompositionTarget.Rendering
            // frequency on high-Hz displays (e.g. 165Hz → 60Hz), saving GPU
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata(60));

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
            Canvas.SetTop(_songText, 0);
            Canvas.SetLeft(_songText, 0);
            _scrollCanvas.Children.Add(_songText);

            _rightFade = CreateFadeRectangle();
            _rightFade.Visibility = Visibility.Collapsed;

            Children.Add(_scrollCanvas);
            Children.Add(_rightFade);

            // Mouse events for pause on hover
            MouseEnter += (s, e) => _isScrollPaused = true;
            MouseLeave += (s, e) =>
            {
                _isScrollPaused = false;
                if (_isScrolling)
                    EnsureRenderingSubscribed();
            };

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
            if (_isScrolling || _isFadingIn)
            {
                _frameStopwatch.Restart();
                _lastFrameTicks = 0;
                EnsureRenderingSubscribed();
            }
        }

        private void OnAppDeactivated(object sender, EventArgs e)
        {
            _isAppFocused = false;
            UnsubscribeRendering();
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
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 10, 14, 30), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(200, 10, 14, 30), 1));

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
            if (_isScrolling && !_isScrollPaused) return true;
            return false;
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

            // Handle scroll animation
            if (_isScrolling && !_isScrollPaused)
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
                dispatcher.Invoke(() => UpdateSongInfoInternal(songInfo));
            }
        }

        private void UpdateSongInfoInternal(SongInfo songInfo)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                if (songInfo == null || songInfo.IsEmpty)
                {
                    if (!string.IsNullOrEmpty(_currentText))
                    {
                        _currentText = string.Empty;
                        _currentBaseText = string.Empty;
                        _songText.Text = string.Empty;
                        Visibility = Visibility.Collapsed;
                        StopScroll();
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
                Visibility = Visibility.Visible;

                _songText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = _songText.DesiredSize.Width;

                bool textGotLonger = textWidth > _lastTextWidth + 5;
                if (!sameBaseText || !_isScrolling || textGotLonger)
                {
                    _lastTextWidth = textWidth;

                    if (textWidth > PanelMaxWidth)
                    {
                        StartScroll(textWidth);
                    }
                    else
                    {
                        StopScroll();
                        _rightFade.Visibility = Visibility.Collapsed;
                    }

                    if (isNewSong || _scrollCanvas.Opacity < 1)
                    {
                        PlayFadeIn();
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void PlayFadeIn()
        {
            _isFadingIn = true;
            _fadeElapsed = 0;
            _scrollCanvas.Opacity = 0;
            _canvasTransform.X = SlideDistance;
            EnsureRenderingSubscribed();
        }

        private void StartScroll(double textWidth)
        {
            _scrollDistance = textWidth - PanelMaxWidth + 10;
            _scrollPosition = 0;
            _scrollDirection = -1;
            _isPausedAtEnd = true;
            _pauseElapsed = 0;
            _isScrolling = true;
            _textTransform.X = 0;
            _rightFade.Visibility = Visibility.Visible;
            EnsureRenderingSubscribed();
        }

        private void StopScroll()
        {
            _isScrolling = false;
            _isPausedAtEnd = false;
            _scrollPosition = 0;
            _textTransform.X = 0;
            if (!NeedsAnimation())
                UnsubscribeRendering();
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
