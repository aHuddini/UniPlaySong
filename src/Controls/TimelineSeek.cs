using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Controls
{
    // Makes theme timelines seekable with no theme changes: any ProgressBar in Playnite whose value is bound to UPS's
    // ActiveMediaProgress (or to Progress on UPS's own media elements) gets click-to-jump and drag-to-scrub, and while
    // one is on screen, tilting the right stick scrubs (no focus needed). It is one class handler for every ProgressBar;
    // bars bound to anything else are left alone. A seek disc on screen keeps the stick for itself.
    internal static class TimelineSeek
    {
        private static ActiveMediaViewModel _media;
        private static ProgressBar _dragging;
        private static RightStick _stick;
        private static RelativeSeeker _seeker;
        private static readonly List<WeakReference<ProgressBar>> _onScreen = new List<WeakReference<ProgressBar>>();
        private static readonly DispatcherTimer _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        private static DateTime _lastTick;
        private static bool _tilting;
        private static FileLogger _log;

        public static void Register(ActiveMediaViewModel media, Func<IMusicPlaybackService> playback, IActiveMediaService activeMedia,
                                    IPlayniteAPI api, FileLogger log)
        {
            bool first = _media == null;
            _media = media;
            _log = log;
            _seeker = new RelativeSeeker(playback, activeMedia);
            if (!first) return; // class handlers live for the process; register them once
            _stick = RightStick.TryCreate(api, out string why);
            _log?.Debug(_stick != null ? "[Seek] Right stick available for timelines" : $"[Seek] Right stick unavailable: {why}");
            if (_stick != null)
            {
                _poll.Tick += (s, e) => PollStick();
                // Timelines are found by their value changing - a bound timeline updates every second while music plays -
                // rather than by Loaded: themes build their views at different times (Fullscreen's main view even before
                // extensions load), and a value change reaches the class handler whenever and however the bar was built.
                EventManager.RegisterClassHandler(typeof(ProgressBar), RangeBase.ValueChangedEvent,
                    new RoutedPropertyChangedEventHandler<double>(OnValueChanged));
                EventManager.RegisterClassHandler(typeof(ProgressBar), FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
            }
            EventManager.RegisterClassHandler(typeof(ProgressBar), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnDown));
            EventManager.RegisterClassHandler(typeof(ProgressBar), UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnMove));
            EventManager.RegisterClassHandler(typeof(ProgressBar), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnUp));
            EventManager.RegisterClassHandler(typeof(ProgressBar), UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnLostCapture));
            EventManager.RegisterClassHandler(typeof(ProgressBar), UIElement.MouseEnterEvent, new MouseEventHandler(OnEnter));
        }

        // {PluginSettings Plugin=UniPlaySong, Path=ActiveMediaProgress} binds "Settings.ActiveMediaProgress";
        // UPS's media elements bind {Binding Progress} on the shared ActiveMediaViewModel.
        internal static bool IsUpsTimeline(string bindingPath, object dataContext) =>
            bindingPath != null
            && (bindingPath.EndsWith(nameof(UniPlaySongSettings.ActiveMediaProgress), StringComparison.Ordinal)
                || (bindingPath == nameof(ActiveMediaViewModel.Progress) && dataContext is ActiveMediaViewModel));

        private static bool IsUpsTimeline(ProgressBar bar) =>
            IsUpsTimeline(BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty)?.ParentBinding?.Path?.Path, bar.DataContext);

        // Shows the bar is draggable. Only set when the theme left the cursor alone.
        private static void OnEnter(object sender, MouseEventArgs e)
        {
            var bar = (ProgressBar)sender;
            if (bar.ReadLocalValue(FrameworkElement.CursorProperty) == DependencyProperty.UnsetValue && IsUpsTimeline(bar))
                bar.Cursor = Cursors.Hand;
        }

        private static void OnDown(object sender, MouseButtonEventArgs e)
        {
            var bar = (ProgressBar)sender;
            if (!IsUpsTimeline(bar) || bar.ActualWidth <= 0) return;
            _dragging = bar;
            bar.CaptureMouse();
            SeekTo(bar, e);
            e.Handled = true;
        }

        private static void OnMove(object sender, MouseEventArgs e)
        {
            if (sender == _dragging && e.LeftButton == MouseButtonState.Pressed) SeekTo(_dragging, e);
        }

        private static void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (sender != _dragging) return;
            _dragging.ReleaseMouseCapture();
            e.Handled = true;
        }

        private static void OnLostCapture(object sender, MouseEventArgs e)
        {
            if (sender == _dragging) _dragging = null;
        }

        // ── right stick: tilt to scrub, while a UPS timeline is on screen ──
        private static readonly HashSet<string> _seenPaths = new HashSet<string>();

        private static void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var bar = (ProgressBar)sender;
            if (Find(bar) >= 0) return;
            var path = BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty)?.ParentBinding?.Path?.Path ?? "(not bound)";
            if (_seenPaths.Add(path)) _log?.Debug($"[Seek] Progress bar bound to {path}: {(IsUpsTimeline(bar) ? "a UPS timeline" : "not ours")}");
            Track(bar);
        }

        private static void Track(ProgressBar bar)
        {
            if (!IsUpsTimeline(bar) || Find(bar) >= 0) return;
            _onScreen.Add(new WeakReference<ProgressBar>(bar));
            bar.IsVisibleChanged += OnVisibilityChanged;
            _log?.Debug($"[Seek] Tracking a timeline ({_onScreen.Count} tracked, visible={bar.IsVisible})");
            UpdatePolling();
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var bar = (ProgressBar)sender;
            int i = Find(bar);
            if (i < 0) return;
            _onScreen.RemoveAt(i);
            bar.IsVisibleChanged -= OnVisibilityChanged;
            UpdatePolling();
        }

        private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePolling();

        private static int Find(ProgressBar bar) =>
            _onScreen.FindIndex(w => w.TryGetTarget(out var b) && ReferenceEquals(b, bar));

        // The stick is only read while a timeline is visible.
        private static void UpdatePolling()
        {
            _onScreen.RemoveAll(w => !w.TryGetTarget(out _));
            bool visible = _onScreen.Exists(w => w.TryGetTarget(out var b) && b.IsVisible);
            if (visible && !_poll.IsEnabled) { _lastTick = DateTime.UtcNow; _poll.Start(); _log?.Debug("[Seek] Timeline on screen: right stick active"); }
            else if (!visible && _poll.IsEnabled) { _poll.Stop(); _seeker?.Flush(); _log?.Debug("[Seek] No timeline on screen: right stick idle"); }
        }

        private static void PollStick()
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.1, (now - _lastTick).TotalSeconds);
            _lastTick = now;
            double x = 0, y = 0;
            bool read = SeekDisc.VisibleCount == 0 && _stick.TryRead(out x, out y);
            double speed = read ? TimelineSeekMath.TiltSecondsPerSecond(x, y) : 0;
            if (speed == 0)
            {
                if (_tilting) { _tilting = false; _seeker?.Flush(); } // let go: land what's left
                return;
            }
            if (!_tilting)
            {
                _tilting = true;
                _log?.Debug($"[Seek] Tilt x={x:F2} y={y:F2} -> {speed:F1} s/s, canSeek={_seeker?.CanSeek}");
            }
            if (_seeker?.CanSeek == true) _seeker.Add(speed * dt);
        }

        private static void SeekTo(ProgressBar bar, MouseEventArgs e)
        {
            double fraction = Math.Max(0, Math.Min(1, e.GetPosition(bar).X / bar.ActualWidth));
            _media?.RequestSeek(fraction * 100);
        }
    }

    // Tilt-to-scrub speed, kept apart so it can be tested without WPF.
    internal static class TimelineSeekMath
    {
        public const double TiltDeadzone = 0.35;          // a resting or barely nudged stick doesn't seek
        public const double MinSpeed = 4, MaxSpeed = 40;  // seconds of song per second held: gentle tilt .. full tilt

        // Right is forward. Mostly up or down is not a seek; past the dead zone, speed grows with the square of the tilt
        // so small tilts stay precise.
        public static double TiltSecondsPerSecond(double x, double y)
        {
            double ax = Math.Abs(x);
            if (ax < TiltDeadzone || ax < Math.Abs(y)) return 0;
            double f = Math.Min(1, (ax - TiltDeadzone) / (1 - TiltDeadzone));
            return Math.Sign(x) * (MinSpeed + (MaxSpeed - MinSpeed) * f * f);
        }
    }
}
