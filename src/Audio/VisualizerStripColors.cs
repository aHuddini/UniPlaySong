using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace UniPlaySong.Audio
{
    // The music player's spectrum strip's two colours (UPS_MusicPlayerControlVisual, UPS_MusicVisualStrip): what the eye
    // reads in the cover, nothing boosted. Every pixel with colour in it (not black, white or grey) goes into a 24-bin hue
    // histogram weighted saturation x brightness^2 - bright colour is what the eye picks up. The strongest family is one
    // colour, the strongest at least 60 degrees from it the other; with no real second family (under 12% of the first)
    // the cover is one-hue and gets darker-to-brighter shades of it. A family's colour is its brightest third (Mass
    // Effect's warm family is a bright orange glow over more numerous dark reds - the eye reads the glow), at the
    // saturation the cover has: only brightness is lifted, so the bars read solid (the renderer draws a pixel only as
    // opaque as its brightest channel). A cover with little vivid colour is toned toward neutral: A Plague Tale (white and
    // grey, dim navy and skin) goes near-white; Quake III (a white cover with a small vivid red logo) keeps its red.
    // Boosting saturation, as the first version did, invented colours a cover doesn't have and made every pair harsh.
    internal static class VisualizerStripColors
    {
        private const int Bins = 24;                           // 15 degrees a bin
        private const double MinV = 0.15, MinS = 0.20;         // below: black, or grey/white - no hue to speak of
        private const int Apart = 4;                           // bins: the second family is at least 60 degrees from the first
        private const double SecondMin = 0.12;                 // of the first family's weight, else the cover counts as one-hue
        private const double LiftV = 0.85;
        private const double VividNone = 0.002, VividFull = 0.006;   // the cover's vivid weight per pixel: neutral below, full colour above

        public static (Color foot, Color top) FromFile(string path) => FromPixels(VisualizerCoverPalette.Pixels(path));

        internal static (Color foot, Color top) FromPixels(byte[] bgra)
        {
            int n = bgra.Length / 4;
            var px = new List<double[]>();   // hue, saturation, value, weight
            var hist = new double[Bins];
            for (int i = 0; i < n; i++)
            {
                double r = bgra[i * 4 + 2] / 255.0, g = bgra[i * 4 + 1] / 255.0, b = bgra[i * 4] / 255.0;
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                double v = mx, s = mx > 0 ? (mx - mn) / mx : 0;
                if (v < MinV || s < MinS) continue;
                double d = mx - mn;
                double h = mx == r ? 60 * (((g - b) / d + 6) % 6) : mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
                double w = s * v * v;
                px.Add(new[] { h, s, v, w });
                hist[(int)(h / 15) % Bins] += w;
            }
            if (px.Count == 0) return (Color.FromRgb(158, 158, 158), Colors.White);   // no colour in it: grey to white

            double vivid = px.Sum(p => p[3]) / n;
            double fade = Math.Max(0, Math.Min(1, (vivid - VividNone) / (VividFull - VividNone)));
            var sm = Enumerable.Range(0, Bins).Select(i => hist[(i + Bins - 1) % Bins] * 0.5 + hist[i] + hist[(i + 1) % Bins] * 0.5).ToArray();
            int a = Enumerable.Range(0, Bins).OrderByDescending(i => sm[i]).First();
            int bBin = Enumerable.Range(0, Bins).Where(i => Math.Min((i - a + Bins) % Bins, (a - i + Bins) % Bins) >= Apart)
                .OrderByDescending(i => sm[i]).First();

            var (ha, sa, va) = Family(px, a);
            if (sm[bBin] < SecondMin * sm[a]) return (Hsv(ha, sa * fade, 0.55), Hsv(ha, sa * fade, 0.95));
            var (hb, sb, vb) = Family(px, bBin);
            Color ca = Hsv(ha, sa * fade, Math.Max(va, LiftV)), cb = Hsv(hb, sb * fade, Math.Max(vb, LiftV));
            return Lum(ca) <= Lum(cb) ? (ca, cb) : (cb, ca);
        }

        // A hue family's colour: of its pixels within 22.5 degrees of the bin's centre, the brightest third - weighted
        // circular-mean hue, weighted mean saturation and brightness
        private static (double h, double s, double v) Family(List<double[]> px, int bin)
        {
            double centre = bin * 15 + 7.5;
            var mine = px.Where(p => Math.Min(Math.Abs(p[0] - centre), 360 - Math.Abs(p[0] - centre)) <= 22.5).OrderBy(p => p[2]).ToList();
            if (mine.Count == 0) mine = px;
            var top = mine.Skip(mine.Count * 2 / 3).ToList();
            if (top.Count == 0) top = mine;
            double tw = top.Sum(p => p[3]);
            double hx = top.Sum(p => Math.Cos(p[0] * Math.PI / 180) * p[3]), hy = top.Sum(p => Math.Sin(p[0] * Math.PI / 180) * p[3]);
            double h = (Math.Atan2(hy, hx) * 180 / Math.PI + 360) % 360;
            return (h, top.Sum(p => p[1] * p[3]) / tw, top.Sum(p => p[2] * p[3]) / tw);
        }

        private static Color Hsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static double Lum(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
    }
}
