using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    [TestFixture]
    public class SpotifyLoopbackClientTests
    {
        [Test]
        public void RingBuffer_ReadReturnsWrittenBytes_ThenZeroFillsOnUnderrun()
        {
            var ring = new SpotifyLoopbackClient.RingBuffer(16);
            ring.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            var dst = new byte[8];
            int n = ring.Read(dst, 0, 8);
            Assert.That(n, Is.EqualTo(8));                       // always fills request
            Assert.That(dst[0], Is.EqualTo(1));
            Assert.That(dst[3], Is.EqualTo(4));
            Assert.That(dst[4], Is.EqualTo(0));                  // underrun -> zero fill
        }

        [Test]
        public void RingBuffer_WrapsAround()
        {
            var ring = new SpotifyLoopbackClient.RingBuffer(4);
            ring.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            var dst = new byte[2];
            ring.Read(dst, 0, 2);                                // consume 2
            ring.Write(new byte[] { 5, 6 }, 0, 2);               // wrap
            var dst2 = new byte[4];
            int n = ring.Read(dst2, 0, 4);
            Assert.That(n, Is.EqualTo(4));
            Assert.That(dst2, Is.EqualTo(new byte[] { 3, 4, 5, 6 }));
        }
    }
}
