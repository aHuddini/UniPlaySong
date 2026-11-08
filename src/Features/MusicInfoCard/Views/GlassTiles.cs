using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // Frosted-glass tiles: every Border marked Tag="Glass" is painted with a more-blurred copy of the card's backdrop
    // (MusicInfoCardHandler builds it), lined up with the backdrop behind the tile, so the tile reads as frosted glass
    // over the card - a CSS backdrop-filter done with a pre-blurred image instead of a live effect. Re-aligned when the
    // stats scroll or the card resizes. Without a backdrop the tiles keep their plain XAML fill.
    internal static class GlassTiles
    {
        public static void Attach(Image backdrop, FrameworkElement statsRoot, ScrollViewer scroll, BitmapSource frost)
        {
            if (frost == null || backdrop?.Source == null) return;

            var tiles = Find(statsRoot)
                .Select(tile =>
                {
                    var brush = new ImageBrush(frost) { ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
                    tile.Background = brush;
                    return (Tile: tile, Brush: brush);
                })
                .ToList();
            if (tiles.Count == 0) return;

            void Align()
            {
                var src = backdrop.Source;
                double w = backdrop.ActualWidth, h = backdrop.ActualHeight;
                if (w <= 0 || h <= 0 || src.Width <= 0 || src.Height <= 0) return;

                // The backdrop is UniformToFill and centred: the picture's own rect, in the Image's coordinates.
                double scale = Math.Max(w / src.Width, h / src.Height);
                double pw = src.Width * scale, ph = src.Height * scale;
                var origin = new Point((w - pw) / 2, (h - ph) / 2);

                foreach (var (tile, brush) in tiles)
                {
                    if (!tile.IsVisible) continue;
                    var p = backdrop.TranslatePoint(origin, tile);
                    brush.Viewport = new Rect(p.X, p.Y, pw, ph);
                }
            }

            scroll.ScrollChanged += (s, e) => Align();   // also fires when the stats arrive and the extent grows
            backdrop.SizeChanged += (s, e) => Align();
            foreach (var (tile, _) in tiles) tile.IsVisibleChanged += (s, e) => Align();
            Align();
        }

        private static IEnumerable<Border> Find(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                if (child is Border b && "Glass".Equals(b.Tag)) yield return b;
                foreach (var nested in Find(child)) yield return nested;
            }
        }
    }
}
