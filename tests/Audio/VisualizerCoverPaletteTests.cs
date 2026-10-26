using System;
using System.Linq;
using System.Windows.Media;
using NUnit.Framework;
using UniPlaySong.Audio;

namespace UniPlaySong.Tests.Audio
{
    [TestFixture]
    public class VisualizerCoverPaletteTests
    {
        // 64x64 BGRA, each pixel from f(index)
        private static byte[] Cover(Func<int, Color> f)
        {
            var px = new byte[64 * 64 * 4];
            for (int i = 0; i < 64 * 64; i++) { var c = f(i); px[i * 4] = c.B; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.R; px[i * 4 + 3] = 255; }
            return px;
        }

        private static double Hue(Color c)
        {
            double r = c.R, g = c.G, b = c.B, mx = Math.Max(r, Math.Max(g, b)), d = mx - Math.Min(r, Math.Min(g, b));
            if (d == 0) return -1;
            return mx == r ? 60 * (((g - b) / d + 6) % 6) : mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        }

        // Shades of one blue (dark navy to pale): every colour back is that blue - the icon extractor invented a second
        // hue 60 degrees round for a cover like this.
        [Test]
        public void OneColourCover_GivesShadesOfItsOwnHue()
        {
            var shades = new[] { Color.FromRgb(10, 25, 70), Color.FromRgb(30, 80, 200), Color.FromRgb(120, 160, 240), Color.FromRgb(200, 215, 250) };
            var cols = VisualizerCoverPalette.FromPixels(Cover(i => shades[(i / 64) * shades.Length / 64]));
            foreach (var c in cols) Assert.AreEqual(223, Hue(c), 10, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            Assert.Less(cols[0].R + cols[0].G + cols[0].B, cols[1].R + cols[1].G + cols[1].B, "darkest first, brightest second");
        }

        // A picture is taken apart once: a new session finds its colours stored (the file unchanged), and a changed
        // picture is taken again.
        // The strip's colours on a red cover whose hues straddle 0/360: a plain average of 355 and 5 is 180 - cyan. The
        // circular mean keeps it red.
        [Test]
        public void StripColours_RedStraddlingZero_StaysRed()
        {
            var (foot, top) = VisualizerStripColors.FromPixels(Cover(i => i % 2 == 0 ? Color.FromRgb(220, 30, 50) : Color.FromRgb(220, 50, 30)));
            foreach (var c in new[] { foot, top })
            {
                double h = Hue(c);
                Assert.IsTrue(h < 15 || h > 345, $"#{c.R:X2}{c.G:X2}{c.B:X2} hue {h:0}");
            }
            Assert.Less(foot.R, top.R, "one hue: dark foot, bright top");
        }

        [Test]
        public void Store_KeepsColoursAcrossSessions_UntilThePictureChanges()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UPS_PaletteTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string pic = System.IO.Path.Combine(dir, "bg.png"), store = System.IO.Path.Combine(dir, "palettes.json");
                void Paint(Color c)
                {
                    var px = Cover(i => i % 64 < 40 ? c : Color.FromRgb(30, 30, 30));
                    var bmp = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, px, 64 * 4);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = System.IO.File.Create(pic)) enc.Save(f);
                }
                Paint(Color.FromRgb(40, 90, 220));
                VisualizerCoverPalette.ResetSession();
                var first = VisualizerCoverPalette.Get(pic, store, out bool analysed);
                Assert.IsTrue(analysed, "first sight: taken apart");
                Assert.IsTrue(System.IO.File.Exists(store), "and stored");

                VisualizerCoverPalette.ResetSession();   // the next Playnite session
                var again = VisualizerCoverPalette.Get(pic, store, out analysed);
                Assert.IsFalse(analysed, "unchanged: the stored colours");
                CollectionAssert.AreEqual(first, again);

                Paint(Color.FromRgb(220, 40, 40));
                System.IO.File.SetLastWriteTimeUtc(pic, DateTime.UtcNow.AddMinutes(1));
                VisualizerCoverPalette.ResetSession();
                var changed = VisualizerCoverPalette.Get(pic, store, out analysed);
                Assert.IsTrue(analysed, "changed: taken again");
                Assert.IsTrue(changed.Any(c => Math.Abs(Hue(c) - 360) < 10 || Hue(c) < 10), "the new picture's red");
            }
            finally
            {
                VisualizerCoverPalette.ResetSession();
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
