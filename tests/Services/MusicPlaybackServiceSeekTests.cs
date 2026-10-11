using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Seek keeps the target inside the song and never reaches a player that can't seek.
    [TestFixture]
    public class MusicPlaybackServiceSeekTests
    {
        private string _tempDir;
        private Mock<IMusicPlayer> _player;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));
            _player = new Mock<IMusicPlayer>();
            _player.SetupGet(p => p.CanSeek).Returns(true);
            _player.SetupGet(p => p.TotalTime).Returns(TimeSpan.FromSeconds(100));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private MusicPlaybackService CreateService() => new MusicPlaybackService(_player.Object, new GameMusicFileService(_tempDir));

        [Test]
        public void Seek_InsideTheSong_GoesThrough()
        {
            CreateService().Seek(TimeSpan.FromSeconds(42));
            _player.Verify(p => p.Seek(TimeSpan.FromSeconds(42)), Times.Once);
        }

        // A seek onto the last samples would just end the song, so it stops a second short.
        [Test]
        public void Seek_PastTheEnd_StopsASecondShort()
        {
            CreateService().Seek(TimeSpan.FromSeconds(500));
            _player.Verify(p => p.Seek(TimeSpan.FromSeconds(99)), Times.Once);
        }

        [Test]
        public void Seek_BeforeTheStart_GoesToZero()
        {
            CreateService().Seek(TimeSpan.FromSeconds(-12));
            _player.Verify(p => p.Seek(TimeSpan.Zero), Times.Once);
        }

        // Chiptunes, PSF and crossfades report CanSeek = false; the request must not reach the player at all.
        [Test]
        public void Seek_WhenThePlayerCannotSeek_DoesNothing()
        {
            _player.SetupGet(p => p.CanSeek).Returns(false);
            var service = CreateService();

            Assert.IsFalse(service.CanSeek);
            service.Seek(TimeSpan.FromSeconds(10));
            _player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }

        [Test]
        public void Seek_WithNoKnownLength_DoesNothing()
        {
            _player.SetupGet(p => p.TotalTime).Returns((TimeSpan?)null);
            var service = CreateService();

            Assert.IsFalse(service.CanSeek);
            service.Seek(TimeSpan.FromSeconds(10));
            _player.Verify(p => p.Seek(It.IsAny<TimeSpan>()), Times.Never);
        }
    }
}
