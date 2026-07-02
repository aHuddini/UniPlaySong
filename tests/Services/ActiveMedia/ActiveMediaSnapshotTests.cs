using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Services.ActiveMedia
{
    [TestFixture]
    public class ActiveMediaSnapshotTests
    {
        [Test]
        public void Empty_HasNoMedia_NoneKind()
        {
            var snap = ActiveMediaSnapshot.Empty;
            Assert.IsFalse(snap.HasActiveMedia);
            Assert.AreEqual(ActiveMediaSourceKind.None, snap.SourceKind);
            Assert.AreEqual(string.Empty, snap.SourceName);
            Assert.IsFalse(snap.CanNext);
            Assert.IsFalse(snap.CanPrevious);
            Assert.AreEqual(0.0, snap.Progress);
        }

        [Test]
        public void Constructed_CarriesAllValues()
        {
            var snap = new ActiveMediaSnapshot(
                hasActiveMedia: true,
                sourceKind: ActiveMediaSourceKind.Spotify,
                sourceName: "Spotify",
                isPlaying: true,
                isMuted: false,
                progress: 42.0,
                positionText: "1:23",
                durationText: "3:45",
                volume: 70.0,
                canNext: true,
                canPrevious: true);

            Assert.IsTrue(snap.HasActiveMedia);
            Assert.AreEqual(ActiveMediaSourceKind.Spotify, snap.SourceKind);
            Assert.AreEqual("Spotify", snap.SourceName);
            Assert.IsTrue(snap.IsPlaying);
            Assert.AreEqual(42.0, snap.Progress);
            Assert.AreEqual("1:23", snap.PositionText);
            Assert.AreEqual(70.0, snap.Volume);
            Assert.IsTrue(snap.CanNext);
        }
    }
}
