using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    // Thin progress bar showing song playback position in the Desktop top panel.
    // Uses a 1-second DispatcherTimer instead of CompositionTarget.Rendering —
    // a 50px bar on a 3-minute song moves ~0.28px/sec, so 1Hz updates are sufficient.
    public class SongProgressBar : Canvas
    {
        private readonly Rectangle _trackBackground;
        private readonly Rectangle _trackFill;
        private readonly Func<IMusicPlaybackService> _getPlaybackService;
        private readonly DispatcherTimer _updateTimer;

        private const double BarWidth = 50;
        private const double BarHeight = 4;

        // Duration from SongMetadataService (set externally on song change)
        private TimeSpan _currentDuration = TimeSpan.Zero;
        private double _lastFillWidth = -1;
        private bool _isActive = false;

        public SongProgressBar(Func<IMusicPlaybackService> getPlaybackService)
        {
            _getPlaybackService = getPlaybackService ?? throw new ArgumentNullException(nameof(getPlaybackService));

            Width = BarWidth;
            Height = BarHeight;
            Margin = new Thickness(4, 0, 4, 0);
            VerticalAlignment = VerticalAlignment.Center;
            ClipToBounds = true;

            _trackBackground = new Rectangle
            {
                Width = BarWidth,
                Height = BarHeight,
                Fill = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), // ~15% white
                RadiusX = 2,
                RadiusY = 2
            };
            SetTop(_trackBackground, 0);
            SetLeft(_trackBackground, 0);
            Children.Add(_trackBackground);

            _trackFill = new Rectangle
            {
                Width = 0,
                Height = BarHeight,
                Fill = new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), // ~60% white
                RadiusX = 2,
                RadiusY = 2
            };
            SetTop(_trackFill, 0);
            SetLeft(_trackFill, 0);
            Children.Add(_trackFill);

            _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += OnTimerTick;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (active)
                _updateTimer.Start();
            else
                _updateTimer.Stop();
        }

        public void SetDuration(TimeSpan duration)
        {
            _currentDuration = duration;
        }

        public void Reset()
        {
            _currentDuration = TimeSpan.Zero;
            _lastFillWidth = -1;
            _trackFill.Width = 0;
            _updateTimer.Stop(); // Pause polling until new song starts (avoids stale CurrentTime during crossfade)
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_isActive)
            {
                _updateTimer.Stop();
                return;
            }

            var playbackService = _getPlaybackService?.Invoke();
            if (playbackService == null) return;

            var currentTime = playbackService.CurrentTime;
            if (!currentTime.HasValue || _currentDuration.TotalSeconds <= 0)
            {
                if (_lastFillWidth != 0)
                {
                    _trackFill.Width = 0;
                    _lastFillWidth = 0;
                }
                return;
            }

            double progress = Math.Min(1.0, Math.Max(0.0,
                currentTime.Value.TotalSeconds / _currentDuration.TotalSeconds));
            double fillWidth = Math.Round(progress * BarWidth, 1);

            // Dirty check: only update if pixel position changed
            if (Math.Abs(fillWidth - _lastFillWidth) >= 0.2)
            {
                _trackFill.Width = fillWidth;
                _lastFillWidth = fillWidth;
            }
        }
    }
}
