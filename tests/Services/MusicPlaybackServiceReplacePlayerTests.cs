using System;
using System.IO;
using Moq;
using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // A backend swap (Live Effects / Calm Down / EQ toggle, chiptune switch) replaces only the player. The service
    // lives for the whole session because ~20 consumers hold it; rebuilding it left them on a discarded instance.
    [TestFixture]
    public class MusicPlaybackServiceReplacePlayerTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp() => _tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySongTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void ReplacePlayer_MovesTheEventsAndKeepsThePauseSources()
        {
            var oldPlayer = new Mock<IMusicPlayer>();
            var newPlayer = new Mock<IMusicPlayer>();
            var service = new MusicPlaybackService(oldPlayer.Object, new GameMusicFileService(_tempDir));
            service.AddPauseSourceImmediate(PauseSource.Manual);

            service.ReplacePlayer(newPlayer.Object);

            oldPlayer.VerifyRemove(p => p.MediaEnded -= It.IsAny<EventHandler>(), Times.Once, "the discarded player must not drive song-end");
            newPlayer.VerifyAdd(p => p.MediaEnded += It.IsAny<EventHandler>(), Times.Once);
            Assert.IsTrue(service.HasPauseSource(PauseSource.Manual), "a pause the user set survives the swap");
        }
    }
}
