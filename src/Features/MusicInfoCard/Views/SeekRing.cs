using System;
using System.Windows;
using System.Windows.Media;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // The ring around the Music Info Card's play/pause button: a faint track, an accent arc clockwise from 12 o'clock to the
    // song's progress, and - while play/pause is the live control - a soft glow and a knob at the arc's end that shows the
    // right stick turns it. No tick marks (owner).
    public sealed class SeekRing : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress), typeof(double), typeof(SeekRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LiveProperty = DependencyProperty.Register(
            nameof(Live), typeof(bool), typeof(SeekRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
            nameof(Accent), typeof(Brush), typeof(SeekRing), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }  // 0..1
        public bool Live { get => (bool)GetValue(LiveProperty); set => SetValue(LiveProperty, value); }
        public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

        private const double Thickness = 4;
        private const double KnobRadius = 5;
        private static readonly Pen TrackPen = FrozenPen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), Thickness);

        protected override void OnRender(DrawingContext dc)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = Math.Min(ActualWidth, ActualHeight) / 2 - KnobRadius - 1;
            if (r <= 0) return;

            if (Live) dc.DrawEllipse(null, new Pen(WithOpacity(Accent, 0.28), Thickness + 6), center, r, r);   // glow
            dc.DrawEllipse(null, TrackPen, center, r, r);

            double p = Math.Max(0, Math.Min(1, Progress));
            if (p > 0)
            {
                var arc = new Pen(Accent, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (p >= 0.9999) dc.DrawEllipse(null, arc, center, r, r);
                else dc.DrawGeometry(null, arc, Arc(center, r, p));
            }

            if (Live)
            {
                var end = PointAt(center, r, p);
                dc.DrawEllipse(Brushes.White, new Pen(Accent, 2), end, KnobRadius, KnobRadius);
            }
        }

        // Clockwise from 12 o'clock to the given fraction of a turn
        private static Geometry Arc(Point c, double r, double fraction)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(PointAt(c, r, 0), false, false);
                ctx.ArcTo(PointAt(c, r, fraction), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private static Point PointAt(Point c, double r, double fraction)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * fraction;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }

        private static Brush WithOpacity(Brush brush, double opacity)
        {
            var b = brush.Clone();
            b.Opacity = opacity;
            b.Freeze();
            return b;
        }

        private static Pen FrozenPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
