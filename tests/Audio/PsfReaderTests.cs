using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Audio;

namespace UniPlaySong.Tests.Audio
{
    // Looping a PSF rewinds it to zero, and a PSF can only get to zero by running its driver again.
    // When that rewind did nothing the reader stayed at EOF, so a looping PSF went silent and span
    // through end-of-song forever. These run the real engine (psf.dll) on the synthetic IRQ-driven
    // tone from native/psf/test/make_irq.py: timers, BIOS events and a pitch flip every 320 ms, so a
    // restart that left any engine state behind shows up as different samples.
    [TestFixture]
    public class PsfReaderTests
    {
        // make_irq.py output: 3 s long, no fade.
        private const string ToneIrqPsf =
            "UFNGAQAAAAB3AQAAe+Wnjnja7Za/S8NAFMffpWnNBX/EQeiQ0hMU7KAgXEDwwIh20EGELh0bSsUOTSVExC2Igw4dHfwD4lCM+Cd0cNCl/gvdXMWpW31JbAfFVRzuA+++7x3vvvdjusPKapWVq2WYQIKJavCD91ExgF+otN1zttNunZz6DY+VXRx9p+m2Gq7P9tz6Gjtqe+yg7fnHbLvV8Jp1hzlewwGJRCKRSCR/S1DURLcA4WiL8qBAwwsMFSi/RwWF8jtU1qO8i0oCXeySfatn6BEBKjQoWU8GjYBQQZSS9RznX72xH5g0VDDAoFw1U78c6hz6a6i6CSH68Kt4rw9VKHDJFchyMHKcBFNiCG+os0KQvvUIOqdDmwC8dBaZDeNaxTq+Qy2kfGWeRlXcYwnVRl8/Pk+N2slvRi1ZULu2hZ3+auI1JJgRGySyAB46SQ/0lzGPivC6noXBQjynmvmb5KHy+U3MQw0GmfHbxUbTSdNZym0KZL7VEolEIpH8Mz4B329rLltUQUdddGl0bGU9dG9uZSBpcnEKbGVuZ3RoPTMKZmFkZT0wCg==";

        private const int Frames1s = 44100;

        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "ups_psfreader_" + Guid.NewGuid().ToString("N") + ".psf");
            File.WriteAllBytes(_path, Convert.FromBase64String(ToneIrqPsf));
        }

        [TearDown]
        public void TearDown()
        {
            try { File.Delete(_path); } catch { }
        }

        private static float[] ReadFrames(PsfReader reader, int frames)
        {
            var all = new float[frames * 2];
            int got = 0;
            while (got < all.Length)
            {
                int n = reader.Read(all, got, Math.Min(4410, all.Length - got));
                if (n == 0) break;
                got += n;
            }
            Assert.AreEqual(all.Length, got, "the reader ended early");
            return all;
        }

        private static void Drain(PsfReader reader)
        {
            var buffer = new float[8820];
            while (reader.Read(buffer, 0, buffer.Length) > 0) { }
        }

        [Test]
        public void RewindToZeroAtEof_PlaysTheSameAudioAsAFreshReader()
        {
            using (var fresh = new PsfReader(_path))
            using (var looped = new PsfReader(_path))
            {
                var expected = ReadFrames(fresh, Frames1s);
                Assert.IsTrue(expected.Any(s => s != 0f), "the fixture rendered silence, so the comparison proves nothing");

                Drain(looped);
                Assert.AreEqual(0, looped.Read(new float[64], 0, 64), "not at EOF after draining");

                looped.CurrentTime = TimeSpan.Zero;

                Assert.AreEqual(TimeSpan.Zero, looped.CurrentTime);
                CollectionAssert.AreEqual(expected, ReadFrames(looped, Frames1s));
            }
        }

        // Any other position would need the driver run up to it - the ignored case stays ignored.
        [Test]
        public void SeekToANonZeroPosition_IsIgnored()
        {
            using (var reader = new PsfReader(_path))
            {
                ReadFrames(reader, Frames1s / 2);
                var before = reader.CurrentTime;

                reader.CurrentTime = TimeSpan.FromSeconds(2);

                Assert.AreEqual(before, reader.CurrentTime);
            }
        }
    }
}
