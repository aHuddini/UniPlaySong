using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class MusicPlaybackServicePauseTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // External Audio pauses instantly; the play buttons resume through the faded path. That path only restarted
        // the player when the fader was paused, and an instant pause left it unpaused - so play ramped the volume of a
        // paused player: silence, and after an idle/lock device release the play button did nothing at all.
        [Test]
        public void AnInstantPause_IsResumedByTheFadedPath()
        {
            var player = new Mock<IMusicPlayer>();
            player.SetupGet(p => p.IsLoaded).Returns(true);
            player.SetupGet(p => p.IsActive).Returns(true);
            var service = new MusicPlaybackService(player.Object, new GameMusicFileService(_tempDir));

            service.AddPauseSourceImmediate(PauseSource.ExternalAudio);
            service.RemovePauseSource(PauseSource.ExternalAudio);

            player.Verify(p => p.Resume(It.IsAny<Action>()), Times.Once);
        }
    }
}
