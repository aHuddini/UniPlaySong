using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // The Details page's small charts, drawn with plain WPF shapes (no chart library). Each returns a ready element so a
    // new tile can reuse them.
    internal static class CardCharts
    {
        private static readonly Brush CentreText = Brushes.White;

        // A ring of slices clockwise from 12 o'clock, a number in the middle
        public static FrameworkElement Donut(IReadOnlyList<(double Value, Brush Brush)> slices, string centre, double size)
        {
            const double stroke = 10;
            var grid = new Grid { Width = size, Height = size };
            double total = slices?.Sum(s => s.Value) ?? 0;
            double r = (size - stroke) / 2;
            var c = new Point(size / 2, size / 2);
            if (total > 0)
            {
                double start = 0;
                foreach (var (value, brush) in slices.Where(s => s.Value > 0))
                {
                    double frac = value / total;
                    var path = new Path { Stroke = brush, StrokeThickness = stroke };
                    path.Data = frac >= 0.9999
                        ? (Geometry)new EllipseGeometry(c, r, r)
                        : ArcGeometry(c, r, start, frac);
                    grid.Children.Add(path);
                    start += frac;
                }
            }
            grid.Children.Add(new TextBlock
            {
                Text = centre, Foreground = CentreText, FontSize = size * 0.2, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            return grid;
        }

        // One bar per song, scaled to the longest; the longest and shortest coloured apart
        public static FrameworkElement LengthBars(IReadOnlyList<TimeSpan> lengths, Brush normal, Brush longest, Brush shortest, double height)
        {
            var grid = new Grid { Height = height };
            if (lengths == null || lengths.Count == 0) return grid;
            double max = lengths.Max(l => l.TotalSeconds);
            if (max <= 0) return grid;
            int longestAt = IndexOf(lengths, lengths.Max());
            int shortestAt = IndexOf(lengths, lengths.Min());
            for (int i = 0; i < lengths.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var bar = new Border
                {
                    Height = Math.Max(2, height * lengths[i].TotalSeconds / max),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    Background = i == longestAt ? longest : i == shortestAt && lengths.Count > 1 ? shortest : normal
                };
                Grid.SetColumn(bar, i);
                grid.Children.Add(bar);
            }
            return grid;
        }

        private static int IndexOf(IReadOnlyList<TimeSpan> list, TimeSpan value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }

        private static Geometry ArcGeometry(Point c, double r, double startFrac, double frac)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(At(c, r, startFrac), false, false);
                ctx.ArcTo(At(c, r, startFrac + frac), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private static Point At(Point c, double r, double frac)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * frac;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
    }
}
