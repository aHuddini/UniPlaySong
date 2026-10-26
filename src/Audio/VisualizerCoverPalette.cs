using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace UniPlaySong.Audio
{
    // The Jukebox Fountain's colours from a picture (the game's background, by default): the bench's coverPalette
    // (native/visualizer/bench/fountain-bench.html) - the picture at 64x64 in five colours by k-means, weighted toward
    // saturated, bright pixels (so each colour comes out as vivid as the picture has it). The three covering the most
    // pixels with colour in them go to the renderer, darkest first, which builds the palette from them
    // (fountain::Colours::FromCover). Greys only fill in when there aren't three colours: brightness is normalised there,
    // so a near-black turns pale. By pixels, not by the weighting: that counts a vivid accent several times over - Mass
    // Effect's orange sun (5% of the pixels) tied its plum (18%) and pushed out the blue. A one-colour cover gives shades
    // of that colour: nothing is made up.
    internal static class VisualizerCoverPalette
    {
        private const int Size = 64, K = 5, Iterations = 12;
        private const double Grey = 0.05;   // saturation (max - min channel) below which a colour counts as grey
        // Raise when the picking changes: every stored palette is then taken again.
        internal const int StoreVersion = 1;

        // Each picture's colours, kept on disk (storeFile) across sessions: a picture is taken apart once, and each session
        // checks it once - its size and time against the stored ones - before trusting the stored colours. Only a changed
        // picture is taken again. Pictures whose files are gone drop out when the store is next written.
        private static readonly object StoreGate = new object();
        private static Dictionary<string, Stored> _store;                      // loaded on first use
        private static Dictionary<string, Color[]> _checked = NewSession();   // checked this session

        private sealed class Stored
        {
            public long Bytes;
            public long Time;
            public string[] Colors;
        }

        private sealed class StoreFile
        {
            public int Version;
            public Dictionary<string, Stored> Pictures;
        }

        private static Dictionary<string, Color[]> NewSession() => new Dictionary<string, Color[]>(StringComparer.OrdinalIgnoreCase);

        // Off the UI thread: the decode and the file work take tens of milliseconds the first time. analysed: taken apart
        // now, rather than found stored.
        public static Color[] Get(string path, string storeFile, out bool analysed)
        {
            lock (StoreGate)
            {
                analysed = false;
                if (_checked.TryGetValue(path, out var known)) return known;
                if (_store == null) _store = Load(storeFile);
                var info = new FileInfo(path);
                long bytes = info.Length, time = info.LastWriteTimeUtc.ToFileTimeUtc();
                Color[] cols;
                if (_store.TryGetValue(path, out var s) && s.Bytes == bytes && s.Time == time && s.Colors?.Length == 3)
                    cols = s.Colors.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();
                else
                {
                    cols = FromFile(path);
                    analysed = true;
                    _store[path] = new Stored { Bytes = bytes, Time = time, Colors = cols.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToArray() };
                    Save(storeFile);
                }
                _checked[path] = cols;
                return cols;
            }
        }

        // Tests: a new Playnite session (nothing checked, the store read again).
        internal static void ResetSession()
        {
            lock (StoreGate) { _checked = NewSession(); _store = null; }
        }

        private static Dictionary<string, Stored> Load(string storeFile)
        {
            try
            {
                var f = JsonConvert.DeserializeObject<StoreFile>(File.ReadAllText(storeFile));
                if (f?.Version == StoreVersion && f.Pictures != null) return new Dictionary<string, Stored>(f.Pictures, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }   // none yet, or unreadable: start again
            return new Dictionary<string, Stored>(StringComparer.OrdinalIgnoreCase);
        }

        private static void Save(string storeFile)
        {
            foreach (var gone in _store.Keys.Where(p => !File.Exists(p)).ToList()) _store.Remove(gone);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(storeFile));
                string temp = storeFile + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(new StoreFile { Version = StoreVersion, Pictures = _store }, Formatting.Indented));
                if (File.Exists(storeFile)) File.Replace(temp, storeFile, null);
                else File.Move(temp, storeFile);
            }
            catch (Exception) { }   // kept for this session anyway; written again with the next new picture
        }

        public static Color[] FromFile(string path) => FromPixels(Pixels(path));

        // The picture at 64x64, BGRA - what both colour pickers read (VisualizerStripColors too)
        internal static byte[] Pixels(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = Size;
            bmp.DecodePixelHeight = Size;   // both: stretched square, as the bench draws it
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // IgnoreImageCache: WPF otherwise hands back its earlier decode of the same path - a changed picture read as the old one
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            var px = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var bgra = new byte[px.PixelWidth * px.PixelHeight * 4];
            px.CopyPixels(bgra, px.PixelWidth * 4, 0);
            return bgra;
        }

        internal static Color[] FromPixels(byte[] bgra)
        {
            int n = bgra.Length / 4;
            var pts = new double[n][];
            for (int i = 0; i < n; i++)
            {
                double r = bgra[i * 4 + 2] / 255.0, g = bgra[i * 4 + 1] / 255.0, b = bgra[i * 4] / 255.0;
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                pts[i] = new[] { r, g, b, 0.05 + (mx - mn) * mx };
            }
            var cent = Enumerable.Range(0, K).Select(k => pts[(int)((k + 0.5) * n / K)].Take(3).ToArray()).ToArray();
            var wsum = new double[K];
            var count = new int[K];
            for (int it = 0; it < Iterations; it++)
            {
                var acc = new double[K, 3];
                wsum = new double[K];
                count = new int[K];
                foreach (var p in pts)
                {
                    int bi = 0;
                    double bd = 1e9;
                    for (int k = 0; k < K; k++)
                    {
                        double d = Sq(p[0] - cent[k][0]) + Sq(p[1] - cent[k][1]) + Sq(p[2] - cent[k][2]);
                        if (d < bd) { bd = d; bi = k; }
                    }
                    for (int q = 0; q < 3; q++) acc[bi, q] += p[q] * p[3];
                    wsum[bi] += p[3];
                    count[bi]++;
                }
                for (int k = 0; k < K; k++)
                    if (wsum[k] > 0) cent[k] = new[] { acc[k, 0] / wsum[k], acc[k, 1] / wsum[k], acc[k, 2] / wsum[k] };
            }
            var pick = Enumerable.Range(0, K).OrderBy(k => Sat(cent[k]) < Grey).ThenByDescending(k => count[k]).Take(3)
                .Select(k => cent[k]).OrderBy(Lum).ToArray();
            return new[] { ToColor(pick[0]), ToColor(pick[2]), ToColor(pick[1]) };
        }

        private static double Sq(double v) => v * v;
        private static double Lum(double[] c) => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
        private static double Sat(double[] c) => c.Max() - c.Min();
        private static Color ToColor(double[] c) => Color.FromRgb(Byte(c[0]), Byte(c[1]), Byte(c[2]));
        private static byte Byte(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
    }
}
