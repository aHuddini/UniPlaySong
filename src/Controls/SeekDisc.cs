using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Controls;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Controls
{
    // Transparent overlay registered as "UPS_SeekDisc". Laid over a theme's cover disc, it seeks the current song by
    // turning the disc: drag around it with the mouse, or circle the right stick like a jog wheel. It draws nothing -
    // the theme turns its own disc by binding a rotation to ScrubAngle:
    //   <RotateTransform Angle="{Binding Content.ScrubAngle, ElementName=UPS_SeekDisc}"/>
    public class SeekDisc : PluginUserControl
    {
        public static readonly DependencyProperty ScrubAngleProperty =
            DependencyProperty.Register(nameof(ScrubAngle), typeof(double), typeof(SeekDisc));

        // Degrees the disc has been turned by seeking, in total; a theme adds it to the disc's own spin.
        public double ScrubAngle
        {
            get => (double)GetValue(ScrubAngleProperty);
            set => SetValue(ScrubAngleProperty, value);
        }

        // Discs on screen. While any is, the disc owns the right stick (turning); otherwise timelines use it (tilting).
        internal static int VisibleCount { get; private set; }

        private readonly RelativeSeeker _seeker;
        private readonly RightStick _stick;
        private readonly DispatcherTimer _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        private double? _stickAngle, _dragAngle;
        private bool _counted;

        public SeekDisc(Func<IMusicPlaybackService> playback, IActiveMediaService activeMedia, IPlayniteAPI api)
        {
            _seeker = new RelativeSeeker(playback, activeMedia);
            _stick = RightStick.TryCreate(api, out _);
            Focusable = false;
            Background = Brushes.Transparent; // hit-testable for the mouse, invisible

            // The stick has no events for analog values, so it is polled - only while the disc is on screen.
            _poll.Tick += (s, e) => PollStick();
            IsVisibleChanged += (s, e) =>
            {
                Count(IsVisible);
                if (IsVisible && _stick != null) _poll.Start();
                else { _poll.Stop(); _stickAngle = null; _seeker.Flush(); }
            };
            Unloaded += (s, e) => { Count(false); _poll.Stop(); _seeker.Flush(); };
        }

        private void PollStick()
        {
            if (!_stick.TryRead(out double x, out double y) || Math.Sqrt(x * x + y * y) < SeekDiscMath.StickDeadzone)
            {
                // Let go (or near the centre, where the angle is noise): stop turning and land the seek.
                if (_stickAngle != null) { _stickAngle = null; _seeker.Flush(); }
                return;
            }
            double angle = SeekDiscMath.AngleDegrees(x, y);
            if (_stickAngle != null) Turn(SeekDiscMath.WrapDegrees(angle - _stickAngle.Value));
            _stickAngle = angle;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!_seeker.CanSeek) return;
            _dragAngle = AngleAt(e.GetPosition(this));
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragAngle == null || !IsMouseCaptured) return;
            double angle = AngleAt(e.GetPosition(this));
            Turn(SeekDiscMath.WrapDegrees(angle - _dragAngle.Value));
            _dragAngle = angle;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_dragAngle == null) return;
            _dragAngle = null;
            ReleaseMouseCapture();
            _seeker.Flush();
        }

        private double AngleAt(Point p) => SeekDiscMath.AngleDegrees(p.X - ActualWidth / 2, p.Y - ActualHeight / 2);

        private void Count(bool visible)
        {
            if (visible == _counted) return;
            _counted = visible;
            VisibleCount += visible ? 1 : -1;
        }

        // Clockwise is forward. The disc follows the hand at once; the audio catches up a few times a second.
        private void Turn(double degrees)
        {
            if (degrees == 0 || !_seeker.CanSeek) return;
            ScrubAngle += degrees;
            _seeker.Add(SeekDiscMath.SecondsForDegrees(degrees));
        }
    }

    // The disc's gesture maths, kept apart so it can be tested without WPF.
    internal static class SeekDiscMath
    {
        public const double SecondsPerTurn = 30;   // one full turn of the disc
        public const double StickDeadzone = 0.6;   // nearer the centre, the stick's angle is too noisy to follow

        // Screen convention: y grows downwards, so the angle grows clockwise.
        public static double AngleDegrees(double x, double y) => Math.Atan2(y, x) * 180 / Math.PI;

        // The shortest turn between two readings, -180..180, so crossing the ±180 seam isn't a full turn back.
        public static double WrapDegrees(double degrees)
        {
            degrees %= 360;
            if (degrees > 180) degrees -= 360;
            else if (degrees < -180) degrees += 360;
            return degrees;
        }

        public static double SecondsForDegrees(double degrees) => degrees / 360 * SecondsPerTurn;
    }
}
