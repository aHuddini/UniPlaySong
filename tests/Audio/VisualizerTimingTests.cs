using NAudio.Wave;
using NUnit.Framework;
using UniPlaySong.Audio;

namespace UniPlaySong.Tests.Audio
{
    // The visualizer used to analyse audio ~350 ms before it was heard (measured with WASAPI loopback): the output
    // device queues two 150 ms blocks, and the bars read the newest one. These pin the arithmetic that now holds the
    // window back to the frame the device is playing.
    [TestFixture]
    public class VisualizerTimingTests
    {
        private const int BlockAlign = 8; // 44.1 kHz float stereo

        private class FakeDevice : IWavePosition
        {
            public long PositionBytes;
            public long GetPosition() => PositionBytes;
            public WaveFormat OutputWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        }

        private class Silence : ISampleProvider
        {
            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            public int Read(float[] buffer, int offset, int count) => count;
        }

        // Played is submitted minus what is queued; a device ahead of submitted counts as all played.
        [TestCase(13230, 6615, ExpectedResult = 6615)]
        [TestCase(1000, 1001, ExpectedResult = 1000)]
        public long Played_IsSubmittedMinusWhatIsQueued(long submitted, long devicePlayedFrames)
        {
            return OutputClock.Played(submitted, devicePlayedFrames * BlockAlign, BlockAlign);
        }

        // waveOut's byte position is 32-bit and wraps after ~3.4 hours; the persistent device runs all session.
        [Test]
        public void Played_SurvivesThe32BitPositionWrap()
        {
            long submitted = (1L << 32) / BlockAlign + 50000;        // both counts past the wrap
            long playedFrames = submitted - 13230;                     // 300 ms queued
            Assert.Greater(playedFrames * BlockAlign, 1L << 32);
            long wrappedPosition = (playedFrames * BlockAlign) & 0xFFFFFFFFL;

            Assert.AreEqual(playedFrames, OutputClock.Played(submitted, wrappedPosition, BlockAlign));
        }

        // A device rebuilt after an audio-device release starts its position at zero; the clock carries on.
        [Test]
        public void Clock_CarriesItsCountAcrossADeviceRebuild()
        {
            var clock = new OutputClock(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
            var first = new FakeDevice();
            clock.Attach(new Silence(), first);
            clock.Read(new float[2000], 0, 2000);                      // 1000 frames
            first.PositionBytes = 1000L * BlockAlign;
            Assert.AreEqual(1000, clock.FramesPlayed);

            var second = new FakeDevice();
            clock.Attach(new Silence(), second);
            clock.Read(new float[600], 0, 600);                        // 300 more, none played yet
            Assert.AreEqual(1000, clock.FramesPlayed);
            second.PositionBytes = 100L * BlockAlign;
            Assert.AreEqual(1100, clock.FramesPlayed);
        }

        [Test]
        public void Clock_WithNoDevice_TreatsEverythingAsPlayed()
        {
            var clock = new OutputClock(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
            clock.Attach(new Silence(), new FakeDevice());
            clock.Read(new float[200], 0, 200);
            clock.Detach();
            Assert.AreEqual(100, clock.FramesPlayed);
        }

        // Newest sample is ring index 20000 = device frame 50000. The window holds back by what is not yet played,
        // ends at the newest sample once all is played, and never reaches back further than the ring holds.
        [TestCase(36770, 20000, ExpectedResult = 20000 - 13230)]
        [TestCase(50000, 20000, ExpectedResult = 20000)]
        [TestCase(60000, 20000, ExpectedResult = 20000)]
        [TestCase(0, 17000, ExpectedResult = 20000 - 17000)]
        public int WindowEnd_HoldsBackByWhatIsNotYetPlayed(long devicePlayed, int ringHolds)
        {
            return VisualizationDataProvider.WindowEnd(20000, 50000, devicePlayed, ringHolds);
        }

        // The smoothing must look the same at any update rate: two half-steps land where one full step does.
        [Test]
        public void ScaleAlpha_IsRateIndependent()
        {
            const float alpha = 0.88f;
            float half = VisualizationDataProvider.ScaleAlpha(alpha, 0.5);

            float oneStep = 0f + (1f - 0f) * alpha;
            float twoHalf = 0f + (1f - 0f) * half;
            twoHalf = twoHalf + (1f - twoHalf) * half;

            Assert.AreEqual(oneStep, twoHalf, 1e-5);
            Assert.AreEqual(alpha, VisualizationDataProvider.ScaleAlpha(alpha, 1.0), 1e-6);
            Assert.AreEqual(0f, VisualizationDataProvider.ScaleAlpha(alpha, 0));
        }
    }
}
