using System;
using System.Linq;
using NAudio.Wave;
using NUnit.Framework;
using UniPlaySong.Audio;
using EqualizerPreset = global::UniPlaySong.EqualizerPreset;

namespace UniPlaySong.Tests.Audio
{
    [TestFixture]
    public class EqualizerTests
    {
        private class Sine : ISampleProvider
        {
            private readonly double _hz;
            private long _n;
            public Sine(double hz) { _hz = hz; }
            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            public int Read(float[] b, int o, int c)
            {
                for (int i = 0; i < c; i += 2)
                {
                    float v = (float)(0.1 * Math.Sin(2 * Math.PI * _hz * _n++ / 44100));
                    b[o + i] = v; b[o + i + 1] = v;
                }
                return c;
            }
        }

        private static double Peak(ISampleProvider p)
        {
            var buf = new float[44100 * 2];
            p.Read(buf, 0, buf.Length);                 // settle
            p.Read(buf, 0, buf.Length);
            return buf.Max(Math.Abs);
        }

        // A lone band reaches its slider at its own centre: the digital-width filter, not NAudio's analog Q.
        [TestCase(10, 0, 6)]
        [TestCase(10, 4, -6)]
        [TestCase(10, 9, 6)]
        [TestCase(15, 0, 6)]
        [TestCase(15, 8, -6)]
        [TestCase(15, 14, 6)]
        public void OneBand_ReachesItsSliderAtItsCentre(int layout, int band, int db)
        {
            var bands = new int[layout];
            bands[band] = db;
            Assert.AreEqual(db, Equalizer.ResponseDb(bands, Equalizer.FrequenciesFor(layout)[band]), 0.05);
        }

        [Test]
        public void FifteenBands_IsTheDefault()
        {
            var s = new UniPlaySongSettings();
            Assert.AreEqual(15, Equalizer.BandCount(s));
            Assert.AreEqual(15, Equalizer.Bands(s).Length);
        }

        [Test]
        public void Off_OrFlat_PassesAudioThroughUntouched()
        {
            var off = new UniPlaySongSettings { EqualizerEnabled = false, Equalizer15Band1k = 12 };
            var flat = new UniPlaySongSettings { EqualizerEnabled = true };
            foreach (var s in new[] { off, flat })
            {
                var reference = new float[4410];
                new Sine(1000).Read(reference, 0, reference.Length);
                var eq = new Equalizer(new Sine(1000), () => s);
                var out_ = new float[4410];
                eq.Read(out_, 0, out_.Length);
                CollectionAssert.AreEqual(reference, out_);
            }
        }

        // Played through the stage, a +6 dB band doubles a sine at its centre, in either layout; the other layout's
        // curve has no effect.
        [Test]
        public void Boost_IsAppliedToTheAudio_FromTheActiveLayoutOnly()
        {
            var fifteen = new UniPlaySongSettings { EqualizerEnabled = true, Equalizer15Band1k = 6, EqualizerBand1k = -12 };
            var ten = new UniPlaySongSettings { EqualizerEnabled = true, EqualizerBandCount = 10, EqualizerBand1k = 6, Equalizer15Band1k = -12 };
            foreach (var s in new[] { fifteen, ten })
                Assert.AreEqual(Math.Pow(10, 6 / 20.0), Peak(new Equalizer(new Sine(1000), () => s)) / 0.1, 0.05);
        }

        [Test]
        public void Preamp_ScalesEverything()
        {
            var s = new UniPlaySongSettings { EqualizerEnabled = true, EqualizerPreampDb = -6 };
            Assert.AreEqual(Math.Pow(10, -6 / 20.0) * 0.1, Peak(new Equalizer(new Sine(400), () => s)), 0.002);
        }

        // Presets are curves the sliders can show: one value per band, each within the +-12 dB range.
        [TestCase(10)]
        [TestCase(15)]
        public void EveryPreset_FitsTheLayout_AndCustomHasNone(int layout)
        {
            Assert.IsNull(Equalizer.PresetBands(EqualizerPreset.Custom, layout));
            foreach (EqualizerPreset p in Enum.GetValues(typeof(EqualizerPreset)))
            {
                if (p == EqualizerPreset.Custom) continue;
                var b = Equalizer.PresetBands(p, layout);
                Assert.AreEqual(layout, b.Length, p.ToString());
                Assert.IsTrue(b.All(g => Math.Abs(g) <= Equalizer.RangeDb), p.ToString());
            }
        }

        // A hand-set zigzag at the limits is the hardest curve to meet; the solver must stay finite and inside its
        // guard, and still land near every slider.
        [TestCase(10)]
        [TestCase(15)]
        public void ExtremeCurve_StaysStable(int layout)
        {
            var zigzag = Enumerable.Range(0, layout).Select(i => i % 2 == 0 ? 12 : -12).ToArray();
            var gains = Equalizer.SolveGains(zigzag);
            Assert.IsTrue(gains.All(g => !double.IsNaN(g) && Math.Abs(g) <= 36));
            var freqs = Equalizer.FrequenciesFor(layout);
            for (int i = 0; i < layout; i++)
                Assert.AreEqual(zigzag[i], Equalizer.ResponseDb(zigzag, freqs[i]), 1.5, $"{layout} bands, {freqs[i]} Hz");
        }

        // Neighbouring bands overlap and add up: with each filter set to its slider, Treble Boost's +4 played +7.2 at
        // 12 kHz. Solved together, every preset meets every slider at its centre, so what the sliders show is what plays.
        [TestCase(10)]
        [TestCase(15)]
        public void EveryPreset_SoundsLikeItsSliders(int layout)
        {
            var freqs = Equalizer.FrequenciesFor(layout);
            foreach (EqualizerPreset p in Enum.GetValues(typeof(EqualizerPreset)))
            {
                var b = Equalizer.PresetBands(p, layout);
                if (b == null) continue;
                for (int i = 0; i < layout; i++)
                    Assert.AreEqual(b[i], Equalizer.ResponseDb(b, freqs[i]), 0.1, $"{p}, {layout} bands, {freqs[i]} Hz");
            }
        }

        // Switching layouts keeps the sound: the carried curve plays within a slider step (plus rounding) of the
        // original across the whole range, not only at the band centres.
        [TestCase(10, 15)]
        [TestCase(15, 10)]
        public void CarriedCurve_SoundsLikeTheOriginal(int from, int to)
        {
            var original = Equalizer.PresetBands(EqualizerPreset.Rock, from);
            var carried = Equalizer.CarryCurve(original, to);
            Assert.AreEqual(to, carried.Length);
            foreach (var hz in new[] { 60, 125, 250, 500, 1000, 2000, 4000, 8000, 12000 })
                Assert.AreEqual(Equalizer.ResponseDb(original, hz), Equalizer.ResponseDb(carried, hz), 1.5, $"{from}->{to} at {hz} Hz");
        }
    }
}
