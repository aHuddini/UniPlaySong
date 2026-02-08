using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    // "Now Playing" panel with scrolling text and fade edges for the Desktop top panel bar.
    // Uses CompositionTarget.Rendering for frame-based animation; only hooks when active to avoid GPU waste.
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
        private DateTime _pauseStartTime;
        private bool _isPausedAtEnd = false;
        private DateTime _lastFrameTime;

        // Fade-in animation state
        private bool _isFadingIn = false;
        private DateTime _fadeStartTime;

        // Render hook management — only subscribe when animating to avoid GPU waste
        private bool _isRenderHooked = false;
        private bool _isAppFocused = true;

        public NowPlayingPanel()
        {
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
                // Re-hook if scrolling was active while paused
                if (_isScrolling)
                    EnsureRenderHooked();
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
            // Resume animation if there's something to animate
            if (_isScrolling || _isFadingIn)
            {
                _lastFrameTime = DateTime.UtcNow;
                // Push pause start forward so we don't skip through the pause
                if (_isPausedAtEnd)
                    _pauseStartTime = DateTime.UtcNow;
                if (_isFadingIn)
                    _fadeStartTime = DateTime.UtcNow;
                EnsureRenderHooked();
            }
        }

        private void OnAppDeactivated(object sender, EventArgs e)
        {
            _isAppFocused = false;
            UnhookRender();
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

        private void EnsureRenderHooked()
        {
            if (!_isRenderHooked && _isAppFocused)
            {
                _lastFrameTime = DateTime.UtcNow;
                CompositionTarget.Rendering += OnRenderFrame;
                _isRenderHooked = true;
            }
        }

        private void UnhookRender()
        {
            if (_isRenderHooked)
            {
                CompositionTarget.Rendering -= OnRenderFrame;
                _isRenderHooked = false;
            }
        }

        // Returns true if anything needs the render loop running
        private bool NeedsRendering()
        {
            if (!_isAppFocused) return false;
            if (_isFadingIn) return true;
            if (_isScrolling && !_isScrollPaused) return true;
            return false;
        }

        private void OnRenderFrame(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;

            // Handle fade-in animation
            if (_isFadingIn)
            {
                double elapsed = (now - _fadeStartTime).TotalSeconds;
                double progress = Math.Min(1.0, elapsed / FadeInDuration);

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
                double deltaTime = (now - _lastFrameTime).TotalSeconds;

                // Clamp delta to avoid huge jumps if frame was delayed
                if (deltaTime > 0.1) deltaTime = 0.016; // ~60fps fallback

                if (_isPausedAtEnd)
                {
                    // Check if pause duration has elapsed
                    if ((now - _pauseStartTime).TotalSeconds >= PauseAtEnds)
                    {
                        _isPausedAtEnd = false;
                        // Reverse direction
                        _scrollDirection = -_scrollDirection;
                    }
                }
                else
                {
                    // Move the scroll position
                    _scrollPosition += _scrollDirection * ScrollSpeed * deltaTime;

                    // Check bounds
                    if (_scrollPosition <= -_scrollDistance)
                    {
                        _scrollPosition = -_scrollDistance;
                        _isPausedAtEnd = true;
                        _pauseStartTime = now;
                    }
                    else if (_scrollPosition >= 0)
                    {
                        _scrollPosition = 0;
                        _isPausedAtEnd = true;
                        _pauseStartTime = now;
                    }

                    _textTransform.X = _scrollPosition;
                }
            }

            _lastFrameTime = now;

            // Unhook when there's nothing left to animate
            if (!NeedsRendering())
                UnhookRender();
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
            _fadeStartTime = DateTime.UtcNow;
            _scrollCanvas.Opacity = 0;
            _canvasTransform.X = SlideDistance;
            EnsureRenderHooked();
        }

        private void StartScroll(double textWidth)
        {
            _scrollDistance = textWidth - PanelMaxWidth + 10;
            _scrollPosition = 0;
            _scrollDirection = -1; // Start by scrolling left
            _isPausedAtEnd = true; // Start with initial pause
            _pauseStartTime = DateTime.UtcNow;
            _isScrolling = true;
            _textTransform.X = 0;
            _rightFade.Visibility = Visibility.Visible;
            EnsureRenderHooked();
        }

        private void StopScroll()
        {
            _isScrolling = false;
            _isPausedAtEnd = false;
            _scrollPosition = 0;
            _textTransform.X = 0;
            if (!NeedsRendering())
                UnhookRender();
        }

        public void Clear()
        {
            UpdateSongInfo(SongInfo.Empty);
        }
    }
}
