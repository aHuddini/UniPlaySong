using NUnit.Framework;
using NAudio.Wave;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Audio
{
    [TestFixture]
    public class SpotifyCaptureSampleProviderTests
    {
        [Test]
        public void Converts16BitPcmToFloat_AndZeroFillsUnderrun()
        {
            var client = new SpotifyLoopbackClient();
            // Push 3 known int16 samples (little-endian): 32767, -32768, 0
            client.PushForTest(new byte[] { 0xFF, 0x7F, 0x00, 0x80, 0x00, 0x00 });

            var prov = new SpotifyCaptureSampleProvider(client);
            var buf = new float[4];
            int n = prov.Read(buf, 0, 4);

            Assert.That(n, Is.EqualTo(4));                             // always fills request
            Assert.That(buf[0], Is.EqualTo(32767f / 32768f).Within(1e-6f)); // int16 max -> ~+0.99997
            Assert.That(buf[1], Is.EqualTo(-1.0f).Within(1e-6f));      // int16 min -> -1.0
            Assert.That(buf[2], Is.EqualTo(0f));                       // zero -> 0
            Assert.That(buf[3], Is.EqualTo(0f));                       // underrun -> silence
        }

        [Test]
        public void WaveFormat_IsIeeeFloat()
        {
            var prov = new SpotifyCaptureSampleProvider(new SpotifyLoopbackClient());
            Assert.That(prov.WaveFormat.Encoding, Is.EqualTo(WaveFormatEncoding.IeeeFloat));
        }
    }
}
