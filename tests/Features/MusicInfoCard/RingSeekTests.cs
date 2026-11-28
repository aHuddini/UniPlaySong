using NUnit.Framework;
using UniPlaySong.Features.MusicInfoCard.Player;

namespace UniPlaySong.Tests.Features.MusicInfoCard
{
    // The Play/Pause ring is the song's progress: the mouse puts the knob where it points. 12 o'clock is the start and
    // the ring runs clockwise; a drag that crosses 12 o'clock stops at the end (or the start) instead of wrapping.
    [TestFixture]
    public class RingSeekTests
    {
        [Test]
        public void FractionAt_ClockPositions()
        {
            Assert.AreEqual(0.0, RingSeek.FractionAt(0, -10, null), 1e-9);
            Assert.AreEqual(0.25, RingSeek.FractionAt(10, 0, null), 1e-9);
            Assert.AreEqual(0.5, RingSeek.FractionAt(0, 10, null), 1e-9);
            Assert.AreEqual(0.75, RingSeek.FractionAt(-10, 0, null), 1e-9);
        }

        [Test]
        public void FractionAt_DragAcrossTheTop_StopsInsteadOfWrapping()
        {
            Assert.AreEqual(1.0, RingSeek.FractionAt(1, -10, previous: 0.95));   // forward past the end
            Assert.AreEqual(0.0, RingSeek.FractionAt(-1, -10, previous: 0.05));  // back past the start
        }
    }
}
