using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.DeskMediaControl;

namespace UniPlaySong.Tests.Services
{
    // Spectrum visualizer bar count (1-12).
    //
    // Asked for by a user whose Desktop theme styles the top panel with short, circular buttons the
    // full-width 12-bar visualizer does not fit beside. Fewer bars means a narrower control.
    //
    // The catch is that every tuning table in the visualizer - band edges, per-bar gain, bleed
    // fractions, gravity scales - is hand-calibrated for exactly 12 bars. Any other count resamples
    // them, and these tests pin the two properties that makes safe: the resampling is the identity
    // at 12, so the default look is untouched, and a lower count still spans the whole spectrum
    // rather than showing a zoomed-in slice of the bass.
    [TestFixture]
    public class VisualizerBarCountTests
    {
        private static double EdgeAt(double pos)
        {
            var m = typeof(SpectrumVisualizerControl).GetMethod("EdgeAt",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m, "band edges must be resampled through one helper");
            return (double)m.Invoke(null, new object[] { pos });
        }

        private static float SampleTable(float[] table, double pos)
        {
            var m = typeof(SpectrumVisualizerControl).GetMethod("SampleTable",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m, "per-bar tuning must be resampled through one helper");
            return (float)m.Invoke(null, new object[] { table, pos });
        }

        private static double[] Edges()
        {
            var f = typeof(SpectrumVisualizerControl).GetField("BandEdges",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(f);
            return (double[])f.GetValue(null);
        }

        [Test]
        public void TwelveBarsReproduceTheTunedBandsExactly()
        {
            // The whole safety argument. If this drifts, every existing user's visualizer changes
            // appearance from a feature they did not ask for.
            var edges = Edges();
            for (int i = 0; i < edges.Length; i++)
                Assert.AreEqual(edges[i], EdgeAt(i), 1e-9, $"edge {i} must come back untouched");
        }

        [Test]
        public void EveryCountStillCoversTheWholeSpectrum()
        {
            // Fewer bars must mean wider bands, not a truncated range. Taking the first N entries of
            // the table would have left a 4-bar visualizer showing bass only, with the top of the
            // spectrum invisible.
            var edges = Edges();
            double lowest = edges.First();
            double highest = edges.Last();

            foreach (int count in new[] { 1, 2, 3, 4, 6, 8, 12 })
            {
                Assert.AreEqual(lowest, EdgeAt(0), 1e-9, $"{count} bars should start at the bottom");
                Assert.AreEqual(highest, EdgeAt(count * (12.0 / count)), 1e-9,
                    $"{count} bars should still reach the top");
            }
        }

        [Test]
        public void BandsAlwaysRiseLeftToRight()
        {
            foreach (int count in new[] { 1, 2, 3, 5, 7, 9, 12 })
            {
                double previous = -1;
                for (int i = 0; i <= count; i++)
                {
                    double hz = EdgeAt(i * (double)12 / count);
                    Assert.Greater(hz, previous, $"{count} bars: edge {i} must be above the one before it");
                    previous = hz;
                }
            }
        }

        [Test]
        public void ATunedTableComesBackUnchangedAtTwelve()
        {
            var table = new[] { 1.4f, 1.7f, 2.2f, 2.8f, 3.6f, 4.5f, 5.5f, 7.0f, 9.0f, 12.0f, 16.0f, 20.0f };

            for (int i = 0; i < table.Length; i++)
                Assert.AreEqual(table[i], SampleTable(table, i), 1e-6, $"entry {i}");
        }

        [Test]
        public void AWideBarTakesTheGainOfTheBassItContains()
        {
            // Sampled at each bar's LOW frequency edge, not its centre. RMS across a band is
            // dominated by its lowest frequencies, so one bar spanning the whole spectrum behaves
            // like bass and needs the bass gain. Centre-sampling would hand it a treble gain around
            // 5.0 and peg it at full height permanently - a solid block, not a visualizer.
            var gains = new[] { 1.4f, 1.7f, 2.2f, 2.8f, 3.6f, 4.5f, 5.5f, 7.0f, 9.0f, 12.0f, 16.0f, 20.0f };

            Assert.AreEqual(gains[0], SampleTable(gains, 0), 1e-6,
                "a single bar spans everything and must take the bass gain");

            // And the first bar of any count starts at the bottom, so it always gets that gain.
            foreach (int count in new[] { 1, 3, 6, 12 })
                Assert.AreEqual(gains[0], SampleTable(gains, 0 * (double)12 / count), 1e-6);
        }

        [Test]
        public void TheSettingIsClampedToWhatTheTablesSupport()
        {
            var s = new UniPlaySongSettings();

            s.VizBarCount = 99;
            Assert.AreEqual(12, s.VizBarCount, "12 is the calibrated maximum");

            s.VizBarCount = 0;
            Assert.AreEqual(1, s.VizBarCount, "zero bars is not a visualizer");
        }

        [Test]
        public void TheDefaultIsTheFullTwelve()
        {
            // Anything else would narrow every existing user's visualizer on upgrade.
            Assert.AreEqual(12, new UniPlaySongSettings().VizBarCount);
        }
    }
}
